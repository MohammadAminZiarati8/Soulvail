using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script
// importer parses a file to find the type it declares and does not understand `namespace X;`, so a
// ScriptableObject declared that way is never linked to a MonoScript: a SkillTree asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11, Traps §5).
// The ScriptableObject leads the file for the same importer's sake; the two serializable rows
// follow it.
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// One class's whole tree as a designer authors it: three branches of tiers of node references.
    /// The Inspector half of <see cref="SkillTreeSpec"/>. Converted once at boot into the immutable
    /// spec core consumes and registered in the <c>ContentCatalog</c>. See AR §10.1, §12 and
    /// ADR-0006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same shape as every other definition in this folder, and it validates nothing
    /// <see cref="SkillTreeSpec"/> already validates: the branch count, the tier ceiling, an empty
    /// tier and a node listed twice all live in the core constructors, which are the single account
    /// of what a legal tree is (M2-02 rule 9). What is refused <em>here</em> is the one mistake core
    /// cannot describe — an empty slot, which has no id to name.
    /// </para>
    /// <para>
    /// <b>Nested serializable classes rather than jagged arrays</b>, because Unity serialises the
    /// former and not the latter: a <c>ContentId[][]</c> field simply would not appear in the
    /// Inspector. <see cref="BranchField"/> holds <see cref="TierField"/>s, each of which holds the
    /// node references — three foldouts deep, which is enough for twelve nodes. M7-04's eighty-one
    /// may want a custom editor, and that is the day to write one (parking lot).
    /// </para>
    /// <para>
    /// <b>Node references, not typed ids</b>, for <see cref="SkillDefinition"/>'s parent reason:
    /// the id is read off the asset at conversion, so a renamed node stays in its tier. The
    /// character is a reference for the same reason.
    /// </para>
    /// <para>
    /// <b>Nothing resolves the other list.</b> A tree carries ids and the catalog is built from
    /// skills and trees at once, so a tier naming a node no boot list holds is not caught here —
    /// it is <c>TreeRules</c>' check at <c>Start</c> (M3-03) and M3-14b's over every shipped asset.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Content/Skill Tree", fileName = "SkillTree")]
    public sealed class SkillTreeDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "tree.new";

        [Tooltip("The class this tree belongs to. A class has exactly one tree (CH §5), and the " +
                 "catalog refuses a second one naming the same character.")]
        [SerializeField] private CharacterDefinition _character;

        [Tooltip("CH §5's three branches, in the order the tree view draws them. Always three — " +
                 "an identical skeleton for every class, so the UI is built once and content " +
                 "varies.")]
        [SerializeField] private BranchField[] _branches = NewBranches();

        /// <summary>
        /// The authored id text, exactly as it sits in the asset — for grouping and diagnostics
        /// before conversion. It is <em>not</em> known to be well-formed: only a
        /// <see cref="ToSpec"/> that returned tells you that.
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// Builds the immutable spec core consumes. A fresh instance every call — this asset holds
        /// no runtime state and hands out nothing it keeps a reference to (ADR-0006).
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Any authored field is invalid, or a node slot is empty. Always this exact type, never
        /// one of its subclasses: the caller cannot act on <em>which</em> field failed, only on
        /// <em>which asset</em> failed, and that is what the message leads with. The original is
        /// kept as the inner exception.
        /// </exception>
        public SkillTreeSpec ToSpec()
        {
            try
            {
                return new SkillTreeSpec(
                    new ContentId(_id),
                    _character == null ? default : new ContentId(_character.Id),
                    BuildBranches());
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching here.
                // Uncaught, a designer reading the Console sees "has 2 branches" with a stack trace
                // through the boot installer and no way to tell which tree to open.
                throw new ArgumentException(
                    $"SkillTreeDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }

        /// <summary>
        /// Turns the authored branches into the <see cref="SkillBranchSpec"/>s core consumes.
        /// </summary>
        /// <remarks>
        /// A null or empty array is passed through as an empty list rather than refused, so
        /// <see cref="SkillTreeSpec"/> reports it as the branch-count failure it is — <em>"has 0
        /// branches; every class has exactly 3"</em> — instead of a second sentence saying the same
        /// thing worse.
        /// </remarks>
        private IReadOnlyList<SkillBranchSpec> BuildBranches()
        {
            if (_branches is null || _branches.Length == 0)
            {
                return Array.Empty<SkillBranchSpec>();
            }

            var branches = new SkillBranchSpec[_branches.Length];

            for (int b = 0; b < _branches.Length; b++)
            {
                BranchField branch = _branches[b];

                if (branch is null)
                {
                    throw new ArgumentException(
                        $"branches[{b}] is missing. A tree has three branches, not two and a hole.",
                        nameof(_branches));
                }

                branches[b] = branch.ToSpec(b);
            }

            return branches;
        }

        /// <summary>
        /// Three empty branches, so a tree created from the Create menu shows CH §5's shape rather
        /// than an empty list a designer has to guess the size of.
        /// </summary>
        private static BranchField[] NewBranches()
        {
            var branches = new BranchField[SkillTreeSpec.BranchCount];

            for (int b = 0; b < branches.Length; b++)
            {
                branches[b] = new BranchField();
            }

            return branches;
        }

        /// <remarks>
        /// Only the id, and only its shape — the established bargain. A missing node slot is
        /// visibly missing in the Inspector, while <c>Tree.Oathbound</c> looks perfectly reasonable
        /// and fails at boot. Warnings, never fixes. The asset is passed as the log context so
        /// clicking the warning selects it.
        /// </remarks>
        private void OnValidate()
        {
            if (!ContentId.IsValid(_id))
            {
                Debug.LogWarning(
                    $"SkillTreeDefinition '{name}': '{_id}' is not a valid content id. Expected " +
                    "lowercase dot-separated segments, at least two, e.g. 'tree.oathbound'.",
                    this);
            }
        }
    }

    /// <summary>
    /// One branch as it sits in the Inspector: a display name and its tiers, tier 1 first.
    /// </summary>
    /// <remarks>
    /// A <c>[Serializable]</c> class for <c>ModeDefinition.ScalingBlock</c>'s reason — Unity can be
    /// relied on to have run a class's field initialisers and not a struct's — and because a class
    /// draws as one foldout, which is what keeps three branches of tiers legible at all.
    /// </remarks>
    [Serializable]
    public sealed class BranchField
    {
        [Tooltip("Localisation key for the branch's display name, drawn above its column.")]
        [SerializeField] private string _nameKey = "tree.new.branch";

        [Tooltip("The tiers, tier 1 first. One to eight of them (CH §5's ceiling), each holding " +
                 "at least one node — gating counts tiers by position, so an empty tier is an " +
                 "unreachable rung rather than a gap.")]
        [SerializeField] private TierField[] _tiers = Array.Empty<TierField>();

        /// <summary>
        /// Converts this branch, letting <see cref="SkillBranchSpec"/> refuse a bad shape.
        /// </summary>
        /// <param name="branch">
        /// Which branch this is, 0-based — the index the Inspector shows, so a failure message
        /// names the element a designer is looking at rather than a number they have to translate.
        /// </param>
        public SkillBranchSpec ToSpec(int branch)
        {
            IReadOnlyList<ContentId>[] tiers = _tiers is null
                ? Array.Empty<IReadOnlyList<ContentId>>()
                : new IReadOnlyList<ContentId>[_tiers.Length];

            for (int t = 0; t < tiers.Length; t++)
            {
                TierField tier = _tiers[t];

                if (tier is null)
                {
                    throw new ArgumentException(
                        $"branches[{branch}].tiers[{t}] is missing. An absent tier is not an empty "
                            + "one — gating counts tiers by position, so a hole would renumber "
                            + "every tier below it.",
                        nameof(_tiers));
                }

                tiers[t] = tier.ToIds(branch, t);
            }

            return new SkillBranchSpec(new LocKey(_nameKey), tiers);
        }
    }

    /// <summary>
    /// One tier as it sits in the Inspector: the nodes that share a rung of a branch.
    /// </summary>
    /// <remarks>
    /// A class for <see cref="BranchField"/>'s reason. One or more nodes, which is the owner's
    /// ruling at M3-00a rather than a reading of CH §5 — a one-node tier makes every branch a
    /// straight chain and the level-up offer the same three heads every time.
    /// </remarks>
    [Serializable]
    public sealed class TierField
    {
        [Tooltip("The nodes on this rung, as references rather than ids so a renamed node stays " +
                 "where it was put. At least one.")]
        [SerializeField] private SkillDefinition[] _nodes = Array.Empty<SkillDefinition>();

        /// <summary>
        /// The ids of this tier's nodes, in the order they were authored.
        /// </summary>
        /// <param name="branch">Which branch holds it, 0-based — the Inspector's own index.</param>
        /// <param name="tier">Which tier this is, 0-based — the Inspector's own index.</param>
        /// <remarks>
        /// <para>
        /// An empty slot is refused here and named with its position, because it is the one mistake
        /// core cannot describe: a missing reference has no id, so <see cref="SkillBranchSpec"/>
        /// would report a <c>default(ContentId)</c> and point at nothing a designer can open.
        /// </para>
        /// <para>
        /// An empty <em>tier</em> is passed through, so <see cref="SkillBranchSpec"/> refuses it
        /// with the message that explains why an empty rung is unreachable.
        /// </para>
        /// <para>
        /// Both indices are 0-based here and the tier is 1-based in <see cref="SkillTreeSpec"/>,
        /// deliberately: this message is read beside the Inspector, where both are element numbers,
        /// and that one is read beside CH §5, where tiers are numbered from 1.
        /// </para>
        /// </remarks>
        public IReadOnlyList<ContentId> ToIds(int branch, int tier)
        {
            if (_nodes is null || _nodes.Length == 0)
            {
                return Array.Empty<ContentId>();
            }

            var ids = new ContentId[_nodes.Length];

            for (int i = 0; i < _nodes.Length; i++)
            {
                SkillDefinition node = _nodes[i];

                if (node == null)
                {
                    throw new ArgumentException(
                        $"branches[{branch}].tiers[{tier}].nodes[{i}] is an empty slot. Every rung "
                            + "of a tree must reference a SkillDefinition asset.",
                        nameof(_nodes));
                }

                ids[i] = new ContentId(node.Id);
            }

            return ids;
        }
    }
}
