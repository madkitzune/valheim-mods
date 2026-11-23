using System;
using System.Reflection;
using HarmonyLib;

namespace AxePlates
{
    // Path of Valheiman: an axe with a plate gets one extra prefix place for rune-table / rune-anvil enchanting.
    // PoV's capacity function does not know the item, so the item is remembered while Why()/Enchant() run.
    internal static class PovCompat
    {
        [ThreadStatic] private static ItemDrop.ItemData ctx;

        public static void Apply(Harmony h)
        {
            if (!Plugin.ExtraPrefix.Value) return;
            try
            {
                var enchants = AccessTools.TypeByName("PassiveTree.RunicCraft.Enchants");
                var roller = AccessTools.TypeByName("PassiveTree.Loot.LootRoller");
                if (enchants == null || roller == null) { Plugin.Log.LogInfo("PoV not found: extra prefix place disabled"); return; }
                var why = AccessTools.Method(enchants, "Why");
                var enchant = AccessTools.Method(roller, "Enchant");
                var cap = AccessTools.Method(roller, "Capacity");
                if (why == null || enchant == null || cap == null) { Plugin.Log.LogWarning("PoV methods not found (Why/Enchant/Capacity): extra prefix place disabled"); return; }
                var pre = new HarmonyMethod(typeof(PovCompat), nameof(SetCtx));
                var fin = new HarmonyMethod(typeof(PovCompat), nameof(ClearCtx));
                h.Patch(why, prefix: pre, finalizer: fin);
                h.Patch(enchant, prefix: pre, finalizer: fin);
                h.Patch(cap, postfix: new HarmonyMethod(typeof(PovCompat), nameof(CapPostfix)));
                Plugin.Log.LogInfo("PoV compat: plated axes get +1 prefix place");
            }
            catch (Exception e) { Plugin.Log.LogWarning("PoV compat failed: " + e.Message); }
        }

        // Why(Player p, ItemData it, Row r, int i) and Enchant(ItemData item, ...) -> find the ItemData argument.
        private static void SetCtx(object[] __args)
        {
            ctx = null;
            foreach (var a in __args) if (a is ItemDrop.ItemData it) { ctx = it; break; }
        }

        private static void ClearCtx() { ctx = null; }

        private static void CapPostfix(object __1, ref int __result)
        {
            if (ctx == null || __result <= 0) return;
            if (__1 == null || __1.ToString() != "Prefix") return;
            if (Plates.IsTarget(ctx) && Plates.Of(ctx) != null) __result += 1;
        }
    }
}
