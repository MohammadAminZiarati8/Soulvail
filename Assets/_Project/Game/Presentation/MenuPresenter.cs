using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
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
    /// The home screen (UI-11e): the picked place, the picked class, <c>Play</c>, and a tab bar.
    /// <c>Play</c> starts a run with both picks in one tap; <c>Continue</c> records the run most
    /// recently written — the one <c>SavedRun</c> holds (M6-11a) — and loads the Run scene. See
    /// AR §3 — this is presentation: it decides nothing about the run beyond which mode, which class
    /// and which seed, and all three are choices a player makes, not outcomes a simulation computes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The picks resolve and never fail</b> (UI-11e rule 1): <see cref="PickedMode"/> and
    /// <see cref="PickedCharacter"/> read the profile's selection and fall back to the first open
    /// place and the first owned class when it names one that is gone, locked or not on offer. A fresh
    /// profile therefore plays the Jungle with the Ranger.
    /// </para>
    /// <para>
    /// <b><c>Play</c> is the one method that starts a fresh run from this screen</b>, and the class
    /// page's Choose is the other (M5-07). The seed is wall-clock derived, because no mode supplies
    /// one yet. A <c>Continue</c> chooses none of the three: mode, class and seed all come off the
    /// snapshot (M2-14b rule 7).
    /// </para>
    /// <para>
    /// <b><c>Play</c> with a save present does not delete it</b>: the new run's opening write
    /// overwrites the file on its first frame (M2-14a rule 1). A confirmation before abandoning a deep
    /// run is M8-02's.
    /// </para>
    /// <para>
    /// <b>Every string on screen is a <see cref="LocKey"/></b> (M3-14a rule 8). The Heroes tab and the
    /// class card open the hero grid (UI-11f); the Places tab and the place card do nothing until
    /// UI-11g.
    /// </para>
    /// </remarks>
    public sealed class MenuPresenter : MonoBehaviour
    {
        /// <summary>The game's name. Shared with <c>Boot.unity</c>'s splash, which nothing resolves.</summary>
        private static readonly LocKey TitleKey = new LocKey("ui.app.title");

        /// <summary>The button that starts a new run with the picked place and class.</summary>
        private static readonly LocKey PlayKey = new LocKey("ui.menu.play");

        /// <summary>The button that resumes the run on disk (M3-07b).</summary>
        private static readonly LocKey ContinueKey = new LocKey("ui.menu.continue");

        /// <summary>"Best: stage {0}" under the place card.</summary>
        private static readonly LocKey BestKey = new LocKey("ui.home.best");

        /// <summary>Layout, in dp (UI-06b). The buttons are never under 240 × 56; the plate keeps its sprite's ratio.</summary>
        public const float ButtonWidthDp = 260f;
        public const float ButtonHeightDp = 60f;
        public const float ButtonGapDp = 16f;
        public const float ButtonTextDp = 22f;
        public const float PlateWidthDp = 320f;
        public const float PlateRatio = 331f / 1000f;
        public const float PlateInsetDp = 28f;
        public const float TitleDp = 40f;

        /// <summary>Home layout (UI-11e), in dp up from the safe area's bottom edge.</summary>
        public const float CardWidthDp = 320f;
        public const float PlaceCardBottomDp = 440f;
        public const float PlaceCardHeightDp = 108f;
        public const float ClassCardBottomDp = 232f;
        public const float ClassCardHeightDp = 192f;
        public const float PrimaryCenterDp = 186f;
        public const float CardNameDp = 22f;
        public const float CardBestDp = 16f;

        private const int FallbackPreviewPx = 512;

        [Tooltip("The Play button (once Descend): starts a run with the picked place and class.")]
        [SerializeField] private Button _descend;

        /// <summary>
        /// Offered only when there is a run to continue, by being switched off entirely rather than
        /// merely made non-interactable: a greyed-out <c>Continue</c> on a fresh install advertises
        /// a feature the player cannot use and cannot find out how to.
        /// </summary>
        [SerializeField] private Button _continue;

        [Tooltip("The game's name over the menu. Written from ui.app.title on enable.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("The Play button's label, written from ui.menu.play.")]
        [SerializeField] private TMP_Text _descendLabel;

        [Tooltip("The Continue button's label, written from ui.menu.continue.")]
        [SerializeField] private TMP_Text _continueLabel;

        [Tooltip("The class page the class card and the Heroes tab open (M5-07).")]
        [SerializeField] private ClassSelectPresenter _classSelect;

        [Header("Home (UI-11e)")]
        [Tooltip("The place card: tapping it opens the Places tab.")]
        [SerializeField] private Button _placeCard;

        [SerializeField] private TMP_Text _placeName;

        [Tooltip("\"Best: stage N\", off when the best is 0.")]
        [SerializeField] private TMP_Text _placeBest;

        [Tooltip("The class card: tapping it opens the Heroes tab.")]
        [SerializeField] private Button _classCard;

        [Tooltip("The picked class's body, drawn from CharacterPreview's texture.")]
        [SerializeField] private RawImage _classPreview;

        [SerializeField] private TMP_Text _className;

        [SerializeField] private TabBar _tabs;

        [Tooltip("The Heroes tab's grid (UI-11f), a child of Safe under the tab bar. Off until the tab is tapped.")]
        [SerializeField] private HeroGrid _heroes;

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
        private ContentCatalog _catalog;
        private ProfileStore _profile;
        private ClassSelectRoster _roster;
        private CharacterPreview _preview;
        private CharacterLookBook _looks;
        private Canvas _canvas;
        private bool _starting;
        private bool _bodyDirty;
        private bool _classSelectWasOpen;
        private bool _hasSave;
        private bool _fitted;
        private int _fitWidth;
        private int _fitHeight;
        private float _fitDpi;
        private Rect _fitSafe;
        private float _fitPx;

        /// <summary>The place the home screen shows and <c>Play</c> starts, as of the last <see cref="Draw"/>.</summary>
        public ContentId SelectedMode { get; private set; }

        /// <summary>The class the home screen shows and <c>Play</c> starts, as of the last <see cref="Draw"/>.</summary>
        public ContentId SelectedCharacter { get; private set; }

        /// <param name="localizer">
        /// What turns this screen's keys into words (M3-14a rule 8). Resolved from <c>BootScope</c>.
        /// </param>
        /// <param name="catalog">What places and classes exist. Null leaves the cards undrawn.</param>
        /// <param name="profile">The picks, the bests and what is owned; <c>Play</c> saves the picks.</param>
        /// <param name="roster">Which classes are on offer. None means every class.</param>
        /// <param name="preview">The 3D stage the class card shows. Null leaves the card without a body.</param>
        /// <param name="looks">The class → body table the preview stands from.</param>
        /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
        [Inject]
        public void Construct(
            PendingRun pending,
            SavedRun saved,
            SceneLoader loader,
            ILocalizer localizer,
            ContentCatalog catalog = null,
            ProfileStore profile = null,
            ClassSelectRoster roster = null,
            CharacterPreview preview = null,
            CharacterLookBook looks = null)
        {
            _pending = pending ?? throw new ArgumentNullException(nameof(pending));
            _saved = saved ?? throw new ArgumentNullException(nameof(saved));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _catalog = catalog;
            _profile = profile;
            _roster = roster;
            _preview = preview;
            _looks = looks;
        }

        /// <summary>
        /// The place a run starts in: the profile's pick if it is listed and open, otherwise the first
        /// listed open place, otherwise the catalog's first (UI-11e rule 1).
        /// </summary>
        /// <exception cref="InvalidOperationException">The catalog holds no modes.</exception>
        public static ContentId PickedMode(ContentCatalog catalog, in PlayerProfile profile)
        {
            ContentId picked = profile.SelectedModeId;

            if (picked.Value is not null
                && catalog.TryGetMode(picked, out ModeSpec chosen)
                && chosen.IsListed
                && PlaceUnlocks.IsUnlocked(picked, profile, catalog))
            {
                return picked;
            }

            IReadOnlyList<ModeSpec> modes = catalog.Modes;

            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i].IsListed && PlaceUnlocks.IsUnlocked(modes[i].Id, profile, catalog))
                {
                    return modes[i].Id;
                }
            }

            if (modes.Count == 0)
            {
                throw new InvalidOperationException(
                    "The content catalog holds no modes, so a run has no mode to start in. Add a " +
                    "ModeDefinition to BootScope's mode list.");
            }

            return modes[0].Id;
        }

        /// <summary>
        /// The class a run starts as: the profile's pick if it is on offer and owned, otherwise the
        /// first owned class on offer (UI-11e rule 1).
        /// </summary>
        /// <exception cref="InvalidOperationException">No class is on offer.</exception>
        public static ContentId PickedCharacter(ContentCatalog catalog, ClassSelectRoster roster, in PlayerProfile profile)
        {
            IReadOnlyList<CharacterSpec> shown = roster is null ? catalog.Characters : roster.ShownFrom(catalog);
            ContentId picked = profile.SelectedCharacterId;

            for (int i = 0; i < shown.Count; i++)
            {
                if (shown[i].Id == picked && ClassUnlocks.IsUnlocked(shown[i].Id, profile, catalog))
                {
                    return picked;
                }
            }

            for (int i = 0; i < shown.Count; i++)
            {
                if (ClassUnlocks.IsUnlocked(shown[i].Id, profile, catalog))
                {
                    return shown[i].Id;
                }
            }

            if (shown.Count == 0)
            {
                throw new InvalidOperationException("No class is on offer, so a run has nobody to start as.");
            }

            return shown[0].Id;
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
                    $"{nameof(MenuPresenter)} has no {nameof(Button)} assigned. Drag the Play " +
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
                    "the class card has no screen to open.");
            }

            _descend.onClick.AddListener(Play);
            _continue.onClick.AddListener(Continue);
            AddListener(_placeCard, OpenPlaces);
            AddListener(_classCard, OpenHeroes);

            _classSelect.Picked += OnPicked;

            if (_heroes != null)
            {
                if (_catalog != null)
                {
                    _heroes.Configure(_catalog, _roster, _localizer, _looks);
                }

                _heroes.Tapped += OnHeroTapped;
                _heroes.Hide();
            }

            if (_tabs != null)
            {
                _tabs.Selected += OnTab;
                _tabs.Configure(_localizer);
                _tabs.Show(Tab.Play);
            }

            // Written on every enable: this is the screen the app comes back to after a death, and
            // drawing it at the moment it goes on screen is the only reading that is true when the
            // player sees it.
            Write(_title, TitleKey);
            Write(_descendLabel, PlayKey);
            Write(_continueLabel, ContinueKey);

            _continue.gameObject.SetActive(_saved.IsPresent);

            _hasSave = _saved.IsPresent;
            _starting = false;
            _fitted = false;

            Draw();
            Refit(Screen.width, Screen.height, Screen.dpi, Screen.safeArea);
        }

        /// <summary>
        /// Resolves the picks and draws the two cards from the profile as it stands (UI-11e rules 1–3).
        /// Does nothing without a catalog and a profile.
        /// </summary>
        public void Draw()
        {
            if (_catalog is null || _profile is null)
            {
                return;
            }

            PlayerProfile profile = _profile.Current;

            SelectedMode = PickedMode(_catalog, profile);
            SelectedCharacter = PickedCharacter(_catalog, _roster, profile);

            ModeSpec mode = _catalog.Mode(SelectedMode);
            int best = profile.BestStage(SelectedMode);

            if (_placeName != null)
            {
                _placeName.text = _localizer.Get(mode.NameKey);
            }

            if (_placeBest != null)
            {
                _placeBest.gameObject.SetActive(best > 0);
                _placeBest.text = best > 0 ? _localizer.Format(BestKey, best) : string.Empty;
            }

            if (_className != null)
            {
                _className.text = _localizer.Get(_catalog.Character(SelectedCharacter).NameKey);
            }

            _bodyDirty = true;
        }

        /// <summary>
        /// Late, so <c>CanvasScaler</c> and <c>SafeAreaFitter</c> have both taken this frame's screen in
        /// their own <c>Update</c> before the menu reads the scale (UI-08d rule 2).
        /// </summary>
        private void LateUpdate()
        {
            Refit(Screen.width, Screen.height, Screen.dpi, Screen.safeArea);

            // The class page shares the one preview stage and puts it down when it closes, so the
            // card stands its body again on the frame after. Late, because ClassSelect hides the
            // stage in its own Start.
            bool open = _classSelect != null && _classSelect.IsOpen;

            if (_classSelectWasOpen && !open)
            {
                _bodyDirty = true;
            }

            _classSelectWasOpen = open;

            if (_bodyDirty && !open)
            {
                _bodyDirty = false;
                ShowBody();
            }
        }

        private void ShowBody()
        {
            if (_preview == null || _looks == null || _classPreview == null || SelectedCharacter.Value is null)
            {
                return;
            }

            float scale = _classPreview.canvas != null && _classPreview.canvas.scaleFactor > 0f ? _classPreview.canvas.scaleFactor : 1f;
            Vector2 size = _classPreview.rectTransform.rect.size * scale;
            bool usable = size.x > 1f && size.y > 1f && float.IsFinite(size.x) && float.IsFinite(size.y);

            _preview.Resize(
                usable ? Mathf.RoundToInt(size.x) : FallbackPreviewPx,
                usable ? Mathf.RoundToInt(size.y) : FallbackPreviewPx);
            _classPreview.texture = _preview.Texture;
            _preview.Show(_looks.For(SelectedCharacter));
        }

        /// <summary>
        /// Lays the menu out again if the screen, its density or its safe area moved since the last
        /// time — checked once a frame, and nothing at all is done when none did (UI-08d rule 2).
        /// </summary>
        /// <returns>Whether it laid out.</returns>
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
        /// Places the pieces in dp, <paramref name="px"/> canvas units to the dp (UI-06b, UI-11e). The
        /// background stretches over the whole canvas, notch strip included (UI-08d rule 1); the rest
        /// is anchored to the safe area. From the top: the title plate on the upper sixth's centre
        /// line; from the bottom: the tab bar, the buttons, the class card and the place card.
        /// </summary>
        /// <remarks>
        /// <b>With a save, Continue is the green primary and Play the blue secondary below it; with
        /// none, Play is the green primary alone</b> (UI-11e rule 4). The decision is only which button
        /// holds which place and which sprites it wears — the click handlers are not touched.
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

            PlaceButton(_continue, _continueLabel, _greenButton, _greenPressed, PrimaryCenterDp, px);

            if (hasSave)
            {
                PlaceButton(_descend, _descendLabel, _blueButton, _bluePressed, PrimaryCenterDp - (ButtonHeightDp + ButtonGapDp), px);
            }
            else
            {
                PlaceButton(_descend, _descendLabel, _greenButton, _greenPressed, PrimaryCenterDp, px);
            }

            LayoutCards(px);

            if (_tabs != null)
            {
                _tabs.Layout(px);
            }

            if (_heroes != null)
            {
                _heroes.Layout(px);
            }
        }

        private void LayoutCards(float px)
        {
            if (_placeCard != null)
            {
                Place((RectTransform)_placeCard.transform, new Vector2(0.5f, 0f), new Vector2(CardWidthDp * px, PlaceCardHeightDp * px), (PlaceCardBottomDp + (PlaceCardHeightDp * 0.5f)) * px);
            }

            if (_placeName != null)
            {
                Stretch(_placeName, px, 0.5f, 1f, CardNameDp, TextAlignmentOptions.Bottom);
            }

            if (_placeBest != null)
            {
                Stretch(_placeBest, px, 0f, 0.5f, CardBestDp, TextAlignmentOptions.Top);
            }

            if (_classCard != null)
            {
                Place((RectTransform)_classCard.transform, new Vector2(0.5f, 0f), new Vector2(CardWidthDp * px, ClassCardHeightDp * px), (ClassCardBottomDp + (ClassCardHeightDp * 0.5f)) * px);
            }

            if (_classPreview != null)
            {
                RectTransform rect = _classPreview.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(0f, 36f * px);
                rect.offsetMax = Vector2.zero;
            }

            if (_className != null)
            {
                RectTransform rect = _className.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.offsetMin = new Vector2(8f * px, 0f);
                rect.offsetMax = new Vector2(-8f * px, 36f * px);
                _className.fontSize = CardNameDp * px;
                _className.alignment = TextAlignmentOptions.Center;
                _className.enableAutoSizing = false;
            }
        }

        /// <summary>A label over a band of its card, between two heights as fractions of the card.</summary>
        private static void Stretch(TMP_Text label, float px, float yMin, float yMax, float sizeDp, TextAlignmentOptions alignment)
        {
            RectTransform rect = label.rectTransform;
            rect.anchorMin = new Vector2(0f, yMin);
            rect.anchorMax = new Vector2(1f, yMax);
            rect.offsetMin = new Vector2(8f * px, 0f);
            rect.offsetMax = new Vector2(-8f * px, 0f);
            label.fontSize = sizeDp * px;
            label.alignment = alignment;
            label.enableAutoSizing = false;
            label.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>A button's centre is <paramref name="yDp"/> up from the safe area's bottom edge.</summary>
        private static void PlaceButton(Button button, TMP_Text label, Sprite face, Sprite pressed, float yDp, float px)
        {
            if (button == null)
            {
                return;
            }

            Place((RectTransform)button.transform, new Vector2(0.5f, 0f), new Vector2(ButtonWidthDp * px, ButtonHeightDp * px), yDp * px);

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

        /// <remarks>
        /// <b>A missing label is silent and a missing localizer falls back to the key</b>, which is
        /// deliberately softer than the <c>MissingReferenceException</c>s above: a menu with no
        /// <c>Button</c> has no way into a run, where one with no <em>label</em> can still be used.
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
                _descend.onClick.RemoveListener(Play);
            }

            if (_continue != null)
            {
                _continue.onClick.RemoveListener(Continue);
            }

            RemoveListener(_placeCard, OpenPlaces);
            RemoveListener(_classCard, OpenHeroes);

            if (_classSelect != null)
            {
                _classSelect.Picked -= OnPicked;
            }

            if (_heroes != null)
            {
                _heroes.Tapped -= OnHeroTapped;
            }

            if (_tabs != null)
            {
                _tabs.Selected -= OnTab;
            }
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button != null)
            {
                button.onClick.AddListener(handler);
            }
        }

        private static void RemoveListener(Button button, UnityEngine.Events.UnityAction handler)
        {
            if (button != null)
            {
                button.onClick.RemoveListener(handler);
            }
        }

        private void OnTab(Tab tab)
        {
            switch (tab)
            {
                case Tab.Heroes:
                    OpenHeroes();
                    break;
                case Tab.Play:
                    ShowHome();
                    break;
                case Tab.Places:
                    OpenPlaces();
                    break;
            }
        }

        /// <summary>The Heroes tab and the class card: the grid of every class on the roster (UI-11f).</summary>
        private void OpenHeroes()
        {
            if (_heroes == null || _catalog is null || _profile is null)
            {
                return;
            }

            _heroes.Show(_profile.Current, SelectedCharacter);
            _heroes.Layout(PixelsPerDp(Screen.dpi));

            if (_tabs != null)
            {
                _tabs.Show(Tab.Heroes);
            }
        }

        /// <summary>The home screen proper: the grid down and the Play tab lit.</summary>
        private void ShowHome()
        {
            if (_heroes != null)
            {
                _heroes.Hide();
            }

            if (_tabs != null)
            {
                _tabs.Show(Tab.Play);
            }
        }

        /// <summary>A hero card was tapped: the character page opens on that class.</summary>
        private void OnHeroTapped(ContentId characterId)
        {
            if (_classSelect != null && !_classSelect.IsOpen)
            {
                _classSelect.Open(characterId);
            }
        }

        /// <summary>Choose on the page saved a pick: home again, drawn with it (UI-11f rule 5).</summary>
        private void OnPicked(ContentId characterId)
        {
            ShowHome();
            Draw();
        }

        /// <summary>The Places tab and the place card: nothing, until UI-11g.</summary>
        private void OpenPlaces()
        {
        }

        /// <summary>
        /// Starts a fresh run with the picked place and class (UI-11e rule 4), and saves the picks.
        /// </summary>
        /// <remarks>
        /// <c>async void</c> is right for what is effectively an event handler: every path out of the
        /// await is handled here. The latch goes down before anything else, and both buttons go dead
        /// with it, so a second touch during the load finds nothing to start.
        /// </remarks>
        private async void Play()
        {
            if (_starting || _catalog is null || _profile is null)
            {
                return;
            }

            _starting = true;
            SetButtons(false);

            try
            {
                _profile.Pick(SelectedCharacter, SelectedMode);
                _pending.Set(SelectedMode, SelectedCharacter, Environment.TickCount);

                await _loader.LoadAsync(SceneLoader.Run);
            }
            catch (Exception exception)
            {
                _starting = false;
                SetButtons(true);
                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// Records the run on disk as the one to play, and loads the scene it is played in.
        /// </summary>
        /// <remarks>
        /// The mode, the class and the seed all come off the snapshot rather than being chosen here,
        /// which is what makes a resume unable to disagree with the run it is resuming. <b>Nothing is
        /// deleted, here or anywhere on this path</b> (M2-14b rule 10): a run interrupted twice inside
        /// one stage resumes twice, rewound to the top of its stage each time.
        /// </remarks>
        private async void Continue()
        {
            if (_starting)
            {
                return;
            }

            _starting = true;
            SetButtons(false);

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
                _starting = false;
                SetButtons(true);
                Debug.LogException(exception, this);
            }
        }

        private void SetButtons(bool value)
        {
            _continue.interactable = value;
            _descend.interactable = value;
        }
    }
}
