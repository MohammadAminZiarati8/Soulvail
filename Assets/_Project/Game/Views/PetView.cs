using System;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    public sealed class PetView : MonoBehaviour, IPoolable
    {
        private const float RunBlendSpeed = 4f;
        private const float LeapHeight = 0.6f;
        private const float HowlGlowIntensity = 0.2f;

        [SerializeField] private Animator _animator;
        [SerializeField] private PetHealthBar _healthBar;

        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int Bite = Animator.StringToHash("Bite");
        private static readonly int RoarTrigger = Animator.StringToHash("Roar");
        private static readonly int Locomotion = Animator.StringToHash("Base Layer.Locomotion");
        private static readonly int Hit = Animator.StringToHash("Base Layer.Hit");
        private static readonly int Fallen = Animator.StringToHash("Base Layer.Fall");
        private static readonly int GetUp = Animator.StringToHash("Base Layer.GetUp");

        private static readonly int LeapTrigger = Animator.StringToHash("Leap");
        private static readonly int Land = Animator.StringToHash("Base Layer.Land");

        private Vector3 _leapFrom;
        private Vector3 _leapTo;
        private float _leapSeconds;
        private float _leapElapsed;
        private Func<int, float?> _healthFraction;
        private bool _heeling;
        private bool _fallen;
        private SkinnedMeshRenderer _body;
        private Material _bodyMaterial;
        private Material _howlMaterial;
        private float _howlUntil;
        private bool _howlGlowing;

        public int Id { get; private set; }
        public Vector3 Velocity { get; private set; }

        public void Bind(int id, Vector3 position, float healthFraction, Camera camera,
            Func<int, float?> readHealthFraction = null)
        {
            Id = id;
            transform.SetPositionAndRotation(position, Quaternion.identity);
            _healthFraction = readHealthFraction;
            _fallen = false;
            _leapSeconds = 0f;
            _animator.ResetTrigger(LeapTrigger);
            _animator.ResetTrigger(RoarTrigger);
            SetHeeling(false);
            _healthBar.Bind(healthFraction, camera);
            _animator.ResetTrigger(Bite);
            _animator.SetFloat(Speed, 0f);
            _animator.Play(Locomotion, 0, 0f);
        }

        public void Leap(Vector3 to, float seconds)
        {
            if (_fallen || !(seconds > 0f))
            {
                return;
            }

            _leapFrom = transform.position;
            _leapTo = to;
            _leapElapsed = 0f;
            _leapSeconds = seconds;
            Velocity = (to - _leapFrom) / seconds;
            var facing = new Vector3(Velocity.x, 0f, Velocity.z);
            if (facing.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(facing);
            }

            _animator.ResetTrigger(Bite);
            _animator.ResetTrigger(RoarTrigger);
            _animator.SetTrigger(LeapTrigger);
        }

        public void StepLeap(float dt)
        {
            if (_leapSeconds == 0f)
            {
                return;
            }

            _leapElapsed += dt;
            float progress = Mathf.Clamp01(_leapElapsed / _leapSeconds);
            if (progress >= 1f)
            {
                transform.position = _leapTo;
                Velocity = Vector3.zero;
                _leapSeconds = 0f;
                _animator.ResetTrigger(LeapTrigger);
                _animator.SetFloat(Speed, 0f);
                _animator.Play(Land, 0, 0f);
                return;
            }

            transform.position = Vector3.Lerp(_leapFrom, _leapTo, progress)
                + Vector3.up * (4f * LeapHeight * progress * (1f - progress));
        }

        public void Apply(in EnemyMoveIntent intent, float dt)
        {
            if (_fallen || _leapSeconds > 0f)
            {
                return;
            }

            Velocity = intent.Velocity.ToUnity();
            transform.position += Velocity * dt;
            var facing = new Vector3(intent.FacingXZ.X, 0f, intent.FacingXZ.Y);
            if (facing.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(facing);
            }

            float speed = Velocity.magnitude;
            _animator.SetFloat(Speed, _heeling && speed > 0f ? RunBlendSpeed : speed);
        }

        public void PlayBite()
        {
            if (!_fallen && _leapSeconds == 0f && _animator.GetCurrentAnimatorStateInfo(0).fullPathHash != GetUp)
            {
                _animator.SetTrigger(Bite);
            }
        }

        public void Roar()
        {
            if (!_fallen && _leapSeconds == 0f)
            {
                _animator.ResetTrigger(Bite);
                _animator.SetTrigger(RoarTrigger);
            }
        }

        public void Howl(float seconds, float now)
        {
            if (_fallen)
            {
                return;
            }

            _animator.ResetTrigger(Bite);
            _animator.SetTrigger(RoarTrigger);
            if (!(seconds > 0f))
            {
                return;
            }

            if (_howlMaterial == null)
            {
                _body = GetComponentInChildren<SkinnedMeshRenderer>();
                _bodyMaterial = _body.sharedMaterial;
                _howlMaterial = new Material(_bodyMaterial);
                _howlMaterial.EnableKeyword("_EMISSION");
                _howlMaterial.SetColor("_EmissionColor", Palette.Player * HowlGlowIntensity);
            }

            _howlUntil = now + seconds;
            _howlGlowing = true;
            _body.sharedMaterial = _howlMaterial;
        }

        public void StepHowl(float now)
        {
            if (_howlGlowing && now >= _howlUntil)
            {
                ClearHowlGlow();
            }
        }

        private void ClearHowlGlow()
        {
            if (_howlGlowing)
            {
                _body.sharedMaterial = _bodyMaterial;
                _howlGlowing = false;
            }
        }

        public void PlayHit()
        {
            _animator.ResetTrigger(Bite);
            _animator.Play(Hit, 0, 0f);
            RefreshHealth();
        }

        public void SetHeeling(bool heeling)
        {
            _heeling = heeling;
            _healthBar.SetPulsing(heeling);
        }

        public void Fall(Vector3 position)
        {
            _fallen = true;
            ClearHowlGlow();
            _leapSeconds = 0f;
            _animator.ResetTrigger(LeapTrigger);
            _animator.ResetTrigger(RoarTrigger);
            Velocity = Vector3.zero;
            transform.position = position;
            _healthBar.SetVisible(false);
            _animator.ResetTrigger(Bite);
            _animator.Play(Fallen, 0, 0f);
        }

        public void Rise()
        {
            _fallen = false;
            RefreshHealth();
            _healthBar.SetVisible(true);
            SetHeeling(true);
            _animator.Play(GetUp, 0, 0f);
        }

        private void LateUpdate()
        {
            StepHowl(Time.time);
            RefreshHealth();
        }

        private void OnDestroy()
        {
            if (_howlMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_howlMaterial);
            }
            else
            {
                DestroyImmediate(_howlMaterial);
            }
        }

        private void RefreshHealth()
        {
            if (_healthFraction?.Invoke(Id) is float fraction)
            {
                _healthBar.SetFraction(fraction);
            }
        }

        public void OnSpawn() { }

        public void OnDespawn()
        {
            ClearHowlGlow();
            _healthFraction = null;
            _leapSeconds = 0f;
            Id = 0;
            Velocity = Vector3.zero;
        }
    }
}
