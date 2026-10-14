using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, so the Menu scene's reference to this
// component would silently deserialise as null (M0-11).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The menu, which is two buttons — one of them usually hidden. Tapping <c>Descend</c> opens
    /// the class-select screen; tapping <c>Continue</c> records the run most recently written — the
    /// one <c>SavedRun</c> holds, which boot seeds and every run's writer keeps current (M6-11a) —
    /// and loads the Run scene. See AR §3 — this is presentation: it decides nothing about the run
    /// beyond which mode, which class and which seed, and all three are choices a player makes, not
    /// outcomes a simulation computes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>Descend</c> stopped starting a run at M5-07 (rule 1).</b> It used to set
    /// <c>PendingRun</c> from the catalog's first mode and first class and await the load in one
    /// method — keeping the mode and losing the class, which is why six merged tasks shipped a
    /// second class nobody could reach. That write now belongs to the card the player taps, and the
    /// double-tap guard, the <c>async void</c> and the <c>catch</c> that gives the buttons back went
    /// with it rather than being copied, so there is still one method in the project that starts a
    /// fresh run. This file no longer reads the catalog at all.
    /// </para>
    /// <para>
    /// The seed is wall-clock derived, because no mode supplies one yet — Daily mode is where a
    /// seed stops being arbitrary. A <c>Continue</c> chooses none of the three: mode, class and
    /// seed all come off the snapshot (M2-14b rule 7), so a resumed Gravecaller run resumes as a
    /// Gravecaller without <see cref="ClassSelectPresenter"/> having an opinion.
    /// </para>
    /// <para>
    /// <b><c>Descend</c> with a save present does not delete it</b>, and the absence of that line is
    /// deliberate. The new run's opening write overwrites the file on its first frame (M2-14a rule
    /// 1), so the abandonment is recorded by the thing that already records every run — and the
    /// alternative would put an <c>ISaveStore</c> and a fire-and-forget delete into a presenter.
    /// A confirmation dialog before abandoning a deep run is real UX and it is M8-02's, with the
    /// string M6-10 owns. <b>Opening the class-select screen is not abandoning anything</b>: nothing
    /// is written until a card is tapped, and <c>Back</c> costs the player nothing.
    /// </para>
    /// <para>
    /// <b>The three strings on screen stopped being raw English at M3-14a</b> (rule 8). They were
    /// typed into <c>Menu.unity</c> from M0-17, and the ROADMAP's parking-lot line said they would
    /// become <see cref="LocKey"/>s <em>"when <c>ILocalizer</c> and the English tables land"</em> —
    /// which is this task, three milestones early. <b>Three, not the two that line names</b>:
    /// <c>Continue</c> was added by M3-07b's resume flow and counted by nobody. So M6-10 inherits
    /// no English typed into this scene, and AR §11.5's <em>"no raw user-facing string anywhere"</em>
    /// is a thing a test asserts here rather than an aspiration. Showing anything <em>about</em> the
    /// saved run on the button — "Stage 9, 12 minutes ago" — needs a format as well as a key, and is
    /// still M8-02's.
    /// </para>
    /// </remarks>
    public sealed class MenuPresenter : MonoBehaviour
    {
        /// <summary>The game's name. Shared with <c>Boot.unity</c>'s splash, which nothing resolves.</summary>
        private static readonly LocKey TitleKey = new LocKey("ui.app.title");

        /// <summary>The button that starts a new run.</summary>
        private static readonly LocKey DescendKey = new LocKey("ui.menu.descend");

        /// <summary>The button that resumes the run on disk (M3-07b).</summary>
        private static readonly LocKey ContinueKey = new LocKey("ui.menu.continue");

        /// <summary>Layout, in dp (UI-06b). The buttons are never under 240 × 56; the plate keeps its sprite's ratio.</summary>
        public const float ButtonWidthDp = 260f;
        public const float ButtonHeightDp = 60f;
        public const float ButtonGapDp = 16f;
        public const float ButtonTextDp = 22f;
        public const float PlateWidthDp = 320f;
        public const float PlateRatio = 331f / 1000f;
        public const float PlateInsetDp = 28f;
        public const float TitleDp = 40f;

        [SerializeField] private Button _descend;

        /// <summary>
        /// Offered only when there is a run to continue, by being switched off entirely rather than
        /// merely made non-interactable: a greyed-out <c>Continue</c> on a fresh install advertises
        /// a feature the player cannot use and cannot find out how to.
        /// </summary>
        [SerializeField] private Button _continue;

        [Tooltip("The game's name over the menu. Written from ui.app.title on enable, so the " +
                 "scene's own value is a placeholder — see the class remarks.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("The Descend button's label, written from ui.menu.descend.")]
        [SerializeField] private TMP_Text _descendLabel;

        [Tooltip("The Continue button's label, written from ui.menu.continue.")]
        [SerializeField] private TMP_Text _continueLabel;

        [Tooltip("The class-select screen Descend opens (M5-07). Dressed in Menu.unity rather " +
                 "than on this object, because the screen is its own prefab — PausePresenter " +
                 "reaches the Skills screen the same way and for the same reason.")]
        [SerializeField] private ClassSelectPresenter _classSelect;

        [Header("Dressing (UI-06b)")]
        [Tooltip("CharacterPage_Background, a child of the canvas rather than of Safe and stretched " +
                 "over the whole of it, so the notch strip is painted too (UI-08d).")]
        [SerializeField] private Image _background;

        [Tooltip("Menu_TitlePlate; the title sits on it.")]
        [SerializeField] private Image _titlePlate;

        [SerializeField] private Sprite _greenButton;
        [SerializeField] private Sprite _greenPressed;
        [SerializeField] private Sprite _blueButton;
        [SerializeField] private Sprite _bluePressed;

        private PendingRun _pending;
        private SavedRun _saved;
        private SceneLoader _loader;
        private ILocalizer _localizer;
        private Canvas _canvas;
        private bool _hasSave;
        private bool _fitted;
        private int _fitWidth;
        private int _fitHeight;
        private float _fitDpi;
        private Rect _fitSafe;
        private float _fitPx;

        /// <param name="localizer">
        /// What turns this screen's three keys into words (M3-14a rule 8). Resolved from
        /// <c>BootScope</c>, which is why the localizer is registered there rather than in
        /// <c>RunScope</c> — <b>the Menu needs it and has no run</b> (rule 4).
        /// </param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// <b>The <c>ContentCatalog</c> left this signature at M5-07</b>, with the two methods that
        /// read it. Nothing on this screen asks what content exists any more — the class-select
        /// screen does, and it resolves its own — and an injected dependency nobody reads is a
        /// claim about a file that has stopped being true.
        /// </remarks>
        [Inject]
        public void Construct(
            PendingRun pending,
            SavedRun saved,
            SceneLoader loader,
            ILocalizer localizer)
        {
            _pending = pending ?? throw new ArgumentNullException(nameof(pending));
            _saved = saved ?? throw new ArgumentNullException(nameof(saved));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        }

        /// <remarks>
        /// Subscribed here and dropped in <see cref="OnDisable"/> rather than held for the object's
        /// life: AR §8's rule for views, and the reason a disabled menu cannot start a run.
        /// </remarks>
        private void OnEnable()
        {
            if (_descend == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(MenuPresenter)} has no {nameof(Button)} assigned. Drag the Descend " +
                    "button in this scene onto its Descend field — without it the menu has no way " +
                    "into a run.");
            }

            if (_continue == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(MenuPresenter)} has no Continue {nameof(Button)} assigned. Drag the " +
                    "Continue button in this scene onto its Continue field — without it a run that " +
                    "survived an app kill can never be resumed, and nothing would say so.");
            }

            if (_classSelect == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(MenuPresenter)} has no {nameof(ClassSelectPresenter)} assigned. Drag " +
                    "the ClassSelect object in this scene onto its Class Select field — without it " +
                    "Descend has no screen to open and the menu has no way into a run.");
            }

            _descend.onClick.AddListener(Descend);
            _continue.onClick.AddListener(Continue);

            // Written on every enable rather than once at injection, for the reason the Continue
            // button's visibility is decided here: this is the screen the app comes back to after a
            // death, and drawing it at the moment it goes on screen is the only reading that is true
            // when the player sees it. Three labels, and none of them typed into the scene (rule 8).
            Write(_title, TitleKey);
            Write(_descendLabel, DescendKey);
            Write(_continueLabel, ContinueKey);

            // Decided here, on every enable, rather than once at injection. The Menu is the screen
            // the app comes back to after a death, so the answer changes while this object is
            // alive — and reading it at the moment the menu goes on screen is the only reading that
            // is true when the player sees it.
            _continue.gameObject.SetActive(_saved.IsPresent);

            _hasSave = _saved.IsPresent;
            _fitted = false;
            Refit(Screen.width, Screen.height, Screen.dpi, Screen.safeArea);
        }

        /// <remarks>
        /// Late, so <c>CanvasScaler</c> and <c>SafeAreaFitter</c> have both taken this frame's screen in
        /// their own <c>Update</c> before the menu reads the scale (UI-08d rule 2).
        /// </remarks>
        private void LateUpdate()
        {
            Refit(Screen.width, Screen.height, Screen.dpi, Screen.safeArea);
        }

        /// <summary>
        /// Lays the menu out again if the screen, its density or its safe area moved since the last
        /// time — checked once a frame, and nothing at all is done when none did (UI-08d rule 2,
        /// UI-07a finding 5: the menu used to lay out once, on enable).
        /// </summary>
        /// <returns>Whether it laid out.</returns>
        /// <remarks>
        /// The canvas's scale is compared too, through the px per dp it gives: <c>CanvasScaler</c> can
        /// take its first reading of a screen a frame after this object's <c>OnEnable</c>.
        /// </remarks>
        public bool Refit(int width, int height, float dpi, Rect safeArea)
        {
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            float px = PixelsPerDp(dpi);

            if (_fitted && width == _fitWidth && height == _fitHeight && dpi == _fitDpi && safeArea == _fitSafe && px == _fitPx)
            {
                return false;
            }

            _fitted = true;
            _fitWidth = width;
            _fitHeight = height;
            _fitDpi = dpi;
            _fitSafe = safeArea;
            _fitPx = px;

            Layout(px, _hasSave);

            return true;
        }

        /// <summary>
        /// Places the pieces in dp, <paramref name="px"/> canvas units to the dp (UI-06b). The
        /// background stretches over the whole canvas, notch strip included (UI-08d rule 1); the rest
        /// is anchored to the safe area, so nothing here needs the rect's size: the plate sits on the
        /// upper third's centre line, the primary button on the middle and the secondary under it.
        /// </summary>
        /// <remarks>
        /// <b>With a save, Continue is the green primary and Descend the blue secondary below it; with
        /// none, Descend is the green primary alone</b> (rule 3). The decision is only which button holds
        /// which place and which sprites it wears — the click handlers are not touched.
        /// </remarks>
        public void Layout(float px, bool hasSave)
        {
            px = px > 0f ? px : 1f;
            _hasSave = hasSave;

            if (_background != null)
            {
                RectTransform rect = _background.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            if (_titlePlate != null)
            {
                Place(_titlePlate.rectTransform, new Vector2(0.5f, 5f / 6f), new Vector2(PlateWidthDp * px, PlateWidthDp * PlateRatio * px), 0f);
                Dress(_titlePlate, _titlePlate.sprite);
            }

            if (_title != null)
            {
                RectTransform rect = _title.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(PlateInsetDp * px, 0f);
                rect.offsetMax = new Vector2(-PlateInsetDp * px, 0f);
                _title.fontSize = TitleDp * px;
                _title.alignment = TextAlignmentOptions.Center;
                _title.enableAutoSizing = false;
                _title.overflowMode = TextOverflowModes.Ellipsis;
            }

            PlaceButton(_continue, _continueLabel, _greenButton, _greenPressed, 0f, px);

            if (hasSave)
            {
                PlaceButton(_descend, _descendLabel, _blueButton, _bluePressed, -(ButtonHeightDp + ButtonGapDp), px);
            }
            else
            {
                PlaceButton(_descend, _descendLabel, _greenButton, _greenPressed, 0f, px);
            }
        }

        private static void PlaceButton(Button button, TMP_Text label, Sprite face, Sprite pressed, float yDp, float px)
        {
            if (button == null)
            {
                return;
            }

            Place((RectTransform)button.transform, new Vector2(0.5f, 0.5f), new Vector2(ButtonWidthDp * px, ButtonHeightDp * px), yDp * px);

            SpriteState sprites = button.spriteState;
            sprites.pressedSprite = pressed;
            button.spriteState = sprites;

            if (button.targetGraphic is Image image)
            {
                image.sprite = face;
                image.color = Color.white;
                Dress(image, face);
            }

            if (label != null)
            {
                RectTransform rect = label.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(8f * px, 0f);
                rect.offsetMax = new Vector2(-8f * px, 0f);
                label.fontSize = ButtonTextDp * px;
                label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = false;
            }
        }

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 size, float y)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0f, y);
        }

        /// <summary>
        /// Sliced, with the sprite's own borders scaled the way its height is: a 348 px button drawn
        /// 60 dp tall carries its 130 px ends at 60/348 of their size.
        /// </summary>
        private static void Dress(Image image, Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = sprite.rect.height / image.rectTransform.sizeDelta.y;
        }

        /// <summary>
        /// How many canvas units one dp is worth at <paramref name="dpi"/> —
        /// <c>ClassSelectPresenter.PixelsPerDp</c>, for its reason. The canvas is found once and kept.
        /// </summary>
        private float PixelsPerDp(float dpi)
        {
            if (_canvas == null)
            {
                _canvas = GetComponentInParent<Canvas>();
            }

            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;

            return StickShaper.PixelsPerDp(dpi) / scale;
        }

        /// <summary>
        /// Draws <paramref name="key"/> onto <paramref name="label"/>, if both are there.
        /// </summary>
        /// <remarks>
        /// <b>A missing label is silent and a missing localizer falls back to the key</b>, which is
        /// deliberately softer than the two <c>MissingReferenceException</c>s above it: a button with
        /// no <c>Button</c> is a menu with no way into a run, where a button with no <em>label</em>
        /// is a menu the player can still use. <c>TreeViewPresenter.Resolve</c>'s answer, and
        /// <c>ToString()</c> rather than <c>Key</c> for <c>TableLocalizer.Get</c>'s reason.
        /// </remarks>
        private void Write(TMP_Text label, LocKey key)
        {
            if (label == null)
            {
                return;
            }

            label.text = _localizer is null ? key.ToString() : _localizer.Get(key);
        }

        private void OnDisable()
        {
            if (_descend != null)
            {
                _descend.onClick.RemoveListener(Descend);
            }

            if (_continue != null)
            {
                _continue.onClick.RemoveListener(Continue);
            }
        }

        /// <summary>
        /// Opens the class-select screen (M5-07 rule 1). Nothing is recorded and no scene loads.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Synchronous, and it writes nothing.</b> Until M5-07 this method set
        /// <c>PendingRun</c> from the catalog's first mode and first class and awaited the Run
        /// scene; the write, the await, the double-tap guard and the <c>catch</c> that gave the
        /// buttons back are all <see cref="ClassSelectPresenter"/>'s now, on the card's tap, because
        /// that is where the class is finally known. What is left here is a screen going up.
        /// </para>
        /// <para>
        /// <b>No double-tap guard, and its absence is the point.</b> The screen is a full-screen
        /// <see cref="CanvasGroup"/> that blocks raycasts, so a second touch lands on it rather than
        /// on this button, and <c>Open</c> called twice draws the same cards twice. The guard that
        /// matters is on the thing that starts a run, which is one screen along.
        /// </para>
        /// </remarks>
        private void Descend()
        {
            if (_classSelect == null || _classSelect.IsOpen)
            {
                return;
            }

            _classSelect.Open();
        }

        /// <summary>
        /// Records the run on disk as the one to play, and loads the scene it is played in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The same path <see cref="Descend"/> takes, differing in one call.</b> The mode, the
        /// class and the seed all come off the snapshot rather than being chosen here, which is
        /// what makes a resume unable to disagree with the run it is resuming — and is why this
        /// method reads nothing from the catalog at all.
        /// </para>
        /// <para>
        /// <b>Nothing is deleted, here or anywhere on this path</b> (M2-14b rule 10). The file
        /// survives until the next stage boundary overwrites it or death clears it, so a player
        /// interrupted twice inside one stage resumes twice. The cost is that a resumed run rewinds
        /// to the top of its stage each time, which the owner ruled acceptable at M2-00e.
        /// </para>
        /// </remarks>
        private async void Continue()
        {
            // Both buttons, not just this one: the guard below is about not starting two runs, and
            // a Descend tapped during the load would start the other kind.
            _continue.interactable = false;
            _descend.interactable = false;

            try
            {
                // Read before the await, while this object is certainly alive, and read through
                // Value rather than guarded here — the button is only on screen because IsPresent
                // was true, so a throw at this line is a wiring fault worth hearing about.
                _pending.Resume(_saved.Value);

                await _loader.LoadAsync(SceneLoader.Run);
            }
            catch (Exception exception)
            {
                _continue.interactable = true;
                _descend.interactable = true;
                Debug.LogException(exception, this);
            }
        }

        // FirstModeId and FirstCharacterId left this file at M5-07. The first is
        // ClassSelectPresenter.FirstModeId, unchanged and beside the write it feeds — GD §4.5's
        // rule that no code may assume Descent is enforced in the same one place it always was.
        // The second is gone outright: the class is a tap now, which is the whole task.
    }
}
