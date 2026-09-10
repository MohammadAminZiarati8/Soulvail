# M2-02 — `ModeSpec` + `ModeDefinition`, Descent as the first instance, and the `RunConfig` reshape

**Size:** M · **Depends on:** M2-01 (only for ordering; no code dependency) · **Branch:** `m2-02-mode-spec`
**Design refs:** GD §4.5 (modes), §8.2 (introduction schedule), §7.1; AR §5, §10.1, §18.1, §18.3; ADR-0006, ADR-0010, ADR-0011 · **Ledger rows:** 3, 6 (with 1 and 10 in mind)

## Goal

A mode is a data object: Descent is an asset, not an assumption, and a run is started with a mode, a depth and a seed that came *in* rather than being read back out of the generator.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ModeSpec.cs` | Core | `ModeSpec` + `RosterEntry` (grouped, `EnemyEvents.cs`' precedent) |
| `Game/Authoring/ModeDefinition.cs` | Game | The ScriptableObject and its `ToSpec()` — **block namespace** (Traps §5) |
| `Data/Modes/Descent.asset` | — | The one V1 mode. Asset, not code; listed, not counted |
| `Tests/Core/Content/ModeSpecTests.cs` | Tests.Core | Roster, eligibility, guards |
| `Tests/Game/Authoring/ModeDefinitionTests.cs` | Tests.Game | Conversion, validation, and that `Descent.asset` says what GD §4.5 and §8.2 say |
| *small edits* | | `RunConfig` reshaped (rule 5); `ContentCatalog` + `modes`, `Mode(id)`, `TryGetMode`; `RunSession.Start` validates content **before** `RunStarted` and records the mode and depth; `RunState` + `ModeId`, + `StageIndex`; `PendingRun.Set` + mode id; `MenuPresenter` chooses Descent; `RunTicker.Start` builds the new config; `BootInstaller` loads `Data/Modes/` into the catalog; **AR §5's `Run` row**, which lists `ModeSpec` under `Run` — corrected to `Content`, with the reason in the note above *Behaviour* |
| *ripple* | | **19 `new RunConfig(...)` call sites** across Tests.Core, Tests.Game and `RunTicker` — a mechanical, compiler-guided sweep. Test fixtures that build a `ContentCatalog` gain a mode |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// One archetype a mode may spawn, and the depth it is first allowed at (GD §8.2).
public readonly struct RosterEntry
{
    public RosterEntry(ContentId specId, int introducedAtStage);
    public ContentId SpecId { get; }
    public int IntroducedAtStage { get; }      // 1 = from the first stage
}

/// A mode, as authored data. Descent is the only instance in V1 (GD §4.5).
public sealed class ModeSpec
{
    public ModeSpec(
        ContentId id,
        LocKey nameKey,
        int startingStage,
        bool isEndless,
        int finalStage,                        // ignored when isEndless
        IReadOnlyList<RosterEntry> roster);

    public ContentId Id { get; }
    public LocKey NameKey { get; }
    public int StartingStage { get; }
    public bool IsEndless { get; }
    public int FinalStage { get; }             // int.MaxValue when endless
    public IReadOnlyList<RosterEntry> Roster { get; }

    /// Whether `stage` is a stage this mode has: >= StartingStage, and <= FinalStage unless endless.
    public bool HasStage(int stage);

    /// Fills `destination` with every entry eligible at `stage`, in roster order, and returns the count.
    /// Allocates nothing; the composer (M2-04) calls it once a stage against a stack buffer.
    public int RosterFor(int stage, Span<RosterEntry> destination);

    /// The archetype introduced *at* `stage`, or false when the stage introduces nothing (GD §8.2).
    public bool TryGetIntroduction(int stage, out ContentId specId);
}
```

```csharp
namespace Soulvail.Core.Run;

public sealed class RunConfig
{
    public RunConfig(
        ContentId modeId,
        ContentId characterId,
        int seed,
        int stageIndex,
        SpawnPlan spawnPlan);

    public ContentId ModeId { get; }
    public ContentId CharacterId { get; }
    public int Seed { get; }          // inbound: what the generator was seeded with, stated by the caller
    public int StageIndex { get; }    // the depth this run begins at — StartingStage, or a resumed one
    public SpawnPlan SpawnPlan { get; }
}

// RunState (added)
public ContentId ModeId { get; }
public int StageIndex { get; internal set; }   // settable: M2-10 advances it
```

```csharp
namespace Soulvail.Game.Authoring
{
    public sealed class ModeDefinition : ScriptableObject   // block namespace — Traps §5
    {
        public ModeSpec ToSpec();
    }
}
```

**`ModeSpec` lives in `Core/Content/`, not `Core/Run/`.** AR §5's module table puts it under `Run`; AR §10.1 puts it in the catalog beside `CharacterSpec` and `EnemySpec`, and the catalog is what a `ContentId` resolves against. §10.1 wins and §5's row is corrected in this change — a sketch is fixed when it misleads (AR §5's preamble). A placement, not a behaviour, so nothing below tests it.

## Behaviour

1. **The roster is the mode's, not the archetype's.** A `RosterEntry` says *this mode may spawn this archetype from this depth* — GD §8.2's schedule is a property of Descent, and a Boss Rush would have neither the schedule nor the roster. What an archetype *costs* stays on `EnemySpec` (M2-04, GD §8.1), because that is a fact about the creature.
2. **At most one archetype may be introduced at any one stage**, and an id may appear once. GD §8.2: *"New enemies arrive one at a time, in a wave where they're the only new thing."* A roster that breaks it is authoring error, refused in the constructor, and it is why `TryGetIntroduction` can answer with a single id.
3. `RosterFor` returns entries in roster order, filtered by `IntroducedAtStage <= stage`, and **allocates nothing** — the composer calls it once a stage against a stack buffer. A destination shorter than the eligible count throws rather than silently truncating a wave's vocabulary. `HasStage` is `stage >= StartingStage && (IsEndless || stage <= FinalStage)`.
4. **`FinalStage` is `int.MaxValue` when endless**, so every comparison reads the same way and no caller needs to branch on `IsEndless` to ask whether a stage exists. The constructor takes the flag rather than inferring it, because "endless" is a designer's statement and `int.MaxValue` in an asset is not.
5. **`RunConfig` is reshaped once, here** — ledger row 6. Five fields: mode, character, seed, stage, plan. The seed now arrives *in*: `RunSession.Start` records `config.Seed` in `RunState.Seed` instead of reading `_random.Seed`, because resume has to state a seed rather than discover one. **The generator and the config must agree**: `Start` throws when `config.Seed != _random.Seed`. One truth, checked at the one place both are visible. **Ruled by the owner at M2-00b**, against the alternative of an `IRandom.Reseed(seed)` that would let core drive the generator — that widens a port ahead of its caller (AR §6) and is half of what restoring stream state rides on, so it stays M2-13's to weigh with ledger row 1. **Do not add `Reseed` here.**
6. **`stageIndex` is validated against the mode, before anything is announced**: `< 1` throws, and so does a stage the mode does not have (rule 3). A run of Descent begins at `mode.StartingStage` unless a caller states otherwise; `RunTicker` passes `mode.StartingStage`, M2-14 passes the saved depth.
7. **All content is resolved before `RunStarted` is published** — ledger row 3. `Start`'s order becomes: resolve the mode → resolve the character → check the seed → check the stage → resolve **every** `SpawnPlan.Entry.SpecId`, the `RespawnPolicy.SpecId` if there is one, and every `RosterEntry.SpecId` against the catalog → build the state → publish `RunStarted` → `IsRunning = true` → `SpawnAll`. An unauthored archetype anywhere now throws with **nothing announced, nothing standing and the previous `State` still readable**, which is what M1-06's *As built* said it was leaving for this task. The ordering after `RunStarted` is unchanged (AR §18.1).
8. Validation walks the roster too, not only the plan, because the director (M2-05) spawns from the roster and the failure would otherwise arrive forty seconds into a run instead of at its first frame.
9. `ModeDefinition.ToSpec()` converts once at boot (ADR-0006), `[SerializeField] private` throughout, and validates nothing the spec already validates — the constructor's exceptions are the single account of what a legal mode is.
10. `Descent.asset` ↔ `mode.descent`, per the asset-naming rule. Its roster is **Husk 1, Spitter 2, Bloater 4** (GD §8.2) even though M2-06 authors the last two: the schedule is the mode's statement of intent, and rule 8's validation is what makes the gap loud instead of mysterious. **This means the roster's ids must exist before Descent is loadable** — so `Descent.asset` ships with Husk alone, and M2-06 adds the other two rows in the same change that authors them. Stated here so it is a decision rather than a surprise.

## Tests

| Test | Given / When / Then |
|---|---|
| `Roster_FiltersByStage` | Husk 1, Spitter 2, Bloater 4 / `RosterFor(3, buf)` / 2 entries, Husk then Spitter |
| `Roster_OrderIsRosterOrder` | roster out of stage order / `RosterFor(9)` / entries in the order authored |
| `Roster_DestinationTooSmall_Throws` | 3 eligible, buffer of 2 / `RosterFor` / throws `ArgumentException` |
| `Roster_AllocatesNothing` | warm-up / 10 000 × `RosterFor` into a stack buffer / allocated-bytes delta == 0 |
| `Roster_DuplicateId_Throws` | Husk twice / ctor / throws |
| `Roster_TwoIntroductionsAtOneStage_Throws` | Spitter 2, Bloater 2 / ctor / throws (GD §8.2, rule 2) |
| `Introduction_AtStage` | Husk 1, Spitter 2 / `TryGetIntroduction(2)` / true, `enemy.spitter` |
| `Introduction_None` | as above / `TryGetIntroduction(3)` / false |
| `HasStage_Endless` | endless, starting 1 / `HasStage(1)`, `HasStage(9999)` / both true |
| `HasStage_Finite` | starting 1, final 10 / `HasStage(10)`, `HasStage(11)` / true, false |
| `HasStage_BeforeStart` | starting 3 / `HasStage(2)` / false |
| `Catalog_ModeLookupAndDuplicates` | — / — / mirrors the character and enemy rows for `Mode(id)` / `TryGetMode` |
| `Config_RecordsAllFive` | — / ctor / every property reads back |
| `Config_StageBelowOne_Throws` | stage 0 / ctor / throws |
| `Start_SeedMismatch_Throws` | `FixedRandom` seed 7, config seed 8 / `Start` / throws; not running; no events |
| `Start_RecordsConfigSeed` | seed 7 both / `Start` / `State.Seed == 7`, `RunStarted.Seed == 7` |
| `Start_RecordsModeAndStage` | Descent, stage 4 / `Start` / `State.ModeId`, `State.StageIndex` |
| `Start_StageNotInMode_Throws` | mode final 5, config stage 6 / `Start` / throws; not running; no events |
| `Start_UnknownPlanArchetype_NothingAnnounced` | plan names `enemy.ghost` / `Start` / throws; **no `RunStarted`**; `IsRunning` false; nothing spawned (row 3) |
| `Start_UnknownRespawnArchetype_NothingAnnounced` | plan valid, respawn names a stranger / `Start` / same |
| `Start_UnknownRosterArchetype_NothingAnnounced` | roster names a stranger / `Start` / same (rule 8) |
| `Start_ValidationRunsBeforeState` | a failed `Start` after a good run / — / previous `State` still readable, unchanged |
| `Start_OrderUnchanged` | good config / `Start` / events `[RunStarted, EnemySpawned…]`, and `IsRunning` was true inside the spawn handler (AR §18.1) |
| `Definition_ToSpec_RoundTrip` | an authored `ModeDefinition` / `ToSpec()` / every field matches |
| `Descent_MatchesDesign` | `Data/Modes/Descent.asset` / load / id `mode.descent`, endless, starting stage 1, roster = Husk at 1 (GD §4.5, §8.2, and rule 10) |

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

1. **[Editor]** Boot → Descend → the run plays exactly as it did before. The reshape is invisible by design; what is being checked is that the menu path still composes.
2. **[Editor]** Press Play in the Run scene directly. The unseeded-run warning still appears, the fallback character still resolves, and the run starts at Descent stage 1.

## Out of scope

- **Which classes a mode allows** and **starting modifiers** (GD §4.5). Neither has a consumer: one class exists, and no effect system does. M5-08 and M6 add them under AR §6's rule.
- **The difficulty curve**, which GD §4.5 also lists as the mode's — M2-03 adds `ModeSpec.Scaling` as a small edit, because the curve types do not exist yet and inventing them here would fix their shape before there is a caller.
- **The resumed-run payload.** It is the one field M2-14 adds, and its name is `RunSnapshot Restore`; the DTO does not exist until M2-13, so nothing is stubbed for it. The shape is stated here so M2-14 does not re-argue it — *reshape once* means these five fields are right, not that a sixth is forbidden.
- Stage flow, arrival, gates — M2-10. `StageIndex` is recorded here and advanced there (`internal set` for exactly that; `ModeId` is get-only, because a run does not change mode).
- `PendingRun.Clear()` still has no caller (ledger row 10) — M2-14.

## As built

**As specified, with seven deviations.** 508 EditMode green (462 + 46), 3 PlayMode green, zero errors, zero analyzer warnings, no `ProjectSettings/` drift. The nineteen `new RunConfig(...)` call sites swept as predicted — eighteen in tests plus `RunTicker` — and six test fixtures gained a mode.

**1. `RunTicker` gained an `IRandom` dependency.** Not in the Files table's small edits, and unavoidable given rule 5: `Start` has to *state* the seed, and `RunTicker` is the only object that knows it on both paths into a run. `PendingRun` carries the menu's seed, but a direct Play has none and `RunInstaller.CreateRandom` invents one from `Environment.TickCount` that reaches nothing else. The alternative — duplicating the fallback in the ticker — would be two places inventing a seed and no guarantee they agreed, which is the exact failure rule 5 exists to catch. Nothing in `RunTicker` ever draws from it, and the field's comment says so.

**2. `BootScope.prefab` was edited, and it is an asset the Files table did not list.** Implied by "`BootInstaller` loads `Data/Modes/` into the catalog": the established route is a serialized array on `BootScope`, not `Resources.Load`, so the prefab is where Descent is named for the container. One line added (`_modes`, one element), verified by reading the field back after the save (Traps §5). `Data/Modes/` is a new folder, likewise implied by the asset's path.

**3. Three of the spec's test rows had no file in its table** and went into the fixtures that already own their kind of assertion rather than into a sixth file — the same call M2-01 made. `Catalog_ModeLookupAndDuplicates` → `ContentTests`; `Config_*` and every `Start_*` row → `RunSessionTests`; and two boot rows (`Boot_ResolvesCatalog_WithDescent`, `Boot_NullModeList_Throws`) → `InstallerTests`, beside the character and enemy rows they mirror.

**4. `HasStage` is written without the `IsEndless` branch.** Rule 3 spells it `stage >= StartingStage && (IsEndless || stage <= FinalStage)`; rule 4's stated purpose is that *"no caller needs to branch on `IsEndless` to ask whether a stage exists"*. Given `FinalStage == int.MaxValue` when endless, the two are the same predicate, and writing the branch would have contradicted the rule that makes it unnecessary. `HasStage_Endless` asserts `FinalStage` is `int.MaxValue`, which is what keeps the simplification honest.

**5. An empty roster is legal, and the spec did not say either way.** Ruled here rather than guessed silently: a mode whose arena content comes entirely from its spawn plan has nothing to schedule, and `RosterFor` already answers 0 for any stage before the first introduction — so an empty roster is not a new shape for a caller to handle, it is the same one for the whole run. `Ctor_EmptyRoster_IsLegal` pins it. It is also what let six ripple fixtures build a mode without inheriting content they are not about.

**6. `Start_OrderUnchanged` became two rows.** The recorded event list shows `[RunStarted, EnemySpawned]`; it cannot show that `IsRunning` was already true *inside* the spawn handler, because a recorder says what was published and not what was true when it happened (Traps §7). The second half needs the fixture's `CapturingEvents`, so it is `Start_IsRunningIsTrueInsideSpawnHandler`.

**7. Two test rows beyond the table.** `Descent_EveryYamlKeyBindsToAField` exists because two of Descent's five fields ship the value their C# initialiser already holds — `_startingStage` 1 and `_isEndless` true — which makes `Descent_MatchesDesign` unable to tell a bound key from a misspelled one for either (Traps §7). It compares the file text either side of a `ForceReserializeAssets`, so it proves the binding *and* leaves no diff behind, which the bare reserialise M1-03 used would not. `AllModeDefinitions_RosterIdsAreAuthored` guards rule 10's ordering: when M2-06 adds the Spitter and the Bloater to the roster, this fails if the `EnemyDefinition` assets did not land in the same change.

**One decision worth carrying forward.** `MenuPresenter` and `RunTicker` both choose *the first mode the catalog holds* rather than the literal `mode.descent`. GD §4.5 says nothing in the code may assume Descent, and an id literal in either place would compile, run, and still be there the day a second mode ships. It is the same stand-in `FirstCharacterId` already was.

**Ledger rows 3 and 6 are answered.** Row 3: `Start` resolves the mode, the class, and every archetype the plan, the respawn policy and the roster name — all before `RunStarted`, so an unauthored archetype now throws with nothing announced, nothing standing, and the previous `State` still readable. The roster walk is the half with no other line of defence, since nothing spawns from a roster until M2-05. Row 6: five fields, one sweep. Rule 5's deferred `IRandom.Reseed` question was already made unnecessary by the owner's M2-00e ruling, and no `Reseed` was added.
