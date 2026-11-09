using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Soulvail.Game.Views
{
    public sealed class GateGuideView : MonoBehaviour
    {
        private const int Segments = 64;

        [SerializeField] private Material _material;
        [SerializeField] private Vector2 _ringSizeM = new(2.4f, 3.2f);
        [SerializeField] private float _ringWidthM = 0.18f;
        [SerializeField] private float _innerSizeMultiplier = 0.8f;
        [SerializeField] private float _innerDegreesPerSecond = 90f;
        [SerializeField] private float _floorDiameterM = 3.2f;
        [SerializeField] private float _calmPulseHz = 0.8f;
        [SerializeField] private float _calmAlpha = 0.55f;
        [SerializeField] private float _strongPulseHz = 2f;
        [SerializeField] private float _strongAlpha = 1f;
        [SerializeField] private float _strongScale = 1.15f;

        private readonly IDisposable[] _subscriptions = new IDisposable[5];
        private ArenaPool _arenas;
        private LineRenderer _outerRing;
        private LineRenderer _innerRing;
        private MeshRenderer _surface;
        private MeshRenderer _floorGlow;
        private MaterialPropertyBlock _properties;
        private Mesh _surfaceMesh;
        private Mesh _floorMesh;
        private int _baseColor;
        private float _pulse;
        private bool _strong;

        [Inject]
        public void Construct(DomainEventHub hub, ArenaPool arenas)
        {
            _arenas = arenas;
            _properties = new MaterialPropertyBlock();
            _baseColor = Shader.PropertyToID("_BaseColor");

            Vector2 radii = _ringSizeM * 0.5f;
            Vector3 centre = Vector3.up * radii.y;
            _outerRing = CreateRing("Outer Ring", centre, radii);
            _innerRing = CreateRing("Inner Ring", centre, radii * _innerSizeMultiplier);
            _surface = CreateDisc("Surface", centre, radii, false, out _surfaceMesh);
            _floorGlow = CreateDisc("Floor Glow", Vector3.up * 0.02f,
                Vector2.one * (_floorDiameterM * 0.5f), true, out _floorMesh);
            _floorGlow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Hide();

            _subscriptions[0] = hub.Subscribe<StageCleared>(Show);
            _subscriptions[1] = hub.Subscribe<GateHintChanged>(evt => SetHint(evt.Strong));
            _subscriptions[2] = hub.Subscribe<StageTransitionStarted>(_ => Hide());
            _subscriptions[3] = hub.Subscribe<StageArrived>(_ => Hide());
            _subscriptions[4] = hub.Subscribe<RunEnded>(_ => Hide());
        }

        private LineRenderer CreateRing(string label, Vector3 centre, Vector2 radii)
        {
            var line = CreatePart<LineRenderer>(label, centre);
            line.useWorldSpace = false;
            line.alignment = LineAlignment.TransformZ;
            line.loop = true;
            line.widthMultiplier = _ringWidthM;
            line.positionCount = Segments;
            for (int i = 0; i < Segments; i++)
            {
                line.SetPosition(i, EllipsePoint(radii, i));
            }

            return line;
        }

        private MeshRenderer CreateDisc(string label, Vector3 centre, Vector2 radii, bool fadeEdge, out Mesh mesh)
        {
            var renderer = CreatePart<MeshRenderer>(label, centre);
            var vertices = new Vector3[Segments + 1];
            var colors = new Color32[vertices.Length];
            var triangles = new int[Segments * 3];
            colors[0] = Color.white;
            Color32 edge = new(255, 255, 255, fadeEdge ? (byte)0 : (byte)255);
            for (int i = 0; i < Segments; i++)
            {
                vertices[i + 1] = EllipsePoint(radii, i);
                colors[i + 1] = edge;
                triangles[i * 3 + 1] = (i + 1) % Segments + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            mesh = new Mesh { name = label };
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.triangles = triangles;
            renderer.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            return renderer;
        }

        private T CreatePart<T>(string label, Vector3 centre) where T : Renderer
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            child.transform.localPosition = centre;
            T renderer = child.AddComponent<T>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private Vector3 EllipsePoint(Vector2 radii, int index)
        {
            float angle = index * (2f * Mathf.PI / Segments);
            return new Vector3(radii.x * Mathf.Cos(angle), radii.y * Mathf.Sin(angle), 0f);
        }

        private void Show(StageCleared evt)
        {
            if (evt.NextArenaId.Value == null || _arenas.Active == null)
            {
                return;
            }

            var gate = new Vector3(evt.GatePosition.X, evt.GatePosition.Y, evt.GatePosition.Z);
            Vector2 centre = _arenas.Active.PlayBounds.center;
            var facing = new Vector3(centre.x - gate.x, 0f, centre.y - gate.z);
            Quaternion rotation = facing.sqrMagnitude > 0f ? Quaternion.LookRotation(facing) : Quaternion.identity;
            transform.SetPositionAndRotation(gate, rotation);
            _innerRing.transform.localRotation = Quaternion.identity;
            SetHint(false);
            SetVisible(true);
        }

        private void SetHint(bool strong)
        {
            _strong = strong;
            _pulse = 0f;
            transform.localScale = Vector3.one * (strong ? _strongScale : 1f);
            Tint();
        }

        private void Update() => Advance(Time.deltaTime);

        public void Advance(float dt)
        {
            if (_outerRing == null || !_outerRing.enabled)
            {
                return;
            }

            _innerRing.transform.Rotate(0f, 0f, _innerDegreesPerSecond * dt);
            _pulse = Mathf.Repeat(_pulse + dt * (_strong ? _strongPulseHz : _calmPulseHz), 1f);
            Tint();
        }

        private void Tint()
        {
            Color color = Palette.Player;
            color.a = (_strong ? _strongAlpha : _calmAlpha) + 0.15f * Mathf.Sin(_pulse * (2f * Mathf.PI));
            ApplyColor(_outerRing, color);
            ApplyColor(_innerRing, color);
            color.a *= 0.5f;
            ApplyColor(_surface, color);
            ApplyColor(_floorGlow, color);
        }

        private void ApplyColor(Renderer renderer, Color color)
        {
            _properties.SetColor(_baseColor, color);
            renderer.SetPropertyBlock(_properties);
        }

        private void Hide() => SetVisible(false);

        private void SetVisible(bool visible)
        {
            _outerRing.enabled = visible;
            _innerRing.enabled = visible;
            _surface.enabled = visible;
            _floorGlow.enabled = visible;
        }

        private void OnDestroy()
        {
            foreach (IDisposable subscription in _subscriptions)
            {
                subscription?.Dispose();
            }

            DestroyMesh(_surfaceMesh);
            DestroyMesh(_floorMesh);
        }

        private void DestroyMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(mesh);
            }
            else
            {
                DestroyImmediate(mesh);
            }
        }
    }
}
