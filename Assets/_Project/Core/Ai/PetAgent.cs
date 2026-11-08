using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;

namespace Soulvail.Core.Ai;

public enum PetState { Hunt, Heel, Fallen }

/// <summary>What body a pet is: the lead Hound, a Pack Hounds follower, or the ranged Boar (BT-07a).</summary>
public enum PetKind { Hound, PackHound, Boar }

public sealed class PetAgent
{
    internal PetAgent(PetSpec spec, PetKind kind)
    {
        Spec = spec;
        Kind = kind;
        MaxHp = new Stat(spec.MaxHp);
        MoveSpeed = new Stat(spec.MoveSpeed);
        ContactDamage = new Stat(spec.BiteDamage);
        HeelAt = new Stat(spec.HeelAt);
        ReturnAt = new Stat(spec.ReturnAt);
        HeelRegen = new Stat(spec.HeelRegen);
        RescueSeconds = new Stat(spec.RescueSeconds);
        Health = new Health(MaxHp, null, 0f);
        Stats = new PetStats(this);
    }

    public PetKind Kind { get; }
    public bool IsLead => Kind == PetKind.Hound;
    public int Id { get; internal set; }
    public Vector3 Position { get; internal set; }
    public Health Health { get; }
    public Stat MaxHp { get; }
    public Stat MoveSpeed { get; }
    public Stat ContactDamage { get; }
    public PetState State { get; internal set; }
    public int QuarryId { get; internal set; }
    public float NextBiteAt { get; internal set; }

    public Stat HeelAt { get; }
    public Stat ReturnAt { get; }
    public Stat HeelRegen { get; }
    public Stat RescueSeconds { get; }
    public float RescueProgress { get; internal set; }

    internal PetSpec Spec { get; }
    internal PetStats Stats { get; }
    internal bool RecoveringAtHeel { get; set; }
    internal float NextRetargetAt { get; set; }
}
