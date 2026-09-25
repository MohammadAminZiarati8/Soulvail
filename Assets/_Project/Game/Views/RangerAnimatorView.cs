using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Ranger.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// Puts a bow on the fight core has already decided. Reads the body's velocity against its
    /// facing, listens for the three combat facts a bow has a pose for, and drives
    /// <c>AC_Ranger</c>. It decides nothing. RS-02a.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A run's events, with a bow's body.</b> <see cref="TargetChanged"/> raises or lowers the
    /// bow, <see cref="PlayerAttacked"/> — the start of the tell, CC §4.2 — draws, and the player's
    /// own <see cref="ProjectileFired"/> releases, so the string snaps on the frame the arrow
    /// leaves rather than on a guess at when it will. These are the facts core publishes for the
    /// Gravecaller today, so the view drops onto a run's player unchanged the day a class wears
    /// this body. In the sandbox the same facts come from <c>RangerSandboxLoop</c>, which is what
    /// makes the sandbox a test of this contract rather than of a second one.
    /// </para>
    /// <para>
    /// <b>The legs read the velocity in the body's own frame, not its speed.</b> Core turns the
    /// character to face its target while the stick moves it (<c>RunSession.TickBody</c>), so a
    /// Ranger kiting a pack runs sideways and backwards as often as forwards. A single speed blend
    /// would play a forward run under a body travelling backwards; the directional blend tree plays
    /// the strafe or the reversed run that matches the feet.
    /// </para>
    /// <para>
    /// <b>The arms are a second layer, masked to the spine and up.</b> CC §4.2 says attacking never
    /// slows or roots the player, so a shot must not stop the legs. Draw and release play over
    /// whatever the legs are doing.
    /// </para>
    /// <para>
    /// <b>This view keeps its own clock.</b> It is the sum of the steps it is given, not
    /// <c>Time.time</c>. That clock reads zero in an EditMode fixture until the Editor has been in
    /// front (the M6-11e rows of <c>PlayerAnimatorViewTests</c>), so a shot cadence measured on it
    /// cannot be tested.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(PlayerView))]
    public sealed class RangerAnimatorView : MonoBehaviour
    {
        /// <summary>
        /// How fast <c>Running_HoldingBow</c>'s grounded foot travels at body scale 1, in m/s:
        /// the speed at which the forward run, and the same clip reversed, stops sliding.
        /// </summary>
        /// <remarks>
        /// Measured at RS-02a by sampling the clip and taking the median speed of whichever foot
        /// is on the ground. It is a property of the clip and scales with the body, which is
        /// why <see cref="Blend"/> takes the body's scale.
        /// </remarks>
        public const float ForwardStrideSpeed = 3.94f;

        /// <summary>
        /// The same measurement for <c>Running_Strafe_Left</c> and <c>_Right</c>, whose feet
        /// travel at 3.10 and 3.06 m/s sideways. Their mean.
        /// </summary>
        public const float SideStrideSpeed = 3.08f;

        /// <summary>
        /// The seconds one shot is authored to take: <c>Ranged_Bow_Draw</c> to full draw (0.8 s)
        /// and <c>Ranged_Bow_Release</c> through its follow-through (0.4 s).
        /// </summary>
        /// <remarks>
        /// Paired with the gap between two <see cref="PlayerAttacked"/> events to give the shot's
        /// speed multiplier, for the reason <see cref="PlayerAnimatorView"/> scales its swing: a
        /// fire rate is a <c>Stat</c>, and a constant speed would drift apart from it.
        /// </remarks>
        public const float AuthoredShotSeconds = 1.2f;

        /// <summary>Ceiling on the shot's speed multiplier, so a very fast bow does not become a blur.</summary>
        public const float MaxShotSpeed = 4f;

        /// <summary>
        /// Where in <c>Ranged_Bow_Draw</c> the arrow is on the string, as normalised time: 0.5 s of
        /// 1.333 s. Before it the right hand is still reaching for the arrow, so the string stays
        /// at rest.
        /// </summary>
        public const float NockedNormalized = 0.375f;

        /// <summary>Where in <c>Ranged_Bow_Draw</c> the draw is full: 0.8 s of 1.333 s.</summary>
        public const float FullDrawNormalized = 0.6f;

        /// <summary>The upper-body layer's name in <c>AC_Ranger</c>.</summary>
        public const string UpperBodyLayer = "Upper Body";

        /// <summary>
        /// How fast the shown velocity may chase the body's real one, in m/s². The value and
        /// the reason are <see cref="PlayerAnimatorView"/>'s: it smooths the sample, not the
        /// movement.
        /// </summary>
        private const float VelocityFollowRate = 24f;

        /// <summary><c>bow_withString</c>'s one blend shape: the string pulled back, at weight 100.</summary>
        private const string DrawBlendShape = "Draw";

        private const float FullBlendShapeWeight = 100f;

        [Tooltip("The Animator on the Ranger model, playing AC_Ranger. Required: this component " +
                 "exists only to drive one, and a missing reference would fail silently as a body " +
                 "that never moves.")]
        [SerializeField] private Animator _animator;

        [Tooltip("The bow's SkinnedMeshRenderer (bow_withString). Optional: without it the string " +
                 "simply never draws.")]
        [SerializeField] private SkinnedMeshRenderer _bow;

        // Hashed once. Instance fields rather than statics, because nothing in this project holds
        // static state (AR §7).
        private readonly int _moveXId = Animator.StringToHash("MoveX");
        private readonly int _moveZId = Animator.StringToHash("MoveZ");
        private readonly int _moveSpeedId = Animator.StringToHash("MoveSpeed");
        private readonly int _aimingId = Animator.StringToHash("Aiming");
        private readonly int _shootId = Animator.StringToHash("Shoot");
        private readonly int _releaseId = Animator.StringToHash("Release");
        private readonly int _shotSpeedId = Animator.StringToHash("ShotSpeed");
        private readonly int _drawStateId = Animator.StringToHash("Draw");
        private readonly int _aimStateId = Animator.StringToHash("Aim");

        private PlayerView _body;

        private IDisposable _attackSubscription;
        private IDisposable _targetSubscription;
        private IDisposable _firedSubscription;

        private Vector3 _shownVelocity;
        private float _clock;
        private float _lastShotAt = -1f;
        private int _upperLayer = -2;
        private int _drawShape = -2;
        private bool _injected;

        /// <param name="hub">The scope's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _injected = true;

            _attackSubscription = hub.Subscribe<PlayerAttacked>(OnAttacked);
            _targetSubscription = hub.Subscribe<TargetChanged>(OnTargetChanged);
            _firedSubscription = hub.Subscribe<ProjectileFired>(OnFired);
        }

        /// <summary>
        /// The four blend-tree inputs for a velocity in the body's own frame.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The tree's clips sit on the unit circle — the forward run at (0, 1), the strafes at
        /// (±1, 0), the reversed run at (0, −1) — so each axis is divided by the speed its clip's
        /// feet travel at. A velocity inside the circle blends towards the idle at the centre.
        /// </para>
        /// <para>
        /// Outside it, the direction is kept on the circle and the extra goes into
        /// <paramref name="playback"/>, the locomotion state's speed multiplier. That is what keeps
        /// the feet planted at 3 m/s when the clips were authored for 2.6: the clip plays faster
        /// instead of the body sliding. Inside the circle the multiplier is 1.
        /// </para>
        /// </remarks>
        /// <param name="localVelocity">Metres per second, in the body's frame: +Z forward, +X right.</param>
        /// <param name="bodyScale">The model's world scale. Zero, negative or non-finite reads as standing still.</param>
        public static void Blend(
            Vector3 localVelocity,
            float bodyScale,
            out float moveX,
            out float moveZ,
            out float playback)
        {
            moveX = 0f;
            moveZ = 0f;
            playback = 1f;

            // Negated positives, so NaN is refused with zero (AR §18.3).
            if (!(bodyScale > 0f) || float.IsInfinity(bodyScale))
            {
                return;
            }

            float x = localVelocity.x / (SideStrideSpeed * bodyScale);
            float z = localVelocity.z / (ForwardStrideSpeed * bodyScale);
            float length = Mathf.Sqrt((x * x) + (z * z));

            if (!(length > 0f) || float.IsInfinity(length))
            {
                return;
            }

            if (length <= 1f)
            {
                moveX = x;
                moveZ = z;

                return;
            }

            moveX = x / length;
            moveZ = z / length;
            playback = length;
        }

        /// <summary>
        /// The shot's speed multiplier for a gap of <paramref name="interval"/> seconds between two
        /// shots: fast enough that the draw finishes before the next shot, and never slower than
        /// authored, because a bow with time to spare holds at full draw rather than drawing in
        /// slow motion.
        /// </summary>
        /// <returns>
        /// In <c>[1, <see cref="MaxShotSpeed"/>]</c>. A zero, negative or non-finite interval is
        /// the ceiling.
        /// </returns>
        public static float ShotSpeed(float interval)
        {
            if (!(interval > 0f) || float.IsInfinity(interval))
            {
                return MaxShotSpeed;
            }

            return Mathf.Clamp(AuthoredShotSeconds / interval, 1f, MaxShotSpeed);
        }

        /// <summary>
        /// How far the bowstring is pulled, as a fraction, from the upper-body layer's state:
        /// rest until the arrow is nocked, pulled with the hand until full draw, held while aiming,
        /// and released the moment the release begins.
        /// </summary>
        /// <param name="inDraw">The layer is in its <c>Draw</c> state.</param>
        /// <param name="inAim">The layer is in its <c>Aim</c> state, holding full draw.</param>
        /// <param name="normalizedTime">The draw's normalised time, when <paramref name="inDraw"/>.</param>
        public static float StringPull(bool inDraw, bool inAim, float normalizedTime)
        {
            if (inAim)
            {
                return 1f;
            }

            if (!inDraw)
            {
                return 0f;
            }

            return Mathf.InverseLerp(NockedNormalized, FullDrawNormalized, normalizedTime);
        }

        /// <summary>
        /// Advances the view by <paramref name="dt"/> seconds: the locomotion blend and the
        /// bowstring. Called from <c>Update</c> with <c>Time.deltaTime</c>.
        /// </summary>
        /// <remarks>
        /// Public so an EditMode fixture can advance the view's clock, which <c>Update</c> never
        /// does there. A zero, negative or non-finite step does nothing.
        /// </remarks>
        public void Step(float dt)
        {
            if (_animator == null || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _clock += dt;

            // Resolved lazily as well as in Awake: an EditMode fixture never gets an Awake.
            if (_body == null)
            {
                _body = GetComponent<PlayerView>();
            }

            // PlayerView.Velocity is what core asked for, not what the controller achieved — the
            // distinction PlayerAnimatorView draws. A Ranger leaning on a wall is still running.
            // InverseTransformDirection ignores scale, so this is metres per second in the body's
            // frame.
            Vector3 target = _body == null ? Vector3.zero : transform.InverseTransformDirection(_body.Velocity);

            target.y = 0f;

            _shownVelocity = Vector3.MoveTowards(_shownVelocity, target, VelocityFollowRate * dt);

            Blend(_shownVelocity, _animator.transform.lossyScale.y, out float moveX, out float moveZ, out float playback);

            _animator.SetFloat(_moveXId, moveX);
            _animator.SetFloat(_moveZId, moveZ);
            _animator.SetFloat(_moveSpeedId, playback);

            PullString();
        }

        private void Awake()
        {
            // Cached once. Rule: never GetComponent in a per-frame path.
            _body = GetComponent<PlayerView>();
        }

        /// <exception cref="InvalidOperationException">No Animator is dressed, or nothing injected this component.</exception>
        /// <remarks>
        /// Checked in <c>Start</c> rather than <c>Awake</c> for <see cref="PlayerAnimatorView"/>'s
        /// reason: injection happens during the scope's own <c>Awake</c>, and Unity gives no order
        /// between two of those.
        /// </remarks>
        private void Start()
        {
            if (_animator == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RangerAnimatorView)} has no {nameof(Animator)} assigned, so the " +
                    "Ranger would stand still whatever happened. Drag the model's Animator onto " +
                    "this component.");
            }

            if (!_injected)
            {
                throw new InvalidOperationException(
                    $"{nameof(RangerAnimatorView)} was never injected, so no shot will ever reach " +
                    "it. Drag this object onto the scope's Animator View field.");
            }
        }

        private void OnDestroy()
        {
            // Unsubscribed explicitly, for PlayerAnimatorView's reason: a body destroyed while its
            // scope lives would otherwise be handed events for a component Unity has killed.
            _attackSubscription?.Dispose();
            _targetSubscription?.Dispose();
            _firedSubscription?.Dispose();

            _attackSubscription = null;
            _targetSubscription = null;
            _firedSubscription = null;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        /// <remarks>
        /// <para>
        /// The multiplier is measured from the gap between this shot and the last, so it follows
        /// whatever the fire rate currently is without this view knowing a weapon exists. The first
        /// shot has nothing to measure against and keeps the controller's value.
        /// </para>
        /// <para>
        /// <b>A bow already drawing, or already drawn, is not told to draw again.</b> Acquiring a
        /// target raises the bow before the first shot is in range, so the shot that follows finds
        /// it at full draw, and its release is the next thing to happen. A trigger set in a state
        /// with no transition for it would stay set and fire a draw later, at a moment nobody chose.
        /// </para>
        /// </remarks>
        private void OnAttacked(PlayerAttacked evt)
        {
            if (_animator == null)
            {
                return;
            }

            if (_lastShotAt >= 0f)
            {
                _animator.SetFloat(_shotSpeedId, ShotSpeed(_clock - _lastShotAt));
            }

            _lastShotAt = _clock;

            if (IsDrawingOrDrawn())
            {
                return;
            }

            _animator.SetTrigger(_shootId);
        }

        /// <remarks>
        /// Only the player's own shots: <see cref="ProjectileFired.SourceId"/> is the enemy that
        /// fired, or 0 for nobody, and a Spitter's bolt is not this bow's release. A draw still
        /// pending is dropped with the release, so it cannot fire a second draw after the arrow has
        /// gone.
        /// </remarks>
        private void OnFired(ProjectileFired evt)
        {
            if (_animator == null || evt.SourceId != 0)
            {
                return;
            }

            _animator.ResetTrigger(_shootId);
            _animator.SetTrigger(_releaseId);
        }

        /// <remarks>
        /// A blocked target still raises the bow. CC §3.6 holds the facing on something the
        /// character cannot hurt, and a Ranger looking down a drawn arrow at it is the right pose
        /// for "go around". The weapon, not this view, is what declines to shoot.
        /// </remarks>
        private void OnTargetChanged(TargetChanged evt)
        {
            if (_animator == null)
            {
                return;
            }

            _animator.SetBool(_aimingId, evt.Id >= 0);
        }

        private void PullString()
        {
            if (_bow == null || _bow.sharedMesh == null)
            {
                return;
            }

            // Looked up once, lazily: -2 means not yet asked and -1 means absent.
            if (_drawShape == -2)
            {
                _drawShape = _bow.sharedMesh.GetBlendShapeIndex(DrawBlendShape);
            }

            if (_upperLayer == -2)
            {
                _upperLayer = _animator.GetLayerIndex(UpperBodyLayer);
            }

            if (_drawShape < 0 || _upperLayer < 0 || !_animator.isInitialized)
            {
                return;
            }

            float pull = PullOf(_animator.GetCurrentAnimatorStateInfo(_upperLayer));

            // Across a crossfade the string follows the arms: eased out as the bow is lowered, and
            // gone within the release's 0.05 s crossfade, which reads as the snap it is.
            if (_animator.IsInTransition(_upperLayer))
            {
                pull = Mathf.Lerp(
                    pull,
                    PullOf(_animator.GetNextAnimatorStateInfo(_upperLayer)),
                    _animator.GetAnimatorTransitionInfo(_upperLayer).normalizedTime);
            }

            _bow.SetBlendShapeWeight(_drawShape, pull * FullBlendShapeWeight);
        }

        private float PullOf(AnimatorStateInfo state)
        {
            return StringPull(
                state.shortNameHash == _drawStateId,
                state.shortNameHash == _aimStateId,
                state.normalizedTime);
        }

        /// <summary>The upper-body layer is in, or crossfading into, <c>Draw</c> or <c>Aim</c>.</summary>
        private bool IsDrawingOrDrawn()
        {
            if (_upperLayer == -2)
            {
                _upperLayer = _animator.GetLayerIndex(UpperBodyLayer);
            }

            if (_upperLayer < 0 || !_animator.isInitialized)
            {
                return false;
            }

            if (IsDrawOrAim(_animator.GetCurrentAnimatorStateInfo(_upperLayer)))
            {
                return true;
            }

            return _animator.IsInTransition(_upperLayer)
                && IsDrawOrAim(_animator.GetNextAnimatorStateInfo(_upperLayer));
        }

        private bool IsDrawOrAim(AnimatorStateInfo state)
        {
            return state.shortNameHash == _drawStateId || state.shortNameHash == _aimStateId;
        }
    }
}
