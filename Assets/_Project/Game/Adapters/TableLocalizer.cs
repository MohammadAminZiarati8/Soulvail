using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Authoring;

namespace Soulvail.Game.Adapters;

/// <summary>
/// <see cref="ILocalizer"/> over one <see cref="LocalizationTable"/>: one dictionary, built once at
/// boot. AR §6's named adapter, arriving with its port.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built once, at boot, and registered as <see cref="ILocalizer"/></b> (rule 4). The table is
/// converted in this constructor rather than on first use, so a duplicate or an empty key is a loud
/// failure at launch naming the asset — <c>ContentCatalog</c>'s bargain, for its reason. Nothing
/// depends on this concrete type, which is the <c>LocalJsonSaveStore</c> precedent.
/// </para>
/// <para>
/// <b><see cref="Get"/> answers <see cref="LocKey.ToString"/> on a miss, never
/// <see cref="LocKey.Key"/>, and the difference is one character and a crash.</b>
/// <see cref="LocKey.Key"/> is the raw string and is <see langword="null"/> for
/// <c>default(LocKey)</c>; <see cref="LocKey.ToString"/> is <c>Key ?? string.Empty</c>. A
/// <c>default</c> key is a perfectly legal dictionary probe — <c>GetHashCode</c> answers 0 for a
/// null key and <c>Equals</c> goes through an ordinal <c>string.Equals</c> — so it simply misses and
/// falls through to here. Returning <c>Key</c> would hand a <see langword="null"/> to a
/// <c>TMP_Text</c> and the <c>NullReferenceException</c> would surface three screens from the
/// unset field that caused it. Returning <c>ToString()</c> hands it an empty label, which is what an
/// unset key deserves.
/// </para>
/// <para>
/// <b>A hit allocates nothing</b> (rule 6): a dictionary probe returning a string the table already
/// holds — no concatenation, no <c>string.Format</c>, no <c>ToString</c> on the hit path. AR §14's
/// rule is about the frame rather than about core alone, and <c>AutoCastRow</c> and
/// <c>HudPresenter</c> both reach this from code that runs whenever a value changes. <b>A miss
/// allocates nothing either</b>, which is less obvious and is why it is measured:
/// <see cref="LocKey.ToString"/> returns the string the key already holds rather than building one,
/// so the whole of M6-10's half-translated normal state is free as well.
/// </para>
/// <para>
/// <b>The comparison is ordinal and therefore case-sensitive</b>, because
/// <see cref="LocKey.Equals"/> is. <c>Skill.Oathbound.Consecrate.Name</c> does not find
/// <c>skill.oathbound.consecrate.name</c>, and it should not: a key is an identifier and ADR-0010's
/// rule about content identity does not soften for text. The failure is visible — the key appears on
/// the screen — which is rule 1 diagnosing itself.
/// </para>
/// </remarks>
public sealed class TableLocalizer : ILocalizer
{
    private readonly IReadOnlyDictionary<LocKey, string> _rows;

    /// <param name="table">
    /// The language. Converted immediately and not retained, so an Inspector edit after boot
    /// changes nothing until the next launch — which is the honest behaviour for a table read once.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The table holds a duplicate or an unusable key. Raised by
    /// <see cref="LocalizationTable.ToDictionary"/>, which names the asset.
    /// </exception>
    public TableLocalizer(LocalizationTable table)
    {
        if (table == null)
        {
            throw new ArgumentNullException(
                nameof(table),
                "A localizer with no table would answer every screen in the game with its own key, "
                    + "which is the state this task exists to end. Drop English.asset onto "
                    + "BootScope's Localization field.");
        }

        _rows = table.ToDictionary();
    }

    /// <summary>How many rows this localizer answers from. M3-14b's other door.</summary>
    public int Count => _rows.Count;

    /// <inheritdoc />
    public string Get(LocKey key)
    {
        // The whole of the adapter. See the class remarks for why the fallback is ToString() and
        // not Key — the null case is reachable and silent.
        return _rows.TryGetValue(key, out string text) ? text : key.ToString();
    }

    /// <summary>
    /// Whether <paramref name="key"/> has a row at all.
    /// </summary>
    /// <remarks>
    /// <b>M3-14b's door, and nothing else calls it.</b> <see cref="Get"/> cannot answer this
    /// question — a key whose row is the key's own text is indistinguishable from a miss — so the
    /// task that sweeps every shipped asset for an unresolved key needs a way to ask. It is
    /// deliberately not used to validate anything here: asserting coverage in the task that wrote
    /// the table would be this task grading its own homework.
    /// </remarks>
    public bool Has(LocKey key) => _rows.ContainsKey(key);
}
