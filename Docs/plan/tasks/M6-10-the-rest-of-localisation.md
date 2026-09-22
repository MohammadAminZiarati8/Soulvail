# M6-10 — The rest of localisation: a second table, a sweep that finds raw strings, and the language nobody has written

**Size:** M · **Depends on:** M6-09a · **Branch:** `m6-10-localisation`
**Design refs:** GD §16.1, §18, §19; AR §6, §11.5; ADR-0012; M3-14a, M3-14b, M3-14c · **Ledger rows:** [7](../ROADMAP.md#carry-forward-into-m6) — **discharged here**, and rule 1 is why it discharges as something other than what it asked for

## Goal

`ILocalizer` learns to pick a table, to substitute inside a sentence and to format a number the
reader's way; a sweep finds every string that never became a key; and a pseudo-locale proves all
three without anybody writing a word of a second language.

## Ledger row 7 asks for three things and one of them has no author, which the counting is what found

The row: *"What remains is **locale selection**, **the other languages' tables** and **every screen
M3 did not build**."* Taken in order, and grepped rather than inherited:

**(i) Locale selection is a field and a picker, and only the field is M6's.**
[M6-09a](M6-09a-profile-v4-and-what-a-shard-buys.md) shipped `PlayerProfile.Locale` for exactly this,
and the *picker* is a row on an options screen — GD §18's list, which is **M8-02**'s whole task.
Building a one-row options screen here would be M8-02 arriving early and unspecified. What ships is
the field being read, the device's locale as the default, and the picker named.

**(ii) The other languages' tables cannot ship, because no document names a language.** Swept:
GD §18's options list has **no language row**; GD §19's V1 scope has **no localisation line**; GD
§21's open questions has none; ADR-0012 says *"adding a language is adding a table"* and stops. **So
"the other languages" is a title with no design, no author and no list.** Three ways forward were
weighed:

- **Guess a list** — say English, German, French, Spanish, Portuguese-BR, because that is what a
  mobile action game usually ships. That is a **business decision** (GD §21.1's *"business model
  decision, needed before M6"* is still a [parking-lot](../ROADMAP.md#parking-lot) line) and it is
  ~177 rows × *n* of translation with no translator, which is content this project has no way to
  produce or to check.
- **Ship a half-filled table** — the failure `ILocalizer.Get`'s own remarks already tolerate
  (*"a half-translated table is the normal state rather than an error"*) and the failure
  [M5-08a](M5-08a-splash-offers-what-install-refuses.md) closed at the screen: offering a language
  that is 40 % English.
- **Ship the mechanism and a pseudo-locale.** Rule 3.

**(iii) Every screen M3 did not build is the part that is real, and it is bigger than a list of
screens.** Every task since M3-14c has shipped its own English rows and its own
`Strings_EveryKeyThisScreenDrawsHasARow`, so the *rows* are not missing — `English.asset` carries
**112** today and M6's merged and specced tasks add **65** more:

| Source | Rows |
|---|---|
| [M6-03a](M6-03a-the-sanctum-screen.md) the Sanctum | 14 |
| [M6-03b](M6-03b-the-meter-on-the-right-edge.md) the two readouts | 3 |
| [M6-05a](M6-05a-what-a-pact-is.md) / [b](M6-05b-the-offer-that-rolls-one.md) Pacts | 4 + 2 |
| [M6-06a](M6-06a-what-an-ordeal-is.md) / [b](M6-06b-four-ordeals-and-two-refusals.md) Ordeals | 8 |
| [M6-07a](M6-07a-the-emberwright-and-the-cinder-orb.md) the Emberwright | 2 |
| [M6-08](M6-08-emberwright-tree-v1.md) its tree, its branches, and the splash refusal | 27 + 1 |
| [M6-09b](M6-09b-a-class-you-cannot-pick-yet.md) the unlock screen | 4 |
| **Total added in M6** | **65** |

**So the row's 31 is now 65, and the row's own framing was the wrong one:** it says *"the sweep is the
cheap part — the tables are not"*, and the truth is the reverse. The tables have no author and the
**sweep is the only thing here that can find a defect**, because a key that was never written is
invisible to every per-task row: `Strings_EveryKeyThisScreenDrawsHasARow` proves the keys a screen
*has* resolve, and says nothing about a `SetText("Continue")` sitting beside them. Rule 2 is that
sweep and rule 3 is the second one that catches what it cannot see.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ports/ILocalizer.cs` | Core | A second member, and AR §6's row corrected rather than obeyed — again (rule 4) |
| `Game/Adapters/TableLocalizer.cs` | Game | **Substantial.** Many tables, one locale, a fallback chain, and the reader's number format |
| `Tests/Game/Adapters/TableLocalizerTests.cs` | Tests.Game | **Substantial.** The pick, the fallback, the substitution, and the culture |
| `Tests/Game/LocalisationSweepTests.cs` | Tests.Game | The two sweeps — rules 2 and 3 |
| `Data/Localisation/Pseudo.asset` | — | Every English row, accented and padded. **Generated, never hand-written** (rule 3) |
| *small edits* | Game | `Game/Authoring/LocalizationTable.cs` — `_locale` and its culture; `Game/Composition/BootInstaller.cs` — the tables array and the device default; `Game/Composition/BootFlow.cs` — the profile's locale, once (rule 6); `Prefabs/Composition/BootScope.prefab` — `_localizationTables`; the callers rule 5 moves to `Format`; `Data/Localisation/English.asset` — whatever rule 2 finds |
| *ripple* | Tests.Game | `BootInstallerTests` and `BootFlowTests` gain the pick; the fixtures of every caller rule 5 moves |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Ports;

public interface ILocalizer
{
    /// <summary>The string for <paramref name="key"/>, or the key's own text. Unchanged.</summary>
    string Get(LocKey key);

    /// <summary>
    /// The string for <paramref name="key"/> with <paramref name="args"/> substituted into it, in
    /// the reader's number format.
    /// </summary>
    /// <remarks>
    /// <b>A second member rather than an overload of <see cref="Get"/>, and AR §6's row is corrected
    /// to say so</b> (rule 4). That row promises *"M6-10 adds the overload"*; an overload would put a
    /// <c>params object[]</c> allocation one accidental argument away from <c>HudPresenter</c> and
    /// <c>AutoCastRow</c>, which is the exact cost M3-14a rule 5 cut the <c>params</c> for. A
    /// differently-named member cannot be reached by accident, and a row asserts the frame-adjacent
    /// callers do not reach it.
    /// </remarks>
    /// <returns>
    /// Never null and never a throw — <see cref="Get"/>'s contract. A row with the wrong number of
    /// placeholders returns the **unsubstituted** row rather than throwing, because a
    /// <c>FormatException</c> out of a translator's typo is a screen that crashes in one language
    /// and works in another.
    /// </returns>
    string Format(LocKey key, params object[] args);
}
```

```csharp
// Game/Adapters/TableLocalizer.cs
namespace Soulvail.Game.Adapters
{
    public sealed class TableLocalizer : ILocalizer
    {
        /// <param name="tables">
        /// Every shipped table, in no particular order. The first whose <c>Locale</c> is empty is
        /// the fallback, and there must be exactly one — rule 1.
        /// </param>
        /// <param name="locale">Which to read, or empty for the fallback.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="tables"/> is empty, holds a null, has no fallback or has two.
        /// </exception>
        public TableLocalizer(IReadOnlyList<LocalizationTable> tables, string locale);

        /// <summary>Which table is being read. Empty means the fallback.</summary>
        public string Locale { get; }

        /// <summary>What a number is formatted with — the locale's, not the invariant one (rule 5).</summary>
        public CultureInfo Culture { get; }

        /// <summary>
        /// Switches tables. **Legal only before the first screen draws** — rule 6.
        /// </summary>
        /// <remarks>
        /// Nothing redraws. Every label in this project is written in <c>Start</c> (M3-14c rule 3),
        /// so a locale changed after the Menu scene loads is a screen in two languages. <b>M8-02's
        /// options row is the task that has to solve that</b>, and it is named here rather than
        /// discovered there. <c>BootFlow</c> is the only caller and a row says so.
        /// </remarks>
        public void SetLocale(string locale);

        /// <summary>Whether the **current** table has a row. Unchanged in meaning.</summary>
        public bool Has(LocKey key);

        /// <summary>How many rows the current table holds.</summary>
        public int Count { get; }
    }
}
```

```csharp
// Game/Authoring/LocalizationTable.cs — two fields.
public sealed class LocalizationTable : ScriptableObject
{
    /// <summary>A BCP-47 tag — <c>"de"</c>, <c>"qps-ploc"</c> — or empty for the fallback.</summary>
    public string Locale { get; }

    /// <summary>
    /// What this language formats numbers with. Resolved from <see cref="Locale"/> once, and
    /// <see cref="CultureInfo.InvariantCulture"/> when the tag names no culture Unity knows.
    /// </summary>
    public CultureInfo Culture { get; }
}
```

## Behaviour

1. **English is the fallback and it is spelled as an empty locale rather than as `"en"`.** A miss in
   the current table falls through to the fallback table, and a miss in *that* returns the key's own
   text — `ILocalizer.Get`'s existing contract, now with one rung in between. **Exactly one table may
   be the fallback and the constructor refuses two**, because *"which English do we show"* is a
   question with no answer and a silently-picked one is the worst kind. The chain is two deep and no
   deeper: a language-then-region chain (`de-AT` → `de` → fallback) is a feature nothing has asked
   for, and adding it later costs one loop.
2. **The first sweep is over the assembly and it is the row ledger 7 is actually about.**
   `LocalisationSweepTests` walks every type in `typeof(Palette).Assembly`, reflects every
   `LocKey`-typed field and every `new LocKey("…")` in IL — `PaletteTests.Reads`' technique, which
   already walks IL for `ldsfld` — and asserts **every key the game can name has an English row**.
   That is strictly stronger than the per-task rows it does not replace: those prove *a screen's*
   keys resolve, and this one proves there is no key anywhere that nobody wrote a row for. Grepped,
   `ILocalizer.Get` has **21 call sites across 15 files** and every one of them is in
   `Soulvail.Game`, which `AssemblyPurityTests.Core_TakesNoLocalizer` already guarantees — so one
   assembly is the whole surface.
3. **The second sweep is a pseudo-locale, and it is the only thing in this project that can see a
   raw string on a prefab.** `qps-ploc` — the industry's pseudo-locale tag — carries **every** English
   key with its text accented and padded by about 35 %: *"Continue"* becomes *"[Çôñtîñûé ···]"*. It
   buys three things at once, and the third is why it is here rather than in M8:
   - **It proves the fallback chain**, because a key missing from it falls back visibly.
   - **It measures layout expansion.** German and Finnish run 30–40 % longer than English, and every
     label in this game was authored against an English word. A screen that fits at 35 % padding fits
     in German; one that does not is a defect found now rather than after a translator is paid.
   - **It is a raw-string detector that works on prefabs.** Rule 2's sweep sees code and cannot see
     `LevelUp.prefab`'s TMP components. **Under the pseudo-locale, anything still in plain English on
     screen is a string that never became a key** — which is the one defect ADR-0012 exists to
     prevent and the one nothing in this project has ever been able to look for.
   **It is generated, never hand-written**: a `RunCommand` through `CreateInstance` and
   `SerializedProperty` (M3-12c's recipe, M5-02's *As built*), so regenerating it after English grows
   is one command. `Pseudo_HasARowForEveryEnglishRow` is what fails the day somebody forgets.
4. **`Format` is a second member and AR §6's row is corrected rather than obeyed — for the second
   time on the same row.** M3-14a rule 5 cut the `params` from `Get` because *"a `params object[]`
   overload allocates an array on every call, and these are reached from `HudPresenter` and
   `AutoCastRow`, which run near the frame"*, and named M6-10 as where it returns. It returns as
   `Format`, because an *overload* of `Get` is one accidental extra argument away from those same
   callers and the compiler would not object. `Localizer_TheFrameAdjacentCallersDoNotFormat` is the
   row.
5. **Formatting moves inside the localiser, and `CultureInfo.InvariantCulture` becomes a defect the
   moment a second table exists.** Today a number in a sentence is formatted beside the key by the
   caller — M3-09b rule 2's ruling, *"core says what kind of number it is; the screen formats it"* —
   and grepped, that is **4 `string.Format` sites across 2 files** (`ClassCard`, `SplashPresenter`)
   plus **31 `InvariantCulture` references** in `Soulvail.Game`. Two things are wrong with it in a
   translated build and neither is visible in English:
   - **Word order.** *"or reach stage 20"* is one sentence in English and puts the number elsewhere
     in German. A caller that concatenates cannot move it; a `Format` row can.
   - **The separator.** `InvariantCulture` renders `3.4 m/s`, and a German reader expects `3,4`.
     `InvariantCulture` is the right answer for a log and the wrong one for a player.
   So `Format` uses `TableLocalizer.Culture`, and **only the callers that put a number inside a
   player-facing sentence move** — the sweep is what says which of the 31 are those and which are log
   text. M3-09b rule 2's ruling survives intact: core still says what *kind* of number it is, and the
   screen still decides; what changes is that the screen asks the localiser to write it.
6. **The locale is read from the profile exactly once, in `BootFlow`, and nothing changes it after.**
   `BootInstaller` registers a `TableLocalizer` on the **device's** locale
   (`Application.systemLanguage`), because the container is built before `ISaveStore.LoadProfile`'s
   `Task` has answered; `BootFlow` then calls `SetLocale(profile.Locale)` if it is non-empty, before
   the Menu scene loads and therefore before any label exists. **Every label in this project is
   written in `Start`** (M3-14c rule 3), so a locale changed later is a screen in two languages — the
   cost is stated here and **M8-02** is the task that has to solve it, because it is the task that
   ships the picker. `Boot_OnlyBootFlowSetsTheLocale` is a reflection row over callers, so the day a
   second caller appears it is a diff rather than a bug report.
7. **A profile naming a locale this build does not ship falls back and says nothing.** A player who
   picked German and then took a build that dropped it gets English rather than a boot failure —
   `SkillRunner.Restore`'s rule for an id the build no longer stocks, and the same reason: the
   alternative is a save that refuses to load because a table was renamed. The profile is **not**
   rewritten, so the locale comes back if the table does.
8. **Nothing in `Soulvail.Core` changes except a port gaining a member**, and
   `AssemblyPurityTests.Core_TakesNoLocalizer` stays green: no core class takes an `ILocalizer`, and
   `Format` is one more thing core does not call. ADR-0012's whole shape, unmoved.
9. **Nothing here allocates on a frame.** `Get` is a dictionary probe on one table plus at most one
   more; `Format` allocates its `params` array and its result, and rule 4's row is what keeps it away
   from the callers that run near the frame.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Table_CarriesItsLocale` | `English.asset` and `Pseudo.asset` / loaded / `""` and `"qps-ploc"` |
| `Localizer_RefusesTwoFallbacks` | two tables with an empty locale / — / throws, naming both assets — rule 1 |
| `Localizer_RefusesNoFallback` | every table with a locale / — / throws — rule 1 |
| `Localizer_RefusesAnEmptyOrNullTableSet` | empty, and a set holding null / — / throws |
| `Localizer_ReadsThePickedTable` | both tables, `"qps-ploc"` / `Get(ui.menu.title)` / the pseudo text |
| `Localizer_FallsBackARung` | a key present only in English, locale `"qps-ploc"` / `Get` / the English text — rule 1 |
| `Localizer_FallsBackToTheKey` | a key in neither / `Get` / the key's own text — `ILocalizer`'s existing contract |
| `Localizer_AnUnknownLocaleIsTheFallback` | locale `"de"` with no German table / — / English, and **no throw** — rule 7 |
| `Localizer_TheChainIsTwoDeep` | `typeof(TableLocalizer)` / reflection or a three-table set / no language-then-region step — rule 1 |
| `Format_Substitutes` | a row reading `"or reach stage {0}"` / `Format(key, 20)` / *"or reach stage 20"* |
| `Format_UsesTheLocalesCulture` | a table whose culture is `de-DE` / `Format(key, 3.4f)` / *"3,4"*, not *"3.4"* — rule 5 |
| `Format_FallsBackToTheCultureItKnows` | a locale naming no culture / — / `InvariantCulture`, no throw |
| `Format_AWrongPlaceholderCountDoesNotThrow` | a row with `{0} {1}` and one argument / — / the **unsubstituted** row, and nothing thrown — the Public API's stated contract |
| `Format_AMissingRowStillSubstitutesNothing` | a key with no row / `Format` / the key's own text |
| `Localizer_TheFrameAdjacentCallersDoNotFormat` | `HudPresenter` and `AutoCastRow` / IL / neither calls `Format` — rule 4 |
| `Port_HasTwoMembers` | *(replaces `Port_HasOneMember`)* `typeof(ILocalizer)` / reflection / `Get` and `Format`, and **no `params` overload of `Get`** — rule 4 |
| `Sweep_EveryKeyInTheAssemblyHasAnEnglishRow` | every `LocKey` field and literal in `Soulvail.Game` / against `English.asset` / every one resolves — rule 2, and the row ledger 7 is about |
| `Sweep_TheSweepWouldHaveCaughtAnUnwrittenKey` | a deliberately unwritten key in a fixture type / the sweep / it is found — rule 2's own diagnosis asserted, `Palette_TheSweepWouldHaveCaughtM4_06`'s shape |
| `Sweep_EveryAuthoredAssetKeyIsAlreadyCovered` | the sweep's set against `ContentValidationTests.EveryLocKey_ResolvesInEnglish`'s / — / neither is a subset of the other, and together they cover code **and** assets — rule 2, so neither row is deleted by mistake |
| `Pseudo_HasARowForEveryEnglishRow` | both tables / — / key-for-key equal sets, and the count matches — rule 3 |
| `Pseudo_IsLongerThanEnglish` | every pair / — / every pseudo row is at least 30 % longer — rule 3 |
| `Pseudo_IsNotEnglish` | every pair / — / no row is character-identical — rule 3, the raw-string detector's premise |
| `Boot_StartsOnTheDeviceLocale` | a container built with no profile / — / `Locale` is `Application.systemLanguage`'s tag, or empty — rule 6 |
| `Boot_TheProfilesLocaleWins` | a profile naming `"qps-ploc"` / `BootFlow` / `Locale` is `"qps-ploc"` before the Menu scene loads — rule 6 |
| `Boot_AnEmptyProfileLocaleLeavesTheDevices` | `Locale` `""` / `BootFlow` / unchanged — rule 6 |
| `Boot_OnlyBootFlowSetsTheLocale` | every type in `Soulvail.Game` / IL / one caller of `SetLocale` — rule 6 |
| `Core_TakesNoLocalizer` | *(existing)* `Soulvail.Core` / — / still green with `Format` on the port — rule 8 |
| `Localizer_GetAllocatesNothing` | 100 000 `Get` calls / `AllocationAssert.None` / zero — rule 9 |

**Guard rows are implied, not listed:** nulls to the constructor and to both members, a
`default(LocKey)` answering empty, and `LocalizationTable.ToDictionary`'s existing refusals firing
unchanged.

## Manual verification (Editor / device)

1. **[Editor]** Edit `profile.json`'s `locale` to `qps-ploc` and boot. *Expected: **every** word in
   the game is bracketed and accented. Anything still in plain English is a raw string — rule 3, and
   this is the only step in the project that can find one.*
2. **[Editor]** Walk the whole game in the pseudo-locale: menu, class select, run, level-up, tree,
   pause, Sanctum, run-end. *Expected: nothing clipped, nothing ellipsised, no label overflowing its
   box at 35 % longer than English. Write down what does — that list is M8-02's layout work and it is
   the real product of this task.*
3. **[Editor]** Set `locale` to `de` and boot. *Expected: English, silently — rule 7.*
4. **[Editor]** Add a row to `English.asset` and run the suite. *Expected:
   `Pseudo_HasARowForEveryEnglishRow` goes red until the generator is re-run — rule 3.*
5. **[device]** **[ledger row 1](../ROADMAP.md#carry-forward-into-m6)**, one row and it is new:
   step 2's expansion check at 400 dpi rather than at the Editor's 120 ([Traps §9](../../Traps.md)).
   A label that fits in the Editor at 35 % padding and clips on a phone is the failure mode this
   whole task exists to find early, and the Editor is the wrong instrument for the last third of it.

## Out of scope

- **Any actual second language.** Rule 1(ii) above, with the three options weighed. **The owner's
  ruling** or **M8-06**, which is the task that decides what a store listing says and is therefore
  the first that has to know which languages ship. A [parking-lot](../ROADMAP.md#parking-lot) line
  records it beside GD §21.1's business-model question, which is the decision it waits on.
- **A language picker.** GD §18's options list, **M8-02** — and rule 6 states the redraw problem that
  task inherits.
- **Plural rules, gender, right-to-left.** `ILocalizer.Get`'s remarks list them and none has a
  consumer: no shipped string pluralises, and RTL is a layout change to every screen rather than a
  table. Promoted by the language list, whenever it exists.
- **Retranslating what English says.** The 177 rows are the strings each task authored to GD §13.1's
  two-second budget; rewriting them is an editorial pass with no task.
- **Fixing what step 2 finds.** A label that clips at 35 % is a **layout** defect, and the standing
  ruling since the `m3` tag is that UI layout belongs to neither M4 nor M5 nor M6 —
  [ledger row 3](../ROADMAP.md#carry-forward-into-m6). This task produces the list; **M8-02** spends
  it. A clip found here becomes a ledger row, not a late edit.
- **Localising the debug overlay.** It is a developer readout and ADR-0012 is about user-facing
  strings; `LocalisationSweepTests` skips it by name, with the reason in the skip.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
