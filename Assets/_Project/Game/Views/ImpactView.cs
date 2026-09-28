using System;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and VFX_ArrowImpact.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One shot's hit, drawn where it landed (RS-06b): a white star that pops and goes out, a spray of
    /// pale shards thrown along the shot's travel with a cyan glint on each, and a thin white ring, all
    /// gone in under 0.3 s. It decides nothing: <c>ProjectileImpacted</c> has already said a shot hit,
    /// and where.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One mesh, one opaque material, one draw call.</b> The star, the six shards and the ring are
    /// one vertex buffer rewritten each step, on <c>M_Impact</c>: URP's <c>Particles/Unlit</c>, opaque,
    /// multiplying the mesh's vertex colours. So a hit adds no transparent layer at all, which is GD
    /// §11.3's fill-rate rule, and a volley's three hits are three draw calls. A part that has finished
    /// is folded to a point, not hidden, so the buffer keeps its layout for the whole life.
    /// </para>
    /// <para>
    /// <b>Flat-shaded by colour, not by light.</b> Every triangle owns its three vertices, and each
    /// face of a shard, and each half of a star spike, carries its own shade of white. An unlit
    /// material then draws the facets GD §17.1 asks for without a light, and a tumbling shard glints
    /// as its faces turn.
    /// </para>
    /// <para>
    /// <b>The star and the ring face the camera; the shards are in the world.</b>
    /// <see cref="Play"/> is handed the viewer's rotation, and the whole effect is placed
    /// <see cref="_height"/> above the landing point, then pulled <see cref="_towardViewer"/> toward
    /// the camera, so the flash is drawn in front of the body it hit rather than inside it. A shot lands
    /// at its target's feet (<c>ProjectileImpacted.Position</c>), and the arrow skims the ground, so the
    /// height is this object's alone, like <c>ProjectileView</c>'s arc.
    /// </para>
    /// <para>
    /// <b>It is stepped on the snapshot's clock and pooled</b>, <see cref="ShockwaveView"/>'s two
    /// rules: <c>ProjectileViews</c> steps it with the clamped <c>Dt</c> and returns it when
    /// <see cref="IsLive"/> goes false, and <see cref="OnDespawn"/> forgets the play.
    /// </para>
    /// </remarks>
    public sealed class ImpactView : MonoBehaviour, IPoolable
    {
        /// <summary>Shards per hit.</summary>
        public const int ShardCount = 6;

        /// <summary>The rotation of a camera looking straight down: the viewer when none is given.</summary>
        /// <remarks>
        /// <c>static readonly</c>, for <see cref="ShockwaveView"/>'s reason: the ban on statics is about
        /// mutable state that survives a run (AR §18.4).
        /// </remarks>
        public static readonly Quaternion Overhead = Quaternion.Euler(90f, 0f, 0f);

        private const int StarPoints = 8;
        private const int StarVertices = StarPoints * 6;
        private const int ShardVertices = 12;
        private const int RingSegments = 20;
        private const int ShardBase = StarVertices;
        private const int RingBase = ShardBase + (ShardCount * ShardVertices);
        private const int VertexCount = RingBase + (RingSegments * 2);

        /// <summary>The fraction of the star's life it spends growing, and the size it grows from.</summary>
        private const float StarRise = 0.2f;
        private const float StarStart = 0.5f;

        /// <summary>The notch between two spikes, as a fraction of the star's size.</summary>
        private const float ValleyRadius = 0.15f;

        /// <summary>The fraction of its radius the ring starts at.</summary>
        private const float RingStart = 0.25f;

        /// <summary>How much a shard's life, speed and yaw vary from hit to hit, as fractions.</summary>
        private const float LifeJitter = 0.15f;
        private const float SpeedJitter = 0.25f;

        /// <summary>The two launch angles above the ground a shard alternates between, in degrees, and their spread.</summary>
        private const float LowPitchDeg = 15f;
        private const float HighPitchDeg = 40f;
        private const float PitchJitterDeg = 8f;

        /// <summary>How fast a shard falls, in m/s², and how quickly the air slows it, per second.</summary>
        private const float Gravity = 20f;
        private const float Drag = 5f;

        /// <summary>The fastest a shard tumbles about its own length, in degrees per second.</summary>
        private const float MaxSpinDeg = 1080f;

        /// <summary>The mesh's fixed bounds, in metres a side: past every shard's reach.</summary>
        private const float BoundsSize = 5f;

        /// <summary>The one face of each shard drawn in the player's cyan: the one in shadow.</summary>
        private const int GlintFace = 2;

        [Tooltip("The one mesh this impact is drawn with, on this object. Its mesh is built at " +
                 "runtime; the renderer beside it wears M_Impact.")]
        [SerializeField] private MeshFilter _filter;

        [Tooltip("Metres above the landing point the impact is centred. A shot lands at its " +
                 "target's feet, so this is what puts the flash on the body.")]
        [Min(0f)]
        [SerializeField] private float _height = 0.6f;

        [Tooltip("Metres the impact is pulled toward the camera, so the flash is drawn in front of " +
                 "the body it hit rather than inside it. A little more than a body's radius.")]
        [Min(0f)]
        [SerializeField] private float _towardViewer = 0.6f;

        [Tooltip("How long the white star lasts, in seconds. It pops to full size in the first " +
                 "fifth of that and shrinks to nothing in the rest.")]
        [Min(0.01f)]
        [SerializeField] private float _starSeconds = 0.1f;

        [Tooltip("The star's longest spike at full size, in metres. The long spike points along " +
                 "the shot's travel as the camera sees it.")]
        [Min(0.01f)]
        [SerializeField] private float _starSize = 1.05f;

        [Tooltip("How long a shard lasts, in seconds, give or take 15 %. It shrinks to nothing " +
                 "over that time, so the longest shard is the impact's lifetime.")]
        [Min(0.01f)]
        [SerializeField] private float _shardSeconds = 0.24f;

        [Tooltip("How fast a shard leaves, in m/s, give or take 25 %. The air slows it, so it " +
                 "travels under a metre.")]
        [Min(0f)]
        [SerializeField] private float _shardSpeed = 7.5f;

        [Tooltip("How far either side of the shot's travel the shards spread, in degrees.")]
        [Range(0f, 90f)]
        [SerializeField] private float _shardSpreadDeg = 50f;

        [Tooltip("A shard's length and width at full size, in metres.")]
        [Min(0.01f)]
        [SerializeField] private float _shardLength = 0.4f;

        [Min(0.01f)]
        [SerializeField] private float _shardWidth = 0.13f;

        [Tooltip("How long the ring lasts, in seconds. It races out and thins to nothing.")]
        [Min(0.01f)]
        [SerializeField] private float _ringSeconds = 0.14f;

        [Tooltip("The ring's outer radius at its widest, in metres.")]
        [Min(0.01f)]
        [SerializeField] private float _ringRadius = 0.75f;

        [Tooltip("The ring's width when it starts, in metres. Keep it thin and brief: a ring round " +
                 "a body that lingers reads as the targeting reticle.")]
        [Min(0.001f)]
        [SerializeField] private float _ringWidth = 0.07f;

        private readonly Vector3[] _vertices = new Vector3[VertexCount];
        private readonly Vector3[] _shardVelocity = new Vector3[ShardCount];
        private readonly float[] _shardLife = new float[ShardCount];
        private readonly float[] _shardRoll = new float[ShardCount];
        private readonly float[] _shardSpin = new float[ShardCount];

        private Mesh _mesh;
        private Vector3 _heading = Vector3.forward;
        private Vector3 _starAxis = Vector3.right;
        private Vector3 _starSide = Vector3.forward;
        private Vector3 _pictureRight = Vector3.right;
        private Vector3 _pictureUp = Vector3.forward;
        private float _age;
        private bool _live;

        /// <summary>
        /// The pose this body was created at, restored on the way back to the pool. Captured in
        /// <see cref="Awake"/>, which outside play mode never runs (Traps §5); the initialisers
        /// describe a prefab authored at the origin, which <c>VFX_ArrowImpact.prefab</c> is.
        /// </summary>
        private Vector3 _restPosition = Vector3.zero;

        /// <summary>True from <see cref="Play"/> until the last part has gone out.</summary>
        public bool IsLive => _live;

        /// <summary>Seconds since <see cref="Play"/>. Zero in the pool.</summary>
        public float Age => _live ? _age : 0f;

        /// <summary>
        /// How long a hit lasts, in seconds: its longest part, which is the longest a shard can
        /// live. Derived from the numbers above, never authored.
        /// </summary>
        public float Lifetime =>
            Mathf.Max(_starSeconds, Mathf.Max(_ringSeconds, _shardSeconds * (1f + LifeJitter)));

        /// <summary>The star's size now, as a fraction of <see cref="_starSize"/>: 0 once it has gone out.</summary>
        public float StarScale
        {
            get
            {
                float u = _live ? _age / _starSeconds : 1f;

                if (u >= 1f)
                {
                    return 0f;
                }

                if (u < StarRise)
                {
                    return Mathf.Lerp(StarStart, 1f, u / StarRise);
                }

                float x = (u - StarRise) / (1f - StarRise);

                return 1f - (x * x);
            }
        }

        /// <summary>The ring's outer radius now, in metres: 0 once it has gone out.</summary>
        public float RingRadius
        {
            get
            {
                float u = _live ? _age / _ringSeconds : 1f;

                if (u >= 1f)
                {
                    return 0f;
                }

                float eased = 1f - ((1f - u) * (1f - u) * (1f - u));

                return _ringRadius * (RingStart + ((1f - RingStart) * eased));
            }
        }

        /// <summary>The ring's width now, in metres: 0 once it has gone out.</summary>
        public float RingWidth
        {
            get
            {
                float u = _live ? _age / _ringSeconds : 1f;

                return u >= 1f ? 0f : _ringWidth * (1f - u);
            }
        }

        /// <summary>
        /// Starts a hit: centred <see cref="_height"/> above <paramref name="landing"/> and pulled
        /// toward the viewer, shards thrown along <paramref name="travel"/>.
        /// </summary>
        /// <param name="landing">Where the shot landed, in world metres — <c>ProjectileImpacted.Position</c>.</param>
        /// <param name="travel">
        /// The flight, origin to target. Only its direction on the ground is read; a flight of no
        /// length throws the shards along world +Z.
        /// </param>
        /// <param name="viewer">The camera's rotation, or <see cref="Overhead"/>.</param>
        /// <param name="seed">
        /// Varies the shards from hit to hit, the same way for the same seed — the shot's id, so
        /// a volley's three hits differ and a test's do not.
        /// </param>
        public void Play(Vector3 landing, Vector3 travel, Quaternion viewer, int seed)
        {
            EnsureMesh();

            Vector3 lookAlong = viewer * Vector3.forward;

            _pictureRight = viewer * Vector3.right;
            _pictureUp = viewer * Vector3.up;

            transform.SetPositionAndRotation(
                landing + (Vector3.up * _height) - (lookAlong * _towardViewer),
                Quaternion.identity);

            _heading = new Vector3(travel.x, 0f, travel.z);
            _heading = _heading.sqrMagnitude > 1e-8f ? _heading.normalized : Vector3.forward;

            AimStar();
            ThrowShards(seed);

            _age = 0f;
            _live = true;

            Draw();
        }

        /// <summary>
        /// Advances the hit by <paramref name="dt"/> seconds. At <see cref="Lifetime"/> it goes out and
        /// <see cref="IsLive"/> reads false. A zero, negative or non-finite step does nothing.
        /// </summary>
        public void Step(float dt)
        {
            if (!_live || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _age += dt;

            if (_age >= Lifetime)
            {
                _live = false;
                _age = 0f;
                Collapse();

                return;
            }

            Draw();
        }

        /// <summary>Where shard <paramref name="index"/> is now, from the impact's centre, in metres.</summary>
        public Vector3 ShardOffset(int index)
        {
            if (!_live || index < 0 || index >= ShardCount)
            {
                return Vector3.zero;
            }

            return ShardPosition(index, Mathf.Min(_age, _shardLife[index]));
        }

        /// <summary>Shard <paramref name="index"/>'s size now, as a fraction of full: 0 once it has gone out.</summary>
        public float ShardScale(int index)
        {
            if (!_live || index < 0 || index >= ShardCount)
            {
                return 0f;
            }

            float u = _age / _shardLife[index];

            return u >= 1f ? 0f : 1f - (u * u);
        }

        /// <inheritdoc />
        /// <remarks>Nothing to do: <see cref="Play"/> sets every field a hit reads.</remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// The play is forgotten and the mesh folded to a point, so a body rented again draws nothing
        /// of its last hit for the frame before its next <see cref="Play"/>.
        /// </remarks>
        public void OnDespawn()
        {
            _live = false;
            _age = 0f;

            transform.SetPositionAndRotation(_restPosition, Quaternion.identity);

            Collapse();
        }

        private void Awake()
        {
            _restPosition = transform.position;

            // Built while the pool prewarms, so the first hit of a run builds nothing.
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

        /// <summary>
        /// The star's long spike along the shot's travel as the camera sees it: the heading laid into
        /// the picture plane. A heading the camera looks straight along points the spike up the screen.
        /// </summary>
        private void AimStar()
        {
            float x = Vector3.Dot(_heading, _pictureRight);
            float y = Vector3.Dot(_heading, _pictureUp);
            float length = Mathf.Sqrt((x * x) + (y * y));

            if (length < 1e-4f)
            {
                x = 0f;
                y = 1f;
            }
            else
            {
                x /= length;
                y /= length;
            }

            _starAxis = (_pictureRight * x) + (_pictureUp * y);
            _starSide = (_pictureUp * x) - (_pictureRight * y);
        }

        /// <summary>
        /// Launches the shards: spread evenly across <see cref="_shardSpreadDeg"/> either side of the
        /// heading, alternately low and high, each nudged by the seed.
        /// </summary>
        private void ThrowShards(int seed)
        {
            uint state = Hash(seed);
            float nudge = _shardSpreadDeg / ShardCount;

            for (int i = 0; i < ShardCount; i++)
            {
                float across = i / (float)(ShardCount - 1);
                float yaw = Mathf.Lerp(-_shardSpreadDeg, _shardSpreadDeg, across) + (Signed(ref state) * nudge);

                yaw = Mathf.Clamp(yaw, -_shardSpreadDeg, _shardSpreadDeg);

                float pitch = ((i & 1) == 0 ? LowPitchDeg : HighPitchDeg) + (Signed(ref state) * PitchJitterDeg);
                float pitchRad = pitch * Mathf.Deg2Rad;

                Vector3 flat = Quaternion.AngleAxis(yaw, Vector3.up) * _heading;
                Vector3 direction = (flat * Mathf.Cos(pitchRad)) + (Vector3.up * Mathf.Sin(pitchRad));

                _shardVelocity[i] = direction * (_shardSpeed * (1f + (Signed(ref state) * SpeedJitter)));
                _shardLife[i] = _shardSeconds * (1f + (Signed(ref state) * LifeJitter));
                _shardRoll[i] = Next(ref state) * 360f;
                _shardSpin[i] = Signed(ref state) * MaxSpinDeg;
            }
        }

        private void Draw()
        {
            DrawStar();
            DrawShards();
            DrawRing();

            _mesh.SetVertices(_vertices);
        }

        /// <summary>
        /// Eight spikes round a point, two triangles each: the long one along the heading, a short
        /// one behind it, two middling ones across it and four short diagonals.
        /// </summary>
        private void DrawStar()
        {
            float size = _starSize * StarScale;

            if (size <= 0f)
            {
                Fold(0, StarVertices);

                return;
            }

            const float step = Mathf.PI * 2f / StarPoints;

            for (int k = 0; k < StarPoints; k++)
            {
                float angle = k * step;
                int v = k * 6;

                Vector3 tip = StarDirection(angle) * (SpikeLength(k) * size);

                _vertices[v] = Vector3.zero;
                _vertices[v + 1] = StarDirection(angle - (step * 0.5f)) * (ValleyRadius * size);
                _vertices[v + 2] = tip;
                _vertices[v + 3] = Vector3.zero;
                _vertices[v + 4] = tip;
                _vertices[v + 5] = StarDirection(angle + (step * 0.5f)) * (ValleyRadius * size);
            }
        }

        /// <summary>Each shard a thin four-faced pyramid pointed along its own flight, turning as it goes.</summary>
        private void DrawShards()
        {
            for (int i = 0; i < ShardCount; i++)
            {
                int v = ShardBase + (i * ShardVertices);
                float scale = ShardScale(i);

                if (scale <= 0f)
                {
                    Fold(v, ShardVertices);

                    continue;
                }

                Vector3 at = ShardPosition(i, _age);
                Vector3 velocity = (_shardVelocity[i] * Mathf.Exp(-Drag * _age)) + (Vector3.down * (Gravity * _age));
                Vector3 forward = velocity.sqrMagnitude > 1e-8f ? velocity.normalized : _heading;
                Vector3 side = Vector3.Cross(Vector3.up, forward);

                side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;

                Vector3 lift = Vector3.Cross(forward, side);
                float roll = (_shardRoll[i] + (_shardSpin[i] * _age)) * Mathf.Deg2Rad;
                Vector3 a = (side * Mathf.Cos(roll)) + (lift * Mathf.Sin(roll));
                Vector3 b = (lift * Mathf.Cos(roll)) - (side * Mathf.Sin(roll));

                float length = _shardLength * scale;
                float radius = _shardWidth * scale * 0.5f;

                Vector3 tip = at + (forward * (length * 0.65f));
                Vector3 back = at - (forward * (length * 0.35f));

                // The base's three corners, a third of a turn apart: cos 120° = −0.5, sin 120° ≈ 0.866.
                Vector3 b0 = back + (a * radius);
                Vector3 b1 = back + (((a * -0.5f) + (b * 0.8660254f)) * radius);
                Vector3 b2 = back + (((a * -0.5f) - (b * 0.8660254f)) * radius);

                _vertices[v] = tip;
                _vertices[v + 1] = b0;
                _vertices[v + 2] = b1;
                _vertices[v + 3] = tip;
                _vertices[v + 4] = b1;
                _vertices[v + 5] = b2;
                _vertices[v + 6] = tip;
                _vertices[v + 7] = b2;
                _vertices[v + 8] = b0;
                _vertices[v + 9] = b0;
                _vertices[v + 10] = b2;
                _vertices[v + 11] = b1;
            }
        }

        /// <summary>A flat band round the centre, facing the camera, racing out and thinning away.</summary>
        private void DrawRing()
        {
            float outer = RingRadius;

            if (outer <= 0f)
            {
                Fold(RingBase, RingSegments * 2);

                return;
            }

            float inner = Mathf.Max(0f, outer - RingWidth);
            const float step = Mathf.PI * 2f / RingSegments;

            for (int j = 0; j < RingSegments; j++)
            {
                float angle = j * step;
                Vector3 direction = (_pictureRight * Mathf.Cos(angle)) + (_pictureUp * Mathf.Sin(angle));

                _vertices[RingBase + (j * 2)] = direction * outer;
                _vertices[RingBase + (j * 2) + 1] = direction * inner;
            }
        }

        /// <summary>Shard <paramref name="index"/>'s place <paramref name="t"/> seconds in: slowed by the air, pulled down.</summary>
        private Vector3 ShardPosition(int index, float t)
        {
            float along = (1f - Mathf.Exp(-Drag * t)) / Drag;

            return (_shardVelocity[index] * along) + (Vector3.down * (0.5f * Gravity * t * t));
        }

        private Vector3 StarDirection(float angle) =>
            (_starAxis * Mathf.Cos(angle)) + (_starSide * Mathf.Sin(angle));

        /// <summary>Every vertex folded to the centre: nothing drawn, and the layout kept.</summary>
        private void Collapse()
        {
            if (_mesh == null)
            {
                return;
            }

            Fold(0, VertexCount);

            _mesh.SetVertices(_vertices);
        }

        private void Fold(int first, int count)
        {
            Array.Clear(_vertices, first, count);
        }

        /// <summary>
        /// The mesh, built once: its triangles and colours never change, only where its vertices are.
        /// Bounds are fixed rather than recalculated, since nothing reaches past them.
        /// </summary>
        private void EnsureMesh()
        {
            if (_mesh != null)
            {
                return;
            }

            _mesh = new Mesh { name = "Impact" };
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

        /// <summary>
        /// White, shaded face by face, with one face of each shard in the player's cyan: a glint as it
        /// tumbles, and the only cyan in the effect. GD §16.4 reserves red-orange, gold and violet, and
        /// none is here.
        /// </summary>
        /// <remarks>
        /// The ring was cyan until the first render: a cyan circle round an enemy for 0.15 s read as the
        /// targeting reticle, and it was the cyan flash the owner had just had removed (RS-06b).
        /// </remarks>
        private static Color32[] BuildColours()
        {
            var colours = new Color32[VertexCount];

            for (int v = 0; v < StarVertices; v++)
            {
                // Each spike's two halves differ, so the flat star reads as a cut one.
                colours[v] = Shade(Palette.HitFlash, (v / 3) % 2 == 0 ? 1f : 0.84f);
            }

            for (int v = ShardBase; v < RingBase; v++)
            {
                int face = ((v - ShardBase) / 3) % 4;

                colours[v] = face == GlintFace ? Palette.Player : Shade(Palette.HitFlash, ShardShade(face));
            }

            for (int v = RingBase; v < VertexCount; v++)
            {
                colours[v] = Palette.HitFlash;
            }

            return colours;
        }

        private static int[] BuildTriangles()
        {
            var triangles = new int[RingBase + (RingSegments * 6)];

            // The star and the shards own their vertices three to a triangle, in order.
            for (int i = 0; i < RingBase; i++)
            {
                triangles[i] = i;
            }

            // The ring shares its vertices: outer and inner in pairs, two triangles per segment.
            for (int j = 0; j < RingSegments; j++)
            {
                int outer = RingBase + (j * 2);
                int inner = outer + 1;
                int nextOuter = RingBase + (((j + 1) % RingSegments) * 2);
                int nextInner = nextOuter + 1;
                int t = RingBase + (j * 6);

                triangles[t] = outer;
                triangles[t + 1] = inner;
                triangles[t + 2] = nextOuter;
                triangles[t + 3] = inner;
                triangles[t + 4] = nextInner;
                triangles[t + 5] = nextOuter;
            }

            return triangles;
        }

        private static Color32 Shade(Color colour, float shade) =>
            new Color(colour.r * shade, colour.g * shade, colour.b * shade, 1f);

        /// <summary>A shard's four face shades: two lit sides, one in shadow, and its base.</summary>
        private static float ShardShade(int face) => face switch
        {
            0 => 1f,
            1 => 0.8f,
            2 => 0.64f,
            _ => 0.9f,
        };

        /// <summary>The long spike ahead, a short one behind, middling ones across and short diagonals.</summary>
        private static float SpikeLength(int k)
        {
            if (k == 0)
            {
                return 1f;
            }

            if (k == StarPoints / 2)
            {
                return 0.45f;
            }

            return (k & 1) == 0 ? 0.6f : 0.32f;
        }

        /// <summary>A seed spread over all 32 bits, so neighbouring shot ids throw unlike shards.</summary>
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
