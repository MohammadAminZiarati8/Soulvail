# M1-14 — `MovementSkillSpec`, `ChargeSkill` (pure): cooldown, buffer, i-frame window

**Size:** S · **Depends on:** M1-02 · **Branch:** `m1-14-charge-skill`
**Design refs:** CC §5, §7 (Charge); CH §3.1

## Goal

The Oathbound's movement skill exists as a tested state machine — buffered input, cooldown, invulnerability window with the touch-latency trail — before any capsule dashes.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/MovementSkillSpec.cs` | Core | Charge numbers |
| `Core/Combat/ChargeSkill.cs` | Core | Request → start → active → cooldown |
| `Tests/Core/Combat/ChargeSkillTests.cs` | Tests.Core | Every rule |
| *small edits* | | `CharacterSpec`/`CharacterDefinition`/`Oathbound.asset` + `MovementSkill` (10 / 0.22 / 2.5 / 0.15 / 20 / 5 / 0.05) |

## Public API

```csharp
namespace Soulvail.Core.Content;

public enum MovementSkillKind { Charge }   // Shroudstep, Blink later

public sealed class MovementSkillSpec
{
    public MovementSkillKind Kind { get; }
    public float Distance     { get; }   // 10
    public float Duration     { get; }   // 0.22
    public float Cooldown     { get; }   // 2.5
    public float InputBuffer  { get; }   // 0.15
    public float Damage       { get; }   // 20
    public float Knockback    { get; }   // 5
    public float IFrameTrail  { get; }   // 0.05
}
```

```csharp
namespace Soulvail.Core.Combat;

public sealed class ChargeSkill
{
    public ChargeSkill(MovementSkillSpec spec);
    public Stat    Cooldown          { get; }   // base spec.Cooldown
    public bool    IsActive          { get; }   // within Duration
    public bool    IsInvulnerable    { get; }   // within Duration + IFrameTrail
    public bool    IsReady           { get; }   // cooldown elapsed and not active
    public float   CooldownFraction  { get; }   // 1 just used → 0 ready
    public Vector2 Direction         { get; }   // unit XZ of the current/last charge
    public void Request(float now);              // a press; buffered
    /// True on the tick the charge starts.
    public bool Tick(float dt, float now, Vector2 stickXZ, Vector2 facingXZ);
    public void Reset();
}
```

## Behaviour

1. `Request(now)` records the press time (latest wins).
2. On `Tick`: a pending press is **live** if `now − pressTime <= InputBuffer`. If live, `!IsActive`, and `now >= readyAt` → **start**: `Direction = |stick| > 0 ? normalize(stick) : facing`; `activeUntil = now + Duration`; `invulnUntil = activeUntil + IFrameTrail`; `readyAt = now + Cooldown.Value`; press consumed; return `true`.
3. A press older than `InputBuffer` is discarded without effect. A press made while active or cooling stays pending and fires when it becomes possible, if still within the buffer — this is exactly "a tap 0.15 s before the cooldown ends still fires".
4. `IsActive = now < activeUntil`; `IsInvulnerable = now < invulnUntil`; `CooldownFraction = clamp((readyAt − now) / Cooldown.Value, 0, 1)`.
5. `Cooldown` is a `Stat`; the value is sampled at start (tree nodes reduce it later).
6. Cannot start while active. `Reset` clears everything (ready immediately).
7. `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `Request_WhenReady_StartsNextTick` | ready / Request(0), Tick(0.016, 0.016) / true; `IsActive`, `Direction` == stick |
| `Direction_FallsBackToFacing` | stick zero, facing (0,1) / — / Direction (0,1) |
| `Direction_IsNormalised` | stick (0.3, 0.4) / — / (0.6, 0.8) |
| `IFrames_CoverDurationPlusTrail` | started at 0 / Tick to 0.21 / invulnerable; 0.26 / invulnerable; 0.28 / not |
| `Active_EndsAtDuration` | started at 0 / 0.219 / active; 0.221 / not |
| `Cooldown_FromStart` | started at 0 / Tick to 2.49 / `!IsReady`; 2.51 / `IsReady` |
| `BufferedPress_FiresWhenCooldownEnds` | started at 0; Request(2.4) / Tick at 2.45 / no start; Tick at 2.5 / starts |
| `StalePress_Discarded` | Request(2.0); cooldown ends at 2.5 / Tick at 2.5 / no start |
| `Request_WhileActive_QueuedWithinBuffer` | active until 0.22; Request(0.2) / Tick at 0.3 (cooling) / no start; not stale-checked until possible → at 2.5 press is stale (0.2 + 0.15 < 2.5) / no start |
| `CooldownFraction_Timeline` | started at 0 / at 0 → 1; at 1.25 → 0.5; at 2.5 → 0 |
| `CooldownModifier_Applies` | −20 % PercentAdd / start / ready at 2.0 |
| `CannotRestart_WhileActive` | active / Request, Tick / no second start |
| `Reset_MakesReady` | cooling / Reset / `IsReady`, fraction 0 |
| `Tick_AllocatesNothing` | warm-up / 10 000 ticks / allocated-bytes delta == 0 |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Emitting the intent, suppressing the motor, applying i-frames to `Health`, pass-through damage — M1-15.
- A second charge (tree node) — M3-12.

## As built

_Filled at merge._
