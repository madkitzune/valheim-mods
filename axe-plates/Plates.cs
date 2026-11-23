using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AxePlates
{
    internal static class Plates
    {
        private static string cachedTargets;
        private static string[] parsed = new string[0];

        public static PlateDef Of(ItemDrop.ItemData item)
        {
            if (item == null || item.m_customData == null || !Plugin.Enabled.Value) return null;
            if (item.m_customData.TryGetValue(PlateTable.DataKey, out var id) && PlateTable.ById.TryGetValue(id, out var d)) return d;
            return null;
        }

        public static PlateDef KitOf(ItemDrop.ItemData item)
        {
            string n = item?.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            if (n == null || !n.StartsWith("AxePlate_")) return null;
            return PlateTable.ById.TryGetValue(n.Substring("AxePlate_".Length), out var d) ? d : null;
        }

        public static bool IsTargetPrefab(string prefabName, ItemDrop.ItemData.SharedData s)
        {
            string t = Plugin.Targets.Value ?? "";
            if (t != cachedTargets)
            {
                cachedTargets = t;
                parsed = t.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parsed.Length; i++) parsed[i] = parsed[i].Trim();
            }
            foreach (var e in parsed)
            {
                if (e == "@axes")
                {
                    if (s != null && s.m_skillType == Skills.SkillType.Axes && s.m_attack != null && s.m_toolTier >= 0
                        && (s.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon || s.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon || s.m_itemType == ItemDrop.ItemData.ItemType.Tool))
                        return true;
                }
                else if (e.StartsWith("$")) { if (s != null && s.m_name == e) return true; }
                else if (prefabName == e) return true;
            }
            return false;
        }

        public static bool IsTarget(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null || !Plugin.Enabled.Value) return false;
            return IsTargetPrefab(item.m_dropPrefab != null ? item.m_dropPrefab.name : "", item.m_shared);
        }

        public static int Quality(ItemDrop.ItemData item)
        {
            if (item?.m_customData != null && item.m_customData.TryGetValue("axeplates.q", out var s) && int.TryParse(s, out var q)) return Mathf.Clamp(q, 1, 4);
            return 1;
        }

        public static readonly float[] DmgMul = { 1f, 1.05f, 1.09f, 1.14f };
        public static readonly float[] LightMul = { 1f, 1.11f, 1.22f, 1.33f };
        public static readonly float[] FireMul = { 1f, 1.25f, 1.5f, 1.75f };
        public static readonly float[] FrostMul = { 1f, 1.03f, 1.07f, 1.10f };

        public static string Loc(string token) => Localization.instance != null ? Localization.instance.Localize(token) : token;
    }

    // Drag a plate kit onto a target axe in the inventory.
    [HarmonyPatch(typeof(InventoryGui), "OnSelectedItem")]
    internal static class DragPatch
    {
        private static bool Prefix(InventoryGui __instance, ItemDrop.ItemData item)
        {
            try
            {
                if (!Plugin.Enabled.Value || item == null) return true;
                var tr = Traverse.Create(__instance);
                if (tr.Field("m_dragGo").GetValue<GameObject>() == null) return true;
                var dragItem = tr.Field("m_dragItem").GetValue<ItemDrop.ItemData>();
                if (dragItem != null && dragItem.m_dropPrefab != null && dragItem.m_dropPrefab.name == "Hammer" && Plates.IsTarget(item) && Plates.Of(item) != null)
                {
                    var old = Plates.Of(item);
                    int oq = Plates.Quality(item);
                    var inv = Player.m_localPlayer?.GetInventory();
                    var kitPrefab = ObjectDB.instance.GetItemPrefab(old.KitPrefab);
                    if (inv == null || kitPrefab == null) return true;
                    var back = inv.AddItem(old.KitPrefab, 1, oq, 0, 0L, "", false);
                    if (back == null) { Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_noroom"); return false; }
                    item.m_customData.Remove(PlateTable.DataKey);
                    item.m_customData.Remove("axeplates.q");
                    tr.Method("SetupDragItem", new[] { typeof(ItemDrop.ItemData), typeof(Inventory), typeof(int) }, new object[] { null, null, 1 }).GetValue();
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, string.Format(Plates.Loc("$axeplates_removed"), Plates.Loc(old.KitToken)));
                    Plugin.Log.LogInfo("plate " + old.Id + " removed");
                    return false;
                }
                var def = Plates.KitOf(dragItem);
                if (def == null || !Plates.IsTarget(item)) return true;
                var dragInv = tr.Field("m_dragInventory").GetValue<Inventory>();
                if (dragInv == null || !dragInv.ContainsItem(dragItem)) return true;

                bool had = item.m_customData.ContainsKey(PlateTable.DataKey);
                item.m_customData[PlateTable.DataKey] = def.Id;
                item.m_customData["axeplates.q"] = Mathf.Max(1, dragItem.m_quality).ToString();
                dragInv.RemoveItem(dragItem, 1);
                tr.Method("SetupDragItem", new[] { typeof(ItemDrop.ItemData), typeof(Inventory), typeof(int) }, new object[] { null, null, 1 }).GetValue();
                try { tr.Field("m_moveItemEffects").GetValue<EffectList>()?.Create(__instance.transform.position, Quaternion.identity); } catch { }
                string kitName = Plates.Loc(def.KitToken);
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, string.Format(Plates.Loc(had ? "$axeplates_replaced" : "$axeplates_applied"), kitName));
                Plugin.Log.LogInfo($"plate {def.Id} fitted on {item.m_dropPrefab?.name} (replaced={had})");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("drag patch: " + e);
                return true;
            }
        }
    }

    // Damage bonus: same method PoV and Epic Loot postfix; ours runs last so its percent applies on top.
    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDamage), new[] { typeof(int), typeof(float) })]
    internal static class DamagePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ItemDrop.ItemData __instance, ref HitData.DamageTypes __result)
        {
            var d = Plates.Of(__instance);
            if (d == null) return;
            int q = Plates.Quality(__instance) - 1;
            float f = 1f + d.DamagePct * Plates.DmgMul[q] / 100f;
            __result.m_slash *= f;
            __result.m_blunt *= f;
            __result.m_pierce *= f;
            __result.m_chop *= f;
            __result.m_lightning += d.CLightning.Value * Plates.LightMul[q];
            __result.m_frost += d.CFrost.Value * Plates.FrostMul[q];
            __result.m_fire += d.CFire.Value * Plates.FireMul[q];
        }
    }

    // Tool tier: vanilla melee and PoV's HitBuilder both create the hit and then call Attack.ModifyDamage.
    [HarmonyPatch(typeof(Attack), "ModifyDamage")]
    internal static class ToolTierPatch
    {
        private static readonly AccessTools.FieldRef<Attack, Humanoid> chRef = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
        private static void Postfix(Attack __instance, HitData hitData)
        {
            var d = Plates.Of(__instance?.GetWeapon());
            if (d == null || hitData == null) return;
            hitData.m_toolTier = (short)(hitData.m_toolTier + d.ToolBonus);
            if (d.Id == "Thunderblood" && Plugin.MissingHpPct.Value > 0f)
            {
                var ch = chRef(__instance) as Player;
                if (ch != null)
                {
                    float miss = Mathf.Max(0f, ch.GetMaxHealth() - ch.GetHealth());
                    float bonus = Mathf.Min(1f, miss * Plugin.MissingHpPct.Value / 100f);
                    if (bonus > 0f) hitData.m_damage.Modify(1f + bonus);
                }
            }
        }
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
    internal static class TooltipPatch
    {
        private static void Postfix(ItemDrop.ItemData item, ref string __result)
        {
            var d = Plates.Of(item);
            if (d == null || __result == null) return;
            string line = string.Format(Plates.Loc("$axeplates_tip"), Plates.Loc(d.KitToken), (d.DamagePct * Plates.DmgMul[Plates.Quality(item) - 1]).ToString("0.#"), d.ToolBonus) + (d.Quality ? " [" + Plates.Quality(item) + "]" : "");
            __result += "\n<color=orange>" + line + "</color>\n<color=grey>" + Plates.Loc("$axeplates_remove_hint") + "</color>";
        }
    }

    // Plate moulds appear next to vanilla moulds (same containers).
    [HarmonyPatch(typeof(Container), "AddDefaultItems")]
    internal static class MouldLoot
    {
        private static void Postfix(Container __instance)
        {
            try
            {
                var inv = __instance.GetInventory();
                if (inv == null || !Plugin.Enabled.Value) return;
                bool has = false;
                foreach (var it in inv.GetAllItems())
                {
                    string n = it.m_dropPrefab != null ? it.m_dropPrefab.name : "";
                    if (n.StartsWith("Mold") && n != "AxePlateMold") { has = true; break; }
                }
                if (!has || UnityEngine.Random.value > Plugin.MouldChance.Value) return;
                var prefab = ObjectDB.instance.GetItemPrefab("AxePlateMold");
                if (prefab == null) return;
                inv.AddItem("AxePlateMold", 1, 1, 0, 0L, "", false);
            }
            catch (Exception e) { Plugin.Log.LogWarning("mould loot: " + e.Message); }
        }
    }

    // Essence cost per quality is exactly 1/1/2/3 (vanilla formula would give 1/2/4).
    internal static class EssenceCost
    {
        public static readonly HashSet<Piece.Requirement> Reqs = new HashSet<Piece.Requirement>();
        public static readonly int[] Cost = { 1, 1, 2, 3 };
    }

    [HarmonyPatch(typeof(Piece.Requirement), nameof(Piece.Requirement.GetAmount), new[] { typeof(int) })]
    internal static class EssenceAmountPatch
    {
        private static void Postfix(Piece.Requirement __instance, int qualityLevel, ref int __result)
        {
            if (EssenceCost.Reqs.Count == 0 || !EssenceCost.Reqs.Contains(__instance)) return;
            __result = EssenceCost.Cost[Mathf.Clamp(qualityLevel, 1, 4) - 1];
        }
    }

    // Thunderblood: chain lightning. The attacker's own client applies it, so it works for every player holding the axe.
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class ChainLightning
    {
        private static bool busy;

        private static void Postfix(Character __instance, HitData hit)
        {
            if (busy || hit == null || !Plugin.Enabled.Value) return;
            try
            {
                var lp = Player.m_localPlayer;
                if (lp == null || hit.GetAttacker() != lp || __instance == lp) return;
                if (hit.m_hitType != HitData.HitType.PlayerHit) return;
                var w = lp.GetCurrentWeapon();
                var d = Plates.Of(w);
                if (d == null || d.Id != "Thunderblood") return;
                if (UnityEngine.Random.value > Plugin.ChainChance.Value) return;
                float dmg = hit.m_damage.m_lightning;
                if (dmg <= 0f) dmg = w.GetDamage().m_lightning;
                dmg *= Plugin.ChainFactor.Value;
                if (dmg <= 0f) return;
                int left = Plugin.ChainTargets.Value;
                var list = new List<Character>();
                foreach (var c in Character.GetAllCharacters())
                {
                    if (c == null || c == __instance || c == lp || c.IsDead() || c.IsTamed()) continue;
                    if (!BaseAI.IsEnemy(lp, c)) continue;
                    if (Vector3.Distance(c.transform.position, __instance.transform.position) > Plugin.ChainRadius.Value) continue;
                    list.Add(c);
                }
                list.Sort((a, b) => Vector3.Distance(a.transform.position, __instance.transform.position).CompareTo(Vector3.Distance(b.transform.position, __instance.transform.position)));
                busy = true;
                foreach (var c in list)
                {
                    if (left-- <= 0) break;
                    var h = new HitData();
                    h.m_damage.m_lightning = dmg;
                    h.m_point = c.GetCenterPoint();
                    h.m_dir = (c.transform.position - __instance.transform.position).normalized;
                    h.m_hitType = HitData.HitType.PlayerHit;
                    h.m_skill = Skills.SkillType.Axes;
                    h.SetAttacker(lp);
                    c.Damage(h);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("chain lightning: " + e.Message); }
            finally { busy = false; }
        }
    }

    // Small round badge in the inventory slot of an axe that carries a plate.
    [HarmonyPatch(typeof(InventoryGrid), "UpdateGui")]
    internal static class BadgePatch
    {
        private static readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private static readonly AccessTools.FieldRef<InventoryGrid, List<InventoryElement>> elemRef = AccessTools.FieldRefAccess<InventoryGrid, List<InventoryElement>>("m_elements");

        private static Sprite SpriteOf(PlateDef d)
        {
            if (!sprites.TryGetValue(d.Id, out var s)) { s = Icons.Badge(d.Tint); sprites[d.Id] = s; }
            return s;
        }

        private static void Postfix(InventoryGrid __instance)
        {
            try
            {
                var inv = __instance.GetInventory();
                if (inv == null) return;
                var elems = elemRef(__instance);
                int w = inv.GetWidth();
                for (int i = 0; i < elems.Count; i++)
                {
                    var el = elems[i];
                    if (el == null) continue;
                    var item = inv.GetItemAt(i % w, i / w);
                    var d = Plates.IsTarget(item) ? Plates.Of(item) : null;
                    var t = el.transform.Find("AxePlateBadge");
                    if (d == null) { if (t != null) t.gameObject.SetActive(false); continue; }
                    if (t == null)
                    {
                        var go = new GameObject("AxePlateBadge", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                        go.transform.SetParent(el.transform, false);
                        var rt = (RectTransform)go.transform;
                        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                        rt.pivot = new Vector2(0f, 1f);
                        rt.sizeDelta = new Vector2(22f, 22f);
                        rt.anchoredPosition = new Vector2(3f, -3f);
                        go.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
                        t = go.transform;
                    }
                    t.gameObject.SetActive(true);
                    t.GetComponent<UnityEngine.UI.Image>().sprite = SpriteOf(d);
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("badge: " + e.Message); }
        }
    }
}
