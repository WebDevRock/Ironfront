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
Milestone commit: `7f7f70b` (7f7f70bdbaf9ae5341a957ebfb558034a10dd893)

---

## Milestone 004 — AI Enemy Commander (2026-10-10)

### Feature requested
An autonomous enemy commander and a complete single-player match:
- Two factions with independent economies. The enemy harvests, builds (same costs and placement rules), produces Light/Heavy Tanks, defends, assembles and attacks.
- State-machine strategy with configurable Normal difficulty and no cheats, plus vision-limited knowledge.
- Victory/defeat/draw with restart, and an optional AI debug panel with bounded logs.
- EditMode/PlayMode tests and AI-vs-AI simulation.

### Changes implemented
- **Pure rules**: `Core/AI` (`CommanderModel`, `StrategyRules`, `EconomyPlanner`, `CompositionRules`, `TargetPriorities`, `SiteSearch`, `BoundedLog`) and `Core/Victory/MatchOutcomeRules` (HQ victory, same-frame draw, exhaustion draw).
- **Commander**: `AI/EnemyCommander` and `AI/AiPerception`, with tuning in `Data/AiDifficulty` (asset `Data/AI/Normal.asset`). It acts only through `BuildingPlacementController.Validate/TryPlace`, `VehicleFactory.Enqueue` and `CombatUnit` orders.
- **Match**: `MatchFlowController`, `MatchResultView`, `FactionStartLocation`, `FactionSpawner`, `CommanderDebugPanel` (F9), and a new `RTS_Match` scene built by `MatchSceneBuilder` (menu **Ironfront ▸ Build All Scenes**), with a point-symmetric layout and the enemy starting with 2000 credits.
- **Shared-system changes**:
  - Buildings can face north/south (footprint, apron, front point, factory exit and rally follow the rotation).
  - Owners are set before `OnEnable` (`FactionSpawner`).
  - Computer factions build without HUD messages.
  - Added `RtsInputMode.ResetAll`, a `UnitMover.PathRequests` counter and the F9 input action.
  - `RtsSandboxBuilder` steps were made reusable; sandbox output is unchanged.
- **Tests**: 31 new EditMode and 10 new PlayMode tests (including AI-vs-AI).
- **Docs**: `Docs/Milestone-004-Report.md`.

### Tests attempted and results
- EditMode **142/142**, PlayMode **35/35** (all Milestone 001–003 tests included). No console warnings or errors.
- AI-vs-AI simulations at 10×: with the final code, 7 runs gave 4 faction-1 wins and 3 faction-2 wins, all decisive, in 6–12 game minutes. Path requests stayed at about 0.1 per unit-second.
- Full match through the real UI in Play Mode: the AI built, assembled, attacked and destroyed the player HQ at 3:57; the Defeat screen appeared; the Restart button gave a fresh match.
- Windows build succeeded. A player window capture shows correct rendering and no cross-faction HUD binding.

### Problems encountered
1. **`GetInstanceID()` is a compile error (obsolete) in this Unity version.** Replaced with commander-assigned unit keys.
2. **Float edge case in the composition rule** (exactly 40 % heavies compared as below 40 % at higher intermediate precision). Fixed with a tolerance; caught by an EditMode test.
3. **Stalemates in AI-vs-AI** (2/3 early runs reached the time limit): all ore was mined out and both armies waited below the attack threshold forever. Added end-game rules (commit everything, no retreat once no ore is left) and an exhaustion draw for true dead ends. The simulation test now requires an outcome.
4. **Systematic faction-2 wins** (4/4 decisive runs early on). I found and fixed three asymmetries in my own code:
   - (a) The enemy dock's distance to its secondary ore sat on the other side of the 25 m "local ore" threshold, so only the enemy expanded.
   - (b) All buildings faced +Z, so the enemy's docks and exits faced away from the fight. Buildings now face the opponent.
   - (c) `SiteSearch` broke ties by world x/z, which point reflection doesn't preserve, so the two bases were laid out differently. Ties are now broken relative to the direction.

   Each fix was verified with tests and further simulations.
5. **Cross-faction ownership bug** (found from a screenshot of the built game): units and buildings had their owner assigned after `Instantiate`, so `OnEnable` handlers saw the default player faction. The player's sidebar bound the enemy's factory, which would have let the player queue units charged to the enemy. Fixed with `FactionSpawner` (owner set before activation) plus a HUD ownership re-check, with a regression test.
6. **Window capture showed the Editor instead of the player** (screen-region capture of a covered window). Switched to `PrintWindow`.
7. **Bash heredocs with apostrophes failed again**; patch scripts were written to files.

### Human interventions required
- None.

### Known limitations
- Single Normal difficulty with no in-game selector. The AI knows the enemy start location by configuration.
- Rule-based, predictable waves: no scouting, flanking, harvester harassment or retreat micro. Defence only reacts to armed enemies near its structures.
- The player's placement tool always builds facing north (no rotate key).
- Ore runs out around minute 5–6, so matches are short.
- Balance is backed by 7 final simulations only.
- No complete human-played victory through the UI yet (victory is covered by an automated test).

### Relevant commit hash
Milestone commit: `105ec38` (105ec383bbaefdfe7fa0a117a75f86603a1072e2)

## Milestone 005 — Gameplay Quality, User Experience and Performance (2026-10-10)

### Feature requested
Audit the project, then make it more reliable, responsive and readable, and measure performance:
- **Audit:** findings ranked by severity, player impact and effort.
- **Gameplay:** reliable commands, sensible combat, responsive controls, clear feedback.
- **UI:**
  - minimap;
  - selection and unit info;
  - queue progress and cancellation;
  - placement feedback;
  - notifications;
  - damage feedback;
  - tooltips and shortcuts;
  - victory/defeat presentation;
  - a consistent style.
- **Performance:** profile at 25/50/100/200 units and optimise proven bottlenecks only.
- **Regression:** a full regression pass, plus an AI-vs-AI balance re-run with multiple seeds and swapped starts.
- **Constraints:** no new factions, multiplayer or campaign content.

### Changes implemented
- **Audit and measurement:**
  - `Docs/Milestone-005-Audit.md` (21 findings).
  - `Diagnostics/PerfMarkers`, `BenchmarkRunner` and `AllocProbe`.
  - `RTS_Benchmark` scene and a `-ironfrontBenchmark` player switch.
- **Gameplay:**
  - Retaliation (`CombatOrderMachine.NotifyAttacked`, leashed).
  - Control groups with double-tap camera focus.
  - Double-click to select all of a type.
  - Formation spacing by unit size.
  - `UnitMover` fallback search for destinations inside footprints.
  - `EnemyCommander`: stuck-attacker give-up plus an optional variation seed.
- **UI:**
  - Minimap.
  - Notification centre with Space jump.
  - Tooltips, F1 help and placement hint.
  - Queue % and seconds remaining.
  - Health-bar hit flash.
  - `MatchStatisticsTracker` and result-screen statistics.
  - `UiTheme`, with the HUD scaling with screen size.
- **Performance:**
  - `FxPool`: projectiles and effects are pooled.
  - Allocation-free target search with a pre-filter and staggered scans.
  - Cached input-action names; throttle checked before formatting notifications.
- **Scenes:**
  - All rebuilt by `MatchSceneBuilder.BuildAll()`.
  - New test-only `RTS_Match_Swapped`, not in Build Settings.
  - `RTS_Benchmark` added last in Build Settings.
- **Tests:**
  - 20 EditMode tests.
  - 7 PlayMode tests plus statistics assertions in the victory test.
  - The `[Explicit]` balance study.
- **Docs:** `Docs/Milestone-005-Report.md`.

### Tests attempted and results
- EditMode **162/162**, PlayMode **42/42**, which includes all Milestone 001–004 tests.
- Release benchmark: 3 runs of the final build compared against the baseline. Average frame times are unchanged (2.22 → 2.15 ms at 200-unit combat) and GC collections halved (4 → 2). One run had two isolated stalls (1073 ms and 103 ms) that the other two runs did not reproduce.
- Development player: 0.05–0.23 KB/frame GC allocation.
- Balance study on the final code: 20 matches (10 seeds × 2 layouts), all decisive.
  - By faction: F1 won 7, F2 won 13.
  - By start: north-east won 13, south-west won 7.
  - With n = 20 this is not significant (p ≈ 0.26) and is not claimed as balance evidence.
- Release Windows build: 0 errors, and the player ran with no errors in its log.
- Screenshots confirmed that the HUD, minimap, notifications and help overlay render. F1 and Space were verified with a simulated keyboard in the Editor.

### Problems encountered
1. **Four MonoBehaviours in one file (`Tooltips.cs`)** compiled and worked when added at runtime, but the builder-added components were silently missing from saved scenes. The tooltip test passed alone and failed in the full suite. Split into one class per file.
2. **First "after" release benchmark showed 3–6× more GC collections.** The cause was 0.55 KB/frame of control-group polling (`"Group" + n` strings). Names are now cached and the result was re-measured.
3. **Balance study timeouts (2 of 20).** A state dump on timeout showed an AI attacker idle forever. `UnitMover` dropped destinations deep inside building footprints, and the commander re-ordered the idle unit indefinitely. Fixed both; a regression test was checked to fail without the fix.
4. **The unused `com.unity.ai.assistant` relay process crashed mid-study** and its logged error aborted the test run. The study now ignores unrelated logged errors.
5. **Tooling issues:**
   - `[Explicit]` tests need `run_tests --include_explicit true`.
   - `test_status` reports the previous run as completed right after a new launch.
   - WScript SendKeys doesn't reach the player's Input System.
   - PrintWindow flag 3 gives a blank image (use 2, sized to the window rect).
   - Editor Play Mode doesn't tick when unfocused unless `runInBackground` is set.
6. **Earlier in the milestone:**
   - A pooled projectile could keep flying when reused (in-flight guard added).
   - Collider bounds weren't valid at `Awake` (lazy bounding radius).
   - The killing blow lost the attacker (attacker is now recorded before damage is applied).

### Human interventions required
- None.
- Pending decision: approve removal of the unused AI packages (audit #18).

### Known limitations
- Needs a human playtest for feel, readability, pacing, hitching on slower hardware and balance.
- The balance sample of 20 can't separate a faction-order effect from a position effect.
- No fog of war, so the minimap shows all units.
- Tooltips on HUD buttons only.
- Keyboard shortcuts were verified in the Editor, not in the built player.
- Audit items 15–17 left by design.

### Relevant commit hash
Milestone commit: `1ad2601` (1ad2601d67dd11fb3971b3636fffef8950c8ec84)


## Milestone 006 — Visual Identity, Art Direction, Audio and Presentation (2026-10-11)

### Feature requested
Turn the functional prototype into a visually coherent game, across eight phases:
1. Visual audit and art-direction document.
2. Distinctive models for the Light Tank, Heavy Tank, Harvester, HQ, Refinery, Power Plant and Vehicle Factory, with faction colour variants and gameplay kept separate.
3. Terrain and environment that don't affect navigation.
4. Combat effects and animation, pooled and event-driven.
5. Audio, appropriately licensed, with volume controls and overlap limiting.
6. UI consistency, including a main menu and the result screens.
7. Screenshots, before/after comparison and Windows performance against M005.
8. Full regression, including a complete automated match.

Deliverables: art direction, asset inventory and licences, report, log, commit.

### Changes implemented
- **Art tooling (Editor/Art):**
  - `MeshKit` (procedural hard-surface modeller), `ArtLibrary` (procedural textures and materials);
  - `VehicleModels`, `StructureModels`;
  - `EnvironmentBuilder` (painted ground, backdrop, styled obstacles with unchanged colliders, collider-free props, lighting and post-processing, shadow distance 100 m);
  - `UiArtBuilder` (9-slice sprites, glyphs, emblem, model icons rendered in a preview scene), `UiBuild`;
  - `MainMenuBuilder`, `AudioContentBuilder`, `ArtPreview`.
- **Runtime visuals (`Visuals/`):**
  - `FxLibrary`: shared world-space particle systems, Emit-only, with view culling and budgets;
  - `WeaponFx`, `VehicleMotionFx` (shared moving-tread material), `DamageFx`, `DestructionFx` and `WreckBurner`;
  - `ConstructionFx`, `StructureAnimator`, `HarvesterFx`, `OreDepositFx`, `StaticTint`;
  - `VisualFeatures` (command-line A/B switches);
  - `FactionColorizer` now also tints emissive renderers; projectiles get tracers.
- **Audio:**
  - `Core/Audio/SfxSynth` (33 deterministic synthesised clips plus WAV encoder), `VoiceLimiter`, `AudioMix`;
  - `Audio/GameAudio` (screen-positioned one-shots, aggregated engine loop, ambience), `SoundBank`, `AudioEventRouter`, `UI/UiSound`.
- **UI:**
  - `UiTheme` palette, `UiSkin` and `UiSkinProvider`;
  - EconomyHud icons, glyph rows and power meter; SelectionHud portrait;
  - `GameMenu` (F10 or MENU button), `AudioOptionsPanel`, `MainMenuController`;
  - restyled result screen with a Main menu button;
  - new `GameMenu` input action (F10).
- **Data:** an `icon` sprite on `UnitDefinition` and `BuildingDefinition`; particle material slots on `CombatVisuals`.
- **Scenes:** new `MainMenu`, first in Build Settings. Match, Match_Swapped, Sandbox and Benchmark rebuilt by `MatchSceneBuilder.BuildAll()`.
- **Diagnostics:**
  - `ShowcaseRunner` (`-ironfrontShowcase`) for reproducible screenshots;
  - `IronfrontBuild.BuildWindows(path)`, `RtsCameraController.SetDistance`;
  - the runners force `runInBackground` on command-line runs.
- **Gameplay code touched:** only `CombatUnit.TryFire`, which dropped its direct muzzle-flash call because `WeaponFx` uses the existing `Fired` event. Rules, values, colliders, NavMesh agents and obstacles are unchanged.
- **Docs:**
  - `Docs/Milestone-006-Art-Direction.md`, `Docs/Milestone-006-Asset-Inventory.md`, `Docs/Milestone-006-Report.md`;
  - `Docs/Screenshots/M006/{before,after,compare}`, `Docs/Performance/M006`.

### Tests attempted and results
- EditMode **181/181** (+19) and PlayMode **49/49** (+7), on the final assets. One earlier full PlayMode run was 48/49: the unused AI Assistant relay logged an error during a SetUp. Re-run: fixture 12/12, full suite 49/49.
- **Complete AI-vs-AI matches:** the balance study, seed 37, both layouts, all effects and audio on. Both decisive: 391 s and 502 s of game time, no timeouts.
- **Release benchmark,** 3 runs each, before (M005 content) vs final:
  - 200-unit combat 2.28 → 3.00 ms (439 → 333 fps); 25-unit idle 1.04 → 1.25 ms;
  - p99 under 6 ms everywhere; about +30–50 MB memory;
  - one isolated 555 ms stall in one run (unconfirmed cause); runs 1–2 had elevated GC counts that did not reproduce;
  - draw calls lower at 200 units (647 → 530).
- **NavMesh:** polygon data byte-identical to M005; only 4 tile source hashes differ.
- **Screenshots:** 10 shots of the final build reviewed visually; 7 before/after pairs.

### Problems encountered
- **Self-inflicted bugs, fixed:**
  - `OreDepositFx`'s `[RequireComponent]` added a duplicate `OreDeposit`; caught by 3 PlayMode tests.
  - `Mathf.SmoothStep` misused as a mask, so the ground masks were empty; caught by reviewing renders.
  - The emission keyword was lost on material creation.
  - Faction caps covered the cooling-tower openings.
  - Turret details read as faces.
  - The sidebar glyph layout wrapped, and the victory statistics overlapped.
- **Performance regression, fixed:** per-frame tread property blocks cost about +4.4 ms render thread at 200 tanks. Found with the A/B switches, fixed with a shared scrolled material.
- **Tooling:**
  - PlayMode tests must run with `--async_tests`;
  - `delayCall` from eval never fired;
  - one baseline benchmark hung because `runInBackground` was off;
  - Python edits changed line endings in mixed-ending files (restored per file);
  - one of my own new files was truncated by an encoding error and rewritten.

### Human interventions required
- **None.** I made the art, audio and tool choices myself. Procedural generation meant no licences or purchases were needed.
- **Still pending from M005:** approval to remove the unused AI Assistant and Inference packages, which cause the "1 errors" line in every build summary.

### Known limitations
- Stylised programmer art: no UV-unwrapped textures, normal maps or AO. Factions differ by livery only. Animation is procedural transforms only.
- Synthesised audio with radio-chirp acknowledgements: no voice acting, no music.
- Built-in LegacyRuntime font.
- About 27 MB of text-YAML mesh assets (deterministic).
- Checked only at 1920×1080 and 1280×720 on one machine. The look, the mix and readability still need human judgement; see the report.

### Relevant commit hash
Milestone commit: `ab902ce` (ab902ce534dff177d362d1513de45ab63f99be67)

## Milestone 007 — Content Expansion, Technology and Strategic Depth (2026-10-11)

### Feature requested
Add strategic depth without new factions, air, naval or campaign:
1. Audit and design document with stats, costs, prerequisites and counters.
2. Rocket Artillery (long range, minimum range, siege) and Recon Vehicle (fast, extended sight).
3. Guard Tower (cheap, anti-light) and Anti-Armour Turret (power-dependent, anti-heavy).
4. Research Centre with 3+ upgrades: costs and time, no double purchase, applies to existing and new units, AI uses it, no leaks between matches.
5. Fog of war: unexplored, explored and visible states; enemies visible and targetable only when revealed; minimap respects it; no AI omniscience; performant; trade-offs documented.
6. AI that scouts, builds defences, researches, uses the new units and counters artillery, on the same architecture.
7. Multi-seed, swapped-start balance simulations that look at viability, not just 50/50, plus performance tests.
8. Player-facing icons, tooltips, progress, range indicators and readable fog.

Deliverables: playable game, tests, balance and performance analysis, report, log, commit.

### Changes implemented
- **Pure core:**
  - `Core/Vision/VisibilityGrid`, `Core/AI/ScoutPlanner`, `Core/Research/ResearchState`;
  - `Core/Combat/WeaponClassRules` (armour classes, splash, firing band);
  - `Core/AI/ExpansionRules` (recon, defences, tech, research choice);
  - `CombatOrderMachine` gains minimum range with back-off, sight awareness, and seeking last-known positions;
  - composition saving for heavies and artillery, end-game spending rules;
  - `HarvesterStateMachine.SinkLost`.
- **Data and content (`ExpansionContentBuilder`, `ExpansionModels`):**
  - new definitions, prefabs, icons and sounds for Rocket Artillery, Recon, Guard Tower, AT Turret, Research Centre and three `ResearchDefinition`s;
  - new data fields: weapon min range, splash, salvo, ballistic, class multipliers; unit sight, armour class, required structure; structure weapon and needs-power.
- **Runtime:**
  - `Vision/` (`VisionSystem`, `VisionEntity`, `StructureGhost`, `FogOfWarView`, `FogOfWar.shader` as a URP full-screen pass with soft edges);
  - `DefenceTurret`, `ResearchCentre`, `RangeIndicator`;
  - ballistic and splash `Projectile`; fog-aware targeting, selection, health bars, effects, audio, minimap and notifications;
  - RESEARCH sidebar section and extended tooltips.
- **AI (`EnemyCommander`, `AiPerception`):** vision-limited perception with remembered structures, scouting, defence and tech building, research, artillery stand-off, counter-artillery, scouts joining the end-game attack, research chosen after the snapshot counts.
- **Map (`MatchSceneBuilder`):** home ore 9000 + 6000 per side (was 6000 + 4000), plus a contested 5000 + 5000 pair at the central wall.
- **Tools:**
  - balance study records economy, composition, damage and research (JSONL);
  - `-fxNoFog` profiling switch;
  - `IronfrontBuild.BuildWindows(path, development)`;
  - showcase shots 08–10.
- **Docs:** `Docs/Milestone-007-Design.md`, `Docs/Milestone-007-Report.md`, `Docs/Balance/M007/`, `Docs/Performance/M007/`, `Docs/Screenshots/M007/`.

### Tests attempted and results
- EditMode **210/210** (+29) and PlayMode **57/57** (+8) on the final code.
- **Balance study:** 5 runs of 10 AI-vs-AI matches.
  - Final: 5 decisive, 5 exhaustion draws, 0 timeouts; south-west/north-east wins 2/3 (baseline 2/8).
  - Every new unit, defence and two of the three topics are used in most matches; artillery is the most cost-efficient damage source.
  - Details in the report, §3.
- **Release benchmark:** M007 vs an M006 control build of `9f7dcd9`, interleaved in one session. Within 0.2 ms everywhere: 200-unit combat 3.14 → 3.32 ms, p99 under 6 ms. Fog cost is in the noise; `Vision.Tick` is about 0.05 ms per frame.
- **Screenshots:** 13 showcase shots of the final build reviewed.

### Problems encountered
- **Fog vs earlier assumptions:** attack orders on hidden targets were dropped and two M003 tests failed. Fixed by seeking the last known position. Two presentation tests also needed fog-aware setups: a visible death, and a vision tick on spawn.
- **Economy too small for the tech tier:** run 1 had no research and no artillery. Ore was raised.
- **End-game deadlock:** factions saved forever, or kept an idle scout, so matches timed out at 1500 s. Fixed with end-game rules.
- **Research-choice bug:** research was chosen before harvesters were counted, so Advanced Harvesting was never researched. Found from balance data.
- **Latent harvester crash:** a refinery destroyed while a harvester was heading to it caused a Unity fake-null in pure code. Fixed.
- **Benchmark noise:** a first non-interleaved benchmark set suggested a +50 % regression. An M006 control build, interleaved, showed it was machine load. Lesson: always interleave.
- **Tooling:**
  - the URP renderer feature map gained a stray character twice when serialised; fixed by hand and reimported;
  - `run_tests --filter` needs the fully qualified class name;
  - `Until()` waits use scaled time.

### Human interventions required
- **None during the work.**
- **Decisions requested:**
  1. AI-mirror exhaustion draws (about 50 %): ore regrowth, or a tiebreak rule?
  2. Approval to remove the unused AI Assistant/Inference packages (pending since M005).

### Known limitations
- Sight is not occluded by walls; visibility uses a 1 m grid at 10 Hz; remembered structures don't show health.
- Enhanced Weapons is rarely reached by the AI.
- Mirror AI matches often end in exhaustion draws.
- The longer economy changes match pacing (about 600 s).
- Fog readability, artillery feel, tooltips and AI fun still need a human playthrough; see the report, §5.

### Relevant commit hash
Milestone commit: `267ecfa` (267ecfab7218ca9d3bc3d128d413394353a8b61b)

## Milestone 008 — Multiple Maps, Skirmish Configuration and Difficulty (2026-10-11 to 2026-10-12)

### Feature requested
Turn the game into a configurable single-player skirmish: architecture review and design doc; four distinct maps
(Dustfall Plains, Iron Pass, Green Divide, Blackstone Basin); an automated map validator; a skirmish setup menu
(map, difficulty, starting resources, start location, victory condition); Easy/Normal/Hard that differ in behaviour
only; robust match initialisation, restart and return to menu; an additional victory mode; AI adaptation to terrain;
tests and multi-seed AI-vs-AI studies; performance and screenshots. **Addendum (mid-milestone):** much larger maps
(600/400/600/400), six to eight validated spawn locations with rule-based, seeded pair selection, no automatic
knowledge of enemy starts (both sides must scout), distributed resources, AI exploration behaviour, pacing review,
and extended verification.

### Changes implemented
- **Pure core:** `Core/Skirmish` (`SkirmishSettings`, `SpawnTable`/`SpawnSelection`/`SpawnPairRules`), `Core/Maps/MapValidation`
  (`IMapProbe`, `MapSpec`, `MapValidator`), `Core/AI/EnemyIntel`; `MatchOutcomeRules.EvaluateAnnihilation`/last stand;
  `CommanderSettings` behaviour switches, Easy/Hard presets, failed-attack escalation, scout throttle, `ScaledForMap`;
  composition/strategy/expansion rules use them; `ScoutPlanner` skip filter.
- **Runtime:** `Skirmish/` (`SkirmishSession`, `SkirmishInitializer`, `StartSlot`, `NavMeshMapProbe`, `SceneMapValidation`);
  `MapDefinition`, `MapCatalog`; `MatchController` starting-credit override; `MatchFlowController` victory modes;
  `VisionSystem` last-stand reveal; `EnemyCommander` intelligence, route-aware rally/defence/attack, path-ranked and
  explored-only expansion, ore-first scouting, unreachable-point memory, factory-based play after HQ loss in Annihilation;
  `SkirmishSetupPanel` + `MainMenuController`; minimap colours; `BuildingPlacementController.ValidateTerrain`;
  `MapBenchmarkRunner` (`-ironfrontMapBenchmark`, `-benchmarkShots`).
- **Editor:** `SkirmishMapLayouts` (seeded symmetric generators), `SkirmishMapBuilder` (scenes, difficulties, catalog,
  previews, two-pass validation storing the spawn table), `MapPalette` + `TerrainStyles` (ridges, mesas, dunes, forests,
  stacks, pipelines, ruins, tank farms, slag), palette-aware `EnvironmentBuilder` (M006 look unchanged by default),
  setup screen in `MainMenuBuilder`.
- **Content:** 4 map scenes in `Assets/Scenes/Maps/`, Easy/Normal/Hard assets, 4 map definitions and previews, catalog.
- **Docs:** `Docs/Milestone-008-Design.md`, `Docs/Milestone-008-Report.md`, `Docs/Balance/M008/`, `Docs/Performance/M008/`,
  `Docs/Screenshots/M008/`.

### Tests attempted and results
- EditMode **242/242** (+32 in `Milestone008RulesTests`, incl. synthetic broken maps), PlayMode **64/64** (+7 in
  `Milestone008PlayModeTests` in the real map scenes). Map validator: all four maps pass.
- AI-vs-AI (explicit `SkirmishStudyPlayModeTests`), 28 matches, 4 maps, 27 distinct seeded start pairs: final
  27 decisive / 1 timeout (Green Divide), Hard beat Normal 8/8, Normal beat Easy 3/4, all 56 commanders located the
  enemy by scouting (median 118 s). The study's no-timeout assertion fails on that one match.
- Release player map benchmark: 0.9–1.2 ms per frame idle/pan, 1.8–2.3 ms in a 100-tank battle on every map; fog
  ~0.3 ms on 600 m maps.

### Problems encountered
- Addendum after the first maps were built: maps, spawn model and AI knowledge reworked.
- Generated mesh assets reached 750 MB; reduced to ~132 MB (no tangents for environment meshes, capped scatter).
- Validator flagged harmless NavMesh islands inside obstacles; changed to reachability.
- Pacing on large maps: starved economies, attrition stalemates, scout churn, one-factory economies; fixed over three
  study iterations without changing unit speeds or adding bonuses.
- Tooling: stray character in `PC_Renderer.asset` again (restored); bridge timeouts on long `eval` calls.

### Human interventions required
- One mid-milestone requirement addendum from the director. No blockers.

### Known limitations
- Green Divide matches are long (AI-vs-AI median 34 min); ~132 MB of generated meshes in the repository; harvester
  targeting and the end-game ore check use global deposit data; a 10/5 faction-1 lean in AI mirrors needs more seeds.
- Acceptance playthrough on all four maps still to be done by a human.

### Relevant commit hash
Milestone commit: `62781b8` (62781b83bd4c8b4ecf37e995d6d2113ab1cc62c6)

## Follow-up — Remove unused AI packages (2026-10-12)

### Feature requested
Director approved removing the unused `com.unity.ai.assistant` and `com.unity.ai.inference` packages (pending since
Milestone 005), confirming the console errors are gone, re-running all tests and committing.

### Changes implemented
- Removed both entries from `Packages/manifest.json`; Unity re-resolved `packages-lock.json` (their exclusive
  dependencies dropped). Checked first: no package and no project code depends on them; `com.unity.ai.navigation`
  (NavMesh) and the `com.unity.pipeline` editor bridge are kept, along with the shared dependencies they still need.

### Tests attempted and results
- Compiles cleanly. EditMode **242/242**, PlayMode **64/64**.
- Console after clearing and a full PlayMode run: **0 errors, 0 warnings**. The recurring
  "Error reason is 'NoSubscription' ... generators.ai.unity.com" messages no longer appear.

### Problems encountered
- None. Bridge timeouts logged while packages re-resolved were from status polling, not the packages.

### Human interventions required
- Director approval for the removal.

### Known limitations
- None new.

## Follow-up — Fog of war hid nothing at match start (2026-10-12)

### Feature requested
Director playtest: "The fog of war is not dark enough. As a player I can see exactly where the AI base is at the
beginning of the map and throughout."

### Changes implemented
- **Root cause (bug since Milestone 007):** `VisionEntity` collects its renderers in `Start`, but the vision system's
  first tick runs before that. The tick marked unseen enemy structures as hidden while their renderer list was still
  empty, and the later `Start` call skipped re-applying the same state. Every enemy building present at match start,
  and some built later, stayed drawn under the fog, and showed through it because unexplored ground was only 93 % dark.
  Fix: `Start` forces the hide/show state onto the collected renderers.
- Never-explored ground is now fully dark (`unexploredDarkness` 0.93 → 1.0, the classic black shroud), set in every scene.
  Explored-but-not-in-sight stays dimmed (0.48).

### Tests attempted and results
- New rendering checks: on every skirmish map and in RTS_Match, every renderer of an enemy structure the player has
  never seen must be switched off at match start; RTS_Match also checks the shroud value. (The Milestone 007/008 tests
  only checked the visibility rules, `CanSee`, not what is drawn — which is why this was missed.)
- Live check on Iron Pass: enemy HQ 6/6 renderers hidden (before the fix: 10 drawn); a screenshot at the enemy HQ shows
  only darkness, and the minimap shows only the player's base.
- EditMode **242/242**, PlayMode **65/65**.

### Problems encountered
- The Editor hung in "compiling" with no CPU activity (as in Milestone 001); restarted with the director's approval.
  Because of that hang, I did not re-run the new test against the reverted code. The live inspection before the fix showed
  exactly the condition the test asserts against.

### Human interventions required
- The playtest report, and approval to restart the hung Editor.

### Known limitations
- Enemy structures stay drawn once seen (remembered, by design); destroyed-while-unseen ones leave a "last known" ghost.

## Milestone 009 — Building Repairs, Vehicle Repair Pads and Queuing (2026-10-12)

### Feature requested
In-place building repairs (toggle, gradual, cost proportional to health restored, pause without credits, AI parity);
a Repair Pad structure (cost, construction time, power, one vehicle at a time, multiple pads, harvesters included);
a FIFO repair queue with explicit states, safe waiting and exit positions and a hand-shake bay transfer; robust
handling of destruction, power loss, cancellation, new orders, blocked routes, stuck vehicles, match end and restart
without teleporting; UI (Repair button/hotkey, right-click a pad, cancel, status, no-pad message); AI pad building and
repair decisions; visuals and audio separate from the logic; tests including 10+ vehicles at one pad and AI-vs-AI
studies; design and report documents.

### Changes implemented
- **Pure core:** `HealthModel.Restore`; `Core/Repair` (`RepairMeter`, `RepairPadQueue`, `RepairOrderMachine` with
  `IRepairVehicle`/`IRepairPadSite`, `RepairTimeouts`); `Core/AI/RepairPlanner`; repair fields in `CommanderSettings`,
  `CostTable`, `CommanderSnapshot`; `AiActionType.BuildRepairPad` in `EconomyPlanner`/`ExpansionRules`.
- **Runtime:** `BuildingRepair` (added by every `Building`), `RepairPad`, `RepairLedger`, `RepairClient` (drives
  vehicles only through their normal orders); cancel hooks in `CombatUnit`/`Harvester`/`UnitCommandController`;
  right-click pad, `IssueRepair`/`IssueCancelRepair`; `RepairHud` (Repair [R], Cancel, building toggle, status);
  `SelectionHud` lines; `Repair` input action (R); `RepairPadFx`, `BuildingRepairFx`, `FxLibrary.RepairSparks`; repair
  sounds; `EnemyCommander.ManageRepairs` and pad construction; `RepairSettings` data with `MatchSetup.Repair`;
  `repairCostMultiplier` on building and unit definitions.
- **Editor/content:** `RepairContentBuilder` (pad definition, settings, prefab, `RegenerateContent()`), `RepairModels`
  (procedural pad), icon and audio generation; pad added to the construction options; difficulty assets regenerated.
- **Docs:** `Docs/Milestone-009-Design.md`, `Docs/Milestone-009-Report.md`, `Docs/Balance/M009/`, `Docs/Screenshots/M009/`.

### Tests attempted and results
- EditMode **264/264** (+22 in `Milestone009RepairTests`), PlayMode **74/74** (+9 in `Milestone009RepairPlayModeTests`,
  including 10 vehicles at one pad with never more than one in the bay). Console clean.
- AI-vs-AI study, 12 matches on 4 maps (two iterations): final 11 decisive / 1 timeout (Dustfall 23), Hard beat
  Normal 4/4, repairs 2–6 % of income. Median match length 1558 s against 927 s on the same seeds in M008. The study's
  no-timeout assertion fails on that one match. Iteration 1 (building repair 2.5 %/s) had 2 timeouts; the rate was
  lowered to 1 %/s.

### Problems encountered
- The recurring "Editor hang" turned out to be a hidden modal save dialog when tests start with a dirty scene; tests
  now start from a clean scene.
- Builder helpers are internal (unreachable from the CLI); added a public regeneration entry point.
- Several test-setup issues (harvester income, prerequisite registration timing) and a double-counted completion, fixed.
- Building repair at 2.5 %/s stalled sieges in the first study.
- Memory pressure on the machine killed background watcher shells during the study (the study itself completed).

### Human interventions required
- The director chose to skip a control study (AI repairs off on the same seeds) and proceed to the commit.

### Known limitations
- Matches are about 1.5–1.7× longer with repairs, and one Normal mirror timed out. Without the control batch,
  repair effects and run-to-run variance are not separated. Data-only tuning options are listed in the report.
- The pad's construction time is an assembly phase after instant placement; structures have no build times.
- Acceptance playtest by a human still needed.

### Relevant commit hash
Milestone commit: `fd8ab51` (fd8ab51504c01e731459bf4c44fb6959e897dc91)

## Milestone 010 — Construction Territory, Building Sales and Mobile Command Vehicles (2026-10-12)

### Feature requested
Faction construction territory around the Headquarters and Forward Command Centres:
- full-footprint validation; overlapping zones combine; enemy exclusion zones; dynamic updates;
- no territory from ordinary buildings;
- a placement overlay that is readable with fog and does not reveal enemy bases.

Selling buildings: timed dismantling, refund by health, cancel, no duplicates, queue/research rules, confirmation for
critical structures, AI use. A Mobile Command Vehicle that deploys in 15 s into a Forward Command Centre (validation,
vulnerability, exactly-once conversion, health rule, no teleport, no pack-up). Victory interactions. AI expansion with
MCVs from legitimate knowledge only. Tests, multi-map AI-vs-AI studies, exploit review, presentation, design and report.

### Changes implemented
- **Pure core:**
  - `Core/Territory/TerritoryMap` (union coverage, known-only exclusion, versioned changes);
  - `Core/Economy/SaleRules` + `SaleProcess`; `Core/Construction/DeployProcess`;
  - `Core/AI/ForwardBaseRules`; new `PlacementResult` values;
  - CommanderSettings / snapshot / cost-table fields; planner integration.
- **Runtime:**
  - `TerritorySystem` + `TerritoryTracker` (anchors from registration events, per-faction sightings);
  - territory-aware `BuildingPlacementController` (`Validate(…, faction)`, `ValidateDeployment`, `PlaceDeployed`);
  - `BuildingSale` (+ `BuildingSaleFx`); `MobileCommandVehicle` (+ `McvFx`); `UnitMover.SetLocked`;
  - `TerritoryOverlay` + `TerritoryOverlayPass` (render-graph pass after the fog); minimap territory outline;
  - Sell / Cancel sale / Deploy [T] / Cancel deploy in the command panel; notifications; help and setup texts;
  - Annihilation counts FCCs; the `EnemyCommander.ForwardBases` partial (MCV tasks, escort, retries, sales);
  - `TerritorySettings` data; new sounds.
- **Content:** `CommandContentBuilder` and `CommandModels` (MCV and FCC), icons, overlay materials, MCV in the factory list, HQ/critical-structure flags, regenerated difficulty assets.
- **Docs:** `Docs/Milestone-010-Design.md`, `Docs/Milestone-010-Report.md`, `Docs/Balance/M010/`, `Docs/Screenshots/M010/`.

### Tests attempted and results
- **EditMode:** 279/279 (+15 in `Milestone010TerritoryTests`).
- **PlayMode:** 88/88 (+14 in `Milestone010PlayModeTests`, covering the acceptance scenario in a real map, including the AI deploying its own MCV).
- **Console:** clean.
- **Visual check** in the running game (5 screenshots) found the overlay hidden by the fog; fixed and re-checked.
- **AI-vs-AI:** four iterations; the final study has 12 matches on 4 maps:
  - 9 decided, 3 timeouts (M009 on the same seeds: 1);
  - Hard beat Normal 3 of 4;
  - 20 of 24 commanders built forward bases (40 of 41 MCVs deployed, none lost);
  - first forward base at a median of 696 s;
  - median match length +31 % (1787 s against 1360 s).

### Problems encountered
- The fog-of-war pass darkened the overlay; solved with a post-fog render pass queued from script.
- The AI economy stalled under territory rules:
  - MCVs were bought only from surplus;
  - home fields ran dry before the commander could save 5000;
  - the escort blocked deployment;
  - forward bases churned because harvesters haul from any field.
- Each was fixed through a data and rule iteration (see the report).
- `GetInstanceID` is obsolete in this Unity version.

### Human interventions required
- None.

### Known limitations
- Slower, more stalemate-prone AI matches; tuning options are listed in the report.
- Exclusion zones apply only to enemy bases the faction has seen (deliberate, against information leaks).
- The AI does not repair its MCVs and does not route around threats.
- Acceptance playtest by a human still needed.

### Relevant commit hash
Milestone commit: `7cadc75` (7cadc758382684699da4661633b434710d4957c1)
