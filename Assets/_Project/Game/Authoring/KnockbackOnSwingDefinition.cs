using System;
using Soulvail.Core.Effects;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script importer
// parses a file to find the type it declares and does not understand `namespace X;`, so a
// ScriptableObject declared that way is never linked to a MonoScript: a KnockbackOnSwing asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11, Traps §5).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// The fifth primitive's Inspector half, and the shortest one there will ever be: how far a
    /// swing shoves. The authoring side of <see cref="KnockbackOnSwing"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One field, and the absence of a second is the feature</b> (M3-12b rule 6). The number this
    /// carries lands on a <c>Stat</c> whose base is zero, where every kind but
    /// <c>ModifierKind.Flat</c> multiplies into nothing — so <see cref="KnockbackOnSwingHandler"/>
    /// chooses the kind and there is no dropdown here to get wrong. That is the entire argument for
    /// this being a primitive rather than a <c>PlayerStat</c> member authored through
    /// <see cref="ModifyStatDefinition"/>, where the same node would ship silently doing nothing.
    /// </para>
    /// <para>
    /// The same shape as every other definition in this folder: <c>[SerializeField] private</c>
    /// fields, one conversion that is the only way out, and every failure rewrapped as a plain
    /// <see cref="ArgumentException"/> naming the asset. It validates nothing
    /// <see cref="KnockbackOnSwing"/> already validates — the one door is that constructor's, which
    /// is the single account of what a legal shove is.
    /// </para>
    /// <para>
    /// <b>No <c>OnValidate</c>, like <see cref="GrantShieldDefinition"/> and
    /// <see cref="SpawnHealZoneDefinition"/>.</b> A zero or negative distance is visibly a zero or
    /// negative distance in the Inspector, and <see cref="ToEffect"/> is where refusal lives. The
    /// types that warn do it for one thing only: a field whose bad state looks like a Unity glitch
    /// rather than like a number.
    /// </para>
    /// <para>
    /// <b>No asset ships here</b>: the node that carries one is M3-12c's.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(
        menuName = "Soulvail/Effects/Knockback On Swing",
        fileName = "KnockbackOnSwing")]
    public sealed class KnockbackOnSwingDefinition : EffectDefinition
    {
        [Tooltip("How far each enemy the swing hits is shoved, in metres. They are all swept the " +
                 "way the swing was facing, not outward from a point. CC §5's Charge is 4 m for " +
                 "comparison — a dash is a positioning tool and this is a beat of breathing room, " +
                 "so a swing node is deliberately a fraction of it. Two nodes of 1.5 give 3.")]
        [SerializeField] private float _distance = 1.5f;

        /// <inheritdoc/>
        /// <remarks>
        /// The one field goes to <see cref="KnockbackOnSwing"/> untouched, and its refusal leaves
        /// through the rewrap, so the Console message always starts with the file a designer can
        /// open (M0-11).
        /// </remarks>
        public override IEffect ToEffect()
        {
            try
            {
                return new KnockbackOnSwing(_distance);
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching here.
                // Uncaught, a designer reading the Console sees "KnockbackOnSwing distance must be
                // a finite number greater than zero" with a stack trace through the boot installer
                // and no way to tell which of the catalog's effect assets to open.
                throw new ArgumentException(
                    $"KnockbackOnSwingDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }
    }
}
