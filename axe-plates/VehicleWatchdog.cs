using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AxePlates
{
    // ValheimRAFT 4.3.5 (built for 1.0.12) keeps hull pieces that wait for their ship in a static dictionary keyed by the
    // ship's persistent id (m_pendingPieces). When a ship's id changes between sessions (seen in hosted / multiplayer games),
    // its pieces stay queued under the OLD id forever: they exist in the world but are never attached -> "holes" in the hull.
    // This repairs such orphans: pieces queued under an id that has no active ship, lying next to an active ship, are
    // re-tagged with that ship's id (ZDO "MBParentId") and handed to RAFT's own ActivatePiece().
    internal class VehicleWatchdog : MonoBehaviour
    {
        private Type controllerType;
        private bool looked;
        private float next, nextLog;
        private static readonly int MBParentId = "MBParentId".GetStableHashCode();

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 5f;
            var lp = Player.m_localPlayer;
            if (lp == null) return;
            if (!looked)
            {
                looked = true;
                controllerType = AccessTools.TypeByName("ValheimVehicles.Controllers.VehiclePiecesController");
                if (controllerType == null) Plugin.Log.LogInfo("[vehicle] RAFT not present, ship repair idle");
            }
            if (controllerType == null) return;
            try
            {
                var pendingAll = AccessTools.Field(controllerType, "m_pendingPieces")?.GetValue(null) as IDictionary;
                var active = AccessTools.Field(controllerType, "ActiveInstances")?.GetValue(null) as IDictionary;
                bool log = Time.time >= nextLog;
                if (log) nextLog = Time.time + 120f;

                foreach (var o in FindObjectsOfType(controllerType))
                {
                    var c = o as Component;
                    if (c == null) continue;
                    float dist = Vector3.Distance(c.transform.position, lp.transform.position);
                    if (dist > 200f) continue;
                    var t = Traverse.Create(c);
                    int vid = 0;
                    try { vid = t.Property("PersistentZdoId").GetValue<int>(); } catch { }
                    int pieces = Count(t.Field("m_pieces").GetValue());
                    if (vid == 0 || pendingAll == null) continue;

                    int ownPending = 0, orphanTotal = 0, repaired = 0;
                    var keys = new List<object>();
                    foreach (var k in pendingAll.Keys) keys.Add(k);
                    foreach (var k in keys)
                    {
                        var list = pendingAll[k] as IList;
                        if (list == null) continue;
                        // drop destroyed entries
                        for (int i = list.Count - 1; i >= 0; i--) if (!(list[i] is ZNetView nv0) || nv0 == null || !nv0.IsValid()) list.RemoveAt(i);
                        int id = (int)k;
                        if (id == vid) { ownPending += list.Count; continue; }
                        if (active != null && active.Contains(id) && active[id] != null && !(active[id] is UnityEngine.Object uo && uo == null)) continue; // belongs to another live ship
                        orphanTotal += list.Count;
                        if (log) for (int q = 0; q < list.Count && q < 6; q++) { var ov = list[q] as ZNetView; if (ov != null && ov.IsValid()) Plugin.Log.LogInfo($"[vehicle] orphan {ov.name} queuedUnder={id} zdoParent={ov.GetZDO().GetInt(MBParentId)} dist={Vector3.Distance(ov.transform.position, c.transform.position):0} owner={ov.IsOwner()}"); }
                        if (!Plugin.AdoptForeign.Value) continue;
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            var nv = list[i] as ZNetView;
                            if (nv == null || !nv.IsValid()) continue;
                            if (Vector3.Distance(nv.transform.position, c.transform.position) > 500f) continue; // RAFT places the piece by its saved local position, so the stale world position does not matter
                            if (!nv.IsOwner()) nv.ClaimOwnership();
                            nv.GetZDO().Set(MBParentId, vid);
                            try { t.Method("ActivatePiece", new[] { typeof(ZNetView) }, new object[] { nv }).GetValue(); repaired++; list.RemoveAt(i); }
                            catch (Exception e) { Plugin.Log.LogWarning("[vehicle] ActivatePiece: " + e.Message); }
                        }
                    }
                    if (repaired > 0) Plugin.Log.LogInfo($"[vehicle] ship {vid}: re-attached {repaired} orphan pieces (queued under an old ship id)");
                    if (log) Plugin.Log.LogInfo($"[vehicle] ship {vid} dist={dist:0} pieces={pieces} pendingOwn={ownPending} orphansNearby={orphanTotal - repaired}");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("vehicle repair: " + e.Message); }
        }

        private static int Count(object o) => o is ICollection c ? c.Count : 0;
    }
}
