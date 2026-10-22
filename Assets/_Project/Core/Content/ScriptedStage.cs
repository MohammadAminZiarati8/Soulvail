using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Soulvail.Core.Content;

/// <summary>
/// One line of a scripted stage: in wave <see cref="Wave"/>, <see cref="Count"/> of
/// <see cref="SpecId"/>.
/// </summary>
/// <remarks>
/// A <see langword="readonly"/> struct for <see cref="RosterEntry"/>'s reason: a scripted stage is
/// a handful of these, and a class per line buys nothing.
/// </remarks>
public readonly struct ScriptedSpawn
{
    /// <param name="wave">The wave it arrives in, numbered from 1.</param>
    /// <param name="specId">The archetype, e.g. <c>enemy.frog</c>.</param>
    /// <param name="count">How many of it. At least 1.</param>
    /// <exception cref="ArgumentException"><paramref name="specId"/> is <c>default(ContentId)</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="wave"/> or <paramref name="count"/> is below 1.
    /// </exception>
    public ScriptedSpawn(int wave, ContentId specId, int count)
    {
        if (wave < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(wave), wave, "Waves are numbered from 1.");
        }

        if (specId.Value is null)
        {
            throw new ArgumentException(
                "specId must be a valid ContentId; default(ContentId) names no archetype.",
                nameof(specId));
        }

        if (count < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count), count, $"count of '{specId}' must be at least 1.");
        }

        Wave = wave;
        SpecId = specId;
        Count = count;
    }

    /// <summary>The wave it arrives in, numbered from 1.</summary>
    public int Wave { get; }

    /// <summary>The archetype.</summary>
    public ContentId SpecId { get; }

    /// <summary>How many of it. Always at least 1.</summary>
    public int Count { get; }
}

/// <summary>
/// A stage whose enemies are written by hand rather than bought from the threat budget (JG-01).
/// </summary>
/// <remarks>
/// <para>
/// <b>It replaces the composition and nothing else.</b> <c>WaveComposer</c> writes these waves
/// instead of drawing them; the depth's hit-point, damage and speed curves still apply to every
/// body, the director still paces and telegraphs them, and the concurrency curve still caps the
/// arena. A stage not scripted is composed exactly as before.
/// </para>
/// <para>
/// Immutable and copied on construction, like every spec. Which archetypes it may name, which
/// stage it may stand on and how many waves it may have are the mode's to check, because only the
/// mode knows its roster, its bosses and its wave curve.
/// </para>
/// </remarks>
public sealed class ScriptedStage
{
    private readonly ReadOnlyCollection<ScriptedSpawn> _spawns;

    /// <param name="stage">The stage this replaces, numbered from 1.</param>
    /// <param name="spawns">
    /// Every line, any order. Copied. Waves must run 1, 2, … with none skipped, and an archetype
    /// appears at most once per wave — two lines for one archetype in one wave are one line with
    /// the counts added, and the author should write that.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="spawns"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="spawns"/> is empty, holds a <c>default(ScriptedSpawn)</c>, skips a wave, or
    /// names one archetype twice in one wave.
    /// </exception>
    public ScriptedStage(int stage, IReadOnlyList<ScriptedSpawn> spawns)
    {
        if (stage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage), stage, "Stages are numbered from 1.");
        }

        if (spawns is null)
        {
            throw new ArgumentNullException(nameof(spawns));
        }

        if (spawns.Count == 0)
        {
            throw new ArgumentException(
                $"Scripted stage {stage} spawns nothing. A stage is never empty; remove the row "
                    + "to let the budget compose it.",
                nameof(spawns));
        }

        var copy = new ScriptedSpawn[spawns.Count];
        int waves = 0;

        for (int i = 0; i < spawns.Count; i++)
        {
            ScriptedSpawn spawn = spawns[i];

            if (spawn.SpecId.Value is null)
            {
                throw new ArgumentException(
                    $"Scripted stage {stage}, line {i} names no archetype.", nameof(spawns));
            }

            for (int j = 0; j < i; j++)
            {
                if (copy[j].Wave == spawn.Wave && copy[j].SpecId == spawn.SpecId)
                {
                    throw new ArgumentException(
                        $"Scripted stage {stage} lists '{spawn.SpecId}' twice in wave {spawn.Wave}. "
                            + "Write it once with the counts added.",
                        nameof(spawns));
                }
            }

            copy[i] = spawn;
            waves = Math.Max(waves, spawn.Wave);
        }

        for (int wave = 1; wave <= waves; wave++)
        {
            if (!HasWave(copy, wave))
            {
                throw new ArgumentException(
                    $"Scripted stage {stage} has a wave {waves} but no wave {wave}. Waves run "
                        + "1, 2, … with none skipped.",
                    nameof(spawns));
            }
        }

        Stage = stage;
        WaveCount = waves;
        _spawns = Array.AsReadOnly(copy);
    }

    /// <summary>The stage this replaces.</summary>
    public int Stage { get; }

    /// <summary>How many waves it is delivered in — the highest wave any line names.</summary>
    public int WaveCount { get; }

    /// <summary>Every line, in authored order.</summary>
    public IReadOnlyList<ScriptedSpawn> Spawns => _spawns;

    private static bool HasWave(ScriptedSpawn[] spawns, int wave)
    {
        for (int i = 0; i < spawns.Length; i++)
        {
            if (spawns[i].Wave == wave)
            {
                return true;
            }
        }

        return false;
    }
}
