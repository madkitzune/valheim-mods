using System.Collections.Generic;
using UnityEngine;

namespace ArcaneArmory
{
    public class WeaponDef
    {
        public string Id, En, Ru, DescEn, DescRu, Base, Effect;
        public float DmgMul = 1f;
        public Color Tint = Color.white;
        public Color Emit = Color.clear;
        public string Station = "forge";
        public int Level = 1;
        public (string item, int amount)[] Reqs;
        public bool FrostToSpirit;
        public float FlatFire;       // >0: damage becomes exactly this much fire
        public float ProjScale;      // >0: projectile clone scaled, no explosion
        public float Eitr;           // >0: override eitr cost
        public float Turn;
        public string Look = "";
        public bool Enabled = true;
        public string Category => Base.StartsWith("Bow") ? "Bows" : Base.StartsWith("Staff") ? "Staves" : Base.StartsWith("Shield") ? "Shields" : "Melee";
        public string Acquire = "craft";   // craft | plan | sigrid
        public bool UseBase;                // the vanilla base item is an ingredient
        public string[] Mobs = new string[0];
        public string[] Chests = new string[0];
        public float Chance = 0.06f;
        public string PlanToken => "$aa_plan_" + Id.ToLowerInvariant();
        public string PlanId => "AA_Plan_" + Id.Substring(3);           // homing turn rate deg/s (0 = default)
        public int Wave = 1;

        public string Token => "$aa_" + Id.ToLowerInvariant();
        public string DescToken => Token + "_d";
    }

    public static class Defs
    {
        private static WeaponDef D(string id, string en, string ru, string descEn, string descRu, string @base, string effect, float mul,
            Color tint, string station, int level, params (string, int)[] reqs)
            => new WeaponDef { Id = id, En = en, Ru = ru, DescEn = descEn, DescRu = descRu, Base = @base, Effect = effect, DmgMul = mul, Tint = tint, Station = station, Level = level, Reqs = reqs };

        public static readonly List<WeaponDef> All = new List<WeaponDef>
        {
            // ---- Wave 1: Black Forest ----
            D("AA_BowFeather", "Feather Bow", "Лук «Перо»", "Fine bow with white fletching. A hit marks the target for 6s; arrows then steer towards marked targets.", "Изящный лук с белым оперением. Попадание метит цель на 6 с, стрелы летят к помеченным целям.",
                "BowFineWood", "mark", 0.95f, new Color(0.95f, 0.95f, 1f), "piece_workbench", 2, ("FineWood", 10), ("RoundLog", 6), ("Feathers", 6), ("Resin", 4)),
            D("AA_BowJay", "Jay Bow", "Лук «Сойка»", "Light bow with a quiet string. Shots from stealth deal +50% damage.", "Лёгкий лук с тихой тетивой. Выстрел из скрытности наносит +50% урона.",
                "BowFineWood", "stealth", 0.85f, new Color(0.45f, 0.65f, 1f), "piece_workbench", 2, ("FineWood", 10), ("Feathers", 8), ("LeatherScraps", 4), ("GreydwarfEye", 4)),
            D("AA_BucklerWatch", "Watchman's Buckler", "Дозорный баклер", "A successful block has a 30% chance to stagger the attacker with a flash of light.", "Удачный блок с шансом 30% пошатывает атакующего вспышкой света.",
                "ShieldBronzeBuckler", "blockstagger", 1.0f, new Color(1f, 0.85f, 0.4f), "forge", 1, ("Bronze", 6), ("Wood", 4), ("Resin", 4), ("GreydwarfEye", 4)),
            D("AA_ShieldRootweave", "Rootweave Shield", "Щит «Корнеплёт»", "Every block returns some stamina as the roots tighten.", "Каждый блок возвращает часть выносливости: корни сжимаются.",
                "ShieldWood", "blockstamina", 1.0f, new Color(0.45f, 0.7f, 0.35f), "piece_workbench", 2, ("Wood", 10), ("RoundLog", 4), ("Resin", 8), ("BoneFragments", 6)),
            D("AA_StaffWisp", "Wisp Staff", "Посох «Огонёк»", "Casts a wandering flame that steers towards enemies and sets them ablaze.", "Выпускает блуждающий огонь, который догоняет врага и поджигает его.",
                "StaffFireball", "homing", 0.38f, new Color(1f, 0.7f, 0.25f), "piece_workbench", 2, ("FineWood", 8), ("GreydwarfEye", 8), ("Resin", 10), ("Coal", 6)),
            D("AA_StaffGlow", "Glowfly Staff", "Посох «Светляк»", "Launches a stream of cold glowing shards that seek out enemies.", "Выпускает поток холодных светящихся осколков, которые ищут врагов.",
                "StaffIceShards", "homing", 0.37f, new Color(0.7f, 1f, 0.8f), "piece_workbench", 2, ("FineWood", 8), ("GreydwarfEye", 8), ("Resin", 8), ("Flint", 6)),
            new WeaponDef { Id = "AA_StaffMote", En = "Mote Staff", Ru = "Посох «Мерцание»", DescEn = "Fires a tiny ember that gently steers towards enemies. About 11 fire damage, no explosion.", DescRu = "Выпускает крошечный уголёк, слабо наводящийся на врага. Около 11 огненного урона, без взрыва.",
                Base = "StaffFireball", Effect = "homing", DmgMul = 1f, Tint = new Color(1f, 0.5f, 0.2f), Station = "piece_workbench", Level = 2,
                Reqs = new (string, int)[] { ("FineWood", 6), ("GreydwarfEye", 4), ("Resin", 6), ("Coal", 4) },
                FlatFire = 11f, ProjScale = 0.35f, Eitr = 6f, Turn = 55f },
            D("AA_AxeWoodsman", "Watchman's Hatchet", "Топор дровосека-дозорного", "Every hit adds a bleed stack (up to 4).", "Каждый удар добавляет стек кровотечения (до 4).",
                "AxeBronze", "bleed", 1.0f, new Color(0.85f, 0.3f, 0.25f), "forge", 1, ("Bronze", 8), ("Wood", 4), ("LeatherScraps", 2), ("Resin", 4)),
            D("AA_AxeCleaver", "Cleaver", "Колун", "Every 4th hit splits the target's defence: +15% damage taken for 5s.", "Каждый 4-й удар раскалывает защиту: цель получает на 15% больше урона 5 с.",
                "AxeBronze", "armorbreak", 1.1f, new Color(0.6f, 0.6f, 0.65f), "forge", 2, ("Bronze", 10), ("Wood", 6), ("LeatherScraps", 3), ("BoneFragments", 5)),
            D("AA_MaceWatch", "Watchman's Mace", "Булава дозорного", "Every 3rd hit staggers the target with a flash.", "Каждый 3-й удар пошатывает цель вспышкой.",
                "MaceBronze", "stagger3", 1.0f, new Color(1f, 0.85f, 0.4f), "forge", 1, ("Bronze", 8), ("Wood", 4), ("LeatherScraps", 3), ("Flint", 4)),
            D("AA_SwordDawn", "Dawnblade", "Меч «Рассвет»", "Each hit lightly heals allies nearby.", "Каждый удар слегка лечит союзников рядом.",
                "SwordBronze", "heal", 1.0f, new Color(1f, 0.9f, 0.55f), "forge", 1, ("Bronze", 8), ("Wood", 3), ("LeatherScraps", 2), ("Resin", 4)),
            D("AA_SwordSickle", "Sickle Blade", "Меч «Серп»", "A wide arc: hits up to two extra enemies near the target for 50%.", "Широкая дуга: задевает ещё до двух врагов рядом с целью на 50%.",
                "SwordBronze", "arc", 0.95f, new Color(0.75f, 0.8f, 0.85f), "forge", 2, ("Bronze", 10), ("Wood", 3), ("LeatherScraps", 3), ("Flint", 4)),
            D("AA_SpearBeacon", "Beacon Spear", "Копьё «Вешка»", "Marks the target for 6s so allies and arrows find it.", "Метит цель на 6 с: стрелы летят к помеченным.",
                "SpearBronze", "mark", 0.95f, new Color(1f, 0.55f, 0.25f), "forge", 1, ("Bronze", 6), ("Wood", 5), ("DeerHide", 2), ("Resin", 4)),
            D("AA_SpearVine", "Vine Spear", "Копьё «Лоза»", "Hits slow the target for 3s as vines grip it.", "Удар замедляет цель на 3 с: лоза цепляется за неё.",
                "SpearBronze", "slow", 0.9f, new Color(0.4f, 0.75f, 0.35f), "forge", 1, ("Bronze", 6), ("Wood", 6), ("DeerHide", 2), ("GreydwarfEye", 4)),
            D("AA_KnifeClaw", "Claw", "Нож «Коготь»", "A backstab or an unaware target suffers heavy bleeding.", "Удар в спину или по ничего не подозревающей цели вызывает сильное кровотечение.",
                "KnifeCopper", "backbleed", 1.0f, new Color(0.8f, 0.35f, 0.3f), "forge", 1, ("Copper", 8), ("Wood", 2), ("LeatherScraps", 2), ("Flint", 4)),
            D("AA_KnifeNeedle", "Needle", "Нож «Игла»", "Each hit on the same target adds +3% damage (up to 5 stacks, 4s).", "Каждый удар по той же цели даёт +3% урона (до 5 стеков, 4 с).",
                "KnifeCopper", "stack", 0.9f, new Color(0.7f, 0.85f, 1f), "forge", 1, ("Copper", 6), ("Tin", 2), ("LeatherScraps", 2), ("Resin", 3)),
            D("AA_AtgeirSting", "Sting", "Алебарда «Жало»", "A sweep slows every enemy it hits for 2s.", "Размах замедляет всех задетых на 2 с.",
                "AtgeirBronze", "slow", 0.95f, new Color(0.45f, 0.7f, 1f), "forge", 2, ("Bronze", 10), ("Wood", 6), ("DeerHide", 3), ("GreydwarfEye", 4)),
        };

        public static readonly Dictionary<string, WeaponDef> ById = new Dictionary<string, WeaponDef>();

        public static void Init()
        {
            foreach (var d in All) ById[d.Id] = d;
            // Glowfly: frost damage of the base staff becomes spirit
            // Glowfly keeps frost damage (the spirit conversion did no damage in the first test)
            void A(string id, string acquire, bool useBase, string[] mobs, string[] chests, float chance = 0.06f)
            {
                var w = ById[id]; w.Acquire = acquire; w.UseBase = useBase; w.Mobs = mobs; w.Chests = chests; w.Chance = chance;
            }
            void LK(string id, string look) { ById[id].Look = look; }
            LK("AA_BowFeather", "feathers2:F4F4FF:7");
            LK("AA_BowJay", "feathers2:4F8CFF:6");
            LK("AA_BucklerWatch", "runes:FFD866");
            LK("AA_ShieldRootweave", "runes:7CCB5A");
            LK("AA_StaffWisp", "gem:FF9A30:head|ring:FFB347:2");
            LK("AA_StaffGlow", "gem:A6FFD0:head|ring:7FE8C0:1|motes:A6FFD0:3");
            LK("AA_StaffMote", "gem:FF7A2F:small|ring:FF9A50:1");
            LK("AA_AxeWoodsman", "runeblade:E02A22");
            LK("AA_AxeCleaver", "bladeclone:9AA0AA");
            LK("AA_MaceWatch", "");
            LK("AA_SwordDawn", "ring:FFE58A:2|inlay:FFFFFF");
            LK("AA_SwordSickle", "stretch:1.25|guard:C5CCD6");
            LK("AA_SpearBeacon", "banner:E03A2A");
            LK("AA_SpearVine", "vine:5FCB45");
            LK("AA_KnifeClaw", "claws:F2F2F2|ring:F2F2F2:1");
            LK("AA_KnifeNeedle", "flamberg:E8EEF8");
            LK("AA_AtgeirSting", "curveblade:E8EEF8");
            var none = new string[0];
            var bfChests = new[] { "TreasureChest_blackforest", "TreasureChest_forestcrypt", "TreasureChest_trollcave" };
            A("AA_BowFeather", "sigrid", false, none, none);
            A("AA_KnifeClaw", "sigrid", false, none, none);
            A("AA_BowJay", "plan", false, new[] { "Greydwarf" }, none, 0.05f);
            A("AA_StaffWisp", "plan", false, none, bfChests, 0.35f);
            A("AA_StaffGlow", "plan", false, new[] { "Greydwarf_Shaman" }, none, 0.25f);
            A("AA_AxeCleaver", "plan", true, none, bfChests, 0.3f);
            A("AA_SwordSickle", "plan", true, none, bfChests, 0.3f);
            A("AA_SpearVine", "plan", true, new[] { "Greydwarf_Elite" }, none, 0.2f);
            A("AA_KnifeNeedle", "plan", true, new[] { "Skeleton" }, none, 0.08f);
            A("AA_AtgeirSting", "plan", true, none, bfChests, 0.3f);
            A("AA_AxeWoodsman", "craft", true, none, none);
            A("AA_MaceWatch", "craft", true, none, none);
            A("AA_SwordDawn", "craft", true, none, none);
            A("AA_SpearBeacon", "craft", true, none, none);
            A("AA_BucklerWatch", "craft", true, none, none);
            A("AA_ShieldRootweave", "craft", true, none, none);
            A("AA_StaffMote", "craft", false, none, none);
        }
    }
}
