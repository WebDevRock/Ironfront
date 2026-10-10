# Milestone 002 — Economy, Construction and Production

**Date:** 2026-10-10
**Engine:** Unity 6000.6.5f1, URP 17.6.0, Input System 1.20.0, AI Navigation 2.0.15
**Builds on:** Milestone 001 (`0424e3b`)
**Commit:** see `Docs/AI-Development-Log.md` (Milestone 002 entry)

## Summary

The sandbox now has a working economy. A harvester mines a finite Ore deposit and delivers it to the
refinery for credits. The player builds a Power Plant and a Vehicle Factory from a construction menu with
a validity-coloured placement preview. The factory produces harvesters and tanks through a visible queue,
spawns them at a free spot and sends them to a rally point. New units use the Milestone 001 selection and
movement systems unchanged.

## How to play

Open `Assets/Scenes/RTS_Sandbox.unity` and press Play, or run `Builds/Windows/Ironfront.exe`.

The match starts with a Headquarters, a Refinery, one Harvester (already harvesting), one Ore deposit
(5000 Ore) and **2000 credits**.

| Action | How |
|---|---|
| Construct | Sidebar (right) ▸ CONSTRUCT button ▸ move the green/red ghost ▸ LMB to build. RMB or Esc cancels. Clicking the same button again also cancels |
| Select a structure | LMB on it (green frame) |
| Produce | Sidebar ▸ Production buttons (Harvester, Tank). Goes to the selected factory, or the first one owned |
| Cancel production | Click the entry in QUEUE (full refund) |
| Rally point | Select the factory, then RMB on the ground (green flag shows while the factory is selected) |
| Order a harvester | RMB on an Ore deposit to harvest it. RMB on ground moves it and pauses harvesting |
| Everything from Milestone 001 | Unchanged: click / shift-click / drag selection, RMB move in formation, WASD / edge pan, wheel zoom |

The sidebar shows credits, power (used / produced) and placement or production feedback. The top-left panel
shows the selected structure, or a single harvester's live status (e.g. `Harvesting 120/200`,
`Returning to refinery`, `Waiting: no ore left`).

## Data (all editable in `Assets/Ironfront/Data`)

| Asset | Cost | Time | Footprint (+ apron) | Power | Other |
|---|---|---|---|---|---|
| Headquarters | — (start only) | — | 8×8 (+3) | +20 | Prerequisite for all construction |
| Refinery | 1000 | — | 7×6 (+4 dock) | −15 | Accepts Ore; 1 credit per Ore |
| Power Plant | 500 | — | 5×5 | +50 | |
| Vehicle Factory | 1300 | — | 8×7 (+5 exit) | −30 | Produces Harvester, Tank |
| Harvester | 800 | 10 s | — | — | Capacity 200, harvest 50/s, unload 100/s |
| Tank | 400 | 6 s | — | — | No weapons yet (combat is out of scope) |
| MatchSetup | — | — | — | — | Factions (player: 2000 credits), menu order, queue size 5, low-power production ×0.5 |
| Ore deposit (prefab) | — | — | — | — | 5000 Ore, harvest radius 2.5 m |

Starting credits (2000) cover the Power Plant and Factory (1800). The two tanks (800) must be paid for
with harvested Ore. When drain exceeds output, the sidebar shows LOW power and production runs at half speed.

## Architecture

**Pure, testable rules** (`Scripts/Runtime/Core`, no scene access):

| Class | Rule |
|---|---|
| `Economy/ResourceWallet` | Credits. `TrySpend` fails rather than going negative, negative amounts throw, `Add` saturates. `Changed` event |
| `Economy/PowerGrid`, `StructureRoster`, `FactionState` | Per-faction power, structure counts (prerequisites) and wallet |
| `Economy/ConstructionRules` | Prerequisite and funds check; `TryPurchase` charges only on success |
| `Production/ProductionQueue` | FIFO queue, paid when queued, full refund on cancel, capacity limit, speed multiplier, `Changed`/`Completed` events |
| `Production/SpawnLocator` | Ordered candidate spots around the factory exit |
| `Construction/PlacementRules` | Footprint vs map bounds, structures plus clearance, aprons (docks/exits), obstacles, resource access ring, units |
| `Harvesting/HarvesterStateMachine` | Move → harvest → return → unload loop, depletion handling, waits and retries |

**Data** (`Scripts/Runtime/Data`): `BuildingDefinition`, `UnitDefinition`, `HarvesterDefinition`,
`MatchSetup` ScriptableObjects.

**Scene components:**

| Component | Role |
|---|---|
| `Match/MatchController`, `FactionMember` | Faction state per id; ownership of units/structures. Nothing is hard-wired to the human player: selection, the HUD and placement ask `MatchController` for the player faction, and economy/production act on whichever faction owns the object |
| `Buildings/Building`, `Refinery`, `VehicleFactory` | Register with their faction for power and prerequisites; refinery dock; factory queue, spawn and rally point |
| `Economy/OreDeposit`, `Harvester` | Finite source with a shrinking visual; harvester adapter around the state machine |
| `Construction/BuildingPlacementController` | Ghost preview, validation (pure rules plus physics overlap and NavMesh checks), purchase and spawn |
| `UI/EconomyHud` | Sidebar generated from the data definitions. Updates from events: wallet, power, structures, queue, placement messages, depletion. Only the head progress bar is polled per frame |
| `Input/RtsInputMode` | Pointer arbitration, so placement clicks never reach selection or move orders |

**Milestone 001 systems reused and extended, not replaced:**
- `SelectionManager` adds structure selection and player-faction filtering, and ignores the pointer while placing.
- `UnitCommandController` adds harvest orders, rally points, and move orders that interrupt harvesting.
- `UnitMover` buffers orders given before its agent is enabled (freshly spawned units).
- `SelectionHud` shows structure and harvester details.
- The RTS input map gained an Escape `Cancel` action.

**Navigation:** structures and deposits are excluded from the NavMesh bake and carve it at runtime with
`NavMeshObstacle`. Placed buildings therefore update pathing, and a depleted deposit frees its area.
"Essential navigation" is protected by rules: structures keep a 1.5 m gap from other structures and
obstacles (wider than a unit), nothing may be built on a refinery dock or factory exit apron, deposits
keep a 3 m access ring, and the site and its apron must be on the NavMesh.

## Verification

### Automated — verified

| Check | Result |
|---|---|
| Compile | No errors; no warnings from project code (two deprecation warnings in the new tests were fixed) |
| EditMode tests | **82 / 82 passed** (36 from Milestone 001 + 46 new): wallet and transactions, power, prerequisites, production queue (cost, capacity, order, low power, cancel/refund, events, credits never negative), spawn locator, placement rules (bounds, overlap, clearance, aprons, obstacles, resources, units), harvester state machine (full cycle, capacity, depletion mid-load, all deposits empty, no refinery, stop, preferred deposit) |
| PlayMode tests (real scene) | **13 / 13 passed**: 5 Milestone 001 tests (adapted to the extra harvester) + 8 new ones. These cover the start state, harvest income (credits match Ore mined), depletion behaviour, placement validity in the scene, no charge on failed placement, factory selection with queue refund, move orders pausing harvesting and harvest orders resuming it, and the **full acceptance scenario**: build the Power Plant and Factory, harvest, produce two tanks, tanks reach the rally point, are click-selectable and obey a group move order. Credits stayed ≥ 0 throughout |
| Windows build | **Succeeded**, 0 errors. 511 warnings, all from packages (as in Milestone 001) |
| Player smoke test | The player ran 20 s with a clean log: no errors, exceptions, NavMesh or setup problems |

### Acceptance scenario through the real UI (simulated input, Play Mode) — verified

I drove every step with injected mouse and keyboard events and read the outcome from game state:

1. Clicking **Power Plant** entered placement mode. The ghost over the HQ was red (`OverlapsStructure`) and a click there built nothing. Over open ground it turned green, and a click built the plant (power 15/20 → 15/70)
2. **Vehicle Factory** was placed the same way (power 45/70); the sidebar then offered Harvester and Tank
3. Clicking the factory selected it (green frame, top-left panel). RMB on the ground set the rally point and showed the flag
4. Two clicks on **Tank** queued two tanks ($400 each, queue shown with progress). Both spawned at separate spots and drove to the rally point
5. A drag-box selected both tanks ("Selected units: 2") and RMB moved them to the clicked point
6. Escape cancelled placement, unit click selection still worked, and A-key panning and wheel zoom still worked
7. Over the session the harvester mined the whole 5000-Ore deposit. The deposit disappeared, the HUD showed "An ore deposit has been depleted", and the harvester delivered its last load, then reported `Waiting: no ore left`

There were 0 game warnings or errors in the console during the session. The same Editor input harness as
Milestone 001 was used (virtual mouse, in-memory input settings), and everything was restored afterwards.
In two early attempts a placement click was lost because the injected pointer position read (0,0) after a
Game-view capture. This was a harness artefact: once the pointer was correct, the same steps worked every time.

### Requires a human tester

- Playing the **built `.exe`** interactively. Only launch and the log were checked automatically
- Feel and readability: ghost colours, sidebar layout at other resolutions, harvester pacing and economy balance
- Harvesters pathing next to each other at the dock when there are several, which wasn't exercised with more than one harvester at a time

## Known limitations

- Construction is **instant** once paid for; there is no build-up animation or construction time.
- Placement checks use axis-aligned footprints; buildings cannot be rotated.
- "Does not obstruct navigation" is enforced through clearance and apron rules plus a NavMesh check at the site. It does **not** compute full paths, so a player could still wall off an area with a chain of buildings spaced exactly 1.5 m apart (each gap stays passable for units).
- Harvesters, tanks and the capsule scouts use the default NavMesh agent type (radius 0.5). The vehicles' own radius (0.8) is only used for local avoidance, so they can clip tight corners slightly.
- Production pays the full cost when an item is queued (not progressively as in some RTS games); cancelling refunds it all.
- Tanks have no weapons or health; combat and enemy AI are explicitly out of scope.
- Only one player faction is configured. Additional factions only need entries in `MatchSetup` and `FactionMember` ids, but there is no AI to use them yet.
- The player build adds the Input System actions asset to `PlayerSettings.preloadedAssets` (standard Input System behaviour for project-wide actions); this change is committed.
