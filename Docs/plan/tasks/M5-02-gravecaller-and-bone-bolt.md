# M5-02 — The Gravecaller, and the numbers a second class forces us to settle

**Size:** M · **Depends on:** M5-01 · **Branch:** `m5-02-gravecaller`
**Design refs:** CH §3, §3.2; GD §6.1, §6.2, §12.4; CC §2.5, §7; AR §10.1; ADR-0006, ADR-0008 · **Ledger rows:** [5(ii)](../ROADMAP.md#carry-forward-into-m5)

## Goal

A second `CharacterDefinition` exists, and every number on it is one the project can defend —
including the two that a second class makes undeniable: **the speed band GD §6.1 still states and the
build has not honoured since M2-03**, and **a Bone Bolt that breaks GD §6.2's primary balance
invariant on the first Husk of a run**.

## Which forcing question this answers

**Neither.** What this task owns instead is the **standing contradiction** the ROADMAP has named
M5-02 for since M3-15 and again at M4-07: it is the first task that has to author a second class's
speed, so it is the first that cannot re-park the band. Rule 1 resolves it rather than restating it.

## The two findings this spec is written around

Both were produced by reading the shipped assets, not the documents.

**(a) The speed band.** GD §6.1 says *"Speed range across classes — 5.4–6.2 m/s"*; CH §3's table says
Oathbound **5.4**, Gravecaller **5.6**, Emberwright **6.2**. `Oathbound.asset` carries `_speed: 3`.
So the starter class sits **44 % below its own stated floor**, and authoring 5.6 for the Gravecaller
would make it nearly **twice** the Oathbound's speed — a class difference the design never asked for.

**(b) Bone Bolt kills a Husk in six hits.** CH §3.2 authors *"7 dmg, 4.0/s"*. `Husk.asset` carries
`_maxHp: 36`. `ceil(36 / 7) = 6`, at **stage 1, with no tree and no Overflow**. GD §6.2 calls
*"a basic enemy dies in 3–5 hits from any class, at any depth"* **the primary balance invariant of
the whole game**, and 6 is outside it before the run has started. The Wights do not rescue it: Rise
needs a kill to produce a Wight, so the **first** enemy of every Gravecaller run is fought with the
weapon alone.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/MinionSpec.cs` | Core | What a class's minions are, as authored data. Consumed by [M5-04a](M5-04a-minion-agents-and-registry.md); authored here |
| `Data/Characters/Gravecaller.asset` | — | The class. One asset, `character.gravecaller` |
| `Tests/Game/Authoring/GravecallerTests.cs` | Tests.Game | Every authored number, asserted against the doc it comes from or the ruling that moved it |
| `Tests/Core/Progression/TimeToKillTests.cs` | Tests.Core | **Substantial.** A second weapon in the band, and ledger row 5(ii)'s derived input |
| *small edits* | Core, Game | `CharacterSpec` gains `Minions` (nullable, rule 6); `CharacterDefinition` gains the three shot fields M5-01 added, six minion fields, and `MovementSkillKind.Shroudstep`; `MovementSkillKind` gains that member (rule 7) |
| *docs* | — | `GameDesign.md` §6.1's band row, `Characters.md` §3's table, `CoreCombat.md` §2.5's closing paragraph — rule 1 |
| *ripple* | Tests.Core, Tests.Game | `ContentTests` gains `MinionSpec`'s guard rows; `CharacterDefinitionTests` and `ContentValidationTests` gain the second character |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
// Core/Content/MinionSpec.cs
/// <summary>
/// A class's minions, as authored data: how many, for how long, how often a kill makes one, and
/// what one is. CH §3.2's Rise entire. Null on a class that has none — ShieldSpec's rule (rule 6).
/// </summary>
public sealed class MinionSpec
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="riseChance"/> is outside <c>(0, 1]</c>; <paramref name="cap"/> is not
    /// positive; any other value is not a finite number greater than zero.
    /// </exception>
    public MinionSpec(
        ContentId specId, LocKey nameKey,
        int cap, float lifespan, float riseChance,
        float maxHp, float moveSpeed, float damage, float attackInterval, float reach);

    public ContentId SpecId { get; }     // minion.wight
    public LocKey NameKey { get; }
    public int Cap { get; }              // 3 — CH §3.2's "base cap 3"
    public float Lifespan { get; }       // 20 s — CH §3.2
    public float RiseChance { get; }     // 0.25 — CH §3.2's "25 % of enemies killed"
    public float MaxHp { get; }
    public float MoveSpeed { get; }
    public float Damage { get; }
    public float AttackInterval { get; }
    public float Reach { get; }
}

// Core/Content/CharacterSpec.cs — widened
/// <summary>The class's minions, or null where it has none. The Oathbound's is null.</summary>
public MinionSpec Minions { get; }

// Core/Content/MovementSkillSpec.cs — widened
public enum MovementSkillKind
{
    Charge,

    /// <summary>
    /// The Gravecaller's Shroudstep (CH §3.2): a 6 m blink leaving a corpse decoy. The member is
    /// content identity and lands here; what it *does* is M5-03's (rule 7).
    /// </summary>
    Shroudstep,
}
```

## Behaviour

1. **The band scales; the Oathbound does not move.** The owner's M2-03 retune is the measurement and
   the documents are the stale copy, so the band is multiplied by the factor the retune already
   applied — **3 / 5.4 = 0.5556** — and rounded to one decimal:

   | Class | GD §6.1 / CH §3 today | Ruled | Against the Husk's 2 m/s |
   |---|---|---|---|
   | Oathbound | 5.4 | **3.0** — unchanged on disk | 1.50× |
   | Gravecaller | 5.6 | **3.1** | 1.55× |
   | Emberwright | 6.2 | **3.4** | 1.70× |

   GD §6.1's row becomes **3.0–3.4 m/s**. Its stated purpose — *"every class must feel faster than
   almost every enemy"* — holds at all three, and the ordering the design wants (the tank is the
   slowest, the wizard the fastest) is preserved exactly. **The alternative was weighed and refused:**
   leaving the band and making the Oathbound *"simply the slow class"* puts the starter outside a row
   that describes *"the range across classes"*, which makes the row false rather than loose, and it
   would put the Gravecaller at 5.6 — 1.87× the class the whole game's pacing was tuned around.
   `CoreCombat.md` §2.5's paragraph, which has flagged this for the owner since M2-03, is rewritten
   to record that the band moved rather than that it is pending.
2. **Bone Bolt is 9 damage at 4.0 swings per second, not 7.** `ceil(36 / 9) = 4`, inside GD §6.2's
   3–5 at stage 1 with nothing taken. **DPS is 36 against the Censer's 39**, so CH §3.2's *"deliberately
   weak; you are not the damage"* is still true — the Gravecaller's weapon is 92 % of the Oathbound's
   and its 80 HP is 57 % of the Oathbound's, which is the trade the class is supposed to make.
   **Two alternatives were weighed.** Keeping 7 and raising the fire rate moves DPS without moving
   hits-to-kill, so it cannot touch an invariant stated in hits. Keeping 7 and arguing the Wights make
   up the difference fails on sequencing: Rise needs a kill, so the first Husk of every run is fought
   at 6 hits. **This is one field on one asset, and the owner overrides it by typing a different
   number** — which is the point of it being authored rather than a `const`.
3. **The weapon is authored whole, and every number that is not CH §3.2's is named as ours.**

   | Field | Value | Where it comes from |
   |---|---|---|
   | `kind` | `Projectile` | CH §3.2, and M5-01 rule 1 |
   | `damage` | **9** | rule 2 — CH §3.2 says 7 |
   | `swingsPerSecond` | 4.0 | CH §3.2 |
   | `range` | **12** | ours. It matches `TargetingSpec.AcquireRange`, so the weapon fires at everything it can acquire — a ranged class whose reach is shorter than its sight would refuse shots for a reason no player can see |
   | `coneAngleDeg` | 360 | M5-01 rule 2 — *the angle does not gate this weapon* |
   | `damageFrame` | **0.15** | ours. The Censer's 0.4 is a readable windup on a melee arc; a bolt's release is the moment it leaves the hand, and 0.15 of a 0.25 s interval is 37 ms |
   | `shotSpeed` | **40** | ours. The Spitter's is 12 and the Emberwright's orb is 25 (CH §3.3, *"slow"*); a fast, weak bolt has to be the quickest thing in the air. At 12 m that is a **0.3 s** flight |
   | `shotRadius` | **0.8** | ours. Half the Spitter's 1.6 — an enemy shot is forgiving on purpose (GD §8.1), and a player's shot that forgave as much would make the lead of CC §3.7 pointless |
4. **The class is authored whole too**: `_maxHp` **80** and `_speed` **3.1** (CH §3.2 and rule 1),
   **no `ShieldSpec`** — `_shieldMax: 0`, because CH §3.1 calls the Aegis *"the only regeneration in
   the game"* — `_hitIFrames` 0.5 as the Oathbound's, targeting and focus as the Oathbound's.
   **Targeting and focus being identical is a decision, not a copy:** CC §3 is one targeting system
   for every class, and a per-class acquire range would be a second tuning surface with no design
   behind it. It is asserted rather than left implicit.
5. **The minion block is authored and consumed by nothing.** M4-01a's bargain: `MinionSpec` ships
   used by nothing until [M5-04a](M5-04a-minion-agents-and-registry.md), so this task moves no number
   in any run this build plays. The values:

   | Field | Value | Where it comes from |
   |---|---|---|
   | `specId` / `nameKey` | `minion.wight` / `minion.wight.name` | naming conventions |
   | `cap` | 3 | CH §3.2, *"base cap 3"* |
   | `lifespan` | 20 | CH §3.2, *"20 s lifespan"* |
   | `riseChance` | 0.25 | CH §3.2, *"25 % of enemies killed"* |
   | `maxHp` | **20** | ours. Fragile against a Husk's 8 contact damage — three hits — so a Wight is a timer rather than a wall |
   | `moveSpeed` | **3.0** | ours. 1.5× the Husk's 2, the same margin GD §6.1 asks the *player* to have, so a Wight can actually catch what it is sent at |
   | `damage` / `attackInterval` | **8 / 1.0 s** | ours. Five hits to kill a stage-1 Husk, 24 DPS at the cap of three against the weapon's 36 — the class's damage is *most* of a second weapon, not a replacement for the first |
   | `reach` | **1.5 m** | ours, the Husk's own strike distance |

   **None of these eight has a document behind it, and that is stated here so M5-08 knows what it is
   looking at**: they are a first authoring, and the first playtest of a Gravecaller is what decides
   whether 25 % at a cap of 3 reads as *"an army"* or as *"an occasional friend"*.
6. **`CharacterSpec.Minions` is nullable and the Oathbound's is null**, exactly as `Shield` is null on
   a class with no Aegis. A zeroed block would have to be read against the class id to be understood,
   which puts the meaning of the data in a second place — `ProjectileSpec`'s own argument, quoted
   because it is the same one.
7. **`MovementSkillKind.Shroudstep` lands here and does nothing here.** The member is *content
   identity* — a class is named by its `CharacterSpec.Id` and its movement skill by this enum — and
   the enum's own remarks already say M5-03 brings it. Until M5-03 merges, `PlayerCombat` builds a
   `ChargeSkill` from the spec whatever the kind says, so **a Gravecaller in this build blinks 6 m in
   0.05 s, damages nothing, shoves nothing, and leaves no decoy**. That is an honest intermediate
   state rather than a broken one, and it is named so nobody reports it as a bug. The movement numbers
   authored: 6 m over 0.05 s, 2.5 s cooldown (the Charge's — the decoy is the payload, not a shorter
   clock), 0 damage, 0 knockback, 0.05 s i-frame trail, 0.15 s input buffer.
8. **Ledger row 5(ii): `TimeToKillTests` derives its Overflow input instead of quoting it.**
   `Ttk_StageFifteen_IsWithinTheBand` passes `overflowLevels: 7`, copied from M3-12c rule 8's table
   (`Level − 1 − 12`); the shipped curve produces **8**. The literal is replaced by a helper built
   from the shipped content the fixture already reaches — `ModeSpec.Xp`, `ThreatBudget.Budget(stage)`
   and `EnemySpec.XpValue` — in the same spirit as `ScaledHuskHp`, which goes through the real
   `DepthScaling` rather than multiplying `h(n)` out by hand. **The hit count does not move:** at 7
   the damage multiplier is ×1.29 and at 8 it is ×1.31, and 66.24 over either is 4 hits. A row asserts
   the derived answer **is 8**, so nobody can quietly restore the 7.
9. **The Gravecaller is not selectable and no run can play it.** `PendingRun.CharacterId` is written
   by the menu, which offers one class until M5-07. The asset is reachable from the catalog and from
   tests and from nowhere else, which is what keeps this task's review about numbers.
10. **Two `LocKey`s ship unresolved**, `character.gravecaller.name` and `minion.wight.name`, in the
    same state M3-12c's twenty-four shipped in: M6-10 owns the table. They are authored rather than
    omitted so the class has a name to fail to display rather than no name at all.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Gravecaller_ConvertsAndIsInTheCatalog` | the shipped asset / `ToSpec` / `character.gravecaller`, and the boot catalog resolves it beside the Oathbound |
| `Gravecaller_IsEightyHitPointsAndNoShield` | the asset / converted / `MaxHp` 80, `Shield` **null** — rule 4 |
| `Gravecaller_SpeedIsTheRuledBand` | the asset / converted / `Movement.Speed` is 3.1, and it is **strictly between** the Oathbound's 3.0 and the ruled ceiling of 3.4 — rule 1 |
| `Speed_EveryClassOutrunsEveryEnemy` | both shipped characters against all shipped enemy assets / — / every character's speed divided by every enemy's `MoveSpeed` is above 1 — GD §6.1's *rule*, asserted as the ratio the parking lot asked for rather than as a pair of pinned numbers |
| `Gravecaller_BoneBoltIsTheRuledWeapon` | the asset / converted / `Projectile`, 9 damage, 4.0/s, 12 m, 360°, 0.15 frame, 40 m/s, 0.8 m — the whole of rule 3's table, one assertion a row |
| `BoneBolt_KillsAStageOneHuskInFourHits` | the shipped Bone Bolt and the shipped Husk / no tree, no Overflow / **4**, inside GD §6.2's 3–5 — rule 2's whole argument, in the fixture that owns the band |
| `BoneBolt_AtTheDocumentedSevenWouldBreakTheBand` | the same, with damage 7 / — / 6 hits, **outside** the band — the control that proves rule 2 moved something that was actually wrong |
| `BoneBolt_IsWeakerThanTheCenser` | both shipped weapons / `DpsOneSecond` / 36 against 39, and the Gravecaller's `MaxHp` is below the Oathbound's — CH §3.2's *"you are not the damage"*, asserted as a comparison rather than a number |
| `Gravecaller_TargetingAndFocusMatchTheOathbound` | both assets / converted / every `TargetingSpec` and `FocusSpec` field is equal — rule 4's decision, pinned so a divergence has to be deliberate |
| `Gravecaller_MovementIsAShroudstepThatIsNotYetOne` | the asset / converted / `Kind` is `Shroudstep`, distance 6, duration 0.05, damage **0**, knockback **0** — rule 7 |
| `Minions_AreTheAuthoredBlock` | the asset / converted / every field of rule 5's table |
| `Minions_AreNullOnTheOathbound` | `Oathbound.asset` / converted / `Minions` is null, and the asset on disk is **unchanged** — rule 6 |
| `MinionSpec_RefusesAnImpossibleBlock` | rise chance 0 / above 1 / a cap of 0 / a non-finite lifespan / constructed / throws in each case, naming the field |
| `Minion_CatchesAHusk` | the authored Wight and the shipped Husk / — / the Wight's speed over the Husk's is above 1, and its damage over the Husk's HP gives 5 hits — rule 5's two claims, so a retune of either asset reddens this |
| `Ttk_OverflowLevelsAreDerivedFromTheCurve` | the shipped `ModeSpec` / the helper at stage 15 / **8**, not 7 — ledger row 5(ii), asserted so the literal cannot come back |
| `Ttk_StageFifteen_IsWithinTheBand` | *(existing, input changed)* Keen Censer, derived Overflow / a stage-15 Husk / still **4** — the row that proves row 5(ii) was a wrong input rather than a wrong answer |
| `Assets_AreLinkedToAMonoScript` | `Gravecaller.asset` / loaded / its `m_Script` resolves — Traps §5, the row every authored asset in this project owes |
| `Keys_AreAuthoredAndUnresolved` | the two new `LocKey`s / against the English table / present on the asset, absent from the table, and the absence is asserted rather than tolerated — rule 10 |

**Guard rows are implied, not listed:** `MinionSpec`'s null-`ContentId` and null-`LocKey` doors, the
non-finite door on each of its six floats, and `CharacterDefinition.OnValidate` on the new fields.

## Manual verification (Editor / device)

1. **[Editor]** Open `Gravecaller.asset`. *Expected: every field of rules 3, 4, 5 and 7 reads as the
   tables say, the shield block is zero, and the minion block is filled.*
2. **[Editor]** Play a run. *Expected: identical to `m5-01` in every respect — the menu still starts
   an Oathbound, and nothing about the new asset is reachable (rule 9).*
3. **[Editor]** Open `Oathbound.asset`, change nothing, and check `git status`. *Expected: no diff.
   `Minions` is a nullable reference and `Shroudstep` is an appended enum member, so no shipped asset
   is rewritten — M4-01a's measured finding, which is that Unity does not rewrite an asset because
   the script that reads it grew a field.*

## Out of scope

- **Making the class playable.** M5-07's class-select screen. Rule 9.
- **Wights doing anything.** M5-04a builds the agent, M5-04b the Rise that produces one.
- **The corpse decoy, and what `Shroudstep` means.** M5-03. Rule 7 ships the member, not the behaviour.
- **The Gravecaller's tree, its Veilrot relationship, its Keystones.** M5-06, and GD §10's Veilrot
  does not exist at all yet.
- **Ledger row 5(i)** — `LevelUpFlow`'s two Overflow `const`s onto `ModeDefinition`. Ruled at M5-00a
  and placed on **M5-06**: it is a change to what a *level* is worth, it ripples through every
  `LevelUpFlow` construction site, and folding it in here would put two arguments in one review.
- **A minion `EnemySpec`.** A Wight is not an enemy and must never be one — the director would spawn
  it. [M5-04a](M5-04a-minion-agents-and-registry.md) rule 1 is where that is argued.

## As built

**Two code files, one asset, and every number of rules 1–7 converts as authored.** `MinionSpec.cs`
(Core), `GravecallerTests.cs` (Tests.Game, 12 rows), `Gravecaller.asset`, and additive edits to
`CharacterSpec`, `MovementSkillKind`, `CharacterDefinition`, `TimeToKillTests`, `ContentTests`,
`ContentValidationTests` and three docs. The asset came from a `RunCommand` through
`CreateInstance(Type)` and `SerializedProperty`, never hand-written YAML, and
`MonoScript.FromScriptableObject` resolves off a freshly loaded handle (Traps §5, M3-12c's recipe).

**Nine deviations. Three change something; the rest are counts and placements.**

**1 — `BootScope.prefab` gained the Gravecaller, and it is not in the Files table.** The Tests table's
first row says *"the boot catalog resolves it beside the Oathbound"*, and the boot catalog is the
prefab's `_characters` array. `GravecallerTests.BootCatalog` builds from that array rather than from
two paths, so the edit is load-bearing, not tidy. `_characters` reads 2; PlayMode's
`Boot_ReachesMenu_WithinFiveSeconds` boots the real prefab and did not move.

**2 — `English.asset` gained one row, and rule 10 is half spent.** The defect M5-00b reported rather
than edited: `EveryLocKey_ResolvesInEnglish` sweeps every `CharacterDefinition`'s name key, so an
unresolved `character.gravecaller.name` is a red row in another fixture rather than a deferral. It
ships as **"Gravecaller"**. **`minion.wight.name` ships unresolved exactly as rule 10 intends** —
`EveryAuthoredKey` walks a character's own name key and stops, so nothing reaches a minion block —
and `Keys_AreAuthoredAndUnresolved` asserts one of each, the absence included, so M6-10 has to add
it on purpose. M6-10 owes one row instead of two.

**3 — an unpredicted red row, and it is a real gap rather than a broken test.**
`SkillTreeValidationTests.EveryShippedCharacter_HasATree` failed on the first run: the Gravecaller has
no tree until M5-06b, and that sweep is M3-02a rule 11's obligation that a class with no tree is a
run-breaking omission. It now skips `character.gravecaller` **by name** — a rule would quietly cover
the next omission too — with `TheTreelessClass_IsStillTreeless` beside it asserting the class is
still treeless, and saying in its own message that **it goes red the day M5-06b merges and the fix
is to delete both it and the skip**.

**4 — ten minion fields on `CharacterDefinition`, not the Files table's six.** `MinionSpec` takes ten
values and all ten are authored; `_minionCap` is the null switch, on `_shieldMax`'s precedent and a
sharper version of its reason — a cap is the one field `MinionSpec` cannot represent at zero.

**5 — `Characters.md` §3.2 gained a paragraph, beyond rule 1's three named doc edits.** CH §3.2 still
publishes *7 dmg* and the control test is named for it, so the number stays; what was missing is any
record that the game ships 9. A table alone with nothing beside it is the CC §2.5 failure this task
exists to clean up, so the ruling is written under it.

**6 — `Minions_AreNullOnTheOathbound` asserts the behaviour and a conditional disk check, not
"unchanged".** `Minions` is null, and *if* `Oathbound.asset` ever carries a `_minionCap` line it must
read 0. The byte-level claim is manual step 3's: a re-serialised asset is correct content, and a row
reddening for it would report a diff rather than a defect. **Verified for this PR anyway —
`Oathbound.asset` does not appear in `git status`**, and the converted spec still reads Cone 13 / 3.0
/ 8 m / 60° / 0.4, speed 3, HP 140. M4-01a's finding holds.

**7 — rule 8's derivation was applied to stage 30 as well, and that is where the confirmation is.**
The ruling names only the stage-15 literal. `LevelAt` walks B(n) priced in Husks (`XpValue /
ThreatCost` = 3) through a real `LevelTracker`, one grant a stage; `OverflowLevelsAt` subtracts
`1 + 12`. Stage 15 is **level 21, overflow 8** — not the table's ~20 and 7. Stage 30 is **level 42,
overflow 29**, the table's own number, so it was wrong in one row rather than systematically. Both
hit counts are unmoved at 4 and 5.

**8 — `ShippedCharacters` 1 → 2**, a floor rather than an equality, so the anti-vacuity row keeps
meaning something. `minion.new.name` was deliberately **not** added to `AuthoringPlaceholders`: it is
the default on two classes of three and nothing sweeps it.

**9 — one row over the Tests table.** `BoneBolt_IsTheQuickestThingInTheAir` pins rule 3's two "ours"
numbers against the Spitter shot they were chosen relative to (40 against 12 m/s, 0.8 against 1.6 m):
a number justified by a comparison and asserted as a literal is one nobody can check. **Also authored
without being named:** accel / decel / turn are the Oathbound's 0.06 / 0.08 / 720 — CC §2.5 publishes
one set of handling numbers, which is rule 4's argument.

**Ledger row 5(ii) DISCHARGES**; row 5 stays open on (i) → M5-06b and (iii) → M6-10. **Two parking-lot
lines close**: the speed band's, whose condition was the three document edits, and the
ratio-versus-pair line, whose home was `Speed_EveryClassOutrunsEveryEnemy`. The pinned pairs in
`EnemyDefinitionTests` were left alone — a different claim.

**Verified.** **2 250 EditMode / 0 / 0** (+22 on M5-01's 2 228), twice on the final code, and
**PlayMode 16 / 0 / 0**, both through `TestRunnerApi` with the Editor focused. Console after the run:
3 errors, 2 warnings, every one a fixture provoking its own failure path and none naming a new file —
baseline unmoved, no new analyzer warnings. `dotnet format whitespace --folder
--verify-no-changes` green over all nine touched C# files. The content was probed by calling `ToSpec`
from a `RunCommand` rather than by trusting a green compile: every field of rules 3, 4, 5 and 7
printed as the tables say.
`ProjectSettings/TimeManager.asset` re-serialised itself again and was reverted before handover
(Traps §5). Manual step 2 — playing a run — is the owner's.
