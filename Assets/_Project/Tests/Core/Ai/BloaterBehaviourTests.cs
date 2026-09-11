using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// The M2-08 spec's thirteen rules: the blast that belongs to the corpse, and the Bloater that
/// waddles in, commits, and goes off.
/// </summary>
/// <remarks>
/// <para>
/// GD §8.1's Bloater throughout, with M2-06's authored numbers — 24 HP, 2.2 m/s, 15 contact damage,
/// a 2 m contact trigger, a 0.8 s fuse and a 3 m blast — so a failure reads as "the archetype we
/// ship stopped being escapable" rather than as an arithmetic puzzle. Rows that vary a number say
/// which and why.
/// </para>
/// <para>
/// <b>Perception goes through <c>EnemySystem.Ingest</c> rather than being written by hand</b>, which
/// is the one place this fixture departs from <c>SpitterBehaviourTests</c>'. A blast is resolved
/// against the player position the <em>system</em> last ingested (rule 5), not against the
/// blackboard, so a fixture that assigned the blackboard directly would leave every explosion
/// measuring its distance to the origin while the row believed it had moved the player. Sensing the
/// whole arena in one call keeps the two halves — what the Bloater perceives and what the blast is
/// resolved against — incapable of disagreeing.
/// </para>
/// <para>
/// <b>The player has no Aegis here</b>, like the Spitter's fixture and for its reason: every damage
/// row below reads what a blast cost, and a 30-point shield would absorb all of it and turn each of
/// them into a test of the Aegis instead.
/// </para>
/// <para>
/// <b>The asset row lives in <c>Soulvail.Tests.Game</c></b>, not here:
/// <c>EnemyLookTests.Assets_AuthoredStaticUntilTheirBehaviourExists</c> is what flips with
/// <c>Bloater.asset</c>, because loading one needs the <c>AssetDatabase</c> and this assembly has no
/// engine reference. The blast radius is pinned beside it in <c>Bloater_MatchesDesign</c>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BloaterBehaviourTests
{
    private const string BloaterId = "enemy.bloater";
    private const string HuskId = "enemy.husk";
    private const string OathboundId = "character.oathbound";
    private const string DescentId = "mode.descent";

    /// <summary>Room for M2-04's concurrency cap.</summary>
    private const int Capacity = 32;

    private const int ProjectileCapacity = 64;

    /// <summary>What the session row seeds with. <c>RunConfig</c> and the generator must agree.</summary>
    private const int Seed = 11;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    // GD §8.1 and M2-06, the numbers the Bloater's fight is made of.
    private const float BloaterMaxHp = 24f;
    private const float MoveSpeed = 2.2f;
    private const float ContactDamage = 15f;

    /// <summary>The contact trigger — where the fuse starts. <b>Not</b> the blast radius.</summary>
    private const float Reach = 2f;

    private const float FuseTime = 0.8f;
    private const float AggroRange = 30f;

    /// <summary>Metres the blast reaches (GD §8.1). Half again as far as the trigger, which is rule 9.</summary>
    private const float BlastRadius = 3f;

    private const float MaxHp = 140f;
    private const float HitIFrames = 0.5f;

    /// <summary>GD §12.4's ceiling: 35 % of the Oathbound's 140.</summary>
    private const float OneShotCeiling = 49f;

    /// <summary>
    /// The speed rule 9's arithmetic is written against, and the speed the Oathbound no longer has.
    /// See <see cref="Fuse_IsEscapable"/>, which pins both.
    /// </summary>
    private const float SpecEscapeSpeed = 5.4f;

    /// <summary>What the owner's post-M2-03 retune actually left the Oathbound walking at.</summary>
    private const float ShippedMoveSpeed = 3f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WorldSnapshot _snapshot;

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
        _snapshot = new WorldSnapshot(Capacity);
        _clock = 0f;
        _allocationClock = 0f;
    }

    // ---- Rule 7: noticing and waddling --------------------------------------------------------

    [Test]
    public void Idle_UntilInsideAggroRange()
    {
        BloaterBehaviour far = Bloater(distance: AggroRange + 1f);

        _intents.Clear();

        Tick(far);

        Assert.That(far.State, Is.EqualTo(BloaterState.Idle));

        // Standing still is still an instruction (rule 10): an idle Bloater is not facing anywhere
        // in particular, so the facing goes out as zero and the body keeps its spawn rotation.
        Assert.That(_intents.EnemyMoves.Count, Is.EqualTo(1));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));

        BloaterBehaviour near = Bloater(distance: AggroRange - 1f);

        Tick(near);

        Assert.That(near.State, Is.EqualTo(BloaterState.Waddle),
            "At 29 m of 30 it has noticed, exactly as a Husk does.");
    }

    [Test]
    public void Waddle_WalksAtScaledSpeed()
    {
        // A stage-40 Bloater, so the row reads the agent's Stat rather than the spec's float: if it
        // read the spec, depth scaling would silently stop reaching the walk and a deep Bloater
        // would be outrun by everything around it.
        BloaterBehaviour bloater = WaddlingAtDepth(depth: 40, distance: 10f);

        _intents.Clear();

        Tick(bloater);

        float expected = Agent(bloater).MoveSpeed.Value;

        Assert.That(expected, Is.GreaterThan(MoveSpeed), "Sanity: depth 40 is faster than the base 2.2.");

        Assert.That(_intents.LastEnemyMove.Velocity.Z, Is.EqualTo(expected).Within(1e-4f));
        Assert.That(_intents.LastEnemyMove.Velocity.Y, Is.EqualTo(0f), "Movement is on the ground plane.");
    }

    [Test]
    public void Waddle_PrefersThePathDirection()
    {
        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        // The player is straight ahead on +Z and the NavMesh says to set off sideways on +X, which
        // is what going round a pillar looks like from inside a behaviour. A Bloater that ignored
        // the path would walk into the pillar and fuse against it.
        Sense(playerAt: new Vector3(0f, 0f, 10f), pathDirection: new Vector2(1f, 0f));

        var bloater = (BloaterBehaviour)agent.Behaviour;

        Tick(bloater);

        Assert.That(bloater.State, Is.EqualTo(BloaterState.Waddle), "Sanity: it has noticed.");

        _intents.Clear();

        Tick(bloater);

        EnemyMoveIntent move = _intents.LastEnemyMove;

        Assert.That(move.Velocity.X, Is.EqualTo(MoveSpeed).Within(1e-4f), "It follows the path, not the line.");
        Assert.That(move.Velocity.Z, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void Waddle_PlantsAtReach()
    {
        // 1.9 m of the 2 m trigger. The reach test runs after the move, so the tick that arrives in
        // range still spends its intent walking — ChaserBehaviour.TickChase's rule, kept.
        BloaterBehaviour bloater = Waddling(distance: 1.9f);

        Tick(bloater);

        Assert.That(bloater.State, Is.EqualTo(BloaterState.Fuse));
    }

    // ---- Rule 8: the fuse ----------------------------------------------------------------------

    [Test]
    public void Fuse_PublishesTelegraphOnce()
    {
        BloaterBehaviour bloater = Fusing(distance: 1.5f);

        _events.Clear();

        // Five ticks well inside the 0.8 s window, so nothing here reaches the detonation.
        for (int i = 0; i < 5; i++)
        {
            Tick(bloater);
        }

        Assert.That(_events.Count<EnemyTelegraph>(), Is.Zero,
            "The telegraph is published on entry, not on every tick of the swell.");

        // And the one that was published named the fuse's own length, so M1-12's swell fills the
        // window the player actually has rather than a number the view guessed at.
        BloaterBehaviour second = Bloater(distance: 1.5f);

        _events.Clear();

        Tick(second);
        Tick(second);

        Assert.That(second.State, Is.EqualTo(BloaterState.Fuse), "Sanity: it planted.");

        IReadOnlyList<EnemyTelegraph> telegraphs = _events.Of<EnemyTelegraph>();

        Assert.That(telegraphs.Count, Is.EqualTo(1));
        Assert.That(telegraphs[0].Id, Is.EqualTo(Agent(second).Id));
        Assert.That(telegraphs[0].Duration, Is.EqualTo(FuseTime).Within(1e-4f));
    }

    [Test]
    public void Fuse_DoesNotMove()
    {
        BloaterBehaviour bloater = Fusing(distance: 1.5f);

        _intents.Clear();

        Tick(bloater);

        EnemyMoveIntent move = _intents.LastEnemyMove;

        Assert.That(move.Velocity, Is.EqualTo(Vector3.Zero), "Planted: the commitment is to a place.");

        // Live facing even at zero velocity — see EnemyMoveIntent on why the two are separate
        // fields. The stare is what makes a lit fuse read as aimed at the player.
        Assert.That(move.FacingXZ.Y, Is.EqualTo(1f).Within(1e-4f));
    }

    [Test]
    public void Fuse_NeverCancels()
    {
        BloaterBehaviour bloater = Fusing(distance: 1.5f);

        EnemyAgent agent = Agent(bloater);

        // The player runs to 40 m — past the 30 m aggro range entirely, which is further than any
        // cancel band could reasonably be drawn.
        Sense(playerAt: new Vector3(0f, 0f, 40f));

        TickUntilDead(bloater, budgetSeconds: FuseTime + (4f * Frame));

        Assert.That(agent.IsAlive, Is.False,
            "It detonates anyway (rule 8): the dodge window is walking out of the radius, not " +
            "making it change its mind.");

        Assert.That(_events.Count<EnemyExploded>(), Is.EqualTo(1));

        Assert.That(_events.Of<EnemyExploded>()[0].HitPlayer, Is.False,
            "And it caught nobody, which is the whole of what escaping looks like.");
    }

    [Test]
    public void Fuse_KillsItselfAtTheEnd()
    {
        BloaterBehaviour bloater = Fusing(distance: 1.5f);

        EnemyAgent agent = Agent(bloater);

        _events.Clear();

        TickUntilDead(bloater, budgetSeconds: FuseTime + (4f * Frame));

        // Rules 1 and 8: the fuse kills it through the one door damage reaches an enemy through, and
        // EnemySystem does the rest off the spec. The order is the assertion — a blast that arrived
        // before the death would mean something other than ApplyDamage had published it.
        Assert.That(TypesOf(_events.All), Does.Contain(typeof(EnemyDamaged)));

        IReadOnlyList<EnemyDamaged> damage = _events.Of<EnemyDamaged>();

        Assert.That(damage[damage.Count - 1].Killed, Is.True);
        Assert.That(damage[damage.Count - 1].Id, Is.EqualTo(agent.Id));

        AssertOrder(typeof(EnemyDamaged), typeof(EnemyDied), typeof(EnemyExploded));

        // It killed itself with exactly what it had left, not with an arbitrary large number.
        Assert.That(agent.Health.Current, Is.Zero);
    }

    [Test]
    public void Fuse_DetonatesOnceOnly()
    {
        BloaterBehaviour bloater = Fusing(distance: 1.5f);

        TickUntilDead(bloater, budgetSeconds: FuseTime + (4f * Frame));

        Assert.That(_events.Count<EnemyExploded>(), Is.EqualTo(1), "Sanity: it went off.");

        // Kept ticking past its own death, which is what a corpse in the span gets for the rest of
        // CorpseTime. Exactly once per life without a flag to remember it: ApplyDamage finds a
        // corpse and answers None, so every later attempt is silent (rule 1).
        for (int i = 0; i < 60; i++)
        {
            Tick(bloater);
        }

        Assert.That(_events.Count<EnemyExploded>(), Is.EqualTo(1));
    }

    // ---- Rules 4 and 5: what the blast reaches -------------------------------------------------

    [Test]
    public void Explode_HitsThePlayerInsideTheRadius()
    {
        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        // 2.5 m of the 3 m radius, which is also outside the 2 m trigger — so this is a blast
        // reaching someone who never got close enough to light the fuse themselves.
        Sense(playerAt: new Vector3(0f, 0f, 2.5f));

        float before = _player.Health.Current;

        Kill(agent);

        // Reached through the PlayerCombat that ApplyDamage now takes (rules 3, 5), which is also
        // what makes the player's i-frames apply to a blast without the blast knowing about them.
        Assert.That(_player.Health.Current, Is.EqualTo(before - ContactDamage).Within(1e-4f));

        EnemyExploded blast = Single<EnemyExploded>();

        Assert.That(blast.HitPlayer, Is.True);
        Assert.That(blast.Radius, Is.EqualTo(BlastRadius).Within(1e-4f));
        Assert.That(blast.Position, Is.EqualTo(Vector3.Zero), "The centre is where it died.");
        Assert.That(blast.SpecId.Value, Is.EqualTo(BloaterId));
        Assert.That(blast.Id, Is.EqualTo(agent.Id));
    }

    [Test]
    public void Explode_MissesOutsideTheRadius()
    {
        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        Sense(playerAt: new Vector3(0f, 0f, 3.2f));

        float before = _player.Health.Current;

        Kill(agent);

        Assert.That(_player.Health.Current, Is.EqualTo(before).Within(1e-4f), "Twenty centimetres clear.");

        // Published anyway (rule 5). A view has to draw the flash either way, and a blast that is
        // invisible when it misses teaches the player that near misses did not happen.
        EnemyExploded blast = Single<EnemyExploded>();

        Assert.That(blast.HitPlayer, Is.False);
    }

    [Test]
    public void Fuse_IsEscapable()
    {
        // Rule 9's arithmetic, pinned so that retuning any of the four numbers — the 2 m trigger,
        // the 0.8 s fuse, the 3 m radius, the player's walk speed — fails here rather than on a
        // phone. Both speeds are asserted because the spec's prose and the shipping asset disagree:
        // rule 9 is written against 5.4 m/s, and the owner's post-M2-03 retune left the Oathbound at
        // 3. The archetype has to survive both, and the margin at 3 m/s is the one that is thin.
        foreach (float speed in new[] { SpecEscapeSpeed, ShippedMoveSpeed })
        {
            SetUp();

            EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

            float distance = Reach;

            Sense(playerAt: PlayerAt(distance));

            var bloater = (BloaterBehaviour)agent.Behaviour;

            // Two ticks to notice and plant. The walk starts from the moment it does, which is the
            // moment the swell appears — the player is reacting to the telegraph, not to the spawn.
            Tick(bloater);
            Tick(bloater);

            Assert.That(bloater.State, Is.EqualTo(BloaterState.Fuse), $"Sanity: planted, at {speed} m/s.");

            float before = _player.Health.Current;

            for (int i = 0; i < 200 && agent.IsAlive; i++)
            {
                distance += speed * Frame;

                Sense(playerAt: PlayerAt(distance));

                Tick(bloater);
            }

            Assert.That(agent.IsAlive, Is.False, $"Sanity: it went off, at {speed} m/s.");

            Assert.That(
                _player.Health.Current,
                Is.EqualTo(before).Within(1e-4f),
                $"A player who walks away at {speed} m/s from the moment it plants takes nothing.");

            Assert.That(
                distance,
                Is.GreaterThan(BlastRadius),
                $"And the margin is real rather than a rounding: {distance - BlastRadius} m clear.");
        }
    }

    [Test]
    public void Explode_IsDecidedOnXz()
    {
        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        // Two metres away on the ground and five metres up. Counted in three dimensions the
        // separation is 5.4 m and this misses; AR §18.4 says the height between two capsule centres
        // is a rendering detail, so it hits — and every other distance in the game already agrees.
        Sense(playerAt: new Vector3(0f, 5f, 2f));

        float before = _player.Health.Current;

        Kill(agent);

        Assert.That(_player.Health.Current, Is.EqualTo(before - ContactDamage).Within(1e-4f));
        Assert.That(Single<EnemyExploded>().HitPlayer, Is.True);
    }

    [Test]
    public void Explode_UsesTheScaledDamage()
    {
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity)
        {
            Depth = 20,
        };

        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        Sense(playerAt: new Vector3(0f, 0f, 1f));

        float before = _player.Health.Current;

        Kill(agent);

        // The agent's ContactDamage Stat, so GD §12.3's d(n) is already in it and M7-02's affixes
        // will reach a blast without a second mechanism (rule 5).
        float expected = agent.ContactDamage.Value;

        Assert.That(expected, Is.GreaterThan(ContactDamage), "Sanity: depth 20 hurts more than depth 1.");
        Assert.That(_player.Health.Current, Is.EqualTo(before - expected).Within(1e-4f));
    }

    [Test]
    public void Explode_ObeysTheOneShotRule()
    {
        // GD §12.4: no non-boss attack may take more than 35 % of the player's max HP at *any*
        // depth. d(n) caps at 3.0×, so this is the worst a Bloater can ever do — and a violation
        // does not show up in a playtest, it shows up forty stages into somebody's run.
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity)
        {
            Depth = 200,
        };

        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        Sense(playerAt: new Vector3(0f, 0f, 1f));

        float before = _player.Health.Current;

        Kill(agent);

        float dealt = before - _player.Health.Current;

        Assert.That(dealt, Is.LessThanOrEqualTo(OneShotCeiling),
            $"A capped Bloater deals {dealt}; GD §12.4's ceiling on 140 max HP is {OneShotCeiling}.");
    }

    // ---- Rule 2: the blast is a property of the corpse -----------------------------------------

    [Test]
    public void Explode_WhenKilledAtRange()
    {
        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        Sense(playerAt: new Vector3(0f, 0f, 10f));

        float before = _player.Health.Current;

        // The door a cone hit arrives through: PlayerCombat.ResolveConeHits calls exactly this, so
        // killing it here is killing it with the Censer as far as EnemySystem is concerned.
        Kill(agent);

        Assert.That(Single<EnemyExploded>().HitPlayer, Is.False);
        Assert.That(_player.Health.Current, Is.EqualTo(before).Within(1e-4f),
            "Ten metres away: it dies and the blast costs the player nothing. That is the trade.");
    }

    [Test]
    public void Explode_WhenKilledMidFuse()
    {
        BloaterBehaviour bloater = Fusing(distance: 1f);

        EnemyAgent agent = Agent(bloater);

        Assert.That(bloater.State, Is.EqualTo(BloaterState.Fuse), "Sanity: mid-swell.");

        float before = _player.Health.Current;

        _events.Clear();

        Kill(agent);

        // Killing it early does not defuse it — the blast is on the corpse, not on the fuse. This is
        // the other half of the trade: in melee it takes a third of the bar with it.
        Assert.That(_events.Count<EnemyExploded>(), Is.EqualTo(1));
        Assert.That(Single<EnemyExploded>().HitPlayer, Is.True);
        Assert.That(_player.Health.Current, Is.EqualTo(before - ContactDamage).Within(1e-4f));
    }

    [Test]
    public void Explode_DamagesNoOtherEnemy()
    {
        EnemyAgent first = Spawn(BloaterId, Vector3.Zero);
        EnemyAgent second = Spawn(BloaterId, new Vector3(1f, 0f, 0f));

        Sense(playerAt: new Vector3(0f, 0f, 20f));

        Kill(first);

        // Rule 4, ruled by the owner at M2-00c. A chain is the better *moment* and it is M7-02's:
        // re-entering ApplyDamage mid-call can kill another Bloater, which explodes, and every one
        // of those touches a registry EnemySystem.Tick may be walking. Player-only keeps the blast
        // a single leaf call.
        Assert.That(_events.Count<EnemyExploded>(), Is.EqualTo(1));
        Assert.That(second.Health.Current, Is.EqualTo(BloaterMaxHp).Within(1e-4f));
        Assert.That(second.IsAlive, Is.True);
    }

    [Test]
    public void Explode_OnlyForArchetypesWithTheBlock()
    {
        EnemyAgent husk = Spawn(HuskId, Vector3.Zero);

        Sense(playerAt: new Vector3(0f, 0f, 1f));

        float before = _player.Health.Current;

        Kill(husk);

        // Rule 1: the trigger is the spec's ExplosionSpec, never the kind. A Husk carries none, so
        // nothing goes off — and the day M7-02's Volatile affix gives one to a Husk, this row is
        // what says the mechanism was already there.
        Assert.That(_events.Count<EnemyExploded>(), Is.Zero);
        Assert.That(_player.Health.Current, Is.EqualTo(before).Within(1e-4f));
        Assert.That(_events.Count<EnemyDied>(), Is.EqualTo(1), "It still died, it just did not go off.");
    }

    [Test]
    public void Explode_PublishedAfterEnemyDied()
    {
        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        Sense(playerAt: new Vector3(0f, 0f, 1f));

        _events.Clear();

        Kill(agent);

        // The order everything downstream depends on: a handler of EnemyDied that resolves the id
        // through the registry must find it, and a blast that preceded its own death would be a
        // corpse hurting somebody before it was a corpse.
        AssertOrder(typeof(EnemyDamaged), typeof(EnemyDied), typeof(EnemyExploded));
    }

    // ---- Rule 11: the corpse, and the span that walks it ---------------------------------------

    [Test]
    public void Explode_DoesNotDespawnTheCorpse()
    {
        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        Sense(playerAt: new Vector3(0f, 0f, 20f));

        Kill(agent);

        // Registered but not breathing (AR §18.4). This is what gives M1-12's dissolve an id to
        // animate, and it is also why a behaviour killing itself cannot disturb the span
        // EnemySystem.Tick is walking — nothing is removed until a later tick's sweep.
        Assert.That(_system.Registry.TryGet(agent.Id, out EnemyAgent found), Is.True);
        Assert.That(found, Is.SameAs(agent));
        Assert.That(found.IsAlive, Is.False);

        _clock += EnemySystem.CorpseTime;

        _system.Tick(Context());

        Assert.That(_system.Registry.TryGet(agent.Id, out _), Is.False, "Swept once its dissolve had played.");
        Assert.That(_events.Count<EnemyDespawned>(), Is.EqualTo(1));
    }

    [Test]
    public void System_TickSpanSurvivesADetonation()
    {
        // The row the spec's remarks warned about and this task disarms: three Bloaters all
        // detonating inside one pass over Registry.Alive. It survives because ApplyDamage leaves
        // each corpse registered — nothing is removed, so no index shifts underneath the walk.
        var agents = new EnemyAgent[3];

        for (int i = 0; i < agents.Length; i++)
        {
            agents[i] = Spawn(BloaterId, new Vector3(i * 0.5f, 0f, 0f));
        }

        Sense(playerAt: PlayerAt(1f));

        // Two ticks to notice and plant, then the fuse, driven through the system so that the span
        // is the real one rather than three separate behaviour calls.
        for (int i = 0; i < 200 && agents[0].IsAlive; i++)
        {
            _intents.Clear();

            Assert.DoesNotThrow(() => _system.Tick(Context()), "The pass must survive its own detonations.");

            _clock += Frame;
        }

        Assert.That(_events.Count<EnemyExploded>(), Is.EqualTo(3), "All three went off.");

        foreach (EnemyAgent agent in agents)
        {
            Assert.That(agent.IsAlive, Is.False);
            Assert.That(_system.Registry.TryGet(agent.Id, out _), Is.True, "Still registered for its dissolve.");
        }
    }

    // ---- Rule 6: a blast during a fact phase ---------------------------------------------------

    [Test]
    public void Session_BlastDuringAFactPhaseEndsTheRunNextTick()
    {
        // Nothing before this task could damage the player from a fact phase. ReportConeHits runs
        // *after* RunSession.Tick, so a Bloater killed by a swing publishes PlayerDied immediately —
        // which the HUD's death overlay hangs off — while RunEnded waits for the next tick's death
        // check. Named in rule 6 rather than found later, and pinned here.
        RunSession session = Session();

        session.Start(FrailConfig());

        var playerPosition = Vector3.Zero;
        var bloaterPosition = new Vector3(0f, 0f, 1f);

        bool reported = false;

        for (int i = 0; i < 480 && session.IsRunning && !reported; i++)
        {
            _events.Clear();
            _intents.Clear();

            _snapshot.Clear();
            _snapshot.Dt = Frame;
            _snapshot.PlayerPosition = playerPosition;

            AddSense(_snapshot, id: 1, position: bloaterPosition);

            session.Tick(_snapshot);

            if (_intents.ConeHits.Count == 0)
            {
                continue;
            }

            // The fact phase: the body reports who its wedge touched, one step after the tick that
            // decided to swing. The Bloater dies here, and the blast lands here with it.
            _events.Clear();

            Span<int> hits = stackalloc int[1];
            hits[0] = 1;

            session.ReportConeHits(hits);

            reported = true;
        }

        Assert.That(reported, Is.True, "Sanity: the Censer swung and the fixture reported the hit.");

        Assert.That(TypesOf(_events.All), Is.EqualTo(new[]
        {
            typeof(EnemyDamaged),
            typeof(EnemyDied),
            typeof(PlayerDamaged),
            typeof(PlayerDied),
            typeof(EnemyExploded),
        }), "PlayerDied inside the report — the overlay has what it needs on the frame of the kill.");

        Assert.That(session.IsRunning, Is.True, "And the run has not ended yet: nothing has ticked since.");

        _events.Clear();

        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = playerPosition;

        session.Tick(_snapshot);

        // One frame of a corpse standing up, which nothing draws. The alternative — checking for
        // death inside the report — would put the run's lifecycle in two places.
        Assert.That(_events.Count<RunEnded>(), Is.EqualTo(1));
        Assert.That(session.IsRunning, Is.False);
    }

    // ---- Rules 10 and 13: the cadence and the cost ---------------------------------------------

    [Test]
    public void Tick_EmitsExactlyOneIntentPerState()
    {
        AssertOneIntentIn(BloaterState.Idle, Bloater(distance: AggroRange + 1f));
        AssertOneIntentIn(BloaterState.Waddle, Waddling(distance: 10f));
        AssertOneIntentIn(BloaterState.Fuse, Fusing(distance: 1.5f));
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);
        var projectiles = new ProjectileSystem(silent, ProjectileCapacity);
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity);

        // A silent hub rather than the fixture's recorder: RecordingEvents stores each payload in a
        // List<object>, so it boxes every struct and the row would measure the fake (Traps §7).
        EnemyAgent walker = system.Spawn(new ContentId(BloaterId), Vector3.Zero);
        EnemyAgent fuser = system.Spawn(new ContentId(BloaterId), new Vector3(40f, 0f, 0f));

        var walking = (BloaterBehaviour)walker.Behaviour;
        var fusing = (BloaterBehaviour)fuser.Behaviour;

        // The walker never arrives — ten metres away and the body never moves it, because nothing
        // here applies an intent — so it waddles for as long as it is ticked. The fuser is inside
        // the trigger, so it plants, goes off, and then keeps being ticked as a corpse, which is the
        // hottest form of the detonation path: ApplyDamage finds a corpse and answers None.
        SenseInto(system, walker, playerAt: new Vector3(0f, 0f, 10f));
        SenseInto(system, fuser, playerAt: new Vector3(40f, 0f, 1f));

        for (int i = 0; i < 600; i++)
        {
            intents.Clear();

            var warm = new EnemyTickContext(Frame, i * Frame, player, intents, silent, projectiles, system);

            walking.Tick(warm);
            fusing.Tick(warm);
        }

        // The context is constructed inside the measured body on purpose: a struct that had quietly
        // become a class would be 10 000 allocations, and building it outside would hide exactly
        // that.
        AllocationAssert.None(() =>
        {
            intents.Clear();

            _allocationClock += Frame;

            var ctx = new EnemyTickContext(
                Frame, _allocationClock, player, intents, silent, projectiles, system);

            walking.Tick(ctx);
            fusing.Tick(ctx);
        });
    }

    // ---- Rule 12: what a recycled agent gets ---------------------------------------------------

    [Test]
    public void Reset_ReturnsToIdle()
    {
        BloaterBehaviour bloater = Fusing(distance: 1.5f);

        Assert.That(bloater.State, Is.EqualTo(BloaterState.Fuse), "Sanity: mid-fuse.");

        bloater.Reset();

        Assert.That(bloater.State, Is.EqualTo(BloaterState.Idle));
        Assert.That(Agent(bloater).Blackboard.StateTimer, Is.Zero,
            "A body that was mid-fuse must not come back already committed.");
    }

    [Test]
    public void Agent_ReplacesAChaserBehaviourOnRecycle()
    {
        EnemyAgent agent = Spawn(HuskId, Vector3.Zero);

        Assert.That(agent.Behaviour, Is.InstanceOf<ChaserBehaviour>(), "Sanity: a Husk chases.");

        _system.Despawn(agent.Id);

        EnemyAgent recycled = Spawn(BloaterId, Vector3.Zero);

        Assert.That(recycled, Is.SameAs(agent), "Sanity: the same agent, a different archetype.");

        Assert.That(recycled.Behaviour, Is.InstanceOf<BloaterBehaviour>());
        Assert.That(((BloaterBehaviour)recycled.Behaviour).State, Is.EqualTo(BloaterState.Idle));
    }

    [Test]
    public void System_DispatchesBloaters()
    {
        EnemyAgent husk = Spawn(HuskId, Vector3.Zero);
        EnemyAgent bloater = Spawn(BloaterId, new Vector3(2f, 0f, 0f));

        Sense(playerAt: new Vector3(0f, 0f, 12f));

        _intents.Clear();

        _system.Tick(Context());

        // All three kinds share one line in the dispatch, which is the seam earning its keep: what
        // differs between a Husk and a Bloater is entirely on the other side of IEnemyBehaviour.
        Assert.That(_intents.CountEnemyMoves(husk.Id), Is.EqualTo(1));
        Assert.That(_intents.CountEnemyMoves(bloater.Id), Is.EqualTo(1));

        Assert.That(((ChaserBehaviour)husk.Behaviour).State, Is.EqualTo(ChaserState.Chase));
        Assert.That(((BloaterBehaviour)bloater.Behaviour).State, Is.EqualTo(BloaterState.Waddle));
    }

    // ---- Guards --------------------------------------------------------------------------------

    [Test]
    public void Ctor_NullAgent_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BloaterBehaviour(null));
    }

    [Test]
    public void Context_NullEnemies_Throws()
    {
        // The fifth reference on the context, guarded where the other four are: once a tick for the
        // whole arena rather than once per enemy, which is the other half of why it is a struct.
        Assert.Throws<ArgumentNullException>(
            () => new EnemyTickContext(Frame, 0f, _player, _intents, _events, _projectiles, null));
    }

    [Test]
    public void ApplyDamage_NullPlayer_Throws()
    {
        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        // Required rather than optional (rule 3): the day an archetype starts exploding, every call
        // site has to say who is standing nearby, and a null one is a mis-wired caller rather than
        // a blast with nobody to catch.
        Assert.Throws<ArgumentNullException>(() => _system.ApplyDamage(agent.Id, 10f, 0f, null));
    }

    // ---- Fixture helpers -----------------------------------------------------------------------

    /// <summary>A spawned Bloater with the player <paramref name="distance"/> away along +Z, idle.</summary>
    private BloaterBehaviour Bloater(float distance)
    {
        EnemyAgent agent = Spawn(BloaterId, Vector3.Zero);

        Assert.That(agent.Behaviour, Is.InstanceOf<BloaterBehaviour>(),
            "A Bloater archetype must have been given a behaviour.");

        Sense(playerAt: PlayerAt(distance));

        return (BloaterBehaviour)agent.Behaviour;
    }

    /// <summary>The same, ticked once so it has noticed the player and is closing.</summary>
    private BloaterBehaviour Waddling(float distance)
    {
        BloaterBehaviour bloater = Bloater(distance);

        Tick(bloater);

        Assert.That(bloater.State, Is.EqualTo(BloaterState.Waddle), "Sanity: the fixture starts in Waddle.");

        return bloater;
    }

    /// <summary>The same again, planted and swelling.</summary>
    private BloaterBehaviour Fusing(float distance)
    {
        BloaterBehaviour bloater = Waddling(distance);

        Tick(bloater);

        Assert.That(bloater.State, Is.EqualTo(BloaterState.Fuse), "Sanity: the fixture starts in Fuse.");

        return bloater;
    }

    /// <summary>
    /// A Bloater spawned at <paramref name="depth"/> and ticked into <see cref="BloaterState.Waddle"/>.
    /// </summary>
    /// <remarks>
    /// A system of its own rather than the fixture's, because <c>EnemySystem.Depth</c> is read at
    /// spawn and the fixture's own Bloaters must stay unscaled — every other row asserts M2-06's
    /// authored numbers, and they are only true at depth 1.
    /// </remarks>
    private BloaterBehaviour WaddlingAtDepth(int depth, float distance)
    {
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity)
        {
            Depth = depth,
        };

        return Waddling(distance);
    }

    private EnemyAgent Spawn(string specId, Vector3 position) =>
        _system.Spawn(new ContentId(specId), position);

    /// <summary><paramref name="distance"/> metres from the origin along +Z, on the ground.</summary>
    private static Vector3 PlayerAt(float distance) => new Vector3(0f, 0f, distance);

    /// <summary>
    /// Puts the player at <paramref name="playerAt"/> and re-senses the whole arena through
    /// <c>EnemySystem.Ingest</c>.
    /// </summary>
    /// <remarks>
    /// Through <c>Ingest</c> rather than by assigning the blackboard, which is the fixture note in
    /// the class remarks: a blast is resolved against the player position the <em>system</em> last
    /// ingested (rule 5), so hand-writing perception would leave every explosion measuring its
    /// distance to the origin while the row believed it had moved the player. Every registered agent
    /// is named at its own current position, so nothing moves as a side effect of sensing.
    /// </remarks>
    private void Sense(Vector3 playerAt, Vector2 pathDirection = default) =>
        SenseInto(_system, playerAt, pathDirection);

    private void SenseInto(EnemySystem system, Vector3 playerAt, Vector2 pathDirection = default)
    {
        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = playerAt;

        ReadOnlySpan<EnemyAgent> agents = system.Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            AddSense(_snapshot, agents[i].Id, agents[i].Position, pathDirection);
        }

        system.Ingest(_snapshot);
    }

    /// <summary>The same, naming one agent only — for the allocation row's two separate arenas.</summary>
    private void SenseInto(EnemySystem system, EnemyAgent agent, Vector3 playerAt)
    {
        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = playerAt;

        AddSense(_snapshot, agent.Id, agent.Position, Vector2.Zero);

        system.Ingest(_snapshot);
    }

    /// <summary>One tick at <see cref="Frame"/>, advancing the fixture's clock with it.</summary>
    /// <remarks>
    /// The clock is a field shared by every helper here rather than a counter restarted per call,
    /// because a landing stamps <c>Health</c>'s i-frames with it: a fixture that replayed the same
    /// seconds on each helper would have every hit after the first blocked by the one before it.
    /// </remarks>
    private void Tick(BloaterBehaviour bloater)
    {
        bloater.Tick(Context());

        _clock += Frame;
    }

    /// <summary>Ticks until <paramref name="bloater"/>'s agent is a corpse, or fails.</summary>
    /// <remarks>
    /// Frames are not counted, for <c>ChaserBehaviourTests</c>' reason: <c>StateTimer</c> is a
    /// running sum of 1/120 s steps, so whether a 0.8 s fuse completes on frame 96 or 97 is float
    /// accumulation rather than behaviour (Traps §7).
    /// </remarks>
    private void TickUntilDead(BloaterBehaviour bloater, float budgetSeconds)
    {
        EnemyAgent agent = Agent(bloater);

        int frames = (int)MathF.Ceiling(budgetSeconds / Frame);

        for (int i = 0; i < frames && agent.IsAlive; i++)
        {
            Tick(bloater);
        }

        Assert.That(agent.IsAlive, Is.False, $"Still alive after {frames} frames; expected a detonation.");
    }

    /// <summary>One tick in <paramref name="state"/> writes exactly one intent, and no more.</summary>
    private void AssertOneIntentIn(BloaterState state, BloaterBehaviour bloater)
    {
        Assert.That(bloater.State, Is.EqualTo(state), "Sanity: the fixture reached the state it is about.");

        int id = Agent(bloater).Id;

        _intents.Clear();

        Tick(bloater);

        Assert.That(_intents.CountEnemyMoves(id), Is.EqualTo(1), $"in {state}");
    }

    /// <summary>Kills <paramref name="agent"/> outright through the one door damage arrives by.</summary>
    private void Kill(EnemyAgent agent) => _system.ApplyDamage(agent.Id, 10_000f, _clock, _player);

    /// <summary>This frame's context, built from the fixture's own clock and ports.</summary>
    private EnemyTickContext Context() =>
        new EnemyTickContext(Frame, _clock, _player, _intents, _events, _projectiles, _system);

    /// <summary>The agent whose behaviour this is, found by walking the registry.</summary>
    private EnemyAgent Agent(BloaterBehaviour bloater)
    {
        ReadOnlySpan<EnemyAgent> agents = _system.Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            if (ReferenceEquals(agents[i].Behaviour, bloater))
            {
                return agents[i];
            }
        }

        Assert.Fail("No registered agent owns this behaviour.");

        return null;
    }

    /// <summary>The one <typeparamref name="T"/> published, asserting that there was exactly one.</summary>
    private T Single<T>() where T : struct
    {
        IReadOnlyList<T> published = _events.Of<T>();

        Assert.That(published.Count, Is.EqualTo(1), $"Expected exactly one {typeof(T).Name}.");

        return published[0];
    }

    /// <summary>Asserts that <paramref name="expected"/> appear in that order among the events.</summary>
    /// <remarks>
    /// Order rather than exact contents, so a row about an ordering does not fail the day something
    /// unrelated starts publishing alongside it. The rows that care about the exact set say so with
    /// an equality instead.
    /// </remarks>
    private void AssertOrder(params Type[] expected)
    {
        Type[] actual = TypesOf(_events.All);

        int at = -1;

        foreach (Type type in expected)
        {
            int found = Array.IndexOf(actual, type, at + 1);

            Assert.That(found, Is.GreaterThan(at), $"{type.Name} did not follow what came before it.");

            at = found;
        }
    }

    private static Type[] TypesOf(IReadOnlyList<object> events)
    {
        var types = new Type[events.Count];

        for (int i = 0; i < events.Count; i++)
        {
            types[i] = events[i].GetType();
        }

        return types;
    }

    private static void AddSense(
        WorldSnapshot snapshot,
        int id,
        Vector3 position,
        Vector2 pathDirection = default)
    {
        ref EnemySense sense = ref snapshot.AddEnemy();

        sense.Id = id;
        sense.Position = position;
        sense.Velocity = Vector3.Zero;
        sense.PathDirectionToPlayer = pathDirection;
        sense.HasLineOfSight = true;
    }

    private RunSession Session() => new RunSession(
        new ContentCatalog(new[] { Frail() }, new[] { Husk(), FrailBloater() }, new[] { Descent() }),
        new FixedRandom(Seed),
        _events,
        _intents,
        Capacity,
        Capacity,
        ProjectileCapacity);

    /// <summary>One Bloater a metre away, and nothing else in the arena.</summary>
    private static RunConfig FrailConfig() => new RunConfig(
        new ContentId(DescentId),
        new ContentId(OathboundId),
        Seed,
        1,
        new SpawnPlan(new[]
        {
            new SpawnPlan.Entry(new ContentId(BloaterId), new Vector3(0f, 0f, 1f)),
        }));

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk(), BloaterSpec() });

    /// <summary>GD §12's curves, required by every <c>EnemySystem</c> as of M2-03.</summary>
    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    /// <summary>GD §8.1's Bloater, with M2-06's numbers and its explosion block.</summary>
    private static EnemySpec BloaterSpec(float maxHp = BloaterMaxHp) => new EnemySpec(
        new ContentId(BloaterId),
        new LocKey("enemy.bloater.name"),
        maxHp,
        moveSpeed: MoveSpeed,
        targetPriority: 2,
        threatCost: 8,
        isElite: false,
        contactDamage: ContactDamage,
        reach: Reach,
        windupTime: FuseTime,
        recoverTime: 0f,
        aggroRange: AggroRange,
        behaviour: EnemyBehaviourKind.Bloater,
        explosion: new ExplosionSpec(BlastRadius));

    /// <summary>
    /// The same archetype with thirteen hit points, so a single Censer swing kills it.
    /// </summary>
    /// <remarks>
    /// Only the session row uses it, and only so that the row is about the <em>ordering</em> of a
    /// fact-phase blast rather than about how many swings a 24 HP body takes before the 0.8 s fuse
    /// beats the Censer to it.
    /// </remarks>
    private static EnemySpec FrailBloater() => BloaterSpec(maxHp: 13f);

    /// <summary>GD §8.1's Husk — the other side of the dispatch row, and the archetype with no blast.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 36f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: 4,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: AggroRange,
        behaviour: EnemyBehaviourKind.Chaser);

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
    /// The Oathbound of CC §7, with <b>no Aegis</b>: every damage row here reads what a blast cost,
    /// and a 30-point shield would absorb all of it.
    /// </summary>
    /// <remarks>
    /// Focus is switched off with a <c>MaxMultiplier</c> of 1, the isolation <c>PlayerCombatTests</c>
    /// uses: no row here is about the player's swing rate.
    /// </remarks>
    private static CharacterSpec Oathbound(float maxHp = MaxHp) => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        maxHp,
        new MovementSpec(ShippedMoveSpeed, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 16f, 4f, 0.05f),
        null,
        HitIFrames);

    /// <summary>The same character with ten hit points, so one unscaled blast ends the run.</summary>
    private static CharacterSpec Frail() => Oathbound(maxHp: 10f);
}
