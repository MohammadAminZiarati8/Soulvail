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

    internal Stat SwapRoarSlow { get; } = new Stat(0f);

    internal Stat PackHounds { get; } = new Stat(0f);
    internal Stat PackShare { get; } = new Stat(0.5f);

    /// <summary>Retired by BT-07a: Pack Tactics is gone, nothing reads it.</summary>
    internal Stat PackTactics { get; } = new Stat(0f);
    /// <summary>Retired by BT-07a: Pack Tactics is gone, nothing reads it.</summary>
    internal Stat PackTacticsSlow { get; } = new Stat(0f);
    internal Stat PetDamageTaken { get; } = new Stat(1f);

    internal Stat BoarCount { get; } = new Stat(0f);
    internal Stat SpitDamage { get; } = new Stat(1f);
    internal Stat SpitSlow { get; } = new Stat(0f);

    public Stat Resolve(PlayerStat stat) => stat switch
    {
        PlayerStat.MaxHp => _pet.MaxHp,
        PlayerStat.MoveSpeed => _pet.MoveSpeed,
        PlayerStat.ContactDamage => _pet.ContactDamage,
        PlayerStat.PetHeelRegen => _pet.HeelRegen,
        PlayerStat.PetReturnAt => _pet.ReturnAt,
        PlayerStat.PetRescueSeconds => _pet.RescueSeconds,
        PlayerStat.PetBiteStunEvery when _pet.IsLead => BiteStunEvery,
        PlayerStat.PetBiteStunSeconds when _pet.IsLead => BiteStunSeconds,
        PlayerStat.SwapRoarSlow when _pet.IsLead => SwapRoarSlow,
        PlayerStat.PackHounds when _pet.IsLead => PackHounds,
        PlayerStat.PackShare when _pet.IsLead => PackShare,
        PlayerStat.PackTactics => PackTactics,
        PlayerStat.PackTacticsSlow => PackTacticsSlow,
        PlayerStat.PetDamageTaken => PetDamageTaken,
        PlayerStat.BoarCount when _pet.IsLead => BoarCount,
        PlayerStat.SpitDamage when _pet.IsLead => SpitDamage,
        PlayerStat.SpitSlow when _pet.IsLead => SpitSlow,
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, "The pet has no such stat."),
    };

    public bool Has(PlayerStat stat) =>
        stat is PlayerStat.MaxHp or PlayerStat.MoveSpeed or PlayerStat.ContactDamage
            or PlayerStat.PetHeelRegen or PlayerStat.PetReturnAt or PlayerStat.PetRescueSeconds
            or PlayerStat.PackTactics or PlayerStat.PackTacticsSlow or PlayerStat.PetDamageTaken
        || (_pet.IsLead && stat is PlayerStat.PackHounds or PlayerStat.PackShare
                or PlayerStat.PetBiteStunEvery or PlayerStat.PetBiteStunSeconds or PlayerStat.SwapRoarSlow
                or PlayerStat.BoarCount or PlayerStat.SpitDamage or PlayerStat.SpitSlow);
}
