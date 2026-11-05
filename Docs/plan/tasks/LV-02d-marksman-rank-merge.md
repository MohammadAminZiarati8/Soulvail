# LV-02d — Marksman ranks merge to fit 29 points

**Size:** S · **Depends on:** LV-02a · **Branch:** `dev`
**Design refs:** CH §3.5, owner 2026-11-05 (Steady Aim 4 ranks, Binding Shot 3)

## Goal

Ranger 9 + Marksman 20 = 29 nodes, one per level to 30; a save that holds a removed rank comes back with that point banked.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Paths/Marksman.asset` | — | Steady Aim 4 ranks, Binding Shot 3 |
| `Data/Skills/Marksman/SteadyAim2*.asset` | — | rank 2 = +9 %/stack (old 2 + 3 merged) |
| `Data/Skills/Marksman/BindingShot2*.asset` | — | rank 2 = stun +1.0 s (old 2 + 3 merged) |
| `Core/Progression/SkillTree.cs` | Core | `Restore` refunds unknown path-rank ids |
| `Core/Run/RunSession.cs` | Core | refund banked as pending |
| `Tests/Core/Progression/PathInstallTests.cs` | Tests.Core | refund rows |
| `Tests/Game/Authoring/MarksmanPathTests.cs` | Tests.Game | asset rows |
| *small edits* | | renumbered rank assets' parents; English/Pseudo descriptions |

## Behaviour

1. Steady Aim has 4 ranks, rank 2 = +9 %/stack; Binding Shot 3 ranks, rank 2 = stun +1.0 s; later ranks keep their effects, re-parented.
2. Ranger tree + Marksman path = 29 nodes.
3. A saved id naming a rank beyond a skill's rank count is dropped and its point added to pending; ids that resolve nowhere else are still refused.

## Tests

| Test | Given / When / Then |
|---|---|
| `Marksman_TwentyNodes` | asset / count / 20; with Ranger 29 |
| `SteadyAim_RankTwo_IsNinePercent` | asset / read / 0.09 |
| `BindingShot_RankTwo_IsOneSecond` | asset / read / +1.0 s |
| `Restore_RemovedRank_RefundsAPoint` | save with Steady Aim 5 / resume / 4 ranks, pending +1 |
| `Restore_UnknownId_StillRefused` | foreign garbage id / resume / throws |

## Out of scope

- Other path skills; Skirmisher / Echo Archer.
