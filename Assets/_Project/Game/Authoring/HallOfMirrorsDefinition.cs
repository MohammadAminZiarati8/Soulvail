using System;
using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/HallOfMirrors", fileName = "HallOfMirrorsCast")]
    public sealed class HallOfMirrorsDefinition : EffectDefinition
    {
        [SerializeField] private float _damageOfWeapon = 0.7f;

        public override IEffect ToEffect()
        {
            try
            {
                return new HallOfMirrors(_damageOfWeapon);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"HallOfMirrorsDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }
    }
}
