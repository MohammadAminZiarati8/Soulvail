using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// The minion module's domain events. Grouped per module like EnemyEvents, for the same reason: an
// event is three lines, and reading a module's vocabulary in one place is worth more than one type
// per file. See AR §5, §8 and <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// The census is `MinionSpawned` / `MinionDespawned`, exactly as it is for an enemy: a Wight started
// existing, a Wight stopped. `MinionDied` is the third, and **it is not a despawn** — M5-04a rule 10
// keeps the two apart because a Wight has two ways to leave and a keystone must be able to tell them
// apart. `MinionStruck` is what one does while it is here.
//
// **The pair that matters is `MinionDespawned` and `MinionDied`.** CH §3.2's Wights are raised for
// twenty seconds and nothing in M5 can hurt one (rule 9), so the clock running out is what happens
// in every run this milestone can play — and M5-06's Second Death keystone, *"enemies killed by your
// minions explode"*, must not fire on a Wight that simply timed out. A view that draws a dissolve
// listens to both; a keystone that pays for a death listens to one.

/// <summary>
/// A Wight now exists. Published by <c>MinionSystem.Spawn</c> after the agent is standing, so a
/// handler that resolves <see cref="Id"/> through the system inside this event finds it.
/// </summary>
/// <remarks>
/// Carries the position because the view has to be placed before its first frame and there is no
/// snapshot to read it from yet — the snapshot is how positions come back <em>in</em>, one frame
/// later. <see cref="EnemySpawned"/>'s reasoning, and its two fields.
/// <para>
/// <see cref="Lifespan"/> rides along for <c>EnemyTelegraph.Duration</c>'s reason: a view told how
/// long the body has can run its own dissolve out without holding a catalog, and the alternative —
/// looking <c>MinionSpec.Lifespan</c> up — would put a content lookup in a listener and let the
/// drawn time drift from the real one.
/// </para>
/// </remarks>
public readonly struct MinionSpawned
{
    /// <summary>The run-stable id core will answer in from now on.</summary>
    public readonly int Id;

    /// <summary>Which minion it is — <c>minion.wight</c>.</summary>
    public readonly ContentId SpecId;

    /// <summary>Where it stood up, in world metres.</summary>
    public readonly Vector3 Position;

    /// <summary>How long it has, in seconds — the spec's <c>Lifespan</c>. 20 for the Wight.</summary>
    public readonly float Lifespan;

    public MinionSpawned(int id, ContentId specId, Vector3 position, float lifespan)
    {
        Id = id;
        SpecId = specId;
        Position = position;
        Lifespan = lifespan;
    }
}

/// <summary>
/// A Wight's twenty seconds ran out. Published by <c>MinionSystem.Tick</c> <em>after</em> the agent
/// has left the army, so a handler that looks <see cref="Id"/> up inside this event correctly finds
/// nothing.
/// </summary>
/// <remarks>
/// <b>Not a death</b> — a death is <see cref="MinionDied"/> and carries the position (rule 10).
/// This is the clock, and it is what happens in every run this milestone can play. It carries no
/// reason, for the reason <c>EnemyDespawned</c> carries none: whatever retired the body announced
/// itself first, and here nothing did, because a clock running out is the ordinary end.
/// </remarks>
public readonly struct MinionDespawned
{
    /// <summary>The id that has just stopped resolving.</summary>
    public readonly int Id;

    public MinionDespawned(int id)
    {
        Id = id;
    }
}

/// <summary>
/// A Wight was killed. Published by <c>MinionSystem.ApplyDamage</c> on the call that took it to
/// zero, exactly once per life, and by nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ships published by a door nothing calls</b> (rule 9). No enemy behaviour retargets onto a
/// Wight and nothing damages one, so in M5 the only way a Wight leaves is
/// <see cref="MinionDespawned"/>. This exists so that when something does hurt one there is a door
/// rather than a new mechanism — and so that M5-06's Second Death can be authored against an event
/// that already means the right thing.
/// </para>
/// <para>
/// It carries the position for <c>EnemyDied</c>'s reason: a death VFX has to be placed where the
/// thing died, and by the time this is handled the id no longer resolves.
/// </para>
/// </remarks>
public readonly struct MinionDied
{
    /// <summary>Which Wight died. No longer resolvable — it left the army on this call.</summary>
    public readonly int Id;

    /// <summary>Where it died, in world metres — as core last had its position reported.</summary>
    public readonly Vector3 Position;

    public MinionDied(int id, Vector3 position)
    {
        Id = id;
        Position = position;
    }
}

/// <summary>
/// A Wight hit something. Published by <c>MinionSystem.Tick</c> once per strike that landed inside
/// its reach, immediately after the damage went through <c>EnemySystem.ApplyDamage</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Published after the damage, so a listener that asks the enemy how it is finds this strike
/// already counted.</b> The <c>EnemyDamaged</c> — and the <c>EnemyDied</c> behind it, when the blow
/// finished the job — are on the wire first, because the kill goes through the one door a death
/// comes through (rule 11) and this says the extra thing that happened.
/// </para>
/// <para>
/// <see cref="Amount"/> is what was <em>swung</em> — the Wight's live
/// <c>MinionAgent.ContactDamage</c> — rather than what landed, which is the opposite of
/// <c>EnemyDamaged.Amount</c> and deliberate: this is the Wight's event and the number on it is the
/// Wight's, while the trimming that overkill does belongs to the body that took it.
/// </para>
/// </remarks>
public readonly struct MinionStruck
{
    /// <summary>Which Wight swung.</summary>
    public readonly int Id;

    /// <summary>Which enemy it swung at — a live id from <c>EnemyRegistry</c>.</summary>
    public readonly int EnemyId;

    /// <summary>What it swung for, in hit points.</summary>
    public readonly float Amount;

    public MinionStruck(int id, int enemyId, float amount)
    {
        Id = id;
        EnemyId = enemyId;
        Amount = amount;
    }
}
