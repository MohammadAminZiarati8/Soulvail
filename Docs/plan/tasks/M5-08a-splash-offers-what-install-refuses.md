# M5-08a — The splash offers what the install refuses

**Size:** S · **Depends on:** M5-07a-ii · **Branch:** `m5-08a-splash-offers`
**Design refs:** CH §5.4; AR §11.5, §18.2 · **Ledger rows:** [M6 row 6](../ROADMAP.md#carry-forward-into-m6)

## Goal

A branch the run cannot borrow is drawn as unavailable with a reason, instead of as a button that
throws — so CH §5.4's screen never offers a choice the model refuses.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/SplashFlow.cs` | Core | `SplashOption` gains `Borrowable` and `RefusedKey`; the installability sweep becomes a predicate both `BranchesOf` and `Install` read |
| `Tests/Core/Progression/SplashBranchTests.cs` | Tests.Core | The predicate, both refusal reasons, and that `Install` still throws |
| `Game/Presentation/SplashPresenter.cs` | Game | A non-borrowable row is non-interactable and says why |
| `Tests/Game/Presentation/SplashPresenterTests.cs` | Tests.Game | The disabled row and its label |
| `Data/Localisation/English.asset` | — | Two refusal strings |
| *ripple* | | none: `RunState.SplashBranchesOf` still answers `IReadOnlyList<SplashOption>`, so no port widens |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
public readonly struct SplashOption
{
    public SplashOption(int branch, LocKey nameKey, int nodeCount, bool borrowable, LocKey refusedKey);

    public readonly int Branch;
    public readonly LocKey NameKey;
    public readonly int NodeCount;

    /// <summary>Whether Choose would accept this branch. False means the row is drawn dead.</summary>
    public readonly bool Borrowable;

    /// <summary>Why not, or default when Borrowable. Never a sentence — AR §11.5.</summary>
    public readonly LocKey RefusedKey;
}

public sealed partial class SplashFlow
{
    // Unchanged signature; every option now carries Borrowable.
    public IReadOnlyList<SplashOption> BranchesOf(ContentId characterId);

    // Unchanged, and still throws. The guard is the backstop, not the gate.
    public void Choose(ContentId characterId, int branch);
}
```

## Behaviour

1. **The sweep `RequireInstallable` performs becomes a predicate**, `TryRefusal(tree, branch, out LocKey reason)`, returning true when the branch may **not** be borrowed. `RequireInstallable` calls it and throws; `BranchesOf` calls it and records. **One sweep, two callers** — a second copy of this logic is how the screen and the model came apart in the first place.
2. **`BranchesOf` sets `Borrowable` and `RefusedKey` on every option it builds.** No branch is omitted from the list: a hidden branch tells the player nothing, and CH §5.4's screen is about understanding what the other class *is*.
3. **The two refusals keep the reasons they already have**, and they are distinct keys: `ui.splash.refused.primitive` for an effect whose handler this run never registered, `ui.splash.refused.minions` for a `ModifyStat` aimed at `StatTarget.Minions` on a class with no `MinionSpec`. A Keystone is skipped before either test, exactly as today.
4. **`Choose` and `Install` are unchanged and still throw.** Rule 1's predicate is the gate; the exception is the invariant. A future caller that reaches `Install` without consulting `BranchesOf` must still fail loudly rather than install half a branch.
5. **A non-borrowable row is `interactable = false` and draws `RefusedKey` where its node count would go.** The row keeps its name, so the player reads *what they cannot have and why*, which is the half CH §5.4 is actually about.
6. **At least one branch of at least one candidate is always borrowable, and nothing asserts it.** With two classes the Oathbound has exactly one, and if a future roster leaves a class with none the screen is a dead end — **out of scope, named in Out of scope, and the reason is that M6-07's third class changes the arithmetic before it can bite.**
7. **`SplashOption`'s constructor stays public and gains two parameters.** Every existing construction site is inside `SplashFlow`; fixtures that build one by hand are ripple and are listed as deviations if any exist.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `BranchesOf_ABranchWithAnUnregisteredPrimitive_IsNotBorrowable` | An Oathbound run, the Gravecaller's Legion branch (Exhume casts `RaiseMinions`) / `BranchesOf` / the option is present, `Borrowable` false, `RefusedKey` is `ui.splash.refused.primitive` |
| `BranchesOf_ABranchAimedAtMinions_IsNotBorrowable` | The same run, the Rot branch (Restless Dead targets `Minions`) / `BranchesOf` / `Borrowable` false, `RefusedKey` is `ui.splash.refused.minions` |
| `BranchesOf_APlainBranch_IsBorrowable` | The same run, Grave-Work (every effect targets `Player`) / `BranchesOf` / `Borrowable` true, `RefusedKey` default |
| `BranchesOf_EveryBranchIsListed_WhetherBorrowableOrNot` | The same run / `BranchesOf` / three options, one borrowable — a refused branch is drawn, never hidden (rule 2) |
| `BranchesOf_AClassWithNoMinionEffects_IsWhollyBorrowable` | A Gravecaller run borrowing the Oathbound / `BranchesOf` / all three borrowable — the asymmetry is real and this pins it |
| `Choose_ARefusedBranch_StillThrowsAndInstallsNothing` | An Oathbound run / `Choose(gravecaller, legion)` / `ArgumentException`, and the run's node count and branch count are unchanged (rule 4) |
| `TryRefusal_AKeystone_IsSkipped` | A branch whose only offending effect is on a Keystone / `TryRefusal` / false — the Keystone is not lent, so it cannot refuse the branch (rule 3) |
| `Presenter_ARefusedRow_IsNotInteractable` | `BranchesOf` answers one refused option / the screen draws / that row's `Button.interactable` is false and its detail label reads the refusal string (rule 5) |
| `Presenter_ABorrowableRow_IsUnchanged` | The borrowable option / the screen draws / interactable, and the detail label still reads the node count — `Hud`-style "nothing else moved" |
| `EveryLocKey_ResolvesInEnglish` | — | Already exists; the two new keys are swept by it |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row. They belong in the test file and are not deviations.

## Manual verification (Editor / device)

1. **[Editor]** Start an Oathbound run, reach the 6th node, choose the Gravecaller at the splash. **Expect:** three branches drawn; *Grave-Work* tappable; *Legion* and *Rot* greyed with a reason where the node count sits. No Console exception on tapping either.
2. **[Editor]** Tap *Grave-Work*. **Expect:** the branch installs, the screen closes, the run resumes — M5-08's log line `SPLASH CHOSEN — branch 1 of character.gravecaller, +4 nodes`.
3. **[Editor]** Start a Gravecaller run and reach the splash. **Expect:** all three Oathbound branches tappable, because the Oathbound authors no minion effects.
4. **[device]** Whether a greyed row with a reason reads as *"you cannot have this"* rather than as a bug, at thumb distance. Deferred — [M6 row 1](../ROADMAP.md#carry-forward-into-m6).

## Out of scope

- **Re-authoring the trees so a minionless class can borrow more than one branch.** Restless Dead is a `Minions` node filed in *Rot* rather than *Legion*, which is why the Oathbound loses two branches instead of one — an authoring observation for **M7-04**, which authors all 81 nodes, not a change to make under an acceptance.
- **A class with no borrowable branch at all** (rule 6). M6-07's third class changes the arithmetic first.
- **The Gravecaller's TTK drift.** Same playtest, different cause — [M6 row 2](../ROADMAP.md#carry-forward-into-m6), owned by M8-05.
- **Anything about the splash's layout or its two-page shape.** M5-07a-ii shipped it and it works.

## As built

**Five files, as the table says, and the behaviour is rules 1–5 unchanged.** `TryRefusal` is the
sweep as a predicate, `RequireInstallable` is untouched beside it, `BranchesOf` sets `Borrowable` and
`RefusedKey` on every option it builds, and `SplashPresenter.Redraw` derives `interactable` from
`Borrowable` and draws the reason where the node count was.

**Four deviations, and three of them are the test fixture.**

*1. The Files table named the wrong core test file.* `BranchesOf` is covered in `SplashFlowTests.cs`,
not `SplashBranchTests.cs` — the latter is about what a borrowed branch does once installed. Four rows
went to `SplashFlowTests.cs`; `SplashBranchTests.cs` is untouched. The Files table's *count* is right
and its *name* was wrong, which is the cheaper half to get wrong.

*2. `Unhandled` moved from branch 1 to branch 2 of the presenter fixture's lender tree, and this is
the deviation worth reading.* Rule 5 makes a refused branch draw its reason **instead of** its node
count, which silently broke two existing rows that had nothing to do with this task:
`Screen_TheBranchCountExcludesTheKeystone` counts the Keystone-dropped number on branch 1, and
`Screen_ATapIsTakenOnce` taps two *different* live rows to prove the latch covers the buttons beside
the one hit — *"uGUI refuses a second touch on the button it hit and says nothing about the ones
beside it."* With two of three branches refused, the first had no borrowable Keystone branch left to
count and the second had no second live row, so both would have kept passing while testing less. One
node moved buys two borrowable branches and one refused, and both rows keep their original claim.
**The alternative — rewriting both assertions — would have quietly narrowed two tests to fit a change
they are not about.**

*3. A stale comment corrected in place.* `GravecallerTree`'s remarks in `SplashFlowTests.cs` said
branch **2** ends in a Keystone; the tree has it in branch **1**, and
`Splash_BranchesOfDropsTheKeystone` has asserted so since M5-07a-ii. Read against the tree while
adding rows beside it.

*4. The refusal keys are `const string` on `SplashFlow` with `new LocKey(...)` at the point of use,
not `static readonly LocKey` fields.* `SaveDtos.EmptySlots` is precedent for a `private static
readonly` immutable, so the project would have allowed it — but `LocKey`'s constructor throws, and a
throw inside a static initialiser surfaces as `TypeInitializationException` with the real message one
level down. The keys are built once a run on a paused frame; there is nothing to save.

**Verified:** **2 527 EditMode / 0 / 0** (+6 on M5-08's 2 521) and **PlayMode 21 / 0 / 0**, the
PlayMode suite run twice — the first pass was 20 / 1 on `FrameOrderTests.Ticker_RunsTheStepsInOrder`
with known issue 1's exact signature (***wrong wedge***, the body 0.0001 m *behind* the apex), and the
second was clean. Console otherwise silent; zero new analyzer warnings; `dotnet format whitespace
--verify-no-changes` green over all four touched C# files. `ProjectSettings/TimeManager.asset`
re-serialised itself and was reverted ([Traps §5](../../Traps.md)).

**Out of scope held.** Restless Dead stays in *Rot* — moving it makes Legion five nodes and Rot three,
which is authoring, and M7-04 places all 81 nodes anyway ([parking lot](../ROADMAP.md#parking-lot)).
Rule 6's *a class with no borrowable branch at all* is still unasserted and still cannot happen with
two classes; M6-07's third class changes the arithmetic before it could.
