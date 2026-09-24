# M7-02a — What an Elite is: a body upgraded at the door

**Size:** S · **Depends on:** M7-01b · **Branch:** `m7-02a-what-an-elite-is`
**Design refs:** GD §8.3, §11.2, §15, §16.2; AR §18.1, §18.3; ADR-0008 · **Ledger rows:** none moved — [11](../ROADMAP.md#carry-forward-into-m7) is [M7-02b](M7-02b-who-buys-an-elite.md)'s, and this is the body it buys

## Goal

`EnemySystem.Spawn` can make any body an Elite — 2.2× its hit points, worth 2.5× its experience,
flagged on the agent and on the event — and a mode authors what an Elite is. **Nothing buys one yet**:
that is M7-02b, and this task is the half with no composer in the room.

## What was already built for it

The seams are the most prepared in the project, and each says the same thing — **an Elite is a body
upgraded at spawn, not an archetype**:

- `EnemySpawned.IsElite`'s remarks — *"M7-02, whose Elites are a body upgraded at spawn by spending
  `WavePlan.UnspentThreat` (GD §8.3) rather than an archetype. Today it comes off `EnemySpec.IsElite`
  … the day an affix makes one, it comes off the affix and nothing downstream changes."*
- `DepthScaling` — *"`PercentMult` and not `PercentAdd`, so depth multiplies with an Elite's 2.2×
  rather than pooling with it (GD §8.3)"*, and AR §18.3's row saying the same.
- `EnemyHealthBar.Bind(id, isElite)` — an Elite's bar never fades (M3-13b), already reading the event.
- `TargetScorer` — `EliteBonus` 2.0, already scoring `TargetCandidate.IsElite`.
- `EssenceSpec.PerElite` — 15, *"and nothing pays it until M7-02"*.

**What reads the wrong thing is two lines.** `EnemySystem.Spawn` publishes `spec.IsElite` and
`PlayerCombat.BuildCandidates` scores `agent.Spec.IsElite` — both ask the *archetype* a question that
is now about the *body*.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ModeSpec.cs` | Core | `EliteSpec`, beside `EssenceSpec` in the file that holds the mode's other blocks, and `ModeSpec.Elites` optional and last (rule 1) |
| `Core/Ai/EnemySystem.cs` | Core | `Spawn(..., elite)` applies it; `ApplyDamage`'s kill branch pays it (rules 2–5) |
| `Tests/Core/Ai/EliteBodyTests.cs` | Tests.Core | **New.** Every rule below |
| *small edits* | Core, Game, Data | `Core/Ai/EnemyAgent.cs` — `IsElite`, reset in `Initialise` (rule 3); `Core/Combat/PlayerCombat.cs` — `BuildCandidates` reads the agent (rule 3); `Core/Run/RunSession.cs` — the census is built with the mode's `Elites`; `Game/Authoring/ModeDefinition.cs` — an `EliteBlock` beside `EssenceBlock`; `Data/Modes/Descent.asset` — rule 1's numbers; `Tests/Game/Authoring/ContentValidationTests.cs` — `EveryShippedMode_AuthorsItsElites` (rule 1) |
| *ripple* | — | **81 `new ModeSpec(...)` sites across 53 files and 54 `new EnemySystem(...)` sites across 28 files are untouched** — both arguments are optional and last |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// <summary>
/// GD §8.3's Elite, as a mode authors it: from which stage the composer may buy one, how often a
/// purchase is one, and what one costs and weighs against the body it upgrades.
/// </summary>
/// <remarks>
/// A struct, defaulted and last on <see cref="ModeSpec"/>, for <see cref="EssenceSpec"/>'s reason:
/// <c>default</c> is <em>this mode has no Elites</em>, which is every fixture that predates M7 and is a
/// true statement about them rather than a misconfiguration.
/// </remarks>
public readonly struct EliteSpec
{
    /// <param name="firstStage">The first stage the composer may buy one at. 8 (GD §8.2). At least 1.</param>
    /// <param name="chance">The share of purchases from that stage that are Elites, in [0, 1]. 0.1 — M7-02b's number.</param>
    /// <param name="costMultiplier">What one costs against its archetype. 2.5 (GD §8.3). At least 1.</param>
    /// <param name="hpMultiplier">What one weighs against its archetype. 2.2 (GD §8.3). At least 1.</param>
    public EliteSpec(int firstStage, float chance, float costMultiplier, float hpMultiplier);

    public int FirstStage { get; }
    public float Chance { get; }
    public float CostMultiplier { get; }
    public float HpMultiplier { get; }

    /// <summary>Whether the mode authored one at all — <c>FirstStage</c> above zero.</summary>
    public bool IsAuthored { get; }
}

public sealed class ModeSpec
{
    // ... after `ordeals`, defaulted and last:  EliteSpec elites = default
    public EliteSpec Elites { get; }
}
```

```csharp
namespace Soulvail.Core.Ai;

public sealed class EnemyAgent
{
    /// <summary>
    /// Whether this body was upgraded to an Elite when it spawned (GD §8.3). A fact about the body,
    /// never the archetype — false on every fresh agent, set only by <c>EnemySystem.Spawn</c>.
    /// </summary>
    public bool IsElite { get; internal set; }
}

public sealed class EnemySystem
{
    // Constructor: after `veilrot`, defaulted and last:  EliteSpec elites = default

    /// <param name="elite">
    /// Spawn this body as an Elite. Defaulted, on <c>EnemySpawned.isElite</c>'s terms: <c>false</c>
    /// <em>is</em> not-an-Elite. An archetype authored <see cref="EnemySpec.IsElite"/> is always one.
    /// </param>
    /// <exception cref="InvalidOperationException">An Elite was asked for on a run whose mode authors none (rule 2).</exception>
    public EnemyAgent Spawn(ContentId specId, Vector3 position, bool elite = false);
}
```

## Behaviour

1. **What an Elite is, is the mode's statement.** `EliteSpec` sits on the mode beside `EssenceSpec`,
   because GD §4.5's other modes — a Trial, a Boss Rush — are exactly the thing that would author
   different Elites, and a number in code would be one mode's number in every mode. **Descent authors
   8 / 0.1 / 2.5 / 2.2**: GD §8.2's first stage, GD §8.3's cost and weight, and M7-02b's chance.
   `ContentValidationTests.EveryShippedMode_AuthorsItsElites` asserts every shipped mode authors one,
   so the default that keeps 81 fixtures compiling cannot reach a build — M6-01a rule 3's bargain.
2. **The upgrade is one modifier, applied after depth and before the announcement.** In `Spawn`, after
   `DepthScaling.Apply`: one `PercentMult` of `HpMultiplier − 1` on `Health.MaxHp`, sourced to the
   census, then `Health.Reset()` so the body arrives full at the upgraded maximum — AR §18.1's
   *"`DepthScaling.Apply` refills health after the `MaxHp` modifier goes on"*, owed a second time for
   a second modifier. **Before `EnemySpawned`**, so a bar built on the event is sized to the Elite's
   maximum on its first frame (AR §18.1's spawn row). An Elite on a run whose mode authors none
   throws, naming the wiring: a silent plain body would be an Elite the composer paid 2.5× for.
3. **`IsElite` lives on the agent, and both readers move to it.** `EnemyAgent.IsElite` is false after
   `Initialise` — so a recycled Elite cannot come back as one, AR §18.1's `RemoveAll()` row applied to
   a flag — and set by `Spawn`. `EnemySpawned` carries it, so `EnemyHealthBar` holds an Elite's bar
   unchanged; `PlayerCombat.BuildCandidates` reads it, so `EliteBonus` scores the body the composer
   bought rather than an archetype nobody authors. **`EnemySpec.IsElite` stays**: it now means *this
   archetype is always an Elite*, which is harmless, costs no ripple through 62 constructions, and
   is authored false on every shipped asset.
4. **GD §8.3's cap is the modifier's shape, and it needs no clamp.** *"Elite HP caps at 2.2× and
   never scales past it"* is true because the upgrade is one `PercentMult` applied once at the door:
   depth's `h(n)` multiplies with it (AR §18.3), and nothing an Elite is or does adds a second.
   [M7-02c](M7-02c-the-affix-roll.md)'s affixes are written against this rule, not around it.
5. **An Elite is worth its price in experience.** The kill branch banks `XpValue × CostMultiplier`
   for an Elite — 30 for a Husk's 12 — so `EnemySpec.XpValue`'s convention, that a stage's experience
   follows the threat it spent, still holds when part of that threat bought Elites. **The Essence term
   is not paid here**: GD §15 pays *"15 per Elite"* at the stage clear, and the composition that knows
   how many a stage held is [M7-02b](M7-02b-who-buys-an-elite.md)'s.
6. **An Elite is the same body in every other respect.** Speed, damage, reach, windup and every
   authored block are its archetype's; the behaviour, the split, the explosion, the guard all run
   unchanged. GD §8.3's *"priority targets, not walls"* is the HP multiplier and the scorer's bonus,
   and nothing else.
7. **Nothing spawned by a death is an Elite.** A Weaver's children and a boss's summons come through
   `Spawn` with the default, so an Elite Weaver is one Elite and two ordinary Weaverlings —
   [M7-01b](M7-01b-the-weaver.md) rule 5's line, inherited rather than restated.
8. **Nothing allocates.** The source token is a field; the modifier is a struct.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Elite_WeighsTwoPointTwoTimesItsBody` | a Husk at depth 1, then at depth 20, each as an Elite / spawn / max HP 36 × 2.2, then 36 × h(20) × 2.2 — rules 2, 4 |
| `Elite_ArrivesFull` | an Elite at depth 20 / spawn / `Health.Current == MaxHp.Value` — rule 2 |
| `Elite_IsAnnouncedAsOne` | an Elite / spawn / `EnemySpawned.IsElite` true, and the bar a handler reads inside it is the upgraded maximum — rules 2, 3 |
| `Elite_IsScoredAsOne` | an Elite Husk beside a plain one, equal otherwise / a targeting tick / the Elite — rule 3 |
| `Elite_ARecycledAgentForgets` | an Elite despawned and its agent rented as a plain Husk / — / `IsElite` false, max HP 36 — rule 3 |
| `Elite_AnAuthoredEliteArchetypeIsAlwaysOne` | a spec authored `isElite: true` / `Spawn` with the default / upgraded — rule 3 |
| `Elite_OnARunWithoutElitesThrows` | a mode with no `EliteSpec` / `Spawn(..., elite: true)` / `InvalidOperationException` naming it, nothing registered — rule 2 |
| `Elite_PaysItsPriceInExperience` | an Elite Husk killed / the drain / 30 — rule 5 |
| `Elite_PaysNoEssenceOnTheKill` | an Elite killed mid-stage / — / the wallet unmoved — rule 5 |
| `Elite_IsOtherwiseItsArchetype` | an Elite Husk and a plain one / a full strike cycle each / identical intents, telegraph and damage — rule 6 |
| `Elite_ChildrenOfAnEliteAreOrdinary` | an Elite Weaver killed / the split / two Weaverlings, `IsElite` false, max HP 20 × h(n) — rule 7 |
| `Elite_SpawnAllocatesNothingOnARecycledAgent` | 1 000 Elite spawns and despawns / `AllocationAssert.None` after the first life / zero — rule 8 |
| `Spec_TheMissingBlockIsNoElites` | `default(EliteSpec)` / — / `IsAuthored` false — rule 1 |
| `Spec_RefusesAMultiplierBelowOne` | cost 0.9, then HP 0.9 / — / throws, naming the field |
| `Spec_RefusesAChanceOutsideZeroToOne` | 1.1, then −0.1 / — / throws |
| `Descent_AuthorsGdsElites` | `Descent.asset` / converted / 8, 0.1, 2.5, 2.2 — rule 1 |
| `EveryShippedMode_AuthorsItsElites` | every `ModeDefinition` under `Data/` / — / `Elites.IsAuthored` — rule 1 |

**Guard rows are implied, not listed:** a non-finite chance or multiplier, and a `firstStage` below 1
on an authored block.

## Manual verification (Editor / device)

None: nothing buys an Elite until M7-02b, and no shipped asset authors one. The rows are the
evidence, which is [M6-01a](M6-01a-essence-wallet-and-drops.md)'s arrangement for a wallet nothing
spent.

## Out of scope

- **Buying one.** [M7-02b](M7-02b-who-buys-an-elite.md).
- **The 15 Essence.** M7-02b, at the clear (rule 5).
- **Affixes.** [M7-02c](M7-02c-the-affix-roll.md) and [M7-02d](M7-02d-the-two-that-fire-on-death.md), specced in M7-00b; the `Affixes` stream is
  not drawn here.
- **The outline.** GD §8.3's *"distinct emissive outline"* is [M7-02e](M7-02e-an-elite-you-can-see.md)'s;
  the bar that never fades already ships.
- **A `TagSet`.** ADR-0010's `Elite` tag waits for a query that needs it; `IsElite` is the one
  question anything asks today.
