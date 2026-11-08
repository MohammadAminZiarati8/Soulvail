using System;
using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Call Hawk", fileName = "HawkCast")]
    public sealed class CallHawkDefinition : EffectDefinition
    {
        [SerializeField] private float _damageOfWeapon = 2f;
        [SerializeField] private float _stunSeconds = 0.4f;

        public override IEffect ToEffect()
        {
            try
            {
                return new CallHawk(_damageOfWeapon, _stunSeconds);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"CallHawkDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }
    }
}
