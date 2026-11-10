using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    public sealed class VentTellView : MonoBehaviour
    {
        [SerializeField] private Renderer _vent;

        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock _properties;
        private EnemyView _body;
        private IDisposable _telegraph;
        private IDisposable _fired;
        private IDisposable _dropped;
        private IDisposable _died;
        private float _remaining;

        public bool IsShowing => _vent != null && _vent.enabled;
        public Transform Muzzle => _vent != null ? _vent.transform.parent : null;

        [Inject]
        public void Construct(DomainEventHub hub)
        {
            _body = GetComponentInParent<EnemyView>(true);
            _telegraph = hub.Subscribe<EnemyTelegraph>(OnTelegraph);
            _fired = hub.Subscribe<ProjectileFired>(evt => HideFor(evt.SourceId));
            _dropped = hub.Subscribe<EnemyAimDropped>(evt => HideFor(evt.Id));
            _died = hub.Subscribe<EnemyDied>(evt => HideFor(evt.Id));
            Hide();
        }

        public void Step(float dt)
        {
            if (!IsShowing || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _remaining -= dt;
            if (!(_remaining > 0f))
            {
                Hide();
            }
        }

        private void Update() => Step(Time.deltaTime);

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            _telegraph?.Dispose();
            _fired?.Dispose();
            _dropped?.Dispose();
            _died?.Dispose();
        }

        private bool IsMine(int id) => _body != null && _body.IsBound && id == _body.Id;

        private void OnTelegraph(EnemyTelegraph evt)
        {
            if (!isActiveAndEnabled || _vent == null || !IsMine(evt.Id) ||
                !(evt.Duration > 0f) || float.IsInfinity(evt.Duration))
            {
                return;
            }

            _remaining = evt.Duration;
            _properties ??= new MaterialPropertyBlock();
            _vent.GetPropertyBlock(_properties);
            _properties.SetColor(_baseColorId, Palette.Danger);
            _vent.SetPropertyBlock(_properties);
            _vent.enabled = true;
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
            _remaining = 0f;
            if (_vent != null)
            {
                _vent.enabled = false;
            }
        }
    }
}
