# Changelog

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
