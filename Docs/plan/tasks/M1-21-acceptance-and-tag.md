# M1-21 — M1 acceptance: CC §8 on device, tuning, tag `m1`

**Size:** S · **Depends on:** everything in M1 · **Branch:** `m1-21-acceptance`
**Design refs:** CC §8 (the full checklist), CC §1, GD §12.4 (guardrails that now apply)

## Goal

Answer the M1 question on evidence — *is moving and swinging, with nothing else around it, fun for two minutes straight?* — record the numbers, and tag `m1`.

## Files

| Path | Purpose |
|---|---|
| `Docs/plan/PROGRESS.md` | Checklist results, the feel verdict, tuned numbers, learnings for M2 |
| `Docs/plan/ROADMAP.md` | M1 ticked; M2 specs written and linked (M2 detailing is triggered at ~75 % of M1, so it should already exist) |
| `Docs/CoreCombat.md` §7 | Updated **only if** tuning changed a shipped number — doc and asset never disagree |
| `Assets/_Project/Data/…` | Only if tuning changed |

No code. A bug found here becomes `M1-21a…` with its own PR.

## Checklist (owner, on device) — [CC §8](../../CoreCombat.md) in full

- [ ] Stick spawns under the thumb anywhere in the left 45 %
- [ ] Dynamic recentering — reversal is immediate
- [ ] Movement and buttons work simultaneously (stick + Charge)
- [ ] Character strafes: faces the target while moving independently
- [ ] Auto-target picks the sensible enemy and **doesn't jitter** between two of them
- [ ] Reticle always visible under the current target
- [ ] Tap-to-focus locks on, and the lock is obvious; tapping ground releases
- [ ] Attacking never slows movement (overlay `|v|` unchanged while swinging)
- [ ] Charge feels reliable — i-frames land, buffered taps register, wall stops are clean
- [ ] Focus: standing still visibly speeds swings; any input cancels instantly
- [ ] A Husk dies in exactly 3 swings; a Charge finishes a 15-HP one
- [ ] Chasers telegraph before striking; stepping away during the windup avoids damage
- [ ] Shield absorbs first, refills after 4 s clear; HP ghost trail reads correctly
- [ ] Dying returns to Menu; Descend starts a fresh run cleanly
- [ ] Respawns never within 6 m; enemies path around pillars
- [ ] Haptics: hit / hurt / charge distinct; off switch works
- [ ] Sustained 60 fps with 12 chasers (overlay); Profiler shows no per-frame GC.Alloc in a 60 s session

Guardrails now in force (GD §12.4) — verify, don't assume:
- [ ] One-shot rule: no single hit exceeds 35 % of max HP (Husk 8 / 140 ≈ 6 %)
- [ ] TTK invariant: basic enemy dies in 3–5 hits (3)
- [ ] Spawn safety: 6 m

**The feel question** (record the answer and *why*): playing for two minutes with no goal — kiting, planting, charging through the pack — is it fun? If not, which number is wrong: speed, swing rate, cone width, charge distance/cooldown, chaser speed, windup, or the stick band?

## Behaviour

1. Tuning edits go to the ScriptableObjects (`Oathbound.asset`, `Husk.asset`) and `StickShaper` constants only; CC §7 / GD §8.1 numbers are updated in the same PR.
2. When every box is ticked: owner merges `dev` → `main` and tags `m1`. Claude does not tag.
3. The PROGRESS entry lists what M2 must know: anything about intents, facts, the enemy system, or the snapshot that turned out different from the specs — especially the cost of `AlliesNearby` (O(n²)) and path refresh at 12 enemies, as inputs to the M2 director's concurrency planning.

## Acceptance

- [ ] Checklist fully ticked; feel verdict recorded
- [ ] `PROGRESS.md` updated; Current State points at M2-01
- [ ] `m1` tag exists on `main`

## Out of scope

- Any stage, wave, or progression logic — M2 and M3 exist so that this milestone stays about *feel*.

## As built

_Filled at merge._
