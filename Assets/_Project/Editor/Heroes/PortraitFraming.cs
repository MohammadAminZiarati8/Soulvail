using UnityEngine;

namespace Soulvail.Editor.Heroes;

/// <summary>
/// Where the portrait camera stands and what it looks at, in the space of the bounds it was framed
/// from (UI-12a rule 4). A value: <see cref="PortraitBaker.Frame"/> computes it and nothing holds it.
/// </summary>
public readonly struct PortraitFraming
{
    public PortraitFraming(Vector3 position, Vector3 lookAt)
    {
        Position = position;
        LookAt = lookAt;
    }

    /// <summary>The camera's position.</summary>
    public Vector3 Position { get; }

    /// <summary>The point the camera looks at: the middle of the head-and-shoulders band.</summary>
    public Vector3 LookAt { get; }
}
