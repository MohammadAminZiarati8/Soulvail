using System;
using Soulvail.Core.Effects;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script
// importer parses a file to find the type it declares and does not understand `namespace X;`, so a
// ScriptableObject declared that way is never linked to a MonoScript: a RaiseMinions asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11, Traps §5).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// The sixth primitive's Inspector half: how many Wights a cast stands up and how far from the
    /// player each one appears. The authoring side of <see cref="RaiseMinions"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both numbers are authored and neither is in code</b> (M3-05 rule 10). CH §4.2's Exhume is
    /// <em>"raise 3 Wights"</em>; the ring is 2.0 m, which is ours — far enough that a Wight is not
    /// inside the player's own silhouette and near enough to read as <em>yours</em> — and the owner
    /// retunes both after a playtest, exactly as M2-03's five were. <b>No asset ships here</b>:
    /// <c>RaiseMinions.asset</c> and the <c>Exhume.asset</c> that casts it are
    /// <see href="../../../../Docs/plan/tasks/M5-06b-gravecaller-tree-v1.md">M5-06b</see>'s.
    /// </para>
    /// <para>
    /// The same shape as every other definition in this folder: <c>[SerializeField] private</c>
    /// fields, one conversion that is the only way out, and every failure rewrapped as a plain
    /// <see cref="ArgumentException"/> naming the asset. It validates nothing
    /// <see cref="RaiseMinions"/> already validates — both doors are that constructor's, which is
    /// the single account of what a legal raise is.
    /// </para>
    /// <para>
    /// <b>No <c>OnValidate</c>, on <see cref="GrantShieldDefinition"/>'s rule.</b> That hook exists
    /// for a mistake the Inspector renders as something other than itself — an address that is no
    /// longer a member of an enum. A zero count or a negative radius is visibly a zero count or a
    /// negative radius in the Inspector, and <see cref="ToEffect"/> is where refusal lives.
    /// </para>
    /// <para>
    /// <b>A run for a class with no <c>MinionSpec</c> never registers a handler for this primitive</b>,
    /// so a tree carrying this asset on such a class refuses the run at <c>RunSession.Start</c> —
    /// naming the node — rather than failing at the moment a thumb lands. That is
    /// <c>SkillTree</c>'s existing <c>CanApply</c> sweep, not a rule this file adds.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Effects/Raise Minions", fileName = "RaiseMinions")]
    public sealed class RaiseMinionsDefinition : EffectDefinition
    {
        [Tooltip("How many Wights the cast tries to stand up. At the live cap it raises fewer, or " +
                 "none, and says nothing — which is what the skill's \"below half the cap\" " +
                 "trigger exists to keep rare. CH §4.2's Exhume is 3.")]
        [SerializeField] private int _count = 3;

        [Tooltip("Metres from the player each one appears at. They are spaced evenly on a ring " +
                 "anchored to the world rather than to the player's facing, so two casts from the " +
                 "same spot stand them in the same three places. 2.0 keeps them out of the " +
                 "player's own silhouette and close enough to read as yours.")]
        [SerializeField] private float _radius = 2f;

        /// <inheritdoc/>
        /// <remarks>
        /// Both fields go to <see cref="RaiseMinions"/> untouched, and its refusal leaves through
        /// the rewrap, so the Console message always starts with the file a designer can open
        /// (M0-11).
        /// </remarks>
        public override IEffect ToEffect()
        {
            try
            {
                return new RaiseMinions(_count, _radius);
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching here.
                // Uncaught, a designer reading the Console sees "RaiseMinions radius must be a
                // finite number greater than zero" with a stack trace through the boot installer
                // and no way to tell which of the catalog's effect assets to open.
                throw new ArgumentException(
                    $"RaiseMinionsDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }
    }
}
