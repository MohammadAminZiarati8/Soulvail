using System;
using Soulvail.Core.Content;

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
/// Reading <see cref="CharacterId"/> or <see cref="Seed"/> before <see cref="Set"/> throws
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
    private ContentId _characterId;
    private int _seed;

    /// <summary>Whether a run has been configured since the last <see cref="Clear"/>.</summary>
    public bool IsSet { get; private set; }

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
    public void Set(ContentId characterId, int seed)
    {
        _characterId = characterId;
        _seed = seed;
        IsSet = true;
    }

    /// <summary>
    /// Forgets the pending run. Called once the run has started, so a second trip through the
    /// Run scene cannot silently reuse the previous run's seed.
    /// </summary>
    public void Clear()
    {
        _characterId = default;
        _seed = 0;
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
