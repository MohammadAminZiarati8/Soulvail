using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

/// <summary>
/// M3-12b's two primitives: the one that names a <em>skill</em> rather than a number, and the one
/// that switches a rule on. What each addresses, what each refuses, what removal takes back — and
/// the thing neither <c>ModifyStat</c> nor any primitive before them had to answer: what happens
/// when the target does not exist yet.
/// </summary>
/// <remarks>
/// <para>
/// <b>Against a real <c>SkillRunner</c>, a real <c>EffectRegistry</c> and a real
/// <c>PlayerCombat</c> throughout.</b> The claim under test is that a modifier reaches the number
/// the game plays with, so a double standing in for the runner would prove only that the handler
/// calls the method it calls.
/// </para>
/// <para>
/// <b>What the swing actually <em>does</em> with its shove is <c>StatReachTests</c>' subject</b>,
/// one module over, where the fixture that drives a Censer to its damage frame already lives. The
/// split is the one <c>PlayerStatCoverageTests</c> and <c>StatReachTests</c> already make: this file
/// is about the primitives and the addresses, that one is about the swing.
/// </para>
/// <para>
/// <b>Rule 3's pending path is not an edge case — it is the path every resumed run takes.</b>
/// <c>RunSession.Start</c> replays the whole tree through <c>SkillTree.Restore</c> and only
/// <em>then</em> pushes the resumed run's Actives into the runner, so a cooldown node is always
/// applied to an empty runner on a resume, whatever order it was taken in. That is what
/// <see cref="Session_RegistersBothHandlers"/> exercises, and it is why the handler holds rather
/// than forgets.
/// </para>
/// <para>
/// CC §6.4's two actives supply the numbers: Consecrate at 12 s, Bulwark at 8 s. <b>Nothing in this
/// build authors either</b> — <c>Consecrate.asset</c> is M3-12c's — so every row builds its own
/// <c>SkillSpec</c>, which is the ninth consecutive task's answer to the same question.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SkillTargetedEffectTests
{
    private const string OathboundId = "character.oathbound";
    private const string ModeId = "mode.test";
    private const string TreeId = "tree.oathbound";

    private const string ConsecrateId = "skill.test.consecrate";
    private const string BulwarkId = "skill.test.bulwark";
    private const string PassiveId = "skill.test.passive";

    /// <summary>An id of the right shape that no row ever gives to a skill.</summary>
    private const string AbsentId = "skill.test.absent";

    /// <summary>CC §6.4's Consecrate. Floors at 4.8 s.</summary>
    private const float ConsecrateCooldown = 12f;

    /// <summary>CC §6.4's Bulwark, and the 8 s the floor rows are written against — 3.2 s.</summary>
    private const float BulwarkCooldown = 8f;

    private const float MaxHp = 140f;
    private const float ShieldMax = 30f;
    private const float ShieldDelay = 4f;
    private const float ShieldRefill = 15f;
    private const float HitIFrames = 0.5f;

    private const int EnemyCapacity = 8;
    private const int ProjectileCapacity = 8;
    private const int DeviceCap = 8;

    private const int Seed = 99;

    private const float Tolerance = 1e-3f;

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _combat;
    private EffectRegistry _registry;
    private SkillRunner _runner;
    private ModifySkillCooldownHandler _cooldowns;
    private KnockbackOnSwingHandler _knockback;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _combat = new PlayerCombat(Character(), _events, _intents, EnemyCapacity);
        _registry = new EffectRegistry();
        _runner = new SkillRunner(_registry, _combat.Blackboard, _events);

        // The handler subscribes to the runner's arrivals from inside this constructor, which is
        // the whole of rule 3's mechanism — see Runner_TakesOneCooldownHandlerPerRun.
        _cooldowns = new ModifySkillCooldownHandler(_runner);
        _knockback = new KnockbackOnSwingHandler(_combat);

        _registry.Register<ModifySkillCooldown>(_cooldowns);
        _registry.Register<KnockbackOnSwing>(_knockback);
    }

    // ---- Rules 1 and 2: the address, and whose Stat it reaches ------------------------------------

    [Test]
    public void Cooldown_AppliesToTheNamedSkill()
    {
        _runner.Add(Active(BulwarkId, BulwarkCooldown));
        _runner.Add(Active(ConsecrateId, ConsecrateCooldown));

        _cooldowns.Apply(Reduce(BulwarkId, ModifierKind.PercentMult, -0.25f), new object());

        Assert.That(
            _runner.EffectiveCooldownOf(IndexOf(BulwarkId)),
            Is.EqualTo(6f).Within(Tolerance),
            "8 x 0.75. A node named one skill and that skill's wait got shorter.");

        Assert.That(
            _runner.EffectiveCooldownOf(IndexOf(ConsecrateId)),
            Is.EqualTo(ConsecrateCooldown).Within(Tolerance),
            "And the skill it did not name is untouched — which is the entire reason this is a "
                + "primitive rather than a PlayerStat member (rule 1).");

        Assert.That(
            _runner.CooldownOf(IndexOf(ConsecrateId)).ModifierCount,
            Is.Zero,
            "Not merely equal by arithmetic: nothing was put on the other stat at all.");
    }

    [Test]
    public void Cooldown_ReachesTheRunnersOwnStat()
    {
        _runner.Add(Active(BulwarkId, BulwarkCooldown));

        Stat address = _runner.CooldownOf(IndexOf(BulwarkId));

        _cooldowns.Apply(Reduce(BulwarkId, ModifierKind.PercentMult, -0.25f), new object());

        // ReferenceEquals, never a value comparison: a handler that resolved to a copy holding 8
        // would satisfy every arithmetic assertion above and move nothing the runner ever reads.
        Assert.That(
            _runner.CooldownOf(IndexOf(BulwarkId)),
            Is.SameAs(address),
            "M3-06's Runner_ExposesTheCooldownAddress pins this from the runner's side; this is the "
                + "same claim from the effect's (rule 2).");

        Assert.That(
            address.ModifierCount,
            Is.EqualTo(1),
            "And the modifier landed on that very instance rather than on one beside it.");
    }

    [Test]
    public void Cooldown_ObeysTheFloor()
    {
        _runner.Add(Active(BulwarkId, BulwarkCooldown));

        // Four additive -20 %, which is 8 x (1 - 0.8) = 1.6 before the floor. CooldownRules still
        // owns the floor (M3-06 rule 1), and this primitive deliberately clamps nothing: a modifier
        // is a contribution to a stack, and a floor applied at the door would depend on how many
        // nodes were taken rather than on the authored base.
        for (int i = 0; i < 4; i++)
        {
            _cooldowns.Apply(Reduce(BulwarkId, ModifierKind.PercentAdd, -0.2f), new object());
        }

        Assert.That(
            _runner.CooldownOf(IndexOf(BulwarkId)).Value,
            Is.EqualTo(1.6f).Within(Tolerance),
            "The stack really did reach 1.6 — so this row is about the floor rather than about a "
                + "modifier that never landed.");

        Assert.That(
            _runner.EffectiveCooldownOf(IndexOf(BulwarkId)),
            Is.EqualTo(3.2f).Within(Tolerance),
            "CH §4.1's 40 % of 8, not 1.6. A cooldown node cannot outrun the floor however deep "
                + "the stack goes.");
    }

    // ---- Rule 3: a skill the player does not own -------------------------------------------------

    [Test]
    public void Cooldown_ForAnUnownedSkill_IsSilent()
    {
        _runner.Add(Active(ConsecrateId, ConsecrateCooldown));

        // A Passive carrying a cooldown node for a skill nobody has taken. TreeRules gates an
        // Upgrade on its parent (M3-03 rule 1) and gates a Passive on nothing, so this is a legal
        // authored tree reaching a legal pick — and a throw here would kill the run at the moment
        // the player tapped a card.
        Assert.DoesNotThrow(
            () => _cooldowns.Apply(Reduce(AbsentId, ModifierKind.PercentMult, -0.25f), new object()));

        Assert.That(
            _runner.EffectiveCooldownOf(IndexOf(ConsecrateId)),
            Is.EqualTo(ConsecrateCooldown).Within(Tolerance),
            "And it did not land on whatever happened to be there instead.");

        Assert.That(
            _runner.CooldownOf(IndexOf(ConsecrateId)).ModifierCount,
            Is.Zero);

        Assert.That(
            _cooldowns.PendingCount,
            Is.EqualTo(1),
            "Held rather than forgotten. Refusing and dropping it would be a node that lies — the "
                + "alternative rule 3 rejects by name.");
    }

    [Test]
    public void Cooldown_LandsWhenTheSkillArrives()
    {
        _cooldowns.Apply(Reduce(BulwarkId, ModifierKind.PercentMult, -0.25f), new object());

        Assert.That(_runner.Count, Is.Zero, "Nothing owned when the node was taken.");

        _runner.Add(Active(BulwarkId, BulwarkCooldown));

        Assert.That(
            _runner.EffectiveCooldownOf(IndexOf(BulwarkId)),
            Is.EqualTo(6f).Within(Tolerance),
            "8 x 0.75, the same number the owned-first row gets. The order the two arrived in is "
                + "invisible from here, which is the whole promise (rule 3).");

        Assert.That(
            _cooldowns.PendingCount,
            Is.Zero,
            "And it is spent — the note is gone, not merely honoured.");
    }

    [Test]
    public void Cooldown_PendingIsSpentOnce()
    {
        _cooldowns.Apply(Reduce(BulwarkId, ModifierKind.PercentMult, -0.25f), new object());

        _runner.Add(Active(BulwarkId, BulwarkCooldown));
        _runner.Add(Active(ConsecrateId, ConsecrateCooldown));

        Stat bulwark = _runner.CooldownOf(IndexOf(BulwarkId));

        // Counted rather than read as a value, which is the difference between this row and the one
        // above: two -25 % PercentMult modifiers give 4.5 and would be caught here either way, but a
        // Flat 0 replayed twice would read identically and only the count would see it.
        Assert.That(
            bulwark.ModifierCount,
            Is.EqualTo(1),
            "Spent once. A second Add must not replay a note that has already been honoured.");

        Assert.That(
            _runner.EffectiveCooldownOf(IndexOf(BulwarkId)),
            Is.EqualTo(6f).Within(Tolerance));

        Assert.That(
            _runner.CooldownOf(IndexOf(ConsecrateId)).ModifierCount,
            Is.Zero,
            "And the skill that arrived second did not collect the other one's modifier.");
    }

    // ---- Rule 4: removal, from either place -------------------------------------------------------

    [Test]
    public void Cooldown_RemoveTakesBackAPendingOne()
    {
        var node = new object();
        ModifySkillCooldown effect = Reduce(BulwarkId, ModifierKind.PercentMult, -0.25f);

        _cooldowns.Apply(effect, node);
        _cooldowns.Remove(effect, node);

        Assert.That(_cooldowns.PendingCount, Is.Zero, "The note was struck, not merely marked.");

        _runner.Add(Active(BulwarkId, BulwarkCooldown));

        Assert.That(
            _runner.EffectiveCooldownOf(IndexOf(BulwarkId)),
            Is.EqualTo(BulwarkCooldown).Within(Tolerance),
            "A node taken and given back before its skill arrived must not land the moment it does.");

        Assert.That(_runner.CooldownOf(IndexOf(BulwarkId)).ModifierCount, Is.Zero);
    }

    [Test]
    public void Cooldown_RemoveTakesBackALiveOne()
    {
        var node = new object();
        ModifySkillCooldown effect = Reduce(BulwarkId, ModifierKind.PercentMult, -0.25f);

        _runner.Add(Active(BulwarkId, BulwarkCooldown));
        _cooldowns.Apply(effect, node);

        Assert.That(
            _runner.CooldownOf(IndexOf(BulwarkId)).ModifierCount,
            Is.EqualTo(1),
            "Sanity: there is something to take back.");

        _cooldowns.Remove(effect, node);

        Assert.That(_runner.CooldownOf(IndexOf(BulwarkId)).ModifierCount, Is.Zero);

        Assert.That(
            _runner.EffectiveCooldownOf(IndexOf(BulwarkId)),
            Is.EqualTo(BulwarkCooldown).Within(Tolerance),
            "Back to what a designer typed, exactly.");

        // Stat.RemoveAll's contract, which is what lets a caller clean up unconditionally — and the
        // door the first timed cooldown buff will use (rule 4). Nothing in M3 removes a node.
        Assert.DoesNotThrow(() => _cooldowns.Remove(effect, node));
        Assert.DoesNotThrow(() => _cooldowns.Remove(effect, new object()));
    }

    [Test]
    public void Cooldown_RemoveLeavesAnotherSourceAlone()
    {
        var kept = new object();
        ModifySkillCooldown effect = Reduce(BulwarkId, ModifierKind.PercentMult, -0.25f);

        _cooldowns.Apply(effect, kept);
        _cooldowns.Apply(effect, new object());

        Assert.That(_cooldowns.PendingCount, Is.EqualTo(2));

        _cooldowns.Remove(effect, kept);

        Assert.That(
            _cooldowns.PendingCount,
            Is.EqualTo(1),
            "Sources are matched by reference, never by equality — Stat.RemoveAll's rule, and the "
                + "pending table holds to it or a removal would take back a node nobody asked "
                + "about.");

        _runner.Add(Active(BulwarkId, BulwarkCooldown));

        Assert.That(_runner.CooldownOf(IndexOf(BulwarkId)).ModifierCount, Is.EqualTo(1));
    }

    // ---- The doors -------------------------------------------------------------------------------

    [Test]
    public void Cooldown_Guards()
    {
        ContentId bulwark = Id(BulwarkId);

        Assert.Throws<ArgumentException>(
            () => new ModifySkillCooldown(default, ModifierKind.PercentMult, -0.25f),
            "An unnamed skill would wait for ever under rule 3 and nothing would ever say so.");

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModifySkillCooldown(bulwark, (ModifierKind)99, -0.25f));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModifySkillCooldown(bulwark, ModifierKind.PercentMult, float.NaN));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModifySkillCooldown(bulwark, ModifierKind.PercentMult, float.PositiveInfinity));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModifySkillCooldown(bulwark, ModifierKind.PercentMult, float.NegativeInfinity));

        // And zero is legal, deliberately: a node authored at -0 % is a designer's problem rather
        // than a content error, and nothing downstream can misread it.
        Assert.DoesNotThrow(() => new ModifySkillCooldown(bulwark, ModifierKind.Flat, 0f));
    }

    [Test]
    public void Swing_Guards()
    {
        // Zero is refused here where EnemyKnockbackIntent.Distance allows it, and the difference is
        // which mistake each door is guarding: an intent of zero is a hit that moved nothing, and a
        // *node* of zero is a pick the player spent and got nothing for.
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnockbackOnSwing(0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnockbackOnSwing(-1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KnockbackOnSwing(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new KnockbackOnSwing(float.PositiveInfinity));

        Assert.DoesNotThrow(() => new KnockbackOnSwing(1.5f));
    }

    [Test]
    public void Handler_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new ModifySkillCooldownHandler(null));
        Assert.Throws<ArgumentNullException>(() => new KnockbackOnSwingHandler(null));

        ModifySkillCooldown cooldown = Reduce(BulwarkId, ModifierKind.PercentMult, -0.25f);
        var swing = new KnockbackOnSwing(1.5f);

        Assert.Throws<ArgumentNullException>(() => _cooldowns.Apply(null, new object()));
        Assert.Throws<ArgumentNullException>(() => _cooldowns.Apply(cooldown, null));
        Assert.Throws<ArgumentNullException>(() => _cooldowns.Remove(null, new object()));
        Assert.Throws<ArgumentNullException>(() => _cooldowns.Remove(cooldown, null));

        Assert.Throws<ArgumentNullException>(() => _knockback.Apply(null, new object()));
        Assert.Throws<ArgumentNullException>(() => _knockback.Apply(swing, null));
        Assert.Throws<ArgumentNullException>(() => _knockback.Remove(null, new object()));
        Assert.Throws<ArgumentNullException>(() => _knockback.Remove(swing, null));

        // A null source is refused on the *pending* path too, and that is the point of the row:
        // the Modifier is built before the owned/unowned branch, so a node applied for an unowned
        // skill fails on the frame that caused it rather than on some later arrival.
        Assert.That(_cooldowns.PendingCount, Is.Zero);
    }

    [Test]
    public void Cooldown_PendingIsRefusedPastTheCeiling()
    {
        for (int i = 0; i < ModifySkillCooldownHandler.MaxPending; i++)
        {
            _cooldowns.Apply(Reduce(AbsentId, ModifierKind.Flat, -0.1f), new object());
        }

        // **Not the throw rule 3 forbids.** A node for a skill you do not own is legal and silent;
        // thirty-three unspent ones at once is not reachable from any tree TreeRules accepts, so
        // this is SkillRunner.Add's capacity throw exactly — a backstop that would otherwise be a
        // silent overwrite.
        var thrown = Assert.Throws<InvalidOperationException>(
            () => _cooldowns.Apply(Reduce(AbsentId, ModifierKind.Flat, -0.1f), new object()));

        Assert.That(thrown.Message, Does.Contain("27"), "…and it names the bound it is arguing from.");

        Assert.That(
            _cooldowns.PendingCount,
            Is.EqualTo(ModifySkillCooldownHandler.MaxPending),
            "Nothing moved: it refuses before it writes.");
    }

    [Test]
    public void Runner_TakesOneCooldownHandlerPerRun()
    {
        // Reached through a second handler because SkillRunner.WatchArrivals is internal and
        // Soulvail.Tests.Core has no InternalsVisibleTo (M0-10) — which is the right shape: the
        // hook is not a general extension point, and the only caller there will ever be is this
        // constructor.
        Assert.Throws<InvalidOperationException>(() => new ModifySkillCooldownHandler(_runner));
    }

    // ---- Rule 6: the base of zero, pinned rather than argued ---------------------------------------

    [Test]
    public void Swing_TheHandlerPicksTheOnlyKindThatCanWork()
    {
        _knockback.Apply(new KnockbackOnSwing(1.5f), new object());

        // **Measured rather than inspected, and the arithmetic is the proof.** SwingKnockback's
        // base is 0 and Stat computes (Base + SumFlat) * (1 + SumPercentAdd) * Prod(1 + PercentMult),
        // so a value of 1.5 off one modifier is only reachable with ModifierKind.Flat — every other
        // kind multiplies into zero. This is the row that would go red the day somebody "tidied" the
        // handler into taking an authored kind, which is the trap M3-12a measured on HealPerKill.
        Assert.That(_combat.SwingKnockback.Value, Is.EqualTo(1.5f).Within(Tolerance));
        Assert.That(_combat.SwingKnockback.ModifierCount, Is.EqualTo(1));

        Assert.That(
            _combat.SwingKnockback.Base,
            Is.Zero,
            "And the base is still zero — the node lifted it rather than re-basing it.");
    }

    [Test]
    public void Swing_StacksAdditively()
    {
        _knockback.Apply(new KnockbackOnSwing(1.5f), new object());
        _knockback.Apply(new KnockbackOnSwing(1.5f), new object());

        Assert.That(
            _combat.SwingKnockback.Value,
            Is.EqualTo(3f).Within(Tolerance),
            "Two nodes of 1.5 give 3, and nothing anywhere counts nodes to arrange it (rule 8).");
    }

    [Test]
    public void Swing_RemoveTakesTheSourceBack()
    {
        var node = new object();
        var effect = new KnockbackOnSwing(1.5f);

        _knockback.Apply(effect, node);
        _knockback.Remove(effect, node);

        Assert.That(_combat.SwingKnockback.Value, Is.Zero);
        Assert.That(_combat.SwingKnockback.ModifierCount, Is.Zero);

        Assert.DoesNotThrow(
            () => _knockback.Remove(effect, new object()),
            "Stat.RemoveAll's contract: a source with nothing on the stat is not an error.");
    }

    // ---- Rule 4 again, and AR §14 -----------------------------------------------------------------

    [Test]
    public void Cooldown_AllocatesNothing()
    {
        var node = new object();
        ModifySkillCooldown owned = Reduce(BulwarkId, ModifierKind.PercentMult, -0.25f);
        ModifySkillCooldown absent = Reduce(AbsentId, ModifierKind.PercentMult, -0.25f);

        _runner.Add(Active(BulwarkId, BulwarkCooldown));

        // Warmed by the harness before it measures; both doors are walked here so the row cannot
        // pass by measuring the cheap one twice.
        AllocationAssert.None(
            () =>
            {
                _cooldowns.Apply(owned, node);
                _cooldowns.Remove(owned, node);
            });

        // The pending path, which is the one with a table in it — two fixed arrays sized at
        // construction and a Modifier struct written into them, so a pick costs nothing either.
        AllocationAssert.None(
            () =>
            {
                _cooldowns.Apply(absent, node);
                _cooldowns.Remove(absent, node);
            });

        Assert.That(_cooldowns.PendingCount, Is.Zero, "…and it did not quietly grow while measured.");
        Assert.That(_runner.CooldownOf(IndexOf(BulwarkId)).ModifierCount, Is.Zero);
    }

    // ---- Rule 10: both are registered before the run is announced ---------------------------------

    [Test]
    public void Session_RegistersBothHandlers()
    {
        // **A whole run, because CanApply cannot be asked from outside core** — RunState.Effects is
        // internal (AR §18.2) — and because the claim rule 10 makes is precisely about the run: a
        // tree carrying either primitive is accepted at Start rather than refused at a pick.
        // SkillTree's constructor sweeps every effect it holds through CanApply, so an unregistered
        // one would throw out of the Start below.
        RunSession session = StartWithTree();

        Assert.That(
            session.State.OwnedActiveCount,
            Is.EqualTo(1),
            "The run stands and owns what it was resumed with.");

        // **And the whole chain, end to end, through the production wiring**: RunSession.Start
        // replays the tree through SkillTree.Restore and only *then* pushes the resumed Actives into
        // the runner — so this cooldown node is applied to an empty runner, held pending, and spent
        // by SkillRunner.Add a few lines later. Rule 3's path is the one every resumed run takes.
        Assert.That(
            session.State.SkillCooldownSeconds(0),
            Is.EqualTo(9f).Within(Tolerance),
            "12 x 0.75. This is the number manual step 1 reads off the Skills screen.");
    }

    [Test]
    public void Session_ARunWithNoTreeStillStarts()
    {
        // The other half, and the one every run in this build actually takes: registering two more
        // handlers must not have made a class with no tree refuse to start (M3-08a rule 5).
        RunSession session = StartWithoutTree();

        Assert.That(session.State, Is.Not.Null);
        Assert.That(session.State.OwnedActiveCount, Is.Zero);
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    private static ContentId Id(string value) => new ContentId(value);

    private int IndexOf(string skillId)
    {
        Assert.That(
            _runner.TryIndexOf(Id(skillId), out int index),
            Is.True,
            $"The runner does not own '{skillId}' — this row's setup is wrong, not its assertion.");

        return index;
    }

    private static ModifySkillCooldown Reduce(string skillId, ModifierKind kind, float value) =>
        new ModifySkillCooldown(Id(skillId), kind, value);

    /// <summary>True on the first tick of any run: a full bar is below 110 %.</summary>
    private static TriggerSpec Always() =>
        new TriggerSpec(new[] { new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 1.1f) });

    /// <summary>
    /// An Active whose cast buffs weapon damage — <c>SkillRunnerTests.Active</c>'s shape, and its
    /// reason: <c>ActiveSpec</c> refuses an empty <c>onCast</c> (<em>"an active must do something
    /// when it fires"</em>), so the cheapest legal payload is the one already registered in every
    /// run. Nothing here ever fires; every row is about the cooldown.
    /// </summary>
    private static SkillSpec Active(string id, float cooldown) =>
        new SkillSpec(
            Id(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            SkillKind.Active,
            Array.Empty<IEffect>(),
            new ActiveSpec(
                cooldown,
                Always(),
                new IEffect[]
                {
                    new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.5f),
                }));

    /// <summary>
    /// A Passive carrying both of this task's primitives — the node M3-12c will author, without
    /// the asset.
    /// </summary>
    private static SkillSpec PassiveCarryingBoth() =>
        new SkillSpec(
            Id(PassiveId),
            new LocKey($"{PassiveId}.name"),
            new LocKey($"{PassiveId}.desc"),
            SkillKind.Passive,
            new IEffect[]
            {
                new ModifySkillCooldown(Id(ConsecrateId), ModifierKind.PercentMult, -0.25f),
                new KnockbackOnSwing(1.5f),
            });

    private static SkillBranchSpec Branch(char letter, params ContentId[] tier) =>
        new SkillBranchSpec(
            new LocKey($"branch.{letter}"),
            new IReadOnlyList<ContentId>[] { tier });

    /// <summary>A run resumed owning Consecrate and the Passive that shortens it.</summary>
    private RunSession StartWithTree()
    {
        SkillSpec consecrate = Active(ConsecrateId, ConsecrateCooldown);
        SkillSpec bulwark = Active(BulwarkId, BulwarkCooldown);
        SkillSpec passive = PassiveCarryingBoth();

        var tree = new SkillTreeSpec(
            Id(TreeId),
            Id(OathboundId),
            new[]
            {
                Branch('a', consecrate.Id),
                Branch('b', bulwark.Id),
                Branch('c', passive.Id),
            });

        var catalog = new ContentCatalog(
            new[] { Character() },
            Array.Empty<EnemySpec>(),
            new[] { Mode() },
            new[] { consecrate, bulwark, passive },
            new[] { tree });

        // The Passive is listed *first*, which is the order that makes the point: its cooldown node
        // is replayed before any Active reaches the runner. It would be pending either way — Restore
        // runs the whole tree before the runner is fed — and taking it first says so out loud.
        return Start(catalog, new[] { passive.Id, consecrate.Id }, takenActives: 1);
    }

    /// <summary>A run for a class with no tree, which is every run this build plays.</summary>
    private RunSession StartWithoutTree()
    {
        var catalog = new ContentCatalog(
            new[] { Character() },
            Array.Empty<EnemySpec>(),
            new[] { Mode() });

        return Start(catalog, Array.Empty<ContentId>(), takenActives: 0);
    }

    private RunSession Start(ContentCatalog catalog, ContentId[] taken, int takenActives)
    {
        var random = new FixedRandom(Seed);

        var session = new RunSession(
            catalog,
            random,
            _events,
            _intents,
            new RunRecorder(random, new FixedClock(Instant), _events),
            EnemyCapacity,
            DeviceCap,
            ProjectileCapacity);

        // Resumed rather than fresh, because that is the door RunSession.Start adds a restored
        // Active through (M3-06 rule 5) — and it is the only door there is until a tree ships.
        session.Start(new RunConfig(
            Id(ModeId),
            Id(OathboundId),
            random.Seed,
            stageIndex: 1,
            SpawnPlan.Empty,
            new RunSnapshot(
                RunSnapshot.CurrentVersion,
                Id(ModeId),
                Id(OathboundId),
                random.Seed,
                1,
                new RandomState(101, 102, 103, 104, 105),
                MaxHp,
                0f,
                0f,
                Instant,
                taken.Length + 1,
                0f,
                0,
                taken,
                new ContentId[SkillRunner.MaxManualSlots],
                default,
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>())));

        Assert.That(
            session.State.OwnedActiveCount,
            Is.EqualTo(takenActives),
            "Sanity: the fixture owns what it authored.");

        _events.Clear();

        return session;
    }

    /// <summary>
    /// The mode with an <b>empty roster</b>: nothing composes, nothing spawns, nothing can hurt the
    /// player. No row here is about a schedule.
    /// </summary>
    private static ModeSpec Mode() => new ModeSpec(
        Id(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>());

    /// <summary>CC §7's class at the owner's retuned numbers.</summary>
    private static CharacterSpec Character() => new CharacterSpec(
        Id(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(ShieldMax, ShieldDelay, ShieldRefill),
        HitIFrames);
}
