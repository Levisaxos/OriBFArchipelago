# Archipelago Statistics Pages

Adds Archipelago run statistics to the game's own inventory/pause screen. Pressing **Y**
(controller) or **L** (keyboard) cycles the **Statistics** block through three pages:

1. **Vanilla** — playtime, completion %, deaths, health, energy, skill points. Untouched.
2. **AP global** — run time, checks collected, deaths, teleports used, time lost, checks
   sent / items received.
3. **AP per-area** — one row per world area: time, deaths, pickups collected / total.

On the AP pages, each acquired skill on the Skills ring is labelled with the run time at which
it arrived. On completion, the screen opens straight onto the AP global page.

Nothing is drawn with IMGUI. The pages reuse the game's own `MessageBox` objects, so the font,
layout and framing are the game's.

---

## Why it is built this way

### `UpdateItems` overwrites the stat boxes ~50x/sec

`InventoryManager.FixedUpdate()` and `OnEnable()` both call `UpdateItems()`, which rewrites all
six stat `MessageBox`es with `SetMessage(...)` regardless of whether the screen is visible.
`SetMessage` nulls `MessageProvider` and pushes text straight into the `TextBox`, so anything
written earlier in the frame is lost.

The AP global page is therefore applied as a **`[HarmonyPostfix]` on `UpdateItems`**, and it
must rewrite all six values every tick — caching an unchanged string there would leave the
vanilla value on screen. `Show()` is not a usable hook; it only calls
`NavigationManager.SetVisible(true)` and `SetIndexToFirst()`.

Cloned boxes (the per-area rows and skill labels) are ours alone and *are* change-cached, since
`SetMessage` re-renders the text mesh.

### There is no `ActionButtonY`

On a controller, Y maps to three `Core.Input` processors: `Bash`, `Delete` and `Legend`.
**`Legend`** is the one used here — vanilla reads it only from `AreaMapUI` while in area-map
mode, so it is free on the inventory screen, and its default keyboard binding is `L`. `Bash`
would have been risky: `OptionsScreen` reads it, and Options is reachable from this screen.

Because it is a game control rather than a randomizer keybind, it is rebound from the game's
own control options, not from `Keybinds.txt`.

### Button glyphs are not guessable

`ButtonIconUtility`'s icon table is a set of private consts, and the letters are not sequential
by button — `<icon>h</>` is **X**, not Y. The values used here were read out of the assembly:

| Button | Glyph |
|---|---|
| A | `<icon>e</>` |
| X | `<icon>h</>` |
| Y | `<icon>i</>` |
| LB | `<icon>R</>` |
| RB | `<icon>S</>` |
| Keyboard L | `<icon>P</>` |

### Pickup counts are derived, never counted

The mod models death rollback: `RandomizerReceiver.OnDeath()` resets `unsavedInventory` and
demotes `CheckedNotSaved` locations to `LostOnDeath`. An incrementing pickup counter would
over-report after every death, so per-area pickups are computed from the receiver's checked
locations joined against `LocationLookup`.

For the same reason the global "checks collected" number comes from
`session.Locations.AllLocationsChecked.Count` rather than the local dictionary, which
double-counts progressive mapstones and retains `LostOnDeath` entries.

### Multiworld counters need no scouting

The mod logs in with `ItemsHandlingFlags.AllItems`, so the server replays every item — including
the player's own — into `session.Items.AllItemsReceived`. `ItemInfo` carries `LocationId` and
`Player`, so the split is a local computation (`ArchipelagoConnection.GetItemCounts`):

```csharp
int selfFound = session.Items.AllItemsReceived
    .Where(i => i.LocationId > 0 && self.IsRelatedTo(i.Player))
    .Select(i => i.LocationId).Distinct().Count();
int sentToOthers = session.Locations.AllLocationsChecked.Count - selfFound;
```

`LocationId > 0` excludes starting-inventory and server-granted items; `Distinct()` is required
because AP replays duplicates on resync; `IsRelatedTo` rather than a raw slot comparison so
item-link group slots still resolve to the local player. `ItemSendLogMessage` is deliberately
**not** used — `PrintJSON` is live-only and does not replay on reconnect.

The whole computation refreshes once a second, not at `UpdateItems` cadence.

---

## Files

| File | Role |
|---|---|
| `Core/RunStats.cs` | Serializable per-run model, plus `FormatTime` matching the vanilla clock format |
| `Core/RunStatsTracker.cs` | Owns the live run's stats, pumped from `RandomizerManager.Update()`; derives per-area pickup counts |
| `Core/RandomizerIO.cs` | `ReadStats` / `WriteStats`, plus copy and delete wiring |
| `ArchipelagoUI/ApStatsPages.cs` | The page state machine and all `MessageBox` work |
| `Patches/InventoryManagerPatches.cs` | The legend hint, measured so it cannot overlap the teleport hints |
| `Patches/CleverMenuItemSelectionManagerPatches.cs` | The `Legend` input branch |

Hooks: `DeathSavePatches` (deaths, save), `TeleporterControllerPatch` (teleports),
`RandomizerReceiver.ReceiveSkill` (skill timeline), `WorldEventPatches.GameCompletePatch`
(completion), `RandomizerManager` (begin/pump/end).

## Save format

`ArchipelagoData\Slot{n}Stats.txt`. Absent for saves predating this feature, which start from
an empty `RunStats`. Numbers are written and parsed with the **invariant culture** so files stay
portable between locales that disagree about the decimal separator.

```
Version=1
TotalTime=19986.8
TimeLost=1057.7
Deaths=52
Teleports=72
Completed=True
CompletionTime=19986.8
Area.Glades=3774.1,1
Skill.Bash=1234.5
```

Unknown keys are logged and skipped, so the format can grow. The file is copied and deleted
alongside the inventory and location files.

Note there is no pickup-time list: PPM and peak PPM were dropped, and they were its only
consumers. Re-adding them means adding `List<float> PickupTimes` and a 10-minute sliding window.

## Time accounting

`TotalTime` accrues whenever `Characters.Sein` exists, is active and is not suspended — so
menus, cutscenes and loads are excluded, but death and respawn are included. That makes
`TimeLost` (death until control returns) a true subset of `TotalTime` rather than a separate
clock. The world area is re-resolved 4x/sec rather than per frame, since
`GameWorld.WorldAreaAtPosition` walks the area list.

---

## Still to verify in-game

The prefab layout is serialised scene data and is not visible in the assemblies, so these were
written against derived values rather than measured ones. `ApStatsPages` logs the statistics
block hierarchy at debug level the first time an AP page is shown — use that output to tune:

1. **Per-area row placement.** `AREA_COLUMNS`, `AREA_COLUMN_SPACING` and `AREA_ROW_SPACING` in
   `ApStatsPages` are first-pass values derived from the vanilla slot spacing. Expect to adjust.
2. **What `HideForAreaPage` hides.** It deactivates each stat slot's *parent*, assuming the icon
   is a sibling of the value. If the icon is elsewhere, this needs changing.
3. **Skill label offset.** Labels are parented to the ring's parent, not to the icon, because a
   locked ability's `TransparencyAnimator` can drive children to zero alpha and, in
   colour/dissolve mode, hard-disable their renderers. The `-0.6` Y offset is a guess.
4. **Icon mismatch on the global page.** Time, completion and deaths keep their meaning, so
   those three icons still read correctly. Teleports, time lost and checks sent borrow icons
   that do not match. Whether the icons can be hidden or swapped depends on (2).

`ApplyPage` is wrapped in a try/catch that reverts to the vanilla page and logs, so a wrong
assumption degrades rather than spamming an exception every FixedUpdate.

## Building

On `DESKTOP-BBR42JK` the csproj's build events kill Ori, copy the DLL into the plugins folder
and relaunch the game. To build without that:

```bash
dotnet build OriBFArchipelago.csproj --no-restore -p:ComputerName=ci
```
