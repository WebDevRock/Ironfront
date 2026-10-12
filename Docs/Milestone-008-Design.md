# Milestone 008 — Design: Multiple Maps, Skirmish Configuration and Difficulty

Status: design written before implementation, then revised for the **Larger Maps, Randomised Spawns and Exploration
addendum** received during the milestone (sections 10–14 supersede the earlier map table, start choice and AI
start-location knowledge). Section 9 lists deviations found while building.

## 1. Architecture review (state after Milestone 007)

| Area | Current state | Map-specific? |
|---|---|---|
| Scene management | `MainMenu` → `SceneManager.LoadScene("RTS_Match")`. Restart reloads the active scene; "Main menu" loads `MainMenu`. | One hard-coded match scene. |
| Match initialisation | `MatchController` (order −200) builds `FactionState`s from `MatchSetup.asset` (credits 2000 each). `VisionSystem` (−150) builds the fog grid. `MatchFlowController.Start` collects every faction with an HQ. | Credits and fog come from one shared asset. |
| Factions and spawns | Bases (HQ, refinery, harvester) and `FactionStartLocation` markers are **pre-placed in the scene** by `MatchSceneBuilder`; a second scene `RTS_Match_Swapped` exists only so balance tests can swap corners. | Spawn = scene content. Swapping requires a second scene. |
| Resources | `OreDeposit` prefabs placed by the builder with per-field amounts. | Yes (correct place for it). |
| Navigation | One `NavMeshSurface` baked per scene from physics colliders; structures and deposits carve at runtime. | Yes. |
| Building placement | `PlacementRules` (pure) + `BuildingPlacementController` (obstacle layer AABBs, `mapBounds` serialised, default 100×100). | Map size baked into four separate serialised fields (camera bounds, placement bounds, fog grid, minimap reads camera bounds). |
| AI | `EnemyCommander` + pure planners. Difficulty asset `AiDifficulty` (only `Normal` exists) holds `CommanderSettings`. Ore choice, rally point and defence sites use **straight-line** distances/directions. Scouts use the faction's own exploration grid. Halts when its HQ dies. | Implicitly assumes open ground: on a map with ridges a straight-line rally point can snap to the wrong side of a wall. |
| Victory | `MatchOutcomeRules` (pure): HQ destruction, simultaneous-HQ draw, exhaustion draw. | Shared, single mode. |
| UI | Legacy uGUI built by editor builders (`UiBuild`, `UiTheme`). Main menu: Start / Options / Quit. In-match: GameMenu (F10) and the result panel (Restart / Main menu). | Shared. |

**Conclusion.** Shared gameplay systems are already independent of the map; what is map-specific is scattered across the builder (layout), four serialised bounds fields and the pre-placed bases. The milestone needs (a) a description of a map that a builder turns into a scene, (b) a small per-match *configuration* that travels from the menu into the scene, and (c) runtime spawning of bases from the chosen start slots. No gameplay system needs to be rewritten.

## 2. Decisions

### D1. One scene per map, generated from a layout description
Each battlefield is its own scene (`Assets/Scenes/Maps/<Id>.unity`) produced by a new editor builder `SkirmishMapBuilder` from a data description (`MapLayout`: size, palette, obstacles with style, ore fields, start slots, roads, contested-field tags). Navigation is baked per scene, as before. Rationale: matches the existing builder pipeline, keeps the baked NavMesh exact, makes each map independently testable, and avoids runtime terrain generation.

`RTS_Match` (and `RTS_Match_Swapped`, `RTS_Sandbox`, `RTS_Benchmark`) are kept unchanged as regression / benchmark scenes. They are not offered in the skirmish menu.

### D2. Map metadata as data
`MapDefinition` (ScriptableObject): id, display name, scene name, description, tags (size, style), preview texture, default start credits... `MapCatalog` (ScriptableObject, referenced by the menu) lists the four maps plus the three difficulty assets. The menu reads only the catalog, so adding a map is data-only plus a build.

### D3. Skirmish configuration: plain data, carried by a session holder
`SkirmishSettings` (pure C#, `Ironfront.Core.Skirmish`): map id, difficulty, starting-resource level, the player's start slot (a slot id or Random — see D12), victory mode and a spawn seed (0 = new seed every match). It validates itself (`Sanitised()` clamps unknown values to defaults) and resolves Random into a concrete slot with an explicit seed (`Resolve`).

`SkirmishSession` (static, runtime) holds **only**: the settings to launch next and the last settings the menu used (to pre-fill the menu). It never holds scene objects or match state. Each scene load creates fresh managers; all registries keep their existing `SubsystemRegistration` resets. Restart reuses the same settings (Random is re-rolled: the setting is "random", not a remembered slot); "New match" goes through the menu, which writes a new settings object.

### D4. Runtime base spawning from start slots
Map scenes contain two `StartSlot` markers (slot 0 = south, slot 1 = north, with HQ / refinery / harvester offsets) and **no pre-placed bases**. `SkirmishInitializer` (order −300):
1. `Awake`: read `SkirmishSession` (or the map's defaults if the scene is opened directly — editor, tests), resolve, then configure before anything else wakes: starting credits on `MatchController`, difficulty on the `EnemyCommander`, victory mode on `MatchFlowController`, camera rig position over the player's base.
2. `Start` (runs before every other `Start`): spawn each faction's HQ, refinery and harvester through `FactionSpawner` (owner set before `Awake`/`OnEnable`, the M004 lesson) and create its `FactionStartLocation`. `MatchFlowController.Start` (order 1000) then finds both HQs as before.

Faction 1 is always the player and faction 2 the computer; only their slots change. Building fronts face the opponent (north/south: every map puts its slots in opposite halves along Z, so the existing `FacesSouth` convention keeps working).

### D5. Map size as one value
The builder writes the map size into the camera bounds, placement bounds, fog grid and ground in one place (`MapLayout.Size`); the minimap already derives from the camera bounds. Validator checks they agree.

### D6. Difficulty = behaviour only
Three `AiDifficulty` assets (Easy / Normal / Hard). Normal is the existing M007 behaviour, unchanged. New `CommanderSettings` fields are behavioural switches whose Normal value reproduces M007 exactly:

| Setting | Easy | Normal | Hard |
|---|---|---|---|
| Decision interval | 3.0 s | 1.5 s | 1.0 s |
| Attack group (EffectiveAttackSize) | larger, later | 6 | 6, but waits for a strength edge (below) |
| Army cap / harvesters / refineries | 8 / 3 / 1 | 12 / 4 / 2 | 16 / 6 / 3 |
| Credit reserve kept | 600 | 300 | 150 |
| Scouts kept | 0 | 1 | 2 |
| Counter-composition (reacting to seen heavies / artillery) | off (fixed mix) | M007 rules | stronger shares + AT defences vs heavies |
| Defend radius / threat memory | 18 m / 10 s | 24 m / 20 s | 30 m / 40 s |
| Attack only with a known strength edge | no | no | yes (≥ 1.15× known enemy strength, or all-in) |
| Retreat threshold | 50 % | 35 % | 45 % (preserves the army) |
| Route-aware defence placement | yes | yes | yes |

No setting changes costs, damage, production time, vision range or knowledge: every difficulty pays the same prices, sees through the same fog, and builds through the same placement validation as the player. (`AiDifficulty.VisionRange` is a perception filter, never larger than unit sight.)

### D7. Victory modes
* **Headquarters (default)** — unchanged M001–M007 rules: a faction with no living HQ is defeated; HQs falling in the same evaluation = draw; exhaustion = draw.
* **Annihilation** — a faction is *eliminated* when it has **no living production-capable structure** (Headquarters or Vehicle Factory) **and no living armed mobile unit** (any unit with a weapon; harvesters, refineries, power plants, research centres and static defences do not keep a faction alive). Last faction not eliminated wins; all eliminated in the same evaluation = draw; exhaustion rule unchanged (draw).
  * **Termination guarantee:** once a faction has lost every production-capable structure, its remaining units are *revealed* to all factions (a "last stand" rule, symmetric for player and AI — a game rule, not AI knowledge). Units can no longer be replaced, so the opponent can find and destroy them; if neither side can act the exhaustion rule ends the match. Losing the HQ no longer halts the computer commander in this mode: it continues from its factory.

### D8. Map validator
Pure rules in `Ironfront.Core.Maps.MapValidation` working against an `IMapProbe` interface (is point walkable, path length between points, can a footprint be placed). Two implementations: `NavMeshMapProbe` (runtime, real scenes; NavMesh + physics + `PlacementRules`) and a grid probe in tests (synthetic broken maps: disconnected regions, blocked chokepoint, unreachable deposit). Checks:
1. Both start slots exist, are inside bounds and on the NavMesh.
2. Starting HQ / refinery / harvester positions pass building placement (not in obstacles).
3. Room for expansion: at least N non-overlapping factory-sized sites within 28 m of each HQ.
4. Each slot's harvester can path from its refinery dock to its home field.
5. Each slot reaches every deposit (unreachable deposits are errors) and the other slot.
6. Total reachable ore per slot above a minimum; fairness ratio of the path-weighted ore score between slots within 15 %.
7. Contested deposits: both slots reach them, path-length ratio within 20 %.
8. Strategic points (map centre, each slot's approach) reachable.
9. NavMesh matches the battlefield: no NavMesh inside obstacle cores, NavMesh covers most open ground, map bounds consistent across camera / placement / fog.

Exposed as `Ironfront > Validate Maps` (editor) and as PlayMode tests over the four real scenes.

### D9. AI adaptation (no map-specific code)
* Deposits are ranked by NavMesh path length from home, unreachable ones skipped.
* Rally point and defence sites lie *along the computed route* to the enemy start, not on the straight line (on Iron Pass this puts towers at the pass).
* Scout waypoints whose path is incomplete are remembered and skipped (no repeated impossible orders).
* Construction failures already back off (20 s); unchanged.

### D10. Skirmish setup menu
Inside `MainMenu`: "Skirmish" opens a setup panel: map list with preview image and description, then four selectors (Difficulty, Starting credits, Start location, Victory). Defaults: Dustfall Plains, Normal, Standard (2000), Random, Headquarters. Only implemented options are listed. Start writes `SkirmishSession` and loads the map scene.

Starting credits levels: Low 1000 / Standard 2000 / High 4000.

## 3. The four battlefields

All flat-ground maps (consistent with M001–M007 movement and placement), with relief built from obstacle families, distinct palettes, lighting tint and props.

| Map | Size | Character | Economy | Routes |
|---|---|---|---|---|
| **Dustfall Plains** | 120 m | Sand and ochre desert, scattered mesas and low dune ridges | Generous: home 12 000 + two flank fields 7 000 per side + centre 6 000 | Wide open, three broad lanes |
| **Iron Pass** | 110 m | Grey-brown mountains: two long ridges cross the map | Home 10 000 per side; contested central basin 12 000 | Two narrow passes per ridge into a central basin |
| **Green Divide** | 120 m | Green grassland, forest belts and clearings | Modest home 6 000 + three expansion clearings (5 000) per side | Many routes around forest blocks |
| **Blackstone Basin** | 130 m | Ash-black industrial wasteland: ruins, stacks, pipelines, slag | Uneven: south has two medium fields, north one large field at equal path cost; rich central basin 10 000; two flank fields | Pipelines and ruins form several medium lanes |

Slots: slot 0 south, slot 1 north on every map; layouts are point-symmetric except Blackstone's deliberately uneven resource split, whose fairness is checked by the validator's path-weighted score and by the AI-vs-AI study.

## 4. Match flow

```
MainMenu ──Skirmish──▶ Setup panel ──Start──▶ SkirmishSession.Launch(settings) ──▶ load map scene
   ▲                                                                                  │
   │                       result panel / F10 menu: Restart ── reload scene (same settings)
   └──────────── Main menu ◀──────────────────────────────────────────────────────────┘
```
Initialisation order inside a map scene: SkirmishInitializer.Awake (−300) → MatchController.Awake (−200) → VisionSystem (−150) → everything else → SkirmishInitializer.Start (−300, spawns bases) → other Starts → MatchFlowController.Start (1000, collects competitors).

## 5. Testing plan

* EditMode: settings defaults / sanitising / resolve; session launch & reset; difficulty presets (ordering, no cheats: all assets have identical non-behaviour fields); annihilation rules; validator rules with synthetic broken maps.
* PlayMode: each map loads, validator passes, two bases spawn at the chosen slots with the chosen credits and difficulty; restart keeps settings; return to menu and launch a different map/difficulty; victory modes end matches (annihilation incl. last-stand reveal); no duplicate managers after several loads.
* AI-vs-AI study (explicit): Normal mirror on all four maps × several seeds × both slots; plus Hard vs Normal and Normal vs Easy on every map. Results reported as observations, not proof.
* Performance: release-build benchmark per map (interleaved runs).

## 6. Risks

* Uneven Blackstone economy may create a slot advantage — measured, adjusted if large.
* Narrow passes may cause pathing congestion; pass width ≥ 7 m (units are 0.5 m radius agents, formations 3 m).
* Longer maps lengthen matches; AI-vs-AI time budget grows.

## 10. Addendum: larger battlefields

| Map | Size | Starts | Symmetry | Terrain |
|---|---|---|---|---|
| Dustfall Plains | 600 × 600 | 8 | dihedral (D4) | mesas, dune ridges, rocks; open lanes |
| Iron Pass | 400 × 400 | 8 | D4 | ridges on x/z = ±67 form 9 valleys joined by 10 m passes; central basin |
| Green Divide | 600 × 600 | 8 | D4 | forest ring (8 gaps) + noise-shaped woods; glades with ore |
| Blackstone Basin | 400 × 400 | 6 | point | ruins, pipelines, stacks, tank farms, slag heaps; uneven ore |

The director's target sizes are kept. Pacing check: tanks move 5–8 m/s, recon 12 m/s, so valid opponents (≥ 300 m apart on
the 600 m maps, ≥ 200 m on the 400 m maps, by ground) are 40–80 s apart for tanks and 25–35 s for a scout. Unit speeds are
**not** changed; instead starts are placed on a ring (not in corners), every start has its home field 25–30 m away, and
extra fields sit between starts and the centre so that expansion meets the enemy halfway. AI-vs-AI durations are
measured in the study.

Layouts are generated deterministically from a seed inside one symmetry cell and replicated (`SkirmishMapLayouts`), so
starts are equivalent by construction; the validator then measures them. Systems scaled per map: camera bounds and pan
speed (×2 on 400+ m maps), placement bounds, fog grid (2 m cells: 300² cells on 600 m maps), minimap (256 px), ground
texture (2048²), scatter density (area-based, capped).

## 11. Addendum: start slots and spawn pairs

* Each map has 6–8 `StartSlot`s (HQ, refinery and harvester positions; the base faces the map centre).
* **Pair rules** (`SpawnPairRules`, per map): minimum *ground* separation (300 m / 200 m) and maximum economic-opportunity
  imbalance (12 %, Blackstone 15 %). Opportunity = Σ ore × 1 / (1 + path distance / 80 m) over every field.
* The validator derives the valid pairs (`SpawnSelection.ValidPairs`); the builder stores each slot's valid opponents in the
  scene; the validator re-checks the stored table against the rules on every run.
* **Selection** (`SpawnSelection.Choose`, pure): Random draws uniformly over all valid *ordered* pairs (no slot favoured);
  a chosen player slot keeps the slot and draws the opponent from its valid partners. Seeded: the seed is recorded in
  the match log and in study results; `SkirmishSettings.Seed` reproduces a match exactly.
* The setup menu offers Random or any slot (numbered on the preview). The computer's slot is never shown.

## 12. Addendum: exploration and intelligence

* Neither side is told where the other started. Fog of war (M007) hides the enemy base from the player; the AI's
  perception already reads only what its faction sees.
* `AiDifficulty.knowsEnemyStartLocation` is **removed**. The AI's model is `EnemyIntel` (pure): it knows the map's candidate
  start sites (public map knowledge — the same numbered markers the player sees on the preview), never which one is
  occupied. A site becomes *Empty* when seen without enemy structures, *EnemyBase* when remembered structures are near it.
  Every belief carries a last-confirmed time; observations older than 180 s are *outdated* and are re-scouted.
* Behaviour: the first recon is built before any tank while the enemy is unknown; scouts visit unexplored candidate
  sites first (nearest, not shared between scouts), then outdated enemy sightings, then unexplored ore fields, then
  generic unexplored ground; unreachable points are remembered and skipped. Attack groups without a known target search
  the nearest unexplored site (crossing sites off as they see them) instead of marching to a known start. Expansion only
  targets ore fields the faction has explored. Rally points, defence sites and attack routes follow the ground route to
  the current best guess, so they adapt to the spawn pair.
* Disclosed limitations (shared by player and AI): the harvester's automatic field choice and the end-game "is any ore
  left on the map" check use global deposit data, as in M005–M007.

## 14. Pacing changes after the first (baseline) study

The baseline AI-vs-AI study on the large maps (28 matches, `Docs/Balance/M008/baseline_runs.jsonl`) showed two failure
patterns, fixed without touching unit speeds or adding any bonus:

1. **Starved economies** (matches decided at ~600–900 s): the loser never built a second refinery. Expansion only targets
   explored ore, and its scouts were busy checking candidate start sites. Now, while every ore field the commander has
   seen is spent or already served, scouts look for ore near home before looking for the enemy.
2. **Attrition stalemates** (2 200–2 700 s, 2 timeouts): rich economies traded equal attack waves of ~6 units into
   defences, and scouts were bought again and again (up to 24 per match). Now a failed attack (less than half its
   strength returns) raises the next attack group and the army cap by 2 (Easy 1) up to +8 (Easy +4), and once the enemy
   is located a lost scout is replaced at most every 150 s.

## 13. Addendum: verification

* Validator: ≥ 6 slots; every slot's base placement, expansion room (5 factory sites in 28 m), ore within 60 m (≥ 8000),
  home field ≤ 45 m, harvest route, reachability of every field; every slot pair connected; derived pair table non-empty
  for every usable slot and equal to the stored one; navigation coverage; reachable ground inside obstacles.
* Tests: spawn selection determinism/uniformity, intel rules, synthetic broken maps, every map's spawn initialisation,
  fog restriction at start, AI discovery by scouting, seeds vary/reproduce starts, restart/menu flow.
* Study: all maps, several seeds (hence different start pairs), Normal mirrors plus Hard/Easy matchups; records map size,
  slot ids, seed, discovery and first-contact times.

## 9. Deviations recorded during implementation

* The addendum replaced the original 100–130 m two-start maps (sections 3 and the South/North start choice are
  superseded by sections 10–12).
* Validator "walkable ground inside an obstacle" became "*reachable* ground inside an obstacle": Recast leaves harmless,
  disconnected islands inside solid boxes.
* AI pacing needed three study iterations (section 14 plus: escalation only after full-size attacks, map-size caps via
  `CommanderSettings.ScaledForMap`, second factory on large maps). Unit speeds unchanged.
* Environment meshes for the large maps are saved without tangents and with capped scatter to keep the generated assets
  to ~132 MB (first build: 750 MB).
* Map previews are auto-levelled renders with numbered start markers; the menu legend explains them.
