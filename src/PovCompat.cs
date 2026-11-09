using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Valheiman.Api;

namespace CarturUIHud
{
    /// <summary>
    /// Path of Valheiman (treextr.pathofvalheiman, API 1.6.0 in 5.1.2 - read from its DLL, decompile in
    /// Valheim/_ref/pov/src). Both mods grow the player inventory with hidden rows under the bag: PoV
    /// adds eight of its own, and our Player.SetInventorySize postfix put our height back, so PoV read
    /// our three slot rows as backpack and its own rows never existed, and both equipment sheets drew
    /// (pilot -Pov, 2026-10-25). PoV's own sheet is turned off here. Keeping its cells in our slots
    /// through its storage provider is planned in docs/pov-plan.md.
    ///
    /// Everything that names a PoV type sits in a NoInlining method that runs only with PoV loaded:
    /// the JIT resolves the types a method names when it compiles it. No class of ours may implement
    /// a PoV interface - HarmonyX's PatchAll reads every type and logs a TypeLoadException without PoV.
    /// </summary>
    internal static class PovCompat
    {
        internal const string Guid = "treextr.pathofvalheiman";
        internal static ManualLogSource Log;

        internal static readonly bool Present = Chainloader.PluginInfos.ContainsKey(Guid);

        /// <summary>PoV's cell ids kept in our hidden rows. Empty while PoV's equipment is turned off
        /// (Init); the full storage-provider route is in docs/pov-plan.md.</summary>
        internal static readonly string[] Cells = new string[0];

        /// <summary>Hidden rows the cells need, at the inventory's eight columns.</summary>
        internal static int Rows => (Cells.Length + 7) / 8;

        internal static void Init()
        {
            if (!Present)
                return;
            // Stopgap: PoV's own equipment system treats our three hidden slot rows as backpack and draws
            // a second equipment sheet beside ours. TurnOff is the same switch PoV uses for the
            // equipment mods it knows (Module.foreignMods), which does not list this one.
            TurnOff();
            // Its category crafting window (CraftQueue, CraftCategories) swaps our recipe list for its own,
            // adds a "< 1 >" block to the Craft button's panel and lifts the requirements box 18 units
            // (read from PoV 5.1.2), none of which sits on our board's art. PoV's own switch for other
            // crafting mods does the same; it is set for this session only and not written to its cfg.
            CraftOff();
            Log?.LogInfo("Path of Valheiman: its equipment system and crafting window are turned off, ours are used");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CraftOff()
        {
            foreach (var entry in new[] { PassiveTree.Plugin.CraftByCategory, PassiveTree.Plugin.CraftByCategoryWithOthers })
            {
                if (entry == null || !entry.Value)
                    continue;
                var file = entry.ConfigFile;
                bool save = file.SaveOnConfigSet;
                file.SaveOnConfigSet = false;
                try { entry.Value = false; }
                finally { file.SaveOnConfigSet = save; }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)] private static void TurnOff() => Equipment.TurnOff(Plugin.PluginGuid);

        internal static bool Accepts(string cell, ItemDrop.ItemData item) => Present && AcceptsIn(cell, item);

        /// <summary>The item is of the cell's kind, whether or not the cell is open right now.</summary>
        internal static bool Fits(string cell, ItemDrop.ItemData item) => Present && FitsIn(cell, item);

        internal static void Changed() { }

        [MethodImpl(MethodImplOptions.NoInlining)] private static bool AcceptsIn(string cell, ItemDrop.ItemData item) => Equipment.Accepts(cell, item) && Equipment.IsOpen(Player.m_localPlayer, cell);
        [MethodImpl(MethodImplOptions.NoInlining)] private static bool FitsIn(string cell, ItemDrop.ItemData item) => Equipment.Accepts(cell, item);
    }
}
