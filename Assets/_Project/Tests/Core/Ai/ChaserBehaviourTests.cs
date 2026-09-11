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
/// The M1-18 spec's eight rules: the aggro edge, the walk, the telegraph, the damage frame, the
/// recovery, what a corpse does not do, and the allocation budget.
/// </summary>
/// <remarks>
/// <para>
/// GD §8.1's Husk throughout — 36 HP, 3.5 m/s, 8 contact damage, 1.2 m reach, a 0.4 s windup and a
/// 0.6 s recovery — so a failure reads as "the enemy we ship stopped fighting" rather than as an
/// arithmetic puzzle. The rows that vary a number say which and why.
/// </para>
/// <para>
/// <b>Perception is hand-written, which is the whole reason a behaviour is testable at all.</b>
/// <c>EnemyBlackboard</c>'s perception half is filled by <c>EnemySystem.Ingest</c> in a real run;
/// here it is assigned directly, so a row can put the player at 1.19 m without building a snapshot,
/// a registry and a world to get him there. That is AR §9's split working as designed — a behaviour
/// reads facts and never asks where they came from.
/// </para>
/// <para>
/// Enemies are reached only through <see cref="EnemyRegistry"/>, because that is the only route
/// there is: <c>EnemyAgent</c>'s constructor and its <c>Position</c> setter are <c>internal</c> and
/// <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c> (M0-10). The behaviour under test is
/// the one the registry built on the agent, never a fresh one, so these rows exercise the same
/// object a run would.
/// </para>
/// <para>
/// The player is a real <see cref="PlayerCombat"/> rather than a fake, exactly as the spec asks.
/// The thing being checked on the other side of a strike is <c>PlayerDamaged</c> — its Aegis split,
/// its i-frame blocking — and a fake would be a second implementation of the rules those events
/// come out of.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ChaserBehaviourTests
{
    private const string HuskId = "enemy.husk";
    private const string OathboundId = "character.oathbound";

    private const int Capacity = 64;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    // GD §8.1 and M1-05, the five numbers the Husk's fight is made of.
    private const float MoveSpeed = 3.5f;
    private const float ContactDamage = 8f;
    private const float Reach = 1.2f;
    private const float WindupTime = 0.4f;
    private const float RecoverTime = 0.6f;

    // CC §7, Survivability — what the strike lands on.
    private const float MaxHp = 140f;
    private const float ShieldMax = 30f;
    private const float HitIFrames = 0.5f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _system;
    private PlayerCombat _player;

    /// <summary>
    /// The clock <see cref="Tick_AllocatesNothing"/> advances. A field rather than a local, so the
    /// measured closure is built over it once instead of capturing a fresh variable — a closure
    /// created inside the measurement would be the allocation it reported.
    /// </summary>
    private float _allocationClock;

    /// <summary>Simulated run time, advanced by every tick the helpers below take.</summary>
    private float _clock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _allocationClock = 0f;
        _clock = 0f;
    }

    // ---- Rule 1: noticing ----------------------------------------------------------------------

    [Test]
    public void Idle_ToChase_WithinThirty()
    {
        ChaserBehaviour near = Chaser(distance: 29f);

        Assert.That(near.State, Is.EqualTo(ChaserState.Idle), "Sanity: a spawned Husk has not noticed anyone.");

        Tick(near);

        Assert.That(near.State, Is.EqualTo(ChaserState.Chase));

        ChaserBehaviour far = Chaser(distance: 31f);

        Tick(far);

        Assert.That(far.State, Is.EqualTo(ChaserState.Idle));
    }

    // ---- Rule 2: the walk ----------------------------------------------------------------------

    [Test]
    public void Chase_EmitsMoveTowardPlayer_AtSpeed()
    {
        ChaserBehaviour chaser = Chase(distance: 10f, direction: new Vector2(0.6f, 0.8f));

        _intents.Clear();

        Tick(chaser);

        EnemyMoveIntent move = _intents.LastEnemyMove;

        // 3.5 m/s along (0.6, 0.8): the direction is a unit vector, so the components are the speed
        // scaled by it and nothing else.
        Assert.That(move.Velocity.X, Is.EqualTo(2.1f).Within(1e-4f));
        Assert.That(move.Velocity.Y, Is.EqualTo(0f), "Movement is on the ground plane; Y is the body's.");
        Assert.That(move.Velocity.Z, Is.EqualTo(2.8f).Within(1e-4f));

        // It looks where it walks. Chase is the one state where facing and velocity agree.
        Assert.That(move.FacingXZ.X, Is.EqualTo(0.6f).Within(1e-4f));
        Assert.That(move.FacingXZ.Y, Is.EqualTo(0.8f).Within(1e-4f));
    }

    [Test]
    public void Chase_PrefersPathDirection()
    {
        ChaserBehaviour chaser = Chase(distance: 10f, direction: new Vector2(0f, 1f));

        // A NavMesh that says "go round the pillar" (M1-19) beats the straight line that would walk
        // into it. Until that lands the path is always zero, which is why the fallback is the one
        // exercised by every other row here.
        Blackboard(chaser).PathDirectionToPlayer = new Vector2(1f, 0f);

        _intents.Clear();

        Tick(chaser);

        EnemyMoveIntent move = _intents.LastEnemyMove;

        Assert.That(move.Velocity.X, Is.EqualTo(MoveSpeed).Within(1e-4f));
        Assert.That(move.Velocity.Z, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void Chase_ToWindup_AtReach()
    {
        ChaserBehaviour chaser = Chase(distance: 10f, direction: new Vector2(0f, 1f));

        Blackboard(chaser).DistanceToPlayer = Reach;

        _intents.Clear();
        _events.Clear();

        Tick(chaser);

        Assert.That(chaser.State, Is.EqualTo(ChaserState.Windup));

        // The tell, and the number on it is the archetype's rather than a constant in a view.
        Assert.That(_events.Count<EnemyTelegraph>(), Is.EqualTo(1));
        Assert.That(LastEvent<EnemyTelegraph>().Duration, Is.EqualTo(WindupTime).Within(1e-6f));

        // The tick that arrives spends its intent walking — see TickChase on why the reach test is
        // after the move — so the standing-still intent is the *next* one.
        _intents.Clear();

        Tick(chaser);

        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    // ---- Rule 3: the telegraph -----------------------------------------------------------------

    [Test]
    public void Windup_ToStrike_AfterWindupTime()
    {
        ChaserBehaviour chaser = Windup(distance: 1f);

        // Two frames short of the windup: still winding up, and nothing has been hit.
        TickFor(chaser, WindupTime - (2f * Frame));

        Assert.That(chaser.State, Is.EqualTo(ChaserState.Windup));
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);

        TickUntil(chaser, ChaserState.Strike, budgetSeconds: 3f * Frame);

        // The damage lands on the tick the windup completed — the frame the player was watching —
        // which is what makes Strike the state it happened in rather than an edge between two
        // others.
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));

        // One tick, then the punish window.
        Tick(chaser);

        Assert.That(chaser.State, Is.EqualTo(ChaserState.Recover));
    }

    [Test]
    public void Windup_CancelsWhenPlayerLeaves()
    {
        ChaserBehaviour chaser = Windup(distance: 1f);

        // 1.9 m is past 1.2 × 1.5. Stepping to 1.7 would *not* cancel — it would be swung at and
        // missed — which is the distinction WindupCancelReachMultiplier exists to draw.
        Blackboard(chaser).DistanceToPlayer = 1.9f;

        TickFor(chaser, WindupTime * 2f);

        Assert.That(chaser.State, Is.EqualTo(ChaserState.Chase));
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero, "A dodged windup deals nothing at all.");
    }

    // ---- Rule 4: the damage frame --------------------------------------------------------------

    [Test]
    public void Strike_DamagesPlayerInReach()
    {
        ChaserBehaviour chaser = Windup(distance: 1f);

        TickUntil(chaser, ChaserState.Strike, budgetSeconds: WindupTime + (2f * Frame));

        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));

        PlayerDamaged hit = LastEvent<PlayerDamaged>();

        // The Aegis takes all 8 — CC §7 gives it 30 points, so the first strike never reaches HP.
        Assert.That(hit.ToShield, Is.EqualTo(ContactDamage).Within(1e-4f));
        Assert.That(hit.ToHp, Is.Zero);
        Assert.That(hit.Blocked, Is.False);
        Assert.That(hit.HpFraction, Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public void Strike_MissesOutOfReach()
    {
        ChaserBehaviour chaser = Windup(distance: 1f);

        // Inside the cancel band (1.8) but outside the reach (1.2): the swing happens and misses,
        // which is a different thing on screen from a windup that was abandoned.
        Blackboard(chaser).DistanceToPlayer = 1.3f;

        // The swing happens — reaching Strike at all is the half of this that separates a miss from
        // a cancel — and lands on nobody.
        TickUntil(chaser, ChaserState.Strike, budgetSeconds: WindupTime + (2f * Frame));

        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    [Test]
    public void Strike_BlockedByIFrames()
    {
        ChaserBehaviour chaser = Windup(distance: 1f);

        // Hurt at t = 0 by something else, which starts CC §7's 0.5 s of i-frames. The windup is
        // 0.4 s, so the Husk's strike lands inside them.
        _player.ApplyDamage(1f, 0f);

        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1), "Sanity: the setup hit landed.");

        TickUntil(chaser, ChaserState.Strike, budgetSeconds: WindupTime + (2f * Frame));

        Assert.That(_clock, Is.LessThan(HitIFrames), "Sanity: the strike lands inside the i-frames.");
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(2), "A blocked hit is still published.");

        PlayerDamaged blocked = LastEvent<PlayerDamaged>();

        Assert.That(blocked.Blocked, Is.True);
        Assert.That(blocked.ToShield, Is.Zero);
        Assert.That(blocked.ToHp, Is.Zero);
    }

    // ---- Rule 5: the punish window -------------------------------------------------------------

    [Test]
    public void Recover_ThenChase()
    {
        ChaserBehaviour chaser = Windup(distance: 1f);

        // Through the windup and the one-tick strike that follows it.
        TickUntil(chaser, ChaserState.Strike, budgetSeconds: WindupTime + (2f * Frame));
        TickUntil(chaser, ChaserState.Recover, budgetSeconds: 2f * Frame);

        // Rooted for all but the last frames of the recovery. This is the window the player is
        // meant to punish, so a Husk that started walking again early would be taking away the
        // reward for dodging.
        TickFor(chaser, RecoverTime - (3f * Frame));

        Assert.That(chaser.State, Is.EqualTo(ChaserState.Recover));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));

        TickUntil(chaser, ChaserState.Chase, budgetSeconds: 5f * Frame);

        // Back to Chase rather than Idle, whatever the distance: an enemy that has hit you knows
        // where you are. Its own reach test sends it straight back into a windup if you stayed.
        Assert.That(chaser.State, Is.EqualTo(ChaserState.Chase));
    }

    // ---- Rules 6 and 8: corpses and the allocation budget --------------------------------------

    [Test]
    public void Dead_NotTicked()
    {
        ChaserBehaviour chaser = Chase(distance: 5f, direction: new Vector2(0f, 1f));

        EnemyAgent agent = Agent(chaser);

        _system.ApplyDamage(agent.Id, 1000f, _clock);

        Assert.That(agent.IsAlive, Is.False, "Sanity: it is a corpse.");

        _intents.Clear();

        // Through the system rather than the behaviour, because the rule belongs to the dispatch:
        // EnemySystem is what refuses to tick a corpse, and that is also what makes a death
        // mid-windup cancel the strike without the behaviour knowing anything about dying.
        _system.Tick(Frame, _clock, _player, _intents);

        Assert.That(_intents.CountEnemyMoves(agent.Id), Is.Zero);
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    [Test]
    public void KilledMidWindup_NeverStrikes()
    {
        // Beyond the spec's Tests table, and the row that proves rule 6 is worth having: the
        // corpse check is what stands between a dying Husk and a strike the player has already
        // earned their way out of.
        ChaserBehaviour chaser = Windup(distance: 1f);
        EnemyAgent agent = Agent(chaser);

        TickFor(chaser, WindupTime - (2f * Frame));

        Assert.That(chaser.State, Is.EqualTo(ChaserState.Windup), "Sanity: a frame or two from the hit.");

        _system.ApplyDamage(agent.Id, 1000f, _clock);

        // Driven through the system from here, because that is where the rule lives: the behaviour
        // itself knows nothing about being dead, and would happily finish its windup if anything
        // kept ticking it. Ten frames is well past the one the strike was due on and well short of
        // EnemySystem.CorpseTime, so the agent is still registered when the assertion runs.
        for (int i = 0; i < 10; i++)
        {
            _system.Tick(Frame, _clock, _player, _intents);

            _clock += Frame;
        }

        Assert.That(chaser.State, Is.EqualTo(ChaserState.Windup), "A corpse is frozen where it fell.");
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);
    }

    // ---- M2-03: the fight reads the agent's stats, not the archetype's floats -------------------

    [Test]
    public void Chaser_WalksAtScaledSpeed()
    {
        // s(40) = 1 + 0.02·floor(40/5) = 1.16, on GD §8.1's 3.5 m/s. The read that makes this true
        // is one line in TickChase — _agent.MoveSpeed.Value rather than _agent.Spec.MoveSpeed —
        // and without it a stage-40 Husk would walk at exactly the speed a stage-1 one does.
        ChaserBehaviour chaser = ChaseAtDepth(40, direction: new Vector2(1f, 0f));

        _intents.Clear();

        Tick(chaser);

        Assert.That(
            _intents.LastEnemyMove.Velocity.X,
            Is.EqualTo(MoveSpeed * 1.16f).Within(1e-3f),
            "GD §12.3's s(40) applied to the Husk's authored 3.5 m/s.");
    }

    [Test]
    public void Chaser_StrikesForScaledDamage()
    {
        // d(40) = 1 + 0.035·39 = 2.365, on GD §8.1's 8. The Oathbound's Aegis has 30 points, so
        // 18.92 lands entirely on the shield — which is what makes the number readable in one
        // field rather than split across two.
        ChaserBehaviour chaser = WindupAtDepth(40);

        TickUntil(chaser, ChaserState.Strike, budgetSeconds: WindupTime + (2f * Frame));

        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));

        PlayerDamaged hit = LastEvent<PlayerDamaged>();

        Assert.That(hit.ToShield, Is.EqualTo(ContactDamage * 2.365f).Within(1e-3f));
        Assert.That(hit.ToHp, Is.Zero, "The Aegis has 30 points; 18.92 does not get through it.");
    }

    [Test]
    public void Tick_StillAllocatesNothing()
    {
        // Neither the recording events fake nor the callback one can be used here: both box every
        // payload, so a telegraph or a strike published inside the measured body would be counted as
        // core allocating when it is the fake doing it. This one throws every event away, which is
        // exactly what "core allocates nothing while publishing" needs on the other end.
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);

        // Depth 40, so every enemy in the crowd carries GD §12.3's three PercentMult modifiers and
        // the tick below is reading a *modified* Stat rather than a bare base. That is what rule 13
        // is about: ChaserBehaviour now reads MoveSpeed.Value and ContactDamage.Value per frame,
        // and Stat's cache keeps its laziness only while nothing is subscribed — a recompute that
        // allocated on read would put a GC spike behind every enemy in the arena (M1-01).
        var system = new EnemySystem(Catalog(), silent, new FixedRandom(), Scaling(), Capacity)
        {
            Depth = 40,
        };

        var snapshot = new WorldSnapshot(Capacity);
        snapshot.Dt = Frame;
        snapshot.PlayerPosition = Vector3.Zero;

        // A crowd spread across the states: half already in reach and cycling through windup, strike
        // and recovery, half walking in from 5 m. Measuring one state would miss whichever
        // transition allocates, and the transitions are the part of a state machine that touches a
        // dictionary at all.
        for (int i = 0; i < 32; i++)
        {
            float x = i < 16 ? 1f : 5f;

            EnemyAgent agent = system.Spawn(new ContentId(HuskId), new Vector3(x, 0f, 0f));

            AddSense(snapshot, agent.Id, agent.Position);
        }

        // Ingested outside the measured body, as M1-06's row does it and for the same reason: this
        // is about the behaviour pass, and perception that stops being refreshed simply holds the
        // distances still, which is what keeps the cycle running.
        system.Ingest(snapshot);

        // Warm-up: takes every machine through its first transitions and grows the sink's list to
        // the 32 entries a tick writes, so neither one-time cost lands inside the measurement.
        for (int i = 0; i < 600; i++)
        {
            intents.Clear();
            system.Tick(Frame, i * Frame, player, intents);
        }

        // Cleared per iteration, not once: 32 intents a tick across 10 000 ticks would otherwise
        // resize the list repeatedly, and that is the fake growing rather than core allocating.
        // Clear keeps the capacity, so it costs nothing after the warm-up above.
        AllocationAssert.None(() =>
        {
            intents.Clear();

            _allocationClock += Frame;

            system.Tick(Frame, _allocationClock, player, intents);
        });
    }

    // ---- Fixture helpers -----------------------------------------------------------------------

    /// <summary>
    /// A spawned Husk with the player <paramref name="distance"/> away along +Z, still in
    /// <see cref="ChaserState.Idle"/>.
    /// </summary>
    private ChaserBehaviour Chaser(float distance)
    {
        EnemyAgent agent = _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        Assert.That(agent.Behaviour, Is.Not.Null, "A Chaser archetype must have been given a behaviour.");

        EnemyBlackboard blackboard = agent.Blackboard;

        blackboard.DistanceToPlayer = distance;
        blackboard.DirectionToPlayer = new Vector2(0f, 1f);

        return agent.Behaviour;
    }

    /// <summary>The same, ticked once so it has noticed the player and is walking.</summary>
    private ChaserBehaviour Chase(float distance, Vector2 direction)
    {
        ChaserBehaviour chaser = Chaser(distance);

        Tick(chaser);

        Assert.That(chaser.State, Is.EqualTo(ChaserState.Chase), "Sanity: the fixture starts in Chase.");

        Blackboard(chaser).DirectionToPlayer = direction;

        return chaser;
    }

    /// <summary>The same again, walked into reach so it is mid-telegraph.</summary>
    private ChaserBehaviour Windup(float distance)
    {
        ChaserBehaviour chaser = Chase(distance: 5f, direction: new Vector2(0f, 1f));

        Blackboard(chaser).DistanceToPlayer = distance;

        Tick(chaser);

        Assert.That(chaser.State, Is.EqualTo(ChaserState.Windup), "Sanity: the fixture starts in Windup.");

        return chaser;
    }

    /// <summary>
    /// A Husk spawned at <paramref name="depth"/> and ticked into <see cref="ChaserState.Chase"/>,
    /// looking <paramref name="direction"/>.
    /// </summary>
    /// <remarks>
    /// A system of its own rather than the fixture's, because <c>EnemySystem.Depth</c> is read at
    /// spawn and the fixture's own Husks must stay unscaled — every other row here asserts GD
    /// §8.1's authored numbers, and they are only true at depth 1.
    /// </remarks>
    private ChaserBehaviour ChaseAtDepth(int depth, Vector2 direction)
    {
        EnemyAgent agent = SpawnAtDepth(depth);

        agent.Blackboard.DistanceToPlayer = 10f;
        agent.Blackboard.DirectionToPlayer = direction;

        Tick(agent.Behaviour);

        Assert.That(agent.Behaviour.State, Is.EqualTo(ChaserState.Chase), "Sanity: it is walking.");

        agent.Blackboard.DirectionToPlayer = direction;

        return agent.Behaviour;
    }

    /// <summary>The same, walked into reach so it is mid-telegraph.</summary>
    private ChaserBehaviour WindupAtDepth(int depth)
    {
        ChaserBehaviour chaser = ChaseAtDepth(depth, direction: new Vector2(0f, 1f));

        Blackboard(chaser).DistanceToPlayer = 1f;

        Tick(chaser);

        Assert.That(chaser.State, Is.EqualTo(ChaserState.Windup), "Sanity: it is winding up.");

        return chaser;
    }

    /// <summary>
    /// Spawns a Husk into a system set to <paramref name="depth"/>, and points the fixture at it.
    /// </summary>
    private EnemyAgent SpawnAtDepth(int depth)
    {
        _system = new EnemySystem(Catalog(), _events, new FixedRandom(), Scaling(), Capacity)
        {
            Depth = depth,
        };

        EnemyAgent agent = _system.Spawn(new ContentId(HuskId), Vector3.Zero);

        Assert.That(agent.Behaviour, Is.Not.Null, "A Chaser archetype must have been given a behaviour.");

        return agent;
    }

    /// <summary>One tick at <see cref="Frame"/>, advancing the fixture's clock with it.</summary>
    /// <remarks>
    /// The clock is a field shared by every helper here rather than a counter restarted per call,
    /// because a strike stamps <c>Health</c>'s i-frames with it: a fixture that replayed the same
    /// seconds on each helper would have every hit after the first blocked by the one before it, and
    /// the row that is actually about i-frames would stop proving anything.
    /// </remarks>
    private void Tick(ChaserBehaviour chaser)
    {
        chaser.Tick(Frame, _clock, _player, _intents, _events);

        _clock += Frame;
    }

    /// <summary>Ticks for at least <paramref name="seconds"/>, a frame at a time.</summary>
    private void TickFor(ChaserBehaviour chaser, float seconds)
    {
        int frames = (int)MathF.Ceiling(seconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            Tick(chaser);
        }
    }

    /// <summary>
    /// Ticks until <paramref name="chaser"/> reaches <paramref name="state"/>, and stops the moment
    /// it does. Fails if it has not within <paramref name="budgetSeconds"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the rows do not simply count frames.</b> <c>StateTimer</c> is a running sum of
    /// 1/120 s steps, and the forty-eighth of those lands within an ulp of 0.4 — so whether the
    /// windup completes on frame 48 or 49 is float accumulation, not behaviour. Pinning it would
    /// make these rows a test of arithmetic that would start failing the day <c>Frame</c> changed.
    /// </para>
    /// <para>
    /// Stopping on arrival is what makes it usable around <see cref="ChaserState.Strike"/>, which
    /// lasts exactly one tick: a helper that ran its budget out would tick straight through it into
    /// <see cref="ChaserState.Recover"/> and the row would never see the state it was about.
    /// </para>
    /// </remarks>
    private void TickUntil(ChaserBehaviour chaser, ChaserState state, float budgetSeconds)
    {
        int frames = (int)MathF.Ceiling(budgetSeconds / Frame);

        for (int i = 0; i < frames; i++)
        {
            if (chaser.State == state)
            {
                return;
            }

            Tick(chaser);
        }

        Assert.That(
            chaser.State,
            Is.EqualTo(state),
            $"Still {chaser.State} after {frames} frames; expected to have reached {state}.");
    }

    /// <summary>The agent whose behaviour this is, found by walking the registry.</summary>
    /// <remarks>
    /// A search rather than a field, because <c>EnemyAgent</c> exposes its behaviour and not the
    /// reverse — a back-reference would exist only for this fixture, which is the wrong reason to
    /// widen a core type.
    /// </remarks>
    private EnemyAgent Agent(ChaserBehaviour chaser)
    {
        ReadOnlySpan<EnemyAgent> agents = _system.Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            if (ReferenceEquals(agents[i].Behaviour, chaser))
            {
                return agents[i];
            }
        }

        Assert.Fail("No registered agent owns this behaviour.");

        return null;
    }

    private EnemyBlackboard Blackboard(ChaserBehaviour chaser) => Agent(chaser).Blackboard;

    /// <summary>The most recently published <typeparamref name="T"/>.</summary>
    /// <remarks>
    /// Here rather than on <c>RecordingEvents</c>: that fake already answers "how many" and "the
    /// only one", and "the latest" is a question only this fixture asks — several of these rows
    /// publish a setup event of the same type before the one under test.
    /// </remarks>
    private T LastEvent<T>() where T : struct
    {
        IReadOnlyList<T> published = _events.Of<T>();

        Assert.That(published.Count, Is.GreaterThan(0), $"No {typeof(T).Name} was published.");

        return published[published.Count - 1];
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

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk() });

    /// <summary>
    /// GD §12's curves, required by every <c>EnemySystem</c> as of M2-03.
    /// </summary>
    /// <remarks>
    /// The fixture's own system is left at <c>Depth</c> 1, where all three of GD §12.3's
    /// multipliers are exactly 1 — so every row that asserts GD §8.1's authored numbers still
    /// asserts them. The two rows that are about depth build their own system through
    /// <see cref="SpawnAtDepth"/>.
    /// </remarks>
    private static DepthScaling Scaling() => new DepthScaling(Scalings.Design());

    /// <summary>GD §8.1's Husk, with M1-05's five numbers and a chaser driving it.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 36f,
        moveSpeed: MoveSpeed,
        targetPriority: 1,
        threatCost: 4,
        isElite: false,
        contactDamage: ContactDamage,
        reach: Reach,
        windupTime: WindupTime,
        recoverTime: RecoverTime,
        behaviour: EnemyBehaviourKind.Chaser);

    /// <summary>The Oathbound of CC §7 — what the strikes land on.</summary>
    /// <remarks>
    /// Focus is switched off with a <c>MaxMultiplier</c> of 1, the same isolation
    /// <c>PlayerCombatTests</c> uses: no row here is about the player's swing rate, and a live ramp
    /// would be background noise in a fixture about enemies.
    /// </remarks>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(ShieldMax, 4f, 15f),
        HitIFrames);

    /// <summary>
    /// An <see cref="IDomainEvents"/> that throws every payload away without touching it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists for <see cref="Tick_AllocatesNothing"/> alone. <c>RecordingEvents</c> stores each
    /// payload in a <c>List&lt;object&gt;</c>, which boxes the struct — so measuring core's tick
    /// through it would report the fake's allocation as core's, and the row would fail for a reason
    /// that has nothing to do with the code under test. The real <c>DomainEventHub</c> does not box:
    /// it fans out through a typed channel per event type, precisely so a publish on a hot path is
    /// free (ADR-0004).
    /// </para>
    /// <para>
    /// Nested and private rather than in <c>Fakes/</c>, on the same terms as <c>EnemySystemTests</c>'
    /// <c>CallbackEvents</c>: one fixture needs it. If a second one does, it moves.
    /// </para>
    /// </remarks>
    private sealed class SilentEvents : IDomainEvents
    {
        public void Publish<T>(in T evt) where T : struct
        {
        }
    }
}
