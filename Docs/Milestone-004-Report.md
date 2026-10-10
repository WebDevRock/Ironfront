# Milestone 004 — AI Enemy Commander

**Date:** 2026-10-10
**Engine:** Unity 6000.6.5f1, URP 17.6.0, Input System 1.20.0, AI Navigation 2.0.15
**Builds on:** Milestone 003 (`7f7f70b`)
**Commit:** `105ec38`

## Summary

There is now a complete single-player match, **`Assets/Scenes/RTS_Match.unity`**, first in Build Settings and
loaded by the Windows build. The player (blue, south-west) faces a rule-based computer commander (red, north-east).

- The commander harvests, builds its base, produces Light/Heavy Tanks, defends, assembles and attacks.
- It acts only through the systems the player uses, with the same costs, placement rules, production queues and combat.
- Destroying the enemy HQ wins; losing yours is a defeat. Both in the same frame is a draw, and so is a stalemate where neither side can act any more.
- The result screen has a Restart button. An optional AI debug panel toggles with **F9**.

## Playing a match

The match starts with a Headquarters, Refinery, one Harvester and 2000 credits on each side. Each side has a
main (6000) and a secondary (4000) ore field at identical distances.

Controls are as in Milestones 001–003, plus **F9** for the AI debug panel. The commander typically:

1. builds a Power Plant and a Factory immediately (the same opening the planner prescribes for anyone);
2. adds harvesters (2 per refinery, max 4), then tanks;
3. assembles about 6 units at its rally point and attacks after roughly 3 minutes of game time;
4. defends when your units come near its base;
5. retreats and rebuilds after a failed attack;
6. builds an expansion refinery when its local ore runs out, and commits everything once no ore is left on the map.

Matches observed in testing last **6–12 minutes** of game time. All ore on the map is typically mined out by
about minute 5–6.

## Map & fairness

The map is **point-symmetric** about its centre:

- HQs, refineries, harvesters, ore fields and all nine obstacles mirror each other.
- Enemy buildings are rotated 180°, so docks and factory exits face the opponent on both sides.
- The commander places its own buildings facing its opponent too.
- The player's placement tool still builds facing north, which is towards the enemy for the player's base.

Verified by test: both factions start with equal credits and structures, dock-to-ore distances are equal to
0.01 m, and the expansion threshold applies to both sides identically. Commander site selection is
point-symmetric; a test checks the first 40 candidates mirror exactly.

## Commander architecture

**Pure rules** (`Scripts/Runtime/Core/AI`, `Core/Victory`, all EditMode-tested):

| Class | Responsibility |
|---|---|
| `CommanderModel` | `StrategicState` (Develop, Defend, Assemble, Attack, Recover), `CommanderSettings`, `CostTable`, `CommanderSnapshot`, `ArmyMath` (Light 1, Heavy 2, × health) |
| `StrategyRules` | State transitions (below) |
| `EconomyPlanner` | One economic step per decision, by priority: refinery → power → harvesters → factory → expansion → army. If the top priority is unaffordable it **saves** rather than spending on lower priorities. It never returns an action the faction can't pay for (2000 randomised snapshots tested) |
| `CompositionRules` | 40 % Heavies normally, 65 % when it has seen more enemy Heavies than Lights; Lights when Heavies are unaffordable; credit reserve kept except when defending |
| `TargetPriorities` | Attack objectives: factory > HQ > refinery > power plant, weighted by distance |
| `SiteSearch` | Candidate building sites, ranked by preferred distance and direction with point-symmetric tie-breaking |
| `BoundedLog` | Fixed-size ring buffer for the decision log |
| `MatchOutcomeRules` | HQ victory, simultaneous draw, exhaustion draw (`CanStillAct`) |

**State transitions (`StrategyRules`):**

- **Defend** pre-empts everything as soon as an enemy armed unit is visible near the base. Every other change waits for `MinStateDuration` (8 s), so the commander doesn't flip between states.
- **Develop → Assemble** once it has a refinery, a harvester, a power plant and a factory.
- **Assemble → Attack** at the attack size (6 × aggressiveness scaling) and strength threshold.
- **Attack → Recover** (retreat) below 35 % of the starting attack strength, or when every attacker is lost.
- **Recover → Assemble/Develop** once the economy and factory are back.
- **End game:** once no ore is left anywhere, it commits its whole army and doesn't retreat to "recover".

**Scene components:**

| Component | Role |
|---|---|
| `AI/EnemyCommander` | Each decision interval (1.5 s on Normal) it updates perception, picks the state, executes one planner step through `BuildingPlacementController.Validate/TryPlace` (the player's placement rules and purchase) or `VehicleFactory.Enqueue` (the player's queues and costs), and manages the army through `CombatUnit` orders. Works for any faction; the AI-vs-AI test runs one for each side. Exposes state, objective, counters and the decision log |
| `AI/AiPerception` | Vision-limited knowledge, see below |
| `Data/AiDifficulty` (`Data/AI/Normal.asset`) | Decision interval, strategy settings (aggressiveness, defence allocation, attack size/threshold, retreat fraction, harvester/refinery/factory/army caps, queue limit, reserve, power margin), vision, threat memory, defend radius, rally distance, stuck detection, log size. **No resource or damage modifiers exist** |
| `Match/MatchFlowController` | Victory/defeat/draw (evaluated in `LateUpdate` after all deaths that frame), exhaustion check every 2 s (two consecutive), pause, halt commanders, `Restart()` |
| `Match/MatchResultView` | Result screen with Restart |
| `UI/CommanderDebugPanel` | F9 debug view |
| `Match/FactionStartLocation` | Start positions; the AI uses its opponent's only if configured |
| `Match/FactionSpawner` | Instantiates buildings and units with the owner set before `OnEnable` |

**What the commander knows (`AiPerception`):** enemies within 22 m of its own units and buildings. It remembers
enemy structures until it sees the spot empty, and enemy units for 20 s (used for composition). Two pieces of
knowledge are explicit configuration:

- **`knowsEnemyStartLocation = true`**: standard 1v1 map knowledge, used to aim the first attack and the rally direction.
- **Ore field positions:** static map features.

**Tactics:**

- The army gathers in formation at a rally point 14 m from the HQ towards the enemy, using attack-move so it fights anything that comes close.
- An attack sends everything except the defence allocation (25 %, nearest home), via attack-move, to the best known structure, otherwise to the enemy start.
- Units that finish a leg attack a visible objective structure or continue towards it. Destroyed objectives trigger a new choice.
- **Blocked routes:** a unit that hasn't moved for 3 decisions (and isn't firing) gets a re-routed order. After 6 decisions it is sent home and logged. Unreachable explicit targets also use the Milestone 003 give-up logic.
- **Defence:** every unit attack-moves to the visible threat nearest the HQ. Production switches to units first, ignoring the reserve.
- Orders are only issued when something changes, so path requests in simulation stay at **0.04–0.24 per unit per second**.

## Fixes to earlier systems

1. **Owner set too late:** a new building's or unit's owner was assigned *after* `Instantiate`, so its `OnEnable` handlers briefly saw the default player faction. The player's sidebar then bound the **enemy's factory** as the player's production building, and units queued there would have been charged to the enemy. I found this from a screenshot of the built game. `FactionSpawner` now sets the owner before activation, the HUD re-checks ownership when buildings register, and a PlayMode test covers it.
2. `BuildingPlacementController.TryPlace` no longer posts "X constructed" to the player's HUD for computer factions.
3. Buildings can face north or south: footprint, apron, dock/exit point, factory spawn direction and rally default all follow the rotation. The player tool still builds facing north.
4. `RtsInputMode.ResetAll()` (used at match end/restart) and a `UnitMover.PathRequests` diagnostic counter were added.
5. The enemy faction now starts with 2000 credits (it had 0 as a test faction).

## Verification

### Automated — verified

| Check | Result |
|---|---|
| Compile | No errors or warnings |
| EditMode tests | **142 / 142 passed** (111 previous + 31 new): state transitions (immediate defence, min duration, develop/assemble/attack/retreat/recover, end-game all-in, no HQ), aggressiveness scaling, planner (opening, saving, harvester replacement, factory/power rebuild, expansion when local ore is exhausted, army cap and queue limits, reserve, defence priority, never unaffordable over 2000 random cases), composition, target priorities, point-symmetric site search, bounded log, army strength, victory/draw/exhaustion rules |
| PlayMode tests | **35 / 35 passed** (25 from Milestones 001–003 + 10 new) |
| Windows build | Succeeded; 0 errors; no project-code warnings. Starts in RTS_Match |
| Player smoke test | Ran 40 s with a clean log. A window capture shows the match HUD, the player base, credits rising, and no enemy factory leaking into the player's sidebar |

The 10 new PlayMode tests run in `RTS_Match`:

1. Equal start, colours and symmetric distances.
2. The AI harvests, builds a power plant and factory and produces tanks; every credit it gained came from ore deliveries; credits never went negative; all structures satisfy the placement rules.
3. The AI's buildings and units have the correct owner from their first event, and the player's sidebar ignores AI factories.
4. The AI enters Defend and destroys a raid on its base.
5. The AI attacks once assembled and **wins by destroying the player HQ**.
6. The AI rebuilds after losing all harvesters, then its refinery, then its factory.
7. Destroying the enemy HQ → Victory, AI halted, game paused; Restart → fresh match with no stale state.
8. Destroying the player HQ → Defeat.
9. Both HQs in the same frame → Draw.
10. **AI-vs-AI**: both sides build and produce, an attack happens, the match reaches an outcome, credits stay ≥ 0, unit counts stay under the cap (no runaway production), and path requests are well below per-frame rates.

**Fixture note:** where a test needs the AI further along than its opening (attack, recovery, isolation), the test
gives it extra credits or units and says so in the test. The commander code itself has no bonus paths.

### AI-vs-AI simulations (10× speed, identical Normal settings both sides)

| Phase | Runs | Faction 1 wins | Faction 2 wins | No result |
|---|---|---|---|---|
| Before the symmetry fixes | 7 | 0 | 4 | 3 (stalemates) |
| After building facing + site-search symmetry | 16 | 5 | 9 | 2 (stalemates; the exhaustion rule didn't exist yet) |
| Final code (+ owner fix, exhaustion draw) | 7 | 4 | 3 | 0 |

Winning games ended between 347 s and 724 s of game time; the last 11 runs (final code plus the runs just
before the owner fix) were all decisive. Two Editor play-mode matches at 3× speed (before the final fixes)
were also won by faction 2. **Either faction can win.** The earlier faction-2 lean came from layout and
start-up asymmetries that were found and fixed. The final sample is small (7), so mild residual bias can't be
ruled out.

### Full match through the real UI (Play Mode, simulated input) — verified

- In `RTS_Match`, **F9** toggled the AI debug panel. It showed the commander in Assemble with its credits, power, structures, planner step ("Save for ProduceHarvester") and recent decisions.
- I built a Power Plant and a Vehicle Factory via the sidebar and queued two Light Tanks, then let the match run at 4×:
  - The commander assembled 6 units by 2:21 and launched its attack at about 2:50 (5 attackers, the rest held home).
  - It advanced on my start location and destroyed my HQ at 3:57.
- The **DEFEAT** screen appeared ("Your headquarters has been destroyed. Match time 3:57"), the game paused, and the debug panel showed the commander **HALTED**.
- Clicking the **Restart match** button started a fresh match: time scale 1, result hidden, AI back in Develop, my credits 2000, no stale input mode.
- The console showed 0 warnings or errors across the session.

### Requires a human tester

- **A full human-played match to victory.** Player victory is verified by an automated test (destroying the enemy HQ), and AI behaviour by simulation and the observed loss above. I did not play a complete match where a human builds an army and defeats the AI through the UI.
- **Difficulty feel:** whether Normal is fun. The AI attacks after about 3 minutes with about 6 units and always knows your start location (configured). Ore runs out after about 5–6 minutes, which makes matches short; field sizes may need tuning.
- **The built `.exe`:** played interactively; only launch, rendering and the log were checked automatically.
- The sidebar covers part of the map at 1280×720 (known since Milestone 002).

## Known limitations

- **Single difficulty (Normal).** Other difficulties are possible by duplicating the `AiDifficulty` asset; there is no menu to choose one.
- **Strategy is rule-based and fairly predictable:** one rally point, attack-move waves, no scouting (it knows your start location by configuration), no flanking or retreat micro, no harassment of harvesters.
- **Defence only reacts to armed enemies** within 24 m of its structures. Unarmed intruders and harvester raids away from the base are ignored.
- **The player's own placement tool always builds facing north.** That suits the player's start, but there is no rotate key.
- **Exhaustion draw:** when nobody can act (no army, nothing queued, can't afford a unit, no way to earn), the match ends as a draw after two consecutive checks 2 s apart. It wasn't observed after the final fixes because all 7 final runs ended in a victory first.
- Outcome balance is supported by 7 final simulations; more runs are needed to quantify any residual bias.
