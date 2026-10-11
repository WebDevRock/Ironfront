# Milestone 007 — Report: Content Expansion, Technology and Strategic Depth

**Date:** 2026-10-11 · **Base:** `9f7dcd9` (end of M006) · **Design:** `Docs/Milestone-007-Design.md`

## 1. What was delivered

| Area | Delivered |
|---|---|
| **Units** | **Rocket Artillery:** 1100, needs a Research Centre. 3-rocket ballistic salvo, range 9–28 m with a visible minimum-range ring, splash, ×2 vs structures, own model and launch effect/sound. **Recon Vehicle:** 450, speed 12, sight 28, light machine gun. Both use the existing production, selection, orders, navigation, faction colours, wrecks and icons. |
| **Defences** | **Guard Tower:** 400, twin MG, strong vs light. **Anti-Armour Turret:** 900, −25 power, ×1.5 vs heavy, offline on low power. Both have targeting, health, destruction, placement validation, range rings (placement ghost and selection) and power integration. |
| **Research** | **Research Centre:** 1200, −30 power. **Topics:** Composite Armour (+10 armour points), Enhanced Weapon Systems (+20 % damage, needs Armour) and Advanced Harvesting (+40 % harvest and unload rate). Research costs credits and takes time, runs one topic at a time, refunds on cancel and can never be bought twice. It applies to existing and new units and is per match (stored in `FactionState`). Low power halves its speed. A sidebar RESEARCH section shows progress, cancel, tooltips and lock reasons, with start and completion notices and sound. |
| **Fog of war** | Per-faction 1 m visibility grid at 10 Hz: unexplored is black, explored is dimmed, visible is clear. A URP full-screen pass reconstructs world position from depth, so tall objects can't poke through, with soft edges. Enemy units are drawn, targetable, clickable and listed on the minimap only while visible. Enemy structures are remembered once seen, with a "last known" ghost if destroyed unseen. Health bars, effects, sounds and death notices are suppressed inside the fog. Attackers are revealed to their victim for 2.5 s. |
| **AI** (same architecture) | Perceives through the same vision grid as the player, so it is no longer omniscient: it remembers structures until it sees them gone. It scouts with recon, builds towers after being attacked and AT turrets after seeing heavies, builds a Research Centre once it has an army, and researches by priority. It saves for heavies and artillery instead of always buying Light Tanks, keeps artillery at a 22 m stand-off, hunts enemy artillery near its base, and goes all-in in the end game. |
| **UX** | Two-column build and unit grids with icons and costs. Extended tooltips (description, role, sight, weapon class). Lock reasons, e.g. "requires Research Centre". Defence range indicators. Readable fog on screen and minimap. Consistent with the M006 art and UI. |

Screenshots of the final build are in `Docs/Screenshots/M007/`: 02 shows recon and artillery, 08 defences and the lab, 09 the fog overview and contested ore, 10 research in progress.

## 2. Automated verification

| Suite | Result | New in M007 |
|---|---|---|
| EditMode | **210/210** | `Milestone007RulesTests` (28): visibility grid, research rules, weapon classes and splash, scouting, expansion/composition/end-game AI rules, min-range back-off, seeking last-known positions. `HarvesterTests.SinkLost…` (1). |
| PlayMode | **57/57** | `Milestone007PlayModeTests` (8), all in the real match scene with fog on: (1) fog hides, protects and un-hides enemies; (2) per-faction sight; (3) the guard tower kills a recon; (4) the AT turret is offline without power and hits heavies hard; (5) research flow (no double charge; existing, new and other-faction units); (6) no research leak between matches; (7) artillery is locked without a Research Centre; (8) artillery holds fire inside its minimum range and splashes a cluster. `AiMatchPlayModeTests` updated for 6 ore fields plus a contested-pair symmetry check. |
| Balance study (Explicit) | **Passes** (every match reaches an outcome) | 10 AI-vs-AI matches, 5 seeds × both layouts. Now records spending, production, losses, damage by type and research timings. |
| Release build | Succeeded | Showcase captured and reviewed; benchmark below. |

Regressions fixed along the way:
- Two M003 combat tests failed under fog because attack orders on unseen targets were silently dropped. Explicit attacks now seek the last known position.
- A full AI match exposed a latent harvester crash when its refinery is destroyed, a Unity "fake null" check in pure code. Fixed and covered by a test.

## 3. Balance study

**Method:** Normal vs Normal at 10× speed. Seeds 11, 23, 37, 51, 73, standard and swapped layouts. Raw data: `Docs/Balance/M007/` (`baseline_*`, `iteration2_runs.jsonl`, `iteration3_runs.jsonl`, `after_*`).

### Iterations

Each run is 10 matches; columns are run in order.

| Result | Baseline (M006) | Run 1: M007, old ore | Run 2: ore 15k / side | Run 3: + end-game fixes | Run 4: + research order | **Final:** + contested ore, snapshot fix |
|---|---|---|---|---|---|---|
| Decisive / draws / timeouts | 10 / 0 / 0 | 8 / 2 / 0 | 2 / 2 / **6** | 6 / 4 / 0 | 3 / 6 / **1** | **5 / 5 / 0** |
| Wins F1 / F2 | 5 / 5 | 5 / 3 | 1 / 1 | 4 / 2 | 2 / 1 | 3 / 2 |
| Wins south-west / north-east | **2 / 8** | 1 / 7 | 1 / 1 | 3 / 3 | 1 / 2 | **2 / 3** |
| Mean length | 380 s | 514 s | 1112 s | 599 s | 628 s | **594 s** (500–702) |
| Research completed | – | **0** | 29 | 23 | 20 | **38** |
| Artillery built | – | **0** | 19 | 23 | 16 | **34** |

What each run found:
1. **Run 1:** each side's ore (10k) was exhausted before any tech paid off. No research, no artillery.
2. **Run 2 (ore 15k per side):** the end game deadlocked. Spent factions saved forever for artillery, and an idle recon at home kept them "able to act", so timeouts replaced decisions.
3. **Run 3:** once no ore remains, the AI no longer saves, ignores its reserve, stops new investments, and its scouts join the final attack. This removed the timeouts.
4. **Run 4:** a last timeout came from 463 credits that could buy a recon but not a tank; the AI now buys the recon. Advanced Harvesting was never researched, which exposed a bug: research was chosen before harvesters were counted in the snapshot. Fixed.
5. **Final:** two 5000-ore fields either side of the central wall. Once home ore runs dry, the existing expansion rule takes both sides there, so the late game is fought over resources.

### Final numbers

The comparison counts are totals over 10 matches, both factions; the resource rows are means per faction.

| Metric | Baseline | Final |
|---|---|---|
| Income / spend per faction | 9,838 / 11,525 | 19,973 / 21,718 (contested fields are mined) |
| First attack | 152 s, identical in every match | 318 s mean, varying from 245 to 449 s |
| Units produced | 257 Light, 1 Heavy, 57 harvesters | 185 Light, **60 Heavy**, **34 Artillery**, 59 Recon, 64 harvesters |
| Defences built | – | 40 Guard Towers, 25 AT Turrets |
| Research completed | – | Harvesting 19/20, Armour 18/20, Weapons 1/20 factions |
| Headquarters destroyed | 10 | 5 |

Damage dealt and cost efficiency by type:

| Type | Damage dealt | Per unit built | Per 1000 credits |
|---|---|---|---|
| Light Tank | 74.7k | 404 | 808 |
| Heavy Tank | 58.5k | 976 | 1084 |
| Rocket Artillery | 43.7k | 1284 | **1167** |
| Recon | 3.4k | 57 | 127 (a scout, as designed) |
| AT Turret | 7.7k | – | 340 |
| Guard Tower | 4.9k | – | 307 |

Defences work mainly as deterrents and kill-zones rather than damage dealers.

### Strategy viability (not only win rate)

1. **The army is diverse now.** Heavies went from 1 to 60 and artillery is the most cost-efficient damage source. The one-unit Light Tank army of M006 is gone.
2. **Artillery correlates with winning.** Winners' artillery dealt 3,848 damage on average; losers' only 495. Two losers fielded none at all.
3. **Over-investing in AT turrets correlates with losing.** Winners built 0.4 on average, losers 1.2. That's 900 credits plus power each that never attacks. The counter works when heavies come, but it is a real trade-off, as intended.
4. **Tech adoption.** Harvesting and Armour are near-universal. Enhanced Weapons is reached in only 1 of 20 factions, because it is gated behind Armour, costs 1200 and arrives late. It is a valid late investment but rarely affordable; consider lowering its cost if more use is wanted.
5. **Start-position bias is gone.** The baseline was 8 / 2 for north-east; the final run is 2 / 3. Across iterations 3–5, the north-east share of decisive wins was 3/6, 2/3 and 3/5.
6. **Recovery:** spent factions now finish the match (all-in, scouts committed) instead of idling. There were no deadlocks or timeouts in the final configuration.

**Main open finding: 50 % exhaustion draws in AI mirror matches.** In every draw, neither side lost a structure. Both commanders attack at the same moment (about 345 s), the armies annihilate each other in midfield, and by then the map is mined out. This is a property of two identical deterministic AIs on a finite map, not of over-strong defences: in the decisive games, defended bases do fall to artillery-supported attacks. Draw counts varied from 2 to 6 across iterations with the same rules, so the estimate is noisy.

**Decision for the director**, as either is a game-rule change:
- (a) ore regrowth so economies never fully end;
- (b) a tiebreak on exhaustion, e.g. most surviving structure value wins.

A human opponent breaks the mirror timing, so this mainly affects AI-vs-AI testing.

## 4. Performance

Release build, 1280×720 windowed, same session. Runs were interleaved because an earlier non-interleaved set was inflated by about 50 % by machine load. The M006 control was rebuilt from commit `9f7dcd9` in a temporary worktree, which has since been removed. Data: `Docs/Performance/M007/summary.txt`.

| Army / phase | M006 avg / p99 | M007 fog on avg / p99 | M007 `-fxNoFog` avg / p99 |
|---|---|---|---|
| 25 idle | 1.30 / 5.95 ms | 1.33 / 5.97 ms | 1.34 / 5.96 ms |
| 50 combat | 1.87 / 5.95 ms | 1.98 / 5.98 ms | 1.93 / 6.01 ms |
| 100 combat | 2.30 / 5.81 ms | 2.39 / 5.80 ms | 2.44 / 5.81 ms |
| 200 idle | 2.54 / 5.97 ms | 2.52 / 5.74 ms | 2.57 / 5.93 ms |
| 200 combat | 3.14 / 5.69 ms | 3.32 / 5.61 ms | 3.34 / 5.83 ms |

- **Overall:** within 0.2 ms of M006 everywhere (+6 % at 200-unit combat, roughly run-to-run noise). Memory is unchanged (about 390 MB).
- **Draw calls:** +90 at 200 units, from range-ring and turret objects.
- **Fog cost:** a development build measured `Vision.Tick` at 0.04–0.05 ms per frame amortised at 200 units. The full-screen pass is not measurable within noise.
- **Spikes:** 90–110 ms one-off maxima appear in the first benchmark phase in both builds (start-up).

Visibility conditions covered: fog on with everything hidden or visible, and fog off. The benchmark armies stand in mutual view.

## 5. Human verification still needed

Automated checks cannot judge these:
- Whether the fog darkness levels read well on your monitor.
- Whether artillery feels satisfying.
- Whether tooltips are clear.
- Whether the AI feels fair and interesting to play against, and the difficulty level.

The acceptance scenario has only been played by the AI against itself and through scripted tests. A human playthrough of a full normal match is the real acceptance test: build an economy, scout with recon, find the enemy, build defences, research, then siege a defended base with artillery.

## 6. Known limitations

- **Vision:** sight is not blocked by walls; visibility is quantised to 1 m and 10 Hz; remembered structures show their last known look but not their health.
- **Artillery:** ballistic rockets aim at the target's position at launch, so a moving target dodges by design.
- **AI:** research and defence priorities are fixed rules rather than adaptive. Enhanced Weapons is rarely reached.
- **Exhaustion draws:** see §3.
- **Map changes:** home ore went from 6000 + 4000 to 9000 + 6000 per side, plus two contested fields. This changes M001–006 match pacing (about 600 s instead of about 380 s).
- **Pending from M005/M006:** the unused AI Assistant/Inference packages still log one CLI relay error during long editor operations.

**Commit:** `267ecfa`
