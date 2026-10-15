using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and RunEnd.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// What the descent was worth: the payout GD §14.1 computes, drawn beside the two terms that
    /// made it, and one button back to the Menu. GD §16.1, §16.4; AR §11.5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><c>ShardsAwarded</c> is what opens it, and both alternatives are refused by name</b> (rule
    /// 1). Not <c>RunEnded</c>: that fires whenever <c>RunScope</c> is torn down, which is every
    /// ordinary exit from the Run scene, so a screen hung off it would appear over a scene already
    /// unloading — <c>SaveWriter</c>, <c>HudPresenter</c> and <c>PausePresenter</c> have each refused
    /// it for exactly this, and a fourth class disagreeing would be the bug. <b>Not <c>PlayerDied</c>
    /// either</b>, and that is the new half of the ruling: it is published one line earlier and
    /// carries no number, so a screen opened on it would be up for a frame with three empty rects.
    /// <c>ShardsAwarded</c> is published only inside <c>RunSession.Tick</c>'s <c>IsDead</c> branch and
    /// carries everything this screen draws, so <b>the screen cannot be on screen before its numbers
    /// are</b>.
    /// </para>
    /// <para>
    /// <b>It replaces the death overlay rather than stacking on it</b> (rule 2). <c>HudPresenter</c>
    /// put up two words and took a tap from M1-17 to M4-06; it now keeps none of that, and
    /// <c>SceneLoader</c>, <c>InputAdapter</c> and <c>ILocalizer</c> left that class with the overlay
    /// because the death path was the only reader of all three. Two screens for one death is the
    /// worst outcome available, and a payout the player taps past to reach a <em>second</em>
    /// dismissal is worse still.
    /// </para>
    /// <para>
    /// <b>It holds no pause and needs none</b> (rule 4). <c>RunTicker.Tick</c> already returns on
    /// <c>!_session.IsRunning</c>, so the simulation is stopped before this screen exists and a
    /// <c>PauseReason</c> would be a second answer to a question already settled. <c>RunPause</c> is
    /// deliberately <em>not</em> a dependency of this class and a test pins its absence —
    /// <c>LevelUpPresenter</c>'s rule 3, reached from the other side. Nothing here touches
    /// <c>Time.timeScale</c>, and there is no fade, no tween and no count-up: the run is already
    /// stopped, and a tween would be a second clock in a screen whose whole job is to be read and
    /// left.
    /// </para>
    /// <para>
    /// <b>Every string is a <see cref="LocKey"/> and every number is drawn beside a label rather than
    /// inside a sentence</b> (rule 5, AR §11.5). No row in <c>English.asset</c> carries a <c>{0}</c>,
    /// because nothing in this project's localisation takes arguments and inventing that here would
    /// be M6-10 arriving early. <b>A null localizer falls back to the key</b> —
    /// <c>MenuPresenter.Write</c>'s answer — because a run-end screen missing a word is still a
    /// screen a player can leave, and throwing here would strand them on a run that has already
    /// ended. The hub and the loader are the two that do throw, because without either this screen
    /// is a dead end rather than an unreadable one.
    /// </para>
    /// <para>
    /// <b>It draws this run's payout and no lifetime total</b> (rule 8). The banked figure lives in
    /// <c>ProfileStore.Current</c> one scope up, and drawing it here would race <c>ShardWriter</c>:
    /// both hang off the same event, the hub guarantees no order between a scoped service and an
    /// injected component, and a screen that showed the total <em>before</em> the write would be
    /// wrong every second run and right every other. A lifetime total belongs beside the thing that
    /// spends it — M5-07's class select or M6-02's Sanctum.
    /// </para>
    /// <para>
    /// <b>No readability verdict is ticked here</b> (rule 11). [Ledger row 1] says the HUD is too
    /// cramped to read; this task states what it shipped — five labels, three numbers, the point
    /// sizes below, a full-screen canvas inside <c>SafeAreaFitter</c>'s inset — and hands the
    /// judgement to M4-07. Unlike M4-04's band it collides with nothing; what it risks is being
    /// unreadable on its own terms.
    /// </para>
    /// </remarks>
    public sealed class RunEndPresenter : MonoBehaviour
    {
        /// <summary>
        /// One whole number, on its own. <c>{0:0}</c> rather than <c>{0}</c> is
        /// <c>HudPresenter.HpFormat</c>'s reason: TMP treats an unformatted placeholder as "up to
        /// nine decimal places", so the natural spelling would render a payout as <c>110.0</c>.
        /// Passed to TMP's <c>SetText</c>, which formats straight into its own backing array and
        /// allocates nothing where an interpolated string would allocate one per draw.
        /// </summary>
        private const string NumberFormat = "{0:0}";

        /// <summary>
        /// <em>"You died"</em> — the headline, and it keeps the key the overlay had (rule 3).
        /// </summary>
        /// <remarks>
        /// Retiring <c>ui.death.title</c> to coin a synonym would be churn that breaks a shipped row
        /// for nothing: it is the right word, <c>English.asset</c> already carries it, and
        /// <c>TableLocalizerTests</c> already asserts it resolves. Authored here rather than on the
        /// prefab, <c>PausePresenter.ResumeKey</c>'s reason — a key in the code cannot drift from the
        /// file that draws it.
        /// </remarks>
        private static readonly LocKey TitleKey = new LocKey("ui.death.title");

        /// <summary>
        /// <em>"Tap to return"</em> — now the exit button's label rather than an instruction under a
        /// full-screen tap (rules 3, 9).
        /// </summary>
        private static readonly LocKey ReturnKey = new LocKey("ui.death.hint");

        /// <summary>How deep the run got. The first term's input, named.</summary>
        private static readonly LocKey DepthKey = new LocKey("ui.runend.depth");

        /// <summary>How many bosses it left behind it. The second term's input.</summary>
        private static readonly LocKey BossesKey = new LocKey("ui.runend.bosses");

        /// <summary>What the two of them are worth, in Soul Shards.</summary>
        private static readonly LocKey ShardsKey = new LocKey("ui.runend.shards");

        /// <summary><em>"{0} cleared!"</em> — a finished place's title, with its name (UI-11d rule 4).</summary>
        private static readonly LocKey FinishedKey = new LocKey("ui.runend.finished");

        /// <summary><em>"New place: {0}"</em> — the line under the title when a finish opened one (rule 5).</summary>
        private static readonly LocKey OpenedKey = new LocKey("ui.runend.opened");

        // The column, in dp (UI-08e rule 3): centred on the safe area, no wider than its width less a
        // side. Guesses until a phone exists — ledger row 4.
        private const float ColumnSideDp = 16f;
        private const float ColumnMaxWidthDp = 360f;
        private const float TitleHeightDp = 56f;
        private const float TitleTextDp = 40f;
        private const float RowHeightDp = 36f;
        private const float RowTextDp = 22f;
        private const float ShardTextDp = 28f;
        private const float RowGapDp = 8f;
        private const float TitleGapDp = 16f;
        private const float ReturnGapDp = 24f;
        private const float ReturnMaxWidthDp = 280f;
        private const float ReturnHeightDp = 56f;
        private const float ReturnTextDp = 22f;

        /// <summary>A row's word takes this share of its width, its number the rest.</summary>
        private const float LabelShare = 0.6f;

        [Tooltip("The whole screen, switched between alpha 0 and 1. No fade — the run is already " +
                 "stopped and a tween here would be a second clock (rule 4).")]
        [SerializeField] private CanvasGroup _root;

        [Tooltip("The one way out. A Button rather than a full-screen tap (rule 9): this is the " +
                 "first screen in the game the player is meant to read, and a stray thumb still " +
                 "travelling from the last dodge would dismiss a full-screen tap before a word of " +
                 "it landed.")]
        [SerializeField] private Button _return;

        [Tooltip("\"You died\", written from ui.death.title in Start.")]
        [SerializeField] private TMP_Text _title;

        [Tooltip("\"Tap to return\" on the exit button, written from ui.death.hint in Start.")]
        [SerializeField] private TMP_Text _returnLabel;

        [Tooltip("\"Depth\", written from ui.runend.depth in Start.")]
        [SerializeField] private TMP_Text _depthLabel;

        [Tooltip("\"Bosses\", written from ui.runend.bosses in Start.")]
        [SerializeField] private TMP_Text _bossLabel;

        [Tooltip("\"Soul Shards\", written from ui.runend.shards in Start.")]
        [SerializeField] private TMP_Text _shardLabel;

        [Tooltip("How deep the run got, off ShardsAwarded.DeepestStage. Palette.Neutral — a fact " +
                 "about the run rather than the reward (rule 6).")]
        [SerializeField] private TMP_Text _depth;

        [Tooltip("How many bosses it killed, off ShardsAwarded.BossesKilled. Palette.Neutral, for " +
                 "the same reason.")]
        [SerializeField] private TMP_Text _bosses;

        [Tooltip("The payout, off ShardsAwarded.Total. Palette.Essence — GD §16.4's reward colour, " +
                 "and this is its first reader (rule 6).")]
        [SerializeField] private TMP_Text _shards;

        private SceneLoader _loader;
        private ILocalizer _localizer;
        private ContentCatalog _catalog;

        private IDisposable _awardSubscription;
        private IDisposable _finishSubscription;

        /// <summary>
        /// The "New place" line, built in <see cref="Start"/> beside the title rather than authored on
        /// the prefab (UI-11d's table lists no prefab). Null until then, and hidden unless a finish
        /// opened a place.
        /// </summary>
        private TMP_Text _opened;

        /// <summary>The title's colour as the prefab dressed it, which a death goes back to.</summary>
        /// <remarks>Nullable so the class still carries no serialized-colour-typed field (rule 6).</remarks>
        private Color? _titleColor;

        /// <summary>Whether <see cref="_opened"/> is drawn, which <see cref="LayOut"/> makes room for.</summary>
        private bool _showsOpened;

        /// <summary>
        /// The exit has been hit and the scene load has not answered yet.
        /// </summary>
        /// <remarks>
        /// The latch rather than the button's <c>interactable</c> alone, <c>PausePresenter._quitting</c>'s
        /// reason: uGUI dispatches both taps of a double tap from one <c>EventSystem</c> pass, and a
        /// handler invoked directly — which is what a test does — never consults <c>interactable</c>
        /// at all. Lowered again by a failed load (rule 10), never by anything else: a run does not
        /// come back from a death.
        /// </remarks>
        private bool _leaving;

        /// <summary>Canvas units to the dp when set above 0, else read from the screen (UI-08e).</summary>
        private float _pxPerDpOverride;

        /// <summary>Whether the screen is currently up. The one read a test needs.</summary>
        public bool IsShown => _root != null && _root.alpha > 0f;

        /// <summary>
        /// Lays the screen out with <paramref name="canvasUnitsPerDp"/> instead of the screen's own:
        /// what a device would have, for a layout check — <c>TreeViewPresenter</c>'s seam (UI-07d
        /// rule 4). Zero or less goes back to the screen's.
        /// </summary>
        public void UsePixelsPerDp(float canvasUnitsPerDp)
        {
            _pxPerDpOverride = float.IsFinite(canvasUnitsPerDp) && canvasUnitsPerDp > 0f ? canvasUnitsPerDp : 0f;

            LayOut();
        }

        /// <param name="hub">The run's event hub. Subscribed for this component's life.</param>
        /// <param name="loader">Where the exit goes. The only thing this screen asks of the app.</param>
        /// <param name="localizer">
        /// What turns this screen's five keys into words. Resolved from <c>BootScope</c>, one scope
        /// up, like every other screen's. <b>Allowed to be null, unlike the two above it</b> — see
        /// <see cref="Write"/>.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="hub"/> or <paramref name="loader"/> is null.</exception>
        /// <remarks>
        /// Subscribed here rather than in <c>OnEnable</c> — <c>HudPresenter</c>'s reason:
        /// <c>RunScope</c> builds its container from its own <c>Awake</c> and Unity orders no two of
        /// those, so an <c>OnEnable</c> subscription reaches for a hub that may not exist yet. The
        /// button handler is wired here for the same reason and dropped in the same place.
        /// </remarks>
        [Inject]
        public void Construct(DomainEventHub hub, SceneLoader loader, ILocalizer localizer, ContentCatalog catalog)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _localizer = localizer;

            // Allowed to be null, like the localizer: without it a finish keeps the death title
            // rather than throwing on a run that has already ended.
            _catalog = catalog;

            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }

            // One event, and rule 1's whole refusal is that there is no second Subscribe in this
            // file: not RunEnded, which every scope teardown publishes, and not PlayerDied, which
            // carries no number.
            _awardSubscription?.Dispose();
            _awardSubscription = hub.Subscribe<ShardsAwarded>(OnShardsAwarded);

            // The one addition (UI-11d): ModeFinished follows ShardsAwarded in the same tick and
            // re-dresses the screen that event just opened. Still no RunEnded and no PlayerDied.
            _finishSubscription?.Dispose();
            _finishSubscription = hub.Subscribe<ModeFinished>(OnModeFinished);

            Wire(_return, ReturnToMenu);
        }

        /// <exception cref="MissingReferenceException">The root or the exit button is not dressed.</exception>
        /// <exception cref="InvalidOperationException">Nothing injected this presenter.</exception>
        /// <remarks>
        /// In <c>Start</c> rather than <c>Awake</c> for <c>HudPresenter</c>'s reason: that is the
        /// earliest moment every <c>Awake</c> in the scene is guaranteed to have run, so "not
        /// injected" is a conclusion rather than a race.
        /// </remarks>
        private void Start()
        {
            if (_root == null || _return == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(RunEndPresenter)} is missing its root or its exit button. A run-end "
                        + "screen that is only partly dressed leaves the player on a run that has "
                        + "already ended with no way out of it, which is indistinguishable from a "
                        + "crash.");
            }

            if (_loader is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(RunEndPresenter)} was never injected, so its button would go nowhere. "
                        + "The component is registered by RunScope — drag this object onto its Run "
                        + "End Presenter field.");
            }

            // The five words, once, here — PausePresenter's argument: none of them changes while the
            // app is running, unlike the three numbers, which arrive on the event. In Start rather
            // than Construct because a serialized field is not guaranteed dressed before every Awake
            // has run, which is this method's own reason for being where it is.
            Write(_title, TitleKey);
            Write(_returnLabel, ReturnKey);
            Write(_depthLabel, DepthKey);
            Write(_bossLabel, BossesKey);
            Write(_shardLabel, ShardsKey);

            Tint();

            if (_title != null)
            {
                _titleColor = _title.color;
                _opened = BuildOpenedLine(_title);
            }

            // Down whatever the prefab was left dressed as, so a screen someone was editing cannot
            // ship covering the arena — HudPresenter's argument for its death panel, inherited with
            // the rest of the path.
            Hide();
        }

        /// <remarks>
        /// Explicit rather than left to the hub's disposal: a screen destroyed before its scope — a
        /// scene reload, an arena opened without a run — would otherwise stay in a subscriber list
        /// and be handed an event for a component Unity has killed.
        /// </remarks>
        private void OnDestroy()
        {
            _awardSubscription?.Dispose();
            _awardSubscription = null;
            _finishSubscription?.Dispose();
            _finishSubscription = null;

            Unwire(_return, ReturnToMenu);
        }

        /// <summary>
        /// The run is over and it was worth this much: draw the three numbers, then go up.
        /// </summary>
        /// <remarks>
        /// <b>The numbers are written before the screen is shown</b>, so no frame can display the
        /// rects the prefab was dressed with — <c>HudPresenter.OnPlayerDied</c>'s ordering, kept. The
        /// figures come off the event rather than out of <c>RunState</c>, which is what the event
        /// carries them for (rule 8): this screen holds no handle to the thing that computed them.
        /// </remarks>
        private void OnShardsAwarded(ShardsAwarded evt)
        {
            Draw(_depth, evt.DeepestStage);
            Draw(_bosses, evt.BossesKilled);
            Draw(_shards, evt.Total);

            // A death's dressing, every time: a finish re-dresses it from ModeFinished, which follows.
            Write(_title, TitleKey);

            if (_title != null && _titleColor.HasValue)
            {
                _title.color = _titleColor.Value;
            }

            _showsOpened = false;

            if (_opened != null)
            {
                _opened.gameObject.SetActive(false);
            }

            // Armed on the way up rather than left as the prefab was dressed, so a screen opened a
            // second time — which no run can do, and a test can — is not opened dead.
            _leaving = false;

            if (_return != null)
            {
                _return.interactable = true;
            }

            Show();
        }

        /// <summary>
        /// The place is finished: the title says so in <see cref="Palette.Essence"/> (rule 4), and a
        /// place this one opens gets a line under it (rule 5).
        /// </summary>
        /// <remarks>
        /// Colour is Essence because gold means rewards. A mode the catalog cannot name keeps the
        /// death title: a screen that says the wrong place was cleared is worse than one that is plain.
        /// </remarks>
        private void OnModeFinished(ModeFinished evt)
        {
            if (_catalog is null || !_catalog.TryGetMode(evt.ModeId, out ModeSpec mode))
            {
                return;
            }

            if (_title != null)
            {
                _title.text = Format(FinishedKey, Name(mode));
                _title.color = Palette.Essence;
            }

            IReadOnlyList<ModeSpec> modes = _catalog.Modes;

            for (int i = 0; i < modes.Count; i++)
            {
                if (modes[i].IsListed && modes[i].UnlockedBy == evt.ModeId)
                {
                    if (_opened != null)
                    {
                        _opened.text = Format(OpenedKey, Name(modes[i]));
                        _opened.gameObject.SetActive(true);
                        _showsOpened = true;
                    }

                    break;
                }
            }

            LayOut();
        }

        private string Name(ModeSpec mode) =>
            _localizer is null ? mode.NameKey.ToString() : _localizer.Get(mode.NameKey);

        /// <summary>A key with one argument; the bare key when there is no localizer.</summary>
        private string Format(LocKey key, string argument) =>
            _localizer is null ? key.ToString() : _localizer.Format(key, argument);

        /// <summary>
        /// The "New place" line: a second text beside the title, built here because the prefab is
        /// not in UI-11d's table. Off until a finish opens a place.
        /// </summary>
        private static TMP_Text BuildOpenedLine(TMP_Text title)
        {
            var go = new GameObject("Opened", typeof(RectTransform));

            go.transform.SetParent(title.transform.parent, false);

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();

            text.font = title.font;
            text.fontSharedMaterial = title.fontSharedMaterial;
            text.color = Palette.Neutral;
            text.raycastTarget = false;
            go.SetActive(false);

            return text;
        }

        /// <summary>
        /// Leaves the run for the Menu, which disposes <c>RunScope</c> and ends everything with it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The button goes dead the moment it is hit</b> (rule 9), so a second tap arriving during
        /// the scene load cannot start a second one — <c>MenuPresenter.Descend</c>'s guard and
        /// <c>HudPresenter.ReturnToMenu</c>'s <c>_awaitingTap</c> lowering, which is the line this
        /// method inherits rather than re-invents.
        /// </para>
        /// <para>
        /// <c>async void</c>, which is otherwise a smell and is exactly right for what is effectively
        /// an event handler — every path out of the await is handled here.
        /// </para>
        /// </remarks>
        private async void ReturnToMenu()
        {
            if (_leaving || _loader is null)
            {
                return;
            }

            _leaving = true;

            if (_return != null)
            {
                _return.interactable = false;
            }

            try
            {
                await _loader.LoadAsync(SceneLoader.Menu);
            }
            catch (Exception exception)
            {
                // Rule 10: the load failed, so this object is still alive and the screen is still
                // up. Arm the button again rather than stranding the player on a dead screen with a
                // run that has already ended — HudPresenter's existing catch, moved with the rest of
                // the path.
                _leaving = false;

                if (_return != null)
                {
                    _return.interactable = true;
                }

                Debug.LogException(exception, this);
            }
        }

        /// <summary>
        /// Writes one whole number onto <paramref name="label"/>, if there is one.
        /// </summary>
        /// <remarks>
        /// <para>
        /// TMP's float overload, which writes into its own backing array: an interpolated string
        /// would allocate one per draw, which is <c>HudPresenter.WriteHp</c>'s reason held to on a
        /// screen that draws three numbers once a run.
        /// </para>
        /// <para>
        /// <b>Clamped at zero rather than drawn as given.</b> <c>ShardPayout</c> cannot produce a
        /// negative and <c>PlayerProfile.Shards</c> refuses one, so this is unreachable through
        /// shipped code — but a run-end screen is the last thing a player sees, and it is the one
        /// readout in the game where a minus sign would read as a debt they now owe.
        /// </para>
        /// </remarks>
        private static void Draw(TMP_Text label, int value)
        {
            if (label == null)
            {
                return;
            }

            label.SetText(NumberFormat, value < 0 ? 0 : value);
        }

        /// <summary>
        /// Draws <paramref name="key"/> onto <paramref name="label"/>, if both are there.
        /// </summary>
        /// <remarks>
        /// <b>A missing label is silent and a missing localizer falls back to the key</b> —
        /// <c>MenuPresenter.Write</c>'s semantics, and <c>ToString()</c> rather than <c>Key</c>
        /// because a <c>default(LocKey)</c>'s <c>Key</c> is null and would reach a <c>TMP_Text</c> as
        /// one. Deliberately softer than <see cref="Start"/>'s two throws: this screen refuses to
        /// exist without a way out of it, and shrugs at a missing word.
        /// </remarks>
        private void Write(TMP_Text label, LocKey key)
        {
            if (label == null)
            {
                return;
            }

            label.text = _localizer is null ? key.ToString() : _localizer.Get(key);
        }

        /// <summary>
        /// GD §16.4 on the three numbers: the reward is <see cref="Palette.Essence"/>, the two facts
        /// about the run are <see cref="Palette.Neutral"/> (rule 6).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b><see cref="Palette.Essence"/>'s first reader.</b> Its own summary reads <em>"rewards,
        /// Essence, Gates. No reader yet (M6)"</em> — a Shard payout is a reward, so this screen
        /// needs no eleventh palette member and no serialized <see cref="Color"/> anywhere on it.
        /// </para>
        /// <para>
        /// <b>It may not be <see cref="Palette.Danger"/></b> — GD §16.4's <em>"nothing else,
        /// ever"</em> — and the depth and boss figures are deliberately not amber either: they are
        /// facts about the run rather than the reward, and three amber numbers would say nothing
        /// about which one matters. Applied here rather than authored on the prefab so that the one
        /// place the colour is written down is the one place the rule is enforceable (M3-13a).
        /// </para>
        /// </remarks>
        private void Tint()
        {
            if (_shards != null)
            {
                _shards.color = Palette.Essence;
            }

            if (_depth != null)
            {
                _depth.color = Palette.Neutral;
            }

            if (_bosses != null)
            {
                _bosses.color = Palette.Neutral;
            }
        }

        /// <remarks>
        /// Alpha 1 on the same call, with no tween and no coroutine (rule 4). The raycast block goes
        /// on with it: the canvas is full-screen, so the stick and the Charge button are covered
        /// rather than disabled — <c>LevelUpPresenter.ShowScreen</c>'s answer, and the run under it
        /// is not ticking anyway.
        /// </remarks>
        private void Show()
        {
            if (_root == null)
            {
                return;
            }

            // Laid out on the way up, because the safe area is read here and the screen is
            // invisible until now.
            LayOut();

            _root.alpha = 1f;
            _root.blocksRaycasts = true;
            _root.interactable = true;
        }

        /// <summary>
        /// Takes the screen out of sight and out of the raycaster (rule 1).
        /// </summary>
        /// <remarks>
        /// <b>Alpha <em>and</em> <c>blocksRaycasts</c>.</b> A canvas left at alpha 0 with its
        /// raycasts on is invisible and still eats every touch in the arena, which on a screen
        /// sorted above the whole HUD would be a run nobody could play.
        /// </remarks>
        private void Hide()
        {
            if (_root == null)
            {
                return;
            }

            _root.alpha = 0f;
            _root.blocksRaycasts = false;
            _root.interactable = false;
        }

        /// <summary>
        /// One centred column in the safe area, top to bottom: the title, the three rows, the exit —
        /// no wider than the safe width less <see cref="ColumnSideDp"/> a side, in dp (UI-08e rule 3).
        /// </summary>
        /// <remarks>
        /// The prefab authored these in reference units of a 1920 × 1080 canvas, where the title was
        /// 1400 wide: past the edges of any portrait phone. Every piece hangs from the safe area's
        /// centre, so the column is centred on the part of the screen a thumb can reach.
        /// </remarks>
        private void LayOut()
        {
            if (_return == null || _return.transform.parent is not RectTransform safe)
            {
                return;
            }

            float px = PixelsPerDp();
            Rect area = safe.rect;

            if (!float.IsFinite(px) || px <= 0f || !float.IsFinite(area.width) || area.width <= 0f)
            {
                return;
            }

            float width = Mathf.Min(area.width - (2f * ColumnSideDp * px), ColumnMaxWidthDp * px);
            float title = TitleHeightDp * px;
            float row = RowHeightDp * px;
            float rowGap = RowGapDp * px;
            float exit = ReturnHeightDp * px;
            float opened = _showsOpened && _opened != null ? row + rowGap : 0f;
            float total = title + (TitleGapDp * px) + opened + (3f * row) + (2f * rowGap) + (ReturnGapDp * px) + exit;

            Vector2 centre = area.center;
            float top = centre.y + (total / 2f);

            if (_title != null)
            {
                Place(_title.rectTransform, new Vector2(width, title), new Vector2(centre.x, top - (title / 2f)));
                Size(_title, TitleTextDp * px, TextAlignmentOptions.Center);
            }

            top -= title + (TitleGapDp * px);

            if (opened > 0f)
            {
                Place(_opened.rectTransform, new Vector2(width, row), new Vector2(centre.x, top - (row / 2f)));
                Size(_opened, RowTextDp * px, TextAlignmentOptions.Center);
                top -= opened;
            }

            PlaceRow(_depthLabel, _depth, RowTextDp, centre.x, top, width, row, px);
            top -= row + rowGap;

            PlaceRow(_bossLabel, _bosses, RowTextDp, centre.x, top, width, row, px);
            top -= row + rowGap;

            PlaceRow(_shardLabel, _shards, ShardTextDp, centre.x, top, width, row, px);
            top -= row + (ReturnGapDp * px);

            if (_return.transform is RectTransform button)
            {
                Place(button, new Vector2(Mathf.Min(width, ReturnMaxWidthDp * px), exit), new Vector2(centre.x, top - (exit / 2f)));
            }

            if (_returnLabel != null)
            {
                RectTransform label = _returnLabel.rectTransform;

                label.anchorMin = Vector2.zero;
                label.anchorMax = Vector2.one;
                label.pivot = new Vector2(0.5f, 0.5f);
                label.offsetMin = Vector2.zero;
                label.offsetMax = Vector2.zero;
                Size(_returnLabel, ReturnTextDp * px, TextAlignmentOptions.Center);
            }
        }

        /// <summary>
        /// One row of the column: its rect <paramref name="width"/> wide with its top at
        /// <paramref name="top"/>, the word on the left share and the number on the right.
        /// </summary>
        private static void PlaceRow(TMP_Text word, TMP_Text figure, float figureDp, float x, float top, float width, float height, float px)
        {
            RectTransform rowRect = word != null ? word.rectTransform.parent as RectTransform
                : figure != null ? figure.rectTransform.parent as RectTransform
                : null;

            if (rowRect == null)
            {
                return;
            }

            Place(rowRect, new Vector2(width, height), new Vector2(x, top - (height / 2f)));

            if (word != null)
            {
                RectTransform rect = word.rectTransform;

                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(LabelShare, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                Size(word, RowTextDp * px, TextAlignmentOptions.MidlineLeft);
            }

            if (figure != null)
            {
                RectTransform rect = figure.rectTransform;

                rect.anchorMin = new Vector2(LabelShare, 0f);
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                Size(figure, figureDp * px, TextAlignmentOptions.MidlineRight);
            }
        }

        /// <summary>A rect of <paramref name="size"/> centred on <paramref name="position"/> from its parent's centre.</summary>
        private static void Place(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static void Size(TMP_Text text, float size, TextAlignmentOptions alignment)
        {
            text.enableAutoSizing = false;
            text.fontSize = size;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>
        /// How many canvas units one dp is worth — <c>HudPresenter.PixelsPerDp</c>, for its reason.
        /// </summary>
        private float PixelsPerDp()
        {
            if (_pxPerDpOverride > 0f)
            {
                return _pxPerDpOverride;
            }

            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;

            return StickShaper.PixelsPerDp(Screen.dpi) / scale;
        }

        /// <remarks>
        /// Removed before it is added, and with a named method rather than a lambda, so that a
        /// component injected twice — which VContainer does not do and a test does — leaves for the
        /// Menu once rather than once per injection. <c>PausePresenter.Wire</c>'s trick, for its
        /// reason.
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
