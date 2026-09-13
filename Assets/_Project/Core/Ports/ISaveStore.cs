using System.Threading.Tasks;
using Soulvail.Core.Save;

namespace Soulvail.Core.Ports;

/// <summary>
/// Persistence. Core defines the DTOs and this port; the adapter owns the medium — a JSON file
/// under <c>persistentDataPath</c> today (M2-13b), a server behind it later. See AR §10.3 and
/// <see href="../../../../Docs/adr/0007-save-store-async-local-first-versioned.md">ADR-0007</see>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Async from day one, with a synchronous first adapter.</b> Nothing here awaits anything and
/// the local write is a file write; the point is that the day <c>SyncingSaveStore</c> wraps the
/// local one — write locally, push in the background, reconcile on launch — not one signature
/// moves. A launched game's saves are a contract, and so is the shape of the port that reads them.
/// </para>
/// <para>
/// <b>No parameter is taken by <c>in</c>, deliberately, despite both DTOs being structs.</b> An
/// <c>async</c> method cannot have a by-ref parameter, so <c>SaveRun(in RunSnapshot)</c> would
/// compile today only because the first adapter is synchronous, and would refuse the first
/// implementation that reaches for <c>async</c> — which is exactly the implementation ADR-0007
/// says is coming. Two struct copies a stage cost nothing; a port that cannot be implemented
/// asynchronously costs a signature change on a shipped save format. <see cref="IRandom.Restore"/>
/// <em>is</em> <c>in</c>, because it is a core call no async implementation is imaginable for.
/// </para>
/// <para>
/// <b>The two <c>Load</c> methods return <see cref="System.Nullable{T}"/>, not nullable
/// references.</b> Both DTOs are structs, so <c>PlayerProfile?</c> is a value that is there or is
/// not, and nothing about it depends on the nullable-reference feature this project enables
/// nowhere. Null means <em>no save</em>; a save that exists but is unreadable is the adapter's
/// problem and it answers it by refusing, not by returning null.
/// </para>
/// </remarks>
public interface ISaveStore
{
    /// <summary>The stored profile, or null when the player has never had one.</summary>
    /// <remarks>
    /// Null rather than <see cref="PlayerProfile.Default"/>, so that "no file yet" and "a file
    /// that says haptics are off" stay different answers. Substituting the default is the
    /// caller's decision and it is made once, where the profile is first read.
    /// </remarks>
    Task<PlayerProfile?> LoadProfile();

    /// <summary>Writes <paramref name="profile"/>, replacing whatever was stored.</summary>
    Task SaveProfile(PlayerProfile profile);

    /// <summary>The run in progress, or null when there is nothing to resume.</summary>
    Task<RunSnapshot?> LoadRun();

    /// <summary>
    /// Writes <paramref name="run"/>, replacing whatever was stored. One run is kept, never a
    /// history: a resume offers the run that was interrupted, and there is no second one to choose
    /// between.
    /// </summary>
    Task SaveRun(RunSnapshot run);

    /// <summary>
    /// Forgets the stored run, so that nothing is offered to resume. Succeeds when there was
    /// nothing to forget — a run is cleared on death and on a completed descent, and neither
    /// caller can know whether a snapshot was ever written.
    /// </summary>
    Task ClearRun();
}
