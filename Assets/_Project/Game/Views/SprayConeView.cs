using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>
    /// A floor fan clipped against cover once, then filled on simulated time (JG-12f). Drawn in
    /// layers of vertex colour (JG-13g): a faint danger area, a fill that brightens to its front,
    /// and a near-opaque rim and sides that beat faster as the wind-up runs out.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SprayConeView : MonoBehaviour, IPoolable
    {
        public const int Rays = 32;
        public const float FlashSeconds = 0.15f;
        // Above the Jungle's stone slabs (top 0.05 m): at 0.03 the stone hid the fan (JG-13 playtest).
        public const float GroundOffset = 0.08f;
        // The rim and side strips, in metres; 0.04 read as a hairline on a phone (JG-13 playtest).
        public const float EdgeWidth = 0.3f;
        public const float SlowPulseHz = 2f;
        public const float FastPulseHz = 6f;

        // Straight alpha on a SrcAlpha/OneMinusSrcAlpha material; JG-12f premultiplied as well, which
        // drew the fan at a third of its colour (the "muddy" fan of the JG-13 playtest).
        private const float AreaAlpha = 0.28f;
        private const float TailAlpha = 0.25f;
        private const float FrontAlpha = 0.8f;
        private const float FrontLift = 0.12f;
        private const float EdgeLow = 0.85f;
        private const float EdgeLift = 0.4f;
        private const float FlashAlpha = 0.95f;

        // Rim outer, rim inner, fill front, fill tail, side corners, side apexes, area tail, area ends.
        private const int FrontBase = Rays * 2;
        private const int Tail = Rays * 3;
        private const int SideBase = Tail + 1;
        private const int ApexBase = SideBase + 4;
        private const int AreaTail = ApexBase + 2;
        private const int AreaBase = AreaTail + 1;
        private const int VertexCount = AreaBase + Rays;

        private readonly float[] _lengths = new float[Rays];
        private readonly Vector3[] _ends = new Vector3[Rays];
        private readonly Vector3[] _directions = new Vector3[Rays];
        private readonly Vector3[] _vertices = new Vector3[VertexCount];
        private readonly Color[] _colours = new Color[VertexCount];
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private float _seconds;
        private float _elapsed;
        private float _phase;
        private bool _fired;

        public float Fill { get; private set; }
        public bool IsLive { get; private set; }
        public float Range { get; private set; }
        public float PulseRate => Mathf.Lerp(SlowPulseHz, FastPulseHz, Fill);
        public float RayLength(int i) => _lengths[i];
        public Vector3 RayEnd(int i) => _ends[i];
        public Vector3 RayDirection(int i) => _directions[i];
        public bool IsBlocked(int i) => _lengths[i] < Range - 0.001f;

        public void Arm(Vector3 origin, Vector2 directionXZ, float halfAngleDeg, float range, float seconds)
        {
            EnsureMesh();
            Clear();
            transform.SetPositionAndRotation(new Vector3(origin.x, GroundOffset, origin.z), Quaternion.identity);
            transform.localScale = Vector3.one;
            _seconds = seconds;
            Range = range;
            float bearing = Mathf.Atan2(directionXZ.x, directionXZ.y);
            int cover = LayerMask.GetMask("Cover");
            var eye = new Vector3(origin.x, LineOfSightSense.EyeHeight, origin.z);
            for (int i = 0; i < Rays; i++)
            {
                float angle = bearing + (Mathf.Lerp(-halfAngleDeg, halfAngleDeg, i / (float)(Rays - 1)) * Mathf.Deg2Rad);
                var ray = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                _lengths[i] = Physics.Raycast(eye, ray, out RaycastHit hit, range, cover, QueryTriggerInteraction.Ignore)
                    ? hit.distance : range;
                _ends[i] = ray * _lengths[i];
                _directions[i] = ray;
            }

            IsLive = true;
            _renderer.enabled = true;
            Draw();
        }

        public void Fire()
        {
            if (!IsLive)
            {
                return;
            }

            _fired = true;
            _elapsed = 0f;
            Fill = 1f;
            Draw();
        }

        public void Clear()
        {
            IsLive = false;
            Fill = 0f;
            Range = 0f;
            _fired = false;
            _elapsed = 0f;
            _seconds = 0f;
            _phase = 0f;
            System.Array.Clear(_lengths, 0, Rays);
            System.Array.Clear(_ends, 0, Rays);
            System.Array.Clear(_directions, 0, Rays);
            if (_renderer == null)
            {
                TryGetComponent(out _renderer);
            }

            if (_renderer != null)
            {
                _renderer.enabled = false;
            }

            if (_mesh != null)
            {
                System.Array.Clear(_vertices, 0, _vertices.Length);
                _mesh.vertices = _vertices;
                _mesh.RecalculateBounds();
            }
        }

        public void Step(float dt)
        {
            if (!IsLive || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _elapsed += dt;
            if (_fired)
            {
                if (_elapsed >= FlashSeconds)
                {
                    Clear();
                }
            }
            else
            {
                Fill = _seconds > 0f ? Mathf.Clamp01(_elapsed / _seconds) : 1f;
                _phase = Mathf.Repeat(_phase + (PulseRate * dt), 1f);
                Draw();
            }
        }

        public void OnSpawn() { }
        public void OnDespawn() => Clear();

        private void EnsureMesh()
        {
            if (_mesh != null)
            {
                return;
            }

            _renderer = GetComponent<MeshRenderer>();
            _mesh = new Mesh { name = "Spray fan (runtime)" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            _mesh.colors = _colours;

            // Index order is draw order inside one transparent mesh: area, fill, then the edge on top.
            var triangles = new int[((Rays - 1) * 12) + 12];
            int t = 0;
            for (int i = 0; i < Rays - 1; i++)
            {
                triangles[t++] = AreaTail;
                triangles[t++] = AreaBase + i;
                triangles[t++] = AreaBase + i + 1;
            }

            for (int i = 0; i < Rays - 1; i++)
            {
                triangles[t++] = Tail;
                triangles[t++] = FrontBase + i;
                triangles[t++] = FrontBase + i + 1;
            }

            for (int i = 0; i < Rays - 1; i++)
            {
                triangles[t++] = i;
                triangles[t++] = Rays + i;
                triangles[t++] = i + 1;
                triangles[t++] = i + 1;
                triangles[t++] = Rays + i;
                triangles[t++] = Rays + i + 1;
            }

            for (int side = 0; side < 2; side++)
            {
                int end = side == 0 ? 0 : Rays - 1;
                int corner = SideBase + (side * 2);
                triangles[t++] = ApexBase + side;
                triangles[t++] = corner;
                triangles[t++] = end;
                triangles[t++] = end;
                triangles[t++] = corner;
                triangles[t++] = corner + 1;
            }

            _mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        private void Draw()
        {
            for (int i = 0; i < Rays; i++)
            {
                Vector3 end = _ends[i];
                Vector3 inner = end.normalized * Mathf.Max(0f, _lengths[i] - EdgeWidth);
                _vertices[i] = end;
                _vertices[Rays + i] = inner;
                // Grow toward the clipped rim without repainting the safe shadows.
                _vertices[FrontBase + i] = end * Fill;
                _vertices[AreaBase + i] = inner;
            }

            _vertices[Tail] = Vector3.zero;
            _vertices[AreaTail] = Vector3.zero;
            for (int side = 0; side < 2; side++)
            {
                int end = side == 0 ? 0 : Rays - 1;
                int corner = SideBase + (side * 2);
                Vector3 along = _ends[end].normalized;
                Vector3 inset = new Vector3(along.z, 0f, -along.x) * (side == 0 ? EdgeWidth : -EdgeWidth);
                _vertices[corner] = inset;
                _vertices[corner + 1] = _ends[end] + inset;
                _vertices[ApexBase + side] = Vector3.zero;
            }

            Paint();
            _mesh.vertices = _vertices;
            _mesh.colors = _colours;
            _mesh.RecalculateBounds();
        }

        private void Paint()
        {
            Color danger = Palette.Danger;
            if (_fired)
            {
                SetColours(0, VertexCount, Tint(danger, FlashAlpha));
                return;
            }

            // A beat between EdgeLow and opaque, lifted toward white at its crest.
            float beat = 0.5f - (0.5f * Mathf.Cos(_phase * 2f * Mathf.PI));
            Color edge = Color.Lerp(danger, Color.white, EdgeLift * beat);
            edge.a = Mathf.Lerp(EdgeLow, 1f, beat);
            SetColours(0, FrontBase, edge);
            SetColours(SideBase, AreaTail, edge);
            SetColours(FrontBase, Tail, Tint(Color.Lerp(danger, Color.white, FrontLift), FrontAlpha));
            SetColours(Tail, Tail + 1, Tint(danger, TailAlpha));
            SetColours(AreaTail, VertexCount, Tint(danger, AreaAlpha));
        }

        private void SetColours(int from, int to, Color colour)
        {
            Color vertex = Palette.Vertex(colour);
            for (int i = from; i < to; i++)
            {
                _colours[i] = vertex;
            }
        }

        private static Color Tint(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, alpha);

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_mesh);
                }
                else
                {
                    DestroyImmediate(_mesh);
                }
            }
        }
    }
}
