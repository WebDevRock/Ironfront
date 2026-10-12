# Milestone 009 — Design: Building Repairs, Vehicle Repair Pads and Queuing

## 1. Architecture review (what repairs plug into)

| System | Current state | Repair use |
|---|---|---|
| Health | `HealthModel` (pure): max, current, armour, damage, death once. `Health` (MonoBehaviour) wraps it; registry `Health.All`; events `Damaged`, `Died`, `AnyDied`. **No way to restore health.** | Add `HealthModel.Restore(amount)` (bounded by max, refused when dead) and `Health.Restore` + `Restored` event. |
| Economy | `FactionState.Wallet` (integer credits, `TrySpend`, `Changed`), `PowerGrid.IsLowPower`, `StructureRoster` (prerequisites). | Repairs spend through the same wallet; the pad checks the faction's power like the AT turret. |
| Construction | `BuildingDefinition` data → `BuildingPlacementController.TryPlace` (rules, purchase, instant spawn). Construction menu built at runtime from `MatchSetup.ConstructionOptions`. **No structure has a build time.** | Repair Pad is a normal `BuildingDefinition` in the options list. Its "construction time" is an assembly phase after placement (see D6). |
| Vehicles | Armed vehicles: `CombatUnit` → `CombatOrderMachine` (Move / Attack / AttackMove / Hold / Stop) → `UnitMover` (NavMeshAgent). Harvesters: `Harvester` state machine → `UnitMover`. | The repair client drives vehicles only through these: `OrderMove` / `OrderHold` / `OrderStop` for armed units, `StopHarvesting` + `UnitMover` for harvesters. |
| Commands | `UnitCommandController` (right-click: attack / harvest / move / rally), `OrderInputController` (attack-move, stop, hold) → `OrdersHud`. | New: right-click a friendly Repair Pad, `IssueRepair`, Repair [R] / Cancel buttons. Every other player command cancels a repair. |
| Selection / HUD | `SelectionManager` (units, one structure), `SelectionHud` (text), `OrdersHud` (armed units only), `EconomyHud` (build / production / research, built at runtime). | New `RepairHud` panel (built at runtime) for vehicles, buildings and pads. |
| AI | `EnemyCommander` + pure planners (`EconomyPlanner`, `ExpansionRules`, `StrategyRules`), `CommanderSettings` per difficulty. | New pure `RepairPlanner` rules; the commander issues the same `IssueRepair` / building-repair calls the player uses. |
| Presentation | `FxLibrary` particles, `GameAudio` + synthesized `SoundBank`, `ExpansionModels` procedural structures. | Pad model, sparks, status lamps, gantry animation, repair sounds — all driven by repair state, never computing it. |

## 2. Decisions

### D1. Data-driven balance: `RepairSettings`
A `RepairSettings` asset referenced by `MatchSetup` holds every number:

| Setting | Initial value | Meaning |
|---|---|---|
| `buildingCostFraction` | 0.5 | restoring 100 % of a building costs 50 % of its construction cost |
| `buildingRatePerSecond` | 0.01 (first 0.025) | fraction of max health restored per second (full repair in 100 s); lowered after the first AI-vs-AI study — at 2.5 % a besieged HQ regained 50 HP/s, half of a six-tank attack, and two matches stalled |
| `vehicleCostFraction` | 0.4 | restoring 100 % of a vehicle costs 40 % of its purchase price |
| `padRatePerSecond` | 0.06 | fraction of max health per second on a pad (full repair in ~17 s) |
| `padAssemblyTime` | 15 s | after placement, before the pad can service vehicles |
| timeouts | travel 120 s, stall 8 s / 1 m, 3 re-routes, enter 25 s, exit 20 s × 3 exit points | navigation recovery |

`BuildingDefinition` / `UnitDefinition` gain an optional `repairCostMultiplier` (default 1).

### D2. Cost proportional to health restored (`RepairMeter`, pure)
Cost per hit point = `costFraction × price × multiplier / maxHealth`. Each tick the meter restores up to `rate × max × dt`,
limited by missing health and by what the faction can afford. Credits are integers, so fractional cost is carried
between ticks and whole credits are charged as they accrue. If the next credit cannot be paid, nothing is restored
(**paused**); it resumes on its own once credits arrive. Total charged = round(total health restored × cost per HP),
never more.

### D3. Building repairs (`BuildingRepair`, one per building, added by `Building`)
Toggle on/off through the selection HUD (player) or the commander (AI). While on, damaged and alive: restore gradually
(also under attack), charge proportionally, pause without credits, switch off at full health. Destroyed buildings cannot
repair (the component dies with them). Status: Off / Repairing / Paused (no credits) / Complete.

### D4. Repair Pad and its FIFO queue (`RepairPadQueue`, pure)
* Building: 1000 credits, −20 power, requires a Vehicle Factory, 6 × 5 m body with an 8 m **apron**: the bay is on the
  apron (aprons are kept clear by placement rules and are not carved out of the NavMesh), so vehicles can drive onto it.
* Queue: tickets in arrival order; a vehicle is enqueued once (duplicates refused). **Exactly one** ticket can hold the
  bay grant. The grant passes to the next ticket only after the previous vehicle has **vacated** the bay (logical
  hand-shake *and* a physical check that no other vehicle stands in the bay).
* Removing a queued ticket shifts everyone behind it forward; removing the active one keeps the bay "vacating" until
  the vehicle is gone or dead.

### D5. Vehicle repair client (`RepairOrderMachine`, pure, explicit states)

```
None → TravellingToQueue → Waiting → EnteringPad → Repairing → ExitingPad → Completed
                 ↘ (any state) Cancelled
```
* **TravellingToQueue:** drive to the waiting slot for its queue position. On arrival → **Waiting**.
* **Waiting:** hold still. When the slot index changes (someone left), drive to the new slot. When granted → **EnteringPad**.
* **EnteringPad:** drive to the bay. Within 1.5 m → **Repairing**.
* **Repairing:** hold (it still returns fire in range); the pad restores health. Full → **ExitingPad**.
* **ExitingPad:** drive to an exit point clear of the entrance and the waiting line. Once outside the bay radius → vacate
  → **Completed**.
* **Cancelled:** pad destroyed, vehicle given another order, cancel button, route blocked, timeouts. The ticket is
  always removed; if it held the bay, the bay is marked vacating.

Unexpected transitions (the pad vanishes or loses its ticket, or the vehicle dies) are handled at every tick, not only
at events.

**Navigation recovery, no teleporting:**
* *Stall:* less than 1 m of progress in 8 s → re-route. After 3 re-routes, or an incomplete NavMesh path, the order is
  cancelled with "route to Repair Pad blocked".
* *Entry:* 25 s timeout → give up the bay (cancelled, the next vehicle is granted).
* *Exit:* 20 s per exit point, 3 exit points. After that, the vehicle is released where it stands. The bay stays
  unavailable until it is physically clear, and the pad orders any idle friendly vehicle blocking it to move away.

### D6. Pad "construction time"
Ironfront places every structure instantly once paid for. Adding build times to all structures would rewrite working
construction and is out of scope. The pad meets the requirement on its own: after placement it spends
`padAssemblyTime` assembling its gantry (shown as progress) and only then services vehicles.

### D7. Pausing
A pad pauses (keeps its queue) while assembling, while the faction is on low power (like the AT turret), and while
credits are insufficient. Vehicles keep waiting; nothing is lost.

### D8. Commands
* **Repair [R]:** sends the selected damaged friendly vehicles to the **best pad**. Best = lowest estimated wait,
  i.e. travel time plus queue length × average repair time (nearest when idle).
* **Right-click a friendly Repair Pad:** sends them to that pad (manual choice).
* **Cancel repair:** cancels any outstanding repair orders in the selection.
* **Any other order** (move, attack, attack-move, stop, hold, harvest) cancels the repair. The hook sits in
  `CombatUnit.Order*`, so it covers player, AI and scripts alike. A new repair order to another pad transfers the
  vehicle cleanly (cancel, then enqueue).
* **No pad:** "No Repair Pad — build one (requires a Vehicle Factory)". Building selected: **Repair** toggle and status.
  Pad selected: assembly / power / queue length / current vehicle and its progress.

### D9. AI (`RepairPlanner`, pure; same API and costs as the player)
* **Build** a pad (up to `MaxRepairPads` per difficulty) once the base is ready, the army is ≥ 6 and it is affordable
  without the reserve.
* **Send** a vehicle when its health is below `VehicleRepairThreshold`, it is not attacking, and either the commander is
  not defending or the vehicle is badly damaged (< 25 %). Never more than half the army at once, and only while credits
  exceed the reserve plus the estimated repair cost.
* **Repair buildings** (HQ, factory, refinery, power plant, research centre, defences) below `BuildingRepairThreshold`
  while credits exceed the reserve plus 300; stop when they fall below the reserve so production is not starved.
* Choose a pad with the same best-pad rule. Recover automatically when a pad dies (vehicles cancelled, re-sent later).
* Difficulty: Easy (1 pad, 35 %), Normal (1 pad, 45 %), Hard (2 pads, 50 %, building threshold 75 %).

### D10. Presentation (separate from the rules)
The pad model: concrete apron with hazard-striped entry and exit lanes, a gantry with welding arms and a status lamp —
green when available, amber when occupied, red when unpowered, flashing blue while assembling. While repairing it emits
sparks and plays weld/ratchet sounds, and the arms sweep. Building repairs emit sparks at random points with the same
sound. All of this is driven by repair events and states, never the other way round.

## 3. Exceptional conditions (summary)

| Condition | Behaviour |
|---|---|
| Queued vehicle destroyed | ticket removed, everyone moves up |
| Vehicle destroyed in the bay | grant released, bay vacated |
| Pad destroyed | every client cancelled ("Repair Pad destroyed"); no references kept |
| Pad loses power | active repair pauses, queue kept |
| Player cancels | ticket removed; vehicle stops where it is |
| New move or combat order | repair cancelled, the new order runs |
| Route blocked | re-route ×3, then cancelled |
| Waiting slot unavailable | slots re-validated each second; next valid slot used |
| Credits run out | paused until credits arrive |
| Vehicle cannot leave the bay | other exit points, then released; the bay stays blocked until physically clear |
| Match ends | everything freezes with the match (time scale 0); no pending effects |
| Restart | scene reload; statics reset on subsystem registration; no persistent references |

## 4. Testing plan
EditMode: meter (cost, gradual, max, insufficient credits, carry), queue (FIFO, duplicates, one active, vacating
hand-shake, removal, transfer, 10+ tickets), order machine (every transition, timeouts, navigation failure), AI rules.
PlayMode, in a real map: 10 vehicles at one pad (one in the bay at a time, FIFO, all repaired, credits ≈ health × rate),
two pads independent, building repair on/off with credits and pausing, power loss/restore, pad and vehicle destruction,
cancel and transfer, new orders cancel, blocked route, restart cleanup, AI builds and uses pads. AI-vs-AI study with
repairs on several maps and seeds compared against M008.
