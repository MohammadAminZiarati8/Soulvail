# BM-06c — Wild Swap's streak and button icon

**Size:** M · **Depends on:** BM-06b · **Branch:** `dev` (direct)
**Design refs:** CH §3.6.3; EA ruling (a teleport on a phone loses the hero)

## Goal

A cyan streak joins the two swap points so the eye follows the hero, and the movement button
shows a swap icon on the Beastmaster.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Views/SwapStreakView.cs` | Game | **New.** On `Swapped`, a cyan line `PlayerFrom` → `PlayerTo`, holds then fades over 0.3 s total; pooled, one live |
| `Prefabs/Vfx/SwapStreak.prefab` | — | **New.** `LineRenderer`, unlit, `Palette.Player` |
| `Art/UI/Icons/Icon_Swap.png` | — | **New.** Two curved arrows chasing each other; white glyph, import settings copied from `Icon_Roll.png` |
| `Game/Controls/SkillButton.cs` | Game | `[SerializeField] private Sprite _swapIcon`; wears it when `Character.MovementSkill.Kind == Swap`, else the roll icon |
| `Game/Composition/RunScope.cs` | Game | `[SerializeField]` streak prefab; registers `SwapStreakView` |
| `Scenes/Run.unity` | — | Assigns the streak prefab on `RunScope` and `Icon_Swap` on the skill button |
| `Tests/Game/Views/SwapStreakViewTests.cs` | Tests.Game | **New** |

## Behaviour

1. On `Swapped`, a streak joins `PlayerFrom` and `PlayerTo` (0.3 m above ground) at full alpha.
2. It fades to zero by 0.3 s after `Swapped`, then returns to the pool; a second swap reuses it.
3. Colour `Palette.Player` only.
4. The movement button wears `Icon_Swap` on a `Swap` character and the roll icon otherwise; the
   6 s cooldown sweep is unchanged.
5. A missing streak prefab on `RunScope` fails loudly at scope build, naming the field.

## Tests

| Test | Given / When / Then |
|---|---|
| `Swapped_StreakJoinsBothPoints` (1) | Line ends == both points, raised 0.3 m |
| `Streak_FadesInPointThree_ThenPooled` (2) | Alpha 1 at 0, 0 at 0.3 s, inactive after; reuse on the next swap |
| `Streak_IsPlayerCyan` (3) | Start/end colour RGB == `Palette.Player` |
| `SkillButton_SwapCharacter_WearsSwapIcon` (4) | Swap kind → `_swapIcon`; Roll kind → roll icon |
| `RunScope_MissingStreakPrefab_Throws` (5) | Null field → error names it |

## Manual verification (Editor / device)

1. **[Editor]** Swap: the cyan streak links both landing points and is gone within a blink.
2. **[device]** The swap icon reads as "swap" at thumb size; the Ranger still shows the roll.

## Out of scope

- Quick Swap (BM-07b). Any streak sound.

## As built

_Filled at merge._
