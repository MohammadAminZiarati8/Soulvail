# M5-07a-i — The run's tree stops being one tree

**Size:** M · **Depends on:** M5-06b · **Branch:** `m5-07a-i-splash-branch`
**Design refs:** CH §4, §5, §5.4; AR §18.2, §18.3; ADR-0006, ADR-0011 · **Ledger rows:** none — this is the **forcing question** [M5's spec-group note](../ROADMAP.md#m5--second-class) names and the [parking-lot](../ROADMAP.md#parking-lot) line has carried since M3-03

## Goal

`TreeRules`, `SkillTree` and `OfferGenerator` stop assuming that a run's nodes all come from one
tree: a branch borrowed from a second class can be installed beside the primary, gated by the same
tier rule, drawn from the same offer, and counted in a branch index that means something across both.

## The forcing question, and why it is this task's

[M5's spec-group note](../ROADMAP.md#m5--second-class) names two things that forced a spec group;
[M5-01](M5-01-projectile-weapon-and-leading.md) through [M5-04b](M5-04b-rise-and-minion-stats.md)
each say which one they answer and none of them answers this one. **This is it.**

[M3-03](M3-03-tree-rules.md)'s constructor takes a single `SkillTreeSpec`, and every branch-shaped
member is an index into that one tree — `TreeRules.TryLocate(id, out int branch, out int tier)`,
`NodeCountOf(int branch)`, `RequiredTakenInBranch`, `SkillTree.TakenInBranch(int branch)`,
`SkillTree`'s `_takenInBranch` array sized from `rules.Tree.Branches.Count`, and
`OfferGenerator.Draw`'s `stackalloc int[SkillTreeSpec.BranchCount]`. CH §5.4 needs a fourth branch
that is **not in that tree**, and the failure is not a refusal — it is silent and then loud in the
wrong place:

- `TreeRules.TryLocate` returns `false` with `branch = −1` for a foreign node, so
  `OfferGenerator.Draw`'s `drawnPerBranch[branch]++` is an **`IndexOutOfRangeException` on the
  second card of the first offer** that draws one.
- `OfferGenerator`'s `_candidates` and `_weights` are sized `rules.Count`, so
  `SkillTree.Available` — which refuses a destination shorter than the tree — would throw first, with
  a message about a buffer.
- `SkillTree.Take` on a foreign id throws `KeyNotFoundException` naming *content validation*, which
  is the one thing that is not wrong.

**The content side is already fine and stays fine.** A class still has exactly one tree, so
M3-02a rule 11's refusal stands and `ContentCatalog.TryGetTreeFor` resolves the second one unchanged.
What widens is the **run's** view — which is why nothing in `Core/Content/` is in the Files table.

## Why this is a task and not the whole of M5-07a

The ROADMAP's M5-07a row owns the branch-index limitation, the half-tree trigger, the splash screen
and the *no Keystone* rule. Counted against the shipped code that is seven counted files, and the
halves are reviewed against different documents: **what a run's tree is** is AR §18.2 and ADR-0011 —
an index space, a walk order and a seed — while **when the moment happens and what it looks like** is
CH §5.4 and GD §13.1. [M5-07a-ii](M5-07a-ii-the-half-tree-moment.md) is the second half.

**This task ships reachable by nothing**: `InstallSplash` is public, tested, and called by no
production code until M5-07a-ii. M4-01a's bargain, and what keeps this PR's review about the index
space.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/TreeRules.cs` | Core | **Substantial.** A primary tree, an optional borrowed branch, and one branch index space over both |
| `Core/Progression/SkillTree.cs` | Core | **Substantial.** `Flatten`, `_takenInBranch` and the walk order widen with it |
| `Tests/Core/Progression/TreeRulesTests.cs` | Tests.Core | **Substantial.** The index space, the Keystone refusal, and what an un-splashed run still answers |
| `Tests/Core/Progression/SplashBranchTests.cs` | Tests.Core | Gating, the offer, the walk order, and the seed |
| *small edits* | Core | `OfferGenerator` sizes its two buffers and its `drawnPerBranch` from `TreeRules` rather than from `SkillTreeSpec.BranchCount` (rule 6) |
| *ripple* | Tests.Core | `OfferGeneratorTests` and `SkillTreeTests` — constructors unchanged, but a fixture that asserts three branches now says *three, un-splashed* |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Progression/TreeRules.cs — widened
public sealed class TreeRules
{
    /// <summary>Unchanged: one class's tree, resolved and cross-checked.</summary>
    public TreeRules(SkillTreeSpec tree, ContentCatalog catalog);

    /// <summary>The primary tree as authored. Renamed from nothing — this is M3-03's `Tree`.</summary>
    public SkillTreeSpec Tree { get; }

    /// <summary>
    /// How many branches this run has: SkillTreeSpec.BranchCount, or one more once a branch has
    /// been borrowed. The number every branch-shaped member is indexed against (rule 1).
    /// </summary>
    public int BranchCount { get; }

    /// <summary>The borrowed branch's index, or -1 while none is installed.</summary>
    public const int NoSplash = -1;
    public int SplashBranch { get; }

    /// <summary>The class the borrowed branch came from, or default while none is installed.</summary>
    public ContentId SplashCharacterId { get; }

    /// <summary>
    /// Installs one branch of <paramref name="tree"/> beside the primary, at branch index
    /// SkillTreeSpec.BranchCount. Once per run — rule 4.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="tree"/> is this run's own; <paramref name="branch"/> is not an index into it;
    /// a node of that branch is already in this tree; or the branch's Keystone could not be
    /// excluded because the branch is a Keystone alone (rule 3).
    /// </exception>
    /// <exception cref="InvalidOperationException">A branch is already installed.</exception>
    public void InstallSplash(SkillTreeSpec tree, int branch, ContentCatalog catalog);

    /// <summary>Unchanged signatures, widened meaning — the branch may now be the borrowed one.</summary>
    public int Count { get; }
    public int ActiveCount { get; }
    public SkillSpec Skill(ContentId id);
    public bool TryLocate(ContentId id, out int branch, out int tier);
    public bool IsKeystone(ContentId id);
    public int RequiredTakenInBranch(ContentId id);
    public bool TryGetParent(ContentId id, out ContentId parentId);
    public int NodeCountOf(int branch);
}

// Core/Progression/SkillTree.cs — widened
public sealed class SkillTree
{
    /// <summary>
    /// Rebuilds the flattened walk to include the branch TreeRules has just borrowed. Called by
    /// TreeRules' owner immediately after InstallSplash, never independently — rule 5.
    /// </summary>
    public void OnSplashInstalled();
}
```

## Behaviour

1. **One branch index space, and the borrowed branch is index 3.** `TryLocate`, `NodeCountOf`,
   `RequiredTakenInBranch`, `SkillTree.TakenInBranch` and `NodeTaken.Branch` all keep their
   signatures and gain one legal value. 0–2 are the primary's, in the order its asset authored them;
   **3 is the borrowed one**, and it is 3 whichever class and whichever branch it came from.
   **The alternative — a second `TreeRules` beside the first — was weighed and refused:** every
   caller that asks *"how many of this branch are taken"* would have to know which object to ask, the
   gate check in `SkillTree.Check` would branch on it, and `OfferGenerator`'s same-branch penalty
   would stop being able to tell branch 1 of the primary from branch 1 of the splash. **One space
   with a fourth slot is the change; two objects is a fork.**
2. **`SkillTreeSpec.BranchCount` stays 3 and stops being the number anything indexes against.** The
   const means *branches per class* — CH §5's *"identical skeleton for every class"* — and it is
   still true of every authored tree. What was wrong was reading it as *branches per run*, which is
   what `OfferGenerator.Draw`'s `stackalloc` does. `TreeRules.BranchCount` is the run's number, and
   a row asserts the two are equal exactly while no branch is installed.
3. **The Keystone does not come with the branch, and it is excluded here rather than filtered later.**
   CH §5.4: *"what you do not gain: its weapon, its movement skill, its signature passive, its
   Veilrot relationship — **and its Keystone**"*, because a splashed Keystone speaks louder than the
   primary it is bolted to and is frequently nonsense on its face. **So `InstallSplash` drops every
   `SkillKind.Keystone` node from the borrowed branch as it resolves it**, and the branch's node
   count is what is left — CH §5.4's *"one branch minus its Keystone is 7"*. Filtering at the offer
   instead would leave a node that `TryLocate` finds, `IsAvailable` refuses and the tree view draws,
   which is three places to remember one rule. **A branch that is a Keystone alone is refused**, with
   a message: it is not content this build ships, and installing an empty branch would make
   `RequiredTakenInBranch` answer −1.
4. **Once per run, and it cannot be taken back.** CH §5.4: *"Reversible: no. Locked for the run."*
   A second `InstallSplash` throws rather than replacing, for `ModifyStatHandler.Aiming`'s reason —
   a silent replacement would leave nodes taken from a branch the run no longer has, and
   `SkillTree.TakenInBranch(3)` would be counting two different branches' picks together.
5. **`SkillTree` rebuilds its flatten and grows its per-branch counts, and it is told rather than
   asked.** `_nodes` is flattened once at construction in tree order and `Available`'s walk order
   **is the contract** — the same seed against the same tree state has to yield the same offer
   (AR §18.3). So `OnSplashInstalled` re-flattens with the borrowed branch **appended after the
   primary's three**, never interleaved, which means **every seed's meaning before the splash is
   unchanged and every seed's meaning after it is a function of one authored order**. `_taken`,
   `_takenInBranch` and `_index` grow with it; the flags already set are copied across by ordinal,
   because the primary's ordinals do not move. It is a method rather than a subscription for the
   reason core never subscribes to its own events (`RunSession`'s own remark).
6. **`OfferGenerator` sizes everything from `TreeRules` and that is the whole of its change.**
   `_candidates` and `_weights` are `new ContentId[rules.Count]` at construction — which is **before**
   a splash can be installed, since `LevelUpFlow` builds the generator in its own constructor — so
   they are sized from a count that can grow. **They are rebuilt in `Draw` when `tree.Rules.Count`
   exceeds them**, once, on the first pick after a splash: an offer is drawn once per level and a
   single array grow there is not the frame path (AR §14, and `EnemyRegistry`'s own bargain).
   `stackalloc int[SkillTreeSpec.BranchCount]` becomes `stackalloc int[rules.BranchCount]`, which is
   at most four.
7. **The tier rule applies to the borrowed branch unchanged, and that is CH §5.4's own sentence.**
   *"That branch's nodes enter your offer pool, gated by the same tier rule (§5)"* — so a splashed
   tier-2 node needs one node of **the splashed branch** taken, counted in `_takenInBranch[3]` and
   nowhere else. The player arrives at the moment with half their own tree taken and **none** of the
   borrowed branch, so the first splashed node they can be offered is always a tier-1 one. A row
   asserts the primary's counts are untouched by a splashed pick.
8. **An Upgrade's parent must still be in its own branch, and for the borrowed branch that check is
   inherited rather than re-run.** `TreeRules.CrossCheck` already refused a cross-branch parent when
   the *source* tree was built, and a branch borrowed whole keeps its internal parent relationships —
   so an Upgrade in the borrowed branch has its parent in the borrowed branch. **What
   `InstallSplash` must re-check is the Keystone drop**: an Upgrade whose parent was the Keystone
   just removed would be permanently unavailable, which is the exact deadlock
   `RequireParentBelowInBranch` exists to prevent. It is refused with a message naming both nodes.
   *(Against the shipped content this cannot fire — no authored tree has a Keystone with a child —
   and the check is what stops that being an accident.)*
9. **`Count` and `ActiveCount` include the borrowed branch, and `SkillRunner`'s capacity is the
   reason to care.** `RunSession.Start` compares `TreeRules.ActiveCount` against
   `SkillRunner.MaxActives` (12) and refuses a tree that would not fit, *before* `RunStarted`. A
   splash arrives mid-run, so that comparison cannot be made at `Start` for a branch nobody has
   chosen yet — **`InstallSplash` therefore makes it again**, and refuses the install rather than
   the run. With v1's one Active in twelve it cannot fire; at M7-04's twenty-seven a primary tree of
   seven Actives plus a borrowed branch of two is nine, still inside twelve, and the check is what
   makes that a fact rather than a hope.
10. **`SkillTree.IsFull` now means both**, which is what CH §5.2's Overflow reads. A run that
    completes its own twelve and its borrowed seven is full at nineteen; one that has not splashed is
    full at twelve. `LevelUpFlow` is untouched — it asks `OfferGenerator.Draw` what is available and
    never `IsFull` (its own remarks say so in as many words) — so this is a widening nothing has to
    be told about.
11. **Nothing here allocates on the frame path, and the two allocations it does make are named.**
    `InstallSplash` builds one dictionary entry per borrowed node and `OnSplashInstalled` rebuilds
    three arrays — both once per run, at a moment the game is paused. `Available`'s walk, the gate
    check and `Weight` are unchanged and still allocation-free; `Available_AllocatesNothing` runs
    against a splashed tree as well as an un-splashed one.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Rules_UnsplashedIsThreeBranches` | the shipped Oathbound tree / constructed / `BranchCount` 3, `SplashBranch` −1, `SplashCharacterId` default — rule 2 |
| `Rules_BranchCountMatchesTheConstUntilASplash` | any tree / — / `BranchCount == SkillTreeSpec.BranchCount`, and the two diverge only after an install — rule 2 |
| `Rules_InstallAddsAFourthBranch` | the Oathbound's rules, the Gravecaller's Legion branch / `InstallSplash` / `BranchCount` 4, `SplashBranch` 3, `SplashCharacterId` `character.gravecaller` |
| `Rules_ABorrowedNodeLocatesAtBranchThree` | the same / `TryLocate` on a borrowed id / branch 3 and the tier it had in its own tree — rule 1 |
| `Rules_ThePrimaryIsUnmoved` | the same / `TryLocate` on every primary id / the branch and tier it had before the install — rule 1 |
| `Rules_NodeCountOfThree` | a borrowed four-node branch / `NodeCountOf(3)` / 4, and `NodeCountOf(4)` throws naming the run's count |
| `Rules_TheKeystoneIsDropped` | a branch of eight plus a Keystone / installed / `NodeCountOf(3)` is **8**, the Keystone does not `TryLocate`, and `Skill` on it throws — **rule 3, CH §5.4's own sentence** |
| `Rules_AKeystoneOnlyBranchIsRefused` | a branch holding nothing but a Keystone / installed / throws, naming the branch — rule 3 |
| `Rules_AnOrphanedUpgradeIsRefused` | a branch whose tier-2 Upgrade parents the Keystone / installed / throws, naming both nodes — rule 8 |
| `Rules_ItsOwnTreeIsRefused` | the primary tree passed to `InstallSplash` / — / throws: a class does not splash itself |
| `Rules_ASharedNodeIdIsRefused` | a borrowed branch holding an id the primary already has / installed / throws — `SkillTreeSpec`'s own uniqueness rule, at the seam where it could be broken |
| `Rules_InstallIsOncePerRun` | a second `InstallSplash` / — / `InvalidOperationException`, and the first branch is still installed — rule 4 |
| `Rules_ActiveCountIncludesTheBorrowed` | a primary with one Active, a branch with two / installed / `ActiveCount` 3 |
| `Rules_RefusesABranchThatOverflowsTheRunner` | a primary at `SkillRunner.MaxActives` − 1 Actives, a branch with two / installed / throws naming the cap, and the **run is unaffected** — rule 9 |
| `Tree_TheWalkAppendsRatherThanInterleaves` | a splashed tree / `Available` / every primary id precedes every borrowed one, in the primary's own order — **rule 5, the seed contract** |
| `Tree_SeedsBeforeTheSplashAreUnchanged` | one seed, the same picks, with and without a later install / the offers drawn **before** the install / identical — rule 5 |
| `Tree_TakenFlagsSurviveTheRebuild` | five nodes taken, then installed / — / all five still `IsTaken`, `TakenCount` 5, `TakenIds` in the same order — rule 5 |
| `Tree_TakenInBranchThreeStartsAtZero` | half the primary taken, then installed / `TakenInBranch(3)` / 0 — rule 7 |
| `Tree_ABorrowedTierTwoNeedsABorrowedPick` | a splashed branch, nothing taken in it / `IsAvailable` on its tier-2 / false; after one tier-1 pick from it / true — rule 7 |
| `Tree_ASplashedPickDoesNotOpenThePrimary` | one borrowed node taken / — / every primary branch's `TakenInBranch` is unmoved — rule 7 |
| `Tree_ABorrowedUpgradeStillNeedsItsParent` | a borrowed Upgrade whose parent is unowned / `Take` / refused, and the message names the parent — rule 8 |
| `Tree_IsFullCountsBoth` | twelve primary and seven borrowed taken / — / `IsFull`; at eighteen it is false — rule 10 |
| `Offer_DrawsFromBothTrees` | a splashed tree, everything available / 10 000 draws / borrowed ids appear, at a rate the same-branch penalty explains |
| `Offer_TheSameBranchPenaltyCountsBranchThree` | two borrowed nodes already drawn this call / `Weight` / 0.25 for a third from branch 3 — rule 6, so the borrowed branch is not silently exempt |
| `Offer_BuffersGrowOnceAndThenNotAgain` | a splash installed mid-run / the next draw and the ten after it / one grow, then none — rule 6 |
| `Offer_DoesNotThrowOnABorrowedNode` | the shipped shape before this task / a draw containing a borrowed id / **no `IndexOutOfRangeException`** — the failure this task exists to prevent, asserted rather than described |
| `Available_AllocatesNothing` | *(existing, extended)* a **splashed** tree / `AllocationAssert.None` over 1 000 calls / zero — rule 11 |

**Guard rows are implied, not listed:** a null `SkillTreeSpec` or `ContentCatalog` to
`InstallSplash`; a branch index outside the borrowed tree; and `OnSplashInstalled` called with no
splash installed.

## Manual verification (Editor / device)

_None._ Nothing calls `InstallSplash` in production until
[M5-07a-ii](M5-07a-ii-the-half-tree-moment.md), so every run this build plays holds a three-branch
tree and is byte-identical in behaviour. The first time a fourth column exists is M5-07a-ii's, and
the first thing that will draw one is `TreeViewPresenter`, which rule 12 of that task rules on.

## Out of scope

- **When the moment happens, who chooses, and what the screen looks like.**
  [M5-07a-ii](M5-07a-ii-the-half-tree-moment.md).
- **Saving the choice.** M5-07a-ii rule 6 rules that it is **derived** from `TakenNodeIds` rather
  than stored, which is why `RunSnapshot.CurrentVersion` is not in either Files table.
- **The tree view drawing a fourth column.** M5-07a-ii rule 12.
- **Splashing more than one branch, or from more than one class.** CH §5.4 is one branch of one
  class; rule 4 refuses the second and `BranchCount` is 3 or 4 and never 5.
- **Class unlocks deciding which classes may be splashed from.** CH §5.4's *"a second **unlocked**
  class"* against a `PlayerProfile` with no unlock set — M5-07a-ii rule 3, and M6-09.
- **`SkillTreeSpec`, `SkillDefinition` or any authored asset.** Rule 2: the content side is already
  correct and M3-02a rule 11's one-tree-per-class refusal stands.

## As built

_Filled at merge, 6 000 bytes or fewer, measured._
