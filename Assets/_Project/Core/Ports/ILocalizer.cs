using Soulvail.Core.Content;

namespace Soulvail.Core.Ports;

/// <summary>
/// How a <see cref="LocKey"/> becomes a word. ADR-0012's other half, three milestones late.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two members, and AR §6's row was corrected rather than obeyed — twice.</b> The architecture
/// carried <c>string Get(LocKey key, params)</c> from M0. M3-14a cut the <c>params</c> (rule 5),
/// because a <c>params object[]</c> overload allocates an array on every call and <c>Get</c> is
/// reached from <c>HudPresenter</c> and <c>AutoCastRow</c>, which run near the frame. M6-10 brings
/// the substitution back as <see cref="Format"/>, a second member with a different name rather than
/// an overload of <see cref="Get"/> (M6-10 rule 4). With an overload, one extra argument at one of
/// those callers would allocate on every draw, and the compiler would not object. A different name
/// cannot be reached by accident, and <c>Localizer_TheFrameAdjacentCallersDoNotFormat</c> checks
/// that those two callers never reach it.
/// </para>
/// <para>
/// <b>Nothing in core takes one.</b> The port is an interface over a <see cref="LocKey"/>, and no
/// core class has a reason to know what a key <em>says</em> — that is ADR-0012's whole shape, and
/// <c>AssemblyPurityTests.Core_TakesNoLocalizer</c> is what keeps it true. Every caller lives in
/// <c>Soulvail.Game</c>, which is where a user-facing string belongs. An interface in
/// <c>Core/Ports</c> mentioning <see langword="string"/> is what a port is (M3-14a rule 10).
/// </para>
/// <para>
/// <b>Many tables, one reader's number format</b> (M6-10). Which language is being read, the
/// fallback it drops to and the culture a number is written in are all the adapter's business, and
/// none of them is on this port. No plural rules, no grammatical gender, no right-to-left: nothing
/// shipped needs one, and the language list that would is still unwritten (M6-10's *Out of scope*).
/// </para>
/// </remarks>
public interface ILocalizer
{
    /// <summary>
    /// The string for <paramref name="key"/>, or the key's own text when there is no row.
    /// </summary>
    /// <param name="key">What to look up. <c>default</c> is legal and answers empty.</param>
    /// <returns>
    /// The row's text, or <see cref="LocKey.ToString"/> when no table has a row for it.
    /// <b>Never null and never a throw</b> (M3-14a rule 1): a screen showing nothing is a bug that
    /// looks like a broken layout, where a screen showing <c>skill.oathbound.consecrate.name</c> is
    /// the behaviour M3 has had since M3-08b and diagnoses itself. It is also what makes a
    /// half-translated table the normal state rather than an error.
    /// </returns>
    string Get(LocKey key);

    /// <summary>
    /// The string for <paramref name="key"/> with <paramref name="args"/> substituted into it, in
    /// the reader's number format.
    /// </summary>
    /// <param name="key">What to look up. <c>default</c> is legal and answers empty.</param>
    /// <param name="args">
    /// What the row's <c>{0}</c>, <c>{1}</c>… stand for. A null array is no arguments.
    /// </param>
    /// <remarks>
    /// <b>A second member rather than an overload of <see cref="Get"/>, and AR §6's row is corrected
    /// to say so</b> (M6-10 rule 4). That row promised <em>"M6-10 adds the overload"</em>. An
    /// overload would put a <c>params object[]</c> allocation one accidental argument away from
    /// <c>HudPresenter</c> and <c>AutoCastRow</c>, which is the cost M3-14a rule 5 cut the
    /// <c>params</c> to avoid. <b>This allocates</b> — its array and its result — so it belongs on
    /// a tap or an open, never on a frame.
    /// </remarks>
    /// <returns>
    /// Never null and never a throw — <see cref="Get"/>'s contract. A row with the wrong number of
    /// placeholders returns the <b>unsubstituted</b> row rather than throwing, because a
    /// <c>FormatException</c> out of a translator's typo is a screen that crashes in one language
    /// and works in another. A key with no row returns the key's own text, unsubstituted.
    /// </returns>
    string Format(LocKey key, params object[] args);
}
