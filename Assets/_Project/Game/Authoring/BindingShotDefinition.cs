using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Binding Shot", fileName = "BindingShotCast")]
    public sealed class BindingShotDefinition : EffectDefinition
    {
        [SerializeField] private float _reach = 4f;
        [SerializeField] private float _coneHalfAngleDeg = 20f;
        [SerializeField] private float _boundSeconds = 2f;
        [SerializeField] private float _aloneSeconds = 0.75f;
        [SerializeField] private float _damageOfWeapon = 2f;

        public override IEffect ToEffect() => new BindingShot(
            _reach, _coneHalfAngleDeg, _boundSeconds, _aloneSeconds, _damageOfWeapon);
    }
}
