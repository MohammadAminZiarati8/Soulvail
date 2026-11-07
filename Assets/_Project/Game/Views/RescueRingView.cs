using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    public sealed class RescueRingView : MonoBehaviour
    {
        private const int Segments = 64;
        private const float FadeSeconds = 0.3f;
        private const float Lift = 0.06f;

        [SerializeField] private Material _material;
        [SerializeField, Min(0f)] private float _radius = 2.5f;

        private LineRenderer _track;
        private LineRenderer _arc;
        private IDisposable[] _subscriptions;
        private int? _petId;
        private float _fadeRemaining;

        public void Construct(DomainEventHub hub, float radius)
        {
            _radius = radius;
            _track = CreateLine("Track", 0.06f, Lift);
            _arc = CreateLine("Fill", 0.12f, Lift + 0.01f);
            Draw(_track, 1f);
            Clear();
            _subscriptions = new IDisposable[]
            {
                hub.Subscribe<PetFell>(OnFell),
                hub.Subscribe<RescueProgressed>(OnProgressed),
                hub.Subscribe<PetRose>(OnRose),
                hub.Subscribe<StageTransitionStarted>(_ => Clear()),
                hub.Subscribe<StageArrived>(_ => Clear()),
                hub.Subscribe<RunEnded>(_ => Clear()),
            };
        }

        private void Update() => Step(Time.deltaTime);

        public void Step(float dt)
        {
            if (_fadeRemaining > 0f)
            {
                _fadeRemaining -= dt;
                if (_fadeRemaining > 0f)
                {
                    Tint(_fadeRemaining / FadeSeconds);
                }
                else
                {
                    Clear();
                }
            }
        }

        private void OnDestroy()
        {
            if (_subscriptions == null)
            {
                return;
            }

            foreach (IDisposable subscription in _subscriptions)
            {
                subscription.Dispose();
            }
        }

        private void OnFell(PetFell evt)
        {
            _petId = evt.PetId;
            _fadeRemaining = 0f;
            transform.position = evt.Position.ToUnity();
            Tint(1f);
            _track.enabled = true;
            _arc.enabled = false;
            _arc.positionCount = 0;
        }

        private void OnProgressed(RescueProgressed evt)
        {
            if (_petId == evt.PetId && _fadeRemaining == 0f)
            {
                float fraction = Mathf.Clamp01(evt.Fraction);
                Draw(_arc, fraction);
                _arc.enabled = fraction > 0f;
            }
        }

        private void OnRose(PetRose evt)
        {
            if (_petId == evt.PetId)
            {
                _fadeRemaining = FadeSeconds;
            }
        }

        private void Clear()
        {
            _petId = null;
            _fadeRemaining = 0f;
            _track.enabled = false;
            _arc.enabled = false;
        }

        private LineRenderer CreateLine(string name, float width, float height)
        {
            var body = new GameObject(name);
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.up * height;
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var line = body.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.TransformZ;
            line.widthMultiplier = width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private void Draw(LineRenderer line, float fraction)
        {
            int segments = Mathf.CeilToInt(fraction * Segments);
            line.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float angle = Mathf.Min((float)i / Segments, fraction) * (2f * Mathf.PI);
                line.SetPosition(i, new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) * _radius);
            }
        }

        private void Tint(float alpha)
        {
            Color cyan = Palette.Player;
            cyan.a = alpha;
            _arc.startColor = _arc.endColor = cyan;
            cyan.a *= 0.45f;
            _track.startColor = _track.endColor = cyan;
        }
    }
}
