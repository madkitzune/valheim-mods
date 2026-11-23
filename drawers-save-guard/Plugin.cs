using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace DrawersSaveGuard
{
    // Makes Path of Valheiman's "Stack nearby" (key L) work with Item Drawers (mkz).
    //  1. A drawer is a Container subclass that registers only its own RPCs, so the vanilla
    //     Container.StackAll() (RPC_RequestStack) fails with "Failed to find rpc method".
    //     StackAll on a drawer is redirected to a local deposit into the drawer.
    //  2. PoV calls the vanilla Container.Save by reflection afterwards; for a drawer this would
    //     overwrite its own data format, so it is redirected to the drawer's own Save.
    [BepInPlugin("local.drawers.savaguard", "Drawers Save Guard", "1.1.0")]
    [BepInDependency("mkz.itemdrawers", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new Harmony("local.drawers.savaguard").PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Drawer guard active (save redirect + stack-nearby deposit)");
        }

        internal static bool IsDrawer(Container c)
        {
            return c != null && c.GetType() != typeof(Container) && c.GetType().Name == "DrawerContainer";
        }
    }

    [HarmonyPatch(typeof(Container), "Save")]
    internal static class SaveGuard
    {
        private static MethodInfo own;
        private static Type ownType;

        private static bool Prefix(Container __instance)
        {
            if (!Plugin.IsDrawer(__instance)) return true;
            Type t = __instance.GetType();
            if (ownType != t)
            {
                ownType = t;
                own = t.GetMethod("Save", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
            }
            if (own == null) return true;
            try { own.Invoke(__instance, null); }
            catch (Exception e) { Plugin.Log.LogWarning("DrawerContainer.Save failed: " + (e.InnerException?.Message ?? e.Message)); }
            return false;
        }
    }

    [HarmonyPatch(typeof(Container), "StackAll")]
    internal static class StackAllGuard
    {
        private static FieldInfo itemField;
        private static MethodInfo addItem;
        private static MethodInfo movable;
        private static bool movableTried;

        private static readonly FieldInfo nviewField = AccessTools.Field(typeof(Container), "m_nview");
        private static ZNetView NView(Container c) { return nviewField?.GetValue(c) as ZNetView; }

        private static bool Bookkeeping(string k)
        {
            return k == "pt.born" || k.StartsWith("pt.work.", StringComparison.Ordinal) || k.StartsWith("pt.quest.", StringComparison.Ordinal);
        }

        private static List<ItemDrop.ItemData> Candidates(Player p)
        {
            if (!movableTried)
            {
                movableTried = true;
                Type qs = AccessTools.TypeByName("PassiveTree.QuickStack");
                movable = qs == null ? null : AccessTools.Method(qs, "Movable", new[] { typeof(Player) });
            }
            if (movable != null)
            {
                try { return (List<ItemDrop.ItemData>)movable.Invoke(null, new object[] { p }); }
                catch (Exception e) { Plugin.Log.LogWarning("PoV Movable failed, using own filter: " + (e.InnerException?.Message ?? e.Message)); }
            }
            // fallback: backpack rows 1..3 only, nothing equipped
            return p.GetInventory().GetAllItems().Where(i => i != null && !i.m_equipped && i.m_gridPos.y >= 1 && i.m_gridPos.y <= 3).ToList();
        }

        private static bool Prefix(Container __instance)
        {
            if (!Plugin.IsDrawer(__instance)) return true;
            try { Deposit(__instance); }
            catch (Exception e) { Plugin.Log.LogWarning("Drawer stack failed: " + (e.InnerException?.Message ?? e.Message)); }
            return false;
        }

        private static void Deposit(Container drawer)
        {
            Player p = Player.m_localPlayer;
            if (p == null || NView(drawer) == null || !NView(drawer).IsValid()) return;
            Type t = drawer.GetType();
            if (itemField == null) itemField = t.GetField("_item", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (addItem == null) addItem = t.GetMethod("AddItem", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string), typeof(int) }, null);
            if (itemField == null || addItem == null) { Plugin.Log.LogWarning("DrawerContainer members not found"); return; }

            var held = itemField.GetValue(drawer) as ItemDrop.ItemData;
            string prefab = held?.m_dropPrefab?.name;
            if (string.IsNullOrEmpty(prefab)) return;

            var items = Candidates(p).Where(i => i != null && i.m_dropPrefab != null && i.m_dropPrefab.name == prefab
                && i.m_shared.m_maxStackSize > 1 && i.m_quality <= 1
                && (i.m_customData == null || i.m_customData.Keys.All(Bookkeeping))).ToList();
            int total = items.Sum(i => i.m_stack);
            if (total <= 0) return;

            if (!NView(drawer).IsOwner()) NView(drawer).ClaimOwnership();
            if (!NView(drawer).IsOwner()) { Plugin.Log.LogInfo("drawer " + prefab + ": ownership not obtained"); return; }

            int accepted = (int)addItem.Invoke(drawer, new object[] { prefab, total });
            int left = accepted;
            Inventory inv = p.GetInventory();
            foreach (var it in items)
            {
                if (left <= 0) break;
                int take = Math.Min(it.m_stack, left);
                inv.RemoveItem(it, take);
                left -= take;
            }
            Plugin.Log.LogInfo("[Drawers] " + prefab + ": " + accepted + " of " + total + " put into the drawer");
            if (accepted > 0)
                p.Message(MessageHud.MessageType.TopLeft, "+" + accepted + " -> " + (held.m_shared?.m_name ?? prefab));
        }
    }
}
