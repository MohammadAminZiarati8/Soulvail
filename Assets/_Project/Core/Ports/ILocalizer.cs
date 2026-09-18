using Soulvail.Core.Content;

namespace Soulvail.Core.Ports;

/// <summary>
/// How a <see cref="LocKey"/> becomes a word. ADR-0012's other half, three milestones late.
/// </summary>
/// <remarks>
/// <para>
/// <b>One member, and AR §6's row was corrected rather than obeyed</b> (rule 5). The architecture
/// has carried <c>string Get(LocKey key, params)</c> since M0 and nothing in M3 needs the
/// <c>params</c>: both callers with a number in them already format it themselves — the Skills
/// screen's trigger line (M3-09b rule 2, <em>"core says what kind of number it is; the screen
/// formats it"</em>) and the Overflow toast (M3-10b rule 13, a key plus a number). A
/// <c>params object[]</c> overload allocates an array on every call, and these are reached from
/// <c>HudPresenter</c> and <c>AutoCastRow</c>, which run near the frame. <b>M6-10 adds the overload
/// the day a translated sentence needs a substitution inside it</b>, which is a real need and not
/// this one — and AR §6's row now says so.
/// </para>
/// <para>
/// <b>Nothing in core takes one.</b> The port is an interface over a <see cref="LocKey"/>, and no
/// core class has a reason to know what a key <em>says</em> — that is ADR-0012's whole shape, and
/// <c>AssemblyPurityTests.Core_TakesNoLocalizer</c> is what keeps it true. Every caller lives in
/// <c>Soulvail.Game</c>, which is where a user-facing string belongs. An interface in
/// <c>Core/Ports</c> mentioning <see langword="string"/> is what a port is (rule 10).
/// </para>
/// <para>
/// <b>One language, one table.</b> No locale selection, no plural rules, no right-to-left, no
/// format arguments — all of that is M6-10, which is a <em>widening</em> of this rather than a
/// rewrite of it (rule 2): a second table beside the first, a locale on the profile, and the rest
/// of the game's rows.
/// </para>
/// </remarks>
public interface ILocalizer
{
    /// <summary>
    /// The string for <paramref name="key"/>, or the key's own text when there is no row.
    /// </summary>
    /// <param name="key">What to look up. <c>default</c> is legal and answers empty.</param>
    /// <returns>
    /// The row's text, or <see cref="LocKey.ToString"/> when the table has no row for it.
    /// <b>Never null and never a throw</b> (rule 1): a screen showing nothing is a bug that looks
    /// like a broken layout, where a screen showing <c>skill.oathbound.consecrate.name</c> is the
    /// behaviour M3 has had since M3-08b and diagnoses itself. It also keeps the port honest for
    /// M6-10's other languages, where a half-translated table is the normal state rather than an
    /// error.
    /// </returns>
    string Get(LocKey key);
}
