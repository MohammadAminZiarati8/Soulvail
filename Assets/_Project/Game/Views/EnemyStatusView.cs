using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Body cosmetics for core's crowd-control events. Built once per pooled body.</summary>
    [RequireComponent(typeof(EnemyView))]
    public sealed class EnemyStatusView : MonoBehaviour
    {
        [SerializeField] private GameObject _stunSparks;
        [SerializeField] private GameObject _rootVines;
        [SerializeField] private GameObject _slowFrost;
        [SerializeField] private StatusIconBook _icons;
        [SerializeField] private Sprite _sweepSprite;
        private readonly GameObject[] _effects = new GameObject[3];
        private readonly bool[] _active = new bool[3];
        private EnemyView _body;
        private EnemyHealthBar _bar;
        private EnemyAnimatorView _animator;
        private IRunSession _session;
        private IDisposable _changed;
        private IDisposable _died;
        private int _id;
        private ParticleSystem _frostParticles;

        [Inject]
        public void Construct(DomainEventHub hub, IRunSession session)
        {
            _session = session;
            _body = GetComponent<EnemyView>();
            _bar = GetComponent<EnemyHealthBar>();
            _animator = GetComponentInChildren<EnemyAnimatorView>(true);
            var parked = new GameObject("Status effects (pooled)");
            parked.SetActive(false);
            parked.transform.SetParent(transform, false);
            GameObject[] prefabs = { _stunSparks, _rootVines, _slowFrost };
            for (int i = 0; i < _effects.Length; i++)
            {
                if (prefabs[i] == null)
                {
                    continue;
                }
                _effects[i] = Instantiate(prefabs[i], parked.transform, false);
                _effects[i].SetActive(false);
            }
            parked.SetActive(true);
            _frostParticles = _effects[2] != null ? _effects[2].GetComponent<ParticleSystem>() : null;
            if (_frostParticles != null)
            {
                // The shared transparent unlit material has no particle vertex-colour input.
                // Tint this renderer once, without cloning a material or changing other effects.
                var tint = new MaterialPropertyBlock();
                tint.SetColor("_BaseColor", _frostParticles.main.startColor.color);
                _frostParticles.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(tint);
            }
            _bar?.ConfigureStatuses(_icons, _sweepSprite);
            _changed = hub.Subscribe<EnemyStatusChanged>(OnChanged);
            _died = hub.Subscribe<EnemyDied>(OnDied);
        }

        public GameObject Effect(ContentId status) => _effects[Slot(status)];

        private static int Slot(ContentId status) => status.Value switch
        {
            EnemyStatuses.StunnedKey => 0,
            EnemyStatuses.RootedKey => 1,
            EnemyStatuses.SlowedKey => 2,
            EnemyStatuses.HastedKey => 3,
            _ => -1,
        };


        public void Bind(int id)
        {
            Unbind();
            _id = id;
            var capsule = _body.Body as CapsuleCollider;
            float radius = capsule != null ? capsule.radius : 0.5f;
            for (int i = 0; i < _effects.Length; i++)
            {
                if (_effects[i] == null)
                {
                    continue;
                }
                _effects[i].transform.localScale = Vector3.one * (i == 0 ? 1f : radius * 2f);
                _effects[i].transform.localPosition = i == 0
                    ? Vector3.up * (capsule != null ? capsule.center.y + capsule.height * 0.5f + 0.2f : 2f)
                    : Vector3.up * 0.06f;
                _effects[i].transform.localRotation = Quaternion.identity;
            }
        }

        public void Unbind()
        {
            _id = EnemyView.Unbound;
            for (int i = 0; i < _active.Length; i++)
            {
                _active[i] = false;
                if (_effects[i] != null)
                {
                    _effects[i].SetActive(false);
                }
            }
            _animator?.SetCrowdControl(false, false, 0f);
            _frostParticles?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _bar?.ClearStatuses();
        }

        public void Step(float dt)
        {
            if (_active[0] && _effects[0] != null && dt > 0f && float.IsFinite(dt))
            {
                _effects[0].transform.Rotate(Vector3.up, 150f * dt, Space.Self);
            }
            ApplyAnimator();
        }

        private void Update() => Step(Time.deltaTime);

        private void ApplyAnimator()
        {
            float slow = _active[2] ? _session?.State?.EnemySlowFraction(_id) ?? 0f : 0f;
            _animator?.SetCrowdControl(_active[0], _active[1], slow);
        }

        private void OnChanged(EnemyStatusChanged evt)
        {
            int index = Slot(evt.Status);
            if (_id == EnemyView.Unbound || evt.EnemyId != _id || (uint)index >= 3u)
            {
                return;
            }
            _active[index] = evt.Seconds > 0f;
            if (_effects[index] != null)
            {
                _effects[index].SetActive(_active[index]);
            }
            if (index == 2 && _frostParticles != null)
            {
                if (_active[index])
                {
                    _frostParticles.Play(true);
                }
                else
                {
                    _frostParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
            ApplyAnimator();
        }

        private void OnDied(EnemyDied evt)
        {
            if (_id != EnemyView.Unbound && evt.Id == _id)
            {
                Unbind();
            }
        }

        private void OnDestroy()
        {
            _changed?.Dispose();
            _died?.Dispose();
        }
    }
}
