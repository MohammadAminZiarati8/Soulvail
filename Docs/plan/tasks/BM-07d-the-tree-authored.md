# BM-07d — The Beastmaster's tree authored, Pounce drawn

**Size:** M · **Depends on:** BM-07b, BM-07c · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.4; CH §5, §5.1 (free-pick, tier rule); MK-07b (an active authored)

## Goal

Levels 2–10 on the Beastmaster: nine nodes in three branches on the free-pick tree screen, Pounce in
the arc, the roar and the pounce drawn.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Trees/Beastmaster.asset` | — | **New.** `tree.beastmaster`: Axe, Beast, Wild, three tiers each |
| `Data/Skills/Beastmaster/*.asset` | — | **New.** The nine nodes (below) |
| `Data/Effects/Beastmaster/*.asset` | — | **New.** Their `ModifyStat` / `Pounce` effects |
| `Game/Authoring/PounceDefinition.cs` | Game | **New.** `Pounce`'s five numbers |
| `Game/Views/PetView.cs` | Game | `Pounce` → `Land` along the leap on `PetPounced`; `Howl`-pose roar on `PetRoared` |
| `Game/Views/RoarRingView.cs` | Game | **New.** A cyan 3 m ring that expands and fades in 0.4 s |
| `Tests/Game/Authoring/BeastmasterTreeTests.cs` | Tests.Game | **New** |
| *small edits* | | `Beastmaster.asset` points at the tree; `English`/`Pseudo` names and lines; `SkillIconBook` nine icons; `RunScope` registers the ring; `Prefabs/Vfx/RoarRing.prefab` |

## Behaviour

1. **Axe:** T1 Heavy Edge — `WeaponDamage` +12 %. T2 Third Axe — `AxeCarry` +1. T3 Cleaving Return —
   `AxeReturnDamage` +0.5 and `AxeReturnSlow` +0.25.
2. **Beast:** T1 Sharp Claws — Pet `ContactDamage` +15 %. T2 Feral Bite — `PetBiteStunEvery` +3.
   T3 Pounce — the active: 3–6 m, 2× bite, 0.8 s, 0.35 s; cooldown 8 s; trigger `MarkedInPounceReach ≥ 1`.
3. **Wild:** T1 Thick Hide — Player `MaxHp` +10 % and Pet `MaxHp` +10 %. T2 Lick Wounds —
   `PetHeelRegen` +0.06 and `PetReturnAt` −0.2. T3 Quick Swap — `MovementSkillCooldown` −1 and
   `SwapRoarSlow` +0.3.
4. Tier *N* needs *N−1* in its branch; nine points fill it (CH §5). The tree screen and the arc
   need no code: Pounce is a circle like Volley, with auto-cast on by default.
5. **Drawn:** a pounce arcs the hound to the target and lands with `Land`; a roar plays a short
   `Howl` and the cyan ring.
6. Every node has a name and a one-line effect in English and Pseudo, from `LocKey`s.

## Tests

| Test | Given / When / Then |
|---|---|
| `Tree_ThreeBranches_ThreeTiers_NineNodes` (4) |
| `AxeNodes_Numbers` / `BeastNodes_Numbers` / `WildNodes_Numbers` (1–3) |
| `ThickHide_HitsPlayerAndPet` (3) |
| `Pounce_Trigger_And_Cooldown` (2) |
| `EveryNode_HasLocKeys_InBothTables` (6) |
| `PounceAndRoar_ViewsReact` (5) |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 at stage 8 with banked points: every node learnable in tier order; Pounce
   fires on a marked frog 4 m from the hound; Quick Swap's roar slows the frogs at the landing.

## Out of scope

- The unlock (BM-08); the paths (PL).

## As built

_Filled at merge._
