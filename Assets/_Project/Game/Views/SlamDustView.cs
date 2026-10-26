using System;
using Soulvail.Game.Pooling;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. A file-scoped namespace would leave
// VFX_SlamDust.prefab's reference to this component deserialising as null with nothing reported
// (Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The ground giving under a slam's landing (JG-06b): earth chunks thrown up from round the body's
    /// feet that fall back and settle, and low dust billows rolling out along the ground and shrinking
    /// away, all gone in <see cref="Lifetime"/>. It decides nothing: <c>ShockwaveEmitted</c> has
    /// already said a slam landed, and where.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One mesh, one opaque material, one draw call</b>, <see cref="ImpactView"/>'s pattern for
    /// its reason: the vertices are rewritten each step on <c>M_Impact</c>, which multiplies vertex
    /// colours, and every face carries its own shade so it reads faceted with no light. The dust is
    /// opaque billows that shrink away rather than alpha that fades, so a landing adds no transparent
    /// layer (GD §11.3). A part that has finished is folded to a point, so the layout never changes.
    /// </para>
    /// <para>
    /// <b>Seeded, never random.</b> The ring id varies the throw from landing to landing and the
    /// same id throws the same way, so a test can pin it. <c>UnityEngine.Random</c> is not used.
    /// </para>
    /// </remarks>
    public sealed class SlamDustView : MonoBehaviour, IPoolable
    {
        /// <summary>Earth chunks per landing.</summary>
        public const int ChunkCount = 12;

        /// <summary>Dust billows per landing.</summary>
        public const int BillowCount = 10;

        /// <summary>Seconds from the landing until every part has gone.</summary>
        public const float Lifetime = 0.6f;

        /// <summary>
        /// How far out from the landing the chunks leave and the billows start, in metres: round Old
        /// Croaker's feet at its 2.2 body scale, so the burst is not hidden under it.
        /// </summary>
        public const float StartRadius = 1.8f;

        /// <summary>How far out the billows roll, in metres.</summary>
        public const float BillowReach = 4.5f;

        private const int ChunkVertices = 24;
        private const int BillowVertices = 24;
        private const int BillowBase = ChunkCount * ChunkVertices;
        private const int VertexCount = BillowBase + (BillowCount * BillowVertices);

        private const float Gravity = 30f;
        private const float ChunkSize = 0.5f;
        private const float BillowSize = 1.3f;
        private const float MaxSpinDeg = 720f;
        private const float BoundsSize = 14f;

        /// <summary>Jungle earth, moss and dust: GD §16.4 keeps red-orange, gold and violet for meaning.</summary>
        private static readonly Color Earth = new Color32(0x7A, 0x56, 0x36, 0xFF);

        private static readonly Color Moss = new Color32(0x5E, 0x7A, 0x3A, 0xFF);
        private static readonly Color Dust = new Color32(0xC9, 0xB4, 0x8A, 0xFF);

        /// <summary>A cube's six faces: the normal, then two axes across it.</summary>
        private static readonly Vector3[] FaceNormal =
        {
            Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back,
        };

        [Tooltip("The one mesh this burst is drawn with, on this object. Its mesh is built at " +
                 "runtime; the renderer beside it wears M_Impact.")]
        [SerializeField] private MeshFilter _filter;

        private readonly Vector3[] _vertices = new Vector3[VertexCount];
        private readonly Vector3[] _chunkStart = new Vector3[ChunkCount];
        private readonly Vector3[] _chunkVelocity = new Vector3[ChunkCount];
        private readonly Vector3[] _chunkAxis = new Vector3[ChunkCount];
        private readonly float[] _chunkSpin = new float[ChunkCount];
        private readonly float[] _chunkScale = new float[ChunkCount];
        private readonly Vector3[] _billowDirection = new Vector3[BillowCount];
        private readonly float[] _billowScale = new float[BillowCount];

        private Mesh _mesh;
        private float _age;
        private bool _live;

        /// <summary>True from <see cref="Play"/> until every part has gone.</summary>
        public bool IsLive => _live;

        /// <summary>Where chunk <paramref name="index"/> is now, from the landing point, in metres.</summary>
        public Vector3 ChunkOffset(int index) =>
            _live && index >= 0 && index < ChunkCount ? ChunkPosition(index, _age) : Vector3.zero;

        /// <summary>Where billow <paramref name="index"/> is now, from the landing point, in metres.</summary>
        public Vector3 BillowOffset(int index) =>
            _live && index >= 0 && index < BillowCount
                ? _billowDirection[index] * Mathf.Lerp(StartRadius, BillowReach, Ease(_age / Lifetime))
                : Vector3.zero;

        /// <summary>Billow <paramref name="index"/>'s size now, as a fraction of full: 0 once gone.</summary>
        public float BillowScale(int index)
        {
            if (!_live || index < 0 || index >= BillowCount)
            {
                return 0f;
            }

            float u = _age / Lifetime;

            // Swells over the first third, then shrinks to nothing.
            float swell = u < 0.3f ? Mathf.Lerp(0.4f, 1f, u / 0.3f) : 1f - ((u - 0.3f) / 0.7f);

            return Mathf.Max(0f, swell) * _billowScale[index];
        }

        /// <summary>Starts a burst at <paramref name="position"/>, varied by <paramref name="seed"/>.</summary>
        /// <param name="position">The landing point, on the ground.</param>
        /// <param name="seed">The ring's id: the same seed throws the same way.</param>
        public void Play(Vector3 position, int seed)
        {
            EnsureMesh();

            transform.SetPositionAndRotation(position, Quaternion.identity);

            uint state = Hash(seed);
            float turn = Next(ref state) * Mathf.PI * 2f;

            for (int i = 0; i < ChunkCount; i++)
            {
                float angle = turn + (i * Mathf.PI * 2f / ChunkCount) + (Signed(ref state) * 0.3f);
                var out_ = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                // Steep and quick, so the heaviest throw (8.8 m/s at 63°) is down by 0.52 s.
                float pitch = (55f + (Signed(ref state) * 8f)) * Mathf.Deg2Rad;
                float speed = 8f * (1f + (Signed(ref state) * 0.1f));

                _chunkStart[i] = out_ * StartRadius;
                _chunkVelocity[i] = ((out_ * Mathf.Cos(pitch)) + (Vector3.up * Mathf.Sin(pitch))) * speed;
                _chunkAxis[i] = new Vector3(Signed(ref state), Signed(ref state), Signed(ref state)).normalized;
                _chunkSpin[i] = Signed(ref state) * MaxSpinDeg;
                _chunkScale[i] = 0.7f + (Next(ref state) * 0.6f);

                if (_chunkAxis[i].sqrMagnitude < 0.5f)
                {
                    _chunkAxis[i] = Vector3.up;
                }
            }

            for (int i = 0; i < BillowCount; i++)
            {
                float angle = turn + ((i + 0.5f) * Mathf.PI * 2f / BillowCount) + (Signed(ref state) * 0.25f);

                _billowDirection[i] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                _billowScale[i] = 0.8f + (Next(ref state) * 0.4f);
            }

            _age = 0f;
            _live = true;

            Draw();
        }

        /// <summary>Plays with seed 0.</summary>
        public void Play(Vector3 position) => Play(position, 0);

        /// <summary>
        /// Advances the burst by <paramref name="dt"/> seconds. Returns false once it has gone (at
        /// <see cref="Lifetime"/>) or was never played. A zero, negative or non-finite step changes
        /// nothing.
        /// </summary>
        public bool Step(float dt)
        {
            if (!_live)
            {
                return false;
            }

            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return true;
            }

            _age += dt;

            if (_age >= Lifetime)
            {
                _live = false;
                _age = 0f;
                Collapse();

                return false;
            }

            Draw();

            return true;
        }

        /// <inheritdoc />
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>A body rented again draws nothing of its last landing before its next <see cref="Play"/>.</remarks>
        public void OnDespawn()
        {
            _live = false;
            _age = 0f;

            Collapse();
        }

        private void Awake()
        {
            // Built while the pool prewarms, so the first landing of a run builds nothing.
            EnsureMesh();
        }

        private void OnDestroy()
        {
            if (_mesh == null)
            {
                return;
            }

            // The play-mode split every view that makes a mesh keeps (M0-14).
            if (Application.isPlaying)
            {
                Destroy(_mesh);
            }
            else
            {
                DestroyImmediate(_mesh);
            }

            _mesh = null;
        }

        /// <summary>A chunk's flight, landing on the ground and staying there: it does not bounce.</summary>
        private Vector3 ChunkPosition(int index, float t)
        {
            Vector3 v = _chunkVelocity[index];
            float landing = 2f * v.y / Gravity;
            float flight = Mathf.Min(t, landing);
            Vector3 at = _chunkStart[index] + (v * flight) + (Vector3.down * (0.5f * Gravity * flight * flight));

            at.y = Mathf.Max(0f, at.y);

            return at;
        }

        private static float Ease(float u)
        {
            u = Mathf.Clamp01(u);

            return 1f - ((1f - u) * (1f - u));
        }

        private void Draw()
        {
            float u = _age / Lifetime;

            // Chunks keep full size until the last fifth, then shrink into the ground.
            float chunkLife = u < 0.8f ? 1f : 1f - ((u - 0.8f) / 0.2f);

            for (int i = 0; i < ChunkCount; i++)
            {
                float half = ChunkSize * 0.5f * _chunkScale[i] * chunkLife;
                Quaternion spin = Quaternion.AngleAxis(_chunkSpin[i] * _age, _chunkAxis[i]);

                Box(i * ChunkVertices, ChunkPosition(i, _age) + (Vector3.up * half), spin, half);
            }

            for (int i = 0; i < BillowCount; i++)
            {
                float half = BillowSize * 0.5f * BillowScale(i);
                Vector3 centre = BillowOffset(i) + (Vector3.up * half * 0.7f);

                // Squat: billows hug the ground.
                Box(BillowBase + (i * BillowVertices), centre, Quaternion.Euler(0f, i * 37f, 0f), half, 0.7f);
            }

            _mesh.SetVertices(_vertices);
        }

        /// <summary>A box of half-size <paramref name="half"/>, four vertices per face; folded at zero size.</summary>
        private void Box(int first, Vector3 centre, Quaternion rotation, float half, float squash = 1f)
        {
            if (half <= 0f)
            {
                Array.Clear(_vertices, first, 24);

                return;
            }

            for (int f = 0; f < 6; f++)
            {
                Vector3 n = FaceNormal[f];
                Vector3 a = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 b = Vector3.Cross(n, a);

                for (int c = 0; c < 4; c++)
                {
                    float sa = (c == 0 || c == 3) ? -1f : 1f;
                    float sb = c < 2 ? -1f : 1f;
                    Vector3 local = (n + (a * sa) + (b * sb)) * half;

                    local.y *= squash;

                    _vertices[first + (f * 4) + c] = centre + (rotation * local);
                }
            }
        }

        private void Collapse()
        {
            if (_mesh == null)
            {
                return;
            }

            Array.Clear(_vertices, 0, VertexCount);

            _mesh.SetVertices(_vertices);
        }

        /// <summary>The mesh, built once: its triangles and colours never change, only its vertices.</summary>
        private void EnsureMesh()
        {
            if (_mesh != null)
            {
                return;
            }

            if (_filter == null)
            {
                TryGetComponent(out _filter);
            }

            _mesh = new Mesh { name = "SlamDust" };
            _mesh.MarkDynamic();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(BuildColours());
            _mesh.SetTriangles(BuildTriangles(), 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, Vector3.one * BoundsSize);

            if (_filter != null)
            {
                _filter.sharedMesh = _mesh;
            }
        }

        /// <summary>Each face its own shade: lit on top, darker down the sides, darkest underneath.</summary>
        private static Color32[] BuildColours()
        {
            var colours = new Color32[VertexCount];

            for (int i = 0; i < ChunkCount; i++)
            {
                Color body = i % 3 == 2 ? Moss : Earth;

                for (int f = 0; f < 6; f++)
                {
                    Color32 shade = Shade(body, FaceShade(f));

                    for (int c = 0; c < 4; c++)
                    {
                        colours[(i * ChunkVertices) + (f * 4) + c] = shade;
                    }
                }
            }

            for (int i = 0; i < BillowCount; i++)
            {
                for (int f = 0; f < 6; f++)
                {
                    Color32 shade = Shade(Dust, 0.15f + (0.85f * FaceShade(f)));

                    for (int c = 0; c < 4; c++)
                    {
                        colours[BillowBase + (i * BillowVertices) + (f * 4) + c] = shade;
                    }
                }
            }

            return colours;
        }

        private static float FaceShade(int face) => face switch
        {
            0 => 1f,
            1 => 0.55f,
            2 => 0.85f,
            3 => 0.75f,
            4 => 0.8f,
            _ => 0.9f,
        };

        private static Color32 Shade(Color colour, float shade) =>
            new Color(colour.r * shade, colour.g * shade, colour.b * shade, 1f);

        /// <summary>Two triangles per face, wound so the face's normal points out.</summary>
        private static int[] BuildTriangles()
        {
            int boxes = ChunkCount + BillowCount;
            var triangles = new int[boxes * 36];
            int t = 0;

            for (int box = 0; box < boxes; box++)
            {
                for (int f = 0; f < 6; f++)
                {
                    int v = (box * 24) + (f * 4);

                    // Corners run (−a −b), (+a −b), (+a +b), (−a +b) with b = n × a, so
                    // (b − a) × (c − a) is +n: Unity's front face (its Quad primitive's rule).
                    triangles[t++] = v;
                    triangles[t++] = v + 1;
                    triangles[t++] = v + 2;
                    triangles[t++] = v;
                    triangles[t++] = v + 2;
                    triangles[t++] = v + 3;
                }
            }

            return triangles;
        }

        /// <summary>A seed spread over all 32 bits — <see cref="ImpactView"/>'s hash.</summary>
        private static uint Hash(int seed)
        {
            uint x = unchecked((uint)seed * 2654435761u);

            x ^= x >> 16;
            x = unchecked(x * 0x7feb352du);
            x ^= x >> 15;
            x = unchecked(x * 0x846ca68bu);
            x ^= x >> 16;

            return x == 0u ? 1u : x;
        }

        /// <summary>The next of a xorshift sequence, in <c>[0, 1)</c>.</summary>
        private static float Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;

            return (state & 0xFFFFFFu) / 16777216f;
        }

        /// <summary>The next of the sequence, in <c>[−1, 1)</c>.</summary>
        private static float Signed(ref uint state) => (Next(ref state) * 2f) - 1f;
    }
}
