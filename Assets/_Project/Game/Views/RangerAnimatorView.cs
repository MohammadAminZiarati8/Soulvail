using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Bodies/Ranger.prefab's reference to this
// component would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// Puts a bow on the fight core has already decided. Reads the body's velocity against its
    /// facing, listens for the combat facts a bow has a pose for, and drives <c>AC_Ranger</c>. It
    /// decides nothing. RS-02a, RS-03d.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A run's events, with a bow's body.</b> <see cref="TargetChanged"/> and
    /// <see cref="HoldFireChanged"/> raise or lower the bow, <see cref="PlayerAttacked"/> — the
    /// start of the tell, CC §4.2 — draws, and the player's own <see cref="ProjectileFired"/>
    /// releases, so the string snaps on the frame the arrow leaves rather than on a guess at when
    /// it will. <see cref="VolleyReady"/> lights the bow, <see cref="ChargeStarted"/> and
    /// <see cref="ChargeEnded"/> bracket the roll, <see cref="PlayerDamaged"/> flinches, and
    /// <see cref="PlayerDied"/> puts it all down.
    /// These are the facts core publishes for the Ranger. In the sandbox the same facts come from
    /// <c>RangerSandboxLoop</c>, which is what makes the sandbox a test of this contract rather than
    /// of a second one.
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
    /// <b>The draw is timed by core.</b> Since RS-08a each <see cref="PlayerAttacked"/> carries the
    /// seconds to its arrow's release, and the shot's speed fits full draw into them. The view no
    /// longer measures the gap between shots, so a first shot after a pause draws as fast as the
    /// arrow leaves.
    /// </para>
    /// <para>
    /// <b>It lives on its body, <c>Bodies/Ranger</c>, and reads the <see cref="PlayerView"/> above
    /// it</b> (RS-02b rule 5) — nested in <c>Player_Ranger</c> for the sandbox, and raised under the
    /// player by a run once a class names it. A body with none above it is refused in
    /// <c>Start</c>.
    /// </para>
    /// </remarks>
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
        /// Paired with <see cref="PlayerAttacked.DrawSeconds"/> to give the shot's speed
        /// multiplier, for the reason <see cref="PlayerAnimatorView"/> scales its swing: a fire
        /// rate is a <c>Stat</c>, and a constant speed would drift apart from it.
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

        /// <summary>How long the snare's crouch-and-place holds the upper body.</summary>
        public const float PlaceSeconds = 0.4f;

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

        [Tooltip("Shown while the next shot is a volley, in the player's cyan (GD §16.4). " +
                 "Optional: without it a volley is told by its fan alone.")]
        [SerializeField] private GameObject _volleyGlow;

        [SerializeField] private RainOfArrowsView _rainPrefab;
        private RainOfArrowsView _rainView;
        private GameObject _rainRoot;
        private IDisposable _rainSubscription;
        private readonly int _castUpId = Animator.StringToHash("CastUp");
        private float _castUpLeft;
        private float _bindingReleaseLeft;
        private IDisposable _bindingCastSubscription;
        [SerializeField] private TrapViews _snarePrefab;
        private TrapViews _snareView;
        private GameObject _snareRoot;
        private IDisposable _placedSubscription;
        private readonly int _placeId = Animator.StringToHash("Place");
        [SerializeField] private KillshotView _killshotPrefab;
        private KillshotView _killshotView;
        private GameObject _killshotRoot;
        private IDisposable _killshotWind, _killshotFire, _killshotBoundary, _killshotArrived, _killshotEnded;
        private IDisposable _killshotStageCleared;
        private const float ImmunePopSeconds = 0.5f;
        private TextMeshPro _immunePop;
        private Camera _immuneCamera;
        private float _immuneLeft;
        private bool _killshotWinding;
        private Vector3 _killshotDirection;

        // Hashed once. Instance fields rather than statics, because nothing in this project holds
        // static state (AR §7).
        private readonly int _moveXId = Animator.StringToHash("MoveX");
        private readonly int _moveZId = Animator.StringToHash("MoveZ");
        private readonly int _moveSpeedId = Animator.StringToHash("MoveSpeed");
        private readonly int _aimingId = Animator.StringToHash("Aiming");
        private readonly int _shootId = Animator.StringToHash("Shoot");
        private readonly int _releaseId = Animator.StringToHash("Release");
        private readonly int _shotSpeedId = Animator.StringToHash("ShotSpeed");
        private readonly int _dodgingId = Animator.StringToHash("Dodging");
        private readonly int _dodgeXId = Animator.StringToHash("DodgeX");
        private readonly int _dodgeZId = Animator.StringToHash("DodgeZ");
        private readonly int _hitId = Animator.StringToHash("Hit");
        private readonly int _drawStateId = Animator.StringToHash("Draw");
        private readonly int _aimStateId = Animator.StringToHash("Aim");
        private readonly int _hitStateId = Animator.StringToHash("Hit");

        private PlayerView _body;

        private IDisposable _attackSubscription;
        private IDisposable _targetSubscription;
        private IDisposable _firedSubscription;
        private IDisposable _holdSubscription;
        private IDisposable _volleySubscription;
        private IDisposable _rollSubscription;
        private IDisposable _rollEndedSubscription;
        private IDisposable _diedSubscription;
        private IDisposable _damagedSubscription;

        private Vector3 _shownVelocity;
        private int _upperLayer = -2;
        private int _flinchLayer = -2;
        private int _drawShape = -2;
        private int _targetId = -1;
        private bool _holding;
        private bool _dead;
        private bool _rolling;
        private bool _injected;

        /// <param name="hub">The scope's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        [Inject]
        public void Construct(DomainEventHub hub, IRunSession session = null, EnemyViews enemies = null,
            ILocalizer localizer = null)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _injected = true;

            _attackSubscription = hub.Subscribe<PlayerAttacked>(OnAttacked);
            _targetSubscription = hub.Subscribe<TargetChanged>(OnTargetChanged);
            _firedSubscription = hub.Subscribe<ProjectileFired>(OnFired);
            _holdSubscription = hub.Subscribe<HoldFireChanged>(OnHoldFireChanged);
            _volleySubscription = hub.Subscribe<VolleyReady>(OnVolleyReady);
            _rollSubscription = hub.Subscribe<ChargeStarted>(OnRollStarted);
            _rollEndedSubscription = hub.Subscribe<ChargeEnded>(OnRollEnded);
            _diedSubscription = hub.Subscribe<PlayerDied>(OnDied);
            _damagedSubscription = hub.Subscribe<PlayerDamaged>(OnDamaged);
            _rainSubscription = hub.Subscribe<RainOfArrowsCast>(OnRainCast);
            _bindingCastSubscription = hub.Subscribe<SkillCast>(OnBindingCast);
            if (_rainPrefab != null)
            {
                // A scene-root pool: neither the ring nor falling arrows follow the moving body.
                _rainRoot = new GameObject("Rain of Arrows (pooled)");
                _rainRoot.SetActive(false);
                _rainView = Instantiate(_rainPrefab, _rainRoot.transform, false);
                _rainView.Construct(hub, session, _bow != null ? _bow.transform : transform);
                _rainRoot.SetActive(true);
            }
            _placedSubscription = hub.Subscribe<TrapPlaced>(OnTrapPlaced);
            _killshotWind = hub.Subscribe<KillshotWindUp>(OnKillshotWindUp);
            _killshotFire = hub.Subscribe<KillshotFired>(_ => EndKillshot(true));
            _killshotBoundary = hub.Subscribe<StageTransitionStarted>(_ => CancelKillshot());
            _killshotArrived = hub.Subscribe<StageArrived>(_ => CancelKillshot());
            _killshotEnded = hub.Subscribe<RunEnded>(_ => CancelKillshot());
            _killshotStageCleared = hub.Subscribe<StageCleared>(_ => CancelKillshot());
            if (localizer is not null)
            {
                var pop = new GameObject("Killshot Immune");
                pop.SetActive(false);
                pop.transform.SetParent(transform, false);
                _immunePop = pop.AddComponent<TextMeshPro>();
                _immunePop.text = localizer.Get(new LocKey("ui.combat.immune"));
                _immunePop.fontSize = 3.2f;
                _immunePop.alignment = TextAlignmentOptions.Center;
                _immunePop.color = Palette.Wind;
                _immuneCamera = Camera.main;
            }
            if (_killshotPrefab != null)
            {
                _killshotRoot = new GameObject("Killshot (pooled)");
                _killshotRoot.SetActive(false);
                _killshotView = Instantiate(_killshotPrefab, _killshotRoot.transform, false);
                Camera camera = Camera.main;
                _killshotView.Construct(hub, session, _bow != null ? _bow.transform : transform,
                    camera != null ? camera.GetComponent<CameraShake>() : null);
                _killshotRoot.SetActive(true);
            }
            if (_snarePrefab != null)
            {
                // Scene-root as well: a placed snare stays where it was dropped.
                _snareRoot = new GameObject("Hunter's Snares (pooled)");
                _snareRoot.SetActive(false);
                _snareView = Instantiate(_snarePrefab, _snareRoot.transform, false);
                _snareView.Construct(hub, session, enemies);
                _snareRoot.SetActive(true);
            }
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
        /// The shot's speed multiplier for an arrow that leaves <paramref name="drawSeconds"/> after
        /// the shot starts: fast enough that full draw lands on the release, and never slower than
        /// authored, because a bow with time to spare holds at full draw rather than drawing in
        /// slow motion. RS-08a rule 5.
        /// </summary>
        /// <returns>
        /// <c><see cref="FullDrawNormalized"/> × <see cref="AuthoredShotSeconds"/> / drawSeconds</c>,
        /// in <c>[1, <see cref="MaxShotSpeed"/>]</c>. A zero, negative or non-finite draw time is
        /// the ceiling.
        /// </returns>
        public static float ShotSpeed(float drawSeconds)
        {
            if (!(drawSeconds > 0f) || float.IsInfinity(drawSeconds))
            {
                return MaxShotSpeed;
            }

            return Mathf.Clamp(FullDrawNormalized * AuthoredShotSeconds / drawSeconds, 1f, MaxShotSpeed);
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
        /// Advances the view by <paramref name="dt"/> seconds: the locomotion blend, the bowstring
        /// and the flinch's weight. Called from <c>Update</c> with <c>Time.deltaTime</c>.
        /// </summary>
        /// <remarks>
        /// Public so an EditMode fixture can advance the view, which <c>Update</c> never
        /// does there. A zero, negative or non-finite step does nothing.
        /// </remarks>
        public void Step(float dt)
        {
            if (_animator == null || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }
            if (_immuneLeft > 0f)
            {
                _immuneLeft = Mathf.Max(0f, _immuneLeft - dt);
                _immunePop.alpha = _immuneLeft / ImmunePopSeconds;
                _immunePop.gameObject.SetActive(_immuneLeft > 0f);
            }
            _castUpLeft = Mathf.Max(0f, _castUpLeft - dt);
            if (_bindingReleaseLeft > 0f)
            {
                _bindingReleaseLeft = Mathf.Max(0f, _bindingReleaseLeft - dt);
                if (_bindingReleaseLeft == 0f && !_dead)
                {
                    _animator.SetTrigger(_releaseId);
                }
            }

            // Resolved lazily as well as in Awake: an EditMode fixture never gets an Awake. Only
            // while there is none, so a body that has one never looks again.
            if (_body == null)
            {
                _body = GetComponentInParent<PlayerView>();
            }

            // PlayerView.Velocity is what core asked for, not what the controller achieved — the
            // distinction PlayerAnimatorView draws. A Ranger leaning on a wall is still running.
            // InverseTransformDirection ignores scale, so this is metres per second in the body's
            // frame — this transform's, which stands at identity under the player's.
            Vector3 target = _body == null ? Vector3.zero : transform.InverseTransformDirection(_body.Velocity);

            target.y = 0f;

            _shownVelocity = Vector3.MoveTowards(_shownVelocity, target, VelocityFollowRate * dt);

            Blend(_shownVelocity, _animator.transform.lossyScale.y, out float moveX, out float moveZ, out float playback);

            _animator.SetFloat(_moveXId, moveX);
            _animator.SetFloat(_moveZId, moveZ);
            _animator.SetFloat(_moveSpeedId, playback);

            PullString();
            WeighFlinch();
        }

        private void Awake()
        {
            // Cached once. Rule: never GetComponent in a per-frame path. In the parents, because
            // this view lives on the body, under the player (RS-02b rule 5).
            _body = GetComponentInParent<PlayerView>();
        }

        /// <exception cref="InvalidOperationException">
        /// No Animator is dressed, no <see cref="PlayerView"/> stands above this body, or nothing
        /// injected this component.
        /// </exception>
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

            if (_body == null)
            {
                _body = GetComponentInParent<PlayerView>();
            }

            if (_body == null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RangerAnimatorView)} on '{name}' has no {nameof(PlayerView)} above " +
                    "it, so its legs have no velocity to read. A body is raised under the player " +
                    "by RunScope, or nested under a PlayerView in a prefab like Player_Ranger.");
            }

            if (!_injected)
            {
                throw new InvalidOperationException(
                    $"{nameof(RangerAnimatorView)} on '{name}' was never injected, so no shot will " +
                    "ever reach it. RunScope injects the body it raises, and the sandbox's scope the " +
                    "view on its Animator View field.");
            }

            // Dark until core says a volley is ready. The prefab stores it inactive too; this is
            // for a scene that saved it lit.
            if (_volleyGlow != null)
            {
                _volleyGlow.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            _killshotWind?.Dispose();
            _killshotFire?.Dispose();
            _killshotBoundary?.Dispose();
            _killshotArrived?.Dispose();
            _killshotEnded?.Dispose();
            _killshotStageCleared?.Dispose();
            if (_killshotRoot != null)
            {
                _killshotView.Dispose();
                if (Application.isPlaying)
                {
                    Destroy(_killshotRoot);
                }
                else
                {
                    DestroyImmediate(_killshotRoot);
                }
            }
            _bindingCastSubscription?.Dispose();
            _placedSubscription?.Dispose();
            if (_snareRoot != null)
            {
                _snareView.Dispose();
                if (Application.isPlaying)
                {
                    Destroy(_snareRoot);
                }
                else
                {
                    DestroyImmediate(_snareRoot);
                }
            }
            _rainSubscription?.Dispose();
            if (_rainRoot != null)
            {
                _rainView.Dispose();
                if (Application.isPlaying)
                {
                    Destroy(_rainRoot);
                }
                else
                {
                    DestroyImmediate(_rainRoot);
                }
            }
            // Unsubscribed explicitly, for PlayerAnimatorView's reason: a body destroyed while its
            // scope lives would otherwise be handed events for a component Unity has killed.
            _attackSubscription?.Dispose();
            _targetSubscription?.Dispose();
            _firedSubscription?.Dispose();
            _holdSubscription?.Dispose();
            _volleySubscription?.Dispose();
            _rollSubscription?.Dispose();
            _rollEndedSubscription?.Dispose();
            _diedSubscription?.Dispose();
            _damagedSubscription?.Dispose();

            _attackSubscription = null;
            _targetSubscription = null;
            _firedSubscription = null;
            _holdSubscription = null;
            _volleySubscription = null;
            _rollSubscription = null;
            _rollEndedSubscription = null;
            _diedSubscription = null;
            _damagedSubscription = null;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (_killshotWinding)
            {
                // Only the visual body turns; core still owns the motor and its release.
                transform.rotation = Quaternion.LookRotation(_killshotDirection, Vector3.up);
            }
            if (_immuneLeft > 0f)
            {
                PlaceImmunePop();
            }
        }

        private void OnDisable() => CancelKillshot();

        private void CancelKillshot()
        {
            EndKillshot(false);
            _killshotView?.Clear();
            _immuneLeft = 0f;
            if (_immunePop != null)
            {
                _immunePop.gameObject.SetActive(false);
            }
        }

        private void PlaceImmunePop()
        {
            float rise = (1f - _immuneLeft / ImmunePopSeconds) * 0.35f;
            _immunePop.transform.SetPositionAndRotation(transform.position + Vector3.up * (1.5f + rise),
                _immuneCamera != null ? _immuneCamera.transform.rotation : Quaternion.identity);
        }

        private void OnKillshotWindUp(KillshotWindUp evt)
        {
            if (_animator == null || _dead)
            {
                return;
            }
            _killshotWinding = true;
            _killshotDirection = new Vector3(evt.Direction.X, 0f, evt.Direction.Z);
            transform.rotation = Quaternion.LookRotation(_killshotDirection, Vector3.up);
            _castUpLeft = 0f;
            _bindingReleaseLeft = 0f;
            _animator.ResetTrigger(_shootId);
            _animator.ResetTrigger(_releaseId);
            _animator.ResetTrigger(_castUpId);
            _animator.ResetTrigger(_placeId);
            _animator.ResetTrigger(_hitId);
            _animator.SetBool(_aimingId, true);
            _animator.CrossFadeInFixedTime("Kneel", 0.1f, 0, 0f);
            _animator.SetFloat(_shotSpeedId, ShotSpeed(evt.Seconds));
            _animator.Play(_drawStateId, _animator.GetLayerIndex(UpperBodyLayer), NockedNormalized);
        }

        private void EndKillshot(bool release)
        {
            if (!_killshotWinding)
            {
                return;
            }
            _killshotWinding = false;
            transform.localRotation = Quaternion.identity;
            _animator.CrossFadeInFixedTime("Locomotion", 0.18f, 0);
            int layer = _animator.GetLayerIndex(UpperBodyLayer);
            if (release)
            {
                _animator.SetFloat(_shotSpeedId, MaxShotSpeed);
                _animator.Play("Release", layer, 0f);
            }
            else
            {
                _animator.ResetTrigger(_shootId);
                _animator.ResetTrigger(_releaseId);
                _animator.Play("Empty", layer, 0f);
            }
            Aim();
        }

        /// <remarks>
        /// <para>
        /// The multiplier comes from the event's draw time, so it follows whatever the fire rate
        /// currently is without this view knowing a weapon exists — the first shot after a pause
        /// included.
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
            if (_castUpLeft > 0f || _killshotWinding)
            {
                return;
            }
            if (_animator == null)
            {
                return;
            }

            _animator.SetFloat(_shotSpeedId, ShotSpeed(evt.DrawSeconds));

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
            if (_animator == null || evt.SourceId != 0 || _castUpLeft > 0f || _killshotWinding)
            {
                return;
            }

            _animator.ResetTrigger(_shootId);
            _animator.SetTrigger(_releaseId);
        }

        private void OnRainCast(RainOfArrowsCast evt)
        {
            if (_killshotWinding)
            {
                return;
            }
            _bindingReleaseLeft = 0f;
            if (_animator == null || _dead)
            {
                return;
            }
            _animator.ResetTrigger(_shootId);
            _animator.ResetTrigger(_releaseId);
            _castUpLeft = 0.4f;
            _animator.SetTrigger(_castUpId);
        }

        private void OnBindingCast(SkillCast evt)
        {
            if (evt.SkillId.Value != "skill.marksman.binding-shot" || _animator == null || _dead || _killshotWinding)
            {
                return;
            }
            // SkillCast follows the accepted arrow's ProjectileFired. Show a quick draw and
            // release cosmetically; the simulation never waits for an animation frame.
            _castUpLeft = 0f;
            _animator.ResetTrigger(_castUpId);
            _animator.ResetTrigger(_shootId);
            _animator.ResetTrigger(_releaseId);
            _animator.SetFloat(_shotSpeedId, MaxShotSpeed);
            _animator.Play(_drawStateId, _animator.GetLayerIndex(UpperBodyLayer), NockedNormalized);
            _bindingReleaseLeft = 0.1f;
        }

        private void OnTrapPlaced(TrapPlaced evt)
        {
            if (_animator == null || _dead || _killshotWinding)
            {
                return;
            }
            // A quick upper-body stoop; the legs keep running and nothing waits on it.
            _bindingReleaseLeft = 0f;
            _animator.ResetTrigger(_shootId);
            _animator.ResetTrigger(_releaseId);
            _animator.ResetTrigger(_castUpId);
            _castUpLeft = PlaceSeconds;
            _animator.SetTrigger(_placeId);
        }

        /// <remarks>
        /// A blocked target still raises the bow. CC §3.6 holds the facing on something the
        /// character cannot hurt, and a Ranger looking down a drawn arrow at it is the right pose
        /// for "go around". The weapon, not this view, is what declines to shoot.
        /// </remarks>
        private void OnTargetChanged(TargetChanged evt)
        {
            _targetId = evt.Id;
            Aim();
        }

        /// <remarks>
        /// A hold lowers the bow and leaves the target alone: <see cref="TargetChanged"/> still
        /// names what the Ranger will shoot when it stops, so letting go raises the bow at it with
        /// no second fact needed (RS-03d rule 1).
        /// </remarks>
        private void OnHoldFireChanged(HoldFireChanged evt)
        {
            _holding = evt.IsHolding;
            Aim();
        }

        private void OnVolleyReady(VolleyReady evt)
        {
            if (_volleyGlow == null)
            {
                return;
            }

            _volleyGlow.SetActive(evt.IsReady && !_dead);
        }

        /// <remarks>
        /// <para>
        /// <b>The direction is read in the body's frame, once.</b> Core holds the facing for the
        /// length of a dash (<c>RunSession.TickBody</c>), so the clip chosen on the first frame is
        /// right for the last. A roll to the side while the Ranger faces its target is
        /// <c>Roll_Left</c> or <c>_Right</c>, not a forward roll sliding sideways. The four rolls are
        /// authored by <c>Tools/Blender/roll.py</c>, because KayKit has none (RS-06d).
        /// </para>
        /// <para>
        /// <c>InverseTransformDirection</c> ignores scale, as <see cref="Step"/>'s does. A direction
        /// with no length, or none that is finite, rolls forward rather than writing a NaN into the
        /// blend (AR §18.3).
        /// </para>
        /// </remarks>
        private void OnRollStarted(ChargeStarted evt)
        {
            if (_animator == null || _dead)
            {
                return;
            }

            Vector3 local = transform.InverseTransformDirection(new Vector3(evt.DirectionXZ.X, 0f, evt.DirectionXZ.Y));

            local.y = 0f;

            float length = local.magnitude;

            local = length > 0f && !float.IsInfinity(length) ? local / length : Vector3.forward;

            _rolling = true;

            _animator.SetFloat(_dodgeXId, local.x);
            _animator.SetFloat(_dodgeZId, local.z);
            _animator.SetBool(_dodgingId, true);
        }

        private void OnRollEnded(ChargeEnded evt)
        {
            _rolling = false;

            if (_animator == null)
            {
                return;
            }

            _animator.SetBool(_dodgingId, false);
        }

        /// <remarks>
        /// No flinch for a hit that did nothing to HP — blocked, or the Aegis took it whole — for
        /// <see cref="PlayerAnimatorView"/>'s reason: it would tell the player they were hurt when
        /// they were not. None for the killing blow, which the death answers in the same tick, and
        /// none mid-roll, whose clip owns the whole body (RS-09 rules 1–5).
        /// </remarks>
        private void OnDamaged(PlayerDamaged evt)
        {
            if (evt.Blocked && _killshotWinding && _immunePop != null && isActiveAndEnabled)
            {
                _immuneLeft = ImmunePopSeconds;
                _immunePop.alpha = 1f;
                PlaceImmunePop();
                _immunePop.gameObject.SetActive(true);
            }

            if (_animator == null || _dead || _rolling || _killshotWinding || evt.Blocked || !(evt.ToHp > 0f) || !(evt.HpFraction > 0f))
            {
                return;
            }

            _animator.SetTrigger(_hitId);
        }

        /// <remarks>
        /// Latched, for <see cref="PlayerAnimatorView"/>'s reason: a fact arriving on the tick after
        /// the killing blow must not raise the bow or light it again.
        /// </remarks>
        private void OnDied(PlayerDied evt)
        {
            CancelKillshot();
            _dead = true;

            if (_volleyGlow != null)
            {
                _volleyGlow.SetActive(false);
            }

            Aim();
        }

        /// <summary>
        /// Raises the bow when there is a target and fire is not held, and lowers it otherwise
        /// (RS-03d rule 1).
        /// </summary>
        /// <remarks>
        /// <b>Lowering the bow ends the string of shots the draw speed is measured over</b>,
        /// whichever fact lowered it (RS-02a's V3, which called it a volley before RS-03b gave that
        /// word to the fan). The next shot's speed is not measured across the time the bow was
        /// down, which is not a fire rate. It keeps the speed the last string measured, so a Ranger
        /// that stops after a run draws its first arrow at the bow's pace rather than at authored
        /// speed, which would release before full draw.
        /// </remarks>
        private void Aim()
        {
            if (_animator == null)
            {
                return;
            }

            bool aiming = !_dead && (_killshotWinding || (_targetId >= 0 && !_holding));

            _animator.SetBool(_aimingId, aiming);
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

        /// <remarks>
        /// The flinch layer sits above <see cref="UpperBodyLayer"/>, on its mask, so a hit jerks the
        /// chest and arms while the legs keep the stick and the draw keeps time underneath (RS-09).
        /// It rests at weight 0; see <see cref="FlinchLayer"/> for why this view raises it (RS-09a).
        /// </remarks>
        private void WeighFlinch()
        {
            // Looked up once, lazily: -2 means not yet asked and -1 means absent.
            if (_flinchLayer == -2)
            {
                _flinchLayer = _animator.GetLayerIndex(FlinchLayer.Name);
            }

            FlinchLayer.Drive(_animator, _flinchLayer, _hitStateId);
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
