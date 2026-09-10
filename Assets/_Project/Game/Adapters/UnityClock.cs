using System;
using Soulvail.Core.Ports;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The device clock behind <see cref="IClock"/>. Reads the system clock on every call and caches
/// nothing.
/// </summary>
/// <remarks>
/// <para>
/// Pure C# despite living in <c>Soulvail.Game</c>, and deliberately not a <c>MonoBehaviour</c>:
/// like <c>SeededRandom</c> it sits on the Unity side of the hexagon by role, not by dependency
/// (AR §6). It touches no engine API at all — the day a device clock needs correcting, an NTP
/// offset or a server time, that correction has somewhere to live that core cannot see. A plain
/// class, so it keeps a file-scoped namespace.
/// </para>
/// <para>
/// Nothing is cached, including the value: two saves written in one session must not share a
/// timestamp, and a field read once at construction would give them one.
/// </para>
/// </remarks>
public sealed class UnityClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
