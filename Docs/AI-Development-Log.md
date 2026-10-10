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
Milestone commit: see the "Commit" line below (recorded after committing).
