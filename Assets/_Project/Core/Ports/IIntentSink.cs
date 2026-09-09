using Soulvail.Core.Run;

namespace Soulvail.Core.Ports;

/// <summary>
/// The outbound port core writes per-tick intents to: "this is what the body should do now."
/// Filled during <c>Tick</c>, read by the views immediately after. See AR §4.1, §6 and
/// <see href="../../../../Docs/adr/0003-commands-facts-tick-events-intents.md">ADR-0003</see>.
/// </summary>
/// <remarks>
/// <para>
/// Intents are not events. An event says what happened and any number of listeners may ignore
/// it; an intent is an instruction to exactly one body, so the adapter keeps only the latest
/// one per kind. Nothing here returns a value — core states what it wants and never asks the
/// world a question.
/// </para>
/// <para>
/// One named method per intent kind, rather than a generic <c>Push&lt;T&gt;</c>. A generic push
/// would force the sink to work out at run time what it had been handed — a type test, a boxed
/// payload or a type-keyed lookup on every intent, every frame — and it would let the port grow
/// by accident, since any new struct would already be accepted. A named method makes adding an
/// intent a deliberate edit to this file. Later tasks add <c>EnemyMove</c>, <c>EnemyAction</c>
/// and <c>Spawn</c> (M1).
/// </para>
/// </remarks>
public interface IIntentSink
{
    /// <summary>
    /// Sets the player's movement intent for this tick, replacing whatever was already set.
    /// The session emits exactly one per tick; last write wins.
    /// </summary>
    void PlayerMove(in PlayerMoveIntent intent);

    /// <summary>
    /// Asks the body to resolve a cone and report who was inside it.
    /// </summary>
    /// <remarks>
    /// Accumulated rather than replaced, unlike <see cref="PlayerMove"/>, and the difference is
    /// what the two intents are. A movement intent is a state — the body can only be moving one
    /// way, so the newest is the only one that matters. A cone is an <em>event</em> the body owes
    /// an answer to, and dropping one because a second arrived in the same tick would lose a whole
    /// swing's damage. Nothing emits two in one tick today; M1-15's Charge is the first that can.
    /// </remarks>
    void ConeHit(in ConeHitIntent intent);

    /// <summary>
    /// Tells the body to dash: this way, this far, over this long. Written on the tick a movement
    /// skill fires and on no other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Replaced rather than accumulated, like <see cref="PlayerMove"/> and unlike
    /// <see cref="ConeHit"/>, and for the same reason movement is a state: a character can only be
    /// dashing one way. Two in a tick is unreachable — <c>ChargeSkill</c> starts at most one dash
    /// per tick and refuses to start another while one is in flight — so "last write wins" is a
    /// statement about the shape of the intent rather than a policy anything relies on.
    /// </para>
    /// <para>
    /// While the dash is in flight core writes no <see cref="PlayerMove"/> at all. The two are the
    /// only instructions about where the player goes, and a body handed both would have to decide
    /// which of them core meant.
    /// </para>
    /// </remarks>
    void Charge(in ChargeIntent intent);

    /// <summary>
    /// Tells the body to shove an enemy: this far, this way. Written while core resolves a
    /// pass-through fact, once per enemy hit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Accumulated, like <see cref="ConeHit"/>: a dash through four Husks writes four of these, and
    /// keeping only the newest would leave three of them standing where they were.
    /// </para>
    /// <para>
    /// The first intent about something that is not the player, and the reason the port names the
    /// subject: everything above is implicitly about the character, and everything after M1-18 will
    /// not be.
    /// </para>
    /// </remarks>
    void EnemyKnockback(in EnemyKnockbackIntent intent);
}
