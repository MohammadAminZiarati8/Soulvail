using Soulvail.Core.Effects;
using UnityEngine;

// Block namespace, deliberately: a ScriptableObject declared file-scoped loads as null (Traps §5).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// BT-06b: the Packleader's Lv-20 cast. No fields: the bonus and the length are
    /// <c>FrenzyBonus</c> and <c>FrenzySeconds</c>, which the ranks raise.
    /// </summary>
    [CreateAssetMenu(menuName = "Soulvail/Effects/Frenzy", fileName = "FrenzyCast")]
    public sealed class FrenzyDefinition : EffectDefinition
    {
        public override IEffect ToEffect() => new Frenzy();
    }
}
