# M4-01b — A boss is a combatant with phases: 66/33, an invulnerable beat, and adds

**Size:** M · **Depends on:** M4-01a, M2-05, M2-06, M2-12b · **Branch:** `m4-01b-boss-agent-framework`
**Design refs:** GD §9.1 (all seven rules), §9.2, §7.1; AR §18.1, §18.4; ADR-0006 · **Ledger rows:** [M4 row 1](../ROADMAP.md#carry-forward-into-m4) is inherited rather than answered — see *Out of scope*

## Goal

A stage can be a **boss stage**: the director spawns one authored boss instead of waves, it crosses 66 % and
33 % of its health into a new phase with a brief invulnerable beat that clears the adds, and it can summon
more. The Warden's actual attacks are M4-02's; this is the thing they hang on.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/BossSpec.cs` | Core | Phases, their thresholds, the beat's length, and what each phase may summon |
| `Core/Ai/BossBehaviour.cs` | Core | `IEnemyBehaviour` that owns the phase machine and delegates the rest |
| `Core/Ai/BossPhases.cs` | Core | The threshold crossing, the beat, and the add-clear — pure, and the thing the tests drive |
| `Tests/Core/Ai/BossPhasesTests.cs` | Tests.Core | Crossings, the beat, hysteresis, and the orderings |
| `Tests/Core/Content/BossSpecTests.cs` | Tests.Core | Authoring refusals, and the roster lookup |
| *small edits* | Core | `ModeSpec` gains a boss roster (rule 1); `SpawnDirector` branches once on *"is this a boss stage"*; `EnemySpawned` gains nothing — rule 7 |
| *ripple* | Tests.Core | `ModeSpec`'s constructor gains a parameter: `ModeDefinition` and the mode fixtures |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Content/BossSpec.cs
public sealed class BossSpec
{
    public BossSpec(ContentId id, ContentId enemySpecId, IReadOnlyList<BossPhaseSpec> phases,
                    float beatSeconds);
    public ContentId Id { get; }
    /// <summary>The EnemySpec the boss's body, health and contact damage come from (rule 2).</summary>
    public ContentId EnemySpecId { get; }
    public IReadOnlyList<BossPhaseSpec> Phases { get; }
    public float BeatSeconds { get; }
}

public sealed class BossPhaseSpec
{
    /// <param name="entersBelow">Health fraction at or under which this phase begins. 1.0 for the first.</param>
    public BossPhaseSpec(float entersBelow, IReadOnlyList<AddWave> summons);
    public float EntersBelow { get; }
    public IReadOnlyList<AddWave> Summons { get; }
}

public readonly struct AddWave
{
    public AddWave(ContentId specId, int count);
    public ContentId SpecId { get; }
    public int Count { get; }
}

// Core/Ai/BossPhases.cs — pure; no agent, no events
public sealed class BossPhases
{
    public BossPhases(BossSpec spec);
    public int Current { get; }               // 0-based
    public bool IsInBeat { get; }
    /// <returns>true on the tick a new phase begins — the caller clears adds and telegraphs.</returns>
    public bool Tick(float hpFraction, float dt);
}

// Core/Events/BossEvents.cs
public readonly struct BossPhaseChanged   { public BossPhaseChanged(int enemyId, int phase, int ofPhases); }
public readonly struct BossBeatStarted    { public BossBeatStarted(int enemyId, float seconds); }
public readonly struct BossBeatEnded      { public BossBeatEnded(int enemyId); }
```

## Behaviour

1. **Which stages hold a boss is authored on the mode, not computed and not hardcoded.** `ModeSpec` gains a
   boss roster with the same shape its enemy roster already has — `(bossId, everyNStages)` — so
   `Descent.asset` says *"every 5th"* in a field rather than a `stage % 5 == 0` anywhere in code. **The owner
   ruled this at M4-00a**: *"we should be able to select which stages what enemies should have."* A stage
   editor is the eventual shape and is a [parking-lot](../ROADMAP.md#parking-lot) line; the field is what makes
   it cheap later and what stops M7's second boss being a code change.
2. **A boss is an `EnemyAgent` with a `BossBehaviour`, not a new kind of object.** It spawns through
   `EnemySystem`, takes damage through `Health`, is targeted by the same `Targeter` and is pooled by the same
   `EnemyRegistry` — which is why `BossSpec` names an `EnemySpecId` rather than restating a body. **The
   alternative was weighed at M4-00a and refused**: a parallel boss system means two spawn paths and two damage
   paths, which is the duplication M2 spent three tasks removing, and every later system (Elites, Ordeals,
   affixes) would have to know about both.
3. **Phases enter at a health fraction and never leave.** `EntersBelow` is 1.0, 0.66, 0.33 for the Warden.
   **Crossing is one-way and latched** — healing a boss above 66 % does not put it back in phase 1 — because
   the beat clears adds and resets pressure, and a boss oscillating across a threshold would clear the arena
   every few seconds. That is hysteresis by construction rather than by a tolerance.
4. **A phase change opens a beat, and during the beat the boss is invulnerable and does not act.**
   `EnemyAgent.IsVulnerable` already exists (`EnemyAgent.cs:180`) and is what the beat drives — so the damage
   path needs no edit at all, which is rule 2 paying for itself. The beat is `BeatSeconds` long, authored, and
   **GD §9.1 rule 3's *"clears adds"* happens on the beat's first tick**, not its last: the point is to reset
   pressure *now*, and a player who has just been told a phase is changing should see the arena empty
   immediately.
5. **Adds are summoned through the existing spawn path and count against the concurrency cap.** An `AddWave`
   is `(specId, count)` and goes through whatever `SpawnDirector` already uses, so a summoned Husk is a Husk in
   every way that matters. **A summon that would exceed the device cap is clamped, not queued** — a boss that
   banks summons and releases twenty at once on a cheap phone is a frame-rate bug wearing a design hat.
6. **`BossPhases` is pure and takes `(hpFraction, dt)`.** No agent, no events, no registry — so the whole of
   GD §9.1 rule 3 is testable without a world, which is how M3-01a's `LevelTracker` and M3-08a's `LevelUpFlow`
   were built and is why both have exhaustive suites. `BossBehaviour` is the thin part that reads
   `Health`, calls `Tick`, and publishes.
7. **`EnemySpawned` gains nothing and there is no `IsBoss` flag on it.** M3-13b added `IsElite` as a defaulted
   field and that was right for a *look*; a boss is not a look. **What a view needs is the phase count**, which
   arrives on `BossPhaseChanged` with `ofPhases` — so M4-04's segmented bar learns its segment count from the
   first event rather than from a flag, and no existing event signature moves. Re-examined here rather than
   copied, because the precedent is nearby and does not apply.
8. **Boss health is authored on the `EnemySpec` like every other body** (ADR-0006), and **a test asserts the
   fight lands in GD §9.1 rule 5's 75–120 s** against expected player DPS at stage 5, computed from the shipped
   assets rather than typed. **The owner delegated this at M4-00a and the reasoning is M3-12c's:** deriving HP
   from DPS at spawn would put a balance number in code, which is exactly what ADR-0006 exists to stop and what
   M3-15 just flagged Overflow's 2 % for. The test is the thing that keeps the authored number honest, and it
   **names what it excludes** the way `Ttk_ExcludesSurvivabilityAndReach` does.
9. **Ordering: the phase check runs after damage is applied and before the behaviour acts**, so a hit that
   crosses 33 % opens the beat on the same frame it landed rather than one later. Recorded in
   [AR §18.1](../../Architecture.md#181-ordering) if it survives the build.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Phase_StartsAtZero` | a three-phase spec / constructed / `Current` is 0 and `IsInBeat` is false |
| `Phase_EntersAtTheThreshold` | 0.66 / hp driven 0.67 → 0.66 / `Tick` returns true exactly once |
| `Phase_IsLatchedAgainstHealing` | phase 1 entered / hp healed to 0.9 / `Current` stays 1 and no crossing is reported |
| `Phase_SkipsWhenOneHitCrossesTwo` | hp 0.7 → 0.2 in one hit / ticked / lands in the **last** phase it passed and reports **one** crossing, not two beats |
| `Beat_HoldsForTheAuthoredSeconds` | beat 1.5 s / ticked at 0.1 / `IsInBeat` true for 15 ticks and false on the 16th |
| `Beat_MakesTheBossInvulnerable` | a crossing / during the beat / `IsVulnerable` is false, and damage applied in it changes no health |
| `Beat_ClearsAddsOnItsFirstTick` | four adds alive / a crossing / all four are gone on that tick, not at the beat's end (rule 4) |
| `Beat_BossDoesNotActDuringIt` | a crossing / during the beat / the delegated behaviour is never ticked |
| `Summon_GoesThroughTheSpawnPath` | phase 2 with `(enemy.husk, 3)` / entered / three Husks exist and are ordinary agents — same registry, same pool |
| `Summon_ClampsToTheConcurrencyCap` | cap nearly full / a phase wanting six / spawns what fits, drops the rest, and **does not queue them** (rule 5) |
| `Boss_SpawnsOnAnAuthoredStage` | roster *every 5th* / stage 5 / one boss and no wave; stage 4 and 6 / waves and no boss |
| `Boss_StageRuleIsAuthoredNotHardcoded` | a mode authored *every 3rd* / stages 1–9 / bosses at 3, 6, 9 — rule 1, asserted against a value no shipped asset uses |
| `Boss_FightLengthIsInsideTheBand` | expected player DPS at stage 5 from the shipped assets / `Warden.asset`'s HP / between 75 and 120 s, and the row **names its exclusions** (rule 8) |
| `Phase_TickAllocatesNothing` | 10 000 ticks across crossings / `AllocationAssert.None` / zero — it is a per-frame path |
| `Order_CrossingIsSeenTheFrameTheHitLands` | a hit crossing 33 % / one frame / the beat is open in that frame's events, not the next (rule 9) |

**Guard rows are implied, not listed:** a phase list that is empty or out of order, `EntersBelow` outside
(0, 1], a non-finite beat, a summon count below 1, and an `EnemySpecId` no catalog knows.

## Manual verification (Editor / device)

1. **[Editor]** Play to stage 5. *Expected: no wave — one large body, and the debug overlay's enemy count is 1.*
2. **[Editor]** Damage it past 66 %. *Expected: it stops moving and stops taking damage for the authored beat,
   any adds vanish, then it resumes.*
3. **[Editor]** Past 33 %. *Expected: the same again, and the phase readout on the overlay reads 2 of 3.*
4. **[device]** Whether the beat reads as *"something is about to change"* or as *"the game froze"* — GD §9.1
   rule 3's entire claim, and unanswerable in an Editor at 30 fps. Deferred to
   [M4 ledger row 3](../ROADMAP.md#carry-forward-into-m4).

## Out of scope

- **The Warden's actual attacks** — shield-slam, fissures. M4-02. This task spawns a body that changes phase
  and summons; what it *does* in a phase is content.
- **The arena hazard and the boss's view** — M4-03.
- **The segmented health bar** — M4-04, which has owned it since M2-15 and reads its segment count from
  rule 7's event.
- **A boss holding a skill or a buff.** M4-01a built the seam; **nothing here walks through it**, because the
  Warden needs none. M7's Archon is the first boss that does, and it will find the seam already built.
- **Archons, biome transitions, stage 20** — GD §9's *"every 20th is an Archon"* is M7.
- **Ordeals stacking on repeat bosses** — M6.
- **M4 ledger row 1 — the unreadable HUD.** This task adds no readout. **It inherits the row rather than
  escaping it**: M4-04's bar lands on the same HUD, and M4-07's acceptance will be asked the same readability
  question M3-15 could not answer.

## As built

**Five counted files — the Files table exactly, no split.** `BossSpec.cs`, `BossPhases.cs`,
`BossBehaviour.cs` and the two test fixtures. `Core/Events/BossEvents.cs` is a sixth new file and is
**not** counted: the ROADMAP's criterion names *"an event struct"* as the shape that does not count,
and this is three of them and nothing else. It is a deviation from the Files table all the same —
the table does not list it and the Public API does, which is the precedence rule settling in the
Public API's favour for the one case the two disagree. Nine additive edits, none of which changed an
existing line's behaviour: `ModeSpec` (a roster type, a trailing optional parameter, a lookup and a
copier), `ContentCatalog` (a dictionary, a trailing optional parameter, two lookups), `EnemySpec`
(one appended enum member), `EnemyAgent` (an `internal set` and a comment), `EnemySystem`
(`SpawnBoss`, `DespawnAtEndOfTick` and its drain, one `switch` case), `SpawnDirector` (three fields,
one branch in each of `Begin`/`Tick`/`IsStageComplete`/`Clear`, and `TickBoss`), `StageFlow` (one
line), `ModeDefinition` (a serialized field, a builder and a row struct), and `Descent.asset` (one
line — see below). **`EnemySystem.cs` is the largest of the nine** and is the one a reviewer should
look at as if it were counted.

### The two things M4-01a handed this task

**1. How a `SkillRunner` reads an enemy's blackboard: it does not, and the refusal is the ruling.**
M4-01a rule 7 put `HpFraction` and `ShieldFraction` on `EnemyBlackboard` and deliberately did not
widen `TriggerSpec`, leaving the shape to this task. **The shape is still not built, on purpose**,
and the reason is that the code cannot yet answer what it would mean. `TriggerClause.IsMet` takes a
`CombatBlackboard`; of its nine `TriggerField` members, **four are meaningless to an enemy**
(`IncomingProjectiles`, `Veilrot`, `StationaryTime`, `FocusRampLevel` are all facts about a *player*)
and **three are defined against the player's senses** (`EnemiesWithin6m`, `EnemiesWithin8m`,
`EnemiesInAcquireRange` — for a boss, is the player one of them? are its own adds?). So the two that
would work are the two M4-01a added, and the other seven need a design answer nobody has written.
Three candidate shapes exist — an `ITriggerBlackboard` both implement, a second `IsMet` overload, a
narrowed enum — and choosing between them for a caller nobody has written is exactly the guess AR §6
bans. **The Warden holds no skill** (this spec's *Out of scope* says so outright), so the first real
caller is **M7's Archon**, and it will arrive with the seven answers this task cannot supply. The
fields stay, doing what they already do: they are what `BossPhases` would read if it read a
blackboard, and rule 2 below is why it does not.

**2. A corpse reads as healthy, and there is a row that would catch trusting it.**
`BossPhasesTests.Boss_ACorpseIsNotAHealthyBoss` kills the boss outright from full health and then
asserts, in this order, that `Blackboard.HpFraction` is still **1.0** (the last *living* reading —
`EnemySystem.Perceive` skips the dead, M4-01a's `Blackboard_ACorpseKeepsItsLastReading`), that
`Health.Fraction` is **0**, that `IsAlive` is false, that **no `BossPhaseChanged` beyond the spawn
announcement was published** — a machine driven off the fraction would have crossed both thresholds
on the corpse — and that `SpawnDirector.IsStageComplete` is **true**, which is where trusting the
fraction actually bites: a boss stage that waited for a frozen 1.0 to reach zero would wait for the
rest of the run. **`BossPhases` is driven from `Health.Fraction` and the director asks `IsAlive`**,
and both are written onto the types rather than left as comments.

### Rulings this task made

**Rule 4's beat needed no edit to the damage path, and it needed two flags rather than one.**
`EnemyAgent.IsVulnerable` answers *"should auto-aim point at it"* (`TargetScorer` reads it);
`Health.SetExternalInvulnerable` answers *"can damage land"* (`Health.ApplyDamage` has returned
`DamageResult.Blocked` for it since M1-10). **The spec names only the first, and the first alone
would have left the boss taking full damage through its beat** — `EnemySystem.ApplyDamage` checks
`IsAlive` and never `IsVulnerable`, which the Tests table's *"damage applied in it changes no
health"* would have caught on the first run. So `BossBehaviour.SetBeat` raises and lowers both. **No
line of `EnemySystem.ApplyDamage` or `Health.ApplyDamage` changed**, which is the instruction kept.

**The add-clear cannot call `Despawn`, and the fix is not the backwards walk `EnemySystem` predicted.**
`EnemyRegistry.Despawn` is an order-preserving *shift*: it moves the tail of the array
`Registry.Alive` spans down and nulls the slot that falls off the end. The beat clears the adds from
inside the behaviour pass, which walks a span taken once — so an immediate despawn would move the
agents the pass had not reached and walk it past its own length. `EnemySystem.Tick`'s remarks have
named this since M2-08 and predicted a backwards walk; **a backwards walk would have changed the
order every enemy in the arena acts in**, which is the order `TargetScorer`'s tie-break reads and the
order a seeded run replays. So a behaviour queues through **`EnemySystem.DespawnAtEndOfTick`**, the
drain runs below the loop, the ban on calling `Despawn` from inside the pass stands, and the
paragraph in `EnemySystem` was rewritten to say which answer was taken and why. The queue is also
what makes summoning safe on the same tick: nothing is freed during the walk, so a summon cannot
recycle an agent the span still points at.

**`EnemyBehaviourKind.Boss` is the one kind `EnemyAgent.Initialise` leaves without a behaviour.**
A `BossBehaviour` needs the `BossSpec`, and the naming runs from the boss *to* the body, so an
`EnemySpec` cannot produce one — the `as`-and-keep trick every other kind uses would hand a recycled
agent the previous boss's phases. `EnemySystem.SpawnBoss` is the one caller that holds both halves,
`EnemyAgent.Behaviour` gained an `internal set` for it, and `EnemySystem.Tick` **throws** on a
Boss-kind agent with no behaviour rather than null-checking past it —
`Boss_AnAgentAuthoredBossWithNoBehaviourIsLoud` is the row.

**A boss announces phase 0 when it stands up, which is what makes rule 7 pay.** `EnemySpawned` gained
nothing, as ruled; but a bar that learned its segment count from the *first* `BossPhaseChanged` would
have learned it at 66 %, three-quarters of the way through the fight. So `SpawnBoss` publishes
`BossPhaseChanged(id, 0, ofPhases)` immediately after the attachment, and M4-04 is sized at the moment
the body appears.

**The first matching boss-roster row wins, and the ordering is a designer's to know.** GD §9 puts an
Archon on every 20th stage *and* a boss on every 5th, and stage 20 is both. Resolved by row order
rather than by *"the rarer interval wins"*, because the latter is a rule nobody wrote down and the
order of rows in an Inspector list is something a designer can see. `Roster_TheFirstMatchingRowWins`
pins it; two rows on one interval are refused outright, because the second could never be reached.

**A boss stage's body stands up on the director's first tick, with no telegraph ring.** GD §7.1's ring
is a promise about a body appearing where the player is about to be standing; a boss arrives after the
two seconds of arrival the stage already owes, is the only thing in the arena, and its arrival is
M4-03's to dress. It still owes `MinPlayerDistance` and still costs exactly one draw
(`Boss_ABossStageOwesThePlayerTheSameClearance`), which is why it happens on a tick rather than in
`Begin` — `Begin` does not know where the player is. `WaveStarted(stage, 1, 1)` and `WaveCleared` are
reused rather than new events, so a HUD that already draws *"wave 1 of 1"* needs no second vocabulary.

**The stage is over when the boss is down, whatever its adds are doing.** Stated rather than
engineered around: an add still standing during `Clear` and `Gate` can hit the player, and
`EnemySystem.Clear` takes it at the boundary like every other body. The alternative wanted the
director to know which agents a behaviour had summoned, which is a handle on a behaviour the director
has no business holding. Named here so M4-02 or M4-03 can revisit it with the fight in front of them.

**The adds appear on a derived ring, never a drawn one.** `BossBehaviour.AddRingRadius` is 3 m and is
**flagged as invented** — GD §9.1 rule 4 says a boss summons adds and does not say where they stand.
Evenly spaced and computed, so summoning consumes no randomness and a seeded run replays a boss fight
identically. The ceiling for a summon is `EnemyRegistry.Capacity` — the device cap `RunSession` built
it at, and the only one a behaviour can see; the stage's authored concurrency is the director's and a
boss stage has no waves to pace.

### Three deviations, each stated

**1. `Boss_FightLengthIsInsideTheBand` asserts a window, not an asset — because the asset is
M4-02's.** The spec's row reads *"`Warden.asset`'s HP"*. `Warden.asset` does not exist, and
**[M4-02's own Files table](M4-02-warden-behaviours.md) owns it**, along with `WardenBoss.asset` and
*"the mode's boss roster entry"*. Two specs disagree, and the one that owns the file wins. So the row
pins the half this task can pin and M4-02 has to land inside: **at stage 5, against the shipped
player's 50.232 DPS and `h(5) = 1.24`, GD §9.1 rule 5's 75–120 s is an authored-HP window of
3 038.23 – 4 861.16.** Every number is computed through shipped code — a real `PlayerCombat` with Keen
Censer and Zealotry applied through the real `EffectRegistry`, and a real `DepthScaling` applied to a
real agent — so a retune of the Censer, of either node, or of `h(n)` moves the window and reddens the
file. `Boss_FightLengthNamesWhatItExcludes` is the companion that names the five things the model
cannot see (the two beats, time spent not swinging, the adds, missed swings, the player dying) and
**prices the one it can**: with two 1.5 s beats outside the model, the honest ceiling is **4 739.63**,
and that is the number M4-02 should author under.

**2. `Descent.asset` gained one line, and a red run is what asked for it.** The first EditMode run came
back **2 057 / 1**, and the failure was
`ModeDefinitionTests.Descent_EveryYamlKeyBindsToAField` — which reserialises the asset and compares
the text. Adding `_bossRoster` to `ModeDefinition` makes a reserialisation write `_bossRoster: []`, so
the round trip differed. **The test is right and the asset was stale**, so the line stays: it is an
empty list, it authors nothing, and it is the same diff M4-01a's `_target` would have produced if any
shipped asset had been round-trip-tested. Two things worth recording: **this is the counter-example to
reading M4-01a's finding as "a new field is free"** — Unity does not rewrite an asset on disk because
a script grew a field, but a test that *forces* a reserialise is not reading the disk; and **the test
rewrites the asset as it fails**, so the working tree carried the fix before the failure had been
read. That is a useful property and a surprising one.

**3. The director rows live in `BossPhasesTests.cs`.** The Files table allows two test fixtures and
the Tests table has rows about a boss *stage* (`Boss_SpawnsOnAnAuthoredStage`, which needs a
`SpawnDirector`). They are in the `Ai` fixture under *"the orderings"*, which is what that file's row
in the table says it carries. The roster *lookup* half of the same claim is in `BossSpecTests`, where
the Files table puts it.

**Verified:** **2 058 / 0 / 0 EditMode, three runs** (27.7 s, 24.0 s, 24.8 s) against M4-01a's
**2 005** — **+53 rows, and the reconciliation is exact**: **31** in `BossSpecTests` (24 methods, of
which three are `[TestCase]` sets of 4, 5 and 2) and **22** in `BossPhasesTests`; no existing row was
removed. **PlayMode 16 / 0 / 0, twice** (9.1 s, 8.5 s) — [ledger row 4](../ROADMAP.md#carry-forward-into-m4)'s
experiment is still unrun and this task did not run it. **The first run was red at 2 057 / 1** and is
recorded above. Console swept after a **clean rebuild of every assembly**
(`RequestScriptCompilation(CleanBuildCache)`): **zero errors and zero warnings of any kind**, so no
compiler and no analyzer warning. `dotnet format whitespace --folder --verify-no-changes` over all
fourteen touched files: **exit 0**. **Nothing under `Data/` but the one `Descent.asset` line**, no
prefab and no scene moved. `ProjectSettings/TimeManager.asset` dirtied and reverted for the
**twentieth** time, same rational form (2 822 399 / 141 120 000 = 0.02, `m_TimeScale` 1).

### What a player of this build can see: nothing

**No shipped mode authors a boss roster row, and no `BossSpec` exists in any catalog.** That is
deliberate and it is M2-02 rule 10's bargain made again: `RunSession.Start` resolves content before it
announces a run, so a `Descent.asset` naming `boss.warden` before `WardenBoss.asset` existed would
refuse to start the game. **M4-02 authors all three in one change** — the body, the boss, and the
roster row — which is the same gap `Spitter.asset` sat in between M2-06 and M2-07b. **The spec's
manual verification steps 1–3 therefore cannot be run and are carried to M4-02**, which is the task
that makes stage 5 reachable; step 4 was already deferred to
[M4 ledger row 3](../ROADMAP.md#carry-forward-into-m4).
