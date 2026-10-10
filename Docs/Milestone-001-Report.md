# Milestone 001 — RTS Sandbox

**Date:** 2026-10-10
**Engine:** Unity 6000.6.5f1 (Unity 6), URP 17.6.0
**Packages used:** Input System 1.20.0, AI Navigation 2.0.15, uGUI 2.6.0, Test Framework 1.8.0
**Commit:** see `Docs/AI-Development-Log.md` (Milestone 001 entry)

## Summary

A playable 3D RTS sandbox: a 100×100 flat map with six obstacles, an angled overhead camera (WASD,
screen-edge pan, wheel zoom), five coloured capsule units that can be selected by click, shift-click and
drag-rectangle, and right-click move orders that spread the selection into a grid formation and path
around obstacles on a baked NavMesh.

## How to play

Open `Assets/Scenes/RTS_Sandbox.unity` and press Play, or run `Builds/Windows/Ironfront.exe`.

| Input | Action |
|---|---|
| Left click unit | Select only that unit |
| Left click empty ground | Clear selection |
| Shift + left click unit | Toggle unit in/out of selection |
| Left drag | Rectangle select (Shift + drag adds to selection) |
| Right click ground | Move selected units into formation at that point |
| WASD / arrow keys | Pan camera |
| Mouse at screen edge | Pan camera |
| Mouse wheel | Zoom (distance 12–60) |

Clicks and drags that start on the HUD panel (top-left) are consumed by the UI and do not affect selection
or issue orders.

## What was built

### Runtime (`Assets/Ironfront/Scripts/Runtime`, assembly `Ironfront.Runtime`)

| File | Responsibility |
|---|---|
| `Core/SelectionSet.cs` | Pure generic selection set with change event |
| `Core/SelectionRules.cs` | Pure click / shift-toggle / box rules, drag threshold, screen-rect maths |
| `Core/FormationCalculator.cs` | Pure grid-formation slots and greedy unit→slot assignment |
| `Core/CameraMath.cs` | Pure edge-pan, zoom clamp, bounds clamp and orbit maths |
| `Input/RtsInput.cs` | Access to the `RTS` action map in the project-wide input actions; UI-hover check |
| `Units/SelectableUnit.cs` | Selection state, indicator toggle, registry of active units |
| `Units/UnitMover.cs` | NavMeshAgent wrapper: snaps destinations to the NavMesh, binds agent after load |
| `Selection/SelectionManager.cs` | Left-click / drag / shift handling, UI blocking |
| `Selection/SelectionBoxView.cs` | Draws the drag rectangle on the HUD canvas |
| `Commands/UnitCommandController.cs` | Right-click ground → formation move order |
| `CameraControl/RtsCameraController.cs` | Pan, edge pan, zoom, map bounds |
| `UI/SelectionHud.cs` | Selected-unit count and control hints |

Simulation logic (selection rules, formation, camera maths) is in plain static/generic classes with no
scene dependencies; MonoBehaviours only gather input and apply results.

### Input

An `RTS` action map was added to the existing project-wide `Assets/InputSystem_Actions.inputactions`
(the existing `Player` and `UI` maps are unchanged): `Pan` (WASD + arrows), `Zoom` (scroll Y), `Point`,
`Select` (LMB), `Command` (RMB), `Additive` (Shift).

### Editor tooling (`Assets/Ironfront/Scripts/Editor`)

- `RtsSandboxBuilder` — **Ironfront ▸ Build RTS Sandbox Scene** regenerates the scene, materials, unit
  prefab, layers (`Ground` = 8, `Units` = 9), bakes the NavMesh to an asset, saves the scene and puts it
  first in Build Settings. Nothing needs to be attached by hand.
- `IronfrontBuild` — **Ironfront ▸ Build Windows Player** builds to `Builds/Windows/Ironfront.exe`.

### Assets

- `Assets/Scenes/RTS_Sandbox.unity` (Build Settings index 0; `SampleScene` kept at index 1)
- `Assets/Ironfront/Prefabs/Unit.prefab` — capsule, NavMeshAgent, `SelectableUnit`, `UnitMover`,
  facing "nose", green `SelectionIndicator` disc
- `Assets/Ironfront/Navigation/RTS_Sandbox_NavMesh.asset` — baked NavMeshSurface data
- `Assets/Ironfront/Materials/*.mat` — ground, obstacle, selection ring, five unit colours

## Verification

### Automated — verified

| Check | Result |
|---|---|
| Compile (Editor) | No errors; no warnings from project code after fixing three deprecation warnings |
| EditMode tests (`Ironfront.Tests.EditMode`) | **36 / 36 passed** — selection rules, formation slots/assignment, camera maths |
| PlayMode tests (`Ironfront.Tests.PlayMode`, loads the real scene) | **5 / 5 passed** — scene contents and 5 units on NavMesh; click select + indicator + ground-click clear; shift toggle; rectangle select; move order around the central wall, with per-frame checks that no unit enters an obstacle footprint and final positions are within 5 m of the target and > 1 m apart |
| Scene in Build Settings | `Assets/Scenes/RTS_Sandbox.unity`, enabled, index 0 |
| Scene references | All serialized references in the saved scene resolved (camera, selection box, HUD label, NavMesh data, layer masks) |
| Windows build | **Succeeded**, 0 errors. 511 warnings, all from package shaders (`com.unity.ai.inference`/Sentis) and package config, none from project code |
| Player smoke test | Built player launched windowed, ran 15 s, log contains no errors, exceptions or NavMesh agent failures |

### Runtime input simulation in Play Mode — verified

Mouse and keyboard events were injected into the Input System during Play Mode, and results were read from
game state:

- Left click on a unit selected it and showed its ring; clicking another unit replaced the selection
- Shift+click added a unit and Shift+click on a selected unit removed it; Shift+click on empty ground kept the selection
- Click on empty ground cleared the selection
- Dragging showed the rectangle; releasing selected all five, then a smaller drag selected exactly two
- Click and drag starting on the HUD panel left the selection unchanged; a click just below the panel cleared it (positive control)
- Right-click on the HUD issued no order; right-click on ground moved all five units into a spread formation, and the selection persisted
- D and W keys panned the camera, and the pointer at the left edge panned left; panning stopped at the ±50 map bounds
- Scrolling zoomed in and out and clamped at 12 and 60

To inject input I had to change the Editor session: I swapped in a virtual mouse and set the Input System
background and editor focus behaviour to deliver input without Game-view focus. These were in-memory
settings only and were restored afterwards. The Pipeline package's `simulate_pointer` command could not be
used directly, because the native Editor mouse overwrites injected positions and its manual
`InputSystem.Update()` writes to the Editor-side state buffer.

### Requires a human tester

- How it feels with real hardware: pan speed, edge-pan border width, zoom step, click and drag responsiveness
- Real mouse-wheel behaviour: zoom moves one step per wheel event regardless of delta size, so high-resolution wheels and touchpads may zoom faster
- Interacting with the **built player**. The automated check covered launch and the log only; selection and movement in the `.exe` were not driven automatically
- Readability of unit colours and the selection ring at maximum zoom-out
- Edge panning in the Editor needs the Game view to have focus, which is expected Input System behaviour

## Requirement checklist

| # | Requirement | Status |
|---|---|---|
| 1 | Playable scene `RTS_Sandbox` saved | Verified |
| 2 | 100×100 flat ground, ≥ 5 obstacles | Verified (6 obstacles). Ground is a scaled plane mesh, not a Unity `Terrain` object |
| 3 | Angled overhead RTS camera | Verified (55° pitch) |
| 4 | WASD + screen-edge pan | Verified (simulated input) |
| 5 | Wheel zoom with limits | Verified (12–60, simulated input + tests) |
| 6 | Five distinguishable units | Verified (red, blue, yellow, purple, orange capsules) |
| 7 | Left-click selects single unit | Verified |
| 8 | Drag rectangle multi-select | Verified |
| 9 | Shift-click toggles | Verified |
| 10 | Visible selection indicator | Verified (green disc; visible in capture) |
| 11 | Right-click moves selection | Verified |
| 12 | Formation spread | Verified (grid slots, greedy assignment, > 1 m separation in test) |
| 13 | Pathing around static obstacles (AI Navigation) | Verified (NavMeshSurface + PlayMode obstacle-footprint test) |
| 14 | Empty click clears selection, right-click does not | Verified |
| 15 | UI interaction blocks selection | Verified (HUD panel, simulated input + positive control) |
| 16 | Everything saved in project | Verified (scripts, prefab, NavMesh asset, materials, scene, build settings) |

## Known limitations

- The ground is a flat 100×100 plane mesh rather than a Unity `Terrain` asset. This is simpler, and it gives raycasts and the NavMesh a guaranteed flat surface
- Rectangle selection tests each unit's pivot, not its full bounds
- The formation is a simple grid facing the direction of travel. Units can still jostle on arrival because of local avoidance
- There is no move-destination marker or other click feedback
- In a player build the pointer starts at (0,0) until the mouse moves, which can cause a brief edge-pan at startup (not observed in testing; theoretical)
- `SampleScene` from the project template is still in Build Settings at index 1, unused
