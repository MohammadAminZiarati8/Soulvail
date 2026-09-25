using System;

namespace Soulvail.Core.Content;

/// <summary>
/// GD §14.2's two routes to a class: pay, or prove. Null on the starter, which is CH §3's
/// <em>"Free — the starter"</em> said in one word.
/// </summary>
/// <remarks>
/// <para>
/// <b>A price is required and a deed is optional</b>, because GD §14.2's table prices every class
/// it gates and proves only some of them: a class is locked by <em>having</em> one of these, so a
/// class with no price would be locked with no way in. A free class authors null (M6-09a rule 3).
/// </para>
/// <para>
/// <b>At most one deed.</b> GD §14.2 names one per class — a depth or a boss — and a class that
/// could be proved two ways would need a rule for which one a screen draws. Refused here rather
/// than resolved.
/// </para>
/// <para>
/// <b><see cref="DeedBossId"/> is not resolved against the catalog</b>, and the Gravecaller's is
/// the reason: GD §9.2's Choirmother is M7-03's, and refusing a dangling id would mean deleting the
/// design's own second route from the asset and re-adding it later (M6-09a rule 4). A boss no mode
/// authors is simply a deed no run can do.
/// </para>
/// </remarks>
public sealed class UnlockSpec
{
    /// <param name="shardPrice">What it costs (GD §14.2). Above zero — a free class authors null.</param>
    /// <param name="deedStage">The depth that proves it, or 0 for none. The Emberwright's 20.</param>
    /// <param name="deedBossId">
    /// The boss whose death proves it, or <c>default</c> for none. The Gravecaller's Choirmother —
    /// <b>and this build ships no such boss</b>, see the type's remarks.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="shardPrice"/> is not above zero, or <paramref name="deedStage"/> is negative.
    /// </exception>
    /// <exception cref="ArgumentException">Both deeds are set, which is two proofs for one class.</exception>
    public UnlockSpec(int shardPrice, int deedStage = 0, ContentId deedBossId = default)
    {
        if (shardPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shardPrice),
                shardPrice,
                "shardPrice must be above 0. A class that costs nothing is the starter, and the " +
                "starter authors no UnlockSpec at all (M6-09a rule 3).");
        }

        if (deedStage < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deedStage),
                deedStage,
                "deedStage must be 0 for none, or the stage that proves the class. Stages are " +
                "numbered from 1 (GD §8.2).");
        }

        if (deedStage > 0 && deedBossId.Value is not null)
        {
            throw new ArgumentException(
                $"An unlock names both a stage ({deedStage}) and a boss ('{deedBossId}'). GD §14.2 " +
                "gives each class one proof; author one of them.",
                nameof(deedBossId));
        }

        ShardPrice = shardPrice;
        DeedStage = deedStage;
        DeedBossId = deedBossId;
    }

    /// <summary>What the class costs, in Soul Shards. Always above zero.</summary>
    public int ShardPrice { get; }

    /// <summary>The depth whose reaching proves the class, or 0 for none.</summary>
    public int DeedStage { get; }

    /// <summary>The boss whose death proves the class, or <c>default</c> for none.</summary>
    public ContentId DeedBossId { get; }

    /// <summary>Whether any deed can prove this class — false for a price-only unlock.</summary>
    public bool HasDeed => DeedStage > 0 || DeedBossId.Value is not null;
}
