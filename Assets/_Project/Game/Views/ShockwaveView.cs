using System;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.Serialization;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and VFX_Shockwave.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One slam's ring, running out over the ground from where the boss landed: a red band exactly as
    /// wide as the part that bites, with a ridge of earth riding its front edge and grit flying off
    /// it (JG-06c). It decides nothing and knows no events: <c>ShockwaveEmitted</c> has already said
    /// where it started, how fast it grows, how far it reaches and how wide it is, and this integrates
    /// that and then goes away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is a complete description of the ring and it asks core nothing.</b> A radius is
    /// <c>Speed × age</c>, so the numbers off the event are the whole of what a ring is for its whole
    /// life — which is exactly why M4-02 put them on the event rather than pairing it with a read on
    /// <c>RunState</c> the way <c>ZoneSpawned</c> is. A ring is an animation and a zone is a place: an
    /// animation a view can run itself is one that cannot stutter when a frame is dropped.
    /// </para>
    /// <para>
    /// <b>The band is the bite.</b> <c>ShockwaveSystem</c> bites out to half a thickness ahead of
    /// <c>Speed × age</c>, so the band runs from half a thickness behind that to half a thickness
    /// ahead, capped at the ceiling: its outer rim is exactly the circle core tests. Until JG-06c the
    /// ring was a disc drawn at <c>Speed × age</c> and faded, which read as a red plate growing rather
    /// than a wave.
    /// </para>
    /// <para>
    /// <b>Two meshes, both rebuilt per step.</b> The band is an annulus of <see cref="Segments"/> on
    /// the telegraph's material, every UV inside the ring texture's solid rim, so the band is one even
    /// red and nothing stretches. Its area is a
    /// band's rather than a 12 m disc's, which is GD §11.3's fill-rate note answered. The earth lip
    /// and grit are one opaque vertex-coloured mesh on <c>M_Impact</c> on a child, so the wave adds no
    /// second transparent layer.
    /// </para>
    /// <para>
    /// <b>Its lifetime is derived, never authored.</b> <see cref="Lifetime"/> is
    /// <c>MaxRadius / Speed</c>, read off the event rather than off a boss's constants. A second boss
    /// with a slower ring would otherwise be drawn at the first one's speed, and nothing would say so.
    /// </para>
    /// <para>
    /// <b>The countdown is the net, and <c>ShockwavePassed</c> is the rule.</b> Rule 7: a ring whose
    /// retirement never reached the census must not expand for the rest of the run, and running out
    /// is the one thing such a ring can safely be assumed to have done. In a run where every event
    /// arrives, the event retires it first — <see cref="ZoneView"/>'s net, for its reason.
    /// </para>
    /// <para>
    /// <b>It owns no frame loop.</b> <c>RunTicker</c> steps this with the snapshot's clamped
    /// <c>Dt</c> — the step core ran the ring's own expansion on, so a ring advanced on
    /// <c>Time.deltaTime</c> would be somewhere other than where the edge that hurts is, on exactly
    /// the hitching frames the clamp exists for (AR §18.1, §18.2).
    /// </para>
    /// <para>
    /// <b>It is pooled, so <see cref="OnDespawn"/> has a job.</b> The same body is a Warden's ring,
    /// then Old Croaker's; the size, the place, the age and both meshes are all inherited by the next
    /// life unless they are undone, and the failure mode is silence (AR §18.4).
    /// </para>
    /// </remarks>
    public sealed class ShockwaveView : MonoBehaviour, IPoolable
    {
        /// <summary>Segments round the band and the lip.</summary>
        public const int Segments = 48;

        /// <summary>The earth wave's crest, in metres above the ground.</summary>
        public const float LipHeight = 0.35f;

        /// <summary>
        /// How far behind the crest the lip's back slope reaches the ground, in metres: a third of the
        /// Warden's and Old Croaker's 1.2 m band, so the red still shows behind it.
        /// </summary>
        public const float LipDepth = 0.4f;

        /// <summary>How far ahead of the crest its front face reaches the ground, in metres.</summary>
        public const float LipFront = 0.1f;

        /// <summary>The band's width when the event did not carry one: the edge, drawn half a metre wide.</summary>
        public const float EdgeOnlyBand = 0.5f;

        /// <summary>Grit specks flying off the crest.</summary>
        public const int GritCount = 16;

        private const int BandVertices = Segments * 2;
        private const int LipRingVertices = Segments * 4;
        private const int GritBase = LipRingVertices;
        private const int GritVertices = 4;
        private const int LipVertexCount = GritBase + (GritCount * GritVertices);

        /// <summary>How high a grit speck flies above the crest, and how far back it lands, in metres.</summary>
        private const float GritRise = 0.5f;

        private const float GritFall = 0.8f;

        /// <summary>How many hops a speck makes per second.</summary>
        private const float GritRate = 3f;

        private const float GritSize = 0.12f;

        /// <summary>
        /// The radii of the ring texture the band samples, in UV: inside its solid rim
        /// (<c>T_TelegraphRing_Mask</c> is opaque from 0.475 out and 45 % inside), so the band is the
        /// full red rather than the disc's faint fill.
        /// </summary>
        private const float RimUvOuter = 0.49f;

        private const float RimUvInner = 0.48f;

        /// <summary>
        /// Jungle earth, lit and in shadow. Written dark because vertex colours are read as linear in
        /// this project and come out lighter than written. GD §16.4 keeps red-orange for the band alone.
        /// </summary>
        private static readonly Color EarthLit = new Color32(0x6E, 0x4E, 0x30, 0xFF);

        private static readonly Color EarthShadow = new Color32(0x30, 0x20, 0x12, 0xFF);

        /// <summary>The direction the light comes from on the ground, for the lip's per-face shade.</summary>
        private static readonly Vector3 LightFrom = new Vector3(-0.5f, 0f, -0.85f).normalized;

        [Tooltip("The band's renderer, on this object, with the telegraph's material. Its mesh is " +
                 "built at runtime on the MeshFilter beside it.")]
        [FormerlySerializedAs("_quad")]
        [SerializeField] private MeshRenderer _band;

        [Tooltip("The earth lip's mesh filter, on a child with M_Impact. Optional — without it the " +
                 "band is drawn alone.")]
        [SerializeField] private MeshFilter _lip;

        [Tooltip("How bright the band sits at while it is travelling.")]
        [Range(0f, 1f)]
        [SerializeField] private float _baseAlpha = 0.65f;

        [Tooltip("The last fraction of the ring's life it spends fading out, in [0, 1]: the band's " +
                 "alpha and the lip's height both go to nothing over it. Without it the ring " +
                 "vanishes at full strength on the frame it reaches its ceiling, which reads as a " +
                 "dropped frame rather than as a wave that has passed.")]
        [Range(0f, 1f)]
        [SerializeField] private float _fadeFraction = 0.2f;

        [Tooltip("Metres the band floats above the floor, to keep it out of a z-fight with the " +
                 "arena it is drawn on. Cosmetic in the strict sense: no value here can change " +
                 "which circle core tested, because every distance in the game is measured on XZ.")]
        [Min(0f)]
        [SerializeField] private float _groundOffset = 0.08f;

        /// <summary>URP's colour property, resolved once rather than per write.</summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private readonly Vector3[] _bandVertices = new Vector3[BandVertices];
        private readonly Vector2[] _bandUvs = new Vector2[BandVertices];
        private readonly Vector3[] _lipVertices = new Vector3[LipVertexCount];

        /// <summary>
        /// Where the colour and the alpha are written, so nothing here ever touches a shared
        /// material. Built on first use: a field initialiser throws at <em>import</em> from inside
        /// prefab serialisation, and <c>Awake</c> never runs in EditMode (Traps §5).
        /// </summary>
        private MaterialPropertyBlock _properties;

        private Mesh _bandMesh;
        private Mesh _lipMesh;

        private float _speed;
        private float _maxRadius;
        private float _thickness;
        private float _elapsed;
        private bool _live;

        /// <summary>
        /// The pose this body was created at, restored on the way back to the pool. Captured in
        /// <see cref="Awake"/>, which outside play mode never runs (Traps §5); the initialisers
        /// describe a prefab authored at the origin, which <c>VFX_Shockwave.prefab</c> is.
        /// </summary>
        private Vector3 _restPosition = Vector3.zero;

        /// <summary>True while this ring is still travelling.</summary>
        public bool IsLive => _live;

        /// <summary>
        /// <c>Speed × age</c>, in metres, capped at the ceiling — the nominal radius core keeps, whose
        /// front edge is half a thickness further out. Zero on the frame it is bound, and in the pool.
        /// </summary>
        public float Radius => _live ? Mathf.Min(_speed * _elapsed, _maxRadius) : 0f;

        /// <summary>
        /// The band's outer rim, in metres: half a thickness ahead of <see cref="Radius"/>, capped at the
        /// ceiling — exactly the circle <c>ShockwaveSystem</c> bites out to. Zero in the pool.
        /// </summary>
        public float OuterRadius => _live ? Mathf.Min(Radius + (_thickness * 0.5f), _maxRadius) : 0f;

        /// <summary>
        /// The band's inner rim, in metres: half a thickness behind <see cref="Radius"/>, never below
        /// zero. With no thickness on the event, <see cref="EdgeOnlyBand"/> behind the edge.
        /// </summary>
        public float InnerRadius
        {
            get
            {
                if (!_live)
                {
                    return 0f;
                }

                float behind = _thickness > 0f ? _thickness * 0.5f : EdgeOnlyBand;

                return Mathf.Max(0f, Radius - behind);
            }
        }

        /// <summary>The band's width off the event, in metres. Zero in the pool.</summary>
        public float Thickness => _live ? _thickness : 0f;

        /// <summary>How fast it grows, in metres per second — the event's own number. Zero in the pool.</summary>
        public float Speed => _live ? _speed : 0f;

        /// <summary>How far it reaches before it is over. Beyond this is the safe ground. Zero in the pool.</summary>
        public float MaxRadius => _live ? _maxRadius : 0f;

        /// <summary>How long this ring lasts, in seconds: <see cref="MaxRadius"/> over <see cref="Speed"/>.</summary>
        public float Lifetime => _live && _speed > 0f ? _maxRadius / _speed : 0f;

        /// <summary>
        /// How strong the ring is, in <c>[0, 1]</c> of full: 1 until the fade begins, then linearly to
        /// nothing. Scales the band's alpha and the lip's height alike. Zero in the pool.
        /// </summary>
        public float Strength
        {
            get
            {
                if (!_live)
                {
                    return 0f;
                }

                float fade = Mathf.Clamp01(_fadeFraction);

                if (fade <= 0f)
                {
                    return 1f;
                }

                float remaining = 1f - Progress();

                return remaining >= fade ? 1f : remaining / fade;
            }
        }

        /// <summary>The band's alpha now: its resting alpha times <see cref="Strength"/>.</summary>
        public float Alpha => _baseAlpha * Strength;

        /// <summary>The lip's crest now, in metres above the ground: <see cref="LipHeight"/> times <see cref="Strength"/>.</summary>
        public float LipCrestHeight => LipHeight * Strength;

        /// <summary>
        /// The colour a slam's band is drawn in: GD §16.4's reserved red-orange, because a ring that
        /// takes 22 hit points off you is danger and nothing else.
        /// </summary>
        public Color Colour => Palette.Danger;

        /// <summary>Whether this body has the band renderer it draws with.</summary>
        /// <remarks>
        /// Exists for one reader: <c>RunScope</c> checks it on the prefab while the run is being
        /// composed, because a ring whose renderer was never dragged into the field runs its whole
        /// life correctly and draws nothing — the feature silently absent rather than broken.
        /// </remarks>
        public bool IsDrawable => _band != null;

        /// <summary>
        /// Sends this ring out from <paramref name="origin"/> at <paramref name="speed"/> metres a
        /// second, as far as <paramref name="maxRadius"/>, as a band <paramref name="thickness"/> wide.
        /// </summary>
        /// <param name="origin">The slam point, in world metres — where the boss <em>stood</em>.</param>
        /// <param name="speed">How fast the front edge travels. The event's number, never a constant.</param>
        /// <param name="maxRadius">Where the ring is over. The event's number, never a constant.</param>
        /// <param name="thickness">
        /// The band's width, the event's number. Zero, negative or non-finite is read as "not said",
        /// and the edge alone is drawn.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="speed"/> or <paramref name="maxRadius"/> is not a finite positive number.
        /// Neither has a meaningful zero: a ring that does not travel never reaches anybody, and one
        /// with no ceiling never ends.
        /// </exception>
        public void Bind(Vector3 origin, float speed, float maxRadius, float thickness = 0f)
        {
            // Negated positives, so NaN is refused rather than admitted (AR §18.3).
            if (!(speed > 0f) || float.IsInfinity(speed))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(speed),
                    speed,
                    "A shockwave's speed must be a finite number greater than zero — it is what " +
                    "turns the ring's age into the circle core is testing, so there is no ring " +
                    "without one.");
            }

            if (!(maxRadius > 0f) || float.IsInfinity(maxRadius))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxRadius),
                    maxRadius,
                    "A shockwave's ceiling must be a finite number greater than zero; a ring with " +
                    "no ceiling is one that never leaves the floor it was slammed on.");
            }

            EnsureMeshes();

            _speed = speed;
            _maxRadius = maxRadius;
            _thickness = thickness > 0f && !float.IsInfinity(thickness) ? thickness : 0f;
            _elapsed = 0f;
            _live = true;

            // Placed and drawn immediately rather than on the first Step, so a ring is never shown
            // for a frame at the size and place of the one before it.
            transform.SetPositionAndRotation(
                new Vector3(origin.x, origin.y + _groundOffset, origin.z),
                Quaternion.identity);

            Draw();
        }

        /// <summary>Advances the ring by <paramref name="dt"/> seconds.</summary>
        /// <param name="dt">
        /// The snapshot's clamped step, never <c>Time.deltaTime</c> — see the class remarks. A zero,
        /// a negative or a non-finite step does nothing.
        /// </param>
        /// <returns>
        /// False once its own countdown is over, which is the census's cue to return it. True while
        /// it is still travelling — including for a step that did nothing.
        /// </returns>
        public bool Step(float dt)
        {
            if (!_live)
            {
                return false;
            }

            // Negated, so NaN is refused with the rest.
            if (!(dt > 0f) || float.IsInfinity(dt))
            {
                return true;
            }

            _elapsed += dt;

            if (_elapsed >= Lifetime)
            {
                // Out of service on the step it reaches its ceiling, which is the step the census
                // returns it. Not drawn again first: the body is about to be deactivated.
                _live = false;

                return false;
            }

            Draw();

            return true;
        }

        /// <summary>The band's vertex <paramref name="index"/>, in the ring's own space (Y up, origin at the slam point).</summary>
        /// <remarks>Even indices are on the outer rim, odd ones on the inner.</remarks>
        public Vector3 BandVertex(int index) => _bandVertices[index];

        /// <summary>The lip's crest at segment <paramref name="segment"/>, in the ring's own space.</summary>
        public Vector3 LipCrest(int segment) => _lipVertices[(segment * 4) + 1];

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to do, and deliberately so: <see cref="BossViews"/> calls <see cref="Bind"/> on
        /// the very next line. <see cref="OnDespawn"/> is where the pooling rule lives.
        /// </remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// The whole of "put this body back the way you found it": the place it was created at, the
        /// ring it was standing for, and both meshes folded to nothing, so a body rented again draws
        /// nothing of its last ring (AR §18.4).
        /// </remarks>
        public void OnDespawn()
        {
            transform.SetPositionAndRotation(_restPosition, Quaternion.identity);

            _speed = 0f;
            _maxRadius = 0f;
            _thickness = 0f;
            _elapsed = 0f;
            _live = false;

            Array.Clear(_bandVertices, 0, BandVertices);
            Array.Clear(_lipVertices, 0, LipVertexCount);

            if (_bandMesh != null)
            {
                _bandMesh.SetVertices(_bandVertices);
            }

            if (_lipMesh != null)
            {
                _lipMesh.SetVertices(_lipVertices);
            }

            Paint();
        }

        private void Awake()
        {
            _restPosition = transform.position;

            // Built while the pool prewarms, so the first slam of a run builds nothing.
            EnsureMeshes();
        }

        private void OnDestroy()
        {
            DestroyMesh(_bandMesh);
            DestroyMesh(_lipMesh);

            _bandMesh = null;
            _lipMesh = null;
        }

        /// <summary>The play-mode split every view that makes a mesh keeps (M0-14).</summary>
        private static void DestroyMesh(Mesh mesh)
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

        /// <summary>How far through its life the ring is, in <c>[0, 1]</c>.</summary>
        private float Progress()
        {
            float lifetime = Lifetime;

            return lifetime > 0f ? Mathf.Clamp01(_elapsed / lifetime) : 1f;
        }

        private void Draw()
        {
            DrawBand();
            DrawLip();
            Paint();
        }

        /// <summary>
        /// The annulus: outer and inner rims in pairs, every vertex sampling the ring texture's solid
        /// rim, so the band is one even red with no stretching.
        /// </summary>
        private void DrawBand()
        {
            if (_bandMesh == null)
            {
                return;
            }

            float outer = OuterRadius;
            float inner = Mathf.Min(InnerRadius, outer);
            const float step = Mathf.PI * 2f / Segments;

            for (int j = 0; j < Segments; j++)
            {
                float cos = Mathf.Cos(j * step);
                float sin = Mathf.Sin(j * step);

                _bandVertices[j * 2] = new Vector3(cos * outer, 0f, sin * outer);
                _bandVertices[(j * 2) + 1] = new Vector3(cos * inner, 0f, sin * inner);
                _bandUvs[j * 2] = new Vector2(0.5f + (RimUvOuter * cos), 0.5f + (RimUvOuter * sin));
                _bandUvs[(j * 2) + 1] = new Vector2(0.5f + (RimUvInner * cos), 0.5f + (RimUvInner * sin));
            }

            _bandMesh.SetVertices(_bandVertices);
            _bandMesh.SetUVs(0, _bandUvs);
            _bandMesh.RecalculateBounds();
        }

        /// <summary>
        /// The earth ridge: a back slope from <see cref="LipDepth"/> behind the crest up to it, and a
        /// steep front face down to <see cref="LipFront"/> ahead — four rings, so each face carries its
        /// own shade. Then the grit, hopping off the crest and landing behind it.
        /// </summary>
        private void DrawLip()
        {
            if (_lipMesh == null)
            {
                return;
            }

            float crest = OuterRadius;
            float height = LipCrestHeight - _groundOffset;
            float back = Mathf.Max(0f, crest - LipDepth);
            float front = crest + LipFront;
            const float step = Mathf.PI * 2f / Segments;

            for (int j = 0; j < Segments; j++)
            {
                float cos = Mathf.Cos(j * step);
                float sin = Mathf.Sin(j * step);
                int v = j * 4;

                _lipVertices[v] = new Vector3(cos * back, -_groundOffset, sin * back);
                _lipVertices[v + 1] = new Vector3(cos * crest, height, sin * crest);
                _lipVertices[v + 2] = new Vector3(cos * crest, height, sin * crest);
                _lipVertices[v + 3] = new Vector3(cos * front, -_groundOffset, sin * front);
            }

            float strength = Strength;

            for (int g = 0; g < GritCount; g++)
            {
                int v = GritBase + (g * GritVertices);
                float size = GritSize * strength;

                if (size <= 0f)
                {
                    Array.Clear(_lipVertices, v, GritVertices);

                    continue;
                }

                // Each speck its own place round the ring and its own phase in the hop.
                float angle = (g + 0.5f) * Mathf.PI * 2f / GritCount;
                float hop = Mathf.Repeat((_elapsed * GritRate) + (g * 0.37f), 1f);
                float r = crest - (GritFall * hop);
                float y = height + (GritRise * 4f * hop * (1f - hop));
                var at = new Vector3(Mathf.Cos(angle) * r, y, Mathf.Sin(angle) * r);

                _lipVertices[v] = at + new Vector3(0f, size, 0f);
                _lipVertices[v + 1] = at + new Vector3(size, -size * 0.5f, 0f);
                _lipVertices[v + 2] = at + new Vector3(-size * 0.5f, -size * 0.5f, size * 0.87f);
                _lipVertices[v + 3] = at + new Vector3(-size * 0.5f, -size * 0.5f, -size * 0.87f);
            }

            _lipMesh.SetVertices(_lipVertices);
            _lipMesh.RecalculateBounds();
        }

        /// <summary>The band's colour and alpha into the property block, and nowhere else.</summary>
        /// <remarks>
        /// <b>Premultiplied</b>: <c>M_TelegraphRing</c> blends One / OneMinusSrcAlpha without URP's
        /// <c>_ALPHAPREMULTIPLY_ON</c>, so the albedo is scaled here or the alpha only decides how
        /// much floor shows through. Tolerates a missing renderer, because <see cref="OnDespawn"/>
        /// runs while a scene is torn down; <c>RunScope</c> refuses a prefab with none, once.
        /// </remarks>
        private void Paint()
        {
            // Unity's ==: a destroyed or unassigned renderer compares equal to null only through it.
            if (_band == null)
            {
                return;
            }

            _properties ??= new MaterialPropertyBlock();

            Color colour = Palette.Danger;
            float alpha = Alpha;

            _properties.SetColor(
                _baseColorId,
                new Color(colour.r * alpha, colour.g * alpha, colour.b * alpha, alpha));

            _band.SetPropertyBlock(_properties);
        }

        /// <summary>Both meshes, built once: their triangles and colours never change, only their vertices.</summary>
        private void EnsureMeshes()
        {
            if (_bandMesh == null && TryGetComponent(out MeshFilter bandFilter))
            {
                _bandMesh = new Mesh { name = "ShockwaveBand" };
                _bandMesh.MarkDynamic();
                _bandMesh.SetVertices(_bandVertices);
                _bandMesh.SetUVs(0, _bandUvs);
                _bandMesh.SetTriangles(BandTriangles(), 0, false);

                bandFilter.sharedMesh = _bandMesh;
            }

            if (_lipMesh == null && _lip != null)
            {
                _lipMesh = new Mesh { name = "ShockwaveLip" };
                _lipMesh.MarkDynamic();
                _lipMesh.SetVertices(_lipVertices);
                _lipMesh.SetColors(LipColours());
                _lipMesh.SetTriangles(LipTriangles(), 0, false);

                _lip.sharedMesh = _lipMesh;
            }
        }

        /// <summary>
        /// Two triangles per segment between the rims, wound so (b − a) × (c − a) is +Y: Unity's
        /// front face, seen from the camera above.
        /// </summary>
        private static int[] BandTriangles()
        {
            var triangles = new int[Segments * 6];

            for (int j = 0; j < Segments; j++)
            {
                int outer = j * 2;
                int inner = outer + 1;
                int nextOuter = ((j + 1) % Segments) * 2;
                int nextInner = nextOuter + 1;
                int t = j * 6;

                triangles[t] = outer;
                triangles[t + 1] = inner;
                triangles[t + 2] = nextOuter;
                triangles[t + 3] = inner;
                triangles[t + 4] = nextInner;
                triangles[t + 5] = nextOuter;
            }

            return triangles;
        }

        /// <summary>The back slope and the front face, segment by segment, then each speck's four faces.</summary>
        private static int[] LipTriangles()
        {
            var triangles = new int[(Segments * 12) + (GritCount * 12)];
            int t = 0;

            for (int j = 0; j < Segments; j++)
            {
                int v = j * 4;
                int n = ((j + 1) % Segments) * 4;

                // Back slope: foot (0) up to crest (1), facing inward and up.
                triangles[t++] = v;
                triangles[t++] = n;
                triangles[t++] = v + 1;
                triangles[t++] = v + 1;
                triangles[t++] = n;
                triangles[t++] = n + 1;

                // Front face: crest (2) down to the outer foot (3), facing outward.
                triangles[t++] = v + 2;
                triangles[t++] = n + 2;
                triangles[t++] = v + 3;
                triangles[t++] = v + 3;
                triangles[t++] = n + 2;
                triangles[t++] = n + 3;
            }

            for (int g = 0; g < GritCount; g++)
            {
                int v = GritBase + (g * GritVertices);

                triangles[t++] = v;
                triangles[t++] = v + 1;
                triangles[t++] = v + 2;
                triangles[t++] = v;
                triangles[t++] = v + 2;
                triangles[t++] = v + 3;
                triangles[t++] = v;
                triangles[t++] = v + 3;
                triangles[t++] = v + 1;
                triangles[t++] = v + 1;
                triangles[t++] = v + 3;
                triangles[t++] = v + 2;
            }

            return triangles;
        }

        /// <summary>
        /// Earth, shaded by which way each face looks: the side of the ring facing the light is lit,
        /// the far side in shadow, and the front face always darker than the slope behind it.
        /// </summary>
        private static Color32[] LipColours()
        {
            var colours = new Color32[LipVertexCount];
            const float step = Mathf.PI * 2f / Segments;

            for (int j = 0; j < Segments; j++)
            {
                var outward = new Vector3(Mathf.Cos(j * step), 0f, Mathf.Sin(j * step));

                // The back slope faces inward, the front face outward.
                float slope = 0.55f + (0.45f * Mathf.Clamp01(Vector3.Dot(-outward, LightFrom) + 0.5f));
                float face = 0.4f + (0.35f * Mathf.Clamp01(Vector3.Dot(outward, LightFrom) + 0.5f));

                Color32 slopeColour = Color.Lerp(EarthShadow, EarthLit, slope);
                Color32 faceColour = Color.Lerp(EarthShadow, EarthLit, face);
                int v = j * 4;

                colours[v] = slopeColour;
                colours[v + 1] = slopeColour;
                colours[v + 2] = faceColour;
                colours[v + 3] = faceColour;
            }

            for (int v = GritBase; v < LipVertexCount; v++)
            {
                colours[v] = (v - GritBase) % 4 == 0 ? (Color32)EarthLit : (Color32)EarthShadow;
            }

            return colours;
        }
    }
}
