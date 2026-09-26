# RS-03g — The suite keeps the saves

**Size:** S · **Depends on:** — · **Branch:** `rs-03g-the-suite-keeps-the-saves`
**Design refs:** AR §10.3, §15; M2-13b; [RS-03f](RS-03f-the-descend-row.md) rule 6;
[M6-11c's *As built*](M6-11c-stage-one-without-the-m1-dummies.md#as-built) · **Ledger rows:** none

## Goal

A PlayMode run, full or filtered, plays against an empty save folder. It leaves `run.json` and
`profile.json` in `persistentDataPath` byte-identical to what was there before, present or absent.

## The diagnosis

`BootInstaller` registers `new LocalJsonSaveStore(Application.persistentDataPath)` at the root. The
PlayMode runner builds that root on its own first scene, before any test body runs
([Traps §7](../../Traps.md)). So every PlayMode run reads the machine's real profile and run through
`BootFlow`, and four rows write the real `run.json`: `BootSmokeTests`' Descend row and
`RunBodyTests`' three each start a run, and that run's `SaveWriter` writes a snapshot. After each
of RS-03f's passes with a save, `run.json` held a fresh Oathbound run in place of the owner's
Ranger. The other three fixtures write nothing: `FrameOrderTests` builds its writers over an
`InertSaveStore`, the sandbox's scope registers no writer, and `PoolingLifecycleTests` builds no
scope.

**Why the files move rather than the store.** A fixture cannot swap boot's `ISaveStore`, because the
root exists before its first line runs. A run-scope shadow, `ResumeFlowTests`' route, would cover
`SaveWriter` but not the root's `ProfileStore`, and every row would have to opt in around its load.
A directory the test runner could redirect would put the tests' concern in `BootInstaller`. The
test framework runs `IPrebuildSetup` before it enters Play and `IPostBuildCleanup` after it leaves,
the second even when the run fails (`PostbuildCleanupTask.RunOnError = RunAlways`). No store exists
at either moment, so the files can be moved with nothing holding them.

**Why on every fixture, not on the assembly.** The framework reads an assembly-level
`[PrebuildSetup]` only from an assembly whose compiled references include `UnityEditor.TestRunner`
(`AttributeFinderBase.GetTypesFromPrebuildAttributes`). `Soulvail.Tests.PlayMode` references
`UnityEngine.TestRunner` alone, so an assembly attribute would compile and do nothing.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Tests/PlayMode/Support/SaveShelter.cs` | Tests.PlayMode | Moves the two saves aside before Play and puts them back after |
| `Tests/PlayMode/SaveShelterTests.cs` | Tests.PlayMode | The shelter over a temporary folder, and the row that every fixture carries it |
| *small edits* | | `BootSmokeTests`, `RunBodyTests` (and its remark on the parking lot), `FrameOrderTests`, `PoolingLifecycleTests` and `RangerSandboxTests` each gain the two attributes (rule 1) |
| *docs* | | this spec; `PROGRESS.md`; `ROADMAP.md`, where RS-03g is ticked and linked; `Traps.md` §7, for the assembly attribute that does nothing |

No production code.

## Public API

```csharp
namespace Soulvail.Tests.PlayMode.Support;

public sealed class SaveShelter : IPrebuildSetup, IPostBuildCleanup
{
    public const string FolderName = "PlayModeShelter";

    public SaveShelter();                 // over Application.persistentDataPath: the runner's
    public SaveShelter(string directory); // over a temporary folder: a row's

    public void Setup();
    public void Cleanup();
}
```

## Behaviour

1. **Every PlayMode fixture carries `[PrebuildSetup(typeof(SaveShelter))]` and
   `[PostBuildCleanup(typeof(SaveShelter))]`.** A run that selects any PlayMode row, full or
   filtered, shelters once before Play and restores once after: the framework runs each type once
   per run.
2. **`Setup` leaves a fresh install.** Each of the store's two files that exists is moved into
   `<directory>/PlayModeShelter/`, and each that does not is recorded there by an empty
   `<name>.absent` marker. Afterwards the folder holds neither file, so on every machine the suite
   plays with no Continue and the default profile.
3. **`Cleanup` puts back what was there.** A file the shelter holds replaces whatever the run wrote
   in its place. A file marked absent is deleted if the run wrote one. Then the shelter is removed.
   Both files are byte-identical to the moment before `Setup`, present or absent: a move within one
   folder is a rename, and the bytes are never read or rewritten.
4. **`Cleanup` touches only what `Setup` recorded.** With no shelter it does nothing. A file the
   shelter neither holds nor marks is left where it is: `Setup` stopped before reaching it, so it is
   still the player's.
5. **A shelter left behind is restored first.** `Setup` that finds the folder already there means
   an earlier run's `Cleanup` never ran, because the Editor was killed mid-pass. It warns once,
   restores that shelter by rule 3, then shelters again. The folder holds the files as they were
   before that run, and what stands in their place is that run's.
6. **The directory is taken, not read**, for `LocalJsonSaveStore`'s reason: the rows run over a
   temporary folder, never `persistentDataPath`. The parameterless constructor, which the runner
   calls, reads `Application.persistentDataPath`. A null or blank directory throws
   `ArgumentException`.
7. **After a full PlayMode pass, the owner's two files are byte-identical**, with a `run.json` on
   disk and without one, measured by SHA-256 before and after each pass.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `SaveShelterTests.EveryFixture_SheltersTheSaves` | every type in `Soulvail.Tests.PlayMode` that declares a `[Test]` or `[UnityTest]` / its attributes read / both name `SaveShelter` (rule 1) |
| `SaveShelterTests.Setup_LeavesAFreshInstall` | both saves / `Setup` / neither file in the folder, and a `LocalJsonSaveStore` over it loads no profile and no run (rule 2) |
| `SaveShelterTests.Cleanup_PutsTheSavesBackByteForByte` | both saves, `Setup`, the run writes both / `Cleanup` / each file's bytes are the originals, and no shelter (rule 3) |
| `SaveShelterTests.Cleanup_RemovesARunWrittenWhereThereWasNone` | a profile alone, `Setup`, the run writes both / `Cleanup` / no `run.json`, and the profile's bytes are the original (rule 3) |
| `SaveShelterTests.Cleanup_WithoutAShelter_TouchesNothing` | both saves, no `Setup` / `Cleanup` / both unchanged (rule 4) |
| `SaveShelterTests.Cleanup_LeavesAFileSetupNeverReached` | a shelter holding the run alone, the profile still in place / `Cleanup` / the run restored, the profile untouched (rule 4) |
| `SaveShelterTests.Setup_RestoresAShelterLeftBehind` | `Setup`, the run writes both, no `Cleanup` / a second shelter's `Setup`, then its `Cleanup` / one warning, and both files' bytes are the originals (rule 5) |
| `SaveShelterTests.Constructor_NullOrBlankDirectory_Throws` | null, `""`, `"   "` / constructed / `ArgumentException` (rule 6) |
| *the suite, twice* | the owner's files, then no `run.json` / a full PlayMode pass / both hashes unchanged each time (rule 7) |

**Red check.** With the attributes taken off every fixture and recompiled, a full pass replaces the
owner's `run.json`, which is RS-03f's observation made on purpose. The owner's files are backed up
under `Logs/` first and restored from there.

## Out of scope

- **A PlayMode row for Continue, end to end.** A row can now write its own `run.json` into a
  sheltered folder, which RS-03f named as the missing piece. It is a new row with its own question
  → [parking lot](../ROADMAP.md#parking-lot).
- **Recovery at Editor launch.** A killed pass leaves the saves in the shelter until the next PlayMode
  run, so a Play pressed meanwhile sees a fresh install. Rule 5's warning names the folder, and an
  `[InitializeOnLoad]` would need an Editor assembly this project does not have.
- **EditMode.** No EditMode row writes `persistentDataPath`: `InstallerTests` resolves boot's store
  and writes nothing, and every save-store row takes a temporary folder.
- **`TestResults.xml`**, which Unity writes beside the saves on every `TestRunnerApi` run
  ([Traps §4](../../Traps.md)), and a stray `.tmp`, which no read looks at. Neither is a save.

## As built

**Rules 1–7 as written, and no production code.** `SaveShelter` moves each save into
`PlayModeShelter/` or leaves a `.absent` marker, and moves it back over whatever the run wrote. All
six PlayMode fixtures carry both attributes, `SaveShelterTests` included, since rule 1 counts it as a
fixture. The guard finds fixtures by any method attribute that is NUnit's `ISimpleTestBuilder` or
`ITestBuilder`, so `[TestCase]` counts. Its control is that the scan finds `BootSmokeTests` and
`RunBodyTests`, a floor rather than a total ([Traps §7](../../Traps.md)).

**Rule 7: every pass left the owner's files byte-identical, and every pass without the shelter did
not.** The owner's files are a stage-1 Ranger `run.json` and a v4 `profile.json`, backed up under
`Logs/` first. They were hashed with SHA-256 before and after each pass, and their mtimes did not
move.

| Pass | Tree | `run.json` before | Unity active | PlayMode | The two files after |
|---|---|---|---|---|---|
| A | this | the owner's | no | 66 / 1: the sandbox row | identical |
| B | this | none | no | 65 / 2: the sandbox row, an AI Assistant warning | identical, `run.json` still absent |
| bisect | `dev` | the owner's | no | 54 / 3: the sandbox row, two AI Assistant warnings | `run.json` replaced by a Ranger run |
| red check | this, attributes off | the owner's | no | 65 / 2: the guard row, the sandbox row | `run.json` replaced |
| C, D | this | the owner's | no | 66 / 1 each: the sandbox row | identical |
| E | this | the owner's | at queue and finish | **67 / 67** | identical |
| F | this | none | at queue and finish | 66 / 1: the sandbox row | identical, `run.json` still absent |

Mid-run, pass A's folder held the suite's own `run.json`, with both originals in the shelter. Pass B's
shelter held `profile.json` and `run.json.absent`. No pass wrote a `profile.json`.

**Red check.** With the twelve attribute lines deleted and recompiled, the guard failed on
*"BootSmokeTests does not carry [PrebuildSetup(typeof(SaveShelter))]"*. No shelter appeared mid-run,
and the owner's `run.json` was replaced, as it also was on `dev`. The six files were restored from a
copy under `Logs/` and compared byte for byte, and the owner's `run.json` from its backup.

**Finding: the sandbox row still fails passes it should not, and an active Editor is not a cure.**
`RangerSandboxTests.Loop_ShootsOnlyStandingStill` failed *"No arrow on the run"* in all six unfocused
passes, `dev`'s included, which is the bisect's answer: not this change. The shell could not keep
Unity in front, because BlueStacks App Player took the foreground back within 2 s. A command that
waits in `EditorApplication.update` for `isApplicationActive`, while the owner clicked into Unity,
queued E and F. E passed, and F failed on this row, although it read active at the queue and at
the finish. The shelter gives the suite the same empty folder in both, so the save cannot be the
difference → [Traps §8](../../Traps.md).

**Finding: an AI Assistant token warning fails whichever row is running.** *"Token refresh timed out
after 30 seconds"* is a `Warning` from `Unity.AI.Toolkit`, and `LogAssert` failed
`FrameOrderTests.Frame_PausedTicksCostNoSimulatedTime` on it in B, and two rows on `dev`'s pass. It
is the family of PROGRESS's Known issue 5, which names *NoSubscription*.

**Deviations: 2.** *1.* `Traps.md` §8 joined §7 during the build, for the focus finding. *2.* The
ROADMAP's parking lot gains the Continue row that *Out of scope* names.

**Rules ↔ rows.** 1: `EveryFixture_SheltersTheSaves`. 2: `Setup_LeavesAFreshInstall`. 3:
`Cleanup_PutsTheSavesBackByteForByte`, `Cleanup_RemovesARunWrittenWhereThereWasNone`. 4:
`Cleanup_WithoutAShelter_TouchesNothing`, `Cleanup_LeavesAFileSetupNeverReached`. 5:
`Setup_RestoresAShelterLeftBehind`. 6: `Constructor_NullOrBlankDirectory_Throws`, three cases. 7:
the table.

**How it was checked.** EditMode 3 270, 3 269 / 0 / 1 inconclusive, the animator clock row by
construction ([Traps §3](../../Traps.md)). The Console straight after it held 46 entries, 16 errors
and 30 warnings, each from a passing negative-path row, which is RS-03f's count. Unity's compiler
with the analyzers reported nothing on the first compile. `dotnet format` is clean over the seven C#
files, and `TimeManager.asset` was reverted.

**Not done here:** nothing in the game changed, so there is no manual step. Play in the Editor still
reads and writes the real saves. Only a test run is sheltered.
