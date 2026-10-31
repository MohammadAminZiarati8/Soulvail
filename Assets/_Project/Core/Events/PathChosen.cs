using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

/// <summary>The run has chosen its one path. Restoring a path publishes nothing.</summary>
public readonly struct PathChosen
{
    public readonly ContentId PathId;

    public PathChosen(ContentId pathId)
    {
        PathId = pathId;
    }
}
