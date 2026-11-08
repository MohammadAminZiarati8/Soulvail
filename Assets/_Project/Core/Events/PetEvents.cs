using System.Numerics;
using Soulvail.Core.Ai;

namespace Soulvail.Core.Events;

public readonly struct PetSpawned
{
    public readonly int Id;
    public readonly Vector3 Position;

    public readonly Vector3? PlayerPosition;
    public readonly float HealthFraction;
    public readonly bool IsLead;
    public readonly PetKind Kind;

    public PetSpawned(int id, Vector3 position, Vector3? playerPosition = null, float healthFraction = 1f,
        bool isLead = true, PetKind? kind = null)
    {
        Id = id;
        Position = position;
        PlayerPosition = playerPosition;
        HealthFraction = healthFraction;
        IsLead = isLead;
        Kind = kind ?? (isLead ? PetKind.Hound : PetKind.PackHound);
    }
}

/// <summary>BT-07a: a ranged pet loosed glob <see cref="GlobId"/> from <see cref="From"/> at an enemy.</summary>
public readonly struct PetSpat
{
    public readonly int PetId;
    public readonly int GlobId;
    public readonly Vector3 From;
    public readonly int TargetId;

    public PetSpat(int petId, int globId, Vector3 from, int targetId)
    {
        PetId = petId;
        GlobId = globId;
        From = from;
        TargetId = targetId;
    }
}

/// <summary>BT-07a: a glob ended — on its quarry (<see cref="Hit"/>), or on cover or a vanished target.</summary>
public readonly struct GlobLanded
{
    public readonly int GlobId;
    public readonly Vector3 Position;
    public readonly bool Hit;

    public GlobLanded(int globId, Vector3 position, bool hit)
    {
        GlobId = globId;
        Position = position;
        Hit = hit;
    }
}

public readonly struct PetDespawned
{
    public readonly int Id;

    public PetDespawned(int id) => Id = id;
}

public readonly struct Swapped
{
    public readonly int PetId;
    public readonly Vector3 PlayerFrom;
    public readonly Vector3 PlayerTo;
    public readonly float Seconds;

    public Swapped(int petId, Vector3 playerFrom, Vector3 playerTo, float seconds)
    {
        PetId = petId;
        PlayerFrom = playerFrom;
        PlayerTo = playerTo;
        Seconds = seconds;
    }
}

public readonly struct PetRoared
{
    public readonly int PetId;
    public readonly Vector3 Position;
    public readonly float Radius;

    public PetRoared(int petId, Vector3 position, float radius)
    {
        PetId = petId;
        Position = position;
        Radius = radius;
    }
}

public readonly struct PackHowled
{
    public readonly Vector3 Position;

    public PackHowled(Vector3 position) => Position = position;
}

public readonly struct PetsFrenzied
{
    public readonly float Seconds;
    public readonly float Bonus;

    public PetsFrenzied(float seconds, float bonus)
    {
        Seconds = seconds;
        Bonus = bonus;
    }
}

public readonly struct PetBit
{
    public readonly int PetId;
    public readonly int EnemyId;
    public readonly float Amount;

    public PetBit(int petId, int enemyId, float amount)
    {
        PetId = petId;
        EnemyId = enemyId;
        Amount = amount;
    }
}

public readonly struct PetRetargeted
{
    public readonly int PetId;
    public readonly int EnemyId;

    public PetRetargeted(int petId, int enemyId)
    {
        PetId = petId;
        EnemyId = enemyId;
    }
}

public readonly struct PetHurt
{
    public readonly int PetId;
    public readonly float Amount;
    public readonly int EnemyId;

    public PetHurt(int petId, float amount, int enemyId)
    {
        PetId = petId;
        Amount = amount;
        EnemyId = enemyId;
    }
}

public readonly struct PetFell
{
    public readonly int PetId;
    public readonly Vector3 Position;

    public PetFell(int petId, Vector3 position)
    {
        PetId = petId;
        Position = position;
    }
}

public readonly struct PetHeeled
{
    public readonly int PetId;

    public PetHeeled(int petId)
    {
        PetId = petId;
    }
}

public readonly struct PetReturned
{
    public readonly int PetId;

    public PetReturned(int petId)
    {
        PetId = petId;
    }
}

public readonly struct RescueProgressed
{
    public readonly int PetId;
    public readonly float Fraction;

    public RescueProgressed(int petId, float fraction)
    {
        PetId = petId;
        Fraction = fraction;
    }
}

public readonly struct PetRose
{
    public readonly int PetId;

    public PetRose(int petId)
    {
        PetId = petId;
    }
}

public readonly struct PetHunted
{
    public readonly int PetId;
    public readonly int EnemyId;
    public readonly bool Started;

    public PetHunted(int petId, int enemyId, bool started)
    {
        PetId = petId;
        EnemyId = enemyId;
        Started = started;
    }
}

public readonly struct PetPounced
{
    public readonly int PetId;
    public readonly int EnemyId;
    public readonly Vector3 From;
    public readonly Vector3 To;

    public PetPounced(int petId, int enemyId, Vector3 from, Vector3 to)
    {
        PetId = petId;
        EnemyId = enemyId;
        From = from;
        To = to;
    }
}

public readonly struct PetLanded
{
    public readonly int PetId;
    public readonly int EnemyId;

    public PetLanded(int petId, int enemyId)
    {
        PetId = petId;
        EnemyId = enemyId;
    }
}
