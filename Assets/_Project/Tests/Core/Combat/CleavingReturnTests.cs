using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class CleavingReturnTests
{
    [Test]
    public void ReturnHit_Slows_OutHitDoesNot()
    {
        var (events, player, stats, enemies, axes) = Build();
        var huskId = new ContentId("enemy.husk");
        EnemyAgent first = enemies.Spawn(huskId, 3f * Vector3.UnitX);
        EnemyAgent second = enemies.Spawn(huskId, 5f * Vector3.UnitX);

        stats.Resolve(PlayerStat.AxeReturnSlow).Base = 1f;
        Assert.That(axes.TryThrow(Vector3.Zero, Vector3.UnitX, 10f, 0f), Is.True);
        axes.Tick(1f, 1f, Vector3.Zero, player, enemies, null);
        Assert.That(events.Of<EnemyStatusChanged>(), Is.Empty);

        axes.Tick(1f, 2f, Vector3.Zero, player, enemies, null);

        Assert.That(events.Of<AxeHit>(), Has.Count.EqualTo(4));
        Assert.That(first.CrowdControl.SlowFraction, Is.EqualTo(0.9f));
        Assert.That(second.CrowdControl.SlowFraction, Is.EqualTo(0.9f));
        var statuses = events.Of<EnemyStatusChanged>();
        Assert.That(statuses, Has.Count.EqualTo(2));
        Assert.That(statuses[0].Seconds, Is.EqualTo(1.5f));
        Assert.That(statuses[1].Seconds, Is.EqualTo(1.5f));
    }

    [Test]
    public void ReturnSlow_Boss_Immune()
    {
        var (events, player, stats, enemies, axes) = Build();
        EnemyAgent boss = enemies.SpawnBoss(new ContentId("boss.test"), 6f * Vector3.UnitX);
        stats.Resolve(PlayerStat.AxeReturnSlow).Base = 0.35f;

        Assert.That(axes.TryThrow(Vector3.Zero, Vector3.UnitX, 10f, 0f), Is.True);
        axes.Tick(1f, 1f, Vector3.Zero, player, enemies, null);
        axes.Tick(1f, 2f, Vector3.Zero, player, enemies, null);

        Assert.That(events.Of<AxeHit>(), Has.Count.EqualTo(2));
        Assert.That(events.Of<AxeHit>()[1].Pass, Is.EqualTo(AxePass.Back));
        Assert.That(boss.CrowdControl.SlowFraction, Is.Zero);
        Assert.That(events.Of<EnemyStatusChanged>(), Is.Empty);
    }

    private static (RecordingEvents, PlayerCombat, PlayerStats, EnemySystem, AxeSystem) Build()
    {
        const int capacity = 8;
        var events = new RecordingEvents();
        var intents = new RecordingIntents();
        var character = new CharacterSpec(
            new ContentId("character.beastmaster"), new LocKey("character.beastmaster.name"),
            new LocKey("character.beastmaster.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.ReturningAxe, 10f, 2f, 8f, 360f, 0.4f,
                shotRadius: 0.3f, axeSpeed: 8f, axeCarry: 1, axeHitsPerPass: 3),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Swap, 8f, 0.25f, 6f, 0.15f, 0f, 0f, 0.2f));
        var huskId = new ContentId("enemy.husk");
        var bodyId = new ContentId("enemy.boss_body");
        var bossId = new ContentId("boss.test");
        var husk = new EnemySpec(huskId, new LocKey("enemy.husk.name"), 100f, 2f, 1, 4, 5f,
            false, 8f, 1.2f, 0.2f, 0.2f, 30f, EnemyBehaviourKind.Static);
        var body = new EnemySpec(bodyId, new LocKey("enemy.boss_body.name"), 900f, 1f, 8, 40, 120f,
            false, 20f, 2f, 1f, 1f, 30f, EnemyBehaviourKind.Boss);
        var bossSpec = new BossSpec(bossId, bodyId, new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f);
        var catalog = new ContentCatalog(new[] { character }, new[] { husk, body }, bosses: new[] { bossSpec });
        var enemies = new EnemySystem(catalog, events, new FixedRandom(), new DepthScaling(Scalings.Design()), capacity);
        var player = new PlayerCombat(character, events, intents, capacity);
        var stats = new PlayerStats(player, new PlayerMotor(character.Movement, Vector3.UnitZ),
            new LevelTracker(Scalings.Xp(), events));
        var axes = new AxeSystem(events, stats, character.Weapon);
        return (events, player, stats, enemies, axes);
    }
}
