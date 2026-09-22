using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// The progression module's domain events. Event structs are grouped per module — the one accepted
// exception to one type per file, because an event is three lines and reading a module's
// vocabulary in one place is worth more than the rule. RunEvents.cs' precedent. See AR §5, §8 and
// <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// Every event here says what happened, never what to do, and carries only what a listener cannot
// look up for itself. They are `readonly struct`s with public readonly fields and a constructor:
// no properties, no logic, no behaviour to go wrong, and nothing to allocate when they cross
// `IDomainEvents` by `in`.
//
// **The order between the two is load-bearing and belongs to the module rather than to either
// type**: every `LeveledUp` a grant earned is published first, in the order the thresholds were
// crossed, and exactly one `XpChanged` closes it. See `LevelTracker.Grant` for why.

/// <summary>
/// A level threshold was crossed. Published once per level, in order, before the
/// <see cref="XpChanged"/> that settles the bar.
/// </summary>
/// <remarks>
/// Several of these in one grant is ordinary rather than exceptional — a Bloater at stage 1 is a
/// quarter of a level, and a stage's last wave can pay for two at once. A screen that pauses on
/// this event (M3-08) therefore has to be able to show twice, which is what
/// <see cref="PendingLevelUps"/> is on the event for: it is the count the player is owed <em>as of
/// this level</em>, so the handler does not have to go and ask.
/// </remarks>
public readonly struct LeveledUp
{
    /// <summary>The level just reached. 2 the first time, and never below 2.</summary>
    public readonly int Level;

    /// <summary>
    /// How many picks the player is owed once this level is counted — earned and not yet spent.
    /// </summary>
    public readonly int PendingLevelUps;

    public LeveledUp(int level, int pendingLevelUps)
    {
        Level = level;
        PendingLevelUps = pendingLevelUps;
    }
}

/// <summary>
/// Experience moved. Published once per grant that changed anything, after any
/// <see cref="LeveledUp"/> the same grant earned — the settled state a bar draws.
/// </summary>
/// <remarks>
/// <see cref="Fraction"/> is always in <c>[0, 1)</c>, and the ordering is what makes that true: an
/// <see cref="XpChanged"/> published before the thresholds were resolved would carry a fraction
/// above 1, which is a bar asked to draw a number it cannot.
/// </remarks>
public readonly struct XpChanged
{
    /// <summary>
    /// How much was granted, after the <c>XpGain</c> stat — the number a floating "+12" would
    /// show, not the archetype's authored value.
    /// </summary>
    public readonly float Gained;

    /// <summary>The level after the grant.</summary>
    public readonly int Level;

    /// <summary>How far into that level the player now is, in <c>[0, 1)</c>.</summary>
    public readonly float Fraction;

    public XpChanged(float gained, int level, float fraction)
    {
        Gained = gained;
        Level = level;
        Fraction = fraction;
    }
}

/// <summary>
/// A tree node was taken. Published after it is recorded and its effects are on, so a handler
/// reads the state it is being told about.
/// </summary>
/// <remarks>
/// <para>
/// <b>It carries kind, branch, tier and count so that nothing reacting to it needs a catalog
/// lookup</b> — <c>EnemyDied</c>'s reasoning. A HUD flashing the branch's colour, M3-08's level-up
/// screen closing itself, and M3-09d's tree view filling a cell all know everything they need from
/// the five fields.
/// </para>
/// <para>
/// <b>Core does not listen to it.</b> M3-08's <c>ChooseOffer</c> tells M3-06's runner about a new
/// Active directly, because core has no business subscribing to its own events — <c>RunSession</c>'s
/// own remark on <c>PlayerDied</c>. <c>SkillTree.OwnedActives</c> is maintained by the take rather
/// than by a handler for the same reason.
/// </para>
/// <para>
/// <b>Nothing publishes this during a restore</b>, which is <c>SkillTree.Restore</c>'s rule: a
/// resumed run's nodes were picked in a previous session and are not news.
/// </para>
/// </remarks>
public readonly struct NodeTaken
{
    /// <summary>The node that was taken.</summary>
    public readonly ContentId SkillId;

    /// <summary>Which of CH §4's four kinds it is.</summary>
    public readonly SkillKind Kind;

    /// <summary>The branch it sits in, 0-based — an index into the tree's branches.</summary>
    public readonly int Branch;

    /// <summary>The tier it sits at, 1-based — CH §5's own numbering.</summary>
    public readonly int Tier;

    /// <summary>How many nodes are owned once this one is counted.</summary>
    public readonly int TakenCount;

    public NodeTaken(ContentId skillId, SkillKind kind, int branch, int tier, int takenCount)
    {
        SkillId = skillId;
        Kind = kind;
        Branch = branch;
        Tier = tier;
        TakenCount = takenCount;
    }
}

/// <summary>
/// A node left this run's pool for good — GD §13.3's Banish, bought in the Sanctum.
/// </summary>
/// <remarks>
/// <para>
/// <b>Beside <see cref="NodeTaken"/> because it is the same kind of fact</b> (M6-02b rule 8): one
/// node enters the player's build, the other leaves the pool, and a tree screen listens for both.
/// Published after the tree has recorded it, so a handler asking <c>IsNodeAvailable</c> from inside
/// it is told no.
/// </para>
/// <para>
/// <b>Nothing publishes it during a restore</b> — <c>SkillTree.RestoreBanished</c> is silent, for
/// <see cref="NodeTaken"/>'s reason.
/// </para>
/// </remarks>
public readonly struct NodeBanished
{
    /// <summary>The node that can no longer be offered.</summary>
    public readonly ContentId SkillId;

    public NodeBanished(ContentId skillId)
    {
        SkillId = skillId;
    }
}

/// <summary>
/// An offer is on the table: the player is being asked to choose, and the run stops being ticked
/// until they have (GD §11.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>It does not carry the ids.</b> They are <c>RunState.Offer</c>, a live view over the one buffer
/// the flow draws into — so a handler that kept this event would be keeping a count and a reason
/// rather than a stale list. This says <em>how many</em> and <em>what it is for</em>; the screen
/// reads what they are.
/// </para>
/// <para>
/// <see cref="Count"/> is not always three: a tree with fewer nodes available than that offers what
/// it has, and CH §5.1's screen draws the cards it is given (M3-04 rule 1).
/// </para>
/// </remarks>
public readonly struct OfferPresented
{
    /// <summary>How many cards are on the table, from 1 to <c>OfferGenerator.DefaultOfferCount</c>.</summary>
    public readonly int Count;

    /// <summary>
    /// How many picks the player is owed <em>including</em> this one — CH §5.1's <em>"pick 1 of
    /// n"</em>, so the screen does not have to go and ask.
    /// </summary>
    public readonly int PicksOwed;

    public OfferPresented(int count, int picksOwed)
    {
        Count = count;
        PicksOwed = picksOwed;
    }
}

/// <summary>
/// Nothing more is owed. The screen closes and the run is ticked again.
/// </summary>
/// <remarks>
/// Published after the last pick is spent, whether it went on a node or on Overflow — so a screen
/// that opened on <see cref="OfferPresented"/> has exactly one event that closes it, rather than
/// having to work out from a count whether another card is coming.
/// </remarks>
public readonly struct LevelUpClosed
{
    /// <summary>The level the run is at once every pick has been spent.</summary>
    public readonly int Level;

    public LevelUpClosed(int level)
    {
        Level = level;
    }
}

/// <summary>
/// CH §5.4's half-tree moment is on the table: the run stops and asks which class it borrows a
/// branch from, and it is the only question in the game the player cannot decline.
/// </summary>
/// <remarks>
/// <para>
/// <b>It does not carry the candidates.</b> They are <c>RunState.SplashCandidates</c>, built once
/// when the run started — so a handler that kept this event would be keeping a count rather than a
/// stale list. <c>OfferPresented</c>'s rule, for its reason: this says <em>why it fired</em>, and
/// the screen reads what there is to choose from.
/// </para>
/// <para>
/// <b>It exists so the moment can be traced without reading the tree.</b> The two numbers are the
/// whole of rule 2's condition, so a log line says <em>"14 of 14"</em> rather than leaving the
/// reader to recompute <c>ceil(NodeCount × 0.5)</c> for whichever class was being played.
/// </para>
/// <para>
/// <b>Nothing publishes this during a restore</b>, which is <c>SkillTree.Restore</c>'s rule: a
/// branch borrowed in a previous session is not news.
/// </para>
/// </remarks>
public readonly struct SplashOffered
{
    /// <summary>How many nodes of the primary tree are taken as of this moment.</summary>
    public readonly int TakenCount;

    /// <summary>
    /// How many it takes to open the moment — <c>ceil(NodeCount × <c>SplashFlow.Threshold</c>)</c>,
    /// six against v1's twelve-node tree.
    /// </summary>
    public readonly int Threshold;

    public SplashOffered(int takenCount, int threshold)
    {
        TakenCount = takenCount;
        Threshold = threshold;
    }
}

/// <summary>
/// A branch of a second class was borrowed, for the rest of the run. Published after the branch is
/// installed and the tree has been rebuilt around it, so a handler reads the state it is being told
/// about.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Branch"/> is the borrowed class's own index, never
/// <c>TreeRules.SplashBranch</c>.</b> The run's index is 3 whichever branch was taken, so it could
/// not say which one the player chose; this one can, and a readout that wants the run's index has
/// it on <c>NodeTaken.Branch</c> the first time a borrowed node is picked.
/// </para>
/// <para>
/// <b>It carries what it granted</b> — <see cref="NodesGained"/> is the post-Keystone count
/// (CH §5.4's <em>"one branch minus its Keystone is 7"</em>), which is what a readout draws and what
/// a later achievement counts. Nothing publishes it during a restore, for <c>NodeTaken</c>'s reason.
/// </para>
/// </remarks>
public readonly struct SplashChosen
{
    /// <summary>The class the branch came from.</summary>
    public readonly ContentId CharacterId;

    /// <summary>Which of that class's branches, 0-based — an index into its own tree.</summary>
    public readonly int Branch;

    /// <summary>How many nodes the run gained, the Keystone already dropped.</summary>
    public readonly int NodesGained;

    public SplashChosen(ContentId characterId, int branch, int nodesGained)
    {
        CharacterId = characterId;
        Branch = branch;
        NodesGained = nodesGained;
    }
}

/// <summary>
/// A pick was spent with nothing left to offer, and paid out as CH §5.2's Overflow instead.
/// </summary>
/// <remarks>
/// <b>Announced rather than shown.</b> Overflow is silent and instant — no pause, no screen — because
/// GD §13.1's pause exists to let someone <em>choose</em>, and a card with one button would tax the
/// player for the game having run out of nodes. This event is what keeps CH §5.2's <em>"levelling
/// never stops meaning something"</em> visible without stopping the game: M3-10's HUD or M3-13 can
/// toast it. Nothing publishes it during a restore, for <c>NodeTaken</c>'s reason.
/// </remarks>
public readonly struct OverflowGranted
{
    /// <summary>The level whose pick was spent on it.</summary>
    public readonly int Level;

    /// <summary>
    /// How many Overflow levels the run has now, this one counted — the running total, so a toast
    /// can say <em>"+2 % damage (×7)"</em> without keeping a tally of its own.
    /// </summary>
    public readonly int Total;

    public OverflowGranted(int level, int total)
    {
        Level = level;
        Total = total;
    }
}
