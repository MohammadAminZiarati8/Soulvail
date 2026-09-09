using System.Numerics;

namespace Soulvail.Core.Ports;

/// <summary>
/// What the player asks the run to do, as opposed to what the world reports. The first inbound
/// <em>command</em> port — the C of ADR-0003's "commands and facts in, events and intents out". See
/// AR §4.1 and §6, and CC §3.4.
/// </summary>
/// <remarks>
/// <para>
/// <b>A command is not a fact and not a tick.</b> A fact says what happened in the world
/// (<c>ReportConeHits</c>, M1-11); a tick is the frame passing; a command is a decision the person
/// holding the phone made, arriving whenever their thumb lands rather than on any schedule. Keeping
/// them separate is what lets core apply a tap at a defined moment in the frame instead of
/// wherever in the call stack the input happened to be read — see <c>Targeter.Focus</c>, which
/// defers the request to the next tick precisely because a tap arrives between ticks.
/// </para>
/// <para>
/// Separate from <see cref="IRunSession"/> rather than bolted onto it, though
/// <c>RunSession</c> implements both. The lifecycle port is what the frame loop holds; this is what
/// an input adapter holds, and an adapter that could also <c>End()</c> the run is an adapter with a
/// reach it has no business having. The two are registered to the same instance in
/// <c>RunInstaller</c>, so there is still exactly one brain.
/// </para>
/// <para>
/// Every member throws when no run is running. A command arriving in a menu is a wiring mistake —
/// the input map is disabled outside a run — and a silent no-op would hide it until someone
/// wondered why tapping did nothing.
/// </para>
/// </remarks>
public interface IPlayerCommands
{
    /// <summary>
    /// The player tapped <paramref name="worldPoint"/> on the ground: focus the enemy nearest to it
    /// within <c>FocusResolver.RadiusMetres</c>, or drop the focus when there is none.
    /// </summary>
    /// <remarks>
    /// A point, not an id, and that is the boundary doing its job. Unity knows where a thumb landed
    /// and how to turn it into a place on the ground; only core knows which enemies are there and
    /// which of them is worth picking. An adapter that resolved the id itself would be an adapter
    /// making a gameplay decision (AR §3), and the 3 m radius would live in the body rather than in
    /// the brain.
    /// </remarks>
    /// <param name="worldPoint">
    /// Where the tap landed on the ground plane, in world metres. Its Y is ignored — every distance
    /// in this project is measured on XZ.
    /// </param>
    /// <exception cref="System.InvalidOperationException">No run is running.</exception>
    void FocusTarget(Vector3 worldPoint);

    /// <summary>
    /// Drops the player's focus, returning target selection to scoring.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="FocusTarget"/> with an empty point, even though tapping bare ground
    /// has the same effect (CC §3.4). This one is the explicit request, for a caller that wants to
    /// clear the focus without pretending to have tapped anywhere — a stage transition, or a death.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">No run is running.</exception>
    void ClearFocus();
}
