using System;
using System.Numerics;

namespace Soulvail.Core.Combat;

/// <summary>
/// Where to aim a shot so that it meets a moving target. CC §3.7's two-iteration solve, as pure
/// arithmetic: no clock, no registry, no allocation, and nothing it could publish.
/// </summary>
/// <remarks>
/// <para>
/// <b>Static because there is nothing to hold.</b> The project bans statics that carry state
/// (AR §4.1) and this carries none — four numbers in, one point out, the same answer every time it
/// is asked. It is the shape <c>ShardPayout</c> and <c>FocusResolver</c> already take, and the
/// reason it is not a method on <c>PlayerCombat</c> is that the arithmetic is worth testing without
/// a character, a target or a weapon around it.
/// </para>
/// <para>
/// <b>Two iterations, fixed, not a loop that converges.</b> CC §3.7 says "iterate twice" and
/// <see cref="Iterations"/> is that number rather than a tolerance and a bail-out, which matters
/// for the case where there is no solution at all: a target moving faster than the shot has an
/// aim point that recedes for ever, and a convergence loop would either spin or return whatever it
/// had reached when it gave up. Two passes always terminate and always answer something bounded,
/// which is what a projectile weapon actually needs — the second pass is worth about three orders
/// of magnitude of residual against a crossing target, and a third would be worth almost nothing.
/// </para>
/// <para>
/// <b>XZ only (AR §18.4).</b> A muzzle is above the ground and a target's centre is above its feet;
/// counting either into the distance inflates the flight time, and <c>ProjectileSystem</c> already
/// resolves an arrival on XZ alone, so a lead solved in three dimensions would disagree with the
/// flight it is solving for. The aim point keeps the target's own Y, untouched — see
/// <see cref="Solve"/>.
/// </para>
/// </remarks>
public static class ProjectileLead
{
    /// <summary>How many times the flight time is re-solved. CC §3.7's "iterate twice".</summary>
    public const int Iterations = 2;

    /// <summary>
    /// Where to aim so that a shot of <paramref name="speed"/> leaving <paramref name="origin"/>
    /// meets a target at <paramref name="targetPosition"/> moving at
    /// <paramref name="targetVelocity"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>t = d / v</c>, <c>aim = p + vel·t</c>, twice: the first pass measures the distance to
    /// where the target is, the second measures it to where the first pass decided to aim. A
    /// stationary target solves to itself on the first pass and the second changes nothing, which
    /// is what makes "iterate twice" free in the common case rather than a cost paid for nobody.
    /// </para>
    /// <para>
    /// <b>It fails by returning <paramref name="targetPosition"/>, never by returning a NaN.</b> A
    /// shot aimed at NaN never arrives and nothing reports it — the failure
    /// <c>Projectile</c>'s own constructor exists to refuse — so an unreadable input here answers
    /// with the unled aim point instead: a shot that misses a moving target is visible, recoverable
    /// on the next swing, and honest about the fact that nothing was known about where the target
    /// was going. Refused inputs are a <paramref name="speed"/> that is not a finite number greater
    /// than zero, and a non-finite component anywhere in either position or in the velocity.
    /// </para>
    /// <para>
    /// The aim point's Y is <paramref name="targetPosition"/>'s, unchanged, and the velocity's Y is
    /// never read. Two targets differing only in height therefore lead to the same XZ point, which
    /// is AR §18.4 stated as arithmetic rather than as a convention.
    /// </para>
    /// </remarks>
    /// <param name="origin">Where the shot leaves from, in world metres. Y is ignored.</param>
    /// <param name="targetPosition">Where the target is now, in world metres.</param>
    /// <param name="targetVelocity">
    /// How fast the target is actually moving — <c>EnemyAgent.Velocity</c>, as last ingested, rather
    /// than a heading a behaviour intends. Y is ignored.
    /// </param>
    /// <param name="speed">Metres per second of flight — the weapon's <c>ShotSpeed</c>.</param>
    /// <returns>
    /// The aim point, or <paramref name="targetPosition"/> unchanged when there is no solution
    /// worth having.
    /// </returns>
    public static Vector3 Solve(
        Vector3 origin,
        Vector3 targetPosition,
        Vector3 targetVelocity,
        float speed)
    {
        // `!(speed > 0f)` rather than `speed <= 0f`, so NaN is refused with everything else — the
        // spelling every guard in this folder uses. Infinity separately, because it passes a `> 0`
        // test and would make every flight time zero, which is a shot that needs no leading at all
        // and is therefore a lie about a weapon that does.
        if (!(speed > 0f) || float.IsInfinity(speed))
        {
            return targetPosition;
        }

        if (!IsFinite(origin) || !IsFinite(targetPosition) || !IsFinite(targetVelocity))
        {
            return targetPosition;
        }

        float aimX = targetPosition.X;
        float aimZ = targetPosition.Z;

        for (int i = 0; i < Iterations; i++)
        {
            float dx = aimX - origin.X;
            float dz = aimZ - origin.Z;

            float flightTime = MathF.Sqrt((dx * dx) + (dz * dz)) / speed;

            // Measured from the target's *current* position each pass, not from the previous aim
            // point: the iteration refines the flight time, and compounding the offset instead
            // would walk the aim point away from the target rather than towards its future.
            aimX = targetPosition.X + (targetVelocity.X * flightTime);
            aimZ = targetPosition.Z + (targetVelocity.Z * flightTime);
        }

        return new Vector3(aimX, targetPosition.Y, aimZ);
    }

    /// <remarks>
    /// All three components, the Y included, even though the solve reads only two of them. A NaN
    /// height is a broken position rather than a height to ignore, and the aim point carries
    /// <c>targetPosition.Y</c> straight through — so a Y this did not check would reach
    /// <c>Projectile</c>'s door as a shot aimed at NaN, which is exactly the outcome the refusal
    /// above exists to avoid.
    /// </remarks>
    private static bool IsFinite(Vector3 value) =>
        IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
