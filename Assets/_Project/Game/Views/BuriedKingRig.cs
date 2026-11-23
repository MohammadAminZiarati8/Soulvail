using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>The authored model, locomotion clip, and weapon for the King's presentation.</summary>
    internal sealed class BuriedKingRig : MonoBehaviour
    {
        [SerializeField] private Transform _model;
        [SerializeField] private AnimationClip _walk;
        [SerializeField] private GameObject _sword;
        [SerializeField] private SlamDustView _landingDust;

        public Transform Model => _model;
        public AnimationClip Walk => _walk;
        public GameObject Sword => _sword;
        public SlamDustView LandingDust => _landingDust;
    }
}
