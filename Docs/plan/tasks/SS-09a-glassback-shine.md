# SS-09a — The glassback in core: glint, shine, open, on the hero's target

**Size:** M · **Depends on:** — · **Branch:** `ss-09a-glassback-shine`
**Design refs:** GD §8.9, §8.1

## Goal

`ChaserBehaviour` honours an optional `ShineSpec`. A chaser that is the hero's current target, in range and off cooldown, glints, then shines (no damage lands, still targetable), then stands open at double damage. The reflection itself is SS-09b.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ShineSpec.cs` | Core | `SidewinderSpec`'s shape |
| `Core/Ai/ChaserBehaviour.cs` | Core | `Glint`, `Shine`, `Open` states, cooldown |
| `Tests/Core/Ai/GlassbackShineTests.cs` | Tests.Core | |
| *small edits* | | `EnemySpec`: param + property + guards; `EnemyEvents.cs`: `EnemyShineChanged`, `ShinePhase` |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class ShineSpec  // SidewinderSpec's validation: finite, > 0; OpenDamageTaken ≥ 1; MinFlight ≤ any flight
{
    public ShineSpec(float glintSeconds, float shineSeconds, float openSeconds, float cooldown, float triggerRange,
        float openDamageTaken, float boltDamage, float boltSpeed, float boltRadius, float boltMinFlight, float boltInterval);
        // 0.5, 2, 1, 4, 12, 2, 6, 14, 0.8, 0.3, 0.4 — bolt fields read by SS-09b
    // one get-only property per parameter, same names
}
// EnemySpec(..., SidewinderSpec sidewinder = null, ShineSpec shine = null) last; EnemySpec.Shine { get; }
// ChaserState.Glint, Shine, Open, appended.
// public enum ShinePhase { None, Glint, Shine, Open }
// public readonly struct EnemyShineChanged { int Id; ShinePhase Phase; float Seconds; }
// ChaserBehaviour.IsShining { get; }  (internal; SS-09b reads it)
```

## Behaviour

1. No `ShineSpec`: unchanged; every existing chaser test stays green.
2. `EnemySpec` refuses a `ShineSpec` on a non-Chaser, or beside a `PackSpec`, `LeapSpec` or `BroodSpec`.
3. **Trigger.** In `Chase` (not mid-wind-up, strike or recover), off cooldown, with `ctx.Player.Targeter.CurrentTargetId == id` and the hero within `TriggerRange` XZ: enter `Glint`. Any other state, or another target, never glints.
4. **Glint.** Stands facing the hero for `GlintSeconds`. Damage lands as normal. A stun (pooled CC, as built) during the glint ends it: back to `Chase`, cooldown started, no shine.
5. **Shine.** Stands for `ShineSeconds`. `Health.SetExternalInvulnerable(true)`, while `IsVulnerable` **stays true**, so the targeter keeps it. `IsShining` is true. Crowd control does not end a shine.
6. **Open.** Invulnerability off. A `DamageTaken` multiplier of `OpenDamageTaken` for `OpenSeconds`, removed on exit by its own source, so no other modifier is touched. Stands still.
7. After `Open`: `Chase`, cooldown `Cooldown` from that tick.
8. One `EnemyShineChanged` on each phase entry (Glint/Shine/Open with its seconds) and one `None` on the return to `Chase` or on death.
9. `Reset()` and death mid-phase: invulnerability off, multiplier removed, `Chase`/idle as built, cooldown cleared.
10. A shining chaser's `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoShine_ChaserUnchanged` | scorpion spec, targeted / full cycle / no `Glint`, no `EnemyShineChanged` |
| `Spec_BadPairings_Throw` | each case of rule 2 and bad numbers / construct / throws |
| `Targeted_InRange_Glints` | hero targets it at 10 m / tick / `Glint`, one event (Glint, 0.5) |
| `NotTargeted_NeverGlints` | hero targets another enemy / 10 s / never `Glint` |
| `OutOfRange_NeverGlints` | targeted at 13 m / tick / `Chase` |
| `Glint_DamageLands` | in `Glint` / 10 damage / applied 10 |
| `Glint_Stunned_CancelsAndCools` | stun in glint / ticks / `Chase`, no shine for 4 s |
| `Shine_BlocksDamage_StaysTargetable` | in `Shine` / damage / `Blocked`; `IsVulnerable` true; targeter keeps it |
| `Open_DoublesDamage_ThenRestores` | in `Open` / 10 damage / 20; after open / 10 |
| `Cycle_TimesAndEvents` | targeted, ticked 8 s / read / Glint 0.5 → Shine 2 → Open 1 → Chase, then 4 s before a second glint |
| `Shine_StunDoesNotEnd` | stun in shine / ticks / shine lasts 2 s |
| `DeathMidShine_Cleans` | killed in open / read / multiplier gone, `None` event |
| `Reset_MidShine_Cleans` | shining / `Reset` / vulnerable to damage, `Chase`, no cooldown |
| `Tick_Shine_NoAlloc` | full cycle / 300 ticks / `AllocationAssert.None` |

## Out of scope

- Bolts and which hits reflect (SS-09b). Model, body, data (SS-09c–e).
- Pets or minions as a trigger. Elites.

## As built

_Commit body._
