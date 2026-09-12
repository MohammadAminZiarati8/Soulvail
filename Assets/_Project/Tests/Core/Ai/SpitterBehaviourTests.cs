using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// The M2-07b spec's fourteen rules: the seam, the context, the dispatch, and the Spitter that stops
/// at fourteen metres, stares, and throws.
/// </summary>
/// <remarks>
/// <para>
/// GD §8.1's Spitter throughout, with M2-06's authored numbers — 28 HP, 2.8 m/s, 12 contact damage,
/// a 0.7 s windup, a 0.9 s recovery, and a projectile block of 14 m / 12 m/s / 1.6 m — so a failure
/// reads as "the archetype we ship stopped keeping its distance" rather than as an arithmetic
/// puzzle. Rows that vary a number say which and why.
/// </para>
/// <para>
/// <b>Perception is hand-written</b>, which is what makes a behaviour testable at all:
/// <c>EnemyBlackboard</c>'s perception half is filled by <c>EnemySystem.Ingest</c> in a real run, and
/// here <see cref="Place"/> assigns it directly, so a row can put the player at 9.79 m without
/// building a snapshot, a registry and a world to get him there (AR §9).
/// </para>
/// <para>
/// <b>The player has no Aegis here</b>, unlike <c>ChaserBehaviourTests</c>'. Every damage row below
/// reads what a landing cost, and a 30-point shield would absorb all of it and turn each of them
/// into a test of the Aegis instead — which is M1-02's fixture's question.
/// </para>
/// <para>
/// <b>The asset row lives in <c>Soulvail.Tests.Game</c></b>, not here:
/// <c>EnemyLookTests.Assets_AuthoredStaticUntilTheirBehaviourExists</c> is what flips with
/// <c>Spitter.asset</c>, because loading one needs the <c>AssetDatabase</c> and this assembly has no
/// engine reference. The rest of the block — the 14, the 12 and the 1.6 — is pinned beside it in
/// <c>Spitter_MatchesDesign</c>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SpitterBehaviourTests
{
    private const string SpitterId = "enemy.spitter";
    private const string HuskId = "enemy.husk";
    private const string OathboundId = "character.oathbound";
    private const string DescentId = "mode.descent";

    /// <summary>Room for the 28-agent context row, which is M2-04's concurrency cap.</summary>
    private const int Capacity = 32;

    /// <summary>Room for every shot those 28 put in the air before anything lands one.</summary>
    private const int ProjectileCapacity = 64;

    /// <summary>What the session row seeds with. <c>RunConfig</c> and the generator must agree.</summary>
    private const int Seed = 11;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    // GD §8.1 and M2-06, the numbers the Spitter's fight is made of.
    private const float MoveSpeed = 2.8f;
    private const float ContactDamage = 12f;
    private const float Reach = 1.2f;
    private const float WindupTime = 0.7f;
    private const float RecoverTime = 0.9f;
    private const float AggroRange = 30f;

    // Its projectile block — where it stops, how fast the shot flies, how much of a near miss counts.
    private const float Standoff = 14f;
    private const float FlightSpeed = 12f;
    private const float BlastRadius = 1.6f;

    /// <summary>
    /// 14 × 0.7 = 9.8 m, the inner edge of the standoff band. Derived from the behaviour's own
    /// constant rather than typed, so a row cannot silently disagree with the code it is about.
    /// </summary>
    private const float RetreatBand = Standoff * SpitterBehaviour.RetreatFraction;

    /// <summary>14 / 12 — the dodge window the whole archetype rests on.</summary>
    private const float FlightTime = Standoff / FlightSpeed;

    private const float MaxHp = 140f;
    private const float HitIFrames = 0.5f;

    /// <summary>Few enough hit points that one unscaled bolt ends the run (the session row).</summary>
    private const float FrailHp = 10f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;

    /// <summary>Simulated run time, advanced by every tick the helpers below take.</summary>
    private float _clock;

    /// <summary>
    /// The clock the allocation rows advance. A field rather than a local, so the measured closure
    /// is built over it once instead of capturing a fresh variable — a closure created inside the
    /// measurement would be the allocation it reported.
    /// </summary>
    private float _allocationClock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, ProjectileCapacity);
        _clock = 0f;
        _allocationClock = 0f;
    }

    // ---- Rule 5: noticing ----------------------------------------------------------------------

    [Test]
    public void Idle_UntilInsideAggroRange()
    {
        SpitterBehaviour far = Spitter(distance: AggroRange + 1f);

        _intents.Clear();

        Tick(far);

        Assert.That(far.State, Is.EqualTo(SpitterState.Idle));

        // Standing still is still an instruction (rule 10): an idle Spitter is not facing anywhere
        // in particular, so the facing goes out as zero and the body keeps its spawn rotation.
        Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(1));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));

        SpitterBehaviour near = Spitter(distance: AggroRange - 1f);

        Tick(near);

        Assert.That(near.State, Is.EqualTo(SpitterState.Approach),
            "At 29 m of 30 it has noticed, exactly as a Husk does — an enemy noticing you at 30 m " +
            "is a spawner's concern rather than a stealth mechanic.");
    }

    // ---- Rule 6: the band ----------------------------------------------------------------------

    [Test]
    public void Approach_WalksInFromBeyondStandoff()
    {
        SpitterBehaviour spitter = Approaching(distance: 20f);

        _intents.Clear();

        Tick(spitter);

        EnemyMoveIntent move = _intents.LastEnemyMove;

        // Straight up +Z at the authored 2.8 m/s: the direction is a unit vector, so the components
        // are the speed scaled by it and nothing else.
        Assert.That(move.Velocity.X, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(move.Velocity.Y, Is.EqualTo(0f), "Movement is on the ground plane; Y is the body's.");
        Assert.That(move.Velocity.Z, Is.EqualTo(MoveSpeed).Within(1e-4f));

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Approach), "Twenty metres is not fighting range.");
    }

    [Test]
    public void Approach_PlantsInsideTheBand()
    {
        // Twelve metres: past 9.8 and short of 14, which is the band it fights from.
        SpitterBehaviour spitter = Approaching(distance: 12f);

        Tick(spitter);

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Aim));
    }

    [Test]
    public void Approach_BacksOutBelowRetreatBand()
    {
        SpitterBehaviour spitter = Approaching(distance: 6f);

        _intents.Clear();

        Tick(spitter);

        // Away from the player, at its own speed. This is the half of rule 6 that makes a Spitter a
        // problem to be closed with rather than a slower Husk.
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(-MoveSpeed).Within(1e-4f));

        // And it keeps staring while it backs off: the velocity retreats, the facing does not.
        Assert.That(_intents.LastEnemyMove.FacingXZ.Y, Is.EqualTo(1f).Within(1e-4f));

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Approach),
            "Backing out is the same state as walking in — one question asked from either side.");
    }

    [Test]
    public void Approach_DoesNotOscillateAtTheEdge()
    {
        // Exactly at the standoff, then a shade inside it, then a shade outside: the gap between 14
        // and 9.8 is what stops a Spitter stepping in and out for ever at exactly fourteen metres.
        SpitterBehaviour spitter = Approaching(distance: Standoff);

        foreach (float distance in new[] { Standoff, 13.9f, 14.1f })
        {
            Place(spitter, distance);

            _intents.Clear();

            Tick(spitter);

            Assert.That(spitter.State, Is.EqualTo(SpitterState.Aim), $"at {distance} m");

            Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero),
                $"at {distance} m it is planted, not stepping — a retreat here would be the " +
                "oscillation RetreatFraction exists to prevent.");
        }
    }

    // ---- M2-11b rule 6: cover, and the promise a telegraph makes --------------------------------

    [Test]
    public void Spitter_HoldsFireWithoutSight()
    {
        // Inside the band, with a pillar in the way. GD §7.2 says cover blocks an enemy projectile,
        // and the honest place to block it is before the telegraph: a ring that produced nothing is
        // what AR §18.4's "a telegraph is a promise" forbids.
        SpitterBehaviour spitter = Approaching(distance: 12f);

        Blackboard(spitter).HasLineOfSight = false;

        _events.Clear();
        _intents.Clear();

        Tick(spitter);

        Assert.That(
            spitter.State,
            Is.EqualTo(SpitterState.Approach),
            "A Spitter that cannot see the player does not start a wind-up it cannot finish.");

        Assert.That(
            _events.Count<EnemyTelegraph>(),
            Is.EqualTo(0),
            "And it promises nothing, which is the whole reason the check is here and not in Aim.");

        // Planted, not walking and not retreating: flanking is a mind this archetype has not got,
        // and a Spitter waiting out a pillar has to read as waiting rather than as one that lost
        // interest.
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
        Assert.That(_intents.LastEnemyMove.FacingXZ.Y, Is.EqualTo(1f).Within(1e-4f));
    }

    [Test]
    public void Spitter_AimsWhenSightReturns()
    {
        SpitterBehaviour spitter = Approaching(distance: 12f);

        Blackboard(spitter).HasLineOfSight = false;

        Tick(spitter);

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Approach), "Sanity: it is waiting.");

        // The player steps out from behind the pillar. Nothing else about the arena changed.
        Blackboard(spitter).HasLineOfSight = true;

        _events.Clear();

        Tick(spitter);

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Aim));

        Assert.That(
            _events.Count<EnemyTelegraph>(),
            Is.EqualTo(1),
            "One ring, on the tick the sight line opened.");
    }

    [Test]
    public void Spitter_AimingIgnoresLostSight()
    {
        // The half of rule 6 that looks wrong and is not, and it is M2-07b rule 7 kept verbatim: an
        // aim never cancels. A wind-up breakable by stepping behind something would be breakable by
        // the same input the dodge already uses, and the archetype would never fire at a moving
        // target — so the pillar is a shield you have to be behind *before* the wind-up starts.
        SpitterBehaviour spitter = Aiming(distance: 12f);

        Blackboard(spitter).HasLineOfSight = false;

        _events.Clear();

        TickUntil(spitter, SpitterState.Release, budgetSeconds: WindupTime + (2f * Frame));

        Assert.That(
            spitter.State,
            Is.EqualTo(SpitterState.Release),
            "It still reaches the release with the player behind cover.");

        Assert.That(
            _events.Count<ProjectileFired>(),
            Is.EqualTo(1),
            "And the shot leaves. It arrives where the player was standing, which is the price of "
                + "the promise.");
    }

    // ---- Rule 11: the path in, the straight line out --------------------------------------------

    [Test]
    public void Approach_PrefersThePathDirection()
    {
        SpitterBehaviour spitter = Approaching(distance: 20f);

        // A NavMesh that says "go round the pillar" beats the straight line that would walk into it,
        // exactly as it does for a Husk. Ninety degrees off, so the two cannot be confused.
        Blackboard(spitter).PathDirectionToPlayer = new Vector2(1f, 0f);

        _intents.Clear();

        Tick(spitter);

        Assert.That(_intents.LastEnemyMove.Velocity.X, Is.EqualTo(MoveSpeed).Within(1e-4f));
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void Retreat_IgnoresThePathDirection()
    {
        SpitterBehaviour spitter = Approaching(distance: 6f);

        // The same path a walk in would follow, and a retreat must not use it: negating a path
        // direction points away from the *next waypoint* rather than away from the player, which
        // walks a Spitter into the pillar it was just routed around (rule 11).
        Blackboard(spitter).PathDirectionToPlayer = new Vector2(1f, 0f);

        _intents.Clear();

        Tick(spitter);

        Assert.That(_intents.LastEnemyMove.Velocity.X, Is.EqualTo(0f).Within(1e-4f),
            "A retreat that used the path would be walking sideways here.");
        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(-MoveSpeed).Within(1e-4f),
            "Exactly −DirectionToPlayer, at its own speed.");
    }

    // ---- Rule 7: the telegraph that never cancels -----------------------------------------------

    [Test]
    public void Aim_PublishesTelegraphOnce()
    {
        SpitterBehaviour spitter = Approaching(distance: 12f);

        _events.Clear();

        // The tick that plants, then four more well inside the 0.7 s window.
        for (int i = 0; i < 5; i++)
        {
            Tick(spitter);
        }

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Aim), "Sanity: still inside the windup.");

        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(1),
            "GD §9.1 rule 1: one tell per attack. A telegraph re-published every tick would make " +
            "the swell restart sixty times a second and read as no warning at all.");

        Assert.That(_events.Single<EnemyTelegraph>().Duration, Is.EqualTo(WindupTime).Within(1e-6f),
            "The duration is the archetype's, carried on the event so the view needs no catalog.");
    }

    [Test]
    public void Aim_DoesNotMove()
    {
        SpitterBehaviour spitter = Aiming(distance: 12f);

        _intents.Clear();

        Tick(spitter);

        EnemyMoveIntent move = _intents.LastEnemyMove;

        Assert.That(move.Velocity, Is.EqualTo(Vector3.Zero));

        // Facing is live even though the velocity is zero: the stare is the tell.
        Assert.That(move.FacingXZ.Y, Is.EqualTo(1f).Within(1e-4f));
    }

    [Test]
    public void Aim_NeverCancels()
    {
        SpitterBehaviour spitter = Aiming(distance: 12f);

        // Forty metres — past its own aggro range, never mind its standoff. A Husk's windup would
        // have been abandoned at a tenth of this.
        Place(spitter, distance: 40f);

        TickUntil(spitter, SpitterState.Release, budgetSeconds: WindupTime + (2f * Frame));

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Release),
            "Rule 7: the dodge window is the flight, not the telegraph. A Spitter that abandoned " +
            "its aim whenever the player moved would never fire, because moving is what the " +
            "player does.");
    }

    [Test]
    public void Aim_NeverCancelsWhenThePlayerClosesToMelee()
    {
        SpitterBehaviour spitter = Aiming(distance: 12f);

        // The other direction, and the one a player actually tries: charging it mid-aim. Inside its
        // own reach, inside the retreat band, and it still throws.
        Place(spitter, distance: 1f);

        TickUntil(spitter, SpitterState.Release, budgetSeconds: WindupTime + (2f * Frame));

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Release));
    }

    // ---- Rule 8: the shot --------------------------------------------------------------------------

    [Test]
    public void Release_FiresAtWhereThePlayerIsNow()
    {
        SpitterBehaviour spitter = Aiming(distance: 12f);

        // Five metres closer during the aim. The shot is aimed at where the player is at the moment
        // of release and never led (CC §3.7 is M5-01's question): an enemy that led its target would
        // remove the dodge the archetype exists to demand.
        Place(spitter, distance: 7f);

        _events.Clear();

        TickUntil(spitter, SpitterState.Release, budgetSeconds: WindupTime + (2f * Frame));

        ProjectileFired fired = _events.Single<ProjectileFired>();

        Assert.That(fired.Target.Z, Is.EqualTo(7f).Within(1e-4f),
            "Where the player is now, not where they were when the aim began.");
        Assert.That(fired.Origin, Is.EqualTo(Vector3.Zero), "It leaves from the Spitter.");
        Assert.That(fired.SpecId, Is.EqualTo(new ContentId(SpitterId)), "A view picks its mesh from this.");
        Assert.That(fired.SourceId, Is.EqualTo(Agent(spitter).Id));
    }

    [Test]
    public void Release_UsesTheDamageStat()
    {
        // d(20) = 1 + 0.035·19 = 1.665, on M2-06's authored 12. The read that makes this true is one
        // line in EnterRelease — ContactDamage.Value rather than a number off the projectile block —
        // and without it a stage-20 Spitter would throw for exactly what a stage-1 one throws
        // (ProjectileSpec carries no damage, deliberately, so that depth reaches a thrown shot).
        SpitterBehaviour spitter = AimingAtDepth(20);

        TickUntil(spitter, SpitterState.Release, budgetSeconds: WindupTime + (2f * Frame));

        ProjectileFired fired = _events.Single<ProjectileFired>();

        Land(fired, playerPosition: fired.Target);

        PlayerDamaged hit = _events.Single<PlayerDamaged>();

        Assert.That(hit.ToHp, Is.EqualTo(ContactDamage * 1.665f).Within(1e-3f),
            "GD §12.3's d(20) applied to the archetype's authored 12.");
    }

    [Test]
    public void Release_UsesSpecSpeedAndRadius()
    {
        SpitterBehaviour spitter = Aiming(distance: Standoff);

        TickUntil(spitter, SpitterState.Release, budgetSeconds: WindupTime + (2f * Frame));

        ProjectileFired fired = _events.Single<ProjectileFired>();

        // 14 m at 12 m/s. This number is the archetype: long enough to react to, short enough that
        // standing still is not free (GD §8.1, M2-07a rule 2).
        Assert.That(fired.FlightTime, Is.EqualTo(FlightTime).Within(1e-3f));
        Assert.That(fired.FlightTime, Is.EqualTo(1.1667f).Within(1e-3f));

        // And the forgiveness on it is the block's 1.6 m, not some default: 1.7 m away is a miss.
        Land(fired, playerPosition: Offset(fired.Target, BlastRadius + 0.1f));

        Assert.That(_events.Single<ProjectileImpacted>().HitPlayer, Is.False);

        // A second Spitter and a second shot, because one arrival can only be resolved against one
        // player position. 1.5 m away is inside the same radius and lands.
        _events.Clear();

        SpitterBehaviour other = Aiming(distance: Standoff);

        TickUntil(other, SpitterState.Release, budgetSeconds: WindupTime + (2f * Frame));

        ProjectileFired second = _events.Single<ProjectileFired>();

        Land(second, playerPosition: Offset(second.Target, BlastRadius - 0.1f));

        Assert.That(_events.Single<ProjectileImpacted>().HitPlayer, Is.True);
    }

    [Test]
    public void Release_LastsOneTick()
    {
        SpitterBehaviour spitter = Aiming(distance: 12f);

        TickUntil(spitter, SpitterState.Release, budgetSeconds: WindupTime + (2f * Frame));

        Tick(spitter);

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Recover),
            "One tick, ChaserState.Strike's shape: the state exists so the moment of the shot is " +
            "nameable by anything watching.");
    }

    [Test]
    public void Release_RefusedShot_StillRecovers()
    {
        // A sky with room for exactly one shot, already holding it. Fire answers NoProjectile and
        // publishes nothing (M2-07a rule 7).
        _projectiles = new ProjectileSystem(_events, capacity: 1);
        _projectiles.Fire(
            new Projectile(new ContentId(SpitterId), 0, Vector3.Zero, new Vector3(0f, 0f, 5f),
                FlightSpeed, BlastRadius, ContactDamage),
            _clock);

        SpitterBehaviour spitter = Aiming(distance: 12f);

        _events.Clear();

        TickUntil(spitter, SpitterState.Release, budgetSeconds: WindupTime + (2f * Frame));

        Assert.That(_events.Count<ProjectileFired>(), Is.Zero, "Sanity: the sky is full.");

        Tick(spitter);

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Recover),
            "The Spitter believes it fired, which is the only reading that does not require an " +
            "enemy to know the projectile system's capacity.");
    }

    // ---- Rule 9: the reload ------------------------------------------------------------------------

    [Test]
    public void Recover_RootedThenApproaches()
    {
        SpitterBehaviour spitter = Recovering(distance: 12f);

        // Rooted for all but the last frames of the 0.9 s. This is the window the player closes in,
        // so a Spitter that started kiting again early would be taking away the reward for it.
        TickFor(spitter, RecoverTime - (3f * Frame));

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Recover));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));

        TickUntil(spitter, SpitterState.Approach, budgetSeconds: 5f * Frame);

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Approach));
    }

    [Test]
    public void Recover_NeverReturnsToIdle()
    {
        SpitterBehaviour spitter = Recovering(distance: 12f);

        // Forty metres, past its aggro range, for the whole recovery: an enemy that has shot at you
        // knows where you are, and re-deciding aggro after every throw would let a player walk away
        // and switch it off.
        Place(spitter, distance: 40f);

        TickFor(spitter, RecoverTime + (2f * Frame));

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Approach));
    }

    // ---- Rule 10: one intent per tick, in every state ----------------------------------------------

    [Test]
    public void Tick_EmitsExactlyOneIntentPerState()
    {
        // EnemyView folds gravity into the same CharacterController.Move that carries the walk, so a
        // tick with no intent is a tick this body is not pinned to the floor by. It is the body's
        // rule rather than the archetype's, which is why it holds in all five states including the
        // standing ones.
        AssertOneIntentIn(SpitterState.Idle, Spitter(distance: AggroRange + 1f));
        AssertOneIntentIn(SpitterState.Approach, Approaching(distance: 20f));
        AssertOneIntentIn(SpitterState.Aim, Aiming(distance: 12f));
        AssertOneIntentIn(SpitterState.Release, Releasing(distance: 12f));
        AssertOneIntentIn(SpitterState.Recover, Recovering(distance: 12f));
    }

    // ---- Rule 12: the walk reads the agent's stat, not the archetype's float -----------------------

    [Test]
    public void Tick_WalksAtScaledSpeed()
    {
        // s(40) = 1 + 0.02·floor(40/5) = 1.16, on M2-06's authored 2.8 m/s. Without the stat read a
        // stage-40 Spitter would kite at exactly the speed a stage-1 one does.
        SpitterBehaviour spitter = ApproachingAtDepth(40, distance: 20f);

        _intents.Clear();

        Tick(spitter);

        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(MoveSpeed * 1.16f).Within(1e-3f));
    }

    // ---- Rule 13: the allocation budget ------------------------------------------------------------

    [Test]
    public void Tick_AllocatesNothing()
    {
        // A silent sink on both ends: RecordingEvents boxes every payload, so the telegraph and the
        // ProjectileFired this cycle publishes would be counted as core allocating when it is the
        // fake doing it.
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, ProjectileCapacity);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity);

        EnemyAgent agent = system.Spawn(new ContentId(SpitterId), Vector3.Zero);
        var spitter = (SpitterBehaviour)agent.Behaviour;

        Place(agent, distance: 12f);

        // Warm-up: a full fire cycle, so every one of the machine's transitions — the part of a state
        // machine that touches a dictionary at all — has run before anything is measured.
        for (int i = 0; i < 600; i++)
        {
            intents.Clear();
            spitter.Tick(new EnemyTickContext(Frame, i * Frame, player, intents, silent, projectiles, system));
        }

        Assert.That(spitter.State, Is.Not.EqualTo(SpitterState.Idle), "Sanity: the cycle is running.");

        AllocationAssert.None(() =>
        {
            intents.Clear();

            _allocationClock += Frame;

            spitter.Tick(new EnemyTickContext(Frame, _allocationClock, player, intents, silent, projectiles, system));
        });
    }

    // ---- Rule 14: what a recycled agent gets -------------------------------------------------------

    [Test]
    public void Reset_ReturnsToIdle()
    {
        SpitterBehaviour spitter = Aiming(distance: 12f);

        TickFor(spitter, WindupTime / 2f);

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Aim), "Sanity: mid-telegraph.");

        spitter.Reset();

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Idle));
        Assert.That(Blackboard(spitter).StateTimer, Is.Zero);
    }

    // ---- Rules 1 and 4: the seam and the agent -----------------------------------------------------

    [Test]
    public void Agent_BuildsASpitterForASpitterSpec()
    {
        EnemyAgent agent = _system.Spawn(new ContentId(SpitterId), Vector3.Zero);

        Assert.That(agent.Behaviour, Is.InstanceOf<SpitterBehaviour>());

        // Rule 1: reached through the seam rather than through a typed field. The property's declared
        // type is the assertion — a Husk and a Spitter are the same kind of thing to everything
        // outside them, which is what makes M2-08's Bloater a third implementer and not a third
        // field.
        PropertyInfo behaviour = typeof(EnemyAgent).GetProperty(nameof(EnemyAgent.Behaviour));

        Assert.That(behaviour, Is.Not.Null);
        Assert.That(behaviour.PropertyType, Is.EqualTo(typeof(IEnemyBehaviour)));
    }

    [Test]
    public void Agent_KeepsItsBehaviourOnSameKindRecycle()
    {
        EnemyAgent first = _system.Spawn(new ContentId(SpitterId), Vector3.Zero);
        IEnemyBehaviour behaviour = first.Behaviour;

        Place(first, distance: 12f);
        Tick((SpitterBehaviour)behaviour);
        Tick((SpitterBehaviour)behaviour);

        Assert.That(((SpitterBehaviour)behaviour).State, Is.EqualTo(SpitterState.Aim), "Sanity: mid-cycle.");

        _system.Despawn(first.Id);

        EnemyAgent second = _system.Spawn(new ContentId(SpitterId), Vector3.Zero);

        Assert.That(second, Is.SameAs(first), "Sanity: the registry recycles agents (M1-05).");

        // Rule 4: a wave of one archetype recycles for free. A behaviour rebuilt per spawn would
        // allocate three dictionaries and nine delegates on a path a wave walks sixty times.
        Assert.That(second.Behaviour, Is.SameAs(behaviour));

        // And it comes back unaware rather than mid-telegraph.
        Assert.That(((SpitterBehaviour)second.Behaviour).State, Is.EqualTo(SpitterState.Idle));
    }

    [Test]
    public void Agent_ReplacesItOnKindChange()
    {
        EnemyAgent agent = _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        Assert.That(agent.Behaviour, Is.InstanceOf<ChaserBehaviour>(), "Sanity: a Husk chases.");

        _system.Despawn(agent.Id);

        EnemyAgent recycled = _system.Spawn(new ContentId(SpitterId), Vector3.Zero);

        Assert.That(recycled, Is.SameAs(agent), "Sanity: the same agent, a different archetype.");

        // The cost rule 4 accepts: one small object per *changed* rental, on the spawn path rather
        // than the frame path. The alternative — a field per kind on the agent — is a list that gets
        // one entry too short the first time somebody adds an archetype.
        Assert.That(recycled.Behaviour, Is.InstanceOf<SpitterBehaviour>());
        Assert.That(((SpitterBehaviour)recycled.Behaviour).State, Is.EqualTo(SpitterState.Idle));
    }

    // ---- Rules 2 and 3: the context and the dispatch -----------------------------------------------

    [Test]
    public void Context_IsBuiltOncePerTickAndAllocatesNothing()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, ProjectileCapacity);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity);

        // M2-04's concurrency cap, all of one archetype and all in the band, so every one of them
        // runs the same cycle at the same time.
        for (int i = 0; i < 28; i++)
        {
            EnemyAgent agent = system.Spawn(new ContentId(SpitterId), Vector3.Zero);

            Place(agent, distance: 12f);
        }

        for (int i = 0; i < 600; i++)
        {
            intents.Clear();
            system.Tick(new EnemyTickContext(Frame, i * Frame, player, intents, silent, projectiles, system));
        }

        // The context is constructed *inside* the measured body, which is the half of rule 2 this
        // row can prove outright: a struct that had quietly become a class would be 28 × 10 000
        // allocations, and building it outside would hide exactly that.
        AllocationAssert.None(() =>
        {
            intents.Clear();

            _allocationClock += Frame;

            system.Tick(new EnemyTickContext(Frame, _allocationClock, player, intents, silent, projectiles, system));
        });

        // The other half — one reading of the clock for the whole arena — asserted the only way a
        // behaviour's view of `now` is observable from outside: 28 Spitters released together, and
        // every shot arrives on the same tick. A context rebuilt per agent from a drifting clock
        // would scatter these arrivals across frames.
        var shots = new RecordingEvents();
        var arena = new EnemySystem(Catalog(), shots, new FixedRandom(), Scaling(), Capacity);
        var sky = new ProjectileSystem(shots, ProjectileCapacity);

        for (int i = 0; i < 28; i++)
        {
            Place(arena.Spawn(new ContentId(SpitterId), Vector3.Zero), distance: Standoff);
        }

        for (int i = 0; i < 200 && shots.Count<ProjectileFired>() == 0; i++)
        {
            arena.Tick(new EnemyTickContext(Frame, i * Frame, player, intents, shots, sky, arena));
        }

        IReadOnlyList<ProjectileFired> fired = shots.Of<ProjectileFired>();

        Assert.That(fired.Count, Is.EqualTo(28), "All 28 released on the same tick.");

        foreach (ProjectileFired shot in fired)
        {
            Assert.That(shot.FlightTime, Is.EqualTo(FlightTime).Within(1e-4f));
        }
    }

    [Test]
    public void System_DispatchesSpitters()
    {
        EnemyAgent husk = _system.Spawn(new ContentId(HuskId), Vector3.Zero);
        EnemyAgent spitter = _system.Spawn(new ContentId(SpitterId), Vector3.Zero);

        Place(husk, distance: 12f);
        Place(spitter, distance: 12f);

        _intents.Clear();

        _system.Tick(Context());

        // Both behaviours ran, and each wrote exactly one instruction for its own body. The two share
        // a line in the dispatch, which is the seam earning its keep.
        Assert.That(_intents.CountEnemyMoves(husk.Id), Is.EqualTo(1));
        Assert.That(_intents.CountEnemyMoves(spitter.Id), Is.EqualTo(1));

        Assert.That(((ChaserBehaviour)husk.Behaviour).State, Is.EqualTo(ChaserState.Chase));
        Assert.That(((SpitterBehaviour)spitter.Behaviour).State, Is.EqualTo(SpitterState.Approach));
    }

    [Test]
    public void System_StillThrowsOnAnUnhandledKind()
    {
        // M1-05's rule, unchanged by the seam: the dispatch is the one site that knows the full set
        // of kinds, so a kind added without teaching it about the new behaviour fails loudly here
        // instead of standing motionless in the arena with nothing in the log. It is also what makes
        // an asset flipped ahead of its PR a red run rather than a silent one.
        var catalog = new ContentCatalog(
            Array.Empty<CharacterSpec>(),
            new[] { Enemy("enemy.unhandled", (EnemyBehaviourKind)99) });

        var system = new EnemySystem(catalog, _events, new FixedRandom(), Scaling(), Capacity);

        system.Spawn(new ContentId("enemy.unhandled"), Vector3.Zero);

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => system.Tick(Context()));

        Assert.That(thrown.Message, Does.Contain("99"), "The message names the kind.");
        Assert.That(thrown.Message, Does.Contain("enemy.unhandled"), "And the archetype.");
    }

    // ---- The end-to-end row M2-07a handed over -----------------------------------------------------

    [Test]
    public void Session_ShotThatKills_EndsTheRunWithNoIntent()
    {
        // The row M2-07a could not write: RunState.Projectiles is internal with no InternalsVisibleTo
        // (AR §18.2), so nothing could put a shot into a *live run* until something could fire one.
        // This is that — a real session, a real Spitter, and a bolt that kills on arrival.
        RunSession session = Session();

        session.Start(Config());

        var snapshot = new WorldSnapshot(Capacity);
        var playerPosition = new Vector3(0f, 0f, 12f);

        // Four seconds of frames: notice, plant, a 0.7 s aim, and a 1 s flight from 12 m at 12 m/s.
        for (int i = 0; i < 480 && session.IsRunning; i++)
        {
            _events.Clear();
            _intents.Clear();

            snapshot.Clear();
            snapshot.Dt = Frame;
            snapshot.PlayerPosition = playerPosition;

            AddSense(snapshot, id: 1, position: Vector3.Zero);

            session.Tick(snapshot);
        }

        Assert.That(session.IsRunning, Is.False, "The bolt landed and the run is over.");

        // The ordering AR §18.1 describes, on the one tick that exercises all of it: the arrival is
        // resolved before the death check, so the damage, the death and the farewell all land on the
        // tick the shot arrived — and PlayerDied precedes RunEnded, whichever of a Husk's strike or a
        // bolt's arrival was the thing that killed.
        Assert.That(TypesOf(_events.All), Is.EqualTo(new[]
        {
            typeof(PlayerDamaged),
            typeof(PlayerDied),
            typeof(ProjectileImpacted),
            typeof(RunEnded),
        }));

        // And nothing steered the corpse. RunSession.Tick returns before TickBody on the tick a run
        // ends, because the intent it would write is a velocity for a run that is over and
        // RunTicker would apply it to a body nobody is driving any more.
        Assert.That(_intents.PlayerMoves, Is.Empty);
    }

    // ---- Guards ------------------------------------------------------------------------------------

    [Test]
    public void Ctor_NullAgent_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SpitterBehaviour(null));
    }

    [Test]
    public void Context_NullDependencies_Throw()
    {
        // Guarded here rather than in every Tick, which is the other half of why the context is a
        // struct: the checks run once a tick for the whole arena instead of once per enemy.
        Assert.Throws<ArgumentNullException>(
            () => new EnemyTickContext(Frame, 0f, null, _intents, _events, _projectiles, _system));
        Assert.Throws<ArgumentNullException>(
            () => new EnemyTickContext(Frame, 0f, _player, null, _events, _projectiles, _system));
        Assert.Throws<ArgumentNullException>(
            () => new EnemyTickContext(Frame, 0f, _player, _intents, null, _projectiles, _system));
        Assert.Throws<ArgumentNullException>(
            () => new EnemyTickContext(Frame, 0f, _player, _intents, _events, null, _system));
    }

    [Test]
    public void Context_NonFiniteClock_Throws()
    {
        // AR §18.3. A NaN clock does not produce a wrong enemy, it produces a silent one: every
        // comparison a state timer makes against it is false, so an aim never completes and nothing
        // reports it.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnemyTickContext(bad, 0f, _player, _intents, _events, _projectiles, _system),
                $"dt {bad}");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => new EnemyTickContext(Frame, bad, _player, _intents, _events, _projectiles, _system),
                $"now {bad}");
        }
    }

    // ---- Fixture helpers -----------------------------------------------------------------------

    /// <summary>A spawned Spitter with the player <paramref name="distance"/> away along +Z, idle.</summary>
    private SpitterBehaviour Spitter(float distance)
    {
        EnemyAgent agent = _system.Spawn(new ContentId(SpitterId), Vector3.Zero);

        Assert.That(agent.Behaviour, Is.InstanceOf<SpitterBehaviour>(),
            "A Spitter archetype must have been given a behaviour.");

        Place(agent, distance);

        return (SpitterBehaviour)agent.Behaviour;
    }

    /// <summary>The same, ticked once so it has noticed the player and is closing.</summary>
    private SpitterBehaviour Approaching(float distance)
    {
        SpitterBehaviour spitter = Spitter(distance);

        Tick(spitter);

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Approach), "Sanity: the fixture starts in Approach.");

        return spitter;
    }

    /// <summary>The same again, planted inside the band and mid-telegraph.</summary>
    private SpitterBehaviour Aiming(float distance)
    {
        SpitterBehaviour spitter = Approaching(distance);

        Tick(spitter);

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Aim), "Sanity: the fixture starts in Aim.");

        return spitter;
    }

    /// <summary>The same again, on the one tick the shot leaves.</summary>
    private SpitterBehaviour Releasing(float distance)
    {
        SpitterBehaviour spitter = Aiming(distance);

        TickUntil(spitter, SpitterState.Release, budgetSeconds: WindupTime + (2f * Frame));

        return spitter;
    }

    /// <summary>The same again, reloading.</summary>
    private SpitterBehaviour Recovering(float distance)
    {
        SpitterBehaviour spitter = Releasing(distance);

        TickUntil(spitter, SpitterState.Recover, budgetSeconds: 2f * Frame);

        return spitter;
    }

    /// <summary>
    /// A Spitter spawned at <paramref name="depth"/> and ticked into <see cref="SpitterState.Approach"/>.
    /// </summary>
    /// <remarks>
    /// A system of its own rather than the fixture's, because <c>EnemySystem.Depth</c> is read at
    /// spawn and the fixture's own Spitters must stay unscaled — every other row asserts M2-06's
    /// authored numbers, and they are only true at depth 1.
    /// </remarks>
    private SpitterBehaviour ApproachingAtDepth(int depth, float distance)
    {
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity)
        {
            Depth = depth,
        };

        return Approaching(distance);
    }

    /// <summary>The same, planted and mid-telegraph at the standoff.</summary>
    private SpitterBehaviour AimingAtDepth(int depth)
    {
        SpitterBehaviour spitter = ApproachingAtDepth(depth, distance: Standoff);

        Tick(spitter);

        Assert.That(spitter.State, Is.EqualTo(SpitterState.Aim), "Sanity: it is aiming.");

        return spitter;
    }

    /// <summary>
    /// Puts the player <paramref name="distance"/> metres away along +Z, as
    /// <c>EnemySystem.Perceive</c> would have.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All five fields together, never one of them: a distance that disagrees with the two positions
    /// would make a release aim somewhere its own arithmetic says the player is not, and the row
    /// that then failed would be about the fixture rather than the behaviour.
    /// </para>
    /// <para>
    /// <b>Line of sight is the fifth, and it has to be stated</b> (M2-11b). <c>EnemyBlackboard.Reset</c>
    /// leaves it <see langword="false"/>, and a Spitter with no sight does not enter <c>Aim</c> —
    /// so every row in this file that reaches a telegraph would be asserting that cover was in the
    /// way rather than that the archetype works. Open ground is what the rest of the fixture
    /// describes, so open ground is what this says; the rows that want a pillar clear it after
    /// calling this.
    /// </para>
    /// </remarks>
    private static void Place(EnemyAgent agent, float distance)
    {
        EnemyBlackboard blackboard = agent.Blackboard;

        blackboard.SelfPosition = agent.Position;
        blackboard.PlayerPosition = agent.Position + new Vector3(0f, 0f, distance);
        blackboard.DistanceToPlayer = distance;
        blackboard.DirectionToPlayer = new Vector2(0f, 1f);
        blackboard.HasLineOfSight = true;
    }

    private void Place(SpitterBehaviour spitter, float distance) => Place(Agent(spitter), distance);

    /// <summary>One tick at <see cref="Frame"/>, advancing the fixture's clock with it.</summary>
    /// <remarks>
    /// The clock is a field shared by every helper here rather than a counter restarted per call,
    /// because a landing stamps <c>Health</c>'s i-frames with it: a fixture that replayed the same
    /// seconds on each helper would have every hit after the first blocked by the one before it.
    /// </remarks>
    private void Tick(SpitterBehaviour spitter)
    {
        spitter.Tick(Context());

        _clock += Frame;
    }

    /// <summary>Ticks for at least <paramref name="seconds"/>, a frame at a time.</summary>
    private void TickFor(SpitterBehaviour spitter, float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            Tick(spitter);
        }
    }

    /// <summary>
    /// Ticks until <paramref name="spitter"/> reaches <paramref name="state"/>, and stops the moment
    /// it does. Fails if it has not within <paramref name="budgetSeconds"/>.
    /// </summary>
    /// <remarks>
    /// The rows do not count frames for <c>ChaserBehaviourTests</c>' reason: <c>StateTimer</c> is a
    /// running sum of 1/120 s steps, so whether a 0.7 s windup completes on frame 84 or 85 is float
    /// accumulation rather than behaviour. Stopping on arrival is also what makes this usable around
    /// <see cref="SpitterState.Release"/>, which lasts exactly one tick.
    /// </remarks>
    private void TickUntil(SpitterBehaviour spitter, SpitterState state, float budgetSeconds)
    {
        int frames = (int)MathF.Ceiling(budgetSeconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            if (spitter.State == state)
            {
                return;
            }

            Tick(spitter);
        }

        Assert.That(
            spitter.State,
            Is.EqualTo(state),
            $"Still {spitter.State} after {frames} frames; expected to have reached {state}.");
    }

    /// <summary>One tick in <paramref name="state"/> writes exactly one intent, and no more.</summary>
    private void AssertOneIntentIn(SpitterState state, SpitterBehaviour spitter)
    {
        Assert.That(spitter.State, Is.EqualTo(state), "Sanity: the fixture reached the state it is about.");

        int id = Agent(spitter).Id;

        _intents.Clear();

        Tick(spitter);

        Assert.That(_intents.CountEnemyMoves(id), Is.EqualTo(1), $"in {state}");
    }

    /// <summary>Lands <paramref name="fired"/> with the player standing at <paramref name="playerPosition"/>.</summary>
    private void Land(in ProjectileFired fired, Vector3 playerPosition)
    {
        _projectiles.Tick(_clock + fired.FlightTime, playerPosition, _player);
    }

    /// <summary>This frame's context, built from the fixture's own clock and ports.</summary>
    private EnemyTickContext Context() =>
        new EnemyTickContext(Frame, _clock, _player, _intents, _events, _projectiles, _system);

    /// <summary>The agent whose behaviour this is, found by walking the registry.</summary>
    private EnemyAgent Agent(SpitterBehaviour spitter)
    {
        ReadOnlySpan<EnemyAgent> agents = _system.Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            if (ReferenceEquals(agents[i].Behaviour, spitter))
            {
                return agents[i];
            }
        }

        Assert.Fail("No registered agent owns this behaviour.");

        return null;
    }

    private EnemyBlackboard Blackboard(SpitterBehaviour spitter) => Agent(spitter).Blackboard;

    /// <summary><paramref name="metres"/> to one side of <paramref name="point"/>, on the ground.</summary>
    private static Vector3 Offset(Vector3 point, float metres) => point + new Vector3(metres, 0f, 0f);

    private static Type[] TypesOf(IReadOnlyList<object> events)
    {
        var types = new Type[events.Count];

        for (int i = 0; i < events.Count; i++)
        {
            types[i] = events[i].GetType();
        }

        return types;
    }

    private static void AddSense(WorldSnapshot snapshot, int id, Vector3 position)
    {
        ref EnemySense sense = ref snapshot.AddEnemy();

        sense.Id = id;
        sense.Position = position;
        sense.Velocity = Vector3.Zero;
        sense.PathDirectionToPlayer = Vector2.Zero;
        sense.HasLineOfSight = true;
    }

    private RunSession Session() => new RunSession(
        new ContentCatalog(new[] { Frail() }, new[] { Husk(), Spitter() }, new[] { Descent() }),
        new FixedRandom(Seed),
        _events,
        _intents,
        new RunRecorder(new FixedRandom(Seed), new FixedClock(default), _events),
        Capacity,
        Capacity,
        ProjectileCapacity);

    /// <summary>One Spitter, standing at the origin, and nothing else in the arena.</summary>
    private static RunConfig Config() => new RunConfig(
        new ContentId(DescentId),
        new ContentId(OathboundId),
        Seed,
        1,
        new SpawnPlan(new[] { new SpawnPlan.Entry(new ContentId(SpitterId), Vector3.Zero) }));

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk(), Spitter() });

    /// <summary>GD §12's curves, required by every <c>EnemySystem</c> as of M2-03.</summary>
    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    /// <summary>GD §8.1's Spitter, with M2-06's numbers and its projectile block.</summary>
    private static EnemySpec Spitter() => Enemy(SpitterId, EnemyBehaviourKind.Spitter);

    /// <summary>GD §8.1's Husk — the other side of the dispatch row.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 36f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: 4,
        isElite: false,
        contactDamage: 8f,
        reach: Reach,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: AggroRange,
        behaviour: EnemyBehaviourKind.Chaser);

    /// <summary>
    /// An archetype with the Spitter's numbers under whatever <paramref name="behaviour"/> a row
    /// needs — including a kind nobody has written, which is what the dispatch row is about.
    /// </summary>
    private static EnemySpec Enemy(string id, EnemyBehaviourKind behaviour) => new EnemySpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        maxHp: 28f,
        moveSpeed: MoveSpeed,
        targetPriority: 3,
        threatCost: 7,
        isElite: false,
        contactDamage: ContactDamage,
        reach: Reach,
        windupTime: WindupTime,
        recoverTime: RecoverTime,
        aggroRange: AggroRange,
        behaviour: behaviour,
        projectile: new ProjectileSpec(Standoff, FlightSpeed, BlastRadius));

    /// <summary>Descent with an empty roster: the session row is not about a schedule.</summary>
    private static ModeSpec Descent() => new ModeSpec(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Array.Empty<RosterEntry>());

    /// <summary>
    /// The Oathbound of CC §7, with <b>no Aegis</b>: every damage row here reads what a landing
    /// cost, and a 30-point shield would absorb all of it.
    /// </summary>
    /// <remarks>
    /// Focus is switched off with a <c>MaxMultiplier</c> of 1, the isolation <c>PlayerCombatTests</c>
    /// uses: no row here is about the player's swing rate, and a live ramp would be background noise
    /// in a fixture about enemies.
    /// </remarks>
    private static CharacterSpec Oathbound(float maxHp = MaxHp) => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        maxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 16f, 4f, 0.05f),
        null,
        HitIFrames);

    /// <summary>The same character with ten hit points, so one unscaled bolt ends the run.</summary>
    private static CharacterSpec Frail() => Oathbound(FrailHp);
}
