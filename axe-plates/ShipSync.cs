using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AxePlates
{
    // ValheimRAFT 4.3.5 keeps every ship piece's world position in its ZDO in sync with the ship, but only for pieces that are
    // currently loaded (EnsurePiecesForVehicle returns m_pieces when the ship is active). Pieces that were unloaded keep a stale
    // world position in another zone, so the game never loads them again near the ship -> walls, chests and stairs vanish.
    // On the host this scans the world data for ALL pieces of each active ship (ZDO int "MBParentId" == ship id) and hands them
    // to RAFT's own SyncAllPrefabsToVehiclePosition(ZDO, HashSet<ZDO>), so they are filed in the ship's zone again.
    // Pieces tagged with the id of a ship that no longer exists are re-tagged to the active ship when there is exactly one.
    internal class ShipSync : MonoBehaviour
    {
        private static readonly int MBParentId = "MBParentId".GetStableHashCode();
        private Type controllerType;
        private MethodInfo syncMethod, getZdo;
        private object zdoWatch;
        private bool looked, running;
        private float next, nextReport;
        private static readonly MethodInfo createObject = AccessTools.Method(typeof(ZNetScene), "CreateObject", new[] { typeof(ZDO) });

        private void Update()
        {
            if (running || Time.time < next || !Plugin.RepairOrphans.Value) return;
            next = Time.time + 10f;
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || Player.m_localPlayer == null) return;
            if (!looked)
            {
                looked = true;
                controllerType = AccessTools.TypeByName("ValheimVehicles.Controllers.VehiclePiecesController");
                if (controllerType != null)
                    syncMethod = AccessTools.Method(controllerType, "SyncAllPrefabsToVehiclePosition", new[] { typeof(ZDO), typeof(HashSet<ZDO>) });
                var zw = AccessTools.TypeByName("ZdoWatcher.ZdoWatchController");
                if (zw != null)
                {
                    zdoWatch = AccessTools.Property(zw, "Instance")?.GetValue(null);
                    getZdo = AccessTools.Method(zw, "GetZdo", new[] { typeof(int) });
                }
                if (controllerType == null || syncMethod == null) Plugin.Log.LogInfo("[ship] RAFT sync method not found, ship position sync idle");
            }
            if (controllerType == null || syncMethod == null) return;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            running = true;
            var ships = new Dictionary<int, ZDO>();
            try
            {
                foreach (var o in FindObjectsOfType(controllerType))
                {
                    var c = o as Component;
                    if (c == null) continue;
                    var t = Traverse.Create(c);
                    int vid = 0;
                    try { vid = t.Property("PersistentZdoId").GetValue<int>(); } catch { }
                    var nv = t.Property("Manager").Field("m_nview").GetValue<ZNetView>();
                    var zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
                    if (vid != 0 && zdo != null) ships[vid] = zdo;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[ship] " + e.Message); }
            if (ships.Count == 0) { running = false; yield break; }

            List<ZDO> all;
            try { var dict = AccessTools.Field(typeof(ZDOMan), "m_objectsByID").GetValue(ZDOMan.instance) as IDictionary; all = new List<ZDO>(); foreach (var v in dict.Values) all.Add(v as ZDO); }
            catch (Exception e) { Plugin.Log.LogWarning("[ship] snapshot: " + e.Message); running = false; yield break; }

            var pieces = new Dictionary<int, HashSet<ZDO>>();
            foreach (var id in ships.Keys) pieces[id] = new HashSet<ZDO>();
            int onlyShip = 0;
            if (ships.Count == 1) foreach (var id in ships.Keys) onlyShip = id;
            int retagged = 0, stale = 0;
            var deadIds = new Dictionary<int, bool>();

            for (int i = 0; i < all.Count; i++)
            {
                if (i % 20000 == 19999) yield return null;     // spread the scan over frames
                var z = all[i];
                if (z == null || !z.IsValid()) continue;
                int pid = z.GetInt(MBParentId);
                if (pid == 0) continue;
                if (!pieces.TryGetValue(pid, out var set))
                {
                    // piece of a ship that is not loaded: re-tag only if that ship does not exist at all and we have exactly one ship
                    if (onlyShip == 0 || !Plugin.AdoptForeign.Value) continue;
                    if (!deadIds.TryGetValue(pid, out bool dead))
                    {
                        dead = getZdo != null && zdoWatch != null && SafeGetZdo(pid) == null;
                        deadIds[pid] = dead;
                    }
                    if (!dead) continue;
                    z.Set(MBParentId, onlyShip);
                    retagged++;
                    set = pieces[onlyShip];
                }
                set.Add(z);
            }

            foreach (var kv in ships)
            {
                var set = pieces[kv.Key];
                if (set.Count == 0) continue;
                Vector3 shipPos = kv.Value.GetPosition();
                foreach (var z in set) if ((z.GetPosition() - shipPos).sqrMagnitude > 60f * 60f) stale++;
                try { syncMethod.Invoke(null, new object[] { kv.Value, set }); }
                catch (Exception e) { Plugin.Log.LogWarning("[ship] sync: " + (e.InnerException?.Message ?? e.Message)); }
                // pieces whose model is missing on the scene while the player is near the ship: create them again
                int missing = 0, created = 0;
                var byName = new Dictionary<string, int>();
                var lp = Player.m_localPlayer;
                bool near = lp != null && (lp.transform.position - shipPos).sqrMagnitude < 120f * 120f;
                if (near && ZNetScene.instance != null)
                {
                    foreach (var z in set)
                    {
                        if (z == null || !z.IsValid() || ZNetScene.instance.FindInstance(z) != null) continue;
                        missing++;
                        var pf = ZNetScene.instance.GetPrefab(z.GetPrefab());
                        string pn = pf != null ? pf.name : z.GetPrefab().ToString();
                        byName.TryGetValue(pn, out int cnt); byName[pn] = cnt + 1;
                        try { if (createObject.Invoke(ZNetScene.instance, new object[] { z }) != null) created++; }
                        catch (Exception e) { Plugin.Log.LogWarning("[ship] create: " + (e.InnerException?.Message ?? e.Message)); }
                    }
                }
                if (stale > 0 || retagged > 0 || missing > 0 || Time.time >= nextReport)
                {
                    nextReport = Time.time + 120f;
                    string what = string.Join(", ", System.Linq.Enumerable.Select(byName, p => p.Key + " x" + p.Value));
                    Plugin.Log.LogInfo($"[ship] ship {kv.Key}: {set.Count} pieces in world data, near={near}, stale positions {stale}, missing on scene {missing} (recreated {created}) {what}");
                }
            }
            running = false;
        }

        private object SafeGetZdo(int id)
        {
            try { return getZdo.Invoke(zdoWatch, new object[] { id }); } catch { return null; }
        }
    }
}
