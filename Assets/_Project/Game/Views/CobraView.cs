using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Renders the cobra's locked warning and spit for the life bound to this body.</summary>
    public sealed class CobraView : MonoBehaviour
    {
        private const float MouthHeight = 0.5f;
        private const float SpitSpeed = 24f;

        [SerializeField] private SprayConeView _cone;
        [SerializeField] private ParticleSystem _spit;

        private EnemyView _body;
        private int _enemyId;
        private int _deadId;
        private Vector3 _apex;
        private IDisposable _armed;
        private IDisposable _spat;
        private IDisposable _died;

        [Inject]
        public void Construct(DomainEventHub events)
        {
            _body = GetComponent<EnemyView>();
            _armed = events.Subscribe<CobraConeArmed>(OnArmed);
            _spat = events.Subscribe<CobraSpat>(OnSpat);
            _died = events.Subscribe<EnemyDied>(OnDied);
        }

        public void Bind(int enemyId)
        {
            Clear();
            _enemyId = enemyId;
            _deadId = 0;
            GetComponentInChildren<EnemyAnimatorView>(true)?.Forget();
        }

        private bool IsMine(int id)
        {
            // EnemyViews binds the EnemyView after activating a rental.
            int currentId = _enemyId != EnemyView.Unbound ? _enemyId : (_body != null ? _body.Id : EnemyView.Unbound);
            return isActiveAndEnabled && id > 0 && id == currentId && id != _deadId;
        }

        private void OnArmed(CobraConeArmed evt)
        {
            if (!IsMine(evt.Id))
            {
                return;
            }

            _apex = evt.Apex.ToUnity();
            _cone.Arm(_apex, evt.Direction.ToUnity(), evt.HalfAngleDegrees, evt.Length, evt.Seconds);
        }

        private void OnSpat(CobraSpat evt)
        {
            if (!IsMine(evt.Id) || !_cone.IsLive)
            {
                return;
            }

            StopSpit();
            _spit.Play();
            Vector3 mouth = _apex + Vector3.up * MouthHeight;

            // Each world-space jet follows a ray of the warning and expires at its clipped end.
            for (int i = 0; i < SprayConeView.Rays; i++)
            {
                float length = _cone.RayLength(i);
                if (!(length > 0f))
                {
                    continue;
                }

                Vector3 travel = _cone.RayDirection(i) * length - Vector3.up * MouthHeight;
                var drop = new ParticleSystem.EmitParams
                {
                    position = mouth,
                    velocity = travel.normalized * SpitSpeed,
                    startLifetime = travel.magnitude / SpitSpeed,
                };
                _spit.Emit(drop, 1);
                drop.velocity *= 0.65f;
                drop.startLifetime /= 0.65f;
                _spit.Emit(drop, 1);
            }

            _cone.Clear();
        }

        private void OnDied(EnemyDied evt)
        {
            if (IsMine(evt.Id))
            {
                _deadId = evt.Id;
                Clear();
            }
        }

        private void LateUpdate()
        {
            if (_cone.IsLive)
            {
                // The nested fan belongs to the rental, but its pose belongs to the locked cone.
                _cone.transform.SetPositionAndRotation(
                    new Vector3(_apex.x, SprayConeView.GroundOffset, _apex.z), Quaternion.identity);
                _cone.Step(Time.deltaTime);
            }
        }

        private void StopSpit() => _spit.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

        private void Clear()
        {
            _cone.Clear();
            StopSpit();
        }

        private void OnDisable()
        {
            _enemyId = 0;
            _deadId = 0;
            Clear();
        }

        private void OnDestroy()
        {
            _armed?.Dispose();
            _spat?.Dispose();
            _died?.Dispose();
        }
    }
}
