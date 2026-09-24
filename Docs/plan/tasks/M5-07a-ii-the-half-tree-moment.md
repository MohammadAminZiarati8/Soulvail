# M5-07a-ii — The half-tree moment, and the branch a run borrows

**Size:** M · **Depends on:** M5-07, M5-07a-i · **Branch:** `m5-07a-ii-splash-moment`
**Design refs:** CH §4, §5.1, §5.2, §5.4, §6; GD §13.1, §16.1; AR §11.5, §18.1, §18.2, §18.3 · **Ledger rows:** none

## Goal

Halfway through their own tree the run stops and asks a question it has never asked before — *which
class do you borrow a branch from?* — and from the next level-up the offer draws from thirty-four
nodes instead of twenty-seven.

## What this task is, and what it deliberately is not

[M5-07a-i](M5-07a-i-the-runs-tree-widens.md) made a run's tree able to hold a borrowed branch and
left `InstallSplash` called by nobody. This is the half that decides **when**, **who chooses**, and
**what it looks like** — CH §5.4's five rules read as a state machine, a command and a screen.

**It is not a second level-up.** `LevelUpFlow` owns picks, offers and Overflow and is **not in the
Files table**: a moment that grants no node, spends no pick and draws from no stream is a different
object, and folding it in would put CH §5.4's *"the choice is mandatory"* inside a class whose whole
contract is *"three cards, take one"*.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/SplashFlow.cs` | Core | The moment: when it is owed, what may be chosen, and the one verb that chooses |
| `Game/Presentation/SplashPresenter.cs` | Game | The screen: classes, then branches, one tap each — **block namespace** ([Traps §5](../../Traps.md)) |
| `Tests/Core/Progression/SplashFlowTests.cs` | Tests.Core | The threshold, the options, the choice, the resume, and the run with nothing to borrow |
| `Tests/Game/Presentation/SplashPresenterTests.cs` | Tests.Game | Two taps, what it draws, and the raw-string sweep |
| `Prefabs/UI/Splash.prefab` | — | The screen. An asset — listed, not counted ([sizing rule](../ROADMAP.md#how-to-read-this)) |
| *small edits* | Core, Game | `RunState` holds the flow and exposes scalar reads (rule 11); `IProgressionCommands` gains `OpenSplash` and `ChooseSplash(characterId, branch)`; `RunSession` builds it, drives it in `Start`'s restore block (rule 6) and answers the two commands; `RunTicker.LevelUpPhase` raises the pause for it (rule 5); `RunScope` gains `_splashPresenter`; `Scenes/Run.unity` is dressed; `English.asset` gains four rows |
| *ripple* | Tests.Core, Tests.Game | `RunSessionTests` and `RunSessionResumeTests` — the command port grows by two; `FrameOrderTests`' `RecordingCore` implements them |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Progression/SplashFlow.cs
/// <summary>
/// CH §5.4's second discipline: the moment half a tree is taken, what may be borrowed, and the
/// choice that locks it. LevelUpFlow's shape — a small state machine RunState owns, driven from
/// outside, publishing rather than subscribed to — and deliberately not part of it (rule 1).
/// </summary>
public sealed class SplashFlow
{
    /// <summary>
    /// The fraction of the primary tree that opens the moment. A half — CH §5.4, and rule 2 is why
    /// it is a fraction rather than a level.
    /// </summary>
    public const float Threshold = 0.5f;

    public SplashFlow(
        SkillTree tree, ContentCatalog catalog, ContentId ownCharacterId, IDomainEvents events);

    /// <summary>Whether the moment is owed and not yet spent. Read by the frame, like HasOffer.</summary>
    public bool IsPending { get; }

    /// <summary>Whether the screen is up — the moment has been opened and not answered.</summary>
    public bool IsOpen { get; }

    /// <summary>Whether this run has already borrowed. True for the rest of the run.</summary>
    public bool HasSplashed { get; }

    /// <summary>The classes that may be borrowed from, in catalog order. Empty means rule 3.</summary>
    public IReadOnlyList<ContentId> Candidates { get; }

    /// <summary>Raises the moment if it is owed. Idempotent, and draws nothing (rule 4).</summary>
    public void Open();

    /// <summary>
    /// Borrows <paramref name="branch"/> of <paramref name="characterId"/>, for the run.
    /// </summary>
    /// <exception cref="InvalidOperationException">The moment is not open, or one was already taken.</exception>
    /// <exception cref="ArgumentException">
    /// The class is this run's own, is not in the catalog, or has no tree; or the branch is not an
    /// index into it.
    /// </exception>
    public void Choose(ContentId characterId, int branch);

    /// <summary>
    /// Replays a resumed run's borrowed branch, silently — derived rather than saved (rule 6).
    /// </summary>
    public void Restore(ContentId characterId, int branch);
}

// Core/Events/ProgressionEvents.cs — widened
public readonly struct SplashOffered { public int TakenCount; public int Threshold; }
public readonly struct SplashChosen { public ContentId CharacterId; public int Branch; public int NodesGained; }

// Core/Ports/IProgressionCommands.cs — widened
void OpenSplash();
void ChooseSplash(ContentId characterId, int branch);
```

## Behaviour

1. **It is its own flow, and `LevelUpFlow` is untouched.** The two look alike — a pending count, an
   open screen, a choice — and they differ in every way that matters: this one **spends no pick**,
   **draws from no stream**, **grants no node**, **happens exactly once a run** and **may legitimately
   never happen at all** (rule 3). `LevelUpFlow.Open`'s loop is written around *"while picks are
   owed"* and its `Choose` ends with `SkillTree.Take`; neither sentence is true here.
2. **The threshold is `ceil(NodeCount × 0.5)` of the *primary* tree, computed rather than authored.**
   CH §5.4 is explicit that this is a fraction and not a level, and explicit about why: *"a
   twelve-node tree fills at level 13 and a twenty-seven-node one at level 28, so any constant is
   wrong at one of the two scales."* Against v1's twelve that is **six nodes**, around level 7;
   against M7-04's twenty-seven it is **fourteen**, around level 15 — the same beat at both content
   sizes, which is what lets the moment be playtested now and still be right later. **It is the
   primary's count and not the run's**, so a borrowed branch cannot move a threshold that has already
   been crossed. `IsPending` is `TakenCount >= Threshold && !HasSplashed`.
3. **A run with nothing to borrow never sees the screen, and *"unlocked"* is read as *"authored"* —
   named, not invented.** CH §5.4: *"If you have unlocked nothing, the moment does not happen. A
   player holding only the Oathbound never sees this screen."* `PlayerProfile` **v3 carries no unlock
   set** — `Version`, `HapticsEnabled`, `SeenFirstActiveHint`, `Shards` — and nothing awards one
   until M6-09a, so `Candidates` is *every class in the catalog with a tree, except this run's own*.
   **With the shipped two that is exactly one**, so the screen's first page has one card and the
   choice CH §5.4 calls mandatory is a formality until a third class exists. **That is stated rather
   than smoothed over**: skipping the class page when there is one candidate would be a special case
   for a build state, and the ruling is to draw the page and let the player tap the only card — one
   tap that costs nothing and reads identically at three classes. `Candidates` being **empty** —
   a build with one authored class — is the CH §5.4 branch that ships working from the first day:
   `IsPending` is false and the moment never opens.
4. **It opens between ticks, never inside one, and it draws nothing.** `RunTicker.LevelUpPhase` is
   where the *when* lives (M3-08a rule 4), so the splash is raised there, **above the level-up**: a
   level that crosses the threshold is spent on a node first and the moment is read at the top of the
   next frame. **There is no draw and therefore no seed question** — every candidate is offered, in
   catalog order, so `Open` is idempotent in the strong sense `LevelUpFlow.Open` has to work for
   (AR §18.3).
5. **It holds the pause under its own reason, and the two gates cannot collide.** `RunPause.Pause`
   throws for a second holder, and `RunTicker` is the only thing that raises one — so
   `PauseReason.Splash` joins `LevelUp` and `Menu`, and `LevelUpPhase` raises whichever is wanted
   with the same *"guarded on nothing else holding it"* shape. **The splash is checked first**: a
   level-up and the splash can be owed on the same frame, the splash is the rarer and more
   consequential of the two, and a player who took a node and then chose a discipline has seen them
   in the order they happened.
6. **The choice is *derived* on resume rather than saved, and `RunSnapshot.CurrentVersion` stays 3.**
   CH §5.4 locks the branch for the run, so **every foreign id in `TakenNodeIds` is in the same
   branch of the same class** — which makes the choice recoverable from what is already written down.
   `RunSession.Start` walks the saved ids, and the first that the primary tree does not hold is
   resolved against the catalog to a class and a branch; `SplashFlow.Restore` and
   `TreeRules.InstallSplash` then run **before** `SkillTree.Restore` replays the takes, or the replay
   would refuse a node the tree has never heard of. **This is `LevelUpFlow.GrantOverflow`'s bargain
   exactly** — *"its caller derives the count rather than reading it, because no field carries it"* —
   and it is what makes a **v4** and a migration unnecessary for a single enum-sized fact.
   **The cost is one case and it is stated: a run saved after the choice and before its first
   borrowed pick resumes un-splashed and asks again.** The window is narrow — a boundary snapshot is
   taken at stage edges (M2-14a), and a resume already rewinds to the top of its stage — and the
   failure is a second choice rather than a broken tree. **A saved id that resolves to no class is
   the existing refusal**, not a new one: `SkillTree.Restore` throws naming content validation, which
   is what a build that has dropped a class should do.
7. **The screen is two taps and one prefab: classes, then branches.** Page one draws `Candidates`
   with the card `ClassSelectPresenter` already established (M5-07) — name, description, three
   numbers. Page two draws the chosen class's **three** branches by their `NameKey`, each with the
   node count it will contribute **after the Keystone is dropped** (M5-07a-i rule 3) — *"7 nodes"*
   against v1's four-node branches reading *"4 nodes"*. **No Back on page two**: CH §5.4's *"the
   choice is mandatory"* and *"there is no stay-pure option"*, so the only way off this screen is
   through it. A row asserts there is no path that closes it without choosing.
8. **Nothing about the borrowed class comes with the branch, and the screen says so.** CH §5.4's
   *"what you do not gain"* is its weapon, its movement skill, its signature passive, its Veilrot
   relationship and its Keystone — five things, of which this build can get four wrong for free
   because none of them is reachable from a `SkillTreeSpec`. **The fifth is the Keystone and
   M5-07a-i rule 3 drops it**; the screen's branch line is written from the post-drop count so a
   player is never shown a number that includes a node they cannot have.
9. **`SplashChosen` carries what it granted, and `SplashOffered` carries why it fired.** The first
   is the event a readout draws and a later achievement counts; the second exists so the moment can
   be traced without reading the tree. Both are published **after** the state has settled —
   `NodeTaken`'s rule, for its reason.
10. **A borrowed node is taken through `SkillTree.Take` like any other.** There is no second take
    path and no second event: `NodeTaken.Branch` is 3, the effects go on through the same
    `EffectRegistry`, and an Active from a borrowed branch reaches `SkillRunner` through
    `LevelUpFlow.Choose`'s existing *"if the node is an Active, tell the runner directly"*. **The
    `CanApply` sweep is the one thing that has to be re-run**, because `SkillTree`'s constructor did
    it once over the primary and a borrowed branch's effects were not in it — `InstallSplash`'s
    caller runs it over the new nodes and refuses the *install* rather than the pick, which is
    `SkillTree`'s own argument.
11. **`RunState` hands out scalars, never the flow.** `IsSplashPending`, `HasSplashed`,
    `SplashCandidates` and `SplashBranchesOf(characterId)` — AR §18.2 for the eighth time. A public
    handle would let a view borrow a branch.
12. **The tree view is not taught to draw a fourth column, and that is a ruling.**
    `TreeViewPresenter` draws three columns from `SkillTreeSpec.BranchCount`, and a fourth is a
    layout change on a screen M4-07 already judged too cramped, in a task whose review is a state
    machine. **So a borrowed node is owned, taken, and invisible on the tree screen until M7's
    redesign** — the same screen the owner's standing ruling has already put in M7. It is named here
    because *"a node I own that the tree does not show"* is exactly the kind of thing discovered in a
    playtest and filed as a bug. **It goes to [M5-08](M5-08-acceptance-and-tag.md)'s checklist as an
    observation**, and the verdict there decides whether it becomes an M6 row or waits for the icons.
13. **Nothing here allocates on the frame path.** `IsPending` is two integer comparisons read once a
    frame in `LevelUpPhase`; the candidate list is built once at construction and wrapped; the choice
    happens at most once a run, on a paused frame.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Splash_FiresAtHalfTheTree` | the twelve-node Gravecaller tree / five nodes taken, then six / not pending, then pending — rule 2 |
| `Splash_TheThresholdIsAFractionNotALevel` | a 12-node tree and a 27-node one / — / 6 and 14, both `ceil(n × 0.5)`, and no level number appears anywhere — CH §5.4's own argument |
| `Splash_TheThresholdIsThePrimarysCount` | half taken, a branch borrowed, more taken / — / the threshold did not move — rule 2 |
| `Splash_HappensOnce` | the moment answered / twenty more picks / never pending again |
| `Splash_ANoCandidateRunNeverOpens` | a catalog with one class / half the tree taken / `IsPending` false, no event, no pause — **rule 3, CH §5.4's own branch** |
| `Splash_OffersEveryAuthoredClassButItsOwn` | the shipped catalog, a Gravecaller run / open / exactly `character.oathbound` — rule 3 |
| `Splash_OneCandidateStillDrawsThePage` | the shipped two / open / one card, and nothing auto-chooses — rule 3 |
| `Splash_DrawsNothingFromAnyStream` | a counting `IRandom` / opened and chosen / every stream unadvanced — rule 4 |
| `Splash_OpenIsIdempotent` | opened twice / — / one `SplashOffered` |
| `Splash_ChooseInstallsTheBranch` | the Oathbound's Censure branch chosen / — / `TreeRules.BranchCount` 4, `SplashBranch` 3, one `SplashChosen` naming the class, the branch and the node count |
| `Splash_ChooseIsRefusedBeforeItIsOpen` | not pending / `Choose` / `InvalidOperationException`, and nothing is installed |
| `Splash_RefusesItsOwnClass` | the run's own id / `Choose` / throws — rule 3 |
| `Splash_RefusesAnUnauthoredClassOrBranch` | a stranger id, then branch 3 of a three-branch tree / `Choose` / throws in both cases, naming what was wrong |
| `Splash_RefusesABranchWhoseEffectsHaveNoHandler` | a branch carrying an unregistered primitive / `Choose` / throws, and **the branch is not installed** — rule 10 |
| `Run_TheOfferWidensAfterTheChoice` | six primary nodes taken, a four-node branch borrowed / the next level-up / the offer may contain borrowed ids, and `TreeRules.Count` is 16 |
| `Run_ABorrowedActiveReachesTheRunner` | a borrowed branch holding an Active / taken through the level-up / `SkillRunner` holds it and it casts — rule 10 |
| `Run_ABorrowedNodesEffectsApply` | a borrowed `ModifyStat(MaxHp, Flat, +15)` / taken / the player's max HP moves by 15 — rule 10 |
| `Run_TheSplashPauseIsItsOwnReason` | the moment owed / one frame / `RunPause.Holder` is `Splash`, and a level-up owed on the same frame does not throw — rule 5 |
| `Run_TheSplashIsReadBeforeTheLevelUp` | both owed on one frame / one frame / the splash screen is up and the level-up is not — rule 5 |
| `Run_TheTickThatCrossedTheThresholdFinishes` | the sixth node taken mid-frame / that frame / its intents are applied and its cone is answered; the moment opens on the **next** frame — rule 4 |
| `Resume_DerivesTheBranchFromTheSavedIds` | a save carrying four primary ids and two borrowed ones / `Start` / the branch is installed, `HasSplashed` is true, and all six replay — **rule 6** |
| `Resume_InstallsBeforeItReplays` | the same / `Start` / no throw, and a restore that replayed first would have thrown `KeyNotFoundException` — rule 6, the ordering asserted |
| `Resume_ASaveWithNoBorrowedIdsComesBackUnsplashed` | a save taken after the choice and before the first borrowed pick / `Start` / `HasSplashed` false and the moment is owed again — **rule 6's stated cost, asserted rather than hidden** |
| `Resume_TheFormatIsStillVersionThree` | `RunSnapshot.CurrentVersion` / — / **3**, and no field was added — rule 6 |
| `Resume_IsSilent` | a splashed save / `Start` / no `SplashOffered` and no `SplashChosen` before `RunStarted` — `SkillTree.Restore`'s rule |
| `Screen_DrawsTheCandidatesThenTheBranches` | the moment open / — / one card per candidate; a tap draws that class's **three** branches with their `NameKey`s |
| `Screen_TheBranchCountExcludesTheKeystone` | a nine-node branch with a Keystone / drawn / the line reads **8** — rules 7, 8 |
| `Screen_HasNoWayOutButThrough` | the screen up / every interactive element / none closes it without choosing, and the pause is still held — rule 7 |
| `Screen_DrawsNoRawEnglish` | `Splash.prefab` / swept / every authored text is a claimed `LocKey` — AR §11.5 |
| `State_HandsOutScalars` | `RunState`'s public surface / — / no `SplashFlow` — rule 11 |

**Guard rows are implied, not listed:** null dependencies to the flow and to `Construct`; a negative
branch index; `Restore` called twice.

## Manual verification (Editor / device)

1. **[Editor]** Play a Gravecaller and take six nodes. *Expected: after the sixth, a screen naming
   the Oathbound; tap it, then tap a branch. The run continues.*
2. **[Editor]** Level again. *Expected: the offer can now contain Oathbound nodes, and taking one
   works — a Censure node changes the bolt's damage. This is CH §5.4's whole promise.*
3. **[Editor]** Open the tree screen. *Expected: **three columns, and the borrowed node is not on
   any of them** — rule 12, which is a known ruling and not a bug. Record whether it reads as one.*
4. **[Editor]** Quit to the menu mid-stage after taking a borrowed node, then `Continue`. *Expected:
   the branch is back, the borrowed node is still owned, and the screen does not reappear (rule 6).*
5. **[Editor]** Do the same **before** taking a borrowed node. *Expected: the screen **does**
   reappear — rule 6's stated cost, seen once so nobody reports it later as a bug.*
6. **[Editor]** Play an Oathbound to six nodes. *Expected: the screen names the Gravecaller, and its
   Legion branch is borrowable — including Exhume, which raises Wights for a class that has none.
   **Watch what happens:** M5-06a rule 5 refuses a `Minions` effect on a class with no `MinionSpec`
   at `Start`, and a branch borrowed mid-run is not at `Start`. If the install is refused, that is
   rule 10 working; if it is not, that is a finding and it goes to M5-08.*
7. **[device]** Two pages, six taps' worth of text, at thumb distance, in the middle of a fight that
   is paused. [Ledger row 3](../ROADMAP.md#carry-forward-into-m5).

## Out of scope

- **The index space, the Keystone drop and the walk order.** [M5-07a-i](M5-07a-i-the-runs-tree-widens.md).
- **A fourth column on the tree screen.** Rule 12, and M5-08 observes.
- **Class unlocks gating who may be borrowed from.** Rule 3. M6-09a, and a `PlayerProfile` v4.
- **Saving the choice.** Rule 6, with the one case it costs written down.
- **Borrowing twice, or from two classes.** M5-07a-i rule 4.
- **A compensating bonus for a run with no candidate.** CH §5.4 offers none and inventing one would
  pay a player for a build state.
- **Pact variants of borrowed nodes.** GD §13.2, M6-05a/b.

## As built

Built as specced: a state machine, a command and a screen. **2 521 EditMode / 0 / 0** (+43 on
M5-07a-i's 2 478 — 30 in `SplashFlowTests`, 11 in `SplashPresenterTests`, 2 in `FrameOrderTests`),
twice on the final code, and **PlayMode 21 / 0 / 0** (+2). Console after the three runs: 24 errors
and 53 warnings, every one a fixture provoking its own failure path and none naming a new file.
Format check green over all sixteen touched C# files. `TimeManager.asset` re-serialised itself and
was reverted ([Traps §5](../../Traps.md)).

**Sixteen deviations, seven changing something.**

1. **`IProgressionCommands` grew *four* members, not the two the *Public API* lists.**
   `RunTicker.LevelUpPhase` needs `IsSplashPending` and `IsSplashOpen` as well — `RunState`'s
   constructor is `internal` with no `InternalsVisibleTo`, so the frame loop reaches core only
   through the port. `IsLevelUpPending` and `HasOffer` are on it for exactly that reason, and the
   port's own remarks say so.
2. **`SplashFlow` takes an `EffectRegistry`**, which the *Public API* does not list, and rule 10's
   *"the branch is not installed"* is why. `SkillTree.OnSplashInstalled` sweeps the borrowed nodes
   for handlers before *it* commits — but `TreeRules.InstallSplash` has already committed by then,
   so a refusal there would leave the pair disagreeing about how many nodes the run has. Swept in
   `Choose` before either call, a missing `Register` line leaves the run untouched.
3. **`RunState.IsSplashPending` carries a fourth term the spec does not state: no offer on the
   table.** Taking the sixth node as pick 1 of 2 leaves a second offer up, and a splash screen
   raised over it would be two screens wanting one `RunPause`. `IsLevelUpPending`'s own shape —
   *"one question rather than three"* — and it is what makes rule 5's *"the splash is checked
   first"* true without the ticker holding a copy of the rule.
4. **`LevelUpPhase` gives the pause back before it takes it.** The frame a splash becomes owed is
   usually the frame a level-up stops being: written as two independent blocks, the splash finds the
   pause still held by `LevelUp` and declines it, the level-up block then hands it back, and the
   screen is up over a running fight. Release-then-acquire changes hands in one frame.
5. **`SplashChosen.Branch` is the lender's own index, not `TreeRules.SplashBranch`.** The run's index
   is 3 whichever branch was taken, so it could not say which one the player chose.
6. **`Run_TheSplashPauseIsItsOwnReason` and `Run_TheSplashIsReadBeforeTheLevelUp` are PlayMode rows,
   in `FrameOrderTests`.** Both are about a real `RunTicker` holding a real `RunPause`, which
   `Soulvail.Tests.Core` cannot reference. The core half — that both can be owed at once, so
   something has to choose — is `SplashFlowTests.Run_BothCanBeOwedOnOneFrame`.
7. **Two rows beyond the Tests table.** `Splash_RefusesABranchAimedAtMinionsThisClassCannotRaise` is
   M5-07a-i's handed-over finding closed; `Splash_BranchesOfDropsTheKeystone` is the core half of
   `Screen_TheBranchCountExcludesTheKeystone`.
8. **The ripple was three test files and neither of the two the Files table named.** `RunSessionTests`
   and `RunSessionResumeTests` *cast* to the port rather than implementing it, so they needed
   nothing; what had to change was `ResumeFlowTests.RecordingSession`, `FrameOrderTests.RecordingCore`
   and — unlisted — `RunPauseTests.Reason_HasBothMembers`, which asserted the enum held exactly two.
   A grep scoped the ripple ([Traps §2](../../Traps.md)), for the second task running.
9. **`SplashFlow.OpensAt`, `TryDerive` and `BranchesOf` are members the *Public API* does not list.**
   The Tests table asks for the threshold as a number, the resume asks for the derivation, and the
   screen asks for a branch's *post-Keystone* count — which `SkillBranchSpec.NodeCount` cannot give.
10. **`SplashOption` is a second public type in `SplashFlow.cs`**, on `SkillBranchSpec`'s precedent:
    a borrowable branch outside the moment that offers it is not a thing the game has.
11. **`RunState` gained a fifth read, `IsSplashOpen`**, beside rule 11's four. It is what the pause is
    held against; `HasOffer`'s pair.
12. **An out-of-range branch throws `ArgumentOutOfRangeException`**, where the *Public API* files it
    under `ArgumentException`. M5-07a-i deviation 4's house style, and AOORE *is* an
    `ArgumentException`.
13. **`ui.splash.nodes` is a table row rather than a `const` format**, where `ClassCard.HpFormat` is a
    const. *"140 HP"* is a number and a unit; *"nodes"* is an English word, and AR §11.5 has no
    exception for a word that happens to sit beside a number.
14. **`Splash.prefab` joins `StaticLabelWiringTests.Screens`** — that array's own remarks say a fifth
    screen should be a decision made there. `ClassSelect.prefab` is still absent (M5-07's choice), so
    the array is the four run screens plus this one rather than every prefab that authors a key.
15. **The screen's canvas sorts at 105** — above the level-up's 100, below the tree view's 110. The
    two cannot be up together, and a sorting order is a fact about the asset where *"the level-up is
    at alpha 0 by then"* is a promise made in another file.
16. **The two pages are laid out by uGUI layout groups rather than by authored positions.** One
    candidate is the shipped case (rule 3), and three fixed slots would pin the only card to the
    left of a full-screen overlay. A `HorizontalLayoutGroup` centres one, two or three for free, with
    no arithmetic in the presenter — `LevelUpPresenter.Place`'s problem answered by the asset.

**Rule 12 stands and goes to [M5-08](M5-08-acceptance-and-tag.md) as an observation:**
`TreeViewPresenter` still draws three columns, so a borrowed node is owned, taken, and invisible on
the tree screen. Manual step 3 is what records whether it reads as a bug.
