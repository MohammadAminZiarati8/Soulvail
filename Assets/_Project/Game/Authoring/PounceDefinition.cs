using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Pounce", fileName = "PounceCast")]
    public sealed class PounceDefinition : EffectDefinition
    {
        [SerializeField] private float _minReach = 3f;
        [SerializeField] private float _maxReach = 6f;
        [SerializeField] private float _biteMultiplier = 2f;
        [SerializeField] private float _knockdownSeconds = 0.8f;
        [SerializeField] private float _leapSeconds = 0.35f;

        public override IEffect ToEffect() =>
            new Pounce(_minReach, _maxReach, _biteMultiplier, _knockdownSeconds, _leapSeconds);
    }
}
