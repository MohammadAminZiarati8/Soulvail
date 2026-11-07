# BM-07d — The Beastmaster's tree authored

**Size:** M · **Depends on:** BM-07b, BM-07c · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.4; CH §5, §5.1 (free-pick, tier rule); MK-07b (an active authored)

## Goal

Levels 2–10 on the Beastmaster: nine nodes in three branches on the free-pick tree screen, Pounce in
the arc. The roar is drawn in BM-07e. The pounce already draws: BM-07c's `PetLeap` intent drives
BM-06b's arc and `Pounce` → `Land`.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Trees/Beastmaster.asset` | — | **New.** `tree.beastmaster`, `_character` = Beastmaster: Axe, Beast, Wild, three tiers each |
| `Data/Skills/Beastmaster/*.asset` | — | **New.** The nine nodes (below). Icons: `Icon_Swap` on Quick Swap, `Icon_Placeholder` on the rest (UI is placeholder; no new art) |
| `Data/Effects/Beastmaster/*.asset` | — | **New.** Their `ModifyStat` / `Pounce` effects |
| `Game/Authoring/PounceDefinition.cs` | Game | **New.** `Pounce`'s five numbers, validated as `Pounce` does |
| `Prefabs/Composition/BootScope.prefab` | — | The nine nodes appended to `_skills`, the tree to `_trees` |
| `Data/Localisation/English.asset`, `Pseudo.asset` | — | Tree, branch and node names; one-line effects |
| `Tests/Game/Authoring/BeastmasterTreeTests.cs` | Tests.Game | **New** |

## Behaviour

1. **Axe:** T1 Heavy Edge — `WeaponDamage` +12 %. T2 Third Axe — `AxeCarry` +1. T3 Cleaving Return —
   `AxeReturnDamage` +0.5 and `AxeReturnSlow` +0.25.
2. **Beast:** T1 Sharp Claws — `StatTarget.Pet` `ContactDamage` +15 %. T2 Feral Bite — Pet
   `PetBiteStunEvery` +3. T3 Pounce — the active: 3–6 m, 2× bite, 0.8 s, 0.35 s; cooldown 8 s;
   trigger `MarkedInPounceReach ≥ 1`.
3. **Wild:** T1 Thick Hide — Player `MaxHp` +10 % and Pet `MaxHp` +10 %. T2 Lick Wounds — Pet
   `PetHeelRegen` +0.06 and `PetReturnAt` −0.2. T3 Quick Swap — `MovementSkillCooldown` −1 and Pet
   `SwapRoarSlow` +0.3. (Each stat goes to whichever target answers it: `PetStats.Has`.)
4. Tier *N* needs *N−1* in its branch; nine points fill it (CH §5). The tree screen and the arc
   need no code: Pounce is a circle like Volley, with auto-cast on by default.
5. Every node, branch and the tree has a name, and every node a one-line effect, in English and
   Pseudo, from `LocKey`s.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tree_ThreeBranches_ThreeTiers_NineNodes` (4) | Load the tree → its character is the Beastmaster; 3 × 3 |
| `AxeNodes_Numbers` / `BeastNodes_Numbers` / `WildNodes_Numbers` (1–3) | Each effect's stat, target, op, value |
| `ThickHide_HitsPlayerAndPet` (3) | Two `ModifyStat`s, `Player` and `Pet` |
| `Pounce_Trigger_And_Cooldown` (2) | `PounceDefinition.ToEffect` numbers; 8 s; `MarkedInPounceReach ≥ 1`; auto-cast on |
| `BootScope_HoldsTreeAndNodes` (4) | The prefab's arrays contain the tree and all nine |
| `EveryNode_HasLocKeys_InBothTables` (5) | No missing key in English or Pseudo |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 at stage 8 with banked points: every node learnable in tier order; Pounce
   fires on a marked frog 4 m from the hound, and the hound arcs and lands on it; Quick Swap's roar
   slows the frogs at the landing (drawn in BM-07e).

## Out of scope

- The roar's `Howl` and ring (BM-07e). The unlock (BM-08); the paths (PL). Node icon art.

## As built

_Filled at merge._
