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

_6 000 bytes or fewer, measured._

**Rule 4, measured: the translation reaches the mesh, so the blend plays copies.** The four
`Dodge_*` move `Rig_Medium/root` 0.25 m forward, 0.65 back and 0.5 to each side, in model units,
and the hips up to 0.19 more. The importer's root node is empty, so nothing extracts it. Sampled on
the body at 0.66, the hips reach 0.22, 0.54, 0.46 and 0.46 m from the `PlayerView` (forward,
backward, left, right). `Roll_TheBodyStaysInItsCapsule` went red on the imported clips, 0.54 m on
the backward roll. The blend now plays `Animation/Clips/A_Dodge_*_InPlace.anim`. Each copy keeps
the source's 237 other curves and its clip settings, drops `root`'s three position curves, and
carries no events. The hips then stay within 0.07, 0.11, 0.13 and 0.13 m, and the row is green.
Each copy is about 500 KB of YAML. The row rolls all four ways, because forward alone stays under
0.3 m on the imported clip. The Knight's `Charge` plays that imported `Dodge_Forward`: [parking
lot](../ROADMAP.md#parking-lot).

**Deviation 1: `Empty`'s two exits also wait for the roll.** An `Any → Empty` on `Dodging` alone
would ping-pong while the bow is raised: `Empty → Aim` on `Aiming` fires on the next frame. So
`Empty → Draw` and `Empty → Aim` gain `Dodging` false. The `Any → Empty` goes first among the
layer's Any State transitions, ahead of `Release`, and cannot re-enter `Empty`. In a run a roll
also holds fire, so `Aiming` is already false. The guard covers the sandbox, where nothing holds on
a published roll, and a running-shot node later. `Locomotion → Dodge` takes 0.05 s and the way back
0.15 s. The Ranger's roll reaches `ChargeEnded` at 0.35 s, against a 0.4 s clip at speed 1.

**Deviation 2: two ripple rows in `RangerSandboxTests`.** `Loop_ShootsOnlyStandingStill` asserted
*"Chosen, not faced"*, which rule 5 reverses. It now asserts the hold, true on the run and false on
the stop, and that the dummy is named. `Ranger_TheLegsAreADirectionalBlend` took `.Single()` of the
base layer's states and now finds `Locomotion` by name. `Sandbox_RunsWithTheBowDown` also asserts
that nothing is held before the push.

**Rule 3, resolved: the direction is read once, in the view's own frame.** `RunSession.TickBody`
holds the facing through a dash, so the clip picked on the first frame is right for the last. A
zero or non-finite direction rolls forward rather than writing a NaN (AR §18.3).
`Roll_SetsDodgingAndItsDirection` also turns the body to +X and asserts that the same roll reads as
forward, which is what separates the body's frame from the world's.

**Rules 1 and 2, resolved: death is latched.** `Aiming` is `target ≥ 0 && !holding && !dead`, and
after `PlayerDied` neither `VolleyReady(true)` nor a roll does anything: `PlayerAnimatorView`'s
`_dead`. Any fact that computes `Aiming` false ends the string of shots the draw speed is measured
over. RS-02a's V3 called that string a volley, before RS-03b gave the word to the fan.

**The glow.** `VolleyGlow` under `Bow` is KayKit's stringless `bow.fbx`. That mesh is the limbs of
the bow held, in the same space, so no rest-pose string ghosts over a drawn one. It is scaled
(1.2, 1.8, 1.06) about the limbs' centre and wears `M_DecoyCyan`: URP Unlit, `#22D3EE` at α 0.5,
with no shadows and no probes. It is the project's one translucent player cyan; a glow material of
its own is M7's art round. The prefab saves it inactive, and `Start` hides it as well.

**Rule 5.** `RangerSandboxLoop` names the targeter's choice whether it runs or not, and publishes
`HoldFireChanged` on each change, starting unheld as core does. With the running shot on, it never
holds, so it never says so.

**How it was checked.** The capsule row went red on the imported clips and green on the copies,
29 / 29 on its fixture. The first full EditMode pass failed
`RangerTests.Ranger_WearsItsBodyAndFliesArrows`: *"Expected same as \<Ranger\> But was
\<Ranger\>"*. The definition's field and a fresh load were `==` but not the same managed object.
That happened after `SaveAsPrefabAsset` and again after the bisect's refresh, and a script reload
cleared it both times ([Traps §5](../../Traps.md)). One full PlayMode pass failed
`Loop_ShootsOnlyStandingStill` (*No arrow on the run*, 1 for 0). The row passed alone after a
reload. Then the whole change was stashed, and on `dev`'s code a full pass failed the same row with
the same message, 55 / 1 ([Traps §8](../../Traps.md)). One EditMode pass failed
`Store_RoundTripsClaimed` on the file lock ([Traps §7](../../Traps.md)), and it passed 34 / 34 alone.

**Not done here:** manual steps 1–3, which are the owner's.
