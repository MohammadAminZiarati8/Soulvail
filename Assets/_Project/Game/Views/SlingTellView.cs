using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>Lights the loaded stone and whirls the sling during the Jackal's wind-up.</summary>
    public sealed class SlingTellView : MonoBehaviour
    {
        [SerializeField] private Renderer _stone;
        [SerializeField] private Transform _sling;
        [SerializeField] private float _turnsPerSecond = 2.5f;

        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _properties;
        private EnemyView _body;
        private IDisposable _telegraph;
        private IDisposable _fired;
        private IDisposable _died;
        private Quaternion _restRotation;
        private float _remaining;

        public bool IsShowing => _remaining > 0f;

        [Inject]
        public void Construct(DomainEventHub hub)
        {
            _body = GetComponentInParent<EnemyView>(true);
            _telegraph = hub.Subscribe<EnemyTelegraph>(OnTelegraph);
            _fired = hub.Subscribe<ProjectileFired>(evt => HideFor(evt.SourceId));
            _died = hub.Subscribe<EnemyDied>(evt => HideFor(evt.Id));
        }

        public void Step(float dt)
        {
            if (!IsShowing || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            if (dt >= _remaining)
            {
                Hide();
                return;
            }

            _remaining -= dt;
            // Pre-multiply to spin about the hand's local up, regardless of the sling's grip angle.
            _sling.localRotation = Quaternion.AngleAxis(360f * _turnsPerSecond * dt, Vector3.up) *
                _sling.localRotation;
        }

        private void Update() => Step(Time.deltaTime);

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            _telegraph?.Dispose();
            _fired?.Dispose();
            _died?.Dispose();
        }

        private bool IsMine(int id) => _body != null && _body.IsBound && id == _body.Id;

        private void OnTelegraph(EnemyTelegraph evt)
        {
            if (!isActiveAndEnabled || _stone == null || _sling == null || !IsMine(evt.Id) ||
                !(evt.Duration > 0f) || float.IsInfinity(evt.Duration))
            {
                return;
            }

            if (!IsShowing)
            {
                _restRotation = _sling.localRotation;
            }

            _remaining = evt.Duration;
            _properties ??= new MaterialPropertyBlock();
            _stone.GetPropertyBlock(_properties);
            _properties.SetColor(_baseColorId, Palette.Danger);
            _stone.SetPropertyBlock(_properties);
            _stone.enabled = true;
        }

        private void HideFor(int id)
        {
            if (IsMine(id))
            {
                Hide();
            }
        }

        private void Hide()
        {
            if (!IsShowing)
            {
                return;
            }

            _remaining = 0f;
            _stone.enabled = false;
            _sling.localRotation = _restRotation;
        }
    }
}
