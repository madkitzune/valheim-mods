using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ArcaneArmory
{
    // Research tool: one CSV with every weapon/shield/bow/staff, its stats, attack and recipe.
    [HarmonyPatch(typeof(Player), "OnSpawned")]
    internal static class DumpWeapons
    {
        private static bool done;

        private static string Csv(object o) => "\"" + (o?.ToString() ?? "").Replace("\"", "'").Replace("\n", " ") + "\"";

        private static void Postfix()
        {
            if (done) return;
            done = true;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("prefab;name;type;skill;anim;toolTier;slash;pierce;blunt;chop;pickaxe;fire;frost;lightning;poison;spirit;block;parry;quality;durability;weight;attack;attackAnim;projectile;stamina;eitr;secondary;station;level;requirements");
                foreach (var go in ObjectDB.instance.m_items.OrderBy(g => g.name))
                {
                    var d = go.GetComponent<ItemDrop>();
                    if (d == null) continue;
                    var s = d.m_itemData.m_shared;
                    var t = s.m_itemType;
                    if (!(t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
                          || t == ItemDrop.ItemData.ItemType.Bow || t == ItemDrop.ItemData.ItemType.Shield || t == ItemDrop.ItemData.ItemType.Tool)) continue;
                    var dm = s.m_damages;
                    var a = s.m_attack;
                    var sa = s.m_secondaryAttack;
                    var rec = ObjectDB.instance.m_recipes.FirstOrDefault(r => r != null && r.m_item == d);
                    string reqs = rec != null ? string.Join(", ", rec.m_resources.Select(r => (r.m_resItem ? r.m_resItem.name : "?") + "x" + r.m_amount + "(+" + r.m_amountPerLevel + ")")) : "";
                    string name = Localization.instance != null ? Localization.instance.Localize(s.m_name) : s.m_name;
                    sb.AppendLine(string.Join(";", new[]
                    {
                        go.name, Csv(name), t.ToString(), s.m_skillType.ToString(), s.m_animationState.ToString(), s.m_toolTier.ToString(),
                        dm.m_slash.ToString("0.#"), dm.m_pierce.ToString("0.#"), dm.m_blunt.ToString("0.#"), dm.m_chop.ToString("0.#"), dm.m_pickaxe.ToString("0.#"),
                        dm.m_fire.ToString("0.#"), dm.m_frost.ToString("0.#"), dm.m_lightning.ToString("0.#"), dm.m_poison.ToString("0.#"), dm.m_spirit.ToString("0.#"),
                        s.m_blockPower.ToString("0.#"), s.m_deflectionForce.ToString("0.#"), s.m_maxQuality.ToString(), s.m_maxDurability.ToString("0"), s.m_weight.ToString("0.#"),
                        a != null ? a.m_attackType.ToString() : "", a != null ? a.m_attackAnimation : "", a != null && a.m_attackProjectile ? a.m_attackProjectile.name : "",
                        a != null ? a.m_attackStamina.ToString("0.#") : "", a != null ? a.m_attackEitr.ToString("0.#") : "",
                        sa != null && !string.IsNullOrEmpty(sa.m_attackAnimation) ? sa.m_attackAnimation : "",
                        rec != null && rec.m_craftingStation ? rec.m_craftingStation.name : "", rec != null ? rec.m_minStationLevel.ToString() : "", Csv(reqs)
                    }));
                }
                string path = Path.Combine(BepInEx.Paths.ConfigPath, "ArcaneArmory_weapons.csv");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                Plugin.Log.LogInfo("weapon dump written: " + path);
            }
            catch (Exception e) { Plugin.Log.LogWarning("dump failed: " + e); }
        }
    }
}
