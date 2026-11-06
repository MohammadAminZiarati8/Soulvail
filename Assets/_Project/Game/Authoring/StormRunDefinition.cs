using System;
using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Storm Run", fileName = "StormRunCast")]
    public sealed class StormRunDefinition : EffectDefinition
    {
        [SerializeField] private float _damageOfWeapon = 0.8f;

        public override IEffect ToEffect()
        {
            try
            {
                return new StormRun(_damageOfWeapon);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"StormRunDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }
    }
}
