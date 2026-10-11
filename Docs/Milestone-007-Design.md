# Milestone 007 — Design: Content Expansion, Technology and Strategic Depth

**Date:** 2026-10-11 · **Baseline code:** `9f7dcd9` (end of Milestone 006)

**Baseline data:**
- `Docs/Balance/M007/baseline_runs.jsonl`: one JSON record per match.
- `Docs/Balance/M007/baseline_summary.txt`.

## 1. Gameplay audit (Milestone 006 state)

### Systems inspected

| System | Contents |
|---|---|
| **Units** | Harvester (800, unarmed), Light Tank (500, 300 HP, 10 % armour, speed 8, 25 dmg/1 s, range 11), Heavy Tank (900, 700 HP, 35 %, speed 5, 70 dmg/2.5 s, range 13) |
| **Structures** | HQ, Refinery (1000), Power Plant (500, +50 power), Vehicle Factory (1300); the sandbox-only outpost |
| **Economy** | Ore fields (6000 / 4000), 1 credit per ore, power deficit halves production |
| **Combat** | Homing shells, armour as a damage fraction, retaliation, attack-move, hold, line of sight against obstacles and structures |
| **AI** | Rule-based commander. Develop → Assemble → Attack → Recover, plus Defend. It sees only within 22 m of its own objects, remembers structures, and builds by fixed priorities (refinery → power → harvesters → factory → expansion → army). The army mix aims at 40 % Heavy (65 % if the enemy is heavy) |
| **Visibility** | None for the player. The whole map and every enemy are always visible, and the minimap shows all enemy units (M005 known limitation). The AI was already vision-limited |

### Baseline statistics

The baseline is 10 AI-vs-AI matches (Normal vs Normal, seeds 11 / 23 / 37 / 51 / 73 × both layouts, 10× speed) on M006 code. The balance study was extended to record composition, spending and damage by unit type.

| Metric | Baseline |
|---|---|
| Wins by faction | F1 5, F2 5 |
| Wins by start | **North-east 8, south-west 2** |
| Timeouts / deadlocks | 0 |
| Match length | mean 380 s (272–559 s) |
| Income per faction | mean 9,838 credits |
| Spending per faction | mean 11,525 credits |
| First attack | 152 s for every faction, every match |
| Units produced (both sides, 10 matches) | **257 Light Tanks, 1 Heavy Tank**, 57 harvesters |
| Structures built (both sides, 10 matches) | 20 Power Plants, 19 Refineries, 20 Factories: an identical build in every match |
| Damage dealt by type | Light Tanks 144,254; Heavy Tanks 672 |

### Gaps in strategic choice

1. **One-unit armies.** The planner buys a Light Tank whenever a Heavy is not affordable, so it never saves the 1,200 credits a Heavy needs (900 plus the 300 reserve). The composition rule effectively never fires, and there is no rock-paper-scissors.
2. **One build order.** Every match builds the same three structures in the same order. There is nothing to invest in except more tanks.
3. **No way to defend without an army.** Bases have no static defences, so harassment can only be answered by pulling the main army home.
4. **No information game.** The player sees everything, so scouting, deception and early warning have no value. The asymmetry also favours the human: the AI is vision-limited but the player is not.
5. **No sieges.** Nothing outranges anything else (ranges 11–13), so every fight is a straight brawl decided by numbers.
6. **No long-term investment.** No upgrades; the first attack lands at the same time in every match.
7. **Start-position bias** (north-east 8/10 here; 13/20 in M005). This is not caused by new content. It is tracked again after the changes; see the report.

## 2. Design goals

Each addition must create a decision, not just more units:

| Addition | Decision it creates |
|---|---|
| Recon Vehicle + fog of war | Spend on information or on force? Information enables everything else. |
| Rocket Artillery | Siege power that is helpless up close: escort it and spot for it, or lose it. |
| Guard Tower | Cheap insurance against scouts and raids, useless against a heavy push. |
| Anti-Armour Turret | Counters heavy tanks, costs power, and loses to artillery. |
| Research Centre + upgrades | Delay army size now for efficiency later; tech timing becomes a strategy. |

## 3. Unit and structure statistics

Existing units and structures keep their M003/M004 combat values.

### Weapons and effectiveness

Damage = base damage × class multiplier × research bonus, then the target's armour applies. Armour classes: **Light** (light tank, recon, artillery), **Heavy** (heavy tank, harvester), **Structure**.

| Weapon (carrier) | Damage | Cooldown | Range | vs Light | vs Heavy | vs Structure | Notes |
|---|---|---|---|---|---|---|---|
| Light Cannon (Light Tank) | 25 | 1.0 s | 11 | 1.0 | 1.0 | 1.0 | unchanged |
| Heavy Cannon (Heavy Tank) | 70 | 2.5 s | 13 | 1.0 | 1.0 | 1.0 | unchanged |
| Recon MG (Recon) | 9 | 0.5 s | 9 | 1.0 | 0.35 | 0.25 | harassment only |
| Rocket Salvo (Artillery) | 120 (3 rockets) | 6.0 s | **9–28** | 0.7 | 1.0 | **2.0** | ballistic (dodgeable), 3 m splash, no line of sight needed |
| Twin MG (Guard Tower) | 14 × 2 | 0.45 s | 12 | 1.0 | 0.35 | 0.3 | about 62 DPS vs light |
| AT Cannon (AT Turret) | 95 | 2.4 s | 15 | 0.6 | **1.5** | 0.5 | offline on low power |

### Units

| Unit | Cost | Build | HP | Armour | Speed | Sight | Requires | Role |
|---|---|---|---|---|---|---|---|---|
| Recon Vehicle | 450 | 5 s | 180 | 0 % | **12** | **28** | Factory | Scout, spotter, early warning |
| Rocket Artillery | 1100 | 12 s | 260 | 5 % | 5.5 | 12 | **Research Centre** | Siege; needs spotters and escorts |
| Light Tank | 500 | 6 s | 300 | 10 % | 8 | 15 | Factory | All-rounder; artillery hunter |
| Heavy Tank | 900 | 10 s | 700 | 35 % | 5 | 14 | Factory | Line breaker |
| Harvester | 800 | 10 s | 600 | 30 % | 6 | 10 | Factory | Economy |

### Structures

| Structure | Cost | Footprint | HP | Armour | Power | Sight | Requires |
|---|---|---|---|---|---|---|---|
| Guard Tower | 400 | 3×3 | 800 | 25 % | 0 | 16 | HQ |
| Anti-Armour Turret | 900 | 3×3 | 1000 | 40 % | −25 | 16 | Vehicle Factory |
| Research Centre | 1200 | 5×5 | 1000 | 20 % | −30 | 10 | Vehicle Factory |

Existing sight ranges: HQ 18, Factory 11, Refinery 10, Power Plant 9.

### Technology

Research is one topic at a time per faction, paid in full when it starts. It can be cancelled with a full refund. Low power halves its speed, and it pauses if the faction has no Research Centre.

| Topic | Cost | Time | Effect | Requires |
|---|---|---|---|---|
| Composite Armour | 1000 | 45 s | +10 armour points for all vehicles, existing and new | Research Centre |
| Enhanced Weapon Systems | 1200 | 50 s | +20 % damage for vehicles and defences | Composite Armour |
| Advanced Harvesting | 800 | 35 s | Harvest and unload rates +40 % | Research Centre |

Rocket Artillery production also requires a Research Centre, so the Research Centre is the gate to the siege tier.

### Counter matrix

How well the row unit fares against the column unit:

| Row vs column | Light | Heavy | Recon | Artillery | Guard Tower | AT Turret |
|---|---|---|---|---|---|---|
| **Light Tank** | = | − | + | **++** (closes inside 9 m) | − | − |
| **Heavy Tank** | + | = | + | + | + (MG does 35 %) | **−−** (AT does 150 %) |
| **Recon** | − | −− | = | + if unescorted | −− | − |
| **Artillery** | + at range | + at range | − (fast) | = | **++** (28 m vs 12 m) | **++** (28 m vs 15 m) |
| **Guard Tower** | + | − | **++** | −− (outranged) | | |
| **AT Turret** | = | **++** | + | −− (outranged) | | |

## 4. Fog of war

**Model.** Each faction has a grid over the 100 × 100 m map with 1 m cells. Every 0.1 s the "visible now" layer is rebuilt by stamping each unit's and structure's sight circle; the "explored" layer only ever grows. An entity is visible to a faction if its centre (structures: centre plus four footprint corners) is in a visible cell. Attackers are revealed to the faction they hit for 2.5 s.

**Rules.**

| Topic | Rule |
|---|---|
| **Targeting** | Automatic targeting only considers visible enemies. A target out of sight for more than 1.5 s is dropped. Attack orders can only be given against visible enemies. |
| **Presentation** | Enemy units are drawn only while visible. Enemy structures are drawn once they have been seen ("remembered"). A structure destroyed out of sight leaves a "last known" ghost until the spot is seen again. Enemy health bars and destruction notices appear only while the enemy is in sight. Effects and positional sounds inside the fog are not played. |
| **Minimap** | Unexplored areas are black and explored areas dimmed. Enemy units are shown only while visible, remembered structures always, and ore only once explored. |
| **AI** | The commander perceives exactly what its faction's grid shows: the same system and the same rules as the player. Remembered structures stay known until seen gone. |

**Rendering choice: a URP full-screen pass.** A hand-written shader reconstructs each pixel's world position from the depth buffer and darkens it by the fog texture. The fog therefore covers obstacles, structures and props correctly, with no parallax error, in a single pass.

| Rejected alternative | Why |
|---|---|
| Overlay quad | Tall objects poke through it; the edges shift with camera angle |
| Per-material shader changes | Every material would need a custom shader |
| URP decal projector | Would have needed new renderer features and decal-ready materials |

**Trade-offs.**
- Sight has no occlusion: walls do not block vision.
- Visibility is grid-quantised (1 m) and refreshed at 10 Hz, so a unit can be seen up to 0.1 s late.
- Remembered structures show their last known look, not their last known health.

**Cost.** Each tick stamps about 600–2,500 cells per revealer and clears 2 × 10,000 bytes, so it scales linearly with unit count. It is measured in the M007 benchmark.

## 5. AI changes (same architecture)

- **Perception:** uses the shared vision system. It also tracks enemy artillery and defences and "remembers" structures until it sees them gone, which closes an M004 leak where it read the health of hidden structures.
- **Planner** (new `ExpansionRules`, inserted after "have a factory"):
  - keep one recon once 2 tanks exist;
  - build an AT turret when 2 or more enemy Heavies have been seen;
  - build a Guard Tower when the base was attacked in the last 60 s, or one as early insurance;
  - build a Research Centre once the army has 5 units;
  - research when it is affordable without touching the reserve: harvesting first with 2 or more harvesters, then armour, then weapons (tuned from 3 after the balance study).
- **Composition:**
  - save for the wanted Heavy Tank or artillery instead of always buying a Light Tank (fixes gap 1; the light fallback remains while defending);
  - artillery reaches about 25 % of the army once a Research Centre exists and enemy defences are known, or the army has 6 or more units;
  - fewer Heavies, more fast Light Tanks, while enemy artillery is known.
- **Tactics:**
  - scouts explore the nearest unexplored area with a bias towards the enemy side, then watch the approach, and withdraw when damaged;
  - attack objectives give defences high priority;
  - artillery attack-moves to a stand-off point 22 m from the objective;
  - up to three fast units hunt enemy artillery seen near the base.
- **No cheating:** same costs, placement validation, production queues, research rules and vision as the player.

## 6. Success criteria for the balance study

- **Health:** no deadlocks or timeouts, and match lengths in the same band or slightly longer.
- **Diversity:** every new unit, structure and topic is used by the AI in most matches, and Heavy Tanks appear in significant numbers.
- **Viability:** artillery deals a meaningful share of structure damage, and defences kill units.
- **Fairness:** win rates by faction and by start position are not made worse by the new content.
