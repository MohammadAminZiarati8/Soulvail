using System;
using System.Collections.Generic;
using System.Globalization;
using Soulvail.Core.Content;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and English.asset's reference to this ScriptableObject
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// One language, as rows of key → English. The asset side of ADR-0012, and the only place in
    /// the project where a user-facing sentence is written down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An Inspector array, and that is enough for one language</b> (rule 2). A translator's
    /// workflow, a CSV importer and <c>.po</c> files belong to the first real second language, and
    /// M6-10 shipped none: its only other table is the pseudo-locale, which is generated rather
    /// than typed. A couple of hundred rows are edited perfectly well in a list. What this shape buys now is that
    /// rewriting a description costs a keystroke rather than a recompile — which is exactly what
    /// GD §13.1's two-second rule needs, because the first draft of twelve descriptions will be
    /// wrong and the fix is a text edit.
    /// </para>
    /// <para>
    /// <b>The conversion refuses a duplicate key, with <c>ContentCatalog</c>'s message and its
    /// reason</b> (rule 3). A duplicate silently picks one row and <em>which</em> one depends on
    /// authoring order, which is the same failure <c>ContentCatalog</c> has refused since M0-08 and
    /// the same fix. An empty or whitespace key is refused for the reason <see cref="LocKey"/>'s own
    /// constructor refuses one — a stray newline in an authored field would otherwise become a key
    /// that mysteriously never resolves.
    /// </para>
    /// <para>
    /// <b>An empty <em>text</em> is legal, and that asymmetry is the point.</b> A row with a key and
    /// nothing beside it is an untranslated row, which is the normal state of every table M6-10 adds
    /// and has to be authorable here too. A row with no <em>key</em> is a typo.
    /// </para>
    /// <para>
    /// This class is never read at runtime: <c>TableLocalizer</c> converts it once at boot and the
    /// dictionary is what every screen asks. <c>ContentCatalog</c>'s bargain, for the same reason —
    /// a broken asset is a loud failure at boot naming the file rather than a missing word on a
    /// screen three scenes later.
    /// </para>
    /// <para>
    /// <b>A table says which language it is</b> (M6-10). <see cref="Locale"/> is a BCP-47 tag, and
    /// <b>English's is empty</b>, because English is the fallback every other table drops to rather
    /// than one language among several (rule 1). <c>TableLocalizer</c> refuses a set with no empty
    /// tag or with two of them.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        menuName = "Soulvail/Localization Table",
        fileName = "English")]
    public sealed class LocalizationTable : ScriptableObject
    {
        [Tooltip("Which language this is, as a BCP-47 tag — 'de', 'pt-BR', 'qps-ploc'. Empty for " +
                 "English, which is the fallback every other table drops to (M6-10 rule 1).")]
        [SerializeField] private string _locale = string.Empty;

        [Tooltip("Every string in the game, one row each. The key is a LocKey — no whitespace, " +
                 "and unique within this asset. An empty text is a legal untranslated row.")]
        [SerializeField] private LocalizationRow[] _rows = Array.Empty<LocalizationRow>();

        /// <summary>A BCP-47 tag — <c>"de"</c>, <c>"qps-ploc"</c> — or empty for the fallback.</summary>
        /// <remarks>
        /// Never null: an asset authored before this field existed deserialises it as null, and
        /// null and empty mean the same thing here.
        /// </remarks>
        public string Locale => _locale ?? string.Empty;

        /// <summary>
        /// What this language formats numbers with, resolved from <see cref="Locale"/>, and
        /// <see cref="CultureInfo.InvariantCulture"/> when the tag names no culture this runtime
        /// knows.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Unknown is invariant rather than a throw</b>, and the case is real rather than
        /// hypothetical: Unity's Mono has no culture for <c>qps-ploc</c>, the pseudo-locale this
        /// project ships, and a translator's table for a tag the runtime lacks should still draw its
        /// words. The fallback's empty tag resolves to the invariant culture too, which writes
        /// <c>3.4</c> — what every screen has drawn since M3.
        /// </para>
        /// <para>
        /// Resolved on each read rather than cached here. <c>TableLocalizer</c> reads it once at
        /// construction and keeps what it got, and a cache on the asset would go stale when somebody
        /// edited the tag in the Inspector.
        /// </para>
        /// </remarks>
        public CultureInfo Culture
        {
            get
            {
                string locale = Locale;

                if (locale.Length == 0)
                {
                    return CultureInfo.InvariantCulture;
                }

                try
                {
                    return CultureInfo.GetCultureInfo(locale);
                }
                catch (CultureNotFoundException)
                {
                    return CultureInfo.InvariantCulture;
                }
            }
        }

        /// <summary>How many rows the asset holds, before any of them is validated.</summary>
        /// <remarks>
        /// The raw count, so a probe can compare it against <c>TableLocalizer.Count</c> and see the
        /// two agree — which is the only way to notice a row that was dropped by the conversion
        /// rather than refused by it.
        /// </remarks>
        public int RowCount => _rows is null ? 0 : _rows.Length;

        /// <summary>
        /// Every row, checked. Throws naming this asset on a duplicate or an empty key.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Two rows share a key, or a row's key is null, empty or whitespace. The message names this
        /// asset, because the thing the reader has to open is the file rather than the code.
        /// </exception>
        /// <remarks>
        /// Built fresh each call rather than cached. It is called once per app launch, by
        /// <c>TableLocalizer</c>'s constructor, and a cache here would be a second copy of the truth
        /// that an Inspector edit could leave stale.
        /// </remarks>
        public IReadOnlyDictionary<LocKey, string> ToDictionary()
        {
            var byKey = new Dictionary<LocKey, string>(RowCount);

            for (int i = 0; i < RowCount; i++)
            {
                LocalizationRow row = _rows[i];

                if (row is null)
                {
                    throw new ArgumentException(
                        $"Row {i} of '{name}' is an empty slot. Every localisation row needs a key.");
                }

                // Constructed rather than validated by hand, so this asset refuses exactly what a
                // LocKey refuses and the two can never drift apart. The message is rewrapped to name
                // the file, because LocKey's own names only the string.
                LocKey key;

                try
                {
                    key = new LocKey(row.Key);
                }
                catch (ArgumentException exception)
                {
                    throw new ArgumentException(
                        $"Row {i} of '{name}' has no usable key. {exception.Message}");
                }

                if (byKey.ContainsKey(key))
                {
                    throw new ArgumentException(
                        $"Duplicate key '{key}' in '{name}'. Localisation keys must be unique.");
                }

                // Never null: an unauthored text field deserialises as null on a fresh row, and a
                // null in the dictionary would reach a TMP_Text as a null assignment rather than as
                // the empty string an untranslated row is supposed to be.
                byKey.Add(key, row.Text ?? string.Empty);
            }

            return byKey;
        }
    }

    /// <summary>
    /// One key and its words.
    /// </summary>
    /// <remarks>
    /// A <c>[Serializable]</c> class at top level rather than a nested struct, which is
    /// <c>SkillTreeDefinition.BranchField</c>'s shape and its reason: Unity serialises a nested type
    /// inconsistently across versions, and a class is what every other authoring row in this project
    /// already is. <c>[SerializeField] private</c> with read-only properties rather than public
    /// fields, per the project's conventions — the Inspector does not care and the compiler does.
    /// </remarks>
    [Serializable]
    public sealed class LocalizationRow
    {
        [Tooltip("The LocKey this row answers — 'skill.oathbound.consecrate.name'. No whitespace, " +
                 "and unique within the asset.")]
        [SerializeField] private string _key = string.Empty;

        [Tooltip("What the player reads. GD §13.1: a node description that needs two lines is a " +
                 "node to redesign, so keep it to one. Empty is a legal untranslated row.")]
        [TextArea(1, 4)]
        [SerializeField] private string _text = string.Empty;

        /// <summary>The raw authored key, not yet known to be a well-formed <see cref="LocKey"/>.</summary>
        public string Key => _key;

        /// <summary>The words. May be empty; never meaningful to a player if it is.</summary>
        public string Text => _text;
    }
}
