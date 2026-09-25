# RS-03b — The volley

**Size:** M · **Depends on:** RS-03a · **Branch:** `rs-03b-the-volley`
**Design refs:** CC §3.7, §4; GD §6.2, §12.4; AR §18.4 · **Ledger rows:** none

## Goal

A class can carry a volley: after every *n* shots, its next shot is a fan of arrows, each arrow
dealing more damage. *n* is a `Stat`, so the Ranger's Volley passive and its two upgrades are plain
`ModifyStat` nodes (RS-03c): 5, then 4, then 3.

## The owner's design of 2026-09-25

*"Every (n) shots, the next shot will be a multiple shot with increased damage. n = 5, 4, 3."*

- **Read literally:** after *n* ordinary shots, the next one is the volley. At *n* = 5, shots 1–5
  are ordinary, the 6th is a volley, 7–11 are ordinary, and the 12th is a volley.
- **A fan at the target**, the owner's pick: the middle arrow goes where an ordinary shot would, and
  the others spread either side of it to catch whoever stands beside the target.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/VolleySpec.cs` | Core | **New.** The authored numbers: arrows, fan, damage (rule 1) |
| `Core/Combat/Volley.cs` | Core | **New.** The three stats, the counter and the fan (rules 2–5) |
| `Tests/Core/Combat/VolleyTests.cs` | Tests.Core | **New.** Rules 1–8 |
| `Core/Combat/PlayerCombat.cs` | Core | **Edit.** Builds the volley. A damage frame offers a fan into a small shot queue (rules 4, 6) |
| *small edits* | Core | `CharacterSpec` gains optional `VolleySpec volley = null`. `PlayerStat` appends `VolleyEvery`, `VolleyArrows` and `VolleyDamage`, resolved through `RequireVolley` and `Has`, Kindling's pattern. `RunSession` fires every queued shot (rule 6) and sweeps the class's own tree at `Start` (rule 7). `CombatEvents` gains `VolleyReady`. `ModifyStatTests.Stats_ResolveEveryMember` gains three rows |
| *small edits* | Game | `CharacterDefinition` gains the volley fields, zero meaning no volley |

## Public API

```csharp
public sealed class VolleySpec
{
    public const int MaxArrows = 7;
    public VolleySpec(int arrows, float fanAngleDeg, float damageMultiplier);
    public int Arrows { get; }            // 2..MaxArrows
    public float FanAngleDeg { get; }     // (0, 180), the whole fan, edge arrow to edge arrow
    public float DamageMultiplier { get; } // >= 1, per arrow
}
public sealed class Volley
{
    public Volley(VolleySpec spec, IDomainEvents events);
    public Stat Every { get; }    // base 0: no volley until a node gives one
    public Stat Arrows { get; }   // base spec.Arrows
    public Stat Damage { get; }   // base spec.DamageMultiplier
    public bool IsReady { get; }  // the next shot is a volley
    public int ShotsTowardNext { get; }
}
public enum PlayerStat { /* …FireWhileMoving, */ VolleyEvery, VolleyArrows, VolleyDamage }
public readonly struct VolleyReady { public readonly bool IsReady; public VolleyReady(bool isReady); }
```

## Behaviour

1. **`VolleySpec` refuses what cannot fly:** arrows outside `2..MaxArrows`, a fan outside (0°, 180°),
   and a damage multiplier below 1 or non-finite.
2. **The count is a floored `Every`**, as `Kindling.Cap` floors `MaxStacks`. Below 1, or non-finite,
   there is no volley and the counter holds where it is. The base is 0, so a class with a volley
   spec fires none until a node gives it `Every`.
3. **The counter.** Each ordinary shot adds one. When the count reaches `Every`, the volley is
   ready, and `VolleyReady(true)` is published once. The next shot is the volley. It resets the
   count to 0 and publishes `VolleyReady(false)`. If `Every` falls under the current count (a rank
   taken mid-count), the volley is ready at once. A dropped shot (RS-03a rule 3) counts nothing,
   because only a shot that leaves counts.
4. **The fan.** The middle aim point is the ordinary shot's, lead-solved (CC §3.7). Arrow *i* of *m*
   is aimed at that point rotated about the shooter by `−fan/2 + i·fan/(m−1)` on XZ, at the same
   distance and height. Every arrow leaves from the same origin at the weapon's shot speed and
   radius, dealing `ShotDamage(Weapon.Damage × Damage)`. `m` is `Arrows` floored and clamped to
   `2..MaxArrows`. `Damage` below 1 is read as 1.
5. **One swing, one draw, one tell.** A volley is one `PlayerAttacked` at the swing's start and
   `m` shots at its damage frame. Fire rate, Focus and the hold (RS-03a) treat it as one shot.
6. **Shots queue rather than overwrite.** `PendingShot` becomes a queue of up to
   `VolleySpec.MaxArrows`, and `TryTakeShot` hands them out in aim order. `RunSession` takes every
   queued shot on the tick it was made, and `ProjectileSystem.Fire` numbers them in that order.
   `ProjectileSystem` refuses a shot when full, as it does today. The capacity (32) holds a
   seven-arrow volley with room to spare, and this is asserted.
7. **A class's own tree may name only addresses it has.** At `Start`, every `ModifyStat` aimed at the
   player in the class's own tree must name a stat `PlayerStats.Has` answers. A node naming
   `VolleyEvery` on a class without a volley is refused, naming the node and the stat. Found while
   planning: nothing checks this today. Only a borrowed branch is checked (`SplashFlow`), and an own
   node naming a missing address throws from `SkillTree.Take` after the node is recorded.
8. **A class without a volley is untouched,** and a borrowed Volley branch is refused by
   `SplashFlow`'s existing address check, with its existing reason shown.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_RefusesArrowsFanAndDamageOutOfRange` | — / — / each refused, naming the field — rule 1 |
| `Every_ZeroMeansNoVolley` | a volley at base 0 / 20 shots / none is a volley — rule 2 |
| `Every_IsFloored` | `Every` 4.9 / — / a volley after 4 — rule 2 |
| `Counter_FiveOrdinaryThenAVolley` | `Every` 5 / 12 shots / shots 6 and 12 are volleys — rule 3 |
| `Counter_RanksCountDown` | `Every` 5 → 4 → 3 by Flat −1 twice / — / cycles of 5, 4 and 3 ordinary — rule 3 |
| `Counter_ALowerEveryReadiesAtOnce` | count 4, `Every` 5 → 3 / — / ready, one `VolleyReady(true)` — rule 3 |
| `Counter_ADroppedShotCountsNothing` | a swing dropped by the hold / — / count unchanged — rule 3 |
| `Fan_ThreeArrowsThirtyDegrees` | 3 arrows, 30°, target 8 m ahead / volley / aims at −15°, 0°, +15°, all 8 m, same height — rule 4 |
| `Fan_EachArrowCarriesTheMultiplier` | damage 13, ×1.5 / volley / three shots of 19.5 — rule 4 |
| `Fan_ArrowsAreClampedAndFloored` | `Arrows` 99 and 1.7 / — / 7 and 2 — rule 4 |
| `Volley_IsOneTell` | a volley / — / one `PlayerAttacked`, three shots — rule 5 |
| `Queue_EveryShotIsFiredOnItsTick` (RunSession) | a volley / ticked / three `ProjectileFired`, consecutive ids, in aim order — rule 6 |
| `Queue_CapacityHoldsTheLargestVolley` | — / — / `ProjectileCapacity >= VolleySpec.MaxArrows × 2` — rule 6 |
| `OwnTree_AMissingAddressIsRefusedAtStart` | a tree naming `VolleyEvery` on a class without a volley / `Start` / refused, naming node and stat — rule 7 |
| `OwnTree_TheShippedTreesPass` | the three shipped classes / `Start` / accepted — rule 7 |
| `Splash_ABorrowedVolleyBranchIsRefused` | a class without a volley borrowing a Volley branch / — / `ui.splash.refused.address` — rule 8 |
| `Stats_ResolveEveryMember` (existing) | +3 rows — `Has` false without a volley — rule 8 |

## Manual verification (Editor / device)

_None here. The volley has no class to fire it until RS-03c, and it is first seen at RS-03d._

## Out of scope

- **The Ranger's nodes and numbers:** RS-03c.
- **What a volley looks like:** RS-03d lights the bow while `IsReady`. The arrows fly as ordinary
  arrows (RS-02c).
- **Kindling and a volley together:** no class has both. Each landing arrow would add a stack, which
  is `ProjectileSystem.Land`'s rule today, and it is left as it is.

## As built

_6 000 bytes or fewer, measured._

**Deviation 1, the Tests table over the Files table: two rows live in `Tests.Game`.**
`Tests.Core` cannot see `BootInstaller.ProjectileCapacity` or the shipped assets.
`Queue_CapacityHoldsTheLargestVolley` joined `InstallerTests`. `OwnTree_TheShippedTreesPass`
joined `EmberwrightTreeTests`, whose catalog is the one that loads all three shipped trees. This
is RS-03a's precedent, and both are small edits.

**Deviation 2, the Tests table over the Public API: `Volley` gains five members.** `ArrowCount`
and `DamageMultiplier` are rule 4's clamped readings, and `Fan_ArrowsAreClampedAndFloored` reads
the first. `Loose()`, `Aim(arrow, arrows, origin, middle)` and `Reset()` are what `PlayerCombat`
calls. Core has no `InternalsVisibleTo`, so all five are public. `PlayerCombat` gains `Volley`,
null without a spec, which is `Kindling`'s shape.

**Rule 3, resolved: ready is a reading of the count against `Every`, kept current both ways.**
`Volley` subscribes to `Every.Changed`. A lower `Every` readies the volley at once, as the rule
asks. A raised `Every`, or one driven under 1, stands a ready volley down with
`VolleyReady(false)`. The spec names only the lowering direction; without the other, a view would
keep a lit bow for a volley that no longer comes. A stat change never moves the count; only a shot
does.

**Rule 2's "the counter holds" means an ordinary shot counts nothing while `Every` is under 1.**
So the count is 0 when a first node gives the class its volley, and the cycle starts clean.

**Rule 6, resolved: `PendingShot` stays, as the queue's head.** Its readers in
`PlayerProjectileTests` and `HoldFireTests` compile unchanged and mean what they meant. The queue
is a fixed array of `VolleySpec.MaxArrows` with a head and a count, so a volley allocates nothing.
A new damage frame still clears what was not taken, which is the old overwrite bargain.
`RunSession` takes shots in a `while` loop.

**Rule 7's sweep walks every node, take and cast, player-aimed only.** It is
`RequireNoMinionTarget`'s walk, beside it in `Start` and before `RunStarted`. `Self` is left to
whoever casts it, and `Minions` to the walk above it. The message names the node, the stat and
the class.

**Two things the spec does not say, left as they are.** A volley on a cone class never counts,
because only `OfferShot` calls `Loose`. No class authors one, and no rule asks for a refusal.
The count is not saved: a resumed run starts it at 0, which is `Kindling`'s bargain.

**`CharacterDefinition`: `_volleyArrows` 0 is the switch**, Kindling's count a fourth time. The
fan and the multiplier default to the Ranger's 30° and ×1.5. The three class assets are
untouched: the new fields load at their defaults, and nothing re-serialised them. RS-03c authors
the Ranger's.

**Existing rows moved for twenty `PlayerStat` members.** In `Stats_ResolveEveryMember`, the Ember
fixture carries a volley on request, the switch gains three rows, and the row now ends by asserting
`Has` false and a refused `Resolve` without a volley. `PlayerStatCoverageTests` counts 20.
`StatBlockTests` gains the three in `NotAnOathbounds`, its refused count and its ordinal list.

**How it was checked.** A red check made three mutations at once: `while` back to `if` in
`RunSession`, the sweep call removed, and `OnEveryChanged` made inert. The six fixtures went
122 / 3, and the three red rows were exactly `Queue_EveryShotIsFiredOnItsTick`,
`OwnTree_AMissingAddressIsRefusedAtStart` and `Counter_ALowerEveryReadiesAtOnce`. The first full
EditMode pass failed one row, `LocalJsonSaveStoreTests.Store_RoundTripsClaimed`, on an
`IOException` from `File.Replace`. That fixture passed 34 / 34 alone, and the next two full passes
were green on the same tree ([Traps §7](../../Traps.md)).

**Not done here:** nothing is visible until RS-03c gives a class the volley.
