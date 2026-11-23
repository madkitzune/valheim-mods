using System.Collections.Generic;
using HarmonyLib;

namespace AxePlates
{
    // Mods that rebuild the language table (Path of Valheiman) drop translations added through Jotunn. Own strings are re-added after every language setup.
    internal static class LocFix
    {
        public static readonly Dictionary<string, string> En = new Dictionary<string, string>();
        public static readonly Dictionary<string, string> Ru = new Dictionary<string, string>();

        public static void Add(Dictionary<string, string> en, Dictionary<string, string> ru)
        {
            foreach (var kv in en) En[kv.Key] = kv.Value;
            foreach (var kv in ru) Ru[kv.Key] = kv.Value;
        }

        public static void Apply(Localization loc, string language)
        {
            if (loc == null) return;
            var t = Traverse.Create(loc);
            foreach (var kv in En) t.Method("AddWord", new[] { typeof(string), typeof(string) }, new object[] { kv.Key, kv.Value }).GetValue();
            if (language == "Russian")
                foreach (var kv in Ru) t.Method("AddWord", new[] { typeof(string), typeof(string) }, new object[] { kv.Key, kv.Value }).GetValue();
        }

        [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
        private static class Setup
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Localization __instance, string language) => Apply(__instance, language);
        }

        [HarmonyPatch(typeof(Player), "OnSpawned")]
        private static class Spawned
        {
            private static void Postfix()
            {
                var l = Localization.instance;
                if (l != null) Apply(l, l.GetSelectedLanguage());
            }
        }
    }
}
