# M5-07 — Class select: the first screen that asks the player a question before a run

**Size:** M · **Depends on:** M5-02, M5-06b · **Branch:** `m5-07-class-select-screen`
**Design refs:** CH §3, §3.1, §3.2, §6; GD §4.5, §16.1, §16.4; AR §3, §8, §11.5; ADR-0012 · **Ledger rows:** none — but this is the task that makes [rows 1](../ROADMAP.md#carry-forward-into-m5), [2](../ROADMAP.md#carry-forward-into-m5) and [3](../ROADMAP.md#carry-forward-into-m5) answerable for a second class

## Goal

`Descend` stops meaning *"play the first class in the catalog"*. It opens a screen with one card per
authored class, and the card the player taps is the run they get — which is also the first moment in
the project's life that a Wight, a decoy, a Bone Bolt or a Gravecaller node exists in a run somebody
is playing.

## What this task is really for

Six merged tasks will have shipped a second class that nobody can reach. [M5-01](M5-01-projectile-weapon-and-leading.md)'s
shot, [M5-02](M5-02-gravecaller-and-bone-bolt.md)'s asset, [M5-03](M5-03-shroudstep-and-corpse-decoy.md)'s
decoy, [M5-04a](M5-04a-minion-agents-and-registry.md)/[b](M5-04b-rise-and-minion-stats.md)'s Wights,
[M5-05a](M5-05a-wight-views-and-concurrency.md)/[b](M5-05b-decoy-view-and-the-look.md)'s bodies and
[M5-06b](M5-06b-gravecaller-tree-v1.md)'s twelve nodes each said *"the first time anyone sees this is
later"*, and this is later. **The Manual verification section is therefore the largest in the
milestone and is the point of the task**, not an appendix to it: seven tasks' worth of content meets
a person for the first time in one Play.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Presentation/ClassSelectPresenter.cs` | Game | The screen: one card per class, the tap that writes `PendingRun`, and the way back — **block namespace** ([Traps §5](../../Traps.md)) |
| `Game/Controls/ClassCard.cs` | Game | One card: name, the three numbers, and the button. `OfferCard`'s shape (rule 4) |
| `Game/Presentation/MenuPresenter.cs` | Game | **Substantial.** `Descend` opens the screen instead of starting a run (rules 1, 2) |
| `Tests/Game/Presentation/ClassSelectPresenterTests.cs` | Tests.Game | What it draws, what a tap writes, the way back, and the raw-string sweep |
| `Prefabs/UI/ClassSelect.prefab` | — | The screen. An asset — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | Game, Core | `MenuScope` registers the presenter; `Scenes/Menu.unity` is dressed with the prefab; `CharacterSpec` and `CharacterDefinition` gain a `DescriptionKey` (rule 5); `English.asset` gains eight rows (rule 8) |
| *ripple* | Tests.Game | `MenuPresenterTests` — `Descend` no longer loads a scene; `TableLocalizerTests`' *"no English typed into a prefab"* sweep gains `ClassSelect.prefab`; `CharacterDefinitionTests` and `ContentValidationTests` gain the new key on both classes |

Only these files change. Anything else is a deviation: say so in *As built*.

**Pre-declared tripwire, in M3-11a's and M4-06's shape:** if the card wants a portrait, a silhouette
or anything that is not a `TMP_Text` and a `Button`, **this task splits into `M5-07b` / `M5-07c`
before a line is written**, never after — the letters skip `a`, which is CH §5.4's already-taken
task ([M5-07a-i](M5-07a-i-the-runs-tree-widens.md)) and not a half of this one. Two cards of five
labels is the bet; art on a card would lose it, and art is M7's.

## Public API

```csharp
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The class-select screen. It decides nothing about the run beyond which class — AR §3, and
    /// MenuPresenter's own argument: mode, class and seed are choices a player makes.
    /// </summary>
    public sealed class ClassSelectPresenter : MonoBehaviour
    {
        // [SerializeField] CanvasGroup _root; Button _back; TMP_Text _title, _backLabel;
        // [SerializeField] ClassCard[] _cards;   // authored on the prefab, never instantiated (rule 3)
        [Inject]
        public void Construct(
            PendingRun pending, ContentCatalog catalog, SceneLoader loader, ILocalizer localizer);

        /// <summary>Draws one card per authored class and puts the screen up.</summary>
        public void Open();

        /// <summary>Takes it down without choosing. Nothing is written.</summary>
        public void Close();

        /// <summary>Whether the screen is up. Read by MenuPresenter and by the tests.</summary>
        public bool IsOpen { get; }
    }
}

namespace Soulvail.Game.Controls
{
    /// <summary>One class, as a card. Dumb: it is told what to draw and it reports a tap.</summary>
    public sealed class ClassCard : MonoBehaviour
    {
        // [SerializeField] Button _button; TMP_Text _name, _description, _hp, _speed, _weapon;

        /// <summary>The id this card is standing for, or default when it is not in use.</summary>
        public ContentId CharacterId { get; }

        /// <summary>Draws <paramref name="spec"/> and becomes tappable.</summary>
        public void Bind(CharacterSpec spec, ILocalizer localizer, Action<ContentId> onChosen);

        /// <summary>Stops standing for anything and switches off. Rule 3.</summary>
        public void Clear();
    }
}

// Core/Content/CharacterSpec.cs — widened
/// <summary>One line about the class, for the card. Rule 5.</summary>
public LocKey DescriptionKey { get; }
```

## Behaviour

1. **`Descend` opens the screen; the screen starts the run.** `MenuPresenter.Descend` currently sets
   `PendingRun` from `FirstModeId()` and `FirstCharacterId()` and awaits `SceneLoader.LoadAsync` in
   one method; it keeps the mode and loses the class. The tap that writes
   `PendingRun.Set(modeId, chosenId, seed)` and awaits the load is the **card's**, and it is one
   method on one object — so the double-tap guard, the `async void` and the `catch` that gives the
   buttons back all move with it rather than being copied.
2. **The mode is still the catalog's first, and nothing here invents a mode-select.** GD §4.5's rule
   is that a mode is a data object and no code may assume Descent; `MenuPresenter.FirstModeId` is the
   stand-in that says so, and it is unchanged. V1 ships one mode and there is no mode-select on the
   roadmap, so the class becomes a choice and the mode stays a fact.
3. **The cards are authored on the prefab and none is instantiated.** `AutoCastRow`'s twelve cells
   and `LevelUpPresenter`'s three `OfferCard`s are both authored and bound rather than built
   (M3-10b rule 7, M3-08b), for AR §14's reason at a moment the player is about to enter a run:
   a screen that instantiates is a screen that hitches. **The prefab carries three cards** — CH §3's
   whole roster — and `Open` binds as many as the catalog holds and calls `Clear` on the rest, so the
   Emberwright's arrival at M6-07 is a card being filled rather than a prefab being edited. **More
   authored classes than cards is a warning, once, not a throw**: `EnemyViews.WarnAboutCapacityOnce`'s
   rule — a menu that refused to open would be a build nobody could play.
4. **A card draws five things and none of them is a number the code invented.** The name and the
   description come from `ILocalizer`; the three figures come off `CharacterSpec` — `MaxHp`,
   `Movement.Speed`, and the weapon as `Weapon.Damage × Weapon.SwingsPerSecond` rounded. **The
   arithmetic is `Weapon.DpsOneSecond`'s, spelled out over the spec** — that property lives on the
   live `Weapon` (`PlayerCombat.cs`) and a menu has no run to have one, so the card multiplies the
   two authored numbers and a row asserts the card and the live weapon agree, which is what stops the
   two spellings drifting. **Three figures and not eight**: CH §3's table has nine columns and a phone card has
   room for the three a player can act on, and *"you are not the damage"* is a sentence in the
   description rather than a fourth row of numbers.
5. **`CharacterSpec` gains a `DescriptionKey`, because the card needs a sentence and there is not
   one.** The spec carries `NameKey` and no description — `SkillSpec` carries both, which is what
   the level-up card draws. It is a **required** field rather than a defaulted one, unlike M5-02's
   `Minions` and M5-01's shot numbers: a class with no description is a card with a blank half, and
   there are exactly two shipped assets to fill. `CharacterDefinition` gains the `[SerializeField]`,
   both assets gain the key, and `ContentValidationTests`' existing sweep over every
   `CharacterDefinition` name key is where the new one is checked — a sweep that already exists is
   why this costs a line rather than a fixture.
6. **Every authored class is selectable, and the unlock gate is named rather than invented.** CH §6
   makes classes the one thing meta-progression buys — *"Shard cost or achievement"* — and
   `PlayerProfile` **v3 carries no unlock set**: four fields, `Version`, `HapticsEnabled`,
   `SeenFirstActiveHint`, `Shards`. Adding one here would be a **v4** and a migration for a gate
   nothing can open, since nothing spends a Shard until M6-02 and nothing awards an achievement at
   all. So the screen offers what the catalog holds, **M6-09 is where a card learns to be locked**,
   and the card's `Bind` takes no `bool interactable` — a parameter with one legal value is a
   promise the next task has to keep rather than a feature.
7. **`Back` closes without writing, and `Continue` is untouched.** A player who opens the screen and
   changes their mind gets the menu back with nothing set; `PendingRun` is written on the card's tap
   and on no other path. **A `Continue` takes none of this**: mode, class and seed all come off the
   snapshot (M2-14b rule 7), so a resumed Gravecaller run resumes as a Gravecaller without this
   screen having an opinion. A row asserts it.
8. **Eight strings, none typed into the scene.** `ui.classselect.title`, `ui.classselect.back`, and
   `character.oathbound.description` / `character.gravecaller.description` — plus the two
   `character.*.name` keys, which **`EveryLocKey_ResolvesInEnglish` already requires to be in
   `English.asset`**. AR §11.5's *"no raw user-facing string anywhere"* is a test here, as it has
   been since M3-14a, and `TableLocalizerTests`' prefab sweep gains this prefab.
9. **The screen is in the Menu scene and not a scene of its own.** `MenuScope` already registers one
   presenter and `SceneLoader` knows two scenes; a third would cost a load, a scope and an entry in
   the build settings for a screen the player is on for four seconds. It is a `CanvasGroup` raised
   over the menu, which is `PausePresenter`'s and `SkillsPresenter`'s shape.
10. **Nothing about the run loop changes.** No core file is in the Files table. `RunTicker`'s
    `FallbackCharacterId` stays exactly as it is — pressing Play with `Run.unity` already open is how
    this game is iterated on, and that path has no menu to have chosen anything.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Menu_DescendOpensTheScreen` | the shipped menu / `Descend` tapped / the screen is up, **no scene load**, and `PendingRun.IsSet` is false — rule 1 |
| `Select_DrawsACardPerAuthoredClass` | a catalog with two / `Open` / two cards bound, the third `Clear`ed and switched off — rule 3 |
| `Select_MoreClassesThanCardsWarnsOnce` | a catalog with four / `Open` twice / three cards, one warning, and the screen still opens — rule 3 |
| `Select_ACardDrawsItsClass` | the shipped Gravecaller / bound / the name and description from `English.asset`, `80`, `3.1`, and `36` for the bolt — rule 4 |
| `Select_TheTwoClassesDrawDifferentNumbers` | both shipped / bound / 140 / 3.0 / 39 against 80 / 3.1 / 36 — the comparison the screen exists to let a player make |
| `Select_ATapWritesThePendingRun` | the Gravecaller's card / tapped / `PendingRun.CharacterId` is `character.gravecaller`, the mode is the catalog's first, and the Run scene is loaded — rules 1, 2 |
| `Select_ATapIsTakenOnce` | a card / tapped twice before the load returns / one `Set`, one load — `MenuPresenter.Descend`'s double-tap guard, moved rather than copied |
| `Select_AFailedLoadGivesTheButtonsBack` | a loader that throws / tapped / the cards are interactable again and the exception is logged — `MenuPresenter`'s rule |
| `Select_BackWritesNothing` | the screen open / `Back` / it closes, the menu is up, and `PendingRun.IsSet` is false — rule 7 |
| `Select_EveryCardIsSelectable` | both shipped classes / `Open` / both buttons interactable, and `Bind` has no lock parameter — **rule 6**, so M6-09 has to add one deliberately |
| `Select_ProfileIsStillVersionThree` | `PlayerProfile.CurrentVersion` / — / **3**, and no field was added — rule 6 |
| `Continue_DoesNotOpenTheScreen` | a save present / `Continue` / the Run scene loads directly and the screen was never up — rule 7 |
| `Continue_ResumesTheSavedClass` | a snapshot naming `character.gravecaller` / `Continue` / `PendingRun` carries that id, from the snapshot and not from a card |
| `Spec_DescriptionKeyIsRequired` | a `CharacterSpec` with a default `DescriptionKey` / constructed / throws, naming the field — rule 5 |
| `Assets_BothClassesCarryADescription` | `Oathbound.asset` and `Gravecaller.asset` / `ToSpec` / two distinct non-default keys, both resolving in English — rules 5, 8 |
| `Select_DrawsNoRawEnglish` | `ClassSelect.prefab` / swept / every `TMP_Text`'s authored text is a `LocKey` claimed by a serialized field — `TableLocalizerTests`' and `StaticLabelWiringTests`' rule, rule 8 |
| `Select_InstantiatesNothing` | `Open` and `Close`, 100 cycles / `AllocationAssert.None` over the binding path / no `Instantiate` — rule 3 |
| `Menu_ContinueVisibilityIsUnchanged` | *(existing)* a save present and absent / `OnEnable` / the button appears and disappears as before — the row that proves the menu's other half did not move |

**Guard rows are implied, not listed:** null dependencies to `Construct`; an unassigned card array or
`Back` button reported as `MenuPresenter` reports an unassigned `Button`; `Bind` with a null spec.

## Manual verification (Editor / device)

**This is the milestone's first playtest of a second class, and every step below is the first time
anybody has seen the thing it names.**

1. **[Editor]** Boot → Menu → `Descend`. *Expected: two cards, each with a name, a sentence and three
   numbers, in English. Tap `Back`; the menu returns and nothing started.*
2. **[Editor]** Tap the Gravecaller. *Expected: a run starts, 80 HP, and the basic attack is a **bolt
   that flies** and leads a walking Husk — [M5-01](M5-01-projectile-weapon-and-leading.md) and
   [M5-02](M5-02-gravecaller-and-bone-bolt.md) seen for the first time. Count the hits on the first
   Husk: **four** (M5-02 rule 2).*
3. **[Editor]** Use the movement skill. *Expected: a 6 m blink, and **a corpse left where you were**
   that enemies walk at for three seconds — [M5-03](M5-03-shroudstep-and-corpse-decoy.md) and
   [M5-05b](M5-05b-decoy-view-and-the-look.md), seen for the first time. Watch a Husk swing at it and
   hurt nobody.*
4. **[Editor]** Kill four or five Husks. *Expected: about one in four stands back up **cyan and
   smaller**, walks at the nearest enemy and hits it — [M5-04b](M5-04b-rise-and-minion-stats.md) and
   [M5-05a](M5-05a-wight-views-and-concurrency.md), seen for the first time. **CH §3.2's watch item
   is answered here or it is not:** can you tell your army from the swarm?*
5. **[Editor]** Level and take the offer. *Expected: three cards drawn from the **Gravecaller's** six
   tier-1 nodes, not the Oathbound's. Take Exhume; watch it auto-cast three Wights when you are below
   two — [M5-06a](M5-06a-what-a-legion-node-may-reach.md) and
   [M5-06b](M5-06b-gravecaller-tree-v1.md), seen for the first time.*
6. **[Editor]** Take Grave Strength, then let a Wight expire and a new one rise. *Expected: the new
   one kills a Husk in four hits where the old took five ([M5-06a](M5-06a-what-a-legion-node-may-reach.md)
   rule 3's twenty-second lag, felt rather than argued).*
7. **[Editor]** Start an Oathbound run from the same screen. *Expected: identical to `m5-06b` in
   every respect — cone, no bolt, no decoy, no Wight, the Oathbound's twelve nodes.*
8. **[device]** Two cards at thumb distance: are the three numbers readable, and is the card a target
   a thumb hits without a mis-tap? [Ledger row 3](../ROADMAP.md#carry-forward-into-m5).

## Out of scope

- **Class unlocks.** Rule 6. CH §6, `PlayerProfile` v4 and M6-09.
- **A mode-select screen.** Rule 2. V1 ships one mode (GD §4.5).
- **The second-class splash at half tree.** CH §5.4 is [M5-07a-i](M5-07a-i-the-runs-tree-widens.md)
  and [M5-07a-ii](M5-07a-ii-the-half-tree-moment.md); this screen is chosen *before* a run and that
  one *during* it, and conflating them would put two mechanics on one prefab.
- **Portraits, silhouettes or class art.** The tripwire above, and M7.
- **Seed entry, a Daily, or anything that makes the seed a choice.** `Environment.TickCount` is
  unchanged; a seed stops being arbitrary when Daily mode exists.
- **Anything in `Soulvail.Core`** beyond `CharacterSpec`'s one required key. Rule 10.

## As built

`Descend` opens `ClassSelect.prefab` over the menu; a card's tap writes `PendingRun` and loads the
Run scene. `ClassCard` draws a name, a sentence and three numbers off `CharacterSpec`; the prefab
carries three cards and `Open` binds two and clears the third. **2 450 EditMode / 0 / 0** (+25) and
**PlayMode 19 / 0 / 0**.

**Ten deviations. Four change something.**

1. **`MenuPresenterTests` does not exist, and neither does `Menu_ContinueVisibilityIsUnchanged`.**
   The menu's rows have lived in [`ResumeFlowTests`](../../../Assets/_Project/Tests/Game/Composition/ResumeFlowTests.cs)
   since M3-07b, as `Menu_ContinueHiddenWithNoSave`, `Menu_ContinueShownWithASave` and
   `Menu_VisibilityIsDecidedOnEveryEnable`. The Tests table's row is built as
   `TableLocalizerTests.RewrittenRows_StillAssertWhatTheyAsserted`'s device — it names the three and
   asserts they still exist and still carry `[Test]` — rather than copied into a second fixture that
   would have to be kept in step. `ResumeFlowTests` took the constructor and `OnEnable` ripple.
2. **`CharacterSpec`'s new parameter is a 42-file ripple, not a 4-file one.** `new CharacterSpec(`
   has **57 call sites**, 11 of them target-typed `new(`, which the Files table's *ripple* row does
   not mention. Under the [sizing rule](../ROADMAP.md#how-to-read-this) these are additive one-line
   edits and the task does not split; the count is recorded because the spec's estimate was off by
   38 files.
3. **`English.asset` gains four rows, not eight.** Rule 8 enumerates six keys and two —
   `character.oathbound.name`, `character.gravecaller.name` — were already in the table. The four
   new ones are `ui.classselect.title`, `ui.classselect.back` and the two descriptions.
4. **The three figures are formatted from `const`s in code, not from table rows** — `"{0:0} HP"`,
   `"{0:0.0} m/s"`, `"{0:0} DPS"`. `SkillRow.CooldownFormat` is `"{0:0.0} s"` and
   `HudPresenter.HpFormat` is `"{0:0}/{1:0}"`, and `TableLocalizerTests` rules a number format out
   of AR §11.5's sweep explicitly. This is what keeps the card at rule 4's **five** `TMP_Text`s and
   the tripwire's bet intact; three static unit labels per card would have made it eight.
5. **`Select_InstantiatesNothing` counts objects rather than bytes.** `AllocationAssert.None` over
   the binding path cannot pass: composing `"140 HP"` allocates a string before TMP is reached, and
   **nothing in this project asserts a presenter *draw* allocation-free** — every existing use is
   over pure computation. The row asserts what rule 3 actually states: the same `ClassCard`
   instances and the same `Transform` count after 100 open/close cycles. Filed as [Traps §7](../../Traps.md).
6. **`MenuPresenter` lost `ContentCatalog`** and both helpers. `FirstCharacterId` is deleted outright
   — the class is a tap now — and `FirstModeId` moved verbatim onto `ClassSelectPresenter`, beside
   the write it feeds, so GD §4.5's *no code may assume Descent* is still enforced in one place.
   Rule 2 says the method is "unchanged"; it is, but it is not in this file any more.
7. **`ClassCard` is not added to `TableLocalizerTests`' `Readers_DrawNoKeyDirectly` or
   `Cells_TakeThePortOnTheirDrawCall`.** The second asserts a public `Show`, and this card's draw
   call is `Bind`. Neither list is in the Files table, so both are left alone.
8. **`MenuScope` registers the screen and refuses an undressed one**, unlike `RunScope`'s optional
   presenters: there is no play-the-Menu-scene-undressed workflow to protect, and a `Descend` that
   opens a dead panel is worse than one that says so at boot.
9. **`ClassCard` gained `IsShown` and `IsInteractable`** beyond the *Public API* block — both are
   reads the Tests table's rows need (`the third Clear`ed and switched off`, `both buttons
   interactable`) and neither is reachable otherwise.
10. **M5-06b left `☐☑` in its own ROADMAP box**; corrected to `☑` here.

**Not done, and named:** manual step 8 is the device question ([row 3](../ROADMAP.md#carry-forward-into-m5)).
Card layout is authored in reference pixels rather than placed in dp — no rule asks for it, and
[Traps §9](../../Traps.md) says the Editor cannot judge it anyway.
