using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Enemy.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// GD §16.2's two enemy treatments, in one component: a thin bar that appears over a basic enemy
    /// while it is being hit and fades two seconds later, and one over an Elite that never leaves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It renders one event and decides nothing.</b> The fraction arrives on
    /// <see cref="EnemyDamaged"/> — it has carried <c>HpFraction</c> since M1-11 — and this filters by
    /// its own bound id and draws. There is no health here, and the only clock is a fade no rule
    /// depends on.
    /// </para>
    /// <para>
    /// <b>Elite-ness comes off <see cref="EnemySpawned"/>, not out of a catalog.</b> <c>EnemyViews</c>
    /// holds an <c>EnemyLookBook</c> and no <c>ContentCatalog</c>, and handing it one would answer the
    /// question wrongly for M7-02, whose Elites are a body upgraded at spawn rather than an archetype
    /// (GD §8.3). See <see cref="EnemySpawned.IsElite"/>'s own remarks. <b>Nothing ships as an
    /// Elite</b> — <c>EnemySpec.IsElite</c> has existed since M1-05 and no authored archetype sets it
    /// — so the persistent treatment is tested and unseen until M7-02, which is the same bargain
    /// M3-03's null-tree branch makes.
    /// </para>
    /// <para>
    /// <b>An Elite's bar is on at full health, and that is a decision rather than a style.</b> GD §16.2
    /// states the reason: <em>"they're priority targets — you're making decisions about them, so you
    /// need the number."</em> A bar that only appeared after the first hit would hide exactly the body
    /// the player is deciding about.
    /// </para>
    /// <para>
    /// <b><c>EnemyHitFeedback</c>'s shape exactly, and for its reasons.</b> Injected once when the pool
    /// builds the body, subscribed for the life of the pool rather than of any one enemy, filtering
    /// every event on the bound id — so a body sitting in the pool matches nothing, having been handed
    /// the id of nobody. <see cref="Bind"/> is called on the rental before the body is put into
    /// service, so a bar is never drawn for one frame over the wrong enemy. Nothing is instantiated
    /// during a run.
    /// </para>
    /// <para>
    /// <b>The fade runs on <see cref="Time.deltaTime"/> and therefore freezes under a pause</b>, which
    /// is correct: <c>Time.timeScale</c> is 0 while the game is stopped (M3-08a rule 13) and a bar
    /// draining over a frozen arena would be the only thing moving on the screen. This is the
    /// deliberate opposite of M3-10b's Overflow toast, which counts <em>unscaled</em> seconds
    /// precisely because it has to outlive a screen.
    /// </para>
    /// <para>
    /// <b>It carries no colour of its own.</b> The fill is <see cref="Palette.Neutral"/> — GD §16.4's
    /// <em>everything else</em>, which is what an enemy is: not the player, not danger, not corruption,
    /// not a reward. A serialized <see cref="Color"/> here would be the placeholder problem with an
    /// extra step (M3-13a rule 8, and <c>Views_CarryNoSerializedColour</c> from this side).
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(EnemyView))]
    public sealed class EnemyHealthBar : MonoBehaviour
    {
        /// <summary>
        /// The rotation that points the bar at the camera: <c>FollowCamera</c>'s authored 57° pitch
        /// and its yaw, which that class's own remarks say <em>"must stay 0"</em>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>private static readonly</c> rather than a field per body, which AR §7 lists as legal
        /// beside <c>TelegraphRingView.FlatRotation</c> and <c>ZoneView.FlatRotation</c>: what that row
        /// sanctions as the one shared static is <c>Palette</c>, and a constant nothing else can reach
        /// is an implementation detail of one type.
        /// </para>
        /// <para>
        /// <b>The angle is written down twice on purpose</b>, the way <c>EnemyLook.BoneGrey</c> and
        /// <c>M_BoneGrey</c> are. Reading it off the live camera would mean <c>Camera.main</c>, a
        /// tagged lookup this project refuses everywhere else (<c>RunScope</c> and
        /// <c>TapToFocusAdapter</c> both say so), and the camera is not something a pooled prefab can
        /// hold a reference to. <c>Bar_FacesTheAuthoredCameraPitch</c> is what tells us the day the two
        /// stop agreeing.
        /// </para>
        /// </remarks>
        private static readonly Quaternion FacingCamera = Quaternion.Euler(57f, 0f, 0f);

        [Tooltip("The bar's root, on the child canvas above the body. Its alpha is the fade and its " +
                 "rect is what gets sized; nothing else is written to it.")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("The bar itself. Its Image must be Filled / Horizontal with the origin on the " +
                 "left — fillAmount and color are the only things written to it.")]
        [SerializeField] private Image _fill;

        [Tooltip("How long the bar stays up at full brightness after a hit, in seconds. GD §16.2's " +
                 "two seconds. An Elite ignores it entirely.")]
        [Min(0f)]
        [SerializeField] private float _secondsShown = 2f;

        [Tooltip("How long it then takes to fade out, in seconds. Short: the bar has already said " +
                 "what it had to say, and a long fade would leave the arena covered in them.")]
        [Min(0f)]
        [SerializeField] private float _fadeSeconds = 0.25f;

        [Tooltip("How wide the bar is, in dp. Applied at runtime for the reason SkillButton applies " +
                 "its own: a canvas measures in reference pixels, which are a different physical " +
                 "size on every phone.")]
        [Min(1f)]
        [SerializeField] private float _widthDp = 28f;

        [Tooltip("How tall the bar is, in dp. Three is thin enough to read as a bar rather than as " +
                 "a second body — whether it reads at all on a six-inch screen is ledger row 4.")]
        [Min(1f)]
        [SerializeField] private float _heightDp = 3f;

        [Tooltip("How far above the body's origin the bar floats, in metres. Clear of the tallest " +
                 "archetype's head at its authored body scale.")]
        [SerializeField] private float _heightMetres = 2.1f;

        private IDisposable _damagedSubscription;

        /// <summary>The id this bar is standing over, or <see cref="EnemyView.Unbound"/>.</summary>
        private int _id = EnemyView.Unbound;

        /// <summary>Whether the bound body is a priority target, and therefore never fades.</summary>
        private bool _isElite;

        /// <summary>The fill as last drawn, in <c>[0, 1]</c>.</summary>
        private float _fraction = 1f;

        /// <summary>
        /// Seconds left of the bar's visible life: the last <see cref="_fadeSeconds"/> of it are the
        /// fade, so one field is both the hold and the ramp.
        /// </summary>
        private float _shownRemaining;

        /// <summary>The body rotation the bar was last aligned against — see <c>LateUpdate</c>.</summary>
        private Quaternion _orientedAgainst = Quaternion.identity;

        /// <summary>
        /// Whether <see cref="_orientedAgainst"/> means anything yet. A flag rather than a sentinel
        /// rotation, because <see cref="Quaternion.identity"/> is a rotation a standing body really
        /// has — a bar bound to one would otherwise never be oriented at all.
        /// </summary>
        private bool _oriented;

        private bool _injected;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> is null.</exception>
        /// <remarks>
        /// Subscribed here rather than in <c>OnEnable</c>, for the reason <c>EnemyHitFeedback</c>
        /// gives: <c>EnemyViews</c> creates this body through <c>IObjectResolver.Instantiate</c>, and
        /// Unity runs <c>Awake</c> and <c>OnEnable</c> during the instantiate itself — before
        /// VContainer has injected anything, so a subscription taken there would be taken against a
        /// null hub on every enemy in the game.
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _injected = true;

            // One subscription, and deliberately not a second to EnemySpawned. EnemyViews calls
            // Bind on the rental — before the body is in service — and EnemyView.OnDespawn calls
            // Unbind on the way out, which is where "what a rental has to forget" already lives. A
            // census subscription here would hand every spawn in the arena to every body in the pool
            // in order to discover that twenty-seven of them are not it.
            _damagedSubscription = hub.Subscribe<EnemyDamaged>(OnDamaged);
        }

        /// <summary>Whether the bar is currently drawn at all.</summary>
        public bool IsShown => _id != EnemyView.Unbound && (_isElite || _shownRemaining > 0f);

        /// <summary>
        /// Told what it is standing over, on the rental. Before the body is in service.
        /// </summary>
        /// <param name="id">The core-side id whose damage this bar answers to.</param>
        /// <param name="isElite">
        /// Whether the body is a priority target. <c>true</c> puts the bar up immediately at full
        /// health and stops it ever fading (rule 7).
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="id"/> is not positive. <c>EnemyRegistry</c> issues ids from 1, and a bar
        /// bound to <see cref="EnemyView.Unbound"/> would match every unbound body in the pool at
        /// once — the one failure this filter exists to make impossible.
        /// </exception>
        public void Bind(int id, bool isElite)
        {
            if (id <= EnemyView.Unbound)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(id),
                    id,
                    "An enemy id must be positive; EnemyRegistry issues them from 1.");
            }

            _id = id;
            _isElite = isElite;
            _fraction = 1f;

            // A basic enemy opens hidden and earns its bar with the first hit (GD §16.2 gives it no
            // standing bar); an Elite's is up from this moment, which is the whole of rule 7.
            _shownRemaining = 0f;

            // Re-armed so the first LateUpdate after a rental orients the bar, whatever the body it
            // is standing on was pointing at when it died.
            _oriented = false;

            Draw();
        }

        /// <summary>
        /// Takes the bar out of service: hidden, forgotten, and not an Elite. Idempotent.
        /// </summary>
        /// <remarks>
        /// AR §18.4 (rule 8). The Elite flag is the thing that must not survive — a body rented once
        /// as an Elite and again as a Husk would otherwise carry a standing bar into a basic enemy,
        /// which reads as the wrong enemy being important.
        /// </remarks>
        public void Unbind()
        {
            _id = EnemyView.Unbound;
            _isElite = false;
            _fraction = 1f;
            _shownRemaining = 0f;

            Draw();
        }

        /// <exception cref="MissingReferenceException">The root or the fill is unassigned.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this body.</exception>
        /// <remarks>
        /// Checked in <c>Start</c> rather than <c>Awake</c> for <c>EnemyHitFeedback</c>'s reason:
        /// injection happens after the instantiate that ran <c>Awake</c>, so <c>Start</c> is the first
        /// moment "nothing injected me" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            // Unity's == rather than `is null`: an unassigned serialized reference is a live object
            // that only compares equal to null through the engine's operator.
            if (_root == null || _fill == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(EnemyHealthBar)} on '{name}' is missing its root or its fill. Drag " +
                    "the Health Bar child's CanvasGroup and its Fill image onto them — without " +
                    "both, an Elite is a priority target the player has no way of reading.");
            }

            if (!_injected)
            {
                throw new InvalidOperationException(
                    $"{nameof(EnemyHealthBar)} on '{name}' was never injected, so it is deaf to " +
                    "every hit. Enemy bodies must be created through EnemyViews — that is what " +
                    "routes VContainer's injection into the prefab.");
            }

            Place();
            Draw();
        }

        private void OnDestroy()
        {
            // Unsubscribed explicitly rather than left to the hub's disposal: a body destroyed
            // mid-run would otherwise stay in the subscriber list and be handed events for a
            // component Unity has already killed.
            _damagedSubscription?.Dispose();
            _damagedSubscription = null;
        }

        /// <remarks>
        /// Most frames this returns after one comparison: a basic enemy's bar is down for all but
        /// 2.25 s of each of its lives, and an Elite's never moves.
        /// </remarks>
        private void Update()
        {
            // Time.deltaTime, which is zero while the game is paused — see the class remarks. A bar
            // that kept draining over a stopped arena would be the only thing moving on the screen.
            // The scaled delta is chosen *here*, which is what makes the pause a one-line decision
            // rather than a property of the fade.
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Advances the fade by <paramref name="dt"/> seconds.
        /// </summary>
        /// <remarks>
        /// Split out of <c>Update</c> on <c>FirstActiveHint</c>'s precedent: <c>Time.deltaTime</c> is
        /// not something an EditMode fixture can advance (Traps §5), so the two-second rule would
        /// otherwise have had to be a PlayMode row.
        /// </remarks>
        private void Tick(float dt)
        {
            if (_isElite || _shownRemaining <= 0f)
            {
                return;
            }

            _shownRemaining -= dt;

            if (_shownRemaining < 0f)
            {
                _shownRemaining = 0f;
            }

            Draw();
        }

        /// <summary>
        /// Points the bar at the camera — once for a body that is standing still.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Rule 9's <em>"oriented once"</em> cannot be taken literally, and the reason is in
        /// <c>EnemyView</c> rather than in the camera.</b> The spec's argument — <c>FollowCamera</c>'s
        /// rotation <em>"is authored, not derived"</em>, so an alignment written at <c>Start</c> holds
        /// for the whole run — is true about the camera and false about this transform:
        /// <c>EnemyView.Face</c> writes <c>transform.rotation</c> on the <b>root</b> every tick the
        /// enemy is walking, and a bar parented to the body inherits that yaw. A rotation written once
        /// at <c>Start</c> would be wrong by the first frame the enemy turned.
        /// </para>
        /// <para>
        /// So the rule's <em>intent</em> is kept instead of its letter: nothing is written while the
        /// bar is hidden, and nothing is written on a frame the body did not turn. A standing body
        /// costs one quaternion for its whole life, which is what
        /// <c>Bar_IsOrientedOnceNotPerFrame</c> asserts; twenty-eight walking Elites would cost
        /// twenty-eight, and nothing ships as an Elite. <b>The day the camera earns a rotation</b> —
        /// the fixed whole-arena framing the owner has in mind — <b>this is the method that has to
        /// change</b>, and <see cref="FacingCamera"/> is where it shows up.
        /// </para>
        /// <para>
        /// <c>LateUpdate</c> rather than <c>Update</c>, because <c>RunTicker</c> applies the move
        /// intents — and therefore the facing — during <c>Update</c>. Aligning first would be aligning
        /// against last frame's yaw.
        /// </para>
        /// </remarks>
        private void LateUpdate()
        {
            if (!IsShown || _root == null)
            {
                return;
            }

            Quaternion body = transform.rotation;

            // Quaternion's == is an approximate dot-product compare, which is cheaper than the matrix
            // decompose a rotation *write* costs — that asymmetry is the whole of the saving.
            if (_oriented && body == _orientedAgainst)
            {
                return;
            }

            _oriented = true;
            _orientedAgainst = body;
            _root.transform.rotation = FacingCamera;
        }

        private void OnDamaged(EnemyDamaged evt)
        {
            if (_id == EnemyView.Unbound || evt.Id != _id)
            {
                return;
            }

            // Non-finite reads as full rather than clamping, for the reason EnemyHitFeedback's tint
            // does: Mathf.Clamp01 is two comparisons and every comparison against NaN is false, so a
            // NaN would pass straight through both bounds into fillAmount (AR §18.3).
            _fraction = float.IsFinite(evt.HpFraction) ? Mathf.Clamp01(evt.HpFraction) : 1f;

            // Restarted from the top by every hit, so a flurry leaves one bar rather than a stutter —
            // HpBarView's ghost rule, for its reason.
            _shownRemaining = _secondsShown + _fadeSeconds;

            Draw();
        }

        /// <remarks>
        /// Every write is guarded by a comparison against what is already on screen, and that is not
        /// merely an optimisation: assigning <c>fillAmount</c> or <c>color</c> marks the graphic dirty
        /// and queues a canvas rebuild, so writing an unchanged value would rebuild twenty-eight
        /// world-space canvases on every frame of a fight where nothing had changed.
        /// </remarks>
        private void Draw()
        {
            // Unity's ==, and a silent return: Bind can be called before Start has run — EnemyViews
            // binds on the rental, and a prewarmed pool is built before anything's Start — so an
            // undressed reference is reported by the guard in Start rather than by a null here.
            if (_root == null || _fill == null)
            {
                return;
            }

            float alpha = Alpha();

            if (!Mathf.Approximately(_root.alpha, alpha))
            {
                _root.alpha = alpha;
            }

            if (!Mathf.Approximately(_fill.fillAmount, _fraction))
            {
                _fill.fillAmount = _fraction;
            }

            if (_fill.color != Palette.Neutral)
            {
                _fill.color = Palette.Neutral;
            }
        }

        /// <summary>
        /// How bright the bar is: 1 for an Elite and for the hold, ramping to 0 across the last
        /// <see cref="_fadeSeconds"/>, and 0 for a body nobody is standing over.
        /// </summary>
        private float Alpha()
        {
            if (_id == EnemyView.Unbound)
            {
                return 0f;
            }

            if (_isElite)
            {
                return 1f;
            }

            if (_shownRemaining <= 0f)
            {
                return 0f;
            }

            // A zero fade is a legal dressing — the bar simply vanishes — and is guarded rather than
            // divided by, because the alternative is an infinity in an alpha and a canvas group that
            // never renders again.
            return _fadeSeconds > 0f ? Mathf.Clamp01(_shownRemaining / _fadeSeconds) : 1f;
        }

        /// <summary>
        /// Sizes the bar in dp and floats it above the body.
        /// </summary>
        /// <remarks>
        /// <c>SkillButton</c>'s argument, applied to a world-space canvas: a canvas measures in
        /// reference pixels, so a bar authored at 28 of those is a different physical width on every
        /// phone. The height is metres and the width is dp because they answer to different things —
        /// how tall the body is, and how big a thumb-sized screen is.
        /// </remarks>
        private void Place()
        {
            if (_root.transform is RectTransform rect)
            {
                rect.sizeDelta = new Vector2(_widthDp, _heightDp) * PixelsPerDp();
            }

            _root.transform.localPosition = new Vector3(0f, _heightMetres, 0f);
        }

        /// <summary>
        /// How many canvas units one dp is worth: device pixels per dp, divided by the canvas scale
        /// so the result is the requested physical size rather than that many reference pixels.
        /// </summary>
        /// <remarks><c>HudPresenter.PixelsPerDp</c>, to the line — the same arithmetic, on a canvas
        /// that happens to be in the world.</remarks>
        private float PixelsPerDp()
        {
            var canvas = GetComponentInChildren<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            return StickShaper.PixelsPerDp(Screen.dpi) / scale;
        }
    }
}
