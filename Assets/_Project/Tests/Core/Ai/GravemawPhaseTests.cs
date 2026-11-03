using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>JG-25d: below half health each cycle erupts twice before its one window, and Frogs crawl out of cracks.</summary>
/// <remarks>
/// The boss stands at the origin and the quarry still at (0, 0, 3). Phase 2's chase is 1.5 s, ring A
/// warns 1 s, B arms 0.6 s after A and warns 0.8 s. The open ground puts the first two cracks on
/// (5.5, 0, -1) and (-5, 0, 0): (0, 0, 5.5) is too near the player, (5, 0, 0) too near the first
/// crack, and (0, 0, -5) loses its tie with (-5, 0, 0) to the lower index.
/// </remarks>
[TestFixture]
public sealed class GravemawPhaseTests
{
    private const string BossId = "boss.gravemaw";
    private const int Capacity = 8;
    private const float Contact = 20f;
    private const float Step = 0.05f;
    private static readonly ContentId Frog = new("enemy.frog");
    private static readonly Vector3 Centre = new(0f, 0f, 3f);
    private static readonly Vector3 FirstCrack = new(5.5f, 0f, -1f);
    private static readonly Vector3 SecondCrack = new(-5f, 0f, 0f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private EnemyAgent _agent;
    private BossBehaviour _boss;
    private PlayerCombat _player;
    private PlayerMotor _motor;
    private ProjectileSystem _projectiles;
    private ShockwaveSystem _shockwaves;
    private GravemawStats _stats;
    private GravemawBehaviour _gravemaw;
    private List<Vector3> _ground;
    private float _now;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Character(), _events, _intents, Capacity);
        _motor = new PlayerMotor(Character().Movement, Vector3.UnitZ);
        _projectiles = new ProjectileSystem(_events, 8);
        _shockwaves = new ShockwaveSystem(_events);
        _stats = new GravemawStats();
        _enemies = new EnemySystem(Catalog(), _events, new FixedRandom(0.5f),
            new DepthScaling(Scalings.Design()), Capacity,
            (agent, _) => _gravemaw = new GravemawBehaviour(agent, _stats, new FixedRandom(0.5f).Misc, _motor,
                _shockwaves, Frog));
        _agent = _enemies.SpawnBoss(new ContentId(BossId), Vector3.Zero);
        _boss = (BossBehaviour)_agent.Behaviour;
        _ground = new List<Vector3>
        {
            new(0f, 0f, 5.5f), SecondCrack, new(5f, 0f, 0f), FirstCrack, new(0f, 0f, -5f),
        };
        _now = 0f;
        Place(Centre);
    }

    // ---- Rule 1 -------------------------------------------------------------------------------------

    [Test]
    public void PhaseTwo_BeatClearsAdds_ThenChases()
    {
        CensusUntil(() => _gravemaw.State == GravemawState.Surfaced);
        Assert.That(_gravemaw.Phase, Is.Zero);
        Hit(_agent.Health.Current - (0.49f * _agent.Health.MaxHp.Value));
        Census(Step);

        Assert.That(_gravemaw.Phase, Is.EqualTo(1), "The 50 % crossing.");
        Assert.That(_boss.IsInBeat, Is.True);
        Assert.That(Hit(10f).Applied, Is.Zero, "Invulnerable through the beat.");
        Assert.That(_gravemaw.AddCount, Is.Zero);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase));

        CensusUntil(() => !_boss.IsInBeat);
        float beatEnded = _now;
        CensusUntil(() => _gravemaw.State == GravemawState.Tunnel);
        Assert.That(_now - beatEnded, Is.EqualTo(1.5f).Within(Step * 1.5f), "Phase 2's chase.");
    }

    // ---- Rule 2 -------------------------------------------------------------------------------------

    [Test]
    public void Double_FirstEruptionHurtsAndThrows_NoWindow()
    {
        _gravemaw.OnPhaseEntered(1);
        FirstEruption();
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
        Assert.That(_intents.Throws.Count, Is.EqualTo(1));
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase), "Straight back under.");
        Assert.That(_agent.IsVulnerable, Is.False);
        Assert.That(_events.Count<GravemawWindow>(), Is.Zero);
        Assert.That(_events.Count<GravemawBurrowed>(), Is.EqualTo(2), "The opening burrow and A's.");
    }

    [Test]
    public void Double_SecondRingFromThrownPlayer_AtPointEight()
    {
        _gravemaw.OnPhaseEntered(1);
        FirstEruption();
        var landed = new Vector3(4f, 0f, 7f);
        Place(landed);
        Tick(0.25f);
        Tick(0.25f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase), "Not before 0.6 s.");
        Tick(0.125f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));

        IReadOnlyList<GravemawRingArmed> rings = _events.Of<GravemawRingArmed>();
        Assert.That(rings.Count, Is.EqualTo(2));
        Assert.That(rings[1].At, Is.EqualTo(landed), "The throw is not read as the quarry's speed.");
        Assert.That(rings[1].Seconds, Is.EqualTo(0.8f));
        IReadOnlyList<EnemyTelegraph> warnings = _events.Of<EnemyTelegraph>();
        Assert.That(warnings[warnings.Count - 1].Move, Is.EqualTo(GravemawBehaviour.EruptionMove));
        Assert.That(warnings[warnings.Count - 1].Duration, Is.EqualTo(0.8f));
    }

    [Test]
    public void Double_WindowAfterSecond_OneMove()
    {
        _gravemaw.OnPhaseEntered(1);
        FirstEruption();
        SecondEruption();
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Surfaced));
        Assert.That(_events.Count<GravemawErupted>(), Is.EqualTo(2));
        Assert.That(_events.Single<GravemawWindow>().Open, Is.True);

        Tick(0.5f);
        Tick(1f);
        Tick(3f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Dive));
        int moves = 0;
        foreach (EnemyTelegraph telegraph in _events.Of<EnemyTelegraph>())
        {
            moves += telegraph.Move == GravemawBehaviour.EruptionMove ? 0 : 1;
        }

        Assert.That(moves, Is.EqualTo(1), "One move a window, double surface or not.");
        Assert.That(_events.Count<ShockwaveEmitted>(), Is.EqualTo(1));
    }

    // ---- Rule 3 -------------------------------------------------------------------------------------

    [Test]
    public void PhaseTwo_ChaseIsShorter()
    {
        _gravemaw.OnPhaseEntered(1);
        Tick(0f);
        Tick(1.25f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase));
        Tick(0.25f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
    }

    [Test]
    public void PhaseOne_SingleSurface()
    {
        Tick(0f);
        Tick(1.5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Chase), "Phase 1 keeps its 2.5 s chase.");
        Tick(1f);
        Tick(1f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Surfaced), "The first eruption opens the window.");
        Assert.That(_events.Single<GravemawWindow>().Open, Is.True);
        Assert.That(_events.Count<GravemawCrack>(), Is.Zero);
    }

    // ---- Rule 4 -------------------------------------------------------------------------------------

    [Test]
    public void Cracks_TwoOpen_SpacedAndClear()
    {
        _gravemaw.OnPhaseEntered(1);
        FirstEruption();
        IReadOnlyList<GravemawCrack> cracks = _events.Of<GravemawCrack>();
        Assert.That(cracks.Count, Is.EqualTo(2));
        Assert.That(cracks[0].At, Is.EqualTo(FirstCrack), "Farthest from the boss, clear of the player.");
        Assert.That(cracks[1].At, Is.EqualTo(SecondCrack), "Spaced from the first; the tie to the lower index.");
        Assert.That(cracks[0].BossId, Is.EqualTo(_agent.Id));
        Assert.That(cracks[0].Seconds, Is.EqualTo(1f));
        Assert.That(_gravemaw.AddCount, Is.EqualTo(2), "Opening cracks count.");
    }

    [Test]
    public void Cracks_FullRoster_NoneOpen()
    {
        _gravemaw.OnPhaseEntered(1);
        FirstEruption();
        SecondEruption();
        Assert.That(_events.Count<GravemawCrack>(), Is.EqualTo(4));
        Assert.That(_gravemaw.AddCount, Is.EqualTo(4));

        Tick(0.5f);
        Tick(1f);
        Tick(3f);
        Tick(0.5f);
        FirstEruption();
        Assert.That(_events.Count<GravemawErupted>(), Is.EqualTo(3));
        Assert.That(_events.Count<GravemawCrack>(), Is.EqualTo(4), "Four adds out: no room for two more.");
        Assert.That(_gravemaw.AddCount, Is.EqualTo(4));
    }

    // ---- Rule 5 -------------------------------------------------------------------------------------

    [Test]
    public void Crack_Summons_Frog_PaysNoXp()
    {
        _gravemaw.OnPhaseEntered(1);
        FirstEruption();
        Tick(0.5f);
        Assert.That(Frogs(), Is.Empty, "Not before the crack's second.");
        Tick(0.5f);

        List<EnemySpawned> frogs = Frogs();
        Assert.That(frogs.Count, Is.EqualTo(2));
        Assert.That(frogs[0].Position, Is.EqualTo(FirstCrack));
        Assert.That(frogs[1].Position, Is.EqualTo(SecondCrack));
        Assert.That(_enemies.Registry.TryGet(frogs[0].Id, out EnemyAgent frog), Is.True);
        Assert.That(frog.PaysXp, Is.False);

        _enemies.ApplyDamage(frog.Id, 1_000f, _now, _player);
        Assert.That(_events.Of<EnemyDied>()[0].Id, Is.EqualTo(frog.Id), "Still a kill.");
    }

    [Test]
    public void Crack_NoRoom_Skipped()
    {
        for (int i = 1; i < Capacity; i++)
        {
            _enemies.Spawn(Frog, new Vector3(20f + i, 0f, 20f));
        }

        _gravemaw.OnPhaseEntered(1);
        FirstEruption();
        Assert.That(_events.Count<GravemawCrack>(), Is.EqualTo(2));
        Tick(0.5f);
        Tick(0.5f);
        Assert.That(Frogs().Count, Is.EqualTo(Capacity - 1), "No add past the registry's room.");
        Assert.That(_gravemaw.AddCount, Is.Zero);
    }

    // ---- Rule 6 -------------------------------------------------------------------------------------

    [Test]
    public void RollCall_DeadLeaves()
    {
        _gravemaw.OnPhaseEntered(1);
        FirstEruption();
        Tick(0.5f);
        Tick(0.5f);
        Assert.That(_gravemaw.AddCount, Is.EqualTo(2));
        _enemies.ApplyDamage(Frogs()[0].Id, 1_000f, _now, _player);
        Tick(0.01f);
        Assert.That(_gravemaw.AddCount, Is.EqualTo(1));
    }

    [Test]
    public void Beat_DespawnsAdds_CancelsCracks()
    {
        ToLiveAndOpening();
        List<EnemySpawned> frogs = Frogs();

        _gravemaw.ClearAdds(Ctx(0f));
        Assert.That(_gravemaw.AddCount, Is.Zero);
        Census(0.01f);
        Assert.That(_enemies.Registry.TryGet(frogs[0].Id, out _), Is.False);
        Assert.That(_enemies.Registry.TryGet(frogs[1].Id, out _), Is.False);

        Tick(1.5f);
        Assert.That(Frogs().Count, Is.EqualTo(2), "The cancelled cracks summon nothing.");
    }

    // ---- Rule 7 -------------------------------------------------------------------------------------

    [Test]
    public void Reset_EmptiesRollCall()
    {
        ToLiveAndOpening();
        List<EnemySpawned> frogs = Frogs();

        _gravemaw.Reset();
        Assert.That(_gravemaw.AddCount, Is.Zero);
        Assert.That(_gravemaw.Phase, Is.Zero);
        Assert.That(_enemies.Registry.TryGet(frogs[0].Id, out EnemyAgent frog) && frog.IsAlive, Is.True,
            "Reset never despawns.");

        Tick(0f);
        Tick(2f);
        Assert.That(Frogs().Count, Is.EqualTo(2), "The opening cracks went with the reset.");
    }

    [Test]
    public void Tick_DoesNotAllocate()
    {
        for (int i = 1; i < Capacity; i++)
        {
            _enemies.Spawn(Frog, new Vector3(20f + i, 0f, 20f));
        }

        float[] cycle = { 0f, 1.5f, 1f, 0.3f, 0.3f, 0.8f, 0.5f, 1f, 3f, 0.5f };
        var silent = new SilentEvents();
        var player = new PlayerCombat(Character(), silent, _intents, Capacity);
        player.Health.SetExternalInvulnerable(true);
        var projectiles = new ProjectileSystem(silent, 8);
        var gravemaw = new GravemawBehaviour(_agent, _stats, new FixedRandom(0.5f).Misc, _motor,
            new ShockwaveSystem(silent), Frog);
        int erupted = 0;
        int mostAdds = 0;
        AllocationAssert.None(() =>
        {
            _intents.Clear();
            projectiles.Clear();
            gravemaw.Reset();
            gravemaw.OnPhaseEntered(1);
            foreach (float dt in cycle)
            {
                _now += dt;
                gravemaw.Tick(new EnemyTickContext(dt, _now, player, _intents, silent, projectiles, _enemies,
                    openGround: _ground));
                erupted += gravemaw.State == GravemawState.Surfaced ? 1 : 0;
                mostAdds = Math.Max(mostAdds, gravemaw.AddCount);
            }
        }, 2);
        Assert.That(erupted, Is.GreaterThan(0), "The probe never reached B's window.");
        Assert.That(mostAdds, Is.EqualTo(2), "The probe never reached the cracks.");
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    /// <summary>Phase 2 from a fresh chase: 1.5 s, then ring A's 1 s warning.</summary>
    private void FirstEruption()
    {
        Tick(0f);
        Tick(1.5f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
        Tick(1f);
    }

    /// <summary>0.6 s under, then ring B's 0.8 s warning.</summary>
    private void SecondEruption()
    {
        Tick(0.3f);
        Tick(0.3f);
        Assert.That(_gravemaw.State, Is.EqualTo(GravemawState.Tunnel));
        Tick(0.8f);
    }

    /// <summary>A's two Frogs out, and B's two cracks still opening.</summary>
    private void ToLiveAndOpening()
    {
        _gravemaw.OnPhaseEntered(1);
        FirstEruption();
        SecondEruption();
        Assert.That(Frogs().Count, Is.EqualTo(2));
        Assert.That(_events.Count<GravemawCrack>(), Is.EqualTo(4));
    }

    private List<EnemySpawned> Frogs()
    {
        var frogs = new List<EnemySpawned>();
        foreach (EnemySpawned spawned in _events.Of<EnemySpawned>())
        {
            if (spawned.SpecId == Frog)
            {
                frogs.Add(spawned);
            }
        }

        return frogs;
    }

    private EnemyTickContext Ctx(float dt) =>
        new(dt, _now, _player, _intents, _events, _projectiles, _enemies, openGround: _ground);

    private void Tick(float dt)
    {
        _now += dt;
        _intents.Clear();
        _motor.ClearHold();
        _gravemaw.Tick(Ctx(dt));
    }

    private void Census(float dt)
    {
        _now += dt;
        _intents.Clear();
        _motor.ClearHold();
        _enemies.Tick(Ctx(dt));
    }

    private void CensusUntil(Func<bool> done)
    {
        for (int i = 0; i < 400 && !done(); i++)
        {
            Census(Step);
        }

        Assert.That(done(), Is.True, "The awaited state never came.");
    }

    private DamageResult Hit(float damage) => _enemies.ApplyDamage(_agent.Id, damage, _now, _player);

    private void Place(Vector3 player)
    {
        EnemyBlackboard board = _agent.Blackboard;
        var flat = new Vector2(player.X, player.Z);
        board.SelfPosition = Vector3.Zero;
        board.PlayerPosition = player;
        board.DistanceToPlayer = flat.Length();
        board.DirectionToPlayer = flat.LengthSquared() > 0f ? Vector2.Normalize(flat) : Vector2.Zero;
        board.PathDirectionToPlayer = board.DirectionToPlayer;
    }

    private static ContentCatalog Catalog()
    {
        var boss = new BossSpec(new ContentId(BossId), Body().Id,
            new[] { new BossPhaseSpec(1f), new BossPhaseSpec(0.5f) }, 1.5f);
        var frog = new EnemySpec(Frog, new LocKey("enemy.frog.name"), 50f, 0f, 8, 40, 300f, false,
            0f, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Static);
        return new ContentCatalog(new[] { Character() }, new[] { Body(), frog },
            modes: null, skills: null, trees: null, bosses: new[] { boss });
    }

    private static EnemySpec Body() => new(new ContentId("enemy.gravemaw"), new LocKey("enemy.gravemaw.name"),
        3000f, 4f, 8, 40, 300f, false, Contact, 2.5f, 0.9f, 1.2f, 40f, EnemyBehaviourKind.Boss);

    private static CharacterSpec Character() => new(new ContentId("character.test"), new LocKey("character.test.name"),
        new LocKey("character.test.description"), 1_000f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
}
