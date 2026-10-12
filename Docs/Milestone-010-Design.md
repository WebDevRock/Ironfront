# Milestone 010 — Design: Construction Territory, Building Sales and Mobile Command Vehicles

**Base:** `9dc7a75` (Milestone 009 committed and verified: EditMode 264/264, PlayMode 74/74, repair pads, queues and
building repairs working; see `Docs/Milestone-009-Report.md`).

## 1. Architecture review

| System | Current state | Milestone 010 use |
|---|---|---|
| Placement | `BuildingPlacementController.Validate(def, centre)` → pure `PlacementRules.Evaluate` (map bounds, structures + clearance, aprons, obstacles, ore, units) then NavMesh checks. **Faction-agnostic**: any faction can build anywhere on the map. `TryPlace` checks prerequisites/credits, validates, charges, spawns. The AI's `FindSite` ranks candidates and calls the same `Validate`. | A faction-aware overload adds the territory rule. `TryPlace` always applies it, so the player, the AI and scripts cannot bypass it. New `PlacementResult` values `OutsideTerritory` and `EnemyTerritory`. Map validation (`ValidateTerrain`) is unchanged. |
| Economy | `ResourceWallet` (integer credits), `PowerGrid`, `StructureRoster` per `FactionState`; `Building` registers/unregisters with its faction in `Start`/`OnDisable` and raises `AnyRegistrationChanged`. | Refunds go through `Wallet.Add`. Territory listens to `AnyRegistrationChanged`, so a sold, destroyed or deployed command structure updates territory without polling. |
| Production / research | `ProductionQueue`: paid when queued, **full refund on cancel**. `ResearchState`: paid at start, full refund on cancel, pauses without a facility. | Sale rules reuse these refunds (§4). |
| Health / repair | `HealthModel`/`Health` (death → `Destroy`), `BuildingRepair` per building, Repair Pads for vehicles (M009). | The FCC can be repaired in place; the MCV at a pad; dismantling switches repairs off. |
| Units | `UnitMover` (NavMeshAgent), `SelectableUnit`, `UnitIdentity`, `Health`, `VisionEntity`; armed units add `CombatUnit`. Unarmed units (harvesters) are moved directly through `UnitMover`. | MCV = unarmed vehicle with a `MobileCommandVehicle` component. During deployment it locks its `UnitMover`, so no order from any source can move it. |
| Victory | `MatchFlowController`: Headquarters mode (HQ destroyed = defeat), Annihilation (no HQ/factory and no armed unit). | Headquarters mode unchanged; Annihilation counts FCCs as production structures (§7). |
| AI | `EnemyCommander` + pure planners (`EconomyPlanner`, `ExpansionRules`, `StrategyRules`, `RepairPlanner`), perception and `EnemyIntel` (only what it has seen). Refineries at any explored ore field, anywhere. | Must respect territory. Remote fields need a forward base: new pure `ForwardBaseRules`; MCV escort/deploy execution in the commander. |
| Fog | Per-faction `VisibilityGrid`; full-screen fog pass before post-processing; `VisionSystem.CanSee/IsExplored`. | Exclusion zones apply only to enemy command structures the faction **has seen** (§2.4), so territory never reveals hidden bases. |
| Maps | 400–600 m skirmish maps, bases cleared to ~46 m, home ore fields ~29 m from the HQ (refinery sites up to ~47 m); 100 m sandbox `RTS_Match`. | Revised: a 10 m build range from every structure; the home refinery site (~10 m from the HQ) is in range from the start. |
| UI | Placement ghost (green/red), `PlacementHint`, `EconomyHud` construction menu, `SelectionHud`, M009 `RepairHud` panel (self-installing). | Territory overlay during placement, reason text, Sell / Deploy buttons in the M009 command panel, minimap territory outline. |

## 2. Construction territory

> **Revised after the director's playtest (2026-10-12).** The first version gave fixed circles around command
> structures only (HQ 55 m, FCC 40 m). The director asked for the build area to stem from the **nearest building**,
> with a much smaller range. The rules below are the revised ones. The brief's "ordinary structures must not extend
> territory" is deliberately superseded.

### D1. Data
* `TerritorySettings.buildRange`: **10 m**, measured edge to edge. Every structure grants it.
  `BuildingDefinition.buildRangeOverride` can change it per structure (0 = the default; none is set).
* `BuildingDefinition.exclusionRadius`, on command structures only:

| Structure | Build range | Enemy exclusion radius |
|---|---|---|
| Headquarters | 10 m | 70 m |
| Forward Command Centre | 10 m | 50 m |
| every other structure | 10 m | 0 |

### D2. Model (`Core/Territory/TerritoryMap`, pure)
Anchors: (id, faction, footprint rectangle, build range, exclusion radius). Every structure is an anchor.
* `Covers(faction, rect)`: true when the **gap** between the new footprint (body and apron) and the nearest of that
  faction's structures is at most that structure's range (exact rectangle–rectangle distance). So a placement only has
  to reach into the shaded area, and each new structure extends the area: bases grow outwards building by building.
* `Gap(faction, rect)`: how far outside the range a footprint is (≤ 0 inside), and which structure is nearest.
* `Excluded(faction, rect, isKnown)`: true if the footprint intersects the exclusion circle of any other faction's
  command structure that `isKnown` accepts (exact rectangle–circle distance test).
* `Outline(area, range)`: the rounded outline of a grown footprint, for the overlay and minimap.
* Updated through `Add/Remove`; increments a `Version` the overlay uses to rebuild only on change.
* Never shared between factions.

### D3. Runtime (`Buildings/TerritorySystem`)
A static registry. A structure becomes an anchor:
* **as soon as it is placed** (`BuildingPlacementController.TryPlace` / `PlaceDeployed`), so two placements in the same
  frame can build on each other;
* or when it registers with its faction (scene start).

It stops being one when it is disabled or destroyed, also before it registered (sold, destroyed, unloaded). Territory
changes raise `Changed(faction)`; there is no per-frame work. Structures cut off when a neighbour is sold or destroyed
keep working and keep their own range.

### D4. Fog-safe exclusion
An enemy command structure's exclusion zone blocks construction and deployment for a faction **only once that faction
has seen the structure**. The system samples `VisionSystem.CanSee` for each anchor every 0.5 s, per faction, and
remembers sightings for the match. Without this, an "enemy territory" rejection would reveal a hidden base, and the AI
would get hidden knowledge. A structure stays "known" after it is destroyed, until it is removed from the registry.

### D5. Placement order
bounds → **territory** (inside a known enemy zone → `EnemyTerritory`; too far from own structures →
`OutsideTerritory`, "Too far from your structures (outside build range)") → structures/obstacles/ore/units → NavMesh.

## 3. Construction territory UI
* **Overlay** (`TerritoryOverlay`, shown only in placement or deploy mode):
  - a translucent cyan fill over the build area: every structure's footprint grown by 10 m, in 1 m cells, so overlaps
    never double-tint;
  - a bright cyan ribbon along the outer edge of the union;
  - red ribbons around **known** enemy exclusion zones.

  It is drawn after the fog of war by `TerritoryOverlayPass`, so it stays readable over unexplored ground, and is rebuilt
  only when the territory changes.
* **Ghost**: green/red as before. `PlacementHint` shows the reason: "Too far from your structures (outside build range)",
  "Inside enemy territory", "Too close to another structure", "Blocked by terrain", "Too close to an ore deposit"…
* **MCV selected:** the FCC footprint at the vehicle, and a dashed outline of the 10 m area it would grant.
* **Minimap**: the outline of the player's build area, dotted.
* Enemy territory is never drawn unless that structure has been seen.

## 4. Building sales

### D6. Rules (`Core/Economy/SaleRules`, pure, deterministic)
`Refund(saleValue, currentHealth, maxHealth, fraction = 0.5) = floor(saleValue × fraction × clamp01(current / max))`.
`saleValue` = construction cost, or a definition override for structures that are not bought (FCC: the MCV price, 5000).
* **Not sellable:** the Headquarters. It has no price, and in Headquarters mode selling it would be self-defeat.
* **Confirmation** (second click within 3 s): FCC, Vehicle Factory, Refinery, Research Centre.
* **Duration:** 8 s of dismantling (`TerritorySettings.sellSeconds`). The building keeps working until the end. Damage
  taken while dismantling lowers the refund (health is read at completion).
* **Cancel** at any time before completion: nothing is charged or refunded.
* **Once only:** a `BuildingSale` state machine `Idle → Dismantling → Sold` (or back to `Idle`). A second request while
  dismantling is refused. The refund is paid in the one transition to `Sold`, and the object is removed right after.
* **Destroyed while dismantling:** no refund (the component dies with the building).
* **Queued production:** selling a factory refunds its whole queue at full price (items are paid when queued; the same
  as the cancel button). **Research:** selling the faction's last Research Centre cancels the topic in progress with a
  full refund (the same as the cancel button). With another centre left, research continues.
* **Repairs:** dismantling switches in-place repair off and refuses it until cancelled. Selling a Repair Pad cancels its
  clients exactly as its destruction does.
* **Cleanup:** removal goes through the same path as destruction: `OnDisable` unregisters structure and power, the
  NavMesh obstacle disappears, territory updates. No death event, so it is not counted as a loss.
* **Exploit review:** refund ≤ 50 % of value and in proportion to health. Repair costs 50 % of value per 100 % health,
  so repairing and then selling gains nothing. MCV 5000 → FCC → sale ≤ 2500.

## 5. Mobile Command Vehicle

| Property | Value |
|---|---|
| Cost / build time | 5000 credits / 45 s, Vehicle Factory |
| Health / armour | 2000 HP, 0.35 (heavy class) |
| Speed | 3.5 m/s (heavy tank 5, light tank 8) |
| Weapon | none |
| Sight | 16 m |

It uses the normal movement, selection, faction and damage systems and is repaired at a Repair Pad like any vehicle.
Model: a long six-wheeled carrier with a folded command module, mast and outriggers in the faction livery.

## 6. Deployment

### D7. Validation (`Core/Construction/DeployRules` + placement)
The FCC footprint is centred on the MCV's current position (snapped to the 1 m grid). Checks:
* map bounds;
* structures and clearance, aprons, obstacles, ore;
* other units in the footprint (the MCV itself excluded) → "Units in the way";
* NavMesh under the centre and all four corners within 0.6 m height, to catch slopes and holes → "Ground too uneven or
  blocked";
* known enemy exclusion → "Inside enemy territory".

Own territory is **not** required.

### D8. State machine (`MobileCommandVehicle`)
`Mobile → Deploying (15 s) → Converted`, and `Deploying → Mobile` on cancel or failure.
* Deploy: validate. If valid, stop, lock the `UnitMover` (move orders are refused) and cancel any repair order.
  Progress is shown on the vehicle and in the HUD. The vehicle stays a normal target.
* Cancel deploy: unlock; it can drive again.
* Completion: re-validate (structures, bounds, exclusion, ground; units ignored, since carving pushes them away).
  - Valid: spawn the FCC through `BuildingPlacementController.PlaceDeployed`, which runs the same validation but does
    not charge. Then remove the MCV. A `_converted` guard makes this happen exactly once.
  - Invalid: back to `Mobile`, with a message.
* **Health conversion:** FCC health fraction = MCV health fraction at completion, at least 10 %.
* Destroyed while deploying: normal death; nothing is spawned (the component dies with the vehicle).
* Selection: if the MCV was selected, the FCC becomes the selected structure.
* FCCs cannot be packed up again.

## 7. Forward Command Centre
`forward_command`: 9 × 9 m, 2500 HP, armour 0.3, +10 power, sight 30 m, build range 10 m like every structure / exclusion 50 m,
sale value 5000. Not in the construction menu (only from an MCV). It can be attacked, repaired in place and sold.
It does not produce MCVs or units.

**Victory:**
* **Headquarters mode:** unchanged. Only the original HQ counts; destroying it defeats a faction even if it holds
  FCCs. The setup and help texts say so.
* **Annihilation:** the FCC counts as a production structure (with credits it can rebuild factories), so a faction
  holding one is not eliminated, and its forces are not revealed by the last-stand rule. MCVs are unarmed and do not
  count, so a hidden MCV cannot stall a match.

## 8. AI

Same rules, prices, territory and deployment as the player. No free MCVs, no instant deployment, no hidden knowledge.

* **Territory compliance:** `FindSite` validates candidates with the faction-aware rules. Refinery deposits are chosen
  only where a refinery site lies in territory.
* **When an MCV is needed** (`ForwardBaseRules.Next`, pure; final values after the study, see the report), all of
  the following:
  - an MCV is available (cost > 0), forward bases < `MaxForwardBases`, and no MCV alive or queued;
  - more income wanted (refineries < cap) but **no refinery site in territory**;
  - an explored, reachable ore field outside territory is known (the expansion target);
  - a factory and an army of `ForwardBaseArmy` (3–4), and not defending;
  - match time ≥ `EarliestForwardBase`;
  - fewer than `MaxMcvLosses` MCVs lost, with a cooldown after each loss.

  When the home field is exhausted **and** the target is a long haul (≥ 70 m from any own refinery), it **saves** for the
  vehicle. Harvesters gather from any field, so a shorter haul does not justify one; then it buys one only from a clear
  surplus (reserve + 1000). The rule comes before defences and research in the planner, and repairs do not spend the
  credits being saved.
* **Site:** near the target field, at least 90 m from every known enemy structure, closer to its own base than to the
  known enemy, and passing the deploy check. Fields are ranked by ground distance.
* **Escort:** up to 3 army units follow the MCV while it travels and guard it while it deploys.
* **Retries:** at most 3 positions per site and 2 sites per MCV; a 240 s travel timeout; failed sites are blacklisted.
  An MCV with no site parks at the rally point.
* **Forward infrastructure:** once the FCC stands, the normal refinery rule builds at the now-reachable field. A guard
  tower is built at each undefended FCC. The existing defence logic already treats threats near *any* own structure
  as base threats, so the FCC is defended.
* **Selling** (`SaleRules`, pure):
  - a refinery with no ore left within 120 m, while another refinery exists (obsolete);
  - an FCC with no ore within 120 m and no factory in its territory (obsolete; frees the slot);
  - an FCC below 25 % health under attack with no own army nearby (compromised; salvage the refund).
* **Territory loss:** handled implicitly. Sites are re-validated against the current territory each decision.
* **Difficulty (final):** forward bases standing at once Easy 1 / Normal 2 / Hard 3. Earliest 420 / 240 / 180 s. Escort
  army 4 / 3 / 3. MCV losses capped at 2. On large maps, each refinery feeds one more harvester (refineries need
  territory now).

## 9. Presentation
* **Territory:** cyan fill and boundary during placement and deployment; red known-enemy zones; minimap outline.
* **Sale:** a progress bar over the building, sinking and dissolving parts (the model lowers into the ground), dust,
  a ratchet/dismantle sound; a credit chime on completion.
* **MCV:** dust and suspension motion (`VehicleMotionFx`); while deploying, outriggers extend and the module rises,
  with a deployment hum and a progress ring on the ground.
* **FCC activation:** a construction flash (`ConstructionFx`), an activation sound, and the notification
  "Forward Command Centre established — new construction territory".
* **Invalid attempts:** red ghost and footprint with the reason; error sound.
* **Territory loss:** the notification "Construction territory lost" and the overlay update.

Effects only read the state; they never change it.

## 10. Testing plan
* **EditMode (pure):**
  - territory: radius, full footprint, overlap, exclusion, known-only, removal;
  - refund: values, rounding, health, unsellable;
  - sale state machine: duplicate, cancel, death;
  - queue cleanup refunds;
  - deploy rules and state machine: valid, invalid, cancel, destroyed, once only;
  - AI rules: when to build an MCV, site choice, selling, retry limits.
* **PlayMode (real map):**
  - placement rejected outside territory and accepted inside;
  - overlapping HQ and FCC territories;
  - sell with the right refund, cancel, duplicate, destroyed while dismantling;
  - factory queue and research refunds;
  - MCV production cost; deploy valid and invalid; destroyed while deploying;
  - FCC creation, territory added, then removed on sale or destruction while surrounding buildings survive;
  - NavMesh obstacle present; MCV repaired at a pad;
  - AI forward base; victory interactions; restart cleanup.
* **Study:** AI-vs-AI on all four maps across seeds and starts, recording MCVs produced, forward bases established,
  their timing, match length and win rates.
