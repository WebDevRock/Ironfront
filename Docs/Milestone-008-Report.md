# Milestone 008 — Report: Multiple Maps, Skirmish Configuration and Difficulty

**Date:** 2026-10-12 · **Base:** `b0725b8` (end of M007) · **Design:** `Docs/Milestone-008-Design.md`
Includes the mid-milestone **addendum** (larger maps, randomised spawns, exploration), integrated rather than separate.

## 1. What was delivered

| Area | Delivered |
|---|---|
| **Four battlefields** | **Dustfall Plains** 600 × 600 (desert; mesas and dune ridges; 8 starts; generous ore). **Iron Pass** 400 × 400 (mountains; ridges cut the map into 9 valleys joined by 10 m passes; contested central basin; 8 starts). **Green Divide** 600 × 600 (forest ring with 8 gaps plus noise-shaped woods; small home fields, many glades; 8 starts). **Blackstone Basin** 400 × 400 (industrial ruins, pipelines, stacks, tank farms, slag; deliberately uneven ore; 6 starts). Each has its own palette, lighting, haze, colour grading, obstacle families, props, backdrop and minimap colours. |
| **Map generation** | `SkirmishMapLayouts`: deterministic, seeded layouts generated in one symmetry cell (D4 for the 8-start maps, point symmetry for Blackstone), so starts are equivalent by construction. `SkirmishMapBuilder` turns each into a scene with its own NavMesh, start slots, ore, strategic markers, preview image and data asset. |
| **Spawns** | 6–8 validated start slots per map. Pairs must be ≥ 300 m (600 m maps) / ≥ 200 m (400 m maps) apart *by ground* and within 12 % (Blackstone 15 %) economic opportunity. Valid pairs: 40 ordered pairs on each 8-start map, 22 on Blackstone. Random start draws uniformly over valid pairs with a **recorded, reproducible seed**; the player may also pick a start (the computer's start is drawn from that start's valid partners). |
| **Exploration** | Neither side is told where the other started. The computer's `EnemyIntel` knows only the candidate starts (shown to the player too, numbered on the preview), crosses them off as it sees them, remembers sightings with timestamps and treats them as outdated after 180 s. The old `knowsEnemyStartLocation` setting is gone. |
| **Map validator** | Pure `MapValidator` over an `IMapProbe` (NavMesh/physics/placement probe for scenes, grid probe for tests). Checks slots, bounds agreement (camera/placement/fog), start-building placement, expansion room, starting ore, home field and harvester route, reachability of every field and every slot pair, spawn-pair rules and the stored spawn table, contested fields, strategic points, reachable ground inside obstacles and NavMesh coverage. Menu: *Ironfront › Validate Skirmish Maps*. **All four maps pass.** |
| **Skirmish setup** | *Skirmish* in the main menu → battlefield list with preview and description; difficulty, starting credits (1000 / 2000 / 4000), start (Random or a numbered slot) and victory condition. Defaults: Dustfall Plains, Normal, Standard, Random, Headquarters. The last choice is remembered. Only implemented options appear. |
| **Difficulty** | Easy / Normal / Hard assets differing only in behaviour (decision speed, economy and army caps, scouting, counter-building, attack timing and caution). No resource, damage, production-time or vision bonuses — a test checks the assets share one vision range. |
| **Victory** | **Headquarters** (default, unchanged) and **Annihilation**: eliminated with no HQ/vehicle factory and no armed unit; a faction that can no longer produce has its forces revealed to everyone ("last stand"), so matches cannot stall on a hidden unit. The computer keeps playing from its factory after losing its HQ in this mode. |
| **Match flow** | `SkirmishSession` carries plain settings into the map; `SkirmishInitializer` (runs first) sets credits, difficulty and victory, then spawns both bases at the drawn slots. Restart reuses the settings; Main Menu → new settings → new map works without restarting the application. |
| **AI adaptation** | Rally points, defence sites and attack routes follow the NavMesh route to the current best guess of the enemy (so they adapt to every spawn pair and to passes). Ore is ranked by ground distance; expansion only to explored fields; scouts look for ore first when there is nowhere known to expand; unreachable scout points are remembered and never ordered again; the first scout comes before any tank while the enemy is unknown; attack groups search candidate starts. Large-map caps and failed-attack escalation (§4) keep games decisive. |
| **Large-map support** | Camera bounds and pan speed, placement bounds, fog grid (2 m cells), minimap (256 px, map colours), ground texture (2048²) and scatter density all follow the map size. |

Screenshots: `Docs/Screenshots/M008/` — `menu_skirmish_setup.png`, the four `*_Preview.png`, and per map `*_1_base` (fog on, start of match), `*_2_terrain` (fog off, mid-map with the full minimap), `*_3_battle` (100-tank battle, fog on).

## 2. Automated verification

| Suite | Result | New in M008 |
|---|---|---|
| EditMode | **242/242** | `Milestone008RulesTests` (32): settings defaults/sanitising/resolve; session copies and reset; spawn selection determinism, uniformity over 24 pairs, requested slot, fallback; pair rules; intel (no knowledge at start, crossing off sites, sightings, HQ focus, staleness); annihilation and HQ rules; difficulty presets, compositions, attack timing, assets; recon timing; pacing (escalation, scout throttle, large-map caps, second factory); **validator against synthetic maps**: valid, disconnected region, blocked chokepoint (open vs blocked), boxed-in deposit, start inside an obstacle, too few slots + stale table, unequal starts; layout targets and determinism; catalog. |
| PlayMode | **64/64** | `Milestone008PlayModeTests` (7) in the real scenes: every map starts with the chosen map, slots (validated pair), credits, difficulty and victory, single managers, map-sized fog/camera/minimap, and **both bases hidden from the other side**; seeds reproduce and vary the starts, unseeded matches draw new seeds; restart keeps settings with fresh state; menu → map → main menu → another map/difficulty with no leakage; annihilation (HQ loss alone does not end, last stand reveals, last unit ends it); HQ mode; **the computer locates the player's base by scouting** (no knowledge at start, real start is one of 7 candidates). All M001–M007 tests unchanged and passing. |
| Map validator | 4/4 maps pass | Reports in the build log and `Temp/validation_<id>.txt`. |
| Skirmish study (Explicit) | **27/28 decisive, 1 timeout** — the study's "every match ends" assertion therefore fails | See §3. |
| Release build | Succeeded | Map benchmark, army benchmark and screenshots from the player. |

## 3. AI-versus-AI study

**Method:** `SkirmishStudyPlayModeTests`, 10× time scale, 2700 s limit. Per map: Normal vs Normal with seeds 11/23/37/51 (each seed draws a different start pair), Hard vs Normal and Normal vs Hard (sides swapped), Normal vs Easy — 28 matches, 27 distinct start pairs, separations 212–440 m. Every record holds map size, slot ids, seed, outcome, duration, first contact, enemy-located times, income, spending, production, structures, scouting and escalation. Data: `Docs/Balance/M008/` (`baseline_*`, `iteration1_partial_runs.jsonl`, `iteration2_replay_*`, `final_*`). **28 matches are not statistically conclusive**; treat the numbers as indications.

| | Baseline (first large-map build) | Final |
|---|---|---|
| Decisive / timeouts | 26 / 2 | **27 / 1** |
| Match length, median (decided) | 1053 s | 1099 s |
| — Dustfall / Iron Pass / Green / Blackstone | 1487 / 1036 / 1179 / 923 s | **836 / 948 / 2016 / 1289 s** |
| Longest decided | 2666 s | 2563 s |
| First contact, median | 114 s | 112 s |
| Commanders that located the enemy by scouting | (not recorded correctly)¹ | **56/56, median 118 s** (3–489 s) |
| Recons bought per side, mean | 5.5 | 4.5 |
| Hard vs Normal | Hard 6 of 8 | **Hard 8 of 8** (both sides) |
| Normal vs Easy | Normal 2, Easy 1, 1 timeout | **Normal 3 of 4** |
| Faction 1 / faction 2 wins in mirrors | 10 / 5 | 10 / 5 |

¹ The baseline wrote discovery times on the absolute Editor clock; fixed for later runs.

**Iterations (what the data showed, what changed):**
1. **Baseline.** Short games (~600–900 s) were economic collapses — the loser never built a second refinery because expansion needs *explored* ore and its scouts were busy checking start sites. Long games (2200–2700 s) were attrition between rich economies with up to 24 scouts bought per side. → Scouts find ore first when there is nowhere known to expand; replacement scouts at most every 150 s once the enemy is located; bigger next attack after a failed one.
2. **Iteration 1 (12 matches, stopped early):** 3 timeouts. A trace showed escalation triggering on tiny early probes (attack size reached 14 while armies were 6–9) and both sides stuck on **one factory, four harvesters, ~1 unit a minute** on a 600 m map. → Escalation only for full-size attacks (+1, max +4); **map-size adaptation** of the economy/army caps (≥ 250 m: +2 harvesters, +1 refinery, +4 army, expansion at 60 % of the credit level); on large maps a strong economy (5+ harvesters) saves for its second factory.
3. **Replay of the three timeout seeds**, then the **final** study above.

**Observations:**
* **Start positions:** with seeded draws every slot was used; no slot shows a pattern in 4 mirrors per map (slot 7 lost both its Normal mirrors on three maps — 2 matches each, not evidence). The validator's economic-opportunity scores differ by at most 1.9 % between all starts on Dustfall and Green Divide; on Iron Pass (corner vs edge valleys, 10.1 %) and Blackstone (10.8 %) pairs over the 12 % / 15 % limit are simply never drawn; the 10/5 faction-1 lean is the same in both studies (p ≈ 0.3) and is the added study commander, not a start position — flagged for the next study.
* **Difficulty:** Hard won all 8 against Normal from either side, with ~37 % more income; Easy took one of four from Normal (Green Divide). Easy earns about as much as Normal because it fights less — it loses on army size and timing.
* **Pacing:** first contact ~2 minutes, enemy base located ~2 minutes, typical matches 14–21 minutes on Dustfall/Iron Pass/Blackstone. **Green Divide is long** (median 34 min; the one timeout is its opposite-corner pair, 440 m through forest gaps, both armies trading at 6–9 units). Unit speeds were not changed.
* **Scouting:** ~92 scout missions per commander per match; ~30 scout destinations per commander were found unreachable (forest and obstacle interiors) and skipped rather than re-ordered.

## 4. Difficulty settings (final values)

| | Easy | Normal | Hard |
|---|---|---|---|
| Decision interval | 3.0 s | 1.5 s | 1.0 s |
| Army cap (≥ 250 m maps) | 8 (12) | 12 (16) | 16 (20) |
| Harvesters / refineries cap (large) | 3 / 2 (5 / 3) | 4 / 2 (6 / 3) | 6 / 3 (8 / 4) |
| Scouts | 0 | 1 | 2 |
| Counter-building | none (fixed mix) | M007 rules | stronger shares, AT turret after 1 heavy |
| Attack | not before 300 s | M007 rules | only with ≥ 1.15× known enemy strength (or at the army cap) |
| Retreat at | 50 % | 35 % | 45 % |
| Failed-attack escalation | +1, max +2 | +1, max +4 | +1, max +4 |
| Defend radius / threat memory | 18 m / 10 s | 24 m / 20 s | 30 m / 40 s |
| Vision, costs, damage, build times | identical | identical | identical |

Normal's M007 rules are unchanged on 100 m maps except: the first scout before tanks while the enemy is unknown, the scout replacement limit and full-size-attack escalation (RTS_Match regression tests all pass).

## 5. Performance (release player, `Docs/Performance/M008/summary.txt`)

| ms per frame (avg / p99), fog on | Opening | Full-map pan | 100-tank battle |
|---|---|---|---|
| Dustfall Plains 600 m | 1.13 / 1.81 | 1.13 / 1.87 | 2.08 / 3.30 |
| Iron Pass 400 m | 0.95 / 1.32 | 0.91 / 1.24 | 1.83 / 2.96 |
| Green Divide 600 m | 1.16 / 1.74 | 1.13 / 1.71 | 2.29 / 3.82 |
| Blackstone Basin 400 m | 0.94 / 1.28 | 0.92 / 1.28 | 1.85 / 2.98 |

Fog costs ~0.3 ms on the 600 m maps (300² cells) and ~0.1 ms on the 400 m maps; ~400 MB total memory. Standard army benchmark: 200-unit combat 2.65 ms (single run, not interleaved with M007 — context only). One fog-off run was disturbed by machine load and repeated; both runs are kept.

## 6. Problems encountered

* **Mid-milestone scope change:** the addendum arrived after the first four (100–130 m, 2-start) maps were built and validated. Maps, spawn model, settings and AI knowledge were reworked; the earlier design remains documented and superseded.
* **Generated mesh size:** the first large-map build wrote **750 MB** of mesh assets (per-obstacle rubble on 360 forest blocks alone was ~14 000 rocks). Reduced to **~132 MB** (no tangents on environment meshes — their materials have no normal maps; capped/skipped rubble; sparser large forests and backdrop rings). Still large for the repository: see §8.
* **Validator false positives:** Recast leaves unreachable NavMesh islands inside solid boxes; the check now flags only *reachable* ground inside obstacles.
* **Pacing iterations** (§3) and a scout re-issuing the same order every decision when parked (fixed).
* **Test bugs found by running:** disabling commanders while iterating their registry; the computer legitimately spending credits before an assertion; a hard-coded time that predated the 180 s staleness.
* **Tooling:** the renderer-feature-map stray character reappeared (restored before commit); long `eval` builds time out the bridge (harmless "1 error" in the build summary); a play-mode toggle via `eval` did not take effect once — `editor_play` worked.

**Post-milestone fix (2026-10-12):** a director playtest found the enemy base visible through the fog from the start.
Cause: a vision-tick ordering bug, present since M007, left starting enemy structures drawn; unexplored ground was also
only 93 % dark. Both are fixed (see the AI log). Rendering is now checked by tests, not just the visibility rules.

## 7. Human verification still needed

* The acceptance run: a full match against the computer on each map, returning to the menu between them.
* Whether matches *feel* well paced to a human (especially Green Divide), whether the setup screen and previews are clear, and whether Easy/Hard feel right.
* Readability of each map's palette and fog on your display; whether 600 m maps are comfortable to navigate (pan speed doubled, minimap navigation).

## 8. Known limitations and decisions for the director

1. **Green Divide pacing:** median 34 min AI-vs-AI and one timeout. Options: shrink the map (e.g. 480 m), move its start ring inward, or thin the forest ring — your call on the 600 m target.
2. **Repository size:** ~132 MB of generated mesh assets (the M008 commit adds ~162 MB in total, including ground textures, scenes and NavMeshes). Generating map decoration at load time instead (deterministic from the layout) would remove them; it needs the art pipeline (MeshKit, terrain styles) available at runtime — a sizeable refactor I did not do without your approval.
3. Harvester auto-targeting and the end-game "ore left anywhere" check still use global deposit data (unchanged from M005–M007, both sides alike).
4. Unoccupied starts keep their home fields as free expansions, so large maps hold a lot of ore; this is intended but contributes to long games.
5. The study commander for faction 1 is added at runtime; the 10/5 faction-1 lean in mirrors should be re-checked with more seeds.
6. ~~Unused AI Assistant/Inference packages~~ — **removed after director approval** (2026-10-12): console clean after a full test run; EditMode 242/242, PlayMode 64/64.
