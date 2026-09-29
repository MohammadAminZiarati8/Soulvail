using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and Hud.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// The top band's right-hand readouts (UI-01d): the stage chip, <em>"STAGE 3 / Wave 2/3"</em>,
    /// left of the pause button, and the skull chip under it for the last five enemies of a stage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A component of its own rather than more of <c>HudPresenter</c>.</b> That class is already
    /// the player's row, the stage fade, the boss band and both economies, and neither chip shares an
    /// event with any of them: the stage and the wave are the stage's progress, not the player's. So
    /// this is a fifth view on <c>Hud.prefab</c>, registered by <c>RunScope</c> the way
    /// <c>XpBarView</c> is.
    /// </para>
    /// <para>
    /// <b>The stage and the wave arrive as events; the count is polled.</b> <c>StageArrived</c> and
    /// <c>WaveStarted</c> carry every number the stage chip draws, so nothing here asks the run for
    /// them after the opening frame. <see cref="RunState.EnemiesLeft"/> (UI-01c) moves on most ticks of
    /// a fight, so it is read each frame and a text is written only when its number moves —
    /// <c>AutoCastRow</c>'s bargain with the cooldowns.
    /// </para>
    /// <para>
    /// <b>The opening state is the stage alone.</b> A resume always opens in a stage's arrival — the
    /// save is taken at the clear, for the stage after — and a fresh run opens in one too, so there is
    /// never a wave to restore. <see cref="RunState.StageIndex"/> is the one read, on
    /// <c>RunStarted</c> and in <c>Start</c>, for <c>HudPresenter</c>'s reason: which of the two runs
    /// first depends on an order Unity does not give.
    /// </para>
    /// <para>
    /// <b>Words are keys and numbers are formats.</b> <em>STAGE</em> and <em>Wave</em> are
    /// <c>ui.hud.stage</c> and <c>ui.hud.wave</c>, read from the localizer once at <c>Start</c> and
    /// written with TMP's float overload and <c>{0:0}</c>, which allocates nothing (Traps §9).
    /// </para>
    /// <para>
    /// <b>GD §16.1's <em>"fades 3s after wave start"</em> is not built.</b> The owner's mockup keeps
    /// the chip up for the whole stage, and the brief is the newer word.
    /// </para>
    /// </remarks>
    public sealed class StageReadout : MonoBehaviour
    {
        /// <summary>The enemies-left chip shows for this many and fewer (GD §16.1, the owner's brief).</summary>
        public const int ShownAtOrBelow = 5;

        /// <summary>The count, on its own — <c>HudPresenter</c>'s spelling for its reasons.</summary>
        private const string CountFormat = "{0:0}";

        /// <summary>
        /// <em>STAGE</em>'s size in dp, over the wave: Android's 12 sp body-text floor, the smallest
        /// the HUD draws a word at (M4-07).
        /// </summary>
        private const float CaptionSizeDp = 12f;

        /// <summary>The wave's and the count's size in dp — and the stage's, when it stands alone.</summary>
        private const float NumberSizeDp = 14f;

        /// <summary>Where the stage line gives way to the wave line, as a fraction of the chip from its bottom.</summary>
        private const float LineSplit = 0.52f;

        private static readonly LocKey StageKey = new LocKey("ui.hud.stage");
        private static readonly LocKey WaveKey = new LocKey("ui.hud.wave");

        [Tooltip("The stage chip: a sliced Pill_Dark left of the pause button. Its two texts are " +
                 "its children, placed inside it by this component.")]
        [SerializeField] private RectTransform _stageChip;

        [Tooltip("\"STAGE 3\" — the chip's top line, or the whole chip before the stage's first wave.")]
        [SerializeField] private TMP_Text _stageText;

        [Tooltip("\"Wave 2/3\" — the chip's bottom line, hidden until the stage's first wave.")]
        [SerializeField] private TMP_Text _waveText;

        [Tooltip("The enemies-left chip: a sliced Chip_Counter under the pause button, with the " +
                 "skull and the count inside it. Hidden unless five or fewer enemies are left.")]
        [SerializeField] private RectTransform _enemiesChip;

        [Tooltip("The count inside the enemies-left chip.")]
        [SerializeField] private TMP_Text _enemiesText;

        [Tooltip("The stage chip's size in dp.")]
        [SerializeField] private Vector2 _stageChipSizeDp = new Vector2(HudLayout.StageChipWidthDp, HudLayout.StageChipHeightDp);

        [Tooltip("Where the stage chip's top-right corner sits, in dp in from the safe area's right " +
                 "edge and down from its top: left of the 44 dp pause button, on the star's centre line.")]
        [SerializeField] private Vector2 _stageChipInsetDp = new Vector2(HudLayout.StageChipRightDp, HudLayout.StageChipTopDp);

        [Tooltip("The enemies-left chip's size in dp.")]
        [SerializeField] private Vector2 _enemiesChipSizeDp = new Vector2(HudLayout.EnemiesChipWidthDp, HudLayout.EnemiesChipHeightDp);

        [Tooltip("Where the enemies-left chip's top-right corner sits, in dp in from the safe area's " +
                 "right edge and down from its top: centred under the pause button.")]
        [SerializeField] private Vector2 _enemiesChipInsetDp = new Vector2(HudLayout.EnemiesChipRightDp, HudLayout.EnemiesChipTopDp);

        private IRunSession _session;
        private ILocalizer _localizer;

        private IDisposable _startedSubscription;
        private IDisposable _arrivedSubscription;
        private IDisposable _waveSubscription;

        /// <summary>The two formats, read once at <c>Start</c>. The keys until then.</summary>
        private string _stageFormat;
        private string _waveFormat;

        /// <summary>The count last written, or −1 so the first frame always writes.</summary>
        private int _shownLeft = -1;

        /// <summary>Canvas units per dp, as of the last <see cref="Place"/>. Zero before it.</summary>
        private float _pxPerDp;

        /// <summary>The stage the chip reads, or 0 before any run.</summary>
        public int Stage { get; private set; }

        /// <summary>The wave the chip reads, numbered from 1 — 0 until the stage's first <c>WaveStarted</c>.</summary>
        public int Wave { get; private set; }

        /// <summary>How many waves the stage holds, as the last <c>WaveStarted</c> said.</summary>
        public int WaveCount { get; private set; }

        /// <summary>Whether the enemies-left chip is on screen.</summary>
        public bool IsEnemiesChipShown => _enemiesChip != null && _enemiesChip.gameObject.activeSelf;

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="session">The run, for the opening stage and the count each frame.</param>
        /// <param name="localizer">The two words, read once at <c>Start</c>.</param>
        /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
        /// <remarks>
        /// Subscribed here rather than in <c>OnEnable</c> — <c>HudPresenter.Construct</c>'s reason:
        /// <c>RunScope</c> builds its container from its own <c>Awake</c> and Unity orders no two of
        /// those. Each subscription is disposed before it is replaced, so a component injected twice —
        /// which VContainer does not do and a test does — holds one of each.
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub, IRunSession session, ILocalizer localizer)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            _startedSubscription?.Dispose();
            _startedSubscription = hub.Subscribe<RunStarted>(OnRunStarted);

            _arrivedSubscription?.Dispose();
            _arrivedSubscription = hub.Subscribe<StageArrived>(OnStageArrived);

            _waveSubscription?.Dispose();
            _waveSubscription = hub.Subscribe<WaveStarted>(OnWaveStarted);
        }

        /// <exception cref="MissingReferenceException">A chip or one of its texts is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this view.</exception>
        /// <remarks>In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason.</remarks>
        private void Start()
        {
            if (_stageChip == null || _stageText == null || _waveText == null || _enemiesChip == null || _enemiesText == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(StageReadout)} is missing a chip or one of its texts. Drag the StageChip, " +
                    "its StageText and WaveText, the EnemiesChip and its EnemiesText onto it — a chip " +
                    "that is only partly dressed reads as a stage that has stopped.");
            }

            if (_session is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(StageReadout)} was never injected, so the chips would read whatever the " +
                    "prefab was left dressed as. The component is registered by RunScope — drag this " +
                    "object onto its Stage Readout field.");
            }

            _stageFormat = Word(StageKey);
            _waveFormat = Word(WaveKey);

            _stageText.color = Palette.HudCaption;
            _waveText.color = Palette.HudText;
            _enemiesText.color = Palette.HudText;

            Place();

            SetShown(_enemiesChip.gameObject, false);

            Redraw();
        }

        /// <remarks>Explicit, for <c>HudPresenter.OnDestroy</c>'s reason.</remarks>
        private void OnDestroy()
        {
            _startedSubscription?.Dispose();
            _startedSubscription = null;

            _arrivedSubscription?.Dispose();
            _arrivedSubscription = null;

            _waveSubscription?.Dispose();
            _waveSubscription = null;
        }

        /// <remarks>
        /// <c>Update</c>, not <c>LateUpdate</c>, for <c>AutoCastRow</c>'s reason: <c>RunTicker</c>
        /// ticks in the Update phase, and the chip reads the count the tick has just written.
        /// </remarks>
        private void Update()
        {
            DrawEnemies();
        }

        private void OnRunStarted(RunStarted evt)
        {
            Redraw();
        }

        /// <summary>Rule 1: a new stage, and no wave yet — GD §7.1's arrival.</summary>
        private void OnStageArrived(StageArrived evt)
        {
            Stage = evt.Stage;
            Wave = 0;
            WaveCount = 0;

            DrawStage();
        }

        /// <summary>Rule 1: a wave began, and the event carries the stage's whole count.</summary>
        private void OnWaveStarted(WaveStarted evt)
        {
            Stage = evt.Stage;
            Wave = evt.Wave;
            WaveCount = evt.WaveCount;

            DrawStage();
        }

        /// <summary>Rule 2: the run's stage, and no wave, which is where every run opens.</summary>
        /// <remarks>A null state is a scene with no run yet, which draws nothing — <c>HudPresenter.Redraw</c>'s rule.</remarks>
        private void Redraw()
        {
            RunState state = _session?.State;

            if (state is null)
            {
                return;
            }

            Stage = state.StageIndex;
            Wave = 0;
            WaveCount = 0;

            DrawStage();
        }

        /// <summary>
        /// The stage chip: the stage over the wave, or the stage alone and centred before the first
        /// wave (rule 1).
        /// </summary>
        private void DrawStage()
        {
            if (_stageText == null || _waveText == null)
            {
                return;
            }

            bool hasWave = Wave > 0;

            SetShown(_waveText.gameObject, hasWave);

            // The stage line takes the chip's top half over a wave, and the whole chip alone — at the
            // wave's size, so the two-second arrival reads as a title rather than as half a chip.
            if (_stageText.transform is RectTransform stage)
            {
                stage.anchorMin = new Vector2(0f, hasWave ? LineSplit : 0f);
                stage.anchorMax = Vector2.one;
                stage.offsetMin = Vector2.zero;
                stage.offsetMax = Vector2.zero;
            }

            SizeText(_stageText, hasWave ? CaptionSizeDp : NumberSizeDp);

            _stageText.SetText(_stageFormat ?? StageKey.ToString(), Stage);

            if (hasWave)
            {
                _waveText.SetText(_waveFormat ?? WaveKey.ToString(), Wave, WaveCount);
            }
        }

        /// <summary>
        /// Rule 3: the skull chip for the last five, written only when the count moves.
        /// </summary>
        private void DrawEnemies()
        {
            RunState state = _session?.State;

            if (state is null || _enemiesChip == null)
            {
                return;
            }

            int left = state.EnemiesLeft;
            bool shown = left >= 1 && left <= ShownAtOrBelow;

            SetShown(_enemiesChip.gameObject, shown);

            if (!shown || left == _shownLeft || _enemiesText == null)
            {
                return;
            }

            _shownLeft = left;
            _enemiesText.SetText(CountFormat, left);
        }

        /// <summary>
        /// Rule 6: both chips from the safe area's top-right in dp, and their texts sized from dp.
        /// </summary>
        /// <remarks>
        /// <c>HudPresenter.PlaceRightEdge</c>'s shape: pinned by the top-right corner, so
        /// <c>SafeAreaFitter</c> insets them on a notched device and they agree with
        /// <c>PausePresenter</c>'s icon, which is placed the same way on its own canvas. A nonsense
        /// field leaves that chip's authored rect alone.
        /// </remarks>
        private void Place()
        {
            _pxPerDp = PixelsPerDp();

            if (IsUsableSize(_stageChipSizeDp) && IsUsableInset(_stageChipInsetDp))
            {
                PlaceFromRight(_stageChip, _stageChipInsetDp, _stageChipSizeDp, _pxPerDp);
            }

            if (IsUsableSize(_enemiesChipSizeDp) && IsUsableInset(_enemiesChipInsetDp))
            {
                PlaceFromRight(_enemiesChip, _enemiesChipInsetDp, _enemiesChipSizeDp, _pxPerDp);
            }

            // Both chips are sliced pills, so their round ends are fitted to the height they were
            // just given — HudLayout.FitSlices' reason. Once, here, never per frame.
            HudLayout.FitSlices(_stageChip == null ? null : _stageChip.GetComponent<Image>());
            HudLayout.FitSlices(_enemiesChip == null ? null : _enemiesChip.GetComponent<Image>());

            SizeText(_waveText, NumberSizeDp);
            SizeText(_enemiesText, NumberSizeDp);
        }

        private string Word(LocKey key) => _localizer is null ? key.ToString() : _localizer.Get(key);

        private void SizeText(TMP_Text text, float sizeDp)
        {
            float size = sizeDp * _pxPerDp;

            if (text == null || !float.IsFinite(size) || size <= 0f)
            {
                return;
            }

            text.enableAutoSizing = false;

            if (!Mathf.Approximately(text.fontSize, size))
            {
                text.fontSize = size;
            }
        }

        private static void SetShown(GameObject target, bool shown)
        {
            if (target.activeSelf != shown)
            {
                target.SetActive(shown);
            }
        }

        private static void PlaceFromRight(RectTransform rect, Vector2 insetDp, Vector2 sizeDp, float pxPerDp)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = sizeDp * pxPerDp;
            rect.anchoredPosition = new Vector2(-insetDp.x * pxPerDp, -insetDp.y * pxPerDp);
        }

        private static bool IsUsableSize(Vector2 dp) =>
            float.IsFinite(dp.x) && float.IsFinite(dp.y) && dp.x > 0f && dp.y > 0f;

        private static bool IsUsableInset(Vector2 dp) =>
            float.IsFinite(dp.x) && float.IsFinite(dp.y) && dp.x >= 0f && dp.y >= 0f;

        /// <summary>How many canvas units one dp is worth — <c>HudPresenter.PixelsPerDp</c>, for its reason.</summary>
        private float PixelsPerDp()
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            return StickShaper.PixelsPerDp(Screen.dpi) / scale;
        }
    }
}
