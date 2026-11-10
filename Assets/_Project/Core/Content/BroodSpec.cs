using System;
using Soulvail.Core.Ai;

namespace Soulvail.Core.Content;

/// <summary>The one generation of children an enemy drops after its death.</summary>
public sealed class BroodSpec
{
    public const int MaxCount = 4;

    public BroodSpec(ContentId child, int count, float burstDelay, float dropRadius)
    {
        if (child.Value is null)
        {
            throw new ArgumentException("The brood must name a child enemy.", nameof(child));
        }

        if (count < 1 || count > MaxCount)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (!(burstDelay > 0f && burstDelay < EnemySystem.CorpseTime))
        {
            throw new ArgumentOutOfRangeException(nameof(burstDelay));
        }

        if (!(dropRadius > 0f) || float.IsInfinity(dropRadius))
        {
            throw new ArgumentOutOfRangeException(nameof(dropRadius));
        }

        Child = child;
        Count = count;
        BurstDelay = burstDelay;
        DropRadius = dropRadius;
    }

    public ContentId Child { get; }

    public int Count { get; }

    public float BurstDelay { get; }

    public float DropRadius { get; }
}
