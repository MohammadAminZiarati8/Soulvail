using System;
using Soulvail.Core.Content;
using Soulvail.Core.Save;

namespace Soulvail.Game.Composition;

/// <summary>
/// What the next run should be. The Menu writes it (M0-17), the Run scope reads it (M0-16) —
/// the one piece of state that has to survive a scene load, so it lives in <c>BootScope</c> as
/// a singleton. See AR §7.
/// </summary>
/// <remarks>
/// <para>
/// A mutable singleton in a project that bans global mutable state, and the difference is that
/// this one is <em>owned</em>: it is registered in a scope, injected where it is needed, and
/// nothing can reach it without declaring that it does. A static <c>NextRun</c> field would do
/// the same job and be reachable from everywhere, which is exactly what ADR-0002 exists to
/// prevent.
/// </para>
/// <para>
/// Reading <see cref="ModeId"/>, <see cref="CharacterId"/> or <see cref="Seed"/> before
/// <see cref="Set"/> throws
/// rather than returning a default, because the two callers want opposite things from an unset
/// run and neither is served by a zero. <c>RunInstaller</c> and <c>RunTicker</c> ask
/// <see cref="IsSet"/> first and fall back deliberately (pressing Play directly in the Run
/// scene); anything else reaching for the values without asking has lost track of whether a
/// menu ran, and a silent <c>default(ContentId)</c> would surface a scene later as missing
/// content.
/// </para>
/// <para>
/// <see cref="Set"/> does not validate the id. A <c>default(ContentId)</c> arriving here means
/// nobody chose a class, and <see cref="Soulvail.Core.Run.RunConfig"/> already owns that
/// diagnostic by name (M0-09) — guarding here as well would give one mistake two error
/// messages and two places to keep in step.
/// </para>
/// </remarks>
public sealed class PendingRun
{
    private ContentId _modeId;
    private ContentId _characterId;
    private int _seed;
    private RunSnapshot? _snapshot;

    /// <summary>Whether a run has been configured since the last <see cref="Clear"/>.</summary>
    public bool IsSet { get; private set; }

    /// <summary>The mode the next run plays, e.g. <c>mode.descent</c>.</summary>
    /// <exception cref="InvalidOperationException">No run is pending.</exception>
    public ContentId ModeId
    {
        get
        {
            ThrowIfNotSet(nameof(ModeId));
            return _modeId;
        }
    }

    /// <summary>The class the next run plays.</summary>
    /// <exception cref="InvalidOperationException">No run is pending.</exception>
    public ContentId CharacterId
    {
        get
        {
            ThrowIfNotSet(nameof(CharacterId));
            return _characterId;
        }
    }

    /// <summary>The seed the next run's generator is built from.</summary>
    /// <exception cref="InvalidOperationException">No run is pending.</exception>
    public int Seed
    {
        get
        {
            ThrowIfNotSet(nameof(Seed));
            return _seed;
        }
    }

    /// <summary>
    /// Records what the next run should be, replacing anything already pending.
    /// </summary>
    /// <remarks>
    /// The parameters are in <c>RunConfig</c>'s order — mode, class, seed — because that is the
    /// object this one is eventually copied into, and two orderings for the same three values is
    /// an argument swap the compiler cannot see (both ids are <see cref="ContentId"/>).
    /// </remarks>
    public void Set(ContentId modeId, ContentId characterId, int seed)
    {
        _modeId = modeId;
        _characterId = characterId;
        _seed = seed;
        _snapshot = null;
        IsSet = true;
    }

    /// <summary>
    /// The resume form: records that the next run continues <paramref name="snapshot"/>, replacing
    /// anything already pending.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It takes no ids and no seed, and that is the point.</b> Mode, class and seed all come off
    /// the snapshot, so a resume cannot be handed a run whose three values disagree with the file
    /// it claims to be continuing — the one mistake that would put the streams on the right
    /// sequence at the wrong position. <c>RunSession.Start</c> checks the agreement anyway (M2-14b
    /// rule 4), because the check costs a comparison and is the only thing standing between a
    /// composition mistake and a run that silently deals itself the wrong stage.
    /// </para>
    /// <para>
    /// <see cref="Set"/> clears <see cref="Snapshot"/> for the matching reason: a fresh run started
    /// after a <c>Continue</c> was offered and declined must not inherit the resume that was on
    /// screen a moment ago.
    /// </para>
    /// </remarks>
    public void Resume(in RunSnapshot snapshot)
    {
        _modeId = snapshot.ModeId;
        _characterId = snapshot.CharacterId;
        _seed = snapshot.Seed;
        _snapshot = snapshot;
        IsSet = true;
    }

    /// <summary>
    /// The run the next one continues, or <see langword="null"/> for a fresh run.
    /// </summary>
    /// <remarks>
    /// Nullable rather than throwing like the three above, and the asymmetry is deliberate: an
    /// unset mode id has no sensible answer, while "is this a resume" has exactly one for a run
    /// that was never set, and it is the same as the answer for a fresh one. It is what
    /// <c>RunTicker.Start</c> passes straight into <c>RunConfig.Restore</c>, on both paths, with
    /// no branch.
    /// </remarks>
    public RunSnapshot? Snapshot => _snapshot;

    /// <summary>
    /// Forgets the pending run. Called once the run has started, so a second trip through the
    /// Run scene cannot silently reuse the previous run's seed.
    /// </summary>
    /// <remarks>
    /// <b>Its one caller is <c>RunTicker.Start</c>, immediately after <c>_session.Start</c>
    /// returns</b> (M2-14b rule 9) — a promise this doc comment made at M0-12 and nothing kept
    /// until then. That is the last read: <c>RunInstaller.CreateRandom</c> resolves this object
    /// when <c>IRandom</c> is first built, which happens while <c>RunSession</c> is being
    /// constructed and therefore before the ticker starts, so clearing here cannot pull a value
    /// out from under anything. Clearing from the installer would.
    /// </remarks>
    public void Clear()
    {
        _modeId = default;
        _characterId = default;
        _seed = 0;
        _snapshot = null;
        IsSet = false;
    }

    private void ThrowIfNotSet(string member)
    {
        if (!IsSet)
        {
            throw new InvalidOperationException(
                $"No run is pending, so {nameof(PendingRun)}.{member} has no value. " +
                $"Check {nameof(IsSet)} first.");
        }
    }
}
