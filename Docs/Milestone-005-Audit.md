# Milestone 005 — Quality Audit

**Date:** 2026-10-10 · **Baseline commit:** `14c8c89` (end of Milestone 004)

## Method

Evidence comes from four sources:

1. **Code review** of every runtime system: commands, selection, combat, economy, AI and UI.
2. **The automated suites**: 142 EditMode and 35 PlayMode tests, all passing at the baseline.
3. **The Milestone 004 AI-vs-AI simulations** and the simulated-input UI playthrough.
4. **A new benchmark harness.** `BenchmarkRunner` runs in the `RTS_Benchmark` scene, or in a player started with `-ironfrontBenchmark`. It spawns two equal armies (half Light, half Heavy) of 25/50/100/200 units in total, with the player army selected. It then measures 4 s idle and 8 s of combat, recording:
   - frame-time percentiles, GC allocation and collections, memory;
   - path requests, projectile counts and draw calls;
   - per-system milliseconds from new profiler markers (Editor and development builds only).

Findings that need a human are marked **[playtest]**.

## Baseline performance

### Release player (authoritative)

Measured on this machine (Windows 11, RTX 3070-class GPU), vsync off.

| Army | Phase | Avg ms | FPS | p95 ms | p99 ms | Max ms | GCs / 8 s | Draw calls | Projectiles |
|---|---|---|---|---|---|---|---|---|---|
| 25 | idle | 0.97 | 1031 | 5.25 | 5.74 | 5.9 | 1 | 114 | 0 |
| 25 | combat | 1.17 | 858 | 5.18 | 5.72 | 25.1 | 2 | 108 | 2 |
| 50 | idle | 1.15 | 866 | 5.40 | 5.83 | 6.2 | 1 | 206 | 0 |
| 50 | combat | 1.45 | 690 | 5.06 | 5.63 | 7.6 | 2 | 187 | 4 |
| 100 | idle | 1.40 | 714 | 5.34 | 5.84 | 7.1 | 1 | 383 | 0 |
| 100 | combat | 1.79 | 559 | 4.98 | 5.58 | 7.1 | 3 | 339 | 7 |
| 200 | idle | 1.71 | 586 | 4.82 | 5.56 | 15.1 | 1 | 636 | 0 |
| 200 | combat | 2.22 | 451 | 4.37 | 5.06 | 13.3 | 4 | 647 | 10 |

The p95 near 5 ms against averages of 1–2 ms is frame-pacing granularity at very high frame rates, not
hitching. Total memory was 318–338 MB.

### Editor attribution (Editor overhead included; use for proportions only)

At 200 units in combat:

| Cost | Value |
|---|---|
| Main thread | 11.6 ms |
| Script `Update` / `LateUpdate` | 1.31 / 0.47 ms |
| NavMesh crowd | 0.57 ms |
| Render thread | 1.0 ms |
| GC allocation | 116 KB/frame (4 collections in 8 s) |

Per-marker split of the script time:

| System | ms/frame |
|---|---|
| Combat tick | 0.90 |
| — of which target search | 0.46 (idle 0.28) |
| Health bars | 0.25 |
| HUD | 0.11 |
| Effects spawn | 0.09 |
| Projectiles | 0.06 |
| Line of sight | 0.05 |

Path requests ran at 96/s for 200 units: re-pathing is throttled, so there's no per-frame path spam. The idle
Editor allocation of about 9 KB/frame stayed the same with every Ironfront system switched off, so it is
Editor overhead rather than game code. The additional 75–105 KB/frame appears only in combat.

### Performance conclusion

There is **no frame-rate problem up to 200 units on this hardware.** The measurable problems are:

- **GC churn in combat:** each shot creates and destroys 3 GameObjects (shell, muzzle flash, impact), and target scans allocate lists.
- **Target search grows quadratically:** every unit checks every enemy using a physics closest-point query each scan. It is already the largest script cost at 200 units.
- **Lower-spec hardware is untested.** **[playtest]** on a slower machine.

## Findings, ranked

Severity is the impact on playing the game. Effort: S (under 2 h), M (half a day), L (more).

| # | Area | Finding | Severity | Player impact | Effort | Action in M005 |
|---|---|---|---|---|---|---|
| 1 | Combat | **Idle units don't respond when shot by an enemy outside their range** (Heavy range 13 vs Light 11): Light Tanks sit and die. Nothing tells the player | High | Units feel broken in common fights | S | Fix: retaliation |
| 2 | UI | **No minimap**: no overview of the map, the camera position or distant fights | High | Hard to navigate and react | M | Add |
| 3 | UI | **No notifications**: attacks on the base or units, completed construction, units ready, low power, insufficient credits | High | Player misses events outside the view | M | Add a notification feed with throttling |
| 4 | Controls | **No control groups** (Ctrl+1–9 / 1–9) and no "select all of this type" | High | Standard RTS control missing; slow army handling | M | Add control groups and double-click type selection |
| 5 | Performance | Per-shot GameObject create/destroy (3 per shot) and GC collections during combat | Medium | Possible hitches on slower machines **[playtest]** | M | Pool projectiles and effects |
| 6 | Performance | Target search: O(units × enemies) closest-point queries, list allocation per scan, and all units scan in the same frame | Medium (grows with army size) | Frame cost rises quadratically | S | Cheap pre-filter, reuse buffers, stagger scans |
| 7 | UI | Placement feedback only explains an invalid site **after** the click fails; the ghost is just red | Medium | Trial and error when building | S | Live reason and cost next to the cursor |
| 8 | UI | Production queue shows progress but no time remaining or tooltip; buttons don't show power use or hotkeys | Medium | Harder to plan production | S | Remaining time plus tooltips |
| 9 | UI | No tooltips or help for keyboard shortcuts (F, X, H, F9 are only discoverable in the top-left text) | Medium | Discoverability | S | Tooltips and an F1 help overlay |
| 10 | UI | HUD uses a constant pixel size: large at 1280×720 (the sidebar covers the map), tiny at 4K | Medium | Readability across resolutions **[playtest]** | S | Scale with screen size (reference 1600×900, match 0.5) |
| 11 | UI | Victory/defeat screen shows only the outcome and match time | Low | Little sense of how the match went | S | Match statistics |
| 12 | UI | Damage feedback is only a health bar; nothing shows that a unit was just hit | Low | Hard to see who is under fire | S | Hit flash on health bars |
| 13 | Movement | Formation spacing is a fixed 2 m regardless of unit size; Heavy Tanks (radius 0.9) crowd and jostle at the destination | Low | Visual jitter at arrival | S | Spacing by largest unit |
| 14 | AI | AI-vs-AI had no seed variation; balance was measured only from faction 1 in the south-west | Medium (evidence) | Balance claims weak | S | Seeded variation and a swapped-start scene; re-run the study |
| 15 | AI | The commander is predictable: one rally point, attack-move waves, no scouting (it knows your start by configuration), no harvester harassment; Defend reacts only to armed units near structures | Medium | Repetitive opponent **[playtest]** | L | Not in M005 (no new strategy content) |
| 16 | Economy | Ore runs out at about minute 5–6, so matches last about 6–12 minutes | Medium | Short games; end game is a scramble **[playtest]** | S (data) | Not changed; needs a playtest decision |
| 17 | Controls | Right-clicking own structures does nothing (e.g. harvester → refinery to unload); no rotate key for placement | Low | Minor convenience | S | Not in M005 |
| 18 | Tech debt | Unused template packages (`com.unity.ai.assistant`, `com.unity.ai.inference`): 511 shader warnings per build and DirectML.dll (14 MB) in every build | Low | Build size and noise | S | **Recommend removal; needs your approval** (package removal) |
| 19 | Tech debt | Gameplay registries are static lists and static events (reset on SubsystemRegistration, unsubscribed in OnDestroy). They work, but a missed unsubscribe leaks across scene loads | Low | Regression risk | — | Keep; covered by the restart test |
| 20 | Tech debt | Scenes are generated from code; manual edits are lost on rebuild. The template `SampleScene` is still in Build Settings | Low | Workflow | — | Documented |
| 21 | Testing | PlayMode tests rely on accelerated time and take about 2–3 minutes; AI simulation outcomes vary run to run | Low | Flakiness risk | — | Keep long simulations `[Explicit]` |

Observations from Milestones 001–004 that still need a human: control feel, difficulty and pacing, readability
of colours and markers, and long-session stability. **[playtest]**
