using System.Numerics;

namespace Soulvail.Core.Ai;

/// <summary>An inner boss behaviour that places its own phase adds.</summary>
public interface ISummonPlacer
{
    Vector3 PlaceAdd(in EnemyTickContext ctx, Vector3 ring);
}
