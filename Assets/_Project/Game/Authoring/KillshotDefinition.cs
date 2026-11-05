using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Killshot", fileName = "KillshotCast")]
    public sealed class KillshotDefinition : EffectDefinition
    {
        [SerializeField] private float _windUp = 0.7f;
        [SerializeField] private float _halfWidth = 1.5f;
        [SerializeField] private float _length = 30f;
        [SerializeField] private float _damageOfWeapon = 8f;
        [SerializeField] private float _falloffPerHit = 0.15f;
        [SerializeField] private float _minFactor = 0.4f;
        [SerializeField] private float _executeBelow = 0.15f;

        [SerializeField] private int _minHits = 1;

        public override IEffect ToEffect() => new Killshot(
            _windUp, _halfWidth, _length, _damageOfWeapon, _falloffPerHit, _minFactor, _executeBelow, _minHits);
    }
}
