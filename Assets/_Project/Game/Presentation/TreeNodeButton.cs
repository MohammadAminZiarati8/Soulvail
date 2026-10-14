using System;
using Soulvail.Core.Content;
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
    /// One tile of the tree screen: a state-plated tile, the skill's icon, a badge at its top-right
    /// corner and the node's tier as a numeral at the bottom-left — and, in a free run with a point
    /// banked, a button (UI-04c, over UI-03c).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It reports a tap and decides nothing.</b> A tap on any shown tile is handed to the
    /// presenter, which selects it (UI-04d); <see cref="Show"/> says whether the node is takeable
    /// only so the plus badge shows, and the presenter asks core again before it sends <c>TakeNode</c>.
    /// </para>
    /// <para>
    /// <b>The tile art carries the state's colour</b>, so it is drawn untinted; the one tint left is
    /// the icon's, dimmed while the node is out of reach. The name and description left the tile with
    /// UI-04c: UI-04d's selection panel is where a node is read.
    /// </para>
    /// <para>
    /// Every piece is written through a Unity-null check rather than assumed:
    /// <c>TreeViewPresenter.Start</c> refuses an undressed screen by name, and this class only has to
    /// not throw on the way there — <c>OfferCard</c>'s bargain.
    /// </para>
    /// </remarks>
    public sealed class TreeNodeButton : MonoBehaviour
    {
        /// <summary>The tier numerals; past the table a tier is written as its number.</summary>
        private static readonly string[] Numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

        [Tooltip("The tile: TreeTile_Taken, _Available or _Locked, by state. Drawn untinted.")]
        [SerializeField] private Image _tile;

        [Tooltip("The skill's icon, centred on the tile. Dimmed to Palette.NodeLocked while out of reach.")]
        [SerializeField] private Image _icon;

        [Tooltip("The badge at the tile's top-right: check, plus or lock; off when there is none.")]
        [SerializeField] private Image _badge;

        [Tooltip("The node's tier in its branch, as a numeral, at the tile's bottom-left.")]
        [SerializeField] private TMP_Text _numeral;

        [Tooltip("What a tap lands on. Interactable whenever the tile is shown: a tap selects (UI-04d rule 1).")]
        [SerializeField] private Button _button;

        [Tooltip("TreeTile_SelectedRing, stretched over the tile; shown while this is the selected node.")]
        [SerializeField] private Image _ring;

        [SerializeField] private Sprite _takenTile;
        [SerializeField] private Sprite _availableTile;
        [SerializeField] private Sprite _lockedTile;
        [SerializeField] private Sprite _checkBadge;
        [SerializeField] private Sprite _plusBadge;
        [SerializeField] private Sprite _lockBadge;

        /// <summary>Which node this tile is currently drawing, or <c>default</c> while hidden.</summary>
        private ContentId _node;

        /// <summary>What it was last drawn as. <c>Locked</c> while hidden.</summary>
        private NodeState _state;

        private bool _takeable;

        private bool _selected;

        /// <summary>The presenter's handler, handed the node on a tap.</summary>
        private Action<ContentId> _tapped;

        /// <summary>Whether the tile is currently drawn.</summary>
        public bool IsShown => gameObject.activeSelf;

        /// <summary>The node this tile is drawing, or <c>default</c> while it is hidden.</summary>
        public ContentId Node => _node;

        /// <summary>How it was last drawn — the read every state row asserts against.</summary>
        public NodeState State => _state;

        /// <summary>Whether a tap would be sent on — Available, free, and a point banked.</summary>
        public bool IsTakeable => _takeable;

        /// <summary>Whether the selection ring is showing on this tile.</summary>
        public bool IsSelected => _selected;

        /// <summary>The tier numeral for a tier from 1: "I", "II"…; past the table, its number.</summary>
        public static string NumeralFor(int tier) =>
            tier >= 1 && tier <= Numerals.Length ? Numerals[tier - 1] : tier.ToString();

        /// <summary>Puts the selection ring on or off this tile (UI-04d rule 1).</summary>
        public void SetSelected(bool selected)
        {
            _selected = selected;

            if (_ring != null)
            {
                _ring.gameObject.SetActive(selected);
            }
        }

        /// <summary>
        /// Hands the tile the one thing it reports to. Called once per clone by the presenter that
        /// built it; a tile is a runtime clone that nothing injects (M3-14a rule 11).
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

        /// <summary>Draws <paramref name="spec"/> as one tile, plated by <paramref name="state"/>.</summary>
        /// <param name="spec">The node to draw.</param>
        /// <param name="state">What it is to this run.</param>
        /// <param name="icon">The skill's icon, or the placeholder the block chose for it.</param>
        /// <param name="tier">The node's tier in its branch, from 1.</param>
        /// <param name="takeable">
        /// Whether a tap may take it, and so whether the plus badge shows. The presenter's answer, and
        /// false for anything but an Available node in a free run with a point banked.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="state"/> is not one of the four — see <see cref="TileFor"/>.
        /// </exception>
        public void Show(SkillSpec spec, NodeState state, Sprite icon, int tier, bool takeable = false)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(
                    nameof(spec),
                    "A tile with no node to draw is a presenter that walked past the end of the "
                        + "tree — SkillTreeSpec.NodeCount is the bound, and it is what the pool was "
                        + "built to.");
            }

            // Resolved before anything is written, so the loud default is reached whether or not
            // this tile happens to have its art dressed onto it.
            Sprite tile = TileFor(state);

            _node = spec.Id;
            _state = state;
            _takeable = takeable && state == NodeState.Available;

            if (_tile != null)
            {
                _tile.sprite = tile;
                _tile.color = Color.white;
            }

            if (_icon != null)
            {
                _icon.sprite = icon;
                _icon.color = state is NodeState.Locked or NodeState.Banished ? Palette.NodeLocked : Color.white;
            }

            if (_badge != null)
            {
                Sprite badge = BadgeFor(state, _takeable);

                _badge.sprite = badge;
                _badge.gameObject.SetActive(badge != null);
            }

            if (_numeral != null)
            {
                _numeral.text = NumeralFor(tier);
            }

            if (_button != null)
            {
                _button.interactable = true;
            }

            gameObject.SetActive(true);
        }

        /// <summary>
        /// Sizes the tile's pieces from its own side, in canvas units the presenter worked out from dp:
        /// the icon inset, the badge in the top-right corner, the numeral in the bottom-left.
        /// </summary>
        public void Size(float tile, float numeralFont)
        {
            if (_icon != null)
            {
                Place(_icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), tile * 0.62f, Vector2.zero);
            }

            if (_badge != null)
            {
                Place(_badge.rectTransform, Vector2.one, Vector2.one, tile * 0.38f, Vector2.zero);
            }

            if (_numeral != null)
            {
                Place(_numeral.rectTransform, Vector2.zero, Vector2.zero, tile * 0.4f, new Vector2(tile * 0.06f, tile * 0.04f));
                _numeral.fontSize = numeralFont;
                _numeral.alignment = TextAlignmentOptions.BottomLeft;
            }
        }

        /// <summary>
        /// Takes the tile off the screen — a pooled tile past the end of a tree shorter than the one
        /// the pool was built for.
        /// </summary>
        public void Hide()
        {
            _node = default;
            _state = NodeState.Locked;
            _takeable = false;
            SetSelected(false);

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
        /// A tap on a shown tile reports its node, in any state (UI-04d rule 1); a hidden tile has
        /// none to report.
        /// </summary>
        private void OnClick()
        {
            if (IsShown)
            {
                _tapped?.Invoke(_node);
            }
        }

        /// <summary>The tile for a state; a banished node wears the locked one (UI-04c rule 2).</summary>
        /// <remarks>
        /// <b>A member with no tile throws</b>: a new state without one would draw as <em>locked</em>,
        /// which is a node the player owns reading as one they cannot reach, silently.
        /// <c>PlayerStats.Resolve</c>'s rule.
        /// </remarks>
        private Sprite TileFor(NodeState state) => state switch
        {
            NodeState.Taken => _takenTile,
            NodeState.Available => _availableTile,
            NodeState.Locked or NodeState.Banished => _lockedTile,

            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "No tile is written for this node state. A member added here without a line would "
                    + "draw as Locked, which reads as a node the player cannot reach."),
        };

        /// <summary>Check when taken, plus while a point can take it, lock when out of reach (rule 4).</summary>
        private Sprite BadgeFor(NodeState state, bool takeable) => state switch
        {
            NodeState.Taken => _checkBadge,
            NodeState.Available => takeable ? _plusBadge : null,
            _ => _lockBadge,
        };

        private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, float size, Vector2 position)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = position;
        }
    }
}
