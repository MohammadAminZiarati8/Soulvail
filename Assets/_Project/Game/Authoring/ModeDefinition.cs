using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script
// importer parses a file to find the type it declares and does not understand `namespace X;`,
// so a ScriptableObject declared that way is never linked to a MonoScript: Descent.asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// One mode as a designer tunes it: the Inspector half of <see cref="ModeSpec"/>. Converted
    /// once at boot into the immutable spec core consumes and registered in the
    /// <c>ContentCatalog</c>. See AR §10.1 and ADR-0006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third definition type in the project, and deliberately the same shape as the first
    /// two: <c>[SerializeField] private</c> fields, one <see cref="ToSpec"/> that is the only way
    /// out, an <see cref="OnValidate"/> that checks the id, and every failure rewrapped as a plain
    /// <see cref="ArgumentException"/> naming the asset. A designer who has learned to read one
    /// Console message has learned to read all of them.
    /// </para>
    /// <para>
    /// <b>It validates nothing <see cref="ModeSpec"/> already validates.</b> The roster's
    /// one-introduction-per-stage rule, the duplicate check, the stage bounds — all of them live
    /// in the spec's constructor, which is the single account of what a legal mode is. A second
    /// copy here would be a second set of messages to keep in step, and the one that fired first
    /// would be the one nobody had updated.
    /// </para>
    /// <para>
    /// <b>Descent ships with the Husk alone.</b> GD §8.2's schedule is Husk 1, Spitter 2,
    /// Bloater 4 and that is what the mode's roster will say — but <c>RunSession.Start</c>
    /// resolves every roster id against the catalog before a run is announced, so a row naming an
    /// archetype nobody has authored yet would refuse to start the game. M2-06 adds the other two
    /// rows in the same change that authors them (M2-02 rule 10).
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Content/Mode", fileName = "Mode")]
    public sealed class ModeDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "mode.new";
        [SerializeField] private string _nameKey = "mode.new.name";

        [Tooltip("The depth a fresh run of this mode begins at. Stages are numbered from 1.")]
        [SerializeField, Min(1)] private int _startingStage = 1;

        [Tooltip("Whether the mode runs until the player dies. True for Descent (GD §4.5); the " +
                 "final stage below is ignored when this is on.")]
        [SerializeField] private bool _isEndless = true;

        [Tooltip("The last stage a finite mode has. Ignored while Is Endless is on.")]
        [SerializeField, Min(1)] private int _finalStage = 1;

        [Tooltip("Every archetype this mode may spawn, and the stage each is introduced at " +
                 "(GD §8.2). At most one introduction per stage, and each archetype once.")]
        [SerializeField] private RosterRow[] _roster = Array.Empty<RosterRow>();

        /// <summary>
        /// The authored id text, exactly as it sits in the asset — for grouping and diagnostics
        /// before conversion. It is <em>not</em> known to be well-formed: only a
        /// <see cref="ToSpec"/> that returned tells you that.
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// Builds the immutable spec core consumes. A fresh instance every call — this asset
        /// holds no runtime state and hands out nothing it keeps a reference to.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Any authored field is invalid. Always this exact type, never one of its subclasses:
        /// the caller cannot act on <em>which</em> field failed, only on <em>which asset</em>
        /// failed, and that is what the message leads with. The original is kept as the inner
        /// exception, so the field and its value survive into the log.
        /// </exception>
        public ModeSpec ToSpec()
        {
            try
            {
                return new ModeSpec(
                    new ContentId(_id),
                    new LocKey(_nameKey),
                    _startingStage,
                    _isEndless,
                    _finalStage,
                    BuildRoster());
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching
                // here. Uncaught, a designer reading the Console sees "introduces two archetypes
                // at stage 2" with a stack trace through the boot installer and no way to tell
                // which of the catalog's assets to open.
                throw new ArgumentException(
                    $"ModeDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }

        /// <summary>
        /// Turns the authored rows into <see cref="RosterEntry"/>s, in the order they were
        /// authored.
        /// </summary>
        /// <remarks>
        /// A null array is treated as an empty one. Unity never serialises one, but a freshly
        /// <c>CreateInstance</c>d asset in a test is a real path here, and an empty roster is a
        /// legal mode (see <see cref="ModeSpec"/>) — so this is not somewhere to invent a
        /// failure the spec does not have.
        /// </remarks>
        private IReadOnlyList<RosterEntry> BuildRoster()
        {
            if (_roster is null || _roster.Length == 0)
            {
                return Array.Empty<RosterEntry>();
            }

            var entries = new RosterEntry[_roster.Length];

            for (int i = 0; i < _roster.Length; i++)
            {
                entries[i] = _roster[i].ToEntry();
            }

            return entries;
        }

        /// <remarks>
        /// Only the id, and only its shape — the same bargain <c>CharacterDefinition</c> and
        /// <c>EnemyDefinition</c> make. A bad stage number is visibly a bad stage number in the
        /// Inspector, while <c>Mode.Descent</c> looks perfectly reasonable and fails at boot. The
        /// asset is passed as the log context so clicking the warning selects it.
        /// </remarks>
        private void OnValidate()
        {
            if (!ContentId.IsValid(_id))
            {
                Debug.LogWarning(
                    $"ModeDefinition '{name}': '{_id}' is not a valid content id. Expected " +
                    "lowercase dot-separated segments, at least two, e.g. 'mode.descent'.",
                    this);
            }
        }

        /// <summary>
        /// One authored roster row: an archetype id and the stage it is introduced at.
        /// </summary>
        /// <remarks>
        /// A serializable struct rather than two parallel arrays, so a designer editing the
        /// schedule sees the id and its stage side by side and cannot get them out of step. It
        /// carries the id as a <see cref="string"/> because <see cref="ContentId"/> validates in
        /// its constructor and Unity's serialiser does not call one — the conversion, and the
        /// failure, belong in <see cref="ToEntry"/>.
        /// </remarks>
        [Serializable]
        private struct RosterRow
        {
            [SerializeField] private string _specId;

            [Tooltip("The first stage this mode may spawn it at. 1 = from the first stage.")]
            [SerializeField, Min(1)] private int _introducedAtStage;

            /// <summary>Converts this row, letting <see cref="RosterEntry"/> refuse a bad one.</summary>
            public RosterEntry ToEntry() =>
                new RosterEntry(new ContentId(_specId), _introducedAtStage);
        }
    }
}
