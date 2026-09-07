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
/// intent a deliberate edit to this file. Later tasks add <c>EnemyMove</c>, <c>EnemyAction</c>,
/// <c>ConeHitRequest</c> and <c>Spawn</c> (M1).
/// </para>
/// </remarks>
public interface IIntentSink
{
    /// <summary>
    /// Sets the player's movement intent for this tick, replacing whatever was already set.
    /// The session emits exactly one per tick; last write wins.
    /// </summary>
    void PlayerMove(in PlayerMoveIntent intent);
}
