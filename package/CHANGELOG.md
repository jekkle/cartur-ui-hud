# Changelog

## 1.1.3

- **Works with Quick Stack Store.** Its trash can no longer throws an error every time the
  inventory opens - it gets a box in the side column instead. And quick stacking, store all and
  sort now leave your equipment and quick slots alone, where before P emptied the quick slots
  into chests. Food in a quick slot and arrows in the quiver can still be restocked. Reported on
  Nexus.
- **`Panels / textScale`** - make the text in the inventory, crafting, chest and trader windows
  bigger (up to 1.6) or smaller, for big monitors. Applies straight away. Requested on Nexus.

## 1.1.2

- **The server password box says when it's locked.** On the world screen it only takes typing
  once Start Server is ticked (that's the game), but it looked ready, so typing did nothing.
  Now it reads "Tick Start Server to set a password" and is shaded until you do. The game's
  own "[Empty]" hint, which the 1.1.0 layout clipped out of sight, shows again. Reported on Discord.

## 1.1.1

- **Your own art.** A PNG in `BepInEx/config/CarturUI/` named like one of the mod's files
  replaces it - bar frames, fills, food frames, boards. Same size as the original, except
  the food frame and bar fills, which can be any size. Requested on Nexus.

## 1.1.0

The rest of the interface. Until now this mod was the HUD; now it reskins most of the game.

- **Read first:** Equipment and Quick Slots and Shield Me Bruh are now incompatible — their
  equipment, quick and shield slots are built in. Remove them before updating. Uninstalling
  this mod drops what is in those slots on the ground, the same as removing Equipment and
  Quick Slots.
- **`Panels / newPanels`** (on by default). Off keeps the HUD from 1.0.5 plus the inventory,
  hotbar and equipment panel that hold the slots, and puts every other screen back to
  vanilla. Restart to apply.
- Inventory, hotbar and an equipment panel with your character live in it, plus equipment,
  quick (Z / V / B) and shield slots. The food diamonds show the quick slots.
- Painted boards for crafting, chests, the trader, the build menu, skills, compendium,
  popups, the pause and settings menus, and the loading and sleep screens.
- Epic Loot's enchanting table (one board per tab) and trader windows, when it is installed.
- With HD Valheim Textures installed, its textures load during the logo screen with a
  progress bar, instead of freezing the main menu for half a minute or more.
- A hover effect on every button, toggle and dropdown; a bronze highlight on the selected row.
- New default layout. Bars keep the positions they had; anything you moved stays where you
  put it.
- Bars stay on screen at very high health, and their numbers are only rebuilt when they
  change (from 1.0.5).

## 1.0.5

- Bar text is only rebuilt when the value changes, not every frame.
- Extended Action Quick Slots: its bar is found even when it appears late, and the mod no longer adds another listener every time the HUD rebuilds.
- The bars stay on screen at very high health and stamina.
- If the mod fails to start, it now removes all of its changes so the game shows its normal bars.
- The adrenaline bar hides at zero adrenaline, like vanilla, instead of showing an empty frame for anyone wearing a trinket.

## 1.0.4

- Edit mode works on machines where it did not. The overlay appeared, the cursor came back,
  and nothing could be dragged. The mod was reading the mouse itself and working out what the
  cursor was over by hand; it hands both jobs to Unity now, the way the compass mod's edit
  mode already does. Drag, ctrl+drag and the wheel all behave as before.

## 1.0.3

- The eitr bar now shows. It shared a row with adrenaline, and adrenaline won that row
  whenever you had any adrenaline pool at all - which every vanilla trinket grants, bronze
  upwards. So anyone wearing a trinket never saw their mana, however much of it they had.
- Eitr and adrenaline get a row each now, and each one appears on its own pool without
  consulting the other, the way the game's own HUD does it.
- A config written by an earlier version has both bars saved at the same spot. It is
  moved to the new rows on load; any size or length you set by hand is kept.

## 1.0.2

- Listing only. New icon: a shot of the HUD in game, so the Thunderstore tile and the
  Nexus page show the same picture. No code change.

## 1.0.1

- Listing only. Links the rest of the Cartur mods, Flooring included, in the same
  place the others carry theirs. No code change.

## 1.0.0

First release.

- Health, stamina, eitr and adrenaline bars in a 9-sliced bronze frame, with a knotwork
  fill that is uncovered rather than stretched as the bar drains.
- Bars take their length from your maximum, so they grow when you eat and shrink when the
  food runs out. Every bar reaches the same length at its own ceiling.
- Three food diamonds with countdowns, driven by Equipment and Quick Slots when it is
  present and by the vanilla eaten-food list otherwise.
- Guardian power box with the name and Ready/cooldown drawn over the icon.
- Eitr and adrenaline share a row; neither is drawn until you have that pool.
- Edit mode: drag to move, wheel to size, shift+wheel for bar length, ctrl for the frame.
  Saved to the config and applied live, with no hotkey to hit by accident.
