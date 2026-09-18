# M3-11b — Consecrate: ground you have to stand on

**Size:** M · **Depends on:** M3-11a (the clock and the pattern), M3-06, M3-05 · **Branch:** `m3-11b-consecrate-and-zones`
**Design refs:** CC §6.4, §7; CH §3.1, §4.2; GD §2 (P2), §12.4, §13.1; AR §5, §14, §18.1, §18.2, §18.3; ADR-0003, ADR-0009 · **Ledger rows:** 1 (a heal is survivability, not damage — the same note M3-11a rule 11 makes), 4 (a per-frame containment test joins the `GC.Alloc` row)

## Goal

CC §6.4's Consecrate: a patch of ground that heals the player while they stand in it — the first thing in the game that asks them to **stop moving**, which is the only interesting thing a heal can do in a game whose whole skill expression is positioning.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Combat/ZoneSystem.cs` | Core | the live zones: where, how big, how long, and the pulse — `ProjectileSystem`'s shape, one milestone later |
| `Core/Effects/SpawnHealZone.cs` | Core | the primitive and its handler, one file per pair (M3-05's shape) |
| `Game/Authoring/SpawnHealZoneDefinition.cs` | Game | its Inspector half — **block namespace** (Traps §5) |
| `Tests/Core/Combat/ZoneSystemTests.cs` | Tests.Core | placement, containment, the pulse, expiry, capacity, allocation |
| `Tests/Core/Effects/SpawnHealZoneTests.cs` | Tests.Core | the handler against a real `Health` and a real blackboard |
| *small edits* | | `Core/Combat/CombatBlackboard.cs` + `PlayerPosition` (rule 2); `Core/Combat/PlayerCombat.cs` — one line in `UpdateBlackboard`; `Core/Events/SkillEvents.cs` + `ZoneSpawned`, `ZoneExpired`, `ZoneHealed` (rule 8); `Core/Run/RunSession.cs` — builds and ticks the system, registers the handler (rule 3); `Core/Run/RunState.cs` + `ActiveZoneCount` and `ZoneAt`; **AR §18.1** gains the zone step's row |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Combat;

/// A patch of ground that does something to whoever is standing in it. One system, because
/// a second kind of zone is a field on the entry rather than a second list (rule 10).
public sealed class ZoneSystem
{
    public const int Capacity = 8;

    public ZoneSystem(Health player, CombatBlackboard blackboard, IDomainEvents events);

    public int Count { get; }
    public Vector3 PositionAt(int index);
    public float RadiusAt(int index);

    /// <summary>Places one where the player is standing. Returns its index.</summary>
    public int Spawn(float radius, float duration, float healPerPulse, float pulseInterval,
                     float now, object source);

    /// <summary>Pulses the zones that are due and retires the ones that are over.</summary>
    public void Tick(float now);

    public void Clear();
}
```

```csharp
namespace Soulvail.Core.Effects;

/// CC §6.4's Consecrate: healing ground, placed under the caster.
public sealed class SpawnHealZone : IEffect
{
    public SpawnHealZone(float radius, float duration, float healPerPulse, float pulseInterval);
    // all four finite and > 0

    public float Radius { get; }
    public float Duration { get; }
    public float HealPerPulse { get; }
    public float PulseInterval { get; }
}

public sealed class SpawnHealZoneHandler : IEffectHandler<SpawnHealZone>
{
    public SpawnHealZoneHandler(ZoneSystem zones, IClock clock);
    // Apply:  zones.Spawn(radius, duration, healPerPulse, pulseInterval, clock.Now, source)
    // Remove: nothing — a zone owns its own life (rule 9)
}
```

```csharp
// CombatBlackboard (added)
public Vector3 PlayerPosition;      // written by UpdateBlackboard, read by the zones (rule 2)

// RunState (added) — reads for the overlay and the view (AR §18.2)
public int ActiveZoneCount { get; }
public Vector3 ZoneAt(int index);
```

```csharp
namespace Soulvail.Core.Events;

public readonly struct ZoneSpawned
{
    public readonly int Index; public readonly Vector3 Position;
    public readonly float Radius; public readonly float Duration;
}

public readonly struct ZoneExpired { public readonly int Index; }

/// A pulse landed. Carries what was actually restored, which is 0 at full health.
public readonly struct ZoneHealed { public readonly int Index; public readonly float Amount; }
```

## Behaviour

**The zone**

1. **Placed, not carried, and that is the whole design.** A heal that followed the player would be a regeneration buff with a radius drawn under it; a heal that stays where it was cast makes the player choose between the ground that is healing them and the ground that is safe. GD §2's second pillar is that skill expression **is** positioning, and this is the first active that asks anything of it. CC §6.4 calls it a *"healing zone"* and CH §4.2 a *"heal zone"* — a zone is a place.
2. **Placed where the player is standing at the moment of the cast, and that needs the position core does not keep.** `PlayerMotor` holds velocity and facing but no position — the player's position arrives on the `WorldSnapshot` each tick and is read, used and forgotten. So `CombatBlackboard` gains `PlayerPosition`, written in `UpdateBlackboard` beside the nine fields it already fills. The blackboard is the right home for the reason it holds the rest: it is *"what a skill's condition might need to know"*, filled once per tick before the runner is asked anything (M3-06 rule 7), so at cast time it is **this** frame's position and not last frame's. `TriggerField` deliberately gains no member — a position is not a threshold anything is compared against.
3. **Ticked after `TimedEffects`, which is after the runner.** The chain is `PlayerCombat` → `SkillRunner` → `TimedEffects` → **zones** → `ProjectileSystem` → the behaviours. After the runner so a zone cast this tick exists this tick; after the timed effects so the two expiry mechanisms never interleave; and still above the projectile step, which is where M3-06 rule 7 put the whole block. An **AR §18.1** row.
4. **It heals in pulses, not continuously.** `healPerPulse` every `pulseInterval`, scheduled as absolute times against the simulated clock — `Weapon`'s shape, and M3-11a rule 2's argument: no accumulator to drift, so the same number of pulses land at 30 fps and at 120. Continuous healing was the alternative and it is worse three ways: it publishes sixty events a second or none, it is untestable without a tolerance, and it reads as a number sliding rather than as the game doing something. A pulse is also what a view can flash (M3-11c).
5. **A pulse heals whoever is inside at the instant it lands**, tested by squared distance on the XZ plane against the zone's radius — no square root, no allocation, and the same flat-world assumption every other range check in the project makes. In and out between pulses costs nothing and gains nothing: standing in it when the pulse lands is the whole of the contract, which is legible in a way a "time inside" accumulator would not be.
6. **The player is the only thing it heals in V1, and the class says so rather than implying it.** `ZoneSystem` takes `Health` — the player's — not an `EnemySystem`. M5-04's Wights are the first allies and are the task that widens this; an enemy-healing zone is an affix (M7-02) and would be a second system or a faction on the entry, decided then. Deciding it now would be inventing a faction model for one caster.
7. **`Heal` is already promised to be safe here, in the code.** `Health.Heal`'s own remarks say a heal *"does not reset the Aegis recharge delay… a heal that reset the Aegis delay would make a Consecrate zone (M3-11) actively counterproductive for the Oathbound"* — the class named this task and made the promise before the task existed. Nothing here has to arrange it; the row below pins it so a later change cannot quietly break it.
8. **Three events, all after the state moved.** `ZoneSpawned` carries position, radius and duration so a view can place and size a decal and run its own countdown without a read — `SpawnTelegraphed`'s shape (M2-12b). `ZoneHealed` carries what was **actually** restored, which is zero at full health, because a view that flashed on a wasted pulse would be lying. `ZoneExpired` retires the view.
9. **A zone owns its own life and the effect handler's `Remove` does nothing.** This is the deliberate difference from M3-11a: a granted shield is *state on the player* and has to be taken back, while a zone is a thing in the world that ends when it ends. Nothing holds it in `TimedEffects`, and re-casting Consecrate places a **second** zone rather than refreshing the first — which is a real, visible consequence of a floored cooldown and is exactly the sort of thing CH §4.1's floor exists to bound.
10. **One system, fixed capacity eight, and a second kind of zone is a field rather than a second list.** Consecrate at a 12 s cooldown and a 6 s duration can have at most one of its own; eight is headroom for the damaging and slowing zones GD §13.2 and M7-02 will want, and `SpawnZone` was already named in M3-05's Out of scope as a primitive the shape was waiting for. A ninth throws rather than growing (M3-11a rule 2's argument). Nothing allocates after construction.

**Consecrate**

11. **Authored, and every number is in the asset: 3.5 m, 6 s, 3 HP per 0.5 s pulse, on a 12 s cooldown.** Thirty-six hit points if the player stands in it for the whole six seconds, against 140 max and CC §6.4's trigger of `HpFraction Below 0.6` — about a quarter of a bar, for holding ground through half of a wave. A first pass in M2-03's sense: the owner playtests and retunes in `Consecrate.asset` (M3-12b), not in a task. The 12 s cooldown floors at 4.8 s (CH §4.1's 40 %), which is the number M3-12's cooldown nodes are bounded by.
12. **What it does to ledger row 1: nothing, again.** A heal is survivability. Row 1 is time-to-kill and M3-12's TTK test must exclude Consecrate exactly as it excludes Bulwark (M3-11a rule 11). Two of the milestone's first three actives move the wrong side of the row on purpose — the tree's damage nodes are what close it, and M3-12 is where that is owed.

## Tests

| Test | Given / When / Then |
|---|---|
| `Zone_SpawnsWhereThePlayerIs` | blackboard position (3, 0, −2) / `Spawn` / `PositionAt(0)` is (3, 0, −2), one `ZoneSpawned` carrying it (rules 1, 2) |
| `Zone_DoesNotFollowThePlayer` | spawned at the origin / move the blackboard position 10 m / `PositionAt(0)` unchanged (rule 1) |
| `Blackboard_CarriesThePlayerPosition` | a snapshot at (5, 0, 1) / `PlayerCombat.Tick` / `Blackboard.PlayerPosition` is it (rule 2) |
| `Blackboard_PositionIsFreshAtCastTime` | a cast triggered on the tick the player moved / `RunSession.Tick` / the zone is at this tick's position, not last tick's (rules 2, 3) |
| `Zone_PulsesOnItsInterval` | 3 HP / 0.5 s, player inside, hp 100 of 140 / `Tick` at 0.49, 0.5, 0.99, 1.0 / heals at 0.5 and 1.0 only, hp 106 (rule 4) |
| `Zone_PulsesAreAbsolute` | the same zone / thirty 0.2 s ticks vs six 1.0 s ticks over 6 s / the same number of pulses either way (rule 4) |
| `Zone_HealsOnlyWhatIsInside` | radius 3.5, player 3.4 m away, then 3.6 m / a pulse each / healed, then not (rule 5) |
| `Zone_ContainmentIsFlat` | the player 1 m above the zone's plane, 2 m out / a pulse / healed — XZ only (rule 5) |
| `Zone_InAndOutBetweenPulsesCostsNothing` | the player leaves at 0.2 s and returns at 0.4 s / the 0.5 s pulse / healed in full (rule 5) |
| `Zone_ExpiresAfterItsDuration` | 6 s / `Tick(6.01)` / `Count` 0, one `ZoneExpired`, and a pulse due at 6.5 never lands (rule 9) |
| `Zone_HealedCarriesTheActualAmount` | hp 138 of 140, a 3 HP pulse / — / `ZoneHealed.Amount` 2 (rule 8) |
| `Zone_HealedIsZeroAtFullHealth` | hp 140 of 140 / a pulse / `ZoneHealed.Amount` 0 — published, and honest (rule 8) |
| `Zone_DoesNotHealTheDead` | the player dead / a pulse / nothing healed (`Health.Heal`'s contract) |
| `Zone_DoesNotResetTheAegisDelay` | the Aegis counting down from a hit, a pulse lands / — / the delay is unchanged and the Aegis refills on time — **the promise `Health.Heal`'s remarks made to this task** (rule 7) |
| `Zone_SecondCastPlacesASecondZone` | one live / `Spawn` again / `Count` 2, both pulsing, both on their own clocks (rule 9) |
| `Zone_OverlappingZonesBothPulse` | two zones over the player / a pulse interval / healed twice (rule 9) |
| `Zone_CapacityThrows` | eight live / a ninth / throws naming the capacity (rule 10) |
| `Zone_ClearDropsEverything` | three live / `Clear` / `Count` 0, no `ZoneExpired` — the run's end publishes nothing |
| `Zone_AllocatesNothing` | eight live, player inside, warm-up / 10 000 × `Tick` / allocated-bytes delta == 0 (rule 10) |
| `Zone_TicksAfterTheTimedEffects` | a Consecrate and a Bulwark cast on one tick / `RunSession.Tick` / the grant, then the zone step — `FrameOrderTests`' shape (rule 3) |
| `Zone_BoundaryLeavesItRunning` | a live zone / clear the stage / still live, still pulsing — M2-10's rule that a door heals nobody, and its mirror |
| `Effect_Guards` | each of the four at 0, −1, NaN, ∞ / ctor / throws each |
| `Handler_ApplySpawns` | `SpawnHealZone(3.5, 6, 3, 0.5)` / `Apply(source)` / one zone with those four, one `ZoneSpawned` |
| `Handler_RemoveDoesNothing` | a live zone / `Remove(source)` / `Count` still 1, no event — the contrast with M3-11a (rule 9) |
| `Consecrate_CastsBelowSixtyPercent` | Consecrate owned and ready, hp 83 then 84 of 140 / `Tick` / cast, then not (rule 11) |
| `Consecrate_HealsAQuarterBarIfYouStand` | hp 70 of 140, cast, stand still / 6 s of `Tick` / hp 106 — twelve pulses of 3 (rule 11) |
| `Consecrate_HealsNothingIfYouLeave` | the same, walk out immediately / 6 s / hp 70 (rules 1, 5) |
| `Definition_ToEffect_RoundTrip` | the four through `SerializedObject` / `ToEffect` / a `SpawnHealZone` with them (M3-02b rule 2) |
| `Definition_Invalid_NamesTheAsset` | radius 0 / `ToEffect` / `ArgumentException` starting with the asset name |
| `State_ExposesTheZones` | two live / `ActiveZoneCount`, `ZoneAt` / 2 and their positions; reflection says `Combat` is still not public |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend with a hand-authored tree holding Consecrate. Take it, leave it on Auto, and let a Husk take you below 60 %. The overlay shows a zone appear at your feet and your hit points climb while you stand still.
2. **[Editor]** Walk out of it immediately. Nothing heals, and the zone keeps burning down where you left it — rule 1, and the only thing that makes this skill a decision.
3. **[Editor]** Take a hit while the zone is healing you. The Aegis's recharge delay behaves exactly as it always has (rule 7).
4. **[Editor]** Cast it twice in a row by dropping below 60 % again after the cooldown. Two zones, two clocks, both pulsing (rule 9).
5. **[device]** Ledger row 4: a containment test per zone per tick is trivial arithmetic, but it joins the unmet `GC.Alloc` row with the rest.
6. **[device]** The feel question, deferred twice like Bulwark's: **is standing still for six seconds survivable at stage 15?** If it is not, the skill is a trap and the answer is a bigger radius rather than a bigger heal. There is no view until M3-11c, so it cannot even be tried yet.

## Out of scope

- **Any view** — M3-11c, which draws the decal and flashes the pulse.
- **Damaging, slowing or buffing zones** — rule 10 says the system is shaped for them and this task ships one kind. Each later one is a primitive pair and a field, by M3-05's rule.
- **Healing anything but the player** — rule 6. M5-04's Wights are the task that widens it.
- **A zone the player carries** — rule 1 ruled against it, and the rule is the design rather than a simplification.
- **Zones on the snapshot.** A zone is core's and is drawn from events, `TelegraphRings`' bargain — nothing physical asks a question about one, so it needs no presence in `WorldSnapshot`.
- **Saving a live zone.** Like a cooldown (M3-07b's Out of scope), a zone is seconds of a fight the player is not in the middle of any more.
- **Making the Aegis's numbers addressable** — M3-11a's Out of scope carries it; Unbroken is the node, and it is not in v1's twelve.

## As built

**Built as specced, five counted files, no split.** `ZoneSystem` (Core/Combat), `SpawnHealZone` and its
handler (Core/Effects), `SpawnHealZoneDefinition` (Game/Authoring, block namespace), and the two test
fixtures — plus the five small edits the table names. Eleven `.cs` files across **four** assemblies,
confirmed through `GetAssemblyNameFromScriptPath`: `Soulvail.Core` ×7, `Soulvail.Game` ×1,
`Soulvail.Tests.Core` ×2, `Soulvail.Tests.Game` ×1. No prefab, no scene.

**Four corrections to this spec, all ruled before a line was written.**

1. **`SpawnHealZoneHandler(ZoneSystem, IClock)` is wrong for the second time in two tasks.** `IClock`
   is one member, `DateTimeOffset UtcNow`, and AR §18.2 says there is no `IClock` in the session. The
   shipped signature is `(ZoneSystem zones, SimulatedClock clock)` — M3-11a-ii's class, one task old,
   already written beside `State.Time += Dt`. `ZoneSystem` remembering `_now` from its own `Tick` was
   considered and refused for the same reason it was there: this class ticks **after** the runner, so
   a zone cast this frame would be scheduled off last frame's clock. Pinned by
   `Handler_TakesNoWallClock`, which is a *test* rather than a probe because a `RunCommand` refuses
   the whole `System.Reflection` namespace before it executes.

2. **Rule 2's stated reason is false and is not in any doc comment.** The player's position is *not*
   "read, used and forgotten": `RunState.PlayerPosition` is public and `RunSession.Tick` assigns it
   before the skills step. Route (a) — `CombatBlackboard.PlayerPosition` plus one line in
   `UpdateBlackboard` — ships anyway, and the real argument is the one the class carries:
   **`Spawn` happens inside `Apply`, which is not inside any `Tick` of `ZoneSystem`'s**, so
   `ProjectileSystem`'s precedent of taking the position as a `Tick` parameter cannot serve the moment
   that actually needs one, and a field cached from this class's own tick is stale for the clock's
   reason. Route (b) therefore drops **no** small-edit file: something has to hold the position at cast
   time either way. Given that, both moments read the same table rather than a class answering "where
   is the player" two ways. `UpdateBlackboard` takes the position as a parameter rather than reaching
   for the snapshot, which keeps it a method that copies and decides nothing.

3. **`Consecrate_HealsAQuarterBarIfYouStand` is 106, and the order inside `Tick` is now a rule**:
   pulses first, then the retirement, so a zone is alive up to and including its last instant. Twelve
   pulses, not eleven. It has its own row, `Zone_PulsesOnTheTickItExpires`, because
   `Zone_ExpiresAfterItsDuration` ticks at 6.01 and steps straight over the coincidence — and the swap
   run proves it: retire-first reddens **four** rows (that one, `Zone_ExpiresAfterItsDuration`,
   `Zone_PulsesAreAbsolute`, `Consecrate_HealsAQuarterBarIfYouStand`) and nothing else.

4. **The three events carry an `Id`, not the spec's `Index`.** A zone retiring shifts the ones placed
   after it down, so an index is a position in a list at one instant rather than a handle: a view keyed
   by it would retire the wrong decal the first time two zones overlapped and the older one ended.
   `Spawn` returns that id. `PositionAt`/`RadiusAt`/`RunState.ZoneAt` keep the dense index, which is
   what an overlay walks.

**Two things the spec did not ask for.** `ZoneSystem.MaxPulses` (10 000) bounds the catch-up loop at
the door: `Tick` lands every pulse a zone was alive for, so a six-second zone with a one-microsecond
interval is a hang rather than a fast zone (AR §18.3). It is guarded at both doors, like `GrantShield`.
And `ZoneSystem.Tick` **refuses** a non-finite `now` — `ProjectileSystem`'s answer rather than
`SkillRunner`'s, because an unreadable clock here is either a loop with no exit or a zone that never
pulses.

**One row ships as its reachable half and it is named rather than implied.**
`Zone_BoundaryLeavesItRunning` drives every call `StageFlow.Advance` makes to the player —
`Targeter.Reset`, `ProjectileSystem.Clear`, and the wider `PlayerCombat.Reset` that method
deliberately refuses — and asserts the zone stands and pulses through all of them. It does not drive a
real boundary: clearing a stage needs an enemy killed, a kill needs a cone report from a Unity adapter
`Soulvail.Tests.Core` has no player for, and that fixture would be larger than the system under it.

**The rows land in three fixtures and none of the zone rows needs a session.** 22 in `ZoneSystemTests`
(bare `ZoneSystem`, real `Health`, real `CombatBlackboard`), 11 in `SpawnHealZoneTests` (5 handler rows
and **6 over a live `RunSession`**), 3 `Definition_*` in the **existing** `SkillAuthoringTests` beside
`GrantShield_*` — a new file there would have been a sixth counted file. **The live fixture carries
CC §7's 140 hit points, which is the deliberate opposite of M3-11a-ii's hundred-thousand sandbag**:
every row here reads HP as a fraction of 140, and the only door to a hurt player is the resumed
snapshot, because `RunState.Combat` is `internal` and the mode's roster is empty.

**`Zone_TicksAfterTheTimedEffects` is an EditMode row in `Tests.Core`, not a `FrameOrderTests` row.**
PlayMode stays at 16. It is proved M3-11a-ii's way — a first pass measures its own timings, the second
arranges the coincidence with half a frame of margin — and the swap run reddens it **alone** (10 passed,
1 failed).

**Two deviations outside the Files table, both one word.** `GrantShieldDefinition.cs` and
`GrantShieldTests.cs` said `Bulwark.asset` is **M3-12b's**; it is **M3-12c's**, whose own Tests table
carries `Bulwark_CarriesItsAuthoredNumbers` and `Consecrate_CarriesItsAuthoredNumbers` where M3-12b is
the two primitives. The same claim was corrected in M3-11a-ii's PROGRESS entry and its Current State
row. Fixed rather than left as a false pointer beside this task's true one.

**One Traps row written as part of this task**: §4 now documents the `RunCommand` wrapper shape —
`internal class CommandScript : IRunCommand`, `public void Execute(ExecutionResult result)`, output
through `result.Log`, helpers beside it and never nested — which cost four probes at M3-11a-ii.
