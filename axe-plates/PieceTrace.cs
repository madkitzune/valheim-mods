using System;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;

namespace AxePlates
{
    // Diagnostics: who removes ship pieces (RAFT hull, walls, floors near a vehicle)? Logs the cause with a short call stack.
    internal static class PieceTrace
    {
        private static int logged;
        private const int Max = 300;

        private static bool Interesting(GameObject go)
        {
            if (go == null) return false;
            string n = go.name;
            if (n.StartsWith("ValheimVehicles_") || n.StartsWith("MBRaft") || n.Contains("hull") || n.Contains("Hull")) return true;
            return go.GetComponentInParent<Component>() != null && go.transform.parent != null && go.transform.parent.name.Contains("Vehicle");
        }

        private static string Stack()
        {
            var st = new StackTrace(3, false);
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var f in st.GetFrames())
            {
                var m = f.GetMethod();
                if (m?.DeclaringType == null) continue;
                sb.Append(m.DeclaringType.Name).Append('.').Append(m.Name).Append(" < ");
                if (++n >= 7) break;
            }
            return sb.ToString();
        }

        [HarmonyPatch(typeof(WearNTear), "Destroy")]
        private static class WearDestroy
        {
            private static void Prefix(WearNTear __instance, HitData hitData)
            {
                if (!Plugin.Diagnostics.Value || logged >= Max || !Interesting(__instance.gameObject)) return;
                logged++;
                Plugin.Log.LogInfo($"[trace] WearNTear.Destroy {__instance.name} pos={__instance.transform.position} hit={(hitData != null ? hitData.m_hitType.ToString() + ":" + hitData.m_damage.GetTotalDamage().ToString("0") : "none")} by {Stack()}");
            }
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy))]
        private static class SceneDestroy
        {
            private static void Prefix(GameObject go)
            {
                if (!Plugin.Diagnostics.Value || logged >= Max || !Interesting(go)) return;
                logged++;
                Plugin.Log.LogInfo($"[trace] ZNetScene.Destroy {go.name} pos={go.transform.position} by {Stack()}");
            }
        }

        // The three prefab hashes PoV reports as "broken records" (objects removed without ZNetScene.Destroy).
        private static readonly System.Collections.Generic.HashSet<int> Suspects = new System.Collections.Generic.HashSet<int> { 179580409, 1327122689, 1353216162 };
        private static readonly System.Collections.Generic.HashSet<int> named = new System.Collections.Generic.HashSet<int>();

        [HarmonyPatch(typeof(ZNetScene), "CreateObject")]
        private static class CreatedSuspect
        {
            private static void Postfix(ZDO zdo, GameObject __result)
            {
                if (!Plugin.Diagnostics.Value || __result == null || zdo == null) return;
                int h = zdo.GetPrefab();
                if (Suspects.Contains(h) && named.Add(h))
                    Plugin.Log.LogInfo($"[trace] suspect hash {h} = prefab '{__result.name}' pos={zdo.GetPosition()}");
            }
        }

        [HarmonyPatch(typeof(ZNetView), "OnDestroy")]
        private static class ViewDestroyed
        {
            private static void Prefix(ZNetView __instance)
            {
                if (!Plugin.Diagnostics.Value || logged >= Max) return;
                var zdo = __instance.GetZDO();
                if (zdo == null || !Suspects.Contains(zdo.GetPrefab())) return;
                logged++;
                Plugin.Log.LogInfo($"[trace] ZNetView.OnDestroy {__instance.name} hash={zdo.GetPrefab()} pos={__instance.transform.position} by {Stack()}");
            }
        }
    }
}
