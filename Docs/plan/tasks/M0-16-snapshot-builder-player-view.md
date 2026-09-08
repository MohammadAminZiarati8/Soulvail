# M0-16 — `SnapshotBuilder`, `PlayerView`, `RunTicker`, player + grey-box prefabs

**Size:** M · **Depends on:** M0-06, M0-12, M0-14 · **Branch:** `m0-16-snapshot-builder-player-view`
**Design refs:** AR §3, §4.2, §4.3, §7; CC §2.4

## Goal

The loop closes: senses report (stick + transform → snapshot), the brain decides (`RunSession.Tick`), the body acts (`PlayerView` applies the intent). A capsule moves in a grey box in the Editor, driven entirely through core.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/SnapshotBuilder.cs` | Game | Fills `WorldSnapshot` from `PlayerView` + `InputAdapter` |
| `Game/Views/PlayerView.cs` | Game | `CharacterController` body; applies `PlayerMoveIntent` |
| `Game/Composition/RunTicker.cs` | Game | Owns the frame order |
| `Game/Composition/RunScope.cs` | Game | *modified:* serialized `PlayerView`; registers view, input, builder, ticker |
| `Tests/Game/Adapters/SnapshotBuilderTests.cs` | Tests.Game | Conversion + clamping |
| **Assets** | | |
| `Prefabs/Player/Player.prefab` | — | Capsule mesh, `CharacterController`, `PlayerView` |
| `Prefabs/Arenas/GreyBox.prefab` | — | 36 × 36 m floor, walls, 4 pillars |
| `Scenes/Run.unity` | — | *modified:* arena, player, `Hud.prefab` instance, `EventSystem` + `InputSystemUIInputModule` |

## Public API

```csharp
namespace Soulvail.Game.Adapters;

public sealed class SnapshotBuilder
{
    public const float MaxDt = 1f / 20f;     // a hitch or scene load must never become a 0.3 s simulation step
    public SnapshotBuilder(PlayerView player, InputAdapter input);
    public void Build(WorldSnapshot snapshot, float dt);
}
```

```csharp
namespace Soulvail.Game.Views;

[RequireComponent(typeof(CharacterController))]
public sealed class PlayerView : MonoBehaviour
{
    public Vector3 Position { get; }           // transform.position
    public Vector3 Velocity { get; }           // last applied horizontal velocity (not controller.velocity)
    public void Apply(in PlayerMoveIntent intent, float dt);
}
```

```csharp
namespace Soulvail.Game.Composition;

public sealed class RunTicker : IStartable, ITickable, IDisposable
{
    public RunTicker(IRunSession session, PendingRun pending, WorldSnapshot snapshot,
                     SnapshotBuilder builder, IntentBuffer intents, PlayerView player, InputAdapter input);
}
```

`RunScope.Configure` adds: `RegisterComponent(_playerView)`, `Register<InputAdapter>(Scoped)`, `Register<SnapshotBuilder>(Scoped)`, `RegisterEntryPoint<RunTicker>()`.

## Behaviour

**SnapshotBuilder**
1. `Build`: `snapshot.Clear()`; `Dt = Mathf.Min(dt, MaxDt)`; `MoveInput = input.Move.ToNum()` (already ≤ 1); `PlayerPosition = player.Position.ToNum()`; `PlayerVelocity = player.Velocity.ToNum()`. `EnemyCount` stays 0 (no enemies until M1). **Every consumer integrates with `snapshot.Dt`** — `RunTicker` passes it to `PlayerView.Apply`, never `Time.deltaTime` — so brain and body take the same step.
2. Stick axes map straight through: stick X → world X, stick Y → world Z. The camera in M0 has yaw 0, so this is camera-relative by construction. When yaw becomes configurable, the rotation goes **here**, never in core.

**PlayerView**
3. `Apply`: `horizontal = intent.Velocity.ToUnity()`; vertical velocity integrates gravity `−20 m/s²` and resets to `−1` while grounded; `controller.Move((horizontal + vertical) × dt)`; records `Velocity = horizontal`.
4. Facing: `transform.rotation = LookRotation(intent.Facing.ToUnity(), up)` when `|Facing| > 0`. No smoothing here — core already rotated at 720°/s.
5. `PlayerView` reads no input and holds no gameplay state. It is a body.

**RunTicker** — the frame order, fixed (AR §4.3)
6. `Start`: `Screen.sleepTimeout = SleepTimeout.NeverSleep` (a run must not dim mid-fight; restored on dispose — menus should let the phone sleep); `input.Enable()`; `session.Start(new RunConfig(pending.IsSet ? pending.CharacterId : catalog.Characters[0].Id))` — the fallback mirrors M0-12 rule 6 so Play-in-Run works.
7. `Tick`, every frame, in this order: `builder.Build(snapshot, Time.deltaTime)` → `intents.Clear()` → `session.Tick(snapshot)` → `if (intents.HasPlayerMove) player.Apply(intents.PlayerMove, snapshot.Dt)`.
8. `Dispose`: `session.End()`; `input.Disable()`; `Screen.sleepTimeout = SleepTimeout.SystemSetting`.
9. `Tick` allocates nothing.

**Prefabs / scene**
10. `Player.prefab`: root with `CharacterController` (radius 0.45, height 1.8, center y 0.9, step offset 0.3), `PlayerView`; child capsule mesh with a cyan material (GD §16.4). Spawned in the scene at (0, 0, 0).
11. `GreyBox.prefab`: floor 36 × 36 with a desaturated grey material; four 1 m-thick walls 2 m high; four 1.5 m-radius pillars at (±8, 0, ±8). Everything static, colliders on.
12. `Run.unity`: `RunScope` (with `_playerView` wired), `GreyBox`, `Player`, `Hud.prefab`, `EventSystem` with `InputSystemUIInputModule`. Camera stays where M0-13 left it (M0-18 replaces it).

## Tests

| Test | Given / When / Then |
|---|---|
| `Build_CopiesPlayerPositionAndVelocity` | GameObject + `PlayerView` at (3, 0, −2); disabled adapter / Build(dt 0.02) / `PlayerPosition == (3,0,−2)`, `Dt == 0.02`, `EnemyCount == 0` |
| `Build_CopiesMoveInput` | `InputTestFixture` gamepad stick (0.3, 0.6) / Build / `MoveInput ≈ (0.3, 0.6)` |
| `Build_ClearsPreviousEnemies` | snapshot with `EnemyCount = 3` / Build / `EnemyCount == 0` |
| `Build_ClampsDt` | — / Build(dt 0.3) / `Dt == 0.05`; Build(dt 0.016) / `Dt == 0.016` |
| `Build_AllocatesNothing` | warm-up / 10 000 builds / allocates nothing (`AllocationAssert`) |

`PlayerView.Apply` and `RunTicker` are exercised by the manual steps; they're the body, tested on the phone.

## Manual verification (Editor, then device after M0-19)

1. Play in `Run`. WASD / gamepad: the capsule moves; full speed in a blink, stops with no slide.
2. The capsule's facing follows its movement direction and holds the last facing when idle.
3. Walk into a pillar — blocked, no jitter. Walk into a wall — blocked.
4. Debug overlay (M0-18) later confirms |v| = 5.4 at full input.

## Acceptance

- [x] All tests green — 153 EditMode, up from 148
- [x] Zero errors, zero new analyzer warnings
- [x] Manual steps 1–3 verified in Editor — driven and measured in play mode, numbers in *As built*
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Camera behaviour — M0-18. Enemies in the snapshot — M1-06/M1-16.
- Any decision in `PlayerView`. If you're tempted to put an `if` about gameplay there, it belongs in core.

**Decision to close here, not a deliverable:** `RunState.Motor` (M0-09) is a public handle on a mutable object with a public `Tick`, so nothing in the compiler stops a view from advancing it a second time — the `internal` seal covers `RunState`'s own fields, not what is reachable through them. This task is the proof of whether anything outside core needs the handle at all: `PlayerView` takes velocity and facing from the intent, never from `RunState`. If nothing in `Soulvail.Game` reads `Motor` by the end of this task, say so in *As built* and narrow it — `internal` handle, public `Velocity` / `Facing` reads; M0-10's `Start_BuildsState` asserts through `Motor.Facing` and changes by one line. Small additive edit; doesn't count toward the Files table.

## As built

Built as specified, with seven deviations — the full account is in [PROGRESS](../PROGRESS.md), M0-16. In short:

- **`RunTicker` takes a `ContentCatalog`.** The Public API block above lists seven parameters and no catalog, while rule 6 requires `catalog.Characters[0].Id`; the two disagreed and the behaviour rule won. Eight parameters.
- **Two materials beyond the Files table** — `Materials/M_PlayerCyan.mat` (`#22D3EE`) and `Materials/M_BoneGrey.mat` (`#6E6A63`) — because rules 10 and 11 ask for a cyan capsule and a grey floor and Unity has no coloured default.
- **`Player.prefab` has a `FacingMarker` child** as well as the capsule mesh. A capsule is rotationally symmetric, so without it manual step 2 is unobservable rather than merely subtle.
- **`Run.unity` gains one `Directional Light`.** The scene's ambient-only lighting rendered floor, walls and pillars at a single flat value, which makes steps 1–3 unjudgeable. GD §17.1 asks for exactly one.
- **`RegisterEntryPoint<RunTicker>(Lifetime.Scoped)`** rather than the defaulted call — identical behaviour in a child scope, honest label.
- **Pillars use a `MeshCollider`.** The cylinder primitive's `CapsuleCollider` has rounded caps that sit inside the mesh silhouette once scaled, so the player would sink into a pillar before being stopped.
- **`skinWidth` 0.045 and `minMoveDistance` 0** on the controller. The default `minMoveDistance` discards sub-millimetre steps, which is exactly the tail of CC §2.4's 0.08 s deceleration.

**Decision closed: `RunState.Motor` is now `internal`.** Nothing in `Soulvail.Game` reads the handle — verified by search, and by `PlayerView` taking both values from the intent — so it is narrowed, with `PlayerVelocity` and `PlayerFacing` as the public reads. The names are qualified rather than the bare `Velocity` / `Facing` this section suggested, to match the neighbouring `PlayerPosition` and `WorldSnapshot.PlayerVelocity`. `RunSessionTests` changed on two lines, as M0-10 predicted.

**Verification.** 153 EditMode tests green (148 before), zero compile errors, zero new analyzer warnings, clean Console.

Manual steps 1–4 were driven in play mode by an editor-update script that injects a known stick deflection and measures, rather than by eye. `sleepTimeout` read `NeverSleep` and `targetFrameRate` 60, confirming `RunTicker.Start` ran:

| Check | Measured | Expected |
|---|---|---|
| 1, 4 — top speed | `PlayerView.Velocity` 5.400 m/s; 5.400 m/s over 0.999 s of travel | 5.4 (CC §2.5) |
| 1 — stops with no slide | \|v\| 0.0000 after release; 0.00000 m moved over the next 30 frames | 0 |
| 2 — facing follows movement | rotY 0.00 moving +Z; 90.00 on stick +X | 0 / 90 |
| 2 — idle holds facing | rotY 90.00 held 60 frames after release | unchanged |
| 3 — pillar blocks, no jitter | centre distance 1.995 m; 0.00000 m jitter over 30 frames | 1.5 + 0.45 + 0.045 skin |
| 3 — wall blocks, no jitter | z 17.505 (north), x −17.505 (west); 0.00000 m jitter over 50 frames | 18 − 0.45 − 0.045 |
| grounding | y at rest 0.0450 | skinWidth above the floor |

Two things worth keeping from the run. Both collision stops land exactly one `skinWidth` short of the geometry, which is the controller behaving correctly and also proves the pillar's `MeshCollider` is the cylinder rather than a capsule — a capsule would have stopped further out. And while pressed against a wall, `PlayerView.Velocity` still reads **5.400**: core is still asking for full speed and the controller is resolving it to zero displacement. That is the live demonstration of why `Velocity` is the requested velocity and not `CharacterController.velocity`, which would have reported 0 and told core the player had stopped trying to move.
