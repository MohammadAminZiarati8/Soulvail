using UnityEngine;

namespace Soulvail.Showcase
{
    /// <summary>
    /// Showcase-only fireball: flies forward, sweeps for a hit, spawns an impact and dies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a scratch rig for looking at the effect, <strong>not</strong> the shipping
    /// projectile. In the real game core owns the flight and the hit, and a view like
    /// <c>ProjectileView</c> only renders what core reports — a projectile that decides its own
    /// collisions in a MonoBehaviour is exactly the shape the architecture forbids. Keep it that
    /// way when this graduates: lift the art, drop the simulation.
    /// </para>
    /// <para>
    /// It also instantiates instead of pooling, so every shot allocates. Fine for a showcase scene,
    /// not fine for a run — and pooling brings the trail-and-particle reset problem with it.
    /// </para>
    /// </remarks>
    public class Fireball : MonoBehaviour
    {
        [SerializeField] private Transform _core;
        [SerializeField] private GameObject _impactPrefab;

        [Header("Flight")]
        [SerializeField] private float _speed = 14f;
        [SerializeField] private float _maxRange = 12f;
        [SerializeField] private float _castRadius = 0.2f;
        [SerializeField] private LayerMask _hitMask = ~0;

        [Header("Look")]
        [SerializeField] private float _spinDegreesPerSecond = 220f;
        [SerializeField] private float _pulseAmplitude = 0.08f;
        [SerializeField] private float _pulseFrequency = 7f;

        private Vector3 _spawnPosition;
        private Vector3 _coreScale;
        private float _age;

        private void Awake()
        {
            _spawnPosition = transform.position;
            _coreScale = _core != null ? _core.localScale : Vector3.one;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            _age += deltaTime;

            float step = _speed * deltaTime;
            Vector3 from = transform.position;

            // Sweep rather than test a point: at 14 m/s a fixed-timestep point test walks straight
            // through anything thinner than half a metre.
            if (Physics.SphereCast(
                    from,
                    _castRadius,
                    transform.forward,
                    out RaycastHit hit,
                    step,
                    _hitMask,
                    QueryTriggerInteraction.Ignore))
            {
                Impact(hit.point, hit.normal);
                return;
            }

            transform.position = from + (transform.forward * step);
            Animate(deltaTime);

            if ((transform.position - _spawnPosition).sqrMagnitude >= _maxRange * _maxRange)
            {
                Impact(transform.position, -transform.forward);
            }
        }

        private void Animate(float deltaTime)
        {
            if (_core == null)
            {
                return;
            }

            _core.Rotate(new Vector3(0.7f, 1f, 0.3f) * (_spinDegreesPerSecond * deltaTime), Space.Self);

            float pulse = 1f + (Mathf.Sin(_age * _pulseFrequency * Mathf.PI * 2f) * _pulseAmplitude);
            _core.localScale = _coreScale * pulse;
        }

        private void Impact(Vector3 position, Vector3 normal)
        {
            if (_impactPrefab != null)
            {
                // The impact's ring lies in its local XZ plane, so stand the prefab up on the
                // surface normal rather than aiming its forward at it.
                Vector3 up = normal.sqrMagnitude > 0.0001f ? normal : Vector3.up;
                Instantiate(_impactPrefab, position, Quaternion.FromToRotation(Vector3.up, up));
            }

            Destroy(gameObject);
        }
    }
}
