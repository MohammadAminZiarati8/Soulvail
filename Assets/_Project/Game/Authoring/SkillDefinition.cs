using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script
// importer parses a file to find the type it declares and does not understand `namespace X;`, so a
// ScriptableObject declared that way is never linked to a MonoScript: a Skill asset would serialise
// as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11, Traps §5). The
// ScriptableObject is declared first in this file for the same importer's sake — the type whose
// name matches the file leads, and the plain serializable class follows it.
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// One tree node as a designer authors it: the Inspector half of <see cref="SkillSpec"/>.
    /// Converted once at boot into the immutable spec core consumes and registered in the
    /// <c>ContentCatalog</c>. See AR §10.1, §12 and ADR-0006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same shape as every other definition in this folder: <c>[SerializeField] private</c>
    /// fields, one <see cref="ToSpec"/> that is the only way out, an <see cref="OnValidate"/> that
    /// warns without fixing, and every failure rewrapped as a plain <see cref="ArgumentException"/>
    /// naming the asset. It validates nothing <see cref="SkillSpec"/> already validates — the kind
    /// and its block, the parent rule, the no-effects rule all live in that constructor, which is
    /// the single account of what a legal node is (M2-02 rule 9).
    /// </para>
    /// <para>
    /// <b>Fields the kind does not use are ignored rather than refused</b>, which the two
    /// <c>[Header]</c>s below announce. A Passive carrying a stray cooldown is not an error: the
    /// <see cref="ActiveSpec"/> is built only for an <see cref="SkillKind.Active"/> and the parent
    /// is read only for an <see cref="SkillKind.Upgrade"/>, so <see cref="SkillSpec"/>'s
    /// both-directions rule is met <em>by construction</em> rather than by a designer keeping
    /// fields in step with a dropdown.
    /// </para>
    /// <para>
    /// <b>A parent is a reference to the asset, never a typed id.</b> <c>_parent.Id</c> is read at
    /// conversion, so renaming a parent's id carries every child with it; a string field here would
    /// be a second copy of that id, and the two would drift the first time one was edited. The same
    /// reasoning puts node references rather than ids in <see cref="SkillTreeDefinition"/>.
    /// </para>
    /// <para>
    /// <b>Effects are referenced assets, not inline data</b> — see <see cref="EffectDefinition"/>
    /// for why, and for the two alternatives that were rejected.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Content/Skill", fileName = "Skill")]
    public sealed class SkillDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "skill.new";
        [SerializeField] private string _nameKey = "skill.new.name";
        [SerializeField] private string _descriptionKey = "skill.new.description";

        [Tooltip("CH §4's four kinds. Which one this is decides which of the two blocks below is " +
                 "read at all — the other is ignored, not refused.")]
        [SerializeField] private SkillKind _kind = SkillKind.Passive;

        [Tooltip("What taking this node puts into force, applied once when it is taken. Required " +
                 "on every kind but Active, whose power is on cast.")]
        [SerializeField] private EffectDefinition[] _effects = Array.Empty<EffectDefinition>();

        [Header("Active — read only when Kind is Active, ignored otherwise")]
        [Tooltip("Seconds between casts, before any reduction. CH §4.1's 40 % floor belongs to " +
                 "M3-06's runner, which is the layer that knows what 'too short' means.")]
        [SerializeField, Min(0.01f)] private float _cooldown = 8f;

        [Tooltip("CC §6.4's condition — when Auto decides to fire it. One or two clauses, all of " +
                 "which must hold. Two, because CH §4.2's Rot Nova needs two.")]
        [SerializeField] private TriggerClauseField[] _trigger = Array.Empty<TriggerClauseField>();

        [Tooltip("What firing it does. At least one effect, or the skill goes on cooldown and " +
                 "produces nothing.")]
        [SerializeField] private EffectDefinition[] _onCast = Array.Empty<EffectDefinition>();

        [Header("Upgrade — read only when Kind is Upgrade, ignored otherwise")]
        [Tooltip("The node this improves, as a reference rather than an id, so a renamed parent " +
                 "carries its children with it. M3-03 checks that it sits in the same branch " +
                 "below this one.")]
        [SerializeField] private SkillDefinition _parent;

        [Header("Pact — GD §13.2's corrupted form. Optional on any kind but Active")]
        [Tooltip("Whether this node has a corrupted form at all. Off means it can never be " +
                 "offered as a Pact. On an Active it is refused at boot (M6-05a rule 3).")]
        [SerializeField] private bool _hasPact;

        [Tooltip("What taking the corrupted node puts into force — instead of the effects above, " +
                 "never as well as. Author GD §13.2's ~1.8× as a budget, downside included.")]
        [SerializeField] private EffectDefinition[] _pactEffects = Array.Empty<EffectDefinition>();

        [Tooltip("What taking it adds to the Veilrot meter. GD §13.2's band: 10 to 20.")]
        [SerializeField] private float _pactVeilrot = 15f;

        [Tooltip("What the corrupted node says. The name stays the clean node's, so the player " +
                 "recognises it (M6-05a rule 4).")]
        [SerializeField] private string _pactDescriptionKey = "skill.new.pact.description";

        /// <summary>
        /// The authored id text, exactly as it sits in the asset — for grouping and diagnostics
        /// before conversion, and for a child node reading its parent's. It is <em>not</em> known
        /// to be well-formed: only a <see cref="ToSpec"/> that returned tells you that.
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// Builds the immutable spec core consumes. A fresh instance every call — this asset holds
        /// no runtime state and hands out nothing it keeps a reference to (ADR-0006).
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Any authored field is invalid, or an effect slot is empty. Always this exact type, never
        /// one of its subclasses: the caller cannot act on <em>which</em> field failed, only on
        /// <em>which asset</em> failed, and that is what the message leads with. The original is
        /// kept as the inner exception, so the field and its value survive into the log — and when
        /// the inner failure came from a referenced effect asset, that asset's own name survives
        /// with it.
        /// </exception>
        public SkillSpec ToSpec()
        {
            try
            {
                return new SkillSpec(
                    new ContentId(_id),
                    new LocKey(_nameKey),
                    new LocKey(_descriptionKey),
                    _kind,
                    BuildEffects(_effects, nameof(_effects)),
                    _kind == SkillKind.Active ? BuildActive() : null,
                    _kind == SkillKind.Upgrade ? ParentId() : default,
                    _hasPact ? BuildPact() : null);
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching here.
                // Uncaught, a designer reading the Console sees "is an Active with no ActiveSpec"
                // with a stack trace through the boot installer and no way to tell which of the
                // catalog's nodes to open.
                throw new ArgumentException(
                    $"SkillDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }

        /// <summary>
        /// Turns one authored effect list into the <see cref="IEffect"/>s core consumes, in the
        /// order they were authored.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An empty slot is refused here and named with its index, because it is the one mistake
        /// neither <see cref="SkillSpec"/> nor the effect asset can report usefully: core would say
        /// only <c>"effects[1] is null"</c>, which points at no file, and the effect that is missing
        /// has no <c>ToEffect</c> to fail in. <c>== null</c> is Unity's operator here rather than
        /// reference equality — the field is a concrete <see cref="UnityEngine.Object"/> type, not a
        /// type parameter — so a <em>destroyed</em> asset is caught along with an empty slot.
        /// </para>
        /// <para>
        /// A null or empty array is passed through as an empty list rather than refused. Unity
        /// never serialises a null array, but a freshly <c>CreateInstance</c>d asset in a test is a
        /// real path here, and an empty list is legal content on an Active — so this is not
        /// somewhere to invent a failure <see cref="SkillSpec"/> does not have.
        /// </para>
        /// </remarks>
        private IReadOnlyList<IEffect> BuildEffects(EffectDefinition[] slots, string field)
        {
            if (slots is null || slots.Length == 0)
            {
                return Array.Empty<IEffect>();
            }

            var effects = new IEffect[slots.Length];

            for (int i = 0; i < slots.Length; i++)
            {
                EffectDefinition slot = slots[i];

                if (slot == null)
                {
                    throw new ArgumentException(
                        $"{field}[{i}] is an empty slot. Every effect on a node must reference an "
                            + "EffectDefinition asset, or taking the node would silently do less "
                            + "than it says.",
                        field);
                }

                // Each effect asset's own failure already names itself (M0-11), so nothing is
                // caught here — the outer rewrap adds this node's name in front of it, and the
                // Console ends up naming both files.
                effects[i] = slot.ToEffect();
            }

            return effects;
        }

        /// <summary>
        /// Builds the cooldown, trigger and cast effects an <see cref="SkillKind.Active"/> needs.
        /// </summary>
        private ActiveSpec BuildActive() =>
            new ActiveSpec(_cooldown, BuildTrigger(), BuildEffects(_onCast, nameof(_onCast)));

        /// <summary>
        /// Builds GD §13.2's corrupted form, when the toggle says there is one.
        /// </summary>
        /// <remarks>
        /// <b>Not gated on the kind, unlike the two blocks above.</b> A Pact on an Active is refused
        /// by <see cref="SkillSpec"/> rather than ignored here, because a toggle a designer switched
        /// on is a claim that the node has a corrupted form — silently dropping it would ship a
        /// node that is never offered as the Pact its asset says it is.
        /// </remarks>
        private PactSpec BuildPact() =>
            new PactSpec(
                BuildEffects(_pactEffects, nameof(_pactEffects)),
                _pactVeilrot,
                new LocKey(_pactDescriptionKey));

        /// <summary>
        /// Turns the authored clauses into the <see cref="TriggerSpec"/> M3-06's runner asks.
        /// </summary>
        /// <remarks>
        /// An empty list is handed to <see cref="TriggerSpec"/> rather than refused here, so the
        /// message a designer reads is the one that explains itself — <em>"an empty condition is a
        /// blank field rather than a skill that always fires"</em> — instead of a second sentence
        /// saying the same thing worse. A null <em>entry</em> is refused here, because a
        /// <c>[Serializable]</c> class Unity would have instantiated can only be null through a
        /// hand-edited asset, and the core type has no index to name.
        /// </remarks>
        private TriggerSpec BuildTrigger()
        {
            if (_trigger is null || _trigger.Length == 0)
            {
                return new TriggerSpec(Array.Empty<TriggerClause>());
            }

            var clauses = new TriggerClause[_trigger.Length];

            for (int i = 0; i < _trigger.Length; i++)
            {
                TriggerClauseField clause = _trigger[i];

                if (clause is null)
                {
                    throw new ArgumentException(
                        $"{nameof(_trigger)}[{i}] is missing. A clause is a field, a comparison "
                            + "and a number; an absent one is a condition nobody authored.",
                        nameof(_trigger));
                }

                clauses[i] = clause.ToClause();
            }

            return new TriggerSpec(clauses);
        }

        /// <summary>
        /// The parent's id, read off the referenced asset at conversion time.
        /// </summary>
        /// <remarks>
        /// An empty slot produces <c>default(ContentId)</c> rather than a failure of its own, so
        /// <see cref="SkillSpec"/> refuses it with the message that already exists for the case —
        /// <em>"is an Upgrade with no parentId"</em>. One account of what a legal node is.
        /// </remarks>
        private ContentId ParentId() =>
            _parent == null ? default : new ContentId(_parent.Id);

        /// <remarks>
        /// The id's shape, and the two kind mismatches that are cheap to see from here — the same
        /// bargain <c>CharacterDefinition</c>, <c>EnemyDefinition</c> and <c>ModeDefinition</c>
        /// make, widened by exactly the two cases that would otherwise fail at boot with a message
        /// a designer meets one scene later. Warnings, never fixes: <see cref="ToSpec"/> is where
        /// refusal lives. The asset is passed as the log context so clicking the warning selects it.
        /// </remarks>
        private void OnValidate()
        {
            if (!ContentId.IsValid(_id))
            {
                Debug.LogWarning(
                    $"SkillDefinition '{name}': '{_id}' is not a valid content id. Expected " +
                    "lowercase dot-separated segments, at least two, e.g. " +
                    "'skill.oathbound.consecrate'.",
                    this);
            }

            if (_kind == SkillKind.Active && (_onCast is null || _onCast.Length == 0))
            {
                Debug.LogWarning(
                    $"SkillDefinition '{name}' is an Active with nothing in On Cast. It will go " +
                    "on cooldown and produce nothing, and ToSpec refuses it at boot.",
                    this);
            }

            if (_kind == SkillKind.Upgrade && _parent == null)
            {
                Debug.LogWarning(
                    $"SkillDefinition '{name}' is an Upgrade with no Parent. An Upgrade improves a " +
                    "skill you already own (CH §4), and one naming nothing could never be gated " +
                    "on anything.",
                    this);
            }

            if (_hasPact && _kind == SkillKind.Active)
            {
                Debug.LogWarning(
                    $"SkillDefinition '{name}' is an Active with a Pact. An Active's corrupted form " +
                    "has no runner door until M7-04, and ToSpec refuses it at boot.",
                    this);
            }

            if (_hasPact && (_pactEffects is null || _pactEffects.Length == 0))
            {
                Debug.LogWarning(
                    $"SkillDefinition '{name}' has a Pact with no effects — a Veilrot price for " +
                    "nothing, and ToSpec refuses it at boot.",
                    this);
            }
        }
    }

    /// <summary>
    /// One clause of a CC §6.4 condition as it sits in the Inspector: which blackboard field, which
    /// way round, and what number. The authoring half of <see cref="TriggerClause"/>.
    /// </summary>
    /// <remarks>
    /// A <c>[Serializable]</c> class rather than a struct, for <c>ModeDefinition</c>'s
    /// <c>ScalingBlock</c> reason: Unity's serialiser can be relied on to have run a class's field
    /// initialisers and not a struct's. It draws as one row of three controls, which is what makes
    /// CC §6.4's <em>"conditions are authored per skill"</em> an asset rather than a line of C#.
    /// </remarks>
    [Serializable]
    public sealed class TriggerClauseField
    {
        [Tooltip("Which CombatBlackboard field to read. These are code, not content — every " +
                 "member names a field PlayerCombat.Tick actually writes.")]
        [SerializeField] private TriggerField _field;

        [Tooltip("Which way round it reads. Below is strictly less than; At Least is greater " +
                 "than or equal.")]
        [SerializeField] private TriggerComparison _comparison;

        [Tooltip("The number the field is compared against. Counts are whole numbers and " +
                 "fractions are in [0, 1]; both arrive as a float.")]
        [SerializeField] private float _threshold;

        /// <summary>
        /// Converts this row, letting <see cref="TriggerClause"/> refuse a bad threshold.
        /// </summary>
        public TriggerClause ToClause() => new TriggerClause(_field, _comparison, _threshold);
    }
}
