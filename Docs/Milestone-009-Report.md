# Milestone 009 — Report: Building Repairs, Vehicle Repair Pads and Queuing

**Date:** 2026-10-12 · **Base:** `21c8334` (fog fix after M008) · **Design:** `Docs/Milestone-009-Design.md`  · **Commit:** `fd8ab51`

## 1. What was delivered

| Area | Delivered |
|---|---|
| **Building repairs** | Any structure can be switched to *Repair* from the selection panel (**Repair** toggle). Health returns gradually at 1 % of max health per second, also under attack. The cost is proportional to the health restored: a full repair costs 50 % of the construction cost (unbuyable structures such as the HQ are valued at 2000). Without credits the repair pauses and resumes on its own. It switches off at full health. A destroyed building cannot be repaired. Status shown: Repairing / Paused (no credits) / Complete. Sparks and a weld sound play at random points on the building. |
| **Repair Pad** | A new structure in the construction menu: 1000 credits, −20 power, requires a Vehicle Factory, 900 HP, 7 × 5 m with an 8 m apron. After placement it spends 15 s **assembling** (its "construction time"; no other structure has a build time), then services vehicles. It repairs at 6 % of max health per second (full repair in about 17 s). A full repair costs 40 % of the vehicle's purchase price. It pauses on low power or without credits and keeps its queue. It serves any friendly ground vehicle, harvesters included (they go back to harvesting afterwards). Several pads work independently. |
| **FIFO queue** | Exactly one vehicle in the bay at a time. The others wait in order at waiting slots to the left of the bay, which are re-checked every second against the NavMesh, footprints and aprons so they never block the entrance. Vehicles leave by exit points on the right. The next vehicle is granted the bay only after the previous one has **left it**: a logical hand-shake plus a physical check that the bay is empty. An idle friendly vehicle standing in the bay for 10 s is told to move. A vehicle is queued once; a new repair order to another pad transfers it cleanly. |
| **Explicit states** | `TravellingToQueue → Waiting → EnteringPad → Repairing → ExitingPad → Completed`, and `Cancelled` from any state (`RepairOrderMachine`, pure). Every tick it checks that the vehicle, pad and ticket still exist. |
| **Recovery (no teleporting)** | Stall watchdog: less than 1 m progress in 8 s → re-route, 3 re-routes then cancel ("route to Repair Pad blocked"). Travel 120 s, enter 25 s (the bay passes to the next vehicle), exit 20 s × 3 exit points, then the vehicle is released where it stands and the bay stays closed until it is physically clear. Pad destroyed → every client cancelled. A destroyed vehicle loses its ticket and the others move up. Match end freezes everything; restart leaves no state (statics reset). |
| **Commands and UI** | **Repair [R]** button and hotkey for selected damaged vehicles → *best pad* (lowest estimated wait: travel time plus queue × average repair time). **Right-click a friendly pad** to choose it. **Cancel repair** button. Any other order (move, attack, attack-move, stop, hold, harvest) cancels the repair. Without a pad: "No Repair Pad — build one (requires a Vehicle Factory)". The selection panel shows each vehicle's state, queue position and progress. A selected pad shows assembly / power / credits / queue length / current vehicle and progress, and the target pad is highlighted. |
| **AI** | The commander builds a pad once it has a factory and an army of 6 or more, if it can afford one beyond its reserve (Easy/Normal 1 pad, Hard 2). It sends a vehicle below the difficulty threshold (Easy 35 %, Normal 45 %, Hard 50 %) when it is not attacking, and while defending only if it is badly damaged (< 25 %). Never more than half the army at once, and only when credits cover the reserve plus the estimated cost. It repairs important structures below 50 % / 70 % / 75 % while credits exceed the reserve + 300 (with hysteresis, stopping before production is starved). It uses the same `IssueRepair` / `SetRepairing` calls, costs and rates as the player — no subsidies. Vehicles in repair are left out of attack groups and scouts. After a pad is lost the vehicles are cancelled and re-sent later. |
| **Presentation** | Pad model: workshop, masts and cantilever beam with a gantry, trolley and welding arms; hazard-striped apron with chevrons and an entry/exit marking; bay ring and status lamp — green available, amber occupied, red no power, flashing blue assembling. While repairing: sparks, arm sweep, the trolley follows the vehicle, weld/ratchet sounds, and a chime on completion. New icon `Icon_RepairPad`. All of it is driven by the repair state only (`RepairPadFx`, `BuildingRepairFx`). |
| **Data** | Every number lives in `Data/RepairSettings.asset` (referenced by `MatchSetup`). Each building and unit definition has an optional `repairCostMultiplier`, and the AI thresholds are in the difficulty assets. |

Screenshots: `Docs/Screenshots/M009/m9_1.png` (a tank in the bay, two waiting on the left, HUD queue status), `m9_2.png`
(repaired vehicles leaving on the right, refinery repairing).

## 2. Automated verification

| Suite | Result | New in M009 |
|---|---|---|
| EditMode | **264/264** | `Milestone009RepairTests` (22): bounded restore and no restore when dead; meter cost proportional to health, stop at max, pause without credits and resume, charged = predicted; queue FIFO, duplicates, one active, vacating hand-shake, removal; order machine with **ten vehicles** (one at a time, FIFO, all repaired), every state, queued and in-bay destruction, pad destruction, cancel and transfer, blocked and incomplete routes (re-route then cancel, the vehicle is never moved by the machine), unable to enter/leave the bay, queue cleared underneath; AI rules (worthwhile repairs only, building hysteresis and reserve, pad choice, when to build). |
| PlayMode | **74/74** | `Milestone009RepairPlayModeTests` (9) on Dustfall Plains: **10 vehicles at one pad** (never more than one in the bay, FIFO completion order, all repaired, credits match health × rate); two pads independent and the best-pad rule spreads the load; low power and no credits pause and then resume; building repair via the HUD toggle is gradual, paid, pausable and stops at full; new orders cancel, transfer moves the ticket, the Cancel button works; the no-pad message; destruction of a queued vehicle, the bay vehicle and the pad leaves no stale state; restart; the computer builds and uses its pad and repairs buildings. All M001–M008 tests unchanged and passing. |
| Console | 0 errors, 0 warnings after the runs; no compile errors. |

## 3. AI-versus-AI study (balance and stalls)

12 matches: 4 maps × (seeds 11 and 23 Normal vs Normal, seed 61 Hard vs Normal), 10× time scale, 2700 s limit,
compared against the same seeds in the Milestone 008 final study. Data: `Docs/Balance/M009/`.

**Iteration 1** (building repair 2.5 %/s): 2 timeouts (Dustfall 23, Green Divide 11). Traces showed sieges failing
because a repairing HQ regained 50 HP/s, about half of a six-tank attack's damage. → building rate lowered to **1 %/s**.

**Iteration 2** (final values):

| Map / seed | M008 | M009 | Vehicles repaired (F1/F2) | Repair credits, % of income |
|---|---|---|---|---|
| Dustfall 11 N v N | F2 832 s | F2 2048 s | 17 / 20 | 6.4 % |
| Dustfall 23 N v N | F1 1385 s | **timeout** | 15 / 11 | 4.8 % |
| Dustfall 61 H v N | Hard 635 s | Hard 1205 s | 10 / 6 | 4.2 % |
| Iron Pass 11 N v N | F2 734 s | F2 1071 s | 0 / 3 | 2.0 % |
| Iron Pass 23 N v N | F1 536 s | F2 1770 s | 15 / 9 | 5.3 % |
| Iron Pass 61 H v N | Hard 2013 s | Hard 1580 s | 18 / 11 | 5.4 % |
| Green Divide 11 N v N | F1 2441 s | F2 1329 s | 5 / 4 | 4.7 % |
| Green Divide 23 N v N | F1 531 s | F1 1244 s | 7 / 6 | 5.5 % |
| Green Divide 61 H v N | Hard 945 s | Hard 1360 s | 11 / 5 | 4.8 % |
| Blackstone 11 N v N | F1 1909 s | F1 1612 s | 7 / 4 | 3.1 % |
| Blackstone 23 N v N | F1 1063 s | F2 1698 s | 4 / 12 | 4.8 % |
| Blackstone 61 H v N | Hard 909 s | Hard 1535 s | 7 / 8 | 3.9 % |

**Assessment:**
* **Decisive:** 11 of 12 matches were decided; M008's 28-match study also had one timeout. Hard beat Normal 4/4, so
  difficulty still separates. 23 of 24 commanders built at least one pad (Hard built two). Repairs kept completing
  through the long matches (up to 20 per side), so no pad stayed blocked for good. The study does not log individual
  navigation-recovery events. Those paths are covered by the EditMode and PlayMode tests, not by this study.
* **Matches are longer:** median 1558 s against 927 s for the same seeds (mean 1596 against 1161). Three matches got
  shorter. Repairs make attrition fights cheaper for the side at home, so attacks break through less often.
* **Repairs are not a dominant credit sink:** 2–6 % of income, 10–20 vehicles repaired per side in long matches.
* **Stall case — Dustfall 23:** two evenly matched Normal commanders traded attacks for 45 minutes. Neither base
  fell. Repair spending was moderate (4.8 %), so repairs are only part of the cause.
* **Not done:** a control batch with AI repairs switched off on the same seeds was skipped at the director's request.
  The AIs are not deterministic, so the per-seed differences above mix the effect of repairs with run-to-run
  variance.

## 4. Repair settings (final values, `Data/RepairSettings.asset`)

| Setting | Value |
|---|---|
| Building: cost fraction / rate / unbuyable value | 0.5 of construction cost / 1 % max HP per s / 2000 |
| Vehicle on pad: cost fraction / rate | 0.4 of purchase price / 6 % max HP per s |
| Pad: cost / power / HP / assembly | 1000 / −20 / 900 / 15 s |
| Navigation: stall / re-routes / travel / enter / exit | 8 s & 1 m / 3 / 120 s / 25 s / 20 s × 3 |
| Bay radius / blocker nudge | 3 m / 10 s |
| AI (Easy / Normal / Hard) pads, vehicle threshold, building threshold | 1, 35 %, 50 % / 1, 45 %, 70 % / 2, 50 %, 75 % |

Examples: a light tank (purchase price P) repaired from 0 % to 100 % on a pad costs 0.4 P and takes about 17 s.
A refinery at half health costs 25 % of its construction cost to restore and takes 50 s.

## 5. Problems encountered
- The "Editor hung" state seen before was in fact a hidden modal *Scene(s) Have Been Modified* dialog, raised when a
  test run started with a dirty scene. Tests now start from a clean scene.
- Builder helpers are `internal`. A public `RepairContentBuilder.RegenerateContent()` (menu *Ironfront › Regenerate
  Content*) regenerates the content.
- Test fixes during development: harvester income distorted credit checks (harvesters are disabled in test setup); the
  pad prerequisite registers in `Start`, so the AI test waits one frame; completion was counted twice.
- Balance: building repair at 2.5 %/s stalled sieges (see §3).
- During the study, memory pressure on the machine killed the background watcher shells (not the study).

## 6. Human verification still needed
- An acceptance play session: build a pad, queue a group with R and by right-click, watch the bay hand-over, repair a
  building while under attack, run out of credits, lose power.
- Whether longer matches with repairs are acceptable for the intended pacing.

## 7. Known limitations and decisions for the director
- **Match length:** AI-vs-AI matches are about 1.5–1.7× longer with repairs, with one timeout in 12. If that is too
  slow, these data-only options are available: a lower vehicle pad rate or higher cost fraction, a stricter AI vehicle
  threshold, or AI building repairs off while under attack.
- Pad "construction time" is an assembly phase after instant placement (no structure has a build time; see design D6).
- Only ground vehicles can be repaired (there are no other unit types). Building repairs use one rate for every
  structure; it can be varied per structure through `repairCostMultiplier` (cost only).
