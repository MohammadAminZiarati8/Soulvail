using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;

namespace Soulvail.Core.Ai;

public enum PetState { Hunt, Heel, Fallen }

public sealed class PetAgent
{
    internal PetAgent(PetSpec spec)
    {
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

    internal PetStats Stats { get; }
    internal bool RecoveringAtHeel { get; set; }
    internal bool Returning { get; set; }
    internal float NextRetargetAt { get; set; }
}
