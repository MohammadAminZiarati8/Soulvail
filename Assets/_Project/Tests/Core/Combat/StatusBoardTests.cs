using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

[TestFixture]
public sealed class StatusBoardTests
{
    private static readonly ContentId A = new("status.test.a");
    private static readonly ContentId B = new("status.test.b");
    private static readonly ContentId C = new("status.test.c");

    [Test]
    public void Post_RefreshesById()
    {
        var board = new StatusBoard();
        board.Post(A, true, 4f, 1);
        board.Tick(1f);
        board.Post(A, true, 6f, 3);
        Assert.That(board.Count, Is.EqualTo(1));
        Assert.That(board[0].Remaining, Is.EqualTo(6f));
        Assert.That(board[0].Duration, Is.EqualTo(6f));
        Assert.That(board[0].Stacks, Is.EqualTo(3));
        Assert.That(board[0].Fraction, Is.EqualTo(1f));
    }

    [Test]
    public void Tick_DropsExpired()
    {
        var board = new StatusBoard();
        board.Post(A, false, 1f, 1);
        board.Post(B, true, 3f, 1);
        board.Tick(0.5f);
        Assert.That(board[0].Remaining, Is.EqualTo(0.5f).Within(1e-5f));
        Assert.That(board[0].Fraction, Is.EqualTo(0.5f).Within(1e-5f));
        board.Tick(0.5f);
        Assert.That(board.Count, Is.EqualTo(1));
        Assert.That(board[0].Id, Is.EqualTo(B));
        board.Tick(float.NaN);
        Assert.That(board[0].Remaining, Is.EqualTo(2f).Within(1e-5f));
    }

    [Test]
    public void Untimed_StaysUntilCleared()
    {
        var board = new StatusBoard();
        board.Post(A, true, float.PositiveInfinity, 2);
        board.Tick(1000f);
        Assert.That(board.Count, Is.EqualTo(1));
        Assert.That(board[0].IsTimed, Is.False);
        board.Clear(B);
        Assert.That(board.Count, Is.EqualTo(1));
        board.Clear(A);
        Assert.That(board.Count, Is.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => board.Post(A, true, 0f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.Post(A, true, float.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => board.Post(A, true, 1f, 0));
        Assert.Throws<ArgumentException>(() => board.Post(default, true, 1f, 1));
    }

    [Test]
    public void Post_OverCapacity_Refused()
    {
        var board = new StatusBoard();
        for (int i = 0; i < StatusBoard.Capacity; i++)
        {
            board.Post(new ContentId($"status.test.s{i}"), true, 5f, 1);
        }

        Assert.That(board.HasRefused, Is.False);
        board.Post(A, false, 5f, 1);
        Assert.That(board.Count, Is.EqualTo(StatusBoard.Capacity));
        Assert.That(board.HasRefused, Is.True);
        board.Post(new ContentId("status.test.s3"), true, 9f, 2);
        Assert.That(board[3].Stacks, Is.EqualTo(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = board[StatusBoard.Capacity]);
    }

    [Test]
    public void Order_DebuffsFirst()
    {
        var board = new StatusBoard();
        board.Post(A, true, 5f, 1);
        board.Post(B, false, 5f, 1);
        board.Post(C, true, 5f, 1);
        var d = new ContentId("status.test.d");
        board.Post(d, false, 5f, 1);
        Assert.That(Ids(board), Is.EqualTo(new[] { B, d, A, C }));
        board.Post(A, false, 5f, 1);
        Assert.That(Ids(board), Is.EqualTo(new[] { B, d, A, C }));
        board.Clear(B);
        board.Post(B, true, 5f, 1);
        Assert.That(Ids(board), Is.EqualTo(new[] { d, A, C, B }));
    }

    [Test]
    public void SteadyAim_PostsStacks_ClearsAtZero()
    {
        var board = new StatusBoard();
        var aim = new SteadyAim(new SilentEvents());
        aim.PerStack.Base = 0.05f;
        aim.AttachStatus(board);
        Assert.That(board.Count, Is.Zero);
        var id = new ContentId(SteadyAim.StatusKey);
        for (int i = 1; i <= 7; i++)
        {
            aim.OnLoose();
            Assert.That(board.Count, Is.EqualTo(1));
            Assert.That(board[0].Id, Is.EqualTo(id));
            Assert.That(board[0].IsBuff, Is.True);
            Assert.That(board[0].IsTimed, Is.False);
            Assert.That(board[0].Stacks, Is.EqualTo(Math.Min(i, 5)));
        }

        aim.OnMoved();
        Assert.That(board.Count, Is.Zero);
    }

    [Test]
    public void Guard_PostedAtFullStacks()
    {
        var board = new StatusBoard();
        var aim = new SteadyAim(new SilentEvents());
        aim.PerStack.Base = 0.05f;
        aim.MaxStacks.Base = 3f;
        aim.AttachStatus(board);
        var guard = new ContentId(SteadyAim.GuardStatusKey);
        for (int i = 0; i < 4; i++)
        { aim.OnLoose(); }
        Assert.That(board.Count, Is.EqualTo(1), "no guard while Guard is 0");

        aim.Guard.Base = 0.2f;
        aim.OnLoose();
        Assert.That(board.Count, Is.EqualTo(2));
        Assert.That(board[1].Id, Is.EqualTo(guard));
        Assert.That(board[1].IsTimed, Is.False);

        aim.OnMoved();
        aim.OnLoose();
        Assert.That(Ids(board), Is.EqualTo(new[] { new ContentId(SteadyAim.StatusKey) }));
    }

    [Test]
    public void Boundary_Clears()
    {
        var board = new StatusBoard();
        board.Post(A, false, 5f, 1);
        board.Post(B, true, float.PositiveInfinity, 3);
        board.Reset();
        Assert.That(board.Count, Is.Zero);
        board.Post(C, true, 5f, 1);
        Assert.That(Ids(board), Is.EqualTo(new[] { C }));

        RunSession session = Session(out ModeSpec mode, out SkillSpec skill);
        var saved = new RunSnapshot(RunSnapshot.CurrentVersion, mode.Id, Character().Id, 99, 1,
            new RandomState(0, 0, 0, 0, 0), 100f, 0f, 0f, default, 2, 0f, 0,
            new[] { skill.Id }, new ContentId[SkillRunner.MaxManualSlots], default,
            Array.Empty<ContentId>(), Array.Empty<ContentId>(), Array.Empty<ContentId>());
        session.Start(new RunConfig(mode.Id, Character().Id, 99, 1, SpawnPlan.Empty, saved));
        var snapshot = new WorldSnapshot(8) { Dt = 0.01f, HasGate = true, GatePosition = Vector3.Zero, SpawnPoints = new[] { new Vector3(0f, 0f, 7f) } };
        int peak = 0;
        for (int i = 0; i < 2000 && session.State.StageIndex == 1; i++)
        {
            snapshot.EnemyCount = 1;
            snapshot.Enemies[0] = new EnemySense { Id = 1, Position = new Vector3(0f, 0f, 7f), HasLineOfSight = true };
            session.Tick(snapshot);
            if (session.State.StatusCount > 0)
            {
                peak = Math.Max(peak, session.State.StatusAt(0).Stacks);
            }

            if (session.State.IsSanctumOpen)
            { session.LeaveSanctum(); }
        }

        Assert.That(session.State.StageIndex, Is.EqualTo(2));
        Assert.That(peak, Is.GreaterThan(0), "Steady Aim posted while standing");
        Assert.That(session.State.StatusCount, Is.Zero);
        session.End();
        Assert.That(session.State.StatusCount, Is.Zero);
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        var board = new StatusBoard();
        var aim = new SteadyAim(new SilentEvents());
        aim.PerStack.Base = 0.05f;
        aim.Guard.Base = 0.2f;
        aim.AttachStatus(board);
        board.Post(A, false, 1f, 1);
        AllocationAssert.None(() =>
        {
            board.Post(B, true, 0.5f, 2);
            board.Post(C, false, float.PositiveInfinity, 1);
            for (int i = 0; i < 6; i++)
            { aim.OnLoose(); }
            board.Tick(0.1f);
            for (int i = 0; i < board.Count; i++)
            { _ = board[i].Fraction; }
            aim.OnMoved();
            board.Clear(C);
            board.Reset();
        });
    }

    private static ContentId[] Ids(StatusBoard board)
    {
        var ids = new ContentId[board.Count];
        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = board[i].Id;
        }

        return ids;
    }

    private static RunSession Session(out ModeSpec mode, out SkillSpec skill)
    {
        var events = new RecordingEvents();
        skill = new SkillSpec(new ContentId("skill.test.aim"), new LocKey("skill.test.name"),
            new LocKey("skill.test.desc"), SkillKind.Passive,
            new IEffect[] { new ModifyStat(PlayerStat.SteadyAimPerStack, ModifierKind.Flat, 0.05f) });
        SkillBranchSpec Branch(string id, ContentId node) => new(new LocKey(id),
            new System.Collections.Generic.IReadOnlyList<ContentId>[] { new[] { node } });
        var other = new SkillSpec(new ContentId("skill.test.other"), new LocKey("skill.test.name"),
            new LocKey("skill.test.desc"), SkillKind.Passive, new IEffect[] { new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f) });
        var third = new SkillSpec(new ContentId("skill.test.third"), new LocKey("skill.test.name"),
            new LocKey("skill.test.desc"), SkillKind.Passive, new IEffect[] { new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 1f) });
        var tree = new SkillTreeSpec(new ContentId("tree.test.bow"), Character().Id,
            new[] { Branch("tree.test.a", skill.Id), Branch("tree.test.b", other.Id), Branch("tree.test.c", third.Id) });
        mode = new ModeSpec(new ContentId("mode.test.empty"), new LocKey("mode.test.name"),
            1, true, 0, new ScalingSpec(new BudgetCurve(4f, 0f, 0f),
                new WaveCurve(1, 1000, 1, 1), new ConcurrencyCurve(8, 1000),
                new StatCurve(0.06f, 4f, 1, 1), new StatCurve(0.035f, 3f, 1, 1),
                new StatCurve(0.02f, 1.3f, 5, 0)), Scalings.Xp(),
            new[] { new RosterEntry(Enemy().Id, 1) });
        var catalog = new ContentCatalog(new[] { Character() }, new[] { Enemy(70f) }, new[] { mode },
            new[] { skill, other, third }, new[] { tree });
        var random = new FixedRandom(99);
        return new RunSession(catalog, random, events, new RecordingIntents(),
            new RunRecorder(random, new FixedClock(default), events), 8, 8, 16);
    }

    private static CharacterSpec Character() => new(new ContentId("character.test.bow"),
        new LocKey("character.test.name"), new LocKey("character.test.desc"), 100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Projectile, 13f, 2f, 12f, 360f, 0.5f, 40f, 0.5f, firesWhileMoving: false),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 6f, 0.3f, 3f, 0.15f, 0f, 0f, 0.05f),
        volley: new VolleySpec(3, 30f, 1.5f));

    private static EnemySpec Enemy(float hp = 1000f) => new(new ContentId("enemy.test.husk"),
        new LocKey("enemy.test.name"), hp, 3.5f, 1, 4, 12f, false, 8f, 1.2f, 0.4f, 0.6f,
        30f, EnemyBehaviourKind.Static);
}
