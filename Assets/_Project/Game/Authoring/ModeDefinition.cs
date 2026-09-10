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

        [Tooltip("GD §12's difficulty curves for this mode: what a stage may cost, how it is " +
                 "paced, and how much depth adds to each enemy.")]
        [SerializeField] private ScalingBlock _scaling = new ScalingBlock();

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
                    BuildScaling(),
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
        /// Turns the authored curve block into the <see cref="ScalingSpec"/> core consumes.
        /// </summary>
        /// <remarks>
        /// A missing block is refused rather than defaulted, unlike a missing roster. Unity never
        /// serialises a null nested object, so the only ways here are a hand-edited asset or a
        /// <c>SerializedProperty</c> write — and a mode with no difficulty model is not an empty
        /// mode, it is one whose stages cost nothing and whose enemies never get harder. The
        /// message says which asset, like every other failure this type produces.
        /// </remarks>
        private ScalingSpec BuildScaling()
        {
            if (_scaling is null)
            {
                throw new ArgumentException(
                    "its scaling block is missing. GD §12's curves are not optional — a mode "
                        + "without them affords nothing at every depth.",
                    nameof(_scaling));
            }

            return _scaling.ToSpec();
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
        /// GD §12's five curves as a designer tunes them. The Inspector half of
        /// <see cref="ScalingSpec"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A <c>[Serializable]</c> <b>class</b> rather than a struct, unlike
        /// <see cref="RosterRow"/>: it carries field initialisers, and a struct's are not
        /// something Unity's serialiser can be relied on to have run. It also draws as one
        /// foldout in the Inspector, which is what stops twenty-one loose numbers from filling a
        /// mode's whole inspector above its roster.
        /// </para>
        /// <para>
        /// <b>The initialisers are GD §12's own numbers</b>, so a mode created from the Create
        /// menu is playable rather than invalid. That does mean a test asserting "the asset carries
        /// these values" cannot tell a bound YAML key from a dropped one — Traps §7 — which is
        /// exactly what <c>ModeDefinitionTests.Descent_EveryYamlKeyBindsToAField</c> exists to
        /// cover, and it covers this block along with everything else.
        /// </para>
        /// <para>
        /// It validates nothing the curves already validate. Every bound is in
        /// <see cref="ScalingSpec"/>'s constructors, which are the single account of what a legal
        /// curve is; <c>[Min]</c> here only clamps the Inspector GUI (Traps §5).
        /// </para>
        /// </remarks>
        [Serializable]
        private sealed class ScalingBlock
        {
            [Header("Threat budget — GD §12.1: B(n) = base + linear·(n−1) + quadratic·(n−1)²")]
            [Tooltip("The stage-1 budget.")]
            [SerializeField, Min(0.01f)] private float _budgetBase = 40f;

            [Tooltip("Added per stage past the first.")]
            [SerializeField, Min(0f)] private float _budgetLinear = 12f;

            [Tooltip("Added per stage-past-the-first squared. This is what makes deep stages bite.")]
            [SerializeField, Min(0f)] private float _budgetQuadratic = 0.9f;

            [Header("Pacing — GD §12.2")]
            [Tooltip("Waves at stage 0, before any step.")]
            [SerializeField, Min(1)] private int _waveBase = 2;

            [Tooltip("Stages per extra wave.")]
            [SerializeField, Min(1)] private int _waveStagesPerStep = 5;

            [Tooltip("Fewest waves a stage may have.")]
            [SerializeField, Min(1)] private int _waveMin = 2;

            [Tooltip("Most waves a stage may have.")]
            [SerializeField, Min(1)] private int _waveMax = 5;

            [Tooltip("Concurrent enemies at stage 0, before any step.")]
            [SerializeField, Min(1)] private int _concurrencyBase = 10;

            [Tooltip("Stages per extra concurrent enemy. The device cap is not authored here — " +
                     "it is a property of the phone (GD §11.1), chosen in code.")]
            [SerializeField, Min(1)] private int _concurrencyStagesPerStep = 2;

            [Header("Stat scaling — GD §12.3, deliberately shallow")]
            [Tooltip("h(n): the hit-point multiplier. GD ships +0.06 a stage, capped at 4.0.")]
            [SerializeField] private StatCurveRow _hp = new StatCurveRow(0.06f, 4f, 1, 1);

            [Tooltip("d(n): the damage multiplier. GD ships +0.035 a stage, capped at 3.0. " +
                     "GD §12.4's one-shot rule is checked against this at M2-15.")]
            [SerializeField] private StatCurveRow _damage = new StatCurveRow(0.035f, 3f, 1, 1);

            [Tooltip("s(n): the move-speed multiplier. GD ships +0.02 every fifth stage, capped " +
                     "at 1.3 — a staircase rather than a ramp, and the step is meant to be felt.")]
            [SerializeField] private StatCurveRow _speed = new StatCurveRow(0.02f, 1.3f, 5, 0);

            /// <summary>Builds the immutable spec, letting the curves refuse a bad number.</summary>
            public ScalingSpec ToSpec() => new ScalingSpec(
                new BudgetCurve(_budgetBase, _budgetLinear, _budgetQuadratic),
                new WaveCurve(_waveBase, _waveStagesPerStep, _waveMin, _waveMax),
                new ConcurrencyCurve(_concurrencyBase, _concurrencyStagesPerStep),
                _hp.ToCurve(),
                _damage.ToCurve(),
                _speed.ToCurve());
        }

        /// <summary>
        /// One of GD §12.3's three stat multipliers as a designer tunes it.
        /// </summary>
        /// <remarks>
        /// A <c>[Serializable]</c> class for <see cref="ScalingBlock"/>'s reason — the four
        /// numbers have initialisers, which differ per curve — and one type for all three because
        /// GD §12.3's formulas are two spellings of one arithmetic, which is the argument
        /// <see cref="StatCurve"/> makes at length.
        /// </remarks>
        [Serializable]
        private sealed class StatCurveRow
        {
            [Tooltip("Added to the multiplier per step. Zero means depth does not touch this stat.")]
            [SerializeField, Min(0f)] private float _perStep;

            [Tooltip("The multiplier's ceiling. At least 1 — below it, deep enemies get weaker.")]
            [SerializeField, Min(1f)] private float _cap = 1f;

            [Tooltip("Stages per step. 1 for a ramp, 5 for GD §12.3's speed staircase.")]
            [SerializeField, Min(1)] private int _stageStep = 1;

            [Tooltip("1 for a curve that starts at stage 1 (HP, damage); 0 for one that steps on " +
                     "the stage number itself (speed). Nothing else is legal.")]
            [SerializeField, Range(0, 1)] private int _stageOffset = 1;

            /// <remarks>
            /// The parameterless constructor Unity's serialiser needs is not written out: a
            /// constructor with arguments removes it, so the four defaults above are what a
            /// deserialised row starts from and this one exists purely so the field initialisers
            /// in <see cref="ScalingBlock"/> can state GD §12.3's three shapes in one line each.
            /// </remarks>
            public StatCurveRow()
            {
            }

            public StatCurveRow(float perStep, float cap, int stageStep, int stageOffset)
            {
                _perStep = perStep;
                _cap = cap;
                _stageStep = stageStep;
                _stageOffset = stageOffset;
            }

            /// <summary>Converts this row, letting <see cref="StatCurve"/> refuse a bad one.</summary>
            public StatCurve ToCurve() => new StatCurve(_perStep, _cap, _stageStep, _stageOffset);
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
