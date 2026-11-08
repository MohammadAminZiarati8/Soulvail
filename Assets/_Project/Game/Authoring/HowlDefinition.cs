using Soulvail.Core.Effects;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    [CreateAssetMenu(menuName = "Soulvail/Effects/Howl", fileName = "HowlCast")]
    public sealed class HowlDefinition : EffectDefinition
    {
        public override IEffect ToEffect() => new Howl();
    }
}
