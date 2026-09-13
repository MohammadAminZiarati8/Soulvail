using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Ai;

/// <summary>
/// Everything a behaviour is allowed to reach this tick: the clock, who it is fighting, and the
/// three places it may say something. Built once per tick by <c>RunSession</c> and passed by
/// <see langword="in"/> to every living agent. See AR §9 and M2-07b rule 2.
/// </summary>
/// <remarks>
/// <para>
/// <b>One struct rather than six parameters.</b> <c>ChaserBehaviour.Tick</c> took five, this task
/// would have made it six and M2-08's Bloater makes it seven — so widening it is one line in one
/// file instead of an edit to every implementer, every dispatch and every test that calls one. It
/// allocates nothing, and it is built <em>once per tick</em> rather than once per agent, so the
/// whole arena is decided against a single reading of the clock.
/// </para>
/// <para>
/// <b>It holds no state, and a behaviour must not store it.</b> Everything on it is this tick's: a
/// behaviour that kept the struct would be keeping this tick's clock and a player the next caller
/// did not supply. That is why both implementers unpack it into fields on the way in and clear them
/// in a <c>finally</c> on the way out, exactly as <c>ChaserBehaviour</c> has since M1-18.
/// </para>
/// <para>
/// <b><see cref="Enemies"/> is here as of M2-08, and the argument M2-07b deferred is this.</b> It
/// was left off because a handle on the census would let a behaviour spawn, despawn or damage its
/// neighbours, which is the census's job and nobody else's. What changed is that a Bloater kills
/// <em>itself</em> — the fuse ends in <c>ApplyDamage(agent.Id, …)</c> on its own id — and the one
/// thing an enemy could not previously reach was the door its own death goes through. The concession
/// is real and bounded: the blast that follows is resolved by <c>EnemySystem</c> off the spec, not
/// by the behaviour, so nothing here gained the power to hurt a neighbour. The widening is the one
/// line in the one file this struct exists to make it.
/// </para>
/// </remarks>
public readonly struct EnemyTickContext
{
    /// <param name="dt">Seconds since the last tick, from the snapshot.</param>
    /// <param name="now">
    /// Simulated run seconds — the same clock <c>Health</c>, <c>Targeter</c> and every i-frame
    /// window are measured in, never a wall clock (AR §18.2).
    /// </param>
    /// <param name="player">
    /// Who the enemies are fighting. Reached directly, because core decides every outcome an enemy
    /// causes and calls <c>PlayerCombat.ApplyDamage</c> itself (AR §18.2, ledger row 7).
    /// </param>
    /// <param name="intents">Where this tick's <c>EnemyMoveIntent</c> goes.</param>
    /// <param name="events">Where an <c>EnemyTelegraph</c> goes.</param>
    /// <param name="projectiles">
    /// The sky a shot is fired into (M2-07a). Handed to every behaviour rather than only to the ones
    /// that throw, because the context is the whole of what a tick may reach and a per-kind context
    /// would be a type per archetype.
    /// </param>
    /// <param name="enemies">
    /// The census, so that a behaviour can end its own life through the one door damage reaches an
    /// enemy through (M2-08 rule 8). See the class remarks for why it was refused in M2-07b and what
    /// changed.
    /// </param>
    /// <exception cref="ArgumentNullException">Any of the five references is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dt"/> or <paramref name="now"/> is not finite. A NaN clock does not produce a
    /// wrong enemy, it produces a silent one: every comparison a state timer makes against it is
    /// false, so an aim never completes and nothing reports it (AR §18.3).
    /// </exception>
    /// <remarks>
    /// Guarded, unlike <c>ChaserBehaviour.Tick</c>, and the asymmetry is what the struct buys: this
    /// runs once a tick instead of once per enemy per tick, so the checks cost four reference
    /// comparisons a frame for the whole arena and catch a mis-wired scope at the one place that
    /// builds one.
    /// </remarks>
    public EnemyTickContext(
        float dt,
        float now,
        PlayerCombat player,
        IIntentSink intents,
        IDomainEvents events,
        ProjectileSystem projectiles,
        EnemySystem enemies)
    {
        Dt = Finite(dt, nameof(dt));
        Now = Finite(now, nameof(now));
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Intents = intents ?? throw new ArgumentNullException(nameof(intents));
        Events = events ?? throw new ArgumentNullException(nameof(events));
        Projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        Enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
    }

    /// <summary>Seconds since the last tick, from the snapshot.</summary>
    public float Dt { get; }

    /// <summary>Simulated run seconds, never a wall clock.</summary>
    public float Now { get; }

    /// <summary>Who the enemies are fighting.</summary>
    public PlayerCombat Player { get; }

    /// <summary>Where this tick's <c>EnemyMoveIntent</c> goes.</summary>
    public IIntentSink Intents { get; }

    /// <summary>Where an <c>EnemyTelegraph</c> goes.</summary>
    public IDomainEvents Events { get; }

    /// <summary>The shots already in the air, and where a new one is fired into.</summary>
    public ProjectileSystem Projectiles { get; }

    /// <summary>
    /// The census — the one door damage reaches an enemy through, this enemy included (M2-08).
    /// </summary>
    public EnemySystem Enemies { get; }

    private static float Finite(float value, string paramName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be finite. It comes from the snapshot's clamped frame time, so a "
                    + "non-finite one is a mis-wired clock rather than a long session.");
        }

        return value;
    }
}

/// <summary>
/// What drives one enemy: implemented once per <see cref="Soulvail.Core.Content.EnemyBehaviourKind"/>
/// that does anything. See AR §9 and GD §8.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists now because there are three implementers landing in three consecutive tasks</b>,
/// which is the condition <c>EnemyAgent</c>'s own remarks set in M1-05: an interface with a single
/// implementer would be an abstraction invented for a second one nobody had written. M2-07b's
/// Spitter and M2-08's Bloater are where the shape became knowable, and this is the shape — two
/// methods, no state, no properties.
/// </para>
/// <para>
/// <b>There is no <c>State</c> on it, deliberately.</b> Each behaviour's states are its own enum —
/// a Spitter aims and a Chaser winds up, and they are not the same thing said twice — and nothing
/// outside a behaviour interprets one. A view that wants to know an enemy is dangerous is told by an
/// <c>EnemyTelegraph</c>, which is a fact about the fight rather than a peek at an FSM.
/// </para>
/// </remarks>
public interface IEnemyBehaviour
{
    /// <summary>
    /// Advances this enemy by one tick: decide, then say so through an intent, an event, or damage.
    /// </summary>
    /// <remarks>
    /// <b>Exactly one <c>EnemyMoveIntent</c> per call, in every state including the standing ones.</b>
    /// <c>EnemyView</c> folds gravity into the same <c>CharacterController.Move</c> that carries the
    /// walk, so a tick with no intent is a tick this body is not pinned to the floor by. It is the
    /// body's rule rather than any one archetype's, which is why it lives on the seam.
    /// </remarks>
    void Tick(in EnemyTickContext ctx);

    /// <summary>
    /// Back to a freshly spawned enemy. What a recycled agent gets instead of a new behaviour
    /// (<c>EnemyAgent.Initialise</c>).
    /// </summary>
    void Reset();
}
