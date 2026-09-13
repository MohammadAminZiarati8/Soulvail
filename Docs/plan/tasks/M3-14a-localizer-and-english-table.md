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

_Filled at merge._
