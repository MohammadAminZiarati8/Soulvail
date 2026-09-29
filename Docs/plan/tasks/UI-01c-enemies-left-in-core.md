# UI-01c — Enemies left, in core

**Size:** S · **Depends on:** — · **Branch:** `ui-01c-enemies-left-in-core`
**Design refs:** GD §7.1, §16.1 (*"Enemies remaining — only when ≤5 remain"*); AR §18.1, §18.2; M2-05, M6-02a · **Ledger rows:** none

The owner's brief of 2026-09-29, item 7: *"Enemies-left chip: under the pause button, shown only when 5
or fewer enemies remain"*, and the rule that goes with it: *"If the stage/wave chip or the
enemies-left chip needs a number core does not publish yet, add the smallest core event or read that
carries it, with tests. If that grows past the task's size, split it into its own task."* It needs a
count that core does not have, so this task adds it; UI-01d draws it.

## Goal

`RunState.EnemiesLeft` says how many enemies the stage still holds: the living, and the bodies the
director has yet to stand up. It counts down to 0 at the clear.

## Why this shape

- **Living plus unspawned, not living alone.** Waves overlap (GD §7.3), so the living count dips
  under five between waves and at every wave's start. A chip that showed 3 and then 9 would be wrong
  twice. The stage's own remainder is a count that only falls, and it falls to 0 at the clear.
- **Corpses are not left.** `RunState.EnemyCount` counts the registry, corpses included, for
  `EnemySystem.CorpseTime`. `EnemySystem.LivingCount` is the honest half.
- **The director keeps its arrays; it answers one number.** Queued, telegraphed and future-wave
  bodies are `SpawnDirector`'s private bookkeeping, and `WavePlan.BodyCount` is the future. One
  read, `Unspawned`, sums them, and nothing outside learns the arrays' shape.
- **Copied by `RunSession.Tick`, as `StageIndex` and `IsSanctumOpen` are.** `RunState` does not hold
  the director. So the number is written once a tick, after the flow has moved, and read by a HUD
  that polls it, as `SkillCooldownFraction` is. An event per kill would be a second publisher of a
  number the tick already knows.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| *small edits* | Core | `SpawnDirector.Unspawned` (rules 1–3); `RunState.EnemiesLeft` and its write in `RunSession.Tick` (rule 4) |
| *small edits* | Tests.Core | `SpawnDirectorTests`: rows for rules 1–3; `RunSessionTests`: rows for rule 4 |

Size S: no new file, two members and their rows.

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// SpawnDirector gains:
/// <summary>Bodies this stage will still stand up: queued, telegraphed, and every wave not yet started.
/// A boss stage's boss until it stands. Zero before Begin and after Clear.</summary>
public int Unspawned { get; }

// RunState gains:
/// <summary>The living enemies and the bodies still to come this stage. Written once a tick.</summary>
public int EnemiesLeft { get; internal set; }
```

## Behaviour

1. **An ordinary stage's unspawned bodies are the started waves' outstanding and the rest of the
   plan.** `Unspawned` is, for each started wave, its bodies not yet standing, queued or telegraphed,
   plus `WavePlan.BodyCount` of every wave not yet started. A body leaves it at the moment it stands.
2. **A boss stage's unspawned body is the boss, until it stands.** 1 from `Begin` until the boss's
   agent exists, then 0. The adds it summons are living when they stand, and never unspawned.
3. **Zero before `Begin`, after `Clear`, and on a director with no plan.** So the arrival beat and the
   Sanctum read 0.
4. **`RunState.EnemiesLeft` is `EnemySystem.LivingCount()` plus `Unspawned`, written once a tick after
   the flow has moved**, and in a mode with nothing to compose as well. A corpse is not counted. A
   stage boundary reads 0 on the tick the arena is swapped, and a resumed run reads its opening
   state on its first tick.
5. **Nothing here allocates**: two sums over at most `WavePlan.WaveCount` and the registry.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Unspawned_IsTheWholePlanAtBegin` | a three-wave plan of 4, 5, 6 / `Begin` / 15 — rule 1 |
| `Unspawned_FallsAsBodiesStand` | the same / ticks until two bodies stand / 13 — rule 1 |
| `Unspawned_ATelegraphedBodyIsStillToCome` | a body telegraphed, not standing / read / counted — rule 1 |
| `Unspawned_ANewWaveMovesNothing` | wave 2 starts / read before and after / equal — rule 1 |
| `Unspawned_IsZeroOnceEveryBodyStands` | every wave spent / read / 0 — rule 1 |
| `Unspawned_ABossIsOneUntilItStands` | a boss stage / `Begin`, then the boss stands / 1, then 0 — rule 2 |
| `Unspawned_IsZeroBeforeBeginAndAfterClear` | a new director; then `Clear` / read / 0, 0 — rule 3 |
| `EnemiesLeft_IsTheLivingAndTheUnspawned` | a run in its first wave / ticks / `LivingCount + Unspawned` — rule 4 |
| `EnemiesLeft_DoesNotCountACorpse` | one enemy killed / the next tick, inside `CorpseTime` / one fewer — rule 4 |
| `EnemiesLeft_IsZeroThroughTheArrival` | a run's first tick / read / 0 — rule 4 |
| `EnemiesLeft_ReachesZeroAtTheClear` | a stage killed to the last body / the tick it clears / 0 — rule 4 |
| `EnemiesLeft_AllocatesNothing` | a run in a wave / `AllocationAssert.None` over 100 ticks / 0 bytes — rule 5 |

## Manual verification (Editor / device)

_None: core only. UI-01d's chip is where it is seen._

## Out of scope

- **The chip.** UI-01d draws it.
- **GD §7.3's survivor waypoint** (*"fewer than 3 … and 8 seconds pass"*): `ThreatArrows` keeps its own
  census, and nothing here changes it.
- **An event for the count.** A HUD polls it; nothing else wants it.

## As built

_Filled at merge, 6 000 bytes or fewer, measured._
