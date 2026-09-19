using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// GD §9.1 rule 3 end to end: the crossings and the latch on the pure machine, then the beat, the
/// add-clear, the summoning and the orderings on a real agent in a real arena — and the stage that
/// holds one boss instead of waves.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two halves, and the split is the point of <see cref="BossPhases"/> existing at all.</b> The
/// first half drives the machine with nothing but a fraction and a delta, so every crossing rule is
/// provable without a world — <c>LevelTracker</c>'s and <c>LevelUpFlow</c>'s shape. The second half
/// drives a <see cref="BossBehaviour"/> through <c>EnemySystem.Tick</c> rather than calling it
/// directly, because the add-clear is queued to the end of that method and a row that called the
/// behaviour on its own would pass while the adds were still standing.
/// </para>
/// <para>
/// <b>The stage rows live here rather than in a third fixture</b>, which the spec's Files table does
/// not allow. They belong to <em>"the orderings"</em>: a boss stage is the director branch, and what
/// it is ordered against is the wave path it replaces.
/// </para>
/// <para>
/// <b>The adds are authored <c>Static</c> on purpose.</b> Nothing here is about what a Husk does —
/// the rows count bodies, registry membership and despawns — and a Chaser would add its own
/// telegraphs and its own damage to every assertion about what the boss published.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BossPhasesTests
{
    private const string BossId = "boss.warden";
    private const string WardenEnemyId = "enemy.warden";
    private const string HuskId = "enemy.husk";
    private const string OathboundId = "character.oathbound";

    private const float SecondPhase = 0.66f;
    private const float ThirdPhase = 0.33f;
    private const float Beat = 1.5f;

    /// <summary>A round number, so a row can drive a fraction by dividing.</summary>
    private const float BossMaxHp = 1000f;

    private const float Frame = 0.1f;
    private const int Capacity = 32;
    private const int ProjectileCapacity = 8;

    /// <summary>Where every row's spawn points sit, comfortably clear of a player at the origin.</summary>
    private const float Ring = 12f;

    private const float Tolerance = 1e-4f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private ProjectileSystem _projectiles;
    private WorldSnapshot _snapshot;
    private float _clock;

    /// <summary>
    /// The counter the allocation row drives its fraction from. A field rather than a local, so the
    /// measured closure is built over it once instead of capturing a fresh variable — a closure
    /// created inside the measurement would be the allocation it reported.
    /// </summary>
    private int _allocationStep;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _enemies = NewEnemies(Capacity);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _projectiles = new ProjectileSystem(_events, ProjectileCapacity);
        _snapshot = new WorldSnapshot(Capacity);
        _clock = 0f;
        _allocationStep = 0;
    }

    // ---- Rule 3: the crossings, on the pure machine -----------------------------------------------

    [Test]
    public void Phase_StartsAtZero()
    {
        var phases = new BossPhases(Warden());

        Assert.That(phases.Current, Is.EqualTo(0));
        Assert.That(phases.IsInBeat, Is.False);
        Assert.That(phases.PhaseCount, Is.EqualTo(3));
        Assert.That(phases.CurrentPhase.EntersBelow, Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void Phase_EntersAtTheThreshold()
    {
        var phases = new BossPhases(Warden());

        Assert.That(phases.Tick(0.67f, Frame), Is.False, "Above the threshold is still phase 0.");
        Assert.That(phases.Current, Is.EqualTo(0));

        // *At or under*, so the threshold itself is inside the new phase.
        Assert.That(phases.Tick(SecondPhase, Frame), Is.True);
        Assert.That(phases.Current, Is.EqualTo(1));

        // Exactly once: the beat is running now, and when it ends the fraction has not moved.
        RunOutTheBeat(phases, SecondPhase);

        Assert.That(phases.Tick(SecondPhase, Frame), Is.False);
        Assert.That(phases.Current, Is.EqualTo(1));
    }

    [Test]
    public void Phase_IsLatchedAgainstHealing()
    {
        var phases = new BossPhases(Warden());

        phases.Tick(SecondPhase, Frame);

        RunOutTheBeat(phases, SecondPhase);

        // Healed back over the threshold — and nothing happens. Hysteresis by construction rather
        // than by a tolerance: a boss oscillating across 66 % would clear the arena every few
        // seconds, because a crossing opens a beat and a beat clears the adds (rule 3).
        Assert.That(phases.Tick(0.9f, Frame), Is.False);
        Assert.That(phases.Current, Is.EqualTo(1));
        Assert.That(phases.IsInBeat, Is.False);

        // And it can still fall further afterwards.
        Assert.That(phases.Tick(ThirdPhase, Frame), Is.True);
        Assert.That(phases.Current, Is.EqualTo(2));
    }

    [Test]
    public void Phase_SkipsWhenOneHitCrossesTwo()
    {
        var phases = new BossPhases(Warden());

        Assert.That(phases.Tick(0.7f, Frame), Is.False);

        // 0.7 → 0.2 in one blow: it lands in the *last* phase it passed and reports **one**
        // crossing. Two beats back to back for one hit would be three seconds of a boss nobody can
        // touch (rule 3).
        Assert.That(phases.Tick(0.2f, Frame), Is.True);
        Assert.That(phases.Current, Is.EqualTo(2));

        RunOutTheBeat(phases, 0.2f);

        Assert.That(phases.Tick(0.2f, Frame), Is.False, "There is nowhere further to go.");
    }

    [Test]
    public void Beat_HoldsForTheAuthoredSeconds()
    {
        var phases = new BossPhases(Warden());

        // The crossing tick is the beat's first — which is where the add-clear happens (rule 4) —
        // so it is not run down, and 1.5 s at 0.1 is open for fifteen ticks and shut on the
        // sixteenth.
        Assert.That(phases.Tick(SecondPhase, Frame), Is.True);
        Assert.That(phases.IsInBeat, Is.True, "tick 1");
        Assert.That(phases.BeatRemaining, Is.EqualTo(Beat).Within(Tolerance));

        for (int tick = 2; tick <= 15; tick++)
        {
            phases.Tick(SecondPhase, Frame);

            Assert.That(phases.IsInBeat, Is.True, $"tick {tick}");
        }

        phases.Tick(SecondPhase, Frame);

        Assert.That(phases.IsInBeat, Is.False, "tick 16");
        Assert.That(phases.BeatRemaining, Is.EqualTo(0f));
    }

    [Test]
    public void Phase_ANonFiniteFractionCrossesNothing()
    {
        var phases = new BossPhases(Warden());

        // Every comparison against NaN is false, so the walk falls through to phase 0 — which leaves
        // the fight where it was rather than skipping it to the end (AR §18.3).
        Assert.That(phases.Tick(float.NaN, Frame), Is.False);
        Assert.That(phases.Current, Is.EqualTo(0));
    }

    [Test]
    public void Phase_ResetGoesBackToTheOpeningPhase()
    {
        var phases = new BossPhases(Warden());

        phases.Tick(ThirdPhase, Frame);

        Assert.That(phases.Current, Is.EqualTo(2));
        Assert.That(phases.IsInBeat, Is.True);

        phases.Reset();

        Assert.That(phases.Current, Is.EqualTo(0));
        Assert.That(phases.IsInBeat, Is.False);
    }

    [Test]
    public void Phases_RefuseABossThatIsNotThere()
    {
        Assert.Throws<ArgumentNullException>(() => new BossPhases(null));
    }

    [Test]
    public void Phase_TickAllocatesNothing()
    {
        var phases = new BossPhases(Warden());

        // Warmed outside the measurement, so the jit is not what is being counted.
        for (int i = 0; i < 600; i++)
        {
            phases.Tick(1f - (i % 100 * 0.01f), Frame);
            phases.Reset();
        }

        _allocationStep = 0;

        AllocationAssert.None(() =>
        {
            _allocationStep++;

            // Sweeps the whole range, so crossings, latched ticks and beat ticks are all inside the
            // measurement rather than only the cheap path. Reset is measured with them: it is what a
            // recycled agent's behaviour runs, and it is on this class's per-fight path.
            phases.Tick(1f - (_allocationStep % 100 * 0.01f), Frame);

            if (_allocationStep % 100 == 0)
            {
                phases.Reset();
            }
        });
    }

    // ---- Rule 4: the beat, on a real agent --------------------------------------------------------

    [Test]
    public void Phase_IsAnnouncedWhenTheBossStandsUp()
    {
        EnemyAgent boss = SpawnBoss();

        // **Rule 7, from the inside.** EnemySpawned gained no IsBoss flag; what a view needs is the
        // phase count, and it arrives at the moment the body does rather than at 66 %.
        BossPhaseChanged announced = _events.Single<BossPhaseChanged>();

        Assert.That(announced.EnemyId, Is.EqualTo(boss.Id));
        Assert.That(announced.Phase, Is.EqualTo(0));
        Assert.That(announced.OfPhases, Is.EqualTo(3));

        // **And the thresholds themselves, which is M4-04's addition and the other half of rule 7.**
        // A count alone tells a segmented bar how many marks to draw and not where any of them goes,
        // and M4-04 rule 4 puts them at the phases' own values rather than at even spacing — so the
        // fight has to say which values. This is the only row in the project that asserts the wire
        // carries the *asset's* numbers rather than a view's idea of them.
        Assert.That(announced.EntersBelow, Is.Not.Null, "The fight announced a phase count and no thresholds.");
        Assert.That(
            announced.EntersBelow,
            Is.EqualTo(new[] { BossSpec.FirstPhaseEntersBelow, SecondPhase, ThirdPhase }),
            "The announced thresholds are not the ones the spec authored, so a boss bar would draw "
                + "its seams somewhere the fight does not change phase.");

        Assert.That(_events.Count<BossBeatStarted>(), Is.Zero, "Phase 0 opens no beat.");
    }

    [Test]
    public void Beat_MakesTheBossInvulnerable()
    {
        EnemyAgent boss = SpawnBoss();

        CrossInto(boss, SecondPhase);

        Assert.That(boss.IsVulnerable, Is.False, "Auto-aim must not point at what it cannot hurt.");

        float before = boss.Health.Current;

        DamageResult result = _enemies.ApplyDamage(boss.Id, 100f, _clock, _player);

        Assert.That(result.Blocked, Is.True, "Something arrived and was turned away.");
        Assert.That(boss.Health.Current, Is.EqualTo(before).Within(Tolerance));

        // And it comes back on its own, with an event to say so.
        RunOutTheBeat(boss);

        Assert.That(boss.IsVulnerable, Is.True);
        Assert.That(_events.Single<BossBeatEnded>().EnemyId, Is.EqualTo(boss.Id));

        Assert.That(_enemies.ApplyDamage(boss.Id, 100f, _clock, _player).Applied, Is.EqualTo(100f).Within(Tolerance));
    }

    [Test]
    public void Beat_ClearsAddsOnItsFirstTick()
    {
        EnemyAgent boss = SpawnBoss();

        // Phase 1 calls in four; phase 2 calls in none, so what the second crossing does to them is
        // the only thing left to measure.
        CrossInto(boss, SecondPhase);

        Assert.That(LivingHusks(), Is.EqualTo(4));

        RunOutTheBeat(boss);

        int beatsBefore = _events.Count<BossBeatStarted>();

        CrossInto(boss, ThirdPhase);

        // On that tick, not at the beat's end: the point of the beat is to reset pressure *now*
        // (rule 4, GD §9.1 rule 3).
        Assert.That(_events.Count<BossBeatStarted>(), Is.EqualTo(beatsBefore + 1));
        Assert.That(LivingHusks(), Is.Zero, "All four are gone on the tick the phase changed.");
        Assert.That(_events.Count<EnemyDespawned>(), Is.EqualTo(4));
    }

    [Test]
    public void Beat_BossDoesNotActDuringIt()
    {
        var inner = new CountingBehaviour();

        EnemyAgent boss = SpawnBoss(inner);

        Tick();

        Assert.That(inner.Ticks, Is.EqualTo(1), "Outside a beat it is the inner behaviour's tick.");

        Hurt(boss, SecondPhase);

        Tick();

        Assert.That(boss.Behaviour, Is.InstanceOf<BossBehaviour>());
        Assert.That(((BossBehaviour)boss.Behaviour).IsInBeat, Is.True);
        Assert.That(inner.Ticks, Is.EqualTo(1), "The crossing tick is the boss's own.");

        for (int i = 0; i < 14; i++)
        {
            Tick();
        }

        Assert.That(inner.Ticks, Is.EqualTo(1), "Fifteen ticks of beat, and it has not acted once.");

        // And the tick the beat shuts, it is acting again.
        Tick();

        Assert.That(inner.Ticks, Is.EqualTo(2));
    }

    [Test]
    public void Beat_StillPinsTheBodyToTheFloor()
    {
        EnemyAgent boss = SpawnBoss();

        CrossInto(boss, SecondPhase);

        _intents.Clear();

        Tick();

        // Exactly one EnemyMoveIntent per call, in every state including the standing ones — the
        // body's rule rather than any one archetype's (IEnemyBehaviour.Tick). Without it, a boss
        // mid-beat is a boss gravity is not being applied to.
        Assert.That(_intents.CountEnemyMoves(boss.Id), Is.EqualTo(1));
        Assert.That(_intents.LastEnemyMove.Velocity, Is.EqualTo(Vector3.Zero));
    }

    // ---- Rule 5: the summons ----------------------------------------------------------------------

    [Test]
    public void Summon_GoesThroughTheSpawnPath()
    {
        EnemyAgent boss = SpawnBoss(phaseOneSummons: 3);

        CrossInto(boss, SecondPhase);

        var husks = new List<EnemyAgent>();

        ReadOnlySpan<EnemyAgent> alive = _enemies.Registry.Alive;

        for (int i = 0; i < alive.Length; i++)
        {
            if (alive[i].Spec.Id == Id(HuskId))
            {
                husks.Add(alive[i]);
            }
        }

        Assert.That(husks.Count, Is.EqualTo(3));

        for (int i = 0; i < husks.Count; i++)
        {
            EnemyAgent husk = husks[i];

            // Ordinary in every way that matters: the same registry, the same announcement, the same
            // depth scaling, the same experience (rule 5). Depth 1 multiplies by exactly 1, so the
            // hit points are the archetype's — what is asserted is that it came through Spawn, which
            // the announcement is the evidence for.
            Assert.That(_enemies.Registry.TryGet(husk.Id, out _), Is.True);
            Assert.That(husk.Spec.XpValue, Is.EqualTo(12f).Within(Tolerance));
            Assert.That(husk.Health.MaxHp.Value, Is.EqualTo(36f).Within(Tolerance));
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(4), "The boss, and three ordinary Husks.");
    }

    [Test]
    public void Summon_ClampsToTheConcurrencyCap()
    {
        // Six slots: the boss, three fillers, and two left over for a phase that wants six.
        _enemies = NewEnemies(capacity: 6);

        EnemyAgent boss = SpawnBoss(phaseOneSummons: 6);

        for (int i = 0; i < 3; i++)
        {
            _enemies.Spawn(Id(HuskId), new Vector3(20f + i, 0f, 0f));
        }

        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(4));

        CrossInto(boss, SecondPhase);

        // Two fit, four are dropped, and nothing threw.
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(6));
        Assert.That(((BossBehaviour)boss.Behaviour).AddCount, Is.EqualTo(2));

        // **And they are not queued** (rule 5). Room appears, and the four that did not fit stay
        // not-fitted — a boss that banked summons and released them later is the frame-rate bug the
        // clamp exists to prevent.
        ReadOnlySpan<EnemyAgent> alive = _enemies.Registry.Alive;

        for (int i = 0; i < alive.Length; i++)
        {
            if (alive[i].Spec.Id == Id(HuskId) && alive[i].Position.X >= 20f)
            {
                _enemies.Despawn(alive[i].Id);

                break;
            }
        }

        RunOutTheBeat(boss);

        Tick();
        Tick();

        Assert.That(((BossBehaviour)boss.Behaviour).AddCount, Is.EqualTo(2), "Still two, with room for a third.");
    }

    // ---- Rule 9, and the corpse M4-01a pinned -----------------------------------------------------

    [Test]
    public void Order_CrossingIsSeenTheFrameTheHitLands()
    {
        EnemyAgent boss = SpawnBoss();

        _events.Clear();

        // Combat runs above the behaviour step in RunSession.Tick, so this is the order a real frame
        // has: the hit lands, then the enemies act. The beat must be open in *this* frame's events.
        _enemies.ApplyDamage(boss.Id, BossMaxHp * (1f - ThirdPhase), _clock, _player);

        Assert.That(_events.Count<BossBeatStarted>(), Is.Zero, "Nothing has ticked yet.");

        Tick();

        Assert.That(_events.Count<BossPhaseChanged>(), Is.EqualTo(1));
        Assert.That(_events.Single<BossBeatStarted>().Seconds, Is.EqualTo(Beat).Within(Tolerance));

        // The phase before the beat, so a handler that sizes itself from the phase count has done so
        // before it is told how long it has to animate the change.
        Assert.That(IndexOf<BossPhaseChanged>(), Is.LessThan(IndexOf<BossBeatStarted>()));
    }

    [Test]
    public void Boss_ACorpseIsNotAHealthyBoss()
    {
        // **M4-01a's finding, made into the row that would catch trusting it.**
        // EnemySystem.Perceive skips the dead, so a corpse's EnemyBlackboard.HpFraction is frozen at
        // the last reading it was perceived with — a boss killed from full health reads as a boss at
        // full health for ever. Anything that decided a phase, or the end of a fight, by looking at
        // that fraction would never notice the fight had finished.
        SpawnDirector director = Director();
        ModeSpec mode = Mode(bossEvery: 5);

        mode.TryGetBossFor(5, out ContentId bossId);

        director.Begin(Plan(), Points(), _clock, bossId);

        director.Tick(_clock, Vector3.Zero, Stream());

        EnemyAgent boss = _enemies.Registry.Alive[0];

        Sense();

        Assert.That(boss.Blackboard.HpFraction, Is.EqualTo(1f).Within(Tolerance));

        _enemies.ApplyDamage(boss.Id, BossMaxHp * 2f, _clock, _player);

        Sense();

        Assert.That(boss.IsAlive, Is.False);
        Assert.That(boss.Health.Fraction, Is.Zero, "Health is honest about it.");
        Assert.That(
            boss.Blackboard.HpFraction,
            Is.EqualTo(1f).Within(Tolerance),
            "And the blackboard is not: this is the last *living* reading, kept.");

        // The machine was never driven on the corpse, so a boss killed outright from full health
        // announces no phase at all beyond the one it stood up in.
        Tick();

        Assert.That(_events.Count<BossPhaseChanged>(), Is.EqualTo(1));
        Assert.That(_events.Count<BossBeatStarted>(), Is.Zero);

        // And the stage is over, because the director asks IsAlive rather than the fraction.
        director.Tick(_clock, Vector3.Zero, Stream());

        Assert.That(director.IsStageComplete, Is.True);
        Assert.That(_events.Count<WaveCleared>(), Is.EqualTo(1));
    }

    // ---- Rule 1: a stage holds a boss instead of waves ---------------------------------------------

    [Test]
    public void Boss_SpawnsOnAnAuthoredStage()
    {
        ModeSpec mode = Mode(bossEvery: 5);

        foreach (int stage in new[] { 4, 5, 6 })
        {
            SetUp();

            SpawnDirector director = Director();

            bool isBossStage = mode.TryGetBossFor(stage, out ContentId bossId);

            director.Begin(Plan(), Points(), _clock, bossId);

            director.Tick(_clock, Vector3.Zero, Stream());

            Assert.That(director.IsBossStage, Is.EqualTo(isBossStage), $"stage {stage}");

            if (isBossStage)
            {
                // One body, and it is the boss's: no telegraph, no wave, and the phase count on the
                // wire the moment it appears.
                Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(1));
                Assert.That(_enemies.Registry.Alive[0].Spec.Id, Is.EqualTo(Id(WardenEnemyId)));
                Assert.That(_events.Count<SpawnTelegraphed>(), Is.Zero);
                Assert.That(_events.Count<BossPhaseChanged>(), Is.EqualTo(1));
                Assert.That(_events.Single<WaveStarted>().WaveCount, Is.EqualTo(1));
            }
            else
            {
                // A wave, announced with a ring, and no boss anywhere.
                Assert.That(_events.Count<BossPhaseChanged>(), Is.Zero);
                Assert.That(_events.Count<SpawnTelegraphed>(), Is.EqualTo(1));
                Assert.That(_enemies.Registry.AliveCount, Is.Zero, "A telegraphed body is not standing yet.");
            }
        }
    }

    [Test]
    public void Boss_TheStageIsOverWhenTheBossIsDown()
    {
        SpawnDirector director = Director();

        director.Begin(Plan(), Points(), _clock, Id(BossId));

        director.Tick(_clock, Vector3.Zero, Stream());

        EnemyAgent boss = _enemies.Registry.Alive[0];

        Assert.That(director.BossEnemyId, Is.EqualTo(boss.Id));
        Assert.That(director.IsStageComplete, Is.False);

        // Half its health is not the end of the fight, whatever phase it is in.
        _enemies.ApplyDamage(boss.Id, BossMaxHp * 0.5f, _clock, _player);

        director.Tick(_clock, Vector3.Zero, Stream());

        Assert.That(director.IsStageComplete, Is.False);

        _enemies.ApplyDamage(boss.Id, BossMaxHp, _clock, _player);

        director.Tick(_clock, Vector3.Zero, Stream());

        Assert.That(director.IsStageComplete, Is.True);
        Assert.That(_events.Count<WaveCleared>(), Is.EqualTo(1), "Announced once, not once a tick.");

        director.Tick(_clock, Vector3.Zero, Stream());

        Assert.That(_events.Count<WaveCleared>(), Is.EqualTo(1));
    }

    [Test]
    public void Boss_ABossStageOwesThePlayerTheSameClearance()
    {
        SpawnDirector director = Director();

        // Standing on the only spawn point there is: nothing appears, and nothing is said about it.
        // GD §12.4's rule is that a spawn on top of you is worse than a pause — the same bargain a
        // wave body gets, which is why the boss stands up on a tick rather than in Begin.
        var onTop = new Vector3(Ring, 0f, 0f);

        director.Begin(Plan(), new[] { onTop }, _clock, Id(BossId));

        director.Tick(_clock, onTop, Stream());

        Assert.That(_enemies.Registry.AliveCount, Is.Zero);
        Assert.That(director.IsStageComplete, Is.False);

        // The player moves, and it arrives.
        director.Tick(_clock, Vector3.Zero, Stream());

        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(1));
    }

    [Test]
    public void Boss_AnAgentAuthoredBossWithNoBehaviourIsLoud()
    {
        // The one kind EnemyAgent.Initialise deliberately leaves without a behaviour, because
        // building one needs the BossSpec an EnemySpec does not carry. Spawned through the ordinary
        // door it would otherwise stand in the arena for the whole fight with nothing in the log.
        _enemies.Spawn(Id(WardenEnemyId), new Vector3(0f, 0f, 5f));

        Assert.Throws<InvalidOperationException>(() => _enemies.Tick(Context()));
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    /// <summary>
    /// An <see cref="IEnemyBehaviour"/> that counts what it was asked to do and does the one thing
    /// the seam requires — so a row can tell "the boss did not act" from "nothing was wired".
    /// </summary>
    private sealed class CountingBehaviour : IEnemyBehaviour
    {
        public int Ticks { get; private set; }

        public int Resets { get; private set; }

        public void Tick(in EnemyTickContext ctx)
        {
            Ticks++;

            ctx.Intents.EnemyMove(new EnemyMoveIntent(1, Vector3.Zero, Vector2.Zero));
        }

        public void Reset()
        {
            Resets++;

            Ticks = 0;
        }
    }

    /// <summary>
    /// The boss, standing up through the door a real stage uses — its middle phase summoning
    /// <paramref name="phaseOneSummons"/> Husks and its last phase summoning none.
    /// </summary>
    /// <remarks>
    /// The census is rebuilt rather than the boss retuned, because a <see cref="BossSpec"/> is
    /// content and content is read out of the catalog. Rebuilt <em>before</em> the spawn, and the
    /// order matters: a receiver is evaluated before its arguments, so a helper that replaced the
    /// field from inside the call would have spawned into the system it was replacing.
    /// </remarks>
    private EnemyAgent SpawnBoss(IEnemyBehaviour inner = null, int phaseOneSummons = 4)
    {
        _enemies = NewEnemies(_enemies.Registry.Capacity, phaseOneSummons);

        return _enemies.SpawnBoss(Id(BossId), new Vector3(0f, 0f, 5f), inner);
    }

    /// <summary>Drives the boss's health to <paramref name="fraction"/> and ticks once.</summary>
    private void CrossInto(EnemyAgent boss, float fraction)
    {
        Hurt(boss, fraction);

        Tick();
    }

    /// <summary>Takes the boss down to exactly <paramref name="fraction"/> of its maximum.</summary>
    private void Hurt(EnemyAgent boss, float fraction)
    {
        float target = boss.Health.MaxHp.Value * fraction;

        _enemies.ApplyDamage(boss.Id, boss.Health.Current - target, _clock, _player);
    }

    /// <summary>Ticks the whole census until the boss's beat has shut.</summary>
    /// <remarks>
    /// Through <c>EnemySystem.Tick</c> rather than the behaviour, because the add-clear is queued to
    /// the end of that method — see the class remarks.
    /// </remarks>
    private void RunOutTheBeat(EnemyAgent boss)
    {
        var behaviour = (BossBehaviour)boss.Behaviour;

        for (int i = 0; i < 64 && behaviour.IsInBeat; i++)
        {
            Tick();
        }

        Assert.That(behaviour.IsInBeat, Is.False, "The beat should have shut by now.");
    }

    /// <summary>The same, on the pure machine.</summary>
    private static void RunOutTheBeat(BossPhases phases, float fraction)
    {
        for (int i = 0; i < 64 && phases.IsInBeat; i++)
        {
            phases.Tick(fraction, Frame);
        }

        Assert.That(phases.IsInBeat, Is.False);
    }

    /// <summary>One frame of the census, advancing the fixture's clock with it.</summary>
    private void Tick()
    {
        _enemies.Tick(Context());

        _clock += Frame;
    }

    private EnemyTickContext Context() => new EnemyTickContext(
        Frame, _clock, _player, _intents, _events, _projectiles, _enemies);

    /// <summary>One frame of perception, so the blackboards are filled the way a run fills them.</summary>
    private void Sense()
    {
        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = Vector3.Zero;

        ReadOnlySpan<EnemyAgent> agents = _enemies.Registry.Alive;

        for (int i = 0; i < agents.Length; i++)
        {
            ref EnemySense sense = ref _snapshot.AddEnemy();

            sense.Id = agents[i].Id;
            sense.Position = agents[i].Position;
            sense.Velocity = Vector3.Zero;
            sense.HasLineOfSight = true;
        }

        _enemies.Ingest(_snapshot);
    }

    private int LivingHusks()
    {
        ReadOnlySpan<EnemyAgent> agents = _enemies.Registry.Alive;

        int count = 0;

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i].IsAlive && agents[i].Spec.Id == Id(HuskId))
            {
                count++;
            }
        }

        return count;
    }

    private int IndexOf<T>()
        where T : struct
    {
        for (int i = 0; i < _events.All.Count; i++)
        {
            if (_events.All[i] is T)
            {
                return i;
            }
        }

        return -1;
    }

    private SpawnDirector Director() => new SpawnDirector(_enemies, _events);

    /// <summary>A one-wave stage of one Husk, so a non-boss stage has something to telegraph.</summary>
    private WavePlan Plan()
    {
        var mode = Mode(bossEvery: 5);
        var plan = new WavePlan(4, 1);

        new WaveComposer(Catalog(), new ThreatBudget(mode.Scaling, Capacity))
            .Compose(1, mode, plan, Stream());

        return plan;
    }

    private static IRandomStream Stream() => new FixedRandom(0.1f).Spawn;

    private static IReadOnlyList<Vector3> Points()
    {
        var points = new Vector3[4];

        for (int i = 0; i < points.Length; i++)
        {
            double angle = 2d * Math.PI * i / points.Length;

            points[i] = new Vector3((float)(Ring * Math.Cos(angle)), 0f, (float)(Ring * Math.Sin(angle)));
        }

        return points;
    }

    private EnemySystem NewEnemies(int capacity, int summons = 4) => new EnemySystem(
        Catalog(summons),
        _events,
        new FixedRandom(0.1f),
        new DepthScaling(Scalings.Design()),
        capacity);

    private static ContentCatalog Catalog(int summons = 4) => new ContentCatalog(
        new[] { Oathbound() },
        new[] { Husk(), WardenBody() },
        new[] { Mode(bossEvery: 5) },
        skills: null,
        trees: null,
        bosses: new[] { Warden(summons) });

    private static ContentId Id(string value) => new ContentId(value);

    private static BossSpec Warden(int summons = 4) => new BossSpec(
        Id(BossId),
        Id(WardenEnemyId),
        new[]
        {
            new BossPhaseSpec(1f),
            new BossPhaseSpec(SecondPhase, new[] { new AddWave(Id(HuskId), summons) }),
            new BossPhaseSpec(ThirdPhase),
        },
        Beat);

    private static ModeSpec Mode(int bossEvery)
    {
        // A flat budget of one Husk a wave and a single wave, so an ordinary stage has exactly one
        // body to telegraph — SpawnDirectorTests' shape, which spells "always this many" by making
        // the curve's step larger than any stage these rows reach.
        var scaling = new ScalingSpec(
            new BudgetCurve(4f, 0f, 0f),
            new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(8, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0));

        return new ModeSpec(
            Id("mode.test"),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            scaling,
            Scalings.Xp(),
            new[] { new RosterEntry(Id(HuskId), 1) },
            arenas: null,
            bossRoster: new[] { new BossRosterEntry(Id(BossId), bossEvery) });
    }

    private static EnemySpec Husk() => new EnemySpec(
        Id(HuskId),
        new LocKey("enemy.husk.name"),
        36f,
        2f,
        1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    /// <summary>The body a boss wears: an ordinary archetype, authored <c>Boss</c> (rule 2).</summary>
    private static EnemySpec WardenBody() => new EnemySpec(
        Id(WardenEnemyId),
        new LocKey("enemy.warden.name"),
        BossMaxHp,
        2f,
        8,
        threatCost: 40,
        xpValue: 120f,
        isElite: false,
        20f,
        2.5f,
        0.8f,
        0.8f,
        aggroRange: 40f,
        EnemyBehaviourKind.Boss);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        Id(OathboundId),
        new LocKey("character.oathbound.name"),
        140f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);
}
