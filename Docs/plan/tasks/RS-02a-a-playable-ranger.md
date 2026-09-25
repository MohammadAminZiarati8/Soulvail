# RS-02a — A playable Ranger, in a sandbox

**Size:** M · **Depends on:** RS-00 · **Branch:** `rs-02a-a-playable-ranger`
**Design refs:** CC §2.4, §3.1–3.6, §4.2; AR §18 (the M2-art rows); GD §16.4 · **Ledger rows:** none

## Goal

KayKit's Ranger, holding a bow, idles, runs, and stops to shoot arrows at training dummies in
`RangerShowcase.unity`. The game's own stick moves it, the game's camera follows it, and core decides
every number. Shooting on the move, the skill the owner means it to be, is a switch.

## Why now, and on KayKit's body

- **The owner's request of 2026-09-25:** the Ranger with proper idle, run and shooting animations, a
  scene of its own, and the game's camera and inputs. The request came before the model RS-01a–c
  plan has been drawn, so it is built on the Ranger the project has, KayKit's.
- **The animator does not wait for the new body.** RS-01b keeps `Rig_Medium`'s 23 bones and names
  unchanged, so `AC_Ranger` plays on the new mesh with no edit. RS-01c's prefab replaces the `Body`
  child.
- **It shoots standing still, by the owner's ruling of the same day:** *"running and shooting
  should be a skill"*. This is the Ranger's rule. CC §4.2's still holds, since attacking never slows
  the Ranger; here it is moving that stops the attack.
- **This takes RS-02's animator half and RS-04's scene half.** RS-02 keeps "a body per class", which
  is the player wearing the Ranger in a run.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/RangerAnimatorView.cs` | Game | **New.** Rules V1–V7 |
| `Game/Sandbox/RangerSandboxLoop.cs` | Game | **New.** Rules L1–L9 |
| `Game/Sandbox/RangerSandboxScope.cs` | Game | **New.** The scene's composition root |
| `Tests/Game/Views/RangerAnimatorViewTests.cs` | Tests.Game | **New.** V1–V7, EditMode |
| `Tests/PlayMode/RangerSandboxTests.cs` | Tests.PlayMode | **New.** L1–L9, V4's guard, S1–S3, on the scene |
| *small edits* | | `Tests/PlayMode/Soulvail.Tests.PlayMode.asmdef` gains `Unity.InputSystem`, for a virtual gamepad |
| *assets* | | `Animation/Controllers/AC_Ranger.controller` and `AC_TrainingDummy.controller`, `Animation/Masks/AM_Ranger_UpperBody.mask` (new folder), `Prefabs/Player/Player_Ranger.prefab`, `Prefabs/Projectiles/Arrow.prefab`, `Scenes/RangerShowcase.unity` |

## Public API

```csharp
namespace Soulvail.Game.Views
{
    [RequireComponent(typeof(PlayerView))]
    public sealed class RangerAnimatorView : MonoBehaviour
    {
        public const float ForwardStrideSpeed = 3.94f, SideStrideSpeed = 3.08f;   // m/s at scale 1
        public const float AuthoredShotSeconds = 1.2f, MaxShotSpeed = 4f;
        public const float NockedNormalized = 0.375f, FullDrawNormalized = 0.6f;
        public const string UpperBodyLayer = "Upper Body";
        [Inject] public void Construct(DomainEventHub hub);
        public static void Blend(Vector3 localVelocity, float bodyScale, out float moveX, out float moveZ, out float playback);
        public static float ShotSpeed(float interval);
        public static float StringPull(bool inDraw, bool inAim, float normalizedTime);
        public void Step(float dt);                       // Update calls it with Time.deltaTime
    }
}

namespace Soulvail.Game.Sandbox;
public sealed class RangerSandboxLoop : IStartable, ITickable, IDisposable
{
    public const string HitTrigger = "Hit";
    public RangerSandboxLoop(InputAdapter input, DomainEventHub hub, PlayerView player, ProjectileViews arrows,
        MovementSpec movement, TargetingSpec targeting, WeaponSpec weapon, Transform muzzle, Animator[] dummies, float aimHeight);
    public int CurrentTargetId { get; }
    public int ArrowsInFlight { get; }
    public void Step(float dt);                           // Tick calls it with Time.deltaTime
}
```

## Behaviour

**The view** — the facts a run publishes, drawn with a bow:

- **V1 — The legs read the velocity in the body's frame.** Divided per axis by the speed its clip's
  feet travel at (`ForwardStrideSpeed`, `SideStrideSpeed`, times the body's scale), it is placed on a
  unit circle: idle at the centre, forward run at (0, 1), the same run reversed at (0, −1), strafes
  at (±1, 0). Outside the circle the direction stays on it and the rest becomes `MoveSpeed`, so a
  3 m/s body plays its clip faster instead of sliding. A degenerate scale or velocity reads as still.
- **V2 — `Step` writes `MoveX`, `MoveZ` and `MoveSpeed`** from `PlayerView.Velocity`, smoothed at
  24 m/s², and advances the view's own clock. A zero, negative or non-finite step does nothing.
- **V3 — `TargetChanged` raises the bow:** `Aiming` is true for any id ≥ 0, blocked included
  (CC §3.6). −1 lowers it and ends the volley, so the next shot's speed is not measured across the
  time the bow was down.
- **V4 — `PlayerAttacked` draws**, setting `Shoot`, unless the upper layer is in, or crossfading
  into, `Draw` or `Aim`. `ShotSpeed` becomes `ShotSpeed(gap)` on the view's clock, in `[1, 4]`, and
  the first shot leaves it.
- **V5 — The player's `ProjectileFired` (`SourceId` 0) releases:** `Release` is set and a pending
  `Shoot` is reset. An enemy's shot is ignored.
- **V6 — The bowstring (`bow_withString`'s `Draw` shape)** is 0 until the arrow is nocked, rises
  with the hand to 1 at full draw, is held at 1 in `Aim`, and is 0 otherwise. It follows the arms
  across a crossfade.
- **V7 — Guards.** A null hub throws. `Construct` subscribes to exactly the three facts.

**The loop** — `RunTicker`'s order, one character wide:

- **L1 — Only dummies inside the acquire range are candidates** (CC §3.1 step 1). With none, nothing
  is faced, the bow stays down and nothing fires.
- **L2 — A dummy in reach is faced** by the motor's `faceDirection`, and `TargetChanged` is
  published whenever the targeter changes.
- **L3 — In range, the bow fires on `Weapon`'s cadence.** `PlayerAttacked` goes out at swing start.
  At the damage frame `ProjectileFired` goes out, `SourceId` 0, from the muzzle, at the shot's own
  dummy plus `aimHeight`, with a flight of XZ distance over the shot speed (AR §18.4).
- **L4 — An arrow lands when its flight is up:** `ProjectileImpacted` (hit) is published, the
  census returns the body, and the dummy's `Hit` trigger fires.
- **L5 — The Ranger shoots standing still.** The stick moves the body with yaw 0, straight through.
  While the stick is pushed, or the body is still slowing, the Ranger faces where it runs, and
  `TargetChanged(−1)` lowers the bow. A shot being drawn is dropped with `Weapon.Reset`, so no arrow
  leaves on the move. The targeter keeps choosing underneath. Once stopped, the Ranger faces its
  target and the first shot starts at once. **`shootWhileMoving`** (the scope's *Shoot While
  Moving*, off by default) is the running shot: it faces the target and shoots on the move, and
  the legs strafe (V1).
- **L6 — Out of reach, `TargetChanged(−1)`** is published and the bow comes down.
- **L7 — Each step is clamped to `SnapshotBuilder.MaxDt`.** A degenerate step does nothing.
- **L8 — Guards:** null arguments, a weapon that is not `Projectile`, and a negative or non-finite
  `aimHeight` are all refused.
- **L9 — `Start` enables the stick.** `Dispose` disables it and lands every arrow in the air as a
  miss.

**The scene and the assets:**

- **S1 — The Run scene's camera and stick.** `FollowCamera` at 57° / 0 / 16 m / 0.12 s on the Ranger;
  `FloatingStick` on the left 45 % writing `<Gamepad>/leftStick`; an `InputSystemUIInputModule`.
  The scene stays out of `EditorBuildSettings`.
- **S2 — `Player_Ranger.prefab`:** `Player.prefab`'s controller, KayKit's `Ranger.prefab` as `Body`
  at 0.6615, `AC_Ranger` with no root motion (AR §18), and `bow_withString` under `handslot.l` at
  identity. Everything wears `M_Ranger`.
- **S3 — `AC_Ranger`:** the legs are V1's blend on `MoveX` and `MoveZ` at `MoveSpeed`. `Upper Body`
  overrides at weight 1 through a mask active from the spine up, 13 of 32 paths.
  `Empty → Draw → Aim`, `Any → Release`, back to `Draw` or `Empty` on `Aiming`, and `Draw → Empty`
  when `Aiming` drops. `ShotSpeed` defaults to 1.2, the sandbox bow's pace, for a first volley that
  has nothing measured yet.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Rule |
|---|---|
| `RangerAnimatorViewTests.Blend_*` (8 rows, 11 cases) | V1 |
| `Step_WritesTheLegsFromTheBody`, `Step_IgnoresADegenerateStep` (4) | V2 |
| `Target_RaisesTheBowAndLowersIt`, `Target_ABlockedTargetStillRaisesTheBow`, `Target_LoweringTheBowEndsTheVolley` | V3 |
| `Attack_*` (3), `ShotSpeed_*` (3 rows, 7 cases) · `RangerSandboxTests.Sandbox_ABowAlreadyDrawnReleasesWithoutDrawingAgain` | V4 |
| `Fired_ByThePlayerReleasesAndDropsAPendingDraw`, `Fired_ByAnEnemyIsNotTheBowsRelease` | V5 |
| `StringPull_*` (3) | V6 |
| `Construct_ANullHubThrows`, `Construct_SubscribesToTheThreeFacts` | V7 |
| `Sandbox_StartsIdleWithNothingInRange` | L1 |
| `Sandbox_FacesADummyInReachAndRaisesTheBow` | L2 |
| `Sandbox_ShootsAtTheWeaponsCadence` | L3 |
| `Sandbox_AnArrowLandsAndTheDummyFlinches` | L4 |
| `Sandbox_RunsWithTheBowDown`, `Sandbox_StopsAndShoots`, `Loop_ShootsOnlyStandingStill`, `Loop_TheRunningShotShootsOnTheMove` | L5, V1 on the real controller |
| `Sandbox_LowersTheBowWhenNothingIsInReach` | L6 |
| `Loop_AHitchIsClampedToTheSnapshotStep`, `Loop_ADegenerateStepDoesNothing` (4) | L7 |
| `Loop_NullArgumentsAreRefused`, `Loop_ABowThatIsNotAProjectileWeaponIsRefused`, `Loop_ANegativeOrNonFiniteAimHeightIsRefused` (3) | L8 |
| `Loop_StartReadsTheStickAndDisposeStops`, `Loop_DisposeLandsEveryArrowInTheAir` | L9 |
| `Scene_FollowsTheRangerWithTheRunCamera`, `Scene_HasTheGamesFloatingStick`, `Scene_IsOutOfTheBuild` | S1 |
| `Ranger_HoldsItsBowInTheLeftHandslot` | S2 |
| `Ranger_TheArmsAreALayerMaskedToTheSpineAndUp`, `Ranger_TheLegsAreADirectionalBlend` | S3 |

## Manual verification (Editor / device)

1. **[Editor]** Open `Scenes/RangerShowcase.unity` and press Play **with the Editor in front**
   (Traps §3). *Expected: the Ranger stands with its bow lowered, and the camera matches a run's.*
2. **[Editor]** Move with WASD, a gamepad's left stick, or a drag on the Game view's left 45 %.
   *Expected: it runs holding the bow, and the feet do not slide.*
3. **[Editor]** Walk to within 10 m of a dummy and let go. *Expected: it turns and draws, and shoots
   once a second. The string snaps as the arrow leaves, the arrow arcs in, and the dummy flinches.*
4. **[Editor]** Move again mid-draw. *Expected: the bow comes down, no arrow leaves, and it runs
   facing where it goes. Stop, and it shoots again.*
5. **[Editor]** Tick *Shoot While Moving* on `RangerSandbox` before Play. *Expected: it faces the
   dummy while it moves, strafes or runs backwards, and never stops shooting.*
6. **[device]** Multi-touch and feel on a phone: deferred with every device row.

## Out of scope

- **A run's player wearing the Ranger:** RS-02, which puts a body field on `CharacterSpec`.
- **An arrow in the hand; hit, death, dodge and cast poses; audio; a trail:** they arrive with the
  run's body and M7's art and audio.
- **The Ranger's kit, and the running shot as a skill.** The bow's numbers are placeholders on the
  scope. The kit, and the skill that turns *Shoot While Moving* on in a run, are RS-03's and the
  owner's.
- **Tap-to-focus and the skill button** in the sandbox. The stick is the only control a Ranger
  without a kit needs.

## As built

_6 000 bytes or fewer, measured._

**This spec was written with the build.** The owner asked for the work directly, with no spec
before it, and the rules above are the ones the tests pin.

**The numbers were measured, not chosen.** Sampling each clip on the Ranger and taking the median
speed of the grounded foot: `Running_HoldingBow` 3.94 m/s at scale 1, the strafes 3.10 and 3.06,
and `Walking_Backwards` 0.70, which is 0.46 m/s at the body's 0.6615. A backpedal at 3 m/s would
play it at 6.5×, so the backwards run is `Running_HoldingBow` at time scale −1. The bow clips:
`Ranged_Bow_Draw` reaches full draw at 0.8 s of 1.333 and then holds; the string snaps about 0.1 s
into `Ranged_Bow_Release`, whose follow-through ends at 0.4 s. `Ranged_Bow_Aiming_Idle` is the full
draw held, with the left hand extended, so the bow is in `handslot.l`, as RS-01c predicted. At one
shot a second the shot plays at 1.2×, and the arrow leaves 0.72 s into it, just past full draw.

**Finding 1: the targeter needs the caller's range filter.** Handed every dummy on the field,
`Targeter` found nothing that scored and fell to CC §3.6: it held facing on the nearest dummy,
blocked, 14 m away. The first capture showed the Ranger drawing at nothing on Play. L1 now gathers
inside the acquire range, as CC §3.1 step 1 says, which is what `PlayerCombat` does.
`Sandbox_StartsIdleWithNothingInRange` pins it.

**The owner's ruling, applied after the first handover: it shoots standing still.** The loop gates
the weapon on the stick and the body's speed, below 0.05 m/s. A shot being drawn is dropped with
`Weapon.Reset`, and "faced" is published as −1 while it runs. The view needed no new input,
because the bow comes down on the fact a run already uses for "nothing to face". Lowering the bow
now ends a volley (V3), or the first arrow after a run would measure three seconds of running as a
fire rate and release before full draw. The running shot stays behind a switch until RS-03 makes
it a skill.

**Finding 2: a draw needs a way down.** With no `Draw → Empty` edge, a target lost mid-draw was
drawn to full and held before the bow came down, about 1.5 s of aiming at nothing.
`Sandbox_LowersTheBowWhenNothingIsInReach` failed on it, and the edge was added in place, so the
controller kept its GUID. A shot already committed still releases through `Any → Release`.

**The release is core's launch fact, not a timer.** The view draws on `PlayerAttacked` and releases
on the player's `ProjectileFired`, which a run publishes for the Gravecaller today. The string
therefore snaps on the frame the arrow leaves at any fire rate. `ShotSpeed` has a floor of 1, unlike
`PlayerAnimatorView`'s swing: a slow bow holds at full draw rather than drawing in slow motion.

**Reused unchanged:** `InputAdapter`, `FloatingStick`, `FollowCamera`, `PlayerView`, `ProjectileView`
and `ProjectileViews` on the Game side, and `PlayerMotor`, `Targeter`, `TargetScorer` and `Weapon`
in core. No core file changed.

**Names.** `Prefabs/Player/Player_Ranger.prefab`, because KayKit's `Ranger.prefab` already sits in
`Prefabs/Characters/` (RS-01c). The owner renamed the scene to `RangerShowcase.unity` in the first
commit, and every reference follows. The class's name is still the owner's to give.

**A held stick is fragile in a PlayMode pass** (Traps §8). A focus change reset the test's pad, so
a row passed alone and failed in the full pass. `IgnoreFocus` fixed that, but let keys typed in
another window through. The rows now also silence every keyboard and pointer, and three full passes
came back 53 / 53.

**How it was checked.** An unfocused Editor froze a CLI-entered Play session at frame 27, and
`EditorApplication.Step` did not move it (Traps §3). A temporary `[Explicit]` PlayMode test rendered
close-up frames of idle, run, draw, release, strafe and backpedal, and was deleted. The assets were
authored through a `run_script` builder that is not committed. `capture_game_view` writes under
`Assets/`, which is now in Traps §4.
