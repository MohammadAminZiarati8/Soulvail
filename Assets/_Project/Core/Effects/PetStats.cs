using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

public sealed class PetStats : IStatBlock
{
    private readonly PetAgent _pet;

    public PetStats(PetAgent pet)
    {
        _pet = pet ?? throw new ArgumentNullException(nameof(pet));
    }

    public Stat Resolve(PlayerStat stat) => stat switch
    {
        PlayerStat.MaxHp => _pet.MaxHp,
        PlayerStat.MoveSpeed => _pet.MoveSpeed,
        PlayerStat.ContactDamage => _pet.ContactDamage,
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "The pet has no such stat."),
    };

    public bool Has(PlayerStat stat) =>
        stat is PlayerStat.MaxHp or PlayerStat.MoveSpeed or PlayerStat.ContactDamage;
}
