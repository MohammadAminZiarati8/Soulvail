using System;
using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Rain of Arrows", fileName = "RainOfArrows")]
    public sealed class RainOfArrowsDefinition : EffectDefinition
    {
        [SerializeField] private float _radius = 1.5f;
        [SerializeField] private float _duration = 2f;
        [SerializeField] private float _pulseInterval = 0.25f;
        [SerializeField] private float _damageOfWeapon = 0.6f;

        public override IEffect ToEffect()
        {
            try
            {
                return new RainOfArrows(_radius, _duration, _pulseInterval, _damageOfWeapon);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"RainOfArrowsDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }
    }
}
