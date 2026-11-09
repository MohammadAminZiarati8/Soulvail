using System.Numerics;

namespace Soulvail.Core.Ai;

/// <summary>
/// A behaviour whose shield turns damage away by direction (BT-09b). The guard still raises
/// against the player; this answers whether a hit from somewhere else falls inside that arc.
/// </summary>
internal interface IFrontalGuard
{
    /// <summary>True while the guard is raised and the XZ direction (enemy → source) is inside its arc.</summary>
    bool Covers(Vector2 towardSource);
}
