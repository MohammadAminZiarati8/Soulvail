# M3-03 — `TreeRules` and `SkillTree`: gating, availability, keystone requirements, and the nodes a save carries

**Size:** M · **Depends on:** M3-02a (the specs), M3-05 (the registry), M3-01b (the field this fills) · **Branch:** `m3-03-tree-rules`
**Design refs:** CH §4, §5, §5.1, §6; AR §5, §18.1, §18.2, §18.3; ADR-0008, ADR-0009 · **Ledger rows:** 2 — fills `TakenNodeIds` and bumps nothing

## Goal

A run has a live tree: which nodes may be taken, what taking one does to the player's numbers, and how a resumed run gets its picks back in the right order before its hit points are clamped against a maximum those picks moved.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/TreeRules.cs` | Core | a class's tree as one run reads it: every id resolved, every cross-spec rule checked once, the structural questions |
| `Core/Progression/SkillTree.cs` | Core | the live state: taken, available, `Take`, `Restore` |
| `Tests/Core/Progression/TreeRulesTests.cs` | Tests.Core | cross-validation, locate, requirements |
| `Tests/Core/Progression/SkillTreeTests.cs` | Tests.Core | gating, taking, effects, restore |
| *small edits* | | `ProgressionEvents.cs` + `NodeTaken`; `RunState` + `internal SkillTree Tree`, reads `TakenNodeCount`, `IsTreeFull`, `TakenNodeIds`; `RunSession.Start` — `TreeRules` and the effect check inside the validation block, the tree built with the state, its restore **before** `Health.Restore` (rule 5); `RunRecorder.Take` passes `state.TakenNodeIds`; `RunSessionResumeTests` and `RunRecorderTests` — M3-01b's two placeholder rows flip (rule 9); **AR §18.1** gains the restore-order row |

Only these files change. Anything else is a deviation: say so in *As built*. **`RunSnapshot.CurrentVersion` stays 2** (M3-01b rule 1).

## Public API

```csharp
namespace Soulvail.Core.Progression;

/// A class's tree as one run reads it. Built in RunSession.Start before RunStarted, so an
/// authoring mistake refuses the run rather than the pick (ledger row 3's shape).
public sealed class TreeRules
{
    public TreeRules(SkillTreeSpec tree, ContentCatalog catalog);

    public SkillTreeSpec Tree { get; }
    public int Count { get; }

    public SkillSpec Skill(ContentId id);                                  // KeyNotFoundException for an id not in the tree
    public bool TryLocate(ContentId id, out int branch, out int tier);
    public bool IsKeystone(ContentId id);
    public int RequiredTakenInBranch(ContentId id);                        // tier − 1; a keystone: the branch's NodeCount − 1
    public bool TryGetParent(ContentId id, out ContentId parentId);        // true for an Upgrade only
    public int NodeCountOf(int branch);
}

public sealed class SkillTree
{
    public SkillTree(TreeRules rules, EffectRegistry effects, IDomainEvents events);   // refuses a node whose effect has no handler

    public TreeRules Rules { get; }
    public int TakenCount { get; }
    public bool IsFull { get; }
    public int TakenInBranch(int branch);
    public int OwnedActives { get; }

    public bool IsTaken(ContentId id);
    public bool IsAvailable(ContentId id);
    public int Available(Span<ContentId> destination);    // tree order; allocates nothing

    public void Take(ContentId id);                       // gate → record → effects → NodeTaken

    public IReadOnlyList<ContentId> TakenIds { get; }     // take order, a read-only view
    internal void Restore(IReadOnlyList<ContentId> takenInOrder);   // replays Take, silently
}
```

```csharp
namespace Soulvail.Core.Events;

/// A node was taken. After it is recorded and its effects are on, so a handler reads the new state.
public readonly struct NodeTaken
{
    public readonly ContentId SkillId; public readonly SkillKind Kind;
    public readonly int Branch; public readonly int Tier; public readonly int TakenCount;
}
```

```csharp
// RunState (added) — reads, never the handle (AR §18.2)
internal SkillTree Tree { get; }                 // null for a class with no tree (rule 10)
public int TakenNodeCount { get; }
public bool IsTreeFull { get; }
public IReadOnlyList<ContentId> TakenNodeIds { get; }   // empty, never null, when there is no tree
```

## Behaviour

**The rules**

1. **`TreeRules` cross-checks once, at `Start`, before `RunStarted`.** Every id in the tree resolves to a `SkillSpec` (`TryGetSkill`, so the message can say *which tree, which branch, which tier* rather than the catalog's bare "no skill with id"); a `Keystone`-kind node is the **sole node of its branch's last tier**, and a last tier holding a Keystone holds nothing else — a partial branch's last tier may be ordinary, which is M3-12's v1; an **Upgrade's parent is in the same branch at a lower tier**. The last is what stops a branch deadlocking: CH §4's *"only offered if you own the parent"* with a parent in another branch would make one branch wait on a pick the player may never make, and the offer would silently narrow. Any failure throws with the tree id, the node id and the reason, and — because it sits in `Start`'s validation block — with nothing announced and nothing standing (M2-02 rule 7).
2. **Gating (CH §5).** A node at tier N is available when it is not taken and `TakenInBranch(branch) >= N − 1`; a **Keystone** needs every other node of its branch (`RequiredTakenInBranch` is the branch's `NodeCount − 1` — CH §5's *"all preceding"*, which for a nine-node branch is 8 where the tier rule would say 4); an **Upgrade** additionally needs its parent taken. With layered tiers a branch exposes several nodes at once — two at level 1, three after one pick — which is what makes M3-04's draw a draw (M3-02a rule 7).
3. **`Available` fills in tree order** — branch 0 tier 1 in authored order, then tier 2, then branch 1 — and the order is load-bearing rather than cosmetic: M3-04 walks it with one draw per pick, so the same seed against the same tree state yields the same offer (AR §18.3's *one draw whatever it then finds*). Allocation-free: a walk over the spec's arrays against a `bool[]` of taken flags.
4. **`Take(id)`**: refuses an unavailable node with an `InvalidOperationException` naming which gate failed — taken, tier, keystone or parent — because M3-08's `ChooseOffer` is the caller and a wrong index there should read as *what* was wrong; then records (the take-order list, the per-branch count, the flag); then applies every `SkillSpec.Effects` entry through the registry with **the `SkillSpec` as the source** (M3-05 rule 7); counts an Active; and publishes `NodeTaken` **last**, so a handler reading `TakenCount` or the player's damage from inside it sees the node. **The registry cannot throw from inside `Take`:** the constructor asks `CanApply` for every take and cast effect of every node in the tree and refuses the tree otherwise — at `Start`, before `RunStarted` — so a half-applied node is not a state this class can be left in.
5. **`Restore(ids)`** replays `Take` for each id in order with the events suppressed (nothing publishes before `RunStarted`) and the gates enforced: an order that breaks a gate throws `ArgumentException`, because a save whose picks its own rules would have refused is corrupt rather than merely old, and a tree that disagrees with itself is worse than a refused resume; an id not in the tree throws `KeyNotFoundException` — a node this build no longer ships is content validation's answer at `Start`, M2-13a rule 10's argument for not refusing it in the DTO. Effects are applied on restore; that is how a resumed run's modifiers come back. **And it runs before `Health.Restore(hp, shield)`** — a `+20 max HP` node has to be on the stat before the absolute hit points are clamped against the maximum, or a run saved at 150 of 160 comes back at 140 of 160. An **AR §18.1** row, and the reason this task depends on M3-01b rather than the other way round.
6. **`PendingLevelUps` is saved, not derived** (M3-01b). It cannot be derived: an overflow level (M3-08) spends a pick with no node, and M6-02b's Banish removes nodes from the pool, so `Level − 1 − TakenCount` stops meaning "owed" the first time either happens.
7. **`NodeTaken` carries kind, branch, tier and count** so a HUD, the level-up screen and the Skills screen need no catalog lookup to react — `EnemyDied`'s reasoning. Core does not listen to it: M3-08's `ChooseOffer` handler tells M3-06's runner about a new Active directly, because core has no business subscribing to its own events (`RunSession`'s own remark on `PlayerDied`).
8. **`RunState` hands out reads and never the tree.** `Take` is public and a public handle would let a view grant a node; `TakenNodeCount`, `IsTreeFull` and the `TakenNodeIds` view are what the recorder, the overlay and M3-08 need (AR §18.2).
9. **The recorder writes `TakenNodeIds` from the view** and the DTO copies it (M3-01b rule 5). Two M3-01b rows flip rather than appear: `Recorder_NodesAreEmptyUntilM3-03` → `Recorder_CapturesTakenNodesInOrder`, `Start_IgnoresTakenNodesUntilM3-03` → `Start_RestoresTakenNodes`. No version bump, no migration step: the field has been in the format since v2.
10. **A class with no tree is legal until M3-12.** `TryGetTreeFor` false leaves `RunState.Tree` null — `_flow`'s precedent for a mode with nothing to compose — and the three reads answer 0, false and empty. M3-04 and M3-08 must handle it (no generator, no offer, a level banked); M3-14 pins that every shipped character has a tree, which is when the null branch stops being reachable in a build.

## Tests

| Test | Given / When / Then |
|---|---|
| `Rules_ResolvesEveryNode` | the 27-tree, a catalog holding all / ctor / `Count` 27, `Skill(id)` for each |
| `Rules_UnknownNode_ThrowsNamingWhere` | one id absent from the catalog / ctor / `KeyNotFoundException`, message names the tree, branch 1, tier 3 and the id (rule 1) |
| `Rules_KeystoneNotInLastTier_Throws` · `Rules_KeystoneSharesItsTier_Throws` | — / ctor / throws each |
| `Rules_LastTierNeedNotBeKeystone` | three branches of 2 / 2, all Passive / ctor / no throw (rule 1) |
| `Rules_UpgradeParentInOtherBranch_Throws` · `Rules_UpgradeParentAtSameOrHigherTier_Throws` · `Rules_UpgradeParentBelowInBranch_IsLegal` | — / ctor / throws, throws, no throw (rule 1) |
| `Rules_RequiredTaken_Tier` | a tier-3 node / `RequiredTakenInBranch` / 2 (rule 2) |
| `Rules_RequiredTaken_Keystone` | a nine-node branch's keystone / — / 8 |
| `Rules_TryLocateAndParent` | — / — / (branch, tier) for a member; false for a stranger; `TryGetParent` true only for the Upgrade |
| `Fresh_AvailableIsEveryTierOne` | the 27-tree, nothing taken / `Available` / 6 ids, in tree order (rules 2, 3) |
| `Take_OpensTheNextTier` | take branch A's first tier-1 node / `Available` in A / the other tier-1 node and both tier-2 nodes — 3 |
| `Take_Unavailable_ThrowsNamingTheGate` | a tier-3 node at the start / `Take` / `InvalidOperationException`, message names the tier gate (rule 4) |
| `Take_Twice_ThrowsNamingTaken` | — / `Take` twice / throws, message says taken |
| `Keystone_NeedsEveryOtherNode` | 7 of 8 taken in A / `IsAvailable(keystone)`; take the 8th / false; true (rule 2) |
| `Upgrade_NeedsItsParent` | parent at tier 1, upgrade at tier 2; take the *other* tier-1 node / `IsAvailable(upgrade)`; take the parent / false; true |
| `Take_AppliesEffectsWithTheSpecAsSource` | a node with `ModifyStat(WeaponDamage, PercentAdd, 0.15)` / `Take` / `Weapon.Damage.Value` 14.95; `CopyModifiersTo` shows the `SkillSpec` as `Source` (rule 4) |
| `Take_PublishesNodeTakenLast` | a handler that reads `TakenCount` and the damage inside the event / `Take` / 1 and 14.95 |
| `Take_CountsActives` | take a Passive, then an Active / `OwnedActives` / 0, then 1 |
| `Tree_UnregisteredEffect_ThrowsAtConstruction` | a node whose effect has no handler / `new SkillTree` / throws naming the node and the effect type (rule 4) |
| `IsFull_AfterEveryNode` | take all 27 in a legal order / `IsFull` / true; `Available` 0 |
| `Available_AllocatesNothing` | warm-up / 10 000 × `Available` / allocated-bytes delta == 0 (rule 3) |
| `Restore_ReplaysInOrder` | `[A1a, A2a, B1b]` / `Restore` / all taken, `TakenInBranch(A)` 2, effects on, **no events** (rule 5) |
| `Restore_GateBreakingOrder_Throws` | `[A2a]` / `Restore` / `ArgumentException` |
| `Restore_UnknownNode_Throws` | `[skill.ghost]` / `Restore` / `KeyNotFoundException` |
| `Start_RestoresTakenNodes` | a snapshot naming two ids / `StartResumed` / `State.TakenNodeCount` 2, the damage modified — `RunSessionResumeTests`, replacing M3-01b's placeholder (rule 9) |
| `Start_RestoresNodesBeforeHealth` | a `+20 MaxHp` node and hp 150 on a 140 class / `StartResumed` / `PlayerHp` 150 — 140 without the ordering (rule 5) |
| `Start_BadTreeFailsBeforeRunStarted` | a tree naming a stranger / `Start` / throws; not running; no `RunStarted`; nothing written (rule 1) |
| `Recorder_CapturesTakenNodesInOrder` | three takes / a boundary `Take` / `TakenNodeIds` the three, in take order — replacing M3-01b's placeholder |
| `State_HandsOutNoTree` | reflection / — / `Tree` is not public (rule 8) |
| `NoTree_ReadsAnswerEmpty` | a class with no tree / `Start` / `TakenNodeCount` 0, `IsTreeFull` false, `TakenNodeIds` empty, no throw (rule 10) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

None visible — nothing takes a node until M3-08, and no tree is authored until M3-12. The Editor check is that Descend and Continue still compose with an empty catalog.

## Out of scope

- **`ChooseOffer`, spending a pick, telling the runner about a new Active** — M3-08.
- **Overflow** — M3-08: a pick with nothing available becomes +2 % damage and +2 % max HP (CH §5.2), two `ModifyStat`s under a per-level source; the tree is not involved.
- **Banish** — M6-02b adds a banished set that `Available` skips; the shape is a second `bool[]`.
- **Respec** — none; CH §7's table deletes it with v0.1's meta systems, and CH §6 leaves nothing permanent to respec into.
- **The offer** — M3-04, which reads `Available` and nothing else here.

## As built

**Built to the Files table: four files and the small edits, plus the AR §18.1 row the table names.** `RunSnapshot.CurrentVersion` stays 2, no migration step, nothing in `Data/`, no prefab, no scene, no `ProjectSettings/` diff.

**The owner's ruling on rule 4's promise, asked before a line was written: `IEffectHandler<T>` does *not* gain `CanApply(TEffect)`, and rule 4 stands as written.** What was traced first, because the decision turns on it: the only throw left inside `ModifyStatHandler.Apply` is `PlayerStats.Resolve`'s, on an address that is not a member — `kind`, finiteness and a null source are each refused twice over, at `ModifyStat`'s constructor and again at `Modifier`'s — and **`ModifyStatDefinition.ToEffect` is the only non-test constructor of a `ModifyStat` in the project**, which is exactly where M3-02b put `Enum.IsDefined`. So for the one primitive that exists there is no reachable path to a handler throw, and the hole is a hand-constructed effect with a cast integer, which only a test can build. The general version was weighed and refused on three counts: it costs four files outside this table for coverage already bought; its diagnostics are *worse* (a node id, where the authoring door names the asset file and the field); and most implementations would be `=> true`, so a rubber-stamp member would make the promise look structural while still resting on each author. **The trigger for building it is named rather than left to memory** — the first primitive whose applicability is a *run-scoped* question, one no authoring check could answer because the answer depends on the run rather than the asset — and it is written into `SkillTree`'s own remarks as the rule a new primitive owes its own door to, plus a ROADMAP parking-lot line. Rule 4's literal promise (the registry's own `KeyNotFoundException` and its null guards) is met by the constructor sweep; its *spirit* — no half-applied node — is met per primitive, and the class says so in as many words rather than implying it.

**The contradiction in the spec's own Public API, resolved: `Restore` is `public`, not `internal`.** `Soulvail.Tests.Core` has no `InternalsVisibleTo` and deliberately never will (AR §18.2, M0-10), so the drafted `internal void Restore` could not be tested from `SkillTreeTests.cs` where the Files table assigns it. **The M3-05 precedent decides it** — `PlayerStats` is public and hands out live `Stat`s, sealed one layer out by `RunState.Effects` being `internal` — and one argument settles it beyond precedent: `Take` is already public, so anything holding a `SkillTree` could grant every node in a legal order by hand. An `internal Restore` beside a public `Take` is a lock on the back door of an open front one; the seal that does the work is `RunState.Tree`, which is `internal`, and `State_HandsOutNoTree` is the row that holds it. `LevelTracker.Restore` staying `internal` is not a contradiction — no test needs it, because its rows already live in `RunSessionResumeTests`.

**Five deviations, all additive or forced, none changing a decision.**

1. **`Start_RestoresTakenNodes` asserts `PlayerMaxHp`, not weapon damage.** The spec's row says *"the damage modified"* and **`RunState` has no read for weapon damage** — only `PlayerMaxHp`, `PlayerHp` and the rest of the health block. Adding one would be outside the Files table and would pre-empt M3-09b, which owns the reads a screen needs. The claim is unchanged (a restored node's effects are in force); the observable is a `+20 max HP` node taking a 140 class to 160. The damage arithmetic is asserted where `PlayerCombat` is directly reachable, in `SkillTreeTests`.
2. **`Recorder_CapturesTakenNodesInOrder` restores three nodes rather than taking three.** The spec says *"three takes"*; `RunState.Tree` is `internal` and nothing public takes a node until M3-08's `ChooseOffer`, so the only route to a run that owns three is starting one that already did. Not circular — `SkillTreeTests.Restore_ReplaysInOrder` is what says the replay works. The row is stronger for it: the restored order is **three, one, two**, so a recorder that read the tree's *shape* instead of its take list would come back sorted and fail.
3. **The tree's validation is split across two points in `Start`, both before `RunStarted`.** `TreeRules` is built in the validation block proper, above the composition; the `CanApply` sweep is the `SkillTree` constructor, which cannot run before the registry, which cannot exist before the live objects its handlers address. The cost is that a bad *effect* is reported after the opening composition has drawn, leaving the Spawn stream advanced — which is the trade the composition's own comment already accepts, and for its reason: the run it was drawn for does not exist. `Start_BadTreeFailsBeforeRunStarted` covers the first half; `Tree_UnregisteredEffect_ThrowsAtConstruction` the second.
4. **`Available` refuses a destination shorter than `TreeRules.Count`.** The spec's signature states the order and the allocation but not the capacity contract. Refused rather than truncated, because a short buffer would silently narrow M3-04's offer with no symptom anywhere; `Available_ShortDestination_Throws` is the row, and sizing by `Count` is the stated contract.
5. **One unused `using` removed.** `RunRecorder.cs` no longer names `ContentId` or `Array` once `Array.Empty<ContentId>()` becomes `state.TakenNodeIds`, so `using Soulvail.Core.Content;` went with it.

**One claim inherited from M3-01b that turned out to be wrong, and is corrected rather than acted on.** `RunRecorder`'s remarks and `RunRecorderTests.Take_AllocatesNothing` both said M3-03 was the task that would **retire** that row. It does not: the copy `RunSnapshot`'s constructor makes is `Array.Empty<ContentId>()` when the list is empty, and that row's run has no tree and has taken nothing — which is every run until M3-12. Both comments now say so, and name the real boundary: **the first node taken is the first boundary write to ask for heap.** `Recorder_NoTreeWritesAnEmptyList` is the row that pins the empty half on purpose rather than by accident.

**One file outside the Files table, named as a deviation: [Traps.md](../../Traps.md) §7 gains a row.** A `TestRunnerApi.RegisterCallbacks` registration outlives the run it was made for, so running EditMode and then PlayMode fires the EditMode collector a second time with the *PlayMode* result and silently overwrites its own output file. It cost time here — the four result files all read `passed=11` at the end, which looks like a suite that dropped from 1266 tests to 11 — and it will cost the next task that runs two suites in one session. The project's own rule is that a toolchain trap goes in Traps.md rather than in an *As built*, so it went there; the EditMode count was then re-confirmed a third time on a fresh path.

**Guard rows: a validation row per new type and a null row per public constructor, both present. No non-finite rows, because neither new type has a float door** — `TreeRules` takes a spec and a catalog, `SkillTree` takes three objects, and every number in the tree came through M3-02a's constructors.

**Verified: 1266 EditMode / 0 / 0, twice consecutively**, against M3-02b's 1218 — **48 new rows**, which is this table's 32 named rows (29 lines, two of them naming several) less the 2 that *replace* M3-01b placeholders, plus 18 implied guard and message rows the *As built* names below. Six assemblies, **zero compile errors, zero analyzer warnings**; all ten touched files confirmed in their intended assembly through `GetAssemblyNameFromScriptPath` and all five new types by direct `typeof`. The 18: `Rules_UpgradeParentNotInTheTree_Throws`, `Rules_IsKeystone`, `Rules_NullArguments_Throw`, `Rules_StrangerQuestions_Throw`, `Rules_NodeCountOfAStrangerBranch_Throws`, `Take_UnknownNode_Throws`, `Keystone_Refusal_NamesTheKeystoneGate`, `Take_AppliesEveryEffectOfANode`, `Tree_UnregisteredCastEffect_ThrowsAtConstruction`, `Available_ShortDestination_Throws`, `Restore_GateBreakingOrder_NamesTheEntry`, `Restore_EmptyIsOrdinary`, `Restore_Null_Throws`, `Tree_TakenIdsIsAView`, `Tree_IsTakenAndIsAvailableAnswerForAStranger`, `Tree_TakenInBranchOfAStranger_Throws`, `Tree_NullArguments_Throw`, `Recorder_NoTreeWritesAnEmptyList`.

**PlayMode 10/11 then 11/11, and the failure is the Known-issues row.** `FrameOrderTests.Ticker_RunsTheStepsInOrder`, with M2-15a's exact message — *"The cone found nothing where the body had just walked to"* — and the fixture passes **4/4 run on its own**. **It is not this task's, and that is checked rather than inherited:** `FrameOrderTests` drives a `RecordingCore : IRunSession` whose `State` property returns `null` outright (`FrameOrderTests.cs:564`), so not one line this task added — all of them in `RunSession.Start`, `RunState`, `RunRecorder`, `SkillTree`, `TreeRules` or `ProgressionEvents` — executes in it, and `RunTicker` was not touched. This is the first task since M3-01b whose change *could* plausibly have moved that row, and the honest reading is that it did not move it either way.

**Three rows worth naming as evidence rather than as counts.** `Start_RestoresNodesBeforeHealth` is the AR §18.1 row and **fails with the two lines swapped** rather than merely reporting a different number: 150 of 160 comes back 140 in the wrong order, and the fixture asserts out loud that its saved hit points sit above the class's own maximum and at or below the modified one, or the row would prove nothing either way. `Rules_RequiredTaken_Keystone` asserts `Is.Not.EqualTo(4)` beside its 8, because 4 is what the tier rule alone would say and the keystone exception is the kind of arithmetic that gets quietly dropped. And **three rows were wrong first and are honest now**: `Restore_ReplaysInOrder` expected 14.95 where the fixture's own default +5 % nodes make it 16.25, and now asserts three modifiers rather than one — so it fails if the replay stops after the first entry; and both `Tree_Unregistered*` rows passed an *empty* registry, which trips the sweep on the fixture's first node instead of the planted one, so they were green while proving nothing about what they planted.
