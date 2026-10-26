# JG-05d — A smaller Puffball blast

**Size:** S · **Depends on:** — · **Branch:** `dev` (direct)
**Design refs:** GD §8.1 (the Bloater's role)

## Goal

The Puffball, the Jungle's bomber, bursts in a **2.5 m** radius instead of 3 m.

## Why this shape

- **The Puffball only, not the Bloater.** The owner is playing the Jungle. Descent's Bloater keeps its 3 m
  until someone asks for a change. That parts the two on one row of `JungleTests`, as the Frog's XP did in JG-01.
- **Nothing else needs to change.** The red-orange blast ring is drawn from the event's own radius
  (`TelegraphRings`), and `VFX_SporeBurst`'s puffs are ~2.3 m out when they fade (JG-03b's numbers),
  so they already sit inside 2.5 m.
- The fuse is unchanged: `_reach` stays 2 m, so a Puffball still starts its wind-up from the same distance.
  Stepping out of the smaller ring is now easier, which is the point.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/Puffball.asset` | — | `_explosionRadius` 2.5 |
| `Tests/Game/Authoring/JungleTests.cs` | Tests.Game | The Puffball's `Explosion.Radius` row: 2.5, no longer the Bloater's |

Only these files change. No code changes.

## Behaviour

1. The Puffball's blast reaches 2.5 m. Its ring is drawn at 2.5 m.
2. Every other Puffball number still matches the Bloater's, and the Bloater's blast is still 3 m.

## Tests

| Test | Rule |
|---|---|
| The Puffball row in `JungleTests` (edited): `Explosion.Radius` is 2.5 | 1 |
| The same test: every other number equal to the Bloater's, and the Bloater's radius is 3 | 2 |

## Manual verification (Editor / device)

1. **[Editor]** Jungle stage 4: the red-orange ring under a swelling Puffball is visibly smaller, the
   spore cloud stays inside it, and two steps back from a fusing Puffball clears the burst.

## Out of scope

- The Bloater (Descent), and the late-wave Puffball flood (PROGRESS, *What does not work yet* 4).

## As built

_Filled at merge._
