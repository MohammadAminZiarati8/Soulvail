using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>One reusable rain, with a fixed arrow pool. It never reports hits to core.</summary>
    public sealed class RainOfArrowsView : MonoBehaviour, IDisposable
    {
        private const int ArrowCount = 8;
        private const float FlightSeconds = 0.28f;
        [SerializeField] private MeshRenderer _ring;
        [SerializeField] private MeshFilter _fill;
        [SerializeField] private ProjectileView _arrowPrefab;
        [SerializeField] private ParticleSystem _dust;
        private readonly Transform[] _arrows = new Transform[ArrowCount];
        private readonly TrailRenderer[] _trails = new TrailRenderer[ArrowCount];
        private readonly Vector3[] _origins = new Vector3[ArrowCount];
        private readonly Vector3[] _landings = new Vector3[ArrowCount];
        private readonly float[] _ages = new float[ArrowCount];
        private readonly bool[] _flying = new bool[ArrowCount];
        private readonly System.Random _random = new System.Random();
        private readonly ContentId _slowRank = new ContentId("skill.marksman.rain-of-arrows-3");
        private MaterialPropertyBlock _properties;
        private Mesh _disc;
        private Mesh _rim;
        private MeshRenderer _fillRenderer;
        private IRunSession _session;
        private Transform _bow;
        private IDisposable _cast;
        private IDisposable _boundary;
        private IDisposable _arrived;
        private IDisposable _ended;
        private float _clock;
        private float _elapsed;
        private float _duration;
        private float _nextArrow;
        private Vector3 _centre;
        private bool _warmed;

        public bool IsLive { get; private set; }
        public float Radius { get; private set; }
        public Color FillColour { get; private set; }
        public int SpawnedArrows { get; private set; }
        public int PoolSize => _warmed ? ArrowCount : 0;
        public Vector3 LandingAt(int slot) => _landings[slot];

        public void Construct(DomainEventHub hub, IRunSession session, Transform bow)
        {
            WarmUp();
            _session = session;
            _bow = bow;
            _clock = session?.State?.Time ?? 0f;
            _cast = hub.Subscribe<RainOfArrowsCast>(OnCast);
            _boundary = hub.Subscribe<StageTransitionStarted>(_ => Clear());
            _arrived = hub.Subscribe<StageArrived>(_ => Clear());
            _ended = hub.Subscribe<RunEnded>(_ => Clear());
        }

        public void WarmUp()
        {
            if (_warmed)
            {
                return;
            }
            _warmed = true;
            _properties = new MaterialPropertyBlock();
            _fillRenderer = _fill.GetComponent<MeshRenderer>();
            // A plain disc beneath a separate cyan rim lets the fill change hue while
            // the rim always stays player cyan. Geometry is built once, outside any cast.
            var vertices = new Vector3[49];
            var triangles = new int[48 * 3];
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2f / 48f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % 48 + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            _disc = new Mesh { name = "Rain fill (pooled)" };
            _disc.vertices = vertices;
            _disc.triangles = triangles;
            _disc.RecalculateBounds();
            _fill.sharedMesh = _disc;
            var rimVertices = new Vector3[96];
            var rimTriangles = new int[48 * 6];
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI * 2f / 48f;
                Vector3 edge = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                rimVertices[i * 2] = edge * 0.5f;
                rimVertices[i * 2 + 1] = edge * 0.475f;
                int next = ((i + 1) % 48) * 2;
                int triangle = i * 6;
                rimTriangles[triangle] = i * 2;
                rimTriangles[triangle + 1] = i * 2 + 1;
                rimTriangles[triangle + 2] = next;
                rimTriangles[triangle + 3] = next;
                rimTriangles[triangle + 4] = i * 2 + 1;
                rimTriangles[triangle + 5] = next + 1;
            }
            _rim = new Mesh { name = "Rain rim (pooled)" };
            _rim.vertices = rimVertices;
            _rim.triangles = rimTriangles;
            _rim.RecalculateBounds();
            _ring.GetComponent<MeshFilter>().sharedMesh = _rim;
            var parked = new GameObject("Arrows (pooled)");
            parked.SetActive(false);
            parked.transform.SetParent(transform, false);
            for (int i = 0; i < ArrowCount; i++)
            {
                ProjectileView arrow = Instantiate(_arrowPrefab, parked.transform, false);
                // Prime the ordinary arrow's trail tint before the first cast.
                arrow.Bind(1, Vector3.zero, Vector3.forward, FlightSeconds);
                arrow.OnDespawn();
                _arrows[i] = arrow.transform;
                _arrows[i].localScale = Vector3.one * 0.8f;
                _trails[i] = arrow.GetComponentInChildren<TrailRenderer>(true);
                if (i == 0 && _trails[i] != null)
                {
                    _trails[i].startColor = Palette.Player;
                    _trails[i].endColor = new Color(Palette.Player.r, Palette.Player.g, Palette.Player.b, 0f);
                }
                arrow.gameObject.SetActive(false);
            }
            parked.SetActive(true);
            Clear();
        }

        public void Bind(Vector3 centre, float radius, float duration, bool slows, Vector3 bow, Vector3 sky)
        {
            if (!(radius > 0f) || !float.IsFinite(radius))
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
            if (!(duration > 0f) || !float.IsFinite(duration))
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }
            WarmUp();
            Clear();
            transform.position = Vector3.zero;
            _centre = centre;
            Radius = radius;
            _duration = duration;
            _elapsed = 0f;
            _nextArrow = 0.15f;
            SpawnedArrows = 0;
            IsLive = true;
            gameObject.SetActive(true);
            _ring.transform.SetPositionAndRotation(centre + Vector3.up * 0.075f, Quaternion.Euler(90f, 0f, 0f));
            _ring.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            Paint(_ring, Palette.Player);
            _fill.transform.position = centre + Vector3.up * 0.07f;
            _fill.transform.localScale = Vector3.one * radius * 0.94f;
            FillColour = slows ? new Color(0.65f, 0.85f, 1f, 0.14f) : new Color(Palette.Player.r, Palette.Player.g, Palette.Player.b, 0.06f);
            Paint(_fillRenderer, FillColour);
            Launch(0, bow, bow + sky.normalized * 6f, 0f);
        }

        public void Step(float dt)
        {
            if (!IsLive || !(dt > 0f) || !float.IsFinite(dt))
            {
                return;
            }
            _elapsed += dt;
            if (_elapsed >= _duration)
            {
                Clear();
                return;
            }
            _dust.Simulate(dt, true, false, false);
            for (int i = 0; i < ArrowCount; i++)
            {
                Advance(i, dt);
            }
            while (_nextArrow <= _elapsed)
            {
                for (int i = 1; i < ArrowCount; i++)
                {
                    if (_flying[i])
                    {
                        continue;
                    }
                    float angle = (float)_random.NextDouble() * Mathf.PI * 2f;
                    float distance = Mathf.Sqrt((float)_random.NextDouble()) * Radius;
                    Vector3 landing = _centre + new Vector3(Mathf.Cos(angle) * distance, 0.08f, Mathf.Sin(angle) * distance);
                    Launch(i, landing + new Vector3(-0.65f, 5f, 0f), landing, _elapsed - _nextArrow);
                    SpawnedArrows++;
                    break;
                }
                _nextArrow += 1f / 16f;
            }
        }

        public void Clear()
        {
            IsLive = false;
            Radius = 0f;
            for (int i = 0; i < ArrowCount; i++)
            {
                _flying[i] = false;
                _trails[i]?.Clear();
                if (_arrows[i] != null)
                {
                    _arrows[i].gameObject.SetActive(false);
                }
            }
            _dust?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            gameObject.SetActive(false);
        }

        private void OnCast(RainOfArrowsCast evt)
        {
            bool slows = false;
            if (_session?.State != null)
            {
                _clock = _session.State.Time;
                foreach (ContentId id in _session.State.TakenNodeIds)
                {
                    slows |= id == _slowRank;
                }
            }
            Vector3 sky = _bow != null ? _bow.forward * 0.5f + Vector3.up * 0.8660254f : Vector3.up;
            Bind(evt.Centre.ToUnity(), evt.Radius, evt.Duration, slows,
                _bow != null ? _bow.position : transform.position, sky);
        }

        private void LateUpdate()
        {
            // Read core's clock so pauses, clamped frames and zone expiration agree exactly.
            if (_session?.State == null)
            {
                return;
            }
            float now = _session.State.Time;
            Step(now - _clock);
            _clock = now;
        }

        private void Launch(int slot, Vector3 origin, Vector3 landing, float age)
        {
            _origins[slot] = origin;
            _landings[slot] = landing;
            _ages[slot] = 0f;
            _flying[slot] = true;
            _arrows[slot].SetPositionAndRotation(origin, Quaternion.LookRotation(landing - origin));
            _trails[slot]?.Clear();
            _arrows[slot].gameObject.SetActive(true);
            Advance(slot, age);
        }

        private void Advance(int slot, float dt)
        {
            if (!_flying[slot])
            {
                return;
            }
            _ages[slot] += dt;
            float duration = slot == 0 ? 0.15f : FlightSeconds;
            _arrows[slot].position = Vector3.Lerp(_origins[slot], _landings[slot], _ages[slot] / duration);
            if (_ages[slot] < duration)
            {
                return;
            }
            if (slot != 0)
            {
                var puff = new ParticleSystem.EmitParams { position = _landings[slot] };
                _dust.Emit(puff, 3);
            }
            _flying[slot] = false;
            _trails[slot]?.Clear();
            _arrows[slot].gameObject.SetActive(false);
        }

        private void Paint(Renderer renderer, Color colour)
        {
            _properties.Clear();
            _properties.SetColor("_BaseColor", colour);
            renderer.SetPropertyBlock(_properties);
        }

        public void Dispose()
        {
            _cast?.Dispose();
            _boundary?.Dispose();
            _arrived?.Dispose();
            _ended?.Dispose();
            _cast = null;
            _boundary = null;
            _arrived = null;
            _ended = null;
            if (_disc != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_disc);
                }
                else
                {
                    DestroyImmediate(_disc);
                }
                _disc = null;
            }
            if (_rim != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_rim);
                }
                else
                {
                    DestroyImmediate(_rim);
                }
                _rim = null;
            }
        }

        private void OnDestroy() => Dispose();
    }
}
