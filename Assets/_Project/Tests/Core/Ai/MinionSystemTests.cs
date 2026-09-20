using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Ai;

/// <summary>
/// CH §3.2's Wight, from the side that is not Rise: what one is, how many may stand, how long,
/// what it walks at, what it hits — and, the row this fixture exists for as much as any other,
/// what it is <em>not</em>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A Wight is spawned where the row wants it and never moved</b>, which is not a shortcut but
/// the architecture: core assigns no position (AR §3, ADR-0003). A <c>MinionAgent.Position</c> only
/// ever changes through <c>MinionSystem.Ingest</c>, so a Wight that walks in these rows walks in
/// intents and stays exactly where it stood up. <c>Minion_IngestsItsReportedPosition</c> is the one
/// row that proves the other half.
/// </para>
/// <para>
/// <b><c>Minion_IsNotAnEnemy</c> is asserted against three real systems rather than by type</b>
/// (M5-04a rule 1). A row that checked <c>is not EnemyAgent</c> would pass on the day somebody made
/// a Wight an enemy and taught six systems to skip it; what the rule is actually about is that the
/// director's door, the player's targeter and the experience ledger each cannot see one, so those
/// are the three things asked.
/// </para>
/// <para>
/// <b>The ordering row the spec's Tests table asks for is not here, and that is a finding.</b>
/// <c>Run_MinionsTickAfterTheEnemiesAndAboveTheDeathCheck</c> needs a Wight standing inside a live
/// <c>RunSession</c>; nothing produces one until M5-04b's Rise, <c>RunState.Minions</c> is
/// <c>internal</c> because AR §18.2 forbids handing a live object out, and
/// <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c> — so there is no door a test can
/// raise one through. <see cref="Run_AGravecallerRunTicksItsArmyAndAnOathboundHasNone"/> is what the
/// boundary does allow; the kill-on-the-death-tick half is M5-04b rule 4's, which owns the same
/// ordering and will have a producer.
/// </para>
/// </remarks>
[TestFixture]
public sealed class MinionSystemTests
{
    private const string WightId = "minion.wight";
    private const string HuskId = "enemy.husk";

    /// <summary>A dummy with more hit points than any row can spend, for the allocation sweep.</summary>
    private const string AnvilId = "enemy.anvil";

    private const string OathboundId = "character.oathbound";
    private const string GravecallerId = "character.gravecaller";
    private const string DescentId = "mode.descent";

    // The Gravecaller's authored minion block, from Gravecaller.asset (M5-02). Written out rather
    // than read, because these rows are what pins the asset's numbers to a behaviour.
    private const int Cap = 3;
    private const float Lifespan = 20f;
    private const float MinionMaxHp = 20f;
    private const float MinionSpeed = 3f;
    private const float MinionDamage = 8f;
    private const float AttackInterval = 1f;
    private const float Reach = 1.5f;

    private const float HuskMaxHp = 36f;
    private const float HuskXp = 12f;

    private const int Capacity = 32;
    private const int ProjectileCapacity = 8;
    private const int Seed = 99;

    private const float Frame = 0.1f;
    private const float Tolerance = 1e-4f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private PlayerCombat _player;
    private MinionSystem _minions;
    private WorldSnapshot _snapshot;

    /// <summary>
    /// The counter the allocation rows drive from. A field rather than a local, so the measured
    /// closure is built over it once instead of capturing a fresh variable.
    /// </summary>
    private int _allocationStep;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _enemies = NewEnemies(_events);
        _player = new PlayerCombat(Oathbound(), _events, _intents, Capacity);
        _minions = new MinionSystem(Wight(), _events, _intents);
        _snapshot = new WorldSnapshot(Capacity);
        _allocationStep = 0;
    }

    // ---- Rules 2 and 3: what stands up, and how many ----------------------------------------------

    [Test]
    public void Minion_StandsUpWithItsAuthoredNumbers()
    {
        var at = new Vector3(1f, 0f, 2f);

        MinionAgent wight = _minions.Spawn(at, now: 5f);

        Assert.That(wight, Is.Not.Null, "A first Wight under the cap is never refused.");

        // The three Stats an EnemyAgent carries and no fourth (rule 2) — the shape M5-04b's
        // MinionStats mirrors rather than invents.
        Assert.That(wight.MaxHp.Value, Is.EqualTo(MinionMaxHp).Within(Tolerance));
        Assert.That(wight.Health.Current, Is.EqualTo(MinionMaxHp).Within(Tolerance));
        Assert.That(wight.MoveSpeed.Value, Is.EqualTo(MinionSpeed).Within(Tolerance));
        Assert.That(wight.ContactDamage.Value, Is.EqualTo(MinionDamage).Within(Tolerance));

        Assert.That(wight.Position, Is.EqualTo(at));
        Assert.That(wight.ExpiresAt, Is.EqualTo(25f).Within(Tolerance), "CH §3.2's twenty seconds, from now.");
        Assert.That(wight.QuarryId, Is.Zero, "It has not looked at anything yet.");
        Assert.That(wight.IsAlive, Is.True);
        Assert.That(_minions.Count, Is.EqualTo(1));

        MinionSpawned spawned = _events.Single<MinionSpawned>();

        Assert.That(spawned.Id, Is.EqualTo(wight.Id));
        Assert.That(spawned.SpecId, Is.EqualTo(Id(WightId)));
        Assert.That(spawned.Position, Is.EqualTo(at));

        // The lifespan rides the event for EnemyTelegraph.Duration's reason: M5-05a's view runs its
        // own dissolve out without holding a catalog.
        Assert.That(spawned.Lifespan, Is.EqualTo(Lifespan).Within(Tolerance));
    }

    [Test]
    public void Minion_RefusesAboveTheCap()
    {
        for (int i = 0; i < Cap; i++)
        {
            Assert.That(_minions.Spawn(Vector3.Zero, now: 0f), Is.Not.Null, $"raise {i + 1}");
        }

        // Silently, and the two standing are kept rather than the newest (rule 3). A throw here
        // would be a routine event once Rise draws against a full army four times a second.
        Assert.That(_minions.Spawn(Vector3.Zero, now: 0f), Is.Null);
        Assert.That(_minions.Count, Is.EqualTo(Cap));
        Assert.That(_events.Count<MinionSpawned>(), Is.EqualTo(Cap), "A refused raise announces nothing.");
    }

    [Test]
    public void Minion_TheCapIsAStat()
    {
        // Where CH §3.2's The Host lands — a Flat modifier on a live Stat, not a second mechanism
        // (rule 3, ADR-0008).
        _minions.Cap.Add(new Modifier(ModifierKind.Flat, 4f, this));

        for (int i = 0; i < 7; i++)
        {
            Assert.That(_minions.Spawn(Vector3.Zero, now: 0f), Is.Not.Null, $"raise {i + 1}");
        }

        Assert.That(_minions.Count, Is.EqualTo(7));

        // Seven is what 3 + 4 buys, so the eighth is refused by the cap it just reached. The
        // ceiling is the next row's — what this one proves is that the node moved the number at all.
        Assert.That(_minions.Spawn(Vector3.Zero, now: 0f), Is.Null);
        Assert.That(_minions.Count, Is.EqualTo(7));
    }

    [Test]
    public void Minion_RefusesAboveMaxConcurrentWhateverTheCapSays()
    {
        // A Stat clamps nothing (ADR-0008), so a stack can say forty-three. The array says eight,
        // and the refusal is the same silent one (rule 3) rather than a write past the end.
        _minions.Cap.Add(new Modifier(ModifierKind.Flat, 100f, this));

        for (int i = 0; i < 9; i++)
        {
            Assert.DoesNotThrow(() => _minions.Spawn(Vector3.Zero, now: 0f));
        }

        Assert.That(_minions.Count, Is.EqualTo(MinionSystem.MaxConcurrent));
        Assert.That(_events.Count<MinionSpawned>(), Is.EqualTo(MinionSystem.MaxConcurrent));
    }

    // ---- Rules 7 and 10: the clock ----------------------------------------------------------------

    [Test]
    public void Minion_ExpiresOnTime()
    {
        MinionAgent wight = _minions.Spawn(Vector3.Zero, now: 0f);

        Tick(now: 19.9f);

        Assert.That(_minions.Count, Is.EqualTo(1), "A Wight with a tenth of a second left is standing.");

        Tick(now: 20f);

        Assert.That(_minions.Count, Is.Zero);
        Assert.That(_minions.TryGet(wight.Id, out MinionAgent _), Is.False, "The id has stopped resolving.");

        Assert.That(_events.Single<MinionDespawned>().Id, Is.EqualTo(wight.Id));

        // The half of rule 10 that matters: a clock running out is not a death, so M5-06's Second
        // Death keystone cannot fire on a Wight that simply timed out.
        Assert.That(_events.Count<MinionDied>(), Is.Zero);
    }

    [Test]
    public void Minion_IsRecycledNotReallocated()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var system = new MinionSystem(Wight(), silent, intents);
        var enemies = NewEnemies(silent);
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);

        float now = 0f;

        void Cycle()
        {
            intents.Clear();

            // Past every standing Wight's moment, so the expiry pass empties the army — then it is
            // filled back to the cap out of the same bodies. Rise puts this on the kill path
            // (M5-04b), so a `new` here would be a GC spike behind every fourth kill.
            now += Lifespan + 1f;

            system.Tick(Frame, now, enemies, player);

            for (int i = 0; i < Cap; i++)
            {
                system.Spawn(Vector3.Zero, now);
            }
        }

        for (int i = 0; i < 100; i++)
        {
            Cycle();
        }

        Assert.That(system.Count, Is.EqualTo(Cap), "Sanity: the cycle refills the army.");

        AllocationAssert.None(Cycle, iterations: 1_000);
    }

    // ---- Rule 4: a body reports where it is -------------------------------------------------------

    [Test]
    public void Minion_IngestsItsReportedPosition()
    {
        MinionAgent wight = _minions.Spawn(Vector3.Zero, now: 0f);

        var reported = new Vector3(4f, 0f, 4f);
        var moving = new Vector3(0f, 0f, 2f);

        _snapshot.Clear();

        ref EnemySense sense = ref _snapshot.AddMinion();

        sense.Id = wight.Id;
        sense.Position = reported;
        sense.Velocity = moving;

        // Filled and deliberately unread (rule 4): a NavMesh path is a route to the *player*, which
        // is not where a Wight is going, and line of sight is a question nothing friendly asks.
        sense.PathDirectionToPlayer = new Vector2(1f, 0f);
        sense.HasLineOfSight = true;

        _minions.Ingest(_snapshot);

        Assert.That(wight.Position, Is.EqualTo(reported), "Unity reports where the body got to (AR §3).");
        Assert.That(wight.Velocity, Is.EqualTo(moving));

        // An id the system does not know is ignored rather than refused — a view is allowed to lag
        // a frame behind a despawn, which is EnemySystem.Ingest's bargain.
        _snapshot.Clear();

        ref EnemySense stale = ref _snapshot.AddMinion();

        stale.Id = wight.Id + 500;
        stale.Position = new Vector3(99f, 0f, 99f);
        stale.Velocity = Vector3.Zero;
        stale.PathDirectionToPlayer = Vector2.Zero;
        stale.HasLineOfSight = false;

        Assert.DoesNotThrow(() => _minions.Ingest(_snapshot));
        Assert.That(wight.Position, Is.EqualTo(reported), "Nobody else's report moved it.");
    }

    // ---- Rule 6: what it walks at -----------------------------------------------------------------

    [Test]
    public void Minion_WalksAtTheNearestLivingEnemy()
    {
        SpawnHusk(new Vector3(12f, 0f, 0f));

        EnemyAgent nearest = SpawnHusk(new Vector3(5f, 0f, 0f));

        SpawnHusk(new Vector3(0f, 0f, 8f));

        MinionAgent wight = _minions.Spawn(Vector3.Zero, now: 0f);

        _intents.Clear();

        Tick(now: Frame);

        Assert.That(wight.QuarryId, Is.EqualTo(nearest.Id));

        Assert.That(_intents.MinionMoves.Count, Is.EqualTo(1), "One walk per standing Wight per tick.");

        EnemyMoveIntent walk = _intents.LastMinionMove;

        Assert.That(walk.Id, Is.EqualTo(wight.Id));

        // Straight at it, at the agent's live MoveSpeed — a Wight has no NavMesh path of its own
        // (rule 4), which is written down rather than discovered.
        Assert.That(walk.Velocity.X, Is.EqualTo(MinionSpeed).Within(Tolerance));
        Assert.That(walk.Velocity.Y, Is.EqualTo(0f).Within(Tolerance), "Gravity is the body's to apply.");
        Assert.That(walk.Velocity.Z, Is.EqualTo(0f).Within(Tolerance));
        Assert.That(walk.FacingXZ.X, Is.EqualTo(1f).Within(Tolerance));
    }

    [Test]
    public void Minion_IgnoresACorpse()
    {
        EnemyAgent near = SpawnHusk(new Vector3(4f, 0f, 0f));
        EnemyAgent far = SpawnHusk(new Vector3(9f, 0f, 0f));

        // Killed and left registered: EnemyRegistry.Alive holds a corpse until EnemySystem's sweep
        // retires it, so "nearest" has to mean nearest *living* rather than nearest entry.
        _enemies.ApplyDamage(near.Id, HuskMaxHp, now: 0f, _player);

        Assert.That(near.IsAlive, Is.False);
        Assert.That(_enemies.Registry.AliveCount, Is.EqualTo(2), "The corpse is still registered.");

        MinionAgent wight = _minions.Spawn(Vector3.Zero, now: 0f);

        Tick(now: Frame);

        Assert.That(wight.QuarryId, Is.EqualTo(far.Id));
    }

    [Test]
    public void Minion_KeepsItsQuarryWithinTheCadence()
    {
        EnemyAgent first = SpawnHusk(new Vector3(5f, 0f, 0f));
        EnemyAgent second = SpawnHusk(new Vector3(-6f, 0f, 0f));

        MinionAgent wight = _minions.Spawn(Vector3.Zero, now: 0f);

        Tick(now: Frame);

        Assert.That(wight.QuarryId, Is.EqualTo(first.Id), "The first tick chooses, because it had nobody.");

        // Now they swap which is nearest on every single frame — the oscillation Targeter's 10 Hz
        // cadence exists to stop, arriving on the friendly side (rule 6).
        float now = Frame;

        for (int i = 2; i <= 4; i++)
        {
            now = i * Frame;

            ReportEnemies((first, new Vector3(7f * Sign(i), 0f, 0f)), (second, new Vector3(1f * Sign(i), 0f, 0f)));

            Tick(now);

            Assert.That(wight.QuarryId, Is.EqualTo(first.Id), $"tick {i}: 0.{i} s is inside the cadence.");
        }

        Assert.That(now, Is.EqualTo(0.4f).Within(Tolerance), "Sanity: four ticks of 0.1.");

        // And the positive control, so the row cannot pass by the cadence simply never firing.
        Tick(now: 5 * Frame);

        Assert.That(wight.QuarryId, Is.EqualTo(second.Id), "Half a second later it does change its mind.");
    }

    [Test]
    public void Minion_DropsADeadQuarryImmediately()
    {
        EnemyAgent first = SpawnHusk(new Vector3(4f, 0f, 0f));
        EnemyAgent second = SpawnHusk(new Vector3(-9f, 0f, 0f));

        MinionAgent wight = _minions.Spawn(Vector3.Zero, now: 0f);

        Tick(now: Frame);

        Assert.That(wight.QuarryId, Is.EqualTo(first.Id));

        _enemies.ApplyDamage(first.Id, HuskMaxHp, now: Frame, _player);

        Tick(now: 2 * Frame);

        // 0.2 s, which is well inside RetargetInterval: the cadence delays a *change* of mind and
        // never a *dead* one (rule 6).
        Assert.That(MinionSystem.RetargetInterval, Is.GreaterThan(0.2f), "Sanity: the cadence has not run out.");
        Assert.That(wight.QuarryId, Is.EqualTo(second.Id));
    }

    // ---- Rules 7 and 11: the strike ---------------------------------------------------------------

    [Test]
    public void Minion_StrikesAtItsReach()
    {
        EnemyAgent husk = SpawnHusk(new Vector3(1.4f, 0f, 0f));

        MinionAgent wight = _minions.Spawn(Vector3.Zero, now: 0f);

        Tick(now: 0f);

        Assert.That(husk.Health.Current, Is.EqualTo(HuskMaxHp - MinionDamage).Within(Tolerance));

        MinionStruck struck = _events.Single<MinionStruck>();

        Assert.That(struck.Id, Is.EqualTo(wight.Id));
        Assert.That(struck.EnemyId, Is.EqualTo(husk.Id));
        Assert.That(struck.Amount, Is.EqualTo(MinionDamage).Within(Tolerance));

        // Standing on top of it rather than shoving it across the arena — ChaserBehaviour's
        // discipline, and what makes the distance test above a fair one.
        Assert.That(_intents.LastMinionMove.Velocity, Is.EqualTo(Vector3.Zero));

        // And the interval holds it off. Every tick between here and 1.0 s swings nothing.
        for (int i = 1; i < 10; i++)
        {
            Tick(now: i * Frame);
        }

        Assert.That(husk.Health.Current, Is.EqualTo(HuskMaxHp - MinionDamage).Within(Tolerance));
        Assert.That(_events.Count<MinionStruck>(), Is.EqualTo(1));

        Tick(now: AttackInterval);

        Assert.That(husk.Health.Current, Is.EqualTo(HuskMaxHp - (2f * MinionDamage)).Within(Tolerance));
        Assert.That(_events.Count<MinionStruck>(), Is.EqualTo(2));
    }

    [Test]
    public void Minion_DoesNotStrikePastItsReach()
    {
        EnemyAgent husk = SpawnHusk(new Vector3(1.6f, 0f, 0f));

        _minions.Spawn(Vector3.Zero, now: 0f);

        // Five seconds of walking at something two tenths of a metre too far away. Core assigns no
        // position, so the Wight never actually closes — which is the architecture rather than a
        // convenience (AR §3).
        for (int i = 0; i <= 50; i++)
        {
            Tick(now: i * Frame);
        }

        Assert.That(husk.Health.Current, Is.EqualTo(HuskMaxHp).Within(Tolerance));
        Assert.That(_events.Count<MinionStruck>(), Is.Zero);
        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
    }

    [Test]
    public void Minion_KillsAndTheKillPaysExperience()
    {
        EnemyAgent husk = SpawnHusk(new Vector3(1f, 0f, 0f));

        _enemies.ApplyDamage(husk.Id, HuskMaxHp - 1f, now: 0f, _player);
        _enemies.DrainXp();

        _minions.Spawn(Vector3.Zero, now: 0f);

        Tick(now: 0f);

        // Through EnemySystem.ApplyDamage, which is the one door a death comes through (rule 11) —
        // so the *enemy's* death pays normally and nobody had to ask who swung.
        Assert.That(husk.IsAlive, Is.False);
        Assert.That(_events.Single<EnemyDied>().Id, Is.EqualTo(husk.Id));
        Assert.That(_enemies.DrainXp(), Is.EqualTo(HuskXp).Within(Tolerance));

        // Both currencies, because they are banked on the same line of EnemySystem.ApplyDamage and
        // a kill that paid one and not the other would be a death worth different things depending
        // on who swung.
        Assert.That(_enemies.DrainKills(), Is.EqualTo(1));
    }

    // ---- Rule 1: what a Wight is not --------------------------------------------------------------

    [Test]
    public void Minion_IsNotAnEnemy()
    {
        MinionAgent wight = _minions.Spawn(new Vector3(1f, 0f, 0f), now: 0f);

        // (i) The census cannot see it, so nothing that walks EnemyRegistry can.
        Assert.That(_enemies.Registry.AliveCount, Is.Zero);
        Assert.That(_enemies.Registry.TryGet(wight.Id, out EnemyAgent _), Is.False);
        Assert.That(_enemies.LivingCount(), Is.Zero);

        // (ii) The targeter. The player's candidate buffer is built from the enemy span, and the
        // Wight is not in it — so auto-aim cannot point at the thing the player raised, even with
        // one standing a metre away inside an acquire range of twelve and nothing else in the arena.
        //
        // Asserted against an *empty* census rather than by comparing ids, because the two
        // registries both count from 1: a Wight's id and a Husk's id can be the same number, which
        // is the whole reason the walk has its own intent door (rule 4).
        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = Vector3.Zero;

        _player.Tick(Frame, Frame, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);

        Assert.That(
            _player.Targeter.CurrentTargetId,
            Is.LessThan(0),
            "Nothing to shoot, and a Wight is not something.");

        // (iii) The director's door. A stage is complete when its bodies are down — and a Wight
        // standing in the arena does not hold it shut, which reusing EnemyAgent would have made it
        // do at SpawnDirector.IsStageComplete.
        var director = new SpawnDirector(_enemies, _events);
        ProjectileSystem projectiles = Projectiles();

        director.Begin(Plan(), Points(), now: 0f);

        float now = 0f;

        for (int i = 0; i < 400 && !director.IsStageComplete; i++)
        {
            now = i * Frame;

            director.Tick(now, Vector3.Zero, Stream());

            ReadOnlySpan<EnemyAgent> alive = _enemies.Registry.Alive;

            for (int e = alive.Length - 1; e >= 0; e--)
            {
                if (alive[e].IsAlive)
                {
                    _enemies.ApplyDamage(alive[e].Id, HuskMaxHp * 10f, now, _player);
                }
            }

            _enemies.Tick(new EnemyTickContext(
                Frame, now, _player, _intents, _events, projectiles, _enemies));

            Tick(now);
        }

        Assert.That(_minions.Count, Is.EqualTo(1), "Sanity: the Wight is still standing.");
        Assert.That(director.IsStageComplete, Is.True, "A Wight is not a body the stage is waiting on.");
    }

    [Test]
    public void Minion_HasNoBlackboard()
    {
        Type agent = typeof(MinionAgent);

        // Rule 5, written so a later task cannot give a Wight a blackboard without arguing with
        // this row. Fields as well as properties, because "private EnemyBlackboard _blackboard" is
        // exactly the shape that would sneak one in.
        foreach (PropertyInfo property in agent.GetProperties(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Assert.That(
                property.PropertyType,
                Is.Not.EqualTo(typeof(EnemyBlackboard)),
                $"MinionAgent.{property.Name} is an EnemyBlackboard.");
        }

        foreach (FieldInfo field in agent.GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Assert.That(
                field.FieldType,
                Is.Not.EqualTo(typeof(EnemyBlackboard)),
                $"MinionAgent.{field.Name} is an EnemyBlackboard.");
        }

        // And the whole of what a Wight remembers, on the agent where a reader can see it.
        Assert.That(agent.GetProperty("QuarryId"), Is.Not.Null);
        Assert.That(agent.GetProperty("NextAttackAt"), Is.Not.Null);
        Assert.That(agent.GetProperty("ExpiresAt"), Is.Not.Null);
    }

    // ---- Rules 9 and 10: the one door, and Clear --------------------------------------------------

    [Test]
    public void Minion_TakesDamageThroughOneDoor()
    {
        var at = new Vector3(3f, 0f, 4f);

        MinionAgent wight = _minions.Spawn(at, now: 0f);

        DamageResult result = _minions.ApplyDamage(wight.Id, MinionMaxHp + 5f, now: 1f);

        Assert.That(result.Killed, Is.True);
        Assert.That(result.ToHp, Is.EqualTo(MinionMaxHp).Within(Tolerance), "Overkill is trimmed to what landed.");
        Assert.That(_minions.Count, Is.Zero);
        Assert.That(_minions.TryGet(wight.Id, out MinionAgent _), Is.False);

        MinionDied died = _events.Single<MinionDied>();

        Assert.That(died.Id, Is.EqualTo(wight.Id));

        // Carried because M5-06's Second Death keystone needs somewhere to happen, and by the time
        // this is handled the id no longer resolves (rule 10).
        Assert.That(died.Position, Is.EqualTo(at));

        Assert.That(_events.Count<MinionDespawned>(), Is.Zero, "A death is not the clock running out.");

        // And the door is quiet about everything that did nothing — EnemySystem.ApplyDamage's rule.
        Assert.That(_minions.ApplyDamage(wight.Id, 5f, now: 2f).Applied, Is.Zero);
        Assert.That(_minions.ApplyDamage(12345, 5f, now: 2f).Applied, Is.Zero);
        Assert.That(_events.Count<MinionDied>(), Is.EqualTo(1));
    }

    [Test]
    public void Minion_ClearIsSilent()
    {
        for (int i = 0; i < Cap; i++)
        {
            _minions.Spawn(Vector3.Zero, now: 0f);
        }

        _events.Clear();

        _minions.Clear();

        Assert.That(_minions.Count, Is.Zero);
        Assert.That(_events.All, Is.Empty, "EnemySystem.Clear's silence, at the same two moments.");
    }

    // ---- Rule 12: the frame path ------------------------------------------------------------------

    [Test]
    public void Minion_TickAllocatesNothing()
    {
        var silent = new SilentEvents();
        var intents = new RecordingIntents();
        var enemies = NewEnemies(silent);
        var player = new PlayerCombat(Oathbound(), silent, intents, Capacity);

        // A full army and an arena at GD §11's kind of density. The bodies are anvils rather than
        // Husks so that nothing dies mid-measurement and the census stops changing shape, and the
        // lifespan is long enough that nobody expires inside a thousand seconds of ticking.
        var system = new MinionSystem(
            Wight(cap: MinionSystem.MaxConcurrent, lifespan: 100_000f), silent, intents);

        for (int i = 0; i < 28; i++)
        {
            double angle = 2d * Math.PI * i / 28d;

            enemies.Spawn(Id(AnvilId), new Vector3(
                (float)(9d * Math.Cos(angle)), 0f, (float)(9d * Math.Sin(angle))));
        }

        for (int i = 0; i < MinionSystem.MaxConcurrent; i++)
        {
            double angle = 2d * Math.PI * i / MinionSystem.MaxConcurrent;

            // Half of them start inside an anvil's reach, so the strike branch and the walk branch
            // are both inside the measurement.
            float radius = i % 2 == 0 ? 9f - (Reach * 0.5f) : 3f;

            system.Spawn(new Vector3(
                (float)(radius * Math.Cos(angle)), 0f, (float)(radius * Math.Sin(angle))), now: 0f);
        }

        Assert.That(system.Count, Is.EqualTo(MinionSystem.MaxConcurrent), "Sanity: a full army.");

        float clock = 0f;

        // Warmed outside the measurement, so the jit, the modifier lists and the intent buffer are
        // not what is being counted.
        for (int i = 0; i < 600; i++)
        {
            intents.Clear();

            clock += Frame;

            system.Tick(Frame, clock, enemies, player);
        }

        Assert.That(intents.MinionMoves.Count, Is.EqualTo(MinionSystem.MaxConcurrent), "Sanity: it is ticking.");

        _allocationStep = 0;

        AllocationAssert.None(() =>
        {
            intents.Clear();

            clock += Frame;
            _allocationStep++;

            system.Tick(Frame, clock, enemies, player);
        });

        Assert.That(_allocationStep, Is.GreaterThan(10_000), "The probe is live.");
        Assert.That(system.Count, Is.EqualTo(MinionSystem.MaxConcurrent), "Nothing expired under the probe.");
    }

    // ---- Rule 8: the frame ------------------------------------------------------------------------

    [Test]
    public void Run_AGravecallerRunTicksItsArmyAndAnOathboundHasNone()
    {
        // **What the boundary allows, and the class remarks say what it does not.** A Wight cannot
        // be raised from this assembly until M5-04b's Rise exists, so what is provable here is the
        // other half of rule 8 and the whole of the manual-verification note: a class that raises
        // the dead carries a minion system through every tick without changing a single frame of
        // the run, and a class that does not carries nothing at all.
        var events = new RecordingEvents();
        var intents = new RecordingIntents();
        var random = new FixedRandom(Seed);
        var catalog = new ContentCatalog(
            new[] { Oathbound(), Gravecaller() }, new[] { Husk() }, new[] { Descent() });

        var recorder = new RunRecorder(random, new FixedClock(default), events);
        var session = new RunSession(catalog, random, events, intents, recorder, Capacity, Capacity, ProjectileCapacity);

        session.Start(new RunConfig(
            Id(DescentId), Id(GravecallerId), Seed, 1, SpawnPlan.Empty, restore: null));

        var snapshot = new WorldSnapshot(Capacity);

        for (int i = 0; i < 120; i++)
        {
            snapshot.Clear();
            snapshot.Dt = 1f / 60f;
            snapshot.PlayerPosition = Vector3.Zero;

            Assert.DoesNotThrow(() => session.Tick(snapshot), $"tick {i}");
        }

        Assert.That(session.IsRunning, Is.True);

        // Nothing raises one yet, so the army is empty in play and the door stays quiet — which is
        // stated here so an empty MinionMoves list is not read as a fault when M5-05a comes to look.
        Assert.That(intents.MinionMoves, Is.Empty);
        Assert.That(events.Count<MinionSpawned>(), Is.Zero);

        session.End();

        // And an Oathbound run holds no MinionSystem at all (M5-04b rule 10's shape, brought
        // forward by the null): it is byte-identical to the run it was before this task.
        intents.Clear();

        session.Start(new RunConfig(
            Id(DescentId), Id(OathboundId), Seed, 1, SpawnPlan.Empty, restore: null));

        for (int i = 0; i < 120; i++)
        {
            snapshot.Clear();
            snapshot.Dt = 1f / 60f;
            snapshot.PlayerPosition = Vector3.Zero;

            session.Tick(snapshot);
        }

        Assert.That(intents.MinionMoves, Is.Empty);
        Assert.That(session.IsRunning, Is.True);

        session.End();
    }

    // ---- Guards -----------------------------------------------------------------------------------

    [Test]
    public void Minion_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new MinionSystem(null, _events, _intents));
        Assert.Throws<ArgumentNullException>(() => new MinionSystem(Wight(), null, _intents));
        Assert.Throws<ArgumentNullException>(() => new MinionSystem(Wight(), _events, null));

        // A Wight standing at NaN is one every distance test answers nonsense about, and a
        // non-finite clock is an expiry that can never be compared against — both refused at the
        // door, where the caller that invented them is still on the stack (AR §18.3).
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _minions.Spawn(new Vector3(float.NaN, 0f, 0f), now: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _minions.Spawn(new Vector3(0f, 0f, float.PositiveInfinity), now: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => _minions.Spawn(Vector3.Zero, float.NaN));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => _minions.Tick(float.NaN, 0f, _enemies, _player));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _minions.Tick(Frame, float.PositiveInfinity, _enemies, _player));
        Assert.Throws<ArgumentNullException>(() => _minions.Tick(Frame, 0f, null, _player));
        Assert.Throws<ArgumentNullException>(() => _minions.Tick(Frame, 0f, _enemies, null));

        // And TryGet on an id that never stood is false rather than a throw — EnemyRegistry's rule.
        Assert.That(_minions.TryGet(7, out MinionAgent missing), Is.False);
        Assert.That(missing, Is.Null);
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    /// <summary>One frame of the army, with the fixture's enemies and player behind it.</summary>
    private void Tick(float now) => _minions.Tick(Frame, now, _enemies, _player);

    private EnemyAgent SpawnHusk(Vector3 at) => _enemies.Spawn(Id(HuskId), at);

    /// <summary>
    /// Reports where each enemy is this frame, the way a run reports it — the only way a test can
    /// move one, because <c>EnemyAgent.Position</c> is core's to read and Unity's to write (AR §3).
    /// </summary>
    private void ReportEnemies(params (EnemyAgent Agent, Vector3 Position)[] placed)
    {
        _snapshot.Clear();
        _snapshot.Dt = Frame;
        _snapshot.PlayerPosition = Vector3.Zero;

        for (int i = 0; i < placed.Length; i++)
        {
            ref EnemySense sense = ref _snapshot.AddEnemy();

            sense.Id = placed[i].Agent.Id;
            sense.Position = placed[i].Position;
            sense.Velocity = Vector3.Zero;
            sense.PathDirectionToPlayer = Vector2.Zero;
            sense.HasLineOfSight = true;
        }

        _enemies.Ingest(_snapshot);
    }

    /// <summary>Alternates ±1 so two enemies swap which is nearer on every frame.</summary>
    private static float Sign(int i) => i % 2 == 0 ? 1f : -1f;

    private ProjectileSystem Projectiles() => new ProjectileSystem(_events, ProjectileCapacity);

    private EnemySystem NewEnemies(IDomainEvents events) => new EnemySystem(
        Catalog(),
        events,
        new FixedRandom(0.1f),
        new DepthScaling(Scalings.Design()),
        Capacity);

    private static IRandomStream Stream() => new FixedRandom(0.1f).Spawn;

    /// <summary>A one-wave stage, so the director has something real to hold a door shut for.</summary>
    private WavePlan Plan()
    {
        ModeSpec mode = Descent();
        var plan = new WavePlan(4, 1);

        new WaveComposer(Catalog(), new ThreatBudget(mode.Scaling, Capacity))
            .Compose(1, mode, plan, Stream());

        return plan;
    }

    private static IReadOnlyList<Vector3> Points()
    {
        var points = new Vector3[4];

        for (int i = 0; i < points.Length; i++)
        {
            double angle = 2d * Math.PI * i / points.Length;

            points[i] = new Vector3((float)(12d * Math.Cos(angle)), 0f, (float)(12d * Math.Sin(angle)));
        }

        return points;
    }

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[] { Oathbound(), Gravecaller() },
        new[] { Husk(), Anvil() },
        new[] { Descent() });

    private static ContentId Id(string value) => new ContentId(value);

    /// <summary>The Gravecaller's authored minion block (M5-02), unless a row needs it wider.</summary>
    private static MinionSpec Wight(int cap = Cap, float lifespan = Lifespan) => new MinionSpec(
        Id(WightId),
        new LocKey("minion.wight.name"),
        cap,
        lifespan,
        riseChance: 0.25f,
        MinionMaxHp,
        MinionSpeed,
        MinionDamage,
        AttackInterval,
        Reach);

    private static EnemySpec Husk() => new EnemySpec(
        Id(HuskId),
        new LocKey("enemy.husk.name"),
        HuskMaxHp,
        2f,
        1,
        threatCost: 4,
        xpValue: HuskXp,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    /// <summary>
    /// A dummy nothing can kill, for the allocation sweep. Static, because none of these rows is
    /// about what an enemy does and a Chaser would add its own telegraphs to the measurement.
    /// </summary>
    private static EnemySpec Anvil() => new EnemySpec(
        Id(AnvilId),
        new LocKey("enemy.anvil.name"),
        1_000_000f,
        2f,
        1,
        threatCost: 4,
        xpValue: 1f,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    /// <summary>A mode with an empty roster, so a run composes nothing and has no stage to pace.</summary>
    private static ModeSpec Descent() => new ModeSpec(
        Id(DescentId),
        new LocKey("mode.descent.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        new[] { new RosterEntry(Id(HuskId), 1) });

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

    /// <summary>The one class in the game that raises the dead — the Oathbound plus a MinionSpec.</summary>
    private static CharacterSpec Gravecaller() => new CharacterSpec(
        Id(GravecallerId),
        new LocKey("character.gravecaller.name"),
        80f,
        new MovementSpec(3.1f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 9f, 4f, 12f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(
            MovementSkillKind.Shroudstep, 6f, 0.05f, 2.5f, 0.15f, 0f, 0f, 0.05f, decoyDuration: 3f),
        shield: null,
        hitIFrames: 0.5f,
        minions: Wight());
}
