using Soulvail.Game.Pooling;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    public sealed class ShadeView : MonoBehaviour, IPoolable
    {
        private const float FadeInSeconds = 0.15f;
        private const float FadeOutSeconds = 0.3f;

        [SerializeField] private Animator _animator;
        [SerializeField] private Renderer[] _renderers;

        private readonly int _baseColor = Shader.PropertyToID("_BaseColor");
        private readonly int _aiming = Animator.StringToHash("Aiming");
        private readonly int _release = Animator.StringToHash("Upper Body.Release");
        private MaterialPropertyBlock _properties;
        private Color _color;
        private int _upperBody;
        private float _opacity;
        private float _fadeFrom;
        private float _fadeLeft;

        [Inject]
        private void Construct()
        {
            _properties = new MaterialPropertyBlock();
            _color = _renderers[0].sharedMaterial.GetColor(_baseColor);
        }

        public bool IsFading { get; private set; }

        public void OnSpawn()
        {
        }

        public void Appear(Vector3 position, Quaternion facing)
        {
            transform.SetPositionAndRotation(position, facing);
            _animator.Rebind();
            _upperBody = _animator.GetLayerIndex("Upper Body");
            _animator.SetLayerWeight(_upperBody, 1f);
            _animator.SetBool(_aiming, true);
            _animator.Play("Upper Body.Aim", _upperBody, 0f);
            _fadeLeft = FadeInSeconds;
            SetOpacity(0f);
        }

        public void Loose(Vector3 target)
        {
            Vector3 direction = target - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.000001f)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }

            _animator.Play(_release, _upperBody, 0f);
        }

        public void Fade()
        {
            IsFading = true;
            _fadeFrom = _opacity;
            _fadeLeft = FadeOutSeconds;
        }

        public bool Step(float dt)
        {
            if (_fadeLeft > 0f)
            {
                _fadeLeft = Mathf.Max(0f, _fadeLeft - dt);
                SetOpacity(IsFading ? _fadeFrom * (_fadeLeft / FadeOutSeconds) : 1f - (_fadeLeft / FadeInSeconds));
            }
            return IsFading && _fadeLeft == 0f;
        }

        public void OnDespawn()
        {
            IsFading = false;
        }

        private void SetOpacity(float opacity)
        {
            _opacity = opacity;
            Color tint = _color;
            tint.a *= opacity;
            _properties.SetColor(_baseColor, tint);
            foreach (Renderer renderer in _renderers)
            {
                renderer.SetPropertyBlock(_properties);
            }
        }
    }
}
