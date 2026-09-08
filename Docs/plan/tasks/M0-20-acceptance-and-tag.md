# M0-20 — M0 device acceptance, tuning, tag `m0`

**Size:** S · **Depends on:** everything in M0 · **Branch:** `m0-20-acceptance`
**Design refs:** CC §8 (movement items), AR §16

## Goal

Declare the walking skeleton done on evidence: the checklist below passes on the owner's phone, the movement numbers are recorded, and `m0` is tagged so there is always a known-good build to return to.

## Files

| Path | Purpose |
|---|---|
| `Docs/plan/PROGRESS.md` | Checklist results, tuned numbers, learnings, M1 readiness notes |
| `Docs/plan/ROADMAP.md` | M0 boxes ticked; M1 specs linked |
| `Assets/_Project/Data/Characters/Oathbound.asset` | *only if* tuning changed a number |

No code in this task. If the checklist reveals a bug, it becomes a fix task (`M0-20a…`) with its own PR.

## Checklist (owner — emulator now, device-only items deferred)

Until a phone is available, items marked **[device]** are recorded as deferred in PROGRESS rather than blocking the tag. They are debt, not exemptions: the first session with real hardware runs every one of them.

Movement and controls — from [CC §8](../../CoreCombat.md):
- [x] Stick spawns under the thumb anywhere in the left 45 %
- [x] Dynamic recentering: drag far right, then left — reversal is immediate
- [x] Deadzone / analog band behave as specified (tiny drag = nothing, ~24 dp = half speed) — at the emulator's 240 DPI
- [ ] **[device]** Movement and a second touch work simultaneously
- [x] Capsule reaches full speed in a blink and stops with no slide
- [x] Facing follows movement; holds when idle
- [ ] **[device]** Sustained 60 fps in the grey box (the emulator's number is meaningless)
- [ ] **[device]** Touch-to-motion latency feels immediate

Architecture proof:
- [x] Play from `Boot`, `Menu`, and `Run` in the Editor all work
- [ ] ~~Kill the app from the recents screen and relaunch~~ — **blocked: the APK does not launch.** Diagnosed and deferred to [M0-20a](M0-20a-apk-runs-on-bluestacks.md)
- [x] Press Play in `Run` directly (no `PendingRun`) — falls back to Oathbound + a warning, no exception
- [x] `Tests/Core`, `Tests/Game`, `Tests/PlayMode` all green in the Test Runner
- [x] Zero analyzer warnings in the Console after a clean recompile of all six assemblies

Feel question (record the answer, it's data): *does moving the capsule around pillars feel responsive and precise for one minute?* If not, which of speed / accel / decel / turn speed / stick band is wrong?

**Answer: good for now.** No number changed, so `Oathbound.asset` and CC §7 are untouched — 5.4 m/s, 0.06 / 0.08 s, 720 °/s, 8 / 40 / 60 dp all stand as authored.

## Behaviour

1. Tuning happens only in `Oathbound.asset` and `StickShaper` constants. Changing either is a normal PR; the numbers in CC §7 are then updated to match in the same PR — the doc and the asset never disagree.
2. When every box is ticked: owner merges `dev` → `main` and tags `m0`. Claude does not tag.
3. The PROGRESS entry for this task lists what M1 should know: anything about the snapshot, intents, or scopes that turned out different from the specs.

## Acceptance

- [x] Checklist fully ticked — **except** the APK-runtime row, deferred to M0-20a by the owner's decision, and the four `[device]` rows that have no hardware to run on
- [x] `PROGRESS.md` updated with results and numbers; Current State points at M1-01
- [ ] `m0` tag exists on `main` — owner's step, after this PR merges

## Out of scope

- Anything combat. The temptation to "just add a swing" here is exactly what M1 is for.

## As built

Three decisions and one diagnosis; no code, and no tuning.

**Camera framing: kept at pitch 57 / distance 16, deliberately.** M0-18 left the choice open because those numbers frame 25.6 × 32.8 m rather than the 36 m arena, which needs `distance ≈ 22.5`. The owner has playtested the follow camera as authored and chose to keep it: M0's job is to judge the *player*, and a whole-arena fixed framing is an endgame direction rather than this milestone's. Verified live and unchanged in a direct Run play — offset `(0.00, 13.42, −8.71)`, pitch `57.00`, FOV 60, matching M0-18's recorded numbers to the millimetre. `FollowCamera` was not touched.

**Tuning: none.** The feel answer was "good for now", so `Oathbound.asset` keeps 5.4 m/s / 0.06 s / 0.08 s / 720 °/s and `StickShaper` keeps 8 / 40 / 60 dp. CC §7 already agrees with both, so rule 1's doc-and-asset-never-disagree obligation is satisfied by doing nothing.

**Working-tree collateral: already settled in `dev` before this task started, and the Known-issues block describing it was stale.** Git says so: the URP `Mobile_RPAsset` and `UniversalRenderPipelineGlobalSettings` changes and `GraphicsSettings` were *committed* in `5f8a331`, `/Builds/` was added to `.gitignore` in that same commit, and `Assets/Resources/PerformanceTestRun*.json` and `Assets/_Recovery/0.unity` are gone from disk. `UnityConnectSettings.m_Enabled` is `0` in `HEAD` and was `0` at repo init, so the analytics flip was reverted before it was ever committed.

**Verification was a clean recompile, not a literal `Assets > Reimport All`.** `CompilationPipeline.RequestScriptCompilation(CleanBuildCache)` is what re-runs the Roslyn analyzers over every assembly, which is what that row is actually testing; a full asset reimport re-imports TMP and URP and tests nothing about analyzers. Recorded as a deviation rather than quietly substituted. Disk was at 95 % (24 GB free), which is its own argument against a gratuitous reimport after M0-19's disk-full failure.

**The APK does not launch, and the reason is not the one the reference-device plan predicted** — see the PROGRESS entry and [M0-20a](M0-20a-apk-runs-on-bluestacks.md). ARM64 translation works; BlueStacks' Vulkan driver crashes on a development-build-only Vulkan call reached through `libhoudini.so`. The owner chose to tag `m0` on the Editor evidence and carry this as diagnosed debt.
