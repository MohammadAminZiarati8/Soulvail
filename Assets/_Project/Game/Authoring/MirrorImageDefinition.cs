using System;
using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Mirror Image", fileName = "MirrorImageCast")]
    public sealed class MirrorImageDefinition : EffectDefinition
    {
        [SerializeField] private float _damageOfWeapon = 0.5f;

        public override IEffect ToEffect()
        {
            try
            {
                return new MirrorImage(_damageOfWeapon);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"MirrorImageDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }
    }
}
