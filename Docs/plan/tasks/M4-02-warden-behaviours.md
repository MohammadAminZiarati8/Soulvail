# M4-02 — The Warden of Ash: a shockwave you walk out of, a fissure you stand off, and Husks

**Size:** M · **Depends on:** M4-01b, M2-06, M2-12b · **Branch:** `m4-02-warden-behaviours`
**Design refs:** GD §9.1 (rules 1, 2, 4, 7), §9.2; AR §18.4 · **Ledger rows:** none — the device half is [M4 row 3](../ROADMAP.md#carry-forward-into-m4)

## Goal

The first boss in the game does three things a player can learn: a **shield-slam shockwave** that expands from
where it stood, **ground fissures** that open under the player's feet and are a place not to be, and it
**summons Husks**. GD §9.2's hook is *"teaches: attack from behind, keep moving"*, and every attack has a safe
answer that costs position rather than health.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/WardenBehaviour.cs` | Core | The attack selection and the three attacks' timing |
| `Core/Combat/Shockwave.cs` | Core | An expanding ring that damages once per body as it passes |
| `Core/Combat/Fissure.cs` | Core | A placed area that arms, fires, and closes |
| `Tests/Core/Ai/WardenBehaviourTests.cs` | Tests.Core | Selection, telegraph lengths, the safe answers |
| `Tests/Core/Combat/ShockwaveAndFissureTests.cs` | Tests.Core | Geometry, once-per-body, and the windows |
| *small edits* | Core | `RunSession.Tick` gains the two steps, beside the zone step M3-11b added; `EnemySpec` gains nothing |
| *data* | — | `Warden.asset` (an `EnemySpec`), `WardenBoss.asset` (a `BossSpec`), and the mode's boss roster entry |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Combat/Shockwave.cs — the same shape as ZoneSystem (M3-11b), on purpose
public sealed class ShockwaveSystem
{
    public const int Capacity = 4;
    public void Emit(Vector3 origin, float speed, float maxRadius, float damage, float thickness);
    public void Tick(float dt, ...);
    public int ActiveCount { get; }
}

// Core/Combat/Fissure.cs
public sealed class FissureSystem
{
    public const int Capacity = 8;
    /// <param name="armSeconds">Telegraph before it bites — GD §9.1 rule 1's ≥ 0.6 s.</param>
    public void Open(Vector3 at, float radius, float armSeconds, float openSeconds, float damage);
    public void Tick(float dt, ...);
    public int ActiveCount { get; }
}

// Core/Events/BossEvents.cs — additions
public readonly struct ShockwaveEmitted { public ShockwaveEmitted(int id, Vector3 origin, float speed, float maxRadius); }
public readonly struct ShockwavePassed  { public ShockwavePassed(int id); }
public readonly struct FissureArmed     { public FissureArmed(int id, Vector3 at, float radius, float armSeconds); }
public readonly struct FissureFired     { public FissureFired(int id); }
public readonly struct FissureClosed    { public FissureClosed(int id); }
```

## Behaviour

1. **Every attack telegraphs for at least 0.6 s, and the number is authored rather than constant** (GD §9.1
   rule 1). A test asserts the *shipped* values clear the floor, so retuning an attack faster than a player can
   react is a red row rather than a playtest discovery.
2. **The shockwave expands from where the Warden stood when it slammed, not from where it is now.** A ring
   that follows the boss is unescapable by walking; one anchored to a place is exactly GD §9.1 rule 2's *"safe
   answer that costs positioning"*. **It damages a body at most once** — the ring passing over someone standing
   still is one hit, not one per frame — which is the same once-per-body discipline `ConeOverlapQuery` has.
3. **A fissure opens under the player's position at the moment it is placed, and does not track.** The safe
   answer is to move; a tracking fissure has none. It **arms visibly**, fires, then closes.
4. **Containment is flat, XZ only** ([AR §18.4](../../Architecture.md#184-combat-and-perception)) — the same rule
   `ZoneSystem` follows, because height is a camera's business and an arena floor is flat.
5. **Both systems tick where the zones do, on the snapshot's clamped `Dt`**, so a 30 fps phone and a 120 fps
   one see the same ring at the same radius. Absolute times, not accumulated deltas — M3-11b's ruling, which is
   why its twelve pulses land identically at both rates.
6. **Summoning is M4-01b's `AddWave` and is not re-implemented here.** The Warden's phases author *"3 Husks"*
   and *"4 Husks"*; this task authors the numbers, not the mechanism (GD §9.1 rule 4).
7. **Attack selection is by cooldown and range, and it is deterministic given a seed.** No `UnityEngine.Random`
   ever, and the stream is the run's — so a boss fight replays identically from a saved run, which is the
   guarantee M3-08a's lazy draw established for offers and which a boss must not break.
8. **A capacity overflow drops rather than throws.** M3-11b's ninth zone *throws*, deliberately, because it was
   unreachable at Consecrate's cooldown — **a boss is different**: an add-heavy phase plus a player-placed zone
   is exactly where a ceiling gets hit, and a run-ending refusal during the first boss fight is the worst
   outcome available. Named here rather than inherited, because the two tasks reach opposite answers on purpose.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Warden_EveryAttackTelegraphsLongEnough` | the shipped `Warden.asset` / every attack's arm time / all ≥ 0.6 s (rule 1) |
| `Shock_ExpandsFromWhereItSlammed` | boss slams then walks 5 m / ticked / the ring's centre is the slam point |
| `Shock_HitsABodyOnce` | a body standing in the path / the ring passing fully over it / exactly one damage event |
| `Shock_MissesSomeoneWhoWalkedOut` | a body outside `maxRadius` when the ring arrives / ticked / no damage — rule 2's safe answer, asserted |
| `Shock_IsFlat` | a body 3 m above the ring's plane / ticked / hit, because containment is XZ (rule 4) |
| `Fissure_OpensWhereThePlayerWas` | player at A, fissure placed, player walks to B / it fires / the damage is at A |
| `Fissure_ArmsBeforeItBites` | arm 0.8 s / a body standing in it / no damage for 0.8 s, then damage |
| `Fissure_MissesSomeoneWhoMoved` | a body leaving during the arm / it fires / no damage (rule 3) |
| `Systems_TickIdenticallyAtBothRates` | the same fight at 30 and 120 fps / the same elapsed time / identical hits, identical radii (rule 5) |
| `Selection_IsDeterministic` | the same seed and the same state / two runs / the identical attack order |
| `Selection_UsesNoEngineRandom` | the assembly / reflected / `Soulvail.Core` names no `UnityEngine.Random` — the standing purity row, extended |
| `Capacity_DropsRatherThanThrows` | `Capacity` shockwaves live / a fifth / dropped, no throw, and the run continues (rule 8) |
| `Warden_SummonsWhatItsPhasesAuthor` | phase 2 / entered / the Husks its `BossSpec` names, through M4-01b's path (rule 6) |
| `Tick_AllocatesNothing` | 10 000 ticks with rings and fissures live / `AllocationAssert.None` / zero |

**Guard rows are implied, not listed:** non-finite radii, speeds and durations; a negative count; a fissure
opened at a non-finite position.

## Manual verification (Editor / device)

1. **[Editor]** Reach stage 5 and let the Warden slam. *Expected: a ring leaves the point it slammed and passes
   through you if you stand still; walking out of it costs you nothing.*
2. **[Editor]** Let a fissure arm under you and step off it. *Expected: it fires where you were and misses.*
3. **[Editor]** Fight it to phase 2. *Expected: Husks appear; killing them still pays XP, because rule 6 makes
   them ordinary agents.*
4. **[device]** Whether a ring and a fissure **read on a 6-inch screen** — GD §9.1 rule 7, which says to test
   every telegraph at phone size *before calling it done*, and which no Editor can answer.
   [M4 ledger row 3](../ROADMAP.md#carry-forward-into-m4).
5. **[device]** Whether 75–120 s of one enemy holds attention at all, or reads as a health sponge.

## Out of scope

- **The boss's body, animations and the hazard's art** — M4-03.
- **The segmented bar** — M4-04.
- **Bosses 2, 3 and 4** (Choirmother, Gravemaw, Archon) — M7. This task authors one roster entry.
- **The boss buffing itself.** M4-01a's seam exists; the Warden does not use it, and authoring a buff it does
  not need to prove a seam is the wrong reason to author content.
- **Audio.** GD §9.1 rule 1 wants *"visual **and** audio cues"* and there is no audio system in this project
  at all — M7's pass owns it, and **this task ships half of rule 1 and says so** rather than pretending.

## As built

_Filled at merge._
