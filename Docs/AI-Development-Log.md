# Ironfront — AI Development Log

---

## Milestone 001 — RTS Sandbox (2026-10-10)

### Feature requested
The first playable RTS sandbox: scene `RTS_Sandbox`, a 100×100 flat map with ≥ 5 obstacles, an angled
RTS camera (WASD, edge pan, wheel zoom), five distinguishable units, click / shift-click / drag selection
with a visible indicator, right-click formation movement with NavMesh pathing, UI input blocking, automated
tests, a Windows build and documentation.

### Changes implemented
- Runtime assembly `Ironfront.Runtime` with pure logic (`SelectionSet`, `SelectionRules`,
  `FormationCalculator`, `CameraMath`) and small components (`SelectableUnit`, `UnitMover`,
  `SelectionManager`, `SelectionBoxView`, `UnitCommandController`, `RtsCameraController`, `SelectionHud`,
  `RtsInput`).
- `RTS` action map added to the existing project-wide `InputSystem_Actions.inputactions`; the existing maps are unchanged.
- Editor tools: `RtsSandboxBuilder` (menu **Ironfront ▸ Build RTS Sandbox Scene**) generates the scene,
  materials, unit prefab, `Ground`/`Units` layers and baked NavMesh. `IronfrontBuild` (menu **Ironfront ▸ Build Windows Player**).
- Scene `Assets/Scenes/RTS_Sandbox.unity` added at Build Settings index 0.
- Tests: `Ironfront.Tests.EditMode` (36 tests) and `Ironfront.Tests.PlayMode` (5 tests against the real scene).
- Git repository initialised with a Unity `.gitignore`. `Builds/`, `Library/`, `Temp/`, `Logs/`,
  `UserSettings/` and generated `.csproj`/`.sln*` files are ignored.
- Docs: `Docs/Milestone-001-Report.md` and this log.

### Tests attempted and results
- EditMode: **36/36 passed**.
- PlayMode: **5/5 passed**. This includes a move order around the central wall, with per-frame checks
  against obstacle footprints and checks of formation spacing.
- Simulated-input runtime checks in Play Mode passed: click, shift toggle, empty-ground clear, drag box,
  HUD blocking (with a positive control), right-click formation move, WASD, edge pan, zoom clamps. See the report for details.
- Windows player build: **succeeded**, 0 errors. All 511 warnings come from packages (`com.unity.ai.inference` shaders).
- Player smoke test: launched for 15 s, and the log is clean.
- Editor console: 0 errors/warnings from project code at completion.

### Problems encountered
1. **Editor hung on first contact.** The running Editor sat in "Compiling Scripts" for ~20 min with no CPU
   activity, and every main-thread command timed out. With the director's approval I force-closed and
   relaunched it, and it compiled normally afterwards.
2. **Input System errors on reload** ("Action/Map/Binding count in old and new state must be the same",
   4 errors) appeared once, when the actions asset changed while the Editor was down. They did not recur
   after the console was cleared and through later compiles and play sessions.
3. **Pipeline `eval` has a 5 s main-thread response limit.** The scene builder takes longer, so the call
   reported a timeout even though the build completed. I verified completion through the console log and the saved assets.
4. **Input simulation in the Editor.** The Pipeline `simulate_pointer` events did not reach game code,
   because the native Editor mouse overwrites injected positions and the command's manual
   `InputSystem.Update()` processes into the Editor-side state buffer. As a workaround I injected
   queue-only state events through a temporary virtual mouse, with the background and editor input
   behaviour relaxed in memory. Everything was restored afterwards.
5. **Bug found by the build smoke test:** in the player, each NavMeshAgent was enabled during scene load
   before the NavMeshSurface had registered its data ("Failed to create agent because there is no valid
   NavMesh" ×5), so units would not have moved in the build. Editor Play Mode and the PlayMode tests did not
   catch this. Fix: the unit prefab ships with its agent disabled, and `UnitMover.Start` enables and binds it,
   logging a warning if placement fails. The rebuilt player log is clean.
6. Three deprecation warnings (`FindFirstObjectByType`, `StaticEditorFlags.NavigationStatic`) were fixed.

### Human interventions required
- Approval to force-restart the hung Unity Editor (granted).

### Known limitations
- The ground is a scaled plane mesh, not a Unity `Terrain` object.
- Interaction in the built `.exe` (selection, movement) was not driven automatically; only launch and the log were checked.
- Zoom moves one fixed step per wheel event, regardless of delta size.
- Rectangle selection tests unit pivots only. There is no move-destination marker.
- `Assets/_ClaudeProbe/ClaudeProbe.cs` (a pre-existing connectivity probe) and the template `SampleScene` were left in place.
- `Assets/Editor/HubForceResolve.cs`, a Unity Hub bootstrap script, deleted itself during the Editor restart, as it was designed to.

### Relevant commit hash
Milestone commit: `0424e3b` (0424e3b5823144897c477a779d42d9e000bc9f3b)

---

## Milestone 002 — Economy, Construction and Production (2026-10-10)

### Feature requested
A single resource (Ore) with finite deposits and automated harvesters delivering to a refinery, credits in
the HUD that can never go negative, construction (Headquarters, Refinery, Power Plant, Vehicle Factory) from
a menu with a validity preview, and Vehicle Factory production of harvesters and tanks with costs, build
times, a visible queue, valid spawning and rally points. Required architecture: data-driven definitions,
independently testable rules, faction-agnostic state and event-driven HUD updates. No combat or AI.

### Changes implemented
- **Pure rules** (`Core/Economy`, `Core/Production`, `Core/Construction`, `Core/Harvesting`): `ResourceWallet`,
  `PowerGrid`, `StructureRoster`, `FactionState`, `ConstructionRules`, `ProductionQueue`, `SpawnLocator`,
  `PlacementRules`, `HarvesterStateMachine`.
- **Data**: `BuildingDefinition`, `UnitDefinition`, `HarvesterDefinition` and `MatchSetup` ScriptableObjects,
  with generated assets in `Assets/Ironfront/Data`.
- **Components**: `MatchController`, `FactionMember`, `Building`, `Refinery`, `VehicleFactory`, `OreDeposit`,
  `Harvester`, `BuildingPlacementController`, `EconomyHud` (sidebar), `RtsInputMode`.
- **Reused and extended Milestone 001 systems**: `SelectionManager` (structure selection, faction filter,
  placement arbitration), `UnitCommandController` (harvest orders, rally points, move interrupts harvesting),
  `UnitMover` (buffers orders given before Start), `SelectionHud` (structure/harvester detail), and an
  Escape `Cancel` action in the RTS input map.
- **Editor**: the new `EconomyContentBuilder` generates definitions, 4 structure prefabs, harvester and tank
  prefabs, the ore deposit prefab and the starting base. `RtsSandboxBuilder` adds `Structures`/`Resources`
  layers, Match/placement systems and the sidebar, and excludes structures/deposits from the bake (they carve at runtime).
- **Tests**: 46 new EditMode tests and 8 new PlayMode tests; the 5 Milestone 001 PlayMode tests were adapted to the extra harvester.
- **Docs**: `Docs/Milestone-002-Report.md`.

### Tests attempted and results
- EditMode: **82/82 passed**.
- PlayMode: **13/13 passed**, including the automated acceptance scenario (harvest → Power Plant + Factory → two tanks → rally → select → move).
- The acceptance scenario through the real UI with simulated input in Play Mode passed every step (see the report). There were 0 game warnings or errors.
- Milestone 001 regression: click/box selection, move orders, panning and zoom were re-checked through simulated input, and the 5 Milestone 001 PlayMode tests pass.
- Windows build: **succeeded**, 0 errors, no project-code warnings. Player smoke test (20 s): clean log.

### Problems encountered
1. **Lost ScriptableObject reference in the generated scene.** `EditorSceneManager.NewScene` unloads unreferenced assets, so the `MatchSetup` reference cached before `NewScene` was dead when assigned. The scene saved `setup: {fileID: 0}` and all 12 PlayMode tests failed with "MatchController has no MatchSetup". Fix: `EconomyContentBuilder.Load()` reloads all generated content from asset paths after `NewScene`.
2. **Bash tool heredocs** containing apostrophes failed to parse. Patch scripts were written to files and run instead (tooling only).
3. **Input harness flakiness:** after a Game-view capture, two injected placement clicks arrived with the pointer at (0,0) and were ignored. Without captures between hover and click the steps worked every time. This was a harness artefact, not a game-code path.
4. **HUD label bug** found during the UI run: the production label showed "VehicleFactory(Clone)", because the HUD bound before the instance was renamed. It now uses the definition's display name.
5. A player build added the Input Actions asset to `PlayerSettings.preloadedAssets` (expected Input System behaviour); committed as is.

### Human interventions required
- None.

### Known limitations
- Construction is instant (no build time). Footprints are axis-aligned with no rotation.
- Navigation protection is rule-based (clearance, aprons, NavMesh check at the site), not a full path check.
- All units use the default NavMesh agent type (radius 0.5). Vehicles (radius 0.8) can clip corners slightly.
- Production cost is paid in full when queued; cancel refunds it in full.
- One player faction is configured; there is no AI or combat (out of scope).
- Interactive play of the built `.exe` was not automated; only launch and the log were checked.

### Relevant commit hash
Milestone commit: `26a945c` (26a945cfb15f6ce37fb1e9d203727ace1416728f)

---

## Milestone 003 — Combat System (2026-10-10)

### Feature requested
Basic RTS combat integrated with movement, production, construction, economy and selection:
- Data-driven Light and Heavy Tanks produced at the factory.
- Reusable health/armour/damage for units and buildings, with friendly-fire prevention, health bars and full destruction cleanup.
- Weapons with range, cooldown, projectiles, line of sight and target (re)acquisition.
- Move / Attack / Attack-Move / Stop / Hold orders with jitter-free chasing and no permanent sticking.
- An enemy test faction (stationary and moving units plus a destructible building) — no strategic AI.
- Combat UI: unit info, order buttons and shortcuts, target feedback, current order.

### Changes implemented
- **Pure rules** (`Core/Combat`): `HealthModel`, `DamageRules`, `WeaponCooldown`, `TargetSelector`, `CombatOrderMachine` (with `ICombatBody`/`ICombatTarget` seams and tuning in `CombatSettings`).
- **Data**: the new `WeaponDefinition` and `CombatVisuals`. `UnitDefinition` gained health, armour, speed and weapon; `BuildingDefinition` gained health and armour; `MatchSetup` gained the enemy faction and colours.
- **Components**: `Health`, `HealthBar`, `CombatUnit`, `Projectile`, `CombatFx`/`TransientEffect`, `UnitIdentity`, `FactionColorizer`, plus the test-scenario helpers `PatrolRoute` and `ScenarioStartOrder`.
- **Orders/UI**:
  - `UnitCommandController` extended: right-click attack; armed units take move/formation orders through `CombatUnit`; `IssueAttack`/`IssueAttackMove`/`IssueStop`/`IssueHold` and order events.
  - `OrderInputController` (F/X/H and targeting mode) and `RtsInputMode.IsTargeting`.
  - `OrdersHud` (buttons and status), `OrderFeedback` (ground markers and target rings) and an extended `SelectionHud`.
  - `SelectionManager` drops dying objects immediately.
- **Content**: the new `CombatContentBuilder` (weapons, Light/Heavy Tank prefabs, Enemy Outpost, visuals, enemy scenario). `EconomyContentBuilder` adds Health, HealthBar, UnitIdentity and FactionColorizer to every unit/structure prefab, and the capsule scouts got Health.
- **Removed**: the Milestone 002 `Tank` definition and prefab, replaced by Light/Heavy Tank (no remaining references).
- **Tests**: 29 new EditMode and 11 new PlayMode tests. The Milestone 002 acceptance test now produces a Light and a Heavy Tank.
- **Docs**: `Docs/Milestone-003-Report.md`.

### Tests attempted and results
- EditMode: **111/111 passed**.
- PlayMode: **25/25 passed** (all Milestone 001/002 tests plus the combat scenarios: single and multi-unit, building destruction, attack-move, move, hold, stop, line of sight, moving target and reacquisition, selected-unit destruction cleanup).
- Acceptance scenario through the real UI with simulated input: every step passed, with 0 game warnings or errors (see report).
- Windows build succeeded; the player smoke test passed (clean log, window screenshot shows correct rendering and rising credits).

### Problems encountered
1. **Colour assertion** compared a property-block colour exactly and failed on float precision (values identical to 3 decimals). Changed to a tolerance comparison.
2. **Runtime-spawned NavMeshObstacles** carve only after their stationary delay (0.5 s), so the building test waits before asserting the carve.
3. **Editor lost OS focus** during the UI run; injected input stopped reaching the game, and `editor_focus` / SetForegroundWindow couldn't take focus back. Setting `Application.runInBackground = true` for the session fixed it; it was restored afterwards.
4. **Camera/pointer off-screen**: a placement click didn't land because the site was off-screen from the starting camera. Solved by panning the camera there first.
5. An Attack-Move leash check could have queried a destroyed target's distance. Caught in review before running: validity is now checked first.
6. **Compile polling**: per the brief, I polled the non-main-thread `console_status` endpoint every 10 s while compiling instead of repeatedly calling main-thread APIs.

### Human interventions required
- None.

### Known limitations
- Projectiles are homing with no misses or splash; line of sight is checked at the moment of firing.
- No enemy AI (test scenario only). Unarmed units don't react to attacks.
- Unreachable-target detection is a 4 s no-progress timer, which could also trigger for a unit jammed in a crowd.
- Interactive combat in the built `.exe` was not automated.
- Placeholder primitive visuals; the sidebar covers part of the view at 1280×720.

### Relevant commit hash
Milestone commit: see the "Commit" line below (recorded after committing).
