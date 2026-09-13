using System;
using Soulvail.Core.Save;

namespace Soulvail.Game.Composition;

/// <summary>
/// What the disk said at launch: the run there is to continue, or nothing. Read once by
/// <see cref="BootFlow"/> before the Menu appears, and asked by the Menu whether to offer a
/// <c>Continue</c>. A <c>BootScope</c> singleton beside <see cref="PendingRun"/>. See AR §7, §10.3.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <see cref="PendingRun"/> with an extra field</b> (M2-14b rule 8). They answer different
/// questions at different times: this one is a <em>fact about the disk</em>, established once at
/// launch and never written again by the app; that one is a <em>choice</em>, made in the Menu,
/// consumed in the Run scene and cleared the moment the run starts. Folding them together would
/// make "is there a saved run" and "did the player pick one" the same boolean, and the first
/// <c>Continue</c>-then-back-out would find them disagreeing — a Menu that offers nothing to
/// resume because the player looked at the offer once.
/// </para>
/// <para>
/// A mutable singleton in a project that bans global mutable state, for the reason
/// <see cref="PendingRun"/>'s own comment gives: it is <em>owned</em> — registered in a scope,
/// injected where it is needed, and unreachable without declaring that you reach it (ADR-0002).
/// </para>
/// <para>
/// <b>It is deliberately not cleared when a run is resumed</b> (M2-14b rule 10). The file survives
/// until the next stage boundary overwrites it or death clears it, so a player interrupted twice
/// inside one stage resumes twice. Clearing on read would mean a second phone call loses the run
/// outright, which is the exact failure GD §7.3 calls the most rage-inducing bug we could ship.
/// </para>
/// </remarks>
public sealed class SavedRun
{
    private RunSnapshot _value;

    /// <summary>
    /// Whether there is a run to continue. False on a fresh install, after a death, and after a
    /// load that failed — <c>LocalJsonSaveStore</c> answers an unreadable or unknown-version file
    /// as <em>there is no save</em> (M2-13b rule 5), and this is the shape that reaches the Menu.
    /// </summary>
    public bool IsPresent { get; private set; }

    /// <summary>The run on disk.</summary>
    /// <exception cref="InvalidOperationException">
    /// Nothing is present. <see cref="PendingRun"/>'s bargain and for its reason: a
    /// <c>default(RunSnapshot)</c> handed back instead would carry version 0 and stage 0, and the
    /// first of those is refused three layers away by a reader that cannot say who asked.
    /// </exception>
    public RunSnapshot Value
    {
        get
        {
            if (!IsPresent)
            {
                throw new InvalidOperationException(
                    $"No run was loaded, so {nameof(SavedRun)}.{nameof(Value)} has no value. " +
                    $"Check {nameof(IsPresent)} first.");
            }

            return _value;
        }
    }

    /// <summary>Records what the disk holds, replacing anything already recorded.</summary>
    /// <remarks>
    /// Nothing is validated. A snapshot only exists if <c>RunSnapshot</c>'s constructor let it,
    /// and whether the content it names is still shipped by this build is <c>RunSession.Start</c>'s
    /// question — asked where the catalog is, with the diagnostic that already names the real
    /// problem.
    /// </remarks>
    public void Set(in RunSnapshot snapshot)
    {
        _value = snapshot;
        IsPresent = true;
    }

    /// <summary>Forgets the loaded run, so the Menu offers nothing to continue.</summary>
    public void Clear()
    {
        _value = default;
        IsPresent = false;
    }
}
