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
/// Which part of its throw a spitter is in. GD §8.1's Spitter loop, and the shape every ranged
/// archetype after it reuses.
/// </summary>
/// <remarks>
/// Five states, flat, no hierarchy — the same shape <see cref="ChaserState"/> has, and the one that
/// looks odd is <see cref="Release"/> for the same reason <c>ChaserState.Strike</c> does: it lasts
/// exactly one tick, because the shot leaves on the way in and the state exists so the moment of it
/// is nameable by anything watching.
/// </remarks>
public enum SpitterState
{
    /// <summary>Spawned and unaware. Nothing moves; the player is too far away to have been noticed.</summary>
    Idle,

    /// <summary>
    /// Walking towards the standoff band, or backing out of it. One state rather than two, because
    /// it is one question — <em>am I at the range I fight from?</em> — asked from either side.
    /// </summary>
    Approach,

    /// <summary>Planted, facing the player, telegraphing the throw. Never cancelled (rule 7).</summary>
    Aim,

    /// <summary>The shot leaves. One tick.</summary>
    Release,

    /// <summary>Rooted and reloading for <c>EnemySpec.RecoverTime</c> — the window the player closes in.</summary>
    Recover,
}

/// <summary>
/// The second kind of mind in the arena: an enemy that stops at fourteen metres, stares, and throws.
/// Every decision is core's; the body only walks where an <see cref="EnemyMoveIntent"/> tells it to,
/// and the shot it fires is a point and a moment rather than a body (M2-07a). See AR §3, §9, §18.1
/// and GD §8.1, §9.1 rule 1.
/// </summary>
/// <remarks>
/// <para>
/// <b>The archetype is in the numbers it carries, and none of them is repeated here.</b> Aggro,
/// windup and recovery come off the spec; the standoff, the flight speed and the blast radius come
/// off <see cref="EnemySpec.Projectile"/>; the walk speed and the damage come off the agent's own
/// <c>Stat</c>s, which is what makes GD §12.3's depth curve and M7-02's affixes reach a thrown shot
/// without a second mechanism. The single constant below — <see cref="RetreatFraction"/> — is what is
/// left of the numbers that belong to the <em>behaviour</em> rather than to any archetype, and it is
/// argued where it is declared.
/// </para>
/// <para>
/// <b>It reads perception and writes working memory, and never the other way round.</b> Everything
/// it knows arrives on <see cref="EnemyBlackboard"/>'s perception half, filled by
/// <c>EnemySystem.Ingest</c> earlier in the same tick; the only field it writes is
/// <see cref="EnemyBlackboard.StateTimer"/>. That is AR §9's discipline, and it is what lets a test
/// put the player at 9.79 m without building a world to get him there.
/// </para>
/// <para>
/// <b>Why the per-tick dependencies are parked in fields.</b> <see cref="StateMachine{TState}"/>
/// hands its tick handlers an <c>Action&lt;float&gt;</c> — <c>dt</c> and nothing else — so the
/// context is unpacked on the way into <see cref="Tick"/> and read by the handlers. Capturing it in
/// closures instead would allocate one per tick per enemy, which at a wave of Spitters is exactly the
/// per-frame garbage AR §14 bans. They are cleared on the way out so that a handler reached from
/// anywhere else cannot read a stale player (<see cref="EnemyTickContext"/>).
/// </para>
/// <para>
/// <b>One instance per agent, built once and reset</b> — <c>ChaserBehaviour</c>'s bargain, and the
/// one <c>EnemyAgent.Initialise</c> keeps when a recycled agent comes back as the same archetype.
/// </para>
/// </remarks>
public sealed class SpitterBehaviour : IEnemyBehaviour
{
    /// <summary>
    /// How far inside the standoff range the player must get before it backs away, as a fraction of
    /// <see cref="ProjectileSpec.StandoffRange"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately less than one, and the gap between the two is the mechanic. At exactly the
    /// standoff it would oscillate on the first centimetre of drift — a step in, a step out, for
    /// ever — which is the same argument <c>ChaserBehaviour.WindupCancelReachMultiplier</c> makes on
    /// the other side of a distance.
    /// </para>
    /// <para>
    /// <b>0.7 is chosen against the Censer rather than picked.</b> 14 × 0.7 is 9.8 m: just outside
    /// the Oathbound's 8 m weapon range and just inside its 12 m acquire range, so a Spitter the
    /// player has targeted still has to be <em>chased</em>, and one standing at full standoff cannot
    /// be auto-acquired at all. That is the pressure the archetype exists to apply (GD §8.1).
    /// </para>
    /// </remarks>
    public const float RetreatFraction = 0.7f;

    private readonly EnemyAgent _agent;
    private readonly StateMachine<SpitterState> _machine;

    /// <summary>Simulated run time as of the tick in progress. See the class remarks.</summary>
    private float _now;

    private PlayerCombat _player;
    private IIntentSink _intents;
    private IDomainEvents _events;
    private ProjectileSystem _projectiles;

    /// <param name="agent">The enemy this drives. Its spec supplies every number; its blackboard, every fact.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    /// <remarks>
    /// Guarded for <c>ChaserBehaviour</c>'s reason: <c>EnemyAgent</c> is the only caller and passes
    /// <c>this</c>, but the type is public and reachable from <c>Soulvail.Tests.Core</c>. The agent's
    /// spec is <em>not</em> checked for a <see cref="ProjectileSpec"/> here, because the spec can
    /// change under a recycled agent — what guarantees one is <see cref="EnemySpec"/>'s own
    /// constructor, which refuses a Spitter that carries no projectile block (M2-06 rule 3).
    /// </remarks>
    public SpitterBehaviour(EnemyAgent agent)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));

        _machine = new StateMachine<SpitterState>(SpitterState.Idle);

        _machine.OnTick(SpitterState.Idle, TickIdle);

        _machine.OnTick(SpitterState.Approach, TickApproach);

        _machine.OnEnter(SpitterState.Aim, EnterAim);
        _machine.OnTick(SpitterState.Aim, TickAim);

        _machine.OnEnter(SpitterState.Release, EnterRelease);
        _machine.OnTick(SpitterState.Release, TickRelease);

        _machine.OnEnter(SpitterState.Recover, EnterRecover);
        _machine.OnTick(SpitterState.Recover, TickRecover);

        // Started here rather than on the first Tick, so Current is meaningful the moment the agent
        // exists and Reset never has to ask whether the machine has run yet.
        _machine.Start();
    }

    /// <summary>Which part of the loop this enemy is in.</summary>
    public SpitterState State => _machine.Current;

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Unguarded, unlike the constructor: this runs sixty times a second for every living Spitter,
    /// and everything a null here could mean was already refused when
    /// <see cref="EnemyTickContext"/> was built — once for the whole arena, which is the other half
    /// of why the context is a struct.
    /// </para>
    /// <para>
    /// Allocates nothing: the machine's <c>Tick</c> is allocation-free by construction, the handlers
    /// are delegates built once, and everything below is struct arithmetic over fields.
    /// </para>
    /// </remarks>
    public void Tick(in EnemyTickContext ctx)
    {
        _now = ctx.Now;
        _player = ctx.Player;
        _intents = ctx.Intents;
        _events = ctx.Events;
        _projectiles = ctx.Projectiles;

        try
        {
            _machine.Tick(ctx.Dt);
        }
        finally
        {
            // Cleared even if a handler threw, so a half-finished tick cannot leave this holding a
            // player, two ports and a sky that the next caller did not supply.
            _player = null;
            _intents = null;
            _events = null;
            _projectiles = null;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// What a recycled agent gets instead of a new behaviour (<c>EnemyAgent.Initialise</c>, rule 4).
    /// The transition runs the current state's exit handlers, which is correct and cheap — there are
    /// none — and is skipped outright when the machine is already idle.
    /// </remarks>
    public void Reset()
    {
        _machine.Transition(SpitterState.Idle);

        _agent.Blackboard.StateTimer = 0f;
    }

    /// <summary>Rule 5: notice the player and start closing.</summary>
    /// <remarks>
    /// The Chaser's rule and the Chaser's range, off the archetype rather than off a constant here:
    /// at 30 m an enemy noticing you is a spawner's concern rather than a stealth mechanic, and
    /// nothing about being a thrower changes that.
    /// </remarks>
    private void TickIdle(float dt)
    {
        Stand(Vector2.Zero);

        if (_agent.Blackboard.DistanceToPlayer <= _agent.Spec.AggroRange)
        {
            _machine.Transition(SpitterState.Approach);
        }
    }

    /// <summary>Rule 6: a band, not a point — walk in, back out, or plant and aim.</summary>
    /// <remarks>
    /// <para>
    /// <b>Walking in follows the NavMesh path; walking out does not</b> (rule 11). A retreat is
    /// <c>−DirectionToPlayer</c> and nothing else, because negating a path direction points away from
    /// the <em>next waypoint</em> rather than away from the player, and would walk a Spitter into the
    /// pillar it was just routed around. The cost is that a retreating Spitter can back into geometry;
    /// the <c>CharacterController</c> slides it along, and GD §7.2 guarantees no dead ends. Named
    /// here rather than discovered in a playtest.
    /// </para>
    /// <para>
    /// It keeps facing the player while it backs off. The velocity is what retreats; the stare is
    /// what makes a Spitter kiting read as a Spitter kiting rather than as one that has lost
    /// interest — and it is the facing the next <see cref="SpitterState.Aim"/> starts from.
    /// </para>
    /// </remarks>
    private void TickApproach(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        float standoff = _agent.Spec.Projectile.StandoffRange;
        float distance = blackboard.DistanceToPlayer;

        // The agent's stat, not the spec's float: depth scaling and M7-02's affixes live on the
        // stack behind it, so a stage-40 Spitter kites at the scaled speed (rule 12).
        float speed = _agent.MoveSpeed.Value;

        if (distance > standoff)
        {
            Vector2 inbound = blackboard.PathDirectionToPlayer != Vector2.Zero
                ? blackboard.PathDirectionToPlayer
                : blackboard.DirectionToPlayer;

            Emit(Ground(inbound, speed), inbound);

            return;
        }

        if (distance < standoff * RetreatFraction)
        {
            Vector2 away = -blackboard.DirectionToPlayer;

            Emit(Ground(away, speed), blackboard.DirectionToPlayer);

            return;
        }

        // Inside the band: plant, and let Aim's own tick do the staring. The intent goes out before
        // the transition for TickChase's reason — one instruction per tick, whatever else happens.
        Stand(blackboard.DirectionToPlayer);

        _machine.Transition(SpitterState.Aim);
    }

    /// <summary>Rule 7, the tell: announce the windup so the body can show it.</summary>
    /// <remarks>
    /// GD §9.1 rule 1 — every attack is telegraphed. The event carries the duration rather than
    /// leaving the view to look it up, and it is the same <c>EnemyTelegraph</c> a Husk publishes, so
    /// M1-12's swell already draws it with nothing added.
    /// </remarks>
    private void EnterAim()
    {
        _agent.Blackboard.StateTimer = 0f;

        _events.Publish(new EnemyTelegraph(_agent.Id, _agent.Spec.WindupTime));
    }

    /// <summary>Rule 7: stand, face, and throw whatever the player does.</summary>
    /// <remarks>
    /// <b>An aim never cancels, and that is the deliberate opposite of the Chaser's windup.</b> A
    /// Spitter that abandoned its aim whenever the player moved would never fire, because moving is
    /// what the player does — the dodge window is the <em>flight</em>, not the telegraph (M2-07a
    /// rule 3). So there is no distance test here at all: it stands and faces for the whole windup,
    /// including while the player walks into melee range or out of aggro entirely.
    /// </remarks>
    private void TickAim(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        // Face, do not move. The stare is the tell, so the facing is live even though the velocity
        // is zero — see EnemyMoveIntent on why the two are separate fields.
        Stand(blackboard.DirectionToPlayer);

        if (blackboard.StateTimer >= _agent.Spec.WindupTime)
        {
            _machine.Transition(SpitterState.Release);
        }
    }

    /// <summary>Rule 8: the shot leaves.</summary>
    /// <remarks>
    /// <para>
    /// Fired on entry rather than on the release's own tick, so the bolt leaves on the tick the aim
    /// completed — the frame the player was watching — instead of one frame later. <c>Strike</c>'s
    /// shape, for <c>Strike</c>'s reason.
    /// </para>
    /// <para>
    /// <b>It is aimed at where the player is standing at that instant</b>, never led (CC §3.7 is the
    /// player's problem and M5-01's decision): an enemy that led its target would remove the dodge
    /// the archetype exists to demand. The damage is the agent's <c>ContactDamage</c> stat, so GD
    /// §12.3's d(n) is already in it; the speed and the radius are the archetype's projectile block.
    /// </para>
    /// <para>
    /// <b>A refused shot is not a special case.</b> <c>Fire</c> answers <c>NoProjectile</c> when the
    /// sky is full and this does not look: the Spitter believes it fired, which is the only reading
    /// that does not require an enemy to know the projectile system's capacity (M2-07a rule 7).
    /// </para>
    /// </remarks>
    private void EnterRelease()
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer = 0f;

        ProjectileSpec projectile = _agent.Spec.Projectile;

        var shot = new Projectile(
            _agent.Spec.Id,
            _agent.Id,
            blackboard.SelfPosition,
            blackboard.PlayerPosition,
            projectile.Speed,
            projectile.Radius,
            _agent.ContactDamage.Value);

        _projectiles.Fire(shot, _now);
    }

    /// <summary>Rule 8: one tick, then the reload.</summary>
    private void TickRelease(float dt)
    {
        Stand(_agent.Blackboard.DirectionToPlayer);

        _machine.Transition(SpitterState.Recover);
    }

    private void EnterRecover()
    {
        _agent.Blackboard.StateTimer = 0f;
    }

    /// <summary>Rule 9: rooted and reloading for as long as the archetype says.</summary>
    /// <remarks>
    /// It goes back to <see cref="SpitterState.Approach"/> rather than to <see cref="SpitterState.Idle"/>
    /// whatever the distance — <c>ChaserBehaviour.TickRecover</c>'s rule, and the same one: an enemy
    /// that has shot at you knows where you are, and re-deciding aggro after every throw would let a
    /// player walk 30 m and switch it off.
    /// </remarks>
    private void TickRecover(float dt)
    {
        EnemyBlackboard blackboard = _agent.Blackboard;

        blackboard.StateTimer += dt;

        Stand(blackboard.DirectionToPlayer);

        if (blackboard.StateTimer >= _agent.Spec.RecoverTime)
        {
            _machine.Transition(SpitterState.Approach);
        }
    }

    /// <summary>A ground-plane velocity: <paramref name="directionXZ"/> at <paramref name="speed"/>.</summary>
    private static Vector3 Ground(Vector2 directionXZ, float speed) =>
        new Vector3(directionXZ.X * speed, 0f, directionXZ.Y * speed);

    /// <summary>Emits a tick's worth of standing still, looking <paramref name="facingXZ"/>.</summary>
    private void Stand(Vector2 facingXZ) => Emit(Vector3.Zero, facingXZ);

    private void Emit(Vector3 velocity, Vector2 facingXZ)
    {
        _intents.EnemyMove(new EnemyMoveIntent(_agent.Id, velocity, facingXZ));
    }
}
