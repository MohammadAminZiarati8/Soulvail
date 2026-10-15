# UI-11a — The profile remembers places and picks

**Size:** M · **Depends on:** — · **Branch:** none — committed on `dev` (owner, 2026-10-14)
**Design refs:** AR §11.6, ADR-0007, GD §4.5 · **Ledger rows:** none

## Goal

`PlayerProfile` v5 carries the places a player has finished, their best stage in each, and the place and class last picked, and every v1–v4 profile still loads.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Save/SaveDtos.cs` | Core | `PlayerProfile` v5: four fields, three `With…` methods, `ModeBest` |
| `Core/Save/SaveMigrations.cs` | Core | the v4 → v5 step |
| `Game/Adapters/LocalJsonSaveStore.cs` | Game | `ProfileMirror` gains the four fields |
| `Tests/Core/Save/SaveMigrationTests.cs` | Tests.Core | the migration rows |
| `Tests/Core/Save/PlayerProfileTests.cs` | Tests.Core | the field and `With…` rows |
| *ripple* | | every `new PlayerProfile(…)` in tests and fixtures gains the four arguments |

## Public API

```csharp
public readonly struct ModeBest            // one place's best: (ModeId, Stage), Stage >= 1
{
    public ModeBest(ContentId modeId, int stage);
    public ContentId ModeId { get; }
    public int Stage { get; }
}

// PlayerProfile, CurrentVersion = 5; the constructor gains, after locale:
//   IReadOnlyList<ContentId> finishedModeIds, IReadOnlyList<ModeBest> bestStages,
//   ContentId selectedCharacterId, ContentId selectedModeId
public IReadOnlyList<ContentId> FinishedModeIds { get; }
public IReadOnlyList<ModeBest> BestStages { get; }
public ContentId SelectedCharacterId { get; }     // default = none picked yet
public ContentId SelectedModeId { get; }          // default = none picked yet
public int BestStage(ContentId modeId);           // 0 when never played
public PlayerProfile WithFinished(ContentId modeId);
public PlayerProfile WithBestStage(ContentId modeId, int stage);
public PlayerProfile WithSelection(ContentId characterId, ContentId modeId);
```

## Behaviour

1. **`WithFinished` adds an id once**; finishing a place a second time changes nothing.
2. **`WithBestStage` only raises**: a lower or equal stage returns an equal profile; a place never seen gains a row.
3. **`WithSelection` replaces both picks.** A `default` id means "none picked" and is legal.
4. **The constructor refuses** null lists, a duplicate id in either list, and a `ModeBest` stage below 1.
5. **v4 → v5** carries every v4 field unchanged and adds empty lists and no picks. A v1 profile walks the whole chain to v5.
6. **The JSON round-trips**: a v5 profile written and read back is equal, including both picks.

## Tests

| Test | Given / When / Then |
|---|---|
| `WithFinished_Twice_AddsOnce` | rule 1 |
| `WithBestStage_Lower_KeepsHigher` · `WithBestStage_NewMode_AddsRow` | rule 2 |
| `WithSelection_ReplacesBoth` | rule 3 |
| `Ctor_DuplicateFinished_Throws` · `Ctor_BestStageZero_Throws` | rule 4 |
| `Migrate_V4_AddsEmptyPlaces` · `Migrate_V1_ReachesV5` | rule 5 |
| `Profile_V5_RoundTrips` (Tests.Game, `LocalJsonSaveStoreTests`) | rule 6 |

## Out of scope

- Who writes these fields: UI-11d (finished, best) and UI-11e (picks).
- Starting a run part-way through a place: [parking lot](../ROADMAP.md#parking-lot).

## As built

Built to the Files table; the ripple was 40 `new PlayerProfile(…)` sites across eight test files, each given the four arguments.

- **`ModeBest` does not guard.** `default(ModeBest)` exists whatever its constructor says, so the one check is `PlayerProfile`'s: it refuses a stage below 1, a `default` id and a repeated place (`Ctor_BestStageZero_Throws` covers both `new ModeBest(id, 0)` and `default`).
- **The mirror stores bests as parallel arrays** (`bestStageModeIds`, `bestStages`) next to `finishedModeIds`, `selectedCharacterId`, `selectedModeId` — flat, like the rest of the document.
- **The decoder is lenient, the constructor is not.** A best row whose id does not parse, whose stage is below 1 or that repeats a place is dropped, as is a repeated finished id (`Store_AMalformedBestRowIsDroppedNotFatal`). A refused profile costs every Shard; a dropped row costs one number.
- **Renamed rows:** `Migrate_V4IsTheIdentity` → `Migrate_V5IsTheIdentity`, `ProfileGate_AcceptsOneToFourRefusesFive` → `…OneToFiveRefusesSix`, `Store_WritesAV4ProfileLiteral` → `Profile_V5_RoundTrips` (byte-for-byte `V5Profile`); `V4Profile` stays as the migration input (`Store_ReadsTheV4ProfileLiteral`).
- **Suite:** 4 119 EditMode, 4 117 passed / 1 failed (`CharacterPreviewTests.Resize_…`, D3D12 depth) / 1 inconclusive.
