# RS-05c — Class select offers the Ranger alone

**Size:** M · **Depends on:** RS-03c (four cards, the Ranger among them) · **Branch:** `rs-05c-the-ranger-alone`
**Design refs:** CH §3, CH §5.4, GD §19 · **Ledger rows:** none

The owner's go of 2026-09-28: *"disable all characters except the Ranger"*, then, asked whether a
Ranger run should still borrow the other classes' branches: *"disable means just not show the
others."*

## Goal

Class select shows the Ranger alone, centred. The Oathbound, the Gravecaller and the Emberwright stay
in the game: a saved run of one still resumes, and a Ranger run can still borrow their branches.

## Why a list on BootScope, not a flag on each class

- **Which classes a build offers is a decision about the build.** `BootScope` already makes the
  like decision for places: its *Modes* order is what makes a new run a Jungle run (M7-05i). A
  *Class Select* list beside *Characters* is the same kind of setting in the same place.
- **The class assets stay what every class test reads.** ClassSelectPresenterTests, EmberwrightTests
  and RangerTests bind the shipped class assets to the four cards, to test locks, prices, deeds and
  binding. A per-class *hidden* flag would change what those assets say and move dozens of rows
  that are not about this. A roster the presenter is handed leaves them as they are.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Composition/ClassSelectRoster.cs` | Game | **New.** The ids shown, in order; empty shows every class (rule 1) |
| `Game/Presentation/ClassSelectPresenter.cs` | Game | binds the roster's classes; centres the row (rules 1, 2) |
| `Tests/Game/Composition/ClassSelectRosterTests.cs` | Tests.Game | **New.** Rules 1, 3, 4 |
| *small edits* | | `Game/Composition/BootScope.cs` (`_classSelect`), `BootInstaller.cs` (registers the roster), `RunCharacter.cs`, `RunTicker.cs`, `RunScope.cs` (rule 4); `Prefabs/Composition/BootScope.prefab` (the Ranger); `Tests/Game/Presentation/ClassSelectPresenterTests.cs` (two rows, and `BuildScreen` takes a roster); `Tests/PlayMode/RunBodyTests.cs` (one row re-pointed, see *As built*) |

## Public API

```csharp
public sealed class ClassSelectRoster
{
    public ClassSelectRoster(IReadOnlyList<ContentId> ids);          // refuses null, default, twice
    public bool ShowsEveryClass { get; }                              // the list is empty
    public IReadOnlyList<CharacterSpec> ShownFrom(ContentCatalog catalog);
}

// ClassSelectPresenter — optional and last, for the fixtures that build the screen by hand
[Inject] public void Construct(PendingRun, ContentCatalog, SceneLoader, ILocalizer, ProfileStore,
                               ClassSelectRoster roster = null);

// RunCharacter
public static ContentId Choose(PendingRun pending, ContentCatalog catalog, ClassSelectRoster roster = null);
```

## Behaviour

1. **Class select shows the roster's classes, in its order.** An empty roster, or none, shows every
   class in the catalog's order, which is every build before this task. A listed class the catalog
   does not hold is skipped.
2. **The cards shown are centred on the authored row, at the authored spacing.** With every card
   shown, each lands exactly where `ClassSelect.prefab` puts it. With one, it stands in the middle.
3. **The shipped roster is the Ranger alone.** All four classes stay in *Characters*.
4. **A run nobody chose plays the first class shown.** This is pressing Play with the Run scene open.
   A pending run still plays its own class, so a Continue of a hidden class's run resumes as that
   class.

## Tests

| Test | Given / When / Then |
|---|---|
| `ClassSelectRosterTests.Roster_EmptyShowsEveryClassInTheCatalogsOrder` | an empty roster / `ShownFrom` / the catalog, in order (rule 1) |
| `ClassSelectRosterTests.Roster_ShowsItsClassesInItsOwnOrder` | Ranger, Oathbound / `ShownFrom` / that order (rule 1) |
| `ClassSelectRosterTests.Roster_SkipsAClassTheCatalogDoesNotHold` | a class the catalog lacks, then the Ranger / `ShownFrom` / the Ranger (rule 1) |
| `ClassSelectRosterTests.Roster_RefusesAnEmptySlotOrAClassTwice` | null, a default id, a duplicate, a null catalog / built / thrown (rule 1) |
| `ClassSelectRosterTests.Boot_ClassSelectShowsTheRangerAlone` | `BootScope.prefab` / its lists / *Class Select* is the Ranger; *Characters* still four (rule 3) |
| `ClassSelectRosterTests.RunCharacter_FallsBackToTheFirstClassShown` | no pending run / chosen / the roster's first; the catalog's first with none (rule 4) |
| `ClassSelectRosterTests.RunCharacter_APendingRunStillWins` | a pending Gravecaller run, a Ranger roster / chosen / the Gravecaller (rule 4) |
| `ClassSelectPresenterTests.Select_ShowsTheRosterAloneAndCentresIt` | four classes, a Ranger roster / opened / the Ranger on card 0 at the row's centre, three hidden (rules 1, 2) |
| `ClassSelectPresenterTests.Select_EveryCardShownSitsWhereAuthored` | a two-class roster, then none / opened twice / roster order and centred; then all four where authored (rules 1, 2) |

## Manual verification (Editor / device)

1. **[Editor]** Boot → Menu → Descend. *Expected:* one card, the Ranger, in the middle of the
   screen. Choosing it starts a Jungle run.
2. **[Editor]** Play a Ranger run to half a tree. *Expected:* the splash still offers the other
   classes' branches.

## Out of scope

- Removing a class, its unlock price or its Shards deed. Hidden, a class cannot be bought, and
  nothing about its price changes.
- A layout for five or more classes. The capacity warning is unchanged.

## As built

**As specified.** The roster is registered inside `BootInstaller.Install` rather than by
`BootScope`, so every container the installer builds can resolve it, fixtures included. Only the
installer registers the catalog, so `RunScope`'s `Resolve<ClassSelectRoster>` beside
`Resolve<ContentCatalog>` cannot find one without the other.

**One PlayMode row pinned the old rule 4, and was re-pointed.** `RunBodyTests.Run_ADirectPlayWearsTheFirstClassesBody` asserted that a direct Play wears the catalog's first class, the Oathbound's Knight. It is now `Run_ADirectPlayWearsTheFirstShownClassesBody`: the catalog's first is still the Oathbound, class select's first is the Ranger, and the run wears the Ranger's body.

**The row is centred off the first and last cards.** The spacing is their gap divided by three and
the centre is their midpoint, so `ClassSelect.prefab` stays the one place the row is laid out.
Four cards land on −690, −230, 230 and 690 exactly, and one on 0. Each card's authored x is read
once, the first time the screen draws, so a narrow draw followed by a full one puts every card
back.

**Verified.** EditMode 3 431 — 3 430 passed, 0 failed, 1 inconclusive (the animator clock row) —
+9 on RS-05b's 3 422. Console: no errors, no warnings. PlayMode, run once over RS-05a to RS-05d, is in RS-05d's *As built*. Format check clean on the nine C# files.
