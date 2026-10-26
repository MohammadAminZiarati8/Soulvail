using Soulvail.Game.Pooling;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. A file-scoped namespace would leave
// VFX_Tongue.prefab's reference to this component deserialising as null with nothing reported
// (Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// Old Croaker's tongue as it strikes (JG-06a): a pink tapered tube with a knob on the end, shot
    /// from the mouth to the far end of the lane that just fired, held a moment, then pulled back.
    /// It decides nothing: <c>LaneFired</c> has already said a lane bit, and <c>LaneArmed</c> said
    /// where it runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why it is drawn rather than animated.</b> The model's own tongue reaches a few metres at
    /// <c>Lash</c>'s reach frame, and the lane hurts 9 m out, so a hit past its tip had nothing on it.
    /// Stretching the <c>tongue.tip</c> bone would fight the Animator and the skin weights; this lies
    /// along the lane exactly, and the model's tongue still flicks out underneath it.
    /// </para>
    /// <para>
    /// <b>One mesh, one opaque material, one draw call</b>, <see cref="ImpactView"/>'s pattern: the
    /// vertices are rewritten each step on <c>M_Impact</c>, whose shader multiplies vertex colours,
    /// and the upper half of the tube is lit pink and the underside dark, so it reads as round with no
    /// light. It adds no transparent layer (GD §11.3).
    /// </para>
    /// <para>
    /// <b>It runs on its own clock</b>, stepped by <c>BossViews</c> with the snapshot's <c>Dt</c>,
    /// so a lane core cleared silently still leaves no tongue standing.
    /// </para>
    /// </remarks>
    public sealed class TongueView : MonoBehaviour, IPoolable
    {
        /// <summary>Seconds from the mouth to full length.</summary>
        public const float ShootSeconds = 0.06f;

        /// <summary>Seconds held at full length.</summary>
        public const float HoldSeconds = 0.12f;

        /// <summary>Seconds back to the mouth.</summary>
        public const float RetractSeconds = 0.2f;

        /// <summary>Metres above the lane's origin the mouth is: Old Croaker's, at its 2.2 body scale.</summary>
        public const float MouthHeight = 1.4f;

        /// <summary>Metres ahead of the lane's origin (the body's centre) the mouth is.</summary>
        public const float MouthForward = 1f;

        /// <summary>Metres above the ground the tip lands at, at the lane's far end.</summary>
        public const float TipHeight = 0.3f;

        /// <summary>The whole life, in seconds.</summary>
        public const float Lifetime = ShootSeconds + HoldSeconds + RetractSeconds;

        private const int Sides = 8;
        private const int Rings = 6;
        private const int KnobRings = 4;
        private const int TubeVertices = Sides * Rings;
        private const int KnobVertices = (Sides * KnobRings) + 2;
        private const int VertexCount = TubeVertices + KnobVertices;

        private const float RootRadius = 0.3f;
        private const float TipRadius = 0.17f;
        private const float KnobRadius = 0.4f;

        /// <summary>The Frog's tongue swatch (<c>enemy_atlas.py</c>, <c>frog_tongue</c>): lit and shadow.</summary>
        private static readonly Color32 Lit = new Color32(0xEC, 0x8F, 0xA2, 0xFF);

        private static readonly Color32 Shadow = new Color32(0xB5, 0x55, 0x70, 0xFF);

        [Tooltip("The one mesh this tongue is drawn with, on this object. Its mesh is built at " +
                 "runtime; the renderer beside it wears M_Impact.")]
        [SerializeField] private MeshFilter _filter;

        [SerializeField] private MeshRenderer _renderer;

        private readonly Vector3[] _vertices = new Vector3[VertexCount];

        private Mesh _mesh;
        private float _fullLength;
        private float _age;
        private bool _live;

        /// <summary>True from <see cref="Fire"/> until the tongue is back in the mouth.</summary>
        public bool IsLive => _live;

        /// <summary>Metres from the mouth to the tip, mouth to lane end at full length.</summary>
        public float FullLength => _fullLength;

        /// <summary>The length drawn now, in metres: 0 when not live.</summary>
        public float Length => _live ? _fullLength * Reach(_age) : 0f;

        /// <summary>
        /// Lays the tongue along a lane that has just fired: from the mouth above and ahead of
        /// <paramref name="origin"/>, along <paramref name="directionXZ"/>, down to
        /// <see cref="TipHeight"/> at <paramref name="length"/> metres out. Restarts a live tongue.
        /// </summary>
        /// <param name="origin">The lane's origin, <c>LaneArmed.Origin</c>: the body's centre on the ground.</param>
        /// <param name="directionXZ">The lane's direction, X then Z. A zero one is read as +Z.</param>
        /// <param name="length">The lane's length, in metres.</param>
        public void Fire(Vector3 origin, Vector2 directionXZ, float length)
        {
            EnsureMesh();

            var flat = new Vector3(directionXZ.x, 0f, directionXZ.y);

            flat = flat.sqrMagnitude > 1e-8f ? flat.normalized : Vector3.forward;

            Vector3 mouth = origin + (flat * MouthForward) + (Vector3.up * MouthHeight);
            Vector3 tip = origin + (flat * Mathf.Max(length, MouthForward)) + (Vector3.up * TipHeight);
            Vector3 along = tip - mouth;

            _fullLength = along.magnitude;

            transform.SetPositionAndRotation(
                mouth,
                _fullLength > 1e-4f ? Quaternion.LookRotation(along / _fullLength, Vector3.up) : Quaternion.LookRotation(flat));

            _age = 0f;
            _live = true;

            Draw();
            SetVisible(true);
        }

        /// <summary>
        /// Advances the tongue by <paramref name="dt"/> seconds. At <see cref="Lifetime"/> it is back in
        /// the mouth, hidden, and <see cref="IsLive"/> reads false. A zero, negative or non-finite step
        /// does nothing.
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
                Hide();

                return;
            }

            Draw();
        }

        /// <inheritdoc />
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>A body rented again draws nothing of its last strike before its next <see cref="Fire"/>.</remarks>
        public void OnDespawn()
        {
            Hide();
        }

        private void Awake()
        {
            // Built while the pool prewarms, so the first lash of a run builds nothing.
            EnsureMesh();
            SetVisible(_live);
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

        /// <summary>The fraction of full length at <paramref name="age"/>: out fast, held, back eased.</summary>
        private static float Reach(float age)
        {
            if (age < ShootSeconds)
            {
                float u = age / ShootSeconds;

                return 1f - ((1f - u) * (1f - u));
            }

            if (age < ShootSeconds + HoldSeconds)
            {
                return 1f;
            }

            float back = (age - ShootSeconds - HoldSeconds) / RetractSeconds;

            return back >= 1f ? 0f : 1f - (back * back);
        }

        private void Hide()
        {
            _live = false;
            _age = 0f;

            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_renderer == null)
            {
                TryGetComponent(out _renderer);
            }

            if (_renderer != null)
            {
                _renderer.enabled = visible;
            }
        }

        /// <summary>The tube along local +Z to the length now, and the knob on its end.</summary>
        private void Draw()
        {
            float length = Length;
            float knob = Mathf.Min(KnobRadius, Mathf.Max(length, 0.01f) * 0.5f);
            const float step = Mathf.PI * 2f / Sides;

            for (int r = 0; r < Rings; r++)
            {
                float t = r / (float)(Rings - 1);
                float radius = Mathf.Lerp(RootRadius, TipRadius, t);
                float z = length * t;

                for (int s = 0; s < Sides; s++)
                {
                    float angle = s * step;

                    _vertices[(r * Sides) + s] = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, z);
                }
            }

            int k = TubeVertices;
            var centre = new Vector3(0f, 0f, length);

            _vertices[k] = centre + new Vector3(0f, 0f, -knob);
            _vertices[k + 1] = centre + new Vector3(0f, 0f, knob);

            for (int r = 0; r < KnobRings; r++)
            {
                float polar = Mathf.PI * (r + 1) / (KnobRings + 1);
                float ringRadius = Mathf.Sin(polar) * knob;
                float z = -Mathf.Cos(polar) * knob;

                for (int s = 0; s < Sides; s++)
                {
                    float angle = s * step;

                    _vertices[k + 2 + (r * Sides) + s] =
                        centre + new Vector3(Mathf.Cos(angle) * ringRadius, Mathf.Sin(angle) * ringRadius, z);
                }
            }

            _mesh.SetVertices(_vertices);
            _mesh.RecalculateBounds();
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

            _mesh = new Mesh { name = "Tongue" };
            _mesh.MarkDynamic();
            _mesh.SetVertices(_vertices);
            _mesh.SetColors(BuildColours());
            _mesh.SetTriangles(BuildTriangles(), 0, false);

            if (_filter != null)
            {
                _filter.sharedMesh = _mesh;
            }
        }

        /// <summary>Lit along the top of the tube, shadowed under it: round, with no light.</summary>
        private static Color32[] BuildColours()
        {
            var colours = new Color32[VertexCount];
            const float step = Mathf.PI * 2f / Sides;

            for (int v = 0; v < TubeVertices; v++)
            {
                colours[v] = SideColour((v % Sides) * step);
            }

            int k = TubeVertices;

            colours[k] = Shadow;
            colours[k + 1] = Lit;

            for (int v = 0; v < Sides * KnobRings; v++)
            {
                colours[k + 2 + v] = SideColour((v % Sides) * step);
            }

            return colours;
        }

        /// <summary>The lit colour at the top (+Y), the shadow at the bottom, blended between.</summary>
        private static Color32 SideColour(float angle) =>
            Color32.Lerp(Shadow, Lit, (Mathf.Sin(angle) + 1f) * 0.5f);

        private static int[] BuildTriangles()
        {
            int tubeTriangles = (Rings - 1) * Sides * 6;
            int knobTriangles = ((KnobRings - 1) * Sides * 6) + (Sides * 6);
            var triangles = new int[tubeTriangles + knobTriangles];
            int t = 0;

            // Clockwise seen from outside, which is Unity's front face.
            for (int r = 0; r < Rings - 1; r++)
            {
                t = Band(triangles, t, r * Sides, (r + 1) * Sides);
            }

            int k = TubeVertices;
            int first = k + 2;

            for (int r = 0; r < KnobRings - 1; r++)
            {
                t = Band(triangles, t, first + (r * Sides), first + ((r + 1) * Sides));
            }

            int last = first + ((KnobRings - 1) * Sides);

            for (int s = 0; s < Sides; s++)
            {
                int next = (s + 1) % Sides;

                // The back cap points −Z, the front cap +Z.
                triangles[t++] = k;
                triangles[t++] = first + next;
                triangles[t++] = first + s;

                triangles[t++] = k + 1;
                triangles[t++] = last + s;
                triangles[t++] = last + next;
            }

            return triangles;
        }

        /// <summary>Two triangles per side between two rings of <see cref="Sides"/> vertices.</summary>
        private static int Band(int[] triangles, int t, int ring, int nextRing)
        {
            for (int s = 0; s < Sides; s++)
            {
                int next = (s + 1) % Sides;

                triangles[t++] = ring + s;
                triangles[t++] = ring + next;
                triangles[t++] = nextRing + s;

                triangles[t++] = ring + next;
                triangles[t++] = nextRing + next;
                triangles[t++] = nextRing + s;
            }

            return t;
        }
    }
}
