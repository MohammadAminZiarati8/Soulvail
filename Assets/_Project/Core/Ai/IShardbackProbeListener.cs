using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

/// <summary>Receives correlated obstruction facts; the listener decides whether they are current.</summary>
public interface IShardbackProbeListener
{
    void OnShardbackStop(in ShardbackStop report);
}
