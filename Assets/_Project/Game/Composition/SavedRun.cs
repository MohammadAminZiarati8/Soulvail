using System;
using Soulvail.Core.Save;

namespace Soulvail.Game.Composition;

/// <summary>
/// The run there is to continue, or nothing: what the disk said at launch, kept current by every
/// write and every clear since. Seeded once by <see cref="BootFlow"/> before the Menu appears,
/// mirrored by <c>SaveWriter</c> ahead of each disk operation, and asked by the Menu whether to
/// offer a <c>Continue</c>. A <c>BootScope</c> singleton beside <see cref="PendingRun"/>. See AR §7,
/// §10.3.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not <see cref="PendingRun"/> with an extra field</b> (M2-14b rule 8). They answer different
/// questions at different times: this one is a <em>fact about the disk</em>, read at launch and
/// kept a step ahead of it after; that one is a <em>choice</em>, made in the Menu, consumed in the
/// Run scene and cleared the moment the run starts. Folding them together would make "is there a
/// saved run" and "did the player pick one" the same boolean, and the first
/// <c>Continue</c>-then-back-out would find them disagreeing — a Menu that offers nothing to
/// resume because the player looked at the offer once.
/// </para>
/// <para>
/// <b>Two writers, at two moments</b> (M6-11a). <see cref="BootFlow"/> seeds it once, before any
/// run exists in this session to lag; <c>SaveWriter</c> then sets it on every snapshot and clears
/// it on every death, <em>before</em> queuing the disk operation, so it is never older than the
/// file. Until M6-11a boot was the only writer, and a quit and a <c>Continue</c> in one app session
/// resumed the run read at launch — M6-11 lost a stage-30 run to it. A write that fails leaves this
/// newer than the file, which is the stated cost: the same session resumes the run just played,
/// and a relaunch the older one.
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

    /// <summary>The run most recently read or written — the one on disk, unless its write failed.</summary>
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

    /// <summary>
    /// Records a run read from disk or about to be written to it, replacing anything already
    /// recorded.
    /// </summary>
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

    /// <summary>Forgets the run, so the Menu offers nothing to continue.</summary>
    public void Clear()
    {
        _value = default;
        IsPresent = false;
    }
}
