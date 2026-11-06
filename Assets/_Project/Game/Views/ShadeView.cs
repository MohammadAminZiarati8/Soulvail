using Soulvail.Core.Combat;
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
        private ShadeKind _kind;
        private float _pulseTime;
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

        public void Appear(Vector3 position, Quaternion facing, ShadeKind kind)
        {
            transform.SetPositionAndRotation(position, facing);
            _animator.Rebind();
            _upperBody = _animator.GetLayerIndex("Upper Body");
            _kind = kind;
            _pulseTime = 0f;
            bool decoy = kind == ShadeKind.Decoy;
            _animator.SetLayerWeight(_upperBody, decoy ? 0f : 1f);
            _animator.SetBool(_aiming, !decoy);
            if (!decoy)
            {
                _animator.Play("Upper Body.Aim", _upperBody, 0f);
            }
            _fadeLeft = FadeInSeconds;
            SetOpacity(0f);
        }

        public void Loose(Vector3 target)
        {
            if (_kind == ShadeKind.Decoy)
            {
                return;
            }

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
            else if (_kind == ShadeKind.Decoy && !IsFading)
            {
                _pulseTime += dt;
                SetOpacity(0.85f + 0.15f * Mathf.Cos(_pulseTime * 2f * Mathf.PI));
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
