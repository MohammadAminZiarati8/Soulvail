using System;
using Soulvail.Core.Effects;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script importer
// parses a file to find the type it declares and does not understand `namespace X;`, so a
// ScriptableObject declared that way is never linked to a MonoScript: a SpawnHealZone asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11, Traps §5).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// The third primitive's Inspector half: how far a healing zone reaches, how long it stands, and
    /// what one pulse of it is worth. The authoring side of <see cref="SpawnHealZone"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>All four numbers are authored and none is in code</b> (M3-05 rule 10). CC §6.4's Consecrate
    /// is 3.5 m for 6 seconds, 3 hit points every 0.5 s — thirty-six against CC §7's 140, about a
    /// quarter of a bar for holding ground through half a wave — on a 12-second cooldown that belongs
    /// to the <c>SkillDefinition</c> rather than here. Every one of them is a first pass in M2-03's
    /// sense: the owner retunes them in <c>Consecrate.asset</c> after a playtest. <b>No asset ships
    /// here</b>: <c>Consecrate.asset</c> is M3-12c's.
    /// </para>
    /// <para>
    /// The same shape as every other definition in this folder: <c>[SerializeField] private</c>
    /// fields, one conversion that is the only way out, and every failure rewrapped as a plain
    /// <see cref="ArgumentException"/> naming the asset. It validates nothing
    /// <see cref="SpawnHealZone"/> already validates — all four doors are that constructor's, which is
    /// the single account of what a legal zone is.
    /// </para>
    /// <para>
    /// <b>No <c>OnValidate</c>, like <see cref="GrantShieldDefinition"/> and unlike
    /// <see cref="ModifyStatDefinition"/>.</b> That type warns about one thing — an address that is no
    /// longer a member of an enum, which looks like a Unity glitch in the Inspector and fails at boot.
    /// A zero or negative number here is visibly a zero or negative number in the Inspector, and
    /// <see cref="ToEffect"/> is where refusal lives.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Effects/Spawn Heal Zone", fileName = "SpawnHealZone")]
    public sealed class SpawnHealZoneDefinition : EffectDefinition
    {
        [Tooltip("How far the zone reaches from where it was cast, in metres. It is placed under " +
                 "the caster and stays there — walking out of it stops the healing, which is the " +
                 "whole decision the skill is made of. Consecrate is 3.5.")]
        [SerializeField] private float _radius = 3.5f;

        [Tooltip("How long it stands, in seconds. It burns down on the run's simulated clock, so a " +
                 "pause or a level-up screen does not spend it. Consecrate is 6.")]
        [SerializeField] private float _duration = 6f;

        [Tooltip("Hit points one pulse restores to whoever is standing in it. Consecrate is 3, " +
                 "which over six seconds of standing still is 36 of the Oathbound's 140.")]
        [SerializeField] private float _healPerPulse = 3f;

        [Tooltip("Seconds between pulses. It heals in beats rather than continuously, so a view has " +
                 "something to flash and the arithmetic is duration over interval. Consecrate is 0.5.")]
        [SerializeField] private float _pulseInterval = 0.5f;

        /// <inheritdoc/>
        /// <remarks>
        /// All four fields go to <see cref="SpawnHealZone"/> untouched, and its refusal leaves through
        /// the rewrap, so the Console message always starts with the file a designer can open (M0-11).
        /// </remarks>
        public override IEffect ToEffect()
        {
            try
            {
                return new SpawnHealZone(_radius, _duration, _healPerPulse, _pulseInterval);
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching here.
                // Uncaught, a designer reading the Console sees "SpawnHealZone radius must be a
                // finite number greater than zero" with a stack trace through the boot installer and
                // no way to tell which of the catalog's effect assets to open.
                throw new ArgumentException(
                    $"SpawnHealZoneDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }
    }
}
