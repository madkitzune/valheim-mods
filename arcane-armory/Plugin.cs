using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace ArcaneArmory
{
    [BepInPlugin(Guid, "Arcane Armory", "0.7.1")]
    [BepInDependency(Main.ModGuid)]
    [BepInDependency("treextr.pathofvalheiman", BepInDependency.DependencyFlags.SoftDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "local.arcanearmory";
        internal static ManualLogSource Log;
        private static bool built;

        private void Awake()
        {
            Log = Logger;
            Defs.Init();
            var adm = new ConfigurationManagerAttributes { IsAdminOnly = true };
            var cBows = Config.Bind("Enable", "Bows", true, new BepInEx.Configuration.ConfigDescription("Bows (needs restart; keep the same on every player)", null, adm));
            var cStaves = Config.Bind("Enable", "MagicStaves", true, new BepInEx.Configuration.ConfigDescription("Magic staves (needs restart; keep the same on every player)", null, adm));
            var cMelee = Config.Bind("Enable", "MeleeWeapons", false, new BepInEx.Configuration.ConfigDescription("Axes, maces, swords, spears, knives, atgeirs (off by default)", null, adm));
            var cShields = Config.Bind("Enable", "Shields", false, new BepInEx.Configuration.ConfigDescription("Shields (off by default)", null, adm));
            foreach (var w in Defs.All)
                w.Enabled = w.Category == "Bows" ? cBows.Value : w.Category == "Staves" ? cStaves.Value : w.Category == "Shields" ? cShields.Value : cMelee.Value;
            Log.LogInfo("enabled weapons: " + string.Join(", ", Defs.All.Where(w => w.Enabled).Select(w => w.Id)));
            PrefabManager.OnVanillaPrefabsAvailable += Build;
            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);
            gameObject.AddComponent<Effects.Ticker>();
            Acquire.ApplySigrid(harmony);
            Log.LogInfo("Arcane Armory loaded");
        }

        // Vanilla items live in the menu ObjectDB when Jotunn fires OnVanillaPrefabsAvailable.
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
            var db = Db;
            if (db == null || db.m_items == null) return null;
            foreach (var go in db.m_items) if (go != null && go.name == name) return go;
            return null;
        }

        private static void Build()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Build;
            if (built) return;
            built = true;
            try { BuildAll(); } catch (Exception e) { Log.LogError("Build failed: " + e); }
        }

        private static void MakePlan(WeaponDef d, Dictionary<string, string> en, Dictionary<string, string> ru)
        {
            var plan = PrefabManager.Instance.CreateClonedPrefab(d.PlanId, "Mushroom");
            if (plan == null) { Log.LogWarning("[" + d.Id + "] cannot clone plan item"); return; }
            var sh = plan.GetComponent<ItemDrop>().m_itemData.m_shared;
            sh.m_name = d.PlanToken;
            sh.m_description = "$aa_plan_desc";
            sh.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
            sh.m_food = 0f; sh.m_foodStamina = 0f; sh.m_foodEitr = 0f; sh.m_foodBurnTime = 0f; sh.m_foodRegen = 0f;
            sh.m_consumeStatusEffect = null;
            sh.m_maxStackSize = 1; sh.m_weight = 0.1f; sh.m_value = 0;
            sh.m_icons = new[] { Look.PlanIcon(d.Tint) };
            foreach (var r in plan.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterials = r.sharedMaterials.Select(m => Look.Tinted(m, d.Tint, Color.clear)).ToArray();
            ItemManager.Instance.AddItem(new CustomItem(plan, false));
            en[d.PlanToken.TrimStart('$')] = "Plan: " + d.En; ru[d.PlanToken.TrimStart('$')] = "Чертёж: " + d.Ru;
            en["aa_plan_desc"] = "A drawing of a weapon. Use it to learn how to craft it."; ru["aa_plan_desc"] = "Чертёж оружия. Используйте, чтобы научиться его создавать.";
            en["aa_plan_learned"] = "Plan learned: {0}"; ru["aa_plan_learned"] = "Чертёж изучен: {0}";
            en["aa_plan_known"] = "You already know this plan"; ru["aa_plan_known"] = "Вы уже знаете этот чертёж";
        }

        private static void ScaleDamage(ref HitData.DamageTypes d, float m)
        {
            d.m_damage *= m; d.m_blunt *= m; d.m_slash *= m; d.m_pierce *= m; d.m_chop *= m; d.m_pickaxe *= m;
            d.m_fire *= m; d.m_frost *= m; d.m_lightning *= m; d.m_poison *= m; d.m_spirit *= m;
        }

        private static void BuildAll()
        {
            try
            {
                var fg = Item("Feathers");
                Mesh fm = null; Material fmat = null;
                if (fg != null)
                {
                    foreach (var mfx in fg.GetComponentsInChildren<MeshFilter>(true))
                        if (mfx.sharedMesh != null && mfx.GetComponent<Renderer>() != null) { fm = mfx.sharedMesh; fmat = mfx.GetComponent<Renderer>().sharedMaterial; break; }
                    if (fm == null)
                    {
                        var smr = fg.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        if (smr != null) { fm = smr.sharedMesh; fmat = smr.sharedMaterial; }
                    }
                }
                if (fm != null) { Visuals.FeatherMesh = fm; Visuals.FeatherMat = fmat; Log.LogInfo("feather asset: " + fm.name); }
                else Log.LogWarning("vanilla Feathers model not found (" + (fg == null ? "no prefab" : "prefab without mesh") + ")");
            }
            catch (Exception e) { Log.LogWarning("feather asset: " + e.Message); }
            var en = new Dictionary<string, string>();
            var ru = new Dictionary<string, string>();
            foreach (var d in Defs.All)
            {
                if (!d.Enabled) continue;
                var baseGo = Item(d.Base);
                if (baseGo == null) { Log.LogWarning($"[{d.Id}] base '{d.Base}' not found, skipped"); continue; }
                var reqs = new List<RequirementConfig>();
                bool okReq = true;
                foreach (var (item, amount) in d.Reqs)
                {
                    if (Item(item) == null) { Log.LogWarning($"[{d.Id}] ingredient '{item}' not found, skipped"); okReq = false; continue; }
                    reqs.Add(new RequirementConfig(item, amount, Math.Max(1, amount / 2), false));
                }
                if (!okReq && reqs.Count == 0) continue;

                var go = PrefabManager.Instance.CreateClonedPrefab(d.Id, d.Base);
                var sh = go.GetComponent<ItemDrop>().m_itemData.m_shared;
                sh.m_name = d.Token;
                sh.m_description = d.DescToken;
                sh.m_value = 0;
                ScaleDamage(ref sh.m_damages, d.DmgMul);
                ScaleDamage(ref sh.m_damagesPerLevel, d.DmgMul);
                if (d.FrostToSpirit)
                {
                    sh.m_damages.m_spirit += sh.m_damages.m_frost; sh.m_damages.m_frost = 0f;
                    sh.m_damagesPerLevel.m_spirit += sh.m_damagesPerLevel.m_frost; sh.m_damagesPerLevel.m_frost = 0f;
                }
                if (d.FlatFire > 0f)
                {
                    sh.m_damages = default; sh.m_damages.m_fire = d.FlatFire;
                    sh.m_damagesPerLevel = default; sh.m_damagesPerLevel.m_fire = 1f;
                }
                if (d.Eitr > 0f && sh.m_attack != null) sh.m_attack.m_attackEitr = d.Eitr;
                if (d.ProjScale > 0f && sh.m_attack != null && sh.m_attack.m_attackProjectile != null)
                {
                    string pn = sh.m_attack.m_attackProjectile.name;
                    var pc = PrefabManager.Instance.CreateClonedPrefab(d.Id + "_proj", pn);
                    if (pc != null)
                    {
                        var pr = pc.GetComponent<Projectile>();
                        if (pr != null)
                        {
                            pr.m_aoe = 0f; pr.m_spawnOnHit = null; pr.m_spawnOnHitChance = 0f; pr.m_spawnCount = 0;
                            pr.m_hitEffects = new EffectList();
                        }
                        pc.transform.localScale *= d.ProjScale;
                        foreach (var ps in pc.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            var main = ps.main; main.startSizeMultiplier *= d.ProjScale; main.startSpeedMultiplier = main.startSpeedMultiplier;
                        }
                        foreach (var l in pc.GetComponentsInChildren<Light>(true)) l.range *= d.ProjScale * 2f;
                        sh.m_attack.m_attackProjectile = pc;
                        PrefabManager.Instance.AddPrefab(pc);
                    }
                }
                if (sh.m_icons != null && sh.m_icons.Length > 0) sh.m_icons = new[] { Look.TintSprite(sh.m_icons[0], d.Tint) };
                Look.Apply(go, d);
                Visuals.Decorate(go, d);

                ItemManager.Instance.AddItem(new CustomItem(go, false));
                if (d.UseBase) reqs.Insert(0, new RequirementConfig(d.Base, 1, 0, false));
                if (d.Acquire != "sigrid")
                    ItemManager.Instance.AddRecipe(new CustomRecipe(new RecipeConfig
                    {
                        Name = "Recipe_" + d.Id, Item = d.Id, Amount = 1, CraftingStation = d.Station, MinStationLevel = d.Level, Requirements = reqs.ToArray()
                    }));
                if (d.Acquire == "plan") MakePlan(d, en, ru);
                en[d.Token.TrimStart('$')] = d.En; ru[d.Token.TrimStart('$')] = d.Ru;
                en[d.DescToken.TrimStart('$')] = d.DescEn; ru[d.DescToken.TrimStart('$')] = d.DescRu;
                Log.LogInfo($"[{d.Id}] registered (base {d.Base}, effect {d.Effect})");
            }
            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("English", en);
            loc.AddTranslation("Russian", ru);
            LocFix.Add(en, ru);
        }
    }

    // Visual: tint the base model, add a glow. (Procedural ornaments are added per weapon later.)
    internal static class Look
    {
        public static Material Tinted(Material m, Color c, Color emit)
        {
            if (m == null) return null;
            var n = new Material(m) { name = m.name + "_aa" };
            Color t = Color.Lerp(Color.white, c, 0.55f);
            t.a = 1f;
            if (n.HasProperty("_Color")) n.SetColor("_Color", t);
            if (n.HasProperty("_BaseColor")) n.SetColor("_BaseColor", t);
            if (emit.a > 0f)
            {
                n.EnableKeyword("_EMISSION");
                foreach (var pn in new[] { "_EmissionColor", "_EmissiveColor" }) if (n.HasProperty(pn)) n.SetColor(pn, emit * 0.6f);
            }
            return n;
        }

        public static void Apply(GameObject go, WeaponDef d)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                r.sharedMaterials = r.sharedMaterials.Select(m => Tinted(m, d.Tint, d.Emit)).ToArray();
            }
        }

        // Scroll with a coloured seal.
        public static Sprite PlanIcon(Color c)
        {
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Color o = new Color(0, 0, 0, 0);
                    if (x > 10 && x < 54 && y > 8 && y < 56)
                    {
                        bool edge = x < 13 || x > 50 || y < 11 || y > 53;
                        o = edge ? new Color(0.35f, 0.25f, 0.12f, 1f) : new Color(0.9f, 0.84f, 0.66f, 1f);
                        if (!edge && (y % 9 == 0) && x > 16 && x < 48) o = new Color(0.45f, 0.35f, 0.2f, 1f);
                    }
                    float r = (new Vector2(x, y) - new Vector2(42, 20)).magnitude;
                    if (r < 8f) { o = r < 6f ? c * 0.9f : new Color(0.2f, 0.1f, 0.05f, 1f); o.a = 1f; }
                    px[y * N + x] = o;
                }
            tex.SetPixels(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }

        public static Sprite TintSprite(Sprite src, Color c)
        {
            try
            {
                if (src == null || src.texture == null) return src;
                var tex = src.texture;
                Rect r = src.textureRect;
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var t2 = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
                t2.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var px = t2.GetPixels();
                for (int i = 0; i < px.Length; i++)
                {
                    float lum = px[i].r * 0.3f + px[i].g * 0.59f + px[i].b * 0.11f;
                    Color tc = c * Mathf.Clamp(lum * 1.5f + 0.1f, 0f, 1.3f);
                    px[i] = new Color(Mathf.Lerp(px[i].r, tc.r, 0.45f), Mathf.Lerp(px[i].g, tc.g, 0.45f), Mathf.Lerp(px[i].b, tc.b, 0.45f), px[i].a);
                }
                t2.SetPixels(px); t2.Apply();
                return Sprite.Create(t2, new Rect(0, 0, t2.width, t2.height), new Vector2(0.5f, 0.5f));
            }
            catch (Exception e) { Plugin.Log.LogWarning("icon tint: " + e.Message); return src; }
        }
    }
}
