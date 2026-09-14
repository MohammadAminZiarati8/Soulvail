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
