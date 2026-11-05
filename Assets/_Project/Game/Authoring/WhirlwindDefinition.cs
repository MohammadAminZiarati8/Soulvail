using System;
using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Whirlwind", fileName = "WhirlwindCast")]
    public sealed class WhirlwindDefinition : EffectDefinition
    {
        [SerializeField] private float _damageOfWeapon = 0.6f;

        public override IEffect ToEffect()
        {
            try
            {
                return new Whirlwind(_damageOfWeapon);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"WhirlwindDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }
    }
}
