using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>The authored model, locomotion clips, and weapon for the King's presentation.</summary>
    internal sealed class BuriedKingRig : MonoBehaviour
    {
        [SerializeField] private Transform _model;
        [SerializeField] private AnimationClip _walk;
        [SerializeField] private AnimationClip _run;
        [SerializeField] private GameObject _sword;
        [SerializeField] private SlamDustView _landingDust;

        public Transform Model => _model;
        public AnimationClip Walk => _walk;
        public AnimationClip Run => _run;
        public GameObject Sword => _sword;
        public SlamDustView LandingDust => _landingDust;
    }
}
