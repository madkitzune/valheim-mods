using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ArcaneArmory
{
    // How weapons are obtained: plans (blueprints) unlock recipes, plans drop from mobs and chests, Sigrid sells some weapons.
    internal static class Acquire
    {
        private static string Key(WeaponDef d) => "aa_plan_" + d.Id;

        private static readonly AccessTools.FieldRef<Player, HashSet<string>> known = AccessTools.FieldRefAccess<Player, HashSet<string>>("m_knownRecipes");

        public static bool IsUnlocked(Player p, WeaponDef d) => p != null && known(p).Contains(Key(d));

        public static WeaponDef PlanOf(ItemDrop.ItemData item)
        {
            string n = item?.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            if (n == null || !n.StartsWith("AA_Plan_")) return null;
            return Defs.ById.TryGetValue("AA_" + n.Substring("AA_Plan_".Length), out var d) ? d : null;
        }

        // Recipes of plan weapons stay hidden until the plan is learned.
        [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), new[] { typeof(Recipe), typeof(bool), typeof(int), typeof(int) })]
        private static class HideRecipes
        {
            private static bool Prefix(Player __instance, Recipe recipe, ref bool __result)
            {
                string n = recipe?.m_item != null ? recipe.m_item.gameObject.name : null;
                if (n == null || !Defs.ById.TryGetValue(n, out var d) || d.Acquire != "plan") return true;
                if (IsUnlocked(__instance, d)) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.CanConsumeItem))]
        private static class CanUse
        {
            private static void Postfix(ItemDrop.ItemData item, ref bool __result)
            {
                if (PlanOf(item) != null) __result = true;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
        private static class UsePlan
        {
            private static bool Prefix(Player __instance, Inventory inventory, ItemDrop.ItemData item, ref bool __result)
            {
                var d = PlanOf(item);
                if (d == null) return true;
                if (IsUnlocked(__instance, d))
                {
                    __instance.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$aa_plan_known"));
                    __result = false;
                    return false;
                }
                known(__instance).Add(Key(d));
                inventory.RemoveOneItem(item);
                __instance.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$aa_plan_learned", Localization.instance.Localize(d.Token)));
                Plugin.Log.LogInfo("plan learned: " + d.Id);
                __result = true;
                return false;
            }
        }

        // ---- sources ----
        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        private static class MobDrops
        {
            private static void Postfix(CharacterDrop __instance, ref List<KeyValuePair<GameObject, int>> __result)
            {
                try
                {
                    string n = __instance.gameObject.name.Replace("(Clone)", "").Trim();
                    foreach (var d in Defs.All)
                    {
                        if (!d.Enabled || d.Acquire != "plan" || d.Mobs.Length == 0 || !d.Mobs.Contains(n)) continue;
                        if (UnityEngine.Random.value > d.Chance) continue;
                        var prefab = ObjectDB.instance?.GetItemPrefab(d.PlanId);
                        if (prefab != null) __result.Add(new KeyValuePair<GameObject, int>(prefab, 1));
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("mob drops: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(Container), "AddDefaultItems")]
        private static class ChestLoot
        {
            private static void Postfix(Container __instance)
            {
                try
                {
                    string n = __instance.gameObject.name.Replace("(Clone)", "").Trim();
                    var inv = __instance.GetInventory();
                    if (inv == null) return;
                    foreach (var d in Defs.All)
                    {
                        if (!d.Enabled || d.Acquire != "plan" || d.Chests.Length == 0 || !d.Chests.Any(c => n.StartsWith(c))) continue;
                        if (UnityEngine.Random.value > d.Chance) continue;
                        inv.AddItem(d.PlanId, 1, 1, 0, 0L, "", false);
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("chest loot: " + e.Message); }
            }
        }

        // ---- Sigrid (Path of Valheiman's wandering trader) ----
        public static void ApplySigrid(Harmony h)
        {
            try
            {
                var sigrid = AccessTools.TypeByName("PassiveTree.Encounters.Sigrid");
                var open = AccessTools.TypeByName("PassiveTree.Encounters.OpenGear");
                if (sigrid == null) { Plugin.Log.LogInfo("PoV Sigrid not found: trader weapons disabled"); return; }
                var pool = AccessTools.Method(sigrid, "GearPool");
                var keep = open != null ? AccessTools.Method(open, "Keep") : null;
                if (pool != null) h.Patch(pool, postfix: new HarmonyMethod(typeof(Acquire), nameof(AddToPool)));
                if (keep != null) h.Patch(keep, postfix: new HarmonyMethod(typeof(Acquire), nameof(AddToKept)));
                Plugin.Log.LogInfo("PoV compat: Sigrid sells trader weapons");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Sigrid compat: " + e.Message); }
        }

        private const int Weight = 14;   // copies in the roll pool: raises the chance against hundreds of vanilla items

        private static void Add(List<GameObject> list)
        {
            if (list == null || ObjectDB.instance == null) return;
            foreach (var d in Defs.All.Where(x => x.Enabled && x.Acquire == "sigrid"))
            {
                var go = ObjectDB.instance.GetItemPrefab(d.Id);
                if (go == null) continue;
                for (int i = 0; i < Weight; i++) list.Add(go);
            }
        }

        private static void AddToPool(ref List<GameObject> __result) => Add(__result);

        private static void AddToKept(ref List<GameObject> __result)
        {
            if (__result == null) return;
            // Keep() filters to gear the player can already obtain; trader items have no recipe, so re-add them once
            if (!__result.Any(g => g != null && Defs.ById.TryGetValue(g.name, out var d) && d.Enabled && d.Acquire == "sigrid")) Add(__result);
        }
    }
}
