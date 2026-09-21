using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

/// <summary>
/// The seam M4-01a exists for: that an address resolves against <em>whoever it is aimed at</em>,
/// that a block with no such number refuses rather than inventing one, and that aiming a handler is
/// a scope rather than a default.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two implementations answering the same question, which is the whole claim.</b>
/// <c>PlayerStats</c> shipped at M3-05 and <c>CombatantStats</c> is new; if the rows about them
/// could not be written against the same interface, <c>IStatBlock</c> would be a rename rather than
/// a seam. Both sides are built from real objects — a real <c>PlayerCombat</c>, a real
/// <c>EnemyAgent</c> out of a real <c>EnemySystem</c> — for <c>ModifyStatTests</c>' reason: the
/// claim is that the address reaches the numbers the game actually plays with.
/// </para>
/// <para>
/// <b>Nothing here is reachable in play.</b> No boss exists, no enemy holds a skill and no shipped
/// asset authors <see cref="StatTarget.Self"/>, so every row in this fixture is about a seam rather
/// than about a behaviour a player can see. That is M3-05's and M3-12a's bargain again, and it is
/// what makes <c>Player_BehaviourIsUnchanged</c> the row that matters most.
/// </para>
/// <para>
/// <b>A third implementation arrived at M5-04b, which is what turns the claim above into a
/// measurement.</b> <see cref="MinionStats"/> is <see cref="CombatantStats"/>' mirror over a body
/// that is neither the player nor an enemy, and <c>Minion_AndCombatantAnswerTheSameSet</c> is the
/// row that says so in a form a later generalisation has to argue with: the same three of twelve
/// answered, the same nine refused, and <b>different messages</b>, because a refusal that could not
/// name its content would be one diagnostic for two different authoring mistakes (M5-04b rule 6).
/// </para>
/// </remarks>
[TestFixture]
public sealed class StatBlockTests
{
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string WightId = "minion.wight";
    private const string DescentId = "mode.descent";

    /// <summary>GD §8.1's Husk, and the numbers every combatant row here is over.</summary>
    private const float HuskMaxHp = 36f;

    private const float HuskMoveSpeed = 3.5f;
    private const float HuskContactDamage = 8f;

    private const float WeaponDamage = 13f;
    private const float PlayerMaxHp = 140f;
    private const float PlayerMoveSpeed = 3f;

    /// <summary>The Gravecaller's authored Wight (M5-02), for the third block's rows.</summary>
    private const float MinionMaxHp = 20f;

    private const float MinionMoveSpeed = 3f;
    private const float MinionDamage = 8f;
    private const float MinionReach = 1.5f;

    private const int Capacity = 8;
    private const float Tolerance = 1e-3f;

    /// <summary>One frame, for the one row that has to make a Wight actually swing.</summary>
    private const float Frame = 0.1f;

    private SilentEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private MinionSystem _minions;

    [SetUp]
    public void SetUp()
    {
        _events = new SilentEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(
            Catalog(),
            _events,
            new FixedRandom(),
            new DepthScaling(Scalings.Design()),
            Capacity);

        MinionSpec wight = WightSpec();

        _minions = new MinionSystem(wight, new MinionRecipe(wight), _events, _intents);
    }

    // ---- Rule 1 and rule 8: the player is a block, and is otherwise untouched --------------------

    [Test]
    public void Player_ImplementsTheBlock()
    {
        PlayerCombat combat = Combat();
        PlayerMotor motor = Motor();
        LevelTracker progression = Progression();

        var stats = new PlayerStats(combat, motor, progression);
        IStatBlock block = stats;

        foreach (PlayerStat member in Enum.GetValues(typeof(PlayerStat)))
        {
            // The one address the player does not have. Its refusal is ModifyStatTests'
            // Stats_ResolveEveryMember; here it is skipped so this row can say the thing it is
            // about, which is that the interface and the class are the same method.
            if (member == PlayerStat.ContactDamage)
            {
                continue;
            }

            // SameAs, never EqualTo: an address that handed back a copy holding the same number
            // would pass an equality check and fail the game, and reaching the very instance is
            // the entire contract IStatBlock exists to state.
            Assert.That(
                block.Resolve(member),
                Is.SameAs(stats.Resolve(member)),
                $"PlayerStat.{member} through the interface must be the very stat the class hands "
                    + "back. A block that resolved differently would be a second address table.");
        }
    }

    [Test]
    public void Player_HasAgreesWithResolve()
    {
        IStatBlock block = new PlayerStats(Combat(), Motor(), Progression());

        AssertHasAgreesWithResolve(block, "PlayerStats");
    }

    [Test]
    public void Player_BehaviourIsUnchanged()
    {
        // The row that makes M4-01a a widening rather than a rewrite. Every one of the player's
        // eleven addresses is moved through the handler and compared against the same modifier put
        // on the same stat by hand — so if aiming had leaked into the default path at all, by a
        // stale scope or by a block resolved from the wrong side, the number would differ here.
        foreach (PlayerStat member in Enum.GetValues(typeof(PlayerStat)))
        {
            if (member == PlayerStat.ContactDamage)
            {
                continue;
            }

            PlayerCombat combat = Combat();
            PlayerMotor motor = Motor();
            LevelTracker progression = Progression();
            var stats = new PlayerStats(combat, motor, progression);

            float before = stats.Resolve(member).Value;

            // Flat and both percentage kinds, because "every modifier path" is three paths and a
            // handler that aimed correctly for one of them is not a handler that works.
            var node = new object();
            var handler = new ModifyStatHandler(stats);

            handler.Apply(new ModifyStat(member, ModifierKind.Flat, 2f), node);
            handler.Apply(new ModifyStat(member, ModifierKind.PercentAdd, 0.15f), node);
            handler.Apply(new ModifyStat(member, ModifierKind.PercentMult, 0.5f), node);

            float actual = stats.Resolve(member).Value;

            // The same arithmetic, stated independently: (base + flat) × (1 + add) × (1 + mult).
            float expected = (before + 2f) * 1.15f * 1.5f;

            Assert.That(
                actual,
                Is.EqualTo(expected).Within(Tolerance),
                $"PlayerStat.{member} must move exactly as it did before there was a target.");

            handler.Remove(new ModifyStat(member, ModifierKind.Flat, 2f), node);

            Assert.That(
                stats.Resolve(member).Value,
                Is.EqualTo(before).Within(Tolerance),
                $"PlayerStat.{member} must come all the way back — Remove takes the source, which "
                    + "is all three of this node's modifiers.");
        }
    }

    // ---- Rule 2: the combatant block, and what it refuses ----------------------------------------

    [Test]
    public void Combatant_AnswersItsThreeStats()
    {
        EnemyAgent agent = Husk();
        IStatBlock block = new CombatantStats(agent);

        Assert.That(block.Resolve(PlayerStat.MaxHp), Is.SameAs(agent.Health.MaxHp));
        Assert.That(block.Resolve(PlayerStat.MoveSpeed), Is.SameAs(agent.MoveSpeed));
        Assert.That(block.Resolve(PlayerStat.ContactDamage), Is.SameAs(agent.ContactDamage));

        // Not a copy: a modifier put on what came back has to move the number the behaviour reads.
        block.Resolve(PlayerStat.MoveSpeed).Add(new Modifier(ModifierKind.Flat, 1f, new object()));

        Assert.That(
            agent.MoveSpeed.Value,
            Is.EqualTo(HuskMoveSpeed + 1f).Within(Tolerance),
            "The block hands back the agent's live stat, so a modifier on it moves the agent.");
    }

    [Test]
    public void Combatant_RefusesAnAddressItDoesNotHave()
    {
        IStatBlock block = new CombatantStats(Husk());

        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => block.Resolve(PlayerStat.WeaponRange),
            "A Husk has no weapon range. Minting a Stat here would put a modifier on a number "
                + "nothing reads, which no check in this project can see.");

        // Both halves of the message, because a content error has to say which content: the
        // address alone does not tell a designer which asset to open, and the spec id alone does
        // not say what was asked of it.
        Assert.That(thrown.Message, Does.Contain(nameof(PlayerStat.WeaponRange)));
        Assert.That(thrown.Message, Does.Contain(HuskId));
    }

    [Test]
    public void Combatant_HasAgreesWithResolve()
    {
        IStatBlock block = new CombatantStats(Husk());

        AssertHasAgreesWithResolve(block, "CombatantStats");

        // Stated as well as derived, so that the row says *which* three rather than only that the
        // two methods agree — a block that answered nothing at all would satisfy the sweep above.
        Assert.That(block.Has(PlayerStat.MaxHp), Is.True);
        Assert.That(block.Has(PlayerStat.MoveSpeed), Is.True);
        Assert.That(block.Has(PlayerStat.ContactDamage), Is.True);
        Assert.That(block.Has(PlayerStat.WeaponDamage), Is.False);
    }

    [Test]
    public void Combatant_NullAgent_Throws()
    {
        Assert.That(() => new CombatantStats(null), Throws.ArgumentNullException);
    }

    // ---- M5-04b rules 6 and 7: the third block ----------------------------------------------------

    [Test]
    public void Minion_ImplementsTheBlock()
    {
        MinionAgent wight = Wight();
        IStatBlock block = new MinionStats(wight);

        // SameAs, never EqualTo, for Combatant_AnswersItsThreeStats' reason: an address that handed
        // back a copy holding 20 would pass every equality check in this file and move nothing.
        Assert.That(block.Resolve(PlayerStat.MaxHp), Is.SameAs(wight.Health.MaxHp));
        Assert.That(block.Resolve(PlayerStat.MoveSpeed), Is.SameAs(wight.MoveSpeed));
        Assert.That(block.Resolve(PlayerStat.ContactDamage), Is.SameAs(wight.ContactDamage));

        block.Resolve(PlayerStat.MoveSpeed).Add(new Modifier(ModifierKind.Flat, 1f, new object()));

        Assert.That(
            wight.MoveSpeed.Value,
            Is.EqualTo(MinionMoveSpeed + 1f).Within(Tolerance),
            "The block hands back the agent's live stat, so a modifier on it moves the Wight.");
    }

    [Test]
    public void Minion_RefusesAnAddressItDoesNotHave()
    {
        IStatBlock block = new MinionStats(Wight());

        ArgumentOutOfRangeException thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => block.Resolve(PlayerStat.WeaponRange),
            "A Wight has no weapon range. Minting a Stat here would put a modifier on a number "
                + "nothing reads, which no check in this project can see.");

        Assert.That(thrown.Message, Does.Contain(nameof(PlayerStat.WeaponRange)));
        Assert.That(thrown.Message, Does.Contain(WightId));
    }

    [Test]
    public void Minion_HasAgreesWithResolve()
    {
        IStatBlock block = new MinionStats(Wight());

        AssertHasAgreesWithResolve(block, "MinionStats");

        Assert.That(block.Has(PlayerStat.MaxHp), Is.True);
        Assert.That(block.Has(PlayerStat.MoveSpeed), Is.True);
        Assert.That(block.Has(PlayerStat.ContactDamage), Is.True);
        Assert.That(block.Has(PlayerStat.WeaponDamage), Is.False);
    }

    [Test]
    public void Minion_AndCombatantAnswerTheSameSet()
    {
        // **The row a later generalisation has to argue with** (M5-04b rule 6). A shared base class
        // would remove about ten lines and would also make the two messages one — and
        // "'enemy.husk' has no stat at address WeaponRange" and "'minion.wight' has no stat at
        // address WeaponRange" are different facts for different authors (M4-01a rule 2).
        IStatBlock combatant = new CombatantStats(Husk());
        IStatBlock minion = new MinionStats(Wight());

        var answered = 0;
        var refused = 0;

        foreach (PlayerStat member in Enum.GetValues(typeof(PlayerStat)))
        {
            Assert.That(
                minion.Has(member),
                Is.EqualTo(combatant.Has(member)),
                $"PlayerStat.{member} is answered by one body and not the other. The three a "
                    + "MinionAgent carries are the three an EnemyAgent carries, deliberately "
                    + "(M5-04a rule 2) — a difference here is one of the two having drifted.");

            if (minion.Has(member))
            {
                answered++;
                continue;
            }

            refused++;

            string combatantMessage = Refusal(combatant, member);
            string minionMessage = Refusal(minion, member);

            Assert.That(
                combatantMessage, Does.Contain(HuskId), $"PlayerStat.{member} on the Husk.");
            Assert.That(
                minionMessage, Does.Contain(WightId), $"PlayerStat.{member} on the Wight.");
            Assert.That(
                minionMessage,
                Is.Not.EqualTo(combatantMessage),
                $"PlayerStat.{member} refuses with the same words on both, so one message is now "
                    + "serving two content errors.");
        }

        Assert.That(answered, Is.EqualTo(3), "MaxHp, MoveSpeed and ContactDamage, and no fourth.");
        Assert.That(refused, Is.EqualTo(9), "The other nine of twelve are player numbers.");
    }

    [Test]
    public void Minion_NullAgent_Throws()
    {
        Assert.That(() => new MinionStats(null), Throws.ArgumentNullException);
    }

    [Test]
    public void Modify_ReachesAWightsDamage()
    {
        // **What M5-04b actually ships for CH §3.2's Legion branch** (rule 9): a Wight has an
        // address book and a live ContactDamage a modifier can sit on. *Who* is allowed to put one
        // there — "every Wight I own" is neither Player nor Self — is M5-06's first question.
        MinionAgent wight = Wight();
        PlayerCombat combat = Combat();
        var stats = new PlayerStats(combat, Motor(), Progression());
        var handler = new ModifyStatHandler(stats);

        EnemyAgent husk = HuskAt(new Vector3(1f, 0f, 0f));

        using (handler.Aiming(new MinionStats(wight)))
        {
            handler.Apply(
                new ModifyStat(PlayerStat.ContactDamage, ModifierKind.PercentAdd, 0.2f, StatTarget.Self),
                new object());
        }

        _minions.Tick(Frame, now: 0f, _system, combat);

        // Read off the Husk rather than off the stat, which is the whole point: the node named a
        // member of an enum and the number a *strike* costs moved.
        Assert.That(
            husk.Health.Current,
            Is.EqualTo(HuskMaxHp - (MinionDamage * 1.2f)).Within(Tolerance),
            "A +20 % ContactDamage aimed at the Wight is 20 % more damage on the swing it throws.");

        Assert.That(
            combat.Weapon.Damage.Value,
            Is.EqualTo(WeaponDamage).Within(Tolerance),
            "And the player's numbers did not move — which is the entire point of the seam.");
    }

    [Test]
    public void PlayerStat_GainedNothing()
    {
        // **The row that proves M4-01a rule 1 was about the design rather than about the one case in
        // front of it** (M5-04b rule 8). A Wight is the second caller that is not the player, and it
        // needs no new member — so the shared address space has now been asked for by two things and
        // widened by neither.
        //
        // The names in ordinal order, not merely the count: every authored ModifyStatDefinition
        // serialises `_stat` as a raw int, so a member *inserted* rather than appended silently
        // re-points every asset that names one after it. PlayerStatCoverageTests counts twelve; this
        // says which twelve and in what order.
        Assert.That(Enum.GetNames(typeof(PlayerStat)), Is.EqualTo(new[]
        {
            nameof(PlayerStat.MaxHp),
            nameof(PlayerStat.WeaponDamage),
            nameof(PlayerStat.FireRate),
            nameof(PlayerStat.MoveSpeed),
            nameof(PlayerStat.MovementSkillCooldown),
            nameof(PlayerStat.XpGain),
            nameof(PlayerStat.WeaponRange),
            nameof(PlayerStat.WeaponConeAngle),
            nameof(PlayerStat.ChargeDamage),
            nameof(PlayerStat.ShieldRechargeDelay),
            nameof(PlayerStat.HealPerKill),
            nameof(PlayerStat.ContactDamage),
        }), "The same twelve members as after M4-01a, in the same order. A new member goes *after* "
            + "ContactDamage and updates this row saying which task added it.");
    }

    // ---- Rules 3 and 4: the target, and the default that keeps the ripple at nothing --------------

    [Test]
    public void Modify_DefaultsToThePlayer()
    {
        PlayerCombat combat = Combat();
        var stats = new PlayerStats(combat, Motor(), Progression());
        var handler = new ModifyStatHandler(stats);

        var effect = new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 15f);

        Assert.That(effect.Target, Is.EqualTo(StatTarget.Player), "Three arguments still mean the player.");

        // No scope open, and it lands anyway — which is the whole of rule 4: every call site
        // written before there was a choice keeps meaning what it meant.
        handler.Apply(effect, new object());

        Assert.That(
            combat.Health.MaxHp.Value,
            Is.EqualTo(PlayerMaxHp + 15f).Within(Tolerance));
    }

    [Test]
    public void Effect_RefusesAnUndefinedTarget()
    {
        Assert.That(
            () => new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f, (StatTarget)99),
            Throws.TypeOf<ArgumentOutOfRangeException>(),
            "Refused where the effect is authored, like the kind and the value — the target has no "
                + "second door, so a stale ordinal would otherwise fall out of the handler mid-run.");
    }

    // ---- Rule 5: aimed for one call, and never defaulted -----------------------------------------

    [Test]
    public void Modify_SelfLandsOnTheCaster()
    {
        EnemyAgent agent = Husk();
        PlayerCombat combat = Combat();
        PlayerMotor motor = Motor();
        var handler = new ModifyStatHandler(new PlayerStats(combat, motor, Progression()));

        var effect = new ModifyStat(PlayerStat.MoveSpeed, ModifierKind.PercentAdd, -0.5f, StatTarget.Self);

        using (handler.Aiming(new CombatantStats(agent)))
        {
            handler.Apply(effect, new object());
        }

        Assert.That(
            agent.MoveSpeed.Value,
            Is.EqualTo(HuskMoveSpeed * 0.5f).Within(Tolerance),
            "The caster's number moved.");

        Assert.That(
            motor.Speed.Value,
            Is.EqualTo(PlayerMoveSpeed).Within(Tolerance),
            "And the player's did not — which is the entire point of the seam.");
    }

    [Test]
    public void Modify_SelfOutsideAScopeThrows()
    {
        PlayerMotor motor = Motor();
        var handler = new ModifyStatHandler(new PlayerStats(Combat(), motor, Progression()));

        var effect = new ModifyStat(PlayerStat.MoveSpeed, ModifierKind.PercentAdd, -0.5f, StatTarget.Self);

        Assert.That(
            () => handler.Apply(effect, new object()),
            Throws.TypeOf<InvalidOperationException>(),
            "A Self effect with nothing aimed at is refused rather than defaulted.");

        // The half that matters more than the throw, and the reason this row exists at all: a
        // fallback to the player would move a real number by the authored amount, report success,
        // leave nothing null and turn no test red. Traps §1's family exactly.
        Assert.That(
            motor.Speed.Value,
            Is.EqualTo(PlayerMoveSpeed).Within(Tolerance),
            "And it did not land on the player on the way out.");

        Assert.That(
            motor.Speed.ModifierCount,
            Is.Zero,
            "Not even as a modifier that happens to cancel — nothing was added at all.");
    }

    [Test]
    public void Modify_SelfIsRemovedFromTheCaster()
    {
        EnemyAgent agent = Husk();
        var handler = new ModifyStatHandler(new PlayerStats(Combat(), Motor(), Progression()));

        var effect = new ModifyStat(PlayerStat.MoveSpeed, ModifierKind.PercentAdd, -0.5f, StatTarget.Self);
        var source = new object();

        // Counted rather than assumed zero: a spawned agent already wears DepthScaling's
        // modifiers, which at depth 1 multiply by exactly 1 and are therefore invisible in the
        // value. Asserting an empty stack here would be asserting the depth curve is off.
        float baseline = agent.MoveSpeed.Value;
        int wornAtSpawn = agent.MoveSpeed.ModifierCount;

        using (handler.Aiming(new CombatantStats(agent)))
        {
            handler.Apply(effect, source);
            handler.Remove(effect, source);
        }

        Assert.That(
            agent.MoveSpeed.Value,
            Is.EqualTo(baseline).Within(Tolerance),
            "Back to where it started.");

        Assert.That(
            agent.MoveSpeed.ModifierCount,
            Is.EqualTo(wornAtSpawn),
            "And the source is gone, not merely inert — what the agent wore before is all it wears.");
    }

    [Test]
    public void Aiming_ClearsOnDispose()
    {
        EnemyAgent agent = Husk();
        var handler = new ModifyStatHandler(new PlayerStats(Combat(), Motor(), Progression()));

        var effect = new ModifyStat(PlayerStat.MoveSpeed, ModifierKind.Flat, 1f, StatTarget.Self);

        using (handler.Aiming(new CombatantStats(agent)))
        {
            handler.Apply(effect, new object());
        }

        // A leaked scope must not be able to aim at a stale agent — one that has since been
        // despawned and handed back out as a different archetype, which is exactly what
        // EnemyRegistry does with every agent it owns.
        Assert.That(
            () => handler.Apply(effect, new object()),
            Throws.TypeOf<InvalidOperationException>(),
            "Disposing un-aims, so the next Self apply has nothing to land on.");
    }

    [Test]
    public void Aiming_RefusesToNest()
    {
        var handler = new ModifyStatHandler(new PlayerStats(Combat(), Motor(), Progression()));
        IStatBlock first = new CombatantStats(Husk());
        IStatBlock second = new CombatantStats(Husk());

        using (handler.Aiming(first))
        {
            Assert.That(
                () => handler.Aiming(second),
                Throws.TypeOf<InvalidOperationException>(),
                "A second aim would either shadow the first or silently restore it on the inner "
                    + "dispose, and both are a buff landing on the wrong body with nothing said.");
        }
    }

    [Test]
    public void Aiming_NullBlock_Throws()
    {
        var handler = new ModifyStatHandler(new PlayerStats(Combat(), Motor(), Progression()));

        Assert.That(() => handler.Aiming(null), Throws.ArgumentNullException);
    }

    [Test]
    public void Aiming_AllocatesNothing()
    {
        EnemyAgent agent = Husk();
        var handler = new ModifyStatHandler(new PlayerStats(Combat(), Motor(), Progression()));
        IStatBlock block = new CombatantStats(agent);

        var effect = new ModifyStat(PlayerStat.MoveSpeed, ModifierKind.Flat, 1f, StatTarget.Self);
        var source = new object();

        // Everything the body touches is built above, so what is measured is the aim, the resolve
        // and the modifier — not a fixture. A scope allocated per call would show up here, which
        // is why the handler builds one and hands the same instance out every time.
        AllocationAssert.None(() =>
        {
            using (handler.Aiming(block))
            {
                handler.Apply(effect, source);
                handler.Remove(effect, source);
            }
        });
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    /// <summary>
    /// Every member of <see cref="PlayerStat"/> asked of both methods: <c>Has</c> must be true
    /// exactly when <c>Resolve</c> does not throw.
    /// </summary>
    /// <remarks>
    /// Walks <see cref="Enum.GetValues(Type)"/> rather than a list somebody has to remember to
    /// extend, which is <c>Trigger_EveryFieldReads</c>' shape: a member added to the enum and to
    /// one of the two switches fails here.
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

    /// <summary>
    /// The refusal message <paramref name="block"/> gives for an address it does not have.
    /// </summary>
    /// <exception cref="AssertionException">It answered instead of refusing.</exception>
    private static string Refusal(IStatBlock block, PlayerStat member)
    {
        try
        {
            block.Resolve(member);
        }
        catch (ArgumentOutOfRangeException thrown)
        {
            return thrown.Message;
        }

        Assert.Fail($"{block.GetType().Name} answered PlayerStat.{member} rather than refusing it.");

        return null;
    }

    private EnemyAgent Husk() => HuskAt(Vector3.Zero);

    private EnemyAgent HuskAt(Vector3 position) => _system.Spawn(new ContentId(HuskId), position);

    /// <summary>
    /// A standing Wight, out of a real <see cref="MinionSystem"/> — the only thing that makes one.
    /// </summary>
    private MinionAgent Wight() => _minions.Spawn(Vector3.Zero, now: 0f);

    private PlayerCombat Combat() =>
        new PlayerCombat(Character(), _events, _intents, Capacity);

    private LevelTracker Progression() => new LevelTracker(Scalings.Xp(), _events);

    private static PlayerMotor Motor() =>
        new PlayerMotor(new MovementSpec(PlayerMoveSpeed, 0.06f, 0.08f, 720f), Vector3.UnitZ);

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Character() },
        new[] { HuskSpec(HuskId) },
        new[] { Descent() });

    private static EnemySpec HuskSpec(string id) => new EnemySpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        maxHp: HuskMaxHp,
        moveSpeed: HuskMoveSpeed,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        contactDamage: HuskContactDamage,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    /// <summary>The Gravecaller's authored minion block (M5-02), for the third block's rows.</summary>
    private static MinionSpec WightSpec() => new MinionSpec(
        new ContentId(WightId),
        new LocKey("minion.wight.name"),
        cap: 3,
        lifespan: 20f,
        riseChance: 0.25f,
        MinionMaxHp,
        MinionMoveSpeed,
        MinionDamage,
        attackInterval: 1f,
        MinionReach);

    private static ModeSpec Descent() => new ModeSpec(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>());

    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        PlayerMaxHp,
        new MovementSpec(PlayerMoveSpeed, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);
}
