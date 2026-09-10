using System;

namespace Soulvail.Core.Ports;

/// <summary>
/// Wall-clock: what time it is <em>in the world</em>. The number a save file is stamped with,
/// and the one "how long were you away" is measured from. Never simulated time — see AR §18.2.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="UtcNow"/> is not seconds, is not since anything, and is not comparable with
/// <c>RunState.Time</c>. Simulated time is the sum of each tick's <c>Dt</c> and lives on the
/// snapshot; nothing in <c>Soulvail.Core.Run</c> takes an <see cref="IClock"/>, which is asserted
/// by reflection rather than left to review — the failure mode is a plausible-looking constructor
/// parameter that nobody questions.
/// </para>
/// <para>
/// <strong>One member, deliberately.</strong> A monotonic elapsed reading is the obvious second
/// one and is absent because nothing needs it: <c>Time.realtimeSinceStartup</c> behaves
/// differently across a suspend on Android, and nothing here has ever run outside the Editor to
/// say how. AR §6's rule — a port grows a member when the mechanic that needs it lands — is the
/// whole content of that decision.
/// </para>
/// <para>
/// <strong>This port promises nothing about monotonicity.</strong> A user changing the date, DST,
/// or an NTP correction all move it backwards. Anything subtracting two timestamps owes a
/// negative-difference branch, and that guard belongs to the code doing the comparison rather
/// than here, where it would be a rule with no caller to judge it against.
/// </para>
/// </remarks>
public interface IClock
{
    /// <summary>
    /// The current instant, UTC, with a zero offset. Read fresh every call — two saves in one
    /// session must not share a timestamp.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
