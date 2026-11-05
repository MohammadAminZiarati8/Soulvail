using System;
using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Ricochet Shot", fileName = "RicochetShotCast")]
    public sealed class RicochetShotDefinition : EffectDefinition
    {
        [SerializeField] private float _damageOfWeapon = 1f;

        public override IEffect ToEffect()
        {
            try
            {
                return new RicochetShot(_damageOfWeapon);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"RicochetShotDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }
    }
}
