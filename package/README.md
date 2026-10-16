# Cartur's UI

A hand-drawn wood-and-bronze interface for Valheim: bronze HUD bars and food diamonds, an inventory with built-in equipment, quick and shield slots, and reskinned crafting, chest, trader and menu screens.

Read this before installing:

- Remove Equipment and Quick Slots and Shield Me Bruh first. Their slots are built in now. If either is still installed, BepInEx skips this mod and starts the game without it.
- Uninstalling this mod drops what is in the equipment, quick and shield slots on the ground where you log in, the same as removing Equipment and Quick Slots. Take your gear off first, or pick it back up.
- The layout is designed for GUI scale 100%. At higher scales the crafting and equipment panels can overlap.
- Not every screen is reskinned yet. The list is below.

Just want the HUD from before? Set `Panels / newPanels` to `false` in the config and restart. That keeps the bars, food diamonds, guardian power and edit mode, plus the inventory, hotbar and equipment panel (they hold the equipment, quick and shield slots), and puts every other screen back to vanilla. The slots stay on either way, because turning them off would drop whatever is in them on the ground.

![Inventory and crafting](https://raw.githubusercontent.com/jekkle/cartur-ui-hud/master/docs/images/ui-inventory-crafting.png)

![Health and stamina bars, food diamonds and the guardian power](https://raw.githubusercontent.com/jekkle/cartur-ui-hud/master/docs/images/hud-closeup.png)

## What it does

The HUD is made of bronze-framed health, stamina, eitr and adrenaline bars that grow with your maximum, three food diamonds with countdowns, and a guardian power box that shows Ready or the cooldown. Eitr and adrenaline only appear once you have that pool.

The inventory is a bag, a hotbar and an equipment panel with your character shown live in it. The panel has equipment slots, three quick slots (Z, V and B by default) and a shield slot. The quick slots are the food diamonds. The slot layout is the one Equipment and Quick Slots used, so a character that has used that mod keeps its worn items where they were.

Crafting, chests and the trader are on painted boards with the controls seated on their plates. The build menu, skills, compendium, popups, pause and settings menus, and the loading and sleep screens use the same boards. Loading screens rotate through 18 pictures that ship with the mod: nine original paintings made for it, and nine pieces of official Valheim artwork (see Credits below). The sleep screen shows pictures and videos from the mod's own folder, a line from `dreams.txt` or the game's own dream text, or black, in proportions you can set. Every button gets a hover effect and the selected row gets a bronze highlight. Tabs that other mods add to the crafting panel share the tab row with Craft, Upgrade and Styles and are drawn in the same style.

When Epic Loot is installed, its enchanting table gets one board per tab and its trader windows are reskinned too.

![Epic Loot's enchanting table](https://raw.githubusercontent.com/jekkle/cartur-ui-hud/master/docs/images/ui-enchanting-table.png)

![Build menu](https://raw.githubusercontent.com/jekkle/cartur-ui-hud/master/docs/images/ui-build-menu.png)

![Epic Loot's trader windows beside the store](https://raw.githubusercontent.com/jekkle/cartur-ui-hud/master/docs/images/ui-trader.png)

With HD Valheim Textures installed, its textures load during the logo screen with a progress bar, instead of the game hanging for half a minute or more on the main menu.

Auto equip puts armour back on when you pick up the kit you died in, draws the weapon and shield you died holding, and puts a carry weight item such as the Megingjord back on as soon as you pick it up. Each of the three has its own switch.

On your first launch Hugin delivers a short welcome message. It shows once.

Still on vanilla art are the large map and minimap frames, the connecting screen and some small dialogs. Controllers work, but the mod is built around mouse and keyboard.

## Arranging it

Turn on `editMode` in the config. It is a tick box in game with [ConfigurationManager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/), or you can edit the file directly. It applies immediately, with no restart.

| | |
|---|---|
| drag | move a piece |
| wheel | size it |
| shift + wheel | a bar's length |
| ctrl + wheel | how far the frame's ornaments stand off the fill |
| ctrl + drag | nudge the frame against the fill |

Your input is held while edit mode is on, so a drag doesn't swing your axe. Turn it off when you're done. The layout saves either way. `resetLayout` puts every piece back where the mod would place it, and `saveAsDefault` makes the current layout the one `resetLayout` returns to.

## Your own art

Put a PNG in `BepInEx/config/CarturUI/` with the same name as one of the mod's own files in `plugins/CarturUIHud/assets/`, and it is used instead. Restart the game to see it. The BepInEx log says `custom art: <name>` for each file it picked up.

Make it the same size as the file it replaces, because the bar frames and boards are cut at fixed pixel positions. `food_frame.png`, `bar_fill.png` and `bar_fill_enemy.png` can be any size. A file that doesn't fit is skipped with a warning in the log, and the original is used. Delete your file to go back.

## Settings

Config file: `BepInEx/config/com.jekkle.valheim.carturuihud.cfg`.

| Setting | Default | What it does |
| --- | --- | --- |
| `Panels / newPanels` | true | Off: HUD, slots, inventory, hotbar and equipment panel only. Restart. |
| `Panels / textScale` | 1 | Text size in the inventory, crafting, chest and trader windows, 0.8 to 1.6. Applies straight away. |
| `Layout / editMode` | false | Arrange the pieces, see above. |
| `Layout / resetLayout` | false | Tick to put every piece back. It unticks itself. |
| `Layout / saveAsDefault` | false | Tick to make the layout on screen the one `resetLayout` goes back to. Open the inventory once first. |
| `Layout / quickSlotDetail` | true | Eat/countdown text on the food diamond's bottom bevel. Off puts the countdown above the bottom corner. Read at startup. |
| `Art / darkMode` | false | Keep vanilla's dimmed panel material. |
| `Art / woodPanel` | true | Use the generated wood panel for every menu panel. Off returns to the panel built from the bar and diamond art. |
| `Quick slots / key1`, `key2`, `key3` | Z, V, B | Hotkeys for the three quick slots. |
| `Auto equip / armourOnPickup` | true | Put armour back on when you pick up the kit you died in. |
| `Auto equip / weaponAndShieldOnPickup` | true | Draw the weapon and shield you died holding when you pick them up. |
| `Auto equip / carryWeightOnPickup` | true | Put a carry weight item back on as soon as it is picked up. |
| `Loading screens / enabled` | true | Show the mod's pictures on the loading screen. Off leaves vanilla's. |
| `Sleep screen / show` | Media | Media shows the mod's pictures and videos, Vanilla the game's own screen, Nothing is black. |
| `Sleep screen / volume` | 0.5 | Volume of a sleep video, 0 to 1. |
| `Sleep screen / videoChance` | 0.08 | Chance a night shows a video. |
| `Sleep screen / vanillaDreamChance` | 0.33 | Chance a night shows the game's own dream text instead of one from `dreams.txt`. |
| `Sleep screen / blackChance` | 0.15 | Chance a night shows nothing at all. |

## Compatibility

Declared incompatible, so BepInEx skips this mod if one of these is in the profile: Auga, AugaLite, Equipment and Quick Slots, Shield Me Bruh.

It works alongside Epic Loot, Jotunn, Better Archery (its quiver sits in three ammo cells of the equipment panel), HD Valheim Textures and Cartur's Waste Management (its trash can and sort button sit in the inventory's side column). It also works with Quick Stack Store: its trash can joins the side column, and its quick stack, store all and sort leave the equipment and quick slots alone. Food in a quick slot and arrows in the quiver can still be restocked. Any other mod that redraws the same windows will fight it.

## Install

Use a mod manager (r2modman, Thunderstore or Gale) and it pulls in BepInEx for you. It is client-side. No server install is needed, and it works in multiplayer.


## Credits

Nine of the loading screens are official Valheim artwork by Iron Gate Studios, taken from valheimgame.com. They belong to Iron Gate. This mod is free and non-commercial, and the Valheim EULA from Coffee Stain Publishing welcomes non-commercial projects using the game's content. Turn `Loading screens / enabled` off to see vanilla's loading screens instead.

This is an unofficial fan mod. It is not made, endorsed or supported by Iron Gate Studios or Coffee Stain Publishing.

---

*Free, and always will be. If it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**[Discord](https://discord.gg/nd5RqpwNkz)**: bug reports, install help, and mod requests.
Bug reports get their own thread so nothing is lost in a chat scroll, and requests are voted on.

**More from Cartur:**
[HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Map Pins](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Map_Pins/) ·
[Compass and Clock](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Compass_and_Clock/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/) ·
[Follow Command](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Follow_Command/) ·
[Flooring](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Flooring/) ·
[Waste Management](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Waste_Management/) ·
[Feeding Trough](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Feeding_Trough/) ·
[Combat Text](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Combat_Text/)
