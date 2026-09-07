namespace Soulvail.Game.Adapters;

/// <summary>
/// The one place <c>UnityEngine</c> vectors become <c>System.Numerics</c> vectors and back.
/// Core speaks <c>System.Numerics</c> because it may never see the engine (ADR-0001), so
/// every value crossing the boundary — snapshots in, intents out — passes through here.
/// </summary>
/// <remarks>
/// <para>
/// Extension methods rather than implicit conversions, which neither type can be given
/// because the project owns neither. Extensions also keep the direction visible at the call
/// site: <c>transform.position.ToNum()</c> reads as a boundary crossing, which is what it is.
/// </para>
/// <para>
/// Both namespaces declare <c>Vector2</c> and <c>Vector3</c>, so every type here is fully
/// qualified. A <c>using</c> for either namespace would silently pick a winner and make the
/// signatures read as though the conversions were identities.
/// </para>
/// </remarks>
public static class Num
{
    public static System.Numerics.Vector3 ToNum(this UnityEngine.Vector3 v) => new(v.x, v.y, v.z);

    public static UnityEngine.Vector3 ToUnity(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static System.Numerics.Vector2 ToNum(this UnityEngine.Vector2 v) => new(v.x, v.y);

    public static UnityEngine.Vector2 ToUnity(this System.Numerics.Vector2 v) => new(v.X, v.Y);

    /// <summary>
    /// Flattens a world vector onto the ground plane as <c>(x, z)</c>, dropping Y.
    /// </summary>
    /// <remarks>
    /// Movement, facing and path directions are two-dimensional in a top-down game, and
    /// carrying a Y that is always zero invites someone to normalise a three-component vector
    /// and get a different answer. Y is reintroduced only where the view needs it.
    /// </remarks>
    public static System.Numerics.Vector2 ToNumXZ(this UnityEngine.Vector3 v) => new(v.x, v.z);
}
