using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

/// <summary>
/// The three sentences a CH §3.2 Legion node is made of, and the one verb CH §4.2's Exhume casts:
/// <em>"+20 % minion damage"</em>, <em>"auto-cast when I have fewer than half my Wights"</em>, and
/// <em>"raise three Wights"</em>. What each addresses, what each refuses, and the twenty seconds of
/// lag the first one costs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Against a real <c>MinionSystem</c>, a real <c>PlayerCombat</c> and — for the two rows that
/// need one — a real <c>RunSession</c>.</b> The claim under test is that a modifier reaches the
/// number a Wight is actually born with, so a double standing in for the army would prove only that
/// the handler calls the method it calls.
/// </para>
/// <para>
/// <b>Two rows go through a whole run because <c>RunState.Effects</c> is <c>internal</c></b>
/// (AR §18.2), which is <c>SkillTargetedEffectTests.Session_RegistersBothHandlers</c>' reason
/// unchanged: <c>CanApply</c> cannot be asked from outside core, so <em>"the run knows this
/// primitive"</em> is spelled <em>"a tree carrying it is accepted at <c>Start</c>"</em>. The same
/// door proves the refusals, which are about the run rather than about the pick.
/// </para>
/// <para>
/// <b>Nothing in this build authors any of it</b> — <c>Exhume.asset</c> and the twelve nodes are
/// <see href="../../../../Docs/plan/tasks/M5-06b-gravecaller-tree-v1.md">M5-06b</see>'s, and the
/// Gravecaller is not selectable until M5-07 — so every row builds its own <c>SkillSpec</c>. That is
/// M4-01a's bargain and what keeps this PR's review about the mechanism.
/// </para>
/// <para>
/// The numbers are <c>Gravecaller.asset</c>'s own: a Wight is 20 hit points, 3.0 m/s and 8 damage,
/// cap 3, twenty seconds.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LegionEffectsTests
{
    private const string WightId = "minion.wight";
    private const string HuskId = "enemy.husk";

    private const string GravecallerId = "character.gravecaller";
    private const string OathboundId = "character.oathbound";

    private const string ModeId = "mode.test";
    private const string TreeId = "tree.test";

    private const string LegionId = "skill.test.legion";
    private const string ExhumeId = "skill.test.exhume";
    private const string EverythingId = "skill.test.everything";

    /// <summary>The Gravecaller's Wight, from <c>Gravecaller.asset</c> (M5-02).</summary>
    private const float MinionMaxHp = 20f;

    private const float MinionSpeed = 3f;
    private const float MinionDamage = 8f;
    private const float AttackInterval = 1f;
    private const float Reach = 1.5f;
    private const int Cap = 3;
    private const float Lifespan = 20f;

    /// <summary>CH §3.2's Legion damage node — the one every rule 1 row applies.</summary>
    private const float LegionDamageBonus = 0.2f;

    /// <summary>8 × 1.2. The number the whole of rule 3 is about arriving at.</summary>
    private const float BuffedDamage = 9.6f;

    /// <summary>CH §4.2's Exhume, and the radius that is ours (rule 10).</summary>
    private const int ExhumeCount = 3;

    private const float ExhumeRadius = 2f;

    private const float GravecallerMaxHp = 80f;
    private const float OathboundMaxHp = 140f;

    private const int Capacity = 16;
    private const int ProjectileCapacity = 8;
    private const int DeviceCap = 8;

    private const int Seed = 4_242;

    private const float Tolerance = 1e-3f;

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private RecordingIntents _intents;

    private MinionSpec _wight;
    private MinionRecipe _recipe;
    private MinionSystem _minions;

    private PlayerCombat _combat;
    private PlayerStats _playerStats;
    private SimulatedClock _clock;

    private ModifyStatHandler _stats;
    private RaiseMinionsHandler _raise;

    private object _source;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();

        _wight = Wight();

        // One recipe, handed to both the army and the handler — which is the production wiring
        // (RunSession.Start) and the whole of rule 1: two recipes would hold identical numbers and
        // independent stacks, so a node would move a number no body reads.
        _recipe = new MinionRecipe(_wight);
        _minions = new MinionSystem(_wight, _recipe, _events, _intents);

        CharacterSpec character = Gravecaller();

        _combat = new PlayerCombat(character, _events, _intents, Capacity);
        _playerStats = new PlayerStats(
            _combat,
            new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), _events));

        _clock = new SimulatedClock();

        _stats = new ModifyStatHandler(_playerStats, new MinionRecipeStats(_recipe));
        _raise = new RaiseMinionsHandler(_minions, _combat.Blackboard, _clock);

        _source = new object();
    }

    // ---- Rules 2 and 4: what a recipe is, and what it answers -------------------------------------

    [Test]
    public void Recipe_CarriesTheAuthoredNumbers()
    {
        Assert.That(_recipe.MaxHp.Value, Is.EqualTo(MinionMaxHp).Within(Tolerance));
        Assert.That(_recipe.MoveSpeed.Value, Is.EqualTo(MinionSpeed).Within(Tolerance));
        Assert.That(_recipe.ContactDamage.Value, Is.EqualTo(MinionDamage).Within(Tolerance));

        // Live Stats at their base, not constants: a modifier has somewhere to sit.
        Assert.That(_recipe.MaxHp.Base, Is.EqualTo(MinionMaxHp).Within(Tolerance));
        Assert.That(_recipe.MoveSpeed.Base, Is.EqualTo(MinionSpeed).Within(Tolerance));
        Assert.That(_recipe.ContactDamage.Base, Is.EqualTo(MinionDamage).Within(Tolerance));

        Assert.That(_recipe.SpecId, Is.EqualTo(Id(WightId)), "It knows which content it is.");
    }

    [Test]
    public void Recipe_ImplementsTheBlock()
    {
        IStatBlock block = new MinionRecipeStats(_recipe);

        // The very instances, never copies — PlayerStats' contract, and what makes a modifier
        // applied through the block visible to the Spawn that reads the recipe.
        Assert.That(block.Resolve(PlayerStat.MaxHp), Is.SameAs(_recipe.MaxHp));
        Assert.That(block.Resolve(PlayerStat.MoveSpeed), Is.SameAs(_recipe.MoveSpeed));
        Assert.That(block.Resolve(PlayerStat.ContactDamage), Is.SameAs(_recipe.ContactDamage));
    }

    [Test]
    public void Recipe_RefusesAnAddressItDoesNotHave()
    {
        IStatBlock block = new MinionRecipeStats(_recipe);

        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => block.Resolve(PlayerStat.WeaponRange));

        Assert.That(
            thrown.Message,
            Does.Contain(nameof(PlayerStat.WeaponRange)),
            "A content error has to say which address.");

        Assert.That(
            thrown.Message,
            Does.Contain("recipe"),
            "And which of the two places a minion number lives refused it — rule 2.");
    }

    [Test]
    public void Recipe_HasAgreesWithResolve()
    {
        AssertHasAgreesWithResolve(new MinionRecipeStats(_recipe), "MinionRecipeStats");
    }

    [Test]
    public void Recipe_AndMinionStatsAnswerTheSameSet()
    {
        MinionAgent wight = _minions.Spawn(Vector3.Zero, now: 0f);

        IStatBlock recipe = new MinionRecipeStats(_recipe);
        IStatBlock body = new MinionStats(wight);

        foreach (PlayerStat member in Enum.GetValues(typeof(PlayerStat)))
        {
            Assert.That(
                recipe.Has(member),
                Is.EqualTo(body.Has(member)),
                $"The recipe and the body disagree about {member}. A recipe is what a Wight is "
                    + "born with, so the two answer the same three addresses by construction.");

            if (recipe.Has(member))
            {
                continue;
            }

            // Same set refused, different messages — rule 2's whole argument for a fourth
            // implementation rather than a generalisation of the first three.
            string fromRecipe = Refusal(recipe, member);
            string fromBody = Refusal(body, member);

            Assert.That(
                fromRecipe,
                Is.Not.EqualTo(fromBody),
                $"Both refuse {member} with the same words, so a designer reading the Console "
                    + "cannot tell whether the run's numbers or a body's were addressed.");

            Assert.That(fromBody, Does.Contain(WightId), "A body names its content (M4-01a rule 2).");
            Assert.That(fromRecipe, Does.Contain("recipe"));
        }
    }

    [Test]
    public void Recipe_TwoRunsDoNotShareAStack()
    {
        var second = new MinionRecipe(_wight);

        _recipe.ContactDamage.Add(new Modifier(ModifierKind.Flat, 10f, _source));

        Assert.That(
            second.ContactDamage.Value,
            Is.EqualTo(MinionDamage).Within(Tolerance),
            "Two runs of one class hold two recipes — rule 4.");

        Assert.That(
            _wight.Damage,
            Is.EqualTo(MinionDamage).Within(Tolerance),
            "And the authored spec is what a designer typed, untouched (AR §10.1).");
    }

    // ---- Rule 1: the third target, and what it aims at --------------------------------------------

    [Test]
    public void Minions_AimedAtMinionsMovesTheRecipe()
    {
        float playerMaxHpBefore = _playerStats.Resolve(PlayerStat.MaxHp).Value;

        _stats.Apply(Buff(PlayerStat.ContactDamage), _source);
        _stats.Apply(Buff(PlayerStat.MaxHp), _source);

        Assert.That(_recipe.ContactDamage.Value, Is.EqualTo(BuffedDamage).Within(Tolerance));
        Assert.That(_recipe.MaxHp.Value, Is.EqualTo(24f).Within(Tolerance));

        // MaxHp is an address *both* blocks answer, which is what makes this half of the row
        // possible at all: the player's is measured before and after and does not move.
        Assert.That(
            _playerStats.Resolve(PlayerStat.MaxHp).Value,
            Is.EqualTo(playerMaxHpBefore).Within(Tolerance),
            "A Legion node moves the minions' numbers and not the player's — rule 1.");
    }

    [Test]
    public void Minions_AimingIsNotNeeded()
    {
        // No `using` and no scope: the recipe is found rather than chosen, so there is nothing to
        // aim at — rule 1. If this needed a scope the row would throw here.
        Assert.DoesNotThrow(() => _stats.Apply(Buff(PlayerStat.ContactDamage), _source));

        Assert.That(_recipe.ContactDamage.Value, Is.EqualTo(BuffedDamage).Within(Tolerance));

        // And no scope was opened on the way through, which is the half that would otherwise go
        // unnoticed: a Self effect still refuses.
        Assert.Throws<InvalidOperationException>(
            () => _stats.Apply(
                new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f, StatTarget.Self),
                _source));
    }

    [Test]
    public void Minions_WithoutARecipeThrowsAndNamesTheClass()
    {
        var handler = new ModifyStatHandler(_playerStats);

        float before = _playerStats.Resolve(PlayerStat.MaxHp).Value;

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => handler.Apply(Buff(PlayerStat.MaxHp), _source));

        Assert.That(
            thrown.Message,
            Does.Contain("raises none"),
            "The message says the class has no minions rather than reporting a null — rule 5.");

        Assert.That(
            _playerStats.Resolve(PlayerStat.MaxHp).Value,
            Is.EqualTo(before).Within(Tolerance),
            "And it did not fall back to the player, which is the outcome the throw exists to "
                + "prevent: a real number moving by the authored amount with nothing reported.");
    }

    [Test]
    public void StatTarget_HasThreeMembersAndTheDefaultIsPlayer()
    {
        Assert.That(Enum.GetValues(typeof(StatTarget)), Has.Length.EqualTo(3));

        Assert.That(
            new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f).Target,
            Is.EqualTo(StatTarget.Player),
            "The default is what makes each widening a widening rather than a migration.");

        // M4-01a's rule held one member later: a fourth cast in from an int is refused at the door
        // rather than falling out of the handler's switch at the moment a player picks the node.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f, (StatTarget)3));
    }

    // ---- Rule 3: what a node changes, and when ----------------------------------------------------

    [Test]
    public void Minions_ANewWightIsBornWithTheBuff()
    {
        _stats.Apply(Buff(PlayerStat.ContactDamage), _source);

        MinionAgent wight = _minions.Spawn(Vector3.Zero, now: 0f);

        Assert.That(
            wight.ContactDamage.Value,
            Is.EqualTo(BuffedDamage).Within(Tolerance),
            "A node changes what being born means — rule 3.");
    }

    [Test]
    public void Minions_AStandingWightDoesNotChange()
    {
        MinionAgent standing = _minions.Spawn(Vector3.Zero, now: 0f);

        _stats.Apply(Buff(PlayerStat.ContactDamage), _source);

        Assert.That(
            standing.ContactDamage.Value,
            Is.EqualTo(MinionDamage).Within(Tolerance),
            "Rule 3's stated cost: a node taken while three Wights are up buffs the fourth.");

        MinionAgent next = _minions.Spawn(Vector3.UnitX, now: 0f);

        Assert.That(next.ContactDamage.Value, Is.EqualTo(BuffedDamage).Within(Tolerance));
    }

    [Test]
    public void Minions_TheLagIsBoundedByTheLifespan()
    {
        var silent = new SilentEvents();
        EnemySystem enemies = NewEnemies(silent);

        MinionAgent before = _minions.Spawn(Vector3.Zero, now: 0f);

        _stats.Apply(Buff(PlayerStat.ContactDamage), _source);

        // Raised after the node, so the arena holds one of each while the lag lasts.
        _minions.Spawn(Vector3.UnitX, now: 1f);

        Assert.That(before.ContactDamage.Value, Is.EqualTo(MinionDamage).Within(Tolerance));

        // One lifespan on from the first raise, and the unbuffed body's twenty seconds are up.
        _minions.Tick(Lifespan, Lifespan, enemies, _combat);

        Assert.That(
            _minions.Count,
            Is.EqualTo(1),
            "The Wight raised before the node has expired; the one raised after has not.");

        ReadOnlySpan<MinionAgent> alive = _minions.Alive;

        for (int i = 0; i < alive.Length; i++)
        {
            Assert.That(
                alive[i].ContactDamage.Value,
                Is.EqualTo(BuffedDamage).Within(Tolerance),
                "Rule 3's bound, measured: every Wight in the arena is replaced within CH §3.2's "
                    + "twenty seconds, so that is how long the lag can last.");
        }
    }

    // ---- Rules 6 and 7: the tenth trigger field ---------------------------------------------------

    [Test]
    public void Trigger_MinionCountReads()
    {
        var blackboard = new CombatBlackboard { MinionCount = 3 };

        Assert.That(
            new TriggerClause(TriggerField.MinionCount, TriggerComparison.Below, 2f)
                .IsMet(blackboard),
            Is.False,
            "Three standing is not below two — CH §4.2's Exhume holds its fire.");

        Assert.That(
            new TriggerClause(TriggerField.MinionCount, TriggerComparison.AtLeast, 2f)
                .IsMet(blackboard),
            Is.True);
    }

    [Test]
    public void Trigger_MinionCountIsWrittenEveryTick()
    {
        var snapshot = new WorldSnapshot(Capacity);

        Tick(snapshot, _minions);

        Assert.That(_combat.Blackboard.MinionCount, Is.Zero, "Nothing has been raised yet.");

        _minions.Spawn(Vector3.Zero, now: 0f);
        _minions.Spawn(Vector3.UnitX, now: 0f);

        Tick(snapshot, _minions);

        Assert.That(_combat.Blackboard.MinionCount, Is.EqualTo(2));

        // Expired rather than cleared, so the field is proved to fall as well as rise.
        _minions.Tick(Lifespan, Lifespan, NewEnemies(new SilentEvents()), _combat);

        Tick(snapshot, _minions);

        Assert.That(_combat.Blackboard.MinionCount, Is.Zero);

        // And zero on a class that raises none, which is what makes an Exhume clause on such a
        // class false rather than absent — rule 7.
        _combat.Blackboard.MinionCount = 7;

        Tick(snapshot, minions: null);

        Assert.That(
            _combat.Blackboard.MinionCount,
            Is.Zero,
            "A run with no army writes the honest zero rather than leaving the field alone.");
    }

    [Test]
    public void Trigger_MinionCountHasItsTwoKeys()
    {
        Assert.That(
            TriggerText.KeyFor(TriggerField.MinionCount, TriggerComparison.Below),
            Is.EqualTo(new LocKey("trigger.minionCount.below")));

        Assert.That(
            TriggerText.KeyFor(TriggerField.MinionCount, TriggerComparison.AtLeast),
            Is.EqualTo(new LocKey("trigger.minionCount.atLeast")));

        // The tenth field reuses one of the four units rather than adding a fifth, which is what
        // TriggerUnit's own remarks predicted a tenth field would do: a Wight is a body, and bodies
        // are counted. `EveryTriggerKey_ResolvesInEnglish` is what proves the rows exist.
        Assert.That(TriggerText.UnitOf(TriggerField.MinionCount), Is.EqualTo(TriggerUnit.Count));
    }

    // ---- Rules 10 and 11: the verb --------------------------------------------------------------

    [Test]
    public void Raise_StandsUpItsAuthoredCount()
    {
        _raise.Apply(Exhume(), _source);

        Assert.That(_minions.Count, Is.EqualTo(ExhumeCount));
        Assert.That(_events.Count<MinionSpawned>(), Is.EqualTo(ExhumeCount));

        ReadOnlySpan<MinionAgent> alive = _minions.Alive;

        for (int i = 0; i < alive.Length; i++)
        {
            Assert.That(
                XZDistance(alive[i].Position, Vector3.Zero),
                Is.EqualTo(ExhumeRadius).Within(Tolerance),
                "Every one of them is on the ring, not merely inside it.");
        }

        Assert.That(
            alive[0].ExpiresAt,
            Is.EqualTo(Lifespan).Within(Tolerance),
            "A raised Wight's twenty seconds start from the run's simulated clock.");
    }

    [Test]
    public void Raise_IsOnADerivedRingAndDrawsNothing()
    {
        // The structural half first: nothing in this handler's door is a source of randomness, so
        // there is no stream for it to spend (ADR-0011). Asked by reflection rather than by reading
        // the body, which is GrantShieldTests' own shape for "the clock is the simulated one".
        ParameterInfo[] parameters =
            typeof(RaiseMinionsHandler).GetConstructors()[0].GetParameters();

        for (int i = 0; i < parameters.Length; i++)
        {
            Assert.That(
                typeof(IRandom).IsAssignableFrom(parameters[i].ParameterType)
                    || typeof(IRandomStream).IsAssignableFrom(parameters[i].ParameterType),
                Is.False,
                "A cast the player chose the moment of is the worst place to spend a draw: two "
                    + "runs on one seed would diverge on the frame a thumb landed.");
        }

        var random = new FixedRandom(Seed, 0.1f, 0.2f, 0.3f);
        RandomState before = random.Capture();

        Vector3[] first = PointsOfOneCast();

        for (int i = 0; i < 9; i++)
        {
            PointsOfOneCast();
        }

        Assert.That(random.Capture(), Is.EqualTo(before), "Ten casts, every stream unadvanced.");

        Vector3[] second = PointsOfOneCast();

        Assert.That(
            second,
            Is.EqualTo(first),
            "Two casts from the same position stand them in the same three places — derived, not "
                + "drawn.");
    }

    [Test]
    public void Raise_FollowsThePlayer()
    {
        var where = new Vector3(12f, 0f, -4f);

        // Written onto the blackboard rather than passed in, which is the claim: the handler reads
        // this frame's feet off the table PlayerCombat fills.
        _combat.Blackboard.PlayerPosition = where;

        _raise.Apply(Exhume(), _source);

        ReadOnlySpan<MinionAgent> alive = _minions.Alive;

        Assert.That(alive.Length, Is.EqualTo(ExhumeCount));

        for (int i = 0; i < alive.Length; i++)
        {
            Assert.That(
                XZDistance(alive[i].Position, where),
                Is.EqualTo(ExhumeRadius).Within(Tolerance));

            Assert.That(alive[i].Position.Y, Is.EqualTo(where.Y).Within(Tolerance));
        }
    }

    [Test]
    public void Raise_AtTheCapRaisesWhatItCan()
    {
        _minions.Spawn(Vector3.Zero, now: 0f);
        _minions.Spawn(Vector3.UnitX, now: 0f);

        _events.Clear();

        Assert.DoesNotThrow(() => _raise.Apply(Exhume(), _source));

        Assert.That(_minions.Count, Is.EqualTo(Cap), "One more stands; the other two are refused.");

        Assert.That(
            _events.Count<MinionSpawned>(),
            Is.EqualTo(1),
            "No event for the two refused — a refused spawn is silent (rule 11).");
    }

    [Test]
    public void Raise_AtAFullArmyIsSilent()
    {
        for (int i = 0; i < Cap; i++)
        {
            _minions.Spawn(new Vector3(i, 0f, 0f), now: 0f);
        }

        _events.Clear();

        Assert.DoesNotThrow(() => _raise.Apply(Exhume(), _source));

        Assert.That(_minions.Count, Is.EqualTo(Cap));
        Assert.That(_events.All, Is.Empty, "Nothing stands, nothing throws, nothing is published.");
    }

    [Test]
    public void Raise_TheNewWightsCarryTheRecipe()
    {
        _stats.Apply(Buff(PlayerStat.ContactDamage), _source);

        _raise.Apply(Exhume(), _source);

        ReadOnlySpan<MinionAgent> alive = _minions.Alive;

        Assert.That(alive.Length, Is.EqualTo(ExhumeCount));

        for (int i = 0; i < alive.Length; i++)
        {
            Assert.That(
                alive[i].ContactDamage.Value,
                Is.EqualTo(BuffedDamage).Within(Tolerance),
                "Rules 3 and 10 meeting: the verb stands bodies up and the recipe says what they "
                    + "are.");
        }
    }

    [Test]
    public void Raise_RemoveDoesNothing()
    {
        RaiseMinions effect = Exhume();

        _raise.Apply(effect, _source);

        _raise.Remove(effect, _source);

        Assert.That(
            _minions.Count,
            Is.EqualTo(ExhumeCount),
            "What a cast left behind is a body on a clock rather than a modifier — rule 11.");

        // The null guards are kept anyway, so a mis-wired caller is refused at this door exactly as
        // it is at GrantShieldHandler's.
        Assert.Throws<ArgumentNullException>(() => _raise.Remove(null, _source));
        Assert.Throws<ArgumentNullException>(() => _raise.Remove(effect, null));
    }

    [Test]
    public void Raise_RefusesAnImpossibleCount()
    {
        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(() => new RaiseMinions(0, ExhumeRadius))
                .ParamName,
            Is.EqualTo("count"));

        Assert.That(
            Assert.Throws<ArgumentOutOfRangeException>(() => new RaiseMinions(-1, ExhumeRadius))
                .ParamName,
            Is.EqualTo("count"));

        foreach (float radius in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.That(
                Assert.Throws<ArgumentOutOfRangeException>(
                        () => new RaiseMinions(ExhumeCount, radius))
                    .ParamName,
                Is.EqualTo("radius"),
                $"A radius of {radius} is an authoring mistake rather than a small ring.");
        }
    }

    // ---- Rules 5 and 10 through a whole run -------------------------------------------------------

    [Test]
    public void Start_RefusesAMinionNodeOnAClassWithoutMinions()
    {
        ArgumentException thrown = Assert.Throws<ArgumentException>(
            () => StartWith(OathboundId, NodeCarrying(LegionId, Buff(PlayerStat.ContactDamage))));

        Assert.That(thrown.Message, Does.Contain(LegionId), "It names the node.");
        Assert.That(thrown.Message, Does.Contain(OathboundId), "And the class — rule 5.");

        Assert.That(
            _events.Count<RunStarted>(),
            Is.Zero,
            "Refused before RunStarted: nothing is announced and nothing is standing.");
    }

    [Test]
    public void Start_AcceptsItOnTheGravecaller()
    {
        RunSession session = null;

        Assert.DoesNotThrow(
            () => session = StartWith(
                GravecallerId,
                NodeCarrying(LegionId, Buff(PlayerStat.ContactDamage))));

        Assert.That(session.State, Is.Not.Null);
        Assert.That(_events.Count<RunStarted>(), Is.EqualTo(1), "The run announces normally.");
    }

    [Test]
    public void Registry_HoldsSixPrimitives()
    {
        // A whole run, because CanApply cannot be asked from outside core — RunState.Effects is
        // internal (AR §18.2). SkillTree's constructor sweeps every take and cast effect through
        // CanApply, so a node carrying all six is accepted only if all six are registered.
        RunSession session = null;

        Assert.DoesNotThrow(() => session = StartWith(GravecallerId, NodeCarryingEverything()));

        Assert.That(session.State, Is.Not.Null);

        // And the asymmetry rule 10 leans on: a verb is a *type*, so a run that cannot raise does
        // not register the handler and the same sweep refuses the tree — which is why RaiseMinions
        // needs no bespoke check of its own.
        KeyNotFoundException thrown = Assert.Throws<KeyNotFoundException>(
            () => StartWith(OathboundId, NodeCarrying(ExhumeId, Exhume())));

        Assert.That(thrown.Message, Does.Contain(nameof(RaiseMinions)));
        Assert.That(thrown.Message, Does.Contain(ExhumeId));
    }

    // ---- Allocation ------------------------------------------------------------------------------

    [Test]
    public void Recipe_AllocatesNothing()
    {
        IStatBlock block = new MinionRecipeStats(_recipe);

        AllocationAssert.None(
            () =>
            {
                block.Resolve(PlayerStat.MaxHp);
                block.Resolve(PlayerStat.MoveSpeed);
                block.Resolve(PlayerStat.ContactDamage);

                block.Has(PlayerStat.WeaponRange);
            },
            iterations: 100_000);

        var silent = new SilentEvents();
        var system = new MinionSystem(_wight, _recipe, silent, new RecordingIntents());

        AllocationAssert.None(
            () =>
            {
                system.Spawn(Vector3.Zero, now: 0f);
                system.Clear();
            },
            iterations: 10_000);
    }

    [Test]
    public void Raise_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var system = new MinionSystem(_wight, _recipe, silent, new RecordingIntents());
        EnemySystem enemies = NewEnemies(silent);

        var handler = new RaiseMinionsHandler(system, _combat.Blackboard, _clock);
        RaiseMinions effect = Exhume();

        var now = 0f;

        AllocationAssert.None(
            () =>
            {
                _clock.Now = now;

                handler.Apply(effect, _source);

                // Expired rather than cleared, so the measured cycle is the one a run actually
                // walks: raise, stand, dissolve. The clock runs past the lifespan each iteration.
                now += Lifespan + 1f;

                system.Tick(Lifespan + 1f, now, enemies, _combat);
            },
            iterations: 10_000);
    }

    // ---- Guards -----------------------------------------------------------------------------------

    [Test]
    public void Legion_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new MinionRecipe(null));
        Assert.Throws<ArgumentNullException>(() => new MinionRecipeStats(null));

        Assert.Throws<ArgumentNullException>(() => new ModifyStatHandler(null));

        Assert.Throws<ArgumentNullException>(
            () => new RaiseMinionsHandler(null, _combat.Blackboard, _clock));

        Assert.Throws<ArgumentNullException>(() => new RaiseMinionsHandler(_minions, null, _clock));

        Assert.Throws<ArgumentNullException>(
            () => new RaiseMinionsHandler(_minions, _combat.Blackboard, null));

        Assert.Throws<ArgumentNullException>(() => _raise.Apply(null, _source));

        // The widened enums, both asked the way M4-01a asks: a member cast in from an int that has
        // no case is refused where it is authored rather than where it is read.
        Assert.That(Enum.IsDefined(typeof(StatTarget), StatTarget.Minions), Is.True);
        Assert.That(Enum.IsDefined(typeof(TriggerField), TriggerField.MinionCount), Is.True);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TriggerClause((TriggerField)99, TriggerComparison.Below, 1f)
                .IsMet(new CombatBlackboard()));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => TriggerText.KeyFor((TriggerField)99, TriggerComparison.Below));

        Assert.Throws<ArgumentOutOfRangeException>(() => TriggerText.UnitOf((TriggerField)99));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    private static ContentId Id(string value) => new ContentId(value);

    /// <summary>CH §3.2's Legion damage node, as the one effect every rule 1 row applies.</summary>
    private static ModifyStat Buff(PlayerStat stat) =>
        new ModifyStat(stat, ModifierKind.PercentAdd, LegionDamageBonus, StatTarget.Minions);

    /// <summary>CH §4.2's Exhume, at the numbers M5-06b will author.</summary>
    private static RaiseMinions Exhume() => new RaiseMinions(ExhumeCount, ExhumeRadius);

    private static float XZDistance(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;

        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>The message a block refuses <paramref name="member"/> with.</summary>
    private static string Refusal(IStatBlock block, PlayerStat member)
    {
        return Assert.Throws<ArgumentOutOfRangeException>(() => block.Resolve(member)).Message;
    }

    /// <remarks>
    /// <c>StatBlockTests.AssertHasAgreesWithResolve</c>'s shape, kept local rather than shared: the
    /// two fixtures are in different files for different subjects, and a helper reached across
    /// would make one of them fail for the other's reason.
    /// </remarks>
    private static void AssertHasAgreesWithResolve(IStatBlock block, string what)
    {
        foreach (PlayerStat member in Enum.GetValues(typeof(PlayerStat)))
        {
            bool resolved;

            try
            {
                block.Resolve(member);
                resolved = true;
            }
            catch (ArgumentOutOfRangeException)
            {
                resolved = false;
            }

            Assert.That(
                block.Has(member),
                Is.EqualTo(resolved),
                $"{what}.Has({member}) disagrees with Resolve. An address that answers one way "
                    + "here and the other way there is a handler refusing what it could do, or "
                    + "applying what it cannot.");
        }
    }

    /// <summary>One cast into a fresh army, and where the bodies ended up.</summary>
    private Vector3[] PointsOfOneCast()
    {
        _minions.Clear();

        _raise.Apply(Exhume(), _source);

        ReadOnlySpan<MinionAgent> alive = _minions.Alive;
        var points = new Vector3[alive.Length];

        for (int i = 0; i < alive.Length; i++)
        {
            points[i] = alive[i].Position;
        }

        return points;
    }

    /// <summary>One tick of the combat step, which is the thing that writes the blackboard.</summary>
    private void Tick(WorldSnapshot snapshot, MinionSystem minions)
    {
        snapshot.Clear();
        snapshot.Dt = 1f / 60f;

        _combat.Tick(
            snapshot.Dt,
            now: 0f,
            snapshot,
            ReadOnlySpan<EnemyAgent>.Empty,
            Vector3.UnitZ,
            lures: null,
            minions);
    }

    private EnemySystem NewEnemies(IDomainEvents events) => new EnemySystem(
        Catalog(),
        events,
        new FixedRandom(0.1f),
        new DepthScaling(Scalings.Design()),
        Capacity);

    /// <summary>A Passive carrying one effect — the node M5-06b will author, without the asset.</summary>
    private static SkillSpec NodeCarrying(string id, IEffect effect) =>
        new SkillSpec(
            Id(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            SkillKind.Passive,
            new[] { effect });

    /// <summary>A Passive carrying every primitive this run is meant to know.</summary>
    private static SkillSpec NodeCarryingEverything() =>
        new SkillSpec(
            Id(EverythingId),
            new LocKey($"{EverythingId}.name"),
            new LocKey($"{EverythingId}.desc"),
            SkillKind.Passive,
            new IEffect[]
            {
                new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.1f),
                new GrantShield(35f, 5f),
                new SpawnHealZone(3.5f, 6f, 3f, 0.5f),
                new KnockbackOnSwing(1.5f),
                new ModifySkillCooldown(Id(ExhumeId), ModifierKind.PercentMult, -0.25f),
                Exhume(),
            });

    /// <summary>A run for <paramref name="characterId"/> whose tree holds exactly one node.</summary>
    private RunSession StartWith(string characterId, SkillSpec node)
    {
        // Three branches because SkillTreeSpec holds three, and the node under test shares the
        // tree with two inert fillers: a branch cannot hold the same node twice, and a filler that
        // carried nothing would not be a legal Passive.
        SkillSpec fillerB = Filler('b');
        SkillSpec fillerC = Filler('c');

        var tree = new SkillTreeSpec(
            Id(TreeId),
            Id(characterId),
            new[]
            {
                Branch('a', node.Id),
                Branch('b', fillerB.Id),
                Branch('c', fillerC.Id),
            });

        var catalog = new ContentCatalog(
            new[] { Gravecaller(), Oathbound() },
            new[] { Husk() },
            new[] { Mode() },
            new[] { node, fillerB, fillerC },
            new[] { tree });

        var random = new FixedRandom(Seed, 0.1f, 0.2f, 0.3f);

        var session = new RunSession(
            catalog,
            random,
            _events,
            _intents,
            new RunRecorder(random, new FixedClock(Instant), _events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        session.Start(new RunConfig(
            Id(ModeId),
            Id(characterId),
            random.Seed,
            stageIndex: 1,
            SpawnPlan.Empty,
            restore: null));

        return session;
    }

    /// <summary>An inert Passive, so the other two branches hold something legal.</summary>
    private static SkillSpec Filler(char letter) =>
        NodeCarrying(
            $"skill.test.filler{letter}",
            new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.01f));

    private static SkillBranchSpec Branch(char letter, ContentId node) =>
        new SkillBranchSpec(
            new LocKey($"branch.{letter}"),
            new IReadOnlyList<ContentId>[] { new[] { node } });

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

    /// <summary><c>Gravecaller.asset</c>'s minion block (M5-02).</summary>
    private static MinionSpec Wight() => new MinionSpec(
        Id(WightId),
        new LocKey("minion.wight.name"),
        Cap,
        Lifespan,
        riseChance: 0.25f,
        MinionMaxHp,
        MinionSpeed,
        MinionDamage,
        AttackInterval,
        Reach);

    /// <summary>The class that raises the dead — CH §3.2, at <c>Gravecaller.asset</c>'s numbers.</summary>
    private static CharacterSpec Gravecaller() => new CharacterSpec(
        Id(GravecallerId),
        new LocKey("character.gravecaller.name"),
        new LocKey("character.gravecaller.description"),
        GravecallerMaxHp,
        new MovementSpec(3.1f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 9f, 4f, 12f, 360f, 0.15f, 40f, 0.8f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(
            MovementSkillKind.Shroudstep, 6f, 0.05f, 2.5f, 0.15f, 0f, 0f, 0.05f, decoyDuration: 3f),
        shield: null,
        hitIFrames: 0.5f,
        minions: Wight());

    /// <summary>The class that raises nothing, which is what rule 5's refusal is about.</summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        Id(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        OathboundMaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        hitIFrames: 0.5f);

    /// <summary>
    /// A Static Husk: the catalog needs one body and no row here is about what an enemy does.
    /// </summary>
    private static EnemySpec Husk() => new EnemySpec(
        Id(HuskId),
        new LocKey("enemy.husk.name"),
        36f,
        2f,
        1,
        threatCost: 4,
        xpValue: 10f,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    private ContentCatalog Catalog() => new ContentCatalog(
        new[] { Gravecaller(), Oathbound() },
        new[] { Husk() },
        new[] { Mode() });
}
