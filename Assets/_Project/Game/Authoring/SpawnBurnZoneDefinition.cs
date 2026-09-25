using System;
using Soulvail.Core.Effects;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script importer
// parses a file to find the type it declares and does not understand `namespace X;`, so a
// ScriptableObject declared that way is never linked to a MonoScript: a SpawnBurnZone asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11, Traps §5).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// The seventh primitive's Inspector half: how far a burning zone reaches, how long it burns, and
    /// what one pulse of it takes. The authoring side of <see cref="SpawnBurnZone"/>, and
    /// <see cref="SpawnHealZoneDefinition"/>'s shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three numbers, all authored</b> (M6-08 rule 1). The pulse interval is not one of them: a burn
    /// beats at the Blink pool's 0.5 s, <c>MovementSkillSpec.PoolPulseInterval</c>. Emberfall is
    /// 4 m · 4 s · 6 and Cinder Nova 6 m · 1.5 s · 10; the owner retunes both here.
    /// </para>
    /// <para>
    /// It validates nothing <see cref="SpawnBurnZone"/> already validates, and has no
    /// <c>OnValidate</c> for <see cref="SpawnHealZoneDefinition"/>'s reason: a zero here is visibly a
    /// zero, and <see cref="ToEffect"/> is where refusal lives.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Effects/Spawn Burn Zone", fileName = "SpawnBurnZone")]
    public sealed class SpawnBurnZoneDefinition : EffectDefinition
    {
        [Tooltip("How far the fire reaches from where it was cast, in metres. It is placed under " +
                 "the caster and stays there. Emberfall is 4, Cinder Nova 6.")]
        [SerializeField] private float _radius = 4f;

        [Tooltip("How long it burns, in seconds, on the run's simulated clock. It pulses every 0.5 s, " +
                 "so this over 0.5 is the pulse count. Emberfall is 4, Cinder Nova 1.5.")]
        [SerializeField] private float _duration = 4f;

        [Tooltip("What one pulse takes off every enemy standing in it. Emberfall is 6, which over " +
                 "eight pulses is 48; Cinder Nova is 10, which over three is 30.")]
        [SerializeField] private float _damagePerPulse = 6f;

        /// <inheritdoc/>
        /// <remarks>
        /// All three fields go to <see cref="SpawnBurnZone"/> untouched, and its refusal leaves
        /// through the rewrap, so the Console message starts with the file a designer can open.
        /// </remarks>
        public override IEffect ToEffect()
        {
            try
            {
                return new SpawnBurnZone(_radius, _duration, _damagePerPulse);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException(
                    $"SpawnBurnZoneDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }
    }
}
