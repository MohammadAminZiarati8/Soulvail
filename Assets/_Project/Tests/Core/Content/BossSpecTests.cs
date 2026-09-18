using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// What a boss is as authored data — every refusal <see cref="BossSpec"/>, <see cref="BossPhaseSpec"/>
/// and <see cref="AddWave"/> make — the mode's boss roster and the schedule it answers with, and
/// GD §9.1 rule 5's 75–120 s expressed as the hit-point window the shipped player's damage implies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which stages hold a boss is authored, and these rows are what makes that claim provable.</b>
/// <see cref="Boss_StageRuleIsAuthoredNotHardcoded"/> asks a mode authored <em>every 3rd</em> and
/// gets bosses at 3, 6 and 9 — a value no shipped asset uses, so a <c>stage % 5</c> that had crept
/// into the code could not pass it (M4-01b rule 1).
/// </para>
/// <para>
/// <b><see cref="Boss_FightLengthIsInsideTheBand"/> asserts a window rather than an asset, and that
/// is a deviation this fixture states rather than hides.</b> The spec's row reads
/// <em>"<c>Warden.asset</c>'s HP"</em>; <c>Warden.asset</c> does not exist, because
/// <c>M4-02</c>'s own Files table owns it along with <c>WardenBoss.asset</c> and the mode's roster
/// entry. So what is pinned here is the half this task can pin and M4-02 has to land inside: the
/// player's damage per second at stage 5, computed through the shipped code the way
/// <c>TimeToKillTests</c> computes hits-to-kill, turned into the authored hit points GD §9.1 rule
/// 5's band allows. A retune of the Censer, of Keen Censer, of Zealotry or of <c>h(n)</c> moves the
/// window and reddens this file, which is the property the row exists for.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BossSpecTests
{
    private const string BossId = "boss.warden";
    private const string ArchonId = "boss.archon";
    private const string WardenEnemyId = "enemy.warden";
    private const string HuskId = "enemy.husk";
    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";

    /// <summary>GD §9.1 rule 3's thresholds for the Warden.</summary>
    private const float SecondPhase = 0.66f;
    private const float ThirdPhase = 0.33f;

    private const float Beat = 1.5f;

    /// <summary>CC §7's Censer, as <c>Oathbound.asset</c> ships it — <c>TimeToKillTests</c>' numbers.</summary>
    private const float WeaponDamage = 13f;
    private const float SwingsPerSecond = 3f;

    /// <summary><c>KeenCenser.asset</c>'s +15 % and <c>Zealotry.asset</c>'s +12 %, both PercentAdd.</summary>
    private const float KeenCenser = 0.15f;
    private const float Zealotry = 0.12f;

    /// <summary>GD §9.1 rule 5's band, in seconds of one boss fight.</summary>
    private const float BandLow = 75f;
    private const float BandHigh = 120f;

    /// <summary>The depth GD §9 puts the first boss at, and the one the band is judged at.</summary>
    private const int BossStage = 5;

    private const float MaxHp = 140f;
    private const float ShieldMax = 30f;
    private const int EnemyCapacity = 8;
    private const float HuskMaxHp = 36f;

    private const float Tolerance = 1e-3f;

    // ---- BossSpec: what a boss must be ------------------------------------------------------------

    [Test]
    public void Spec_KeepsWhatItWasAuthoredWith()
    {
        BossSpec spec = Warden();

        Assert.That(spec.Id, Is.EqualTo(new ContentId(BossId)));
        Assert.That(spec.EnemySpecId, Is.EqualTo(new ContentId(WardenEnemyId)));
        Assert.That(spec.BeatSeconds, Is.EqualTo(Beat).Within(Tolerance));
        Assert.That(spec.Phases.Count, Is.EqualTo(3));
        Assert.That(spec.Phases[0].EntersBelow, Is.EqualTo(1f).Within(Tolerance));
        Assert.That(spec.Phases[1].EntersBelow, Is.EqualTo(SecondPhase).Within(Tolerance));
        Assert.That(spec.Phases[2].EntersBelow, Is.EqualTo(ThirdPhase).Within(Tolerance));
    }

    [Test]
    public void Spec_CopiesThePhaseListRatherThanHoldingIt()
    {
        var authored = new List<BossPhaseSpec> { Phase(1f) };

        var spec = new BossSpec(Id(BossId), Id(WardenEnemyId), authored, Beat);

        authored.Add(Phase(0.5f));

        Assert.That(spec.Phases.Count, Is.EqualTo(1), "A builder that kept filling its own list "
            + "must not be able to change what the boss holds.");
    }

    [Test]
    public void Spec_MaxSummonedBodiesIsTheLargestPhase()
    {
        var spec = new BossSpec(
            Id(BossId),
            Id(WardenEnemyId),
            new[]
            {
                Phase(1f),
                Phase(SecondPhase, (HuskId, 3)),
                Phase(ThirdPhase, (HuskId, 4), (HuskId, 2)),
            },
            Beat);

        // Six, not nine: a phase's summons are cleared before the next phase's are called in, so
        // the table a BossBehaviour sizes from this only ever holds one phase's worth.
        Assert.That(spec.MaxSummonedBodies, Is.EqualTo(6));
        Assert.That(spec.Phases[0].SummonedBodyCount, Is.EqualTo(0));
        Assert.That(spec.Phases[1].SummonedBodyCount, Is.EqualTo(3));
    }

    [Test]
    public void Spec_RefusesAnIdItCannotName()
    {
        Assert.Throws<ArgumentException>(
            () => new BossSpec(default, Id(WardenEnemyId), new[] { Phase(1f) }, Beat));

        Assert.Throws<ArgumentException>(
            () => new BossSpec(Id(BossId), default, new[] { Phase(1f) }, Beat),
            "A boss wears an ordinary body (ADR-0006) and cannot be spawned without one.");
    }

    [Test]
    public void Spec_RefusesNoPhasesAtAll()
    {
        Assert.Throws<ArgumentNullException>(
            () => new BossSpec(Id(BossId), Id(WardenEnemyId), null, Beat));

        Assert.Throws<ArgumentException>(
            () => new BossSpec(Id(BossId), Id(WardenEnemyId), Array.Empty<BossPhaseSpec>(), Beat));

        Assert.Throws<ArgumentException>(
            () => new BossSpec(Id(BossId), Id(WardenEnemyId), new BossPhaseSpec[] { null }, Beat));
    }

    [Test]
    public void Spec_RefusesAFirstPhaseThatDoesNotStartAtFullHealth()
    {
        // The whole of the fight above 0.9 would be in no phase at all, which is a boss that spends
        // its opening tenth deciding nothing.
        Assert.Throws<ArgumentException>(
            () => new BossSpec(Id(BossId), Id(WardenEnemyId), new[] { Phase(0.9f) }, Beat));
    }

    [Test]
    public void Spec_RefusesPhasesOutOfOrder()
    {
        // Rising: phase 2 could only be entered by healing past phase 1's threshold, which the latch
        // forbids — so it is a phase that can never be reached.
        Assert.Throws<ArgumentException>(() => new BossSpec(
            Id(BossId), Id(WardenEnemyId), new[] { Phase(1f), Phase(ThirdPhase), Phase(SecondPhase) }, Beat));

        // Flat: two phases on one threshold, and the second is unreachable for the same reason.
        Assert.Throws<ArgumentException>(() => new BossSpec(
            Id(BossId), Id(WardenEnemyId), new[] { Phase(1f), Phase(SecondPhase), Phase(SecondPhase) }, Beat));
    }

    [TestCase(0f)]
    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Spec_RefusesABeatThatIsNotAFiniteLength(float beat)
    {
        // NaN and infinity are both beats that never end — every comparison against a NaN is false,
        // and an infinite one is a boss that is untouchable for the rest of the run (AR §18.3).
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BossSpec(Id(BossId), Id(WardenEnemyId), new[] { Phase(1f) }, beat));
    }

    // ---- BossPhaseSpec and AddWave ----------------------------------------------------------------

    [TestCase(0f)]
    [TestCase(-0.1f)]
    [TestCase(1.1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    public void Phase_RefusesAThresholdOutsideTheRange(float entersBelow)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BossPhaseSpec(entersBelow));
    }

    [Test]
    public void Phase_RefusesASummonRowThatNamesNothing()
    {
        // default(AddWave) carries a zeroed id straight past the struct's own constructor, so the
        // check is repeated where the list is copied (AR §18.3).
        Assert.Throws<ArgumentException>(
            () => new BossPhaseSpec(1f, new[] { default(AddWave) }));
    }

    [Test]
    public void Phase_SummonsAreOptionalAndEmptyIsLegal()
    {
        Assert.That(new BossPhaseSpec(1f).Summons, Is.Empty);
        Assert.That(new BossPhaseSpec(1f, Array.Empty<AddWave>()).Summons, Is.Empty);
    }

    [Test]
    public void Add_RefusesAnArchetypeItCannotName()
    {
        Assert.Throws<ArgumentException>(() => new AddWave(default, 3));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Add_RefusesACountBelowOne(int count)
    {
        // A phase that summons nothing authors no row at all, rather than one of zero — the same
        // rule EnemySpec.ThreatCost makes about a free archetype.
        Assert.Throws<ArgumentOutOfRangeException>(() => new AddWave(Id(HuskId), count));
    }

    [Test]
    public void Add_TakesTwoRowsOfOneArchetypeOnPurpose()
    {
        // Deliberately *not* refused, unlike ModeSpec's roster: five Husks and three Husks is a
        // legible thing to author, where two roster rows for one archetype could only disagree.
        var phase = new BossPhaseSpec(SecondPhase, new[] { new AddWave(Id(HuskId), 5), new AddWave(Id(HuskId), 3) });

        Assert.That(phase.SummonedBodyCount, Is.EqualTo(8));
    }

    // ---- Rule 1: the schedule is the mode's, and it is authored ------------------------------------

    [Test]
    public void Boss_SpawnsOnAnAuthoredStage()
    {
        ModeSpec mode = Mode((BossId, 5));

        Assert.That(mode.TryGetBossFor(5, out ContentId at5), Is.True);
        Assert.That(at5, Is.EqualTo(Id(BossId)));

        Assert.That(mode.TryGetBossFor(10, out _), Is.True);

        Assert.That(mode.TryGetBossFor(4, out ContentId at4), Is.False);
        Assert.That(at4, Is.EqualTo(default(ContentId)), "A stage with no boss names none.");

        Assert.That(mode.TryGetBossFor(6, out _), Is.False);
    }

    [Test]
    public void Boss_StageRuleIsAuthoredNotHardcoded()
    {
        // **Every 3rd — a value no shipped asset uses.** A `stage % 5` that had crept into the code
        // would pass the row above and fail this one, which is the whole point of asserting against
        // an interval nothing ships (rule 1).
        ModeSpec mode = Mode((BossId, 3));

        for (int stage = 1; stage <= 9; stage++)
        {
            Assert.That(
                mode.TryGetBossFor(stage, out _),
                Is.EqualTo(stage % 3 == 0),
                $"stage {stage} against an authored interval of 3");
        }
    }

    [Test]
    public void Boss_AModeWithNoRosterNeverReachesOne()
    {
        ModeSpec mode = Mode();

        for (int stage = 1; stage <= 40; stage++)
        {
            Assert.That(mode.TryGetBossFor(stage, out _), Is.False);
        }
    }

    [Test]
    public void Roster_TheFirstMatchingRowWins()
    {
        // GD §9 puts an Archon on every 20th stage *and* a boss on every 5th, and stage 20 is both.
        // The rarer row is authored first, so stage 20 is an Archon — stated rather than resolved by
        // picking the larger interval, because "the rarer one wins" is a rule nobody wrote down.
        ModeSpec mode = Mode((ArchonId, 20), (BossId, 5));

        Assert.That(mode.TryGetBossFor(20, out ContentId at20), Is.True);
        Assert.That(at20, Is.EqualTo(Id(ArchonId)));

        Assert.That(mode.TryGetBossFor(15, out ContentId at15), Is.True);
        Assert.That(at15, Is.EqualTo(Id(BossId)));
    }

    [Test]
    public void Roster_RefusesARowItCannotRead()
    {
        Assert.Throws<ArgumentException>(() => new BossRosterEntry(default, 5));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BossRosterEntry(Id(BossId), 0),
            "A zero interval is a modulo by zero on the first stage the mode is asked about.");

        Assert.Throws<ArgumentOutOfRangeException>(() => new BossRosterEntry(Id(BossId), -5));

        // And the struct's zeroed form, which never passed that constructor (AR §18.3).
        Assert.Throws<ArgumentException>(
            () => ModeWith(new[] { default(BossRosterEntry) }));
    }

    [Test]
    public void Roster_RefusesARosterThatCannotBeRead()
    {
        Assert.Throws<ArgumentException>(
            () => ModeWith(new[] { new BossRosterEntry(Id(BossId), 5), new BossRosterEntry(Id(BossId), 7) }),
            "One boss comes round on one schedule.");

        Assert.Throws<ArgumentException>(
            () => ModeWith(new[] { new BossRosterEntry(Id(BossId), 5), new BossRosterEntry(Id(ArchonId), 5) }),
            "The first matching row wins, so the second would never be reached at any depth.");
    }

    [Test]
    public void Roster_IsCopiedRatherThanHeld()
    {
        var authored = new List<BossRosterEntry> { new BossRosterEntry(Id(BossId), 5) };

        ModeSpec mode = ModeWith(authored);

        authored.Add(new BossRosterEntry(Id(ArchonId), 20));

        Assert.That(mode.BossRoster.Count, Is.EqualTo(1));
    }

    // ---- Rule 8: GD §9.1 rule 5's 75–120 s, as a hit-point window ---------------------------------

    [Test]
    public void Boss_FightLengthIsInsideTheBand()
    {
        // **Every number here is computed off shipped code, never typed.** The damage per swing and
        // the swings per second come out of a real PlayerCombat with the two damage-raising nodes of
        // M3-12c's tree applied through the real EffectRegistry; the depth multiplier comes out of
        // the shipped DepthScaling applied to a real agent. What the row asserts is the authored
        // hit-point window GD §9.1 rule 5's 75–120 s allows at stage 5 — the number M4-02's
        // `Warden.asset` has to land inside.
        float dps = PlayerDpsAtTheBossStage();

        Assert.That(dps, Is.EqualTo(50.232f).Within(Tolerance), "13 × 1.15 swung 3 × 1.12 times a second.");

        float depth = DepthMultiplierAt(BossStage);

        Assert.That(depth, Is.EqualTo(1.24f).Within(Tolerance), "h(5) = 1 + 0.06 × 4.");

        float lowest = BandLow * dps / depth;
        float highest = BandHigh * dps / depth;

        Assert.That(lowest, Is.EqualTo(3038.226f).Within(0.01f));
        Assert.That(highest, Is.EqualTo(4861.161f).Within(0.01f));

        // The round trip, so the window is not merely two numbers that happen to look right: a boss
        // authored at either edge fights for exactly the band's edge.
        Assert.That(FightSeconds(lowest, dps, depth), Is.EqualTo(BandLow).Within(Tolerance));
        Assert.That(FightSeconds(highest, dps, depth), Is.EqualTo(BandHigh).Within(Tolerance));

        // And the sanity that makes the whole row worth having: a boss authored like an ordinary
        // body is over in under a second, which is the failure the band exists to catch.
        Assert.That(FightSeconds(HuskMaxHp, dps, depth), Is.LessThan(1f));
    }

    [Test]
    public void Boss_FightLengthNamesWhatItExcludes()
    {
        // **The row that makes the exclusion a claim rather than an omission**, the way
        // `Ttk_ExcludesSurvivabilityAndReach` does for hits-to-kill. The window above is maximum hit
        // points divided by damage per second and nothing else. Five things it cannot see, each of
        // which makes a real fight *longer* than the model says:
        //
        //   1. The two invulnerable beats — 2 × BeatSeconds of a fight in which no damage lands.
        //   2. Time spent not swinging: walking out of a shockwave, standing off a fissure (M4-02).
        //   3. The adds, which are both a distraction and a second thing to spend swings on.
        //   4. Swings that miss, because the Censer is a cone and a boss is a moving body.
        //   5. The player dying, which ends the fight rather than lengthening it.
        //
        // So the window is a *ceiling* on authored hit points rather than a prediction of the clock,
        // and M4-02 authoring at its top would produce a fight longer than 120 s. The two beats are
        // the only one of the five this task can price, and it does:
        float dps = PlayerDpsAtTheBossStage();
        float depth = DepthMultiplierAt(BossStage);

        float highest = BandHigh * dps / depth;
        float beats = 2f * Beat;

        Assert.That(beats, Is.EqualTo(3f).Within(Tolerance), "Two crossings at the Warden's 1.5 s.");

        // A boss authored at the ceiling already spends three seconds of its 120 untouchable, so the
        // honest ceiling is lower — and this is the number that says by how much.
        Assert.That(
            FightSeconds(highest, dps, depth) + beats,
            Is.GreaterThan(BandHigh),
            "The beats are outside the model, so the top of the window already breaches the band.");

        Assert.That(
            (BandHigh - beats) * dps / depth,
            Is.EqualTo(4739.632f).Within(0.01f),
            "The ceiling with the beats priced in, which is what M4-02 should author under.");
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    /// <summary>
    /// Damage per second at stage 5, through the shipped code: a real <c>PlayerCombat</c> with the
    /// two damage-raising nodes of M3-12c's tree applied through the real <c>EffectRegistry</c>.
    /// </summary>
    /// <remarks>
    /// <b>Two nodes and no Overflow, and the path is stated.</b> Eight levels is what the shipped
    /// <c>XpCurve</c> reaches by stage 5 and the tree holds twelve nodes, so nothing has overflowed
    /// yet — Overflow pays only once the tree is full, which is around stage 9 (M3-15's measurement).
    /// Keen Censer and Zealotry are the only two of the twelve that can move damage per second;
    /// <c>TimeToKillTests.Ttk_ExcludesSurvivabilityAndReach</c> is the row that proves the other six
    /// cannot, and Zealotry is on <em>this</em> list and off that one because hits-to-kill is stated
    /// in swings and this is stated in seconds.
    /// </remarks>
    private static float PlayerDpsAtTheBossStage()
    {
        var events = new RecordingEvents();
        var intents = new RecordingIntents();

        CharacterSpec spec = Oathbound();

        var combat = new PlayerCombat(spec, events, intents, EnemyCapacity);
        var motor = new PlayerMotor(spec.Movement, Vector3.UnitZ);
        var progression = new LevelTracker(Scalings.Xp(), events);

        var effects = new EffectRegistry();
        effects.Register(new ModifyStatHandler(new PlayerStats(combat, motor, progression)));

        var node = new object();

        effects.Apply(new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, KeenCenser), node);
        effects.Apply(new ModifyStat(PlayerStat.FireRate, ModifierKind.PercentAdd, Zealotry), node);

        return combat.Weapon.Damage.Value * combat.Weapon.FireRate.Value;
    }

    /// <summary>
    /// GD §12.3's h(n) at <paramref name="stage"/>, read off the shipped <see cref="DepthScaling"/>
    /// applied to a real agent rather than multiplied out here — <c>TimeToKillTests</c>' shape.
    /// </summary>
    private static float DepthMultiplierAt(int stage)
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        EnemyAgent agent = registry.Spawn(Husk(), new Vector3(0f, 0f, 3f));

        new DepthScaling(Scalings.Design()).Apply(agent, stage);

        return agent.Health.MaxHp.Value / HuskMaxHp;
    }

    /// <summary>How long a boss authored at <paramref name="authoredHp"/> lasts, in seconds.</summary>
    private static float FightSeconds(float authoredHp, float dps, float depth) =>
        authoredHp * depth / dps;

    private static ContentId Id(string value) => new ContentId(value);

    private static BossSpec Warden() => new BossSpec(
        Id(BossId),
        Id(WardenEnemyId),
        new[] { Phase(1f), Phase(SecondPhase, (HuskId, 3)), Phase(ThirdPhase, (HuskId, 4)) },
        Beat);

    private static BossPhaseSpec Phase(float entersBelow, params (string Id, int Count)[] summons)
    {
        var waves = new AddWave[summons.Length];

        for (int i = 0; i < summons.Length; i++)
        {
            waves[i] = new AddWave(Id(summons[i].Id), summons[i].Count);
        }

        return new BossPhaseSpec(entersBelow, waves);
    }

    private static ModeSpec Mode(params (string Id, int Every)[] bosses)
    {
        var entries = new BossRosterEntry[bosses.Length];

        for (int i = 0; i < bosses.Length; i++)
        {
            entries[i] = new BossRosterEntry(Id(bosses[i].Id), bosses[i].Every);
        }

        return ModeWith(entries);
    }

    private static ModeSpec ModeWith(IReadOnlyList<BossRosterEntry> bossRoster) => new ModeSpec(
        Id(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>(),
        arenas: null,
        bossRoster: bossRoster);

    /// <summary>
    /// CC §7's Oathbound with the Focus ramp switched off — <c>TimeToKillTests</c>' fixture, for its
    /// reason: a ramp would move the swing cadence, and this file multiplies by it.
    /// </summary>
    private static CharacterSpec Oathbound() => new CharacterSpec(
        Id(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, SwingsPerSecond, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(ShieldMax, 4f, 15f),
        0.5f);

    /// <summary>GD §8.1's Husk, Static so that it stands where it was put.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        Id(HuskId),
        new LocKey("enemy.husk.name"),
        HuskMaxHp,
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
}
