# SS-11a — The priest in core: a mend beam on the most hurt ally, broken by a hit

**Size:** M · **Depends on:** — · **Branch:** `ss-11a-priest-mend`
**Design refs:** GD §8.11, §8.1, JG-16 (the Moss Shaman)

## Goal

`ShamanBehaviour` honours an optional `MendSpec`. With one, it no longer pulses. It channels a heal into the most hurt ally for up to 3 s, hides behind that ally, and any damage it takes breaks the beam and stops it mending for 2 s.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/MendSpec.cs` | Core | `ShineSpec`'s shape and validation |
| `Core/Ai/ShamanBehaviour.cs` | Core | `Mend` state, target pick, break |
| `Core/Events/ShamanEvents.cs` | Core | `PriestMendStarted`, `PriestMendEnded`, `MendEndReason` |
| `Tests/Core/Ai/PriestMendTests.cs` | Tests.Core | |
| *small edits* | | `EnemySpec`: `MendSpec mend = null` last + property + guard (Shaman only) |

## Public API

```csharp
namespace Soulvail.Core.Content;
public sealed class MendSpec   // finite, > 0; HurtBelow in (0, 1]; HealPerSecond ≤ 1
{
    public MendSpec(float range, float breakRange, float hurtBelow, float channelSeconds,
        float healPerSecond, float cooldown, float brokenSeconds);
        // 10, 12, 0.9, 3, 0.08, 3, 2
    // one get-only property per parameter, same names
}
// EnemySpec.Mend { get; } — only beside a ShamanSpec.
namespace Soulvail.Core.Events;
public enum MendEndReason { Done, Healed, Lost, Broken }   // Lost = ally died or left BreakRange
public readonly struct PriestMendStarted { int Id; int AllyId; float Seconds; }
public readonly struct PriestMendEnded { int Id; int AllyId; MendEndReason Reason; }
// ShamanState.Mend appended. ShamanBehaviour.MendTargetId { get; } (−1 when none)
```

## Behaviour

1. No `MendSpec`: the shaman is unchanged, and every `ShamanBehaviourTests` row stays green.
2. With one, there is no pulse and no haste. `Reposition` and `Flee` are as built.
3. **Pick.** Off cooldown and not broken: among living allies within `Range` XZ (not itself, not another Shaman-kind, not a boss, as `Ally`), those below `HurtBelow` of max health. It takes the lowest health fraction, ties to the nearest. With one, it enters `Mend` and publishes `PriestMendStarted`.
4. **Anchor.** While a target is held, it is the anchor (rule 5's hiding goal), so the priest stands behind it from the hero.
5. **Mend.** It stands planted and turns to the ally. Each tick it heals `HealPerSecond × max × dt` (`Health.Heal`, `EnemyHealed` as the shaman's pulse publishes it, at most once per 0.25 s so the event rate stays flat). It ends:
   - after `ChannelSeconds` (`Done`),
   - when the ally is at full health (`Healed`),
   - when the ally dies or is beyond `BreakRange` (`Lost`),
   - or when the priest takes any damage (`Broken`, rule 6).
   Each end publishes `PriestMendEnded` and starts `Cooldown`.
6. **Broken.** Any damage that lands on the priest, from any source, in any state, stops mending for `BrokenSeconds`. The cooldown runs after that. Damage is read as a health drop since the last tick. A heal on the priest is not a break.
7. If the hero comes within `FleeDistance` during `Mend`, the mend ends `Lost` (cooldown only, no `BrokenSeconds`) and the priest flees as built.
8. Death or `Reset()` mid-mend publishes `PriestMendEnded(Lost)` once and clears the target.
9. `Tick` allocates nothing.

## Tests

| Test | Given / When / Then |
|---|---|
| `NoMend_ShamanUnchanged` | shaman spec / full pulse cycle / as built; no `PriestMendStarted` |
| `Spec_Guards` | mend on a Chaser; bad numbers / construct / throws |
| `Mend_NoPulseNoHaste` | priest, hurt allies / 10 s / no `ShamanCast`, no haste |
| `Pick_MostHurtInRange` | allies at 50 % (8 m), 30 % (11 m), 60 % (5 m) / tick / target the 50 % one |
| `Pick_TieToNearest` | two at 40 % / tick / nearer |
| `Pick_IgnoresHealthyAndBosses` | ally at 95 %, a boss at 10 % / tick / no mend |
| `Mend_HealsEightPercentPerSecond` | ally 100 HP at 20 % / 3 s / about 44 %, `Done` |
| `Mend_EndsWhenFull` | ally at 85 % / tick / `Healed` before 3 s |
| `Mend_LostOnDeathOrRange` | ally killed; ally walked to 13 m / tick / `Lost` |
| `Mend_BrokenByHeroHit` | mending / 5 damage to priest / `Broken`; no mend for 2 s, then 3 s cooldown |
| `Mend_BrokenByPoison` | poisoned priest / ticks / no mend while health drops |
| `Mend_AnchorIsTarget` | mending / read / `AnchorId == MendTargetId` |
| `Mend_FleeEnds` | hero to 3 m / tick / mend ended, `Flee` |
| `Death_MidMend_EndsOnce` | killed mending / read / one `PriestMendEnded(Lost)` |
| `Tick_Mend_NoAlloc` | mend cycle / 300 ticks / `AllocationAssert.None` |

## Out of scope

- Model, body, beam drawing (SS-11b–c). Data and roster (SS-11d).
- Mending pets, bosses, itself or another priest. Elites.

## As built

_Commit body._
