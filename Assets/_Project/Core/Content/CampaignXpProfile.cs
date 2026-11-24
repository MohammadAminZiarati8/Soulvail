using System;

namespace Soulvail.Core.Content;

/// <summary>The shared progression policy for campaign modes.</summary>
public sealed class CampaignXpProfile
{
    private readonly int[] _regularBudgets =
    {
        54, 135, 174, 234, 240,
        246, 312, 336, 408, 396,
        384, 432, 459, 491, 488,
        484, 538, 546, 587, 580,
        572, 634, 689, 737, 731,
        725, 755, 782, 800, 820,
    };

    public XpCurve Curve { get; } = new XpCurve(20f, 5.5f, 1.4f, maxLevel: 30);

    public float BossFraction => 0.75f;

    public int RegularBudget(int stage)
    {
        if (stage < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        return _regularBudgets[Math.Min(stage, _regularBudgets.Length) - 1];
    }
}
