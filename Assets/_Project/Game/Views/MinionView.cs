using System;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Wight.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One Wight's body. Core decided that a minion exists and where it stood up; this is the
    /// object standing there, and it decides nothing at all. See AR §3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="EnemyView"/>'s shape, and deliberately not <see cref="EnemyView"/></b>
    /// (M5-05a rule 1). <c>EnemyViews</c> keys <c>_idByColliderInstance</c> by
    /// <c>Collider.GetInstanceID()</c>, and that index is the whole of what
    /// <c>ConeOverlapQuery.Query</c> turns a physics hit into an enemy id with. A Wight rented from
    /// the enemy pool would be in it, so the player's own swing would report <em>its</em> id to
    /// <c>RunSession.ReportConeHits</c> and core would apply the swing's damage to whichever
    /// <em>enemy</em> holds that number — not to the Wight, which is not in <c>EnemyRegistry</c> at
    /// all (M5-04a rule 1), but to a stranger. It would be silent, wrong by an amount nobody could
    /// predict, and reachable on the first swing of the first Gravecaller run. The cost of a
    /// separate view is this file and a second pool; the cost of reuse is the targeting system
    /// quietly lying.
    /// </para>
    /// <para>
    /// <b>Two colliders, each answering a different question</b> — <see cref="EnemyView"/>'s split
    /// for its reasons. The <see cref="CharacterController"/> is what moves, so a Wight bumps into
    /// walls, pillars and the bodies it is walking at instead of through them; the trigger
    /// <see cref="CapsuleCollider"/> is the shape a query can ask about, and it reaches the physics
    /// scene only through <c>RunTicker</c>'s single <c>Physics.SyncTransforms()</c>. Nothing sweeps
    /// a Wight in M5 (rule 1's other half, and M5-04a rule 9) — the capsule sits on the Default
    /// layer, which no mask in the game selects — so what it buys today is that <em>"the bodies
    /// moved before the flush"</em> is a claim a test can put a question to.
    /// </para>
    /// <para>
    /// <b>No knockback, no health bar, no hit flash, and each absence is a decision.</b> A shove is
    /// an <c>EnemyKnockbackIntent</c> and core writes none for a Wight; a bar is a readout for a
    /// number that never moves, because nothing in M5 damages one; and a dissolve is M7's art pass.
    /// A Wight slides, as every enemy did until M2-06.
    /// </para>
    /// <para>
    /// <b>An unbound view is not a live Wight.</b> <see cref="MinionViews"/> rents one from a
    /// <see cref="ViewPool{T}"/> per <c>MinionSpawned</c> and returns it on <c>MinionDespawned</c>
    /// or <c>MinionDied</c>, so the same object stands in for one Wight, then another, with a
    /// different id. <see cref="Bind"/> and <see cref="Unbind"/> are that seam.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(CharacterController))]
    public sealed class MinionView : MonoBehaviour, IPoolable
    {
        /// <summary>
        /// The id of a view that is not standing in for anything. Zero, because
        /// <c>MinionSystem</c> hands out ids from 1 and never reuses one within a stage, so no
        /// live Wight can ever collide with it.
        /// </summary>
        public const int Unbound = 0;

        /// <summary>
        /// Downward acceleration while airborne, in m/s². <see cref="EnemyView"/>'s number and its
        /// reason: it is not a gameplay decision, only what keeps a
        /// <see cref="CharacterController"/> in contact with the floor. Nothing in core can observe
        /// it — the position comes back through the snapshot either way.
        /// </summary>
        private const float Gravity = -20f;

        /// <summary>
        /// The downward speed held while grounded, matching <see cref="EnemyView"/>: a controller
        /// pushed with no vertical component drifts off the ground for a frame at a time and
        /// <c>isGrounded</c> flickers.
        /// </summary>
        private const float GroundedFallSpeed = -1f;

        [Tooltip("The body's renderer — the Mesh child. Assigned rather than searched for, for " +
                 "EnemyHitFeedback's reason: a GetComponentInChildren per instance is a cost for a " +
                 "reference the prefab already knows. Optional — a body with no renderer is " +
                 "invisible rather than broken, which is what an EditMode fixture builds.")]
        [SerializeField] private Renderer _renderer;

        [Tooltip("What the body's _BaseColor is driven to. GD §16.4's player cyan, because CH " +
                 "§3.2 asks for a Wight that reads as yours; the separator from the player " +
                 "themself is size, not hue (M5-05a rule 8).")]
        [SerializeField] private Color _tint = Palette.Player;

        /// <summary>
        /// URP's base-colour property, resolved once. An instance field rather than a static, for
        /// the reason this project bans mutable statics outright and <c>EnemyHitFeedback</c> holds
        /// the same id the same way.
        /// </summary>
        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private CapsuleCollider _body;
        private CharacterController _controller;
        private MaterialPropertyBlock _block;
        private int _id = Unbound;
        private Vector3 _velocity;
        private float _fallSpeed;

        /// <summary>The core-side id this body stands for, or <see cref="Unbound"/>.</summary>
        public int Id => _id;

        /// <summary>Whether this view is currently standing in for a live Wight.</summary>
        public bool IsBound => _id != Unbound;

        /// <summary>Where the body is, for the snapshot to report back to core.</summary>
        public Vector3 Position => transform.position;

        /// <summary>The horizontal velocity last applied — what core asked for, not what collision achieved.</summary>
        /// <remarks>
        /// <see cref="EnemyView.Velocity"/>'s contract exactly, and for its reason: a zero that is
        /// written every frame is a fact, while a field nobody fills is a slot that keeps whatever
        /// the previous occupant left in it (AR §18.2).
        /// </remarks>
        public Vector3 Velocity => _velocity;

        /// <summary>What this body is drawn as. Authored, and read by nothing at runtime.</summary>
        /// <remarks>
        /// Exposed so <c>MinionViewsTests</c> can pin the shipped prefab's colour against
        /// <see cref="Palette.Player"/> — GD §16.4 reserves that cyan for the player and CH §3.2
        /// asks for a Wight that is unmistakably theirs, so a recolour has to be a deliberate edit
        /// rather than a drift.
        /// </remarks>
        public Color Tint => _tint;

        /// <summary>
        /// This body's trigger collider, cached once.
        /// </summary>
        /// <remarks>
        /// <see cref="EnemyView.Body"/>'s shape and its lazy resolution, for its reason: outside
        /// play mode <c>Awake</c> never runs on an instantiated prefab, and an EditMode test that
        /// builds one must still get a whole object rather than a half-null one. At runtime
        /// <c>Awake</c> has always already filled it.
        /// </remarks>
        public Collider Body
        {
            get
            {
                // Unity's == rather than `is null`: a destroyed collider is a live reference that
                // only compares equal to null through the engine's operator.
                if (_body == null)
                {
                    _body = GetComponent<CapsuleCollider>();
                }

                return _body;
            }
        }

        /// <remarks>
        /// <see cref="Body"/>'s lazy resolution and its reason. Private, because nothing outside
        /// this class has any business moving the body.
        /// </remarks>
        private CharacterController Controller
        {
            get
            {
                if (_controller == null)
                {
                    _controller = GetComponent<CharacterController>();
                }

                return _controller;
            }
        }

        /// <summary>
        /// Applies one tick's intent: walk at this velocity, look this way.
        /// </summary>
        /// <param name="intent">What core decided for this Wight this tick.</param>
        /// <param name="dt">
        /// The same step core integrated with — <c>snapshot.Dt</c>, clamped by
        /// <c>SnapshotBuilder.MaxDt</c>, never <c>Time.deltaTime</c>. <see cref="EnemyView.Apply"/>'s
        /// rule: brain and body disagreeing about how much time passed is how a hitch becomes a
        /// teleport.
        /// </param>
        /// <remarks>
        /// <para>
        /// The id on the intent is deliberately not checked against <see cref="Id"/>. Routing an
        /// intent to the right body is <c>RunTicker</c>'s job and it does it by looking this object
        /// up by that same id, so a check here could only ever be true.
        /// </para>
        /// <para>
        /// <b>A non-finite or negative step moves nothing</b>, where <see cref="EnemyView"/> has no
        /// such door. The difference is who writes the number: an enemy's step is the snapshot's and
        /// always clamped, while a minion's arrives through the same clamp but the body is new and
        /// the guard is a line. A NaN through <see cref="CharacterController.Move"/> is a body at a
        /// position no later distance test can be true about, for the rest of the run, in silence.
        /// </para>
        /// </remarks>
        public void Apply(in EnemyMoveIntent intent, float dt)
        {
            // Negated positives: every comparison against NaN is false, so the natural spelling
            // would let an unreadable step through.
            if (!(dt >= 0f) || float.IsInfinity(dt))
            {
                return;
            }

            Vector3 horizontal = intent.Velocity.ToUnity();

            MoveBody(horizontal, dt);

            _velocity = horizontal;

            Face(intent.FacingXZ);
        }

        /// <summary>
        /// Puts this body into service as <paramref name="id"/>, standing at
        /// <paramref name="position"/>.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="id"/> is not positive. <c>MinionSystem</c> issues ids from 1, so a zero
        /// or negative one means the caller invented it — and a view bound to <see cref="Unbound"/>
        /// would report itself into the snapshot under an id core can never resolve, which core
        /// ignores in silence.
        /// </exception>
        public void Bind(int id, Vector3 position)
        {
            if (id <= Unbound)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(id),
                    id,
                    "A minion id must be positive; MinionSystem issues them from 1.");
            }

            _id = id;
            _velocity = Vector3.zero;

            // Back to rest, so a body pooled while it was falling does not arrive at its next
            // rental still carrying the previous life's downward speed.
            _fallSpeed = 0f;

            transform.position = position;
        }

        /// <summary>Takes this body out of service. Idempotent.</summary>
        public void Unbind()
        {
            _id = Unbound;
            _velocity = Vector3.zero;
            _fallSpeed = 0f;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing to do, and deliberately so: <see cref="MinionViews"/> calls <see cref="Bind"/> on
        /// the very next line with the id and the position this body is being put into service
        /// with, and the pool has neither to give. Everything a rental needs undone was undone on
        /// the way out — see <see cref="OnDespawn"/>.
        /// </remarks>
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        /// <remarks>
        /// <b>Short, and the shortness is the point.</b> <c>EnemyView.OnDespawn</c> is the most
        /// dangerous method in that file because a life leaves a dissolve, a disabled collider, an
        /// Elite flag and a shove behind it; a Wight leaves an id, a velocity and a fall speed,
        /// because everything that would remember more was ruled out of M5. Anything added to
        /// <c>Wight.prefab</c> that remembers something joins the list here (AR §18.4).
        /// </remarks>
        public void OnDespawn()
        {
            Unbind();
        }

        private void Awake()
        {
            // Cached once. Rule: never GetComponent in a per-frame path.
            _body = GetComponent<CapsuleCollider>();
            _controller = GetComponent<CharacterController>();

            ApplyTint();
        }

        /// <summary>
        /// Drives the body's base colour to <see cref="_tint"/>, once, for the life of the instance.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Through a <see cref="MaterialPropertyBlock"/> rather than by assigning a material, which
        /// is <c>EnemyLook</c>'s bargain and GD §11.3's own plan: every body in the arena keeps one
        /// shared material and one draw call, and eight Wights cost no batch of their own.
        /// </para>
        /// <para>
        /// In <c>Awake</c> rather than on the rental, unlike <c>EnemyViews</c> repainting an
        /// archetype's look every time a body is handed out. There is one kind of minion and its
        /// colour never changes, and nothing else on this prefab writes <c>_BaseColor</c> — so
        /// there is no second writer for a rental to undo (AR §18.4's pooled-reset invariant, read
        /// from the side where it has nothing to say).
        /// </para>
        /// </remarks>
        private void ApplyTint()
        {
            // Unity's ==: an unassigned renderer is a real answer, not a mis-wiring. A fixture
            // builds a body with no mesh at all.
            if (_renderer == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();

            _renderer.GetPropertyBlock(_block);
            _block.SetColor(_baseColorId, _tint);
            _renderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// Moves the body by <paramref name="horizontal"/> for <paramref name="dt"/> seconds, with
        /// gravity folded in. The one place this object touches the controller.
        /// </summary>
        /// <remarks>
        /// One <c>Move</c> per frame with both components together, exactly as
        /// <see cref="EnemyView"/> does it: two calls would resolve collision twice and let a
        /// horizontal push climb what a vertical one had just settled onto.
        /// </remarks>
        private void MoveBody(Vector3 horizontal, float dt)
        {
            CharacterController controller = Controller;

            _fallSpeed = controller.isGrounded ? GroundedFallSpeed : _fallSpeed + (Gravity * dt);

            controller.Move((horizontal + new Vector3(0f, _fallSpeed, 0f)) * dt);
        }

        /// <summary>
        /// Turns the body to look along <paramref name="facingXZ"/>, or leaves it alone when core
        /// has no opinion.
        /// </summary>
        /// <remarks>
        /// A zero facing is core saying "keep the rotation you have" — a Wight with no quarry, or
        /// one standing exactly on top of it — and is the normal reason this does nothing
        /// (<c>MinionSystem.Walk</c> writes a zero facing in both cases). It is also what stops
        /// <c>LookRotation</c> logging an error over a zero vector.
        /// </remarks>
        private void Face(System.Numerics.Vector2 facingXZ)
        {
            var facing = new Vector3(facingXZ.X, 0f, facingXZ.Y);

            if (facing.sqrMagnitude > 0f)
            {
                transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
            }
        }
    }
}
