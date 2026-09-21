using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// CH §4.1's 40 % floor and CH §4.2's <em>"the player does nothing"</em>: the shortest a cooldown
/// may become, and the object that owns the clocks and fires a skill without being asked.
/// </summary>
/// <remarks>
/// <para>
/// <b>The floor and the runner in one fixture</b>, because the floor is only interesting as the
/// thing two objects obey: the runner and <c>ChargeSkill</c>. The rows that hold the Charge to it
/// are in <c>ChargeSkillTests</c>, beside the rest of that clock.
/// </para>
/// <para>
/// <b>Against a real <c>EffectRegistry</c> over a real <c>PlayerCombat</c> throughout</b> —
/// <c>SkillTreeTests</c>' shape and its reason: a fixture handler would agree with whatever the
/// runner did, and the one thing rule 8 is about is which <em>object</em> ends up on a
/// <c>Modifier.Source</c>.
/// </para>
/// <para>
/// The two ordering rows drive a whole <c>RunSession</c> with a Spitter in the arena, because there
/// is no other way to observe where in a tick the runner runs. They tick one frame at a time and
/// clear the recorder between frames, so each assertion is about <em>one</em> tick's event order
/// rather than about a run's — <c>FrameOrderTests</c>' shape, on the core side of the boundary.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SkillRunnerTests
{
    private const string OathboundId = "character.oathbound";
    private const string SpitterId = "enemy.spitter";
    private const string ModeId = "mode.test";
    private const string TreeId = "tree.oathbound";

    private const string ArenaOne = "arena.pillars";
    private const string ArenaTwo = "arena.tiered";

    private const string ActiveA = "skill.test.a";
    private const string ActiveB = "skill.test.b";
    private const string ActiveThird = "skill.test.third";

    /// <summary>The fourth and fifth exist only so the slot rows can reach CC §6.2's ceiling.</summary>
    private const string ActiveFourth = "skill.test.fourth";
    private const string ActiveFifth = "skill.test.fifth";

    private const string PassiveB = "skill.test.passive";
    private const string PassiveC = "skill.test.c";

    /// <summary>The Charge's authored cooldown, and the number every floor row counts from.</summary>
    private const float ChargeCooldown = 2.5f;

    /// <summary>An active's authored cooldown. Eight seconds floors at 3.2.</summary>
    private const float ActiveCooldown = 8f;

    private const float MaxHp = 140f;
    private const float WeaponDamage = 13f;
    private const float MoveSpeed = 3f;

    private const int EnemyCapacity = 32;
    private const int ProjectileCapacity = 8;
    private const int DeviceCap = 28;

    private const float Frame = 1f / 60f;

    /// <summary>Comfortably outside the director's minimum player distance.</summary>
    private const float Ring = 12f;

    /// <summary>The Spitter's standoff range (GD §8.1) — where the fixture holds every body.</summary>
    private const float StandoffRange = 14f;

    private static readonly Vector3 Standoff = new Vector3(0f, 0f, StandoffRange);

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _combat;
    private PlayerStats _stats;
    private EffectRegistry _registry;
    private SkillRunner _runner;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _combat = new PlayerCombat(Character(), _events, _intents, EnemyCapacity);
        _stats = Stats(_combat);
        _registry = new EffectRegistry();

        _registry.Register<ModifyStat>(new ModifyStatHandler(_stats));

        _runner = new SkillRunner(_registry, _combat.Blackboard, _events);
    }

    // ---- The floor (rules 1, 2) ------------------------------------------------------------------

    [Test]
    public void Floor_IsFortyPercentOfBase()
    {
        // CH §4.1: "multiplicative and floored at 40 % of base". 2.5 s floors at 1.0 s, and a live
        // value of half a second is below it.
        Assert.That(CooldownRules.Effective(ChargeCooldown, 0.5f), Is.EqualTo(1f).Within(0.0001f));

        // And the 8 s active the rest of this fixture plays, which floors at 3.2.
        Assert.That(CooldownRules.Effective(ActiveCooldown, 0.4f), Is.EqualTo(3.2f).Within(0.0001f));

        Assert.That(CooldownRules.FloorFraction, Is.EqualTo(0.4f));
    }

    [Test]
    public void Floor_LeavesAnUnreducedCooldown()
    {
        // The floor is a clamp and not a transformation: a cooldown nothing has touched comes back
        // as itself, to the bit. A row that only checked "not less than the floor" would pass
        // against a function that returned the floor every time.
        Assert.That(CooldownRules.Effective(ChargeCooldown, ChargeCooldown), Is.EqualTo(ChargeCooldown));
        Assert.That(CooldownRules.Effective(ChargeCooldown, 1.4f), Is.EqualTo(1.4f));
    }

    [Test]
    public void Floor_CatchesZeroNegativeAndNaN()
    {
        // Stat clamps nothing (ADR-0008), so a stack can drive a cooldown to zero, below it, or —
        // through no door Stat allows, but through arithmetic a caller might do — to nonsense. This
        // is the layer that says no, and it says the same thing to all four.
        Assert.That(CooldownRules.Effective(ChargeCooldown, 0f), Is.EqualTo(1f).Within(0.0001f));
        Assert.That(CooldownRules.Effective(ChargeCooldown, -4f), Is.EqualTo(1f).Within(0.0001f));

        Assert.That(
            CooldownRules.Effective(ChargeCooldown, float.NaN),
            Is.EqualTo(1f).Within(0.0001f),
            "!(live > floor) rather than live < floor: the natural spelling waves NaN through, and "
                + "a NaN _readyAt is a skill that never fires again.");

        Assert.That(
            CooldownRules.Effective(ChargeCooldown, float.NegativeInfinity),
            Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void Floor_InfiniteLiveIsNotFloored()
    {
        // The one value that is not floored, and deliberately: a stack that made the wait
        // unmeasurable has not earned having it shortened to 40 % of base. The callers draw a full
        // fill for it rather than dividing by it — see CooldownFraction.
        Assert.That(
            CooldownRules.Effective(ChargeCooldown, float.PositiveInfinity),
            Is.EqualTo(float.PositiveInfinity));
    }

    [Test]
    public void Floor_BaseGuards()
    {
        // The non-finite row this class's one float door owes, on the argument that is content
        // rather than a stack: there is no such thing as 40 % of nothing. ActiveSpec already
        // refuses a cooldown like this at the door (M3-02a rule 4); this is the second end.
        Assert.Throws<ArgumentOutOfRangeException>(() => CooldownRules.Effective(0f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => CooldownRules.Effective(-1f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => CooldownRules.Effective(float.NaN, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CooldownRules.Effective(float.PositiveInfinity, 1f));
    }

    [Test]
    public void Floor_SixMultiplicativeNodesReachIt()
    {
        // **Rule 2's arithmetic, which is what M3-12 authors against.** Six −15 % multiplicative
        // nodes reach the floor and five do not: 0.85^5 = 0.4437 → 1.109 s, above; 0.85^6 = 0.3771
        // → 0.943 s, floored to 1.0. That the multiplicative route costs half again as many picks
        // as the additive one below is the point of CH §4.1's word.
        var stat = new Stat(ChargeCooldown);

        for (int i = 0; i < 5; i++)
        {
            stat.Add(new Modifier(ModifierKind.PercentMult, -0.15f, $"node{i}"));
        }

        Assert.That(
            CooldownRules.Effective(ChargeCooldown, stat.Value),
            Is.EqualTo(1.109f).Within(0.001f),
            "Five nodes leave it above the floor, so the floor is not what produced this number.");

        stat.Add(new Modifier(ModifierKind.PercentMult, -0.15f, "node5"));

        Assert.That(stat.Value, Is.LessThan(1f), "The sixth takes the raw value under the floor.");
        Assert.That(CooldownRules.Effective(ChargeCooldown, stat.Value), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void Floor_FourAdditiveNodesReachIt()
    {
        // **Four additive −15 % nodes, and the assertion is on Effective rather than on the stat.**
        // In real arithmetic 1 − 0.60 = 0.40 lands exactly on the floor; in float it does not. Four
        // PercentAdd of −0.15f sum to −0.6000000238, so the multiplier is 0.3999999762 and the stat
        // is 0.99999994 — a hair *below* 1.0, where FloorFraction × 2.5f is exactly 1.0f. The row
        // is green because the floor **catches** it, which is the right reason; asserting the
        // stat's own value would be asserting 1f against 0.99999994f.
        var stat = new Stat(ChargeCooldown);

        for (int i = 0; i < 4; i++)
        {
            stat.Add(new Modifier(ModifierKind.PercentAdd, -0.15f, $"node{i}"));
        }

        Assert.That(
            stat.Value,
            Is.LessThan(1f),
            "The fixture's own claim, out loud: four additive nodes land just under the floor "
                + "rather than on it, which is why the assertion below is on Effective.");

        Assert.That(CooldownRules.Effective(ChargeCooldown, stat.Value), Is.EqualTo(1f).Within(0.0001f));
    }

    // ---- Taking ownership (rule 5) ---------------------------------------------------------------

    [Test]
    public void Add_TakesOwnership()
    {
        SkillSpec skill = Active(ActiveA);

        _runner.Add(skill);

        Assert.That(_runner.Count, Is.EqualTo(1));
        Assert.That(_runner.IdAt(0), Is.EqualTo(Id(ActiveA)));
        Assert.That(_runner.SpecAt(0), Is.SameAs(skill));

        // A fresh Stat seeded from ActiveSpec.Cooldown, not the spec's raw number (ADR-0008): this
        // is what M3-12's node puts a modifier on, and the spec stays what a designer typed.
        Assert.That(_runner.CooldownOf(0).Base, Is.EqualTo(ActiveCooldown));
        Assert.That(_runner.CooldownOf(0).ModifierCount, Is.EqualTo(0));
    }

    [Test]
    public void Add_Duplicate_Throws()
    {
        _runner.Add(Active(ActiveA));

        Assert.Throws<InvalidOperationException>(() => _runner.Add(Active(ActiveA)));

        Assert.That(_runner.Count, Is.EqualTo(1), "And the refusal left the entry alone.");
    }

    [Test]
    public void Add_NonActive_Throws()
    {
        // Every other kind is already in force the moment SkillTree.Take applied its effects, and
        // has nothing here to own — a Passive in the walk would be an entry with no ActiveSpec to
        // read a cooldown or a trigger off.
        Assert.Throws<ArgumentException>(() => _runner.Add(Passive(PassiveB, 0.05f)));
    }

    [Test]
    public void Add_PastCapacity_Throws()
    {
        for (int i = 0; i < SkillRunner.MaxActives; i++)
        {
            _runner.Add(Active($"skill.test.fill{i}"));
        }

        Assert.That(_runner.Count, Is.EqualTo(SkillRunner.MaxActives));

        // **Unreachable in a live run, and kept anyway.** RunSession.Start refuses a tree holding
        // more actives than this before RunStarted, which is the same shape SkillTree's constructor
        // gives EffectRegistry.Apply: sweep the authored content at Start, keep the throw as the
        // backstop. Start_OverCapacityTreeRefusesTheRun is the row for the sweep.
        Assert.Throws<InvalidOperationException>(() => _runner.Add(Active("skill.test.thirteenth")));
    }

    [Test]
    public void Add_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _runner.Add(null));
    }

    [Test]
    public void Runner_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(
            () => new SkillRunner(null, _combat.Blackboard, _events));

        Assert.Throws<ArgumentNullException>(() => new SkillRunner(_registry, null, _events));

        Assert.Throws<ArgumentNullException>(
            () => new SkillRunner(_registry, _combat.Blackboard, null));
    }

    [Test]
    public void Runner_IndexGuards_Throw()
    {
        _runner.Add(Active(ActiveA));

        // Every door that takes an index, on both sides of the owned range. One row rather than
        // six, because they share the guard and a failure names the member either way.
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.IdAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.SpecAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.CooldownOf(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.IsReady(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.CooldownFraction(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.Cast(1, 0f, auto: false));
    }

    [Test]
    public void Fresh_SkillIsReady()
    {
        _runner.Add(Active(ActiveA));

        // Zero at rest is in the past for any clock, so the first cast of a run fires on the tick it
        // is asked for rather than waiting out a cooldown nobody spent — ChargeSkill's _readyAt.
        Assert.That(_runner.IsReady(0), Is.True);
        Assert.That(_runner.CooldownFraction(0), Is.EqualTo(0f));
    }

    // ---- The walk (rules 6, 8, 9) ----------------------------------------------------------------

    [Test]
    public void Tick_CastsWhenTheTriggerIsMet()
    {
        _runner.Add(Active(ActiveA));

        _combat.Blackboard.HpFraction = 0.5f;

        _runner.Tick(Frame, 1f);

        SkillCast cast = _events.Single<SkillCast>();

        Assert.That(cast.SkillId, Is.EqualTo(Id(ActiveA)));
        Assert.That(cast.WasAuto, Is.True);
        Assert.That(cast.Cooldown, Is.EqualTo(ActiveCooldown).Within(0.0001f));

        // The OnCast effect is in force: +50 % weapon damage on a 13 damage class.
        Assert.That(_stats.Resolve(PlayerStat.WeaponDamage).Value, Is.EqualTo(WeaponDamage * 1.5f).Within(0.01f));

        Assert.That(_runner.IsReady(0), Is.False);
    }

    [Test]
    public void Tick_DoesNotCastWhenItIsNot()
    {
        _runner.Add(Active(ActiveA));

        _combat.Blackboard.HpFraction = 0.7f;

        _runner.Tick(Frame, 1f);

        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(0));
        Assert.That(_stats.Resolve(PlayerStat.WeaponDamage).ModifierCount, Is.EqualTo(0));
        Assert.That(_runner.IsReady(0), Is.True);
    }

    [Test]
    public void Tick_DoesNotCastWhileCooling()
    {
        _runner.Add(Active(ActiveA));

        _combat.Blackboard.HpFraction = 0.5f;

        _runner.Tick(Frame, 0f);

        _events.Clear();

        // One second into an eight second cooldown, with the trigger still met. The trigger holding
        // is the point: a row where it had lapsed would pass with the cooldown deleted.
        _runner.Tick(Frame, 1f);

        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(0));
    }

    [Test]
    public void Tick_CastsAgainAfterTheCooldown()
    {
        _runner.Add(Active(ActiveA));

        _combat.Blackboard.HpFraction = 0.5f;

        _runner.Tick(Frame, 0f);

        _events.Clear();

        _runner.Tick(Frame, ActiveCooldown + 0.1f);

        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(1));
    }

    [Test]
    public void Tick_CastsAtMostOnePerTick()
    {
        _runner.Add(Active(ActiveA));
        _runner.Add(Active(ActiveB));

        _combat.Blackboard.HpFraction = 0.5f;

        _runner.Tick(Frame, 1f);

        // **Exactly one, and the earlier-added one** (rule 6). Four skills detonating on one frame
        // is the opposite of the deliberate CH §4.2 asks Auto to feel, and take order is what makes
        // which one predictable.
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(1));
        Assert.That(_events.Single<SkillCast>().SkillId, Is.EqualTo(Id(ActiveA)));

        Assert.That(_runner.IsReady(1), Is.True, "The second is untouched rather than merely unpublished.");
    }

    [Test]
    public void Tick_ReachesTheSecondOnTheNextTick()
    {
        _runner.Add(Active(ActiveA));
        _runner.Add(Active(ActiveB));

        _combat.Blackboard.HpFraction = 0.5f;

        _runner.Tick(Frame, 1f);
        _events.Clear();

        // No starvation: the first is on cooldown now, so the walk reaches the second on the very
        // next tick. Four ready-and-triggered actives are therefore all away within four frames,
        // ≈ 50 ms at 60 fps, which is below anything a player can time.
        _runner.Tick(Frame, 1f + Frame);

        Assert.That(_events.Single<SkillCast>().SkillId, Is.EqualTo(Id(ActiveB)));
    }

    [Test]
    public void Tick_NaNBlackboardCastsNothing()
    {
        _runner.Add(Active(ActiveA));

        // M3-02a rule 6 from this side: every comparison against NaN is false, so `value < t` and
        // `value >= t` are both false and a broken blackboard casts nothing — where the tempting
        // spelling of AtLeast would have fired every active the player owns at once.
        _combat.Blackboard.HpFraction = float.NaN;

        _runner.Tick(Frame, 1f);

        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(0));
    }

    [Test]
    public void Tick_NonFiniteNowCastsNothing()
    {
        _runner.Add(Active(ActiveA));

        _combat.Blackboard.HpFraction = 0.5f;

        // **The non-finite row this float door owes, and it is not a throw.** The snapshot is the
        // door — Dt is clamped by SnapshotBuilder and RunState.Time is its sum (AR §18.2) — so this
        // trusts the clock like every other Tick in core. What it does instead is spell the
        // readiness test `!(now >= ready)`, so an unreadable clock takes the *cooling* branch: the
        // natural spelling would cast, schedule a NaN _readyAt, and fire every tick for the rest of
        // the run.
        _runner.Tick(Frame, float.NaN);

        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(0));

        // And the entry is untouched, so a clock that recovers finds a skill that never fired.
        Assert.That(_runner.IsReady(0), Is.False, "A NaN clock reads as not-ready rather than as ready.");

        _runner.Tick(Frame, 1f);

        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(1));
    }

    // ---- Casting (rules 1, 4, 8, 9) --------------------------------------------------------------

    [Test]
    public void Cast_SourceIsTheActiveSpecNotTheSkill()
    {
        // **Rule 8, and the row has to show two *different* sources rather than two modifiers.**
        // A node that both grants a passive and buffs on cast needs the two separable, because
        // Stat.RemoveAll(source) takes a whole source back at once (M3-05 rule 6) — and a row that
        // only counted modifiers would pass against a runner that passed the SkillSpec.
        SkillSpec node = ActiveThatAlsoTakes(ActiveA);

        SkillTree tree = TreeHolding(node);

        tree.Take(node.Id);

        _runner.Add(node);

        _combat.Blackboard.HpFraction = 0.5f;

        _runner.Tick(Frame, 1f);

        Stat damage = _stats.Resolve(PlayerStat.WeaponDamage);
        var modifiers = new List<Modifier>();

        damage.CopyModifiersTo(modifiers);

        Assert.That(modifiers.Count, Is.EqualTo(2), "One from the take, one from the cast.");

        Assert.That(
            modifiers[0].Source,
            Is.SameAs(node),
            "SkillTree.Take passes the SkillSpec (M3-03 rule 4).");

        Assert.That(
            modifiers[1].Source,
            Is.SameAs(node.Active),
            "And the cast passes the ActiveSpec — 'whoever owns the effect' (M3-05 rule 7).");

        Assert.That(
            modifiers[0].Source,
            Is.Not.SameAs(modifiers[1].Source),
            "Which is the whole row: two sources, so the cast can be taken back on its own.");

        // And it can be: taking the ActiveSpec back leaves the take's modifier standing.
        int removed = damage.RemoveAll(node.Active);

        Assert.That(removed, Is.EqualTo(1));
        Assert.That(damage.ModifierCount, Is.EqualTo(1));
        Assert.That(damage.Value, Is.EqualTo(WeaponDamage * 1.1f).Within(0.01f));
    }

    [Test]
    public void Cast_UsesTheEffectiveCooldown()
    {
        _runner.Add(Active(ActiveA));

        // −90 % on an 8 s skill is 0.8 s raw, and the floor is 3.2. The event carries the effective
        // number rather than the authored one, because that is what a radial fill is over.
        _runner.CooldownOf(0).Add(new Modifier(ModifierKind.PercentMult, -0.9f, "stack"));

        Assert.That(_runner.CooldownOf(0).Value, Is.EqualTo(0.8f).Within(0.0001f));

        _combat.Blackboard.HpFraction = 0.5f;

        _runner.Tick(Frame, 0f);

        Assert.That(_events.Single<SkillCast>().Cooldown, Is.EqualTo(3.2f).Within(0.0001f));

        // Asked through Cast rather than by ticking to each moment, because a Tick that found the
        // skill ready would cast and move _readyAt out from under the next assertion.
        Assert.That(
            _runner.Cast(0, 3.1f, auto: false),
            Is.False,
            "Still cooling at 3.1 s — the floor held it to 3.2 rather than letting it be 0.8.");

        Assert.That(_runner.Cast(0, 3.2f, auto: false), Is.True);
    }

    [Test]
    public void Runner_EffectiveCooldownIsFloored()
    {
        _runner.Add(Active(ActiveA));

        Assert.That(
            _runner.EffectiveCooldownOf(0),
            Is.EqualTo(ActiveCooldown).Within(0.0001f),
            "The fixture's premise: an untouched cooldown comes back as itself.");

        // −93.75 % on an 8 s skill is 0.5 s raw. **What the Skills screen prints is 3.2** — what
        // the wait actually is after CH §4.1's floor, not what the stack asked for (M3-09b rule 3).
        // The alternative was the screen applying the floor itself, which is the second copy of
        // CooldownRules that M3-06 rule 1 exists to prevent.
        _runner.CooldownOf(0).Add(new Modifier(ModifierKind.PercentMult, -0.9375f, "stack"));

        Assert.That(
            _runner.CooldownOf(0).Value,
            Is.EqualTo(0.5f).Within(0.0001f),
            "The fixture's premise: the stack really did drive the live value to half a second.");

        Assert.That(
            _runner.EffectiveCooldownOf(0),
            Is.EqualTo(3.2f).Within(0.0001f),
            "EffectiveCooldownOf handed back the raw stack value rather than the floored one.");

        // **And it is the same number the cast schedules against**, which is the whole reason this
        // is one member rather than an expression the screen keeps its own copy of.
        _combat.Blackboard.HpFraction = 0.5f;

        _runner.Tick(Frame, 0f);

        Assert.That(_events.Single<SkillCast>().Cooldown, Is.EqualTo(_runner.EffectiveCooldownOf(0)));

        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.EffectiveCooldownOf(1));
    }

    [Test]
    public void State_ExposesCooldownSeconds()
    {
        StartSession(Below(TriggerField.HpFraction, 0f), cooldown: 2.5f);

        // CC §6.3 asks for "its cooldown" on a screen where the tick is gated, so a radial fill
        // would be a frozen ring saying nothing (M3-09b rule 3). The number is the readable form,
        // and it is the whole wait rather than what is left of it — SkillCooldownFraction is the
        // other question and answers zero here.
        Assert.That(_session.State.SkillCooldownSeconds(0), Is.EqualTo(2.5f).Within(0.0001f));
        Assert.That(_session.State.SkillCooldownFraction(0), Is.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => _session.State.SkillCooldownSeconds(1));

        // **And the seal did not move to let it out** (AR §18.2, M3-06 rule 13). A new read on
        // RunState is one more scalar, not a handle: SkillRunner.SetAutoCast and CastSkill are both
        // public on the runner, so a public property here would let a view fire the player's skills
        // and rearrange their thumb. Asked by reflection because the compiler cannot be asked — a
        // test in Soulvail.Tests.Core sees `internal` through the assembly's InternalsVisibleTo.
        PropertyInfo handle = typeof(RunState).GetProperty(
            "Skills",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(handle, Is.Not.Null, "RunState.Skills is gone — this row is out of date.");

        Assert.That(
            handle.GetMethod.IsPublic,
            Is.False,
            "RunState.Skills became public. M3-09b added a read beside it and must not have "
                + "loosened the seal to do it.");
    }

    [Test]
    public void State_SlotReadsAnswerByPosition()
    {
        // A trigger that can never be met, so the only thing that casts here is the player.
        StartSession(Below(TriggerField.HpFraction, 0f), cooldown: 4f);

        var commands = (IPlayerCommands)_session;

        commands.SetAutoCast(Id(ActiveA), auto: false);

        Assert.That(
            _session.State.ManualSlotAt(0),
            Is.EqualTo(Id(ActiveA)),
            "The fixture's premise: the skill is in S1.");

        commands.CastSkill(0);

        // **Addressed by *slot*, which is what a button is** (M3-10a rule 2). SkillCooldownFraction
        // is addressed by the runner's walk order, and a button knows only which thumb position it
        // is — so a screen mapping one to the other would be re-deriving what TryIndexOf already
        // answers, in the presentation layer.
        Assert.That(_session.State.SlotCooldownFraction(0), Is.GreaterThan(0f));
        Assert.That(_session.State.IsSlotReady(0), Is.False);

        // And S2 is empty, which answers 0 and false rather than throwing — unlike CastSlot, which
        // throws for an empty slot (M3-07a rule 4). A *read* of an empty slot is what a button does
        // on every frame it is not drawn.
        Assert.That(_session.State.ManualSlotAt(1), Is.EqualTo(default(ContentId)));
        Assert.That(_session.State.SlotCooldownFraction(1), Is.Zero);
        Assert.That(_session.State.IsSlotReady(1), Is.False);

        // **And the seal did not move to let them out** (AR §18.2, M3-09b's row one screen back).
        // Two more scalars, not a handle: SetAutoCast and CastSlot are both public on the runner.
        PropertyInfo handle = typeof(RunState).GetProperty(
            "Skills",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(handle, Is.Not.Null, "RunState.Skills is gone — this row is out of date.");

        Assert.That(
            handle.GetMethod.IsPublic,
            Is.False,
            "RunState.Skills became public. M3-10a added two reads beside it and must not have "
                + "loosened the seal to do it.");
    }

    [Test]
    public void State_SlotReadsWithNoTree()
    {
        // Every run in the build until M3-12 authors one: TryGetTreeFor answers false, RunState.Tree
        // is null, and the runner owns nothing — so all four slots are empty and stay empty.
        StartTreelessSession();

        Assert.That(_session.State.OwnedActiveCount, Is.Zero, "The fixture's premise.");

        for (int slot = 0; slot < SkillRunner.MaxManualSlots; slot++)
        {
            Assert.That(
                _session.State.SlotCooldownFraction(slot),
                Is.Zero,
                $"S{slot + 1} answered a cooldown for a run with no skills in it.");

            Assert.That(
                _session.State.IsSlotReady(slot),
                Is.False,
                $"S{slot + 1} reported itself live with nothing in it, so its button would be "
                    + "drawn bright and send a command core refuses.");
        }
    }

    [Test]
    public void State_SlotReadsOutOfRange_Throw()
    {
        StartTreelessSession();

        // **A bad *slot* is loud where an empty one is quiet**, and the two are different mistakes:
        // an empty slot is a legal state the button reads every frame, while slot 4 is a screen
        // addressing a button CC §6.2 does not draw. SlotAt is the one place that refuses it, which
        // is why both reads refuse it identically.
        Assert.Throws<ArgumentOutOfRangeException>(() => _session.State.SlotCooldownFraction(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _session.State.SlotCooldownFraction(SkillRunner.MaxManualSlots));

        Assert.Throws<ArgumentOutOfRangeException>(() => _session.State.IsSlotReady(-1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _session.State.IsSlotReady(SkillRunner.MaxManualSlots));
    }

    [Test]
    public void Cast_FractionIsOneOnTheCastTick()
    {
        // **Rule 9's ordering, observed from inside the publish.** The event is last — after the
        // effects and after _readyAt moved — so a handler reading the fill sees 1 rather than 0.
        float seen = -1f;

        var watching = new WatchingEvents(null);
        var runner = new SkillRunner(_registry, _combat.Blackboard, watching);

        runner.Add(Active(ActiveA));

        watching.OnPublish = _ => seen = runner.CooldownFraction(0);

        _combat.Blackboard.HpFraction = 0.5f;

        runner.Tick(Frame, 1f);

        Assert.That(seen, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void Cast_MidCooldownBuffMovesTheFillNotTheWait()
    {
        // **Rule 4's bargain, and the spec's own word for the direction is the wrong way round.**
        // The wait is sampled at the start of the cast and held; the fill is measured against the
        // *current* effective value. So halving the cooldown mid-wait moves the fill — upward,
        // because the same remaining seconds are a larger share of a shorter cooldown — while
        // IsReady still answers at the original moment. ChargeSkill.CooldownFraction is the same
        // paragraph, and M3-10 draws both fills side by side.
        _runner.Add(Active(ActiveA));

        _combat.Blackboard.HpFraction = 0.5f;

        _runner.Tick(Frame, 0f);
        _runner.Tick(Frame, 6f);

        Assert.That(_runner.CooldownFraction(0), Is.EqualTo(0.25f).Within(0.0001f), "2 s of 8.");

        _runner.CooldownOf(0).Add(new Modifier(ModifierKind.PercentMult, -0.5f, "buff"));

        Assert.That(_runner.CooldownFraction(0), Is.EqualTo(0.5f).Within(0.0001f), "2 s of 4.");

        // **And the wait did not move.** Six seconds have already passed, so a runner that re-based
        // _readyAt from the live stat would be ready this instant.
        Assert.That(_runner.IsReady(0), Is.False);

        // Asked through Cast rather than by ticking to each moment, because a Tick that found the
        // skill ready would cast and move _readyAt out from under the next assertion.
        Assert.That(_runner.Cast(0, 7.9f, auto: false), Is.False);

        Assert.That(
            _runner.Cast(0, 8f, auto: false),
            Is.True,
            "At the original eight seconds, not at the four the buff would have made it.");
    }

    [Test]
    public void Cast_Directly_IgnoresTheTrigger()
    {
        _runner.Add(Active(ActiveA));

        // The door M3-07a's manual cast uses. The trigger is deliberately not met — a Manual skill
        // fires because the player asked, and its authored condition is what Auto reads.
        _combat.Blackboard.HpFraction = 0.9f;

        Assert.That(_runner.Cast(0, 1f, auto: false), Is.True);

        SkillCast cast = _events.Single<SkillCast>();

        Assert.That(cast.SkillId, Is.EqualTo(Id(ActiveA)));
        Assert.That(cast.WasAuto, Is.False, "Which is what lets CC §6.2 give this one a haptic.");
    }

    [Test]
    public void Cast_Directly_WhileCooling_IsFalse()
    {
        _runner.Add(Active(ActiveA));

        Assert.That(_runner.Cast(0, 0f, auto: false), Is.True);

        _events.Clear();

        Assert.That(_runner.Cast(0, 1f, auto: false), Is.False);
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(0));

        // Nothing applied either: the second cast must not stack another buff on a skill it refused.
        Assert.That(_stats.Resolve(PlayerStat.WeaponDamage).ModifierCount, Is.EqualTo(1));
    }

    [Test]
    public void Cast_NonFiniteNow_IsFalse()
    {
        _runner.Add(Active(ActiveA));

        // `!(now >= ready)` again, from the manual door: an unreadable clock is refused rather than
        // waved through, and nothing is applied and nothing published.
        Assert.That(_runner.Cast(0, float.NaN, auto: false), Is.False);

        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(0));
        Assert.That(_stats.Resolve(PlayerStat.WeaponDamage).ModifierCount, Is.EqualTo(0));
    }

    // ---- Auto/Manual and the four slots (M3-07a rules 1, 2, 3, 7, 9) -----------------------------

    [Test]
    public void Added_SkillIsAuto()
    {
        _runner.Add(Active(ActiveA));

        // CC §6.1: "a player who never opens the menu has a complete, playable game with one
        // button." Auto is therefore the state a skill arrives in, not one it is put into.
        Assert.That(_runner.IsAuto(Id(ActiveA)), Is.True);
        Assert.That(_runner.ManualSlotCount, Is.Zero);
        Assert.That(_runner.SlotAt(0), Is.EqualTo(default(ContentId)));
    }

    [Test]
    public void SetManual_TakesTheLowestFreeSlot()
    {
        _runner.Add(Active(ActiveA));
        _runner.Add(Active(ActiveB));
        _runner.Add(Active(ActiveThird));

        // A and then C, skipping B — so the row fails on a runner that assigned by runner index
        // rather than by the lowest free slot, which would put C in slot 2.
        _runner.SetAutoCast(Id(ActiveA), auto: false);
        _runner.SetAutoCast(Id(ActiveThird), auto: false);

        Assert.That(_runner.SlotAt(0), Is.EqualTo(Id(ActiveA)));
        Assert.That(_runner.SlotAt(1), Is.EqualTo(Id(ActiveThird)));
        Assert.That(_runner.SlotAt(2), Is.EqualTo(default(ContentId)));
        Assert.That(_runner.ManualSlotCount, Is.EqualTo(2));

        Assert.That(_runner.IsAuto(Id(ActiveA)), Is.False);
        Assert.That(_runner.IsAuto(Id(ActiveB)), Is.True, "Never asked for, never moved.");
    }

    [Test]
    public void SetAuto_EmptiesTheSlotAndMovesNothing()
    {
        ManualThree();

        _runner.SetAutoCast(Id(ActiveB), auto: true);

        // **Rule 3's whole point.** CC §6.2 draws four fixed thumb positions, so compacting would
        // slide C from slot 2 into slot 1 — a silent re-bind of the muscle memory a player built,
        // handed to them as the reward for dropping a skill.
        Assert.That(_runner.SlotAt(0), Is.EqualTo(Id(ActiveA)));
        Assert.That(_runner.SlotAt(1), Is.EqualTo(default(ContentId)), "The hole stays a hole.");
        Assert.That(_runner.SlotAt(2), Is.EqualTo(Id(ActiveThird)), "And C did not move.");
        Assert.That(_runner.ManualSlotCount, Is.EqualTo(2));
    }

    [Test]
    public void SetManual_RefillsTheHole()
    {
        ManualThree();

        _runner.SetAutoCast(Id(ActiveB), auto: true);

        _runner.Add(Active(ActiveFourth));
        _runner.SetAutoCast(Id(ActiveFourth), auto: false);

        // The lowest *free* slot, which is the hole rather than the end — so a runner appending at
        // ManualSlotCount would put D in slot 2 and lose the one CC §6.2 left empty.
        Assert.That(_runner.SlotAt(1), Is.EqualTo(Id(ActiveFourth)));
        Assert.That(_runner.SlotAt(2), Is.EqualTo(Id(ActiveThird)));
        Assert.That(_runner.ManualSlotCount, Is.EqualTo(3));
    }

    [Test]
    public void SetManual_FifthThrowsNamingTheCeiling()
    {
        ManualFour();

        _runner.Add(Active(ActiveFifth));

        _events.Clear();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => _runner.SetAutoCast(Id(ActiveFifth), auto: false));

        // The message has to carry both numbers, because the screen that hit this is the one that
        // did not ask ManualSlotCount first — and CC §6.2's "Manual slots full — which skill goes
        // back to auto?" is what it should have shown instead of sending the command.
        Assert.That(thrown.Message, Does.Contain("4"));
        Assert.That(thrown.Message, Does.Contain("ManualSlotCount"));

        // **Nothing moved and nothing was published.** A refusal that had already half-applied
        // itself would leave the loadout describing a state the player never chose.
        Assert.That(_runner.ManualSlotCount, Is.EqualTo(4));
        Assert.That(_runner.IsAuto(Id(ActiveFifth)), Is.True);
        Assert.That(_events.Count<SkillAutoCastChanged>(), Is.Zero);
    }

    [Test]
    public void SetManual_AfterFreeingASlot_Succeeds()
    {
        ManualFour();

        _runner.Add(Active(ActiveFifth));

        // **CC §6.2's answer, and it needs no mechanism of its own** (rule 2): the screen asks which
        // skill goes back to Auto and then sends two ordinary commands — the victim, then the one
        // the player actually wanted. Core never swaps anything.
        _runner.SetAutoCast(Id(ActiveB), auto: true);
        _runner.SetAutoCast(Id(ActiveFifth), auto: false);

        Assert.That(_runner.SlotAt(1), Is.EqualTo(Id(ActiveFifth)), "It took the freed slot.");
        Assert.That(_runner.ManualSlotCount, Is.EqualTo(4));
    }

    [Test]
    public void SetManual_Idempotent_TakesNoSecondSlot()
    {
        // **The dangerous half of rule 9, which names only the `true` case.** Manual twice must not
        // take a second slot: one skill in two of them puts ManualSlotCount at 4 with two skills
        // owned, and the ceiling then throws for a reason no screen can explain.
        _runner.Add(Active(ActiveA));

        _runner.SetAutoCast(Id(ActiveA), auto: false);

        _events.Clear();

        _runner.SetAutoCast(Id(ActiveA), auto: false);

        Assert.That(_runner.ManualSlotCount, Is.EqualTo(1));
        Assert.That(_runner.SlotAt(1), Is.EqualTo(default(ContentId)));
        Assert.That(_events.Count<SkillAutoCastChanged>(), Is.Zero, "Nothing happened to describe.");
    }

    [Test]
    public void SetAutoCast_UnknownSkill_Throws()
    {
        _runner.Add(Active(ActiveA));

        // Rule 7. A Passive, an Upgrade and a Keystone never reach this class at all, so
        // "passive skills have no toggle and no button" needs no check of its own — and an id the
        // runner does not hold is a screen addressing a skill this run never took.
        ContentId stranger = Id("skill.test.stranger");

        Assert.That(
            Assert.Throws<KeyNotFoundException>(() => _runner.SetAutoCast(stranger, auto: false))
                .Message,
            Does.Contain("skill.test.stranger"));

        // The read is loud for the same reason: answering `true` would describe a skill that does
        // not exist as one that auto-casts.
        Assert.Throws<KeyNotFoundException>(() => _runner.IsAuto(stranger));
    }

    [Test]
    public void SetAutoCast_Idempotent_PublishesNothing()
    {
        _runner.Add(Active(ActiveA));

        _events.Clear();

        _runner.SetAutoCast(Id(ActiveA), auto: true);

        // AR §8: an event describes what happened, and nothing happened.
        Assert.That(_events.Count<SkillAutoCastChanged>(), Is.Zero);
        Assert.That(_runner.IsAuto(Id(ActiveA)), Is.True);
        Assert.That(_runner.ManualSlotCount, Is.Zero);
    }

    [Test]
    public void SetAutoCast_PublishesTheSlot()
    {
        _runner.Add(Active(ActiveA));

        _events.Clear();

        _runner.SetAutoCast(Id(ActiveA), auto: false);
        _runner.SetAutoCast(Id(ActiveA), auto: true);

        IReadOnlyList<SkillAutoCastChanged> moved = _events.Of<SkillAutoCastChanged>();

        Assert.That(moved.Count, Is.EqualTo(2));

        Assert.That(moved[0].SkillId, Is.EqualTo(Id(ActiveA)));
        Assert.That(moved[0].IsAuto, Is.False);
        Assert.That(moved[0].Slot, Is.Zero);

        // −1 rather than the slot it just left: the event says where the skill *is*, and an Auto
        // skill is in no slot. M3-09's list and M3-10's buttons both redraw from this and need no
        // second read.
        Assert.That(moved[1].IsAuto, Is.True);
        Assert.That(moved[1].Slot, Is.EqualTo(-1));
    }

    [Test]
    public void Slots_AreAlwaysFourLong()
    {
        _runner.Add(Active(ActiveA));

        Assert.That(_runner.Slots.Count, Is.EqualTo(SkillRunner.MaxManualSlots));

        for (int slot = 0; slot < _runner.Slots.Count; slot++)
        {
            Assert.That(_runner.Slots[slot], Is.EqualTo(default(ContentId)));
        }

        _runner.SetAutoCast(Id(ActiveA), auto: false);

        // Still four, with the empties still in it: a compacted list could not say *which* three
        // are empty, and CC §6.2 draws S1–S4 at fixed positions. This is the shape M3-07b writes.
        Assert.That(_runner.Slots.Count, Is.EqualTo(SkillRunner.MaxManualSlots));
        Assert.That(_runner.Slots[0], Is.EqualTo(Id(ActiveA)));
        Assert.That(_runner.Slots[3], Is.EqualTo(default(ContentId)));
    }

    [Test]
    public void Slots_IsTheSameInstanceEveryCall()
    {
        // **The identity pin the allocation row cannot make on its own.** A per-call
        // `Array.AsReadOnly` would allocate on the path M3-10's HUD polls every frame, and
        // ReferenceEquals fails on it loudly where an allocation probe would have to be widened to
        // notice. TriggerSpec._clausesView and SkillTree._takenIdsView are the idiom.
        _runner.Add(Active(ActiveA));

        Assert.That(ReferenceEquals(_runner.Slots, _runner.Slots), Is.True);

        IReadOnlyList<ContentId> before = _runner.Slots;

        _runner.SetAutoCast(Id(ActiveA), auto: false);

        Assert.That(ReferenceEquals(before, _runner.Slots), Is.True, "And a write does not re-wrap.");

        // And it is a view rather than a copy: the write above is visible through the reference
        // taken before it.
        Assert.That(before[0], Is.EqualTo(Id(ActiveA)));
    }

    [Test]
    public void SlotAt_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.SlotAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.SlotAt(SkillRunner.MaxManualSlots));
    }

    // ---- What Manual costs the tick (M3-07a rules 4, 5, 8) ---------------------------------------

    [Test]
    public void Manual_NeverAutoCasts()
    {
        // A trigger that is met on every tick of a healthy run, so the only thing that can explain
        // silence is the skip.
        _runner.Add(Active(ActiveA, trigger: AtLeast(TriggerField.HpFraction, 0f)));

        _runner.SetAutoCast(Id(ActiveA), auto: false);

        _events.Clear();

        for (int i = 0; i < 100; i++)
        {
            _runner.Tick(Frame, i * Frame);
        }

        Assert.That(_events.Count<SkillCast>(), Is.Zero);
    }

    [Test]
    public void Manual_TriggerIsNotEvaluated()
    {
        // **Rule 5's *ordering*, which `Manual_NeverAutoCasts` above cannot see.** That row stays
        // green with the skip placed *below* the trigger test — the skill still never fires, and the
        // condition is still paid for on every tick, which is exactly what CC §6.5 says switching to
        // Manual buys you out of.
        //
        // There is no counting fake to write: TriggerSpec is sealed with a non-virtual IsMet and no
        // interface, TriggerClause is a readonly struct, and CombatBlackboard is sealed. So the
        // clause is made *unreadable* instead — TriggerClause's constructor validates only the
        // threshold, never the field, so an out-of-range field constructs fine and IsMet's loud
        // `default` throws the instant anything reads it (Traps §7).
        _runner.Add(Active(ActiveA, trigger: Unreadable()));

        // **The control, and without it this row proves nothing**: left Auto, the walk reaches the
        // clause on the first tick and says so. A row that only asserted the silence below would be
        // green against a runner that never reached the trigger for any reason at all.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _runner.Tick(Frame, 0f),
            "Auto: the trigger is read, so the unreadable clause is reached.");

        _runner.SetAutoCast(Id(ActiveA), auto: false);

        // A Manual skill never casts, so _readyAt stays 0 and the cooldown `continue` never fires —
        // which leaves the IsAuto skip as the only thing that can explain a hundred silent ticks.
        Assert.DoesNotThrow(
            () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    _runner.Tick(Frame, i * Frame);
                }
            },
            "Manual: the trigger is never asked, so the unreadable clause is never reached.");
    }

    [Test]
    public void Auto_StillCastsWhileAnotherIsManual()
    {
        _runner.Add(Active(ActiveA));
        _runner.Add(Active(ActiveB));

        _runner.SetAutoCast(Id(ActiveA), auto: false);

        // Both triggered. A is first in the walk order and is Manual, so a runner that `return`ed on
        // the skip instead of `continue`ing would leave B silent for ever.
        _combat.Blackboard.HpFraction = 0.5f;

        _events.Clear();

        _runner.Tick(Frame, 1f);

        Assert.That(_events.Single<SkillCast>().SkillId, Is.EqualTo(Id(ActiveB)));
    }

    [Test]
    public void Switch_KeepsTheRunningCooldown()
    {
        // **Rule 8: a switch costs nothing.** CC §6.3 wants the management screen usable during
        // play, and a switch that reset — or even nudged — a cooldown would make opening it a
        // tactical decision instead.
        _runner.Add(Active(ActiveA));

        _combat.Blackboard.HpFraction = 0.5f;

        _runner.Tick(Frame, 0f);

        Assert.That(_events.Single<SkillCast>().Cooldown, Is.EqualTo(ActiveCooldown).Within(0.0001f));

        _runner.Tick(Frame, 2f);

        Assert.That(_runner.CooldownFraction(0), Is.EqualTo(0.75f).Within(0.0001f), "6 s of 8.");

        _runner.SetAutoCast(Id(ActiveA), auto: false);

        // Continuous across the switch, not restarted and not zeroed.
        Assert.That(_runner.CooldownFraction(0), Is.EqualTo(0.75f).Within(0.0001f));

        _runner.SetAutoCast(Id(ActiveA), auto: true);

        Assert.That(_runner.CooldownFraction(0), Is.EqualTo(0.75f).Within(0.0001f));

        // Probed through Cast rather than by ticking to the moment, because a Tick that found the
        // skill ready would cast and move _readyAt out from under the next assertion
        // (Cast_UsesTheEffectiveCooldown's lesson, twice over in M3-06).
        Assert.That(_runner.Cast(0, 7.9f, auto: false), Is.False, "Still the original eight.");
        Assert.That(_runner.Cast(0, 8f, auto: false), Is.True);
    }

    // ---- Casting from a slot (M3-07a rule 4) -----------------------------------------------------

    [Test]
    public void CastSlot_FiresRegardlessOfTheTrigger()
    {
        _runner.Add(Active(ActiveA));
        _runner.SetAutoCast(Id(ActiveA), auto: false);

        // Deliberately not met: a Manual skill fires because the player asked, and its authored
        // condition is what Auto reads.
        _combat.Blackboard.HpFraction = 0.9f;

        _events.Clear();

        Assert.That(_runner.CastSlot(0, 1f), Is.True);

        SkillCast cast = _events.Single<SkillCast>();

        Assert.That(cast.SkillId, Is.EqualTo(Id(ActiveA)));
        Assert.That(cast.WasAuto, Is.False, "Which is what lets CC §6.2 give this one a haptic.");
    }

    [Test]
    public void CastSlot_WhileCooling_IsFalse()
    {
        _runner.Add(Active(ActiveA));
        _runner.SetAutoCast(Id(ActiveA), auto: false);

        Assert.That(_runner.CastSlot(0, 0f), Is.True);

        _events.Clear();

        // An ordinary early tap rather than an error: CC §6.2 answers it with 40 % opacity and no
        // tap response rather than with a buffer (M3-06's Out of scope argues why the dash's does
        // not generalise).
        Assert.That(_runner.CastSlot(0, 1f), Is.False);

        Assert.That(_events.Count<SkillCast>(), Is.Zero);
        Assert.That(
            _stats.Resolve(PlayerStat.WeaponDamage).ModifierCount,
            Is.EqualTo(1),
            "And nothing applied: the refused cast must not stack a second buff.");
    }

    [Test]
    public void CastSlot_Empty_Throws()
    {
        _runner.Add(Active(ActiveA));
        _runner.SetAutoCast(Id(ActiveA), auto: false);

        // CC §6.2 draws no button for an empty slot, so a command from one is a view sending for a
        // control it is not drawing — IPlayerCommands' standing rule, "a silent no-op would hide
        // it". Distinct from cooling, which is the legitimate `false` above.
        Assert.Throws<InvalidOperationException>(() => _runner.CastSlot(2, 1f));
    }

    [Test]
    public void CastSlot_OutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _runner.CastSlot(-1, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _runner.CastSlot(SkillRunner.MaxManualSlots, 1f));
    }

    [Test]
    public void CastSlot_NonFiniteNow_IsFalse()
    {
        // **The non-finite row the new float door owes, and it is trusted rather than guarded** —
        // M3-06's answer, with the safety in the spelling. `Cast`'s `!(now >= _readyAt)` is what
        // makes an unreadable clock take the refusing branch, so this door needs no check of its
        // own: the snapshot is the door (AR §18.2) and every other Tick in core trusts its clock.
        _runner.Add(Active(ActiveA));
        _runner.SetAutoCast(Id(ActiveA), auto: false);

        _events.Clear();

        Assert.That(_runner.CastSlot(0, float.NaN), Is.False);

        Assert.That(_events.Count<SkillCast>(), Is.Zero);
        Assert.That(_stats.Resolve(PlayerStat.WeaponDamage).ModifierCount, Is.Zero);
    }

    // ---- The address, the reset and the seal (rules 11, 12, 13) ----------------------------------

    [Test]
    public void Runner_ExposesTheCooldownAddress()
    {
        _runner.Add(Active(ActiveA));
        _runner.Add(Active(ActiveB));
        _runner.Add(Active(ActiveThird));

        Assert.That(_runner.TryIndexOf(Id(ActiveB), out int index), Is.True);
        Assert.That(index, Is.EqualTo(1));

        Assert.That(_runner.TryIndexOf(Id("skill.test.stranger"), out int missing), Is.False);
        Assert.That(missing, Is.EqualTo(-1));

        // **The very Stat the entry holds, by reference.** M3-12's ModifySkillCooldown handler
        // resolves an address and puts a modifier on what it finds; a copy would take the modifier
        // and leave the skill untouched, which is the failure an equality check cannot see.
        Stat resolved = _runner.CooldownOf(index);

        resolved.Add(new Modifier(ModifierKind.PercentAdd, -0.15f, "node"));

        Assert.That(_runner.CooldownOf(index), Is.SameAs(resolved));
        Assert.That(_runner.CooldownOf(index).Value, Is.EqualTo(ActiveCooldown * 0.85f).Within(0.001f));
    }

    [Test]
    public void Reset_ClearsTheClocksAndKeepsTheSkills()
    {
        _runner.Add(Active(ActiveA));

        _runner.CooldownOf(0).Add(new Modifier(ModifierKind.PercentAdd, -0.15f, "node"));

        _runner.Cast(0, 1f, auto: true);

        Assert.That(_runner.IsReady(0), Is.False);

        _runner.Reset();

        // A fresh cooldown clock, not a stripped character: the entry stays and so does everything
        // on its stack, because this class put none of it there (ChargeSkill.Reset's reason).
        // Nothing in M3 calls this — Session_BoundaryLeavesCooldownsRunning is the row that says a
        // stage boundary is not one of its callers.
        Assert.That(_runner.Count, Is.EqualTo(1));
        Assert.That(_runner.IdAt(0), Is.EqualTo(Id(ActiveA)));
        Assert.That(_runner.CooldownOf(0).ModifierCount, Is.EqualTo(1));

        // A blackboard the trigger does not like, so this tick moves the clock without casting —
        // the default HpFraction is 0, which satisfies `Below 0.6` and would fire the skill again.
        _combat.Blackboard.HpFraction = 0.9f;

        _runner.Tick(Frame, 1f);

        Assert.That(_runner.IsReady(0), Is.True);
    }

    [Test]
    public void State_HandsOutNoRunner()
    {
        // **AR §18.2 for the eighth time.** Add and Cast are both public on the runner, so a public
        // handle here would let a view grant the player a skill or fire one. The four reads below it
        // are what M3-10's buttons need, and they are all this hands out.
        PropertyInfo skills = typeof(RunState).GetProperty(
            "Skills",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.That(skills, Is.Not.Null, "The property exists — this row is about its accessibility.");
        Assert.That(skills.GetMethod.IsPublic, Is.False);

        Assert.That(
            typeof(RunState).GetProperty("Skills", BindingFlags.Public | BindingFlags.Instance),
            Is.Null,
            "And it is not reachable as a public member from outside core.");
    }

    // ---- Allocation (rule 10) --------------------------------------------------------------------

    [Test]
    public void Tick_AllocatesNothing()
    {
        // Seven actives — a full 27-node tree at CH §4's ~25 % Active — and none of them triggered,
        // which is what a frame of a fight actually looks like. Fourteen float comparisons and no
        // list anywhere.
        var runner = new SkillRunner(_registry, _combat.Blackboard, new SilentEvents());

        for (int i = 0; i < 7; i++)
        {
            runner.Add(Active($"skill.test.seven{i}"));
        }

        _combat.Blackboard.HpFraction = 0.9f;

        AllocationAssert.None(() => runner.Tick(Frame, 1f));
    }

    [Test]
    public void Cast_AllocatesNothing()
    {
        // A silent IDomainEvents rather than RecordingEvents, which stores each payload in a
        // List<object> and would box every struct — the row would be measuring the fake (Traps §7).
        var runner = new SkillRunner(_registry, _combat.Blackboard, new SilentEvents());

        SkillSpec skill = Active(ActiveA, cooldown: 0.5f);

        runner.Add(skill);

        Stat damage = _stats.Resolve(PlayerStat.WeaponDamage);

        float now = 0f;

        // Cast *and expire*: the modifier goes on and comes straight back off, so the stat's list
        // stops growing after the warm-up. That is also M3-11's shape — a timed cast effect owns its
        // own clock and calls EffectRegistry.Remove — standing in here for the clock that does not
        // exist yet. Without it the row would measure List<Modifier> doubling ten thousand times.
        AllocationAssert.None(() =>
        {
            now += 1f;

            runner.Cast(0, now, auto: true);
            damage.RemoveAll(skill.Active);
        });

        // The probe is live rather than measuring a no-op: every iteration really did cast.
        Assert.That(now, Is.GreaterThan(10_000f));
    }

    [Test]
    public void SetAutoCast_AllocatesNothing()
    {
        // A silent IDomainEvents rather than RecordingEvents, which stores each payload in a
        // List<object> and would box every struct — the row would be measuring the fake (Traps §7).
        var runner = new SkillRunner(_registry, _combat.Blackboard, new SilentEvents());

        runner.Add(Active(ActiveA));

        ContentId id = Id(ActiveA);

        // **Widened past the spec's body to read Slots and ManualSlotCount inside the measured
        // window**, because toggling allocating nothing says nothing about the view: a
        // `Array.AsReadOnly(_slots)` per call would sail through a body that only wrote. This plus
        // Slots_IsTheSameInstanceEveryCall is the pair — one catches the allocation, the other says
        // out loud that it is the same object.
        AllocationAssert.None(() =>
        {
            runner.SetAutoCast(id, auto: false);

            IReadOnlyList<ContentId> slots = runner.Slots;

            _ = slots[0];
            _ = runner.ManualSlotCount;
            _ = runner.SlotAt(0);
            _ = runner.IsAuto(id);

            runner.SetAutoCast(id, auto: true);
        });

        // The probe is live rather than measuring a no-op: the toggle really did move both ways.
        Assert.That(runner.IsAuto(id), Is.True);
        Assert.That(runner.ManualSlotCount, Is.Zero);
    }

    // ---- Through the port, and what it does not touch (M3-07a rules 4, 6, 11) --------------------

    [Test]
    public void Commands_ForwardToTheRunner()
    {
        // Here rather than in RunSessionTests, whose catalog is the three-argument overload — no
        // skills and no trees, so RunState.Tree is null in every row and the runner owns nothing.
        // This fixture already starts a real run holding an Active, for the two ordering rows.
        StartSession(Below(TriggerField.HpFraction, 0.6f), ActiveCooldown);

        var commands = (IPlayerCommands)_session;

        commands.SetAutoCast(Id(ActiveA), auto: false);

        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(1));
        Assert.That(_session.State.ManualSlotAt(0), Is.EqualTo(Id(ActiveA)));
        Assert.That(_session.State.IsAutoCast(Id(ActiveA)), Is.False);

        _events.Clear();

        commands.CastSkill(0);

        // The port and IRunSession are registered to the same instance in RunInstaller, so this is
        // the one brain being commanded rather than a second copy of the state.
        SkillCast cast = _events.Single<SkillCast>();

        Assert.That(cast.SkillId, Is.EqualTo(Id(ActiveA)));
        Assert.That(cast.WasAuto, Is.False);
    }

    [Test]
    public void MovementSkill_DoesNotCountAgainstTheSlots()
    {
        // **Rule 6, and it is true by construction rather than by an exemption.** The dash is
        // ChargeSkill — no SkillSpec, no trigger, no entry in the runner — so CC §6.2's "4 manual
        // slots, plus the always-present movement button" needs nothing to enforce it.
        StartSession(Below(TriggerField.HpFraction, 0.6f), ActiveCooldown, actives: 4);

        var commands = (IPlayerCommands)_session;

        for (int i = 0; i < SkillRunner.MaxManualSlots; i++)
        {
            commands.SetAutoCast(_session.State.SkillIdAt(i), auto: false);
        }

        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(SkillRunner.MaxManualSlots));

        _events.Clear();

        commands.MovementSkill();

        Assert.That(
            TickUntil(() => _events.Count<ChargeStarted>() > 0),
            Is.GreaterThan(0),
            "The dash still fires with all four slots taken.");

        Assert.That(
            _session.State.ManualSlotCount,
            Is.EqualTo(SkillRunner.MaxManualSlots),
            "And it took none of them.");
    }

    [Test]
    public void Snapshot_CarriesTheLoadout()
    {
        // **M3-07a rule 11's pin, inverted by the task it was pinning.** It read
        // `Snapshot_CarriesNoLoadout` and asserted `CurrentVersion` was still 2 with no member of
        // `RunSnapshot` looking like a slot — a red row M3-07b had to *change* rather than
        // remember. Kept and turned over rather than deleted, because the thing worth asserting for
        // ever is the same thing from the other side: the runner's table is what reaches the file.
        Assert.That(RunSnapshot.CurrentVersion, Is.EqualTo(3), "M3-07b is the bump.");

        Assert.That(
            typeof(RunSnapshot).GetProperty(nameof(RunSnapshot.ManualSkillIds)),
            Is.Not.Null,
            "The field the pin was holding a place for.");

        // And a run that has a loadout writes a different file from one that does not.
        StartSession(Below(TriggerField.HpFraction, 0.6f), ActiveCooldown, actives: 2);

        var commands = (IPlayerCommands)_session;

        commands.SetAutoCast(_session.State.SkillIdAt(0), auto: false);
        commands.SetAutoCast(_session.State.SkillIdAt(1), auto: false);

        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(2));

        _events.Clear();

        new RunRecorder(_random, new FixedClock(Instant), _events)
            .Take(_session.State, resumeStage: 2);

        RunSnapshot snapshot = _events.Single<RunSnapshotTaken>().Snapshot;

        Assert.That(snapshot.Version, Is.EqualTo(3));
        Assert.That(
            snapshot.TakenNodeIds.Count,
            Is.EqualTo(2),
            "The nodes are in the file.");

        // And so is where they sit, which is the sentence this row used to say the opposite of.
        Assert.That(snapshot.ManualSkillIds[0], Is.EqualTo(_session.State.SkillIdAt(0)));
        Assert.That(snapshot.ManualSkillIds[1], Is.EqualTo(_session.State.SkillIdAt(1)));
        Assert.That(snapshot.ManualSkillIds[2], Is.EqualTo(default(ContentId)));
        Assert.That(snapshot.ManualSkillIds[3], Is.EqualTo(default(ContentId)));
    }

    // ---- Where it runs in a tick (rule 7) --------------------------------------------------------

    [Test]
    public void Session_TicksTheRunnerAfterCombatAndBeforeTheBehaviours()
    {
        // **The first half of rule 7.** The runner reads a blackboard PlayerCombat.Tick filled
        // *this* tick, so a trigger over HP answers about this frame's hit points. A bolt lands
        // during the projectile step of tick N, which is below both; the new fraction is written by
        // combat at tick N+1 and the cast follows immediately.
        //
        // Ticked *before* combat instead, the runner would be reading tick N's fraction at tick
        // N+1 — the pre-damage one — and the cast would land at N+2. So the row fails on a swap
        // rather than merely reporting a different number.
        StartSession(Below(TriggerField.HpFraction, 0.6f), cooldown: 100f);

        int landing = TickUntil(() => _events.Count<PlayerDamaged>() > 0);

        Assert.That(landing, Is.GreaterThan(0), "The fixture failed to land a bolt on the player.");

        Assert.That(
            _events.Count<SkillCast>(),
            Is.EqualTo(0),
            "Nothing casts on the landing tick itself: the runner ran before the bolt arrived, so "
                + "the blackboard it read still held the undamaged fraction.");

        Assert.That(
            _session.State.PlayerHpFraction,
            Is.LessThan(0.6f),
            "The fixture's own claim, out loud: one bolt really does take the player under the "
                + "threshold, or the rows below prove nothing.");

        _events.Clear();

        TickOnce();

        Assert.That(
            _events.Count<SkillCast>(),
            Is.EqualTo(1),
            "And on the very next tick it casts — the tick combat first wrote the new fraction.");
    }

    [Test]
    public void Session_TicksTheRunnerBeforeTheProjectileStep()
    {
        // **The second half of rule 7, and the one that is easy to write vacuously.** The assertion
        // is on the *order within one tick*: the SkillCast has to precede the PlayerDamaged the
        // bolt causes, not merely to have happened at some point.
        //
        // IncomingProjectiles is written by ProjectileSystem.Tick and nowhere else, at the end of
        // its own step, so read here it is the count of bolts still in the air *before* this tick's
        // arrivals are resolved — a shield raised before the bolt lands, which is what CC §6.4's
        // Bulwark means. Ticked below that step, the field would read 0 on the landing tick and
        // nothing would cast at all.
        //
        // The cooldown is a hundredth of a second so the skill is ready on every tick a bolt is
        // inbound, which is what puts the cast and the landing in the same frame. A long cooldown
        // would fire once, several ticks earlier, and the ordering assertion would be vacuously
        // true across two different ticks.
        StartSession(AtLeast(TriggerField.IncomingProjectiles, 1f), cooldown: 0.01f);

        int landing = TickUntil(() => _events.Count<PlayerDamaged>() > 0);

        Assert.That(landing, Is.GreaterThan(0), "The fixture failed to land a bolt on the player.");

        int castAt = IndexOfFirst<SkillCast>();
        int damagedAt = IndexOfFirst<PlayerDamaged>();

        Assert.That(
            castAt,
            Is.GreaterThanOrEqualTo(0),
            "The runner saw a bolt in the air on the tick it landed. Below the projectile step it "
                + "would have seen zero, because that step had just emptied the sky.");

        Assert.That(
            castAt,
            Is.LessThan(damagedAt),
            "And the shield went up before the bolt landed rather than over the wound — the whole "
                + "of the archetype's answer, in the order of one tick's events.");
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    private RunSession _session;
    private FixedRandom _random;

    /// <summary>Every body the director has spawned, so the fixture can report it back each tick.</summary>
    private List<int> _alive;

    private static ContentId Id(string value) => new ContentId(value);

    private static TriggerSpec Below(TriggerField field, float threshold) =>
        new TriggerSpec(new[] { new TriggerClause(field, TriggerComparison.Below, threshold) });

    private static TriggerSpec AtLeast(TriggerField field, float threshold) =>
        new TriggerSpec(new[] { new TriggerClause(field, TriggerComparison.AtLeast, threshold) });

    /// <summary>
    /// A trigger that cannot be read without saying so — the proof that a Manual skill's condition
    /// is never evaluated (rule 5), given that no counting fake can exist.
    /// </summary>
    /// <remarks>
    /// <b>The technique, and it generalises past triggers (Traps §7):</b> to prove a sealed,
    /// non-virtual collaborator was never *asked*, hand it an input that throws the moment it is
    /// used, and let a hundred silent ticks be the proof. Here <see cref="TriggerClause"/>'s
    /// constructor validates only the threshold and never the field, so an out-of-range field
    /// constructs and survives <see cref="TriggerSpec"/>'s count-only constructor, and
    /// <c>TriggerClause.IsMet</c>'s loud `default` throws on the first read.
    /// </remarks>
    private static TriggerSpec Unreadable() =>
        new TriggerSpec(new[]
        {
            new TriggerClause((TriggerField)99, TriggerComparison.AtLeast, 0f),
        });

    /// <summary>A, B and C owned and all three Manual, filling slots 0, 1 and 2 in that order.</summary>
    private void ManualThree()
    {
        _runner.Add(Active(ActiveA));
        _runner.Add(Active(ActiveB));
        _runner.Add(Active(ActiveThird));

        _runner.SetAutoCast(Id(ActiveA), auto: false);
        _runner.SetAutoCast(Id(ActiveB), auto: false);
        _runner.SetAutoCast(Id(ActiveThird), auto: false);
    }

    /// <summary>The state above plus D, which is CC §6.2's ceiling exactly reached.</summary>
    private void ManualFour()
    {
        ManualThree();

        _runner.Add(Active(ActiveFourth));
        _runner.SetAutoCast(Id(ActiveFourth), auto: false);
    }

    /// <summary>An Active whose cast buffs weapon damage, and which takes with nothing.</summary>
    private static SkillSpec Active(string id, float cooldown = ActiveCooldown, TriggerSpec trigger = null) =>
        new SkillSpec(
            Id(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            SkillKind.Active,
            Array.Empty<IEffect>(),
            new ActiveSpec(
                cooldown,
                trigger ?? Below(TriggerField.HpFraction, 0.6f),
                new IEffect[] { Damage(0.5f) }));

    /// <summary>
    /// An Active that <em>also</em> does something on take — the shape rule 8 is about, and the
    /// only one where the two sources can be told apart.
    /// </summary>
    private static SkillSpec ActiveThatAlsoTakes(string id) =>
        new SkillSpec(
            Id(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            SkillKind.Active,
            new IEffect[] { Damage(0.1f) },
            new ActiveSpec(
                ActiveCooldown,
                Below(TriggerField.HpFraction, 0.6f),
                new IEffect[] { Damage(0.5f) }));

    private static SkillSpec Passive(string id, float damagePercent) =>
        new SkillSpec(
            Id(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            SkillKind.Passive,
            new IEffect[] { Damage(damagePercent) });

    private static ModifyStat Damage(float percent) =>
        new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, percent);

    /// <summary>
    /// A three-branch tree holding <paramref name="node"/> alone in branch 0, over this fixture's
    /// own registry — so that <c>Take</c> applies through the same table the runner casts through.
    /// </summary>
    private SkillTree TreeHolding(SkillSpec node)
    {
        var spec = new SkillTreeSpec(
            Id(TreeId),
            Id(OathboundId),
            new[]
            {
                Branch('a', node.Id),
                Branch('b', Id(PassiveB)),
                Branch('c', Id(PassiveC)),
            });

        var catalog = new ContentCatalog(
            new[] { Character() },
            Array.Empty<EnemySpec>(),
            new[] { Mode(Array.Empty<RosterEntry>()) },
            new[] { node, Passive(PassiveB, 0.05f), Passive(PassiveC, 0.05f) },
            new[] { spec });

        return new SkillTree(new TreeRules(spec, catalog), _registry, _events);
    }

    private static SkillBranchSpec Branch(char letter, params ContentId[] tier) =>
        new SkillBranchSpec(
            new LocKey($"branch.{letter}"),
            new IReadOnlyList<ContentId>[] { tier });

    private PlayerStats Stats(PlayerCombat combat) => new PlayerStats(
        combat,
        new PlayerMotor(new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f), Vector3.UnitZ),
        new LevelTracker(Scalings.Xp(), _events));

    /// <summary>CC §7's class at the owner's retuned numbers.</summary>
    private static CharacterSpec Character() => new CharacterSpec(
        Id(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        MaxHp,
        new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, 3f, 8f, 60f, 0.4f),

        // The Focus ramp is switched off with a maximum of 1, for RunSessionTests' reason: it would
        // put a modifier and a stream of events into a fixture measuring neither.
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, ChargeCooldown, 0.15f, 20f, 4f, 0.05f));

    private static ModeSpec Mode(IReadOnlyList<RosterEntry> roster) => new ModeSpec(
        Id(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        roster,
        new[] { Id(ArenaOne), Id(ArenaTwo) });

    /// <summary>
    /// The Spitter, with no shield on the class and a bolt big enough to take the player under
    /// 60 % in one hit — which is what makes the first ordering row's threshold a real edge.
    /// </summary>
    private static EnemySpec Spitter() => new EnemySpec(
        Id(SpitterId),
        new LocKey("enemy.spitter.name"),
        maxHp: 40f,
        moveSpeed: 2.4f,
        targetPriority: 1,
        threatCost: 6,
        xpValue: 18f,
        isElite: false,
        contactDamage: 70f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 40f,
        EnemyBehaviourKind.Spitter,
        new ProjectileSpec(StandoffRange, speed: 12f, radius: 0.4f));

    /// <summary>
    /// A live run with one Spitter in the arena and <paramref name="actives"/> owned Actives, whose
    /// trigger and cooldown are the row's.
    /// </summary>
    /// <param name="actives">
    /// How many to own. The first keeps <see cref="ActiveA"/>'s name, which is the one every M3-06
    /// row quotes; the rest exist only so the slot rows have four skills to fill four slots with.
    /// <c>BuildWithActiveTree</c>'s shape, in <c>RunSessionResumeTests</c>, and its reason.
    /// </param>
    [Test]
    public void State_ExposesTheGrantedShield()
    {
        StartSession(Below(TriggerField.HpFraction, 0f), cooldown: 2.5f);

        // **The reachable half, and it is the one that matters today.** A live run reads zero
        // because nothing in this build can put a grant on the player: Health.GrantShield is public
        // but RunState.Combat is not, and the only thing that will ever call it is M3-11a-ii's
        // GrantShieldHandler. So this is the read being correctly empty and *saying so* — the same
        // reading `actives 0` has on the overlay — and the spec's "granted 35" half lands with the
        // handler, over a real cast, in that task's Handler_ApplyGrantsAndHolds.
        //
        // It sits here rather than in HealthTests because it needs a RunSession and that fixture
        // has none; the three State_* rows M3-09b and M3-10a added are already in this file for the
        // same practical reason.
        Assert.That(_session.State.PlayerGrantedShield, Is.Zero);

        // **And the seal did not move to let it out** (AR §18.2). A twelfth scalar read is one more
        // number, not a handle: Health now has a public GrantShield beside its public ApplyDamage
        // and Heal, so a view holding Combat could hand itself a shield as easily as heal to full.
        // Asked by reflection because the compiler cannot be — a test in Soulvail.Tests.Core would
        // see `internal` if the assembly had InternalsVisibleTo, and it deliberately does not.
        PropertyInfo handle = typeof(RunState).GetProperty(
            "Combat",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(handle, Is.Not.Null, "RunState.Combat is gone — this row is out of date.");

        Assert.That(
            handle.GetMethod.IsPublic,
            Is.False,
            "RunState.Combat became public. M3-11a-i added a read beside it and must not have "
                + "loosened the seal to do it.");
    }

    private void StartSession(TriggerSpec trigger, float cooldown, int actives = 1)
    {
        var nodes = new List<SkillSpec>();
        var tier = new List<ContentId>();

        for (int i = 0; i < actives; i++)
        {
            SkillSpec node = Active(i == 0 ? ActiveA : $"{ActiveA}.{i}", cooldown, trigger);

            nodes.Add(node);
            tier.Add(node.Id);
        }

        var tree = new SkillTreeSpec(
            Id(TreeId),
            Id(OathboundId),
            new[]
            {
                new SkillBranchSpec(
                    new LocKey("branch.a"),
                    new IReadOnlyList<ContentId>[] { tier }),
                Branch('b', Id(PassiveB)),
                Branch('c', Id(PassiveC)),
            });

        var skills = new List<SkillSpec>(nodes)
        {
            Passive(PassiveB, 0.05f),
            Passive(PassiveC, 0.05f),
        };

        var catalog = new ContentCatalog(
            new[] { Character() },
            new[] { Spitter() },
            new[] { Mode(new[] { new RosterEntry(Id(SpitterId), 1) }) },
            skills,
            new[] { tree });

        _random = new FixedRandom(7, Alternating(8_192));
        _alive = new List<int>();

        // **The fixture plays SnapshotBuilder**, which is what these two rows cost: a Spitter will
        // not begin a wind-up it cannot see through (M2-11b), and `EnemySense.HasLineOfSight` is a
        // *sense* filled by a Unity adapter — false by default in a core-only test, so nothing ever
        // fires. Collecting the spawned ids here is the only way to report them back each tick.
        var reporting = new WatchingEvents(_events)
        {
            OnPublish = payload =>
            {
                if (payload is EnemySpawned spawned)
                {
                    _alive.Add(spawned.Id);
                }
            },
        };

        _session = new RunSession(
            catalog,
            _random,
            reporting,
            new RecordingIntents(),
            new RunRecorder(_random, new FixedClock(Instant), reporting),
            EnemyCapacity,
            DeviceCap,
            ProjectileCapacity);

        // Resumed rather than fresh, because that is the door RunSession.Start adds a restored
        // Active through (rule 5) — and it is the only door there is until M3-08a's ChooseOffer.
        _session.Start(new RunConfig(
            Id(ModeId),
            Id(OathboundId),
            _random.Seed,
            stageIndex: 1,
            SpawnPlan.Empty,
            new RunSnapshot(
                RunSnapshot.CurrentVersion,
                Id(ModeId),
                Id(OathboundId),
                _random.Seed,
                1,
                new RandomState(101, 102, 103, 104, 105),
                MaxHp,
                0f,
                0f,
                Instant,

                // **The level has to account for the nodes** (M3-08a rule 9). Every pick a run has
                // earned is spent on a node, spent on Overflow, or unspent — so a level-1 save
                // holding taken nodes is arithmetic that cannot happen, and `RunSession.Start`
                // refuses it. One level per node and nothing owed is the shape that says "these
                // nodes were paid for", which is what this fixture always meant.
                1 + tier.Count,
                0f,
                0,
                tier.ToArray(),
                new ContentId[SkillRunner.MaxManualSlots])));

        Assert.That(
            _session.State.OwnedActiveCount,
            Is.EqualTo(actives),
            "The fixture owns its Actives.");

        _events.Clear();
    }

    /// <summary>
    /// A live run whose class has no tree at all — the shipped build, and the one every slot read
    /// has to tolerate (M3-10a rule 2).
    /// </summary>
    /// <remarks>
    /// The three-argument catalog, so <c>TryGetTreeFor</c> answers false, <c>RunState.Tree</c> is
    /// null and the runner owns nothing. Fresh rather than resumed, unlike
    /// <see cref="StartSession"/>: there are no nodes to restore, and a fresh run is the shorter
    /// statement of the same state.
    /// </remarks>
    private void StartTreelessSession()
    {
        var catalog = new ContentCatalog(
            new[] { Character() },
            Array.Empty<EnemySpec>(),
            new[] { Mode(Array.Empty<RosterEntry>()) });

        _random = new FixedRandom(7, Alternating(8_192));

        _session = new RunSession(
            catalog,
            _random,
            _events,
            new RecordingIntents(),
            new RunRecorder(_random, new FixedClock(Instant), _events),
            EnemyCapacity,
            DeviceCap,
            ProjectileCapacity);

        _session.Start(new RunConfig(
            Id(ModeId),
            Id(OathboundId),
            _random.Seed,
            stageIndex: 1,
            SpawnPlan.Empty,
            null));

        _events.Clear();
    }

    /// <summary>
    /// Ticks one frame at a time, clearing the recorder before each, and stops on the tick
    /// <paramref name="done"/> first answers true — so the recorder holds exactly that tick's
    /// events when it returns.
    /// </summary>
    /// <returns>How many ticks it took, or −1 if it never happened.</returns>
    private int TickUntil(Func<bool> done)
    {
        for (int i = 1; i <= 3_000; i++)
        {
            _events.Clear();

            TickOnce();

            if (done())
            {
                return i;
            }
        }

        return -1;
    }

    private void TickOnce()
    {
        var snapshot = new WorldSnapshot(EnemyCapacity)
        {
            Dt = Frame,
            PlayerPosition = Vector3.Zero,
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

        // Every live body reported back at exactly the Spitter's standoff range, with line of
        // sight. Held there rather than integrated from its move intents, which is the shortest
        // honest way to get a bolt into the air: at standoff it has nowhere to back off to, so it
        // winds up and releases instead of repositioning for ever. It is also outside the class's
        // 12 m acquire range, so the player never swings and nothing here ever dies.
        for (int i = 0; i < _alive.Count; i++)
        {
            ref EnemySense sense = ref snapshot.AddEnemy();

            sense.Id = _alive[i];
            sense.Position = Standoff;
            sense.Velocity = Vector3.Zero;
            sense.PathDirectionToPlayer = new Vector2(0f, -1f);
            sense.HasLineOfSight = true;
        }

        _session.Tick(snapshot);
    }

    /// <summary>Where the first <typeparamref name="T"/> sits in this tick's event order.</summary>
    private int IndexOfFirst<T>()
        where T : struct
    {
        IReadOnlyList<object> all = _events.All;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is T)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary><paramref name="count"/> points on a ring, clear of the origin and of each other.</summary>
    private static IReadOnlyList<Vector3> Points(int count)
    {
        var points = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            double angle = 2d * Math.PI * i / count;

            points[i] = new Vector3((float)(Ring * Math.Cos(angle)), 0f, (float)(Ring * Math.Sin(angle)));
        }

        return points;
    }

    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }

    /// <summary>
    /// An <see cref="IDomainEvents"/> that hands each payload to a callback as it is published, so
    /// one row can read the runner from <em>inside</em> the publish.
    /// </summary>
    /// <remarks>
    /// <c>RecordingEvents</c> cannot do this job — it records what was published, which says
    /// nothing about what was true at the moment it happened, and that ordering is rule 9.
    /// <c>RunSessionTests</c> keeps a private one of these for the same reason.
    /// </remarks>
    private sealed class WatchingEvents : IDomainEvents
    {
        private readonly RecordingEvents _log;

        /// <param name="log">Where everything is also recorded, or null to watch only.</param>
        internal WatchingEvents(RecordingEvents log)
        {
            _log = log;
        }

        public Action<object> OnPublish { get; set; }

        public void Publish<T>(in T evt)
            where T : struct
        {
            _log?.Publish(in evt);

            OnPublish?.Invoke(evt);
        }
    }
}
