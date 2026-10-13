using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and TreeView.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
//
// Two types in one file, `SkillTreeSpec.cs`'s reason: a node state outside a node cell is not a
// thing the game has, and nothing but this class ever produces or consumes one.
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// What one node of the tree is to <em>this</em> run: owned, takeable now, gone, or none of those
    /// (CH §5).
    /// </summary>
    /// <remarks>
    /// UI-03c's spec writes three members; <see cref="Banished"/> stays as the fourth, because a
    /// drawing mode still has the Sanctum's Banish (M6-03b) and a free one simply never produces it.
    /// Appended order is kept, so no existing member's value moves.
    /// </remarks>
    public enum NodeState
    {
        /// <summary>Not owned and not takeable: a tier gate, a keystone's branch, or a parent.</summary>
        Locked,

        /// <summary>Takeable on the next pick — <c>RunState.IsNodeAvailable</c>.</summary>
        Available,

        /// <summary>Already owned this run — one entry of <c>RunState.TakenNodeIds</c>.</summary>
        Taken,

        /// <summary>
        /// Taken out of this run's offer pool by a Sanctum Banish — one entry of
        /// <c>RunState.BanishedNodeIds</c>. Never taken and never available (M6-02b rules 4, 5).
        /// </summary>
        Banished,
    }

    /// <summary>
    /// One cell of the tree screen: a name, a description, a stripe for CH §4's kind, a frame for its
    /// state, a padlock while locked — and, in a free run with a point banked, a button (UI-03c).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It reports a tap and decides nothing.</b> <see cref="Show"/> says whether the cell is
    /// takeable; the button is interactable and the outline lit exactly then, and a tap is handed to
    /// the presenter, which asks core again before it sends <c>TakeNode</c> (UI-03c rule 4). A
    /// drawing run's cells are never takeable, which is rule 7's read-only view.
    /// </para>
    /// <para>
    /// Replaced <c>TreeNodeView</c> in place, GUID and all, so the prefab's cell kept its four
    /// references. Every piece is written through a Unity-null check rather than assumed:
    /// <c>TreeViewPresenter.Start</c> refuses an undressed screen by name, and this class only has to
    /// not throw on the way there — <c>OfferCard</c>'s bargain.
    /// </para>
    /// </remarks>
    public sealed class TreeNodeButton : MonoBehaviour
    {
        [Tooltip("The node's name, resolved through ILocalizer.")]
        [SerializeField] private TMP_Text _name;

        [Tooltip("What the node does, resolved the same way.")]
        [SerializeField] private TMP_Text _description;

        [Tooltip("The cell's body, tinted by the four node-state colours — the thing a player reads " +
                 "their own path off.")]
        [SerializeField] private Image _frame;

        [Tooltip("The stripe that says which of CH §4's four kinds this is — OfferCard's stripe.")]
        [SerializeField] private Image _kindStrip;

        [Tooltip("What a tap lands on. Interactable only while the node can be taken (UI-03c rule 4).")]
        [SerializeField] private Button _button;

        [Tooltip("The Palette.Available outline, on only while the node can be taken (rule 3).")]
        [SerializeField] private Outline _outline;

        [Tooltip("The padlock drawn over a locked node (rule 3). Placeholder art until the owner's.")]
        [SerializeField] private GameObject _padlock;

        /// <summary>Which node this cell is currently drawing, or <c>default</c> while hidden.</summary>
        private ContentId _node;

        /// <summary>What it was last drawn as. <c>Locked</c> while hidden.</summary>
        private NodeState _state;

        private bool _takeable;

        /// <summary>The presenter's handler, handed the node on a tap.</summary>
        private Action<ContentId> _tapped;

        /// <summary>Whether the cell is currently drawn.</summary>
        public bool IsShown => gameObject.activeSelf;

        /// <summary>The node this cell is drawing, or <c>default</c> while it is hidden.</summary>
        public ContentId Node => _node;

        /// <summary>How it was last drawn — the read every state row asserts against.</summary>
        public NodeState State => _state;

        /// <summary>Whether a tap would be sent on — Available, free, and a point banked.</summary>
        public bool IsTakeable => _takeable;

        /// <summary>
        /// Hands the cell the one thing it reports to. Called once per clone by the presenter that
        /// built it; a cell is a runtime clone that nothing injects (M3-14a rule 11).
        /// </summary>
        public void Bind(Action<ContentId> tapped)
        {
            _tapped = tapped;

            if (_button != null)
            {
                _button.onClick.RemoveListener(OnClick);
                _button.onClick.AddListener(OnClick);
            }
        }

        /// <summary>
        /// Draws <paramref name="spec"/> as one cell, framed by <paramref name="state"/>.
        /// </summary>
        /// <param name="spec">The node to draw. Any of CH §4's four kinds.</param>
        /// <param name="state">What it is to this run — one of the four states.</param>
        /// <param name="localizer">What turns the spec's two <c>LocKey</c>s into words.</param>
        /// <param name="takeable">
        /// Whether a tap may take it. The presenter's answer, and false for anything but an
        /// Available node in a free run with a point banked.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="spec"/> or <paramref name="localizer"/> is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="state"/> is not one of the four — see <see cref="Frame"/>.
        /// </exception>
        public void Show(SkillSpec spec, NodeState state, ILocalizer localizer, bool takeable = false)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(
                    nameof(spec),
                    "A cell with no node to draw is a presenter that walked past the end of the "
                        + "tree — SkillTreeSpec.NodeCount is the bound, and it is what the pool was "
                        + "built to.");
            }

            if (localizer is null)
            {
                throw new ArgumentNullException(
                    nameof(localizer),
                    "A cell with no localizer would draw two keys, and this screen draws a whole tree "
                        + "at once (M3-14a rule 1).");
            }

            // Resolved before anything is written, so the loud default below is reached whether or
            // not this cell happens to have a frame dressed onto it.
            Color frame = Frame(state);

            _node = spec.Id;
            _state = state;
            _takeable = takeable && state == NodeState.Available;

            if (_name != null)
            {
                _name.text = localizer.Get(spec.NameKey);
            }

            if (_description != null)
            {
                _description.text = localizer.Get(spec.DescriptionKey);
            }

            if (_frame != null)
            {
                _frame.color = frame;
            }

            if (_kindStrip != null)
            {
                _kindStrip.color = Tint(spec.Kind);
            }

            if (_button != null)
            {
                _button.interactable = _takeable;
            }

            if (_outline != null)
            {
                _outline.effectColor = Palette.Available;
                _outline.enabled = _takeable;
            }

            if (_padlock != null)
            {
                _padlock.SetActive(state == NodeState.Locked);
            }

            gameObject.SetActive(true);
        }

        /// <summary>The name's and the description's sizes, in canvas units the presenter worked out from dp.</summary>
        public void SizeText(float name, float description)
        {
            if (_name != null)
            {
                _name.fontSize = name;
            }

            if (_description != null)
            {
                _description.fontSize = description;
            }
        }

        /// <summary>
        /// Takes the cell off the screen — a pooled cell past the end of a tree shorter than the one
        /// the pool was built for.
        /// </summary>
        public void Hide()
        {
            _node = default;
            _state = NodeState.Locked;
            _takeable = false;

            if (_button != null)
            {
                _button.interactable = false;
            }

            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            _tapped = null;

            if (_button != null)
            {
                _button.onClick.RemoveListener(OnClick);
            }
        }

        /// <summary>
        /// A tap on a cell that is not takeable reports nothing — <c>onClick</c> fires on an
        /// <c>Invoke</c> whatever <c>interactable</c> says, so the guard is here as well (rule 4).
        /// </summary>
        private void OnClick()
        {
            if (_takeable)
            {
                _tapped?.Invoke(_node);
            }
        }

        /// <summary>The four node states, in <see cref="Palette"/>'s four.</summary>
        /// <remarks>
        /// <b>A member with no colour throws, where <see cref="Tint"/>'s falls back</b>: a new state
        /// without a colour would draw as <em>locked</em>, which is a node the player owns reading as
        /// one they cannot reach, silently. <c>PlayerStats.Resolve</c>'s rule.
        /// </remarks>
        private static Color Frame(NodeState state) => state switch
        {
            NodeState.Locked => Palette.NodeLocked,
            NodeState.Available => Palette.NodeAvailable,
            NodeState.Taken => Palette.NodeTaken,
            NodeState.Banished => Palette.NodeBanished,

            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "No frame colour is written for this node state. A member added here without a "
                    + "line would draw as Locked, which reads as a node the player cannot reach."),
        };

        /// <summary>CH §4's four kinds, in <see cref="Palette"/>'s four tints — <c>OfferCard.Tint</c>'s values.</summary>
        private static Color Tint(SkillKind kind) => kind switch
        {
            SkillKind.Passive => Palette.KindPassive,
            SkillKind.Active => Palette.KindActive,
            SkillKind.Upgrade => Palette.KindUpgrade,
            SkillKind.Keystone => Palette.KindKeystone,
            _ => Palette.KindPassive,
        };
    }
}
