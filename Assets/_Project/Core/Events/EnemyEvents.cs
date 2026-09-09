using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// The enemy module's domain events. Grouped per module like RunEvents, for the same reason: an
// event is three lines, and reading a module's vocabulary in one place is worth more than one type
// per file. See AR §5, §8 and <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// Two pairs and a single, and the split between them is deliberate. `EnemySpawned` /
// `EnemyDespawned` are the *census* — an enemy started existing, an enemy stopped — and a view is
// created and destroyed on them whatever else is said. `EnemyDamaged` / `EnemyDied` are what happens
// to one while it exists (M1-11). A death is not a despawn: a Husk dies, its dissolve plays for
// `EnemySystem.CorpseTime`, and only then is it despawned. `EnemyTelegraph` (M1-18) is the odd one
// out and the only one an enemy publishes about *itself* rather than about something done to it.

/// <summary>
/// An enemy now exists. Published by <c>EnemySystem.Spawn</c> after the agent is registered, so a
/// handler that resolves <see cref="Id"/> through the registry inside this event finds it.
/// </summary>
/// <remarks>
/// Carries the position because the view has to be placed before its first frame and there is no
/// snapshot to read it from yet — the snapshot is how positions come back *in*, one frame later.
/// Everything else about the enemy is reachable from <see cref="SpecId"/> through the catalog.
/// </remarks>
public readonly struct EnemySpawned
{
    /// <summary>The run-stable id core will answer in from now on.</summary>
    public readonly int Id;

    /// <summary>Which archetype it is, e.g. <c>enemy.husk</c>.</summary>
    public readonly ContentId SpecId;

    /// <summary>Where it was spawned, in world metres.</summary>
    public readonly Vector3 Position;

    public EnemySpawned(int id, ContentId specId, Vector3 position)
    {
        Id = id;
        SpecId = specId;
        Position = position;
    }
}

/// <summary>
/// An enemy has stopped existing. Published by <c>EnemySystem.Despawn</c> <em>after</em> the agent
/// leaves the registry, so a handler that looks <see cref="Id"/> up inside this event correctly
/// finds nothing.
/// </summary>
/// <remarks>
/// Not a death — a death is <c>EnemyDied</c> (M1-11) and comes first, one or more frames earlier,
/// so the view has time to dissolve. This is the id going out of service: return the view to its
/// pool, drop any reference held to it. It carries no reason, for the reason <c>RunEnded</c>
/// carries none: whatever retired the enemy announced itself first.
/// </remarks>
public readonly struct EnemyDespawned
{
    /// <summary>The id that has just stopped resolving.</summary>
    public readonly int Id;

    public EnemyDespawned(int id)
    {
        Id = id;
    }
}

/// <summary>
/// Damage got through to an enemy. Published by <c>EnemySystem.ApplyDamage</c>, once per call that
/// actually landed something — a hit on an unknown, dead or unharmed enemy says nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Amount"/> is what <em>landed</em>, not what was swung: a 13-damage swing on a Husk
/// with 10 HP left reports 10. That is what a damage-number view should float, and what anything
/// counting damage dealt has to sum if the total is to mean anything.
/// </para>
/// <para>
/// <see cref="HpFraction"/> rides along because the health bar needs it and re-deriving it would
/// mean a listener holding a reference into core's state. It is the fraction <em>after</em> this
/// hit, so a <see cref="Killed"/> event carries zero.
/// </para>
/// <para>
/// <see cref="Killed"/> is here as well as in <see cref="EnemyDied"/>, and the redundancy is
/// deliberate: a hit-flash listener needs to know in one event whether to flash or to start a
/// dissolve, and making it correlate two events by id to find out would be a state machine per
/// enemy in every listener.
/// </para>
/// </remarks>
public readonly struct EnemyDamaged
{
    /// <summary>Which enemy was hit.</summary>
    public readonly int Id;

    /// <summary>How much damage actually landed, after any overkill was trimmed.</summary>
    public readonly float Amount;

    /// <summary>Its HP over its live maximum, after the hit. Zero when this one killed it.</summary>
    public readonly float HpFraction;

    /// <summary>This hit took it to zero. An <see cref="EnemyDied"/> follows immediately.</summary>
    public readonly bool Killed;

    public EnemyDamaged(int id, float amount, float hpFraction, bool killed)
    {
        Id = id;
        Amount = amount;
        HpFraction = hpFraction;
        Killed = killed;
    }
}

/// <summary>
/// An enemy has committed to an attack and is telegraphing it. Published by the agent's behaviour
/// on the tick the windup begins — <c>ChaserBehaviour</c>'s is the first — and by nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>GD §9.1 rule 1 made into a fact.</b> Every attack in this game is telegraphed, and a telegraph
/// only exists if something draws it: this is the event the drawing hangs off. It is published on
/// entering the windup rather than on the damage frame, for the reason <c>PlayerAttacked</c> is
/// published at the start of a swing — a tell that arrives with the hit is not a tell.
/// </para>
/// <para>
/// <see cref="Duration"/> rides along so that a view can fill a pulse, a ring or a bar over exactly
/// the right span without holding a catalog. The view has an id and a number of seconds, which is
/// all any of those need, and the alternative — looking <c>EnemySpec.WindupTime</c> up — would put a
/// content lookup in a listener and let the drawn time drift from the real one.
/// </para>
/// <para>
/// <b>There is no matching "telegraph ended".</b> The windup can end three ways — the strike, a
/// cancel when the player leaves, or the enemy dying mid-windup — and a view that has been told how
/// long the tell lasts can simply run out on its own, while a second event would have to be
/// published on all three paths and correlated by id. Anything that needs the strike itself has
/// <c>PlayerDamaged</c>; anything that needs the death has <c>EnemyDied</c>.
/// </para>
/// </remarks>
public readonly struct EnemyTelegraph
{
    /// <summary>Which enemy is winding up.</summary>
    public readonly int Id;

    /// <summary>How long the windup lasts, in seconds — the archetype's <c>WindupTime</c>. 0.4 for the Husk.</summary>
    public readonly float Duration;

    public EnemyTelegraph(int id, float duration)
    {
        Id = id;
        Duration = duration;
    }
}

/// <summary>
/// An enemy has been killed. Published by <c>EnemySystem.ApplyDamage</c> immediately after the
/// <see cref="EnemyDamaged"/> that did it, exactly once per life.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a despawn, and separated from one by <c>EnemySystem.CorpseTime</c>.</b> The id still
/// resolves through the registry while this is handled and for the whole of the dissolve that
/// follows; <see cref="EnemyDespawned"/> is when it stops. Everything that keys off a kill —
/// M1-12's dissolve, M3-01's XP, M6-01's Essence — hangs off this one.
/// </para>
/// <para>
/// It carries the archetype and the position because both are questions about a corpse that the
/// registry will stop being able to answer, and because a drop or a death VFX has to be placed
/// where the thing died rather than where its view has drifted to. The same reasoning, and the
/// same two fields, as <see cref="EnemySpawned"/>.
/// </para>
/// </remarks>
public readonly struct EnemyDied
{
    /// <summary>Which enemy died. Still resolvable through the registry until it is despawned.</summary>
    public readonly int Id;

    /// <summary>Which archetype it was, e.g. <c>enemy.husk</c>.</summary>
    public readonly ContentId SpecId;

    /// <summary>Where it died, in world metres — as core last had its position reported.</summary>
    public readonly Vector3 Position;

    public EnemyDied(int id, ContentId specId, Vector3 position)
    {
        Id = id;
        SpecId = specId;
        Position = position;
    }
}
