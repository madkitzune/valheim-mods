using System;
using HarmonyLib;

namespace AxePlates
{
    // ZNetScene.CreateObjects "destroys" ZDOs with an unknown prefab through ZDOMan.DestroyZDO, which only queues the uid.
    // A ZDO whose uid is 0:0 (seen with RAFT's persistent ids) is never removed from its sector list, so it is retried
    // and logged every frame (tens of thousands of lines, lag). Remove such a ZDO from the sector right away.
    [HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.DestroyZDO))]
    internal static class ZdoCleanup
    {
        private static int removed;

        private static bool Prefix(ZDOMan __instance, ZDO zdo)
        {
            try
            {
                if (zdo == null || zdo.m_uid != ZDOID.None) return true;
                __instance.RemoveFromSector(zdo, zdo.GetSectorIndex());
                if (++removed <= 3 || removed % 5000 == 0) Plugin.Log.LogInfo("[zdo] removed dead ZDO 0:0 from its sector (" + removed + ")");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("zdo cleanup: " + e.Message);
                return true;
            }
        }
    }

    // The vanilla "Destroyed invalid prefab ZDO:0:0" line is written for every such dead ZDO (dozens per second near a RAFT ship).
    // It is harmless after the cleanup above, so it is not written to the log at all (a count is logged instead).
    [HarmonyPatch(typeof(ZLog), nameof(ZLog.Log), new[] { typeof(object) })]
    internal static class ZeroZdoLogFilter
    {
        private static int hidden;

        private static bool Prefix(object o)
        {
            if (!(o is string s) || s != "Destroyed invalid prefab ZDO:0:0") return true;
            if (++hidden % 20000 == 0) Plugin.Log.LogInfo("[zdo] hidden " + hidden + " 'Destroyed invalid prefab ZDO:0:0' lines so far");
            return false;
        }
    }
}
