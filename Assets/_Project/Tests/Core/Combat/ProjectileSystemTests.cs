using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// The M2-07a spec's thirteen rules: the ruling that core decides an arrival itself, the shot that
/// is a point and a moment, the arrival that is decided on XZ, and the wiring that makes a run
/// carry shots at all.
/// </summary>
/// <remarks>
/// <para>
/// The Spitter's numbers throughout (GD §8.1) — 14 m of standoff, 12 m/s of flight, a 1.6 m blast —
/// so a failure reads as "the archetype we are about to build stopped being dodgeable" rather than
/// as an arithmetic puzzle. Rows that need a different number say which and why.
/// </para>
/// <para>
/// <b>The player is a real <c>PlayerCombat</c>, never a stand-in.</b> Rule 1 is precisely that core
/// calls <c>ApplyDamage</c> directly, and the thing most worth proving is that a landing reaches the
/// same door a Husk's strike does — i-frames, Aegis, <c>PlayerDamaged</c>, <c>PlayerDied</c> and
/// all. A fake damage sink here would be a test of the arrangement rather than of the rule.
/// </para>
/// <para>
/// <b>Three rows are weaker than the spec asked for, and say so where they stand.</b> Nothing fires
/// a shot until M2-07b, and <c>RunState.Projectiles</c> is <c>internal</c> with no
/// <c>InternalsVisibleTo</c> anywhere (AR §18.2) — deliberately, since a handle a test could reach
/// is a handle a view could reach. So a test cannot put a shot into a <em>live run</em>, and the
/// session rows below assert what is reachable: that the scalar read exists and the handle does not,
/// that a run does not carry shots between lives, and that a landing leaves the player dead by the
/// time <c>Tick</c> returns, which is what makes a death check placed after the step end the run on
/// the same tick. <b>The end-to-end ordering assertion belongs to M2-07b</b>, the first task with
/// something that can fire.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ProjectileSystemTests
{
    private const string OathboundId = "character.oathbound";
    private const string SpitterId = "enemy.spitter";

    /// <summary>GD §8.1's Spitter: it stops at 14 m and throws at 12 m/s.</summary>
    private const float Standoff = 14f;
    private const float Speed = 12f;

    /// <summary>Metres of near-miss that still count. GD §8.1's blast on an arriving bolt.</summary>
    private const float Radius = 1.6f;

    /// <summary>14 / 12, the dodge window the whole archetype rests on.</summary>
    private const float FlightTime = Standoff / Speed;

    /// <summary>Enough to hurt and never enough to kill, so a row about geometry stays about geometry.</summary>
    private const float Damage = 9f;

    private const float MaxHp = 140f;
    private const float HitIFrames = 0.5f;

    /// <summary>Room for every row's shots, and small enough that the capacity rows can fill it.</summary>
    private const int Capacity = 8;

    /// <summary>The buffer <c>PlayerCombat</c> preallocates. No row here spawns an enemy.</summary>
    private const int EnemyCapacity = 8;

    /// <summary>The device cap a run composes its stages under (M2-05), for the session rows.</summary>
    private const int DeviceCap = 8;

    private const string DescentId = "mode.descent";
    private const int Seed = 99;

    private RecordingEvents _events;
    private RecordingIntents _intents;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
    }

    // ---- Rule 2 and 3: a shot is a point and a moment ------------------------------------------

    [Test]
    public void Fire_PublishesWithFlightTime()
    {
        ProjectileSystem projectiles = NewSystem();

        int id = projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);

        ProjectileFired fired = _events.Single<ProjectileFired>();

        Assert.That(id, Is.EqualTo(1));
        Assert.That(fired.Id, Is.EqualTo(1));
        Assert.That(fired.SpecId, Is.EqualTo(new ContentId(SpitterId)));

        // 14 / 12 = 1.1667 s, against a player who moves and a 1.6 m blast. This number is GD
        // §8.1's "punishes standing still" stated as arithmetic, which is why it is asserted to a
        // millisecond rather than approximately.
        Assert.That(fired.FlightTime, Is.EqualTo(FlightTime).Within(0.001f));

        Assert.That(fired.Origin, Is.EqualTo(From(0f)));
        Assert.That(fired.Target, Is.EqualTo(At(Standoff)));
        Assert.That(projectiles.InFlightCount, Is.EqualTo(1));
    }

    [Test]
    public void Fire_FlightTimeIsXzOnly()
    {
        ProjectileSystem projectiles = NewSystem();

        // 14 m away on the ground and 3 m below it. Counted, the separation would be 14.32 m and
        // every dodge window in the game would inflate with the height of whatever fired — AR
        // §18.4 says the Y axis is a rendering detail, and this is that rule on a projectile.
        var origin = new Vector3(0f, 3f, 0f);
        var target = new Vector3(0f, 0f, Standoff);

        projectiles.Fire(Shot(origin, target), 0f);

        Assert.That(_events.Single<ProjectileFired>().FlightTime, Is.EqualTo(FlightTime).Within(0.001f));
    }

    [Test]
    public void Fire_ZeroDistance_ArrivesNextTick()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        // A Spitter firing at the point it is standing on. The interesting half is that the
        // division is never reached with a zero denominator — the speed is guarded positive at
        // Projectile's door — so a zero distance is a zero flight time and not a NaN.
        int id = projectiles.Fire(Shot(From(0f), From(0f)), 0f);

        Assert.That(_events.Single<ProjectileFired>().FlightTime, Is.Zero);

        Assert.DoesNotThrow(() => projectiles.Tick(0f, Vector3.Zero, player));

        Assert.That(_events.Single<ProjectileImpacted>().Id, Is.EqualTo(id));
        Assert.That(projectiles.InFlightCount, Is.Zero);
    }

    [Test]
    public void Fire_IssuesIdsFromOneAndNeverReuses()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        int first = projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);

        projectiles.Tick(FlightTime, Far(), player);

        int second = projectiles.Fire(Shot(From(0f), At(Standoff)), FlightTime);

        // EnemyRegistry's rule, for its reason: an id held across an impact should be *stale*
        // rather than misleading. Reuse would let a view that missed one impact draw the next
        // shot as the one it is still holding.
        Assert.That(first, Is.EqualTo(1));
        Assert.That(second, Is.EqualTo(2));
    }

    // ---- Rule 7: a refused shot is silent and costs nothing -------------------------------------

    [Test]
    public void Fire_AtCapacity_IsRefusedSilently()
    {
        var projectiles = new ProjectileSystem(_events, 2);

        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);
        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);

        _events.Clear();

        int refused = projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);

        // One lost bolt is better than an exception that ends the run, and the caller that wanted
        // to know has the return value.
        Assert.That(refused, Is.EqualTo(ProjectileSystem.NoProjectile));
        Assert.That(refused, Is.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Assert.That(projectiles.InFlightCount, Is.EqualTo(2));
    }

    [Test]
    public void Fire_AfterCapacityFrees_Works()
    {
        var projectiles = new ProjectileSystem(_events, 2);
        PlayerCombat player = Player();

        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);
        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);
        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);

        projectiles.Tick(FlightTime, Far(), player);

        _events.Clear();

        int id = projectiles.Fire(Shot(From(0f), At(Standoff)), FlightTime);

        // A refusal costs nothing permanently: the slot comes back and the ids carry on from where
        // the accepted ones left off. Two were accepted, so the next is 3 and not 4.
        Assert.That(id, Is.EqualTo(3));
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(1));
    }

    // ---- Rule 6: arrival ------------------------------------------------------------------------

    [Test]
    public void Tick_LandsNothingEarly()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        projectiles.Fire(Shot(From(0f), At(Standoff), speed: 1f, distance: 1f), 0f);

        projectiles.Tick(0.99f, Vector3.Zero, player);

        Assert.That(_events.Count<ProjectileImpacted>(), Is.Zero);
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
        Assert.That(player.Health.Current, Is.EqualTo(MaxHp));
        Assert.That(projectiles.InFlightCount, Is.EqualTo(1));
    }

    [Test]
    public void Tick_LandsOnTheDot()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        projectiles.Fire(Shot(From(0f), At(Standoff), speed: 1f, distance: 1f), 0f);

        // "At or before now", not "before": a frame boundary that fell exactly on the arrival
        // would otherwise defer the shot by a whole frame, and with a fixed dt that is every shot.
        projectiles.Tick(1f, Vector3.Zero, player);

        Assert.That(_events.Count<ProjectileImpacted>(), Is.EqualTo(1));
    }

    [Test]
    public void Tick_HitsPlayerInsideRadius()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);

        // 1.5 m from the impact point against a 1.6 m radius: inside, and only just, because the
        // interesting failures are at the edge rather than at the centre.
        projectiles.Tick(FlightTime, At(Standoff - 1.5f), player);

        ProjectileImpacted impacted = _events.Single<ProjectileImpacted>();

        Assert.That(impacted.HitPlayer, Is.True);
        Assert.That(impacted.Position, Is.EqualTo(At(Standoff)));
        Assert.That(player.Health.Current, Is.EqualTo(MaxHp - Damage));

        // Rule 1, and the whole of what M2-07a settles: the damage arrived by a direct call inside
        // the tick. No fact was reported, no IRunSession member was involved, and the player was
        // hurt through the same door a Husk's strike uses.
        PlayerDamaged damaged = _events.Single<PlayerDamaged>();

        Assert.That(damaged.ToHp, Is.EqualTo(Damage));
        Assert.That(damaged.Blocked, Is.False);
        Assert.That(
            MemberNames(typeof(IRunSession)),
            Has.No.Member("ReportProjectileHit").And.No.Member("ReportContact"),
            "IRunSession gains no member in M2 — ledger row 7, M2-07a rule 1.");
    }

    [Test]
    public void Tick_MissesPlayerOutsideRadius()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);

        projectiles.Tick(FlightTime, At(Standoff - 1.7f), player);

        // The event is published anyway, because the view has to stop existing either way — this
        // is the mirror of EnemyDespawned rather than of EnemyDied.
        ProjectileImpacted impacted = _events.Single<ProjectileImpacted>();

        Assert.That(impacted.HitPlayer, Is.False);
        Assert.That(impacted.Position, Is.EqualTo(At(Standoff)));
        Assert.That(player.Health.Current, Is.EqualTo(MaxHp));
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
        Assert.That(projectiles.InFlightCount, Is.Zero);
    }

    [Test]
    public void Tick_MissIsDecidedOnXz()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);

        // 1 m away on the ground and 4 m above it — on a ledge, or mid-jump in a game that has
        // neither. A hit, because the height is not counted (AR §18.4). The same rule as the
        // flight time, asked at the other end of the shot.
        projectiles.Tick(FlightTime, new Vector3(0f, 4f, Standoff - 1f), player);

        Assert.That(_events.Single<ProjectileImpacted>().HitPlayer, Is.True);
        Assert.That(player.Health.Current, Is.EqualTo(MaxHp - Damage));
    }

    // ---- Rule 4: the target is a point, and it never tracks --------------------------------------

    [Test]
    public void Tick_MovingOutOfTheWayWorks()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        // Fired at where the player is standing, which is what M2-07b will aim at.
        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);

        // And by the time it lands they are 6 m away — a shade over a second of walking at the
        // Oathbound's retuned 3 m/s. GD §8.1's whole archetype: a homing shot would punish
        // nothing, because the only counter left would be an i-frame.
        projectiles.Tick(FlightTime, At(Standoff - 6f), player);

        Assert.That(_events.Single<ProjectileImpacted>().HitPlayer, Is.False);
        Assert.That(player.Health.Current, Is.EqualTo(MaxHp));
    }

    // ---- Rule 6: i-frames are somebody else's question -------------------------------------------

    [Test]
    public void Tick_DoesNotConsultIFrames()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        // A hit at t = 0 opens CC §7's half-second of i-frames.
        player.ApplyDamage(1f, 0f);

        projectiles.Fire(Shot(From(0f), At(Standoff), speed: 5f, distance: 1f), 0f);

        projectiles.Tick(0.2f, At(1f), player);

        // Published, blocked, and by PlayerCombat rather than by anything here: a shot that arrives
        // during a dodge is still a shot that arrived, and the dodge is what made it harmless
        // rather than what made it miss. ChaserBehaviour.EnterStrike leaves it in exactly the same
        // place, which is what stops two enemies answering one question two ways.
        IReadOnlyList<PlayerDamaged> damaged = _events.Of<PlayerDamaged>();

        Assert.That(damaged.Count, Is.EqualTo(2));
        Assert.That(damaged[1].Blocked, Is.True);
        Assert.That(damaged[1].ToHp, Is.Zero);
        Assert.That(player.Health.Current, Is.EqualTo(MaxHp - 1f));

        // And the impact is a fact about the geometry, not about the outcome.
        Assert.That(_events.Single<ProjectileImpacted>().HitPlayer, Is.True);
    }

    // ---- Rule 6 and 8: order, and a store that does not shift what it has not examined ----------

    [Test]
    public void Tick_LandsSeveralInFireOrder()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        for (int i = 0; i < 3; i++)
        {
            projectiles.Fire(Shot(From(0f), At(Standoff), speed: 1f, distance: 1f), 0f);
        }

        projectiles.Tick(1f, Far(), player);

        IReadOnlyList<ProjectileImpacted> impacts = _events.Of<ProjectileImpacted>();

        Assert.That(Ids(impacts), Is.EqualTo(new[] { 1, 2, 3 }));
    }

    [Test]
    public void Tick_LandsOldestFirstAcrossRemovals()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        // Five shots, and the three due are in the middle — so the store has to remove from the
        // middle of its own array while it is walking it. Arrival is *not* fire order here: a shot
        // fired later can be aimed closer and land first, which is why the store cannot simply
        // assume the front of the array is the next to go.
        projectiles.Fire(Shot(From(0f), At(Standoff), speed: 1f, distance: 10f), 0f);

        for (int i = 0; i < 3; i++)
        {
            projectiles.Fire(Shot(From(0f), At(Standoff), speed: 1f, distance: 1f), 0f);
        }

        projectiles.Fire(Shot(From(0f), At(Standoff), speed: 1f, distance: 10f), 0f);

        projectiles.Tick(1f, Far(), player);

        IReadOnlyList<ProjectileImpacted> impacts = _events.Of<ProjectileImpacted>();

        Assert.That(Ids(impacts), Is.EqualTo(new[] { 2, 3, 4 }));
        Assert.That(projectiles.InFlightCount, Is.EqualTo(2));

        // And the two survivors are still the two survivors, in the order they were fired.
        _events.Clear();

        projectiles.Tick(10f, Far(), player);

        Assert.That(Ids(_events.Of<ProjectileImpacted>()), Is.EqualTo(new[] { 1, 5 }));
    }

    // ---- Rule 5: a shot outlives its shooter -----------------------------------------------------

    [Test]
    public void Tick_ShotOutlivesItsShooter()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        // Enemy 4 fires and is then killed — the registry it came from is not even present in this
        // fixture, which is the point: SourceId is a record, never resolved against anything.
        // Killing a Spitter does not un-fire the bolt the player is already dodging.
        projectiles.Fire(Shot(From(0f), At(Standoff), sourceId: 4), 0f);

        Assert.That(_events.Single<ProjectileFired>().SourceId, Is.EqualTo(4));

        projectiles.Tick(FlightTime, At(Standoff), player);

        Assert.That(_events.Single<ProjectileImpacted>().HitPlayer, Is.True);
        Assert.That(player.Health.Current, Is.EqualTo(MaxHp - Damage));
    }

    // ---- Rule 9: one owner for one blackboard field ---------------------------------------------

    [Test]
    public void Tick_WritesIncomingProjectiles()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);
        projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);

        projectiles.Tick(0f, Far(), player);

        // CC §6.4's Bulwark trigger, and the field M1-08 left saying "zero until M2-07 gives
        // something the means to fire one". PlayerCombat still does not touch it — one owner for
        // one field — and the tick order is what makes this value this frame's rather than last
        // frame's.
        Assert.That(player.Blackboard.IncomingProjectiles, Is.EqualTo(2));

        projectiles.Tick(FlightTime, Far(), player);

        Assert.That(player.Blackboard.IncomingProjectiles, Is.Zero);
    }

    // ---- Rule 13 and AR §14: no draws, no allocation ---------------------------------------------

    [Test]
    public void Tick_AllocatesNothing()
    {
        var projectiles = new ProjectileSystem(new SilentEvents(), 16);
        PlayerCombat player = Player();

        for (int i = 0; i < 16; i++)
        {
            projectiles.Fire(Shot(From(0f), At(Standoff), speed: 1f, distance: 100f), 0f);
        }

        // A warm-up tick, so that anything the first call touches is already touched — the steady
        // state of a run with shots in the air is what this measures.
        projectiles.Tick(0f, Vector3.Zero, player);

        AllocationAssert.None(() => projectiles.Tick(1f, Vector3.Zero, player));

        Assert.That(projectiles.InFlightCount, Is.EqualTo(16));
    }

    [Test]
    public void Tick_DrawsNoRandom()
    {
        var projectiles = new ProjectileSystem(_events, 10);
        PlayerCombat player = Player();

        for (int i = 0; i < 10; i++)
        {
            projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);
        }

        projectiles.Tick(FlightTime, Far(), player);

        Assert.That(_events.Count<ProjectileImpacted>(), Is.EqualTo(10));

        // A generator that throws cannot be handed to this class, and that is the assertion: there
        // is nowhere to put one. Spelled against the type rather than by injecting a throwing fake,
        // because a fake proves only that today's code path missed it while this fails the day
        // somebody adds a spread and does not weigh ADR-0011 first — a shot's spread is a change to
        // what a seed means, and it is not made by accident.
        Assert.That(
            ConstructorParameterTypes(typeof(ProjectileSystem)),
            Has.No.Member(typeof(IRandom)).And.No.Member(typeof(IRandomStream)));

        Assert.That(
            FieldTypes(typeof(ProjectileSystem)),
            Has.No.Member(typeof(IRandom)).And.No.Member(typeof(IRandomStream)));
    }

    // ---- Rule 12: Clear ---------------------------------------------------------------------------

    [Test]
    public void Clear_ForgetsEverythingSilently()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        for (int i = 0; i < 3; i++)
        {
            projectiles.Fire(Shot(From(0f), At(Standoff)), 0f);
        }

        _events.Clear();

        projectiles.Clear();

        // EnemySystem.Clear's reasoning at the same moment: the scope is going away and with it
        // every subscriber an event could reach, so three farewells would be noise.
        Assert.That(projectiles.InFlightCount, Is.Zero);
        Assert.That(_events.All, Is.Empty);

        projectiles.Tick(100f, At(Standoff), player);

        Assert.That(_events.All, Is.Empty);
        Assert.That(player.Health.Current, Is.EqualTo(MaxHp));
    }

    // ---- Rules 10 and 11: the run ------------------------------------------------------------------

    [Test]
    public void Session_TicksProjectilesAfterBehavioursBeforeDeathCheck()
    {
        var projectiles = new ProjectileSystem(_events, Capacity);
        PlayerCombat player = Player();

        // A bolt that kills outright. The rule it stands for is that the death check runs *after*
        // this step: if the landing did not leave the player dead by the time Tick returns, a
        // check placed after it would miss the death and the run would carry on for a frame with
        // no hit points.
        projectiles.Fire(Shot(From(0f), At(Standoff), damage: MaxHp), 0f);

        projectiles.Tick(FlightTime, At(Standoff), player);

        Assert.That(player.IsDead, Is.True);

        // PlayerDied before ProjectileImpacted, because the damage is applied before the arrival is
        // announced — so a listener handling the impact has already seen what it cost.
        Assert.That(Types(_events.All), Is.EqualTo(new[]
        {
            typeof(ProjectileFired),
            typeof(PlayerDamaged),
            typeof(PlayerDied),
            typeof(ProjectileImpacted),
        }));

        // And the step is wired into the run: a live session ticks without complaint and reports
        // the count core owns. The end-to-end row — a shot that kills mid-run, PlayerDied then
        // RunEnded, and no intent written — belongs to M2-07b, which is the first task with
        // something that can fire one into a live run. See the fixture's remarks.
        RunSession session = Session();

        session.Start(Config());
        session.Tick(Snapshot());

        Assert.That(session.IsRunning, Is.True);
        Assert.That(session.State.InFlightProjectiles, Is.Zero);
    }

    [Test]
    public void Session_ExposesCountAndNotTheSystem()
    {
        RunSession session = Session();

        session.Start(Config());

        Assert.That(session.State.InFlightProjectiles, Is.Zero);

        // AR §18.2: a live object is never handed out of RunState. ProjectileSystem has a public
        // Tick, a public Fire and a public Clear, so a public handle would let a view land every
        // shot in the arena a second time, invent one, or empty the sky — with nothing in the
        // compiler to object. The scalar read is the whole of the surface.
        Assert.That(
            PublicMemberTypes(typeof(RunState)),
            Has.No.Member(typeof(ProjectileSystem)));
    }

    [Test]
    public void Session_EndClearsThem()
    {
        RunSession session = Session();

        session.Start(Config());
        session.End();
        session.Start(Config());

        // A second run does not inherit the first one's sky. Composed fresh at every Start and
        // cleared at every End, which is two answers to the same question on purpose: End is what
        // stops a bolt landing on a run that is over, Start is what stops one crossing into the
        // next.
        Assert.That(session.State.InFlightProjectiles, Is.Zero);
    }

    // ---- Guards ------------------------------------------------------------------------------------

    [Test]
    public void Ctor_BadArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new ProjectileSystem(null, Capacity));

        // Guarded positive (rule 7). A run allowed no shots refuses every one of them in silence,
        // which is the one failure a playtest cannot see.
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectileSystem(_events, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectileSystem(_events, -1));
    }

    [Test]
    public void Projectile_NonFiniteNumbers_Throw()
    {
        // AR §18.3, at the one door every number a shot carries comes through. A NaN speed makes
        // an arrival that is never at or before any `now`, so the slot is held for the rest of the
        // run and nothing reports it; a NaN radius makes every hit test answer *no*. Both are
        // silence rather than a visible fault.
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Projectile(Spec(), 0, From(0f), At(Standoff), bad, Radius, Damage),
                $"speed {bad}");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Projectile(Spec(), 0, From(0f), At(Standoff), Speed, bad, Damage),
                $"radius {bad}");
        }

        // Damage is the one number of the three that may be zero: a shot that deals nothing is
        // what a stat driven to zero produces, and PlayerCombat.ApplyDamage already does nothing
        // with it. Negative and non-finite are still refused.
        Assert.DoesNotThrow(() => new Projectile(Spec(), 0, From(0f), At(Standoff), Speed, Radius, 0f));

        foreach (float bad in new[] { -1f, float.NaN, float.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new Projectile(Spec(), 0, From(0f), At(Standoff), Speed, Radius, bad),
                $"damage {bad}");
        }

        var nan = new Vector3(float.NaN, 0f, 0f);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Projectile(Spec(), 0, nan, At(Standoff), Speed, Radius, Damage));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Projectile(Spec(), 0, From(0f), nan, Speed, Radius, Damage));
    }

    [Test]
    public void Fire_And_Tick_NonFiniteNow_Throw()
    {
        ProjectileSystem projectiles = NewSystem();
        PlayerCombat player = Player();

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => projectiles.Fire(Shot(From(0f), At(Standoff)), bad));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => projectiles.Tick(bad, Vector3.Zero, player));
        }

        Assert.Throws<ArgumentNullException>(() => projectiles.Tick(0f, Vector3.Zero, null));
    }

    // ---- Fixture -------------------------------------------------------------------------------

    private ProjectileSystem NewSystem() => new(_events, Capacity);

    private PlayerCombat Player() => new(Character(), _events, _intents, EnemyCapacity);

    private RunSession Session()
    {
        var catalog = new ContentCatalog(new[] { Character() }, null, new[] { Descent() });

        return new RunSession(
            catalog,
            new FixedRandom(Seed),
            _events,
            _intents,
            EnemyCapacity,
            DeviceCap,
            Capacity);
    }

    private static RunConfig Config() => new(
        new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, SpawnPlan.Empty);

    private static WorldSnapshot Snapshot()
    {
        var snapshot = new WorldSnapshot(EnemyCapacity);

        snapshot.Dt = 1f / 60f;

        return snapshot;
    }

    private static ContentId Spec() => new(SpitterId);

    /// <summary>Where a Spitter is standing: the origin, on the ground.</summary>
    private static Vector3 From(float z) => new(0f, 0f, z);

    /// <summary>A point <paramref name="z"/> metres up the +Z axis.</summary>
    private static Vector3 At(float z) => new(0f, 0f, z);

    /// <summary>Far enough from every impact point in this fixture that nothing can be hit.</summary>
    private static Vector3 Far() => new(1000f, 0f, 1000f);

    /// <summary>
    /// One shot. <paramref name="distance"/> overrides the geometry for rows that want a round
    /// flight time rather than GD §8.1's 1.1667 s — the target stays where it is, and only the
    /// arithmetic the row is asserting changes.
    /// </summary>
    private static Projectile Shot(
        Vector3 origin,
        Vector3 target,
        float speed = Speed,
        float distance = -1f,
        int sourceId = 1,
        float damage = Damage)
    {
        if (distance >= 0f)
        {
            // Re-aimed rather than re-timed, because there is no flight time to set: a shot is a
            // point and a moment, and the moment is derived from the two points (rule 2).
            target = origin + (Vector3.UnitZ * distance);
        }

        return new Projectile(Spec(), sourceId, origin, target, speed, Radius, damage);
    }

    /// <summary>
    /// The Oathbound of CC §7, with <b>no Aegis</b>: every row here reads
    /// <c>Health.Current</c> directly, and a shield would absorb the first 30 points and make each
    /// of them a test of the Aegis instead. M1-02's own fixture owns that question.
    /// </summary>
    private static CharacterSpec Character() => new(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 16f, 4f, 0.05f),
        null,
        HitIFrames);

    /// <summary>Descent with an empty roster: no row here is about a schedule.</summary>
    private static ModeSpec Descent() => new(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Array.Empty<RosterEntry>());

    private static int[] Ids(IReadOnlyList<ProjectileImpacted> impacts)
    {
        var ids = new int[impacts.Count];

        for (int i = 0; i < impacts.Count; i++)
        {
            ids[i] = impacts[i].Id;
        }

        return ids;
    }

    private static Type[] Types(IReadOnlyList<object> events)
    {
        var types = new Type[events.Count];

        for (int i = 0; i < events.Count; i++)
        {
            types[i] = events[i].GetType();
        }

        return types;
    }

    private static List<string> MemberNames(Type type)
    {
        MemberInfo[] members = type.GetMembers();
        var names = new List<string>(members.Length);

        foreach (MemberInfo member in members)
        {
            names.Add(member.Name);
        }

        return names;
    }

    private static List<Type> ConstructorParameterTypes(Type type)
    {
        var types = new List<Type>();

        foreach (ConstructorInfo constructor in type.GetConstructors())
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                types.Add(parameter.ParameterType);
            }
        }

        return types;
    }

    private static List<Type> FieldTypes(Type type)
    {
        var types = new List<Type>();

        FieldInfo[] fields = type.GetFields(
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        foreach (FieldInfo field in fields)
        {
            types.Add(field.FieldType);
        }

        return types;
    }

    /// <summary>
    /// Every type a caller outside core could reach through a public property or field of
    /// <paramref name="type"/> — what AR §18.2's "a live object is never handed out" is asked of.
    /// </summary>
    private static List<Type> PublicMemberTypes(Type type)
    {
        var types = new List<Type>();

        MemberInfo[] members = type.GetMembers(BindingFlags.Instance | BindingFlags.Public);

        foreach (MemberInfo member in members)
        {
            switch (member)
            {
                case PropertyInfo property:
                    types.Add(property.PropertyType);

                    break;

                case FieldInfo field:
                    types.Add(field.FieldType);

                    break;
            }
        }

        return types;
    }

    /// <summary>
    /// An event sink that keeps nothing, for the allocation row.
    /// </summary>
    /// <remarks>
    /// <c>RecordingEvents</c> boxes every event into a <c>List&lt;object&gt;</c>, which is an
    /// allocation belonging to the fake rather than to the code under test — and the measured loop
    /// would report it as if it were the system's. Nothing lands in that row, so nothing is
    /// published either way; this is here so the row cannot start lying the day it does.
    /// </remarks>
    private sealed class SilentEvents : IDomainEvents
    {
        public void Publish<T>(in T evt)
            where T : struct
        {
        }
    }
}
