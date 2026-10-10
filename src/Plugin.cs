using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CarturUIHud
{
    // Part 1 of Cartur's UI: the HUD. Health, stamina, eitr and adrenaline bars, three food
    // boxes and the guardian power box, re-skinned onto the objects vanilla Hud already builds.
    // See HudSkin for why almost none of this needs code.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    // Both replace the same bars, loading screen and panels; running either beside this is a
    // fight over the same objects every frame, so BepInEx refuses to load us next to them.
    [BepInIncompatibility("randyknapp.mods.auga")]
    [BepInIncompatibility("ZenDragon.AugaLite")]
    // These two are now part of this mod rather than dependencies of it. Both grow the player's
    // inventory into the same hidden rows and patch the same equip path, so side by side they
    // would each move the other's items every frame. Remove them from the profile; a character
    // that has been through them keeps every worn item exactly where it was, because the slot
    // grid layout here is the one Equipment and Quick Slots used.
    [BepInIncompatibility("randyknapp.mods.equipmentandquickslots")]
    [BepInIncompatibility("vapok.mods.shieldmebruh")]
    // Soft, so it changes nothing when Better Archery is absent - it is here purely for load
    // order. QuiverCompat sets a field inside that mod, so that mod has to exist first.
    [BepInDependency("ishid4.mods.betterarchery", BepInDependency.DependencyFlags.SoftDependency)]
    // Also soft, also only load order. Cartur's Waste Management builds its trash can and sort
    // button on its own InventoryGui.Show postfix, and InventoryScreen.Column re-seats those two
    // into the side column - so it has to be patched first. See the [HarmonyAfter] on Column.
    [BepInDependency("com.jekkle.valheim.carturwastemanagement", BepInDependency.DependencyFlags.SoftDependency)]
    // Soft, and only for load order again: EnchantingScreen looks its window type up by name, so
    // EpicLoot has to have been loaded before this runs or the lookup finds nothing.
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    // Soft, load order only: HDLoader reads HD Valheim Textures' TextureReplacer, which that mod
    // builds in its own Awake.
    [BepInDependency(HDLoader.HdGuid, BepInDependency.DependencyFlags.SoftDependency)]
    // Soft, load order only: JotunnWindows looks Jotunn's ModCompatibility up by name.
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.SoftDependency)]
    // Soft, load order only: QssCompat patches Quick Stack Store's slot checks and trash can by name.
    [BepInDependency(QssCompat.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.jekkle.valheim.carturuihud";
        public const string PluginName = "Carturs UI - HUD";
        public const string PluginVersion = "1.1.3";

        private void Awake()
        {
            try
            {
                // First, so the HD texture load starts while the logo screens are still up. It
                // catches its own failures and leaves HD Valheim Textures to load as before.
                HDLoader.Log = Logger;
                HDLoader.Init(this, new Harmony(PluginGuid));

                HudSkin.Log = Logger;
                HudLayout.Log = Logger;
                Skin.Log = Logger;
                InventoryScreen.Log = Logger;
                Hotbar.Log = Logger;
                HotbarRow.Log = Logger;
                CraftingBoard.Log = Logger;
                MenuBoards.Log = Logger;
                LoadingArt.Log = Logger;
                SleepVideo.Log = Logger;
                Inlays.Log = Logger;
                AutoBoard.Log = Logger;
                ChestBoard.Log = Logger;
                CookTimer.Log = Logger;
                Tooltip.Log = Logger;
                StyleTab.Log = Logger;
                EnchantingScreen.Log = Logger;
                JotunnWindows.Log = Logger;
#if DIAGNOSTICS
                Dump.Log = Logger;
                IconRender.Log = Logger;
#endif
                LoadingScreen.Log = Logger;
                MapBorder.Log = Logger;
                ShieldSlot.Log = Logger;
                SlotPatches.Log = Logger;
                EquipmentPanel.Log = Logger;
                CharacterPreview.Log = Logger;
                QuickSlots.Log = Logger;
                AutoEquip.Log = Logger;
                QuiverCompat.Log = Logger;
                QssCompat.Log = Logger;

                // One experiment, one switch, nothing destroyed: the generated wood panel is a
                // separate PNG beside the built one, and turning this off goes straight back.
                AssetLoader.WoodPanel = Config.Bind(
                    "Art", "woodPanel", true,
                    "Use the generated wood panel (panel_wood.png) for every menu panel. "
                    + "Off returns to the panel built from the bar and diamond art.").Value;

                // The panels are dim because vanilla's woodpanel Images carry the `litpanel`
                // material - Custom/LitGui at _Brightness 0.37 - and swapping a sprite does not
                // change that. On keeps it, off drops the material so our art draws at its real
                // value. Default off, because the dimness is the complaint.
                Skin.DarkMode = Config.Bind(
                    "Art", "darkMode", false,
                    "Keep vanilla's dimmed panel material (Custom/LitGui at 0.37 brightness). Off draws the panels at full brightness.").Value;

                if (!AssetLoader.LoadTextures(Logger))
                {
                    Logger.LogWarning("assets missing from " + AssetLoader.AssetsDir + " - HUD left vanilla");
                    return;
                }

                // The food diamonds' second layout, and the way back off it. Read once, when the
                // diamonds are built at Hud.Awake, so a change takes effect on the next launch.
                HudSkin.SlotDetail = Config.Bind(
                    "Layout", "quickSlotDetail", true,
                    "On the food diamonds: the Eat/countdown text sits on the bottom bevel beside "
                    + "where the key sits on the top one. Off puts the countdown back above the "
                    + "bottom corner.").Value;

                ConfigEntry<bool> editMode = Config.Bind(
                    "Layout", "editMode", false,
                    "Turn on to arrange the HUD: drag a piece to move it, wheel to size it, "
                    + "shift+wheel for a bar's length, ctrl+wheel and ctrl+drag for its frame. "
                    + "Turn off when done - the layout is saved either way.");
                HudLayout.Init(Config, editMode);
                TextScale.Init(Config);

                // The loading screen pictures in assets/loading.
                LoadingArt.Init(Config);

                // A video on the sleep screen, from assets/sleep.
                SleepVideo.Init(Config);
                Inlays.Init();

                // Only what a Release build does not compile goes inside this guard. Three
                // shipped features - LoadingArt, SleepVideo and Inlays - had their Init in here
                // while harmony.PatchAll ran on them unconditionally below, so in Release they
                // were patched and never set up, and each one silently did nothing. The rule:
                // a type that is PatchAll'ed unconditionally has its setup out here too. Only
                // Dump, Probe and IconRender belong inside - the csproj Compile-Removes those
                // three files, so naming them at all is a compile error in Release.
#if DIAGNOSTICS
                // Development tools, Debug builds only - `dotnet build -c Debug`.
                //
                // They are not just config clutter: Dump and IconRender each hook Hud.Update,
                // and Dump also hooks InventoryGui.Update. Those are per-frame
                // patches, and a player's install should carry neither them nor a settings
                // section named Debug.

                // One launch with this on writes the vanilla UI hierarchy, sprite names and
                // canvas scalers to the log, then turns itself off. It is how the skin table
                // gets its names: from the game, not from memory.
                Dump.Enabled = Config.Bind(
                    "Debug", "dumpOnce", false,
                    "Write the vanilla UI hierarchy to the BepInEx log on the next launch, then switch off.");

                // Renders item icons from the prefabs. Dev tool, one-shot, off by default.
                IconRender.Init(Config);
#endif

                // HudLayout.Init turns SaveOnConfigSet off - a drag writes every frame otherwise -
                // so anything bound after it stays in memory and never reaches the file. One save
                // here puts the sections on disk where they can be ticked.
                Config.Save();

                // A mod that throws during load takes the whole chainloader with it, so every
                // patch group is guarded on its own: one target renamed by a game update leaves
                // that one part vanilla instead of skipping everything after it. SlotPatches is
                // what keeps Humanoid.DropInvalidItems off the hidden equipment/quick/shield rows;
                // inside one try it was skipped whenever any of the ~25 cosmetic groups before it
                // threw, and a character's worn kit dropped on the floor at login (Fable review,
                // 2026-10-05). The order is unchanged - postfixes on a shared method (InventoryGui.
                // Show) run in patch order, and the layout depends on it.
                var harmony = new Harmony(PluginGuid);

                // One switch back to the published HUD (Cartur, 2026-10-05). Off leaves the bars,
                // food diamonds, power box, edit mode, the HD texture loader and everything the
                // equipment/quick/shield slots need - the inventory, hotbar and equipment panel host
                // them, and switching the slots off would drop worn gear on the ground - and takes
                // every other new panel back to vanilla. Read at start-up; a change needs a restart.
                bool panels = Config.Bind("Panels", "newPanels", true,
                    "Turn off to keep only the HUD (bars, food diamonds, guardian power) and the "
                    + "equipment/quick/shield slots with the inventory, hotbar and equipment panel that "
                    + "hold them; crafting, chests, trader, menus, build menu, loading/sleep screens, "
                    + "popups, hover effect and the rest go back to vanilla. Restart to apply.").Value;

                foreach (Type t in new[] { typeof(HudSkin), typeof(VanillaBars), typeof(EditInputBlock),
                                           typeof(Hotbar), typeof(HotbarRow), typeof(IconHoverPatch),
                                           typeof(InventoryScreen), typeof(CookTimer), typeof(StyleTab),
                                           typeof(TextScale) })
                    Guard(t.Name, () => harmony.PatchAll(t));
                if (panels)
                {
                    foreach (Type t in new[] { typeof(CraftingBoard), typeof(ChestBoard), typeof(HoverFx),
                                               typeof(StoreBoard), typeof(MenuBoards), typeof(LoadingScreen),
                                               typeof(LoadingArt), typeof(SleepVideo), typeof(Inlays),
                                               typeof(SettingsScreen), typeof(PauseMenu), typeof(BuildMenu),
                                               typeof(ListScreens), typeof(Screens), typeof(Tooltip),
                                               typeof(Welcome) })
                        Guard(t.Name, () => harmony.PatchAll(t));
                    // EpicLoot's enchanting table and Jotunn's failed-connection window, if those mods
                    // are installed: hand-patched, because the types are in mods this one does not reference.
                    Guard("EnchantingScreen", () => EnchantingScreen.Init(harmony));
                    Guard("JotunnWindows", () => JotunnWindows.Init(harmony));
                    Guard("MapBorder", () => harmony.PatchAll(typeof(MapBorder)));
                }
                else
                    Logger.LogInfo("newPanels off: HUD, slots, inventory, hotbar and equipment panel only");
#if DIAGNOSTICS
                Guard("IconRender", () => harmony.PatchAll(typeof(IconRender)));
#endif

                // The equipment, quick and shield slots.
                Guard("slot system", () =>
                {
                    QuickSlots.Init(Config);
                    AutoEquip.Init(Config);
                    // Better Archery, if it is here: trips its own inventory-expansion gate so it
                    // stands down, and points its quiver row at our slots.
                    QuiverCompat.Init();
                });
                Guard("QssCompat", () => QssCompat.Init(harmony));
                // BaseRowsPatch is nested inside QuiverCompat, and PatchAll(Type) reads
                // AccessTools.GetDeclaredMethods on the type it is given - checked in the shipped
                // 0Harmony, PatchTools.GetPatchMethods - so it never descends into a nested class.
                // Its Prepare() no-ops when Better Archery is absent.
                foreach (Type t in new[] { typeof(QuiverCompat.BaseRowsPatch), typeof(SlotPatches), typeof(AutoEquip),
                                           typeof(EquipmentPanel), typeof(CharacterPreview), typeof(QuickSlots),
                                           typeof(GamepadSlots), typeof(ShieldSlot) })
                    Guard(t.Name, () => harmony.PatchAll(t));
                Guard("ShieldSlot.Init", ShieldSlot.Init);
                SlotPatches.CheckLookups();
                if (!StyleTab.Found)
                    Logger.LogWarning("Inventory.Changed not found - a style change will not refresh the icon");
                if (!CookTimer.Found)
                    Logger.LogWarning("CookingStation.m_nview not found - no cooking timers");

#if DIAGNOSTICS
                harmony.PatchAll(typeof(Dump));
#endif
            }
            catch (Exception e)
            {
                Logger.LogWarning("patch failed, HUD left vanilla: " + e);
            }
        }

        private void Guard(string part, Action patch)
        {
            try { patch(); }
            catch (Exception e) { Logger.LogWarning(part + " not patched, that part left vanilla: " + e); }
        }
    }
}
