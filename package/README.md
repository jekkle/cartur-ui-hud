# Cartur's UI — HUD

*Free, and always will be — if it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**More from Cartur:** [HD Blood](https://thunderstore.io/c/valheim/p/Cartur/Carturs_HD_Blood/) ·
[Map Pins](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Map_Pins/) ·
[Compass and Clock](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Compass_and_Clock/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/) ·
[Follow Command](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Follow_Command/) ·
[Flooring](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Flooring/)

A hand-drawn bronze HUD for Valheim. Norse knotwork frames on the bars, diamond frames
on the food, and every piece placed and sized by you.

![The HUD in place](https://raw.githubusercontent.com/jekkle/cartur-ui-hud/master/docs/images/hud-closeup.png)

Client-side. No server install needed, works in multiplayer.

## What you get

- **Health, stamina, eitr and adrenaline bars** in a sliced bronze frame that keeps its
  ornaments crisp at any length. The knotwork fill is uncovered as the bar drains rather
  than squashed.
- **Three food diamonds** with the countdown on each. With
  [Equipment and Quick Slots](https://thunderstore.io/c/valheim/p/RandyKnapp/EquipmentAndQuickSlots/)
  installed they show your quick slot items instead, and a slot holding something you are
  currently digesting still shows that food's timer.
- **The guardian power** in its own frame, with the name and Ready/cooldown over the icon.
- **Eitr and adrenaline share a row** — whichever pool you actually have is the one drawn.
  Neither appears at all until you have one.

Bars grow with your maximum, not just your current value: eat, and the bar itself gets
longer; let the food run out and it shrinks back. Each bar reaches the same length at its
own ceiling, so a 100-point adrenaline pool reads as full as a 325-point health pool.

## Arranging it

Everything is movable. Turn on **`editMode`** in the config — with
[Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/)
that is a tick box in game, or edit the config file in r2modman or a text editor and it
applies immediately without a restart.

| | |
|---|---|
| drag | move a piece |
| wheel | size it — bar height, or overall scale on the food and power group |
| shift + wheel | a bar's length |
| ctrl + wheel | how far the frame's ornaments stand off the fill |
| ctrl + drag | nudge the frame against the fill |

Player input is held while edit mode is on, so a drag does not swing your axe. Turn it
off when you are done; the layout saves either way and survives restarts.

The three food diamonds and the power box move together as one group. The four bars are
individual, so you can stack them how you like.

![In world](https://raw.githubusercontent.com/jekkle/cartur-ui-hud/master/docs/images/hud-in-world.png)

## Compatibility

This mod takes over the vanilla health, stamina, eitr and adrenaline bars outright — it
skips the game's own update for each of them and draws them itself. **It will fight any
other mod that replaces those bars**, including Auga and AugaLite. Pick one.

Everything else is left alone. It does not touch the inventory, the build menu, the map
or the status effects, and mods that change those are unaffected.

Known good alongside: Equipment and Quick Slots, Epic Loot, BetterArchery, Jotunn.

## Config

`BepInEx/config/com.jekkle.valheim.carturuihud.cfg`

One `editMode` switch and one line per piece, written as `x,y,size,length,frameSize,frameX,frameY`.
Hand-editable if you want exact numbers — changes apply live.

## Part of a set

This is the HUD. More of the interface is coming as separate mods, so you can take the
parts you want.
