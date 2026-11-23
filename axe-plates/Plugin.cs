using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace AxePlates
{
    [BepInPlugin(Guid, "Axe Plates", "1.3.15")]
    [BepInDependency(Main.ModGuid)]
    [BepInDependency("treextr.pathofvalheiman", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("mkz.itemdrawers", BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.axeplates";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> Targets;
        internal static ConfigEntry<bool> Diagnostics;
        internal static ConfigEntry<bool> VehicleKick;
        internal static ConfigEntry<bool> RepairOrphans;
        internal static ConfigEntry<bool> AdoptForeign;
        internal static ConfigEntry<bool> ShipSpeedEnabled;
        internal static ConfigEntry<float> SailPowerT1, SailPowerT2, SailPowerT3, SailPowerT4, CustomSailAreaPerPower, PiecesPerSailPower, TopSailSpeed;
        private Harmony harmonyRef;
        internal static ConfigEntry<float> MouldChance, MissingHpPct, ChainChance, ChainRadius, ChainFactor;
        internal static ConfigEntry<int> ChainTargets;
        internal static ConfigEntry<bool> CraftableMould;
        internal static ConfigEntry<int> FallbackVariant;
        internal static ConfigEntry<bool> ExtraPrefix;
        private static bool built;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, new ConfigDescription("Master switch", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            Targets = Config.Bind("General", "Targets", "AxeEarly",
                new ConfigDescription("Comma separated list of items that accept plates: prefab names or $item tokens. Special entry @axes = every axe-skill weapon. Default: AxeEarly (Early Axes = Древние топоры)", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            Diagnostics = Config.Bind("General", "Diagnostics", true, "Log stats of target items and recipe resolution at world load");
            MouldChance = Config.Bind("Mould", "ChanceInMouldChests", 1f, new ConfigDescription("Chance that a container holding a vanilla Nord mould also gets an axe plate mould (0..1)", new AcceptableValueRange<float>(0f, 1f), new ConfigurationManagerAttributes { IsAdminOnly = true }));
            CraftableMould = Config.Bind("Mould", "CraftableAtForge", false, "Fallback: allow crafting the mould (10 iron + 5 bloodgold) at the Nord cast station. Needs restart.");
            MissingHpPct = Config.Bind("Thunderblood", "DamagePctPerMissingHealth", 0.2f, new ConfigDescription("Thunderblood plate: percent damage per missing health point (capped at +100%)", null, new ConfigurationManagerAttributes { IsAdminOnly = true }));
            var adm = new ConfigurationManagerAttributes { IsAdminOnly = true };
            ChainChance = Config.Bind("Thunderblood", "ChainLightningChance", 0.25f, new ConfigDescription("Chance per hit to trigger chain lightning", new AcceptableValueRange<float>(0f, 1f), adm));
            ChainRadius = Config.Bind("Thunderblood", "ChainLightningRadius", 10f, new ConfigDescription("Radius around the victim in metres", null, adm));
            ChainTargets = Config.Bind("Thunderblood", "ChainLightningTargets", 3, new ConfigDescription("Max number of extra targets", null, adm));
            ChainFactor = Config.Bind("Thunderblood", "ChainLightningDamageFactor", 1f, new ConfigDescription("Chain damage = lightning damage of the hit x this", null, adm));
            FallbackVariant = Config.Bind("Visual", "FallbackVariant", 0, new ConfigDescription("If the plate sits on the wrong side of the axe head (bounds-based placement), try 1, 2 or 3", new AcceptableValueRange<int>(0, 3)));
            ExtraPrefix = Config.Bind("Compat", "ExtraPrefixPlace", true, "Path of Valheiman: an axe with a plate gets one extra prefix place for rune-table enchanting");
            RepairOrphans = Config.Bind("Raft", "SyncShipPieces", false, "Only for the old ValheimRAFT 4.3.5 fork: keep the world position of ALL pieces of a RAFT ship (also unloaded ones) in the ship's zone, like ValheimRAFT 1.4.9 did");
            AdoptForeign = Config.Bind("Raft", "AdoptPiecesOfOtherShipIds", false, "Risky: attach pieces queued under another (removed) ship id to the active ship. Can misplace pieces if they belonged to a different ship. Off by default.");
            ShipSpeedEnabled = Config.Bind("ShipSpeed", "Enabled", true, "RAFT ships: sailing speed depends on ship size (pieces) and the number and quality of sails. Keep the same on every player.");
            SailPowerT1 = Config.Bind("ShipSpeed", "PowerRaftMast", 1f, "Sail power of a raft mast (tier 1)");
            SailPowerT2 = Config.Bind("ShipSpeed", "PowerKarveMast", 2f, "Sail power of a karve mast (tier 2)");
            SailPowerT3 = Config.Bind("ShipSpeed", "PowerLongshipMast", 4f, "Sail power of a longship mast (tier 3)");
            SailPowerT4 = Config.Bind("ShipSpeed", "PowerDrakkarMast", 6f, "Sail power of a drakkar mast (tier 4)");
            CustomSailAreaPerPower = Config.Bind("ShipSpeed", "CustomSailAreaPerPower", 20f, "Custom (drawn) sails: this much sail area gives 1 power");
            PiecesPerSailPower = Config.Bind("ShipSpeed", "PiecesPerSailPower", 40f, "Ship size: every this many pieces need 1 sail power for half of the top speed");
            TopSailSpeed = Config.Bind("ShipSpeed", "TopSailSpeed", 16f, "Speed approached with many more sails than needed (power = needed gives half)");
            VehicleKick = Config.Bind("Diagnostics", "VehicleKick", false, "Experimental: nudge RAFT to activate pending ship pieces (was suspected to cause stutter and piece churn, off by default)");
            PlateTable.BindConfig(this);
            PrefabManager.OnVanillaPrefabsAvailable += Build;
            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            PovCompat.Apply(harmony);
            harmonyRef = harmony;
            gameObject.AddComponent<VehicleWatchdog>();
            gameObject.AddComponent<ShipSync>();
            Log.LogInfo("Axe Plates loaded");
        }

        // RAFT loads after this plugin: patch it once every plugin is in
        private void Start() { ShipSpeed.Apply(harmonyRef); }

        // Jotunn fires OnVanillaPrefabsAvailable from ObjectDB.CopyOtherDB: the vanilla items live in the menu ObjectDB, not in ObjectDB.instance yet.
        private static ObjectDB Db
        {
            get
            {
                try
                {
                    var prop = typeof(PrefabManager).GetProperty("MenuObjectDB", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    var o = prop != null ? prop.GetValue(PrefabManager.Instance) as ObjectDB : null;
                    if (o != null) return o;
                }
                catch { }
                return ObjectDB.instance;
            }
        }

        private static GameObject Item(string name)
        {
            if (name == null) return null;
            var db = Db;
            if (db != null && db.m_items != null)
                foreach (var go in db.m_items) if (go != null && go.name == name) return go;
            return null;
        }

        private static string FindPrefab(string token, string fallback)
        {
            try
            {
                foreach (var go in Db.m_items)
                {
                    var d = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (d != null && d.m_itemData.m_shared.m_name == token) return go.name;
                }
            }
            catch (Exception e) { Log.LogWarning("lookup " + token + ": " + e.Message); }
            if (fallback != null && Item(fallback) != null) return fallback;
            return null;
        }

        private static Material IngotMaterial(string prefab)
        {
            var go = prefab != null ? Item(prefab) : null;
            if (go == null) return null;
            var r = go.GetComponentInChildren<MeshRenderer>(true);
            return r != null ? r.sharedMaterial : null;
        }

        private static void Build()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Build;
            if (built) return;
            built = true;
            try { BuildAll(); } catch (Exception e) { Log.LogError("Build failed: " + e); }
        }

        private static void BuildAll()
        {
            var en = new Dictionary<string, string>();
            var ru = new Dictionary<string, string>();
            en["axeplates_tip"] = "Axe plate: {0} (+{1}% damage, tool tier +{2})";
            ru["axeplates_tip"] = "Обух: {0} (+{1}% урона, уровень инструмента +{2})";
            en["axeplates_remove_hint"] = "Remove the plate: drag a Hammer onto the axe";
            ru["axeplates_remove_hint"] = "Снять обух: перетащите Молоток на топор";
            en["axeplates_removed"] = "Plate removed: {0}";
            ru["axeplates_removed"] = "Обух снят: {0}";
            en["axeplates_applied"] = "Plate fitted: {0}";
            ru["axeplates_applied"] = "Обух прибит: {0}";
            en["axeplates_replaced"] = "Plate replaced: {0}";
            ru["axeplates_replaced"] = "Обух заменён: {0}";
            en["axeplates_desc"] = "A curved metal band with a row of nails. Drag it onto Early Axes in your inventory to nail it onto the blade. A new plate replaces the old one.";
            ru["axeplates_desc"] = "Изогнутая металлическая полоса с рядом гвоздей. Перетащите на Древние топоры в инвентаре, чтобы прибить к лезвию. Новый обух заменяет старый.";
            en["axeplates_cast_desc"] = "Rough cast of an axe plate. Quench it on the same forge that tempers Nord weapons.";
            ru["axeplates_cast_desc"] = "Необработанная заготовка обуха. Закаляется там же, где закаляется нордское оружие.";
            en["axeplates_nails_desc"] = "Nails for axe plates.";
            ru["axeplates_nails_desc"] = "Гвозди для обухов.";

            BuildMould(en, ru);
            foreach (var d in PlateTable.All)
            {
                d.IngotPrefab = FindPrefab(d.IngotToken, d.IngotPrefabFallback);
                d.ExtraPrefab = d.ExtraToken != null ? FindPrefab(d.ExtraToken, d.ExtraPrefabFallback) : null;
                bool ok = d.IngotPrefab != null && (d.ExtraToken == null || d.ExtraPrefab != null)
                          && (d.NailsVanilla == null || Item(d.NailsVanilla) != null);
                Log.LogInfo($"plate {d.Id}: ingot={d.IngotPrefab} extra={d.ExtraPrefab} -> {(ok ? "ok" : "UNAVAILABLE (recipe skipped)")}");
                d.Available = ok;
                d.Mat = IngotMaterial(d.IngotPrefab);

                en["axeplate_" + d.Id.ToLowerInvariant()] = d.En + " axe plate";
                ru["axeplate_" + d.Id.ToLowerInvariant()] = "Обух (" + d.Ru + ")";
                en["axeplatenail_" + d.Id.ToLowerInvariant()] = d.En + " nails";
                ru["axeplatenail_" + d.Id.ToLowerInvariant()] = "Гвозди (" + d.Ru + ")";
                if (!ok) continue;

                // custom nails (metals without vanilla nails)
                if (d.NailsVanilla == null && d.Parent == null && Item(d.NailPrefab) == null && PrefabManager.Instance.GetPrefab(d.NailPrefab) == null)
                {
                    var nails = PrefabManager.Instance.CreateClonedPrefab(d.NailPrefab, "IronNails");
                    var nd = nails.GetComponent<ItemDrop>();
                    var sh = nd.m_itemData.m_shared;
                    sh.m_name = d.NailToken;
                    sh.m_description = "$axeplates_nails_desc";
                    var src = Item("IronNails").GetComponent<ItemDrop>().m_itemData.m_shared.m_icons;
                    sh.m_icons = new[] { Icons.Tint(src != null && src.Length > 0 ? src[0] : null, d.Tint) };
                    foreach (var r in nails.GetComponentsInChildren<Renderer>(true))
                    {
                        r.sharedMaterials = r.sharedMaterials.Select(m => Icons.TintedMaterial(m, d.Tint)).ToArray();
                    }
                    ItemManager.Instance.AddItem(new CustomItem(nails, false));
                    var nr = new RecipeConfig
                    {
                        Name = "Recipe_" + d.NailPrefab,
                        Item = d.NailPrefab,
                        Amount = 12,
                        CraftingStation = "forge",
                        MinStationLevel = Math.Max(1, d.CStation.Value - 1),
                        Requirements = new[] { new RequirementConfig(d.IngotPrefab, 1, 0, false) }
                    };
                    ItemManager.Instance.AddRecipe(new CustomRecipe(nr));
                }

                // the plate itself
                var kit = PrefabManager.Instance.CreateClonedPrefab(d.KitPrefab, "IronNails");
                var ksh = kit.GetComponent<ItemDrop>().m_itemData.m_shared;
                ksh.m_name = d.KitToken;
                ksh.m_description = "$axeplates_desc";
                ksh.m_icons = new[] { Icons.PlateIcon(d.Tint) };
                ksh.m_maxStackSize = d.Quality ? 1 : 20;
                if (d.Quality) ksh.m_maxQuality = 4;
                ksh.m_weight = 1f;
                ksh.m_itemType = ItemDrop.ItemData.ItemType.Material;
                ksh.m_value = 0;
                foreach (var r in kit.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                PlateMesh.AttachKitModel(kit, d);
                ItemManager.Instance.AddItem(new CustomItem(kit, false));

                string station = "forge"; int level = d.CStation.Value;
                var reqs = new List<RequirementConfig>();
                if (d.Parent != null)
                {
                    // elemental variant: Bloodgold plate + gold + essence, station/level copied from the vanilla enchanted weapon
                    Proto(d.TemperProto, ref station, ref level);
                    reqs.Add(new RequirementConfig("AxePlate_" + d.Parent, 1, 0, false));
                    reqs.Add(new RequirementConfig(d.IngotPrefab, d.CIngots.Value, 10, false)); // quality 2..4 costs +10/+20/+40 (vanilla formula)
                    if (d.ExtraPrefab != null) reqs.Add(new RequirementConfig(d.ExtraPrefab, d.ExtraCount, 1, false));
                }
                else if (d.CastProto != null)
                {
                    // Bloodgold: mould -> cast (black forge) -> quench (ice forge), stations copied from the vanilla Nord axe chain
                    string cs = "forge"; int cl = d.CStation.Value;
                    Proto(d.CastProto, ref cs, ref cl);
                    Proto(d.TemperProto, ref station, ref level);
                    var cast = PrefabManager.Instance.CreateClonedPrefab("AxePlateCast_" + d.Id, "IronNails");
                    var csh = cast.GetComponent<ItemDrop>().m_itemData.m_shared;
                    csh.m_name = "$axeplatecast_" + d.Id.ToLowerInvariant();
                    csh.m_description = "$axeplates_cast_desc";
                    csh.m_icons = new[] { Icons.PlateIcon(d.Tint * 0.55f) };
                    csh.m_maxStackSize = 20; csh.m_weight = 2f; csh.m_itemType = ItemDrop.ItemData.ItemType.Material;
                    foreach (var r in cast.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    PlateMesh.AttachKitModel(cast, d);
                    ItemManager.Instance.AddItem(new CustomItem(cast, false));
                    en["axeplatecast_" + d.Id.ToLowerInvariant()] = "Rough " + d.En + " axe plate (cast)";
                    ru["axeplatecast_" + d.Id.ToLowerInvariant()] = "Заготовка обуха (" + d.Ru + ")";
                    var cr = new List<RequirementConfig>();
                    cr.Add(new RequirementConfig("AxePlateMold", 1, 0, false));
                    cr.Add(new RequirementConfig(d.IngotPrefab, d.CIngots.Value, 0, false));
                    cr.Add(new RequirementConfig(d.NailPrefab, d.CNails.Value, 0, false));
                    ItemManager.Instance.AddRecipe(new CustomRecipe(new RecipeConfig { Name = "Recipe_AxePlateCast_" + d.Id, Item = "AxePlateCast_" + d.Id, Amount = 1, CraftingStation = cs, MinStationLevel = cl, Requirements = cr.ToArray() }));
                    reqs.Add(new RequirementConfig("AxePlateCast_" + d.Id, 1, 0, false));
                }
                else
                {
                    reqs.Add(new RequirementConfig(d.IngotPrefab, d.CIngots.Value, 0, false));
                    reqs.Add(new RequirementConfig(d.NailPrefab, d.CNails.Value, 0, false));
                }
                ItemManager.Instance.AddRecipe(new CustomRecipe(new RecipeConfig
                {
                    Name = "Recipe_" + d.KitPrefab, Item = d.KitPrefab, Amount = 1, CraftingStation = station, MinStationLevel = level, Requirements = reqs.ToArray()
                }));
            }

            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("English", en);
            loc.AddTranslation("Russian", ru);
            LocFix.Add(en, ru);
            ItemManager.OnItemsRegistered += Diag;
            ItemManager.OnItemsRegistered += CaptureEssence;
        }

        private static void BuildMould(Dictionary<string, string> en, Dictionary<string, string> ru)
        {
            en["axeplatemold"] = "Axe plate mould"; ru["axeplatemold"] = "Форма обуха";
            en["axeplatemold_desc"] = "A mould for casting an axe plate out of Bloodgold. Found where other Nord moulds are found.";
            ru["axeplatemold_desc"] = "Форма для отливки обуха из кровавого золота. Встречается там же, где остальные нордские формы.";
            var go = PrefabManager.Instance.CreateClonedPrefab("AxePlateMold", "IronNails");
            var sh = go.GetComponent<ItemDrop>().m_itemData.m_shared;
            sh.m_name = "$axeplatemold"; sh.m_description = "$axeplatemold_desc";
            sh.m_icons = new[] { Icons.MouldIcon() };
            sh.m_maxStackSize = 5; sh.m_weight = 3f; sh.m_itemType = ItemDrop.ItemData.ItemType.Material;
            Material baseMat = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { if (baseMat == null) baseMat = r.sharedMaterial; r.enabled = false; }
            PlateMesh.AttachMouldModel(go, baseMat);
            ItemManager.Instance.AddItem(new CustomItem(go, false));
            if (CraftableMould.Value)
            {
                string st = "forge"; int lv = 3;
                Proto("AxeGold", ref st, ref lv);
                ItemManager.Instance.AddRecipe(new CustomRecipe(new RecipeConfig { Name = "Recipe_AxePlateMold", Item = "AxePlateMold", Amount = 1, CraftingStation = st, MinStationLevel = lv,
                    Requirements = new[] { new RequirementConfig("Iron", 10, 0, false), new RequirementConfig("Gold", 5, 0, false) } }));
            }
        }

        private static void CaptureEssence()
        {
            EssenceCost.Reqs.Clear();
            foreach (var d in PlateTable.All)
            {
                if (d.Parent == null || d.ExtraPrefab == null) continue;
                var r = FindRecipe("AxePlate_" + d.Id, ObjectDB.instance);
                if (r == null) continue;
                foreach (var q in r.m_resources)
                    if (q.m_resItem != null && q.m_resItem.name == d.ExtraPrefab) EssenceCost.Reqs.Add(q);
            }
            Log.LogInfo("essence requirements tracked: " + EssenceCost.Reqs.Count);
        }

        private static Recipe FindRecipe(string item, ObjectDB db = null)
        {
            if (item == null) return null;
            foreach (var r in (db ?? Db).m_recipes) if (r != null && r.m_item != null && r.m_item.name == item) return r;
            return null;
        }

        private static void Proto(string item, ref string station, ref int level)
        {
            var r = FindRecipe(item);
            if (r != null && r.m_craftingStation != null) { station = r.m_craftingStation.name; level = r.m_minStationLevel; }
            Log.LogInfo($"proto {item}: station={station} level={level}{(r == null ? " (recipe not found, fallback)" : "")}");
        }

        private static IEnumerable<string> MouldNames(string item)
        {
            var r = FindRecipe(item);
            var list = new List<string>();
            if (r != null)
                foreach (var q in r.m_resources)
                    if (q.m_resItem != null && q.m_resItem.name.StartsWith("Mold")) list.Add(q.m_resItem.name);
            if (list.Count == 0 && Item("MoldAxe") != null) list.Add("MoldAxe");
            return list;
        }

        private static void Diag()
        {
            ItemManager.OnItemsRegistered -= Diag;
            if (!Diagnostics.Value) return;
            try
            {
                foreach (var rec in ObjectDB.instance.m_recipes)
                {
                    string rn = rec != null && rec.m_item != null ? rec.m_item.name : "";
                    if (!(rn.Contains("BloodLightning") || rn.Contains("FrostFire") || rn == "AxeEarly" || rn == "AxeGold" || rn.StartsWith("Orb"))) continue;
                    string reqs = string.Join(", ", System.Linq.Enumerable.Select(rec.m_resources, r => (r.m_resItem ? r.m_resItem.name : "?") + "x" + r.m_amount + "(+" + r.m_amountPerLevel + ")"));
                    Log.LogInfo($"recipe {rec.name}: item={rn} station={(rec.m_craftingStation ? rec.m_craftingStation.name : "none")} lvl={rec.m_minStationLevel} reqs={reqs}");
                }
                foreach (var go in ObjectDB.instance.m_items)
                {
                    var d = go != null ? go.GetComponent<ItemDrop>() : null;
                    if (d != null && (go.name.Contains("BloodLightning") || go.name.Contains("FrostFire")) && go.name.Contains("Axe") && !go.name.Contains("Uncooked"))
                    {
                        var s2 = d.m_itemData.m_shared;
                        Log.LogInfo($"variant {go.name}: toolTier={s2.m_toolTier} lightning={s2.m_damages.m_lightning} fire={s2.m_damages.m_fire} frost={s2.m_damages.m_frost} slash={s2.m_damages.m_slash} chop={s2.m_damages.m_chop} equipSE={(s2.m_equipStatusEffect ? s2.m_equipStatusEffect.name : "none")} attackSE={(s2.m_attackStatusEffect ? s2.m_attackStatusEffect.name : "none")}");
                    }
                    if (d == null || !Plates.IsTargetPrefab(go.name, d.m_itemData.m_shared)) continue;
                    var s = d.m_itemData.m_shared;
                    Log.LogInfo($"target {go.name}: name={s.m_name} type={s.m_itemType} toolTier={s.m_toolTier} skill={s.m_skillType} slash={s.m_damages.m_slash} chop={s.m_damages.m_chop} blunt={s.m_damages.m_blunt} pierce={s.m_damages.m_pierce} pickaxe={s.m_damages.m_pickaxe} anim={s.m_animationState}");
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                        Log.LogInfo($"  target mesh {mf.name}: mesh={(mf.sharedMesh ? mf.sharedMesh.name : "null")} readable={(mf.sharedMesh ? mf.sharedMesh.isReadable : false)}");
                }
            }
            catch (Exception e) { Log.LogWarning("diag: " + e.Message); }
        }
    }
}
