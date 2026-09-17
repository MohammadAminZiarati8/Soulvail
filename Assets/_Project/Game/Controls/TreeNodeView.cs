using System;
using Soulvail.Core.Content;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and TreeView.prefab's reference to this component would
// silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
//
// Two types in one file, `SkillTreeSpec.cs`'s reason: a node state outside a node cell is not a
// thing the game has, and nothing but this class ever produces or consumes one.
namespace Soulvail.Game.Controls
{
    /// <summary>
    /// What one node of the tree is to <em>this</em> run: owned, takeable now, or neither (CH §5).
    /// </summary>
    /// <remarks>
    /// Three rather than four, and the fourth is named so its absence is a decision: M6-02's Banish
    /// gives <c>SkillTree.IsAvailable</c> a second <see cref="bool"/><c>[]</c> behind it and this
    /// enum a <c>Banished</c> member, added by the task that adds the mechanic (M3-09d's
    /// <em>Out of scope</em>).
    /// </remarks>
    public enum NodeState
    {
        /// <summary>Not owned and not takeable: a tier gate, a keystone's branch, or a parent.</summary>
        Locked,

        /// <summary>Takeable on the next pick — <c>RunState.IsNodeAvailable</c>.</summary>
        Available,

        /// <summary>Already owned this run — one entry of <c>RunState.TakenNodeIds</c>.</summary>
        Taken,
    }

    /// <summary>
    /// One cell of CH §5.1's tree view: a name, a description, a stripe saying which of CH §4's four
    /// kinds it is, and a frame saying whether the player owns it, may take it, or neither.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>It is not a button and that is rule 1 made structural.</b> CH §5.1 chose random-from-
    /// available <em>over</em> free-pick, so a tree you could tap to buy from would be the screen the
    /// design refused arriving through the back door. <c>OfferCard</c> carries a
    /// <see cref="Button"/> because a card is a choice; this carries none, so there is nothing here
    /// for a later task to wire a command to without deleting a field first.
    /// </para>
    /// <para>
    /// <b>It draws the <c>LocKey</c>, because nothing in the build resolves one</b> —
    /// <c>OfferCard</c>'s paragraph, and this is ledger row 9's <em>fourth</em> reader and by a long
    /// way its densest: a card shows three keys for two seconds and a full tree shows twenty-seven at
    /// once. <c>ILocalizer</c> is an AR §6 port whose table is M3-14a's, so a cell reads
    /// <c>skill.oathbound.consecrate</c> rather than "Consecrate" (ADR-0012). GD §13.1's
    /// <em>"readable in under two seconds"</em> is judged on a card; whether a <em>tree</em> of keys
    /// is navigable at all is a question only M6-10 can answer, and M3-15 rules on the whole of the
    /// row rather than on the cards alone.
    /// </para>
    /// <para>
    /// <b>Seven serialized <see cref="Color"/>s until M3-13a, and none now</b> (M3-09d rule 8).
    /// Three were this class's own — the frame's locked, available and taken — and four were the
    /// <em>same placeholder values</em> <c>OfferCard</c> shipped for CH §4's four kinds, copied
    /// value for value. That made this the largest single reader ledger row 6 ever collected, and
    /// the shared object it was waiting for is <see cref="Palette"/>: all seven are now one name
    /// each, and a Keystone is the same colour on the card that offered it and the tree that holds
    /// it because there is only one value left to be the same.
    /// </para>
    /// <para>
    /// Every piece is written through a Unity-null check rather than assumed. A cell is a clone of
    /// the prefab's template and <c>TreeViewPresenter.Start</c> is what refuses an undressed screen
    /// by name; this class only has to not throw on the way there — <c>OfferCard</c>'s bargain.
    /// </para>
    /// </remarks>
    public sealed class TreeNodeView : MonoBehaviour
    {
        [Tooltip("The node's name. Draws its LocKey until M3-14a — see the class remarks.")]
        [SerializeField] private TMP_Text _name;

        [Tooltip("What the node does. Draws its LocKey until M3-14a, for the same reason.")]
        [SerializeField] private TMP_Text _description;

        [Tooltip("The cell's border, tinted by the three state colours below. This is the thing a " +
                 "player reads their own path off, so it is the one piece that has to differ at a " +
                 "glance.")]
        [SerializeField] private Image _frame;

        [Tooltip("The stripe that says which of CH §4's four kinds this is, tinted from the four " +
                 "fields below — OfferCard's stripe, on the other screen.")]
        [SerializeField] private Image _kindStrip;

        /// <summary>Which node this cell is currently drawing, or <c>default</c> while hidden.</summary>
        private ContentId _skillId;

        /// <summary>What it was last drawn as. <c>Locked</c> while hidden.</summary>
        private NodeState _state;

        /// <summary>Whether the cell is currently drawn. <c>Prefab_IsDressed</c>'s reachable read.</summary>
        public bool IsShown => gameObject.activeSelf;

        /// <summary>The node this cell is drawing, or <c>default</c> while it is hidden.</summary>
        public ContentId SkillId => _skillId;

        /// <summary>How it was last drawn — the read every state row asserts against.</summary>
        public NodeState State => _state;

        /// <summary>
        /// Draws <paramref name="spec"/> as one cell, framed by <paramref name="state"/>.
        /// </summary>
        /// <param name="spec">The node to draw. Any of CH §4's four kinds.</param>
        /// <param name="state">What it is to this run — rule 3's three answers.</param>
        /// <exception cref="ArgumentNullException"><paramref name="spec"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="state"/> is not one of the three. Loud rather than silent, for
        /// <c>PlayerStats.Resolve</c>'s reason — see <see cref="Frame"/>.
        /// </exception>
        public void Show(SkillSpec spec, NodeState state)
        {
            if (spec is null)
            {
                throw new ArgumentNullException(
                    nameof(spec),
                    "A cell with no node to draw is a presenter that walked past the end of the "
                        + "tree — SkillTreeSpec.NodeCount is the bound, and it is what the pool was "
                        + "built to.");
            }

            // Resolved before anything is written, so the loud default below is reached whether or
            // not this cell happens to have a frame dressed onto it.
            Color frame = Frame(state);

            _skillId = spec.Id;
            _state = state;

            if (_name != null)
            {
                // The key, not English. See the class remarks and ledger row 9.
                _name.text = spec.NameKey.Key;
            }

            if (_description != null)
            {
                _description.text = spec.DescriptionKey.Key;
            }

            if (_frame != null)
            {
                _frame.color = frame;
            }

            if (_kindStrip != null)
            {
                _kindStrip.color = Tint(spec.Kind);
            }

            gameObject.SetActive(true);
        }

        /// <summary>
        /// Takes the cell off the screen — a pooled cell past the end of a tree shorter than the one
        /// the pool was built for.
        /// </summary>
        /// <remarks>
        /// There is no listener to drop, unlike <c>OfferCard.Hide</c> and <c>SkillRow.Hide</c>, and
        /// that is rule 1 again rather than an omission: a cell reports nothing to anybody.
        /// </remarks>
        public void Hide()
        {
            _skillId = default;
            _state = NodeState.Locked;

            gameObject.SetActive(false);
        }

        /// <summary>Rule 3's three states, in <see cref="Palette"/>'s three.</summary>
        /// <remarks>
        /// <b>A member with no colour throws, where <see cref="Tint"/>'s falls back</b>, and the two
        /// answers differ because the enums do. <see cref="SkillKind"/> is CH §4's closed four,
        /// validated at authoring time and shared with <c>OfferCard</c>, so a fallback there is one
        /// answer to a question already settled elsewhere. <see cref="NodeState"/> is this file's
        /// own, and M6-02 is already named as the task that adds a member to it — a new state
        /// without a colour would draw as <em>locked</em>, which is a node the player owns reading
        /// as one they cannot reach, silently. <c>PlayerStats.Resolve</c>'s rule. <b>Moving the
        /// three colours into the palette does not move that rule</b>: the throw is about this
        /// enum's members, not about where their colours are kept, and M6-02's <c>Banished</c> still
        /// has to add a line here as well as a member there.
        /// </remarks>
        private static Color Frame(NodeState state) => state switch
        {
            NodeState.Locked => Palette.NodeLocked,
            NodeState.Available => Palette.NodeAvailable,
            NodeState.Taken => Palette.NodeTaken,

            _ => throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "No frame colour is written for this node state. A member added here without a "
                    + "line would draw as Locked, which reads as a node the player cannot reach."),
        };

        /// <summary>CH §4's four kinds, in <see cref="Palette"/>'s four tints.</summary>
        /// <remarks>
        /// <c>OfferCard.Tint</c>'s body, and as of M3-13a against <c>OfferCard.Tint</c>'s own
        /// <em>values</em> rather than against a copy of them: it is the same lookup on the same
        /// closed enum, and two answers to one question is how a Keystone ends up a different colour
        /// on two screens. That was seven serialized <see cref="Color"/>s here until this task and is
        /// the largest single reader ledger row 6 collected. A <c>switch</c> on a
        /// <see cref="SkillKind"/> and not on an effect type — the banned shape is
        /// <c>switch (effect.Type)</c>, which is a dispatch that should have been polymorphism.
        /// </remarks>
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
