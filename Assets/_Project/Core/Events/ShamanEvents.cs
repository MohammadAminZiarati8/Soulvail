using System.Numerics;

namespace Soulvail.Core.Events;

public readonly struct ShamanCast
{
    public readonly int ShamanId;
    public readonly float Seconds;
    public ShamanCast(int shamanId, float seconds) { ShamanId = shamanId; Seconds = seconds; }
}

public readonly struct ShamanPulsed
{
    public readonly int ShamanId;
    public readonly Vector3 Position;
    public readonly float Radius;
    public readonly int Touched;
    public ShamanPulsed(int shamanId, Vector3 position, float radius, int touched)
    { ShamanId = shamanId; Position = position; Radius = radius; Touched = touched; }
}

public readonly struct EnemyHealed
{
    public readonly int EnemyId;
    public readonly float Amount;
    public readonly float HpFraction;
    public EnemyHealed(int enemyId, float amount, float hpFraction)
    { EnemyId = enemyId; Amount = amount; HpFraction = hpFraction; }
}
