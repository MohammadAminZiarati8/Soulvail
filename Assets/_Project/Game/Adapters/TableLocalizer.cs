using System;
using System.Collections.Generic;
using System.Globalization;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Authoring;

namespace Soulvail.Game.Adapters;

/// <summary>
/// <see cref="ILocalizer"/> over every shipped <see cref="LocalizationTable"/>: one dictionary per
/// language, built once at boot, one of them read, and English behind it. AR §6's named adapter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built once, at boot, and registered as <see cref="ILocalizer"/></b> (M3-14a rule 4). Every
/// table is converted in this constructor rather than on first use, so a duplicate or an empty key
/// is a loud failure at launch naming the asset — <c>ContentCatalog</c>'s bargain, for its reason.
/// <b>One caller depends on the concrete type</b>, and it is <c>BootFlow</c>, which is the only
/// thing allowed to call <see cref="SetLocale"/> (M6-10 rule 6). Every screen asks the port.
/// </para>
/// <para>
/// <b>The fallback chain is two deep and no deeper</b> (M6-10 rule 1). A miss in the current table
/// falls through to the fallback, the one table whose locale is empty, and a miss there answers
/// <see cref="LocKey.ToString"/>. <b>There is no language-then-region step</b>: <c>de-AT</c> does
/// not find <c>de</c>. No shipped table needs one, and adding it later is one loop in
/// <see cref="Pick"/>. A tag is compared ignoring case, because BCP-47 tags are case-insensitive
/// and <c>de-de</c> is the same language as <c>de-DE</c>. That is not a region step.
/// </para>
/// <para>
/// <b>A miss answers <see cref="LocKey.ToString"/>, never <see cref="LocKey.Key"/>, and the
/// difference is one character and a crash.</b> <see cref="LocKey.Key"/> is the raw string and is
/// <see langword="null"/> for <c>default(LocKey)</c>; <see cref="LocKey.ToString"/> is
/// <c>Key ?? string.Empty</c>. A <c>default</c> key is a legal dictionary probe that simply misses.
/// Returning <c>Key</c> would hand a <see langword="null"/> to a <c>TMP_Text</c>, and the
/// <c>NullReferenceException</c> would surface three screens from the unset field that caused it.
/// </para>
/// <para>
/// <b><see cref="Get"/> allocates nothing, on a hit, on a fallback hit, or on a miss</b> (M6-10
/// rule 9). It is at most two dictionary probes returning a string a table already holds, and
/// <c>AutoCastRow</c> and <c>HudPresenter</c> reach it from code that runs whenever a value
/// changes. <see cref="Format"/> allocates its array and its result, which is why it is a
/// different member rather than an overload (rule 4).
/// </para>
/// <para>
/// <b>The comparison of keys is ordinal and therefore case-sensitive</b>, because
/// <see cref="LocKey.Equals"/> is. A key is an identifier, and ADR-0010's rule about content
/// identity does not soften for text. The failure is visible: the key appears on the screen.
/// </para>
/// </remarks>
public sealed class TableLocalizer : ILocalizer
{
    private readonly Language[] _languages;
    private readonly Language _fallback;
    private Language _current;

    /// <summary>A localizer over one table, which has to be the fallback.</summary>
    /// <param name="table">
    /// The one language, with an empty locale. Converted immediately and not retained, so an
    /// Inspector edit after boot changes nothing until the next launch.
    /// </param>
    /// <remarks>
    /// <b>Kept beside the list form rather than replaced by it.</b> Forty-odd fixtures build a
    /// localizer over one table — an empty one to draw keys, or <c>English.asset</c> to draw words —
    /// and a one-element array at every one of them would say nothing the list form does not.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="table"/> names a locale, so there is no fallback; or it holds a duplicate or an
    /// unusable key, which <see cref="LocalizationTable.ToDictionary"/> reports naming the asset.
    /// </exception>
    public TableLocalizer(LocalizationTable table)
        : this(Single(table), string.Empty)
    {
    }

    /// <param name="tables">
    /// Every shipped table, in no particular order. <b>Exactly one must have an empty locale</b>;
    /// that one is the fallback (M6-10 rule 1). Converted immediately and not retained.
    /// </param>
    /// <param name="locale">
    /// Which to read, or empty for the fallback. A tag no table carries reads the fallback, silently
    /// (rule 7).
    /// </param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="tables"/> is empty, holds an empty slot, has no fallback, or has two tables
    /// carrying one locale — two fallbacks included. Each message names the assets.
    /// </exception>
    public TableLocalizer(IReadOnlyList<LocalizationTable> tables, string locale)
    {
        if (tables is null)
        {
            throw new ArgumentNullException(
                nameof(tables),
                "A localizer with no tables would answer every screen in the game with its own key. "
                    + "BootScope's Localization field is where English.asset goes.");
        }

        if (locale is null)
        {
            throw new ArgumentNullException(
                nameof(locale),
                "locale must not be null. Empty means the fallback; null means nobody said.");
        }

        if (tables.Count == 0)
        {
            throw new ArgumentException(
                "A localizer needs at least one table, and one of them has to be the fallback — "
                    + "English.asset, with an empty locale.",
                nameof(tables));
        }

        _languages = new Language[tables.Count];

        for (int i = 0; i < tables.Count; i++)
        {
            LocalizationTable table = tables[i];

            // Unity's operator rather than `is null`: a slot left empty in the Inspector is a live
            // reference only the engine calls null — BootInstaller.Convert's reason.
            if (table == null)
            {
                throw new ArgumentException(
                    $"tables[{i}] is an empty slot. Every entry in the language list must reference "
                        + "a LocalizationTable asset.",
                    nameof(tables));
            }

            string tag = table.Locale;

            for (int j = 0; j < i; j++)
            {
                if (!string.Equals(_languages[j].Locale, tag, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // "Which English do we show" has no answer, and a silently picked one depends on the
                // order the tables were dragged onto a prefab. Refused for any locale, not only for
                // the empty one: two German tables are the same question.
                throw new ArgumentException(
                    tag.Length == 0
                        ? $"'{_languages[j].Name}' and '{table.name}' both have an empty locale, so both "
                            + "claim to be the fallback. Exactly one table is English; give the other "
                            + "its BCP-47 tag."
                        : $"'{_languages[j].Name}' and '{table.name}' are both '{tag}'. One language, one "
                            + "table.",
                    nameof(tables));
            }

            _languages[i] = new Language(table.name, tag, table.Culture, table.ToDictionary());

            if (tag.Length == 0)
            {
                _fallback = _languages[i];
            }
        }

        if (_fallback is null)
        {
            throw new ArgumentException(
                $"None of the {tables.Count} table(s) has an empty locale, so there is no fallback "
                    + "and a key a translation lacks would draw as itself. English.asset is the "
                    + "fallback, and its locale is empty (M6-10 rule 1).",
                nameof(tables));
        }

        _current = Pick(locale);
    }

    /// <summary>Which table is being read, as its tag. Empty means the fallback.</summary>
    /// <remarks>
    /// The table <em>read</em>, not the tag asked for. A request for a language no table carries
    /// reads English and reports empty here (rule 7).
    /// </remarks>
    public string Locale => _current.Locale;

    /// <summary>
    /// What a number is formatted with: the current table's culture, never simply the invariant one
    /// (rule 5).
    /// </summary>
    /// <remarks>
    /// The <em>reader's</em> culture, and it stays the reader's when a word falls back a rung: a
    /// German player reading an English row still reads <c>3,4</c>.
    /// </remarks>
    public CultureInfo Culture => _current.Culture;

    /// <summary>How many rows the current table holds. M3-14b's other door.</summary>
    public int Count => _current.Rows.Count;

    /// <summary>
    /// Switches tables. <b>Legal only before the first screen draws</b> (M6-10 rule 6).
    /// </summary>
    /// <param name="locale">The tag to read, or empty for the fallback. Unknown reads the fallback.</param>
    /// <exception cref="ArgumentNullException"><paramref name="locale"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Nothing redraws.</b> Every label in this project is written in <c>Start</c> (M3-14c
    /// rule 3), so a locale changed after the Menu scene loads leaves a screen in two languages.
    /// <b>M8-02's options row has to solve that</b>: it ships the picker, so it needs a redraw.
    /// <c>BootFlow</c> is the only caller, before the Menu exists, and
    /// <c>Boot_OnlyBootFlowSetsTheLocale</c> is the row that says so.
    /// </para>
    /// <para>
    /// A tag this build does not ship reads English and says nothing, which is
    /// <c>SkillRunner.Restore</c>'s rule for an id the build no longer stocks (rule 7).
    /// </para>
    /// </remarks>
    public void SetLocale(string locale)
    {
        if (locale is null)
        {
            throw new ArgumentNullException(
                nameof(locale),
                "locale must not be null. Empty means the fallback; null means nobody said.");
        }

        _current = Pick(locale);
    }

    /// <inheritdoc />
    public string Get(LocKey key)
    {
        return TryRow(key, out string text) ? text : key.ToString();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>The reader's culture, the row's word order.</b> Before M6-10 a number in a sentence was
    /// formatted beside the key, in the invariant culture, by the screen. That is two defects in a
    /// translated build, and neither shows in English: a sentence that puts its number somewhere
    /// else cannot move it, and a German reader expects <c>3,4</c> where the invariant culture
    /// writes <c>3.4</c>. M3-09b rule 2 survives: core still says what <em>kind</em> of number it
    /// is, and the screen still decides; what changed is that the screen asks this to write it.
    /// </para>
    /// <para>
    /// <b>A <see cref="FormatException"/> is caught and the row returned whole.</b> A row with
    /// <c>{1}</c> and one argument is a translator's typo, and a typo that crashes one language's
    /// screen and not another's is the worst kind to find on a device.
    /// </para>
    /// </remarks>
    public string Format(LocKey key, params object[] args)
    {
        if (!TryRow(key, out string row))
        {
            return key.ToString();
        }

        try
        {
            return string.Format(_current.Culture, row, args ?? Array.Empty<object>());
        }
        catch (FormatException)
        {
            return row;
        }
    }

    /// <summary>
    /// Whether the <b>current</b> table has a row for <paramref name="key"/>. The fallback is not
    /// asked.
    /// </summary>
    /// <remarks>
    /// <b>M3-14b's door, and unchanged in meaning.</b> <see cref="Get"/> cannot answer it: a key
    /// whose row is the key's own text is indistinguishable from a miss. It asks the table being read
    /// rather than the chain, so a sweep over a translation finds the rows that translation lacks
    /// rather than being told English has them.
    /// </remarks>
    public bool Has(LocKey key) => _current.Rows.ContainsKey(key);

    /// <summary>The row, from the current table and then from the fallback — two deep, no deeper.</summary>
    private bool TryRow(LocKey key, out string text)
    {
        if (_current.Rows.TryGetValue(key, out text))
        {
            return true;
        }

        return !ReferenceEquals(_current, _fallback) && _fallback.Rows.TryGetValue(key, out text);
    }

    /// <summary>The table carrying <paramref name="locale"/>, or the fallback.</summary>
    private Language Pick(string locale)
    {
        for (int i = 0; i < _languages.Length; i++)
        {
            if (string.Equals(_languages[i].Locale, locale, StringComparison.OrdinalIgnoreCase))
            {
                return _languages[i];
            }
        }

        return _fallback;
    }

    private static LocalizationTable[] Single(LocalizationTable table)
    {
        if (table == null)
        {
            throw new ArgumentNullException(
                nameof(table),
                "A localizer with no table would answer every screen in the game with its own key, "
                    + "which is the state M3-14a exists to end. Drop English.asset onto "
                    + "BootScope's Localization field.");
        }

        return new[] { table };
    }

    /// <summary>One converted table: its tag, its culture and its rows.</summary>
    private sealed class Language
    {
        internal Language(
            string name, string locale, CultureInfo culture, IReadOnlyDictionary<LocKey, string> rows)
        {
            Name = name;
            Locale = locale;
            Culture = culture;
            Rows = rows;
        }

        /// <summary>The asset's name, for the messages that have to say which file to open.</summary>
        internal string Name { get; }

        internal string Locale { get; }

        internal CultureInfo Culture { get; }

        internal IReadOnlyDictionary<LocKey, string> Rows { get; }
    }
}
