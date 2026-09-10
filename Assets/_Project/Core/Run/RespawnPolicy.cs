using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Run;

/// <summary>
/// What replaces the dead: keep <see cref="KeepAlive"/> enemies breathing, wait
/// <see cref="RespawnDelay"/> seconds after the last death, and never put one within
/// <see cref="MinPlayerDistance"/> metres of the player. Carried by <see cref="SpawnPlan"/> and
/// applied by <c>EnemySystem.Tick</c>. See GD §12.4 and AR §4.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for the feel test, and replaced rather than extended.</b> M2-03's threat budget
/// and M2-05's director own spawning from the moment they land; this exists because M1's last two
/// tasks are about how a fight <em>feels</em> over minutes, and a fight that runs out after eight
/// seconds cannot be felt at all. Everything it deliberately does not do — composition, waves,
/// concurrency scaling, telegraphed spawn rings — is M2's, and none of it is stubbed here.
/// </para>
/// <para>
/// Authored data, not live state: immutable, its positions copied on construction, and safe to
/// hand to two runs at once. The two pieces of state the rule needs — how many are alive, and when
/// the last one died — belong to the system applying it, because both are facts about a run rather
/// than about the policy. It names its archetype by <see cref="ContentId"/> and never holds an
/// <c>EnemySpec</c>, for the reason <see cref="SpawnPlan"/> gives.
/// </para>
/// <para>
/// <b>Spawn safety is measured on the ground plane.</b> Distances everywhere in core are XZ (see
/// <c>EnemySystem.Perceive</c>), and the Y gap between a player capsule's centre and a spawn point
/// on the floor is a rendering detail that would otherwise make every position look further away
/// than it is — which for a rule whose entire job is to refuse near ones is the wrong direction to
/// be wrong in.
/// </para>
/// </remarks>
public sealed class RespawnPolicy
{
    private readonly ReadOnlyCollection<Vector3> _positions;

    /// <param name="specId">The archetype to replace the dead with, e.g. <c>enemy.husk</c>.</param>
    /// <param name="positions">
    /// Where a replacement may appear, in world metres. Copied; the caller's list is not retained.
    /// Order is meaningful — rule 2 walks it from the drawn index — so two runs from the same seed
    /// only agree if the arena hands over the same list in the same order.
    /// </param>
    /// <param name="keepAlive">How many enemies should be breathing at once.</param>
    /// <param name="respawnDelay">
    /// Seconds of quiet after the most recent death before anything is replaced. The pause the
    /// player reads as "I cleared that", without which a kill is invisible.
    /// </param>
    /// <param name="minPlayerDistance">
    /// Metres of clearance a spawn position needs from the player (GD §12.4). Zero is legal and
    /// means "anywhere", which is a decision an arena is entitled to make rather than a mistake.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="specId"/> is <c>default(ContentId)</c>, or <paramref name="positions"/> is
    /// empty. Both are policies that can never spawn anything: the first would fail one layer down
    /// as the catalog's "no enemy with id ''", pointing at content that was never at fault, and the
    /// second would silently keep an arena empty — which is the one failure a playtest cannot tell
    /// apart from a broken spawner.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="positions"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="keepAlive"/> is not positive, either of the other two numbers is negative or
    /// non-finite, or a position has a non-finite component. A NaN position is permanent and silent
    /// for the reason <c>SpawnPlan.Entry</c> gives: every comparison against it is false, so the
    /// distance check below would accept it and the enemy would stand somewhere nothing can reach.
    /// </exception>
    public RespawnPolicy(
        ContentId specId,
        IReadOnlyList<Vector3> positions,
        int keepAlive,
        float respawnDelay,
        float minPlayerDistance)
    {
        if (specId.Value is null)
        {
            throw new ArgumentException(
                "specId must be a valid ContentId; default(ContentId) names no archetype.",
                nameof(specId));
        }

        if (positions is null)
        {
            throw new ArgumentNullException(nameof(positions));
        }

        if (positions.Count == 0)
        {
            throw new ArgumentException(
                $"positions is empty, so nothing of '{specId}' could ever be respawned. An arena "
                    + "that should not refill carries no policy at all.",
                nameof(positions));
        }

        if (keepAlive <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(keepAlive),
                keepAlive,
                "keepAlive must be greater than zero. A policy that keeps nobody alive is spelled "
                    + "by having no policy.");
        }

        // Negated positives: every comparison against NaN is false, so `< 0f` would let one
        // through and the delay would then never elapse — an arena that empties once and stays
        // empty, with nothing in the log.
        if (!(respawnDelay >= 0f) || float.IsInfinity(respawnDelay))
        {
            throw new ArgumentOutOfRangeException(
                nameof(respawnDelay),
                respawnDelay,
                "respawnDelay must be finite and not negative.");
        }

        if (!(minPlayerDistance >= 0f) || float.IsInfinity(minPlayerDistance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(minPlayerDistance),
                minPlayerDistance,
                "minPlayerDistance must be finite and not negative.");
        }

        var copy = new Vector3[positions.Count];

        for (int i = 0; i < positions.Count; i++)
        {
            Vector3 position = positions[i];

            if (!IsFinite(position))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(positions),
                    position,
                    $"positions[{i}] for '{specId}' must be finite in every component.");
            }

            copy[i] = position;
        }

        SpecId = specId;
        KeepAlive = keepAlive;
        RespawnDelay = respawnDelay;
        MinPlayerDistance = minPlayerDistance;

        // Wrapped rather than handed out as the array it is: an array exposed as IReadOnlyList<T>
        // casts straight back to Vector3[], and then the copy above protects nothing. The same
        // guard SpawnPlan makes for the same reason.
        _positions = Array.AsReadOnly(copy);
    }

    /// <summary>How many enemies should be breathing at once — living ones, corpses excluded.</summary>
    public int KeepAlive { get; }

    /// <summary>Seconds after the most recent death before a replacement may appear.</summary>
    public float RespawnDelay { get; }

    /// <summary>Metres of clearance a spawn position needs from the player (GD §12.4).</summary>
    public float MinPlayerDistance { get; }

    /// <summary>Where a replacement may appear, in the order rule 2 walks them.</summary>
    public IReadOnlyList<Vector3> Positions => _positions;

    /// <summary>The archetype to spawn, resolved against the <c>ContentCatalog</c>.</summary>
    public ContentId SpecId { get; }

    /// <summary>
    /// Whether <paramref name="position"/> is far enough from <paramref name="playerPosition"/> to
    /// spawn at.
    /// </summary>
    /// <remarks>
    /// Compared squared, so the check never takes a square root — this runs up to
    /// <c>Positions.Count</c> times on the frame something respawns.
    /// </remarks>
    public bool IsSafe(Vector3 position, Vector3 playerPosition)
    {
        float dx = position.X - playerPosition.X;
        float dz = position.Z - playerPosition.Z;

        return (dx * dx) + (dz * dz) >= MinPlayerDistance * MinPlayerDistance;
    }

    private static bool IsFinite(Vector3 v) =>
        !float.IsNaN(v.X) && !float.IsInfinity(v.X)
        && !float.IsNaN(v.Y) && !float.IsInfinity(v.Y)
        && !float.IsNaN(v.Z) && !float.IsInfinity(v.Z);
}
