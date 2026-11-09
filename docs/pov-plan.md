# Path of Valheiman compatibility - plan (2026-10-25)

Owner call: fully compatible, Cartur's UI is the main system; PoV's cells on a second page of
the equipment board; no stopgap release. PoV decompile: `Valheim/_ref/pov/src` (5.1.2, API 1.6.0).

## Measured (pilot `run.ps1 -Pov -ProfileName PovTest`, 2026-10-25)
Inventory stays 8x7 (our FullHeight wins over PoV's +8 rows). PoV reports Owns True / Visible 7 /
On True / no provider: it reads our 3 hidden slot rows as backpack. Both equipment sheets draw.

## Read from PoV
- `Storage.Register(IStorageProvider)` before a character loads. `Cells.Choose()` runs in a
  `Player.Load` prefix (priority 800); with a provider it sets hosts "rows" + "equipment", and
  PoV then never attaches its rows (EquipRows checks `Cells.External`) and hides its Sheet,
  ToolBar and HudCells (Module, `Equipment.HostedBy != null`). Its hotkeys still run (Module:355-382,
  before that check).
- Provider: `ItemIn(cell)` must return an item that is in the player's inventory and that
  `State.Accepts` the slot; `Put(cell,item)` / `TakeOut(cell)`; call `Storage.Changed(cell)` after.
  PoV's own reference is `PassiveTree.Gear/SelfCheckStorage.TestProvider`.
- Cells routed through the provider: `Storage.Cells` (IsStorage: food 3, arrow 3, tool 8,
  potion 3, key 8, utility/wisp/wishbone/tome/earring 5, ring 10 = 40) plus purse.0-7 (Wallet
  calls `Cells.MoveIn` for them). Worn armour/belt/trinket read vanilla Humanoid fields.
- `Storage.KeptOnDeath(p, cell)` (purse when the wallet perk is on) - the host must keep those.
- `Rows.Extra` - backpack rows earned in PoV's tree; the rows host must show them.

## Steps
1. PoV cells as a Slots kind (indices from 24, after our three rows), HiddenRows grows to fit;
   provider = our grid positions, so it persists with the save. Cells parked off-screen. Pilot:
   put/take each kind, food key eats from food.0, relog keeps them.
2. Page 2 on the equipment board drawing those cells (locked look when `Equipment.IsOpen` is false,
   `Equipment.KeyLabel` on the cell).
3. HUD for PoV's food/potion/tool/arrow keys (PoV hides its own when hosted).
4. `Rows.Extra` into VisibleRows; `KeptOnDeath` cells kept out of the grave.
5. RuneUI parity.

Open: removing PoV later leaves items in rows past our 3 - DropInvalidItems would drop them.
Needs a guard before release (keep height while those rows hold items, or move them to the bag).
