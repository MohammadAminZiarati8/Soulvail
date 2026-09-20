using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script
// importer parses a file to find the type it declares and does not understand `namespace X;`,
// so a ScriptableObject declared that way is never linked to a MonoScript: WardenBoss.asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// One boss as a designer tunes it: the Inspector half of <see cref="BossSpec"/>. Converted
    /// once at boot into the immutable spec core consumes and registered in the
    /// <c>ContentCatalog</c>. See AR §10.1, ADR-0006 and GD §9.1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sixth definition type in the project, and deliberately the same shape as the five
    /// before it: <c>[SerializeField] private</c> fields, one <see cref="ToSpec"/> that is the
    /// only way out, an <see cref="OnValidate"/> that checks the id, and every failure rewrapped
    /// as a plain <see cref="ArgumentException"/> naming the asset.
    /// </para>
    /// <para>
    /// <b>It ships with M4-02 rather than with M4-01b, which built everything it authors
    /// against.</b> An authoring type with no asset is a guess about how content will be written
    /// (AR §6), and the type belongs in the change that authors the first one — the same bargain
    /// <c>EnemyDefinition</c>'s Spitter row made between M2-06 and M2-07b.
    /// </para>
    /// <para>
    /// <b>It names an enemy archetype rather than restating a body</b> (M4-01b rule 2). A boss's
    /// hit points, contact damage, reach, telegraph and experience are authored on an ordinary
    /// <c>EnemyDefinition</c> — <c>Warden.asset</c> — like every other creature in the game, and
    /// this asset adds only what is true of a <em>boss</em>: that it has phases. Which stages hold
    /// one is not here either; that is the mode's statement, on <c>ModeDefinition</c>'s boss
    /// roster (GD §4.5).
    /// </para>
    /// <para>
    /// <b>It validates nothing <see cref="BossSpec"/> already validates.</b> The first phase
    /// beginning at full health, the strictly descending thresholds, a summon count below one, a
    /// beat that is not a finite positive number — all of them live in the spec's constructors,
    /// which are the single account of what a legal boss is. A second copy here would be a second
    /// set of messages to keep in step, and the one that fired first would be the one nobody had
    /// updated.
    /// </para>
    /// <para>
    /// <b>The defaults below are GD §9.2's Warden rather than neutral zeroes</b>, for
    /// <c>EnemyDefinition</c>'s reason: the fields <see cref="BossSpec"/> refuses when empty — a
    /// phase list with nothing in it, a zero beat — would otherwise make every freshly created
    /// asset invalid content until all of them were filled in. A new boss starts from the Warden
    /// and is edited into whatever it is.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Content/Boss", fileName = "Boss")]
    public sealed class BossDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "boss.new";

        [Tooltip("The EnemyDefinition this boss's body, health and contact damage come from " +
                 "(ADR-0006). A boss wears an ordinary archetype; it does not restate one.")]
        [SerializeField] private string _enemySpecId = "enemy.new";

        [Tooltip("Seconds the invulnerable beat between two phases lasts — GD §9.1 rule 3. One " +
                 "number for every transition: the beat is a property of the change rather than " +
                 "of the phase being entered.")]
        [SerializeField, Min(0.01f)] private float _beatSeconds = 1.5f;

        [Tooltip("Its phases, outermost first. The first must enter at 1 — a boss is in its " +
                 "opening phase from full health — and each one after it at a strictly lower " +
                 "fraction. GD §9.1 rule 3 ships 1, 0.66 and 0.33.")]
        [SerializeField] private PhaseRow[] _phases = Array.Empty<PhaseRow>();

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
        public BossSpec ToSpec()
        {
            try
            {
                return new BossSpec(
                    new ContentId(_id),
                    new ContentId(_enemySpecId),
                    BuildPhases(),
                    _beatSeconds);
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching
                // here. Uncaught, a designer reading the Console sees "phases are out of order"
                // with a stack trace through the boot installer and no way to tell which of the
                // catalog's assets to open.
                throw new ArgumentException(
                    $"BossDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }

        /// <summary>
        /// Turns the authored rows into <see cref="BossPhaseSpec"/>s, in the order authored.
        /// </summary>
        /// <remarks>
        /// A null array is treated as an empty one, which <see cref="BossSpec"/> then refuses with
        /// its own message. Unity never serialises a null, but a freshly <c>CreateInstance</c>d
        /// asset in a test is a real path here — and this is not somewhere to invent a second
        /// failure for a case the spec already names better than this method could.
        /// </remarks>
        private IReadOnlyList<BossPhaseSpec> BuildPhases()
        {
            if (_phases is null || _phases.Length == 0)
            {
                return Array.Empty<BossPhaseSpec>();
            }

            var phases = new BossPhaseSpec[_phases.Length];

            for (int i = 0; i < _phases.Length; i++)
            {
                phases[i] = _phases[i].ToSpec();
            }

            return phases;
        }

        /// <remarks>
        /// Only the ids, and only their shape — the same bargain every other definition makes. A
        /// bad threshold is visibly a bad threshold in the Inspector, while <c>Boss.Warden</c>
        /// looks perfectly reasonable and fails at boot. The asset is passed as the log context so
        /// clicking the warning selects it.
        /// </remarks>
        private void OnValidate()
        {
            if (!ContentId.IsValid(_id))
            {
                Debug.LogWarning(
                    $"BossDefinition '{name}': '{_id}' is not a valid content id. Expected " +
                    "lowercase dot-separated segments, at least two, e.g. 'boss.warden'.",
                    this);
            }

            if (!ContentId.IsValid(_enemySpecId))
            {
                Debug.LogWarning(
                    $"BossDefinition '{name}': '{_enemySpecId}' is not a valid enemy id. A boss " +
                    "wears an ordinary archetype, e.g. 'enemy.warden'.",
                    this);
            }
        }

        /// <summary>
        /// One authored phase: the health fraction it begins at, and what entering it calls in.
        /// </summary>
        /// <remarks>
        /// A <c>[Serializable]</c> <b>class</b> rather than a struct, unlike
        /// <c>ModeDefinition.RosterRow</c>: it holds an array of its own, and it renders as one
        /// foldout, which is what stops three phases' worth of summon rows from being one flat
        /// list a designer has to count through.
        /// </remarks>
        [Serializable]
        private sealed class PhaseRow
        {
            [Tooltip("Health fraction at or under which this phase begins, in (0, 1]. 1 for the " +
                     "opening phase; GD §9.1 rule 3's others are 0.66 and 0.33.")]
            [SerializeField, Range(0.01f, 1f)] private float _entersBelow = 1f;

            [Tooltip("What entering this phase summons (GD §9.1 rule 4). Leave empty for a phase " +
                     "that calls in nothing — a row of zero is refused rather than ignored.")]
            [SerializeField] private SummonRow[] _summons = Array.Empty<SummonRow>();

            /// <summary>Converts this row, letting the spec refuse a bad one.</summary>
            public BossPhaseSpec ToSpec()
            {
                if (_summons is null || _summons.Length == 0)
                {
                    return new BossPhaseSpec(_entersBelow);
                }

                var waves = new AddWave[_summons.Length];

                for (int i = 0; i < _summons.Length; i++)
                {
                    waves[i] = _summons[i].ToWave();
                }

                return new BossPhaseSpec(_entersBelow, waves);
            }
        }

        /// <summary>
        /// One authored summon: an archetype and how many of it.
        /// </summary>
        /// <remarks>
        /// A serializable struct, <c>ModeDefinition.RosterRow</c>'s shape and its reasons — the id
        /// and its count side by side so they cannot get out of step, and carried as a
        /// <see cref="string"/> because <see cref="ContentId"/> validates in a constructor Unity's
        /// serialiser never calls.
        /// </remarks>
        [Serializable]
        private struct SummonRow
        {
            [SerializeField] private string _specId;

            [Tooltip("How many. At least 1 — a phase that summons nothing authors no row at all.")]
            [SerializeField, Min(1)] private int _count;

            /// <summary>Converts this row, letting <see cref="AddWave"/> refuse a bad one.</summary>
            public AddWave ToWave() => new AddWave(new ContentId(_specId), _count);
        }
    }
}
