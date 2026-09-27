# Cartur's UI — HUD

A hand-drawn bronze HUD. Norse knotwork frames on the bars, diamond frames on the
food, and every piece placed and sized by you.

![The HUD in place](https://raw.githubusercontent.com/jekkle/cartur-ui-hud/master/docs/images/hud-closeup.png)

![In world](https://raw.githubusercontent.com/jekkle/cartur-ui-hud/master/docs/images/hud-in-world.png)

- **Health, stamina, eitr and adrenaline bars** in a sliced bronze frame that keeps
  its ornaments crisp at any length.
- **Three food diamonds** with the countdown on each.
- **The guardian power** in its own frame, with Ready or cooldown over the icon.
- **Eitr and adrenaline get a row each** — neither is drawn until you have that
  pool, and neither hides the other.

Bars grow with your maximum, not just your current value: eat and the bar itself
gets longer. Each reaches the same length at its own ceiling, so a 100-point
adrenaline pool reads as full as a 325-point health pool.

## Arranging it

Turn on **`editMode`** in the config — a tick box in game with
[ConfigurationManager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/),
or edit the file directly. It applies immediately, no restart.

| | |
|---|---|
| drag | move a piece |
| wheel | size it |
| shift + wheel | a bar's length |
| ctrl + wheel | how far the frame's ornaments stand off the fill |
| ctrl + drag | nudge the frame against the fill |

Your input is held while edit mode is on, so a drag doesn't swing your axe. Turn it
off when you're done; the layout saves either way.

The three food diamonds and the power box move together as one group. The four bars
are individual, so you can stack them how you like.

## Settings

`BepInEx/config/com.jekkle.valheim.carturuihud.cfg`. Changes apply live.

| Setting | Default | What it does |
| --- | --- | --- |
| `Layout / editMode` | false | Arrange the HUD — see above. |
| `Art / woodPanel` | true | Use the drawn wood panel for menu panels. |
| `Art / darkMode` | false | Keep vanilla's dimmed panel material. On makes panels darker. |

Under `Layout` there is also one line per piece, written as
`x,y,size,length,frameSize,frameX,frameY`. Hand-editable if you want exact numbers.

## Compatibility

Four mods are declared incompatible in the plugin itself, so BepInEx will not start
the game with this one and any of them in the same profile. Take the other one out:

- **Auga** and **AugaLite** — they replace the same bars, panels and loading screen.
- **[Equipment and Quick Slots](https://thunderstore.io/c/valheim/p/RandyKnapp/EquipmentAndQuickSlots/)**
  and **Shield Me Bruh** — their equipment, quick and shield slots are part of this
  mod now. A character that has been through Equipment and Quick Slots keeps every
  worn item exactly where it was: the slot grid layout here is the one it used.

**BetterArchery** works alongside — its quiver is hosted in three ammo cells of the
equipment panel. **Cartur's Waste Management** works alongside too; its trash can and
sort button are seated into the inventory's side column.

This is more than a HUD, so any other mod that redraws one of these will fight it:
the four bars, the food and guardian-power boxes, the hotbar, the inventory,
container, crafting and info panels, the equipment, quick and shield slots, item
tooltips, the build menu, the skills and compendium screens, the settings and pause
menus, the trader, chat and radial menus, and the loading and sleep screens. It also
draws a frame around the minimap and the map; the map itself is left alone.

Known good alongside: Epic Loot, Jotunn.

## Install

Use a mod manager (r2modman / Thunderstore / Gale) and it pulls in BepInEx for you.

Client-side. No server install needed, works in multiplayer.

This is the HUD. More of the interface is coming as separate mods, so you can take
the parts you want.

---

*Free, and always will be. If it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**[Discord](https://discord.gg/nd5RqpwNkz)** — bug reports, install help, and mod requests.
Bug reports get their own thread so nothing is lost in a chat scroll, and requests are voted on.

**More from Cartur:**
[HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Map Pins](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Map_Pins/) ·
[Compass and Clock](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Compass_and_Clock/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/) ·
[Follow Command](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Follow_Command/) ·
[Flooring](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Flooring/) ·
[Waste Management](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Waste_Management/) ·
[Feeding Trough](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Feeding_Trough/)
