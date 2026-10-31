using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Hunter's Snare", fileName = "HuntersSnareCast")]
    public sealed class HuntersSnareDefinition : EffectDefinition
    {
        [SerializeField] private float _radius = 0.8f;
        [SerializeField] private float _armDelay = 0.3f;
        [SerializeField] private float _lifetime = 10f;
        [SerializeField] private float _rootSeconds = 2f;
        [SerializeField] private float _damageOfWeapon = 3f;

        public override IEffect ToEffect() => new PlaceSnare(
            _radius, _armDelay, _lifetime, _rootSeconds, _damageOfWeapon);
    }
}
