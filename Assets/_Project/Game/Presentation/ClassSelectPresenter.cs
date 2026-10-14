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

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and ClassSelect.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The class-select screen: one class at a time, and the button that decides which run the
    /// player gets. CH §3, §6; GD §4.5, §16.1; AR §3; UI-05c.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It decides nothing about the run beyond which class</b> — AR §3, and
    /// <see cref="MenuPresenter"/>'s own argument: mode, class and seed are choices a player makes,
    /// not outcomes a simulation computes. <b>The mode is still the catalog's first and this screen
    /// invents no mode-select</b>. GD §4.5's rule is that a mode is a data object and no code may
    /// assume Descent, so <see cref="FirstModeId"/> is the same stand-in
    /// <c>MenuPresenter.FirstModeId</c> was, moved here with the write it feeds. The seed stays
    /// wall-clock derived for the reason it always was.
    /// </para>
    /// <para>
    /// <b>The tap that starts the run is here, in one method, and it was moved rather than
    /// copied.</b> The double-tap guard, the <c>async void</c> and the <c>catch</c> that gives the
    /// button back came with the write they were guarding, so there is still exactly one method in
    /// the project that starts a fresh run: <see cref="OnCardChosen"/>.
    /// </para>
    /// <para>
    /// <b>One page, an index into the roster</b> (UI-05c rule 1). The presenter owns the index and
    /// <see cref="CharacterPage"/> is told what to draw. <b>Nothing is instantiated</b> but the
    /// page's dots, once, on demand: the screen is up for the four seconds before a run and must not
    /// hitch (AR §14).
    /// </para>
    /// <para>
    /// <b>It draws the unlock gate and owns none of it</b> (M6-09b rule 1). <c>ClassUnlocks</c>
    /// decides which classes are owned and whether a price can be paid, the spec carries the price,
    /// and <see cref="ProfileStore"/> holds the balance and makes the one write. A locked class is
    /// drawn with its price rather than hidden, and a price that cannot be paid is drawn dead —
    /// M5-08a's finding: <b>a screen may not offer what the model refuses</b>.
    /// </para>
    /// <para>
    /// <b>The profile is read on open and after a purchase and on nothing else</b> (M6-09b rule 5).
    /// Stepping between classes redraws from that read, so the balance and the page never disagree
    /// about what is owned.
    /// </para>
    /// <para>
    /// <b>Buying is not picking</b> (M6-09b rule 8). The tap that spends Shards redraws the page and
    /// starts nothing; a second tap plays. <see cref="_buying"/> is what stops a double tap from
    /// being both in one <c>EventSystem</c> pass.
    /// </para>
    /// <para>
    /// <b>It is a <see cref="CanvasGroup"/> in the Menu scene, not a scene of its own</b>.
    /// <c>PausePresenter</c>'s and <c>SkillsPresenter</c>'s shape.
    /// </para>
    /// <para>
    /// <b>A <c>Continue</c> takes none of this.</b> Mode, class and seed all come off the snapshot
    /// (M2-14b rule 7), and <c>PendingRun</c> is written on the Choose tap and on no other path
    /// through this file.
    /// </para>
    /// </remarks>
    public sealed class ClassSelectPresenter : MonoBehaviour
    {
        private const float HeaderDp = 72f;
        private const float BackDp = 56f;
        private const float HeaderMarginDp = 12f;
        private const float BalanceWidthDp = 144f;
        private const float BalanceHeightDp = 44f;
        private const float BalanceIconDp = 28f;
        private const float BalanceTextDp = 18f;

        /// <summary>The preview texture's size if the image has no rect yet, in pixels.</summary>
        private const int FallbackPreviewPx = 512;

        /// <summary>
        /// The deed line for a depth deed. There is no key for a boss deed, and that is a ruling.
        /// </summary>
        private static readonly LocKey DeedStageKey = new LocKey("ui.classselect.locked.deed");

        [Tooltip("The screen, switched between alpha 0 and 1. No fade — a menu is not a fight, and " +
                 "a tween here would be the first one in the project's UI.")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("The one page: name, role, body, arrows, dots, button.")]
        [SerializeField] private CharacterPage _page;

        [Tooltip("Closes without writing anything. The menu comes back exactly as it was.")]
        [SerializeField] private Button _back;

        [Tooltip("The Shard balance's group, top-right: icon and number.")]
        [SerializeField] private RectTransform _balanceGroup;

        [SerializeField] private Image _balanceIcon;

        [Tooltip("What the player has to spend, a number in Palette.Essence. Drawn on open and after " +
                 "a purchase, and on nothing else (M6-09b rule 5).")]
        [SerializeField] private TMP_Text _balance;

        private PendingRun _pending;
        private ContentCatalog _catalog;
        private ClassSelectRoster _roster;
        private CharacterPreview _preview;
        private CharacterLookBook _looks;
        private SkillIconBook _icons;
        private readonly List<StripEntry> _strip = new List<StripEntry>(SkillStrip.MaxTiles);
        private SceneLoader _loader;
        private ILocalizer _localizer;
        private ProfileStore _profile;

        /// <summary>The classes on offer, in order, as of the last open.</summary>
        private IReadOnlyList<CharacterSpec> _shown = Array.Empty<CharacterSpec>();

        private int _index;

        /// <summary>The profile as of the last open or purchase — what every redraw reads.</summary>
        private PlayerProfile _drawn;

        /// <summary>
        /// A purchase was made in this frame. Cleared by <see cref="Update"/>, and by nothing else.
        /// </summary>
        /// <remarks>
        /// M6-09b rule 8's second latch. A purchase redraws the class as owned and live, and uGUI
        /// dispatches both taps of a double tap from one <c>EventSystem</c> pass — so without it the
        /// second tap of a double tap on a price would be a descent the player never asked for. It
        /// refuses a second purchase in the same pass for the same reason.
        /// </remarks>
        private bool _buying;

        /// <summary>
        /// A class has been chosen and the scene load has not answered yet.
        /// </summary>
        /// <remarks>
        /// The latch rather than the button's <c>interactable</c>, because the button is only the
        /// first of the two guards: uGUI dispatches both taps of a double tap from one
        /// <c>EventSystem</c> pass, and a handler invoked directly — which is what a test does —
        /// never consults <c>interactable</c> at all. <c>PausePresenter._quitting</c>'s argument.
        /// </remarks>
        private bool _descending;

        /// <summary>Whether the screen is up. Read by <see cref="MenuPresenter"/> and by the tests.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// What the player has to spend, as last drawn. Read from the profile at every draw and at
        /// no other time — rule 5.
        /// </summary>
        public int Shards { get; private set; }

        /// <summary>The roster index the page is on.</summary>
        public int Index => _index;

        /// <param name="pending">What the next run should be. Written on Choose and nowhere else.</param>
        /// <param name="catalog">What classes exist, and which mode is first. Read, never written.</param>
        /// <param name="loader">Where a chosen class goes.</param>
        /// <param name="localizer">
        /// What turns this screen's keys, and each class's, into words. Resolved from
        /// <c>BootScope</c> through <c>MenuScope</c>'s parent container (M3-14a rule 4).
        /// </param>
        /// <param name="profile">
        /// The balance, what is owned, and the one writer a purchase goes through (M6-09b rule 6).
        /// </param>
        /// <param name="roster">
        /// Which classes to show. None shows every class, which is what an empty roster says too.
        /// </param>
        /// <param name="preview">
        /// The 3D stage the page's image shows (UI-05b). <c>MenuScope</c> requires it; the fixtures
        /// that build this screen by hand may leave it out and get a page with no body.
        /// </param>
        /// <param name="looks">The class → body table the preview stands from.</param>
        /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
        /// <remarks>
        /// The Back button and the page are wired here rather than in <c>OnEnable</c>,
        /// <c>PausePresenter</c>'s reason: <c>MenuScope</c> builds its container from its own
        /// <c>Awake</c> and Unity orders no two of those. They are dropped in <see cref="OnDestroy"/>.
        /// </remarks>
        [Inject]
        public void Construct(
            PendingRun pending,
            ContentCatalog catalog,
            SceneLoader loader,
            ILocalizer localizer,
            ProfileStore profile,
            ClassSelectRoster roster = null,
            CharacterPreview preview = null,
            CharacterLookBook looks = null,
            SkillIconBook icons = null)
        {
            _pending = pending ?? throw new ArgumentNullException(nameof(pending));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));

            _roster = roster;
            _preview = preview;
            _looks = looks;
            _icons = icons;

            Wire(_back, Close);

            if (_page != null)
            {
                _page.Bind(Previous, Next, OnPrimary, Rotate);
            }
        }

        /// <exception cref="MissingReferenceException">The root, the page or the Back button is not dressed.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>PausePresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// dressed" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_root == null || _back == null || _page == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(ClassSelectPresenter)} has no root {nameof(CanvasGroup)}, no " +
                    $"{nameof(CharacterPage)} or no Back {nameof(Button)} assigned. A class-select " +
                    "screen that is only partly dressed covers the menu and then offers no way back " +
                    "or no way forward, which is indistinguishable from a crash.");
            }

            // Down whatever the prefab was left dressed as, so a screen someone was editing cannot
            // ship covering the menu — HudPresenter's argument for its death panel.
            HideScreen();
        }

        /// <summary>
        /// Lowers the purchase latch, and does nothing else — no draw and no profile read (M6-09b
        /// rules 5 and 10). <c>LevelUpPresenter._choosing</c>'s rule.
        /// </summary>
        /// <remarks>
        /// <b><see cref="_descending"/> is not cleared here</b>: it spans an awaited scene load, which
        /// is many frames, and comes down only when that load fails.
        /// </remarks>
        private void Update()
        {
            _buying = false;
        }

        /// <remarks>
        /// Explicit rather than left to the scene's teardown: the handlers are wired in
        /// <see cref="Construct"/>, so a component injected and then destroyed — which a test does —
        /// would otherwise leave a listener pointing at a dead object.
        /// </remarks>
        private void OnDestroy()
        {
            Unwire(_back, Close);
        }

        /// <summary>
        /// Puts the screen up on the first class the player owns.
        /// </summary>
        /// <remarks>
        /// Idempotent about its own state and not about the draw: reopening rebinds, which is what
        /// makes a screen closed with <c>Back</c> and opened again show live buttons rather than the
        /// dead ones a tap left behind. <b>A catalog with no classes opens an empty screen rather
        /// than throwing</b>: the Back button still works, so the player is on a blank screen they
        /// can leave instead of in a menu that raised an exception mid-tap.
        /// </remarks>
        public void Open()
        {
            if (_catalog is null)
            {
                return;
            }

            _descending = false;
            _buying = false;

            _drawn = _profile.Current;
            _shown = _roster is null ? _catalog.Characters : _roster.ShownFrom(_catalog);
            _index = FirstOwnedIndex();

            PlaceChrome();
            DrawBalance();
            DrawPage();
            ShowBody();

            IsOpen = true;

            ShowScreen();
        }

        /// <summary>
        /// Takes the screen down without choosing. Nothing is written (rule 8).
        /// </summary>
        /// <remarks>
        /// The Back button's handler, and the way <see cref="MenuPresenter"/> is given its menu back.
        /// <c>PendingRun</c> is untouched on this path.
        /// </remarks>
        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;

            HideScreen();
        }

        /// <summary>Steps to the previous class, if there is one. Does not wrap (rule 2).</summary>
        private void Previous() => Step(-1);

        /// <summary>Steps to the next class, if there is one. Does not wrap (rule 2).</summary>
        private void Next() => Step(1);

        private void Step(int delta)
        {
            int target = _index + delta;

            if (_descending || target < 0 || target >= _shown.Count)
            {
                return;
            }

            _index = target;

            DrawPage();
            ShowBody();
        }

        /// <summary>A drag on the preview turns the body and nothing else (rule 3).</summary>
        private void Rotate(float degrees)
        {
            if (_preview != null)
            {
                _preview.Rotate(degrees);
            }
        }

        /// <summary>The one button: Choose plays an owned class, Unlock buys a locked one.</summary>
        private void OnPrimary()
        {
            if (_index < 0 || _index >= _shown.Count)
            {
                return;
            }

            CharacterSpec spec = _shown[_index];

            if (ClassUnlocks.IsUnlocked(spec.Id, _drawn, _catalog))
            {
                OnCardChosen(spec.Id);
            }
            else
            {
                OnUnlockTapped(spec.Id);
            }
        }

        private int FirstOwnedIndex()
        {
            for (int i = 0; i < _shown.Count; i++)
            {
                if (ClassUnlocks.IsUnlocked(_shown[i].Id, _drawn, _catalog))
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// Draws the Shard balance: a number beside <c>Icon_SoulShard</c>, in GD §16.4's reward gold.
        /// </summary>
        private void DrawBalance()
        {
            Shards = _drawn.Shards;

            if (_balance != null)
            {
                _balance.text = _drawn.Shards.ToString();

                // The colour RunEndPresenter already pays Shards in: the number that screen showed
                // and the number this one spends look like one currency because they are.
                _balance.color = Palette.Essence;
            }
        }

        /// <summary>
        /// Draws the class at the index from the profile as last read. On open, on a step and after
        /// a purchase, and on nothing else (M6-09b rule 5).
        /// </summary>
        private void DrawPage()
        {
            if (_page == null)
            {
                return;
            }

            if (_index < 0 || _index >= _shown.Count)
            {
                _page.Clear();
                return;
            }

            CharacterSpec spec = _shown[_index];
            bool owned = ClassUnlocks.IsUnlocked(spec.Id, _drawn, _catalog);

            PageButton button = owned
                ? PageButton.Choose
                : ClassUnlocks.CanBuy(spec.Id, _drawn, _catalog) ? PageButton.Unlock : PageButton.Dead;

            SkillStrip.Build(spec, _catalog, _icons, _looks?.For(spec.Id) ?? default, _strip);

            _page.Show(
                new ClassPageModel(
                    spec,
                    _index,
                    _shown.Count,
                    button,
                    owned ? 0 : spec.Unlock.ShardPrice,
                    owned ? null : DeedLine(spec.Unlock),
                    RoleOf(spec),
                    _strip),
                _localizer);
        }

        /// <summary>
        /// Stands the shown class's body on the preview stage, sized to the page's image. A class
        /// with no authored body hides the stage (UI-05b).
        /// </summary>
        private void ShowBody()
        {
            if (_preview == null || _looks == null || _index < 0 || _index >= _shown.Count)
            {
                return;
            }

            if (_page != null && _page.Preview != null)
            {
                Vector2 size = PreviewPixels(_page.Preview);

                _preview.Resize(Mathf.Max(1, Mathf.RoundToInt(size.x)), Mathf.Max(1, Mathf.RoundToInt(size.y)));
                _page.Preview.texture = _preview.Texture;
            }

            _preview.Show(_looks.For(_shown[_index].Id));
        }

        private static Vector2 PreviewPixels(RawImage image)
        {
            float scale = image.canvas != null && image.canvas.scaleFactor > 0f ? image.canvas.scaleFactor : 1f;
            Vector2 size = image.rectTransform.rect.size * scale;

            return size.x > 1f && size.y > 1f && float.IsFinite(size.x) && float.IsFinite(size.y)
                ? size
                : new Vector2(FallbackPreviewPx, FallbackPreviewPx);
        }

        /// <summary>
        /// The deed line for <paramref name="unlock"/>, or null for none (M6-09b rule 4).
        /// </summary>
        /// <remarks>
        /// <b>A depth deed draws; a boss deed does not, and that is a ruling.</b> The Gravecaller's
        /// deed is the Choirmother, which no mode in this build authors until <b>M7-03</b>
        /// (M6-09a rule 4), so a line promising it would be the one thing on any screen that offers
        /// what the model cannot deliver. The line arrives with the boss.
        /// </remarks>
        private string DeedLine(UnlockSpec unlock) =>
            unlock.DeedStage > 0 ? _localizer.Format(DeedStageKey, unlock.DeedStage) : null;

        /// <summary>
        /// Melee for a swept cone, ranged for a shot (rule 5). <see cref="RoleKind.Caster"/> is
        /// drawn when a weapon kind says so; none does yet.
        /// </summary>
        private static RoleKind RoleOf(CharacterSpec spec) =>
            spec.Weapon.Kind == WeaponKind.Cone ? RoleKind.Melee : RoleKind.Ranged;

        /// <summary>
        /// A locked class's button was tapped: buy it if it can be bought, and redraw (rules 6, 8).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b><c>CanBuy</c> is re-asked before anything is sent</b> — M5-08a rule 4's second door. A
        /// dead button refuses a tap, but a handler invoked directly never consults
        /// <c>interactable</c>, and the alternative is an <see cref="InvalidOperationException"/>
        /// out of a <see cref="Button"/>'s <c>onClick</c>. <see cref="ProfileStore.Unlock"/> still
        /// throws: the predicate is the gate, the exception stays the invariant.
        /// </para>
        /// <para>
        /// <b>It does not descend.</b> The page comes back owned and live, and the next tap — in a
        /// later frame — plays it.
        /// </para>
        /// </remarks>
        private void OnUnlockTapped(ContentId characterId)
        {
            if (_descending || _buying)
            {
                return;
            }

            if (!ClassUnlocks.CanBuy(characterId, _profile.Current, _catalog))
            {
                return;
            }

            _buying = true;

            // The one writer, moving both fields in one save.
            _profile.Unlock(characterId, _catalog);

            _drawn = _profile.Current;

            DrawBalance();
            DrawPage();
        }

        /// <summary>
        /// The Choose button was tapped: record the run and load the scene it is played in.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The latch goes down before anything else happens</b>, and the button goes dead with
        /// it, so a second touch arriving during the load finds a dead button —
        /// <c>MenuPresenter.Descend</c>'s guard.
        /// </para>
        /// <para>
        /// <c>async void</c>, which is otherwise a smell, is exactly right for what is effectively
        /// an event handler: every path out of the await is handled here, so nothing escapes to the
        /// synchronisation context.
        /// </para>
        /// <para>
        /// <b>Nothing touches <c>SavedRun</c> and nothing deletes the file</b> (M2-14b rule 7).
        /// Abandoning a deep run by starting a new one is recorded by the new run's opening write,
        /// one scene later.
        /// </para>
        /// </remarks>
        /// <param name="characterId">The class the page was standing for.</param>
        private async void OnCardChosen(ContentId characterId)
        {
            // _buying as well: a class bought this frame is live again, and the second half of the
            // double tap that bought it must not start a run (M6-09b rule 8).
            if (_descending || _buying)
            {
                return;
            }

            _descending = true;

            SetButtonInteractable(false);

            try
            {
                _pending.Set(FirstModeId(), characterId, Environment.TickCount);

                await _loader.LoadAsync(SceneLoader.Run);
            }
            catch (Exception exception)
            {
                // The load failed, so this object is still alive and the screen is still up. Give
                // the button back rather than stranding the player on a dead screen.
                _descending = false;

                SetButtonInteractable(true);

                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// The mode this run plays: the first the catalog holds, which is Descent because it is the
        /// only one (GD §4.5).
        /// </summary>
        /// <remarks>
        /// <b><c>MenuPresenter.FirstModeId</c>, moved with the write it feeds</b> — the first, rather
        /// than <c>mode.descent</c> written here. GD §4.5's rule is that a mode is a data object and
        /// nothing in the code may assume Descent. When there is a mode-select screen this becomes a
        /// second choice; until then it is a stand-in.
        /// </remarks>
        /// <exception cref="InvalidOperationException">The catalog holds no modes.</exception>
        private ContentId FirstModeId()
        {
            if (_catalog.Modes.Count == 0)
            {
                throw new InvalidOperationException(
                    "The content catalog holds no modes, so a chosen class has no mode to start a " +
                    "run with. Add a ModeDefinition to BootScope's mode list.");
            }

            return _catalog.Modes[0].Id;
        }

        /// <summary>
        /// Lays out the header and the page in dp, and the page under the header (rule 9). The
        /// back button sits top-left and the balance top-right of the safe area.
        /// </summary>
        private void PlaceChrome()
        {
            float px = PixelsPerDp();

            if (_back != null && _back.transform is RectTransform back)
            {
                back.anchorMin = back.anchorMax = back.pivot = new Vector2(0f, 1f);
                back.sizeDelta = new Vector2(BackDp * px, BackDp * px);
                back.anchoredPosition = new Vector2(HeaderMarginDp * px, -HeaderMarginDp * px);
            }

            if (_balanceGroup != null)
            {
                _balanceGroup.anchorMin = _balanceGroup.anchorMax = _balanceGroup.pivot = new Vector2(1f, 1f);
                _balanceGroup.sizeDelta = new Vector2(BalanceWidthDp * px, BalanceHeightDp * px);
                _balanceGroup.anchoredPosition = new Vector2(-HeaderMarginDp * px, -(HeaderMarginDp * px) - (((BackDp - BalanceHeightDp) * 0.5f) * px));
            }

            if (_balanceIcon != null)
            {
                RectTransform icon = _balanceIcon.rectTransform;
                icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0f, 0.5f);
                icon.sizeDelta = new Vector2(BalanceIconDp * px, BalanceIconDp * px);
                icon.anchoredPosition = new Vector2(8f * px, 0f);
            }

            if (_balance != null)
            {
                RectTransform text = _balance.rectTransform;
                text.anchorMin = Vector2.zero;
                text.anchorMax = Vector2.one;
                text.offsetMin = new Vector2((BalanceIconDp + 16f) * px, 0f);
                text.offsetMax = new Vector2(-8f * px, 0f);
                _balance.fontSize = BalanceTextDp * px;
                _balance.alignment = TextAlignmentOptions.MidlineRight;
                _balance.enableAutoSizing = false;
            }

            if (_page != null && _page.transform is RectTransform page)
            {
                page.anchorMin = Vector2.zero;
                page.anchorMax = Vector2.one;
                page.pivot = new Vector2(0.5f, 0.5f);
                page.offsetMin = Vector2.zero;
                page.offsetMax = new Vector2(0f, -HeaderDp * px);

                _page.Layout(px);
            }
        }

        /// <summary>
        /// How many canvas units one dp is worth — <c>TreeViewPresenter.PixelsPerDp</c>, for its
        /// reason.
        /// </summary>
        private float PixelsPerDp()
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            return StickShaper.PixelsPerDp(Screen.dpi) / scale;
        }

        /// <remarks>
        /// Alpha 1 on the same call, with no tween and no coroutine. The raycast block goes on with
        /// it: the screen is full-screen, so the menu underneath is covered rather than disabled —
        /// <c>PausePresenter.ShowPanel</c>'s shape.
        /// </remarks>
        private void ShowScreen()
        {
            if (_root == null)
            {
                return;
            }

            _root.alpha = 1f;
            _root.blocksRaycasts = true;
            _root.interactable = true;
        }

        private void HideScreen()
        {
            if (_root != null)
            {
                _root.alpha = 0f;
                _root.blocksRaycasts = false;
                _root.interactable = false;
            }

            if (_page != null)
            {
                _page.Clear();
            }

            // Rule 4: the stage goes down with the page, and its camera stops rendering.
            if (_preview != null)
            {
                _preview.Hide();
            }
        }

        private void SetButtonInteractable(bool value)
        {
            if (_page != null && _page.Primary != null)
            {
                _page.Primary.interactable = value;
            }
        }

        /// <remarks>
        /// Removed before it is added, and with a named method rather than a lambda, so that a
        /// component injected twice — which VContainer does not do and a test does — closes once
        /// rather than once per injection. <c>PausePresenter.Wire</c>'s trick, for its reason.
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
