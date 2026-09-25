# M3-04 — `OfferGenerator`: three from the available, weighted for variety, from the `Offers` stream

**Size:** S · **Depends on:** M3-03 · **Branch:** `m3-04-offer-generator`
**Design refs:** CH §4, §5.1, §8 (Q3); GD §13.1, §13.3, §13.4; AR §11.4, §18.3; ADR-0011 · **Ledger rows:** none

## Goal

A level-up's three offers are a seeded, weighted, no-replacement draw from what the tree makes available — never from the whole tree, never from a stream anything else draws on, and never a dead offer when a live one was there to be drawn.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Progression/OfferGenerator.cs` | Core | the draw and the weight table |
| `Tests/Core/Progression/OfferGeneratorTests.cs` | Tests.Core | with a private counting stream and a private LCG, `SpawnDirectorTests`' shape |
| *small edits* | | none in production. Nothing registers or calls it: M3-08 builds one in `Start` beside the tree and calls `Draw` when the screen opens |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Progression;

public sealed class OfferGenerator
{
    public const int DefaultOfferCount = 3;         // CH §5.1; Vigil passes 2 (GD §13.4)
    public const float SameBranchPenalty = 0.5f;    // per offer already drawn this call from the branch
    public const float ActiveBoost = 2f;            // for an Active while fewer than ActivesWorthBoosting are owned
    public const int ActivesWorthBoosting = 2;      // CH §8 Q3's "if you own fewer than 2"

    public OfferGenerator(TreeRules rules);         // two buffers sized rules.Count, and three ints

    /// Fills `destination` with up to `count` distinct available nodes and returns how many.
    public int Draw(SkillTree tree, IRandomStream offers, int count, Span<ContentId> destination);

    /// The table, pure, so it is testable without a stream.
    public static float Weight(SkillKind kind, int branch, ReadOnlySpan<int> drawnPerBranch, int ownedActives);
}
```

## Behaviour

1. **Random from the available, never from the tree** (CH §5.1, GD §13.1). The candidate set is `tree.Available` and nothing else; `count` is clamped to it; with nothing available the call writes nothing and **makes no draw** — there is no pick to make one for.
2. **One draw per pick, weighted, without replacement.** Each pick makes exactly one `NextFloat()` and walks the remaining candidates' cumulative weights in tree order (M3-03 rule 3); the chosen one is removed and the next pick walks what is left. Consumption is therefore a function of the pick count alone — the same seed against the same tree state gives the same offer, and a candidate refused or removed costs nothing extra. AR §18.3's *"choosing makes exactly one draw whatever it then finds"*, for a pick rather than a position.
3. **The weights are CH §8 Q3's soft variety, ruled yes at M3-00a.** Base 1. **× 0.5 for each offer already drawn this call from the same branch**, so a third from one branch is possible and four times less likely than the first — GD §13.1 wants variety, not a rule that forbids a build. **× 2 for an Active while the player owns fewer than two** — a first active is what turns a stat sheet into a build (CH §4), and CH §8 Q3's own example. Upgrades weigh 1: their parent gate already makes them a considered offer. `Weight` is a pure static so the table is a test and not an inference from ten thousand draws.
4. **`Offers` and no other stream** (ADR-0011): a reroll or a Pact must never shift what the next stage is made of, and a wave must never change what the next screen shows. Passed in, not held, `RunSession`'s shape for `Spawn`.
5. **Allocates nothing after construction** — two arrays sized `rules.Count` and three per-branch counters, refilled per call. A level-up screen is a moment the frame is already spending on UI.
6. **`count` is a parameter, three by default** — GD §13.4's Vigil offers two, and that is M6-06b passing 2 rather than a flag here. `count` below 1 and a destination shorter than `count` both throw.
7. **Which stat a node moves is not read here.** The generator sees kinds and branches; a weight on "the player has no damage yet" would be a second opinion about balance inside a draw, and M3-12's tree layout is where that opinion belongs.

**Inherited by M3-08, not testable here:** *the draw is lazy.* `Draw` is called when the level-up screen opens, never on the tick the level was earned. The boundary snapshot is taken on entering `Clear` — the same tick as a stage's last kill, which is the kill most likely to level — and captures the `Offers` position *before* the draw, so a run killed with a pick owed rolls the **same** offer on resume rather than a fresh one. Ordering closes the free-reroll-by-killing-the-app hole; M3-08 owes `Offer_IsDrawnAfterTheBoundarySnapshot`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Draw_FromAvailableOnly` | the fresh 27-tree / `Draw(3)` / three ids, all in `Available`, all distinct (rule 1) |
| `Draw_NeverRepeatsWithinAnOffer` | the LCG over 1 000 seeds, a state with 8 available / `Draw(3)` each / never a duplicate in one offer (rule 2) |
| `Draw_FewerThanCountWhenScarce` | two available / `Draw(3)` / 2 written, exactly two draws on the counting stream |
| `Draw_NothingAvailable_NoDraw` | a full tree / `Draw(3)` / 0 written, zero draws (rule 1) |
| `Draw_IsDeterministic` | one `FixedRandom` script, one tree state, twice / `Draw` / the same ids in the same order (rule 2) |
| `Draw_OneDrawPerPickAndOnlyOffers` | a counting `IRandom` / `Draw(3)` / `Offers` drew 3, the other four streams 0 (rules 2, 4) |
| `Draw_AllocatesNothing` | warm-up / 10 000 × `Draw` / allocated-bytes delta == 0 (rule 5) |
| `Draw_CountIsAParameter` | — / `Draw(2)` / 2 written (rule 6) |
| `Draw_Guards` | count 0; destination of 2 with count 3; null tree; null stream / `Draw` / throws each |
| `Weight_Base` | Passive, nothing drawn, 0 actives / `Weight` / 1 |
| `Weight_SameBranchHalves` | `drawnPerBranch[b]` 1, then 2 / `Weight` / 0.5, 0.25 (rule 3) |
| `Weight_ActiveBoostedBelowTwo` | Active, owned 0, 1, 2 / `Weight` / 2, 2, 1 |
| `Weight_UpgradeIsOne` | Upgrade / `Weight` / 1 |
| `Weight_Compounds` | Active in a branch with one drawn, 0 owned / `Weight` / 1 |
| `Draw_UsesTheWeights` | a scripted 0.99 after one pick from branch A, candidates A, A, B, C remaining / second pick / lands in C — under uniform weights the same float lands in A's second (rule 3) |
| `Draw_SpreadsBranches` | a state with 4 available in A and 2 each in B and C, the LCG over 10 000 seeds / `Draw(3)` / offers with all three from A are under 3 % — uniform-from-8 would give 7.1 %, the weights ≈ 1.5 % |
| `Draw_NoTreeIsTheCallersProblem` | — / — / *no row*: M3-03 rule 10's null tree never reaches this class; M3-08 does not build a generator for it |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

None. Nothing draws until M3-08 opens a screen.

## Out of scope

- **When to draw** — M3-08, with the note above.
- **The screen** — M3-08.
- **Pact variants** (GD §13.2, M6-05b) — a second pass over a drawn offer that corrupts one of them; the draw itself is unchanged.
- **Reroll and Banish** (GD §13.3, M6-02b) — reroll is a second `Draw` from the same stream, banish is a filter on `Available` (M3-03's).
- **Vigil** (GD §13.4, M6-06b) — passes `count` 2.
- **Promoting the counting stream to `Tests/Core/Fakes/`** — a third private copy would be the day; two is not (parking lot, one line).

## As built

**Built exactly the Files table: two new files, nothing else in `Assets/`.**
`Core/Progression/OfferGenerator.cs` and `Tests/Core/Progression/OfferGeneratorTests.cs`, plus their
two `.meta`s. No folder `.meta` — both folders already existed and now hold four files each. Nothing
in production references the class: M3-08 builds one in `Start` beside the tree, so the table's
*"small edits: none in production"* was literal and stayed that way.

**Four deviations, all named here, two of them corrections to this spec.**

1. **`Draw` refuses a tree built over different `TreeRules` — the owner's ruling, asked before a
   line was written.** The constructor takes `TreeRules` and `Draw` takes a `SkillTree`, so the two
   can disagree, and `SkillTree.Rules` is public so one `ReferenceEquals` closes it. Against it was
   M0-10's argument that `RunState`'s constructor carries no guards because it is reachable only
   from core with arguments core just built. **What decided it is that the failure is asymmetric and
   silent:** only a *larger* wrong tree trips `SkillTree.Available`'s short-buffer refusal, so a
   same-sized or smaller one fills a correctly sized buffer with another class's ids and returns a
   plausible count — and the first symptom is M3-08's `ChooseOffer` handing `Take` a node that tree
   has never heard of, in a run, at the moment the player taps it. The M0-10 analogy also does not
   carry: `RunState`'s arguments are its own parts, established once; `Draw` re-asserts the pairing
   on **every call**, which is `SkillTree.Restore`'s shape rather than a composition root's. The
   refusal distinguishes the two cases — another class's tree, and a second `TreeRules` over the
   same spec — because they are different mistakes. One row, `Draw_RefusesAnotherTreesRules`.

2. **`Draw_UsesTheWeights` used a float that did not discriminate, and the row's own contrast
   sentence was false.** As written — a scripted **0.99** with A, A, B, C remaining and one already
   drawn from A — it lands in **C under both** tables: weighted, total 3.0 and 0.99 × 3 = 2.97
   against cumulative 0.5 / 1.0 / 2.0 / 3.0; uniform, total 4.0 and 0.99 × 4 = 3.96 against
   1 / 2 / 3 / 4. So the row would have gone green against a generator that ignored the weight table
   completely, and its *"under uniform weights the same float lands in A's second"* was wrong twice
   over. **Shipped with 0.7**, which separates them: 0.7 × 3 = 2.1 → **C** weighted, 0.7 × 4 = 2.8 →
   **B** uniform. Intent unchanged; the arithmetic is written out in the row. This is M3-02a's NaN
   spelling again — a spec row that tested that something *happened* rather than that the rule holds.

3. **One behaviour row added: `Draw_BoostsAnActiveThroughTheTable`.** Row 2 above pins that `Draw`
   applies the **same-branch penalty**; nothing pinned that it applies the **Active boost**, so a
   `Draw` that inlined half the table and dropped the other half would have passed. Two available
   nodes, the Active first in tree order, the player owning none: weighted 2 + 1 = 3.0 and
   0.6 × 3 = 1.8 → the **Active**; uniform 1 + 1 = 2.0 and 0.6 × 2 = 1.2 → the **Passive**. This is
   the row that makes *"`Draw` calls the public static `Weight`"* a fact rather than an intention —
   M3-03's precedent, where `SkillTree.Check` pays two dictionary probes per candidate to ask
   `TreeRules.RequiredTakenInBranch` rather than keep a second copy of the keystone exception.

4. **`Draw_SpreadsBranches` asserts the weighted figure, not the spec's ceiling.** The spec's
   numbers are right and needed no correction — uniform-from-8 is C(4,3)/C(8,3) = **7.14 %** and the
   weights give 0.5 × (1.5/5.5) × (0.5/4.5) = **1.52 %** — but *"under 3 %"* alone is also met by a
   generator that never offers three from one branch at all, which is the hard rule GD §13.1
   explicitly does not want. The row asserts **1.52 % ± 0.5 pp** and keeps the 3 % ceiling as a
   second assertion.

**No non-finite row, and that is not an omission.** The implied guards are a validation row per new
type, a null row per public constructor and a non-finite row per `float` door — and this class has
**no `float` door**: `Weight` takes a kind, an int, a span of ints and an int, and `Draw` takes no
`float` at all. The only float in the class is the one it reads back out of the stream. M3-03 said
the same for the same reason. The other two implied rows are present: `Generator_NullRules_Throws`
and `Weight_Guards` (branch out of range either way, a negative drawn count, a negative
`ownedActives`, and an undefined `SkillKind` — the last against the table's loud `default`, which
exists for `PlayerStats.Resolve`'s reason).

**Implementation notes worth keeping.** Removal is a **compaction**, never a swap with the last:
`Available`'s order is what a seed means (AR §18.3), and a swap would reorder the survivors and
change every seed's meaning — enemy despawn's rule, for its reason. The three per-branch counters
are a `stackalloc int[SkillTreeSpec.BranchCount]` inside `Draw` rather than a field, because they
mean nothing between calls; the two heap buffers are sized by `TreeRules.Count` because `Available`
refuses a short destination rather than truncating it. `ownedActives` is read **once** at the top of
`Draw`: a node drawn into an offer is not owned, so the boost answers the same for all three picks,
and the drawn term is the only thing a call accumulates. The cumulative walk stops one short and
falls through to the last candidate, which is the right answer rather than an unwritten index — the
total is a sum of floats re-added in the same order, so the last comparison can miss by an ulp.

**Verified: 1 286 EditMode / 0 / 0, three times** (two consecutively, then a third after PlayMode),
against M3-03's 1 266 — **20 new rows**, which reconciles to the row: the spec's Tests table has 17
lines of which `Draw_NoTreeIsTheCallersProblem` is explicitly *no row*, so **16 named** + the 2 added
above + the 2 implied guard rows = 20. **PlayMode 11/11 twice**, the Known-issues row not firing;
this task touches no `Tick`, no view, no scene and nothing in production, so it could not have moved
it either way, and the tally is recorded because a gap is worse than a green run. Six assemblies,
**zero compile errors, zero analyzer warnings** — a sweep for `CS`, `UNT` and `IDE` diagnostics
across the whole Console returns nothing, against 10 `LogAssert`-expected fixture errors and 23
runtime `Debug.LogWarning`s from M3-02b's `OnValidate` rows and M0-13's fallback-seed row. **Both new
files confirmed in their intended assembly through `GetAssemblyNameFromScriptPath` and both new
types by direct `typeof`** rather than assumed. `git status` clean of anything unasked: two new
`.cs`, two `.meta`, and three docs — no `ProjectSettings/` diff, no folder `.meta`, no prefab, no
scene re-serialisation, no asset churn.

**Two traps confirmed and one new one.** Traps §7's `TestRunnerApi.RegisterCallbacks` row
reproduced exactly as written — the EditMode collector fired again on the PlayMode run and
overwrote its own results file with `passed=11`; each run's file was read before the next started.
Traps §4's `File.Delete` refusal cost one dead end, which is the third time that bullet has been
paid for. **New, and filed in [Traps §7](../../Traps.md) — one file outside the Files table, named here as a
deviation:** a PlayMode run **clears the Console on entering Play**, so a Console sweep taken after
one describes eleven tests and reads as a clean suite. Sweep after the run you mean to describe, or
run EditMode last. It is also why the Console was empty at the start of this session.

**Ledger: opens none, closes none, moves none — and that is the right answer rather than an
omission**, checked row by row rather than taken from the spec's *"Ledger rows: none"*. It ships no
content and raises no damage (row 1), writes nothing to disk and needs no version — the `Offers`
position has been in `RandomState` since M0-04 (row 2), draws nothing (rows 5 and 6), adds no
`LocKey` reader, because what it hands out is `ContentId`s (row 9), and rows 4 and 8 are device and
stopwatch questions it cannot touch. **It moved one parking-lot line**, which is the correct home
for an obligation with no owning task: the counting `IRandom` fake is now on its **second** private
copy, so the promotion rule has a fact behind it rather than a prediction — and the line records
that the private **LCG** beside it is a *first* copy, promoted on its own count.
