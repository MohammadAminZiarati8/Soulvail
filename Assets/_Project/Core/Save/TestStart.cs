using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Save;

/// <summary>
/// A run that begins at any stage with the experience the stages before it would have paid —
/// DT-01's development start, so a deep stage can be played with a tree's worth of picks to spend.
/// </summary>
/// <remarks>
/// <para>
/// <b>It builds a <see cref="RunSnapshot"/> and nothing else</b>, so the run it starts goes down
/// the Continue path that already exists: <c>RunSession.Start</c>'s restore puts the levels in
/// the bank, and the run's first frame opens the tree because picks are owed. No new branch is
/// added to the session for it.
/// </para>
/// <para>
/// <b>The experience is the design's arithmetic, not a replay.</b> An open stage pays
/// <c>3 × B(n)</c> — every archetype is shipped worth three times its threat cost, so a stage's
/// XP is its budget's (<see cref="EnemySpec.XpValue"/>); a scripted stage pays for the bodies it
/// lists. A real run lands within a few threat of it, whatever the seed drew.
/// </para>
/// </remarks>
public sealed class TestStart
{
    /// <summary>What an open stage's budget is worth in experience; see the remarks.</summary>
    private const float XpPerThreat = 3f;

    private readonly ContentCatalog _catalog;

    /// <param name="catalog">Where the mode and the scripted stages' archetypes are read from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
    public TestStart(ContentCatalog catalog)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    /// <summary>
    /// The experience a run of <paramref name="modeId"/> has earned on reaching
    /// <paramref name="stage"/>: every stage from the mode's first up to the one before it.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The mode has no such stage.</exception>
    public float XpBefore(ContentId modeId, int stage)
    {
        ModeSpec mode = _catalog.Mode(modeId);
        Guard(mode, stage);

        float total = 0f;

        for (int earlier = mode.StartingStage; earlier < stage; earlier++)
        {
            total += XpOf(mode, earlier);
        }

        return total;
    }

    /// <summary>
    /// A fresh run of <paramref name="characterId"/> standing at <paramref name="stage"/>, at full
    /// health, with <see cref="XpBefore"/>'s experience turned into levels and every pick unspent.
    /// </summary>
    /// <param name="random">
    /// The generator's state for <paramref name="seed"/>, captured by whoever owns the generator —
    /// core cannot build one.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The mode has no such stage.</exception>
    public RunSnapshot At(ContentId modeId, ContentId characterId, int seed, RandomState random, int stage)
    {
        ModeSpec mode = _catalog.Mode(modeId);
        float xp = XpBefore(modeId, stage);

        // LevelTracker.Grant's own loop, against the mode's own curve, so the level and the
        // leftover are the ones the run would be holding after those kills.
        int level = 1;

        while (xp >= mode.Xp.ToReach(level + 1))
        {
            xp -= mode.Xp.ToReach(level + 1);
            level++;
        }

        return new RunSnapshot(
            RunSnapshot.CurrentVersion,
            modeId,
            characterId,
            seed,
            stage,
            random,
            playerHp: float.MaxValue,
            playerShield: float.MaxValue,
            runTime: 0f,
            writtenAt: default,
            level: level,
            xp: xp,
            pendingLevelUps: level - 1,
            takenNodeIds: Array.Empty<ContentId>(),
            manualSkillIds: new ContentId[SkillRunner.MaxManualSlots],
            economy: default,
            banishedNodeIds: Array.Empty<ContentId>(),
            pactedNodeIds: Array.Empty<ContentId>(),
            ordealIds: Array.Empty<ContentId>());
    }

    private float XpOf(ModeSpec mode, int stage)
    {
        if (!mode.TryGetScriptedStage(stage, out ScriptedStage scripted))
        {
            return XpPerThreat * mode.Scaling.Budget.At(stage);
        }

        float total = 0f;

        for (int i = 0; i < scripted.Spawns.Count; i++)
        {
            ScriptedSpawn spawn = scripted.Spawns[i];
            total += _catalog.Enemy(spawn.SpecId).XpValue * spawn.Count;
        }

        return total;
    }

    private static void Guard(ModeSpec mode, int stage)
    {
        if (!mode.HasStage(stage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                $"'{mode.Id}' runs stages {mode.StartingStage} to {mode.FinalStage}.");
        }
    }
}
