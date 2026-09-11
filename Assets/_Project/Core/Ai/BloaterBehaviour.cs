using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Common;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>
/// Which part of its approach a bloater is in. GD §8.1's Bloater loop, and the shortest of the
/// three: it has no recovery, because nothing recovers from going off.
/// </summary>
/// <remarks>
/// Three states rather than the Chaser's and the Spitter's five, and the missing two are the point.
/// There is no one-tick damage state — the blast is resolved by <c>EnemySystem</c> off the corpse's
/// spec, not by this class, so there is no moment here for a <c>Strike</c> or a <c>Release</c> to
/// name — and no recovery after it, because the agent is dead by then.
/// </remarks>
public enum BloaterState
{
    /// <summary>Spawned and unaware. Nothing moves; the player is too far away to have been noticed.</summary>
    Idle,

    /// <summary>Walking at the player at <c>EnemyAgent.MoveSpeed</c>, depth scaling included.</summary>
    Waddle,

    /// <summary>
    /// Planted, swelling, committed. It does not move, does not cancel, and ends only by going off
    /// (rule 8).
    /// </summary>
    Fuse,
}

/// <summary>
/// The third kind of mind in the arena, and the first that kills itself: something that waddles at
/// the player, commits to going off, and hurts them whether they kill it or ignore it. See AR §3,
/// §9, §18.4 and GD §8.1, §9.1 rule 1.
/// </summary>
/// <remarks>
/// <para>
/// <b>The blast is not in this class, and that is the whole design.</b> "Explodes on death or
/// contact" is implemented as <em>contact kills it, and death explodes it</em>: the fuse ends by
/// calling <c>EnemySystem.ApplyDamage</c> on its own id for its own remaining hit points, and
/// <c>EnemySystem</c> resolves the explosion because the archetype's spec carries an
/// <see cref="ExplosionSpec"/> (M2-08 rules 1 and 8). So there is exactly one place a blast can come
/// from and no flag to keep in step — a Bloater shot at range, cut down mid-fuse or killed by a
/// Charge goes off by the identical path, and this class is not consulted for any of them. It has no
/// death hook, deliberately.
/// </para>
/// <para>
/// <b>The archetype is in the numbers it carries, and none of them is repeated here.</b> Aggro, the
/// contact trigger and the fuse length come off the spec; the blast radius comes off
/// <see cref="EnemySpec.Explosion"/>; the walk speed and the damage come off the agent's own
/// <c>Stat</c>s, which is what makes GD §12.3's depth curve and M7-02's affixes reach a blast
/// without a second mechanism. Unlike its two predecessors this class declares <em>no</em> constant
/// at all: a fuse that never cancels needs no cancel band, and a contact trigger that never retreats
/// needs no retreat fraction.
/// </para>
/// <para>
/// <b>The fuse is escapable, and the arithmetic says by how much.</b> It starts at the spec's 2 m
/// reach, the blast reaches 3 m, and 0.8 s at the Oathbound's 5.4 m/s covers 4.3 m — so a player who
/// reacts to the swell walks out with a metre to spare, and one who does not eats 15, a third of the
/// bar. That is the archetype: it punishes <em>not noticing</em>, and it punishes clustering,
/// because the metre of room you dodge into is often inside the next one's circle. All four numbers
/// are authored, and <c>BloaterBehaviourTests.Fuse_IsEscapable</c> pins the relationship between
/// them so that retuning any one of them fails there rather than on a phone.
/// </para>
/// <para>
/// <b>It reads perception and writes working memory, and never the other way round.</b> Everything
/// it knows arrives on <see cref="EnemyBlackboard"/>'s perception half, filled by
/// <c>EnemySystem.Ingest</c> earlier in the same tick; the only field it writes is
/// <see cref="EnemyBlackboard.StateTimer"/>. That is AR §9's discipline.
/// </para>
/// <para>
/// <b>Why the per-tick dependencies are parked in fields.</b> <see cref="StateMachine{TState}"/>
/// hands its tick handlers an <c>Action&lt;float&gt;</c> — <c>dt</c> and nothing else — so the
/// context is unpacked on the way into <see cref="Tick"/> and read by the handlers. Capturing it in
/// closures instead would allocate one per tick per enemy, which at a wave of Bloaters is exactly
/// the per-frame garbage AR §14 bans. They are cleared on the way out so that a handler reached from
/// anywhere else cannot read a stale player (<see cref="EnemyTickContext"/>).
/// </para>
/// <para>
/// <b>One instance per agent, built once and reset</b> — <c>ChaserBehaviour</c>'s bargain, and the
/// one <c>EnemyAgent.Initialise</c> keeps when a recycled agent comes back as the same archetype.
/// </para>
/// </remarks>
public sealed class BloaterBehaviour : IEnemyBehaviour
{
    private readonly EnemyAgent _agent;
    private readonly StateMachine<BloaterState> _machine;

    /// <summary>Simulated run time as of the tick in progress. See the class remarks.</summary>
    private float _now;

    private PlayerCombat _player;
    private IIntentSink _intents;
    private IDomainEvents _events;
    private EnemySystem _enemies;

    /// <param name="agent">The enemy this drives. Its spec supplies every number; its blackboard, every fact.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    /// <remarks>
    /// Guarded for <c>ChaserBehaviour</c>'s reason: <c>EnemyAgent</c> is the only caller and passes
    /// <c>this</c>, but the type is public and reachable from <c>Soulvail.Tests.Core</c>. The agent's
    /// spec is <em>not</em> checked for an <see cref="ExplosionSpec"/> here, because the spec can
    /// change under a recycled agent — what guarantees one is <see cref="EnemySpec"/>'s own
    /// constructor, which refuses a Bloater that carries no explosion block (M2-06 rule 3).
    /// </remarks>
    public BloaterBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));

        _machine = new StateMachine<BloaterState>(BloaterState.Idle);

        _machine.OnTick(BloaterState.Idle, TickIdle);

        _machine.OnTick(BloaterState.Waddle, TickWaddle);

        _machine.OnEnter(BloaterState.Fuse, EnterFuse);
        _machine.OnTick(BloaterState.Fuse, TickFuse);

        // Started here rather than on the first Tick, so Current is meaningful the moment the agent
        // exists and Reset never has to ask whether the machine has run yet.
        _machine.Start();
    }

    /// <summary>Which part of the loop this enemy is in.</summary>
    public BloaterState State => _machine.Current;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Unguarded, unlike the constructor: this runs sixty times a second for every living Bloater,
    /// and everything a null here could mean was already refused when
    /// <see cref="EnemyTickContext"/> was built — once for the whole arena, which is the other half
    /// of why the context is a struct.
    /// </para>
    /// <para>
    /// <see cref="EnemyTickContext.Projectiles"/> is deliberately ignored: a Bloater throws nothing,
    /// it arrives. The context is what a behaviour <em>may</em> reach, not what it must.
    /// </para>
    /// <para>
    /// Allocates nothing: the machine's <c>Tick</c> is allocation-free by construction, the handlers
    /// are delegates built once, and everything below is struct arithmetic over fields. The
    /// detonation is on this path and allocates nothing either — it publishes two structs through a
    /// non-boxing hub and mutates no collection.
    /// </para>
    /// </remarks>
    public void Tick(in EnemyTickContext ctx)
    {
        _now = ctx.Now;
        _player = ctx.Player;
        _intents = ctx.Intents;
        _events = ctx.Events;
        _enemies = ctx.Enemies;

        try
        {
            _machine.Tick(ctx.Dt);
        }
        finally
        {
            // Cleared even if a handler threw, so a half-finished tick cannot leave this holding a
            // player, two ports and a census that the next caller did not supply.
            _player = null;
            _intents = null;
            _events = null;
            _enemies = null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// What a recycled agent gets instead of a new behaviour (<c>EnemyAgent.Initialise</c>), and
    /// what stops a body that was mid-fuse coming back already committed (rule 12). The transition
    /// runs the current state's exit handlers, which is correct and cheap — there are none — and is
    /// skipped outright when the machine is already idle.
    /// </remarks>
    public void Reset()
    {
        _machine.Transition(BloaterState.Idle);

        _agent.Blackboard.StateTimer = 0f;
    }

    /// <summary>Rule 7: notice the player and start waddling.</summary>
    /// <remarks>
    /// The Chaser's rule and the Chaser's range, off the archetype rather than off a constant here:
    /// at 30 m an enemy noticing you is a spawner's concern rather than a stealth mechanic, and
    /// nothing about being a bomb changes that.
    /// </remarks>
    private void TickIdle(float dt)
    {
        Stand(Vector2.Zero);

        if (_agent.Blackboard.DistanceToPlayer <= _agent.Spec.AggroRange)
        {
            _machine.Transition(BloaterState.Waddle);
        }
    }

    /// <summary>Rule 7: walk at the player, and plant when close enough to be a problem.</summary>
    /// <remarks>
    /// <para>
    /// <c>ChaserBehaviour.TickChase</c>'s shape exactly, including its two arguments: the path
    /// direction wins when there is one because it is the only one that goes round a pillar, and
    /// the reach test comes after the move so the tick that arrives in range still spends its intent
    /// walking. Both are argued there at length and neither is re-decided here.
    /// </para>
    /// <para>
    /// <b>The trigger is <see cref="EnemySpec.Reach"/> — 2 m — and not the blast radius.</b> They
    /// are two different numbers doing two different jobs: the reach is how close it has to get
    /// before it commits, and the radius is how far the consequence spreads once it has. The gap
    /// between them is the metre of escape rule 9 is about, so collapsing them would quietly delete
    /// the archetype's counterplay.
    /// </para>
    /// </remarks>
    private void TickWaddle(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        Vector2 direction = blackboard.PathDirectionToPlayer != Vector2.Zero
            ? blackboard.PathDirectionToPlayer
            : blackboard.DirectionToPlayer;

        // The agent's stat, not the spec's float: depth scaling and M7-02's affixes live on the
        // stack behind it, so a stage-40 Bloater waddles at the scaled speed.
        float speed = _agent.MoveSpeed.Value;

        Emit(new Vector3(direction.X * speed, 0f, direction.Y * speed), direction);

        if (blackboard.DistanceToPlayer <= _agent.Spec.Reach)
        {
            _machine.Transition(BloaterState.Fuse);
        }
    }

    /// <summary>Rule 8, the tell: announce the fuse so the body can show it.</summary>
    /// <remarks>
    /// GD §9.1 rule 1 — every attack is telegraphed, and on this archetype the telegraph is the
    /// whole of the counterplay: it is the only warning the player gets, and 0.8 s is how long they
    /// have. The event carries the duration rather than leaving the view to look it up, and it is
    /// the same <c>EnemyTelegraph</c> a Husk publishes, so M1-12's swell already draws it with
    /// nothing added.
    /// </remarks>
    private void EnterFuse()
    {
        _agent.Blackboard.StateTimer = 0f;

        _events.Publish(new EnemyTelegraph(_agent.Id, _agent.Spec.WindupTime));
    }

    /// <summary>Rule 8: stand, swell, and go off whatever the player does.</summary>
    /// <remarks>
    /// <para>
    /// <b>Committed: it does not move, does not cancel, and ends only by going off.</b> There is no
    /// distance test here at all — the Spitter's rule rather than the Chaser's, and for a stronger
    /// reason than the Spitter has. A Bloater that abandoned its fuse when the player stepped away
    /// would be an enemy that can never do anything, because stepping away is the entire counterplay
    /// the archetype offers; the dodge window is the fuse itself, and walking out of the radius is
    /// how it is won (rule 9). So it detonates at 40 m if the player ran, and that is correct — the
    /// blast simply catches nobody, and <c>EnemyExploded</c> says so.
    /// </para>
    /// <para>
    /// <b>It kills itself, which is what makes rule 1 the only explosion path in the game.</b> The
    /// call is for <c>Health.Current</c> rather than for a large number, so the arithmetic is a kill
    /// and not an overkill whatever the agent's hit points are at that moment — a Bloater that was
    /// chipped on the way in still dies to exactly what it had left. <c>ApplyDamage</c> then
    /// publishes the damage, the death and the blast in that order, and leaves the corpse registered
    /// for its dissolve, which is why this can be called from inside <c>EnemySystem.Tick</c>'s own
    /// pass without disturbing the span it is walking (rule 11).
    /// </para>
    /// <para>
    /// The intent goes out <em>before</em> the detonation, not after: one instruction per tick is a
    /// promise this class keeps in every state including its last (rule 10), and writing it after
    /// would make the final tick of a Bloater's life the one tick its body was not pinned to the
    /// floor by.
    /// </para>
    /// </remarks>
    private void TickFuse(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        // Plant, and keep staring. The velocity is zero and the facing is live — see EnemyMoveIntent
        // on why the two are separate fields — so the swell reads as aimed at the player rather than
        // as an enemy that lost interest.
        Stand(blackboard.DirectionToPlayer);

        if (blackboard.StateTimer < _agent.Spec.WindupTime)
        {
            return;
        }

        // The one line the whole archetype is built around. Nothing is transitioned to afterwards:
        // the agent is dead, EnemySystem.Tick's aliveness check skips it from the next tick on, and
        // SweepCorpses retires it once its dissolve has played. Leaving the machine in Fuse is also
        // what makes a second detonation impossible without a flag — a corpse is never ticked again,
        // and ApplyDamage refuses one anyway (rule 1).
        _enemies.ApplyDamage(_agent.Id, _agent.Health.Current, _now, _player);
    }

    /// <summary>Emits a tick's worth of standing still, looking <paramref name="facingXZ"/>.</summary>
    private void Stand(Vector2 facingXZ) => Emit(Vector3.Zero, facingXZ);

    private void Emit(Vector3 velocity, Vector2 facingXZ)
    {
        _intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, facingXZ));
    }
}
