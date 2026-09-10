using System;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Player.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// The body. Core decided a velocity and a facing; this moves the capsule and turns it, and
    /// decides nothing at all. See AR §3 and CC §2.4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It reads no input, holds no gameplay state and never touches <c>RunState</c>. Everything
    /// it applies arrives in a <see cref="PlayerMoveIntent"/>, and everything it knows goes back
    /// out through <see cref="Position"/> and <see cref="Velocity"/>, which the snapshot builder
    /// reports as senses. If an <c>if</c> about gameplay ever wants to live here, it belongs in
    /// core.
    /// </para>
    /// <para>
    /// Gravity is the one number here that core does not own, and it is not a gameplay decision:
    /// there is no jump, no fall damage and no airborne state in this game's design — the
    /// downward push exists only so <c>CharacterController</c> keeps contact with the floor and
    /// reports <see cref="CharacterController.isGrounded"/> honestly. Nothing in core can observe
    /// it, because the snapshot's player position comes back from the controller either way.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerView : MonoBehaviour
    {
        /// <summary>
        /// Downward acceleration while airborne, in m/s². Steeper than Earth's, which is the
        /// convention in top-down action games: the character should settle onto a step or a ramp
        /// immediately rather than float down it.
        /// </summary>
        private const float Gravity = -20f;

        /// <summary>
        /// The downward speed held while grounded. Not zero: <c>CharacterController.isGrounded</c>
        /// is a report about the *last* <c>Move</c>, so a controller pushed with no vertical
        /// component drifts off the ground for a frame at a time and the flag flickers. A small
        /// constant push keeps it pinned and is invisible at any frame rate.
        /// </summary>
        private const float GroundedFallSpeed = -1f;

        /// <summary>Segments in the swing wedge's arc. 12 is smooth at 60° and costs nothing once.</summary>
        private const int SwingSegments = 12;

        [Tooltip("The flat wedge flashed on each swing. Optional, like the reticle and the debug " +
                 "overlay — a scene without one plays identically, it just cannot show the " +
                 "Censer's cadence. Its mesh is built at runtime from the two numbers below.")]
        [SerializeField] private MeshFilter _swingCone;

        [Tooltip("How long the wedge stays on screen, in seconds. A flash, not an animation: the " +
                 "Censer swings three times a second and anything longer would never go out.")]
        [Min(0f)]
        [SerializeField] private float _swingSeconds = 0.1f;

        [Tooltip("How far the drawn wedge reaches, in metres. PLACEHOLDER: it must be kept equal " +
                 "to the Censer's WeaponSpec range by hand until M7 gives the swing real VFX — " +
                 "PlayerAttacked carries only the facing, because the tell has to start before the " +
                 "cone that answers it exists.")]
        [Min(0.1f)]
        [SerializeField] private float _swingRange = 8f;

        [Tooltip("The drawn wedge's full opening angle, in degrees. PLACEHOLDER, on the same " +
                 "terms as the range above: 60 means 30° either side of the swing's facing.")]
        [Range(1f, 360f)]
        [SerializeField] private float _swingAngleDeg = 60f;

        [Tooltip("The wedge's colour. Cyan at a quarter alpha — the player's own colour, faint " +
                 "enough to read the enemies through it.")]
        [SerializeField] private Color _swingColour = new Color(0.3f, 0.9f, 1f, 0.25f);

        /// <summary>
        /// URP's colour property, hashed once. An instance field rather than a static, because
        /// nothing in this project holds static state (AR §7) and the four bytes are free.
        /// </summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private CharacterController _controller;
        private Vector3 _velocity;
        private float _fallSpeed;

        private Renderer _swingRenderer;
        private Mesh _swingMesh;
        private IDisposable _swingSubscription;
        private float _swingRemaining;
        private bool _injected;

        /// <summary>Where the body is, for the snapshot to report back to core.</summary>
        public Vector3 Position => transform.position;

        /// <summary>
        /// The horizontal velocity last applied — what core asked for, not what the controller
        /// achieved.
        /// </summary>
        /// <remarks>
        /// Deliberately not <c>CharacterController.velocity</c>, which is the resolved
        /// displacement over the last frame divided by <c>dt</c>. That number collapses to zero
        /// the moment the player leans on a wall, and core reading it back as
        /// <c>PlayerVelocity</c> would see a player who had stopped trying to move rather than one
        /// who is blocked — a difference that matters the first time a mechanic asks "is the
        /// player moving?". Position is a sense because collision genuinely changes it; intended
        /// velocity is not collision's to edit.
        /// </remarks>
        public Vector3 Velocity => _velocity;

        /// <summary>
        /// Applies one tick's intent: move at this velocity, face this way.
        /// </summary>
        /// <param name="intent">What core decided this tick.</param>
        /// <param name="dt">
        /// The same step core integrated with — <c>snapshot.Dt</c>, clamped by
        /// <see cref="SnapshotBuilder.MaxDt"/>, never <c>Time.deltaTime</c>. Brain and body
        /// disagreeing about how much time passed is how a hitch becomes a teleport.
        /// </param>
        public void Apply(in PlayerMoveIntent intent, float dt)
        {
            Vector3 horizontal = intent.Velocity.ToUnity();

            _fallSpeed = _controller.isGrounded ? GroundedFallSpeed : _fallSpeed + (Gravity * dt);

            // One Move per frame, with both components together. Two calls would resolve collision
            // twice and let a horizontal push climb what a vertical one had just settled onto.
            _controller.Move((horizontal + new Vector3(0f, _fallSpeed, 0f)) * dt);

            _velocity = horizontal;

            // No smoothing: core already turned at 720°/s (CC §2.4), so easing here would be a
            // second rotation curve on top of the one the design specifies. Guarded because
            // LookRotation of a zero vector logs an error and leaves the rotation unchanged, and
            // core is free to send a facing it has not established yet.
            Vector3 facing = intent.Facing.ToUnity();

            if (facing.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }
        }

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        /// <remarks>
        /// The one thing this component listens to, and it is purely cosmetic: everything it
        /// <em>applies</em> still arrives in an intent. <c>PlayerAttacked</c> rather than the
        /// <c>ConeHitIntent</c> that follows it, because CC §4.2 puts the damage 40 % of the way
        /// through the swing — a wedge drawn on the damage frame would appear after the swing it is
        /// meant to be showing.
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _injected = true;

            _swingSubscription = hub.Subscribe<PlayerAttacked>(OnAttacked);
        }

        private void Awake()
        {
            // Cached once. Rule: never GetComponent in a per-frame path.
            _controller = GetComponent<CharacterController>();

            if (_swingCone == null)
            {
                return;
            }

            _swingRenderer = _swingCone.GetComponent<Renderer>();

            // Built here rather than authored, for the reason ReticleView builds its ring here: a
            // baked mesh in the prefab would quietly disagree with the two numbers above the moment
            // one of them was tuned. Held in a field as well as assigned, because a Mesh created in
            // code is not collected when the filter holding it is destroyed — it has to be
            // destroyed by whoever made it, which is this.
            _swingMesh = BuildWedge();
            _swingCone.sharedMesh = _swingMesh;

            SetSwingColour();

            // Hidden until the first swing. A wedge sitting under the player for the opening frame
            // of a run is a bug report waiting to be filed.
            _swingCone.gameObject.SetActive(false);
        }

        /// <exception cref="InvalidOperationException">A swing cone is dressed but nothing injected this body.</exception>
        /// <remarks>
        /// Only an error when there is something to draw, which is what makes the cone optional in
        /// the same way the reticle is: a scene may legitimately not dress one. Checked in
        /// <c>Start</c> rather than <c>Awake</c> for the reason <c>ReticleView</c> gives — injection
        /// happens during <c>RunScope</c>'s own <c>Awake</c>, and Unity gives no order between two
        /// of those.
        /// </remarks>
        private void Start()
        {
            if (_swingCone != null && !_injected)
            {
                throw new InvalidOperationException(
                    $"{nameof(PlayerView)} has a swing cone but was never injected, so no swing " +
                    "will ever be drawn. The body is registered by RunScope — drag this object " +
                    "onto its Player View field.");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribed explicitly rather than left to the hub's disposal, for the reason
            // ReticleView unsubscribes: a body destroyed mid-run would otherwise stay in the
            // subscriber list and be handed events for a component Unity has already killed.
            _swingSubscription?.Dispose();
            _swingSubscription = null;

            if (_swingMesh == null)
            {
                return;
            }

            // The play-mode split InputAdapter and EnemyViews both make: in edit mode Destroy
            // destroys nothing and logs an error rather than throwing, which would leak the mesh
            // and redden any test that tears one of these down (M0-14).
            if (Application.isPlaying)
            {
                Destroy(_swingMesh);
            }
            else
            {
                DestroyImmediate(_swingMesh);
            }

            _swingMesh = null;
        }

        private void Update()
        {
            if (_swingRemaining <= 0f)
            {
                return;
            }

            _swingRemaining -= Time.deltaTime;

            if (_swingRemaining <= 0f)
            {
                _swingRemaining = 0f;
                _swingCone.gameObject.SetActive(false);
            }
        }

        /// <remarks>
        /// The wedge is given the swing's own facing in world space rather than left to inherit the
        /// body's rotation, so what is drawn is the wedge core asked about rather than wherever the
        /// capsule has turned to since. It still rides along with the body for its tenth of a
        /// second, which at walking pace is under 5 cm.
        /// </remarks>
        private void OnAttacked(PlayerAttacked evt)
        {
            if (_swingCone == null)
            {
                return;
            }

            var facing = new Vector3(evt.FacingXZ.X, 0f, evt.FacingXZ.Y);

            // Guarded because LookRotation of a zero vector logs an error and changes nothing, and
            // a swing thrown before any facing is established is core's business, not a crash.
            if (facing.sqrMagnitude > 0f)
            {
                _swingCone.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }

            _swingRemaining = _swingSeconds;

            _swingCone.gameObject.SetActive(true);
        }

        /// <summary>
        /// A flat triangle fan: apex at the body, an arc of <see cref="SwingSegments"/> segments at
        /// <see cref="_swingRange"/>, spanning <see cref="_swingAngleDeg"/> around local forward.
        /// </summary>
        private Mesh BuildWedge()
        {
            var vertices = new Vector3[SwingSegments + 2];
            var triangles = new int[SwingSegments * 3];

            float half = _swingAngleDeg * 0.5f * Mathf.Deg2Rad;

            vertices[0] = Vector3.zero;

            for (int i = 0; i <= SwingSegments; i++)
            {
                float angle = Mathf.Lerp(-half, half, i / (float)SwingSegments);

                vertices[i + 1] = new Vector3(
                    Mathf.Sin(angle) * _swingRange,
                    0f,
                    Mathf.Cos(angle) * _swingRange);
            }

            // Wound so the face points up: for a triangle (v0, v1, v2) Unity's normal is
            // cross(v1 − v0, v2 − v0), and going from the −X edge of the arc to the +X edge gives
            // +Y. Wound the other way the wedge would be invisible from the only angle the game is
            // ever seen from.
            for (int i = 0; i < SwingSegments; i++)
            {
                triangles[i * 3] = 0;
                triangles[(i * 3) + 1] = i + 1;
                triangles[(i * 3) + 2] = i + 2;
            }

            var mesh = new Mesh { name = "SwingWedge" };

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <remarks>
        /// A property block rather than the material, because assigning to
        /// <c>Renderer.material</c> instantiates a copy — and this renderer shares its material
        /// with the reticle, which would then be drawing a different asset than it thought.
        /// </remarks>
        private void SetSwingColour()
        {
            if (_swingRenderer == null)
            {
                return;
            }

            var block = new MaterialPropertyBlock();

            _swingRenderer.GetPropertyBlock(block);
            block.SetColor(_baseColorId, _swingColour);
            _swingRenderer.SetPropertyBlock(block);
        }
    }
}
