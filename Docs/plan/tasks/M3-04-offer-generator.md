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
6. **`count` is a parameter, three by default** — GD §13.4's Vigil offers two, and that is M6-06 passing 2 rather than a flag here. `count` below 1 and a destination shorter than `count` both throw.
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
- **Pact variants** (GD §13.2, M6-05) — a second pass over a drawn offer that corrupts one of them; the draw itself is unchanged.
- **Reroll and Banish** (GD §13.3, M6-02) — reroll is a second `Draw` from the same stream, banish is a filter on `Available` (M3-03's).
- **Vigil** (GD §13.4, M6-06) — passes `count` 2.
- **Promoting the counting stream to `Tests/Core/Fakes/`** — a third private copy would be the day; two is not (parking lot, one line).

## As built

_Filled at merge._
