using System.Numerics;

namespace Soulvail.Core.Events;

public readonly struct PetSpawned
{
    public readonly int Id;
    public readonly Vector3 Position;

    public readonly Vector3? PlayerPosition;
    public readonly float HealthFraction;

    public PetSpawned(int id, Vector3 position, Vector3? playerPosition = null, float healthFraction = 1f)
    {
        Id = id;
        Position = position;
        PlayerPosition = playerPosition;
        HealthFraction = healthFraction;
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
