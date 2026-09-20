# M4-05b — `PlayerProfile` v3: the first thing a dead run leaves behind

**Size:** S · **Depends on:** M4-05a (`ShardsAwarded`), M3-09c (`ProfileStore`, the single writer), M2-13b (the chain and its fixtures) · **Branch:** `m4-05b-profile-v3-and-shard-writer`
**Design refs:** GD §14.1, §14.2, §14.3; AR §10.3, §11.6, §18.2, §18.3; ADR-0007 · **Ledger rows:** **[5](../ROADMAP.md#carry-forward-into-m4)** — the bump the row was opened for, in its own PR, with the field, its migration step and its fixture together

## Goal

`PlayerProfile` becomes v3 with the one number a dead run is worth, the v2 → v3 step ships in the same PR as the field, and a player who dies at stage 12 and kills the app still has 220 Shards tomorrow.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Adapters/ShardWriter.cs` | Game | hears `ShardsAwarded`, adds it to the live profile through `ProfileStore`, once |
| `Tests/Game/Adapters/ShardWriterTests.cs` | Tests.Game | the add, the once, the other field it must not erase, and the failed write that must not end a run |
| *small edits* | Core / Game | `Core/Save/SaveDtos.cs` — `PlayerProfile.CurrentVersion` 3, `Shards`, `WithShards` (rules 1, 2); `Core/Save/SaveMigrations.cs` — the `if (version < 3)` step below the `< 2` one (rule 3); `Game/Adapters/LocalJsonSaveStore.cs` — `ProfileMirror` gains `shards`; `Game/Composition/RunInstaller.cs` registers the writer (rule 6) |
| *tests* | Tests.Core / Tests.Game | `SaveDtoTests` (the field, the guard, the helper, `Default`), `SaveMigrationTests` (`CanReadProfile` to 3, the new step, the profile chain running **two** steps for the first time), `LocalJsonSaveStoreTests` (a v1 and a v2 literal each migrate; a v3 literal is what this build writes), `ProfileStoreTests` (the reflection row at the door — rule 2) |
| *ripple* | | every `new PlayerProfile(...)` site gains one argument — compiler-guided, M3-09c's fourteen sites again |

Only these files change. Anything else is a deviation: say so in *As built*. **`RunSnapshot.CurrentVersion` stays 3** — the two formats version independently (M2-13b), and nothing about a run's shape changed.

## Public API

```csharp
namespace Soulvail.Core.Save;

public readonly struct PlayerProfile
{
    public const int CurrentVersion = 3;

    public PlayerProfile(int version, bool hapticsEnabled, bool seenFirstActiveHint, int shards);

    /// <summary>Soul Shards banked across every run this install has ever finished. GD §14.1. v3.</summary>
    public int Shards { get; }

    /// <summary>This profile with <see cref="Shards"/> moved and nothing else touched.</summary>
    public PlayerProfile WithShards(int value);

    // Default => new PlayerProfile(CurrentVersion, hapticsEnabled: true,
    //                              seenFirstActiveHint: false, shards: 0);
}
```

```csharp
namespace Soulvail.Game.Adapters;

/// <summary>The one thing that banks a payout. Scoped to the run, writes to BootScope's store.</summary>
public sealed class ShardWriter : IDisposable
{
    public ShardWriter(ProfileStore profiles, DomainEventHub hub);
    public void Dispose();
}
```

## Behaviour

1. **v3 is one `int` and deliberately not two fields.** GD §14.1's third term needs a *set* of `ContentId`s and
   [M4-05a](M4-05a-shard-payout.md) rule 6 ruled it out of this milestone; GD §14.2's unlocks are M6-09's and
   are a second set. **`SaveDtos.cs`' own standing rule is honoured rather than waived:** *"no field ships
   before a consumer"* — and this one has two the day it lands, the writer below and
   [M4-06](M4-06-run-end-screen.md)'s screen. The remark in that file naming *"M4-07 and M6-09"* as the
   arrival is corrected in the same PR to name this task, so the file does not go on predicting a past.
2. **A writer that knows one field must never author the whole DTO** — M3-09c rule 3, and the reason
   `ProfileStore` exists at all. `ShardWriter` moves the number with `WithShards` and hands the profile back;
   it never calls the constructor. **`ProfileStoreTests`' reflection row gains this door**, so a future writer
   reaching for `new PlayerProfile(...)` fails a test rather than silently resetting a player's haptics.
3. **The field, its migration step and its fixture ship in this PR, together** — [ledger row 5](../ROADMAP.md#carry-forward-into-m4),
   unchanged since M2-13b and honoured at M3-01b, M3-03, M3-07b and M3-09c. The trap is adding the field and
   the migration separately: the first merges green, because an old file still decodes. **v2 → v3 writes
   `shards: 0` unconditionally rather than from what the adapter decoded**, on M3-01b rule 3's terms — a v2
   document was written by a build with no payout in it, so zero is the truth about that player rather than a
   default standing in for an unknown. A v2 document that somehow carried a number is still a v2 document.
4. **The profile chain runs two steps for the first time.** `MigrateProfile` has had exactly one `if` since
   M3-09c; `Chain_IsUnbrokenFromOldestToCurrent` and `ProfileChain` already loop from
   `OldestSupportedProfileVersion` to `CurrentVersion` and need **no text changed** — which is the claim row 5
   makes about the harness, and this is where it is collected. A v1 literal must walk **both** steps and
   arrive at v3 with a v3 number on it, which is `Migrate_V1_RunsBothStepsInOrder`'s shape on the other format.
5. **The write happens on `ShardsAwarded` and on nothing else.** Not `RunEnded`, which fires on every scope
   teardown ([M4-05a](M4-05a-shard-payout.md) rule 1); not `PlayerDied`, which carries no number. One event,
   one add, and a run that ends any other way banks nothing. **Adding rather than assigning**: the profile
   holds a lifetime total and the event carries one run's worth, so the writer reads `Current.Shards`, adds,
   and saves — which is also why it must be the only thing that does.
6. **It is a scoped service on `RunTicker`'s dependency chain, not a component** — `SaveWriter`'s shape and
   its argument (`SaveWriter.cs:22`): subscribing from a constructor on that chain is what guarantees the
   object is listening before core can publish, where an `OnEnable` or a `Start` is ordered against nothing.
   It resolves `ProfileStore` **from `BootScope`**, the way `FirstActiveHint` does (`RunScope.cs:414`): a
   profile outlives a run, and a store registered per run would forget the payout between the death and the
   menu. A run scope built against a container with no `ProfileStore` does not compose at all, loudly.
7. **A failed write is logged and swallowed** — `ProfileStore.Save` already does exactly this, and this task
   adds no second policy. The worst consequence available is a player losing one run's Shards to a full disk,
   which must not be allowed to throw out of an event callback on the frame the player died.
8. **Shards are persisted with nothing to spend them on, and that is ruled rather than defaulted.** GD §14.2's
   unlocks (Gravecaller 2 000, Emberwright 3 500) are M6-09's and the Sanctum is M6-02's, so this build banks
   a number no player can spend — which is `Palette.Veilrot`'s and `Palette.Essence`'s shape, *"ship the
   member, no reader"*. **The difference is that this one is on a save format, and the difference cuts the
   other way from the obvious reading.** An unread colour costs nothing to add later. **A Shard total not
   written is data destroyed**: the runs that earned it are gone, and a v4 at M6 could not pay anybody back
   for the deaths they already spent. GD §14.1's *"Dying must always pay something, or the loop breaks"* is a
   claim about a number that persists; a payout shown and forgotten is an actively dishonest readout. **So it
   ships.** The `SaveDtos.cs` rule it is measured against is *"no field before a consumer"*, not *"no field
   before a spender"*, and rule 1 shows two consumers.
9. **`Shards` is a non-negative `int` and the constructor refuses less.** GD §14.2's largest cost is 3 500;
   `int` is not close to tight, and a negative total is a hand-edited file rather than anything a writer can
   produce — which is exactly the class of input this constructor guards against and `RunState`'s deliberately
   does not (AR §18.2).

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Profile_CarriesShards` | `new PlayerProfile(3, true, false, 220)` / read / 220, and the other three unchanged (rule 1) |
| `Profile_RefusesNegativeShards` | −1 / constructed / `ArgumentOutOfRangeException` (rule 9) |
| `Profile_DefaultStartsAtZero` | `PlayerProfile.Default` / read / version 3, shards 0 (rules 1, 3) |
| `Profile_WithShardsKeepsEverythingElse` | haptics off, hint seen / `WithShards(50)` / both survive (rule 2) |
| `Profile_HasNoConstructorThatOmitsAField` | the type / reflected / exactly one public constructor, four parameters — M3-09c's row, one wider (rule 2) |
| `Migrate_V2ProfileGainsZeroShards` | a v2 profile with haptics off / migrated / v3, shards 0, haptics still off (rule 3) |
| `Migrate_V1ProfileRunsBothStepsInOrder` | a v1 profile / migrated / v3, hint false, shards 0 — both steps, in order (rule 4) |
| `Migrate_RefusesAVersionAboveCurrent` | version 4 / `CanReadProfile` / false, and `MigrateProfile` throws (rule 3) |
| `Chain_IsUnbrokenFromOldestToCurrent` | *existing row, unchanged text* / run / now loops over three versions (rule 4) |
| `Store_DecodesAV2ProfileLiteral` | a v2 JSON literal with no `shards` key / loaded / shards 0, not garbage (rules 3, 4) |
| `Store_WritesAV3ProfileLiteral` | a profile saved by this build / read back as text / carries `"version":3` and a `shards` key (rule 3) |
| `Writer_BanksThePayout` | profile at 100 / `ShardsAwarded(total: 220, …)` / `ProfileStore.Current.Shards` is 320 and the store was written (rule 5) |
| `Writer_AddsRatherThanAssigns` | profile at 100 / two awards of 40 / 180 (rule 5) |
| `Writer_IgnoresRunEnded` | a live writer / `RunEnded` published / nothing written — rule 5's refusal, `SaveWriter.Writer_IgnoresRunEnded`'s shape (rule 5) |
| `Writer_KeepsTheOtherFields` | haptics off, hint seen / an award / both survive the write (rule 2) |
| `Writer_SurvivesAFailedWrite` | a store that faults / an award / logged, not thrown, and `Current` still moved (rule 7) |
| `Writer_StopsAtDispose` | disposed / an award published / nothing written (rule 6) |
| `RunScope_ComposesWithTheWriter` | the run container built / resolved / one `ShardWriter`, and a container with no `ProfileStore` fails to build (rule 6) |

**Guard rows are implied, not listed:** null store, null hub, and the `ProfileMirror` round trip.

## Manual verification (Editor / device)

1. **[Editor]** Play, die at stage 1. *Expected: `profile.json` in the persistent data path carries
   `"version":3` and `"shards":10`.*
2. **[Editor]** Play again, die at stage 1 again. *Expected: `"shards":20` — added, not replaced (rule 5).*
3. **[Editor]** Toggle haptics off, then die. *Expected: the haptics flag is still off in the same file*
   (rule 2 — the erasure M3-09c existed to stop, checked from the new door).
4. **[Editor]** Hand-edit the file back to a v2 literal, relaunch. *Expected: it loads, shards read 0, and
   nothing throws* (rule 3).
5. **[device]** Kill from recents immediately after a death, then relaunch. *Expected: the payout is there.*
   The one **correctness** question on [row 3](../ROADMAP.md#carry-forward-into-m4)'s list, now with a second
   file behind it. **[device]**

## Out of scope

- **The screen that shows it.** [M4-06](M4-06-run-end-screen.md). This task banks a number; drawing it,
  naming it in English and deciding what happens to the death overlay are that task's.
- **The arithmetic.** [M4-05a](M4-05a-shard-payout.md). This task adds nothing to the formula and must not
  quietly acquire the third term on the way past.
- **A lifetime-total readout anywhere.** It belongs beside the thing that spends it — M5-07's class select or
  M6-02's Sanctum — and putting one on the run-end screen buys a subscription-order race for a number nobody
  can use (M4-06 rule 8).
- **`RunSnapshot`.** It stays v3. A run's shape did not change, and a boss counter that would have changed it
  was refused at [M4-05a](M4-05a-shard-payout.md) rule 3.
- **Unlocks, costs, the Sanctum, cosmetics** — GD §14.2, M6-02 and M6-09.
- **Cloud save.** GD §19's open question, decided before M5.

## As built

**Built as specified, with three deviations, all additive and all named below.** `PlayerProfile` is
v3 with `Shards`, `WithShards` and a non-negative guard; `MigrateProfile` has an
`if (version < 3)` step below the `< 2` one and writes `shards: 0` unconditionally; `ProfileMirror`
gained a `shards` key; `ShardWriter` hears `ShardsAwarded`, adds, and saves through `ProfileStore`.
**`RunSnapshot.CurrentVersion` stayed 3** — the two numbers are now equal and mean different things,
which is said out loud in `SaveMigrations`' remarks and in `ProfileGate_AcceptsOneToThreeRefusesFour`
rather than left to be discovered.

**Deviation 1 — `Game/Composition/RunTicker.cs` is a sixth file the Files table does not name, and
rule 6 does.** The table says only that `RunInstaller` registers the writer, but a VContainer
`Lifetime.Scoped` registration nobody resolves is **never constructed**: the writer would have been
registered, never built, never subscribed, and every run would have ended paying nothing with no
error anywhere. Rule 6 already required the fix in as many words — *"a scoped service on
`RunTicker`'s dependency chain … `SaveWriter`'s shape **and its argument**"* — so the constructor
gained `ShardWriter shardWriter`, discarded into `_` beside the `saveWriter` line that does the same
thing for the same reason. **Behaviour beats Files**, and the table is one file short rather than the
rule being wrong. It rippled to the project's three `new RunTicker(...)` sites
(`ResumeFlowTests`, `SkillBarPresenterTests`, `FrameOrderTests`), which is additive and changed no
assertion.

**Deviation 2 — `RunScope_ComposesWithTheWriter` lives in `Tests/Game/Composition/InstallerTests.cs`,
which the tests row does not name.** The row is a claim about the **two installers together** — the
writer is registered in the run scope and its store in `BootScope` — and `InstallerTests` is the only
fixture in the project that builds both. Putting it in `ShardWriterTests` would have meant either
duplicating `BuildBoot`'s asset loading or asserting against a container the test itself wired, which
tests the test. The negative half (a container with no `ProfileStore` refuses to compose the writer)
is built bare rather than through `RunInstaller`, so the throw names the missing dependency instead
of whichever of the run's other parents is looked up first.

**Deviation 3 — rule 2 names a row that has never existed.** It says *"`ProfileStoreTests`'
reflection row gains this door"*; there is no reflection row in that fixture. The door is
`SaveDtoTests.Profile_HasNoConstructorThatOmitsAField`, which is what the **Tests table** names, and
it went from three parameters to four. **Spec-versus-code, and the code won**, as the resolution rule
asks. `ProfileStoreTests` still changed, and meaningfully: `Store_CopiesThroughOneFieldAtATime` is
three writes rather than two and asserts the third field survives the other two, which is rule 2's
claim checked through the store rather than at the DTO.

**Three existing rows were renamed rather than joined by new ones, each following a precedent the run
format set at M3-07b.** `MigrateProfile_V2_IsIdentity` → `MigrateProfile_V3_IsIdentity` (identity is a
property of the current version and moves up with every bump); `MigrateProfile_V1_HasNotSeenTheHint`
→ `MigrateProfile_V1_RunsBothStepsInOrder` (`Migrate_V1_RunsBothStepsInOrder`'s shape, and the row
that proves rule 4); `ProfileGate_AcceptsOneAndTwoRefusesThree` →
`ProfileGate_AcceptsOneToThreeRefusesFour`, which also absorbed the Tests table's
`Migrate_RefusesAVersionAboveCurrent` — the gate refuses 4 **and** `MigrateProfile(4, …)` throws.
`Fixture_V2Profile_IsWhatThisBuildWrites` → `Fixture_V2Profile_DecodesToTheExpectedProfile` for the
same reason on the adapter side, with `Fixture_V3Profile_IsWhatThisBuildWrites` new beside it.
`SaveDtoTests.Profile_HasNoShards` → `Profile_HasNoUnlocks`: the row's subject is the property list,
and what it refuses moves on as each field arrives with the mechanic that owns it.

**Naming: the Tests table's row names are honoured where the row is new and the fixture has no
stronger convention, and the fixture's convention wins where it does.** `Store_DecodesAV2ProfileLiteral`
and `Store_WritesAV3ProfileLiteral` ship as `Fixture_V2Profile_DecodesToTheExpectedProfile` and
`Fixture_V3Profile_IsWhatThisBuildWrites`, because `LocalJsonSaveStoreTests`' class docstring
cross-references the `Fixture_*` family by name. `Migrate_V2ProfileGainsZeroShards` and
`Migrate_V1ProfileRunsBothStepsInOrder` ship as `MigrateProfile_V2_GainsNoShards` and
`MigrateProfile_V1_RunsBothStepsInOrder`, matching the `MigrateProfile_Vn_*` family beside them.
Every other row ships under the table's own name.

**Verified: 2 177 EditMode / 0 / 0, twice consecutively on the final code** (27.0 s, 21.6 s), against
M4-05a's **2 162** — **+15, and the arithmetic lands to the row**: 4 in `SaveDtoTests`
(`Profile_CarriesShards`, `Profile_RefusesNegativeShards`, `Profile_DefaultStartsAtZero`,
`Profile_WithShardsKeepsEverythingElse`), 1 in `SaveMigrationTests` (`MigrateProfile_V2_GainsNoShards`),
1 in `LocalJsonSaveStoreTests` (`Fixture_V3Profile_IsWhatThisBuildWrites`), 8 in `ShardWriterTests`
(the table's six plus the two implied guards it names — `Construct_NullProfiles_Throws`,
`Construct_NullHub_Throws`), and 1 in `InstallerTests` (`RunScope_ComposesWithTheWriter`). The
**third** implied guard, the `ProfileMirror` round trip, is an assertion added to the existing
`Store_RoundTripsAProfile` rather than a new row — the round trip already had one, and a second would
have been the same claim twice. **The first run was green**, which is worth recording because M4-05a's
was not: nothing in the suite asserts a profile's whole shape the way `SpitterBehaviourTests` asserts
a death tick's, so the constructor ripple was caught by the compiler rather than by a red row.

Compile clean through the MCP, confirmed the way [Traps §3](../../Traps.md) asks rather than by
timestamp — `PlayerProfile.CurrentVersion` read back as 3, `RunSnapshot.CurrentVersion` as 3, a
`WithShards` that kept its haptics, a v1 → v3 migration landing at v3 with `shards: 0` from a decoded
999, and `typeof(ShardWriter)` resolving — all from a `Unity_RunCommand` on an Editor reporting
`isApplicationActive == False` throughout. **Zero new analyzer warnings**: the Console's errors and
warnings are the suite's own deliberate rows — the `WarnsOn*` authoring sweeps, the corrupt-save
discards, the `FailNextWrite`s — and the one line naming anything this task built is
*"Could not save the player profile: There is not enough space on the disk"*, which is
`Writer_SurvivesAFailedWrite`'s own `LogAssert.Expect`. `dotnet format whitespace --folder
--verify-no-changes` clean over all eighteen changed files. `ProjectSettings/TimeManager.asset`
re-serialised again — the eighth time in twelve tasks — and was reverted ([Traps §5](../../Traps.md)).

**The ripple was twenty-five call sites, not the fourteen the Files table predicted.** M3-09c's
figure was carried forward rather than recounted, and the format has gained two writers and three
fixture rows since. All twenty-five are compiler-guided and none changed an assertion.

**Rule 8 is the ruling this task is most likely to be questioned on later, so it is restated here as
built rather than as argued:** the build banks a number no player can spend, `Shards` has no reader
in `Soulvail.Game` until [M4-06](M4-06-run-end-screen.md), and that is deliberate. The test is *"no
field before a consumer"*, and there are two the day it lands.

**Manual verification was not performed and that is stated rather than implied.** The spec's five
steps are listed in the handover for the owner; step 5 is **[device]** and is deferred to the first
hardware session ([row 3](../ROADMAP.md#carry-forward-into-m4)).
