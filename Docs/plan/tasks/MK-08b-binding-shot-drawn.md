# MK-08b — Binding Shot in Unity

**Size:** M · **Depends on:** MK-08a, MK-06b, MK-02b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5; JG-12g (stones), AR §18.4 (`LineOfSightSense`'s `Cover` mask)

## Goal

Unity answers the anchor probe against stones, trees and walls, and the bind is drawn: a roped arrow,
then a glowing chain pulling the pair together for the stun.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Adapters/BindProbeAdapter.cs` | Game | **New.** Reads `BindProbe` intents, casts, reports |
| `Game/Views/BindingChainView.cs` | Game | **New.** On `BindingShotBound`: the chain for the stun |
| `Prefabs/VFX/BindingChain.prefab` | prefab | **New.** `LineRenderer` rope/chain, cyan glow, end clasps |
| `Data/Skills/Marksman/BindingShot{,2,3,4}.asset` | assets | **New.** + effect assets |
| `Tests/Game/Adapters/BindProbeAdapterTests.cs` | Tests.Game | **New** |
| *small edits* | | `RunTicker` (adapter in the facts phase, after `SyncTransforms` — AR §18.1), `ProjectileViews` (the bind arrow: cyan, trailing a short rope), `Data/Paths/Marksman.asset` (inserted **third**, unlock 14), `English`/`Pseudo`, `SkillIconBook`, `MarksmanPathTests` |

## Behaviour

1. **Probe:** a sphere cast (radius 0.2 m, at `EyeHeight` 1.1 m) from the probe's start along its
   direction for its reach, against the `Cover` mask, trees, and the arena's boundary walls; the first
   hit reports `hit` + point; none reports a miss. Every probe of a frame is answered that frame.
2. **Chain (Enemy/Obstacle):** from the target's chest to the anchor (the other enemy's chest, or
   the hit point), pulled taut over 0.15 s, glowing cyan, a clasp at each end; it follows moving
   bodies and fades on the stun's end event.
3. **Alone:** a short snap of rope around the target, gone with its stun.
4. The stun itself is drawn by MK-06b; this task adds only the chain.
5. **Cast animation:** the Ranger's normal draw and release, faster (one quick shot); the arrow is
   the bind arrow (cyan, rope trail).
6. **Assets:** `skill.marksman.binding-shot`, `-2`…`-4`, MK-08a rule 7 exactly.
7. Loc: *"Binding Shot — Chains an enemy to whatever is behind it: another enemy, a stone, a tree or
   a wall. Both are stunned."* and one line per rank.
8. Chains pooled; all end on despawn, death or stage boundary.

## Tests

| Test | Given / When / Then |
|---|---|
| `Probe_HitsAStone` / `Probe_HitsTheWall` / `Probe_MissesOpenGround` (1) |
| `Probe_AnsweredSameFrame` (1) |
| `Chain_SpansTargetToAnchor_AndFollows` / `Chain_EndsWithStun` (2) |
| `Alone_ShowsSnap` (3) |
| `Assets_MatchRankTable` / `Path_BindingIsThirdAtFourteen` (6) |
| `Loc_Keys` (7) / `Chains_Pooled` (8) |

## Manual verification (Editor / device)

1. **[Editor]** Stage 12 at level 14: shoot an enemy standing in front of a stone — chain to the
   stone, long stun; two enemies in a line — both chained; open ground — short stun.

## Out of scope

- Tree colliders where the Jungle has none yet (walls and stones suffice).

## As built

_Filled at merge._
