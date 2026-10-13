# UI-02c — The Roll and skill buttons in the new art

**Size:** M · **Depends on:** UI-02a · **Branch:** `ui-02c-the-roll-and-skill-buttons`
**Design refs:** CC §5, §6.2; GD §16.4; CLAUDE.md · **Ledger rows:** none

## Goal

The Roll button (`SkillButton`) and manual skill buttons (`ManualSkillButton`) on `Hud.prefab` wear the
new art: Roll displays `RollButton_Ready`, `RollButton_Cooling`, `RollButton_Pressed`, and `Icon_Roll`,
while manual skill buttons wear `SkillButton_Ready` and `SkillButton_Cooling`.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Prefabs/UI/Hud.prefab` | — | Roll and manual skill button instances dressed in new art |
| `Game/Controls/SkillButton.cs` | Game | Swaps Roll button states (Ready, Cooling, Pressed) and binds `Icon_Roll` |
| `Game/Controls/ManualSkillButton.cs` | Game | Swaps manual skill button states (Ready, Cooling) |
| `Tests/Game/Controls/SkillButtonTests.cs` | Tests.Game | Verifies Roll button states and dressing on `Hud.prefab` |
| `Tests/Game/Presentation/SkillBarPresenterTests.cs` | Tests.Game | Verifies manual skill button dressing and state changes |
| *small edits* | | `Docs/plan/ROADMAP.md`, `Docs/plan/PROGRESS.md` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// SkillButton gains serialized sprite doors for states:
[SerializeField] private Image _buttonFrame;
[SerializeField] private Sprite _readySprite;
[SerializeField] private Sprite _coolingSprite;
[SerializeField] private Sprite _pressedSprite;
```

## Behaviour

1. **Roll button wears `RollButton_Ready` when ready.** With cooldown at 0, the button displays
   `RollButton_Ready.png` with `Icon_Roll` centered.
2. **Roll button wears `RollButton_Cooling` while cooling.** While `RunState.MovementSkillCooldownFraction` > 0,
   the button displays `RollButton_Cooling.png`.
3. **Roll button wears `RollButton_Pressed` on pointer down.** When pressed, the frame displays
   `RollButton_Pressed.png`.
4. **Input buffer preserved.** CC §5's 0.15 s input buffer remains active — the button remains interactable
   while cooling.
5. **Manual skill buttons wear `SkillButton_Ready` and `SkillButton_Cooling`.** When slot is ready,
   the button displays `SkillButton_Ready.png`; while cooling, it displays `SkillButton_Cooling.png`
   at reduced opacity per CC §6.2.

## Tests

| Test | Given / When / Then |
|---|---|
| `SkillButton_WearsReadySpriteWhenAvailable` | movement cooldown == 0 / frame inspected / sprite is `RollButton_Ready` — rule 1 |
| `SkillButton_WearsCoolingSpriteWhileCooling` | movement cooldown > 0 / frame inspected / sprite is `RollButton_Cooling` — rule 2 |
| `SkillButton_WearsPressedSpriteOnPointerDown` | pointer down / frame inspected / sprite is `RollButton_Pressed` — rule 3 |
| `SkillButton_HasRollIcon` | icon Image / inspected / sprite is `Icon_Roll` — rule 1 |
| `ManualSkillButton_WearsSkillButtonSprites` | slot ready vs cooling / inspected / `SkillButton_Ready` vs `SkillButton_Cooling` — rule 5 |

## Manual verification (Editor / device)

1. **[Editor]** Play `Run.unity`. Tap Roll. Confirm button transitions from Ready to Pressed, to Cooling,
   and back to Ready when cooldown expires.
2. **[device]** Confirm thumb ergonomics and responsiveness of the bottom-right control cluster.

## Out of scope

- Virtual stick (UI-02b).
- Radial numeric countdown labels.

## As built

`SkillButton` and `ManualSkillButton` on `Prefabs/UI/Hud.prefab` now wear the new Clash of Clans-style art:
- `SkillButton` (Roll) root Image wears `RollButton_Ready` (`Color.white`, `preserveAspect = true`), with child `Icon` wearing `Icon_Roll`. Serialized fields `_buttonFrame`, `_icon`, `_readySprite` (`RollButton_Ready`), `_coolingSprite` (`RollButton_Cooling`), and `_pressedSprite` (`RollButton_Pressed`) wired. `preserveAspect` is set on the prefab, not in code. Implemented `IPointerDownHandler` and `IPointerUpHandler` (both reach this component and the `OnScreenButton` beside it) to show `RollButton_Pressed` during touch, swapping to `RollButton_Cooling` while cooling fraction > 0, and `RollButton_Ready` when available. CC §5's input buffer remains intact (button stays interactable while cooling).
- `ManualSkillButton` instances (`S1`..`S4`) root Images wear `SkillButton_Ready` (`Color.white`, `preserveAspect = true`). Serialized fields `_buttonFrame`, `_readySprite` (`SkillButton_Ready`), and `_coolingSprite` (`SkillButton_Cooling`) wired. `Draw()` swaps the frame to `_coolingSprite` while cooling and back to `_readySprite` when ready; the 40% dimming and dead tap while cooling (CC §6.2) were already there and are unchanged.
- Existing radial fills, labels, canvas groups, and dp sizing are preserved intact.

`SkillButtonTests.cs` adds 4 tests (`SkillButton_WearsReadySpriteWhenAvailable`, `SkillButton_WearsCoolingSpriteWhileCooling`, `SkillButton_WearsPressedSpriteOnPointerDown`, `SkillButton_HasRollIcon`). `SkillBarPresenterTests.cs` adds `ManualSkillButton_WearsSkillButtonSprites` and extends `Prefab_IsDressed` to assert all serialized sprite doors.

**Verified:** **3 759 EditMode — 3 758 passed / 0 failed / 1 inconclusive (+5)** through `TestRunnerApi`; `SkillButtonTests` 6 rows, `SkillBarPresenterTests` 30 rows, `FloatingStickTests` 8 rows, `LowerControlsArtTests` 50 rows green. Zero compiler errors, zero analyzer warnings, `dotnet format whitespace` clean over touched files, `TimeManager.asset` reverted. **UI-02 is complete.**

**Deviations:** none.
