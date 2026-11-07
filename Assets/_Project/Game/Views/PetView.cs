using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using UnityEngine;

namespace Soulvail.Game.Views
{
    public sealed class PetView : MonoBehaviour, IPoolable
    {
        [SerializeField] private Animator _animator;
        [SerializeField] private PetHealthBar _healthBar;

        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int Bite = Animator.StringToHash("Bite");

        public int Id { get; private set; }
        public Vector3 Velocity { get; private set; }

        public void Bind(int id, Vector3 position, float healthFraction, Camera camera)
        {
            Id = id;
            transform.SetPositionAndRotation(position, Quaternion.identity);
            _healthBar.Bind(healthFraction, camera);
        }

        public void Apply(in EnemyMoveIntent intent, float dt)
        {
            Velocity = intent.Velocity.ToUnity();
            transform.position += Velocity * dt;
            var facing = new Vector3(intent.FacingXZ.X, 0f, intent.FacingXZ.Y);
            if (facing.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(facing);
            }

            _animator.SetFloat(Speed, Velocity.magnitude);
        }

        public void PlayBite() => _animator.SetTrigger(Bite);

        public void OnSpawn() { }

        public void OnDespawn()
        {
            Id = 0;
            Velocity = Vector3.zero;
        }
    }
}
