# UI-01e — The auto-cast row: icons, sweeps, glows and the volley's pips

**Size:** M · **Depends on:** UI-01b (the row's place) · **Branch:** `ui-01e-the-auto-cast-row`
**Design refs:** GD §16.1 (*"auto-cast skills need visible cooldowns"*), §16.4; CC §6.1; RS-03b, RS-03d; M3-10b · **Ledger rows:** 3 (icons first — this row's part, not its discharge)

The owner's brief of 2026-09-29, item 4: *"Auto-cast skill row: under the health bar, one cell per
auto-cast active skill, with its icon, the cooldown sweep, and a ready glow. Volley shows its 5 charge
pips under its cell. Passives never appear in this row."* And: *"add an icon field to
SkillDefinition. The Ranger's Volley gets Icon_Volley; any skill without an icon shows
Icon_Placeholder."* Starting size: 34 dp cells.

## Goal

Every skill that fires on its own has a cell with its icon, a dark sweep over what is left of its
cooldown, and a glow when it is ready — and the Ranger's volley, which counts shots rather than
seconds, shows its count as pips under its cell.

## Why this shape

- **The volley is a cell of its own, read from core, not a node.** `skill.ranger.volley` is a Passive
  in the tree, and it is also the one self-firing thing the Ranger has. So the row admits two kinds of
  member: an Active on Auto, as since M3-10b, and the class's volley once a node has given it one.
  QuickDraw, FleetFoot and every other Passive still make no cell.
- **Which node the volley is drawn as is authored, not inferred.** The Ranger's `CharacterDefinition`
  names its volley node, beside the volley's own numbers. The HUD asks the look book for it and the icon
  book for its icon. Inferring it, as *"the first taken node that moves `VolleyEvery`"*, would read
  effects to decide a picture.
- **Icons live in Game, keyed by `ContentId`.** A `Sprite` cannot cross into core
  (`noEngineReferences`), so `SkillIconBook` is built at boot beside `ContentCatalog`, as
  `CharacterLookBook` and `EnemyLookBook` are.
- **Three narrow volley reads.** The pips need the count and the shots, which only `PlayerCombat`'s
  internal `Volley` holds. `RunState` forwards them, as it forwards the cooldowns (AR §18.2).
- **The sweep shades what is left, in ink.** The mockup and the art's `RollButton_Cooling` draw a
  cooling skill as a dark wedge over the icon that clears clockwise. So the sweep's fill is the cooldown
  fraction, where M3-10b's was one minus it under a bare cell. The information is the same; the
  picture is now the icon, and the shade is over it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Authoring/SkillIconBook.cs` | Game | **New.** Rule 2 |
| `Game/Presentation/AutoCastRow.cs` | Game | **Rewrite.** Rules 1, 3–8 |
| `Tests/Game/Authoring/SkillIconBookTests.cs` | Tests.Game | **New.** Rules 2, 9 |
| `Tests/Game/Presentation/AutoCastRowTests.cs` | Tests.Game | **Edit.** Rules 1, 3–8 |
| *small edits* | Game | `SkillDefinition._icon`; `CharacterDefinition._volleyNode` and `CharacterLook.VolleyNode`; `BootInstaller` builds and registers the book |
| *small edits* | Core | `Volley.EveryCount`; `RunState.VolleyEvery`, `VolleyShotsTowardNext`, `IsVolleyReady` |
| *small edits* | Tests | `VolleyTests` (`EveryCount`); `RangerTreeTests` (the three reads through a real run); `HudLayoutTests` (the cells and the pips in the band) |
| *assets* | | `Volley.asset` wears `Icon_Volley`; `Ranger.asset` names `Volley.asset` its volley node; `Hud.prefab`'s row: twelve cells of frame, icon, sweep and glow, and one volley cell with five pips |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
public sealed class SkillIconBook                     // Soulvail.Game.Authoring, file-scoped
{
    public SkillIconBook(IReadOnlyDictionary<ContentId, Sprite> icons);  // copies; refuses a default id
    public Sprite For(ContentId skillId);             // null for a skill with none, or an unknown id
}
// SkillDefinition: [SerializeField] private Sprite _icon; public Sprite Icon => _icon;
// CharacterDefinition: [SerializeField] private SkillDefinition _volleyNode;
// CharacterLook: public ContentId VolleyNode { get; }   — default when the class names none
// Volley: public int EveryCount { get; }              — Every floored, 0 when there is no volley
// RunState: public int VolleyEvery { get; } public int VolleyShotsTowardNext { get; } public bool IsVolleyReady { get; }
//           — 0, 0, false for a class with no volley

// AutoCastRow:
public const float CellSizeDp = 34f;
public const float GapDp = 4f;
public const int MaxPips = 5;
[Inject] public void Construct(IRunSession session, DomainEventHub hub, SkillIconBook icons, CharacterLookBook looks);
[SerializeField] private Image[] _frames = new Image[SkillRunner.MaxActives];   // the cell, and what is switched off
[SerializeField] private Image[] _icons = new Image[SkillRunner.MaxActives];
[SerializeField] private Image[] _sweeps = new Image[SkillRunner.MaxActives];   // Filled / Radial360, from the top
[SerializeField] private Image[] _glows = new Image[SkillRunner.MaxActives];
[SerializeField] private Image _volleyFrame, _volleyIcon, _volleyGlow;
[SerializeField] private Image[] _pips = new Image[MaxPips];
[SerializeField] private Sprite _placeholderIcon, _pipEmpty, _pipFull;
[SerializeField, Range(0f, 1f)] private float _sweepAlpha = 0.55f;
public int CellCount { get; }        // Active cells drawn
public bool HasVolleyCell { get; }
public int PipsShown { get; }
public int PipsFull { get; }
```

## Behaviour

1. **The row is the volley's cell, then the Actives on Auto.** The volley's cell is drawn when
   `RunState.VolleyEvery` is at least 1, which is when a node has given one. The Actives on Auto follow
   in the runner's order, as M3-10b rule 5 had them, up to twelve. **No other Passive makes a cell.**
   Cells pack from the front, with the volley first when it is there.
2. **An icon is the skill's own, or the placeholder.** `SkillIconBook.For(id)` is the definition's
   `_icon`, built at boot from the same skill list as the catalog. A skill with none, and an unknown
   id, draws `_placeholderIcon` (`Icon_Placeholder`). The volley's cell draws its volley node's icon:
   `CharacterLookBook.For(RunState.CharacterId).VolleyNode`. The Ranger's is `Volley.asset`, which wears
   `Icon_Volley`.
3. **The sweep is what is left of the cooldown.** Each Active's sweep is `Sweep_Disc`, filled
   radially from the top, clockwise. Its fill is `SkillCooldownFraction`: full at the cast, empty when
   the skill is live. It is `Palette.Ink` at `_sweepAlpha`, over the icon. It is written only when it
   changes, as before.
4. **A ready skill glows.** `Glow_Soft`, behind the cell, in `Palette.Player`, is shown while
   `IsSkillReady` holds, and for the volley while `IsVolleyReady` holds.
5. **The volley's pips are its shots toward the next.** Under its cell sit `min(VolleyEvery, MaxPips)`
   pips, as wide together as the cell, 3 dp below it. The first `VolleyShotsTowardNext` are
   `ChargePip_Full` and the rest `ChargePip_Empty`. Past five, the shots are scaled to five, floored.
   Ready fills every pip. At Volley's 5 there are five pips, and at Volley II's 4 there are four.
6. **Membership is rebuilt on its events; the fills, glows and pips are polled.** `NodeTaken`,
   `SkillAutoCastChanged` and `RunStarted` mark the row dirty, and the next `Update` rebuilds, which is
   M3-09c's deferral. A rebuild re-places the cells, because the volley's cell moves the rest by one.
   The sweeps, the glows and the pips are read each frame and written on change.
7. **34 dp cells with 4 dp gaps, from UI-01b's place under the bar.** Nine cells fit a 9:20 portrait
   from there; a tenth is a [parking-lot](../ROADMAP.md#parking-lot) line. A nonsense dp field leaves
   the authored layout alone.
8. **No colour is serialized.** The sweep is `Palette.Ink`, the glow `Palette.Player`, and the frames,
   icons and pips are the art, untinted. The kind tint goes: an icon is the cell's identity now, and
   only Actives and the volley reach the row.
9. **The shipped content says so.** `Volley.asset` wears `Icon_Volley`, and `Ranger.asset` names
   `Volley.asset` its volley node. Every other skill wears no icon yet, and draws the placeholder.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Volley_HasNoCellUntilANodeGivesOne` | a Ranger at `VolleyEvery` 0, then 5 / rebuilt / no cell, then a cell — rule 1 |
| `Volley_ComesFirst` | a volley and one Active / rebuilt / the volley at slot 0, the Active at slot 1 — rule 1 |
| `Row_IgnoresPassives` (kept) | a Passive taken / rebuilt / no cell — rule 1 |
| `Row_ShowsOneCellPerAutoActive`, `Row_ExcludesAManualSkill`, `Row_TakesASkillBack`, `Row_AndButtonsAreDisjoint`, `Row_HoldsTwelve`, `Row_DrawsTheOpeningStateOnRunStarted` (kept) | as before — rule 1 |
| `Row_DrawsEachSkillsIcon` | an Active whose definition has an icon / rebuilt / the cell's icon is it — rule 2 |
| `Row_ASkillWithNoIconDrawsThePlaceholder` | an Active with none / rebuilt / `_placeholderIcon` — rule 2 |
| `Volley_DrawsItsNodesIcon` | the Ranger / rebuilt / `Icon_Volley` — rule 2 |
| `Row_SweepIsWhatIsLeftOfTheCooldown` | a cast, half cooled / a frame / fill 1, then 0.5 — rule 3 |
| `Row_WritesOnlyChangedSweeps` | an idle frame / read / no write — rule 3 |
| `Row_GlowsWhenReady` | cooling, then ready / frames / glow off, then on — rule 4 |
| `Volley_PipsAreTheShotsTowardTheNext` | Every 5, 3 shots / a frame / 5 shown, 3 full — rule 5 |
| `Volley_PipsFollowARank` | Every 4 / a frame / 4 shown — rule 5 |
| `Volley_PipsScalePastFive` | Every 8, 4 shots / a frame / 5 shown, 2 full — rule 5 |
| `Volley_ReadyFillsThePipsAndGlows` | ready / a frame / every pip full, the glow on — rules 4, 5 |
| `Row_IsPlacedInDp` (changed) | placed / read / 34 dp cells, 4 dp gaps, from UI-01b's margin; the pips 3 dp under the volley cell — rules 5, 7 |
| `Row_IgnoresANonFiniteDpField` (kept) | — rule 7 |
| `Row_CarriesNoColourOfItsOwn` | drawn / read / sweep `Ink` at `_sweepAlpha`, glow `Player`; no serialized colour — rule 8 |
| `Construct_RefusesNull` (changed) | each of four / null / throws — Public API |
| `Book_AnUnknownIdIsNull`, `Book_RefusesADefaultId`, `Book_CopiesItsSource` | — rule 2 |
| `Definition_CarriesItsIcon`, `Look_CarriesTheVolleyNode` | — rule 2 |
| `Install_RegistersTheIconBook` | a built boot container / resolve / the book, holding the shipped skills — rule 2 |
| `Shipped_TheVolleyWearsIconVolley`, `Shipped_TheRangerNamesItsVolleyNode` | the assets / read / as rule 9 says — rule 9 |
| `EveryCount_IsEveryFloored` (VolleyTests) | Every 4.7, 0, NaN / read / 4, 0, 0 — Public API |
| `Volley_RunStateReadsTheCount` (RangerTreeTests) | a Ranger run, Volley taken, three shots / read / 5, 3, false — Public API |
| `Band_Landscape_NothingOverlaps`, `Band_Portrait_NothingOverlaps` (extended) | the volley cell, eight Active cells and the pips / placed / inside the band — rule 7 |

**Changed expectations, and why.** `AutoCastRow_IsStillIconless` goes: its subject was the absence of
icons, and this task is their arrival. `Row_FillsTrackTheirSkills` asserted a fill of one minus the
fraction; the sweep is a shade of the fraction (rule 3), and it becomes `Row_SweepIsWhatIsLeftOfTheCooldown`.
`Row_WritesOnlyChangedFills` is renamed for the sweep. `Row_TintsByKind` goes with the tint (rule 8).
`Row_IsPlacedInDp` pinned 24 dp cells; the brief's are 34 (rule 7). `Construct_RefusesNull` loses the
catalog and gains the two books.

## Manual verification (Editor / device)

1. **[Editor]** A Ranger run; take Volley. *Expected: a cell under the bar with the arrows icon and
   five pips filling one a shot, then a cyan glow, then empty after the fan.* Take Volley II: four pips.
2. **[Editor]** The two captures of UI-01b's step 1, with the cell and pips compared to the mockup's.
3. **[device]** A 34 dp icon and 6 dp pips at 400 dpi. Deferred with every device row.

## Out of scope

- **Icons for every other skill.** Each comes with its class's art; until then, the placeholder.
- **The manual buttons and the Roll button's sweep.** Below the band; a later row of this track.
- **The tree screen's icons**: ledger row 3's readout, after this.

## As built

**As specified in behaviour, with six deviations.**

**Deviation 1: a 9:20 portrait holds seven cells, not nine.** Rule 7's nine was written before UI-01d
put the enemies-left chip under the pause button. The chip's column starts 58 dp in from the right,
and the row, at 34 dp cells from 60, reaches it on its eighth cell. So `HudLayoutTests` checks the
volley's cell, its five pips and six Active cells at both sizes, and the
[parking-lot](../ROADMAP.md#parking-lot) line reads *"past its seventh cell"*.

**Deviation 2: the volley's arrival is polled as well as rule 6's events.** The volley's node is a
Passive, and `NodeTaken` skips a Passive without a lookup, which is M3-10b's rule and still right for
every other Passive. So `Update` compares `VolleyEvery >= 1` with the cell's presence and marks the row
dirty when they disagree. One comparison a frame; the rebuild is still deferred.

**Deviation 3: a cell is a container, not a frame.** The Public API named `_frames`. A ready glow has
to draw *behind* the frame, and uGUI draws a parent before its children, so a frame on the cell's root
could only ever have its glow on top. Each cell is a `RectTransform` (`_cells`) holding `Glow`,
`Frame`, `Icon` and `Sweep` in that order. The volley's cell holds `Glow`, `Frame`, `Icon` and five
`Pip`s, and no sweep. `Row_HoldsTwelve` counts 13 children and 56 images, and a full row is 452 dp.

**Deviation 4: three files outside the table.** `XpBarViewTests` builds the row in two rows and pins
its prefab in a third, so it follows the constructor and the new fields. And
`PaletteTests.RewrittenRows_StillAssertWhatTheyAsserted` named `Row_TintsByKind` as the row that
must keep reading `Palette.KindActive`. The tint went with the icons (rule 8), so the entry names
`Row_CarriesNoColourOfItsOwn`, which reads `Palette.Ink`.

**Deviation 5: `EveryCount_IsEveryFloored` drives the count below zero, not to NaN.** `ModifyStat`
refuses a non-finite value at construction, so NaN never reaches the stat. The row gives 4.7, then
−10, and reads 4, then 0.

**Deviation 6: `SkillIconBook` leaves a skill with no icon out**, and has a `Count`. The spec said the
book answers null for such a skill, and it does, by having no entry. `Install_RegistersTheIconBook`
builds a boot container over the Ranger, Volley and Quick Draw, and finds one icon.

**How the rows reach a volley.** The fixture's class carries a `VolleySpec` when a row asks for one,
and a taken Passive node gives it its count through `ModifyStat` on `VolleyEvery`, as a run's does.
Shots are loosed on core's own `Volley` through the internal `Combat` handle, which is
`RangerTreeTests`' reflection. `Volley_RunStateReadsTheCount` does the same over a real Ranger run
that takes Volley through the level-up.

**The captures** are in `Soulvail_GeminiSamples/UI_v2/captures/`, landscape on the Device Simulator at
480 dpi and 9:20 at its dp-equivalent. A level-1 Ranger has not taken Volley, so the volley's cell was
shown with three of five pips charged for the frame, as the mockup draws it. The first capture after
showing it landed a frame early and the second showed it, so each was taken twice.
