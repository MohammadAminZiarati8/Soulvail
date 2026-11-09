using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using VContainer;

namespace Soulvail.Game.Views
{
    public sealed class GateGuideView : MonoBehaviour
    {
        [SerializeField] private Material _material;
        [SerializeField] private float _beamHeightM = 14f;
        [SerializeField] private float _beamWidthM = 1.6f;
        [SerializeField] private float _trailLiftM = 0.05f;
        [SerializeField] private float _repathS = 0.25f;
        [SerializeField] private float _calmPulseHz = 0.8f;
        [SerializeField] private float _calmAlpha = 0.55f;
        [SerializeField] private float _strongPulseHz = 2f;
        [SerializeField] private float _strongAlpha = 1f;
        [SerializeField] private float _strongWidthMul = 1.5f;

        private readonly IDisposable[] _subscriptions = new IDisposable[5];
        private readonly Vector3[] _corners = new Vector3[64];
        private PlayerView _player;
        private LineRenderer _beam;
        private LineRenderer _trail;
        private NavMeshPath _path;
        private MaterialPropertyBlock _properties;
        private int _baseColor;
        private Vector3 _gate;
        private float _repathRemaining;
        private float _pulse;
        private bool _strong;

        [Inject]
        public void Construct(DomainEventHub hub, PlayerView player)
        {
            _player = player;
            _path = new NavMeshPath();
            _properties = new MaterialPropertyBlock();
            _baseColor = Shader.PropertyToID("_BaseColor");
            _beam = CreateLine("Beam", _beamWidthM);
            _trail = CreateLine("Trail", 0.22f);
            _trail.alignment = LineAlignment.TransformZ;
            _trail.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            _subscriptions[0] = hub.Subscribe<StageCleared>(Show);
            _subscriptions[1] = hub.Subscribe<GateHintChanged>(evt => SetHint(evt.Strong));
            _subscriptions[2] = hub.Subscribe<StageTransitionStarted>(_ => Hide());
            _subscriptions[3] = hub.Subscribe<StageArrived>(_ => Hide());
            _subscriptions[4] = hub.Subscribe<RunEnded>(_ => Hide());
        }

        private LineRenderer CreateLine(string label, float width)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();
            line.sharedMaterial = _material;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthMultiplier = width;
            line.useWorldSpace = true;
            line.enabled = false;
            return line;
        }

        private void Show(StageCleared evt)
        {
            if (evt.NextArenaId.Value == null)
            {
                return;
            }

            _gate = new Vector3(evt.GatePosition.X, evt.GatePosition.Y, evt.GatePosition.Z);
            _beam.positionCount = 2;
            _beam.SetPosition(0, _gate);
            _beam.SetPosition(1, _gate + Vector3.up * _beamHeightM);
            _beam.enabled = true;
            _trail.enabled = true;
            Repath();
            SetHint(false);
        }

        private void SetHint(bool strong)
        {
            _strong = strong;
            _pulse = 0f;
            _beam.widthMultiplier = _beamWidthM * (strong ? _strongWidthMul : 1f);
            Tint();
        }

        private void Update()
        {
            if (_beam == null || !_beam.enabled)
            {
                return;
            }

            float dt = Time.deltaTime;
            _repathRemaining -= dt;
            if (_repathRemaining <= 0f)
            {
                Repath();
            }

            _pulse = Mathf.Repeat(_pulse + dt * (_strong ? _strongPulseHz : _calmPulseHz), 1f);
            Tint();
        }

        private void Tint()
        {
            Color color = Palette.Player;
            float wave = 0.75f + 0.25f * Mathf.Cos(_pulse * (2f * Mathf.PI));
            color.a = (_strong ? _strongAlpha : _calmAlpha) * wave;
            _properties.SetColor(_baseColor, color);
            _beam.SetPropertyBlock(_properties);
            _trail.SetPropertyBlock(_properties);
        }

        private void Repath()
        {
            _repathRemaining = _repathS;
            Vector3 start = _player.Position;
            Vector3 end = _gate;
            Vector3 lift = Vector3.up * _trailLiftM;

            if (NavMesh.SamplePosition(start, out NavMeshHit from, 2f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(end, out NavMeshHit to, 2f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _path)
                && _path.status == NavMeshPathStatus.PathComplete)
            {
                int count = _path.GetCornersNonAlloc(_corners);
                if (count >= 2 && count < _corners.Length)
                {
                    _trail.positionCount = count + 1;
                    for (int i = 0; i < count; i++)
                    {
                        _trail.SetPosition(i, _corners[i] + lift);
                    }

                    end.y = to.position.y;
                    _trail.SetPosition(count, end + lift);
                    return;
                }
            }

            _trail.positionCount = 2;
            _trail.SetPosition(0, start + lift);
            _trail.SetPosition(1, end + lift);
        }

        private void Hide()
        {
            _beam.enabled = false;
            _trail.enabled = false;
        }

        private void OnDestroy()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription?.Dispose();
            }
        }
    }
}
