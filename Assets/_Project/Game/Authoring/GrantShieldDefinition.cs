using System;
using Soulvail.Core.Effects;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script
// importer parses a file to find the type it declares and does not understand `namespace X;`, so a
// ScriptableObject declared that way is never linked to a MonoScript: a GrantShield asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11, Traps §5).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// The second primitive's Inspector half: how many shield points a cast puts on the player and
    /// how long they stand. The authoring side of <see cref="GrantShield"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both numbers are authored and neither is in code</b> (M3-05 rule 10). CC §6.4's Bulwark is
    /// 35 points for 5 seconds on an 8-second cooldown — one Aegis and a bit, held about as long as a
    /// Spitter volley — and every one of those three numbers lives in an asset the owner retunes
    /// after a playtest, exactly as M2-03's five did. The cooldown is the <c>SkillDefinition</c>'s;
    /// these two are this one's. <b>No asset ships here</b>: <c>Bulwark.asset</c> is M3-12c's.
    /// </para>
    /// <para>
    /// The same shape as every other definition in this folder: <c>[SerializeField] private</c>
    /// fields, one conversion that is the only way out, and every failure rewrapped as a plain
    /// <see cref="ArgumentException"/> naming the asset. It validates nothing
    /// <see cref="GrantShield"/> already validates — both doors are that constructor's, which is the
    /// single account of what a legal grant is.
    /// </para>
    /// <para>
    /// <b>No <c>OnValidate</c>, unlike <see cref="ModifyStatDefinition"/>, and the asymmetry is the
    /// rule rather than an omission.</b> That type warns about one thing: an address that is no
    /// longer a member of an enum, which looks like a Unity glitch in the Inspector and fails at
    /// boot. A zero or negative number here is visibly a zero or negative number in the Inspector,
    /// and <see cref="ToEffect"/> is where refusal lives.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Effects/Grant Shield", fileName = "GrantShield")]
    public sealed class GrantShieldDefinition : EffectDefinition
    {
        [Tooltip("Shield points the cast grants. They sit on top of the Aegis and are spent " +
                 "before it, per source — a second cast from the same node refreshes these rather " +
                 "than doubling them. CC §6.4's Bulwark is 35, one Aegis and a bit.")]
        [SerializeField] private float _amount = 35f;

        [Tooltip("How long the points stand, in seconds. They come off on the run's simulated " +
                 "clock, so a pause or a level-up screen does not drain them. Bulwark is 5 — about " +
                 "as long as a Spitter volley.")]
        [SerializeField] private float _duration = 5f;

        /// <inheritdoc/>
        /// <remarks>
        /// Both fields go to <see cref="GrantShield"/> untouched, and its refusal leaves through the
        /// rewrap, so the Console message always starts with the file a designer can open (M0-11).
        /// </remarks>
        public override IEffect ToEffect()
        {
            try
            {
                return new GrantShield(_amount, _duration);
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching here.
                // Uncaught, a designer reading the Console sees "GrantShield duration must be a
                // finite number of seconds" with a stack trace through the boot installer and no way
                // to tell which of the catalog's effect assets to open.
                throw new ArgumentException(
                    $"GrantShieldDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }
    }
}
