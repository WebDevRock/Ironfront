# Milestone 010 — Report: Construction Territory, Building Sales and Mobile Command Vehicles

**Date:** 2026-10-12 · **Base:** `9dc7a75` (Milestone 009) · **Design:** `Docs/Milestone-010-Design.md`

Milestone 009 status checked first: committed, EditMode 264/264, PlayMode 74/74. Repair pads, queues and building
repairs are unchanged. Milestone 010 builds on them: the MCV uses Repair Pads, and the Forward Command Centre (FCC) can
be repaired in place.

## 1. What was delivered

| Area | Delivered |
|---|---|
| **Construction territory** | The Headquarters (55 m) and Forward Command Centres (40 m) grant construction territory. Every ordinary structure's **whole footprint** (body and apron) must lie inside the union of its owner's circles; overlapping circles form one buildable area. Placement is also refused inside the exclusion zone of an enemy command structure the faction **has seen** (HQ 70 m, FCC 50 m). Terrain, structure, ore, unit and NavMesh checks still apply. Territory follows command structures as they register and unregister (built, deployed, sold, destroyed, unloaded): event-driven, no per-frame work. Structures left outside a shrunken territory keep working and never become anchors. Territory is never shared between factions. Radii are data on the building definitions. |
| **Territory UI** | In placement mode, and while an MCV is selected:<br>• a translucent cyan fill over the buildable area and a bright edge along the union's outer boundary;<br>• red rings around **known** enemy exclusion zones only;<br>• for a selected MCV, the FCC footprint (cyan valid, red invalid) and a dashed ring for the territory it would grant.<br>A dedicated render pass draws all of this **after the fog of war**, so it stays readable over black, unexplored ground; scene depth still hides it behind structures. The placement hint names the reason ("Outside your construction territory", "Inside enemy territory", "Blocked by terrain", "Too close to an ore deposit", …). The minimap shows the player's territory as a dotted outline. Everything is hidden when not constructing. |
| **Building sales** | **Sell** button in the selection panel. Rules:<br>• refund = floor(value × 50 % × remaining health), with health read at completion (`SaleRules.Refund`, pure);<br>• 8 s of dismantling, during which the model sinks with dust, sparks and clanks; a credit chime at the end;<br>• **Cancel sale** any time before completion, free;<br>• duplicate requests are refused; the refund is paid once, on the transition to Sold;<br>• destroyed while dismantling: no refund;<br>• the HQ cannot be sold; FCC, factory, refinery and research centre need a second click to confirm;<br>• a factory's queued units are refunded in full, and selling the last Research Centre cancels the topic in progress with a full refund — both the same as the cancel buttons;<br>• removal unregisters power, prerequisites, territory and the NavMesh obstacle exactly like destruction, but without a death event. |
| **Mobile Command Vehicle** | 5000 credits, 45 s at the Vehicle Factory, 2000 HP, heavy armour (0.35), 3.5 m/s, unarmed, sight 16 m. Uses the normal movement, selection, faction, damage and vision systems. Repaired at Repair Pads like any vehicle. Procedural model: an eight-wheeled carrier with a folded command module, mast, four outriggers and faction livery. |
| **Deployment** | **Deploy [T]**. The FCC footprint at the vehicle's position must pass:<br>• map bounds, structures and clearance, aprons, obstacles, ore;<br>• other units (the vehicle itself excluded);<br>• level, walkable NavMesh under the centre and corners (slope or hole);<br>• known enemy exclusion zones.<br>Own territory is **not** required. Deployment then takes 15 s: the vehicle is locked (every move order refused, it cannot be sent for repair) and stays a target. The outriggers drop, the module rises and the mast extends, with a hydraulic sound. It can be cancelled. At the end the site is checked again, and the vehicle is replaced by an FCC exactly once through the placement controller, without a charge. The FCC keeps the vehicle's health fraction (at least 10 %), is selected if the vehicle was, and grants territory immediately. A vehicle destroyed while deploying leaves nothing. FCCs cannot be packed up again. |
| **Forward Command Centre** | 9 × 9 m, 2500 HP, armour 0.3, +10 power, sight 30 m, territory 40 m, exclusion 50 m, sale value 5000 (refund up to 2500). Can be attacked, repaired in place and sold. It does not produce units. Model: a low armoured command post on a hazard-striped pad, with a radar dish, beacon, landing lights and outrigger feet. A notification "Forward Command Centre established: new construction territory" and a chime mark activation; "Construction territory lost" and a warning tone mark its loss. |
| **Victory** | **Headquarters mode:** unchanged. Only the original HQ counts; losing it loses the match even with FCCs standing (tested). The setup and help texts say so. **Annihilation:** an FCC counts as a production structure, since with credits it can rebuild. MCVs do not count, so a hidden MCV cannot stall a match. |
| **AI** | The same territory, prices, timings and validation as the player:<br>• refinery sites only inside its territory;<br>• when the next ore field lies outside its territory, its home field is exhausted and that field is a long haul (≥ 70 m from any own refinery), it saves for an MCV (otherwise it buys one only from a clear surplus); from 4 min (Normal), 3 min (Hard) or 7 min (Easy), with at least 3–4 units to escort;<br>• the vehicle drives with up to 3 escorts to an explored, reachable field at least 90 m from any known enemy structure and on its own side of the map, and deploys there;<br>• units in the way are moved; at most 3 positions per site and 2 sites per vehicle, a 240 s travel timeout and blacklisted failed sites;<br>• it builds a guard tower at each new forward base;<br>• it sells refineries and forward bases with no ore left within 120 m (keeping its last refinery), and forward bases about to fall with no army near;<br>• forward bases standing at once: Easy 1, Normal 2, Hard 3; it stops after 2 MCVs are lost, with a 300 s cooldown after a loss. |

Screenshots in `Docs/Screenshots/M010/`:

| File | Shows |
|---|---|
| `m10_1_mcv_selected.png` | HQ territory over the fog, the deploy footprint and the dashed ring of the new territory |
| `m10_2_deploying.png` | the MCV deploying, 74 %, immobile |
| `m10_3_fcc.png` | the new FCC selected, with its territory and Sell button |
| `m10_4_territory_union.png` | HQ and FCC territories merged into one area |
| `m10_5_selling.png` | a refinery being dismantled, with progress and refund |

## 2. Automated verification

| Suite | Result | New in M010 |
|---|---|---|
| EditMode | **279/279** | `Milestone010TerritoryTests` (15), each run on the pure rules (no engine):<br>• **territory:** the whole footprint must fit (centre inside, corner outside → refused); overlapping circles combine (a footprint neither circle holds alone); never shared between factions; exclusion only for known enemy structures, never from own ones; removal shrinks the territory, is versioned, and ordinary structures never anchor;<br>• **refund rule:** values, rounding, health, zero for destroyed or unpriced structures;<br>• **sale state machine:** duplicates, cancel, single payment, destroyed while dismantling;<br>• **deployment state machine:** invalid site, single conversion, cancel, failed final check, destroyed vehicle, failed placement;<br>• **AI:** when to buy an MCV (every gate), saving for one versus spending surplus, never planning an unplaceable refinery, guard towers for forward bases, site rules, sale rules. |
| PlayMode | **88/88** | `Milestone010PlayModeTests` (14) on Dustfall Plains:<br>• **territory:** far construction rejected with no charge; a footprint partly outside the territory refused; inside accepted; the overlay is shown only while placing and never shows an unseen enemy zone;<br>• **sales:** refund by health paid once, duplicates refused, cancel free, power removed; queued units and the last research refunded in full; destroyed while dismantling pays nothing; the HQ cannot be sold; critical structures need two clicks (through the HUD buttons);<br>• **MCV:** produced at the factory for 5000; deployment creates exactly one FCC with the health fraction kept, immediate territory, a carving NavMesh obstacle and vision, and no duplicate units; refused next to the HQ and inside a **seen** enemy HQ zone but not an unseen one; locked while deploying; cancel works; destroyed while deploying leaves nothing; repaired at a Repair Pad;<br>• **forward territory:** building around the FCC, then selling it (refund 2500) removes the territory while the surrounding plant keeps working; a destroyed FCC also removes its territory;<br>• **victory:** with an FCC standing, losing the HQ still loses in Headquarters mode;<br>• **AI:** with an explored remote field and an MCV, the commander deploys there by itself, and every structure it builds lies inside its territory;<br>• **restart:** no territory, sale or MCV state survives.<br>All M001–M009 tests pass unchanged. |
| Console | 0 errors, 0 warnings after the runs. |

Visual check in the running game: screenshots `m10_1`–`m10_5` (§1). The first screenshot showed the overlay
**invisible over unexplored ground**: the fog pass darkens everything drawn before it. This was fixed with a render
pass after the fog (`TerritoryOverlayPass`) and checked again.

## 3. AI-versus-AI study (balance)

The same 12-match plan as Milestone 009: four maps, seeds 11 and 23 Normal mirrors plus seed 61 Hard vs Normal,
10× speed, 2700 s limit. Data: `Docs/Balance/M010/`.

### Iterations
1. **First run, cancelled after one match:**
   - MCVs were bought only from surplus credits, so none were ever bought.
   - Both economies stalled at one refinery and two to three harvesters, and the match timed out.
   - Cause: with territory, the second refinery needs a forward base, and the commander never kept 5000 spare.
2. **Probe (`probe_dustfall11_trace.txt`):**
   - Saving for the MCV made the forward base happen, but late (19 min).
   - On Dustfall, the home field (2 × 6000 ore) runs dry at about 6 min, after which there is no income to save from.
   - The MCV rule was therefore moved before defences and research, starts saving at 4 min (Normal), and repairs no
     longer spend the credits being saved.
3. **Iteration 1 (partial):**
   - The escort parked inside the planned footprint: "Units in the way", repeated, then the sites were abandoned.
   - Fix: the escort keeps 10–13 m away, and own units on the footprint are moved off before retrying.
4. **Iteration 2 (partial, 6 matches):** forward-base churn.
   - Harvesters gather from *any* field and haul to the nearest refinery, so the forward field was drained within
     minutes and the forward base was sold as obsolete. Each cycle cost about 4000 net; one side bought 5 MCVs.
   - Fix: an MCV is saved for only when the home field is exhausted **and** the next field is at least 70 m from any own
     refinery (otherwise only from a clear surplus). Refineries and forward bases are kept while ore remains within
     120 m. Caps are Easy 1, Normal 2, Hard 3 standing forward bases.
5. **Iteration 3 (final, 12 matches)**, below.

### Final results

| Match | M009 | M010 | MCVs / forward bases (F1 · F2) | Structures sold (F1 · F2) |
|---|---|---|---|---|
| Dustfall 11 N v N | F2 2048 s | F2 2302 s | 0/0 · 3/3 | 0 · 4 |
| Dustfall 23 N v N | **timeout** | **timeout** | 1/1 · 1/1 | 2 · 1 |
| Dustfall 61 H v N | Hard 1205 s | Hard 1761 s | 2/2 · 1/1 | 3 · 0 |
| Iron Pass 11 N v N | F2 1071 s | F2 2406 s | 2/2 · 3/3 | 4 · 6 |
| Iron Pass 23 N v N | F2 1770 s | F1 2177 s | 3/3 · 1/1 | 2 · 4 |
| Iron Pass 61 H v N | Hard 1580 s | Hard 1246 s | 2/2 · 1/1 | 4 · 2 |
| Green Divide 11 N v N | F2 1329 s | F2 1787 s | 1/1 · 1/1 | 4 · 2 |
| Green Divide 23 N v N | F1 1244 s | F2 912 s | 0/0 · 0/0 | 1 · 1 |
| Green Divide 61 H v N | Hard 1360 s | Hard 1410 s | 3/3 · 2/2 | 6 · 4 |
| Blackstone 11 N v N | F1 1612 s | F2 2184 s | 0/0 · 3/3 | 0 · 3 |
| Blackstone 23 N v N | F2 1698 s | **timeout** | 1/1 · 3/3 | 3 · 4 |
| Blackstone 61 H v N | Hard 1535 s | **timeout** | 3/2 · 4/4 | 3 · 8 |

**Requested measurements:**
* **MCV production frequency:** 1.7 per side per match (maximum 4); 20 of 24 commanders built at least one.
* **Forward base success:** 40 of 41 MCVs deployed; one gave up after its sites failed. No MCV was destroyed. Of 72
  deploy attempts, 11 were refused (blocked terrain at the first position); each was resolved by the next position.
* **Expansion timing:** first forward base at a median of 696 s (range 447–2103).
* **Game duration:** 9 of 12 decided (M009 on the same seeds: 11 of 12). The median of matches decided in both
  studies is 1787 s against 1360 s (+31 %).
* **Win rates:** Hard beat Normal 3 of 4 (the fourth timed out). When only one side built a forward base, that side won
  (2 of 2). In Normal mirrors faction 2 won 5 of 6 decided matches (M009: 5 of 7); with this few matches that is
  inconclusive.
* **Economy:** income per side is comparable to M009 on Iron Pass, Green Divide and Blackstone, and about 28 % lower on
  Dustfall (48k against 67k). Net MCV spending (price minus sale refunds) is about 10 % of income. The AI sold 71
  structures (exhausted refineries and forward bases) for 82,000 credits in total.

**Assessment.** Territory makes expansion a deliberate, expensive decision, as intended. The AI handles it without
cheating or getting stuck: no stuck MCVs, no repeated failed deployments, no stale reservations. The cost is pace.
Matches are about 30 % longer, and two more seeds ended in a stalemate. On Blackstone the fields are small and uneven,
so forward bases there run dry within about 15 minutes. The commander then relocates repeatedly; one Hard commander
spent 15,000 credits on MCVs while its army stayed small.

## 4. Exploit review

| Scenario | Result |
|---|---|
| Sell for more than paid | Impossible: refund ≤ 50 % of value, scaled by health. |
| Repair then sell | Repair costs 50 % of value per 100 % health; the sale returns 50 % per 100 % health, so it nets exactly zero. |
| Sell a factory with a queue | The queue is refunded at the price paid (as cancelling does); nothing extra. |
| Sell the last research centre mid-topic | The topic's price is refunded and its progress lost; nothing extra. |
| Double refund (two requests, damage, death or match end during dismantling) | Refused: one state transition pays; death cancels; refused after the match ends (all tested). |
| MCV → FCC → sell | −5000 + up to 2500: always a loss. Repairing the MCV at a pad first (40 % rate) and then selling the FCC still loses at least 2500. |
| Repeated deployment / duplication | One conversion per vehicle (guarded state), no pack-up, the vehicle is destroyed on conversion; tested for duplicates. |
| Teleporting or remote construction | The FCC appears at the vehicle's own snapped position after 15 s; ordinary structures still need territory. Chaining territory costs 5000 per 40 m circle. |
| Building beside an unseen enemy base | Allowed, by the fog rule. Exclusion applies only once that base has been seen, so territory never reveals hidden bases. An FCC 50 m from an unseen enemy HQ is possible but slow, visible and exposed. *Decision for the director: see §7.* |
| Free harvester with a refinery | None exists; nothing to farm. |

## 5. Settings (data)

| Data | Value |
|---|---|
| HQ territory / exclusion | 55 m / 70 m (`Headquarters.asset`) |
| FCC territory / exclusion | 40 m / 50 m; 2500 HP; +10 power; sale value 5000 (`ForwardCommandCentre.asset`) |
| MCV | 5000 cr, 45 s, 2000 HP, armour 0.35, 3.5 m/s (`MobileCommandVehicle.asset`) |
| Sale | 8 s, 50 % of value × health, 3 s confirmation window (`TerritorySettings.asset`) |
| Deployment | 15 s, FCC health ≥ 10 %, ground tolerance 0.6 m (`TerritorySettings.asset`) |
| AI (Easy / Normal / Hard) | forward bases 1 / 2 / 3; earliest 420 / 240 / 180 s; escort army 4 / 3 / 3; MCV losses ≤ 2; large maps: harvesters per refinery +1 |

## 6. Problems encountered
- **Fog hid the territory overlay.** Fixed with a render-graph pass after the fog pass, queued from script (no renderer
  asset change).
- **The AI economy stalled** once refineries needed territory. Four study iterations were needed (see §3).
- **The escort blocked deployment.** Fixed.
- **First-forward-base time logged on the wrong clock.** Fixed: it now uses match time, as intel does.
- `Object.GetInstanceID` is obsolete in this Unity version, so anchors use their own session ids.
- Long-running `eval` content regeneration times out the bridge (known) but completes.
- Generated meshes for the new models add about 25 MB (157 MB in total).

## 7. Human verification and decisions for the director
- **Playtest the acceptance scenario:**
  1. Try to build a factory far away (refused: "Outside your construction territory").
  2. Build inside the territory, then sell it.
  3. Produce an MCV, drive it out and press T.
  4. Build around the new centre.
  5. Sell or destroy the centre, and check that its territory goes while the surrounding buildings stay.
  6. Watch the AI's forward base.
- **Pace.** Matches are about 30 % longer and timeouts rose from 1 to 3 of 12. If that is too slow, these data-only
  options exist:
  - a cheaper MCV (e.g. 3000–3500);
  - a larger FCC radius (50–60 m, covering two fields);
  - an HQ radius that reaches the nearest field on every map (it is 49–57 m on Iron Pass and Green Divide but 80–129 m
    on Dustfall and Blackstone, which is why those two maps depend most on MCVs).
- **Fog rule for exclusion zones.** Enforced only for enemy command structures you have seen. The alternative, always
  enforced, would leak hidden bases through rejection messages.
- **Victory.** Headquarters mode is unchanged: FCCs never replace the HQ. Annihilation counts FCCs.

## 8. Known limitations
- Territory is checked with 0.5 m sampling when several circles are involved; this is exact for a single circle.
- The overlay draws a flat ribbon at ground height; on steep terrain it can sink into slopes (the skirmish maps are flat
  where building is possible).
- The AI sends its MCV with a small escort but does not route around known threats.
- The AI never repairs MCVs at pads (the player can).
- Structures have no build time (unchanged); the FCC's "construction" is the 15 s deployment.
- The study's no-timeout assertion fails on the three timeouts.
