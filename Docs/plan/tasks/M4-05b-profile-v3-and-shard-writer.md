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

_Filled at merge._
