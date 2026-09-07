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

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] Manual steps 1–3 verified in Editor
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Camera behaviour — M0-18. Enemies in the snapshot — M1-06/M1-16.
- Any decision in `PlayerView`. If you're tempted to put an `if` about gameplay there, it belongs in core.

## As built

_Filled at merge._
