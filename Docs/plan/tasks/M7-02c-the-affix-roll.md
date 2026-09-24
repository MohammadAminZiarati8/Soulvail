# M7-02c — The affix roll, and the three that act while alive

**Size:** M · **Depends on:** M7-01d — the shrug a turned-away hit makes is drawn before the ward makes them common · **Branch:** `m7-02c-the-affix-roll`
**Design refs:** GD §8.2, §8.3, §9.1, §12.3, §16.3; CC §3.6; AR §9, §11.2, §18.1, §18.3, §18.4; ADR-0008, ADR-0009, ADR-0011 · **Ledger rows:** none moved. The three gaps [M7-00a](M7-02a-what-an-elite-is.md) found and handed to this group (no attack-rate `Stat`, a shared invulnerability bool, no way for an affix to hurt the player without a blow) are rules 6, 7 and 10

## Goal

From stage 12 every Elite arrives carrying one affix, rolled at the door on the `Affixes` stream. Three
of GD §8.3's five act while the body lives: **Hasted** walks faster and recovers sooner, **Warded** is
immune while another enemy stands within 8 m, and **Siphoning** drinks from a player inside 10 m and
heals by what it drank.

## What was already built for it

- `IRandom.Affixes` — stream index 2, in `RandomState` since M2-13a, drawn today only by `Ordeals`,
  whose remarks say *"M7-02's Elites will be its second drawer."* No save format moves.
- `EnemyAgent.MoveSpeed` is a `Stat` because *"the alternative has no answer for M7-02's Hasted affix"*,
  and `Initialise` wipes all three stats with `RemoveAll()` so a recycled agent cannot wear *"the
  previous life's Elite affixes (M7-02)"*.
- `EnemySpawned.IsElite`'s remarks — *"the day an affix makes one, it comes off the affix and nothing
  downstream changes."*
- `OrdealSpec` — *"five named fields and no kind enum"* (M6-06a rule 2), the shape a closed set of
  authored modifiers takes in this project.

**What was not built, and one thing found while writing this.** M7-00a found that no attack-rate `Stat`
exists and that invulnerability is one bool Warded would share. This spec found a third: **every way
core hurts the player is a blow**. `PlayerCombat.ApplyDamage` starts the Oathbound's 0.5 s i-frames on
each hit, so a Siphon pulsing twice a second would keep him invulnerable to everything else for as long
as he stood near it. The Elite would be acting as his bodyguard. Rule 10 is the door that fixes it.

## The ruling: an affix is a spec with blocks, not an effect

ADR-0009 lists Elite affixes among *"an effect that does something"*, and AR §11.2 says the same. Its
primitives (`ModifyStat`, `SpawnHealZone` and the rest) are handled through `EffectRegistry` against
the **player's** stats, and none of them addresses an enemy body. M6-06a met this question for Ordeals
and chose named dials. This is that shape for a body. `AffixSpec` carries two stat dials and optional
blocks. Each consumer asks for its own block, and **nothing switches on which affix it is**, so
ADR-0009's ban holds. Its registry is not extended to enemies, and AR §11.2 gains one sentence saying
so, because a paragraph that promises otherwise is the drift M6-06a left behind.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/AffixSpec.cs` | Core | **New.** `AffixSpec`, and beside it `WardSpec`, `SiphonSpec` and `AffixSet` — `ProjectileSpec.cs`'s arrangement, which holds `ExplosionSpec` (rule 1) |
| `Core/Ai/EnemySystem.cs` | Core | The roll at the door (rules 3–5), the ward in perception and at the damage door (7–8), the siphon pass (11) |
| `Game/Authoring/AffixDefinition.cs` | Game | **New.** `OrdealDefinition`'s shape: an id, a name key, two dials, two blocks, an exclusion |
| `Tests/Core/Ai/AffixRollTests.cs` | Tests.Core | **New.** Rules 1–6 |
| `Tests/Core/Ai/LivingAffixTests.cs` | Tests.Core | **New.** Rules 7–12 |
| *small edits* | Core, Game, Data, Docs | `Core/Ai/EnemyAgent.cs` — `Affixes`, `AttackRate`, `RecoverTime`, `IsWarded` and the siphon clock, every one reset in `Initialise`; `Core/Ai/ChaserBehaviour.cs`, `SpitterBehaviour.cs`, `LungerBehaviour.cs`, `ShieldedBehaviour.cs`, `WardenBehaviour.cs` — one read each (rule 6); `Core/Content/ModeSpec.cs` — `AffixScheduleSpec` beside `OrdealScheduleSpec`, and `ModeSpec.AffixSchedule` / `Affixes` optional and last (rule 2); `Core/Combat/Health.cs` — `Drain`; `Core/Combat/PlayerCombat.cs` — `Drain`, and `BuildCandidates` reads the ward (rules 9–10); `Core/Events/EnemyEvents.cs` — `EnemySpawned`'s two affix ids, `EnemyWardChanged`, `EnemyHealed`; `Core/Events/CombatEvents.cs` — `PlayerDrained`; `Game/Authoring/ModeDefinition.cs` — an `AffixesBlock` beside `OrdealsBlock`; `Game/Presentation/HudPresenter.cs` — the bar follows a drain (rule 12); `Data/Affixes/Hasted.asset`, `Warded.asset`, `Siphoning.asset` — **new**, rule 13; `Data/Modes/Descent.asset` — the schedule and the pool; `Data/Localisation/English.asset` — three names; `Pseudo.asset` regenerated; `Tests/Game/Authoring/ContentValidationTests.cs` — `EveryShippedMode_AuthorsItsAffixes`; `Docs/GameDesign.md` — §8.3's Hasted and Siphoning rows as built; `Docs/Architecture.md` — §11.2's sentence, an §18.3 row (the stream's second drawer) and an §18.4 row (attrition starts nothing) |
| *ripple* | — | **81 `new ModeSpec(...)` sites and 21 `new EnemySpawned(...)` sites are untouched**: every new argument is optional and last. `EnemySystem`'s constructor remark *"Only `IRandom.Spawn` is ever drawn from here"* is rewritten |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Content;

/// <summary>
/// One of GD §8.3's affixes, as authored data: two stat dials, the blocks it carries, and the one
/// affix it may never share a body with. No kind enum — each consumer asks for its own block (rule 1).
/// </summary>
public sealed class AffixSpec
{
    /// <param name="excludes">The affix this one never shares a body with, or <c>default</c>. Checked both ways (M7-02f).</param>
    /// <param name="moveSpeedMultiplier">Times the body's move speed. 1 is unchanged; Hasted 1.45.</param>
    /// <param name="attackRateMultiplier">Times its attack rate. 1 is unchanged; Hasted 1.3.</param>
    /// <exception cref="ArgumentException">A default id or name key; <paramref name="excludes"/> naming itself; or every dial neutral and no block (rule 1).</exception>
    /// <exception cref="ArgumentOutOfRangeException">A multiplier that is not finite and above zero.</exception>
    public AffixSpec(
        ContentId id,
        LocKey nameKey,
        ContentId excludes = default,
        float moveSpeedMultiplier = 1f,
        float attackRateMultiplier = 1f,
        WardSpec ward = null,
        SiphonSpec siphon = null);
    // M7-02d appends:  PoolSpec deathPool = null, BurstSpec deathBurst = null

    public ContentId Id { get; }
    public LocKey NameKey { get; }
    public ContentId Excludes { get; }
    public float MoveSpeedMultiplier { get; }
    public float AttackRateMultiplier { get; }
    public WardSpec Ward { get; }
    public SiphonSpec Siphon { get; }
}

/// <summary>Immune while any other living enemy stands within <see cref="Radius"/> (rule 7).</summary>
public sealed class WardSpec
{
    /// <param name="radius">Metres, XZ, inclusive. 8 for Warded (GD §8.3). Finite, above zero.</param>
    public WardSpec(float radius);
    public float Radius { get; }
}

/// <summary>Drains a player inside <see cref="Radius"/> in pulses, and heals by what it took (rule 11).</summary>
public sealed class SiphonSpec
{
    /// <summary>Seconds between pulses. A constant, like <c>MovementSkillSpec.PoolPulseInterval</c>.</summary>
    public const float PulseInterval = 0.5f;

    /// <param name="radius">Metres, XZ, inclusive. 10 (GD §8.3).</param>
    /// <param name="strikeFractionPerSecond">Of the body's <c>ContactDamage</c>, per second. 0.25.</param>
    public SiphonSpec(float radius, float strikeFractionPerSecond);
    public float Radius { get; }
    public float StrikeFractionPerSecond { get; }
}

/// <summary>
/// What one body carries: none, one or two affixes. A struct, defaulted to none, on
/// <c>EnemySpawned</c>'s reasoning — <c>default</c> is a true statement about a plain body.
/// </summary>
public readonly struct AffixSet
{
    public AffixSet(AffixSpec first, AffixSpec second = null);
    public AffixSpec First { get; }
    public AffixSpec Second { get; }
    public int Count { get; }

    /// <summary>The first ward either affix carries, or null. Two affixes never both carry one (M7-02f).</summary>
    public WardSpec Ward { get; }
    public SiphonSpec Siphon { get; }
}

/// <summary>When a mode's Elites carry affixes. <c>default</c> is never — every fixture before M7.</summary>
public readonly struct AffixScheduleSpec
{
    /// <param name="firstStage">The first depth an Elite rolls one. 12 (GD §8.2). At least 1.</param>
    public AffixScheduleSpec(int firstStage);
    // M7-02f appends:  int secondStage = 0, float secondCostMultiplier = 0f
    public int FirstStage { get; }
    public bool IsAuthored { get; }
}

public sealed class ModeSpec
{
    // ... after M7-02a's `elites`, defaulted and last:
    //     AffixScheduleSpec affixSchedule = default, IReadOnlyList<AffixSpec> affixes = null
    public AffixScheduleSpec AffixSchedule { get; }
    public IReadOnlyList<AffixSpec> Affixes { get; }
}
```

```csharp
namespace Soulvail.Core.Ai;

public sealed class EnemyAgent
{
    /// <summary>What this body carries. Empty on every fresh agent; set only by <c>EnemySystem.Spawn</c>.</summary>
    public AffixSet Affixes { get; internal set; }

    /// <summary>Attacks per unit time against the archetype's, as a stat. Base 1; Hasted's 1.3 is a PercentMult.</summary>
    public Stat AttackRate { get; }

    /// <summary><c>Spec.RecoverTime</c> ÷ <see cref="AttackRate"/>, or the authored time for a rate that is not a positive finite number (rule 6).</summary>
    public float RecoverTime { get; }

    /// <summary>Whether a ward is up this tick — written by perception, and by nothing else (rule 7).</summary>
    public bool IsWarded { get; internal set; }
}
```

```csharp
namespace Soulvail.Core.Combat;

public sealed class Health
{
    /// <summary>
    /// Attrition: takes up to <paramref name="amount"/> from hit points alone, starting nothing (rule 10).
    /// Turned away while invulnerable. Can kill.
    /// </summary>
    public DamageResult Drain(float amount, float now);
}

public sealed class PlayerCombat
{
    /// <param name="sourceId">The enemy draining, or 0 for no body (M7-02d's pool).</param>
    public DamageResult Drain(float amount, float now, int sourceId);
}
```

```csharp
namespace Soulvail.Core.Events;

public readonly struct EnemySpawned
{
    // ... after isElite, defaulted — default(ContentId) *is* "none":
    //     ContentId affix = default, ContentId secondAffix = default
    public readonly ContentId Affix;
    public readonly ContentId SecondAffix;
}

/// <summary>A Warded body's ward went up or down. Published on the edge, never per tick.</summary>
public readonly struct EnemyWardChanged { public EnemyWardChanged(int id, bool isWarded); /* fields */ }

/// <summary>An enemy recovered hit points. Only a Siphon does, today.</summary>
public readonly struct EnemyHealed { public EnemyHealed(int id, float amount, float hpFraction); /* fields */ }

/// <summary>Attrition reached the player. Not a blow: <c>PlayerDamaged</c> is for those.</summary>
public readonly struct PlayerDrained { public PlayerDrained(int sourceId, float amount, float hpFraction); /* fields */ }
```

## Behaviour

1. **An affix is dials and blocks, and nothing dispatches on which one it is.** `AffixSpec` carries two
   stat multipliers, where 1 means unchanged, plus a `WardSpec` and a `SiphonSpec`. M7-02d appends its
   two death blocks. An affix that leaves every dial at 1 and carries no block is refused at the door,
   under OrdealSpec rule 3's sentence. `Spec_HasNoKindAndNothingDispatchesOnOne` pins the shape by
   reflection, which is how `OrdealSpec`'s row does it. The helper types live in `AffixSpec.cs` because
   `ProjectileSpec.cs` already holds `ExplosionSpec`, and a ward is no more a file than a blast is.
2. **What rolls, and from when, is the mode's statement.** Descent authors `AffixScheduleSpec(12)` and a
   pool of Hasted, Warded and Siphoning; M7-02d adds the other two. The mode refuses four things, each
   naming its id:
   - a pool without a schedule, or a schedule without a pool;
   - a schedule on a mode that authors no `EliteSpec`, because affixes ride Elites;
   - two affixes with one id;
   - an `Excludes` naming an affix outside the pool, which is a typo that would otherwise pass as a rule.

   `EveryShippedMode_AuthorsItsAffixes` asserts that every shipped mode authoring Elites also authors
   affixes. That is M7-02a rule 1's bargain.
3. **The roll is at the door, on the `Affixes` stream, one draw per Elite from the first stage.** The
   roll runs in `Spawn` after M7-02a's upgrade and before `EnemySpawned`. A body that is an Elite, either
   asked for or authored `IsElite`, at `Depth >= FirstStage` draws
   `_random.Affixes.NextInt(0, pool.Count)` once. A plain body draws nothing, and so does an Elite below
   the first stage. **The `Spawn` stream is never touched**, so a seed composes the same waves with or
   without a pool (ADR-0011). Ordeals draw on the same stream at boundaries and Elites draw mid-stage;
   both orders are fixed by the run, so a seed replays. Nothing spawned by a death is an Elite (M7-02a
   rule 7), so a Weaver's children roll nothing.
4. **An affix is a fact about the body, forgotten on recycle.** `EnemyAgent.Affixes` is empty after
   `Initialise`. Every modifier an affix adds is sourced to its `AffixSpec`, and `Initialise` already
   wipes every stat with `RemoveAll()`, so ledger row 2's answer covers affixes without a new line.
   `EnemySpawned` carries both ids, and `default` means none, the same reasoning that defaults
   `IsElite`. **Nothing reads them until [M7-02e](M7-02e-an-elite-you-can-see.md).** Like
   `EnemyWardChanged` and `EnemyHealed`, that is a stated exception to M6-06a rule 6, argued as
   M7-01a rule 4 argues it: publishing them from a view task would mean a view task editing the
   census.
5. **Hasted is two dials.** `MoveSpeed` gains a `PercentMult` of 0.45 after depth and Veilrot, so
   the three multiply. `AttackRate` gains 0.3. A Hasted Spitter at 2.8 × 1.45 = 4.06 m/s outruns all
   three classes. `Speed_EveryClassOutrunsEveryEnemy` walks *authored bases* and stays true. An Elite
   you cannot outrun is GD §8.3's own number, and the game's answer to it is that an Elite is a
   priority target.
6. **Attack rate shortens the recovery and never the tell.** `EnemyAgent.AttackRate` is a `Stat` with
   base 1, built once and wiped in `Initialise` as `MoveSpeed` is. `RecoverTime` divides the
   archetype's recovery by it. A rate that is not positive and finite reads as the authored time, since
   `Stat` clamps nothing (M3-12a rule 7's floor).
   - **Five behaviours read `_agent.RecoverTime` in place of `Spec.RecoverTime`**: Chaser, Spitter,
     Lunger, Shielded, and the boss's `WardenBehaviour`. A boss is never an Elite, so its rate is 1;
     changing it anyway leaves no reader of the spec's value for the next behaviour to copy.
   - **`WindupTime` is not divided anywhere.** The tell is the dodge window (GD §9.1 rule 1, P1), and
     a Hasted body that telegraphed for less would be harder to *read*, not merely faster.
   - So a Hasted Husk's cycle is 0.4 + 0.46 s rather than 0.4 + 0.6 s. GD §8.3's row is amended in
     this PR to say *"+30 % attack rate: its recovery ÷ 1.3, and the telegraph never shortens."*
7. **Warded is its own flag, with one writer.** `EnemyAgent.IsWarded` is written once a tick in
   `Perceive`, for an agent whose affixes carry a ward. It is true while any *other living* agent
   stands within `Ward.Radius` (XZ, inclusive). The walk runs only for warded agents, so its cost is
   paid by the Elites that carry one.
   - **It is not `Health.SetExternalInvulnerable`**, whose one writer on an enemy is the boss beat.
   - **It is not `IsVulnerable`**, which M7-01c's Warden writes every tick. A second writer on either
     bool is the bug M7-00a found: whichever wrote last would win.
   - `EnemyWardChanged` is published on each edge. A fresh spawn is unwarded until its first
     perception, one tick later.
8. **The damage door asks the ward before the guard.** `ApplyDamage` on a warded agent returns
   `Blocked`, applies nothing, and publishes `EnemyDamaged(id, 0, fraction, killed: false)`, which is
   M7-01c rule 3's shape. This holds **for every source and every door**: the cone, the Charge, a
   bolt, a burn, a Wight, and a Bloater's blast. The ward is immunity, not a direction.
9. **Auto-aim does not pick a warded body.** `BuildCandidates` scores
   `IsAlive && IsVulnerable && !IsWarded`, so the gun works through the escort, which is the answer
   GD §8.3's row implies. **`Targeter` and `TargetScorer` do not change**. When every candidate is
   warded, CC §3.6's hold-on-the-nearest is what happens, as M7-01c reached it.
10. **Attrition gets its own door, and it starts nothing.** `Health.Drain` works as follows:
    - It takes up to `amount` from **hit points alone**, never from a granted shield or the Aegis.
    - It starts **no i-frames** and does **not restart the Aegis delay**.
    - It is turned away (`Blocked`, nothing taken) while `IsInvulnerable`, so a Charge's dash beats a
      Siphon as it beats a blow.
    - It can kill.

    `PlayerCombat.Drain` publishes `PlayerDrained(sourceId, taken, fraction)` for what was actually
    taken and stays silent otherwise. It **does not break Kindling**: M6-07a rule 7 breaks the ramp on
    being hit, and a drain is not a hit. It reports a death through `AnnounceDeath`, the one publisher
    of `PlayerDied`.

    **The alternatives were both worse, measured on the Oathbound.** Through `ApplyDamage`, every pulse
    grants 0.5 s of i-frames, and a pulse every 0.5 s keeps him invulnerable to everything else. Through
    the Aegis, a 2 HP/s drain against a 15/s refill never reaches his hit points at all. M7-02d's pool
    walks through this same door.
11. **Siphoning pulses on an absolute clock while the real player is within 10 m.** For an agent whose
    affixes carry a siphon, the pass runs in `EnemySystem.Tick` after that agent's behaviour ticks, and
    therefore above the death check.
    - **Range** is measured to the real player, `EnemySystem.PlayerPosition` (M7-01a rule 6), never a
      decoy, and is XZ and inclusive.
    - **Timing:** the first pulse lands one `PulseInterval` after the player entered range. Out of
      range the clock stops, and the next entry starts it fresh. A long tick catches up rather than
      skipping, which is `ZoneSystem.Tick`'s rule.
    - **Amount:** each pulse drains `ContactDamage.Value × StrikeFractionPerSecond × PulseInterval`.
      That is 8 × 0.25 × 0.5 = 1 per pulse, or 2 HP/s on a stage-1 Husk, which is GD's number, and
      d(n) reaches it through the stat.
    - **Heal:** what was taken heals the body by the same amount through `Health.Heal`, never past its
      maximum (M7-02a rule 4). A heal that restored anything publishes `EnemyHealed`.

    GD §8.3's row is amended in this PR to say *"drains a quarter of its strike a second (2 HP/s on a
    stage-1 Husk) and heals by what it took"*.
12. **The HUD's bar follows a drain, and nothing else does.** `HudPresenter` subscribes to
    `PlayerDrained` and redraws exactly as `OnPlayerDamaged` does for a hit that landed. **There is no
    haptic and no blocked flash.** GD §16.3's heavy buzz is for blows, and one twice a second would
    last as long as the Siphon did.
13. **The three assets.** GD §8.3 publishes every number except the siphon's fraction, which rule 11
    derives from GD's own 2 HP/s.

    | Asset | `_id` | Dials and blocks | `_excludes` |
    |---|---|---|---|
    | `Hasted.asset` | `affix.hasted` | move **1.45**, attack **1.3** | — |
    | `Warded.asset` | `affix.warded` | ward radius **8** | `affix.siphoning` — GD §8.3's blacklist, authored now and first checked by M7-02f |
    | `Siphoning.asset` | `affix.siphoning` | siphon radius **10**, **0.25** of a strike a second | — |
14. **Nothing on a frame path allocates.** The roll is one draw and a struct write, the ward walk is a
    span and squared distances, and a siphon pulse is arithmetic plus two calls that already allocate
    nothing.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.**

## Tests

| Test | Given / When / Then |
|---|---|
| `Spec_RefusesAnAffixThatDoesNothing` | every dial 1, no block / — / throws — rule 1 |
| `Spec_HasNoKindAndNothingDispatchesOnOne` | `typeof(AffixSpec)` / reflection / no enum member — rule 1 |
| `Spec_RefusesAnAffixThatExcludesItself` | `excludes == id` / — / throws |
| `Mode_RefusesAPoolWithoutASchedule` | and the reverse / — / throws, naming the mode — rule 2 |
| `Mode_RefusesAffixesWithoutElites` | a schedule on a mode with no `EliteSpec` / — / throws — rule 2 |
| `Mode_RefusesAnExclusionOutsideThePool` | Warded excluding an id the pool lacks / — / throws, naming both — rule 2 |
| `Mode_RefusesTwoAffixesWithOneId` | / — / throws — rule 2 |
| `Roll_AnEliteFromTheFirstStageCarriesOne` | stage 12, the draw scripted to 1 / an Elite spawn / `Affixes.First` is the pool's second entry, `Count` 1 — rule 3 |
| `Roll_BelowTheFirstStageDrawsNothing` | stage 11 / an Elite spawn / no affix, the `Affixes` stream unmoved — rule 3 |
| `Roll_APlainBodyDrawsNothing` | stage 20 / a plain spawn / no affix, stream unmoved — rule 3 |
| `Roll_OneDrawPerElite` | five Elites at stage 20 / — / the stream advanced by exactly five — rule 3 |
| `Roll_TheSpawnStreamIsUntouched` | one seed, with and without a pool / a stage composed and spawned / identical `Spawn` positions and plans — rule 3 |
| `Roll_AnAuthoredEliteArchetypeRollsToo` | a spec authored `isElite` / default spawn at 12 / one affix — rule 3 |
| `Roll_ChildrenOfAnAffixedEliteAreBare` | an Elite Weaver with an affix / its split / both children carry none — rule 3 |
| `Roll_IsAnnounced` | an affixed Elite / spawn / `EnemySpawned.Affix` is its id, `SecondAffix` default — rule 4 |
| `Roll_ARecycledAgentForgets` | a Hasted Elite despawned and rented as a plain Husk / — / no affix, speed `2 × s(n)`, `AttackRate.Value` 1 — rule 4 |
| `Hasted_MultipliesWithDepthAndTheVeil` | stage 20, Veilrot 25 / a Hasted spawn / speed = base × s(20) × 1.05 × 1.45 — rule 5 |
| `Hasted_RecoversSoonerAndTellsAsLong` | a Hasted Husk / a full strike cycle / windup 0.4 s, recovery 0.6 ÷ 1.3 — rule 6 |
| `Hasted_ReachesEveryBehaviourThatRecovers` | a Hasted Chaser, Spitter, Lunger, Shielded and a boss inner with its rate raised / each recovery / ÷ 1.3 — rule 6 |
| `RecoverTime_ReadsTheAuthoredTimeForARateThatIsNotANumber` | `AttackRate` driven to 0, then NaN / — / `Spec.RecoverTime` — rule 6 |
| `Ward_UpWhileAnotherEnemyStandsWithinEight` | a Warded Elite, a Husk at 7.9 m / ingest / `IsWarded` — rule 7 |
| `Ward_DownWhenAlone` | the Husk walks to 8.1 m / ingest / not warded — rule 7 |
| `Ward_TheEdgeIsInclusive` | a Husk at exactly 8 m / — / warded — rule 7 |
| `Ward_ACorpseWardsNothing` | the only neighbour dead / — / not warded — rule 7 |
| `Ward_PublishesItsEdgesOnly` | up for ten ticks, then down / — / exactly two `EnemyWardChanged` — rule 7 |
| `Ward_SharesNoFlag` | a warded Elite / — / `Health.IsInvulnerable` false, `IsVulnerable` true — rule 7 |
| `Ward_TurnsAwayEveryDoor` | warded / a cone, a Charge, a bolt, a burn, a Wight, a blast / each `Blocked`, HP unchanged, `EnemyDamaged(id, 0, 1, false)` — rule 8 |
| `Ward_IsNotTargeted` | a warded Elite at 3 m, a Husk at 8 m / a targeting tick / the Husk; the Husk killed, then the Elite — rule 9 |
| `Drain_TakesHitPointsAndStartsNothing` | the Oathbound, Aegis full / `Drain(5)` / HP −5, Aegis full, not invulnerable, a Husk's strike on the next tick lands — rule 10 |
| `Drain_DoesNotHoldOffTheAegis` | Aegis empty, 4 s since the last blow, a drain every tick / — / the Aegis refills on time — rule 10 |
| `Drain_IsTurnedAwayByInvulnerability` | mid-Charge / `Drain(5)` / `Blocked`, HP unchanged, no `PlayerDrained` — rule 10 |
| `Drain_CanKill` | 1 HP left / `Drain(5)` / dead, one `PlayerDied` — rule 10 |
| `Drain_DoesNotBreakKindling` | the Emberwright at full Kindling / a drain / Kindling unchanged — rule 10 |
| `Siphon_DrainsTheRealPlayerWithinTen` | a stage-1 Siphoning Husk Elite, the player at 9 m / 2 s / 4 HP taken in four pulses — rule 11 |
| `Siphon_FirstPulseIsOneIntervalAfterEntering` | the player steps in at t / — / nothing before t + 0.5 — rule 11 |
| `Siphon_StopsOutsideTheRadius` | the player at 10.1 m / 2 s / nothing — rule 11 |
| `Siphon_IgnoresADecoy` | a decoy within 10 m, the player at 20 / — / nothing — rule 11 |
| `Siphon_HealsItselfWhatItTook` | the Elite at half HP / a pulse / healed by the amount taken, one `EnemyHealed` — rule 11 |
| `Siphon_NeverHealsPastItsMaximum` | the Elite full / pulses / no `EnemyHealed`, HP at max — rule 11 |
| `Siphon_ScalesWithDepth` | stage 20 / a pulse / `8 × d(20) × 0.125` — rule 11 |
| `Siphon_AKillingPulseEndsTheRunOnItsTick` | 1 HP left / the tick a pulse lands / `RunEnded` on that tick — rule 11, above the death check |
| `Hud_RedrawsTheBarOnADrain` | a HUD over a run / `PlayerDrained` / the bar and the readout at the new fraction, no blocked flash — rule 12 |
| `LivingAffixes_TickAllocatesNothing` | a warded, a siphoning and a Hasted Elite, 10 000 ticks / `AllocationAssert.None` / zero — rule 14 |
| `Roll_SpawnAllocatesNothingOnARecycledAgent` | 1 000 affixed spawns and despawns / after the first life / zero — rule 14 |
| `Affixes_AssetsCarryTheDesignNumbers` | the three assets / converted / rule 13's table |
| `Descent_AuthorsTheAffixSchedule` | `Descent.asset` / converted / 12, and the three ids in order — rule 2 |
| `EveryShippedMode_AuthorsItsAffixes` | every shipped mode with Elites / — / an authored schedule and a non-empty pool — rule 2 |

**Guard rows are implied, not listed:** each block's non-finite and non-positive numbers, a
`firstStage` below 1 on an authored schedule, `Drain`'s NaN amount and clock.

## Manual verification (Editor / device)

1. **[Editor]** Play to stage 12 and let an Elite live. *Expected: most will be one of three things you
   can feel before you can see. A Hasted one keeps pace with you. A Warded one shrugs off the Censer
   while Husks stand near it and dies normally once they are gone. A Siphoning one takes your hit
   points down a point at a time while you stand within ten metres, with no buzz and no flash.*
   The affix is not drawn until M7-02e; a warded body's turned-away hits shrug in M7-01d's steel.
2. **[Editor]** As the Oathbound, stand beside a Siphoning Elite with Husks around it. *Expected: the
   Husks' strikes still land — rule 10's defect, answered.*

## Out of scope

- **Volatile and Splintered.** [M7-02d](M7-02d-the-two-that-fire-on-death.md).
- **A second affix, and the blacklist it is checked against.** [M7-02f](M7-02f-a-second-affix-bought.md);
  `Excludes` is authored here and read there.
- **Drawing any of it** — the ward, the siphon's line, the affix's name. [M7-02e](M7-02e-an-elite-you-can-see.md).
- **A heal-over-time or a drain on anything but the player.** One reader each; the doors wait for a second.
