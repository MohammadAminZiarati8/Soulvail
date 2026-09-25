# M4-05a — Death pays: the Shard payout, computed where the run ends

**Size:** S · **Depends on:** M4-01b (`ModeSpec.TryGetBossFor`, the authored boss roster), M2-10 (`StageFlow` advances the depth) · **Branch:** `m4-05a-shard-payout`
**Design refs:** GD §14.1, §14.3; AR §5, §8, §18.1; ADR-0004, ADR-0006 · **Ledger rows:** none directly — **[row 5](../ROADMAP.md#carry-forward-into-m4) is [M4-05b](M4-05b-profile-v3-and-shard-writer.md)'s**, and this task deliberately persists nothing so that it is not a second format bump wearing a payout's clothes

## Goal

GD §14.1's *"Earned on death regardless of outcome"* becomes a number the game actually computes: the run ends, core says what it was worth, and exactly one event carries it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Run/ShardPayout.cs` | Core | the arithmetic: a pure function of the depth reached and the mode's authored boss roster |
| `Tests/Core/Run/ShardPayoutTests.cs` | Tests.Core | the two terms, the shipped `Descent.asset` numbers, and the term that is *not* here |
| *small edits* | Core | `Core/Events/RunEvents.cs` — the `ShardsAwarded` struct (an event struct; [listed, not counted](../ROADMAP.md#how-to-read-this)); `Core/Run/RunSession.cs` — one publish inside the `IsDead` branch, above `End()` (rule 5) |
| *tests* | Tests.Core | `RunSessionTests` — the publish happens on death, once, in order; and **does not** happen when `End()` is called for any other reason |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Run;

/// <summary>GD §14.1, minus the term this build cannot compute. See the type's remarks.</summary>
public static class ShardPayout
{
    public const int PerStage = 10;
    public const int PerBoss = 50;

    /// <param name="deepestStage">RunState.StageIndex at the moment of death. Below 1 is refused.</param>
    /// <param name="mode">The run's mode, for its authored boss roster. Null is refused.</param>
    public static int For(int deepestStage, ModeSpec mode);

    /// <summary>How many boss stages lie strictly below <paramref name="deepestStage"/> (rule 3).</summary>
    public static int BossesKilled(int deepestStage, ModeSpec mode);
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>What the run just paid. Published on the death path and nowhere else (rule 5).</summary>
public readonly struct ShardsAwarded
{
    public readonly int Total;          // what this run is worth
    public readonly int DeepestStage;   // the first term, so a screen can show the breakdown
    public readonly int BossesKilled;   // the second term

    public ShardsAwarded(int total, int deepestStage, int bossesKilled);
}
```

## Behaviour

1. **`RunEnded` gains nothing, and that is a ruling rather than an omission.** The tempting move is M4-04's: a
   readout's facts ride the event. It does not apply here, for two reasons and the second is the load-bearing
   one. First, the payout is computed *in core from core's own state* — nothing has to cross the Game boundary
   for the arithmetic to happen, so a field on `RunEnded` would be core telling itself something it already
   knows. Second, and decisively: **`RunEnded` is also published when `RunScope` is torn down for any other
   reason** — `RunSession.End` is a deliberate no-op-if-not-running so disposal can call it blind
   (`RunSession.cs:1253`), and `SaveWriter.cs:78`, `HudPresenter.cs:50` and `PausePresenter.cs:64` have each
   already refused that event for exactly this. A payout riding `RunEnded` would pay a player for quitting to
   the menu, and would pay them again on every scope teardown. So the payout gets **its own event**, and
   M4-04's precedent applies to *that* one: `ShardsAwarded` carries its own breakdown, because the screen that
   draws it is a readout and must not hold a handle to the thing that computed it.
2. **`ShardPayout` is a pure function and owns no state.** No counter, no field on `RunState`, no subscription.
   It is handed a depth and a `ModeSpec` and answers an `int`; two runs with the same two inputs pay the same.
   That is what makes every row in the Tests table a one-line arithmetic assertion rather than a simulation.
   **A `static class` here is not the thing AR §13 bans**, and `SaveMigrations` is the shipped precedent with
   the argument already written on it: what is banned is static *mutable* state and service location, and this
   class has no fields to mutate, nothing to reset under a disabled domain reload, and nothing anyone could
   reach a dependency through. If it ever needs a collaborator it becomes an injected object that day.
3. **Bosses killed is *derived* rather than counted, and the reason is that it then survives a resume for
   free.** Nothing in this game counts bosses — M4-01b ruled deliberately that there is no `BossDied`
   (`BossEvents.cs:15`), and `EnemyDied` carries a `SpecId` nobody tallies. A counter would have to live on
   `RunState`, which means it would have to live in `RunSnapshot` to survive a kill-from-recents, which is a
   **`RunSnapshot` v4** — a second format bump, in the one task that was split away from the format bump.
   **So it is derived instead, and the derivation is exact:** a stage is only left once
   `SpawnDirector.IsStageComplete` is true, and on a boss stage that property *is* `_bossCleared`
   (`SpawnDirector.cs:243`), which is set the moment the boss stops breathing. **Therefore every boss stage
   strictly below the current depth has had its boss killed**, and `ModeSpec.TryGetBossFor` — the authored
   roster M4-01b built, `Descent.asset`'s *every 5th* — is what says which stages those were. The count is
   `TryGetBossFor` asked once per stage in `[1, deepestStage)`.
4. **The one case rule 3 under-pays is named rather than hidden:** a player who kills the boss and then dies on
   the same stage before walking through the door is paid for the depth but not for that boss — at most 50
   Shards, on a stage they did not finish. **The alternative under-pays far worse and far more often** (a
   resumed run losing every boss it killed before the app died), and a fix costs a run-format bump. It is
   stated in *As built*, it is an M4-07 checklist line, and it is the M5 ledger's if anybody minds.
5. **The publish site is the `IsDead` branch of `RunSession.Tick`, immediately above `End()`** — the same three
   lines that already make `RunEnded` follow `PlayerDied` on the same tick (`RunSession.cs:948`). That is what
   makes rule 1's refusal true rather than aspirational: `End()` is reachable from disposal and this line is
   not. The order on the wire is **`PlayerDied` → `ShardsAwarded` → `RunEnded`**, and a test asserts all three
   in sequence rather than each alone.
6. **GD §14.1's third term does not ship, and the spec says which and why.** The formula is
   `10·(deepest stage) + 50·(bosses killed) + 25·(new archetype first encountered)`. The third term is a
   **lifetime** fact — *first* encountered, across every run this install has ever played — so it is a
   `PlayerProfile` field holding a *set of `ContentId`s*, not a run fact, and it would drag a collection into
   the format bump [M4-05b](M4-05b-profile-v3-and-shard-writer.md) is already the review for. **Deferring it
   over-pays a returning player rather than under-paying them** — a profile with an empty set pays 25 the
   first time it meets a Husk, whenever that milestone arrives — which is the exact opposite of the Shard
   *total*, where not writing the number destroys it. **And the handover is cheap**: when it ships it needs no
   new run tracking either, because `ModeSpec.TryGetIntroduction` (`ModeSpec.cs:492`) already authors which
   archetype arrives at which stage, so a run's contribution is another walk over `[1, deepestStage]`. All it
   will need is the profile-side set. **`ShardPayout` is named for what it computes rather than for the
   formula**, and its remarks carry this paragraph so the next reader does not think the term was forgotten.
7. **`PerStage` and `PerBoss` are `const`s in core, and that is a knowing breach of ADR-0006** — the same
   breach `LevelUpFlow`'s Overflow 2 % is, which [ledger row 6](../ROADMAP.md#carry-forward-into-m4) already
   owns. They are GD §14.1's own numbers rather than a designer's tuning surface, there is no asset a payout
   belongs on (a payout is not a mode, a character or an enemy), and inventing one here would be M6's Sanctum
   arriving early and unspecified. **It is recorded as a fourth reader for row 6 rather than quietly done.**
8. **Non-finite and out-of-range inputs are refused at the door.** `deepestStage` below 1 throws — that is the
   value `default(RunSnapshot)` carries and `RunSnapshot`'s own constructor already refuses it — and a null
   `ModeSpec` throws. A run cannot reach this with either, which is precisely why the guard is cheap.
9. **Nothing in `Soulvail.Game` changes.** No writer, no screen, no registration. The event is published into a
   hub nobody is listening on, which is `M4-01b`'s shape — *"no player of this build can see any of it"* —
   and the two halves land one PR apart on purpose.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Payout_PaysTenPerStage` | stage 7, a mode with no boss roster / `For` / 70 (rule 2) |
| `Payout_PaysFiftyPerBossStagePassed` | stage 12, `Descent.asset`'s *every 5th* / `For` / 220 — 120 for depth, 100 for the bosses at 5 and 10 (rules 2, 3) |
| `Payout_DoesNotPayForTheStageDiedOn` | stage 5, every 5th / `BossesKilled` / **0** — the boss stage the player died on is not below the depth (rules 3, 4) |
| `Payout_ReadsTheAuthoredIntervalNotAFive` | a mode authoring *every 3rd* / stage 10 / 3 bosses, so no `stage % 5` can pass this (rule 3) |
| `Payout_IsTheSameForTheSameInputs` | the same depth and mode twice / `For` twice / equal, and no state was written (rule 2) |
| `Payout_HasNoArchetypeTerm` | the shipped `Descent.asset` at stage 1 / `For` / **10, not 35** — the row that pins rule 6 so nobody adds the third term without reading it |
| `Payout_RefusesAStageBelowOne` | 0 and −1 / `For` / `ArgumentOutOfRangeException` (rule 8) |
| `Payout_RefusesANullMode` | null / `For` / `ArgumentNullException` (rule 8) |
| `Payout_AllocatesNothing` | `For` over the shipped mode / `AllocationAssert.None` / zero — it is a walk over an authored array, and it runs on the death frame |
| `Run_AwardsShardsOnDeath` | a run killed at stage 4 / ticked / exactly one `ShardsAwarded`, `Total` 40, `DeepestStage` 4, `BossesKilled` 0 (rule 5) |
| `Run_AwardsShardsBeforeRunEnded` | a run killed / ticked / the wire reads `PlayerDied`, `ShardsAwarded`, `RunEnded`, in that order (rule 5) |
| `Run_AwardsNothingWhenTheScopeIsTornDown` | a live run / `End()` called directly, as disposal does / `RunEnded` published and **no** `ShardsAwarded` — rule 1's whole argument, asserted (rules 1, 5) |
| `Run_AwardsShardsOnceOnly` | a run killed, then ticked ten more frames / `ShardsAwarded` count / 1 (rule 5) |

**Guard rows are implied, not listed:** the event struct's field copy, and a run that dies on stage 1.

## Manual verification (Editor / device)

_None. Nothing in `Soulvail.Game` changes and nothing is drawn_ — the number exists on the wire and is read by [M4-06](M4-06-run-end-screen.md). The Console and the suite are the whole of this task's evidence.

## Out of scope

- **Persisting anything.** The Shard total on disk, `PlayerProfile` v3, the migration and the writer are all
  [M4-05b](M4-05b-profile-v3-and-shard-writer.md), and the split exists so [ledger row 5](../ROADMAP.md#carry-forward-into-m4)'s
  *"its own review"* is true of the format bump rather than of a task that also contains a formula.
- **Drawing it.** [M4-06](M4-06-run-end-screen.md). A `LocKey`, a screen and `English.asset` rows are that
  task's, and it is the one required to say what becomes of the death overlay.
- **GD §14.1's third term**, and the profile-wide first-encounter set it needs — rule 6.
- **A `RunSnapshot` bump so a resumed run keeps its boss count** — rule 4. A second format bump in this
  milestone is a task, not a line.
- **Anything a Shard buys.** GD §14.2's unlocks are M6-09a's and the Sanctum is M6-02b's.
- **Difficulty-modifier payout multipliers** (GD §18's ×0.7 / ×1.0 / ×1.4) — M8-03's, with the modifiers.

## As built

**The payout ships, every behaviour rule is met as written, and both of M4-00b's load-bearing rulings were
re-checked against the code before anything was built.** `2 162 / 0 / 0`, twice consecutively on the final
code (24.7 s), against M4-04's `2 145` — **+17, and the arithmetic lands to the row**: twelve cases in
`ShardPayoutTests` (eleven methods, one of them two `TestCase`s) and five in `RunSessionTests`. Nothing in
`Soulvail.Game` changed, nothing new is drawn, and no asset moved.

### The two rulings, confirmed rather than assumed

- **Rule 1 holds.** `RunSession.End` is still a no-op-if-not-running (`RunSession.cs:1254`) and still the
  only publisher of `RunEnded`. The three refusals still stand and two of them have moved a little, which
  is stated rather than left to be found: `SaveWriter`'s `Subscribe<PlayerDied>` is at **`SaveWriter.cs:82`**
  rather than 78, and `PausePresenter`'s remark now cites `SaveWriter.cs:128` rather than its own line 64.
  `HudPresenter.cs:50` is exact. **`Run_AwardsNothingWhenTheScopeIsTornDown` was written second, before any
  of the payout arithmetic**, so the refusal was a failing-then-passing row rather than a comment.
- **Rule 3 holds.** `SpawnDirector.IsStageComplete` still branches on `IsBossStage` and still returns
  `_bossCleared` — at **`SpawnDirector.cs:243`**, the line the spec named, unmoved. `Descent.asset` still
  authors one row, `boss.warden` on every 5th, so `Payout_ReadsTheAuthoredIntervalNotAFive` drives **every
  3rd** and asserts 3 bosses at stage 10, where a `stage % 5` would answer 1.

Every other type the spec named is where it said: `ModeSpec.TryGetBossFor` (`ModeSpec.cs:531`),
`ModeSpec.TryGetIntroduction` (`ModeSpec.cs:492`, exact), the death branch of `RunSession.Tick`
(`RunSession.cs:948`, exact). **Nothing had moved that changed a decision.**

### One deviation, and it is an existing test rather than a file

**`Assets/_Project/Tests/Core/Ai/SpitterBehaviourTests.cs` gained one line.**
`Session_ShotThatKills_EndsTheRunWithNoIntent` asserts the *whole wire* of the death tick as an ordered
array of types, and it was the only row in the project that did — so the first suite run came back
`2 161 / 1`, with the failure reading *"Expected `RunEnded`, but was `ShardsAwarded`"* at index 3. That is
the row doing its job: `typeof(ShardsAwarded)` was inserted between `ProjectileImpacted` and `RunEnded` and
the comment now says why. **It is a small additive edit to an existing file** — the ROADMAP's sizing rule
does not count one — so the counted total is still the table's two new files, and size **S** stands. It is
also, unexpectedly, the strongest evidence in the suite that the payout is on the death path: a bolt killed
a player in a fixture with no interest in Shards, and the run was paid for it.

**No other deviation.** `RunEvents.cs`' `RunEnded` remark carried the clause *"M4-05's payout reads
`PlayerDied`"*, which stopped being true the moment this task chose its own event; it now names
`ShardsAwarded` instead. That is a stale sentence in a file the table already lists, corrected rather than
left to mislead.

### What the publish site actually does, and the one cost it pays

The mode is **looked up rather than held**: `_catalog.Mode(State.ModeId)`, because `RunState` already
carries the id and a second field for it could only disagree. `BossesKilled` is then asked twice — once for
the event's breakdown and once inside `For` — which is **one redundant walk of an authored array on the one
frame a run ever ends**, paid instead of re-declaring GD §14.1's arithmetic at the call site. Said out loud
because it is the kind of thing that looks like an oversight.

### Rules 6 and 7, discharged rather than done quietly

- **Rule 6: the third term does not ship**, and `Payout_HasNoArchetypeTerm` asserts **10 and
  `Is.Not.EqualTo(35)`** at stage 1 of the shipped Descent — 35 being what all three terms would pay a run
  that met its first Husk there. The reason lives in `ShardPayout`'s own remarks, so the row and the
  paragraph point at each other.
- **Rule 7: `PerStage` and `PerBoss` are `const`s**, and [ledger row 6](../ROADMAP.md#carry-forward-into-m4)
  **already records this class as its knowing fourth reader** — M4-00b wrote that in, so nothing had to be
  added to the ledger here. No asset was invented.

### Rule 4's under-payment, as promised

A player who kills the boss on stage 5 and dies there before walking through the door is paid **50 rather
than 100**. `Payout_DoesNotPayForTheStageDiedOn` asserts it in both directions — 0 bosses at stage 5, 1 at
stage 6 — so the case is pinned rather than described. It is an M4-07 checklist line and the M5 ledger's if
anybody minds.

### Three things a reader of the tests should know

- **`Payout_HoldsNoState` is `SaveMigrations`' reflection row, copied deliberately.** It is what makes
  rule 2's *"a `static class` here is not what AR §13 bans"* checkable rather than asserted, and it is the
  row that fails the day this class grows a field.
- **`RunSessionTests` gained a fixture device, not an archetype.** Nothing in that fixture could kill a
  player — its Husk is `Static`, which does nothing by definition — so the four payout rows needed one
  `Chaser` with 400 contact damage, a 2 m reach and a 0.05 s windup, spawned at the player's feet. The
  snapshot reports no enemies on purpose: `EnemySystem.Ingest` only overwrites the agents a snapshot names,
  so a body nobody reports stays exactly where the plan put it and nothing has to simulate a walk.
- **`Run_AwardsShardsOnceOnly` asserts the ten further ticks *throw*.** The spec's row says *"ticked ten
  more frames"*; a session that has ended refuses `Tick` with `InvalidOperationException`, so the
  once-only property is **guaranteed by the run stopping** rather than guarded by a flag. Written as the
  code makes it true, and named here because the row reads differently from the table.

**One finding about a file this task did not write, for the seventh time in eleven tasks:**
`ProjectSettings/TimeManager.asset` was re-serialised into the `serializedVersion: 2` count/rate pair worth
exactly 0.02 again. Reverted here, and it will come back for whoever opens the Editor next.
