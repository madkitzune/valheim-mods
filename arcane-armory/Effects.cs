using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace ArcaneArmory
{
    // Effects of the wave-1 weapons. The wielder's own client applies them (like vanilla hits), so they work for every player.
    internal static class Effects
    {
        public static bool Busy;
        private static int hitLogged;

        public static WeaponDef Of(ItemDrop.ItemData item)
        {
            string n = item?.m_dropPrefab != null ? item.m_dropPrefab.name : null;
            return n != null && Defs.ById.TryGetValue(n, out var d) ? d : null;
        }

        // ---- state ----
        private class Bleed { public int Stacks; public float Until; public float Next; public float Dps; }
        private class Slow { public float Until, Speed, Walk, Run; }
        private static readonly Dictionary<Character, float> marks = new Dictionary<Character, float>();
        private static readonly Dictionary<Character, float> armorBreak = new Dictionary<Character, float>();
        private static readonly Dictionary<Character, Bleed> bleeds = new Dictionary<Character, Bleed>();
        private static readonly Dictionary<Character, Slow> slows = new Dictionary<Character, Slow>();
        private static readonly Dictionary<Character, (int stacks, float until)> needle = new Dictionary<Character, (int, float)>();
        private static readonly Dictionary<string, int> hitCount = new Dictionary<string, int>();

        public static bool IsMarked(Character c) => c != null && marks.TryGetValue(c, out var t) && t > Time.time;
        public static IEnumerable<Character> MarkedTargets() => marks.Where(kv => kv.Key != null && kv.Value > Time.time && !kv.Key.IsDead()).Select(kv => kv.Key);

        private static void AddBleed(Character c, int add, int max, float dps)
        {
            if (!bleeds.TryGetValue(c, out var b)) { b = new Bleed(); bleeds[c] = b; }
            b.Stacks = Mathf.Min(max, b.Stacks + add);
            b.Until = Time.time + 6f;
            b.Dps = dps;
            if (b.Next < Time.time) b.Next = Time.time + 1f;
        }

        private static void ApplySlow(Character c, float seconds, float factor)
        {
            if (c == null || c.IsPlayer() || c.IsBoss()) return;
            if (!slows.TryGetValue(c, out var s))
            {
                s = new Slow { Speed = c.m_speed, Walk = c.m_walkSpeed, Run = c.m_runSpeed };
                slows[c] = s;
                c.m_speed *= factor; c.m_walkSpeed *= factor; c.m_runSpeed *= factor;
            }
            s.Until = Time.time + seconds;
        }

        private static void Strike(Character victim, Player lp, float damage, HitData.DamageTypes template, Vector3 dir)
        {
            var h = new HitData { m_point = victim.GetCenterPoint(), m_dir = dir, m_hitType = HitData.HitType.PlayerHit, m_skill = Skills.SkillType.Swords };
            h.m_damage = template;
            h.m_damage.Modify(damage);
            h.SetAttacker(lp);
            victim.Damage(h);
        }

        // ---- damage-side patches ----
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        internal static class DamagePatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (Busy || hit == null) return;
                var lp = Player.m_localPlayer;
                if (lp == null || hit.GetAttacker() != lp) return;
                var def = Of(lp.GetCurrentWeapon());
                if (def == null) return;
                float f = 1f;
                if ((def.Effect == "mark" || def.Effect == "armorbreak") && false) { }
                if (armorBreak.TryGetValue(__instance, out var ab) && ab > Time.time) f *= 1.15f;
                if (def.Effect == "stealth" && lp.IsCrouching()) f *= 1.5f;
                if (def.Effect == "stack" && needle.TryGetValue(__instance, out var n) && n.until > Time.time) f *= 1f + 0.03f * n.stacks;
                if (f != 1f) hit.m_damage.Modify(f);
            }

            private static void Postfix(Character __instance, HitData hit)
            {
                if (Busy || hit == null || __instance == null) return;
                var lp = Player.m_localPlayer;
                if (lp == null || hit.GetAttacker() != lp || __instance == lp || hit.m_hitType != HitData.HitType.PlayerHit && hit.m_hitType != HitData.HitType.Undefined) return;
                var weapon = lp.GetCurrentWeapon();
                var def = Of(weapon);
                if (def == null) return;
                if (hitLogged < 150)
                {
                    hitLogged++;
                    var dm = hit.m_damage;
                    Plugin.Log.LogInfo($"[hit] {def.Id} -> {__instance.name} total={dm.GetTotalDamage():0.#} (blunt {dm.m_blunt:0.#} slash {dm.m_slash:0.#} pierce {dm.m_pierce:0.#} fire {dm.m_fire:0.#} frost {dm.m_frost:0.#} lightning {dm.m_lightning:0.#} poison {dm.m_poison:0.#} spirit {dm.m_spirit:0.#}) type={hit.m_hitType}");
                }
                try
                {
                    Vector3 dir = (__instance.transform.position - lp.transform.position).normalized;
                    switch (def.Effect)
                    {
                        case "mark": marks[__instance] = Time.time + 6f; break;
                        case "bleed": AddBleed(__instance, 1, 4, 3f); break;
                        case "armorbreak":
                            if (Count(def.Id) % 4 == 0) armorBreak[__instance] = Time.time + 5f;
                            break;
                        case "stagger3":
                            if (Count(def.Id) % 3 == 0) __instance.Stagger(dir);
                            break;
                        case "heal":
                            foreach (var p in Player.GetAllPlayers())
                                if (p != null && p != lp && Vector3.Distance(p.transform.position, lp.transform.position) < 6f) p.Heal(3f, true);
                            lp.Heal(1f, false);
                            break;
                        case "slow": ApplySlow(__instance, def.Id == "AA_AtgeirSting" ? 2f : 3f, 0.5f); break;
                        case "backbleed":
                            if (hit.m_backstabBonus > 1f || (__instance.GetComponent<BaseAI>() is BaseAI ai && !ai.IsAlerted())) AddBleed(__instance, 3, 6, 4f);
                            else AddBleed(__instance, 1, 6, 2f);
                            break;
                        case "stack":
                            {
                                needle.TryGetValue(__instance, out var cur);
                                needle[__instance] = (cur.until > Time.time ? Mathf.Min(5, cur.stacks + 1) : 1, Time.time + 4f);
                                break;
                            }
                        case "arc":
                            {
                                int left = 2;
                                float total = hit.m_damage.GetTotalDamage();
                                Busy = true;
                                try
                                {
                                    foreach (var c in Character.GetAllCharacters().OrderBy(x => (x.transform.position - __instance.transform.position).sqrMagnitude))
                                    {
                                        if (left <= 0) break;
                                        if (c == null || c == __instance || c == lp || c.IsDead() || c.IsTamed() || !BaseAI.IsEnemy(lp, c)) continue;
                                        if ((c.transform.position - __instance.transform.position).magnitude > 3.5f) continue;
                                        Strike(c, lp, 0.5f, hit.m_damage, dir);
                                        left--;
                                    }
                                }
                                finally { Busy = false; }
                                break;
                            }
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning("effect " + def.Effect + ": " + e.Message); }
            }

            private static int Count(string id)
            {
                hitCount.TryGetValue(id, out int c);
                hitCount[id] = ++c;
                return c;
            }
        }

        // ---- blocks ----
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> leftRef = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");

        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        internal static class BlockPatch
        {
            private static void Postfix(Humanoid __instance, Character attacker, bool __result)
            {
                if (!__result || __instance != Player.m_localPlayer) return;
                var def = Of(leftRef(__instance));
                if (def == null) return;
                if (def.Effect == "blockstamina") __instance.AddStamina(8f);
                else if (def.Effect == "blockstagger" && attacker != null && UnityEngine.Random.value < 0.3f)
                    attacker.Stagger((attacker.transform.position - __instance.transform.position).normalized);
            }
        }

        // ---- homing for marked targets / wisps ----
        private static readonly AccessTools.FieldRef<Projectile, Vector3> velRef = AccessTools.FieldRefAccess<Projectile, Vector3>("m_vel");
        private static readonly AccessTools.FieldRef<Projectile, Character> ownerRef = AccessTools.FieldRefAccess<Projectile, Character>("m_owner");
        private static readonly AccessTools.FieldRef<Projectile, ItemDrop.ItemData> weaponRef = AccessTools.FieldRefAccess<Projectile, ItemDrop.ItemData>("m_weapon");

        [HarmonyPatch(typeof(Projectile), "FixedUpdate")]
        internal static class HomingPatch
        {
            private static void Postfix(Projectile __instance)
            {
                try
                {
                    if (ownerRef(__instance) != Player.m_localPlayer) return;
                    var def = Of(weaponRef(__instance));
                    if (def == null || (def.Effect != "homing" && def.Effect != "mark")) return;
                    Vector3 vel = velRef(__instance);
                    if (vel.sqrMagnitude < 1f) return;
                    Character best = null; float bd = 1e9f;
                    IEnumerable<Character> pool = def.Effect == "mark" ? MarkedTargets() : Character.GetAllCharacters();
                    foreach (var c in pool)
                    {
                        if (c == null || c.IsDead() || c.IsTamed() || c == Player.m_localPlayer || !BaseAI.IsEnemy(Player.m_localPlayer, c)) continue;
                        Vector3 to = c.GetCenterPoint() - __instance.transform.position;
                        float dist = to.magnitude;
                        if (dist > 25f || Vector3.Angle(vel, to) > 70f) continue;
                        if (dist < bd) { bd = dist; best = c; }
                    }
                    if (best == null) return;
                    Vector3 dir = (best.GetCenterPoint() - __instance.transform.position).normalized;
                    float turn = (def.Turn > 0f ? def.Turn : (def.Effect == "homing" ? 160f : 90f)) * Mathf.Deg2Rad * Time.fixedDeltaTime;
                    velRef(__instance) = Vector3.RotateTowards(vel, dir * vel.magnitude, turn, 0f);
                }
                catch { }
            }
        }

        // ---- ticking: bleed, slow cleanup, mark cleanup ----
        internal class Ticker : MonoBehaviour
        {
            private float next;

            private void Update()
            {
                if (Time.time < next) return;
                next = Time.time + 0.25f;
                var lp = Player.m_localPlayer;
                foreach (var c in bleeds.Keys.ToList())
                {
                    var b = bleeds[c];
                    if (c == null || c.IsDead() || b.Until < Time.time) { bleeds.Remove(c); continue; }
                    if (lp != null && Time.time >= b.Next)
                    {
                        b.Next = Time.time + 1f;
                        var h = new HitData { m_point = c.GetCenterPoint(), m_dir = Vector3.up, m_hitType = HitData.HitType.PlayerHit, m_skill = Skills.SkillType.Knives };
                        h.m_damage.m_slash = b.Stacks * b.Dps;
                        h.SetAttacker(lp);
                        Busy = true;
                        try { c.Damage(h); } finally { Busy = false; }
                    }
                }
                foreach (var c in slows.Keys.ToList())
                {
                    var s = slows[c];
                    if (c == null) { slows.Remove(c); continue; }
                    if (s.Until < Time.time || c.IsDead()) { c.m_speed = s.Speed; c.m_walkSpeed = s.Walk; c.m_runSpeed = s.Run; slows.Remove(c); }
                }
                foreach (var c in marks.Keys.ToList()) if (c == null || marks[c] < Time.time) marks.Remove(c);
                foreach (var c in armorBreak.Keys.ToList()) if (c == null || armorBreak[c] < Time.time) armorBreak.Remove(c);
                foreach (var c in needle.Keys.ToList()) if (c == null || needle[c].until < Time.time) needle.Remove(c);
            }
        }
    }
}
