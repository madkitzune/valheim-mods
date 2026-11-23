using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AxePlates
{
    // RAFT ship sailing speed = ship size + number and quality of sails.
    //   sail power: raft mast 1, karve mast 2, longship mast 4, drakkar mast 6, custom sails: area / CustomSailAreaPerPower
    //   power needed: pieces / PiecesPerSailPower (at least 1)
    //   r = power / needed;  speed = TopSpeed * r / (r + 1)   (r = 1 -> half of top speed, more sails -> closer to top speed)
    internal static class ShipSpeed
    {
        private static FieldInfo fPieces, fT1, fT2, fT3, fT4, fCustom;
        private static MethodInfo mArea;

        public static void Apply(Harmony h)
        {
            try
            {
                var pc = AccessTools.TypeByName("ValheimVehicles.Controllers.VehiclePiecesController");
                if (pc == null) return;
                var force = AccessTools.Method(pc, "GetSailingForce");
                mArea = AccessTools.Method(pc, "GetTotalSailArea", new[] { typeof(bool) });
                fPieces = AccessTools.Field(pc, "m_pieces");
                fT1 = AccessTools.Field(pc, "numberOfTier1Sails"); fT2 = AccessTools.Field(pc, "numberOfTier2Sails");
                fT3 = AccessTools.Field(pc, "numberOfTier3Sails"); fT4 = AccessTools.Field(pc, "numberOfTier4Sails");
                fCustom = AccessTools.Field(pc, "customSailsArea");
                if (force == null || mArea == null || fPieces == null || fT1 == null || fCustom == null) { Plugin.Log.LogInfo("[ship] RAFT sail formula not found, ship speed left to RAFT"); return; }
                h.Patch(force, postfix: new HarmonyMethod(typeof(ShipSpeed), nameof(ForcePostfix)));
                var wheel = AccessTools.TypeByName("ValheimVehicles.Propulsion.Rudder.SteeringWheelComponent");
                var hover = wheel != null ? AccessTools.Method(wheel, "GetHoverText") : null;
                if (hover != null) h.Patch(hover, postfix: new HarmonyMethod(typeof(ShipSpeed), nameof(HoverPostfix)));
                Plugin.Log.LogInfo("[ship] sailing speed = ship size + sails");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[ship] speed patch: " + e.Message); }
        }

        public struct Stats { public int Pieces; public float Power, Needed, Speed; }

        public static Stats Compute(object pc)
        {
            var s = new Stats();
            mArea.Invoke(pc, new object[] { false });   // refreshes the sail counters (cached by RAFT)
            s.Pieces = fPieces.GetValue(pc) is ICollection c ? c.Count : 0;
            s.Power = (int)fT1.GetValue(pc) * Plugin.SailPowerT1.Value + (int)fT2.GetValue(pc) * Plugin.SailPowerT2.Value
                    + (int)fT3.GetValue(pc) * Plugin.SailPowerT3.Value + (int)fT4.GetValue(pc) * Plugin.SailPowerT4.Value
                    + (float)fCustom.GetValue(pc) / Mathf.Max(1f, Plugin.CustomSailAreaPerPower.Value);
            s.Needed = Mathf.Max(1f, s.Pieces / Mathf.Max(1f, Plugin.PiecesPerSailPower.Value));
            float r = s.Power / s.Needed;
            s.Speed = s.Power <= 0f ? 0f : Plugin.TopSailSpeed.Value * r / (r + 1f);
            return s;
        }

        private static void ForcePostfix(object __instance, ref float __result)
        {
            if (!Plugin.ShipSpeedEnabled.Value) return;
            try { __result = Compute(__instance).Speed; } catch { }
        }

        private static void HoverPostfix(object __instance, ref string __result)
        {
            if (!Plugin.ShipSpeedEnabled.Value) return;
            try
            {
                var ci = Traverse.Create(__instance).Field("ControllersInstance").GetValue();
                var pc = ci != null ? Traverse.Create(ci).Property("PiecesController").GetValue() : null;
                if (pc == null || (pc is UnityEngine.Object uo && uo == null)) return;
                var s = Compute(pc);
                bool ru = Localization.instance != null && Localization.instance.GetSelectedLanguage() == "Russian";
                __result += ru
                    ? $"\n<color=#9fd3ff>Паруса: сила {s.Power:0.#} из нужных {s.Needed:0.#} (частей {s.Pieces}), скорость под парусом ~{s.Speed:0.#}</color>"
                    : $"\n<color=#9fd3ff>Sails: power {s.Power:0.#} of {s.Needed:0.#} needed ({s.Pieces} pieces), sailing speed ~{s.Speed:0.#}</color>";
            }
            catch { }
        }
    }
}
