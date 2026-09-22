# M6-01b — Save format v4: the one bump this milestone gets

**Size:** S (no new files; four test fixtures substantially extended) · **Depends on:** M6-01a · **Branch:** `m6-01b-save-format-v4`
**Design refs:** GD §7.3, §10, §13.3, §13.4, §15; AR §10.3, §11.6, §18.1, §18.3; ADR-0007, ADR-0011; M2-13a, M2-13b, M2-14a, M3-01b, M3-07b · **Ledger rows:** none

## Goal

`RunSnapshot` becomes v4 carrying **everything M6 will ask a run to remember** — Essence, Veilrot,
the reroll counters, the banished nodes and the Ordeals — three of the five with no writer yet, the
v3 → v4 step ships in the same PR, and the milestone pays the ripple once instead of four times.

## Why every field lands now, including the three nothing writes

This is [M3-01b](M3-01b-save-format-v2.md)'s ruling applied a second time, and this time it is
counted rather than argued. That task put `TakenNodeIds` on disk two tasks before `SkillTree`
existed to fill it, and `SaveDtos.cs` still carries the sentence that justified it: *"the reader is
two tasks away in the same milestone, and the alternative is a second step in the chain, for ever,
for a format nobody has shipped."*

**The number that makes it not a matter of taste:** `new RunSnapshot(...)` has **36 call sites
across 30 files.** Bumping once per mechanic — Essence here, Veilrot at
[M6-04](M6-04-veilrot-thresholds-and-the-claiming.md), the shop's counters at
[M6-02b](M6-02b-four-things-essence-buys.md), Ordeals at M6-06 — is **four** migration steps, four fixture
sets and **144** edited call sites, for a format no player has ever seen. One bump is 36.

**The risk is stated rather than waved at.** Three of these fields are shaped by specs
[M6-00b](../ROADMAP.md#m6--systems-complete) has not written. If one of them wants a
different shape, that is a deviation on *that* task and this format is re-cut before `m6` is tagged
— which costs nothing, because **v4 has never left the machine it was written on.** What would cost
something is the opposite mistake: a field omitted, and a run's Veilrot silently destroyed by the
first `Continue` after M6-04 merges. `PlayerProfile.Shards` is the precedent for which direction
matters (M4-05b rule 8) — *a number not written is data destroyed.*

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| *small edits* | Core, Game | `Core/Save/SaveDtos.cs` — `CurrentVersion` 4, the `RunEconomy` struct, three arguments and their guards (rules 1–4); `Core/Save/SaveMigrations.cs` — the `if (version < 4)` step, below the `< 3` one (rule 5); `Game/Adapters/LocalJsonSaveStore.cs` — `RunMirror` gains six flat fields (rule 6); `Core/Save/RunRecorder.cs` — `Take` reads the wallet and passes empties for the rest (rule 7); `Core/Run/RunState.cs` — the `Veilrot`, `BanishedNodeIds` and `OrdealIds` reads, answering zero and empty until their systems exist (rule 8); `Core/Run/RunSession.cs` — one restore line, below the tree's and above `Health.Restore` (rule 9); **AR §18.1**'s restore-order row gains its sixth line |
| *tests* | Tests.Core / Tests.Game | `SaveDtoTests` (the struct's guards, the copy, `default`, the two lists' opposite rules), `SaveMigrationTests` (the step, the chain now **four** long, v1 through three steps), `LocalJsonSaveStoreTests` (a v4 literal is what this build writes; the v3 literal becomes the new step's input; v1 and v2 still decode), `RunRecorderTests` (capture), `RunSessionResumeTests` (restore, and an Essence balance that survives a kill) |
| *ripple* | | **36 `new RunSnapshot(...)` sites across 30 files** gain three arguments — compiler-guided, M3-01b's thirteen at three times the size |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Save;

/// <summary>
/// The four scalars a run accumulates and spends — GD §15's economy table, minus the one that
/// outlives the run. One argument rather than four, for RandomState's reason: a constructor that
/// already takes fourteen things does not get better by taking eighteen.
/// </summary>
/// <remarks>
/// <b><c>default</c> is a legal fresh run</b> — nothing earned, nothing corrupted, nothing bought —
/// which is what makes rule 5's migration step one token. AR §18.3's "a struct with an invariant
/// needs the check at both ends" is met because the zeroed form is the *valid* opening state, not an
/// invalid one; the check that matters is on the way in.
/// </remarks>
public readonly struct RunEconomy
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="essence"/>, <paramref name="rerollsBought"/> or
    /// <paramref name="rerollsSpent"/> is negative; <paramref name="veilrot"/> is non-finite or
    /// outside [0, 100]; or more rerolls are spent than were bought.
    /// </exception>
    public RunEconomy(int essence, float veilrot, int rerollsBought, int rerollsSpent);

    /// <summary>What the wallet held at the write (M6-01a).</summary>
    public int Essence { get; }

    /// <summary>GD §10's meter, in [0, 100]. Zero until M6-04 writes it.</summary>
    public float Veilrot { get; }

    /// <summary>How many rerolls this run has bought — GD §13.3's doubling price. M6-02's.</summary>
    public int RerollsBought { get; }

    /// <summary>How many of them have been used. Never more than were bought. M6-02's.</summary>
    public int RerollsSpent { get; }
}

public readonly struct RunSnapshot
{
    public const int CurrentVersion = 4;

    // ... the fifteen v3 arguments, then:
    //     RunEconomy economy
    //     IReadOnlyList<ContentId> banishedNodeIds   // v4: empty until M6-02; no default(ContentId)
    //     IReadOnlyList<ContentId> ordealIds         // v4: empty until M6-06; no default(ContentId)

    public RunEconomy Economy { get; }

    /// <summary>Nodes GD §13.3's Banish took out of this run's pool. Never null; empty is ordinary.</summary>
    public IReadOnlyList<ContentId> BanishedNodeIds { get; }

    /// <summary>GD §13.4's Ordeals, in the order they were drawn. Never null.</summary>
    public IReadOnlyList<ContentId> OrdealIds { get; }
}

// SaveMigrations.MigrateRun (changed): a third step, `if (version < 4)`, below the other two —
// it rebuilds at v4 with default(RunEconomy) and two empty lists, keeping every v3 field as read.
```

## Behaviour

1. **`RunEconomy` is one argument carrying four numbers, on `RandomState`'s precedent.** That struct
   put five `ulong`s behind one parameter for exactly this reason, and the alternative here is
   eighteen positional arguments on a constructor that already has fifteen — where the two adjacent
   `int`s (`rerollsBought`, `rerollsSpent`) would be one transposition away from a silent bug no
   compiler could see. Behind one struct they are named at the call site.
2. **Veilrot is guarded at both ends of its range, and that is not symmetry for its own sake.**
   Negative is a file somebody edited; **above 100 is a file somebody edited *usefully***, because
   GD §10.2's Claiming fires at 100 and a saved 10 000 would arrive Claimed with 10 000 points of
   headroom that nothing can ever cleanse. `!(v >= 0f)` rather than `v < 0f`, so NaN is refused with
   the negatives — `playerHp`'s spelling, and here the symptom of letting one through is a meter
   whose every threshold comparison is false for the rest of the run (AR §18.3).
3. **More rerolls spent than bought is refused, and it is the one *relational* guard this format
   has.** Every other check on `RunSnapshot` is about a single value. This one exists because the
   pair is a stock — `Bought − Spent` is what the player may still use — and a hand-edited file with
   `spent: 0, bought: 99` is merely rich, while `spent: 99, bought: 0` is a negative stock that
   [M6-02b](M6-02b-four-things-essence-buys.md)'s reroll counter would carry for the rest of the run.
4. **Both new lists refuse `default(ContentId)`, which puts them with `TakenNodeIds` and against
   `ManualSkillIds`.** M3-07b rule 3 wrote that contrast down at both ends because it is exactly
   what a later reader gets wrong: a slot's empty is expressible only as a defaulted id, and these
   two lists have no such state — a banished node has an id and an Ordeal has an id. **An id this
   build no longer ships is *not* refused here**, for `takenNodeIds`' reason: that is content
   validation's answer at `RunSession.Start`, with the diagnostic that names the asset. Both are
   copied and wrapped on the way in, and an empty one costs nothing (`Array.Empty<ContentId>()`),
   which is what keeps `RunRecorder.Take` allocation-free for every run until M6-02 merges.
5. **The v3 → v4 step is written unconditionally from the shape rather than from what was decoded**
   — the rule both earlier steps state and the reason M3-01b rule 3 gives: a v3 document that
   somehow carried an Essence field is still a v3 document and gets v3's meaning, which is a run
   that had no economy by construction. So the step rebuilds at 4 with `default(RunEconomy)` and two
   empty lists and keeps every v3 field as read. **The chain now runs three steps**, and
   `MigrateRun_V1_RunsEveryStepInOrder` is what proves a v1 file arrives as a playable v4.
6. **`RunMirror` flattens `RunEconomy` into six fields rather than nesting it.** `RandomState`'s
   treatment exactly — `randomSpawn`, `randomOffers`, … — because `JsonUtility` serialises a nested
   `[Serializable]` class and the mirror's whole job is to be a flat document a human can read in a
   bug report. The two lists are `string[]`, defaulted to `Array.Empty<string>()` so a v3 document
   decodes without them, which is the same shape `takenNodeIds` has carried since M3-01b.
7. **`RunRecorder.Take` reads the wallet and passes empties for everything else.** One real value
   and four placeholders, and that asymmetry is the whole of this task's honesty: the recorder is
   where a field stops being a promise. It writes `new RunEconomy(state.Essence, 0f, 0, 0)` and two
   `Array.Empty<ContentId>()`, and each of the four later tasks replaces exactly one of those
   arguments **without touching `CurrentVersion`** — `Store_WritesAV4Literal` is the row that
   objects if one of them bumps it.
8. **`RunState` grows three reads that answer zero and empty, and they are not placeholders.**
   `RunState.Veilrot` is `0f`, `BanishedNodeIds` and `OrdealIds` are `Array.Empty<ContentId>()`,
   each written as a real answer rather than a stub: a run with no meter genuinely has no Veilrot,
   the way `TakenNodeIds` was genuinely empty for a class with no tree (M3-03). **Empty, never
   null**, for that property's own rule — no reader ever has to ask. Each becomes a forward to its
   system in the task that builds one, and none of them widens `RunState`'s seal (AR §18.2).
9. **The restore line goes below the tree's and above `Health.Restore`**, which is
   `SkillRunner.Restore`'s placement one line over (M3-07b rule 7). Below the tree because a banished
   node has to be refused against a tree that already exists; above `Health.Restore` because
   [M6-04](M6-04-veilrot-thresholds-and-the-claiming.md)'s meter puts a **−20 % max HP** modifier on
   at 75, and a maximum restored before the modifier that shrinks it would refill the player to a
   number they never had. Only the wallet's restore is *written* here; the other three are the lines
   their own tasks add, and AR §18.1's restore-order row is edited now so they land in a stated order
   rather than wherever each task happens to put them.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Economy_CarriesItsFour` | `new RunEconomy(317, 42.5f, 2, 1)` / — / all four read back |
| `Economy_DefaultIsAFreshRun` | `default(RunEconomy)` / — / 0, 0, 0, 0 and no throw — rule 1 |
| `Economy_RefusesNegatives` | each `int` negative in turn / — / throws, naming the field |
| `Economy_RefusesVeilrotOutsideItsRange` | −0.1, 100.1, NaN, +∞ / — / throws in each case — rule 2 |
| `Economy_AcceptsBothEnds` | 0 and 100 exactly / — / no throw: 100 is the Claiming, not an error |
| `Economy_RefusesMoreSpentThanBought` | `(0, 0f, 0, 1)` / — / throws; `(0, 0f, 5, 5)` does not — rule 3 |
| `Snapshot_CarriesTheThreeNewFields` | a v4 built with a stocked economy and two lists / — / all three read back, lists copied |
| `Snapshot_DefaultAnswersEmptyForBothLists` | `default(RunSnapshot)` / — / `BanishedNodeIds` and `OrdealIds` are empty and **not null**; `Economy` is `default` |
| `Snapshot_BothNewListsRefuseADefaultedId` | a list containing `default(ContentId)` / — / throws for each, and the message names **which** list and the index — rule 4 |
| `Snapshot_BothNewListsRefuseNull` | null for each / — / `ArgumentNullException`, whose message says empty and null are not the same thing |
| `Snapshot_BothNewListsAreCopied` | a caller's `List<ContentId>` passed, then mutated / — / the snapshot is unmoved — `TakenNodeIds`' rule |
| `Snapshot_AnUnshippedIdIsNotRefusedHere` | a list naming `skill.deleted` / — / constructs; the refusal is `RunSession.Start`'s — rule 4 |
| `Snapshot_EmptyListsAllocateNothing` | 10 000 constructions with empty lists / `AllocationAssert.None` / zero — rule 4 |
| `Migrate_V3ToV4IsAnEmptyEconomy` | a v3 snapshot with every field set / migrated / version 4, `Economy` is `default`, both lists empty, **every v3 field unchanged** — rule 5 |
| `Migrate_V4IsTheIdentity` | a v4 snapshot / migrated at 4 / byte-identical |
| `Migrate_V1RunsEveryStepInOrder` | a v1 snapshot / migrated / v4, level 1, four empty slots, empty economy, empty lists — the chain, now three steps — rule 5 |
| `Migrate_V3DocumentCarryingAnEconomyStillGetsV3sMeaning` | a v3 `RunMirror` with `essence: 500` decoded / migrated / `Economy.Essence` is **0** — rule 5 |
| `Migrate_RefusesAVersionAboveThis` | version 5 / `CanReadRun` / false, and `MigrateRun` throws |
| `Store_WritesAV4Literal` | this build's `SaveRun` / the file on disk / the six flat fields and both arrays are present — rule 6 |
| `Store_ReadsTheV3Literal` | the checked-in v3 JSON / loaded / decodes and migrates, `Essence` 0 |
| `Store_ReadsTheV2AndV1Literals` | both older literals / loaded / still decode — the chain is not broken by the third step |
| `Store_ADocumentWithoutTheNewArraysDecodes` | a v4 document with both arrays absent / loaded / empty, not null — rule 6 |
| `Recorder_CapturesTheWallet` | a run with 84 Essence / `Take` / `Economy.Essence` 84, Veilrot 0, both counters 0, both lists empty — rule 7 |
| `Recorder_TakeAllocatesNothing` | 10 000 takes on a run with no nodes and no banishes / `AllocationAssert.None` / zero |
| `Resume_TheWalletComesBack` | a run saved at 84 Essence, killed, resumed / — / `RunState.Essence` is 84 — rule 9 |
| `Resume_TheOtherThreeComeBackEmpty` | the same run / — / `Veilrot` 0, both lists empty, no throw — rule 8 |
| `State_TheThreeReadsAnswerWithoutASystem` | a live run / — / `Veilrot` 0, both lists empty and non-null, and `RunState` exposes no meter, no shop and no ordeal set — rule 8 |

**Guard rows are implied, not listed:** `version` below 1 on the widened constructor, and every v3
guard still firing unchanged.

## Manual verification (Editor / device)

1. **[Editor]** Play to stage 3, quit to the menu, and open `run.json` in the persistent data path.
   It carries `"version": 4`, `"essence": 84`, `"veilrot": 0`, both counters at 0 and both arrays
   empty. Press `Continue`: the debug overlay reads 84.
2. **[Editor]** Replace the file with the checked-in v1 literal and press `Continue`. The run loads
   at stage 1 with an empty wallet and nothing in the Console. This is the only manual step that
   exercises three migration steps in one load.

## Out of scope

- **Cooldowns, and the question was asked rather than skipped.** `SaveDtos.cs`'s own remarks name
  **v4** as where they would be cheap — *"`_readyAt` is absolute against a clock that is itself
  restored exactly, so a v4 step is add-only"* — and the answer is still no, for the reason written
  there: the only safe spelling is a keyed pair of lists with its own length, duplicate and
  non-finite guards, which is a task rather than a field, and it has no owner. **It stays cheap at
  v5 for exactly the same reason it was cheap at v4**, and the known gap is unchanged: a `Continue`
  is a free cooldown reset, bounded by the longest cooldown.
- **Shields granted and live zones**, unchanged from the standing ruling.
- **A sixth random stream.** `RandomState` keeps its five, and that is a *format* decision made here
  because a sixth member would be a v5. [M6-05](../ROADMAP.md#m6--systems-complete)'s
  Pact roll belongs on `Offers` — it is part of the offer and happens in the same moment — and
  M6-06's Ordeal draw must pick one of the five: **`Affixes` is the candidate** (drawn by nothing
  until M7-02, and an Ordeal is the same kind of thing as an affix — a modifier on what you fight),
  with `Spawn` the alternative at the stated cost that every seed's wave sequence past stage 25
  changes. That choice is M6-06's; the constraint is this task's.
- **The `PlayerProfile` bump.** GD §14.2's unlocks, GD §14.1's archetype set and M6-10's locale are
  all profile fields, and the two formats version independently (`SaveMigrations`' own remarks).
  **M6-09** is the profile's one bump, by the same argument this task makes for the run's.
- **Any field's writer.** Essence is [M6-01a](M6-01a-essence-wallet-and-drops.md)'s and already
  exists; the other three are their own tasks', and none of them bumps the version.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
