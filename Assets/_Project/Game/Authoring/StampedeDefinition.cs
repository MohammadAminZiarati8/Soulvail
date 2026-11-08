using System;
using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Stampede", fileName = "StampedeCast")]
    public sealed class StampedeDefinition : EffectDefinition
    {
        [SerializeField] private float _knockdownSeconds = 0.8f;

        public override IEffect ToEffect()
        {
            try
            {
                return new Stampede(_knockdownSeconds);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"StampedeDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }
    }
}
