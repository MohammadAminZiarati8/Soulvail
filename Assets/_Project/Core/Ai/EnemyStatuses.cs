using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Ai;

/// <summary>The ids and rules of the statuses an enemy can be under (MK-06a, JG-16b).</summary>
public sealed class EnemyStatuses
{
    public const string StunnedKey = "status.enemy.stunned";
    public const string RootedKey = "status.enemy.rooted";
    public const string SlowedKey = "status.enemy.slowed";
    public const string HastedKey = "status.enemy.hasted";
    public const string PoisonedKey = "status.enemy.poisoned";

    public const string MarkedKey = "status.enemy.marked";

    // JG-26a: no boss is stunned, rooted or slowed.
    private const float CrowdControlBossScale = 0f;

    public EnemyStatuses()
    {
        Stunned = new StatusRule(new ContentId(StunnedKey), StatusSide.Debuff, 40, CrowdControlBossScale);
        Rooted = new StatusRule(new ContentId(RootedKey), StatusSide.Debuff, 30, CrowdControlBossScale);
        Slowed = new StatusRule(new ContentId(SlowedKey), StatusSide.Debuff, 20, CrowdControlBossScale);
        Hasted = new StatusRule(new ContentId(HastedKey), StatusSide.Buff, 10, 1f);
        Poisoned = new StatusRule(new ContentId(PoisonedKey), StatusSide.Debuff, 25, 1f);
        Marked = new StatusRule(new ContentId(MarkedKey), StatusSide.Debuff, 5, 1f);
    }

    public StatusRule Stunned { get; }

    public StatusRule Rooted { get; }

    public StatusRule Slowed { get; }

    public StatusRule Hasted { get; }

    public StatusRule Poisoned { get; }

    public StatusRule Marked { get; }

    /// <returns>False for an unrecognized id.</returns>
    public bool TryGet(ContentId id, out StatusRule rule)
    {
        string key = id.Value;

        if (key == StunnedKey)
        {
            rule = Stunned;
        }
        else if (key == RootedKey)
        {
            rule = Rooted;
        }
        else if (key == SlowedKey)
        {
            rule = Slowed;
        }
        else if (key == HastedKey)
        {
            rule = Hasted;
        }
        else if (key == PoisonedKey)
        {
            rule = Poisoned;
        }
        else if (key == MarkedKey)
        {
            rule = Marked;
        }
        else
        {
            rule = default;

            return false;
        }

        return true;
    }
}
