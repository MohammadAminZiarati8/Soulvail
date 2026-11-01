namespace Soulvail.Core.Ai;

/// <summary>An inner boss behaviour that clears its own adds at a phase beat.</summary>
public interface IBossAddOwner
{
    void ClearAdds(in EnemyTickContext ctx);
}
