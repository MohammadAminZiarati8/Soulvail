# M2-13b — `LocalJsonSaveStore`: format v1 on disk, an atomic write, and the migration harness

**Size:** M · **Depends on:** M2-13a · **Branch:** `m2-13b-local-json-save-store`
**Design refs:** AR §10.3, §11.6, §13; ADR-0007; GD §16.3; Traps §1, §10 · **Ledger rows:** 11 — `HapticsSettings` moves off `PlayerPrefs` and becomes the port's first consumer, and the row leaves here

## Goal

The two DTOs have a medium: a JSON file under `persistentDataPath` that is written atomically, refuses a version it does not understand instead of guessing, and is pinned by a fixture that no serialiser generated.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/LocalJsonSaveStore.cs` | Game | The adapter, its two `[Serializable]` mirrors (nested, rule 2), and the atomic write |
| `Core/Save/SaveMigrations.cs` | Core | The version gate and the chain — pure functions, no state (rule 6) |
| `Tests/Core/Save/SaveMigrationTests.cs` | Tests.Core | The gate, and the chain test that is the file's whole reason to exist at v1 |
| `Tests/Game/Adapters/LocalJsonSaveStoreTests.cs` | Tests.Game | Round trip, the fixtures, missing, corrupt, atomic |
| `Tests/Game/Adapters/HapticsSettingsTests.cs` | Tests.Game | Row 11: the toggle over a store rather than over the registry |
| *small edits* | | `HapticsSettings` — `FromStore`/`Apply` replace `FromPlayerPrefs`, `InMemory` unchanged (rule 8); `BootInstaller` registers `LocalJsonSaveStore` as `ISaveStore` singleton and builds `HapticsSettings` from it; `BootFlow` loads the profile before it leaves Boot (rule 9); `InstallerTests`' haptics rows stop reading the registry |
| *ripple* | | `HapticsListenerTests` keeps `InMemory` and is untouched — which is the check that rule 8's "keeps its shape" claim is true |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Game.Adapters;

/// ADR-0007's first adapter: one JSON file per DTO under `Application.persistentDataPath`.
/// Synchronous inside an async signature, deliberately (rule 3).
public sealed class LocalJsonSaveStore : ISaveStore
{
    public const string RunFileName = "run.json";
    public const string ProfileFileName = "profile.json";

    /// `directory` is `Application.persistentDataPath` in the container, and a temp folder in a
    /// test. Taking it rather than reading it is what makes this testable at all.
    public LocalJsonSaveStore(string directory);
}
```

```csharp
namespace Soulvail.Core.Save;

/// Whether a file this build did not write can still be read, and what it becomes. Pure
/// functions with no state — ADR-0007's "migrations are pure core functions" read literally.
public static class SaveMigrations
{
    /// The oldest format on disk this build still understands.
    public const int OldestSupportedRunVersion = 1;
    public const int OldestSupportedProfileVersion = 1;

    /// True when there is an unbroken chain of steps from `version` to `CurrentVersion`.
    /// False for 0, for anything older than supported, and for anything **newer** (rule 5).
    public static bool CanReadRun(int version);
    public static bool CanReadProfile(int version);

    /// Brings a decoded run of `version` up to `RunSnapshot.CurrentVersion`. Identity at current.
    /// <exception cref="NotSupportedException">`CanReadRun(version)` is false.</exception>
    public static RunSnapshot MigrateRun(int version, in RunSnapshot decoded);
    public static PlayerProfile MigrateProfile(int version, in PlayerProfile decoded);
}
```

```csharp
namespace Soulvail.Game.Adapters;

// HapticsSettings (changed)
/// The real one: starts at `profile`'s value and writes every later change through `store`.
public static HapticsSettings FromStore(ISaveStore store);

/// Adopts a profile that arrived after construction, **without writing it back** (rule 9).
public void Apply(in PlayerProfile profile);

// FromPlayerPrefs and PrefsKey are deleted. InMemory(bool) is unchanged.
```

## Behaviour

**On disk**

1. **Two files, not one.** A profile outlives every run and a run snapshot is deleted on death; one file would mean rewriting settings at every stage boundary and losing them to a corrupt run. `ClearRun` deletes `run.json` and never touches `profile.json`, which M2-13a's fake already asserts from the other side.
2. **`JsonUtility` over `[Serializable]` mirror types nested in the adapter, and the mirrors are the on-disk format.** Core's DTOs cannot be serialised directly: they are `readonly struct`s with properties, and `JsonUtility` sees only fields — and `ContentId` is a struct whose `Value` is a property, so it would silently write `{}`. The mirrors hold `string` ids and primitives, and the mapping between them and the DTOs is the whole of what "the adapter owns the medium" means (AR §10.3). Nested private types rather than five files, on `SeededRandom.Pcg32`'s precedent.
   **Rejected: `com.unity.nuget.newtonsoft-json`.** It is the obvious answer and it is a package, which CLAUDE.md's *Ask before* covers; a mirror struct per DTO is thirty lines and adds no dependency to a shipped save format.
3. **Async signatures, a synchronous body, and no thread.** `Task.CompletedTask` and `Task.FromResult`, exactly as ADR-0007 describes. A background write would race the next boundary's write for the same file, and the thing it would buy — not blocking a frame — is a sub-millisecond local write on a frame that is already swapping an arena. When the store is a network one, that adapter brings its own concurrency and this one is untouched.
4. **The write is atomic: `<name>.tmp` first, then `File.Move(tmp, target, overwrite: true)`.** Android kills backgrounded apps mid-anything (GD §7.3), and a half-written `run.json` is worse than no `run.json` — it is a run the player watched being saved and then lost. Move is a rename within one directory, so the file either has the old content or the new one. A stray `.tmp` left by a kill between the two calls is overwritten by the next write and ignored by every read.
5. **A version this build does not understand is refused, and a run it cannot read is deleted.** The order on load: read the file → decode the mirror → if `CanRead` is false, or decoding threw, or the JSON is malformed, **delete the file and return null**. Three reasons, one behaviour, and the behaviour is *"there is no save"* rather than an exception into the boot path: a player whose save is unreadable has lost the run either way, and the difference between the two designs is whether the app also refuses to start. **Newer than current is refused with the same answer** — a downgraded build must not read a v2 file as though the fields it does not know are absent. The Editor gets a `Debug.LogError` naming the file and the version, because in the Editor an unreadable save is nearly always a bug in the format rather than a corrupt phone.

**Migrations**

6. **`SaveMigrations` is `static`, and that is not a violation of the no-statics rule.** What ADR-0002 and CLAUDE.md ban is static *mutable* state and service location — reachable-from-anywhere handles that make composition a lie. This class has no fields, no state to reset under a disabled domain reload (CLAUDE.md's Unity section), and ADR-0007 specifies migrations as *"pure core functions"* in as many words. Called out because it will look like a violation at review and the answer should be in the spec rather than in a PR comment.
7. **At v1 the chain is empty, and the chain *test* is why the file ships now.** There is nothing to migrate, so `MigrateRun` is identity and the gate admits exactly one version. What exists is the shape and the assertion: for every version in `[OldestSupported, Current]`, `CanRead` is true and `Migrate` returns something at `CurrentVersion`. The day someone bumps `CurrentVersion` to 2 without writing a step, that test fails — which is the only mechanism in the project that makes AR §11.6's promise self-enforcing rather than remembered. **The migration signature takes and returns the current DTO**, so a version that only *adds* fields is decoded with documented defaults and fixed up by its step; a version that renames or retypes one needs a versioned mirror in the adapter and a wider signature, and that is a deliberate future change this shape does not pretend to have solved.

**Row 11**

8. **`HapticsSettings` keeps its shape and changes where it reads from** — which is what its own class comment has promised since M1-20, quoted here so the promise is checked rather than assumed. `FromStore(ISaveStore)` replaces `FromPlayerPrefs()`, `InMemory(bool)` is untouched, and the `Enabled` setter still skips a write when nothing changed and still writes immediately rather than at the next app pause. The write is fire-and-forget with the fault observed and logged: a setting that fails to persist must not throw out of a UI callback.
   **`PrefsKey` is deleted and nothing migrates it.** Nothing has shipped, so the population that would be carried over is the owner's own dev machine; a `PlayerPrefs` → profile migration would be a migration with no users and a permanent line in `SaveMigrations`.
9. **The profile is loaded once, at boot, before Boot hands off to the Menu.** `BootFlow` already awaits scene work and is the only place in the app with a legitimate reason to block on I/O. `HapticsSettings` is constructed at its default (on — GD §16.3 puts the burden on the player who wants silence) and `Apply` adopts the loaded profile without writing it back, because a load that immediately re-saves is a load that can corrupt what it just read. A missing profile is `PlayerProfile.Default` and is **not** written to disk on the spot: the first file appears when the player first changes something, and until then a fresh install has no profile, which is the truth.

## Tests

| Test | Given / When / Then |
|---|---|
| `Store_RoundTripsARun` | a snapshot with every field distinct / `SaveRun`, `LoadRun` / equal field for field, `ContentId`s included (rule 2) |
| `Store_RoundTripsMaxUlongStreamState` | `RandomState` of five `ulong.MaxValue` / save, load / all five exact — the numbers `JsonUtility` is least likely to honour, so they are asserted rather than assumed ([Traps §1](../../Traps.md), the family this is a textbook member of) |
| `Store_CompletesSynchronously` | a save and a load / — / the returned tasks are already `IsCompleted` on return; no continuation is needed to observe the result (rule 3) |
| `Store_RoundTripsTheTimestamp` | `WrittenAt` with a non-zero offset / save, load / the same instant in UTC |
| `Store_RoundTripsAProfile` | haptics false / save, load / false |
| `Store_LoadWithNoFile_IsNull` | an empty directory / `LoadRun`, `LoadProfile` / both null, no throw, nothing created |
| `Store_SaveReplaces` | two saves / load / the second |
| `Store_ClearDeletesTheRunFile` | a saved run and profile / `ClearRun` / `run.json` gone, `profile.json` present, `LoadProfile` unchanged (rule 1) |
| `Store_ClearWithNoFile_DoesNotThrow` | empty directory / `ClearRun` / no throw |
| `Store_WritesThroughATempFile` | a save / — / no `.tmp` remains in the directory afterwards (rule 4) |
| `Store_StrayTempFileIsIgnored` | a `run.json.tmp` of garbage beside a good `run.json` / `LoadRun` / the good run (rule 4) |
| `Store_CorruptJson_DeletesAndReturnsNull` | `run.json` = `"{ not json"` / `LoadRun` / null, the file is gone, one logged error (rule 5) |
| `Store_UnknownVersion_DeletesAndReturnsNull` | a v0 file / `LoadRun` / null, file gone (rules 5, 9 of M2-13a) |
| `Store_NewerVersion_DeletesAndReturnsNull` | a v99 file / `LoadRun` / null, file gone — a downgraded build refuses rather than guesses (rule 5) |
| `Store_CorruptProfile_DoesNotTakeTheRunWithIt` | a good run and a corrupt profile / both loads / the run survives (rule 1) |
| `Store_SaveIntoAnUnwritableDirectory_FaultsTheTask` | a directory made read-only / `SaveRun` / the returned task faults; the call itself does not throw (rule 3, and M2-14a rule 7's case) |
| **The fixtures — literal bytes, not a re-serialisation (rule 7)** | |
| `Fixture_V1Run_DecodesToTheExpectedSnapshot` | a `const string` of v1 JSON written by hand / write it to the directory, `LoadRun` / every field equals the documented value |
| `Fixture_V1Profile_DecodesToTheExpectedProfile` | as above / `LoadProfile` / haptics false, version 1 |
| `Fixture_V1Run_IsWhatThisBuildWrites` | the same snapshot / `SaveRun` / the file's text equals the fixture string — the row that fails the day the format drifts without the fixture moving with it |
| **Migrations** | |
| `Gate_AcceptsCurrent` | `CanReadRun(RunSnapshot.CurrentVersion)` / — / true |
| `Gate_RefusesZero` | `CanReadRun(0)` / — / false (rule 5) |
| `Gate_RefusesOlderThanSupported` | `CanReadRun(OldestSupportedRunVersion - 1)` / — / false |
| `Gate_RefusesNewerThanCurrent` | `CanReadRun(CurrentVersion + 1)` / — / false |
| `Migrations_HoldsNoState` | reflection over `typeof(SaveMigrations)` / — / every field is `const` or `static readonly`; no mutable state to reset under a disabled domain reload (rule 6) |
| `Chain_IsUnbrokenFromOldestToCurrent` | every version in `[Oldest, Current]` / `CanRead` then `Migrate` / true, and a result at `CurrentVersion` — **the row that fails when `CurrentVersion` is bumped without a step** (rule 7) |
| `Migrate_AtCurrent_IsIdentity` | a v1 snapshot / `MigrateRun(1, x)` / `x`, field for field |
| `Migrate_RefusedVersion_Throws` | `MigrateRun(0, x)` / — / `NotSupportedException` naming the version |
| `Profile_ChainAndGateMirrorTheRun` | — / — / the four rows above, for `PlayerProfile` |
| **Row 11** | |
| `Haptics_StartsFromTheLoadedProfile` | a profile with haptics false / `FromStore`, `Apply` / `Enabled` false |
| `Haptics_DefaultsOn` | no profile / `FromStore`, `Apply(PlayerProfile.Default)` / `Enabled` true (GD §16.3, rule 9) |
| `Haptics_ApplyDoesNotWrite` | a store counting writes / `Apply` / `ProfileWriteCount` 0 (rule 9) |
| `Haptics_SettingWrites` | `Enabled = false` / — / one profile write, `HapticsEnabled` false |
| `Haptics_SettingToTheSameValueDoesNotWrite` | already true / `Enabled = true` / no write (M1-20's rule, kept) |
| `Haptics_FailedWriteDoesNotThrow` | `FailNextWrite()` / `Enabled = false` / no throw out of the setter, `Enabled` is false, one logged error (rule 8) |
| `Haptics_InMemoryTouchesNoStore` | `InMemory(true)` / flip it twice / no store involved, no file (rule 8) |
| `Haptics_HasNoPrefsKey` | reflection over `typeof(HapticsSettings)` / — / no `PrefsKey`, no `FromPlayerPrefs` — row 11 pinned rather than remembered |
| `Container_ResolvesSaveStore` | a built `BootScope` / `Resolve<ISaveStore>()` / a `LocalJsonSaveStore`, same instance twice |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Play, quit, and look in `Application.persistentDataPath` (Console-log it once, or open the folder). Nothing is there — rule 9's "a fresh install has no profile" being true rather than assumed.
2. **[Editor]** Flip `HapticsSettings.Enabled` from a debug hook or the test, and look again: `profile.json` exists, is readable text, and says what you set. Delete it by hand and relaunch — the game starts with haptics on and does not complain.
3. **[Editor]** Corrupt `profile.json` by hand — delete a brace — and relaunch. The game starts, the Console names the file, and the file is gone. That is rule 5, and it is the whole reason a bad save cannot brick a launch.
4. **[device]** Whether `persistentDataPath` survives an app update and a force-stop on a real phone, and whether the atomic write holds under a kill-from-recents mid-save. Nothing in the Editor can answer either; deferred with the rest of the device list.

## Out of scope

- **Anything writing a `RunSnapshot`** — M2-14a. `run.json` is round-tripped here by tests and by nothing else in the running game.
- **The `Continue` button and the resume path** — M2-14b.
- **`SyncingSaveStore` and a server** — ADR-0007 says it wraps this one and core never learns; nothing here anticipates it beyond the async signatures M2-13a already fixed.
- **Encryption, obfuscation or tamper-proofing.** A single-player roguelite's save is the player's to edit; the day a leaderboard makes that matter (GD §14.3), it is a server-authority problem and not a file-format one.
- **An options screen to flip haptics from** — M8-02. Row 11 is about where the value is stored, not about who sets it.
- **Migrating the `PlayerPrefs` key** — rule 8. Nothing has shipped.

## As built

**Eight deviations, three that change a decision.** Every behaviour rule and every test row shipped as written except where noted.

**The three that change a decision**

1. **Rule 4's `File.Move(tmp, target, overwrite: true)` does not compile in this project, and the atomic write is spelled with two branches instead.** `CS1501: No overload for method 'Move' takes 3 arguments` — verified in `Soulvail.Game` itself and not only in the MCP's scratch assembly, by temporarily putting the call in `Rename` and reading the Console. `File.Replace(tmp, target, null)` replaces an existing file and throws `FileNotFoundException` when there is none, so the rename is `File.Replace` when the target exists and the two-argument `File.Move` when it does not. **Both branches are still a rename within one directory, which is the only property rule 4 actually depends on**, and the test rows are unchanged. Filed in [Traps §5](../../Traps.md) — the setting reads as .NET Standard and the overload is part of .NET Standard 2.1, so this will look like a mistake to the next person who tries it.
2. **Rule 5's "the Editor gets a `Debug.LogError`" is logged unconditionally rather than behind `#if UNITY_EDITOR`.** The rule's reason is about why the level is *error* rather than *warning*; making the log Editor-only would leave the one code path that can lose a run silent on the only platform where losing one matters, and would leave the device branch untested. On a phone this line is what tells a bug report apart from *"the game forgot my run"*.
3. **`FromStore` does not read the store, and the profile arrives through `Apply`.** The Public API sketch says *"starts at `profile`'s value"* while rule 9 says the settings are constructed at the default and `Apply` adopts the loaded profile — the two cannot both be true of a factory whose only parameter is a store. Resolved towards **Behaviour**, per the spec's own precedence rule: a factory that loaded would have to block on I/O or return before the value it promised had arrived. `FromStore` starts at `PlayerProfile.Default.HapticsEnabled`, `BootFlow` loads once and calls `Apply`. Recorded on [ledger row 11](../ROADMAP.md#carry-forward-into-m2) as the thing the row did not say.

**The five that do not**

4. **Eight files rather than five**, all of them accounted for: the five in the Files table, plus the three *small edits* the table itself lists (`HapticsSettings`, `BootInstaller`+`BootFlow`, `InstallerTests`), plus **[Traps.md](../../Traps.md)**, which is not in the table. Two durable toolchain findings were filed there under PROGRESS's own rule that a lesson about the toolchain does not go in a log entry: deviation 1, and the `JsonUtility` fidelity probe below.
5. **The `JsonUtility` behaviour the format rests on was probed before the format was written, not assumed** ([Traps §1](../../Traps.md)'s rule applied deliberately). `ulong.MaxValue` round-trips to the digit and a `float` round-trips exactly, so the five stream positions are plain `ulong` fields rather than the decimal strings that would otherwise have been the safe choice — which is why `Store_RoundTripsMaxUlongStreamState` passes against a readable file rather than a quoted one. `DateTimeOffset` is *not* serialisable and is a round-trip `"o"` string in invariant culture, normalised to UTC on write; `Store_RoundTripsTheTimestamp` is what pins that.
6. **The mirrors use public fields, against AR §13's "never public fields".** `JsonUtility` takes a field's name as its JSON key, so private ones would put a leading underscore into every key of every file a player will ever own — and the mirrors *are* the on-disk format, not state. They are nested and private, so nothing outside the adapter can hold one; the precedent is `SeededRandom.Pcg32`. The five random positions are flattened into the run mirror rather than nested, which is what keeps the count at the spec's two mirrors.
7. **`Store_SaveIntoAnUnwritableDirectory_FaultsTheTask` makes the directory unwritable by putting a file where the directory should be**, not by setting the read-only attribute: on Windows a read-only *directory* does not refuse file creation, so the obvious spelling of this row would pass against an adapter with no error handling at all. What the row is for — the failure arriving at the `await` rather than out of the call — is asserted unchanged.
8. **Three rows beyond the table, all of them the template's implied guards:** `Constructor_NullDirectory_Throws` and `Constructor_BlankDirectory_Throws` on the new public constructor, and `FromStore_NullStore_Throws`. `Store_CorruptJson_DeletesAndReturnsNull` and its two version siblings each assert the logged error through `LogAssert`, which is what makes rule 5's diagnostic a tested behaviour rather than a comment.

**Two things worth knowing that are not deviations.** A read that fails for *I/O* reasons logs and returns null but **does not delete** — only the three decode-ish reasons in rule 5 do, because a locked file may be perfectly good next launch and deleting it would turn a transient fault into a lost run. And a `modeId` that does not parse becomes `default(ContentId)` rather than a decode failure, which is `RunSnapshot`'s own documented position from M2-13a: content this build no longer ships is content validation's answer to give at `RunSession.Start`, with the diagnostic that names the real problem.
