# M7-02f — A second affix, bought with what the cap leaves

**Size:** M · **Depends on:** M7-02d · **Branch:** `m7-02f-a-second-affix-bought`
**Design refs:** GD §8.3, §11.2, §12.1, §12.2, §15; AR §18.3; ADR-0011 · **Ledger rows:** none moved. The spend [M7-02b](M7-02b-who-buys-an-elite.md) handed on — *"what is left after every body is one waits for the second affix"* — is rule 2

## Goal

From stage 30, when a capped wave has already made every body an Elite and budget is still left over,
the composer buys those Elites a second affix. The door rolls it against GD §8.3's blacklist, so a
Warded body never also siphons.

## The reading of GD §8.3, ruled here

*"From stage 30, Elites may roll two affixes"* admits a schedule, where every Elite past 30 carries two,
and a purchase, where the budget buys the second. **It is a purchase**, for the reason
[M7-02b](M7-02b-who-buys-an-elite.md) gives for promoting rather than replacing: GD §11.2 lists
*"extra affixes"* as the third way surplus becomes quality, and a schedule spends nothing.

Under a schedule, a low-tier phone at stage 45 caps at 18 all-Elite bodies and leaves the rest of its
budget unspent, while a flagship spends it on 32 bodies. That makes difficulty depend on the phone,
which §11.2 calls *"not optional"*. Bought, the second affix is where that surplus goes.

**When it first appears therefore depends on the tier.** The arithmetic is M7-02b's, one step on. At
mid tier (cap 28) the cap binds from stage 36 and promotion absorbs wave 5's budget to about 40. At
stage 45 wave 5 is 770 against about 630 for 28 Elites, so the rest buys about fifteen second affixes;
by stage 50 it buys all 28. At low tier (cap 18) that happens from about 31, and at high tier (40)
from about 60, the stage its cap first binds. **GD §12.1's *"multi-affix Elites"* at stage 40 is the mid tier's answer**, and GD §8.3's
sentence is amended in this PR to name stage 30 as the earliest a second may be bought.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Director/WaveComposer.cs` | Core | **Substantial.** `Promote`'s second pass (rules 2–4) |
| `Core/Ai/EnemySystem.cs` | Core | `Spawn(..., secondAffix)` rolls it against the blacklist, and the kill pays for it (rules 6–9) |
| `Tests/Core/Director/SecondAffixTests.cs` | Tests.Core | **New.** Every rule below |
| *small edits* | Core, Game, Data, Docs | `Core/Director/WavePlan.cs` — `WaveEntry.SecondAffixCount`; `Core/Director/SpawnDirector.cs` — each queued Elite knows whether it bought a second (rule 5); `Core/Content/ModeSpec.cs` — `AffixScheduleSpec`'s two appended arguments and the stranded-affix refusal (rules 1, 7); `Game/Authoring/ModeDefinition.cs` — two fields on `AffixesBlock`; `Data/Modes/Descent.asset` — 30 and 1.0; `Docs/GameDesign.md` — §8.3's two-affix sentence |
| *ripple* | Tests.Core | M7-02b's composer, plan and director rows run unedited on fixtures that author no second stage (rule 3), and `Compose_ACappedDeepWaveKeepsItsMix` runs on Descent's shape with the second pass live (rule 4) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

public readonly struct AffixScheduleSpec
{
    /// <param name="secondStage">The first depth a second may be bought. 30 (GD §8.3). 0 for never; otherwise after <c>firstStage</c>.</param>
    /// <param name="secondCostMultiplier">What a second costs against the archetype. 1.0 — ours. Authored with the stage or not at all.</param>
    public AffixScheduleSpec(int firstStage, int secondStage = 0, float secondCostMultiplier = 0f);

    public int SecondStage { get; }
    public float SecondCostMultiplier { get; }
    public bool BuysSeconds { get; }
}
```

```csharp
namespace Soulvail.Core.Director;

public readonly struct WaveEntry
{
    /// <param name="secondAffixCount">How many of <paramref name="eliteCount"/> bought a second affix. 0..eliteCount.</param>
    public WaveEntry(ContentId specId, int count, int eliteCount = 0, int secondAffixCount = 0);
    public int SecondAffixCount { get; }
}
```

```csharp
namespace Soulvail.Core.Ai;

public sealed class EnemySystem
{
    /// <param name="secondAffix">This Elite bought a second affix (rule 6). Defaulted false.</param>
    /// <exception cref="InvalidOperationException">A second asked for on a body that is not an Elite, or below the mode's second stage.</exception>
    public EnemyAgent Spawn(ContentId specId, Vector3 position, bool elite = false, bool secondAffix = false);
}
```

## Behaviour

1. **A second affix is the mode's to allow and to price.** `AffixScheduleSpec` gains `SecondStage` and
   `SecondCostMultiplier`, and Descent authors 30 and 1.0.
   - **Both or neither**, which is `OrdealSpec`'s threat-cost rule: a stage with no price is free,
     and a price with no stage is inert.
   - **A second stage at or before the first is refused.**
   - The price is ours. At 1.0 a two-affix Elite costs 3.5× its archetype, one archetype more than
     an Elite, and the rule 2 arithmetic says that is what the budget can absorb between 40 and 50.
2. **The composer buys seconds only after every body is an Elite, and only from `SecondStage`.** At
   `Stage >= SecondStage`, once M7-02b's `Promote` pass has promoted every plain body in a capped
   wave, a second pass walks the wave's Elites that have no second yet. It goes cheapest-first with
   ties in roster order, and adds one second at a time while `ceil(cost × SecondCostMultiplier)` fits
   what is left.
   - **Cheapest-first**, for `Promote`'s reason: the most seconds the budget can buy.
   - **Only after the first pass is exhausted**, because a second affix on one Elite while a plain body
     stands beside it would spend on depth before breadth. GD §8.3's Elite is the unit of quality,
     and the second is the upgrade to it.
   - **An uncapped wave buys none**, because it has no surplus. The chance-bought Elites of M7-02b
     rule 2 carry one affix.
   - What is left after every Elite has two stays in `UnspentThreat`, and nothing buys a third.
3. **Nothing changes where no second is allowed.** Below `SecondStage`, or on a mode whose schedule
   authors none, the second pass does not run. The plan, the draws and `UnspentThreat` are then exactly
   M7-02b's, and every row that task wrote runs unedited.
4. **The second pass draws nothing and changes no archetype.** `Promote`'s rule 5 applies: which Elites
   get a second is forced by the budget, and a seed must not change a capped stage's difficulty. It
   edits `SecondAffixCount` and never `Count` or `EliteCount`, so row 11's instrument,
   `Compose_ACappedDeepWaveKeepsItsMix`, holds with it live.
5. **The plan carries the seconds, and the director spawns them.** `WaveEntry.SecondAffixCount`
   (≤ `EliteCount`) is written by `SetWave` from a parallel buffer. `StartWave` marks the **last**
   `SecondAffixCount` of each entry's Elite bodies, so a wave's two-affix Elites arrive last among
   its Elites. That is M7-02b rule 9's order one step on, and it is the order a player can learn from.
   The flag rides the queue beside the Elite flag, and `FireDueTelegraphs` passes it to `Spawn`.
6. **The door rolls the second after the first, on the same stream, as exactly one more draw.** The
   eligible set is the pool less the first affix, less any affix whose `Excludes` names the first, and
   less the first's own `Excludes`. The blacklist is checked **both ways**, so authoring it on one
   side is enough. The door counts the eligible affixes, draws `NextInt(0, k)`, and walks to the
   *k*-th in pool order, allocation-free. `EnemySpawned.SecondAffix` carries the result, and both
   affixes' dials and blocks apply (rule 8).
   - A second asked for on a body that is not an Elite throws, naming the wiring.
   - So does a second below `SecondStage`. The composer never asks for either, so a caller that does
     is a mistake rather than content.
7. **The door never has to buy nothing, because the mode refuses a pool that could make it.** A
   schedule that buys seconds is refused when any affix in the pool has no eligible partner. Descent's
   five, with one excluded pair, leave every affix at least three. The door can then rely on
   *k* ≥ 1, and a purchase the composer made is always a purchase the body receives.
8. **Two affixes each do what they do.** Dials multiply: only Hasted has one, and one body never
   carries Hasted twice. `AffixSet`'s block reads find a block on either affix, so a Volatile and
   Splintered Elite leaves a pool and throws a burst from the one death, by M7-02d rule 2's walk with
   nothing added. **Warded and Siphoning never share a body**. That is GD §8.3's blacklist, and the
   only one authored, since M7-02c.
9. **A two-affix Elite is worth its price in experience.** The kill branch banks
   `XpValue × (CostMultiplier + SecondCostMultiplier)`, which is 42 for a Husk. That keeps M7-02a
   rule 5's convention: a stage's experience follows the threat it spent. **Essence is unchanged**,
   because it is still one Elite, and GD §15's 15 is per Elite, not per affix.
10. **Nothing allocates.** The second pass reuses `Promote`'s buffers, and the eligibility walk is two
    loops over the pool.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Schedule_RefusesHalfASecond` | a stage with no price, then a price with no stage / — / throws — rule 1 |
| `Schedule_RefusesASecondAtOrBeforeTheFirst` | first 12, second 12 / — / throws — rule 1 |
| `Mode_RefusesAPoolThatStrandsAnAffix` | a pool of Warded and Siphoning, seconds allowed / — / throws, naming both — rule 7 |
| `Compose_BelowTheSecondStageBuysNone` | a capped stage-29 wave with surplus past every Elite / — / no seconds, the surplus unspent — rule 2 |
| `Compose_SecondsWaitForEveryBodyToBeElite` | a capped wave whose surplus runs out mid-promotion / — / no seconds — rule 2 |
| `Compose_SecondsAreBoughtCheapestFirst` | an all-Elite wave of Husks and Wardens with 20 left / — / five Husks buy one (4 each), no Warden — rule 2 |
| `Compose_AnUncappedWaveBuysNone` | stage 40, cap 40 / — / no seconds — rule 2 |
| `Compose_EverySecondBoughtLeavesTheRestUnspent` | surplus past two affixes on every Elite / — / every Elite two, spend + unspent = B(n) — rule 2 |
| `Compose_AModeWithoutSecondsIsUnchanged` | a mode whose schedule buys none / stages 30–60 / plans and positions identical to M7-02b's — rule 3 |
| `Compose_SecondsChangeNoArchetypeAndNoElite` | any capped wave / — / every `Count` and `EliteCount` as the first pass left them — rule 4 |
| `Compose_SecondsDrawNothing` | one capped stage composed twice from one position / — / identical plans and positions — rule 4 |
| `Plan_CarriesSecondsPerEntry` | a composed stage / — / Σ `SecondAffixCount` ≤ Σ `EliteCount` — rule 5 |
| `Entry_RefusesMoreSecondsThanElites` | `new WaveEntry(husk, 3, 1, 2)` / — / throws — rule 5 |
| `Director_SpawnsTheSecondsThePlanBought` | 3 Husks, 2 Elite, 1 second / the wave / the third spawn carries two affixes, the second one — rule 5 |
| `Roll_TheSecondIsNeverTheFirst` | every first pick × every draw / — / two distinct ids — rule 6 |
| `Roll_WardedAndSiphoningNeverShareABody` | the same exhaustive walk, the blacklist authored on Warded only / — / never the pair, in either order — rules 6, 8 |
| `Roll_TheSecondIsOneMoreDraw` | a two-affix Elite / spawn / the `Affixes` stream advanced by exactly two — rule 6 |
| `Door_RefusesASecondWithoutAnElite` | `Spawn(husk, p, elite: false, secondAffix: true)` / — / throws, nothing registered — rule 6 |
| `Door_RefusesASecondBeforeItsStage` | stage 29 / a second asked for / throws — rule 6 |
| `TwoAffixes_BothAct` | Hasted + Volatile, then Volatile + Splintered / spawn and kill / faster and a pool; a pool and a burst — rule 8 |
| `TwoAffixes_PayTheirPriceInExperience` | a two-affix Husk killed / the drain / 42 — rule 9 |
| `TwoAffixes_AreStillOneEliteForEssence` | a stage holding one / the clear / 15 for it — rule 9 |
| `Compose_SecondPassAllocatesNothing` | 1 000 capped compositions at stage 50 / after the first / zero — rule 10 |
| `Descent_BuysSecondsFromThirty` | `Descent.asset` / converted / 30, 1.0 — rule 1 |

**Guard rows are implied, not listed:** a non-finite or non-positive second multiplier, a negative
second stage.

## Manual verification (Editor / device)

**Not an Editor step, deliberately.** A second affix appears on the mid tier past stage 40, a run of
over half an hour, and nothing on screen names two affixes until M7-02e. The rows are the evidence.
**M7-08's instrument**, which counts Elites and affixes per wave, is what witnesses it in Play: M5-08's
rule that *a row that wants a number is discharged by an instrument*.

## Out of scope

- **A third affix.** Surplus past two on every Elite is `UnspentThreat`, and GD §12.1's table asks for
  nothing more by stage 40.
- **A second affix by chance.** An uncapped wave's Elites carry one (rule 2); a chance would be a second
  dial on difficulty that the budget does not pay for.
- **Drawing two names.** [M7-02e](M7-02e-an-elite-you-can-see.md).
