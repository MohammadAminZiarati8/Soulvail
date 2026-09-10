using System;
using System.Numerics;
using Soulvail.Core.Ai;

namespace Soulvail.Core.Combat;

/// <summary>
/// CC §3.4's thumb-tap, as arithmetic: a point on the ground becomes the enemy the player most
/// plausibly meant, or nobody. Pure, static and stateless — it is a function, and the only reason
/// it is a type is that C# needs one.
/// </summary>
/// <remarks>
/// <para>
/// <b>The radius is generous on purpose.</b> Three metres is far wider than a Husk, because the
/// input is a thumb on a phone: a tap lands where the finger's contact patch says it did, the
/// finger is covering the thing it is pointing at, and the arena is being looked at from
/// <c>FollowCamera</c>'s 57°. Requiring a hit on the silhouette would make the override feel
/// broken about a third of the time, which is worse than occasionally focusing the neighbour of
/// the enemy that was meant.
/// </para>
/// <para>
/// <b>Nearest wins, and a miss is a miss.</b> With overlapping radii the closest enemy to the tap
/// is the one that was meant — not the closest to the player, and not the most dangerous. Scoring
/// belongs to <see cref="TargetScorer"/>, and letting priority leak in here would mean the game
/// second-guessing a decision it just promised was the player's.
/// </para>
/// <para>
/// <b>It does not look at <c>IsVulnerable</c>.</b> Focusing a Warden that cannot currently be hurt
/// is exactly what the override is for — the player has decided to hold the ring on it and walk
/// around (CC §3.6) — so blockedness is the reticle's business and never this one's. Death is the
/// only disqualification, because a corpse is not something anyone means to point at.
/// </para>
/// </remarks>
public static class FocusResolver
{
    /// <summary>
    /// How far from the tap an enemy may be and still count as tapped, in metres (CC §3.4, §7).
    /// </summary>
    /// <remarks>
    /// A constant here rather than a field on <c>TargetingSpec</c>, which deliberately carries only
    /// the scoring block. It shares that decision with <c>Targeter</c>'s focus drop delay, and for
    /// the same reason: both belong to the override rather than to the scoring it overrides, and if
    /// either ever needs to vary per class they move into authored data together.
    /// </remarks>
    public const float RadiusMetres = 3f;

    /// <summary>
    /// The living enemy nearest <paramref name="point"/> within <paramref name="radius"/> metres of
    /// it, measured on XZ.
    /// </summary>
    /// <param name="point">
    /// Where the tap landed, in world metres. Its Y is ignored, like every other distance in this
    /// project: the height difference between a tap on the ground plane and an enemy capsule's
    /// centre is a rendering detail that would inflate every comparison below.
    /// </param>
    /// <param name="enemies">
    /// Every registered enemy, the dead included — <c>EnemyRegistry.Alive</c>. Borrowed for the
    /// duration of the call and never retained.
    /// </param>
    /// <param name="radius">
    /// The tap radius, normally <see cref="RadiusMetres"/>. Zero, negative and NaN all resolve to
    /// nothing, which falls out of the comparison rather than needing a guard — see the remarks.
    /// </param>
    /// <returns>
    /// The enemy's id, or −1 when nothing qualifies: an empty span, an all-dead span, or a tap on
    /// bare ground. −1 is "nobody" everywhere in this project, and <c>PlayerCombat.FocusAt</c>
    /// turns it into a cleared focus.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Compared squared, so a whole arena of enemies costs no square roots at all. The tie-break is
    /// the lowest id, which is the earliest spawned — the same "first in wins" that
    /// <c>TargetScorer</c> uses, so two enemies standing on the same spot resolve the same way
    /// whichever of the two systems is asked.
    /// </para>
    /// <para>
    /// Both float comparisons are spelled as negated positives (<c>!(a &lt;= b)</c>), for the reason
    /// <c>MovementSpec</c> documents: every comparison against NaN is false, so the natural
    /// spelling would admit a NaN distance as "within radius" and focus an enemy whose position has
    /// gone bad. Spelled this way a NaN is simply skipped, and so is every candidate when the radius
    /// itself is NaN.
    /// </para>
    /// </remarks>
    public static int Resolve(Vector3 point, ReadOnlySpan<EnemyAgent> enemies, float radius)
    {
        // Checked before it is squared, and that is the whole reason this line exists: −1 squared
        // is 1, so a negative radius would otherwise resolve as a one-metre one — a nonsense
        // argument silently becoming a plausible answer. Negated positive, so NaN lands here too.
        if (!(radius > 0f))
        {
            return -1;
        }

        float radiusSquared = radius * radius;

        int bestId = -1;
        float bestDistanceSquared = 0f;

        for (int i = 0; i < enemies.Length; i++)
        {
            EnemyAgent agent = enemies[i];

            // A corpse sits in the registry until M1-11 despawns it, so the span genuinely carries
            // the dead and this is not a defensive check — it is rule 1.
            if (!agent.IsAlive)
            {
                continue;
            }

            Vector3 position = agent.Position;
            float dx = position.X - point.X;
            float dz = position.Z - point.Z;
            float distanceSquared = (dx * dx) + (dz * dz);

            if (!(distanceSquared <= radiusSquared))
            {
                continue;
            }

            if (bestId < 0
                || distanceSquared < bestDistanceSquared
                || (distanceSquared == bestDistanceSquared && agent.Id < bestId))
            {
                bestId = agent.Id;
                bestDistanceSquared = distanceSquared;
            }
        }

        return bestId;
    }
}
