namespace Soulvail.Core.Ai;

/// <summary>
/// How fast a hopping body moves this tick: a leap of <c>hopTime</c>, then <c>restTime</c> sat still,
/// with the leap fast enough that the average is still the walking speed. JG-05c; the arithmetic
/// <see cref="ChaserBehaviour"/> carried inline since M7-05j, moved here so Old Croaker hops the same way.
/// </summary>
/// <remarks>
/// <b>Static because there is nothing to hold</b>, <see cref="Combat.ProjectileLead"/>'s reason: the
/// state is the caller's own <see cref="EnemyBlackboard.StateTimer"/>, passed by reference.
/// </remarks>
public static class HopGait
{
    /// <summary>
    /// The ground speed this tick, and the timer advanced by <paramref name="dt"/>.
    /// </summary>
    /// <param name="speed">The walking speed — the average a hopper keeps.</param>
    /// <param name="hopTime">Seconds in the air per hop. Zero or less is a walker, which gets <paramref name="speed"/>.</param>
    /// <param name="restTime">Seconds sat still between hops.</param>
    /// <param name="timer">
    /// Seconds into the gait, read as the phase in one cycle and kept inside it, so a long chase
    /// never loses the float's precision. Zero is the top of a leap.
    /// </param>
    /// <param name="dt">The tick's step.</param>
    public static float Speed(float speed, float hopTime, float restTime, ref float timer, float dt)
    {
        if (!(hopTime > 0f))
        {
            return speed;
        }

        float cycle = hopTime + restTime;
        float phase = timer % cycle;

        timer = phase + dt;

        return phase < hopTime ? speed * cycle / hopTime : 0f;
    }
}
