using System.Collections.Generic;
using BepInEx.Configuration;
using Jotunn.Configs;
using UnityEngine;

namespace AxePlates
{
    // Data table of plate materials. Everything tunable lives in the BepInEx config (section "Plate.<Id>").
    public class PlateDef
    {
        public string Id;
        public string En, Ru;               // adjective used in names: "Bronze Axe Plate"
        public Color Tint;                  // plate colour (icon, fallback material)
        public string IngotToken;           // $item_xxx of the ingot
        public string IngotPrefabFallback;  // used when token lookup fails
        public string NailsVanilla;         // vanilla nails prefab, or null -> custom nails are made from the ingot
        public string ExtraToken;           // optional extra ingredient token (essence)
        public string ExtraPrefabFallback;
        public int ExtraCount;
        public bool TintMaterial;
        public Color Emit;                  // emission colour (a=0: none)
        public string Parent; public bool Quality => Parent != null;               // plate id used as ingredient (elemental chain)
        public string CastProto, TemperProto; // vanilla items whose recipes give station/level (Cast and quench)           // tint the ingot material (variants that share an ingot)
        // defaults
        public int Ingots, Nails, Station;
        public float Dmg, Lightning, Frost, Fire;
        public int Tool;
        // config entries
        public ConfigEntry<float> CDmg, CLightning, CFrost, CFire;
        public ConfigEntry<int> CTool, CStation, CIngots, CNails;
        // runtime
        public string IngotPrefab, ExtraPrefab;
        public Material Mat, Inst;
        public bool Available;

        public string KitPrefab => "AxePlate_" + Id;
        public string NailPrefab => NailsVanilla ?? ("AxePlateNail_" + (Parent ?? Id));
        public string KitToken => "$axeplate_" + Id.ToLowerInvariant();
        public string NailToken => "$axeplatenail_" + Id.ToLowerInvariant();

        public float DamagePct => CDmg.Value;
        public int ToolBonus => CTool.Value;
    }

    public static class PlateTable
    {
        public const string DataKey = "axeplates.material";
        public static readonly List<PlateDef> All = new List<PlateDef>
        {
            new PlateDef { Id="Bronze", En="Bronze", Ru="бронзовый", Tint=new Color(0.80f,0.50f,0.25f), IngotToken="$item_bronze", IngotPrefabFallback="Bronze", NailsVanilla="BronzeNails",
                Ingots=4, Nails=8, Station=2, Dmg=8, Tool=1 },
            new PlateDef { Id="Iron", En="Iron", Ru="железный", Tint=new Color(0.55f,0.55f,0.58f), IngotToken="$item_iron", IngotPrefabFallback="Iron", NailsVanilla="IronNails",
                Ingots=4, Nails=8, Station=3, Dmg=14, Tool=1 },
            new PlateDef { Id="Silver", En="Silver", Ru="серебряный", Tint=new Color(0.85f,0.87f,0.92f), IngotToken="$item_silver", IngotPrefabFallback="Silver",
                Ingots=4, Nails=8, Station=4, Dmg=20, Tool=2 },
            new PlateDef { Id="BlackMetal", En="Black Metal", Ru="из чёрного металла", Tint=new Color(0.22f,0.22f,0.27f), IngotToken="$item_blackmetal", IngotPrefabFallback="BlackMetal",
                Ingots=4, Nails=8, Station=4, Dmg=28, Tool=2 },
            new PlateDef { Id="Bloodgold", En="Bloodgold", Ru="из кровавого золота", Tint=new Color(0.90f,0.52f,0.20f), IngotToken="$item_gold", IngotPrefabFallback="Gold", TintMaterial=true, CastProto="AxeGold", TemperProto="AxeGold",
                Ingots=4, Nails=8, Station=5, Dmg=36, Tool=3 },
            new PlateDef { Id="Thunderblood", En="Thunderblood", Ru="громокровный", Tint=new Color(0.95f,0.25f,0.22f), Emit=new Color(0.35f,0.55f,1.0f,1f), Parent="Bloodgold", TemperProto="AxeGold_BloodLightning", IngotToken="$item_gold", IngotPrefabFallback="Gold",
                ExtraToken="$item_orbthunderblood", ExtraPrefabFallback="OrbThunderBlood", ExtraCount=1, TintMaterial=true,
                Ingots=6, Nails=0, Station=5, Dmg=44, Tool=3, Lightning=12 },
            new PlateDef { Id="Frostfire", En="Frostfire", Ru="огнеледяной", Tint=new Color(0.55f,0.85f,1.00f), Emit=new Color(1.0f,0.45f,0.12f,1f), Parent="Bloodgold", TemperProto="AxeGold_FrostFire", IngotToken="$item_gold", IngotPrefabFallback="Gold",
                ExtraToken="$item_orbfrostfire", ExtraPrefabFallback="OrbFrostFire", ExtraCount=1, TintMaterial=true,
                Ingots=6, Nails=0, Station=5, Dmg=44, Tool=3, Frost=6, Fire=6 },
        };
        public static readonly Dictionary<string, PlateDef> ById = new Dictionary<string, PlateDef>();

        public static void BindConfig(BepInEx.BaseUnityPlugin p)
        {
            var admin = new ConfigurationManagerAttributes { IsAdminOnly = true };
            foreach (var d in All)
            {
                ById[d.Id] = d;
                string s = "Plate." + d.Id;
                d.CDmg = p.Config.Bind(s, "DamagePercent", d.Dmg, new ConfigDescription("Axe damage bonus in % (slash/chop/blunt/pierce)", null, admin));
                d.CTool = p.Config.Bind(s, "ToolTierBonus", d.Tool, new ConfigDescription("Added to the tool tier of the hit (tree/ore hardness)", null, admin));
                d.CLightning = p.Config.Bind(s, "ExtraLightning", d.Lightning, new ConfigDescription("Flat extra lightning damage", null, admin));
                d.CFrost = p.Config.Bind(s, "ExtraFrost", d.Frost, new ConfigDescription("Flat extra frost damage", null, admin));
                d.CFire = p.Config.Bind(s, "ExtraFire", d.Fire, new ConfigDescription("Flat extra fire damage", null, admin));
                d.CStation = p.Config.Bind(s, "ForgeLevel", d.Station, "Minimum forge level for crafting (needs restart)");
                d.CIngots = p.Config.Bind(s, "IngotCount", d.Ingots, "Ingots per plate (needs restart)");
                d.CNails = p.Config.Bind(s, "NailCount", d.Nails, "Nails per plate (needs restart)");
            }
        }
    }
}
