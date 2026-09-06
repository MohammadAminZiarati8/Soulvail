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

## Checklist (owner, on device)

Movement and controls — from [CC §8](../../CoreCombat.md):
- [ ] Stick spawns under the thumb anywhere in the left 45 %
- [ ] Dynamic recentering: drag far right, then left — reversal is immediate
- [ ] Deadzone / analog band behave as specified (tiny drag = nothing, ~24 dp = half speed)
- [ ] Movement and a second touch work simultaneously
- [ ] Capsule reaches full speed in a blink and stops with no slide
- [ ] Facing follows movement; holds when idle
- [ ] Sustained 60 fps in the grey box (overlay)

Architecture proof:
- [ ] Play from `Boot`, `Menu`, and `Run` in the Editor all work
- [ ] Kill the app from the recents screen and relaunch — clean start, no errors
- [ ] `Tests/Core`, `Tests/Game`, `Tests/PlayMode` all green in the Test Runner
- [ ] Zero analyzer warnings in the Console after a full reimport

Feel question (record the answer, it's data): *does moving the capsule around pillars feel responsive and precise for one minute?* If not, which of speed / accel / decel / turn speed / stick band is wrong?

## Behaviour

1. Tuning happens only in `Oathbound.asset` and `StickShaper` constants. Changing either is a normal PR; the numbers in CC §7 are then updated to match in the same PR — the doc and the asset never disagree.
2. When every box is ticked: owner merges `dev` → `main` and tags `m0`. Claude does not tag.
3. The PROGRESS entry for this task lists what M1 should know: anything about the snapshot, intents, or scopes that turned out different from the specs.

## Acceptance

- [ ] Checklist fully ticked
- [ ] `PROGRESS.md` updated with results and numbers; Current State points at M1-01
- [ ] `m0` tag exists on `main`

## Out of scope

- Anything combat. The temptation to "just add a swing" here is exactly what M1 is for.

## As built

_Filled at merge._
