# M6-06b — Four Ordeals that work, and two that this build cannot honestly ship

**Size:** S · **Depends on:** M6-06a · **Branch:** `m6-06b-ordeal-effects`
**Design refs:** GD §7.2, §11.2, §12.1, §12.2, §12.4, §13.4, §15; AR §13, §14, §18.3; ADR-0006, ADR-0011 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m6) — nothing added, and why; [2](../ROADMAP.md#carry-forward-into-m6) — one clause

## Goal

Famine, Vigil, Swarm and Hunger each turn one dial in one system; Fracture and Echo are refused in
writing, with the arithmetic that refuses them and the task that takes them.

## Four of six, counted first

GD §13.4 lists six. [M6-06a](M6-06a-what-an-ordeal-is.md) gives them all a home — five neutral
dials on an `OrdealSpec` — and four of the six turn one dial each:

| Ordeal | GD §13.4 | Where it lands | Cost |
|---|---|---|---|
| **Famine** | Essence drops −40 % | `StageFlow.EnterClear`, over [M6-01a](M6-01a-essence-wallet-and-drops.md) rule 4's one call | one line |
| **Vigil** | Level-ups offer 2 instead of 3 | `LevelUpFlow.Open`'s `count` — `OfferGenerator.DefaultOfferCount`'s own remarks name this task by ID | one argument |
| **Swarm** | Concurrency +8 (device permitting), Husk cost halved | `WaveComposer.Compose` — the cap, and `_costs` | **substantial** |
| **Hunger** | Veilrot +50 % from all sources | `Veilrot.Gain`, above the clamp | one line |
| **Fracture** | 3 fewer cover pillars | — | **refused**, below |
| **Echo** | Every 4th wave repeats the previous at full strength | — | **refused**, below |

## Fracture is refused, and the arithmetic is the reason rather than the effort

1. **Core has no concept of an arena's cover, and this would be the first mechanic to reach past the
   boundary for one.** `ArenaView.CoverCount` counts Unity `GameObject`s on the `Cover` layer —
   *"counted by layer rather than from an authored list, and that is the whole point"* — and nothing
   in `Soulvail.Core` has ever seen that number. Every other Ordeal turns a number core already owns.
2. **The two shipped arenas do not have three to give.** Grepped on the prefabs:
   `Arena_Pillars.prefab` authors **4** and `Arena_Tiered.prefab` authors **5**. GD §7.2's rule is
   *"3–6 cover pillars"* and `ArenaView.MinCoverPillars` is **3**, so *"3 fewer"* leaves **1** and
   **2** — below the arena contract's own floor, and against GD §12.4's *Cover guarantee*
   (*"every arena has a valid circle-strafe path at all times"*). An Ordeal that makes the shipped
   content invalid is not a tuning knob.
3. **The pillars are baked into the NavMesh, so removing one at runtime leaves its hole.** Each arena
   carries a baked `NavMeshData` asset (`NavMeshArena_Pillars.asset`) and its `NavMeshSurface`
   collects **every** layer (`m_LayerMask.m_Bits: 4294967295`). Deactivate a pillar and enemies still
   path around a thing nobody can see; re-bake instead and it is a `BuildNavMesh` on the stage
   transition frame, which is the moment `WavePlan`'s own reasoning already calls *"the worst moment
   in a run to allocate."*

**Owner: M7-05/06.** The arena art pass authors 8–12 rooms per biome, which is the first point at
which *"fractured"* can be a **variant arena** — `Arena_Pillars_Fractured`, authored with the
pillars it actually has and baked that way — instead of a subtraction performed on a room that was
built assuming them. `ModeSpec.ArenaFor` is already the door: an Ordeal that swapped an arena pool
is a data change, which is the shape this project keeps choosing.

## Echo is refused because GD §13.4 admits two readings with opposite signs

*"Every 4th wave repeats the previous wave's composition at full strength."*

**First, the scope, grepped.** `W(n) = clamp(2 + floor(n / 5), 2, 5)` is **5 for every stage from 15
onward**, and Ordeals start at 25 — so *"every 4th wave"* is **wave 4 of 5, once per stage, for
ever**. Whatever Echo does, it does it to one wave in five.

- **Read as *replace*** — wave 4 gets wave 3's entries — it makes a deep stage **easier**.
  `WaveComposer` splits the budget triangularly: wave *i* of *W* is worth *i* shares of *W(W+1)/2*,
  so wave 3 is 3/15 of the stage and wave 4 is 4/15. Handing wave 4 wave 3's composition removes
  **1/15 — about 6.7 % — of the stage's threat budget**, and leaves it in `WavePlan.UnspentThreat`,
  where GD §11.2's upgrade pass would then try to spend it. A *deep-run modifier* (GD §13.4's own
  heading) that lowers difficulty is the opposite of the table's purpose.
- **Read as *add*** — wave 4 is its own composition **and** wave 3's — it breaks the concurrency
  cap. `WaveComposer` holds each wave's body count to `WavePlan.Concurrency`, so two waves' worth at
  once is up to **2 × C(n)** alive: GD §12.2's cap *"does two jobs: it protects frame rate **and**
  readability (P1)"*, and GD §11.2's device-independence rule is built on it. Making that safe means
  a cap-aware merge, which is a real piece of composer work **and** a decision about what to drop.

Choosing between them is a design decision, not an implementation detail, and
[M5-06b](M5-06b-gravecaller-tree-v1.md) rule 3's precedent is to refuse and say so rather than guess.
**Owner: the owner's ruling, or M7-02** — Elites are already the task that has to spend
`UnspentThreat` inside `WaveComposer`, so it arrives with the budget arithmetic open and a reason to
touch it.

## What this task does not measure, and who does

**M5-08's rule — a row that wants a number is discharged by an instrument, not by an argument —
applied before the fact.** Ordeals begin at stage 25 and M5-08's two instrumented runs reached 16 and
19, so *"does Swarm make stage 35 unreadable"* is a question no Editor row here can answer. What
this task ships toward it is a **debug stage-jump** in the existing overlay — no new file, manual
step 1 — and **M6-11**'s acceptance carries the measurement, beside the hits-to-kill instrument
[ledger row 2](../ROADMAP.md#carry-forward-into-m6) already assigns it. **No new ledger row**: the
obligation is already M6-11's, and a second row saying the same thing is the duplication
[PROGRESS](../PROGRESS.md#how-to-write-an-entry) bounds.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Director/WaveComposer.cs` | Core | **Substantial.** Swarm: the cap, the per-archetype cost, and the floor that keeps the buy walk finite |
| `Tests/Core/Run/OrdealEffectsTests.cs` | Tests.Core | The four, end to end through a run, and the two refusals pinned |
| *small edits* | Core, Game | `Core/Stage/StageFlow.cs` — Famine over the award (rule 1); `Core/Progression/LevelUpFlow.cs` — Vigil's `count` and an optional `Ordeals` (rule 2); `Core/Run/Veilrot.cs` — Hunger above the clamp (rule 4); `Core/Run/RunSession.cs` — the set handed to the flow, the meter and the composer; `Data/Ordeals/*.asset` — the four authored; `Data/Localisation/English.asset` — eight rows |
| *ripple* | Tests.Core | `WaveComposerTests`, `LevelUpFlowTests`, `VeilrotTests` and `StageFlowTests` each gain their Ordeal's rows; **no constructor moves** (rule 5) |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

**No new type and no new member.** What moves is three signatures, each gaining the run's `Ordeals`
as an **optional and last** argument — rule 5's ruling, and the counted reason it differs from
[M6-01a](M6-01a-essence-wallet-and-drops.md) rule 5.

```csharp
namespace Soulvail.Core.Director;

public sealed class WaveComposer
{
    /// <param name="ordeals">
    /// What this run has been dealt, or <see langword="null"/> for none — which is the ordinary
    /// state below the mode's first Ordeal stage and for every mode that schedules none (rule 5).
    /// **47 call sites**, which is why it is last.
    /// </param>
    public void Compose(
        int stage, ModeSpec mode, WavePlan destination, IRandomStream spawn,
        Ordeals ordeals = null);
}
```

```csharp
namespace Soulvail.Core.Progression;

public sealed class LevelUpFlow
{
    // ... M6-05b's `veilrot`, then, optional and last — 9 call sites across 2 files:
    //     Ordeals ordeals = null
}
```

```csharp
namespace Soulvail.Core.Run;

public sealed class Veilrot
{
    // ... M6-04's four arguments, then, optional and last:
    //     Ordeals ordeals = null
}
```

`StageFlow` needs nothing: [M6-06a](M6-06a-what-an-ordeal-is.md) rule 5 already made its `Ordeals`
**required**, and Famine reads it from there. `ThreatBudget` is untouched — rule 3.

## Behaviour

1. **Famine multiplies the award and never the formula.** `StageFlow.EnterClear` already calls
   `_essence.Earn(_mode.Essence.ForStageClear(Stage, _director.IsBossStage))`
   ([M6-01a](M6-01a-essence-wallet-and-drops.md) rule 4); the multiplier wraps that call's result,
   which is exactly what that task's *Out of scope* reserved — *"a wallet that knew about Ordeals
   would be the Ordeal system in the wrong file"*. Rounded with `MathF.Round` to the nearest whole
   Essence and **floored at 1 whenever the unmodified award was positive**, so a stacked Famine can
   make a stage clear nearly worthless and never literally nothing: `EssenceWallet.Earn` is silent
   for zero, so a zero award would be a stage clear that published no `EssenceChanged` and looked to
   the HUD like a stage that was never cleared.
2. **Vigil passes a number and needs no screen at all.** `LevelUpFlow.Open` draws
   `_ordeals.OfferCount` when it is non-zero and `_offer.Length` otherwise —
   `OfferGenerator.DefaultOfferCount`'s own remarks call this shot: *"A default rather than a
   constant the code reads, because GD §13.4's Vigil offers two — that is M6-06 passing 2 to `Draw`,
   not a flag here."* **Grepped, the screen already handles it**: `LevelUpPresenter.Draw` hides every
   card past `OfferPresented.Count` rather than drawing it empty (M3-08b rule 6), so a two-card offer
   draws two cards and `LevelUp.prefab` is untouched. `_offer` stays a three-array;
   `OfferGenerator.Draw` clamps to what is available, as it already does.
3. **Swarm's cap is added and re-clamped to the device, which is what *"device permitting"* means.**
   `_budget.Concurrency(stage)` is already `min(10 + floor(n/2), deviceCap)`, so the stage's cap
   becomes `min(that + bonus, _budget.DeviceCap)` — **and `ThreatBudget` is not edited**, because
   `DeviceCap` is already public on it and the clamp belongs where the bonus is known. Worked
   through: at stage 25 on the mid tier that is `min(22 + 8, 28) = 28`, six more bodies; from stage
   36 it is `min(28 + 8, 28) = 28` and Swarm's first half silently does nothing, which is GD §11.2's
   rule working rather than the Ordeal failing. `Swarm_StopsGivingBodiesWhenTheDeviceCapBinds` is the
   row that says so, because *"it stopped working at stage 36"* is exactly the report a playtest
   would file.
4. **Swarm's second half is a per-archetype cost multiplier with a floor of 1, and the floor is the
   load-bearing part.** `WaveComposer.PrepareEligible` fills `_costs[i]` from
   `EnemySpec.ThreatCost`; each is multiplied by `_ordeals.ThreatCostMultiplier(id)` and rounded,
   then **floored at 1**. A cost of zero would make the composer's affordability walk able to buy an
   unbounded number of one archetype for nothing — the budget never falls, and the only thing that
   ends the loop is the concurrency cap, which Swarm has just raised. That is a hang rather than a
   balance problem, and it is the same class of hole as the `"xp":1e38`
   [parking-lot line](../ROADMAP.md#parking-lot), caught at the one door that can see it. **Naming
   `enemy.husk` is the Ordeal asset's business and not the composer's**: `WaveComposer`'s remarks
   say *"the vocabulary is the mode's, never the catalog's"*, and an `OrdealSpec` naming a
   `ContentId` is authored data doing exactly that.
5. **Three consumers take the set as an optional-and-last argument, and that is a different ruling
   from [M6-01a](M6-01a-essence-wallet-and-drops.md) rule 5 for a stated reason.** That rule made
   `StageFlow`'s wallet required because *"an optional wallet defaulting to null would make a
   mis-wired run clear stages and be paid nothing"* — there is no legitimate null wallet. **Here
   there is:** no Ordeal exists below the mode's `FirstStage`, which is the first twenty-four stages
   of every Descent run, and no Ordeal exists at all for a mode that schedules none. A null `Ordeals`
   therefore means *"no Ordeals"*, which is a true statement rather than a misconfiguration, and
   **`WaveComposer.Compose` alone has 47 call sites** against `StageFlow`'s 11. What keeps it honest
   is that the one object that *deals* them — `StageFlow` — takes a **required** one
   ([M6-06a](M6-06a-what-an-ordeal-is.md) rule 5), and three rows assert that `RunSession` wires the
   other three rather than trusting it.
6. **Hunger multiplies the gain and the clamp still wins.** `Veilrot.Gain` multiplies its argument
   before M6-04 rule 1's clamp at `Max`, so a 15-Rot Pact under Hunger is 22.5 and a run at 85 still
   arrives at exactly 100 and Claims. **`Cleanse` is untouched**: GD §13.4 says *"Veilrot gains +50 %
   **from all sources**"*, and a cleanse is not a gain — multiplying it too would make Hunger a
   discount at the Sanctum, which inverts the Ordeal. **Hunger has exactly one non-test caller after
   this task**, [M6-05b](M6-05b-the-offer-that-rolls-one.md)'s Pact take, which is the only thing in
   the build that raises the meter; that is recorded rather than left for a playtest to find.
7. **Every one of the four is inert below the mode's first Ordeal stage**, which is the property that
   makes this PR safe to merge without a deep playtest: `Ordeals` answers 1, 0, 0, 1 and 1 for a run
   that has been dealt nothing ([M6-06a](M6-06a-what-an-ordeal-is.md) rule 3), so every arithmetic
   change here is the identity for the whole of the depth anybody has ever played.
8. **Nothing here allocates.** Four multiplications, one `Math.Min`, one `Math.Max`, and a walk of at
   most four dealt Ordeals per archetype per composition — once a stage, off the frame path (AR §14).

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Famine_TakesFortyPercent` | Famine dealt, a stage-10 clear worth 60 / — / **36** earned, one `EssenceChanged` of +36 — rule 1 |
| `Famine_RoundsToTheNearestEssence` | a clear worth 24 under 0.6 / — / 14, not 14.4 and not 15 |
| `Famine_NeverPaysZero` | a stacked multiplier taking a 24 award below 0.5 / — / **1**, and an event is published — rule 1 |
| `Famine_LeavesTheFormulaAlone` | Famine dealt / `EssenceSpec.ForStageClear(10, false)` / still 60 — the multiplier is at the award, not in the spec |
| `Famine_IsInertBeforeItIsDealt` | a stage-3 clear / — / 32, exactly as [M6-01a](M6-01a-essence-wallet-and-drops.md) left it — rule 7 |
| `Vigil_OffersTwo` | Vigil dealt, a tree of twelve / a level-up / `OfferPresented.Count` 2, two ids — rule 2 |
| `Vigil_TheScreenDrawsTwoCards` | the same / the presenter / cards 0 and 1 shown, card 2 **hidden** and not drawn empty — rule 2, M3-08b rule 6 unchanged |
| `Vigil_StillRollsItsPact` | Vigil and a Pact-carrying tree / a level-up / four draws on `Offers` and `PactIndex` in [−1, 2) — [M6-05b](M6-05b-the-offer-that-rolls-one.md) rule 10 |
| `Vigil_ClampsToWhatIsAvailable` | one node left / a level-up / one card, no throw |
| `Swarm_RaisesTheCap` | Swarm dealt, stage 25, device cap 28 / composed / `WavePlan.Concurrency` **28**, against 22 without it — rule 3 |
| `Swarm_StopsGivingBodiesWhenTheDeviceCapBinds` | stage 36, cap 28 / composed / 28 with and without Swarm — rule 3's stated cost |
| `Swarm_HalvesTheNamedArchetype` | Swarm dealt / composed / a Husk costs 2 where the spec says 4, and a Spitter costs what it always did — rule 4 |
| `Swarm_BuysMoreOfTheCheapOne` | the same budget with and without / — / strictly more Husks, and the stage's total threat is unchanged |
| `Swarm_ACostNeverFallsBelowOne` | a multiplier of 0.01 on a 4-cost archetype / composed / cost 1, the composition **terminates**, and the budget is spent — rule 4 |
| `Swarm_IsInertBeforeItIsDealt` | stage 10 / composed / byte-identical to the plan M2-05 composes today — rule 7 |
| `Hunger_RaisesAGain` | Hunger dealt / `Gain(10)` / 15 — rule 6 |
| `Hunger_TheClampStillWins` | 95, Hunger / `Gain(10)` / **100**, and `ClaimingBegan` fires once |
| `Hunger_DoesNotDiscountACleanse` | 40 Rot, Hunger / `Cleanse(15)` / 25, not 17.5 — rule 6 |
| `Hunger_IsSilentForNothing` | Hunger / `Gain(0)`, `Gain(-5)` / unmoved, nothing published — M6-04 rule 1 survives the multiplier |
| `Run_TheSetReachesAllThree` | a live `RunSession` / reflection over the composer, the flow and the meter / each holds the run's `Ordeals` — rule 5 |
| `Run_ANullSetMeansNoOrdeals` | a flow, a meter and a composer built without one / — / three cards, an unmultiplied gain and an unchanged plan — rule 5 |
| `Run_FourAtOnce` | all four dealt, a stage-55 clear and a level-up / — / 36-style income, two cards, the raised cap, a 1.5× gain, all in one run — rule 7's converse |
| `Ordeal_FractureIsNotAuthored` | `Data/Ordeals/` / — / no asset turns a cover dial, and `OrdealSpec` **has no cover dial** — the refusal, pinned so nobody adds one without reading why |
| `Ordeal_EchoIsNotAuthored` | the same / — / no asset, and `WaveComposer` still composes every wave from the budget alone |
| `Arena_HasFewerThanFourPillarsToSpare` | both shipped arena prefabs / — / `CoverCount` 4 and 5 against `MinCoverPillars` 3 — the arithmetic behind Fracture's refusal, asserted so it is re-checked when M7-05/06 authors more rooms |
| `Waves_TheFourthIsNotACopy` | any stage from 25 / composed / wave 4's entries are its own and its share is 4/15 of the budget — Echo's refusal, pinned |
| `Effects_AllocateNothing` | 100 000 awards, gains and compositions under four Ordeals / `AllocationAssert.None` / zero — rule 8 |

**Guard rows are implied, not listed:** every existing guard on the four touched methods firing
unchanged, and a non-finite multiplier refused at `OrdealSpec`'s door rather than here.

## Manual verification (Editor / device)

1. **[Editor]** Jump to stage 24 with the debug overlay up and walk through. Read which Ordeal was
   dealt, then check the one thing it should have moved and one thing it should not: Famine's clear
   pays less and the level-up still offers three; Vigil's offer is two cards and the clear pays full.
2. **[Editor]** Keep going to 55 with all four in force. The arena holds visibly more bodies, Husks
   outnumber everything, the clear pays about a third, offers are two cards, and a Pact taken moves
   the meter by half again what its card says.
3. **[Editor]** Play stages 1–10 with the overlay up. Nothing is different from the build before this
   PR — rule 7, which is the claim that makes this merge safe.
4. **[device]** **Nothing new**, and that is the point: every dial here moves a number that was
   already on [ledger row 1](../ROADMAP.md#carry-forward-into-m6)'s device list. Swarm's raised cap
   is the exception worth naming — GD §11.3's 28-body budget is the one M5-08 measured 37 bodies
   against in the Editor, and **M6-11** takes it with the rest.

## Out of scope

- **Fracture and Echo.** Refused above, each with an owner.
- **A fifth dial.** `OrdealSpec` ships the five [M6-06a](M6-06a-what-an-ordeal-is.md) authored; a
  seventh Ordeal that needs a sixth adds a field, which is that spec's rule 2.
- **Balancing any of the four.** GD §13.4's numbers ship as written. **M8-05** is the balance pass
  and **M6-11** is the instrument.
- **Showing an Ordeal to the player.** [M6-06a](M6-06a-what-an-ordeal-is.md)'s *Out of scope*,
  unchanged: the debug overlay names them and a readout is M6-11's call.
- **Editing `ThreatBudget`.** Rule 3: the device clamp is already there and the bonus is known one
  layer up.
- **Removing GD §13.4's two refused rows from the design document.** They are correct designs this
  build cannot carry, not mistakes — the parking lot records both, the way M6-04 recorded the
  Revenant.

## As built

**Built to the Public API.** `StageFlow.UnderOrdeals` wraps the award: it rounds, and it floors at 1
when the award was positive. `LevelUpFlow.CardsToOffer` passes Vigil's count to both `Draw`s. It is
held to the three-array. `WaveComposer.Compose` adds the bonus and re-clamps to `DeviceCap`.
`CostUnder` multiplies, rounds and floors at 1, and returns the cost untouched at a multiplier of
exactly 1. `Veilrot.Gain` multiplies below its guard and above the clamp. `Cleanse` is untouched.
The flow and the meter take `Ordeals` optional and last. No constructor moved and `ThreatBudget` is
untouched. **EditMode 2 830 → 2 858 (+28), twice; PlayMode 26, twice.**

### Deviations

1. **`RunSession` builds and restores the set above the opening composition**, not beside the
   wallet. M6-06a put it there when nothing read a dial. Left there, a resumed stage-35 run holding
   Swarm would compose its first stage without it and every later one with it. The restore is
   silent and reads only the mode, so it moved as one piece. Pinned by
   `Run_AResumedStageIsComposedUnderItsOrdeals`, a row the table does not list. AR §18.1's
   restore-order row is amended to say so.
2. **The composer holds no set, so `Run_TheSetReachesAllThree` checks the flow, the meter and the
   `StageFlow` that calls `Compose`.** The Public API makes `Ordeals` a `Compose` argument, so there
   is no composer field for reflection to find. The resume row above covers the call `RunSession`
   makes itself.
3. **Four files outside the table, each forced by an assembly boundary.**
   `Vigil_TheScreenDrawsTwoCards` is in `LevelUpPresenterTests`, because `Tests.Core` cannot see a
   presenter. The fixture's mode gains a Vigil in its pool, and `StartRun` gains an optional
   `ordeals` list. The two refusal rows and `Arena_HasFewerThanFourPillarsToSpare` are a new
   `Tests/Game/Authoring/OrdealRefusalTests.cs`, because they read assets. `OrdealsTests`'
   `Ordeals_NothingReadsTheDialsYet` is inverted to `Ordeals_EachDialHasItsOneReader`, which names
   the five readers exactly. Its `ClearOneStage` now kills body by body: under Swarm a budget of 4
   buys two Husks at 2, and `Recorder_WritesWhatWasDealt` went red on the second one.
4. **The four ripple test files are untouched.** Every row lives in `OrdealEffectsTests`, grouped by
   Ordeal, and no constructor moved, so none of them needed an edit.
5. **`Data/Ordeals/*.asset` and `English.asset` were not edited.** M6-06a authored all four with
   GD §13.4's numbers, and the eight rows are already there.
6. **No debug stage-jump was written.** The overlay is a readout with no controls, and a jump inside
   a live run would need a debug command on core. **`Descent.asset`'s *Starting Stage* already is
   the jump**, because a fresh run begins at the mode's `StartingStage`. Manual step 1 sets it to 24
   and reverts it after.
7. **The composer rows introduce the Spitter at stage 2 and compose from there.** `ModeSpec` refuses
   two archetypes introduced on one stage (GD §8.2).

### Findings

- **Swarm changes a fixture premise as well as a composition.** Any fixture that clears "the" body
  of a one-Husk stage clears one of two once Swarm is dealt. `OrdealsTests` was the only one, and
  `OrdealEffectsTests` shares its fixed helper.
- **Rounding is `MathF.Round`'s default, to even.** No shipped number sits on a half: 0.6 of a whole
  clear is never x.5 for any GD §15 award, and Swarm halves an even cost. An odd-cost archetype under
  a 0.5 Swarm would round to even. That is recorded here, not fixed.
- **Known issue 1 did not fire.** Both PlayMode passes were 26 / 0.
- **Ledger row 1** already carried Swarm's clause, so nothing was added. **Row 2** gains one
  clause: M6-11's session takes the Ordeals past stage 25.
