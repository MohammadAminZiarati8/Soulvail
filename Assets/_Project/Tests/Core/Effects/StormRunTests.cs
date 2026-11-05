using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

[TestFixture]
public sealed class StormRunTests
{
    private const int Capacity = 32;
    private const float WeaponDamage = 20f;
    private const float DamageOfWeapon = 0.8f;
    private static readonly ContentId SkillId = new("skill.skirmisher.storm-run");
    private static readonly ContentId EnemyId = new("enemy.husk");
    private static readonly ContentId StatusId = new("status.storm-run");

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private RunSession _session;
    private WorldSnapshot _snapshot;

    [SetUp]
    public void SetUp() => _events = new RecordingEvents();

    [Test]
    public void Cast_StartsAndPublishes_PostsStatus()
    {
        Start(Array.Empty<Vector3>());
        Tick(1f);

        Cast();

        StormRunChanged changed = _events.Single<StormRunChanged>();
        Assert.That(changed.Active, Is.True);
        Assert.That(changed.Until, Is.EqualTo(7f));
        Assert.That(_session.State.StatusCount, Is.EqualTo(1));
        StatusEntry status = _session.State.StatusAt(0);
        Assert.That(status.Id, Is.EqualTo(StatusId));
        Assert.That(status.IsBuff, Is.True);
        Assert.That(status.Remaining, Is.EqualTo(6f));
    }

    [Test]
    public void Active_RollCooldownHalved()
    {
        Start(Array.Empty<Vector3>());
        Cast();
        StartRoll();

        Tick(1f);
        Assert.That(_session.State.MovementSkillReadyCharges, Is.Zero);
        Tick(0.25f);
        Assert.That(_session.State.MovementSkillReadyCharges, Is.EqualTo(1));
    }

    [Test]
    public void RunningRefill_KeepsItsTime()
    {
        Start(Array.Empty<Vector3>(), upgrade: Mod(PlayerStat.MovementSkillCharges, 1f));
        StartRoll();
        Cast();
        Tick(0.5f);
        StartRoll();

        Tick(1.75f);
        Assert.That(_session.State.MovementSkillReadyCharges, Is.Zero);
        Tick(0.125f);
        Assert.That(_session.State.MovementSkillReadyCharges, Is.EqualTo(1));
        Tick(1.25f);
        Assert.That(_session.State.MovementSkillReadyCharges, Is.EqualTo(2));
    }

    [Test]
    public void RollEnd_FiresOneArrowPerEnemy_CapEight()
    {
        var positions = new List<Vector3>();
        for (int x = 10; x >= 1; x--)
        {
            positions.Add(x * Vector3.UnitX);
        }

        positions.Add(Vector3.UnitZ);
        positions.Add(13f * Vector3.UnitX);
        Start(positions.ToArray());
        _snapshot.Enemies[9].BehindCover = true;

        Cast();
        Roll();

        var arrows = _events.Of<ProjectileFired>();
        Assert.That(arrows, Has.Count.EqualTo(StormRun.MaxArrows));
        Assert.That(arrows.All(arrow => arrow.IsVolley), Is.True);
        Assert.That(arrows.Select(arrow => arrow.Target),
            Is.EqualTo(new[] { Vector3.UnitZ }.Concat(Enumerable.Range(2, 7).Select(x => x * Vector3.UnitX))));
    }

    [Test]
    public void RollEnd_NoEnemy_NoArrows()
    {
        Start(new[] { 13f * Vector3.UnitZ });
        Cast();
        Roll();

        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
    }

    [Test]
    public void RollStart_FiresNothing()
    {
        Start(new[] { 4f * Vector3.UnitZ });
        Cast();

        StartRoll();
        Tick(0.25f);

        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);
        Tick(0.0625f);
        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(1),
            "The volley leaves when the roll stops moving, before its invulnerability trail ends.");
    }

    [Test]
    public void StormVolley_LeavesOwnVolleyAlone()
    {
        Start(new[] { 4f * Vector3.UnitZ }, withVolley: true, upgrade: Mod(PlayerStat.VolleyEvery, 1f));
        _snapshot.MoveInput = Vector2.Zero;
        _snapshot.PlayerVelocity = Vector3.Zero;
        for (int i = 0; i < 20 && !_session.State.IsVolleyReady; i++)
        {
            Tick(0.125f);
        }

        Assert.That(_session.State.IsVolleyReady, Is.True);
        _snapshot.MoveInput = Vector2.UnitX;
        int shots = _events.Count<ProjectileFired>();
        Cast();

        Roll();

        Assert.That(_events.Count<ProjectileFired>(), Is.EqualTo(shots + 1));
        Assert.That(_session.State.IsVolleyReady, Is.True);
        Assert.That(_session.State.VolleyShotsTowardNext, Is.EqualTo(1));
    }

    [Test]
    public void WorksWithoutVolleyTaken()
    {
        Start(new[] { 4f * Vector3.UnitZ }, withVolley: true);
        Assert.That(_session.State.VolleyEvery, Is.Zero);
        Cast();
        Roll();

        Assert.That(_events.Single<ProjectileFired>().IsVolley, Is.True);
        Assert.That(_session.State.VolleyShotsTowardNext, Is.Zero);
    }

    [Test]
    public void StormVolley_CanShareAFrameWithAWeaponShot()
    {
        Start(new[] { 4f * Vector3.UnitZ }, firesWhileMoving: true);
        Cast();
        Roll();

        Assert.That(_events.Of<ProjectileFired>().Select(arrow => arrow.IsVolley), Is.EqualTo(new[] { false, true }));
    }

    [Test]
    public void StormVolley_HitsStartBurst()
    {
        Start(new[] { 4f * Vector3.UnitZ }, upgrade: Mod(PlayerStat.HitSpeedBurst, 0.2f));
        Cast();
        Roll();
        Tick(0.25f);

        Assert.That(_events.Single<EnemyDamaged>().Amount, Is.EqualTo(DamageOfWeapon * WeaponDamage));
        Assert.That(_session.State.StatusAt(1).Id, Is.EqualTo(new ContentId(HitAndRun.StatusKey)));
    }

    [Test]
    public void End_ReleasesAndClears()
    {
        Start(new[] { 4f * Vector3.UnitZ });
        Cast();
        Tick(5.5f);
        StartRoll();
        Tick(0.375f);

        var changes = _events.Of<StormRunChanged>();
        Assert.That(changes, Has.Count.EqualTo(2));
        Assert.That(changes[1].Active, Is.False);
        Assert.That(changes[1].Until, Is.EqualTo(6f));
        Assert.That(_session.State.StatusCount, Is.Zero);
        Assert.That(_events.Count<ProjectileFired>(), Is.Zero);

        // The refill that started during Storm Run still ends on its original deadline.
        Tick(0.875f);
        Assert.That(_session.State.MovementSkillReadyCharges, Is.EqualTo(1));
        StartRoll();
        Tick(1.25f);
        Assert.That(_session.State.MovementSkillReadyCharges, Is.Zero);
        Tick(1.25f);
        Assert.That(_session.State.MovementSkillReadyCharges, Is.EqualTo(1));
    }

    [Test]
    public void Boundary_Ends()
    {
        Start(Array.Empty<Vector3>(), withWaves: true);
        _snapshot.SpawnPoints = new[] { 6f * Vector3.UnitZ };
        for (int i = 0; i < 160 && _events.Count<EnemySpawned>() == 0; i++)
        {
            Tick(0.125f);
        }

        AddSense(_events.Single<EnemySpawned>());
        Cast();
        Roll();
        for (int i = 0; i < 12 && _events.Count<StageCleared>() == 0; i++)
        {
            Tick(0.125f);
        }

        Assert.That(_events.Count<StageCleared>(), Is.EqualTo(1));
        Assert.That(_events.Of<StormRunChanged>().Select(change => change.Active), Is.EqualTo(new[] { true, false }));
        Assert.That(_session.State.StatusCount, Is.Zero);
    }

    [Test]
    public void Death_EndsStormRunOnce()
    {
        Start(new[] { Vector3.UnitZ }, lethalEnemy: true);
        Cast();

        for (int i = 0; i < 24 && _session.IsRunning; i++)
        {
            Tick(0.125f);
        }

        Assert.That(_events.Count<PlayerDied>(), Is.EqualTo(1));
        Assert.That(_events.Of<StormRunChanged>().Select(change => change.Active), Is.EqualTo(new[] { true, false }));
        Assert.That(_session.State.StatusCount, Is.Zero);
    }

    [TestCase(4, false)]
    [TestCase(5, true)]
    public void AutoCast_RequiresFiveEnemiesAndStartsThirtySecondCooldown(int count, bool expected)
    {
        Start(Enumerable.Range(1, count).Select(x => x * Vector3.UnitZ).ToArray());
        _session.SetAutoCast(SkillId, true);
        Tick(0.125f);

        Assert.That(_events.Count<StormRunChanged>(), Is.EqualTo(expected ? 1 : 0));
        Assert.That(_session.State.SkillCooldownSeconds(0), Is.EqualTo(30f));
        Assert.That(_session.State.IsSkillReady(0), Is.EqualTo(!expected));
    }

    [Test]
    public void Rank2_EightSeconds()
    {
        Start(Array.Empty<Vector3>(), upgrade: Mod(PlayerStat.StormRunSeconds, 2f));
        Cast();

        Assert.That(_events.Single<StormRunChanged>().Until, Is.EqualTo(8f));
        Assert.That(_session.State.StatusAt(0).Remaining, Is.EqualTo(8f));
    }

    [Test]
    public void Rank3_ThirtyPercent()
    {
        Start(new[] { 4f * Vector3.UnitZ }, upgrade: Mod(PlayerStat.StormRunDamage, 0.30f));
        Cast();
        Roll();
        Tick(0.25f);

        Assert.That(_events.Single<EnemyDamaged>().Amount, Is.EqualTo(DamageOfWeapon * WeaponDamage * 1.3f).Within(0.001f));
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        Start(new[] { 4f * Vector3.UnitZ }, upgrade: Mod(PlayerStat.StormRunSeconds, 1000f), silent: true);
        Cast();

        AllocationAssert.None(() =>
        {
            Roll();
            Tick(1f);
            _intents.Clear();
        }, iterations: 50);
    }

    private void Cast() => _session.CastSkillNow(SkillId);

    private void Roll()
    {
        StartRoll();
        Tick(0.375f);
    }

    private void StartRoll()
    {
        _session.MovementSkill();
        Tick(0.125f);
    }

    private void Tick(float dt)
    {
        _snapshot.Dt = dt;
        _session.Tick(_snapshot);
    }

    private void AddSense(EnemySpawned enemy) =>
        _snapshot.AddEnemy() = new EnemySense { Id = enemy.Id, Position = enemy.Position, HasLineOfSight = true };

    private void Start(Vector3[] positions, bool withVolley = false, ModifyStat upgrade = null,
        bool withWaves = false, bool silent = false, bool lethalEnemy = false, bool firesWhileMoving = false)
    {
        CharacterSpec character = new(new ContentId("character.ranger"),
            new LocKey("character.ranger.name"), new LocKey("character.ranger.description"), 100f,
            new MovementSpec(3.4f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Projectile, WeaponDamage, 1.5f, 12f, 360f, 0.15f, 25f, 0.1f,
                firesWhileMoving: firesWhileMoving),
            new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 5f, 0.3f, 2.5f, 0.15f, 0f, 0f, 0.05f),
            volley: withVolley ? new VolleySpec(3, 30f, 1.5f) : null);
        EnemySpec enemy = new(EnemyId, new LocKey("enemy.husk.name"), withWaves ? 1f : 10000f, 2f, 1, 4, 12f,
            false, lethalEnemy ? 400f : 8f, 2f, 0.05f, 0.6f, 30f,
            lethalEnemy ? EnemyBehaviourKind.Chaser : EnemyBehaviourKind.Static);
        ContentId modeId = new("mode.storm-run-test");
        var scaling = new ScalingSpec(new BudgetCurve(4f, 0f, 0f), new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(Capacity, 1000), new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1), new StatCurve(0.02f, 1.3f, 5, 0));
        var mode = new ModeSpec(modeId, new LocKey("mode.storm-run.name"), 1, true, 0, scaling,
            Scalings.Xp(), withWaves ? new[] { new RosterEntry(EnemyId, 1) } : Array.Empty<RosterEntry>());
        var storm = new SkillSpec(SkillId, new LocKey("skill.storm-run.name"), new LocKey("skill.storm-run.desc"),
            SkillKind.Active, Array.Empty<IEffect>(), new ActiveSpec(30f,
                new TriggerSpec(new[] { new TriggerClause(TriggerField.EnemiesInAcquireRange, TriggerComparison.AtLeast, 5f) }),
                new IEffect[] { new StormRun(DamageOfWeapon) }));
        SkillSpec rank = Passive("skill.test.storm-rank", upgrade ?? Mod(PlayerStat.MaxHp, 1f));
        SkillSpec filler = Passive("skill.test.filler", Mod(PlayerStat.MaxHp, 1f));
        var tree = new SkillTreeSpec(new ContentId("tree.test"), character.Id,
            new[] { Branch(storm.Id), Branch(rank.Id), Branch(filler.Id) });
        var catalog = new ContentCatalog(new[] { character }, new[] { enemy }, new[] { mode },
            new[] { storm, rank, filler }, new[] { tree });
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, character.Id, 7, 1,
            new RandomState(101, 102, 103, 104, 105), 100f, 0f, 0f, default, 3, 0f, 0,
            upgrade is null ? new[] { SkillId } : new[] { SkillId, rank.Id }, new ContentId[SkillRunner.MaxManualSlots],
            default, Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        var random = new FixedRandom(7);
        IDomainEvents events = silent ? new SilentEvents() : _events;
        _intents = new RecordingIntents();
        _session = new RunSession(catalog, random, events, _intents,
            new RunRecorder(random, new FixedClock(default), events), Capacity, Capacity, Capacity);
        var spawn = new SpawnPlan(positions.Select(position => new SpawnPlan.Entry(EnemyId, position)).ToArray());
        _session.Start(new RunConfig(modeId, character.Id, 7, 1, spawn, saved));
        _session.SetAutoCast(SkillId, false);

        _snapshot = new WorldSnapshot(Capacity) { MoveInput = Vector2.UnitX, PlayerVelocity = Vector3.UnitX };
        if (silent)
        {
            // A scripted spawn plan assigns the first enemy id 1.
            _snapshot.AddEnemy() = new EnemySense { Id = 1, Position = positions[0], HasLineOfSight = true };
        }
        else
        {
            foreach (EnemySpawned spawned in _events.Of<EnemySpawned>())
            {
                AddSense(spawned);
            }
        }
    }

    private static ModifyStat Mod(PlayerStat stat, float value) => new(stat, ModifierKind.Flat, value);

    private static SkillSpec Passive(string id, ModifyStat effect) =>
        new(new ContentId(id), new LocKey("skill.test.name"), new LocKey("skill.test.desc"), SkillKind.Passive,
            new IEffect[] { effect });

    private static SkillBranchSpec Branch(ContentId id) =>
        new(new LocKey("branch.test"), new IReadOnlyList<ContentId>[] { new[] { id } });
}
