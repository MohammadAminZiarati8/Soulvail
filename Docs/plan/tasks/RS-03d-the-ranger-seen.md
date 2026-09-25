# RS-03d — The Ranger, seen: the bow held, the volley lit, the roll

**Size:** S · **Depends on:** RS-03c · **Branch:** `rs-03d-the-ranger-seen`
**Design refs:** GD §16.4; CC §3.5; AR §18 (the M2-art rows) · **Ledger rows:** none

## Goal

What RS-03a to RS-03c decide, the Ranger shows: the bow comes down while it runs, the bow lights
when the next shot is a volley, and the roll is a roll in the direction it goes. The sandbox moves
onto the same fact a run publishes.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/RangerAnimatorView.cs` | Game | **Edit.** Rules 1–4 |
| `Tests/Game/Views/RangerAnimatorViewTests.cs` | Tests.Game | **Edit.** Rows for rules 1–4 |
| *small edits* | Game | `RangerSandboxLoop` publishes `HoldFireChanged` instead of `TargetChanged(−1)` (rule 5). `RangerSandboxTests`' running rows assert it |
| *assets* | | `AC_Ranger`: a `Dodge` 2D blend of `Dodge_Forward`, `_Backward`, `_Left` and `_Right` on `DodgeX` and `DodgeZ`, entered on `Dodging`, and the upper layer's `Any → Empty` on `Dodging`. `Bodies/Ranger`: a `VolleyGlow` child on the bow, cyan `#22D3EE` |

## Public API

```csharp
// RangerAnimatorView gains:
[SerializeField] private GameObject _volleyGlow;   // optional, shown while a volley is ready
// Subscribes, besides its three facts: HoldFireChanged, VolleyReady, ChargeStarted, ChargeEnded,
// and PlayerDied (which puts the glow out and lowers the bow).
```

## Behaviour

1. **The bow is up only when there is a target and fire is not held.** `Aiming` is
   `target ≥ 0 && !holding`, so `HoldFireChanged(true)` lowers a raised bow through `Draw → Empty`
   and `Aim → Empty`, and `HoldFireChanged(false)` raises it again if a target is held. Lowering ends
   the volley of shots (RS-02a's V3), whichever fact lowered it.
2. **The volley is lit.** `VolleyReady(true)` shows `_volleyGlow`, and `VolleyReady(false)` hides
   it. It is hidden at `Start` and on death. It is GD §16.4's player cyan, because a volley is the
   player's own and not a danger or a reward.
3. **The roll plays the clip that matches its direction.** `ChargeStarted` sets `Dodging` and writes
   the dash's direction in the body's frame into `DodgeX` and `DodgeZ`. So a roll to the side while
   the Ranger faces its target plays `Dodge_Left` or `_Right`, not a forward roll sliding sideways.
   `ChargeEnded` clears `Dodging`. The upper layer is `Empty` for the roll's length, so the arms roll
   with the body.
4. **A roll never moves the body.** `ChargeMotion` moves it, and root motion stays off (AR §18). The
   four `Dodge_*` clips are among the nine that carry root translation (KayKit `VERSIONS.md`). The
   builder measures whether that translation reaches the skinned mesh with root motion off. If it
   does, the blend uses copies with the root curve removed, and *As built* says which.
5. **The sandbox publishes what a run publishes.** `RangerSandboxLoop` keeps `TargetChanged` for the
   targeter's choice, and holds fire with `HoldFireChanged`. That is one contract, tested in both
   places.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Hold_LowersTheBowAndRaisesItAgain` | a target held / `HoldFireChanged(true)`, then `(false)` / `Aiming` false, then true — rule 1 |
| `Hold_WithNoTargetStaysDown` | no target / `HoldFireChanged(false)` / `Aiming` false — rule 1 |
| `Volley_LightsTheBowAndPutsItOut` | `_volleyGlow` dressed / `VolleyReady(true)`, then `(false)` / active, then inactive — rule 2 |
| `Volley_NoGlowIsNotAnError` | `_volleyGlow` empty / `VolleyReady(true)` / nothing thrown — rule 2 |
| `Volley_DeathPutsTheGlowOut` | glow shown / `PlayerDied` / hidden, `Aiming` false — rule 2 |
| `Roll_SetsDodgingAndItsDirection` | the body facing +Z / `ChargeStarted((1, 0))` / `Dodging` true, `DodgeX` 1, `DodgeZ` 0 — rule 3 |
| `Roll_EndsOnChargeEnded` | — / `ChargeEnded` / `Dodging` false — rule 3 |
| `Roll_TheBodyStaysInItsCapsule` (PlayMode, `RangerShowcase`) | a roll / each frame / the hips within 0.3 m of the `PlayerView` on XZ — rule 4 |
| `Sandbox_RunsWithTheBowDown` (existing, changed) | — / running / `HoldFireChanged(true)`, `TargetChanged` still names the dummy — rule 5 |

## Manual verification (Editor / device)

1. **[Editor]** A run as the Ranger with Volley taken. *Expected: the bow glows cyan when the next
   shot is the fan, and goes dark after it.*
2. **[Editor]** Roll forward, backward and to each side, while standing and while shooting.
   *Expected: a roll that matches the direction, and the body never jumps back after it.*
3. **[device]** Whether the glow reads at phone scale. Deferred with every device row.

## Out of scope

- **An arrow in the hand, and hit and death poses:** M7's art round.
- **Volley arrows that look different:** the fan and the glow are the tell. A distinct arrow is a
  later look on `CharacterLook`.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
