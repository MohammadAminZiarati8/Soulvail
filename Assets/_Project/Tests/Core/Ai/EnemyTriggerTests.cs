using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// The other half of M4-01a: that an enemy's blackboard carries what a trigger asks about the enemy
/// <em>itself</em>, published on the tick the rest of its perception is, and readable by the
/// <c>TriggerSpec</c> the game already ships.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>EnemyBlackboard</c> was a perception blackboard and is now a trigger one too.</b>
/// Everything M1-06 wrote there is about the world — where the player is, how far, who else is
/// near — and a boss skill firing <em>"when my own health drops below 66 %"</em> had nothing to
/// read. Two fields close that, and the rows here are about when they are written rather than about
/// arithmetic <c>HealthTests</c> already owns.
/// </para>
/// <para>
/// <b>The trigger row bridges the two blackboards by hand, and that is the point rather than a
/// shortcut.</b> <c>TriggerClause.IsMet</c> takes a <c>CombatBlackboard</c> and M4-01a rule 7
/// deliberately does not widen it: the two carry different questions, and merging them would put
/// <c>HasFocus</c> and <c>Veilrot</c> on a Husk. What the shape of the narrow read is — how
/// M4-01b's runner gets an enemy's numbers in front of a clause — is ruled there. What this row
/// says is the thing that has to be true first: the fields exist, they are in the same units, and
/// the shipped clause answers correctly when handed them.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EnemyTriggerTests
{
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";
    private const string DescentId = "mode.descent";

    /// <summary>GD §8.1's Husk. Halved, it is the spec's 18 of 36.</summary>
    private const float HuskMaxHp = 36f;

    private const int Capacity = 8;
    private const float Tolerance = 1e-4f;

    private SilentEvents _events;
    private EnemySystem _system;

    [SetUp]
    public void SetUp()
    {
        _events = new SilentEvents();
        _system = new EnemySystem(
            Catalog(),
            _events,
            new FixedRandom(),
            new DepthScaling(Scalings.Design()),
            Capacity);
    }

    [Test]
    public void Blackboard_PublishesHealthFraction()
    {
        EnemyAgent agent = Husk();

        agent.Health.ApplyDamage(HuskMaxHp / 2f, now: 0f);

        Assert.That(agent.Health.Current, Is.EqualTo(18f).Within(Tolerance), "Eighteen of thirty-six.");

        _system.Ingest(new WorldSnapshot(Capacity));

        // This tick, not next. Perception runs inside Ingest, which is above the behaviour step, so
        // a condition asked this tick reads this tick's health — the difference between a boss that
        // enters its phase when it crosses the threshold and one that enters a frame late.
        Assert.That(
            agent.Blackboard.HpFraction,
            Is.EqualTo(0.5f).Within(Tolerance));
    }

    [Test]
    public void Blackboard_ShieldFractionIsZeroAndSaysSo()
    {
        EnemyAgent agent = Husk();

        _system.Ingest(new WorldSnapshot(Capacity));

        // Rule 6's anti-Veilrot row. Nothing builds an enemy with a ShieldSpec and nothing grants
        // one, so this reads zero for every agent in the game — and it ships written rather than
        // untouched precisely so that TriggerField.ShieldFraction is a clause that is *false*
        // instead of a clause nobody fills in, which is the trap M3-07a raised about Veilrot and
        // nothing has fixed.
        Assert.That(agent.Blackboard.ShieldFraction, Is.Zero);
        Assert.That(agent.Health.HasShield, Is.False, "And it is zero because there is no shield.");
    }

    [Test]
    public void Blackboard_HealthFractionIsNeverNonFinite()
    {
        EnemyAgent agent = Husk();

        // A maximum driven to zero is the one input that could put a NaN on the blackboard, and a
        // NaN there fails *both* comparisons in TriggerClause.IsMet — so a boss would simply never
        // change phase and nothing would say why. The guarantee lives in Health.Fraction, which
        // answers zero rather than dividing by a maximum that is not positive; this row is what
        // stops it being quietly removed one layer down, where nothing else would notice.
        agent.Health.MaxHp.Add(new Modifier(ModifierKind.PercentMult, -1f, new object()));

        Assert.That(agent.Health.Fraction, Is.Zero, "The source of the field, not the field.");
        Assert.That(float.IsNaN(agent.Health.Fraction), Is.False);

        _system.Ingest(new WorldSnapshot(Capacity));

        Assert.That(float.IsNaN(agent.Blackboard.HpFraction), Is.False);
        Assert.That(float.IsInfinity(agent.Blackboard.HpFraction), Is.False);
    }

    [Test]
    public void Blackboard_ACorpseKeepsItsLastReading()
    {
        // Found by writing the row above and getting it wrong, so it is pinned rather than
        // remembered: EnemySystem.Perceive skips agents that are not alive, which it has done since
        // M1-06 for every field it writes. So a dead enemy's HpFraction is the last one it was
        // perceived with, *not* zero — and anything reading a boss's blackboard after its death
        // (M4-01b's phase machine, first of all) has to ask IsAlive rather than trust the fraction.
        EnemyAgent agent = Husk();

        _system.Ingest(new WorldSnapshot(Capacity));

        Assert.That(agent.Blackboard.HpFraction, Is.EqualTo(1f).Within(Tolerance));

        agent.Health.ApplyDamage(HuskMaxHp, now: 0f);

        Assert.That(agent.IsAlive, Is.False);

        _system.Ingest(new WorldSnapshot(Capacity));

        Assert.That(
            agent.Blackboard.HpFraction,
            Is.EqualTo(1f).Within(Tolerance),
            "A corpse is not perceived, so its board is frozen at its last living tick. Read "
                + "IsAlive, not the fraction.");

        Assert.That(
            agent.Health.Fraction,
            Is.Zero,
            "The health itself does say zero — the staleness is the blackboard's, by design.");
    }

    [Test]
    public void Blackboard_SpawnSeedsFullHealthRatherThanZero()
    {
        // Reset zeroes every field, and for the perception half that is merely wrong — "distance
        // zero" is a number nothing acts on for one tick. A zero *health* fraction is not: it reads
        // as dead to every trigger that asks, and an agent spawned inside EnemySystem.Tick is not
        // perceived until the next frame's Ingest. So Initialise seeds it, and this is the row.
        EnemyAgent agent = Husk();

        Assert.That(
            agent.Blackboard.HpFraction,
            Is.EqualTo(1f).Within(Tolerance),
            "A freshly spawned enemy is at full health, and must not spend its first tick claiming "
                + "to be at zero.");
    }

    [Test]
    public void Blackboard_ResetClearsTheTriggerFields()
    {
        var blackboard = new EnemyBlackboard
        {
            HpFraction = 0.3f,
            ShieldFraction = 0.9f,
        };

        blackboard.Reset();

        Assert.That(blackboard.HpFraction, Is.Zero);
        Assert.That(blackboard.ShieldFraction, Is.Zero);
    }

    [Test]
    public void Trigger_EvaluatesAgainstAnEnemysHealth()
    {
        EnemyAgent agent = Husk();

        var clause = new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.66f);

        _system.Ingest(new WorldSnapshot(Capacity));

        Assert.That(
            clause.IsMet(Read(agent.Blackboard)),
            Is.False,
            "A Husk at full health is not below two thirds.");

        agent.Health.ApplyDamage(HuskMaxHp / 2f, now: 0f);
        _system.Ingest(new WorldSnapshot(Capacity));

        Assert.That(
            clause.IsMet(Read(agent.Blackboard)),
            Is.True,
            "At a half it is — which is the whole claim: the field a boss reads about itself is in "
                + "the same units and on the same scale the shipped clause already compares.");
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    /// <summary>
    /// An enemy's two trigger fields, in the shape <c>TriggerClause.IsMet</c> takes.
    /// </summary>
    /// <remarks>
    /// <b>Written out here rather than built into either type</b> (M4-01a rule 7). Merging the two
    /// blackboards would put <c>HasFocus</c> and <c>Veilrot</c> on a Husk, and giving
    /// <c>TriggerClause</c> a second overload would double every row in <c>TriggerSpecTests</c>.
    /// The read M4-01b's runner actually wants is ruled there; what this fixture needs is only to
    /// prove the numbers are readable, so it copies the two fields that exist and leaves the seven
    /// that are the player's at their defaults — which is exactly what a clause about an enemy's
    /// own health may ask about.
    /// </remarks>
    private static CombatBlackboard Read(EnemyBlackboard blackboard) => new CombatBlackboard
    {
        HpFraction = blackboard.HpFraction,
        ShieldFraction = blackboard.ShieldFraction,
    };

    private EnemyAgent Husk() => _system.Spawn(new ContentId(HuskId), Vector3.Zero);

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Character() },
        new[] { HuskSpec(HuskId) },
        new[] { Descent() });

    private static EnemySpec HuskSpec(string id) => new EnemySpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        maxHp: HuskMaxHp,
        moveSpeed: 3.5f,
        targetPriority: 1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

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
        120f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
