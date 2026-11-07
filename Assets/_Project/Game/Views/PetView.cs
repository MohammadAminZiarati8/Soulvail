using System;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using UnityEngine;

namespace Soulvail.Game.Views
{
    public sealed class PetView : MonoBehaviour, IPoolable
    {
        private const float RunBlendSpeed = 4f;

        [SerializeField] private Animator _animator;
        [SerializeField] private PetHealthBar _healthBar;

        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int Bite = Animator.StringToHash("Bite");
        private static readonly int Locomotion = Animator.StringToHash("Base Layer.Locomotion");
        private static readonly int Hit = Animator.StringToHash("Base Layer.Hit");
        private static readonly int Fallen = Animator.StringToHash("Base Layer.Fall");
        private static readonly int GetUp = Animator.StringToHash("Base Layer.GetUp");

        private Func<int, float?> _healthFraction;
        private bool _heeling;
        private bool _fallen;

        public int Id { get; private set; }
        public Vector3 Velocity { get; private set; }

        public void Bind(int id, Vector3 position, float healthFraction, Camera camera,
            Func<int, float?> readHealthFraction = null)
        {
            Id = id;
            transform.SetPositionAndRotation(position, Quaternion.identity);
            _healthFraction = readHealthFraction;
            _fallen = false;
            SetHeeling(false);
            _healthBar.Bind(healthFraction, camera);
            _animator.ResetTrigger(Bite);
            _animator.SetFloat(Speed, 0f);
            _animator.Play(Locomotion, 0, 0f);
        }

        public void Apply(in EnemyMoveIntent intent, float dt)
        {
            if (_fallen)
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
            if (!_fallen && _animator.GetCurrentAnimatorStateInfo(0).fullPathHash != GetUp)
            {
                _animator.SetTrigger(Bite);
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

        private void LateUpdate() => RefreshHealth();

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
            _healthFraction = null;
            Id = 0;
            Velocity = Vector3.zero;
        }
    }
}
