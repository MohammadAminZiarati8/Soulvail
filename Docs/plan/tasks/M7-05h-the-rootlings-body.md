# M7-05h — The Rootling's body

**Size:** M · **Depends on:** M7-05f (the model), M7-05g (a body per archetype) · **Branch:** `m7-05h-the-rootlings-body`
**Design refs:** GD §9.1, GD §16.3, GD §16.4, GD §17.1; AR §18.4 · **Ledger rows:** none

The fifth task cut from the owner's go of 2026-09-27: *"KayKit's Rig_Medium clips animate it (walk,
attack, hit, death; Generic rig, no retargeting) … red-orange appears only on its attack telegraph."*

## Goal

`Rootling.prefab`, a whole enemy body. It walks, strikes, flinches and dies on `Rig_Medium` clips,
driven by an animator view that reads only what core already publishes. It flashes white on a hit
and glows red-orange over its wind-up.

## Why this shape

- **The animator view is `PlayerAnimatorView`'s shape, for an enemy.** It sits on the model, finds
  its `EnemyView` above it, and reads the view's velocity each frame. It subscribes to
  `EnemyTelegraph`, `EnemyDamaged` and `EnemyDied`, filtering by its own id as `EnemyHitFeedback`
  does. It decides nothing.
- **The strike lands when core's does.** `EnemyTelegraph.Duration` is the wind-up core is counting.
  The attack clip's authored impact time over that duration is the speed it plays at, so the blow
  on screen and the damage are one moment (GD §9.1 rule 1).
- **The flash moves to emission, and only a material that emits sees it.** A textured body's colour
  is the texture times `_BaseColor`, so the white `_BaseColor` flash leaves the texture as it was.
  `EnemyHitFeedback` therefore also writes `_EmissionColor`: white for the flash, and a rise to
  danger red-orange over a wind-up that drops to black at the strike. `M_BoneGrey` has emission
  off, so the three capsules look exactly as they did. `M_Enemy` (M7-05f) has it on, so every body
  that wears it flashes and telegraphs the same way. Red-orange on a telegraph is GD §16.4's own
  rule, and the swell stays beside it.
- **An Animator per body is a cost GD §17.1 names.** It asks for *"no per-enemy Animator where a
  simple skinned or vertex-animated loop will do"*. Four clips that answer three events are a state
  machine, not a loop, so the Rootling has one. It culls its transform updates off screen, is one
  base layer and one upper-body layer, and has no IK or root motion. Its frame cost at 28 bodies is
  a device row.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/EnemyAnimatorView.cs` | Game | **New.** Drives an enemy body's Animator (rules 1–5) |
| `Tests/Game/Views/EnemyAnimatorViewTests.cs` | Tests.Game | **New.** Rules 1–5, 7, 8 |
| `Animation/Controllers/AC_Rootling.controller`, `Animation/Masks/AM_UpperBody.mask` | — | **New.** Its states and its flinch layer's mask (rule 7) |
| `Prefabs/Enemies/Rootling.prefab` | — | **New.** A variant of `Enemy.prefab` wearing `Rootling.fbx` (rule 8) |
| *small edits* | Game | `EnemyHitFeedback` writes `_EmissionColor` (rule 6). `EnemyView.OnDespawn` tells the animator view to forget its life (rule 5) |
| *small edits* | Tests.Game | `EnemyHitFeedbackTests`: rule 6's rows |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Views
{
    public sealed class EnemyAnimatorView : MonoBehaviour
    {
        // [SerializeField] Animator _animator; float _strideSpeed (m/s at 1×); float _strikeSeconds (impact, at 1×)
        [Inject] public void Construct(DomainEventHub hub);
        public void Step(float dt);          // Update calls it with Time.deltaTime
        public void Forget();                // a pooled body's reset: EnemyView.OnDespawn calls it
    }
}
// Animator parameters: Speed, WalkRate, AttackRate (float); Attack, Hit (trigger); Dead (bool)
// EnemyHitFeedback: [SerializeField] float _telegraphGlow; writes _EmissionColor beside _BaseColor
```

## Behaviour

1. **The legs follow the body.** `Step` moves `Speed` toward the `EnemyView`'s horizontal speed.
   `WalkRate` is that speed over `_strideSpeed`, clamped to 0.6–4. Above the ceiling the feet
   slide rather than blur. A zero, negative or non-finite `dt` does nothing.
2. **The strike lands when core's does.** On its own `EnemyTelegraph`, `AttackRate` becomes
   `_strikeSeconds / Duration` and `Attack` fires. A wind-up of zero or less fires nothing, the way
   `EnemyHitFeedback` draws nothing for it.
3. **A hit that does not kill flinches the upper body.** `Hit` fires on its own `EnemyDamaged` when
   `Killed` is false. The flinch plays on an upper-body layer, so the legs keep walking.
4. **Death is final for the life.** On its own `EnemyDied`, `Dead` is set. A corpse neither
   flinches, strikes nor walks. The death clip plays fast enough to fall inside the dissolve.
5. **A pooled body forgets its life.** `Forget` clears `Dead` and the speed and returns the Animator
   to its entry state. `EnemyView.OnDespawn` calls it, beside the feedback's reset, so a body
   rented again never rises mid-death (AR §18.4).
6. **Emission carries the flash and the wind-up.** At rest `_EmissionColor` is black. A hit that
   does not kill writes `Palette.HitFlash` for the flash's length. A wind-up rises from black to
   `Palette.Danger × _telegraphGlow` and drops to black at the strike. A death, a reset and a new
   look write black. `_BaseColor` is written exactly as before, so a material without emission
   renders as it did.
7. **`AC_Rootling` names its clips.** Locomotion is a 1D blend on `Speed` from a crouched idle to a
   crouched walk, played at `WalkRate`. Attack is an overhead two-armed blow at `AttackRate`. Hit
   is on an upper-body layer masked by `AM_UpperBody`. Death is reached from any state on `Dead`.
   The clips come from KayKit's `Rig_Medium` files, and which ones is recorded in *As built*. Root
   motion is off.
8. **`Rootling.prefab` is a whole body.** It is a variant of `Enemy.prefab`, so its colliders, hit
   feedback and health bar are the Husk's. It swaps the capsule for `Rootling.fbx`, scaled to about
   1.2 m, with its Animator on `AC_Rootling`, culling to `CullUpdateTransforms` and an
   `EnemyAnimatorView`. The feedback drives its skinned renderer and dissolves it on
   `M_Enemy_Dissolve`. Its health bar sits above its head.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Walk_SpeedFollowsTheBody` | an `EnemyView` parent moving at 2 m/s / `Step` several frames / `Speed` reaches 2 (rule 1) |
| `Walk_RateIsTheStrideClamped` | stride 1.25; speeds 2, 8 and 0.1 / `Step` / 1.6, 4, 0.6 (rule 1) |
| `Walk_ABadStepDoesNothing` | `Step(0)`, `Step(NaN)` / — / `Speed` unchanged (rule 1) |
| `Attack_LandsWhenCoresBlowDoes` | strike 0.5 s; its own telegraph of 0.4 s / published / `AttackRate` 1.25, `Attack` set (rule 2) |
| `Attack_IgnoresAnotherEnemy` | a telegraph for another id / published / nothing set (rule 2) |
| `Attack_AZeroWindUpFiresNothing` | a telegraph of 0 / published / `Attack` clear (rule 2) |
| `Hit_FlinchesOnAHitThatDoesNotKill`, `Hit_AKillingBlowDoesNotFlinch` | — / published / `Hit` set; clear (rule 3) |
| `Death_IsFinal` | its own death, then a hit and a telegraph / published / `Dead` set; `Hit` and `Attack` clear (rule 4) |
| `Forget_ClearsTheLife` | a dead view / `Forget` / `Dead` clear, `Speed` 0 (rule 5) |
| `View_FindsEnemyViewInItsParent`, `View_WithNoEnemyViewAboveThrowsInStart` | — (rule 1) |
| `Glow_RestsAtBlack`, `Glow_FlashIsWhite`, `Glow_WindUpRisesToDangerAndDropsAtTheStrike`, `Glow_ResetAndDeathWriteBlack` | a feedback body / the event / `_EmissionColor` as rule 6 says (rule 6) |
| `Glow_TheBaseColourIsUntouched` | a wind-up / halfway / `_BaseColor` is the archetype's (rule 6) |
| `Rootling_ControllerNamesItsStates` | `AC_Rootling` / read / the four states and the six parameters the view writes, every motion a `Rig_Medium` clip, the flinch layer masked (rule 7) |
| `Rootling_IsAWholeBody` | `Rootling.prefab` / read / a variant of `Enemy.prefab`; its colliders equal the Husk's; the feedback's renderer is its skinned mesh and its dissolve `M_Enemy_Dissolve`; the Animator on `AC_Rootling`, culling transforms, no root motion (rule 8) |
| `Rootling_StandsAboutAMetreTall` | the prefab's mesh at the model's scale / its height / 1.0–1.4 m (rule 8) |

## Manual verification (Editor / device)

1. **[Editor]** Seen at M7-05i: Rootlings walk at the player crouched and rear up to strike. The
   whole body glows red-orange over the wind-up and the glow drops at the blow. A hit flashes
   white, and a death falls and fades.
2. **[device]** Frame time with 28 animated bodies, against a stage of capsules. Deferred with every
   device row.

## Out of scope

- Naming the body on an asset and rostering it (M7-05i).
- The capsules' own bodies, and a telegraph for the Spitter and the Bloater. They keep the swell.
- An Animation Event per strike. AR §18's M2-art row keeps clips free of events, and the rate does
  the timing.

## As built

**The clips (rule 7).** The clip timings were measured on the model, not read off names.
- **Locomotion** blends `Skeletons_Idle` (hunched, with no root drift) at speed 0 into `Crouching`
  at 0.37 m/s, played at `WalkRate`. `Crouching` is a crouched walk, the only KayKit walk that is
  both hunched (head at 1.03 m against 1.22 upright) and not glacial.
- **Attack** is `Melee_2H_Attack_Chop`, entered at 0.35 s (a transition offset of 0.214) with the
  hands already rising. Its blow lands at 0.87 s, measured where the hands bottom out, so a 0.4 s
  wind-up plays the rest at 1.3×.
- **Hit** is `Hit_A` at 1.4× on the Flinch layer, masked by `AM_UpperBody` (13 of 26 transforms,
  the spine up).
- **Death** is `Death_A` at 1.5×. The body is down by about 0.35 s, inside the 0.5 s fade.
- Every state has *Write Defaults* off, so an empty flinch state cannot snap the masked bones to
  the bind pose.
- **Rejected:** `Sneaking` (0.23 m/s, far too slow), and `Skeletons_Walking` and `Skeletons_Death`,
  which carry root translation (VERSIONS.md).

**Deviations.** *(1)* **The walk-rate ceiling is 4, not 1.8**, and the spec was corrected.
`Crouching` covers 0.46 m/s at rig size, 0.37 at the prefab's 0.8, so a Husk's 2 m/s would need
5.4×. At the ceiling the feet slide by about a quarter at 2 m/s, and by 43 % at depth's 2.6 m/s.
KayKit has no fast hunched gait, and at phone scale the posture reads, not the feet. *(2)*
**`Rootling_StandsAboutAMetreTall` reads the mesh**: 1.71 m × 0.8 = 1.37 m at rest. The skinned
renderer's padded bounds read 1.43. The crouch carries it to about 1.05 m in motion. *(3)* The
variant **switches the base's capsule off** rather than removing it, so `Enemy.prefab` stays the one
place its settings live. *(4)* The Rootling's `_dissolveStretch` is 1: it falls rather than
stretches. *(5)* Two rows beyond the table: `Despawn_TellsTheViewToForget` and
`Glow_AFlashDuringAWindUpReturnsToTheGlow`. `Tint_SurvivesATelegraph`'s comment said the wind-up
was *"deliberately not a red one"*, which is no longer true of emission, and was corrected.

**Cost.** One Animator per Rootling, with transform updates culled off screen, two layers, and no
IK or root motion. Its frame cost at 28 bodies is a device row, with GD §17.1's caveat named in the
spec.

**Verified.** `EnemyAnimatorViewTests` 18 / 18 and the six glow rows. Across the four enemy-view
fixtures, 70 / 70. PlayMode 67 / 67. Suite counts are M7-05d's *As built*. Renders:
`Temp/Renders/rootling_in_motion.png` (the walk, the wind-up glowing to the strike, the flash, the
flinch and the fall), `rootlings_close.png` and `rootlings_hollow_game_camera.png` (a pack in the
Hollow beside a capsule). The poses are sampled into the real prefab, one instance per pose,
because skinning updates once per Editor frame.
