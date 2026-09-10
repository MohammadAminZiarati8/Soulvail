# M2-13a — `ISaveStore`, the DTOs, and the stream position that makes a resume honest

**Size:** M · **Depends on:** M2-01 (`IClock` stamps the snapshot), M2-02 (`RunConfig`'s five fields are what the DTO mirrors) · **Branch:** `m2-13a-save-port-and-dtos`
**Design refs:** AR §6, §10.3, §11.4, §11.6, §18.2, §18.3; ADR-0007, ADR-0011 · **Ledger rows:** 1 — the half that makes a stream's position sayable at all; **M2-14a** captures it and **M2-14b** restores it

## Goal

Core can describe a run completely enough to rebuild it — including where every random stream had got to — in two versioned value types behind one async port, with not a byte of file I/O anywhere in this task.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ports/ISaveStore.cs` | Core | The port, async from day one (ADR-0007) |
| `Core/Save/SaveDtos.cs` | Core | `RunSnapshot` + `PlayerProfile`, grouped — `EnemyEvents.cs`' precedent |
| `Tests/Core/Save/SaveDtoTests.cs` | Tests.Core | Versions, guards, and what `default` means |
| `Tests/Core/Fakes/InMemorySaveStore.cs` | Tests.Core | The store every later task tests against |
| `Tests/Core/Fakes/InMemorySaveStoreTests.cs` | Tests.Core | The fake's own rules — `FixedRandomTests` and `FixedClockTests`' precedent |
| *small edits* | | `IRandom` + `RandomState`, `Capture()` and `Restore(in RandomState)` (rule 5); `SeededRandom` implements both and its private `Pcg32` gains a state accessor; `FixedRandom` implements both; **AR §10.3**'s `ISaveStore` block — the two `Load` signatures are annotated as `Nullable<T>` rather than as nullable references (rule 2) |
| *ripple* | | any test fake implementing `IRandom` — today only `FixedRandom` — gains two members. The compiler enumerates them |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Ports;

/// Where every stream's generator had got to at one instant. Position only: the run's seed is
/// what selects each stream's *sequence*, and it is carried separately (rule 6).
public readonly struct RandomState
{
    public RandomState(ulong spawn, ulong offers, ulong affixes, ulong drops, ulong misc);

    public ulong Spawn { get; }
    public ulong Offers { get; }
    public ulong Affixes { get; }
    public ulong Drops { get; }
    public ulong Misc { get; }
}

// IRandom (added)
/// Where every stream stands right now. Allocates nothing.
RandomState Capture();

/// Puts every stream back where `state` says it was. The seed is not restored — it is a
/// constructor argument and already agrees, which M2-14b's guard is what checks.
void Restore(in RandomState state);
```

```csharp
namespace Soulvail.Core.Save;

/// A run, written to disk at a stage boundary and read back after an app kill. GD §7.3.
/// **Not `WorldSnapshot`** — that is the per-frame block of facts Unity hands core, lives in
/// `Core/Run`, and has nothing to do with this. The names are AR §10.3's and are kept.
public readonly struct RunSnapshot
{
    public const int CurrentVersion = 1;

    public RunSnapshot(
        int version,
        ContentId modeId,
        ContentId characterId,
        int seed,
        int stageIndex,
        RandomState random,
        float playerHp,
        float playerShield,
        float runTime,
        DateTimeOffset writtenAt);

    public int Version { get; }
    public ContentId ModeId { get; }
    public ContentId CharacterId { get; }
    public int Seed { get; }

    /// The stage this run resumes *at* — the one the player had not started yet (M2-14a rule 2).
    public int StageIndex { get; }

    public RandomState Random { get; }
    public float PlayerHp { get; }
    public float PlayerShield { get; }

    /// Simulated seconds, `RunState.Time`. Never wall-clock — see `WrittenAt` for that.
    public float RunTime { get; }

    /// `IClock.UtcNow` at the write. The only wall-clock number in the run (M2-01 rule 1).
    public DateTimeOffset WrittenAt { get; }
}

/// What survives every run. Settings today; Shards and unlocks when they exist (rule 4).
public readonly struct PlayerProfile
{
    public const int CurrentVersion = 1;

    public PlayerProfile(int version, bool hapticsEnabled);

    public int Version { get; }
    public bool HapticsEnabled { get; }

    /// A profile for a player who has never had one: current version, GD §16.3's defaults.
    public static PlayerProfile Default { get; }
}
```

```csharp
namespace Soulvail.Core.Ports;

/// Persistence. Core defines the DTOs and this port; the adapter owns the medium (AR §10.3).
public interface ISaveStore
{
    Task<PlayerProfile?> LoadProfile();
    Task SaveProfile(PlayerProfile profile);
    Task<RunSnapshot?> LoadRun();
    Task SaveRun(RunSnapshot run);
    Task ClearRun();
}
```

```csharp
namespace Soulvail.Tests.Core.Fakes;

/// A store that answers without a disk. `FixedRandom` and `FixedClock`'s shelf (rule 12).
public sealed class InMemorySaveStore : ISaveStore
{
    public int RunWriteCount { get; }
    public int ProfileWriteCount { get; }
    public int ClearCount { get; }

    /// Makes the next write fault its *task*, which is how real I/O fails (rule 13).
    public void FailNextWrite();
}
```

## Behaviour

**The port**

1. **Async signatures, synchronous first adapter** — ADR-0007, unchanged. Nothing here awaits anything; the point is that the day `SyncingSaveStore` wraps the local one, not one signature moves.
2. **Both DTOs are `readonly struct`s, and that is what makes AR §10.3's signatures compile as written.** The project does not enable nullable reference types anywhere — no `#nullable`, nothing in `.editorconfig` — so `Task<RunSnapshot?>` is only legal as `Nullable<RunSnapshot>`. Reading it as a nullable *reference* would need the feature switched on for one file, which is a language-mode decision this task has no business making on the strength of two return types. AR §10.3's block is annotated rather than changed: the signatures were already right, only ambiguous.
3. **No parameter is taken by `in`, deliberately, despite both DTOs being structs.** An `async` method cannot have a by-ref parameter, so `SaveRun(in RunSnapshot)` compiles today only because the local adapter is synchronous and would refuse the first implementation that reaches for `async` — which is exactly the implementation ADR-0007 says is coming. Two 80-byte copies a stage cost nothing; a port that cannot be implemented asynchronously costs a signature change on a shipped save format. `IRandom.Restore` *is* `in`, because it is a hot-path core call and no async implementation of it is imaginable.

**The DTOs**

4. **`PlayerProfile` v1 is one setting, and Shards are not in it.** ADR-0007 names Shards and unlocks and they are M4-06 and M6-08; a field written to disk at v1 that nothing reads is a field every later migration has to carry for ever. AR §6's rule — *a port grows a member when the mechanic that needs it lands* — reads the same way for a save format, and this is the version where obeying it is free. `HapticsEnabled` is here because ledger row 11 has a consumer today (M2-13b).
5. **`RandomState` is position, and the seed is identity — a resume needs both.** Each `Pcg32` derives its increment, which selects which of 2^63 sequences it walks, from `(seed, streamIndex)` through SplitMix64; the increment is `readonly` and is reproduced exactly by constructing `SeededRandom` with the same seed. So restoring the five state words on top of a correctly-seeded generator is a *complete* restore, and neither half is sufficient alone. `RunSnapshot` carries both, and M2-14b is where their agreement is checked.
6. **`RandomState` names its five streams rather than holding an array**, because core allocates nothing and a `ulong[5]` would allocate on every capture. The consequence is real and is the version field's job: a sixth stream (AR §18.3 — *a new stream takes the next free index*) is a format change with a migration, which is what ADR-0007 exists to make survivable.
7. **`Capture` and `Restore` are on `IRandom`, not on `IRandomStream`** — the owner's ruling, against ledger row 1's own literal wording of `ulong State { get; set; }` per stream. A settable position on `IRandomStream` would be reachable from every core system that draws, which is the *"a live object is never handed out"* family AR §18.2 guards: any behaviour holding a stream could rewind it, and a determinism bug caused that way looks like a content bug for a week. Two members on the port that composition already owns is one door instead of five, and no core system's vocabulary changes at all.
8. **`Restore` refuses nothing.** It cannot tell a state captured under a different seed from a legitimate one — every value is a legal generator position. The agreement between seed and state is checkable exactly where both are visible, which is `RunSession.Start`, where M2-02 rule 5 already throws on a seed the generator disagrees with. Guarding here would be a check that cannot fail correctly; guarding there is the check that already exists.
9. **`default(RunSnapshot)` has `Version` 0, and that closes AR §18.3's both-ends problem for free.** A struct with an invariant needs the check at both ends because `default` skips the constructor — and here the zeroed form is self-identifying: `CurrentVersion` starts at 1, so version 0 is the value no writer can produce and every reader already has to refuse (M2-13b rule 5). No `IsValid` member, and no second concept.
10. **The constructors guard what a *reader* can be handed.** `version < 1`, `stageIndex < 1`, a negative or non-finite `playerHp` or `playerShield`, a negative or non-finite `runTime` all throw — these are values that arrive from a file written by an older build or edited by hand, so unlike `RunState`'s constructor (AR §18.2, guards deliberately absent because it is reachable only from core) this one defends a real boundary. `default(ContentId)` is *not* refused: a mode id that resolves to nothing is content validation's answer to give, at `RunSession.Start`, with the diagnostic M2-02 rule 7 already writes.
11. `RunTime` is simulated seconds and `WrittenAt` is wall-clock, and they are never compared. M2-01 rule 1 says why; the DTO carrying both under names that cannot be confused is this task's whole contribution to keeping it true.

**The fake**

12. **`InMemorySaveStore` is a `Tests.Core` fake, not an adapter** — it lives beside `FixedRandom` and `FixedClock` because M2-14a and M2-14b both need a store that answers without touching the disk, and a test that writes to `persistentDataPath` is a test that fails differently on the machine that runs it second. It holds one profile and one run, returns completed tasks, and counts its calls so a test can assert *how many times* something was written.
13. **It can be told to fail**, because M2-14a rule 7's "a failed write must not end a run" needs a way to fail one. `FailNextWrite()` throws from the returned task rather than from the call, which is how a real I/O failure arrives and is the one thing a naive fake gets wrong.

## Tests

| Test | Given / When / Then |
|---|---|
| `Snapshot_RecordsEveryField` | every argument distinct / ctor / each property reads back |
| `Snapshot_VersionBelowOne_Throws` | version 0 / ctor / throws (rule 10) |
| `Snapshot_StageBelowOne_Throws` | stage 0 / ctor / throws |
| `Snapshot_NegativeHp_Throws` | hp −1 / ctor / throws |
| `Snapshot_NonFiniteHp_Throws` | hp NaN, then ∞ / ctor / throws both (AR §18.3's `!(v >= 0f)` spelling) |
| `Snapshot_NegativeRunTime_Throws` | runTime −1 / ctor / throws |
| `Snapshot_DefaultIsVersionZero` | `default(RunSnapshot)` / read `Version` / 0 — the value no writer can produce (rule 9) |
| `Snapshot_DefaultContentIdIsAllowed` | `default(ContentId)` as the mode / ctor / no throw (rule 10) |
| `Snapshot_RunTimeAndWrittenAtAreIndependent` | a clock 3 days on and a run time of 5 s / ctor / both read back unchanged, neither derived from the other (rule 11) |
| `Store_EveryMemberIsAsync` | reflection over `typeof(ISaveStore)` / — / every method returns `Task` or `Task<T>` (rule 1) |
| `Dtos_AreValueTypes` | reflection over `RunSnapshot` and `PlayerProfile` / — / both `IsValueType`, so AR §10.3's `Task<T?>` is `Nullable<T>` and needs no nullable-reference switch (rule 2) |
| `Profile_Default_IsCurrentVersionAndHapticsOn` | `PlayerProfile.Default` / — / version 1, haptics true (GD §16.3, rule 4) |
| `Profile_VersionBelowOne_Throws` | version 0 / ctor / throws |
| `Profile_HasNoShards` | reflection over `typeof(PlayerProfile)` / — / two properties, `Version` and `HapticsEnabled` — rule 4 pinned rather than remembered |
| `RandomState_RecordsFiveStreams` | five distinct words / ctor / each reads back in stream-index order |
| `Capture_Restore_ReplaysTheSameDraws` | seed 7, 100 draws, `Capture`, 100 more recorded, `Restore`, 100 again / — / identical to the recorded hundred (rule 5) |
| `Capture_CoversEveryStream` | draw from all five, capture, draw more on all five, restore / draw once per stream / all five match (rule 5) |
| `Restore_IsCompleteAcrossAFreshGenerator` | draw 500 on seed 7, capture; a **new** `SeededRandom(7)`, `Restore` / draw / the same values as the original's 501st — the increment came back with the seed (rule 5) |
| `Restore_UnderADifferentSeed_DoesNotThrow` | state from seed 7 restored onto seed 8 / `Restore` / no throw, values differ (rule 8) |
| `Capture_AllocatesNothing` | warm-up / 10 000 × `Capture` / allocated-bytes delta == 0 |
| `Restore_AllocatesNothing` | warm-up / 10 000 × `Restore` / allocated-bytes delta == 0 |
| `Random_StreamHasNoSettableState` | reflection over `typeof(IRandomStream)` / — / four members, none named `State` (rule 7) |
| `Store_HasNoByRefParameter` | reflection over `typeof(ISaveStore)` methods / — / no parameter `IsByRefLike` or `ParameterType.IsByRef` (rule 3) |
| `Fake_LoadBeforeSave_IsNull` | a fresh `InMemorySaveStore` / `LoadRun` / `null`, no throw (rule 12) |
| `Fake_RoundTripsARun` | `SaveRun(x)` / `LoadRun` / `x`, field for field |
| `Fake_SaveReplaces` | two saves / `LoadRun` / the second |
| `Fake_ClearRemovesTheRunNotTheProfile` | both saved / `ClearRun` / `LoadRun` null, `LoadProfile` unchanged |
| `Fake_CountsWrites` | three saves / — / `RunWriteCount` 3 (rule 12) |
| `Fake_FailNextWrite_FaultsTheTask` | `FailNextWrite()` / `SaveRun` / the call returns; awaiting the task throws; the next save succeeds (rule 13) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

None. Nothing is visible, nothing renders, nothing is registered in a container yet, and no file is written — the honest check is that six assemblies still compile, which the suite makes.

## Out of scope

- **Any file, any path, any JSON** — M2-13b. This task deliberately ends at the port, so that the review of *what a run is* is separate from the review of *how it is spelled on disk*.
- **Registering `ISaveStore`** — M2-13b, with the adapter that would be registered.
- **Migrations** — M2-13b. There is nothing to migrate at v1; what that task builds is the harness and the refusal.
- **Anyone calling any of this** — M2-14a writes, M2-14b reads. Nothing in this PR has a consumer, which is the same shape M2-01 shipped in and for the same reason.
- **Level, XP, the tree, Essence, Shards, unlocks** — M3, M4-06, M6. Rule 4 is the decision not to reserve fields for them.
- **Live enemies, waves and projectiles in the snapshot.** The write point has none by construction (M2-10 rule 10 clears both systems at the boundary), and the owner's ruling at M2-00e is that a resume restarts the stage whole. M2-14a rule 6 lists what is left out and why.

## As built

_Filled at merge. Deviations from the above with their reasons, or "as specified". This footer owns the deviations; the PROGRESS entry only counts them and links here._
