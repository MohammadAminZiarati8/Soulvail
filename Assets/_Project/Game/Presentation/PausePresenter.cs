using System;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Pause.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// GD §7.3's <em>"pause anywhere, instantly"</em>: a small top-right icon stops the run dead, a
    /// panel offers the two ways out of it, and <c>PauseReason.Menu</c> gets the first caller
    /// M3-08a wrote it for. GD §5.2, §11.4.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A uGUI <see cref="Button"/>, not the Input System — the opposite of the death overlay's
    /// choice, for the same reason.</b> <c>HudPresenter</c> reads the tap that dismisses death
    /// through <c>InputAdapter</c> because <em>"tap anywhere"</em> has to include the stick and the
    /// Charge button, which a uGUI raycast would have to be layered above. A pause icon is the exact
    /// inverse: GD §5.2 puts it top-right and calls it <em>"a small target, but non-critical and out
    /// of the way"</em>, and the one thing it must never be is tappable anywhere. A
    /// <see cref="Button"/> gives that for free, and it leaves <c>HudPresenter</c>'s standing claim —
    /// the only class in <c>Soulvail.Game</c> that reads the Input System — intact.
    /// </para>
    /// <para>
    /// <b>It holds both halves of the pause, in this file.</b> <c>RunPause.Pause(PauseReason.Menu)</c>
    /// in <see cref="Open"/>, <c>Resume(PauseReason.Menu)</c> in <see cref="Close"/>. A pause raised
    /// in one class and lowered in another is how a screen ends up closed over a frozen game.
    /// <b>That this differs from M3-08b is deliberate rather than a contradiction</b>: the level-up
    /// gate lives in <c>RunTicker.LevelUpPhase</c> because <em>stopped</em> is a once-a-frame pure
    /// function of core state there — <c>HasOffer</c> — so a missing presenter costs a missing screen
    /// instead of a run that ticks on with an offer nobody can spend. A pause <em>menu</em> has no
    /// core state behind it at all: nothing in <c>RunState</c> knows the player tapped an icon, and
    /// there is nothing for a ticker to read. So the two screens' pause halves living in different
    /// files is the same rule applied twice, not two rules.
    /// </para>
    /// <para>
    /// <b>It does not gate the tick and must not try to.</b> <c>RunTicker.Tick</c> already returns
    /// while the pause is held (M3-08a rule 4), so this file raises a flag and draws a panel.
    /// Nothing here touches <c>Time.timeScale</c> or <c>Application.targetFrameRate</c> either —
    /// <c>RunPause</c> owns both globals (M3-08a rule 13), and a second writer is how one of them
    /// stops being restored.
    /// </para>
    /// <para>
    /// <b>No fade, in or out.</b> GD §7.3 says <em>instantly</em>, and <c>Time.timeScale</c> is 0 the
    /// moment the pause is held, so a scaled animation would freeze half-played and an unscaled one
    /// is a second clock — M3-08b rule 4's argument, and the same answer.
    /// </para>
    /// <para>
    /// <b>Quitting does not delete the run.</b> <c>SaveWriter</c> clears the file on
    /// <c>PlayerDied</c> and deliberately not on <c>RunEnded</c> (<c>SaveWriter.cs:128</c>), so
    /// leaving through this panel ends the session and leaves the last boundary's snapshot on disk —
    /// <c>Continue</c> resumes from it. This class adds no deletion and no new write. Said out loud
    /// because it is the first way out of a run that is not dying, and a player who quits to the
    /// menu and finds their run gone would have no way to tell that from a bug.
    /// </para>
    /// <para>
    /// <b>What this costs the stopwatch, said once.</b> A Menu pause contributes no <c>Dt</c> for the
    /// same reason a level-up does (M3-08a rule 11), so <c>RunState.Time</c> still counts play
    /// seconds only and a wall clock now diverges from it by <em>two</em> kinds of interruption
    /// rather than one. M3-15 quotes one number and says which; this task adds the second term to the
    /// gap (ledger row 8).
    /// </para>
    /// </remarks>
    public sealed class PausePresenter : MonoBehaviour
    {
        [Tooltip("The top-right icon. A Button rather than a read of the Input System — see the " +
                 "class remarks: the one thing this target must never be is tappable anywhere.")]
        [SerializeField] private Button _icon;

        [Tooltip("The panel, switched between alpha 0 and 1. No fade — timeScale is 0 while this " +
                 "is up, so a scaled tween would freeze half-played.")]
        [SerializeField] private CanvasGroup _panel;

        [Tooltip("Back to the fight, from exactly where it stood.")]
        [SerializeField] private Button _resume;

        [Tooltip("Out to the Menu. It does not delete the run — see the class remarks.")]
        [SerializeField] private Button _quit;

        [Tooltip("The icon's side in dp — GD §5.2's small top-right target. Applied at runtime for " +
                 "the reason SkillButton applies its own: a Scale-With-Screen-Size canvas measures " +
                 "in reference pixels, which are a different physical size on every phone. A guess " +
                 "until a phone exists — ledger row 4.")]
        [SerializeField] private float _iconSizeDp = 44f;

        [Tooltip("How far the icon's top-right corner sits in from the safe area's right edge and " +
                 "down from its top, in dp. A guess until a phone exists — ledger row 4.")]
        [SerializeField] private Vector2 _iconMarginDp = new Vector2(16f, 16f);

        private IRunSession _session;
        private RunPause _pause;
        private SceneLoader _loader;

        private IDisposable _diedSubscription;

        /// <summary>
        /// The player is dead, so this screen is over for the rest of the run.
        /// </summary>
        /// <remarks>
        /// One-way, and never cleared: a run does not come back from a death, and the object is
        /// destroyed with the scene that held it.
        /// </remarks>
        private bool _retired;

        /// <summary>
        /// A Quit has been asked for and the scene load has not answered yet.
        /// </summary>
        /// <remarks>
        /// The latch rather than the button's <c>interactable</c>, because the button is only the
        /// first of the two guards: uGUI dispatches both taps of a double tap from one
        /// <c>EventSystem</c> pass, and a handler invoked directly — which is what a test and a
        /// future screen both do — never consults <c>interactable</c> at all.
        /// </remarks>
        private bool _quitting;

        /// <summary>True while this screen holds the pause — M3-09b and M3-09c's gate.</summary>
        /// <remarks>
        /// A field rather than a read of <c>RunPause.Holder</c>, so that the panel being up and the
        /// pause being held are the same fact by construction. A derived answer would go false under
        /// this screen the moment anything else released the pause, leaving a panel on screen with
        /// nothing behind it.
        /// </remarks>
        public bool IsOpen { get; private set; }

        /// <param name="session">
        /// The run, for the one thing <see cref="Open"/> has to know: whether there is still a run to
        /// stop. Not <c>IPlayerCommands</c> — a pause screen asks the game for nothing.
        /// </param>
        /// <param name="pause">
        /// The run's pause, and this file holds both halves of it. <c>RunPause</c> owns
        /// <c>Time.timeScale</c> and <c>Application.targetFrameRate</c>; nothing here writes either.
        /// </param>
        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="loader">Where Quit goes.</param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// Subscribed here rather than in <c>OnEnable</c> — <c>HudPresenter</c>'s reason:
        /// <c>RunScope</c> builds its container from its own <c>Awake</c> and Unity orders no two of
        /// those, so an <c>OnEnable</c> subscription reaches for a hub that may not exist yet. The
        /// two button handlers are wired here for the same reason and dropped in the same place.
        /// </remarks>
        [Inject]
        public void Construct(
            IRunSession session,
            RunPause pause,
            DomainEventHub hub,
            SceneLoader loader)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _pause = pause ?? throw new ArgumentNullException(nameof(pause));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            // Death is the one event this screen renders — see OnPlayerDied.
            _diedSubscription?.Dispose();
            _diedSubscription = hub.Subscribe<PlayerDied>(OnPlayerDied);

            Wire(_icon, Open);
            Wire(_resume, Close);
            Wire(_quit, Quit);
        }

        /// <exception cref="MissingReferenceException">The icon, the panel or a button is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this presenter.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_icon == null || _panel == null || _resume == null || _quit == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(PausePresenter)} is missing its icon, its panel or one of its two " +
                    "buttons. A pause screen that is only partly dressed stops the run and then " +
                    "offers no way back, which is indistinguishable from a crash.");
            }

            if (_pause is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(PausePresenter)} was never injected, so its icon would stop nothing. " +
                    "The component is registered by RunScope — drag this object onto its Pause " +
                    "Presenter field.");
            }

            Place();

            // Down whatever the prefab was left dressed as, so a panel someone was editing cannot
            // ship covering the arena — HudPresenter's argument for its death panel.
            HidePanel();

            RefreshIcon();
        }

        /// <remarks>
        /// Explicit rather than left to the hub's disposal: a screen destroyed before its scope — a
        /// scene reload, an arena opened without a run — would otherwise stay in a subscriber list
        /// and be handed an event for a component Unity has killed.
        /// <para>
        /// <b>It drops the subscription and the handlers, and lowers nothing.</b> A scope torn down
        /// with the panel up leaves the pause held, and <c>RunPause.Dispose</c> restores both globals
        /// unconditionally (M3-08a rule 13) — VContainer orders no two disposals, so this file must
        /// not be the only thing that restores a zeroed <c>timeScale</c>. It must not skip doing so
        /// on its own path either, which is why <see cref="Quit"/> closes before it loads.
        /// </para>
        /// </remarks>
        private void OnDestroy()
        {
            _diedSubscription?.Dispose();
            _diedSubscription = null;

            Unwire(_icon, Open);
            Unwire(_resume, Close);
            Unwire(_quit, Quit);
        }

        /// <remarks>
        /// One bool read per frame, and it is the whole of rule 4: the icon follows
        /// <c>!RunPause.IsPaused</c>, so <em>any</em> future holder retires it without this file
        /// learning that holder's events. Runs at <c>timeScale</c> 0 because <c>Update</c> does.
        /// </remarks>
        private void Update()
        {
            RefreshIcon();
        }

        /// <summary>Raises the Menu pause and shows the panel. The icon's own handler.</summary>
        /// <remarks>
        /// <para>
        /// <b>Every refusal here is silent, and that is the point.</b> <c>RunPause.Pause</c> throws on
        /// a second reason (M3-08a rule 12) and that guard is deliberately loud, so a pause menu must
        /// never be the thing that trips it. In practice M3-08b's screen is a full-screen canvas
        /// above this one and takes the touches anyway — belt and braces, and the braces are the ones
        /// that hold when a later screen forgets its scrim.
        /// </para>
        /// <para>
        /// A run that has already ended is refused for the same family of reasons: the death overlay
        /// owns that screen, and freezing the app behind a pause menu over a run nothing is ticking
        /// is exactly the state <c>RunPause.Dispose</c> exists to prevent.
        /// </para>
        /// </remarks>
        public void Open()
        {
            if (IsOpen || _retired || _pause is null || _session is null)
            {
                return;
            }

            // Rule 4, re-checked here rather than trusted from the icon's own interactable: Update
            // sets that once a frame, and the frame a level-up raises the pause is one where the
            // two can disagree.
            if (_pause.IsPaused || !_session.IsRunning)
            {
                return;
            }

            _pause.Pause(PauseReason.Menu);

            IsOpen = true;
            _quitting = false;

            ShowPanel();
            RefreshIcon();
        }

        /// <summary>
        /// Lowers it and hides the panel. Resume's handler, and M3-09b/09c's way back.
        /// </summary>
        /// <remarks>
        /// The pause goes down before the panel does. Neither line can fail today — the holder is
        /// checked first — but if one ever did, the app is left running with a panel over it, which a
        /// player can still tap out of, rather than frozen behind a screen that is already gone.
        /// </remarks>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;

            // Checked rather than assumed: RunPause.Dispose releases unconditionally on the way out
            // of a run, so a scope torn down under this panel leaves nothing here to give back —
            // and Resume throws when it is handed a reason that is not the holder.
            if (_pause != null && _pause.Holder == PauseReason.Menu)
            {
                _pause.Resume(PauseReason.Menu);
            }

            HidePanel();
            RefreshIcon();
        }

        /// <summary>
        /// Ends the session and goes back to the Menu, leaving the run on disk.
        /// </summary>
        /// <remarks>
        /// <b>It closes before it loads, and <c>RunPause.Dispose</c> is the backstop rather than the
        /// mechanism</b> (M3-08b rule 12's reasoning): VContainer orders no two disposals, so this
        /// file must not be the only thing that restores a zeroed <c>timeScale</c>, and it must not
        /// skip doing so either. The latch and both buttons go dead first, so a second tap arriving
        /// while the scene is still coming in cannot start a second load — <c>MenuPresenter</c>'s
        /// guard, with its sibling taken away for the same reason.
        /// </remarks>
        private void Quit()
        {
            if (_quitting)
            {
                return;
            }

            _quitting = true;

            SetPanelInteractable(false);

            // Before the load is asked for, and this is the ordering rule 7 is about: the Menu must
            // not be handed a frozen clock, and a scene load is not a place to find that out.
            Close();

            LeaveForMenu();
        }

        /// <summary>
        /// Leaves the run for the Menu, which disposes <c>RunScope</c> and ends everything with it.
        /// </summary>
        /// <remarks>
        /// <c>async void</c>, which is otherwise a smell and is exactly right for what is effectively
        /// an event handler — <c>MenuPresenter.Descend</c>'s shape, and every path out of the await
        /// is handled here. <b>Nothing on this path writes or deletes a save</b> (rule 8): the last
        /// boundary already wrote the file, and quitting is not dying.
        /// </remarks>
        private async void LeaveForMenu()
        {
            try
            {
                await _loader.LoadAsync(SceneLoader.Menu);
            }
            catch (Exception exception)
            {
                // The load failed, so this object is still alive and the run is still there. Give
                // the panel back rather than stranding the player on a dead screen — and re-open,
                // because Close already lowered the pause and the fight would otherwise be running
                // underneath a panel the player is still looking at.
                _quitting = false;

                SetPanelInteractable(true);
                Open();

                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// The player died underneath the screen: close it and take the icon away for good.
        /// </summary>
        /// <remarks>
        /// Death is the one thing that can happen under a screen that stops the world, because the
        /// tick that killed the player completes before the pause takes effect (M3-08a rule 11) — and
        /// a pause icon over a death overlay is two screens arguing about whose tap it was.
        /// <c>PlayerDied</c> rather than <c>RunEnded</c>, for <c>HudPresenter</c>'s reason: the second
        /// of those also fires when a scope is torn down, which is every ordinary exit from the Run
        /// scene, including the one this class just asked for.
        /// </remarks>
        private void OnPlayerDied(PlayerDied evt)
        {
            Close();

            _retired = true;

            if (_icon != null)
            {
                _icon.gameObject.SetActive(false);
            }
        }

        /// <remarks>
        /// Alpha 1 on the same call, with no tween and no coroutine — rule 5. The raycast block goes
        /// on with it: the panel is full-screen, so the stick and the Charge button are covered
        /// rather than disabled, which is what M3-08b's screen does for the same reason.
        /// </remarks>
        private void ShowPanel()
        {
            if (_panel == null)
            {
                return;
            }

            _panel.alpha = 1f;
            _panel.blocksRaycasts = true;
            _panel.interactable = true;

            SetPanelInteractable(true);
        }

        private void HidePanel()
        {
            if (_panel == null)
            {
                return;
            }

            _panel.alpha = 0f;
            _panel.blocksRaycasts = false;
            _panel.interactable = false;
        }

        /// <summary>Rule 4: the icon is live exactly when nothing is holding the pause.</summary>
        private void RefreshIcon()
        {
            if (_icon == null || _pause is null || _retired)
            {
                return;
            }

            _icon.interactable = !_pause.IsPaused;
        }

        private void SetPanelInteractable(bool value)
        {
            if (_resume != null)
            {
                _resume.interactable = value;
            }

            if (_quit != null)
            {
                _quit.interactable = value;
            }
        }

        /// <summary>
        /// Puts the icon <see cref="_iconSizeDp"/> across in the parent's top-right corner, in dp
        /// (rule 11).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Here rather than in the prefab for <c>SkillButton</c>'s reason: a Scale-With-Screen-Size
        /// canvas measures in reference pixels, so a 44 dp icon authored as 44 of those is a
        /// different physical size on every phone — and GD §5.2 is making a claim about a thumb,
        /// which is a physical object.
        /// </para>
        /// <para>
        /// Anchored to the parent's top-right so <c>SafeAreaFitter</c> insets it: in landscape the
        /// cutout eats exactly one of the two top corners.
        /// </para>
        /// <para>
        /// <b>A non-finite dp field leaves the prefab's authored layout alone</b> rather than writing
        /// it through. These are Inspector doors — the owner is meant to tune them on a device — and
        /// a NaN reaching <c>sizeDelta</c> is a <c>RectTransform</c> that never renders again, which
        /// here is a pause icon that cannot be found on a run that cannot be paused.
        /// </para>
        /// </remarks>
        private void Place()
        {
            if (_icon == null)
            {
                return;
            }

            if (!IsUsableSize(_iconSizeDp) ||
                !IsUsableMargin(_iconMarginDp.x) ||
                !IsUsableMargin(_iconMarginDp.y))
            {
                return;
            }

            if (_icon.transform is not RectTransform rect)
            {
                return;
            }

            float pxPerDp = PixelsPerDp();

            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(_iconSizeDp, _iconSizeDp) * pxPerDp;
            rect.anchoredPosition = new Vector2(-_iconMarginDp.x * pxPerDp, -_iconMarginDp.y * pxPerDp);
        }

        private static bool IsUsableSize(float dp) => float.IsFinite(dp) && dp > 0f;

        private static bool IsUsableMargin(float dp) => float.IsFinite(dp) && dp >= 0f;

        /// <summary>
        /// How many canvas units one dp is worth — <c>HudPresenter.PixelsPerDp</c>, for its reason.
        /// </summary>
        private float PixelsPerDp()
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            return StickShaper.PixelsPerDp(Screen.dpi) / scale;
        }

        /// <remarks>
        /// Removed before it is added, and with a named method rather than a lambda, so that a
        /// component injected twice — which VContainer does not do and a test does — reports one tap
        /// once rather than once per injection. <c>OfferCard</c>'s trick, for its reason.
        /// </remarks>
        private static void Wire(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(handler);
            button.onClick.AddListener(handler);
        }

        private static void Unwire(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(handler);
            }
        }
    }
}
