using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script importer
// parses a file to find the type it declares and does not understand `namespace X;`, so a
// ScriptableObject declared that way is never linked to a MonoScript: a ModifySkillCooldown asset
// would serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11,
// Traps §5).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// The fourth primitive's Inspector half: which skill's cooldown this moves, which stack
    /// position the modifier occupies, and by how much. The authoring side of
    /// <see cref="ModifySkillCooldown"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The skill is authored as an id string rather than as a <see cref="SkillDefinition"/>
    /// reference</b>, which is the opposite of <c>SkillDefinition._parent</c> and is deliberate. An
    /// effect asset lives under <c>Data/Effects/</c> and knows nothing about the tree it ends up in;
    /// a hard reference from an effect to a skill would put every cooldown node in the same load
    /// batch as the skill it names and make the pair impossible to author in either order. The
    /// catalog already keys skills by <see cref="ContentId"/>, and the id is what
    /// <see cref="ModifySkillCooldown"/> carries.
    /// </para>
    /// <para>
    /// <b>A typo therefore ships as a node that does nothing, silently — and that is M3-12b rule 3
    /// working rather than failing.</b> A modifier for a skill the run does not own is held pending,
    /// because a Passive carrying one is gated by nothing and a throw would kill a legal run at a
    /// pick. The shape of the id is checked here and in <see cref="ModifySkillCooldown"/>; whether
    /// any tree ships a node with that id is M3-14b's content sweep, which is the layer that can see
    /// the whole catalog at once.
    /// </para>
    /// <para>
    /// The same shape as every other definition in this folder: <c>[SerializeField] private</c>
    /// fields, one conversion that is the only way out, an <see cref="OnValidate"/> that warns
    /// without fixing, and every failure rewrapped as a plain <see cref="ArgumentException"/> naming
    /// the asset. It validates nothing <see cref="ModifySkillCooldown"/> already validates — the
    /// kind and the finiteness of the value both live in that constructor, which is the single
    /// account of what a legal effect is.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        menuName = "Soulvail/Effects/Modify Skill Cooldown",
        fileName = "ModifySkillCooldown")]
    public sealed class ModifySkillCooldownDefinition : EffectDefinition
    {
        [Tooltip("Which skill's cooldown this moves — the Active's content id, exactly as its " +
                 "SkillDefinition spells it, e.g. 'skill.oathbound.consecrate'. A node taken " +
                 "before that skill is owned waits silently and lands the moment it arrives.")]
        [SerializeField] private string _skillId = "skill.new";

        [Tooltip("Which of the three stack positions the modifier occupies. CH §4.1 describes " +
                 "cooldown reduction as multiplicative, so Percent Mult is the kind it means — " +
                 "and what that buys is the cost: six -15 % multiplicative nodes reach an 8 s " +
                 "skill's floor where four additive ones do.")]
        [SerializeField] private ModifierKind _kind = ModifierKind.PercentMult;

        [Tooltip("The amount, read according to the kind above: seconds for Flat, a fraction for " +
                 "either percentage kind — -0.25 is -25 %. CH §4.1's 40 % floor is applied where " +
                 "the cooldown is used, so no stack authored here can drive one below it.")]
        [SerializeField] private float _value = -0.25f;

        /// <inheritdoc/>
        /// <remarks>
        /// The id door runs first, then the three fields go to <see cref="ModifySkillCooldown"/>
        /// untouched. Both failures leave through the same rewrap, so the Console message always
        /// starts with the file a designer can open (M0-11).
        /// </remarks>
        public override IEffect ToEffect()
        {
            try
            {
                // Thrown inside the try on purpose, so it is rewrapped and kept as the inner
                // exception exactly like a failure out of ModifySkillCooldown's own constructor. A
                // designer should not be able to tell which door refused the asset.
                if (!ContentId.IsValid(_skillId))
                {
                    throw new ArgumentException(
                        $"'{_skillId}' is not a valid content id. Expected lowercase dot-separated "
                            + "segments, at least two, e.g. 'skill.oathbound.consecrate'.",
                        nameof(_skillId));
                }

                return new ModifySkillCooldown(new ContentId(_skillId), _kind, _value);
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching here.
                // Uncaught, a designer reading the Console sees "ModifySkillCooldown value must be
                // finite" with a stack trace through the boot installer and no way to tell which of
                // the catalog's effect assets to open.
                throw new ArgumentException(
                    $"ModifySkillCooldownDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }

        /// <remarks>
        /// Only the id, and only its <em>shape</em> — <see cref="ModifyStatDefinition"/>'s bargain
        /// one field along. A malformed id fails at boot and looks like nothing in the Inspector,
        /// where a bad value is visibly a bad value; whether the id names a skill that exists cannot
        /// be asked from one asset and is M3-14b's. Warnings, never fixes: <see cref="ToEffect"/> is
        /// where refusal lives. The asset is passed as the log context so clicking the warning
        /// selects it.
        /// </remarks>
        private void OnValidate()
        {
            if (!ContentId.IsValid(_skillId))
            {
                Debug.LogWarning(
                    $"ModifySkillCooldownDefinition '{name}': '{_skillId}' is not a valid content " +
                    "id. Expected lowercase dot-separated segments, at least two, e.g. " +
                    "'skill.oathbound.consecrate'.",
                    this);
            }
        }
    }
}
