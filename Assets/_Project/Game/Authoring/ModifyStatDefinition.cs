using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Effects;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script
// importer parses a file to find the type it declares and does not understand `namespace X;`, so a
// ScriptableObject declared that way is never linked to a MonoScript: a ModifyStat asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11, Traps §5).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// The first primitive's Inspector half: which player number this moves, which stack position
    /// the modifier occupies, and by how much. The authoring side of <see cref="ModifyStat"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One modifier, not one node.</b> A node granting <em>"+2 damage and +15 %"</em> references
    /// two of these assets, which is <see cref="ModifyStat"/>'s own remark and the reason this type
    /// carries three fields rather than a list.
    /// </para>
    /// <para>
    /// The same shape as every other definition in this folder: <c>[SerializeField] private</c>
    /// fields, one conversion that is the only way out, an <see cref="OnValidate"/> that warns
    /// without fixing, and every failure rewrapped as a plain <see cref="ArgumentException"/> naming
    /// the asset. It validates nothing <see cref="ModifyStat"/> already validates — the kind and the
    /// finiteness of the value both live in that constructor, which is the single account of what a
    /// legal effect is.
    /// </para>
    /// <para>
    /// <b>The address is the one thing it does check, and that is the owner's ruling at M3-02b
    /// rather than a second copy of a core rule.</b> <see cref="ModifyStat"/> deliberately does not
    /// validate <see cref="PlayerStat"/> — its remarks say why — and <c>EffectRegistry.CanApply</c>
    /// answers only <em>"is there a handler for this type"</em>, so between the two of them a node
    /// authored against an address that is not a member of the enum passes every door in the game
    /// and throws out of <c>PlayerStats.Resolve</c> at the moment a player picks it. A member with
    /// no resolver line is already caught by <c>Stats_ResolveEveryMember</c>; what is left is a
    /// serialized <see cref="int"/> that is no longer a member at all — a removed or reordered
    /// enum entry, or hand-edited YAML — and <see cref="Enum.IsDefined(Type, object)"/> at
    /// conversion is what turns that from a crash in a run into a content error naming the file.
    /// The check is per primitive by design: each primitive's authoring half answers for its own
    /// fields, which is the same open set ADR-0009 buys one layer down.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Effects/Modify Stat", fileName = "ModifyStat")]
    public sealed class ModifyStatDefinition : EffectDefinition
    {
        [Tooltip("Which player number this moves. The closed set of addresses that exist as a " +
                 "Stat in code — a new one is a Stat on the object that owns the number, a member " +
                 "on PlayerStat and a line in PlayerStats.Resolve, never an authored string.")]
        [SerializeField] private PlayerStat _stat;

        [Tooltip("Which of the three stack positions the modifier occupies. Percent Add is the " +
                 "kind a node reaches for (GD §13.1: additively within a family); Percent Mult is " +
                 "reserved for the loud multipliers — Focus at full ramp, a boss phase, depth " +
                 "scaling, the Claiming.")]
        [SerializeField] private ModifierKind _kind = ModifierKind.PercentAdd;

        [Tooltip("The amount, read according to the kind above: units for Flat, a fraction for " +
                 "either percentage kind — 0.15 is +15 %.")]
        [SerializeField] private float _value;

        /// <inheritdoc/>
        /// <remarks>
        /// The address door runs first, then the three fields go to <see cref="ModifyStat"/>
        /// untouched. Both failures leave through the same rewrap, so the Console message always
        /// starts with the file a designer can open (M0-11).
        /// </remarks>
        public override IEffect ToEffect()
        {
            try
            {
                // Thrown inside the try on purpose, so it is rewrapped and kept as the inner
                // exception exactly like a failure out of ModifyStat's own constructor. A designer
                // should not be able to tell which door refused the asset.
                if (!Enum.IsDefined(typeof(PlayerStat), _stat))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(_stat),
                        _stat,
                        "no player stat is addressed by this value. A PlayerStat member that was "
                            + "removed or reordered leaves the old number behind in the asset, and "
                            + "it would otherwise throw at the moment a player picked the node.");
                }

                return new ModifyStat(_stat, _kind, _value);
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching here.
                // Uncaught, a designer reading the Console sees "ModifyStat value must be finite"
                // with a stack trace through the boot installer and no way to tell which of the
                // catalog's effect assets to open.
                throw new ArgumentException(
                    $"ModifyStatDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }

        /// <remarks>
        /// Only the address, and only whether it is still a member — the established bargain, one
        /// field along. A bad value is visibly a bad value in the Inspector, while a stat dropdown
        /// showing a bare number looks like a Unity glitch and fails at boot. Warnings, never
        /// fixes: <see cref="ToEffect"/> is where refusal lives. The asset is passed as the log
        /// context so clicking the warning selects it.
        /// </remarks>
        private void OnValidate()
        {
            if (!Enum.IsDefined(typeof(PlayerStat), _stat))
            {
                Debug.LogWarning(
                    $"ModifyStatDefinition '{name}': '{(int)_stat}' addresses no PlayerStat. The " +
                    "member this asset was authored against has been removed or reordered — pick " +
                    "an address from the dropdown again.",
                    this);
            }
        }
    }
}
