using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// JG-01's hand-written stages: what a <see cref="ScriptedStage"/> accepts, and what a
/// <see cref="ModeSpec"/> refuses to carry. What the composer does with one is
/// <c>WaveComposerTests</c>'.
/// </summary>
[TestFixture]
public sealed class ScriptedStageTests
{
    private static readonly ContentId Frog = new ContentId("enemy.frog");
    private static readonly ContentId Pitcher = new ContentId("enemy.pitcher");

    // ---- Rule 1: a scripted stage ------------------------------------------------------------------

    [Test]
    public void Stage_WaveCountIsTheHighestWave()
    {
        var stage = new ScriptedStage(3, new[]
        {
            new ScriptedSpawn(2, Pitcher, 2),
            new ScriptedSpawn(1, Frog, 3),
        });

        Assert.That(stage.Stage, Is.EqualTo(3));
        Assert.That(stage.WaveCount, Is.EqualTo(2));
        Assert.That(stage.Spawns, Has.Count.EqualTo(2));
        Assert.That(stage.Spawns[0].SpecId, Is.EqualTo(Pitcher), "Authored order is kept.");
    }

    [Test]
    public void Stage_CopiesItsLines()
    {
        var lines = new List<ScriptedSpawn> { new ScriptedSpawn(1, Frog, 3) };
        var stage = new ScriptedStage(1, lines);

        lines.Add(new ScriptedSpawn(1, Pitcher, 1));

        Assert.That(stage.Spawns, Has.Count.EqualTo(1));
    }

    [Test]
    public void Spawn_RefusesABadLine()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScriptedSpawn(0, Frog, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScriptedSpawn(1, Frog, 0));
        Assert.Throws<ArgumentException>(() => new ScriptedSpawn(1, default, 1));
    }

    [Test]
    public void Stage_RefusesABadStage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScriptedStage(0, new[] { new ScriptedSpawn(1, Frog, 1) }));
        Assert.Throws<ArgumentNullException>(() => new ScriptedStage(1, null));
        Assert.Throws<ArgumentException>(() => new ScriptedStage(1, Array.Empty<ScriptedSpawn>()), "Empty.");
        Assert.Throws<ArgumentException>(() => new ScriptedStage(1, new[] { default(ScriptedSpawn) }), "Default line.");
    }

    [Test]
    public void Stage_RefusesASkippedWave()
    {
        var thrown = Assert.Throws<ArgumentException>(() =>
            new ScriptedStage(1, new[] { new ScriptedSpawn(1, Frog, 1), new ScriptedSpawn(3, Frog, 1) }));

        Assert.That(thrown.Message, Does.Contain("no wave 2"));
    }

    [Test]
    public void Stage_RefusesOneArchetypeTwiceInAWave()
    {
        Assert.Throws<ArgumentException>(() =>
            new ScriptedStage(1, new[] { new ScriptedSpawn(1, Frog, 1), new ScriptedSpawn(1, Frog, 2) }));

        Assert.DoesNotThrow(() =>
            new ScriptedStage(1, new[] { new ScriptedSpawn(1, Frog, 1), new ScriptedSpawn(2, Frog, 2) }),
            "The same archetype in two waves is two lines, and legal.");
    }

    // ---- Rule 2: what a mode carries -----------------------------------------------------------------

    [Test]
    public void Mode_FindsItsScriptedStage()
    {
        ModeSpec mode = Mode(Scripted(1, Frog, 3), Scripted(2, Pitcher, 2));

        Assert.That(mode.TryGetScriptedStage(1, out ScriptedStage first), Is.True);
        Assert.That(first.Spawns[0].Count, Is.EqualTo(3));
        Assert.That(mode.TryGetScriptedStage(2, out ScriptedStage second), Is.True);
        Assert.That(second.Spawns[0].SpecId, Is.EqualTo(Pitcher));
        Assert.That(mode.TryGetScriptedStage(3, out ScriptedStage none), Is.False);
        Assert.That(none, Is.Null);
        Assert.That(mode.ScriptedStages, Has.Count.EqualTo(2));
    }

    [Test]
    public void Mode_WithoutScriptedStagesHasNone()
    {
        ModeSpec mode = Mode();

        Assert.That(mode.ScriptedStages, Is.Empty);
        Assert.That(mode.TryGetScriptedStage(1, out _), Is.False);
    }

    [Test]
    public void Mode_RefusesAStageScriptedTwice()
    {
        var thrown = Assert.Throws<ArgumentException>(() => Mode(Scripted(1, Frog, 3), Scripted(1, Frog, 1)));

        Assert.That(thrown.ParamName, Is.EqualTo("scriptedStages"));
    }

    [Test]
    public void Mode_RefusesAnArchetypeOutsideItsRoster()
    {
        var thrown = Assert.Throws<ArgumentException>(() => Mode(Scripted(1, new ContentId("enemy.bloater"), 1)));

        Assert.That(thrown.Message, Does.Contain("enemy.bloater"));
    }

    [Test]
    public void Mode_RefusesAStageItDoesNotHave()
    {
        Assert.Throws<ArgumentException>(() => Mode(Scripted(31, Frog, 1)), "Past the final stage.");
    }

    [Test]
    public void Mode_RefusesABossStage()
    {
        var thrown = Assert.Throws<ArgumentException>(() => Mode(Scripted(5, Frog, 1)));

        Assert.That(thrown.Message, Does.Contain("boss.warden"));
    }

    [Test]
    public void Mode_RefusesMoreWavesThanItsCurve()
    {
        var lines = new ScriptedSpawn[6];

        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = new ScriptedSpawn(i + 1, Frog, 1);
        }

        Assert.Throws<ArgumentException>(() => Mode(new ScriptedStage(1, lines)), "The design curve stops at 5.");
    }

    [Test]
    public void Mode_RefusesANullStage()
    {
        Assert.Throws<ArgumentException>(() => Mode(new ScriptedStage[] { null }));
    }

    [Test]
    public void Mode_TryGetScriptedStage_AllocatesNothing()
    {
        ModeSpec mode = Mode(Scripted(1, Frog, 3), Scripted(2, Pitcher, 2));

        AllocationAssert.None(() => mode.TryGetScriptedStage(2, out _), 10_000);
    }

    private static ScriptedStage Scripted(int stage, ContentId specId, int count) =>
        new ScriptedStage(stage, new[] { new ScriptedSpawn(1, specId, count) });

    /// <summary>The Jungle's shape: thirty stages, the Frog and the Pitcher, a boss every fifth.</summary>
    private static ModeSpec Mode(params ScriptedStage[] scripted) => new ModeSpec(
        new ContentId("mode.test"),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: false,
        finalStage: 30,
        Scalings.Design(),
        Scalings.Xp(),
        new[] { new RosterEntry(Frog, 1), new RosterEntry(Pitcher, 2) },
        bossRoster: new[] { new BossRosterEntry(new ContentId("boss.warden"), 5) },
        scriptedStages: scripted);
}
