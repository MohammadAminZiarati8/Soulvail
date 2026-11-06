using System;
using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Crossfire", fileName = "CrossfireCast")]
    public sealed class CrossfireDefinition : EffectDefinition
    {
        [SerializeField] private float _damageOfWeapon = 1.5f;

        public override IEffect ToEffect()
        {
            try
            {
                return new Crossfire(_damageOfWeapon);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"CrossfireDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }
    }
}
