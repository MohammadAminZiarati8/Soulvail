namespace Soulvail.Core.Events;

/// <summary>A crowd-control state an enemy can be under (MK-06a).</summary>
public enum EnemyStatus
{
    Stunned,
    Rooted,
    Slowed,
}

/// <summary>
/// An enemy's status started, lengthened or ended. <see cref="Seconds"/> is what remains, 0 for ended.
/// Death ends every status silently: nothing follows an <c>EnemyDied</c>.
/// </summary>
public readonly struct EnemyStatusChanged
{
    public readonly int EnemyId;
    public readonly EnemyStatus Status;
    public readonly float Seconds;

    public EnemyStatusChanged(int enemyId, EnemyStatus status, float seconds)
    {
        EnemyId = enemyId;
        Status = status;
        Seconds = seconds;
    }
}
