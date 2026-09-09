using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Player.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The cyan disc under a planted character: CC §4.3's "subtle ground glow that intensifies".
    /// The only way the player can see that standing still is paying for itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It reads one event and decides nothing.</b> <c>FocusRampChanged</c> carries a level in
    /// <c>[0, 1]</c>; the alpha and the radius are that number scaled. There is no timer here, no
    /// notion of what standing still means and no reading of the stick — all of that happened in
    /// <c>FocusTracker</c> before the event was published. AR §3: the view renders events.
    /// </para>
    /// <para>
    /// <b>The ramp is the feedback, so it has to be legible while it is happening.</b> Both the
    /// alpha and the radius move, rather than only the alpha, because a disc that merely brightened
    /// would be competing with the arena's own lighting at a glance — growing is the part that reads
    /// in peripheral vision, which is where this will actually be seen while the player is watching
    /// the enemies.
    /// </para>
    /// <para>
    /// <b>No <c>Update</c> at all.</b> The glow changes only when core says so, and core says so
    /// about twenty times over a 1.0 s ramp — so everything is written from the event handler. What
    /// the frame costs is a child transform riding along with its parent, which is free.
    /// </para>
    /// <para>
    /// The disc's mesh is built here rather than authored, for the reason <c>ReticleView</c> builds
    /// its ring here: a baked circle in the prefab would quietly disagree with the radius a designer
    /// changed. It is built at unit radius and scaled, so the radius is one transform write.
    /// </para>
    /// </remarks>
    public sealed class FocusGlowView : MonoBehaviour
    {
        /// <summary>
        /// Segments in the disc. 48 is a clean circle at the camera's 57° (GD §5.1) and costs 48
        /// triangles once per run.
        /// </summary>
        private const int Segments = 48;

        [Tooltip("The disc itself, on a child of the body. Its mesh is built at runtime from the " +
                 "numbers below — the filter may ship empty.")]
        [SerializeField] private MeshFilter _glow;

        [Tooltip("The glow's colour. Cyan, the player's own colour: red is reserved for danger " +
                 "(GD §16.4), so a reward must never read as a threat.")]
        [SerializeField] private Color _colour = new Color(0.3f, 0.9f, 1f, 1f);

        [Tooltip("Alpha at full Focus. 0.35 is CC §4.3's 'subtle' — visible under the character " +
                 "without hiding what is standing on it.")]
        [Range(0f, 1f)]
        [SerializeField] private float _maxAlpha = 0.35f;

        [Tooltip("Radius in metres at zero Focus — the size the disc pops in at.")]
        [Min(0.1f)]
        [SerializeField] private float _baseRadius = 1f;

        [Tooltip("Metres of radius added at full Focus, on top of the base above: 1 m becomes " +
                 "1.5 m over the ramp.")]
        [Min(0f)]
        [SerializeField] private float _radiusGain = 0.5f;

        [Tooltip("Metres above the floor. Enough to clear z-fighting with the arena, little " +
                 "enough that the disc still reads as lying on the ground.")]
        [SerializeField] private float _groundOffset = 0.015f;

        /// <summary>
        /// URP's colour property, hashed once. An instance field rather than a static, because
        /// nothing in this project holds static state (AR §7) and the four bytes are free.
        /// </summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Reused for every colour write. A property block rather than the material, because
        /// assigning to <c>Renderer.material</c> instantiates a copy — and this renderer shares its
        /// material with the reticle and the swing wedge, which would then be drawing a different
        /// asset than they thought.
        /// </summary>
        private MaterialPropertyBlock _block;

        private Renderer _renderer;
        private Mesh _mesh;
        private IDisposable _subscription;
        private bool _injected;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        /// <remarks>
        /// Subscribing here rather than in <c>OnEnable</c>, and for the reason <c>ReticleView</c>
        /// gives: <c>RunScope</c> injects during its own <c>Awake</c>, well before the first tick,
        /// so this is listening before core can publish anything.
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _injected = true;

            _subscription = hub.Subscribe<FocusRampChanged>(OnFocusRampChanged);
        }

        private void Awake()
        {
            if (_glow == null)
            {
                return;
            }

            _block = new MaterialPropertyBlock();
            _renderer = _glow.GetComponent<Renderer>();

            // Held in a field as well as assigned, because a Mesh created in code is not collected
            // when the filter holding it is destroyed — whoever made it has to destroy it.
            _mesh = BuildDisc();
            _glow.sharedMesh = _mesh;

            _glow.transform.localPosition = new Vector3(0f, _groundOffset, 0f);

            // Hidden until core says otherwise. A disc under a character who has not stood still
            // yet would be the game claiming a reward nobody earned.
            Apply(0f);
        }

        /// <exception cref="InvalidOperationException">A glow is dressed but nothing injected it.</exception>
        /// <remarks>
        /// Only an error when there is something to draw, which is what makes the glow optional in
        /// the same way the reticle and the swing wedge are: a test scene may legitimately not dress
        /// one. Checked in <c>Start</c> rather than <c>Awake</c> because <c>RunScope</c> injects
        /// from its own <c>Awake</c> and Unity gives no order between two of those (M0-18).
        /// </remarks>
        private void Start()
        {
            if (_glow != null && !_injected)
            {
                throw new InvalidOperationException(
                    $"{nameof(FocusGlowView)} has a glow but was never injected, so Focus will " +
                    "never be visible. The component is registered by RunScope — drag the Player " +
                    "object in this scene onto its Focus Glow field.");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribed explicitly rather than left to the hub's disposal, for the reason
            // ReticleView unsubscribes: a body destroyed mid-run would otherwise stay in the
            // subscriber list and be handed events for a component Unity has already killed.
            _subscription?.Dispose();
            _subscription = null;

            if (_mesh == null)
            {
                return;
            }

            // The play-mode split PlayerView makes: in edit mode Destroy destroys nothing and logs
            // an error rather than throwing, which would leak the mesh and redden any test that
            // tears one of these down (M0-14).
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

        private void OnFocusRampChanged(FocusRampChanged evt)
        {
            Apply(evt.Level);
        }

        /// <summary>Rule 6: alpha <c>0.35 × level</c>, radius <c>1 + 0.5 × level</c>, gone at zero.</summary>
        /// <remarks>
        /// The level is clamped rather than trusted. Core guarantees <c>[0, 1]</c> and there is no
        /// path by which it would not, but an alpha outside the range is the kind of thing a URP
        /// blend mode renders as a black disc rather than as an error, and one clamp is cheaper than
        /// the bug report.
        /// </remarks>
        private void Apply(float level)
        {
            if (_glow == null)
            {
                return;
            }

            float clamped = Mathf.Clamp01(level);
            bool visible = clamped > 0f;

            if (_glow.gameObject.activeSelf != visible)
            {
                _glow.gameObject.SetActive(visible);
            }

            if (!visible)
            {
                return;
            }

            float radius = _baseRadius + (_radiusGain * clamped);

            _glow.transform.localScale = new Vector3(radius, 1f, radius);

            if (_renderer == null)
            {
                return;
            }

            // Filled from the renderer first so that a material with anything else on it keeps it —
            // a block replaces every property it names and leaves the rest alone only if they were
            // read in.
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(
                _baseColorId,
                new Color(_colour.r, _colour.g, _colour.b, _maxAlpha * clamped));
            _renderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// A flat triangle fan of unit radius on the XZ plane, centred on the child's origin.
        /// </summary>
        /// <remarks>
        /// Wound so the face points up: for a triangle <c>(v0, v1, v2)</c> Unity's normal is
        /// <c>cross(v1 − v0, v2 − v0)</c>, and going clockwise seen from above gives +Y. Wound the
        /// other way the disc would be invisible from the only angle the game is ever seen from.
        /// </remarks>
        private Mesh BuildDisc()
        {
            var vertices = new Vector3[Segments + 2];
            var triangles = new int[Segments * 3];

            vertices[0] = Vector3.zero;

            float step = 2f * Mathf.PI / Segments;

            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * step;
                vertices[i + 1] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            }

            for (int i = 0; i < Segments; i++)
            {
                triangles[i * 3] = 0;
                triangles[(i * 3) + 1] = i + 1;
                triangles[(i * 3) + 2] = i + 2;
            }

            var mesh = new Mesh { name = "FocusGlow" };

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
