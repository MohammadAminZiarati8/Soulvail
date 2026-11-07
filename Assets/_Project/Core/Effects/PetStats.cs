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

    internal Stat BiteStunEvery { get; } = new Stat(0f);
    internal Stat BiteStunSeconds { get; } = new Stat(0.5f);

    public Stat Resolve(PlayerStat stat) => stat switch
    {
        PlayerStat.MaxHp => _pet.MaxHp,
        PlayerStat.MoveSpeed => _pet.MoveSpeed,
        PlayerStat.ContactDamage => _pet.ContactDamage,
        PlayerStat.PetHeelRegen => _pet.HeelRegen,
        PlayerStat.PetReturnAt => _pet.ReturnAt,
        PlayerStat.PetRescueSeconds => _pet.RescueSeconds,
        PlayerStat.PetBiteStunEvery => BiteStunEvery,
        PlayerStat.PetBiteStunSeconds => BiteStunSeconds,
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "The pet has no such stat."),
    };

    public bool Has(PlayerStat stat) =>
        stat is PlayerStat.MaxHp or PlayerStat.MoveSpeed or PlayerStat.ContactDamage
            or PlayerStat.PetHeelRegen or PlayerStat.PetReturnAt or PlayerStat.PetRescueSeconds
            or PlayerStat.PetBiteStunEvery or PlayerStat.PetBiteStunSeconds;
}
