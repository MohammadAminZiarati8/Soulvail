using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// The enemy module's domain events. Grouped per module like RunEvents, for the same reason: an
// event is three lines, and reading a module's vocabulary in one place is worth more than one type
// per file. See AR §5, §8 and <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// These two are the *census* — an enemy started existing, an enemy stopped. What happens to one
// while it exists is M1-11's (`EnemyDamaged`, `EnemyDied`), and the split is deliberate: a view
// needs to be created and destroyed on the first pair whatever the second pair says, and a death
// is not a despawn. A Husk dies, its dissolve plays, and only then is it despawned.

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
