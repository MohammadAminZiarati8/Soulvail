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
        [SerializeField] private GameObject _poisonBubbles;
        [SerializeField] private StatusIconBook _icons;
        [SerializeField] private Sprite _sweepSprite;
        private const int Stun = 0;
        private const int Slow = 2;
        private const int Poison = 3;
        private static readonly ContentId[] Keys =
        {
            new(EnemyStatuses.StunnedKey),
            new(EnemyStatuses.RootedKey),
            new(EnemyStatuses.SlowedKey),
            new(EnemyStatuses.PoisonedKey),
        };
        private readonly GameObject[] _effects = new GameObject[4];
        private readonly ParticleSystem[] _particles = new ParticleSystem[4];
        private readonly bool[] _active = new bool[4];
        private EnemyView _body;
        private EnemyHealthBar _bar;
        private EnemyAnimatorView _animator;
        private IRunSession _session;
        private IDisposable _changed;
        private IDisposable _died;
        private int _id;

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
            GameObject[] prefabs = { _stunSparks, _rootVines, _slowFrost, _poisonBubbles };
            for (int i = 0; i < _effects.Length; i++)
            {
                if (prefabs[i] == null)
                {
                    continue;
                }
                _effects[i] = Instantiate(prefabs[i], parked.transform, false);
                _effects[i].SetActive(false);
                if (i >= Slow)
                {
                    _particles[i] = _effects[i].GetComponent<ParticleSystem>();
                    Tint(_particles[i]);
                }
            }
            parked.SetActive(true);
            _bar?.ConfigureStatuses(_icons, _sweepSprite);
            _changed = hub.Subscribe<EnemyStatusChanged>(OnChanged);
            _died = hub.Subscribe<EnemyDied>(OnDied);
        }

        /// <summary>The body effect drawn for a status; null for a status without one.</summary>
        public GameObject Effect(ContentId status)
        {
            int index = Slot(status);
            return index < 0 ? null : _effects[index];
        }

        private static int Slot(ContentId status)
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                if (Keys[i] == status)
                {
                    return i;
                }
            }
            return -1;
        }

        // The shared transparent unlit material has no particle vertex-colour input.
        // Tint each particle renderer once, without cloning a material or changing other effects.
        private static void Tint(ParticleSystem particles)
        {
            if (particles == null)
            {
                return;
            }
            var tint = new MaterialPropertyBlock();
            tint.SetColor("_BaseColor", particles.main.startColor.color);
            particles.GetComponent<ParticleSystemRenderer>().SetPropertyBlock(tint);
        }

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
                _effects[i].transform.localScale = Vector3.one * (i == Stun ? 1f : radius * 2f);
                _effects[i].transform.localPosition = Vector3.up * Height(i, capsule);
                _effects[i].transform.localRotation = Quaternion.identity;
            }
        }

        private static float Height(int index, CapsuleCollider capsule) => index switch
        {
            Stun => capsule != null ? capsule.center.y + capsule.height * 0.5f + 0.2f : 2f,
            Poison => capsule != null ? capsule.center.y : 1f,
            _ => 0.06f,
        };

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
                _particles[i]?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            _animator?.SetCrowdControl(false, false, 0f);
            _bar?.ClearStatuses();
        }

        public void Step(float dt)
        {
            if (_active[Stun] && _effects[Stun] != null && dt > 0f && float.IsFinite(dt))
            {
                _effects[Stun].transform.Rotate(Vector3.up, 150f * dt, Space.Self);
            }
            ApplyAnimator();
        }

        private void Update() => Step(Time.deltaTime);

        private void ApplyAnimator()
        {
            float slow = _active[Slow] ? _session?.State?.EnemySlowFraction(_id) ?? 0f : 0f;
            _animator?.SetCrowdControl(_active[Stun], _active[1], slow);
        }

        private void OnChanged(EnemyStatusChanged evt)
        {
            int index = Slot(evt.Status);
            if (_id == EnemyView.Unbound || evt.EnemyId != _id || index < 0)
            {
                return;
            }
            _active[index] = evt.Seconds > 0f;
            if (_effects[index] != null)
            {
                _effects[index].SetActive(_active[index]);
            }
            ParticleSystem particles = _particles[index];
            if (particles != null)
            {
                if (_active[index])
                {
                    particles.Play(true);
                }
                else
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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
