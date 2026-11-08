using System;
using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Howl", fileName = "HowlCast")]
    public sealed class HowlDefinition : EffectDefinition
    {
        [SerializeField] private float _attackSpeedBonus = 0.25f;

        public override IEffect ToEffect()
        {
            try
            {
                return new Howl(_attackSpeedBonus);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"HowlDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }
    }
}
