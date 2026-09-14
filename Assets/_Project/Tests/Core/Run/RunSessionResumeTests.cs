using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// <c>RunConfig</c>'s sixth field and what <c>RunSession.Start</c> does with it: what a restore
/// puts back, when it puts it back, what it refuses, and the property the whole feature exists
/// for — that a resumed run fights the stage the interrupted one was about to fight.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row drives a real session rather than building a <c>RunState</c></b>, for
/// <c>RunRecorderTests</c>' reason: <c>RunState</c>'s constructor is <c>internal</c> and
/// <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c>, deliberately. A fixture that
/// skipped starting a run would stop testing the thing that applies the restore.
/// </para>
/// <para>
/// <b>The generator is put back by the fixture, standing in for the composition root</b> (rule 3).
/// That is not a shortcut around the code under test — it <em>is</em> the contract: core is handed
/// a generator already standing where the save left it and never learns that anything was rewound.
/// <c>ResumeFlowTests.Installer_RestoresTheGeneratorState</c> is where the real
/// <c>RunInstaller</c> answers for doing it.
/// </para>
/// <para>
/// A fixture beside <c>RunSessionTests</c> rather than rows inside it. That file is M0's brain and
/// is already 800 lines; these rows need a real budget curve, a boundary and two runs compared
/// against each other, which is a different world to build.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RunSessionResumeTests
{
    private const string ModeId = "mode.test";
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";

    /// <summary>An arena roster of two, so <c>ArenaFor</c>'s no-repeat walk has somewhere to go.</summary>
    private const string FirstArenaId = "arena.pillars";
    private const string SecondArenaId = "arena.tiered";

    /// <summary>GD §8.1's threat cost for the one archetype these rows compose from.</summary>
    private const int HuskCost = 4;

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    /// <summary>The class's hit points, and never the number any row restores to.</summary>
    private const float MaxHp = 100f;

    /// <summary>The Aegis this fixture's class carries.</summary>
    private const float ShieldMax = 12f;

    /// <summary>What every restoring row puts the player back at. Neither is a maximum.</summary>
    private const float SavedHp = 62f;
    private const float SavedShield = 9f;

    private const float SavedRunTime = 412.5f;

    /// <summary>
    /// What a levelled save carries, and none of it is the value a fresh run has. Level 4 rather
    /// than 2 and one pick owed rather than none, so a restore that was quietly deleted would show
    /// up as a wrong number rather than as a plausible one.
    /// </summary>
    /// <remarks>
    /// 30 experience is comfortably inside level 4's bar — <c>Scalings.Xp()</c> charges
    /// 20 + 12·5^1.4 ≈ 134 to reach level 5 — so these rows test the restore rather than the settle.
    /// <c>Start_RestoreSettlesAnOverfullBar</c> is the row that goes the other way on purpose.
    /// </remarks>
    private const int SavedLevel = 4;
    private const float SavedXp = 30f;
    private const int SavedPending = 1;

    /// <summary>
    /// The tree the node rows turn on: five positions, three of them sharing branch 0's only tier
    /// so that any order of those three is one the gating allows.
    /// </summary>
    private const string TreeIdValue = "tree.oathbound";
    private const string NodeMaxHp = "skill.test.bulwark";
    private const string NodeDamage = "skill.test.vow";
    private const string NodeSpare = "skill.test.spare";
    private const string NodeB = "skill.test.b";

    /// <summary>The one Active, for M3-06's rows. Not in <see cref="Tree"/> — see BuildWithActiveTree.</summary>
    private const string NodeActive = "skill.test.active";

    /// <summary>
    /// Its cooldown, ten minutes rather than a plausible eight seconds.
    /// </summary>
    /// <remarks>
    /// <c>ClearTheStage</c> plays a whole stage out and may tick for two and a half simulated
    /// minutes, so an eight-second skill whose trigger always holds would fire twenty times on the
    /// way and <c>Session_BoundaryLeavesCooldownsRunning</c> would be counting them instead of
    /// asking whether the boundary reset the clock. The long wait is what makes "still cooling on
    /// the other side of the door" a claim about the boundary.
    /// </remarks>
    private const float ActiveCooldown = 600f;

    private const string NodeC = "skill.test.c";

    /// <summary>
    /// The class the tree rows play, at 140 rather than <see cref="MaxHp"/>: the ordering row needs
    /// a saved hit-point value that is above the class's own maximum and below the modified one, and
    /// 150 of 160 against 140 is the clearest arithmetic for that.
    /// </summary>
    private const float TreeMaxHp = 140f;

    /// <summary>What the one max-HP node adds. Twenty, so 140 becomes 160.</summary>
    private const float MaxHpNodeBonus = 20f;

    /// <summary>
    /// Hit points a run was saved with that only a modified maximum can hold. Above
    /// <see cref="TreeMaxHp"/> on purpose — that is the whole of the ordering row.
    /// </summary>
    private const float SavedHpAboveBaseMax = 150f;

    private const float Frame = 1f / 60f;

    /// <summary>Comfortably outside <c>SpawnDirector.MinPlayerDistance</c> of the origin.</summary>
    private const float Ring = 10f;

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private FixedRandom _random;
    private FixedClock _clock;
    private ContentCatalog _catalog;
    private ModeSpec _mode;
    private RunSession _session;

    // ---- The field (rule 1) --------------------------------------------------------------------

    [Test]
    public void Config_RecordsTheRestore()
    {
        RunSnapshot snapshot = Snapshot(stage: 4, seed: 7);

        var resuming = new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            seed: 7,
            stageIndex: 4,
            SpawnPlan.Empty,
            restore: snapshot);

        Assert.That(resuming.Restore.HasValue, Is.True);
        Assert.That(resuming.Restore.Value.StageIndex, Is.EqualTo(4));
        Assert.That(resuming.Restore.Value.PlayerHp, Is.EqualTo(SavedHp));

        var fresh = new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            seed: 7,
            stageIndex: 4,
            SpawnPlan.Empty,
            restore: null);

        Assert.That(
            fresh.Restore,
            Is.Null,
            "A fresh run says so with null rather than by omission — which is why the sixth "
                + "parameter is required and not defaulted.");
    }

    // ---- What comes back, and when (rules 2, 3) ------------------------------------------------

    [Test]
    public void Start_RestoresHpAndShield()
    {
        Build(seed: 7);

        StartResumed(stage: 4);

        Assert.That(_session.State.PlayerHp, Is.EqualTo(SavedHp));
        Assert.That(_session.State.PlayerShield, Is.EqualTo(SavedShield));

        // The fixture's own claim, checked out loud: if the class's maximum happened to equal the
        // saved value, this row would pass with the restore deleted.
        Assert.That(SavedHp, Is.Not.EqualTo(MaxHp));
        Assert.That(SavedShield, Is.Not.EqualTo(ShieldMax));
    }

    [Test]
    public void Start_RestoresLevelXpPending()
    {
        Build(seed: 7);

        StartResumed(stage: 4);

        // The three reads a HUD and a level-up flow start from. Absolute experience, not the
        // fraction: XpToNext moves with the level and with the mode's curve, so a fraction cannot
        // be restored without the maximum that produced it (rule 8).
        Assert.That(_session.State.Level, Is.EqualTo(SavedLevel));
        Assert.That(_session.State.Xp, Is.EqualTo(SavedXp));
        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(SavedPending));

        // The fixture's own claim, checked out loud: a fresh run is level 1 with nothing owed, so
        // if the saved values were those this row would pass with the restore deleted.
        Assert.That(SavedLevel, Is.Not.EqualTo(1));
        Assert.That(SavedPending, Is.Not.EqualTo(0));
    }

    [Test]
    public void Start_RestoreSettlesAnOverfullBar()
    {
        Build(seed: 7);

        _events.Clear();

        // Far past the bar. The case is not hypothetical: CH §5.2's exponent is flagged for a
        // retune at M3-15, and a curve that got cheaper between builds would leave a legal save
        // sitting above its own threshold — levelling only when the next kill happened to push it
        // over, which is a run that is silently one or more picks poorer than it earned.
        StartResumed(stage: 4, Snapshot(4, _random.Seed, level: 2, xp: 10_000f, pendingLevelUps: 0));

        Assert.That(_session.State.Level, Is.GreaterThan(2), "The thresholds the saved XP pays for are crossed.");
        Assert.That(_session.State.PendingLevelUps, Is.GreaterThan(0), "And each one banks a pick.");

        // Settled, not merely climbed: what is left is inside the new level's bar.
        Assert.That(_session.State.XpFraction, Is.LessThan(1f));

        // **Silently.** Nothing may publish before RunStarted — a LeveledUp raised here would put a
        // level-up screen in front of a player for a level they earned in a previous session, and
        // an XpChanged would reach a bar that RunStarted has not drawn yet (rule 7).
        Assert.That(_events.Count<LeveledUp>(), Is.EqualTo(0));
        Assert.That(_events.Count<XpChanged>(), Is.EqualTo(0));
    }

    [Test]
    public void Start_RestoresTakenNodes()
    {
        BuildWithTree(seed: 7);

        // Both at branch 0's only tier, so either order is one the gating allows and this row is
        // about the restore rather than about the order.
        var taken = new[] { new ContentId(NodeMaxHp), new ContentId(NodeDamage) };

        StartResumed(stage: 4, Snapshot(4, _random.Seed, takenNodeIds: taken));

        Assert.That(_session.State.TakenNodeCount, Is.EqualTo(2));
        Assert.That(_session.State.TakenNodeIds, Is.EqualTo(taken), "In take order, which is what the list means.");
        Assert.That(_session.State.IsTreeFull, Is.False, "Two of five.");

        // **And their effects are on**, which is the half that matters: without it every passive in
        // a save would be silently forgotten, and the run would come back weaker than the one that
        // was interrupted with no symptom anywhere.
        //
        // Max HP rather than weapon damage, because `RunState` has a read for one and not the other
        // — see this task's *As built*. The arithmetic is the class's 140 plus the node's 20.
        Assert.That(_session.State.PlayerMaxHp, Is.EqualTo(TreeMaxHp + MaxHpNodeBonus).Within(0.01f));

        Assert.That(
            _session.State.PlayerMaxHp,
            Is.Not.EqualTo(TreeMaxHp),
            "The fixture's own claim: if the node moved nothing, this row would pass with the "
                + "restore deleted.");
    }

    // ---- M3-06: the runner a resumed run comes back with -----------------------------------------

    [Test]
    public void Session_RestoreAddsTheTakenActives()
    {
        // **M3-06 rule 5.** Core pushes a skill into the runner and the runner subscribes to
        // nothing, so a resumed run's actives arrive here — `Start` walking `TakenIds` in take order
        // after `SkillTree.Restore` has replayed it. One of the two nodes is an Active and the other
        // is not, so a loop that added everything would answer 2.
        BuildWithActiveTree(seed: 7);

        var taken = new[] { new ContentId(NodeActive), new ContentId(NodeDamage) };

        StartResumed(stage: 4, Snapshot(4, _random.Seed, takenNodeIds: taken));

        Assert.That(_session.State.TakenNodeCount, Is.EqualTo(2), "Both nodes came back.");
        Assert.That(_session.State.OwnedActiveCount, Is.EqualTo(1), "And exactly one of them fires.");

        Assert.That(_session.State.SkillIdAt(0), Is.EqualTo(new ContentId(NodeActive)));
        Assert.That(_session.State.IsSkillReady(0), Is.True, "A resumed skill is off cooldown.");
        Assert.That(_session.State.SkillCooldownFraction(0), Is.EqualTo(0f));

        // **Silently**, like every other restore: nothing may publish before `RunStarted`, and a
        // `SkillCast` raised here would announce as news a skill nobody fired.
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(0));
    }

    // ---- v3: the loadout comes back (M3-07b rules 6, 7) -----------------------------------------

    [Test]
    public void Start_RestoresTheSlots()
    {
        BuildWithActiveTree(seed: 7, actives: 3);

        StartResumed(stage: 4, Snapshot(
            4,
            _random.Seed,
            takenNodeIds: ThreeActives(),
            manualSkillIds: new[]
            {
                new ContentId(NodeActive),
                default(ContentId),
                new ContentId(NodeActive + ".2"),
                default(ContentId),
            }));

        // **The hole in place.** A restore that compacted would put the third skill under the thumb
        // that had learned the second — the silent re-bind M3-07a rule 3 refuses during a run and
        // this rule refuses across a restart.
        Assert.That(_session.State.ManualSlotAt(0), Is.EqualTo(new ContentId(NodeActive)));
        Assert.That(_session.State.ManualSlotAt(1), Is.EqualTo(default(ContentId)));
        Assert.That(_session.State.ManualSlotAt(2), Is.EqualTo(new ContentId(NodeActive + ".2")));
        Assert.That(_session.State.ManualSlotAt(3), Is.EqualTo(default(ContentId)));

        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(2));

        // The flags moved with the table — the two restored skills are Manual, the third is still
        // Auto. Written out because `_isAuto` and `_slots` are separate arrays and a restore that
        // wrote one without the other would pass every assertion above and then let a Manual skill
        // fire itself on the first tick.
        Assert.That(_session.State.IsAutoCast(new ContentId(NodeActive)), Is.False);
        Assert.That(_session.State.IsAutoCast(new ContentId(NodeActive + ".2")), Is.False);
        Assert.That(_session.State.IsAutoCast(new ContentId(NodeActive + ".1")), Is.True);

        // **Silently**, like every other restore in this block: nothing may publish before
        // `RunStarted`, and a `SkillAutoCastChanged` raised here would announce as news a toggle
        // the player did not touch — CC §6.3's screen would flash on a resume.
        Assert.That(_events.Count<SkillAutoCastChanged>(), Is.EqualTo(0));
    }

    [Test]
    public void Start_RestoreDropsAnUnownedSlot()
    {
        BuildWithActiveTree(seed: 7, actives: 3);

        // Only two of the three nodes come back, and the slot list names the third — a node this
        // build no longer ships, a hand-edited file, or a tree that changed between builds. All
        // three arrive here looking identical and none is worth refusing a save for.
        StartResumed(stage: 4, Snapshot(
            4,
            _random.Seed,
            takenNodeIds: new[]
            {
                new ContentId(NodeActive),
                new ContentId(NodeActive + ".1"),
            },
            manualSkillIds: new[]
            {
                new ContentId(NodeActive),
                new ContentId(NodeActive + ".2"),
                new ContentId(NodeActive + ".1"),
                default(ContentId),
            }));

        // **Deliberately unlike `SkillTree.Restore`, which throws for an unknown node** (M3-03 rule
        // 5). A taken node *is* the run's power, so dropping one silently hands the player a weaker
        // character than they saved; a slot is only where a button sits, and a missing button costs
        // one visit to CC §6.3's screen.
        Assert.That(_session.State.ManualSlotAt(0), Is.EqualTo(new ContentId(NodeActive)));
        Assert.That(_session.State.ManualSlotAt(1), Is.EqualTo(default(ContentId)), "Dropped.");
        Assert.That(_session.State.ManualSlotAt(2), Is.EqualTo(new ContentId(NodeActive + ".1")));

        // The rest restored, and the run is running — the drop is not a failure.
        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(2));
        Assert.That(_session.IsRunning, Is.True);
        Assert.That(_events.Count<RunStarted>(), Is.EqualTo(1));
    }

    [Test]
    public void Start_RestoredManualSkillDoesNotAutoCast()
    {
        // **The point of the field.** `ActiveNode`'s trigger is `HpFraction Below 1.5`, which holds
        // on every tick of a healthy run — so this skill fires on the first tick unless something
        // stops it, and the only thing that can is the Manual flag the restore put back.
        BuildWithActiveTree(seed: 7);

        StartResumed(stage: 4, Snapshot(
            4,
            _random.Seed,
            takenNodeIds: new[] { new ContentId(NodeActive) },
            manualSkillIds: new[]
            {
                new ContentId(NodeActive),
                default(ContentId),
                default(ContentId),
                default(ContentId),
            }));

        _events.Clear();

        TickFor(1);

        Assert.That(
            _events.Count<SkillCast>(),
            Is.EqualTo(0),
            "A resumed Manual skill fired itself, which is the loadout not surviving the restart.");

        // And it is ready rather than suppressed — the difference between "Manual" and "cooling".
        Assert.That(_session.State.IsSkillReady(0), Is.True);
    }

    [Test]
    public void Start_RestoresSlotsAfterTheRunnerKnowsTheActives()
    {
        // **The ordering row (rule 7, AR §18.1).** The slot restore sits *below* the loop that
        // tells the runner about the restored Actives. Swap the two lines and this slot is empty:
        // `SkillRunner.Restore` would be asked about a skill the runner had not been told about
        // yet, which is indistinguishable from rule 6's stale id — so every slot would be dropped
        // in silence and a resumed run would come back with no buttons and no error.
        BuildWithActiveTree(seed: 7);

        StartResumed(stage: 4, Snapshot(
            4,
            _random.Seed,
            takenNodeIds: new[] { new ContentId(NodeActive) },
            manualSkillIds: new[]
            {
                new ContentId(NodeActive),
                default(ContentId),
                default(ContentId),
                default(ContentId),
            }));

        Assert.That(
            _session.State.ManualSlotAt(0),
            Is.EqualTo(new ContentId(NodeActive)),
            "The slot is empty if the restore runs above the loop that adds the actives.");

        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(1));
    }

    [Test]
    public void Start_FreshRunHasFourEmptySlots()
    {
        Build(seed: 7);

        _session.Start(FreshConfig(stage: 4));

        // The other half of the restore rows: a config with no snapshot inherits nothing, and CC
        // §6.1's default is Auto — so a fresh run and a migrated v2 run are indistinguishable on
        // this axis, which is what lets the v2 → v3 step need no special case above the DTO.
        Assert.That(_session.State.ManualSlotCount, Is.EqualTo(0));

        for (int slot = 0; slot < SkillRunner.MaxManualSlots; slot++)
        {
            Assert.That(_session.State.ManualSlotAt(slot), Is.EqualTo(default(ContentId)));
        }

        Assert.That(
            _session.State.ManualSkillIds,
            Has.Count.EqualTo(SkillRunner.MaxManualSlots));
    }

    [Test]
    public void Session_BoundaryLeavesCooldownsRunning()
    {
        // **M3-06 rule 11.** M2-10's standing rule is that a boundary resets the `Targeter` and
        // never `PlayerCombat` — a door is not a free heal, and it is not a free set of cooldowns
        // either. `SkillRunner.Reset` exists and nothing in M3 calls it; this is the row that says
        // a stage boundary is not one of its callers.
        BuildWithActiveTree(seed: 7);

        StartResumed(
            stage: 4,
            Snapshot(4, _random.Seed, takenNodeIds: new[] { new ContentId(NodeActive) }));

        // The trigger is `HpFraction Below 1.5`, which always holds, so the first tick of the run
        // casts it and puts an eight-second wait on the clock.
        TickFor(1);

        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(1), "Arranged: it cast.");
        Assert.That(_session.State.IsSkillReady(0), Is.False);

        float fractionBefore = _session.State.SkillCooldownFraction(0);

        _events.Clear();

        ClearTheStage();
        CrossTheBoundary();

        // Still cooling on the other side of the door, and no second cast — a boundary that had
        // reset the runner would have handed the player a free skill on arrival.
        Assert.That(_session.State.IsSkillReady(0), Is.False);
        Assert.That(_events.Count<SkillCast>(), Is.EqualTo(0));

        Assert.That(
            _session.State.SkillCooldownFraction(0),
            Is.LessThan(fractionBefore),
            "And the wait ran down across the boundary rather than standing still — it is an "
                + "absolute time against the simulated clock, which the boundary does not rewind.");

        Assert.That(_session.State.OwnedActiveCount, Is.EqualTo(1), "The skill itself is still owned.");
    }

    [Test]
    public void Start_OverCapacityTreeRefusesTheRun()
    {
        // **The owner's ruling at M3-06.** A tree holding more actives than the runner can own is an
        // authoring mistake, and it refuses the *run* rather than the pick — `TreeRules`' own
        // argument one class over. Left to `SkillRunner.Add`, the thirteenth would throw inside
        // M3-08a's `ChooseOffer`, *after* `SkillTree.Take` had recorded the node, applied its
        // effects and published `NodeTaken`: a run that dies at the moment a card is tapped, and
        // dies dirty.
        BuildWithActiveTree(seed: 7, actives: SkillRunner.MaxActives + 1);

        Assert.Throws<ArgumentException>(() => _session.Start(FreshConfig(stage: 4)));

        AssertNothingStands();

        // And one fewer is a legal tree, so the row is about the boundary rather than about any
        // tree with actives in it.
        BuildWithActiveTree(seed: 7, actives: SkillRunner.MaxActives);

        Assert.DoesNotThrow(() => _session.Start(FreshConfig(stage: 4)));
    }

    [Test]
    public void Start_RestoresNodesBeforeHealth()
    {
        BuildWithTree(seed: 7);

        // **The ordering row (rule 5, AR §18.1).** A +20 max HP node on a 140 class is a live
        // maximum of 160, and the save was written at 150 of 160. Restore the nodes first and the
        // hit points come back at 150; restore health first and 150 is clamped against the class's
        // unmodified 140, so the player silently loses ten points once per resume — and the only
        // visible symptom is a bar slightly shorter than the one they put the phone down in front
        // of.
        StartResumed(
            stage: 4,
            Snapshot(
                4,
                _random.Seed,
                playerHp: SavedHpAboveBaseMax,
                takenNodeIds: new[] { new ContentId(NodeMaxHp) }));

        Assert.That(_session.State.PlayerMaxHp, Is.EqualTo(TreeMaxHp + MaxHpNodeBonus).Within(0.01f));

        Assert.That(
            _session.State.PlayerHp,
            Is.EqualTo(SavedHpAboveBaseMax).Within(0.01f),
            "Swap the two lines in RunSession.Start and this is 140 rather than 150.");

        // The fixture's own claims, checked out loud: the saved hit points have to be above the
        // class's own maximum and at or below the modified one, or the row proves nothing either
        // way.
        Assert.That(SavedHpAboveBaseMax, Is.GreaterThan(TreeMaxHp));
        Assert.That(SavedHpAboveBaseMax, Is.LessThanOrEqualTo(TreeMaxHp + MaxHpNodeBonus));
    }

    [Test]
    public void Start_BadTreeFailsBeforeRunStarted()
    {
        // A tree naming a node nobody authored. The class's tree is resolved and cross-checked in
        // Start's validation block (rule 1), so an authoring mistake refuses the run rather than
        // the pick — with nothing announced and nothing written (ledger row 3).
        BuildWithTree(seed: 7, treeNamesAStranger: true);

        Assert.Throws<KeyNotFoundException>(() => _session.Start(FreshConfig(stage: 4)));

        AssertNothingStands();
    }

    [Test]
    public void NoTree_ReadsAnswerEmpty()
    {
        Build(seed: 7);

        RunSnapshot naming = Snapshot(
            4,
            _random.Seed,
            takenNodeIds: new[]
            {
                new ContentId("skill.oathbound.bulwark"),
                new ContentId("skill.oathbound.consecrate"),
            });

        // **A class with no tree is legal until M3-12** (rule 10) — `TryGetTreeFor` is false for
        // every class this build ships. Two nodes named, neither of them content this build holds,
        // and nothing throws: there is no tree to apply them to, which is the state `RunSession`
        // was already in between M3-01b and here.
        Assert.DoesNotThrow(() => StartResumed(stage: 4, naming));

        Assert.That(_session.State.TakenNodeCount, Is.Zero);
        Assert.That(_session.State.IsTreeFull, Is.False);
        Assert.That(_session.State.TakenNodeIds, Is.Empty);
        Assert.That(_session.State.TakenNodeIds, Is.Not.Null, "Empty, never null — no reader has to ask.");

        Assert.That(_session.State.Level, Is.EqualTo(SavedLevel), "The rest of the restore still ran.");
        Assert.That(naming.TakenNodeIds, Has.Count.EqualTo(2), "The fixture named two, so the row is not vacuous.");
    }

    [Test]
    public void Start_RestoresRunTime()
    {
        Build(seed: 7);

        StartResumed(stage: 4);

        Assert.That(_session.State.Time, Is.EqualTo(SavedRunTime));
    }

    [Test]
    public void Start_RestoredRunTimeKeepsRunning()
    {
        Build(seed: 7);

        StartResumed(stage: 4);
        TickFor(60);

        // Simulated seconds carry on from where the save left them rather than restarting — a run
        // resumed at seven minutes is seven minutes old, which is what the next snapshot's RunTime
        // has to say and what M4's run summary will read.
        Assert.That(_session.State.Time, Is.GreaterThan(SavedRunTime));
        Assert.That(_session.State.Time, Is.EqualTo(SavedRunTime + (60f * Frame)).Within(0.01f));
    }

    [Test]
    public void Start_RestoreIsAppliedBeforeRunStarted()
    {
        Build(seed: 7);

        float seenFromTheHandler = float.NaN;
        float shieldFromTheHandler = float.NaN;
        int levelFromTheHandler = 0;
        int pendingFromTheHandler = -1;

        var watching = new WatchingEvents();
        RunSession session = SessionOver(watching);

        watching.On<RunStarted>(_ =>
        {
            seenFromTheHandler = session.State.PlayerHp;
            shieldFromTheHandler = session.State.PlayerShield;
            levelFromTheHandler = session.State.Level;
            pendingFromTheHandler = session.State.PendingLevelUps;
        });

        session.Start(ResumedConfig(stage: 4));

        // M1-17's HUD draws the bar it is told about from inside this handler. A restore applied
        // after the publish would show a resumed run a full bar that drops to 62 % on the next
        // frame, which is the one frame a resumed run does not get to be wrong in.
        Assert.That(
            seenFromTheHandler,
            Is.EqualTo(SavedHp),
            "A subscriber reading PlayerHp from RunStarted must already see the restored value.");

        Assert.That(shieldFromTheHandler, Is.EqualTo(SavedShield));

        // The same rule, for the same reason, for the three M3-01b added. M3-10b's XP strip and
        // level readout are drawn from inside this handler, so a progression restore applied after
        // the publish would show a resumed run level 1 with an empty bar for one frame — and M3-08's
        // flow, which acts on a pending pick, would read zero and show no screen at all.
        Assert.That(
            levelFromTheHandler,
            Is.EqualTo(SavedLevel),
            "A subscriber reading State.Level from RunStarted must already see the restored value.");

        Assert.That(pendingFromTheHandler, Is.EqualTo(SavedPending));
    }

    [Test]
    public void Start_FreshRunStartsAtFullHealth()
    {
        Build(seed: 7);

        _session.Start(FreshConfig(stage: 4));

        Assert.That(_session.State.PlayerHp, Is.EqualTo(MaxHp));
        Assert.That(_session.State.PlayerShield, Is.EqualTo(ShieldMax));
        Assert.That(_session.State.Time, Is.EqualTo(0f), "A fresh run has lasted no time at all.");
    }

    [Test]
    public void Start_FreshRunIsLevelOne()
    {
        Build(seed: 7);

        _session.Start(FreshConfig(stage: 4));

        // The other half of the restore rows: a config with no snapshot must not inherit anything,
        // and a run that begins deep is still a run that begins at level 1 — depth and level are
        // different numbers, and GD §4.5 lets a mode start at stage 7.
        Assert.That(_session.State.Level, Is.EqualTo(1));
        Assert.That(_session.State.Xp, Is.EqualTo(0f));
        Assert.That(_session.State.PendingLevelUps, Is.EqualTo(0));
    }

    // ---- The agreement (rule 4) -----------------------------------------------------------------

    [Test]
    public void Start_RestoreSeedDisagrees_Throws()
    {
        Build(seed: 7);

        var config = new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            seed: 7,
            stageIndex: 4,
            SpawnPlan.Empty,
            restore: Snapshot(stage: 4, seed: 8));

        Assert.Throws<ArgumentException>(() => _session.Start(config));

        AssertNothingStands();
    }

    [Test]
    public void Start_RestoreStageDisagrees_Throws()
    {
        Build(seed: 7);

        var config = new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            seed: 7,
            stageIndex: 4,
            SpawnPlan.Empty,
            restore: Snapshot(stage: 5, seed: 7));

        Assert.Throws<ArgumentException>(() => _session.Start(config));

        AssertNothingStands();
    }

    [Test]
    public void Start_RestoreOfAnUnauthoredMode_ThrowsFirst()
    {
        Build(seed: 7);

        // Both wrong at once: the mode is not in this build's catalog *and* the restore disagrees
        // about the stage. Validation runs first (rule 2), so the diagnostic must be the content
        // one — a player whose save names a mode we stopped shipping deserves to be told that,
        // not that two numbers do not match.
        var config = new RunConfig(
            new ContentId("mode.ghost"),
            new ContentId(OathboundId),
            seed: 7,
            stageIndex: 4,
            SpawnPlan.Empty,
            restore: Snapshot(stage: 5, seed: 7, modeId: "mode.ghost"));

        Assert.Throws<KeyNotFoundException>(() => _session.Start(config));

        AssertNothingStands();
    }

    // ---- What is rebuilt rather than read back (rule 5) ----------------------------------------

    [Test]
    public void Resume_LandsInTheSameArena()
    {
        Build(seed: 7);

        RunSnapshot snapshot = Snapshot(stage: 7, seed: 7);

        // Derived from the seed and the depth and never drawn (M2-11a rule 3), which is exactly
        // what lets a resumed run land in the room it left without a byte of it being saved. A
        // roster of two, so the answer is a choice rather than the only option.
        Assert.That(_mode.Arenas, Has.Count.EqualTo(2));

        Assert.That(
            _mode.ArenaFor(snapshot.StageIndex, snapshot.Seed),
            Is.EqualTo(_mode.ArenaFor(7, 7)));

        // And the run says the same thing out loud: the arena named on arrival at the resumed
        // stage is the one the pure function gives.
        StartResumed(stage: 7, snapshot: snapshot);

        StageArrived arrived = _events.Single<StageArrived>();

        Assert.That(arrived.Stage, Is.EqualTo(7));
        Assert.That(arrived.ArenaId, Is.EqualTo(_mode.ArenaFor(7, 7)));
    }

    [Test]
    public void Resume_ComposesTheSameStage()
    {
        // Run A: begun at stage 3, cleared, and walked through the door into stage 4.
        Build(seed: 4_242);
        _session.Start(FreshConfig(stage: 3));
        ClearTheStage();

        RunSnapshot boundary = _events.Of<RunSnapshotTaken>()[1].Snapshot;

        Assert.That(boundary.StageIndex, Is.EqualTo(4), "The boundary snapshot names the stage ahead.");

        CrossTheBoundary();
        TickFor(600);

        IReadOnlyList<string> continuous = BodiesOfTheLastStage();

        Assert.That(continuous, Is.Not.Empty, "The fixture failed to land stage 4's first wave.");

        // Run B: the same world, a generator put back where the snapshot says the streams stood —
        // which is what RunInstaller does — and a config carrying the snapshot as its restore.
        Build(seed: 4_242);
        _random.Restore(boundary.Random);

        _session.Start(ResumedConfig(stage: 4, snapshot: boundary));
        TickFor(600);

        Assert.That(
            BodiesOfTheLastStage(),
            Is.EqualTo(continuous),
            "A resumed run must fight the stage the interrupted one was about to fight — same "
                + "archetypes, same positions, in the same order. This is ledger row 1 through "
                + "RunConfig.Restore rather than through a hand-driven session.");
    }

    [Test]
    public void Resume_SpawnsAtTheSamePositions()
    {
        // The row above compares whole bodies, archetype and position together. This one is the
        // narrower claim on its own — the order and the coordinates — so that a failure says which
        // half moved.
        Build(seed: 4_242);
        _session.Start(FreshConfig(stage: 3));
        ClearTheStage();

        RunSnapshot boundary = _events.Of<RunSnapshotTaken>()[1].Snapshot;

        CrossTheBoundary();
        TickFor(600);

        List<Vector3> continuous = PositionsOfTheLastStage();

        Assert.That(continuous, Is.Not.Empty, "The fixture failed to land stage 4's first wave.");

        Build(seed: 4_242);
        _random.Restore(boundary.Random);
        _session.Start(ResumedConfig(stage: 4, snapshot: boundary));
        TickFor(600);

        Assert.That(PositionsOfTheLastStage(), Is.EqualTo(continuous));
    }

    [Test]
    public void Resume_StartsWithNoEnemies()
    {
        Build(seed: 7);

        StartResumed(stage: 4);

        // A boundary has no bodies by construction — M2-10 clears both systems there — so a
        // resumed run brings none back. Every enemy it sees arrives from the director's first
        // wave, which has not run yet on the frame Start returns.
        Assert.That(
            _events.Count<EnemySpawned>(),
            Is.Zero,
            "Nothing may be standing before the director's first wave: live enemies are the "
                + "largest thing the save format deliberately does not carry.");
    }

    [Test]
    public void Resume_DoesNotRewindTheStreams()
    {
        Build(seed: 7);

        RunSnapshot snapshot = Snapshot(stage: 4, seed: 7);

        // Where the fixture's generator stands before the run is built, and where it stands after.
        // Core is handed a generator already in position and must not touch its position itself —
        // if Start ever applied `snapshot.Random`, this row would see the opening capture move
        // back onto the snapshot's five words instead of staying on the fixture's.
        RandomState before = _random.Capture();

        Assert.That(
            before.Spawn,
            Is.Not.EqualTo(snapshot.Random.Spawn),
            "The fixture's generator and the snapshot must disagree, or this row proves nothing.");

        _session.Start(ResumedConfig(stage: 4, snapshot: snapshot));

        RunSnapshot opening = _events.Of<RunSnapshotTaken>()[0].Snapshot;

        Assert.That(
            opening.Random.Spawn,
            Is.EqualTo(before.Spawn),
            "The opening write records the generator core was handed, never the one the restore "
                + "describes. Rewinding is the composition root's job and core does not have the "
                + "door (M2-13a rule 7).");
    }

    // ---- Guards ---------------------------------------------------------------------------------

    [Test]
    public void Config_NullSpawnPlan_StillThrows()
    {
        // The sixth field did not move the fifth one's guard.
        Assert.Throws<ArgumentNullException>(() => new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            seed: 7,
            stageIndex: 1,
            spawnPlan: null,
            restore: null));
    }

    [Test]
    public void Config_DefaultRestore_IsRefusedAtStart()
    {
        Build(seed: 7);

        // `default(RunSnapshot)` carries version 0, seed 0 and stage 0 — the form no writer can
        // produce. It cannot reach `RunConfig`'s constructor as a *validated* snapshot, so the
        // thing that refuses it is the agreement check, on the stage.
        var config = new RunConfig(
            new ContentId(ModeId),
            new ContentId(OathboundId),
            seed: 7,
            stageIndex: 4,
            SpawnPlan.Empty,
            restore: default(RunSnapshot));

        Assert.Throws<ArgumentException>(() => _session.Start(config));

        AssertNothingStands();
    }

    // ---- Fixture --------------------------------------------------------------------------------

    /// <summary>A catalog, a generator, a clock and a session, all over one seed.</summary>
    private void Build(int seed)
    {
        _events = new RecordingEvents();

        // Long enough that no row here can exhaust the script and start reading the fake's default
        // 0.5f, which would make every position after that point stop moving.
        _random = new FixedRandom(seed, Alternating(8_192));
        _clock = new FixedClock(Instant);
        _mode = Mode();

        _catalog = new ContentCatalog(new[] { Oathbound() }, new[] { Husk() }, new[] { _mode });

        _session = SessionOver(_events);
    }

    /// <summary>
    /// The same world with a tree for the class, and a 140 HP Oathbound to hang the ordering row's
    /// arithmetic on.
    /// </summary>
    /// <param name="treeNamesAStranger">
    /// Leaves one of the tree's node ids unauthored, for the row about an authoring mistake being
    /// caught at <c>Start</c>.
    /// </param>
    private void BuildWithTree(int seed, bool treeNamesAStranger = false)
    {
        _events = new RecordingEvents();
        _random = new FixedRandom(seed, Alternating(8_192));
        _clock = new FixedClock(Instant);
        _mode = Mode();

        IReadOnlyList<SkillSpec> skills = treeNamesAStranger
            ? new[] { Passive(NodeDamage, 0.15f), Passive(NodeB, 0.05f), Passive(NodeC, 0.05f) }
            : TreeSkills();

        _catalog = new ContentCatalog(
            new[] { Oathbound(TreeMaxHp) },
            new[] { Husk() },
            new[] { _mode },
            skills,
            new[] { Tree() });

        _session = SessionOver(_events);
    }

    /// <summary>
    /// The same world with a tree whose branch 0 holds <paramref name="actives"/> Actives beside the
    /// two passives the node rows use — M3-06's runner needs a tree that can hand it something.
    /// </summary>
    /// <remarks>
    /// A tree of its own rather than an Active added to <see cref="Tree"/>, so that every row above
    /// keeps the shape it was written against: a fourth node in branch 0 would move
    /// <c>Available</c>'s count under rows that are not about it.
    /// </remarks>
    private void BuildWithActiveTree(int seed, int actives = 1)
    {
        _events = new RecordingEvents();
        _random = new FixedRandom(seed, Alternating(8_192));
        _clock = new FixedClock(Instant);
        _mode = Mode();

        var skills = new List<SkillSpec> { Passive(NodeDamage, 0.15f), Passive(NodeB, 0.05f) };
        var tier = new List<ContentId>();

        for (int i = 0; i < actives; i++)
        {
            // The first keeps the name the rows quote; the rest only exist to fill the tree.
            string id = i == 0 ? NodeActive : $"{NodeActive}.{i}";

            skills.Add(ActiveNode(id));
            tier.Add(new ContentId(id));
        }

        tier.Add(new ContentId(NodeDamage));

        var tree = new SkillTreeSpec(
            new ContentId(TreeIdValue),
            new ContentId(OathboundId),
            new[]
            {
                new SkillBranchSpec(
                    new LocKey("branch.a"),
                    new IReadOnlyList<ContentId>[] { tier }),
                Branch('b', new[] { NodeB }),
                Branch('c', new[] { NodeC }),
            });

        skills.Add(Passive(NodeC, 0.05f));

        _catalog = new ContentCatalog(
            new[] { Oathbound(TreeMaxHp) },
            new[] { Husk() },
            new[] { _mode },
            skills,
            new[] { tree });

        _session = SessionOver(_events);
    }

    /// <summary>
    /// An Active whose trigger always holds, so the boundary row can put it on cooldown with one
    /// tick rather than by arranging a fight.
    /// </summary>
    private static SkillSpec ActiveNode(string id) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Active,
        Array.Empty<IEffect>(),
        new ActiveSpec(
            ActiveCooldown,
            new TriggerSpec(new[]
            {
                // Below 1.5 rather than a clause that reads as "always": HpFraction is at most 1,
                // so this holds on every tick of a healthy run and on every tick of a hurt one.
                new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 1.5f),
            }),
            new IEffect[]
            {
                new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.5f),
            }));

    private RunSession SessionOver(IDomainEvents events) => new RunSession(
        _catalog,
        _random,
        events,
        new RecordingIntents(),
        new RunRecorder(_random, _clock, events),
        Capacity,
        DeviceCap,
        ProjectileCapacity);

    private void StartResumed(int stage, RunSnapshot? snapshot = null)
    {
        _session.Start(ResumedConfig(stage, snapshot));
    }

    /// <summary>
    /// The three ids <c>BuildWithActiveTree(seed, actives: 3)</c> authors, in the order it authors
    /// them — what the slot rows hand to <c>takenNodeIds</c>.
    /// </summary>
    private static ContentId[] ThreeActives() => new[]
    {
        new ContentId(NodeActive),
        new ContentId(NodeActive + ".1"),
        new ContentId(NodeActive + ".2"),
    };

    private RunConfig ResumedConfig(int stage, RunSnapshot? snapshot = null) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        _random.Seed,
        stage,
        SpawnPlan.Empty,
        snapshot ?? Snapshot(stage, _random.Seed));

    private RunConfig FreshConfig(int stage) => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        _random.Seed,
        stage,
        SpawnPlan.Empty,
        restore: null);

    private static RunSnapshot Snapshot(
        int stage,
        int seed,
        string modeId = ModeId,
        int level = SavedLevel,
        float xp = SavedXp,
        int pendingLevelUps = SavedPending,
        IReadOnlyList<ContentId> takenNodeIds = null,
        float playerHp = SavedHp,
        IReadOnlyList<ContentId> manualSkillIds = null) =>
        new RunSnapshot(
            RunSnapshot.CurrentVersion,
            new ContentId(modeId),
            new ContentId(OathboundId),
            seed,
            stage,
            new RandomState(101, 102, 103, 104, 105),
            playerHp,
            SavedShield,
            SavedRunTime,
            Instant,
            level,
            xp,
            pendingLevelUps,
            takenNodeIds ?? Array.Empty<ContentId>(),
            manualSkillIds ?? new ContentId[SkillRunner.MaxManualSlots]);

    private void TickFor(int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            _session.Tick(Snapshot(Frame, Vector3.Zero));
        }
    }

    private static WorldSnapshot Snapshot(float dt, Vector3 playerPosition) =>
        new WorldSnapshot(Capacity)
        {
            Dt = dt,
            PlayerPosition = playerPosition,
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

    /// <summary>
    /// Plays the whole stage out — every body of every wave — and stops on the tick
    /// <c>StageCleared</c> lands. <c>RunRecorderTests</c>' helper, for its reasons: a session's
    /// <c>PlayerCombat</c> and <c>EnemySystem</c> are both <c>internal</c>, so the only route to a
    /// kill is walking the player onto a body and answering its swing the way Unity would.
    /// </summary>
    private void ClearTheStage()
    {
        int target = _events.Count<StageCleared>() + 1;
        int killed = _events.Count<EnemyDied>();

        var report = new int[1];

        for (int i = 0; i < 9_000 && _events.Count<StageCleared>() < target; i++)
        {
            IReadOnlyList<EnemySpawned> spawned = _events.Of<EnemySpawned>();

            bool standing = spawned.Count > killed;
            Vector3 where = standing ? spawned[killed].Position : Vector3.Zero;

            if (standing)
            {
                report[0] = spawned[killed].Id;
            }

            _session.Tick(Snapshot(Frame, where));

            if (!standing || !_session.IsRunning)
            {
                continue;
            }

            _session.ReportConeHits(report);

            if (_events.Count<EnemyDied>() > killed)
            {
                killed++;
            }
        }

        Assert.That(
            _events.Count<StageCleared>(),
            Is.GreaterThanOrEqualTo(target),
            "The fixture failed to clear the stage, so whatever this row asserts next is about the "
                + "fixture rather than about the run.");
    }

    /// <summary>Waits out the clear beat, walks into the door and lets the fade run out.</summary>
    private void CrossTheBoundary()
    {
        int arrivals = _events.Count<StageArrived>();

        for (int i = 0; i < 900 && _events.Count<StageArrived>() == arrivals; i++)
        {
            _session.Tick(Snapshot(Frame, Door));
        }

        Assert.That(
            _events.Count<StageArrived>(),
            Is.GreaterThan(arrivals),
            "The fixture failed to cross the boundary, so whatever this row asserts next is about "
                + "the fixture rather than about the run.");
    }

    /// <summary>
    /// Every body spawned since the most recent <c>StageArrived</c>, as archetype-and-position
    /// strings — what a wave plan turns into, which is the only part of it a session lets a test
    /// see.
    /// </summary>
    private IReadOnlyList<string> BodiesOfTheLastStage()
    {
        var bodies = new List<string>();

        int arrivals = 0;

        foreach (object published in _events.All)
        {
            if (published is StageArrived)
            {
                arrivals++;
                bodies.Clear();
                continue;
            }

            if (published is EnemySpawned spawned)
            {
                bodies.Add($"{spawned.SpecId} @ {spawned.Position}");
            }
        }

        Assert.That(arrivals, Is.GreaterThan(0), "No stage ever arrived.");

        return bodies;
    }

    /// <summary>The same walk, positions only.</summary>
    private List<Vector3> PositionsOfTheLastStage()
    {
        var positions = new List<Vector3>();

        foreach (object published in _events.All)
        {
            if (published is StageArrived)
            {
                positions.Clear();
                continue;
            }

            if (published is EnemySpawned spawned)
            {
                positions.Add(spawned.Position);
            }
        }

        return positions;
    }

    private void AssertNothingStands()
    {
        Assert.That(_session.IsRunning, Is.False, "A refused Start leaves the session not running.");

        Assert.That(
            _events.Count<RunStarted>(),
            Is.Zero,
            "Nothing is announced when Start refuses — ledger row 3's rule, which the restore "
                + "check joins by sitting inside the same validation block.");

        Assert.That(_events.Count<RunSnapshotTaken>(), Is.Zero, "And nothing is written down.");
    }

    // ---- Content ---------------------------------------------------------------------------------

    /// <summary>
    /// GD §12's curves as the game ships them, so stage 4 is several bodies of a real composition:
    /// two runs agreeing about one Husk would not be evidence that a stream was restored.
    /// </summary>
    private static ModeSpec Mode()
    {
        var scaling = new ScalingSpec(
            new BudgetCurve(20f, 6f, 0.04f),
            new WaveCurve(2, 1000, 2, 2),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0));

        return new ModeSpec(
            new ContentId(ModeId),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            scaling,
            Scalings.Xp(),
            new[] { new RosterEntry(new ContentId(HuskId), 1) },
            new[] { new ContentId(FirstArenaId), new ContentId(SecondArenaId) });
    }

    /// <summary>
    /// The Husk, authored <c>Static</c> on purpose: these rows are about where a body lands and
    /// when, and a Chaser would walk away from the position it was composed at.
    /// </summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: HuskCost,
        xpValue: HuskCost * 3f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    /// <summary>CC §7's class, plus the Aegis these rows restore half of.</summary>
    /// <param name="maxHp">
    /// The class's hit points. Parameterised only so the tree rows can play a 140 HP Oathbound —
    /// every other row wants <see cref="MaxHp"/>, which is deliberately not a value any row
    /// restores to.
    /// </param>
    private static CharacterSpec Oathbound(float maxHp = MaxHp) => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        maxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),

        // The Focus ramp is switched off with a maximum of 1, for RunSessionTests' reason: it
        // would put a modifier and a stream of events into a fixture measuring neither.
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        new ShieldSpec(ShieldMax, 3f, 1f));

    /// <summary>
    /// A tree of three branches, with three nodes sharing branch 0's only tier so that every one of
    /// them is available on the first pick and any order of them is a legal take order.
    /// </summary>
    private static SkillTreeSpec Tree() => new SkillTreeSpec(
        new ContentId(TreeIdValue),
        new ContentId(OathboundId),
        new[]
        {
            Branch('a', new[] { NodeMaxHp, NodeDamage, NodeSpare }),
            Branch('b', new[] { NodeB }),
            Branch('c', new[] { NodeC }),
        });

    private static IReadOnlyList<SkillSpec> TreeSkills() => new[]
    {
        MaxHpNode(NodeMaxHp),
        Passive(NodeDamage, 0.15f),
        Passive(NodeSpare, 0.05f),
        Passive(NodeB, 0.05f),
        Passive(NodeC, 0.05f),
    };

    private static SkillBranchSpec Branch(char letter, string[] tier)
    {
        var ids = new ContentId[tier.Length];

        for (int i = 0; i < tier.Length; i++)
        {
            ids[i] = new ContentId(tier[i]);
        }

        return new SkillBranchSpec(
            new LocKey($"branch.{letter}"),
            new IReadOnlyList<ContentId>[] { ids });
    }

    /// <summary>A flat <c>+20 max HP</c> node — the one the ordering row is about.</summary>
    /// <remarks>
    /// <c>Flat</c> rather than a percentage, so the expected maximum is 160 exactly and the row
    /// reads as arithmetic rather than as a tolerance.
    /// </remarks>
    private static SkillSpec MaxHpNode(string id) => Node(
        id,
        new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, MaxHpNodeBonus));

    private static SkillSpec Passive(string id, float damagePercent) => Node(
        id,
        new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, damagePercent));

    private static SkillSpec Node(string id, IEffect effect) => new SkillSpec(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Passive,
        new[] { effect });

    /// <summary><paramref name="count"/> points on a ring, clear of the origin and of each other.</summary>
    private static IReadOnlyList<Vector3> Points(int count)
    {
        var points = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            double angle = 2d * Math.PI * i / count;

            points[i] = new Vector3((float)(Ring * Math.Cos(angle)), 0f, (float)(Ring * Math.Sin(angle)));
        }

        return points;
    }

    /// <summary><paramref name="count"/> values alternating between the first and last candidate.</summary>
    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }

    /// <summary>
    /// A <see cref="IDomainEvents"/> that records and also hands each payload to whoever asked for
    /// that type — a subscriber, without a hub, for the one row that reads state from inside a
    /// publish. <c>DomainEventHub</c> is the real thing and lives in <c>Soulvail.Game</c>, which
    /// this assembly does not reference and must not.
    /// </summary>
    private sealed class WatchingEvents : IDomainEvents
    {
        private readonly RecordingEvents _log = new RecordingEvents();
        private readonly Dictionary<Type, Action<object>> _handlers = new Dictionary<Type, Action<object>>();

        public void On<T>(Action<T> handler)
            where T : struct
        {
            _handlers[typeof(T)] = payload => handler((T)payload);
        }

        public void Publish<T>(in T evt)
            where T : struct
        {
            _log.Publish(in evt);

            if (_handlers.TryGetValue(typeof(T), out Action<object> handler))
            {
                handler(evt);
            }
        }
    }
}
