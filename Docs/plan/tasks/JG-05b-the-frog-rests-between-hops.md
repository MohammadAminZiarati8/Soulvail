# JG-05b — The Frog rests between hops

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.1 (the Husk's role)

## Goal

The Frog hops less often: a longer sit between hops, and each hop the same length as today.

## Why this shape

`ChaserBehaviour.TickChase` already supports hopping: a leap of `HopTime`, then `HopRestTime` sat still,
with the leap fast enough that the **average** stays `MoveSpeed`. If only the rest grows, the average
holds, so each leap gets faster and longer (2.6 m instead of 1.6 m) and the frog looks like it is
jumping harder rather than waiting longer. The owner asked for more delay between jumps, so the leap
keeps its distance, and the frog covers ground more slowly.

| | `_hopTime` | `_hopRestTime` | `_moveSpeed` | leap speed | hop length | hops / s |
|---|---|---|---|---|---|---|
| today | 0.5 | 0.3 | 2.0 | 3.2 m/s | 1.6 m | 1.25 |
| after | 0.5 | **0.8** | **1.25** | 3.25 m/s | 1.6 m | 0.77 |

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Frog.asset` | — | `_hopRestTime` 0.8, `_moveSpeed` 1.25 |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | `JunglesHusk_PlaysTheHusksRole`: the Frog's `MoveSpeed` parts from the Husk's, as its XP did in JG-01; a new row pins the hop numbers |

Only these files change. No code changes.

## Behaviour

1. The Frog sits **0.8 s** between hops, and each hop is still 0.5 s and ~1.6 m.
2. Its `MoveSpeed` is 1.25. Every other number still matches the Husk, apart from XP.

## Tests

| Test | Rule |
|---|---|
| `Frog_RestsBetweenHops_LeapUnchanged`: `HopTime` 0.5, `HopRestTime` 0.8, and `MoveSpeed × (HopTime + HopRestTime) / HopTime` within 0.1 of 3.2 | 1 |
| `JunglesHusk_PlaysTheHusksRole` (edited): `MoveSpeed` equal to the Husk's for the Rootling only, and 1.25 for the Frog | 2 |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage 1: the three Frogs hop, visibly sit, then hop again. The hops are the same
   size as before, and the gap between them is about twice as long.

## Out of scope

- The Croaker's hop (JG-05c).
- Retuning stage 1's pacing. Slower Frogs reach the player later, and the owner judges that in the playtest.

## As built

_Filled at merge._
