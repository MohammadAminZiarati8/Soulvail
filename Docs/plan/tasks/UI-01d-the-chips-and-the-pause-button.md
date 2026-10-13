# UI-01d — The stage chip, the enemies-left chip and the pause button

**Size:** S · **Depends on:** UI-01b (the band's layout), UI-01c (`RunState.EnemiesLeft`) · **Branch:** `ui-01d-the-chips-and-the-pause-button`
**Design refs:** GD §7.1, §16.1, §16.4; ADR-0012; M3-14c, M6-03b · **Ledger rows:** none

The owner's brief of 2026-09-29, items 5–7: *"Stage/wave chip: 'STAGE 3 / Wave 2/3', left of the pause
button. Pause button: the new round button. Its behaviour is unchanged. Enemies-left chip: under the
pause button, shown only when 5 or fewer enemies remain."* Starting size: pause button 44 dp.

## Goal

The top-right corner reads like the mockup's: a dark pill with the stage and the wave, the round pause
button beside it, and a skull chip under it that appears for the last five enemies of a stage.

## Why this shape

- **One new component for the two chips.** Both read the stage's progress, and neither shares an
  event with the player's row. `HudPresenter` is already the player's row, the fade, the boss band and
  both economies, so a fifth concern gets its own view, `StageReadout`, registered as `XpBarView` is.
- **The events carry the stage and the wave; the count is polled.** `StageArrived.Stage` and
  `WaveStarted`'s wave and count are published already. `RunState.EnemiesLeft` (UI-01c) changes on
  most ticks of a fight, so it is read each frame and written only when it moves, as the skill row's
  cooldowns are.
- **The pause button is art, and its behaviour is `PausePresenter`'s, untouched.** `Button_Pause`
  carries its own glyph, so the `II` text goes. The icon moves to the band's margin so its centre is
  the star's and the stage chip's.
- **The two economy readouts step down, still wired.** The stage chip takes the place left of the pause
  icon where the Essence counter was. The Essence counter and the Veilrot meter move to the right-hand
  column under the boss band's row, where nothing else is. Both are switched off in every shipped
  mode (RS-05b, RS-05d), and switched off is not removed.
- **Words are keys.** *STAGE* and *Wave* are user strings, so they are `ui.hud.stage` and
  `ui.hud.wave`, formatted by TMP's float overload with `{0:0}`, as the Pact card's price is.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/StageReadout.cs` | Game | **New.** Rules 1–6 |
| `Tests/Game/Presentation/StageReadoutTests.cs` | Tests.Game | **New.** Rules 1–6 |
| *small edits* | Game | `RunScope` registers `StageReadout` (an optional field, as `_autoCastRow` is); `PausePresenter._iconMarginDp`'s default (rule 7); `HudPresenter`'s two economy insets (rule 8); `HudLayout` gains the chips' numbers; `Palette.HudCaption` (rule 9) |
| *small edits* | Data | `English.asset` gains `ui.hud.stage` and `ui.hud.wave`; `Pseudo.asset` regenerated |
| *small edits* | Tests.Game | `HudLayoutTests` gains the chips, the icon and the two economy readouts; `PausePresenterTests.Icon_IsPlacedInDpFromTheSafeArea`'s margin; `SkillBarPresenterTests.Hud_NothingElseMoved`'s set of texts; `PaletteTests` (one row, the count 23 → 24) |
| *assets* | | `Hud.prefab`: `StageChip` (`Pill_Dark`, `StageText`, `WaveText`) and `EnemiesChip` (`Chip_Counter`, `Icon_EnemiesLeft`, `EnemiesText`), both in `LilitaOne_SDF`. `Pause.prefab`: `Icon` wears `Button_Pause`, untinted, and loses `Glyph` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
public sealed class StageReadout : MonoBehaviour     // block namespace (Traps §5)
{
    public const int ShownAtOrBelow = 5;
    [SerializeField] private RectTransform _stageChip;
    [SerializeField] private TMP_Text _stageText;      // "STAGE 3"
    [SerializeField] private TMP_Text _waveText;       // "Wave 2/3"
    [SerializeField] private RectTransform _enemiesChip;
    [SerializeField] private TMP_Text _enemiesText;
    [SerializeField] private Vector2 _stageChipSizeDp = new Vector2(80f, 36f);
    [SerializeField] private Vector2 _stageChipInsetDp = new Vector2(62f, 17f);   // from the top-right
    [SerializeField] private Vector2 _enemiesChipSizeDp = new Vector2(48f, 22f);
    [SerializeField] private Vector2 _enemiesChipInsetDp = new Vector2(10f, 61f);
    [Inject] public void Construct(DomainEventHub hub, IRunSession session, ILocalizer localizer);
    public int Stage { get; }
    public int Wave { get; }              // 0 until the stage's first WaveStarted
    public int WaveCount { get; }
    public bool IsEnemiesChipShown { get; }
}
// LocKeys: ui.hud.stage = "STAGE {0:0}", ui.hud.wave = "Wave {0:0}/{1:0}".
// Palette gains HudCaption #CCC4B7. HudPresenter: _essenceInsetDp (36, 124), _meterInsetDp (16, 124).
// PausePresenter: _iconMarginDp (12, 13).
```

## Behaviour

1. **The stage chip reads the stage and the wave.** `StageArrived` sets the stage and forgets the wave.
   `WaveStarted` sets the wave and its count. Before a stage's first wave, which is GD §7.1's two-second
   arrival, the chip reads the stage alone, centred. After it, *STAGE 3* sits over *Wave 2/3*. A boss
   stage reads what core says, `Wave 1/1`.
2. **The opening state is read.** On `RunStarted`, and in `Start`, the stage is `RunState.StageIndex`
   and the wave is unknown. A resume always opens in an arrival, so there is no wave to restore.
3. **The enemies-left chip shows for the last five.** Each frame it reads `RunState.EnemiesLeft`. It is
   shown with the number when the count is 1 to `ShownAtOrBelow`, and hidden at 0 and above five. A
   text is written only when its number changes.
4. **Words are keys, numbers are formats.** The two formats are read from the localizer once, at
   `Start`, and written with TMP's float overload, so nothing allocates per event or per frame. A
   missing localizer draws the keys, as `HudPresenter`'s captions do.
5. **Colours are `Palette`'s.** *STAGE n* is `Palette.HudCaption`, bone. The wave and the enemy count
   are `Palette.HudText`. The chips' art is untinted.
6. **Both chips are placed in dp from the safe area's top-right**, as the Essence counter was. The
   stage chip is 80 × 36 dp, `GapDp + 2` left of the pause icon, on the star's centre line. The enemies
   chip is 48 × 22 dp, centred under the pause icon, 4 dp below it. The fonts are sized from dp: 11 dp
   for *STAGE*, 14 dp for the wave and the count. A nonsense field leaves the chip's authored rect
   alone.
7. **The pause button wears `Button_Pause` at 44 dp, `MarginDp` in and `RowTopDp` down.** It is
   untinted, with no text of its own. Every rule of `PausePresenter` but the margin is unchanged.
8. **The Essence counter and the Veilrot meter hang in the right-hand column under the boss band's
   row**, from 124 dp down: the meter at 16 dp in, the counter left of it. Their events, their seeding
   and their switches are M6-03b's and RS-05's, unchanged.
9. **`Palette.HudCaption` is `#CCC4B7`.** It is the bone of a caption: *everything else* in GD §16.4,
   at the brightness a word on a dark pill needs. It has a row of its own, and it is not danger.
10. **The band still holds.** At 914 × 411 and at 411 × 914 dp, the chips, the icon and both economy
    readouts overlap nothing, lie inside the safe area, and the chips stay above `BandBottomDp`.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Stage_ArrivalReadsTheStageAlone` | `StageArrived(3)` / drawn / `STAGE 3` shown, wave hidden — rule 1 |
| `Stage_AWaveReadsTwoLines` | then `WaveStarted(3, 2, 3)` / drawn / `STAGE 3` and `Wave 2/3` — rule 1 |
| `Stage_ANewStageForgetsTheWave` | then `StageArrived(4)` / drawn / `STAGE 4`, wave hidden — rule 1 |
| `Stage_ABossStageReadsOneOfOne` | `WaveStarted(5, 1, 1)` / drawn / `Wave 1/1` — rule 1 |
| `Stage_OpensOnTheRunsStage` | a run resumed at stage 7 / `Start` / `STAGE 7`, wave hidden — rule 2 |
| `Enemies_ShownForTheLastFive` | `EnemiesLeft` 6, 5, 1, 0 / a frame each / hidden, `5`, `1`, hidden — rule 3 |
| `Enemies_WritesOnlyWhenTheCountMoves` | 3 for ten frames / read the text's instance / one write — rule 3 |
| `Strings_AreKeysAndResolve` | the two keys / English / both rows exist and hold `{0:0}` — rule 4 |
| `Strings_NoLocalizerDrawsTheKeys` | a null localizer / `Start` / the key text — rule 4 |
| `Readout_AllocatesNothingPerFrame` | a run in a wave / `AllocationAssert.None` over 100 frames / 0 bytes — rule 4 |
| `Colours_AreThePalettes` | drawn / read / caption `HudCaption`, wave and count `HudText` — rule 5 |
| `Chips_ArePlacedInDpFromTheTopRight` | placed / read / rule 6's rects, to 0.5 dp — rule 6 |
| `Chips_ANonFiniteFieldLeavesTheChipAlone` | a NaN size / placed / the authored rect — rule 6 |
| `Prefab_IsDressed` | `Hud.prefab`, `Pause.prefab` / read / the art's sprites, `LilitaOne_SDF`, no `Glyph`, the icon white — rules 5–7 |
| `Band_Landscape_NothingOverlaps`, `Band_Portrait_NothingOverlaps` (extended) | the chips, the icon, both economy readouts / placed / rule 10 |
| `HudCaption_IsBone` (PaletteTests) | read / `#CCC4B7`, not danger — rule 9 |
| `Icon_IsPlacedInDpFromTheSafeArea` (changed) | placed / read / margin (12, 13) — rule 7 |
| `Hud_NothingElseMoved` (changed) | `Hud.prefab` / its texts / the set gains `StageText`, `WaveText`, `EnemiesText` — rule 1 |

**Changed expectations, and why.** `Icon_IsPlacedInDpFromTheSafeArea` pinned a 16 dp margin; the band's is
12 and 13 (rule 7). `Hud_NothingElseMoved` pins every text on the HUD by name, and this task adds three
(rule 1). The Palette count goes 23 → 24 (rule 9).

## Manual verification (Editor / device)

1. **[Editor]** A Ranger run from stage 1. *Expected: `STAGE 1` alone for two seconds, then `STAGE 1 /
   Wave 1/2`; the skull chip at 5 left and counting down; gone at the clear.*
2. **[Editor]** Tap the new pause button. *Expected: the pause screen, exactly as before.*
3. **[Editor]** The two captures of UI-01b's step 1, with the corner compared to the mockup's.
4. **[device]** The 11 dp caption at 400 dpi. Deferred with every device row.

## Out of scope

- **GD §16.1's *"fades 3s after wave start"*.** The owner's mockup keeps the chip up; the fade is a
  later ruling if play asks for it.
- **The pause screen itself**, and every other overlay.
- **Moving `II` to a key.** The glyph is art now, so there is no text left to key.

## As built

**As specified in its chips, with six deviations, two of them layout the band's own test found.**

**Deviation 1: the Essence counter sits over the meter, not left of it.** At the spec's insets the
counter (36 dp in, 124 down) and the Claiming's name, which hangs beside the meter's top, occupied
the same rect once both were switched on. So the counter heads the column at (16, 124) and the meter
starts under its caption at (16, 172). Both are off in every shipped mode. `Hud_PlacesBothInDp`
pinned the counter's old 68 dp inset and reads 16 now.

**Deviation 2: *STAGE* is 12 dp, not 11.** `Hud_NothingElseMoved` holds every text on the HUD to
Android's 12 sp floor (M4-07), and 11 is under it. The three new texts are sized in dp at run time,
so the row skips their authored points and `Chips_ClearTheAndroidFloor` measures the dp they are drawn
at. Twelve and fourteen still fit two lines in the 36 dp chip.

**Deviation 3: the skill row starts 8 dp under the bar, not 6.** `HudLayoutTests` found the stage
chip's bottom edge (53 dp) and the eighth cell's top (52 dp) overlapping at 9:20. `HudLayout.SkillRowTopDp`
is 54 now, and the pips still end inside the band's 96 dp floor at UI-01e's 34 dp cells. UI-01b's two
rows that pinned 6 read 8.

**Deviation 4: `Strings_NoLocalizerDrawsTheKeys` is `Strings_AMissingRowDrawsTheKey`.** `Construct`
refuses a null localizer, as `HudPresenter.Construct` does, so rule 4's null branch is unreachable.
The row asserts the reachable case: a table with no row draws the key.

**Deviation 5: the allocation row measures frames on which the count holds.** Its first form went
red, and a bisect found the whole allocation in TMP's `SetText(string, float)`, which ends with
`#if UNITY_EDITOR m_text = InternalTextBackingArrayToString()`. That line is compiled out of a
build, so the float overload stays the zero-garbage route on a phone. An Editor row over a changing
count measures that line, so this one measures the chip, and `Enemies_WritesOnlyWhenTheCountMoves`
pins that a write happens on a change alone. Filed as [Traps §9](../../Traps.md), which is not in
the Files table.

**Deviation 6: two files outside the table.** `RunSceneTests.RunScene_WiresTheStageReadout` pins the
scene's wiring, since no row checked `RunScope`'s HUD fields. `Run.unity` was edited by hand to the
twelve lines that wiring needs: the Editor's own save carried 380 lines of TMP overrides on other
prefab instances. `English.asset` was edited by hand for the same reason. `Pseudo.asset` came from its
generator's menu.

**As built, the rest.** `StageReadout` sits on a stretched `StageReadout` object under the safe area,
with `StageChip` (`Pill_Dark`, `StageText`, `WaveText`) and `EnemiesChip` (`Chip_Counter`, `Skull`,
`EnemiesText`) under it. `Place` fits both pills' slices to their height. `Pause.prefab`'s icon wears
`Button_Pause`, untinted, and its `Glyph` child is gone. The captures are in
`Soulvail_GeminiSamples/UI_v2/captures/`. In landscape the pill, the round button and the skull chip
sit on the star's centre line, as the mockup's corner does. At 9:20 the bar narrows to 201 dp beside
them. For each capture the chip was forced on with *3*, because stage 1 holds ten enemies.
