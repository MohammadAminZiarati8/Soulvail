# M6-05a — What a Pact is, and why 1.8× is a budget rather than an operation

**Size:** M · **Depends on:** M6-04 · **Branch:** `m6-05a-pact-nodes`
**Design refs:** GD §10.1, §13.1, §13.2, §16.4; CH §4, §4.4; AR §10.1, §13, §18.1, §18.2, §18.3; ADR-0006, ADR-0009 · **Ledger rows:** [7](../ROADMAP.md#carry-forward-into-m6) — this task's share of M6's strings

## Goal

A node can carry a corrupted form of itself — its own effects, its own description and its own
Veilrot price — the tree can take one instead of the clean node, and a run that is killed and resumed
still has the version it paid for.

## The counting, and the ruling it forced

**The ROADMAP predicted this task splits at [M5-06a](M5-06a-what-a-legion-node-may-reach.md)/b's
seam, and it does — but not for the reason it predicted.** That prediction was *"~1.8× a clean node
means scaling an open set of effect primitives, which is a question about `IEffect`."* Grepped, that
is the shape of a task that cannot be built at all:

- **`IEffect` is a marker interface with no members** (`Core/Effects/EffectRegistry.cs`). A
  `Corrupt(float)` on it is an edit to **six** primitive files — `ModifyStat`, `GrantShield`,
  `KnockbackOnSwing`, `ModifySkillCooldown`, `RaiseMinions`, `SpawnHealZone` — plus six fixtures,
  before one Pact is ever offered. That is three tasks, not one.
- **Multiplying is not the same as strengthening, and for half the shipped primitives it is
  backwards.** `SpawnHealZone.PulseInterval` × 1.8 heals *less often*; `RaiseMinions.Count` is an
  `int` and 1.8 × 2 is 3.6; `ModifySkillCooldown.Value` under `ModifierKind.PercentAdd` is a
  *reduction* authored negative, so the sign decides whether ×1.8 is a gift or a punishment; and
  `ModifyStat.Value` under `PercentMult` is not linear in "power" at all. So each primitive needs its
  own answer, which is the member, which is the six files.
- **The project already refused this exact shape once.** The
  [parking-lot line](../ROADMAP.md#parking-lot) on `EffectRegistry.CanApply` records the owner's
  M3-03 ruling against widening `IEffectHandler<T>`, and its argument — *"most implementations would
  be `=> true` — a rubber-stamp member"* — is the weaker half of the argument here: a `Corrupt`
  member would not be a rubber stamp, it would be **six separate design decisions with no author**,
  each silently wrong if the next primitive's writer guesses.

**And GD §13.2's own worked examples are not 1.8× of anything.** Beside *"+15 % fire rate · +20 max
HP · movement skill leaves a damaging trail"* it lists *"+45 % damage, **−25 max HP** *(+15 Rot)*"*
and *"kills heal 4 HP, **enemies spawn 20 % faster** *(+12 Rot)*"*. Those carry **downsides**. No
multiplication of a clean node produces a downside, so the examples the design actually wrote down
are hand-authored corrupted nodes and the table's *"Power ~1.8×"* is a **budget a designer authors
to**, not an operation the code performs. That reading also makes CH §4.4's *"roughly"* load-bearing
rather than apologetic.

**So a Pact is authored (ADR-0006), and the split is `PactSpec` against the offer that rolls one.**
This task is 3 counted files plus a fixture; [M6-05b](M6-05b-the-offer-that-rolls-one.md) is 4.
The contradiction between GD §13.2's table and its own examples is
[flagged for the owner rather than edited](../ROADMAP.md#parking-lot) — M3-02a rule 7's precedent.

## What it costs, stated rather than discovered

**A Pact is authored per node, so a node with no Pact block can never be offered as one** — which
narrows GD §13.2's *"**Any** node can appear in its corrupted form"* to *any node somebody wrote one
for.* That is a content obligation rather than a mechanism gap: the shipped trees are twelve nodes a
class, **M7-04 authors eighty-one**, and `Content_ReportsItsPactCoverage` prints the ratio at build
time so the gap is visible rather than inferred. The alternative — a mechanical corruption with no
author — is the six files above and three of them would be wrong.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/SkillSpec.cs` | Core | **Substantial.** `PactSpec`, `SkillSpec.Pact`, and the validation both ways |
| `Core/Progression/SkillTree.cs` | Core | **Substantial.** A node can be taken in its corrupted form, and the tree remembers which |
| `Game/Authoring/SkillDefinition.cs` | Game | **Substantial.** The Pact block in the Inspector, and its rewrapped failures |
| `Tests/Core/Content/PactSpecTests.cs` | Tests.Core | The block, its band, and the kinds it is refused on |
| *small edits* | Core, Game | `Core/Save/RunRecorder.cs` — `PactedNodeIds` gets its writer ([M6-01b](M6-01b-save-format-v4.md) rule 7's third placeholder); `Core/Run/RunState.cs` — the `PactedNodeIds` read forwards to the tree; `Core/Run/RunSession.cs` — one restore argument; `Data/Skills/**` — four Pact blocks; `Data/Effects/**` — the effects they name; `Data/Localisation/English.asset` — four description rows |
| *ripple* | Tests.Core, Tests.Game | `SkillSpecTests` and `SkillTreeTests` gain the Pact cases; `SkillDefinitionTests` gains the block; `ContentValidationTests` gains rules 6 and 7; `RunSessionResumeTests` gains a Pact surviving a kill |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Content/SkillSpec.cs — a fourth type in the file that already holds three, for its own
// stated reason: a kind, the block that kind requires, and the record that carries both.
namespace Soulvail.Core.Content;

/// <summary>
/// GD §13.2's corrupted form of one node: what it does instead, what it says, and what it costs in
/// Veilrot. Authored beside the clean node rather than derived from it — see the task's ruling.
/// </summary>
public sealed class PactSpec
{
    /// <summary>The least Veilrot a Pact may ask. GD §13.2 and CH §4.4's band.</summary>
    public const float MinVeilrot = 10f;

    /// <summary>The most. A band rather than a number, because GD §13.2 writes one.</summary>
    public const float MaxVeilrot = 20f;

    /// <param name="effects">
    /// What taking the corrupted node puts into force — **instead of** the clean node's, never as
    /// well as (rule 3). At least one, no nulls. Copied.
    /// </param>
    /// <param name="veilrot">What it adds to GD §10's meter. In [10, 20].</param>
    /// <param name="descriptionKey">
    /// What it says. Its own, because its effects differ; the **name** stays the clean node's
    /// (rule 4).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="effects"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="effects"/> is empty or holds a null;
    /// <paramref name="descriptionKey"/> is a default.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="veilrot"/> is not a finite number inside the band.
    /// </exception>
    public PactSpec(IReadOnlyList<IEffect> effects, float veilrot, LocKey descriptionKey);

    public IReadOnlyList<IEffect> Effects { get; }
    public float Veilrot { get; }
    public LocKey DescriptionKey { get; }
}

public sealed class SkillSpec
{
    // ... the six arguments that exist, then, optional and last (rule 2):
    //     PactSpec pact = null

    /// <summary>
    /// The corrupted form of this node, or <see langword="null"/> for a node nobody wrote one for.
    /// </summary>
    public PactSpec Pact { get; }

    /// <summary>Whether this node can ever be offered as a Pact — rule 2.</summary>
    public bool HasPact { get; }
}
```

```csharp
namespace Soulvail.Core.Progression;

public sealed class SkillTree
{
    /// <summary>
    /// Takes <paramref name="id"/>, in its clean form or its corrupted one (GD §13.2).
    /// </summary>
    /// <param name="asPact">
    /// Whether to apply <see cref="SkillSpec.Pact"/>'s effects instead of the node's own.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// The node is not available, or <paramref name="asPact"/> is true and it has no Pact.
    /// </exception>
    public void Take(ContentId id, bool asPact = false);

    /// <summary>Whether this run took <paramref name="id"/> in its corrupted form.</summary>
    public bool IsPact(ContentId id);

    /// <summary>The ids taken as Pacts, in take order — a subset of <c>TakenIds</c>.</summary>
    public IReadOnlyList<ContentId> PactedIds { get; }

    /// <summary>
    /// Replays a save's picks, corrupting the ones <paramref name="pacted"/> names — rule 5.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// An entry of <paramref name="pacted"/> is not in <paramref name="takenInOrder"/>, or names a
    /// node with no Pact.
    /// </exception>
    public void Restore(IReadOnlyList<ContentId> takenInOrder, IReadOnlyList<ContentId> pacted);
}
```

## Behaviour

1. **A Pact replaces the node's effects rather than adding to them.** GD §13.2's table is two
   columns — *Clean node* against *Pact node* — and its examples describe whole nodes with their own
   downsides, not a clean node with a bonus stapled on. So `Record` applies `Pact.Effects` **instead
   of** `Spec.Effects`, with the same `source` (the `SkillSpec`), so `EffectRegistry.Remove` and
   `Stat.RemoveAll(spec)` keep working unchanged and nothing downstream has to know which version is
   in force.
2. **The block is optional and last on `SkillSpec`, because the alternative is 70 edits.** `new
   SkillSpec(...)` has **70 call sites across 24 files** — every fixture in the project builds one —
   so `pact` goes after `parentId`, exactly where `EssenceSpec` went on `ModeSpec` for the same
   counted reason ([M6-01a](M6-01a-essence-wallet-and-drops.md) rule 2). `HasPact` is the read every
   caller uses; nothing compares `Pact` to null outside this file.
3. **A Pact is refused on a `SkillKind.Active`, and three of this build's twenty-four nodes are
   Actives.** An Active's power is on cast (`SkillSpec`'s own rule: it is the one kind that may take
   with no `Effects`), so a corrupted Active is a second `ActiveSpec` — a second cooldown `Stat`, a
   second `TriggerSpec` and a second auto-cast slot — and `SkillRunner.Add(spec)` takes a
   **`SkillSpec`**, which a `PactSpec` is not. Threading the corrupted version through
   `LevelUpFlow.Choose`'s `_runner.Add(spec)` line is a mechanism rather than a field, and it buys
   three nodes: **Exhume, Bulwark and Consecrate**. Refused here in the constructor, with the id in
   the message, and promoted by **M7-04**, which authors eighty-one nodes and is the first task with
   enough corrupted Actives to be worth the runner's second door. This is
   [M5-06b](M5-06b-gravecaller-tree-v1.md) rule 3's shape: refuse in writing, name what it costs,
   name who takes it.
4. **The name is the clean node's and the description is the Pact's.** GD §13.2 says *"visually
   corrupted"* and CH §4.4 says *"any node in any branch can appear in its corrupted form"* — the
   player is meant to recognise the node they have been offered clean before, which is what makes
   the temptation continuous. The effects differ, so the description must; GD §13.1's *"readable in
   under two seconds"* is a claim about that line and about nothing else. One `LocKey` per Pact, and
   `ADR-0012`'s rule that the key exists from the first node even though the text does not.
5. **A Pact survives a resume, and the save carries it because nothing else can.** Grepped:
   `SkillTree.Restore(takenInOrder)` replays each id through `Record`, which applies `Spec.Effects` —
   so without a second list a run that took a Pact comes back with the **clean** node's power and the
   Veilrot it already paid, because `RunEconomy.Veilrot` *is* saved
   ([M6-01b](M6-01b-save-format-v4.md)). That is `PlayerProfile.Shards`' failure direction exactly
   (M4-05b rule 8): *a number not written is data destroyed.* **`pactedNodeIds` is therefore a
   fourth v4 field and it is added to [M6-01b](M6-01b-save-format-v4.md) rather than here**, on that
   task's own argument — one bump is 36 call sites and four bumps are 144 — and under the licence it
   wrote for exactly this case: *"if one of them wants a different shape, that is a deviation on that
   task and this format is re-cut before `m6` is tagged."* This task supplies the writer.
   **The alternative was weighed and refused**: giving each Pact its own `ContentId` and saving that
   would need `TreeRules`, `IsTaken`, `IsBanished`, `TreeViewPresenter` and `RunRecorder` each to
   know that two ids mean one node, and `TakenNodeIds` would stop being *"ids of this tree"*.
6. **Content validation refuses a Pact that is weaker than nothing and reports the ones nobody
   wrote.** `ContentValidationTests` gains two rows:
   `EveryPact_AsksWithinTheBand` sweeps every `SkillDefinition` under `Data/` for a Veilrot outside
   [10, 20] or a Pact on an Active — the authoring door, where the message can name the asset file
   and the field (M3-02b's placement) — and `ReportsItsPactCoverage` writes *"n of m nodes carry a
   Pact"* to the test log without asserting a ratio, because a ratio would be a balance claim and
   **M7-04** is where the number becomes one.
7. **A Pact's effects are held to every rule a clean node's are.** `SkillSpec.CopyEffects` is the one
   copy-and-null-check in the file and `PactSpec` uses it, so an empty list, a null entry and the
   `Array.Empty` fast path all behave identically. Whether a `ModifyStat` inside one *resolves* is
   still `PlayerStats.Resolve`'s throw and `ModifyStatDefinition.ToEffect`'s `Enum.IsDefined`, as for
   every other effect in the project — a Pact is not a new kind of effect, it is a second list of
   the ones that exist.
8. **Nothing here allocates on a path that runs more than once a level.** `PactSpec` is one object
   per authored Pact at boot; `SkillTree` gains one `bool[]` sized with the tree beside `_taken` and
   `_banished`, and a `List<ContentId>` that grows at most once per Pact taken. `IsPact` is a
   dictionary probe and an array read.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Pact_CarriesItsThree` | effects, 15, a key / — / all three read back, the effects copied |
| `Pact_RefusesAnEmptyOrNullEffect` | empty, then a list holding null / — / throws, naming the index — rule 7 |
| `Pact_RefusesAVeilrotOutsideTheBand` | 9.9, 20.1, NaN, +∞ / — / throws in each case, naming the band — GD §13.2 |
| `Pact_AcceptsBothEnds` | 10 and 20 exactly / — / no throw |
| `Pact_RefusesADefaultDescriptionKey` | `default(LocKey)` / — / throws — `SkillSpec`'s forgotten-field rule |
| `Pact_HasNoNameKeyOfItsOwn` | `typeof(PactSpec)` / reflection / no `NameKey`: the corrupted node keeps the clean one's name so the player recognises it — rule 4 |
| `Skill_CarriesAPact` | a Passive with a block / — / `HasPact`, and `Pact.Veilrot` is what was authored |
| `Skill_WithoutOneIsUnchanged` | a `SkillSpec` built with six arguments / — / `HasPact` false, every other property what it was — rule 2 |
| `Skill_RefusesAPactOnAnActive` | an Active with a block / — / `ArgumentException` naming the id and the three shipped Actives' problem — rule 3 |
| `Skill_AcceptsAPactOnUpgradeAndKeystone` | each in turn / — / no throw: only Active is refused |
| `Tree_TakingAPactAppliesTheOtherEffects` | a node whose clean effect is +15 % damage and whose Pact is +45 % / `Take(id, asPact: true)` / damage is +45 % and **not** +60 % — rule 1 |
| `Tree_TakingAPactStillPublishesNodeTaken` | as above / — / one `NodeTaken` carrying the id, after the effects — `Take`'s existing ordering |
| `Tree_ACleanTakeIsUnchanged` | a node with a Pact / `Take(id)` / the clean effects, `IsPact` false — rule 1 |
| `Tree_TakingAPactOnANodeWithoutOneThrows` | `Take(id, asPact: true)` on a node with no block / — / `InvalidOperationException` naming the id, nothing taken, nothing applied |
| `Tree_PactedIdsIsASubsetOfTaken` | three taken, one as a Pact / — / `PactedIds` is that one, `TakenIds` is all three, in take order |
| `Tree_IsPactAnswersFalseForAStranger` | an id from another class's tree / `IsPact` / false, no throw — `IsTaken`'s rule |
| `Tree_TheGatesAreUnchangedByCorruption` | a tier-2 node with a Pact, nothing taken / `Take(id, asPact: true)` / refused for the tier, as the clean take is — a Pact is not a way past CH §5 |
| `Restore_BringsBackTheCorruptedVersion` | a save with two taken and the second pacted / restored / the Pact's effects on the second and the clean effects on the first, **nothing published** |
| `Restore_RefusesAPactedIdThatWasNotTaken` | a save naming an id in `pacted` and not in `taken` / — / throws — a save that disagrees with itself is corrupt rather than stale |
| `Restore_RefusesAPactedIdWithNoPact` | a save pacting a node this build ships no block for / — / throws, naming the id: the content changed under the save and the run's power would silently differ |
| `Resume_APactSurvivesAKill` | a run that took a Pact, killed and resumed / — / the same damage number as before the kill, and the Veilrot it paid — rule 5 |
| `Recorder_WritesThePactedIds` | a run with one Pact / `Take` / `PactedNodeIds` holds it — [M6-01b](M6-01b-save-format-v4.md) rule 7's third placeholder replaced |
| `State_PactedNodeIdsForwardsToTheTree` | a live run / — / `RunState.PactedNodeIds` tracks `SkillTree.PactedIds`, and `RunState` still exposes no `SkillTree` — AR §18.2 |
| `Content_EveryPactAsksWithinTheBand` | every `SkillDefinition` under `Data/` / converted / every block is in [10, 20] and none is on an Active — rule 6 |
| `Content_ReportsItsPactCoverage` | the same sweep / — / logs *n* of *m*, asserts only that *n* is above zero — rule 6 |
| `Definition_RewrapsTheCoreFailure` | an asset with a 25-Rot Pact / `ToSkill` / `ArgumentException` whose message leads with the asset's name — `EffectDefinition`'s bargain |
| `Tree_AllocatesNothingWhenAsked` | 100 000 `IsPact` reads / `AllocationAssert.None` / zero — rule 8 |

**Guard rows are implied, not listed:** a null effect list, a `default(ContentId)` to `Take`, and
every existing `SkillSpec` and `SkillTree` guard firing unchanged.

## Manual verification (Editor / device)

1. **[Editor]** Open one of the four Pact-carrying skill assets. The Pact foldout shows its effects,
   its Rot and its description key; clearing the effect list makes the Console name the asset on the
   next content load.
2. **[Editor]** With a debug command that takes a node as a Pact, take one and read the debug
   overlay: the damage figure is the Pact's, not the clean node's, and the Veilrot meter has moved.
   Quit to the menu, press `Continue`, and both numbers come back — rule 5, which is the only step
   that exercises it.
3. **[device]** Nothing. The card is [M6-05b](M6-05b-the-offer-that-rolls-one.md)'s and its device
   row is on [ledger row 1](../ROADMAP.md#carry-forward-into-m6) there.

## Out of scope

- **Anything that *offers* one.** [M6-05b](M6-05b-the-offer-that-rolls-one.md): the roll, the card,
  the violet frame, and the `Veilrot.Gain` that pays for it. After this task the only caller of
  `Take(id, asPact: true)` is a test and the debug overlay.
- **Corrupting a node by arithmetic.** The ruling above, with the six files behind it.
- **A Pact on an Active.** Rule 3, with the reason and the owner.
- **Pacts for the other twenty nodes.** Four ship, one per branch pair, so every branch of both
  classes can produce one; the rest are **M7-04**'s with the eighty-one.
- **The 1.8× as a checked ratio.** There is no unit in which a `SpawnHealZone` and a `ModifyStat`
  are comparable, so a test asserting *"the Pact is 1.8× the clean node"* would be a test of one
  arbitrary metric. Rule 6's coverage report is what ships instead.
- **Bumping the save format.** [M6-01b](M6-01b-save-format-v4.md) carries `pactedNodeIds`; this task
  writes it.

## As built

**Built to the table's shape.** `PactSpec` (effects, a Rot in [10, 20], a description key, no
name) sits in `SkillSpec.cs` and attaches as `SkillSpec`'s optional last argument, refused on an
Active. `SkillTree` gains a third flag array beside `_taken` and `_banished`, `Take(id, asPact)`,
`IsPact`, `PactedIds` and a two-list `Restore`; `Record` applies the Pact's effects **instead of** the
clean ones, with the `SkillSpec` as source. `RunState.PactedNodeIds` forwards to the tree,
`RunRecorder` writes it (the v4 placeholder replaced, no bump), and `RunSession` hands it to the
replay. EditMode 2 736 → **2 766, +30**; PlayMode **26**, unchanged.

**The four Pacts**, two effect assets each (the power and its price, GD §13.2's shape):

| Node | Pact | Rot |
|---|---|---|
| Keen Censer (Oathbound, Censure) | +45 % damage, −25 max HP (GD §13.2's own example) | 15 |
| Zealotry (Oathbound, Judgment) | +30 % fire rate, Aegis recharges 50 % slower | 12 |
| Sharpened Bone (Gravecaller, Grave-work) | +40 % damage, −15 % move speed | 15 |
| Rot Feast (Gravecaller, Rot) | +40 % XP, −15 % max HP | 12 |

These numbers are authored to the budget, not balanced. M7-04 and M8-05 own balance.
`Content_ReportsItsPactCoverage` logs **4 of 24**.

### Deviations

1. **"Four ship, one per branch pair, so every branch of both classes can produce one" is not
   possible with four.** Six branches, four Pacts: **Oath and Legion carry none**. The Files table,
   the four `English.asset` rows and ledger row 7's count all say four, so four shipped.
2. **`Restore(takenInOrder)` is kept** as a one-line overload that passes an empty `pacted`. It is
   called from dozens of existing rows, and the resume path now uses the two-list form.
3. **Both `pacted` refusals run before any take is replayed**, so a save that fails either leaves
   the tree untouched. A pacted id that is not in this tree is left to the replay's existing
   `KeyNotFoundException`.
4. **`RequireHandlers` sweeps a Pact's effects too** (`"pacts"`). Rule 7 applied at the
   constructor, for the reason the cast list is swept there.
5. **No `SkillDefinitionTests` exists.** The authoring rows are in `SkillAuthoringTests`:
   `Definition_RewrapsTheCoreFailure`, plus three rows beyond the table (`Skill_Pact_ToSpec`,
   `Skill_NoPactByDefault` and `Skill_PactWithNoEffects_NamesTheAsset`, which is manual step 1
   as a test). The `Pact_*` and `Skill_*` rows are all in `PactSpecTests`, and `SkillSpecTests`
   is untouched.
6. **The Inspector block is a `[Header]` with an `_hasPact` toggle, not a foldout.** On an Active
   the toggle is refused by `SkillSpec`, not ignored like the kind-gated blocks: a designer who
   switched it on made a claim. `OnValidate` warns about a Pact on an Active and about a Pact with
   no effects.
7. **Test files outside the table.** `RunRecorderTests` holds `Recorder_WritesThePactedIds`, and
   its `NodeTwo` now carries a Pact. `RunSessionResumeTests`' max-HP node carries one too (+45
   against +20). `OathboundTreeTests.Modify_ShippedAssetsAllTargetThePlayer` pins **9 → 13**
   ModifyStats and caught the four new assets on its first run.
8. **`Resume_APactSurvivesAKill` compares max HP rather than damage.** `RunState` has a read for
   one and not the other, which is `Start_RestoresTakenNodes`' precedent. It writes through the
   real recorder and restores into a fresh session, so the row covers the save format as well as
   the replay.
9. **`ContentValidationTests` changes beyond its two new rows.** `ShippedEffects` 25 → 33, the Pact's
   key joins the `EveryLocKey_*` sweeps, and `skill.new.pact.description` is a known placeholder.
   `Content_EveryPactAsksWithinTheBand` reads the serialized fields, so its message names
   `_pactVeilrot`.

### Findings

- **Manual step 2 cannot be run in this build.** No debug command takes a node as a Pact, and
  M6-03a removed the F-keys. Rule 5 is covered by `Resume_APactSurvivesAKill` and nothing else
  until M6-05b's card.
- **`FrameOrderTests.Ticker_RunsTheStepsInOrder` failed once in four PlayMode runs** with the
  wedge-behind-the-apex message, which is known issue 1. Runs 3 and 4 were clean.
- **Ledger row 7** gets its four Pact descriptions (M6-05b owes the other two). It is touched but
  not moved.
