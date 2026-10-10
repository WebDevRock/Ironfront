# Milestone 003 — Combat System

**Date:** 2026-10-10
**Engine:** Unity 6000.6.5f1, URP 17.6.0, Input System 1.20.0, AI Navigation 2.0.15
**Builds on:** Milestone 002 (`26a945c`)
**Commit:** see `Docs/AI-Development-Log.md` (Milestone 003 entry)

## Summary

Basic RTS combat, integrated with the existing movement, selection, production, construction and economy
systems:

- Light and Heavy Tanks, produced at the Vehicle Factory.
- A reusable health/armour system on every unit and building, with health bars.
- Projectile weapons with cooldowns, range, line of sight and automatic target acquisition.
- Five orders: Move, Attack, Attack-Move, Stop and Hold Position.
- An enemy test faction with stationary and patrolling tanks and a destructible outpost.

Strategic enemy AI is deliberately not included.

## How to play

| Action | Input |
|---|---|
| Attack a unit or building | Select armed units, **right-click an enemy** (a red ring marks the target) |
| Move (no engaging on the way) | Right-click ground (green marker) |
| Attack-move | **F** or the *Attack-Move* button, then left-click a location (orange marker) or an enemy. Right-click / Esc cancels |
| Stop | **X** or *Stop* |
| Hold position | **H** or *Hold* |
| Produce tanks | Build a Vehicle Factory, then use *Light Tank* / *Heavy Tank* in the sidebar |

Notes:

- F, X and H were chosen because A/S/D are camera-pan keys.
- The orders panel (bottom centre) appears while armed units are selected. It shows targeting mode and the last order issued.
- The top-left panel shows a single selected unit's HP, armour, weapon stats and a plain-language current order, e.g. `Attacking Enemy Outpost - closing in`, `Attack-move - firing at Light Tank`, `Holding position`, `Idle (target unreachable)`.
- For several units it shows the HP total and a count of orders.
- Health bars appear over damaged or selected units and buildings.

### Enemy test scenario (faction 2, red)

Enemy tanks and the outpost are tinted red; player units are blue. All enemies sit north of the central wall,
away from the player's base and ore route.

| Target | Where | Behaviour |
|---|---|---|
| Enemy Outpost (1000 HP) | (32, 36) | Destructible building |
| Light Tank "West Sentry" | (−40, 16) | Hold position (fires back in range) |
| Light + Heavy Tank "Outpost Guards" | (25, 30), (38, 28) | Hold position |
| Light Tank "West Patrol" | (−32..−14, ~21) | Patrols with plain move orders (moving target) |
| Light Tank "East Patrol" | (22..42, 10) | Patrols with plain move orders |

Patrols and holding tanks use two small, clearly labelled test-scenario components (`Testing/PatrolRoute`,
`Testing/ScenarioStartOrder`). They issue normal orders and make no decisions, so this is not AI.

## Data (`Assets/Ironfront/Data`)

| | Light Tank | Heavy Tank |
|---|---|---|
| Cost / build time | 500 / 6 s | 900 / 10 s |
| Health / armour | 300 / 10 % | 700 / 35 % |
| Speed | 8 m/s | 5 m/s |
| Weapon | Light Cannon: 25 dmg, range 11 m, every 1.0 s | Heavy Cannon: 70 dmg, range 13 m, every 2.5 s |
| Look | Sand hull, round turret, single barrel | Dark hull, front plate, square turret, twin barrels |

Weapons (`Data/Weapons`) also define acquisition range, projectile speed and size, line-of-sight requirement
and friendly fire (off). Building health and armour are on each `BuildingDefinition`, e.g. HQ 2000 / 30 % and
Outpost 1000 / 10 %. The Harvester now has 600 HP / 30 %, and the capsule scouts 100 HP. `MatchSetup` holds
both factions and their colours, plus `CombatVisuals` (effect and marker materials).

The Milestone 002 "Tank" definition and prefab were **replaced** by Light/Heavy Tank and deleted; nothing
referenced them.

## Architecture

**Pure rules** (`Core/Combat`, EditMode-tested):

| Class | Rule |
|---|---|
| `HealthModel` | HP, armour, dies exactly once, overkill clamped |
| `DamageRules` | Damage × (1 − armour), armour capped at 90 %, minimum 1 damage. Hostility = different faction. Same-faction damage only with explicit friendly fire |
| `WeaponCooldown` | Never fires faster than the interval |
| `TargetSelector` | Prefers armed units, then units, then structures, then the nearest |
| `CombatOrderMachine` | The five orders and their transitions (below) |

**Order behaviour (`CombatOrderMachine`):**

- **Move:** travels and ignores enemies, then becomes Idle.
- **Idle and Hold:** fire at enemies in range with line of sight and never move. Hold persists until a new order; Idle is the default after any order completes.
- **Attack:** chases until the target is within *range − margin* and visible, halts, then fires as the cooldown allows.
  - It keeps firing up to full range (hysteresis, so there is no start/stop jitter) and re-approaches when the target leaves range or sight.
  - Re-paths only when the target has moved more than 2 m (at most every 0.5 s, at least every 3 s).
  - Gives up (*target unreachable*) if the unit itself has made no progress for 4 s, so it never stays stuck. A fleeing target doesn't trigger this.
  - Ends Idle when the target is destroyed.
- **Attack-Move:** scans the acquisition range, engages what it finds (dropping auto-targets that run beyond 1.5 × acquisition range), then resumes the advance. Unreachable targets are skipped and ignored for 6 s.
- **Stop:** halts and clears the order and target.

**Scene components** (`Scripts/Runtime/Combat`):

| Component | Role |
|---|---|
| `Health` | Health for units and buildings. Raises `Died`/`AnyDied`, plays an explosion and destroys the object. Distance and aim point use the nearest collider surface, so big buildings are attackable from their edge |
| `CombatUnit` | Implements the order machine's body using the existing `UnitMover` (NavMesh), the `Health` registry for targets, linecasts against scenery and structures for line of sight, and `Projectile` for firing; rotates the turret |
| `Projectile` | Visible homing shell; applies damage on arrival through `Health.ApplyDamage` (faction rules applied there); impact flash |
| `HealthBar` | Camera-facing bar, shown when damaged or selected; green → red |
| `CombatFx` | Placeholder primitive effects: muzzle flash, impact, explosion, order markers |
| `FactionColorizer`, `UnitIdentity` | Faction tint from `MatchSetup`; links a unit to its definition and applies its speed |

**Integration with earlier systems (extended, not replaced):**
- `UnitCommandController` now handles right-click attack, and routes move and formation orders through `CombatUnit` for armed units. It adds `IssueAttack`, `IssueAttackMove`, `IssueStop`, `IssueHold` and order events. Formation movement and harvest orders are unchanged.
- `SelectionManager` drops dying units and structures immediately. `RtsInputMode` gained a targeting mode so attack-move clicks never reach selection.
- The RTS input map gained `AttackMove` (F), `Stop` (X) and `Hold` (H). The new `OrderInputController`, `OrdersHud` and `OrderFeedback` handle shortcuts, buttons and markers, and `SelectionHud` shows combat details.

**Destruction cleanup:**
- Destroying a unit removes it from the selection (via `AnyDied`) and from every registry (`OnDisable`).
- A building also unregisters its power and prerequisites, and its `NavMeshObstacle` carve disappears.
- The factory list rebinds when a factory is destroyed.
- Projectiles headed for a dead target only play the impact, and attackers drop dead targets on their next tick.
- `Health.IsAlive` uses Unity's lifetime check, so references to destroyed objects read as dead rather than throwing.

## Verification

### Automated — verified

| Check | Result |
|---|---|
| Compile | No errors, no warnings |
| EditMode tests | **111 / 111 passed** (82 previous + 29 new): armour, damage, minimum damage, death once, friendly-fire rules, cooldown rate, target priority, and order transitions (move ignores enemies, idle fires without moving, attack close-in/halt/fire, target destroyed → idle, re-approach after target moves, no jitter inside the hysteresis band, re-path throttling, unreachable give-up, line of sight blocks firing, fire rate, attack-move engage → resume → arrive, attack-move skips unreachable, hold never pursues, hold needs line of sight, stop cancels attack/move, order replacement) |
| PlayMode tests (real scene) | **25 / 25 passed** (14 previous + 11 new) |
| Windows build | Succeeded, 0 errors, no project-code warnings |
| Player smoke test | Ran 15 s with a clean log. A window screenshot shows the scene, faction tinting and HUD rendering, and credits rising |

The 11 new PlayMode tests cover:

1. The scenario contents and red/blue tinting, with patrols moving.
2. Light and Heavy Tanks differing in every stat.
3. Friendly fire ignored, enemy fire mitigated by armour.
4. Single-unit attack: in range only, cooldown respected, target destroyed and removed from all registries, survivor still selected.
5. **Multi-unit** attack on a building: destroyed, unregistered from its faction, NavMesh carve removed.
6. Attack-move: engages a threat on the way, then reaches the destination.
7. Move passes an enemy in range without firing.
8. Hold: no fire out of range, no pursuit, kills a target in range without moving.
9. Stop cancels an attack and the unit comes to rest.
10. Line of sight: no shots through the wall; an attack order repositions around it and kills the target.
11. A moving target is tracked and destroyed, the next target is reacquired, and a selected unit's death cleans up selection, registries and the HUD with no errors.

All earlier Milestone 001/002 PlayMode tests still pass. The economy acceptance test now produces **one Light and one Heavy Tank** instead of two Tanks.

### Acceptance scenario through the real UI (simulated input, Play Mode) — verified

1. The Power Plant and Vehicle Factory were built via the sidebar and ghost placement.
2. Light Tank and Heavy Tank were queued with the sidebar buttons; both were produced and drove to the rally point.
3. A box-select gave "Selected units: 2 (2 armed)" and the orders panel appeared. **Right-clicking the West Sentry** issued "Attack x2". The tanks closed in and destroyed it, and the Light Tank took damage (300 → 165).
4. **F** entered attack-move targeting (status line shown); a left-click issued "AttackMove x2". The tanks met the West Patrol on the way, destroyed it, and finished at the destination.
5. **X** gave "Idle x2" and the tanks stayed put; **H** gave "Hold x2 / Holding position".
6. **Right-clicking the Enemy Outpost** showed the red target ring and the outpost's health bar, which fell 1000 → 717 → 528 → 276 → 87 → destroyed.
   - The outpost's Light Tank guard destroyed our damaged Light Tank. It vanished from the selection and lists without errors; the HUD switched to the surviving Heavy's details.
   - The Heavy then automatically engaged and destroyed the guard (target reacquisition).
7. The surviving Heavy (18/700 HP) obeyed a further move order. Credits kept rising throughout (economy unaffected).

The console showed 0 game warnings or errors.

Harness notes:
- Same harness as before. The Editor lost OS focus mid-session (I couldn't bring it to the foreground), which stopped injected input. Setting `Application.runInBackground = true` for the session fixed it. All harness changes were restored, and ProjectSettings are unchanged.
- Fights resolve quickly relative to my polling (each check takes about 1 s), so several steps were observed from their end state plus shot and HP counters rather than frame by frame.

### Requires a human tester

- Interactive combat in the **built `.exe`**: only launch, rendering and the log were checked.
- Feel and readability: projectile/explosion visibility, health bar size, the F/X/H key choice, whether fights resolve at a satisfying pace, and balance (the Heavy outranges the Light by design; an explicit building attack ignores the guards shooting back).
- Many-unit engagements (more than about 5 per side), and units squeezing past each other during chases.
- At 1280×720 the economy sidebar covers part of the play area (layout only).

## Known limitations

- Projectiles always hit their intended target (homing, no misses or splash). Line of sight is checked when firing, not during flight.
- Units without a weapon (scouts, harvesters) don't flee or return fire. Enemy tanks only act through Hold/Idle and the test patrols — no AI by design.
- An explicit Attack order focuses its target even while other enemies fire at the unit, which is standard RTS behaviour.
- Attacking the HQ or other player-faction objects isn't offered (right-click on friendly units still selects or moves), so friendly fire can't occur in play. It is only reachable through `allowFriendlyFire` on a weapon.
- The "unreachable" give-up uses a 4 s no-progress timer. A unit squeezed in a crowd for 4 s may give up and become Idle (it still fires at anything in range).
- All vehicles use the default NavMesh agent type (radius 0.5) for pathing, so the larger tanks can clip tight corners.
- Effects and health bars are simple primitives (final visual polish is out of scope).
