# M3-14a — `ILocalizer`, `TableLocalizer`, and one English table: the port ADR-0012 has been waiting three milestones for

**Size:** M (four code files, and a ripple across every screen M3 built) · **Depends on:** M3-12c (the twenty-four keys it has to hold), M3-08b, M3-09b, M3-09c, M3-09d, M3-10b (the readers) · **Branch:** `m3-14a-localizer-and-english-table`
**Design refs:** GD §13.1, §16.1; CH §5.1; AR §6, §11.5, §14; ADR-0001, ADR-0012 · **Ledger rows:** **9 — closed for six of its seven readers, and explicitly not for the seventh** (rule 9); 2 (the table is content, not a save format: rule 4 says why it is not a bump)

## Goal

A `LocKey` becomes a word. One port, one adapter, one English table — so the twelve nodes M3-12c ships read *"Consecrate"* rather than `skill.oathbound.consecrate`, and GD §13.1's two-second rule becomes something M3-15 can actually judge.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ports/ILocalizer.cs` | Core | AR §6's outbound port, written at last — one member (rule 10) |
| `Game/Authoring/LocalizationTable.cs` | Game | the `ScriptableObject`: rows of key → English, and its own duplicate check — **block namespace** (Traps §5) |
| `Game/Adapters/TableLocalizer.cs` | Game | the adapter AR §6 names: one dictionary, built once |
| `Tests/Game/Adapters/TableLocalizerTests.cs` | Tests.Game | lookup, a miss, a duplicate, allocation, and the boot registration |
| `Data/Localisation/English.asset` | — | about fifty rows. An asset, not a code file — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | | `Docs/Architecture.md` — **AR §6**'s `ILocalizer` row loses its `params` (rule 5); `Prefabs/Composition/BootScope.prefab` + the table; `Game/Composition/BootInstaller.cs` registers the adapter as `ILocalizer` (rule 4) |
| *ripple* | | six readers each gain the port and call `Get`: `Game/Controls/OfferCard.cs`, `Game/Controls/SkillRow.cs`, `Game/Controls/TreeNodeView.cs`, `Game/Controls/ManualSkillButton.cs`, `Game/Presentation/FirstActiveHint.cs`, `Game/Presentation/OverflowToast.cs` — and their `Construct` signatures, their fixtures, and the presenters that hand cells a template; `Game/Presentation/MenuPresenter.cs` + `Scenes/Menu.unity` and `Game/Presentation/HudPresenter.cs` + `Prefabs/UI/Hud.prefab` for the parking lot's four raw strings (rule 8) |

Only these files change. Anything else is a deviation: say so in *As built*. **If the readers push this past five code files, the split is the mechanism (`M3-14a-i`: port, table, adapter, boot) and the readers (`M3-14a-ii`) — decided before continuing, never after.**

## Public API

```csharp
namespace Soulvail.Core.Ports;

/// <summary>How a <see cref="LocKey"/> becomes a word. ADR-0012's other half, three milestones late.</summary>
public interface ILocalizer
{
    /// <summary>The string for <paramref name="key"/>, or the key's own text when there is no row.</summary>
    string Get(LocKey key);
}
```

```csharp
namespace Soulvail.Game.Authoring
{
    public sealed class LocalizationTable : ScriptableObject
    {
        // [SerializeField] Row[] _rows;  where Row is { string Key; [TextArea] string Text; }

        /// <summary>Every row, checked. Throws naming this asset on a duplicate or an empty key.</summary>
        public IReadOnlyDictionary<LocKey, string> ToDictionary();
    }
}
```

```csharp
namespace Soulvail.Game.Adapters;

public sealed class TableLocalizer : ILocalizer
{
    public TableLocalizer(LocalizationTable table);   // built once, at boot
    public int Count { get; }
    public string Get(LocKey key);
    public bool Has(LocKey key);                       // M3-14b's door, and nothing else calls it
}
```

## Behaviour

1. **A missing key returns the key's own text — never empty, never a throw.** A screen showing nothing is a bug that looks like a broken layout; a screen showing `skill.oathbound.consecrate` is the behaviour M3 has had since M3-08b and diagnoses itself. It also keeps the port honest for M6-10's other languages, where a half-translated table is the normal state rather than an error, and it is why `LocKey.Key` — **`.Key`, not `.Value`; the member M3-00c had to correct in five specs** — stays the fallback rather than a placeholder string nobody can trace.
2. **One language, one table, no locale selection, no plural rules, no format arguments.** This is the minimum that lets M3-15 judge GD §13.1's *"every node description must be readable in under 2 seconds on a phone"* and CH §5.1's case for random-from-available, both of which rest on a player reading three cards fast. **M6-10 is a widening, not a rewrite**: it adds languages beside `English.asset`, a locale on the profile, and the tables for the rest of the game. Named so that pulling one file forward does not turn into pulling a system forward.
3. **The table refuses a duplicate key at conversion, with `ContentCatalog`'s message and its reason.** *"Duplicate key 'x' in 'English'. Localisation keys must be unique."* A duplicate silently picks one row, and which one depends on authoring order — the same failure `ContentCatalog` has refused since M0-08, and the same fix. An empty or whitespace key is refused for the reason `LocKey`'s own constructor refuses one.
4. **Registered in `BootScope`, not `RunScope`, and it is content rather than a save format.** The Menu needs it as much as a run does (rule 8), and a localizer that existed only during a run is exactly how `"Descend"` would have stayed English for another three milestones. Registered as `ILocalizer` so nothing depends on the concrete adapter — the `LocalJsonSaveStore` precedent. **It bumps no format:** a table is a `ScriptableObject` read at boot, like every other `Data/` asset, so ledger row 2's rule does not fire and `PlayerProfile` stays at M3-09c's v2. The locale *setting* is a profile field and belongs to M6-10 with the second language.
5. **One member, and AR §6's row is corrected rather than obeyed.** The architecture has carried `string Get(LocKey key, params)` since M0, and nothing in M3 needs the `params`: the two callers with a number in them already format it themselves — M3-09b rule 2 (*"core says what kind of number it is; the screen formats it"*) and M3-10b rule 13 (the toast is a key plus a number). A `params object[]` overload allocates an array on every call, and these are called from `HudPresenter` and `AutoCastRow`, which run near the frame. So the port ships with one member and **AR §6's row is edited to match the code** — the M3-01a precedent, where `AR §5`'s `Progression` row was corrected by the task that found it wrong. M6-10 adds the overload when a translated sentence needs a substitution inside it, which is a real need and not this one.
6. **`Get` allocates nothing on a hit.** A dictionary probe returning a string the table already holds: no concatenation, no `string.Format`, no `ToString`. AR §14's no-allocation rule is about the frame rather than about core alone, and `AutoCastRow` and `HudPresenter` both call this from code that runs every frame a value changes.
7. **The table holds M3's real keys, and they are written to the rule this task exists to make testable.** Twenty-four from M3-12c (`skill.oathbound.<name>` and `.desc` ×12) plus three branch names (`tree.oathbound.oath` / `.censure` / `.judgment`); eighteen trigger lines from `TriggerText.KeyFor` (M3-09b rule 1, a key per `(TriggerField, TriggerComparison)` pair — *"Player HP below"*, *"Enemies within 5 m at least"*); the first-active hint (M3-09c rule 8); the Overflow toast (M3-10b rule 13); and the Skills screen's *"you own no actives yet"* line (M3-09b rule 6). **Every description is written short because GD §13.1 says a node that needs two lines is a node to redesign** — and this is the first task in the project where that sentence has a consequence.
8. **The parking lot's four raw strings become keys, because the line's own trigger has fired.** It says `"Soulvail"` and `"Descend"` in `Menu.unity` and `"You died"` and `"Tap to return"` on `Hud.prefab` *"become `LocKey`s when `ILocalizer` and the English tables land"* — this is that moment, and it is four table rows and two small edits. It means M6-10 inherits **no English typed into a prefab**, and it turns AR §11.5's *"no raw user-facing string anywhere"* from an aspiration into a thing a test asserts. The HP readout is deliberately not among them: `"{0:0}/{1:0}"` is a number format, not a sentence, and it survives localisation unchanged.
9. **Ledger row 9 closes for six readers and stays open for the seventh, which is the point of saying so here.** Closed: the offer card (M3-08b rule 7), the Skills row and its trigger line (M3-09b rules 1–2), the first-active hint (M3-09c rule 8), the tree view's twelve-to-twenty-seven cells (M3-09d rule 7), the Overflow toast (M3-10b rule 13), and the slot buttons (M3-10a rule 9, where the key never fitted and a word does). **Not closed: M3-10b rule 8's 24 dp auto-cast cells**, which can hold neither a key nor a word — that gap is an icon and it is M7's art pass, exactly as the row has said since M3-00c. M3-15 records the six and the one rather than discovering them.
10. **Core gains no dependency and resolves nothing.** The port is an interface over a `LocKey`; no core class takes an `ILocalizer`, because nothing in core has a reason to know what a key says (ADR-0012's whole shape). Every caller is in `Soulvail.Game`, which is where a user-facing string belongs. `AssemblyPurityTests` is unmoved: an interface in `Core/Ports` mentioning `string` is what a port is.
11. **A cell is handed the localizer by the screen that instantiates it, not by injection.** `TreeNodeView`, `OfferCard`, `SkillRow` and `ManualSkillButton` are pooled templates instantiated from a prefab field, and nothing injects them individually — the same fact that decided M3-13a rule 2 against an injected palette. So each takes the port as an argument on the method it already has (`Show`, `Draw`, `Bind`), and the presenter that owns the pool is the one thing injected. Three of the four already take a `SkillSpec` on that call, so this is one more parameter rather than a new shape.

## Tests

| Test | Given / When / Then |
|---|---|
| `Get_ReturnsTheRow` | a table with `skill.oathbound.consecrate` → *"Consecrate"* / `Get` / *"Consecrate"* (rule 1) |
| `Get_MissingKeyReturnsTheKey` | an empty table / `Get(new LocKey("a.b"))` / *"a.b"*, no throw, nothing logged (rule 1) |
| `Get_DefaultKeyReturnsEmpty` | — / `Get(default)` / `string.Empty`, no throw — `LocKey.ToString`'s own contract (rule 1) |
| `Get_IsOrdinalAndCaseSensitive` | a row for `a.b` / `Get("A.B")` / the key back, not the row — `LocKey.Equals` is ordinal (rule 1) |
| `Get_AllocatesNothing` | a warm table, 1 000 keys / 10 000 × `Get` on hits and misses alike / allocated-bytes delta == 0 (rule 6) |
| `Has_AnswersWithoutAllocating` | the same / `Has` on a hit and a miss / true, false, zero delta (rules 6, M3-14b's door) |
| `Table_ConvertsEveryRow` | three rows / `ToDictionary` / three entries, the text preserved including line breaks (rule 7) |
| `Table_DuplicateKey_NamesTheAsset` | two rows keyed `a.b` / `ToDictionary` / `ArgumentException` starting with the asset name and naming the key (rule 3) |
| `Table_EmptyKey_NamesTheAsset` | a row keyed `""` and one keyed `"  "` / `ToDictionary` / throws each (rule 3) |
| `Table_EmptyTextIsLegal` | a row with a key and no text / `ToDictionary` / one entry, empty text — an untranslated row is a normal state (rule 2) |
| `Table_RoundTripsThroughSerializedObject` | rows written through `SerializedObject` / `ToDictionary` / the same entries (Traps §5, M3-02b rule 2's shape) |
| `Port_HasOneMember` | reflection over `ILocalizer` / — / exactly one method, no `params` overload — the row AR §6 was corrected against (rule 5) |
| `Boot_RegistersTheLocalizer` | `BootScope.prefab` with the table / `Install`, resolve `ILocalizer` / a `TableLocalizer` over `English.asset`, and it is a singleton (rule 4) |
| `Boot_MissingTable_NamesTheField` | `BootScope` with no table / `Install` / a message naming the field, not a null reference (rule 4) |
| `Boot_LocalizerOutlivesARun` | resolve from `BootScope`, load Run, dispose `RunScope` / resolve again / the same instance (rule 4) |
| `Card_DrawsEnglish` | a spec keyed `skill.oathbound.consecrate` and a table row / `Show` / *"Consecrate"* — **M3-08b's `Card_DrawsTheKeyNotEnglish`, inverted, and that row is rewritten rather than deleted** (rule 9) |
| `Row_DrawsTheTriggerLineInWords` | a spec with an `HpFraction Below 0.6` trigger / `Open` / *"Player HP below 60 %"* — the key and the formatted number, assembled (rules 7, 9; M3-09b rule 2 from this side) |
| `Tree_DrawsEnglish` | the twelve-node tree / `Open` / twelve names and twelve descriptions, none of them a key (rule 9) |
| `Hint_DrawsEnglish` | the first Active taken / — / a sentence naming Pause → Skills (rule 9) |
| `Toast_DrawsEnglish` | `OverflowGranted(30, 14)` / — / *"Overflow ×14"*, the key resolved and the number formatted (rules 7, 9) |
| `SlotButton_DrawsAWord` | a manual skill in slot 1 / `Draw` / *"Bulwark"* rather than an id — M3-10a rule 9's gap, closed (rule 9) |
| `AutoCastRow_IsStillIconless` | an Auto Active / — / the cell carries no text component at all, and this test says why: rule 9's seventh reader is art (rule 9) |
| `Menu_AndDeathOverlayDrawFromTheTable` | the two scenes' four strings / `Start` / all four resolved through `ILocalizer`; **no `TMP_Text` in either carries authored English** (rule 8) |
| `Readers_DrawNoKeyDirectly` | reflection over the six readers / — / none references `LocKey.Key` or `LocKey.ToString` outside a fallback path (rules 9, 11) |
| `Cells_TakeThePortOnTheirDrawCall` | reflection over the four cell types / — / none has an `[Inject]` method; each takes `ILocalizer` on `Show`/`Draw`/`Bind` (rule 11) |
| `Core_TakesNoLocalizer` | reflection over every public constructor in `Soulvail.Core` / — / none takes an `ILocalizer` (rule 10) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Descend and level up. **The three cards read English for the first time in the project's history** — a name and a description each, no keys anywhere. This is the moment GD §13.1 becomes checkable (rule 7).
2. **[Editor]** Read each of the twelve descriptions and time yourself. Anything that takes more than two seconds, or needs two lines at card width, is a description to rewrite *here* — the table is a text asset and rewriting a row costs nothing. **If an effect cannot be said in one line, GD §13.1 says the effect is wrong**, and that is a finding for *As built* and for M3-15.
3. **[Editor]** Pause → Skills. Each row reads *"Player HP below 60 %"* or *"Projectiles incoming at least 1"* rather than `trigger.hpFraction.below` (rule 7).
4. **[Editor]** Pause → View Tree. Twelve cells, each a name and a description, which is the screen M3-09d rule 7 said could only be judged once keys resolved.
5. **[Editor]** Open the Menu. *"Soulvail"* and *"Descend"* are drawn from the table, and nothing in `Menu.unity` has English typed into it. Die in a run and check the same for the death overlay (rule 8).
6. **[Editor]** Delete a row from `English.asset` and Play. The card for that node reads its key again, nothing throws, and the rest of the screen is unaffected (rule 1). **Put the row back.**
7. **[device]** Ledger row 9's last question, and the one this task exists to enable: whether *"Consecrate — a zone that heals you while you stand in it"* is readable in under two seconds on a 6-inch screen mid-fight. The Editor can answer whether it *fits*; a phone answers whether it *reads*.
8. **[device]** The tree view with real text: M3-09d rule 9 bet that twelve cells fit a landscape safe area with no scroll. Keys were short; English is longer, and this is where that bet is settled.

## Out of scope

- **A second language, locale selection, a locale on the profile, plural rules, right-to-left, font fallback** — **M6-10**, all of it. Rule 2 is the line.
- **Formatting arguments inside a translated string** (`"{0} damage"`) — rule 5. The two callers that need a number format it beside the key, which is M3-09b rule 2's ruling and survives translation.
- **Localising the remaining UI the project has not built**: the Sanctum, Ordeals, settings labels, achievement text. Each arrives with its own screen and its own rows; this table holds what M3 shows.
- **Skill icons** — M7's art pass, and rule 9's seventh reader. This task makes five screens readable and deliberately does not pretend to fix the sixth.
- **Validating that every key has a row** — **M3-14b**, which is the task that sweeps assets. `Has` is the door it calls; asserting coverage here would be this task grading its own homework against a table it wrote.
- **A translator's workflow, a CSV importer, or `.po` files.** Fifty rows in an Inspector array is enough for one language; M6-10 owns the pipeline the day there are three.
- **Changing any `LocKey` a spec already authored.** M3-12c's twenty-four ids are content and stable (ADR-0010); this task writes the *text* beside them.

## As built

**Five code files, not the four ruled before writing — and the fifth is named rather than absorbed.** `ILocalizer`, `LocalizationTable`, `TableLocalizer`, `TableLocalizerTests` were ruled out loud as four, with `English.asset` listed and not counted. The fifth is `Tests/Core/Fakes/DictionaryLocalizer.cs`, forced by the ripple: **ten test fixtures gained an `ILocalizer` argument, not the six predicted**. Seven of them need only *a* localizer and use the real `TableLocalizer` over an **empty** `LocalizationTable`, which answers every key with itself — so every row written before this task still asserts exactly what it asserted, and no fixture needed rewriting to stay honest. The three that assert a screen reads English need rows, and three inline `SerializedObject` blocks is the project's own "promoted by the third copy" threshold. **Five is size M's ceiling, so the pre-declared `M3-14a-i` / `-ii` split did not fire** — it fires *past* five — but the ruling was four and the count is five, which is the finding rather than a footnote. Every production edit is additive (a parameter on `Show`, a dependency on an existing `[Inject] Construct`, two new `TMP_Text` fields, one `Register`): **zero substantial rewrites**.

**The spec's own key spelling is wrong, and the failure mode is silent by design.** Rule 7 says the table holds `skill.oathbound.<name>` and **`.desc`**; the twelve shipped assets author **`.description`**, and the names are **kebab-case** (`keen-censer`, `broad-censure`, `tempered-vow`, `lasting-ground`, `crashing-censure`, `steady-breath`, `long-reach`). A table written to the spec's spelling ships **twenty-four rows that resolve to nothing and a suite that is entirely green**, because rule 1 makes a miss return the key. **Rules 1 and 7 are therefore in tension: the graceful fallback is exactly what makes rule 7's correctness untestable inside this task.** Closed by *generating* the rows — all 63 rows were written by a command that read `_nameKey` / `_descriptionKey` off each asset through `SerializedObject`, never typed — and by probing all 24 through `Has` afterwards: **24 of 24 answer true**, while the spec's own `skill.oathbound.consecrate.desc` answers **false** and returns the key. **Coverage is deliberately unasserted in the suite**: "every key has a row" is **M3-14b**'s, over every asset, and asserting it here would be this task grading its own homework against a table it wrote. `Has` ships as that task's door and nothing else calls it.

**Rule 8 says four raw strings and there are five.** `Menu.unity` holds `Descend`, `Soulvail` **and `Continue`** — the last added by M3-07b's resume flow and counted by neither rule 8 nor the [parking-lot line](../ROADMAP.md#parking-lot), both of which say four. It is a key too, because AR §11.5 is unenforceable if it is exempt. **The ROADMAP's count is the owner's to correct, not this task's to edit silently.** The HP readout stays out, as the line intends: `"{0:0}/{1:0}"` is a number format, and `Menu_AndDeathOverlayDrawFromTheTable` asserts `140/140` is *still* authored. **A sixth English string was found and is deliberately out of scope**: `Boot.unity` carries its own `Soulvail` splash, which nothing resolves because nothing in that scene is a presenter. It shares `ui.app.title`'s row, so wiring it later costs one field.

**The draw call is `Show` on all four cells, and the spec's text says otherwise twice.** `ManualSkillButton` has `Bind(int, SkillSlotInput, IRunSession)` and `Show(SkillSpec)`; the label is written in `Show`, and its only `Draw` is **private**. So the Tests table's `SlotButton_DrawsAWord` (*"/ `Draw` /"*) and rule 11's *"`Show`/`Draw`/`Bind`"* are both loose. `Cells_TakeThePortOnTheirDrawCall` **asserts the method exists before asserting anything about it**, because a reflection row hunting a method that is not there goes green on nothing. Rule 11's premise held: none of the four carries an `[Inject]` method.

**`Get` returns `key.ToString()` and never `key.Key`, and it is one character.** `LocKey.Key` is null for `default`; `ToString()` is `Key ?? string.Empty`; `GetHashCode` answers 0 and `Equals` is an ordinal `string.Equals`, so a `default` probe is legal and simply misses. `Key` would hand a null to a `TMP_Text` and the `NullReferenceException` would surface three screens from the unset field. Probed: `DEFAULT -> '' isNull=False length=0`.

**`Core_TakesNoLocalizer` lives in `AssemblyPurityTests`, which is what makes this four assemblies** — `Soulvail.Core` ×1, `Soulvail.Game` ×16, `Soulvail.Tests.Core` ×2, `Soulvail.Tests.Game` ×12, 31 files, every one confirmed through `GetAssemblyNameFromScriptPath`. A claim about core's purity belongs beside `Run_NoTypeTakesAClock`, not in a fixture about a Unity-side adapter. **`Menu_AndDeathOverlayDrawFromTheTable` went into `TableLocalizerTests`** rather than a new `MenuPresenterTests` — there is neither that nor a `HudPresenterTests` — beside `Readers_DrawNoKeyDirectly` and `Cells_TakeThePortOnTheirDrawCall`, because all three are assertions about **AR §11.5's rule** rather than about a screen.

**The finding the spec does not have: eleven static labels on four prefabs still draw unresolved keys, and nine of them are still doing it after this task.** `Pause.prefab` authors `ui.pause.resume` / `.skills` / `.tree` / `.quit`, `Skills.prefab` authors `ui.skills.close` / `.cancel` / `.slotsFull` / `.empty`, `TreeView.prefab` authors `ui.tree.close` / `.none`, and `LevelUp.prefab` authors `ui.levelup.tree`. These are a **third category** neither rule names — rule 7 lists only the Skills screen's empty line, and rule 8 counts only raw *English*. **Two were wired and nine were not, on a stated line**: a label was wired where the presenter is already in the ripple *and* already holds a `TMP_Text` field for it, needing no prefab change — which is `ui.skills.empty` (rule 7 names it) and `ui.tree.none` (the same two-line edit). The other nine each need a new serialized field and a prefab re-dress on `Pause.prefab`, `Skills.prefab`, `TreeView.prefab` and `LevelUp.prefab` — four assets and a presenter outside the Files table. **All eleven have rows in `English.asset` regardless**, so wiring them later is one line each and M3-14b's sweep has something to find. The visible consequence is real and should not be discovered in a playtest: **the tree view now draws twelve English nodes under a Close button reading `ui.tree.close`.**

**GD §13.1's one-line rule cannot be met at the shipped card geometry, and that is a finding about the *card* rather than about the effects.** Measured rather than guessed: the description label on `LevelUp.prefab` is **176 dp wide at 18 pt**, which is about **twenty characters a line**. Of the twelve descriptions, **ten wrap to two lines and one to three** — and the two that fit are `+15 maximum health.` (19 chars) and `+12% attack speed.` (18). Three rows were shortened here first, exactly as manual step 2 invites (Bulwark 50 → 38, Consecrate 51 → 38, Unbowed 44 → 35), and **the count did not move**: character count is a poor predictor of wrapping — `+15% censer damage.` (19) takes two lines where `+15 maximum health.` (19) takes one. So *"a node that needs two lines is a node to redesign"* is unsatisfiable by any description that says what a node does, at this width. **The label's box is 176 × 150 dp — room for about eight lines — so two lines is not a layout failure there, it is normal.** What survives is GD §13.1's *real* rule, reading time, and by that measure the two worst were the two Actives: an Active has to state a condition *and* an outcome where a Passive states a number. Both are now inside a two-second read. **The owner is expected to rewrite rows; that is what a text asset is for.**

**Probed by calling in rather than by trusting a green compile.** `tableRows=63 localizerCount=63 agree=True`; a real key off `Consecrate.asset` → *"Consecrate"*; a miss → the key; `default` → empty and not null; a case-wrong key → the key back (ordinal, per `LocKey.Equals`); all 24 shipped node keys and all 3 branch keys resolve, printed with their words. **The container could not be built from a probe** — Traps §4's *"no VContainer reference"* — so `BootInstaller`'s registration is covered by `Boot_RegistersTheLocalizer` and `Boot_LocalizerOutlivesARun` in the suite instead, and `BootSmokeTests` passing 3/3 is the evidence that the dressed `BootScope.prefab` composes with a now-**required** table.

**The table is 63 rows where the spec says "about fifty"**: 24 node + 3 branch + 18 trigger + `ui.skills.empty` + `ui.tree.none` + `ui.hint.firstActive` + `ui.overflow.granted` + rule 8's five + the nine unwired. Registered **eagerly** with `RegisterInstance<ILocalizer>` rather than through a factory — `ContentCatalog`'s bargain rather than `ISaveStore`'s — so a duplicate key is a loud failure at boot naming the asset instead of surfacing on whichever screen asked for a word first. **It bumps no format**: a table is a `ScriptableObject` read at boot like every other `Data/` asset, so [ledger row 2](../ROADMAP.md#carry-forward-into-m3) does not fire and `PlayerProfile` stays at M3-09c's v2.

**Two of my own rows went red on the first full run and both were the test rather than the code.** `Menu_AndDeathOverlayDrawFromTheTable` searched the whole scene file and found `Descend` — as the **GameObject's name**; it now reads only `m_text:` values, because a well-named object is not a localisation failure and the first draft would have forced the scene to be renamed to pass. And `Card_DrawsEnglish` opened a level-up twice against two screens, so the second presenter never received `OfferPresented` and drew nothing; it now authors a word per node and asserts card 0 shows **its own** node's word, which is discriminating rather than merely non-empty.

**Ledger row 9 closes for six readers by name** — `OfferCard`, `SkillRow`, `TreeNodeView`, `ManualSkillButton`, `FirstActiveHint`, `OverflowToast` — **and stays open for the seventh**, `AutoCastRow`'s 24 dp cells, where the gap is an icon and it is M7's. `AutoCastRow_IsStillIconless` asserts the absence of a text component rather than an empty string, so the row cannot close by accident. **One of the six closes only on a phone**: `ManualSkillButton`'s 60 dp circle holds *"Bulwark"* plausibly and legibly is [row 4](../ROADMAP.md#carry-forward-into-m3)'s. **[Ledger row 2] does not fire** (above). **[Row 4] gains the two device questions manual steps 7–8 name.**

**1 940 EditMode / 0 / 0, three times** — twice consecutively on the final code, then once after PlayMode so the Console sweep had something to read — against M3-13b's 1 912: **+28, and the reconciliation is exact.** The spec's Tests table is 26 rows; 23 landed in `TableLocalizerTests`, and the 5 over are named: `Core_TakesNoLocalizer` (in `AssemblyPurityTests`), `Count_IsTheRowCount`, `Construct_RefusesANullTable`, `Table_IsLinkedToAMonoScript` (Traps §5, the only row that would catch the asset loading as null), and three miss-path rows the rule-1/rule-7 tension owes by name — `Card_MissingRowFallsBackToTheKey`, `Row_MissingTriggerRowKeepsTheNumber`, `Bar_MissingRowFallsBackToTheKey` — less `Boot_ScopeCarriesTheTable`'s and `AutoCastRow_IsStillIconless`'s homes being elsewhere. **The five inverted rows changed the count by zero, which is what makes them rewrites rather than deletions**: `Card_DrawsTheKeyNotEnglish` in *both* `LevelUpPresenterTests` and `SkillsPresenterTests`, `Tree_DrawsKeysNotEnglish`, `Bar_LabelsAreKeys` and the toast's key assertion inside `Overflow_ToastShowsTheTotal`. `RewrittenRows_StillAssertWhatTheyAsserted` copies M3-13a's shape so a *deleted* row fails rather than only a renamed one. **PlayMode 16 / 16, every row named and passed**, `Ticker_RunsTheStepsInOrder` among them — and PlayMode was genuinely in the blast radius this time, because `BootSmokeTests` boots the real `BootScope` and the table is now required. Console after the last EditMode run: **11 errors / 25 warnings, identical to baseline**, every entry a known family, zero `CS`/`UNT`/`IDE` diagnostics and **zero mentions of anything this task touched**. `dotnet format whitespace --verify-no-changes` clean on all 31 files. **`BootScope.prefab`, `Menu.unity` and `Hud.prefab` moved; `Run.unity` and `Enemy.prefab` did not.** `ProjectSettings/TimeManager.asset` dirtied and was reverted for the **seventeenth time in eighteen tasks**, the same `Fixed Timestep` rational re-serialisation at an identical 0.02 with `m_TimeScale` correctly 1.
