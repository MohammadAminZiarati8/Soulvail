using System;
using System.Collections.Generic;
using System.Numerics;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Save;
using Soulvail.Core.Stage;

namespace Soulvail.Core.Run;

/// <summary>
/// The brain. Given where everything is, once a frame, it decides what the player does and says
/// so — through an intent for the body and events for everyone else. The only implementation of
/// <see cref="IRunSession"/>, and the one object Unity holds for the length of a run. See AR §3,
/// §4.1 and §7.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately tiny in M0: a run is one character moving. Everything a run will grow — health,
/// targeting, weapons (M1), stages and a director (M2), levels and a tree (M3) — is composed in
/// here as it lands, and each addition is a field ticked from <see cref="Tick"/>, never a second
/// entry point. The port stays <c>Start</c> / <c>Tick</c> / <c>End</c> plus the facts M1 brings
/// (AR §6).
/// </para>
/// <para>
/// No <c>IClock</c>. Simulated time is the sum of each tick's <c>Dt</c>, which rides in on the
/// snapshot, so a clock here would be a second answer to "how much time has passed" — and the
/// wrong one, since it would keep running while the game is paused or backgrounded. AR §7's
/// sketch listed one; M0-10 built the class without it and §7 now says so. Wall-clock arrives
/// with M2-01, for persistence, which is a different question asked by different code.
/// </para>
/// <para>
/// Dependencies come in through the constructor and are never resolved from anywhere: no
/// statics, no locator, no <c>[Inject]</c> attributes in core. <c>RunInstaller</c> (M0-12) builds
/// this and <c>RunScope</c> owns its lifetime.
/// </para>
/// </remarks>
public sealed class RunSession : IRunSession, IPlayerCommands, IProgressionCommands
{
    private readonly ContentCatalog _catalog;
    private readonly IRandom _random;
    private readonly IDomainEvents _events;
    private readonly IIntentSink _intents;
    private readonly RunRecorder _recorder;
    private readonly int _enemyCapacity;
    private readonly int _deviceEnemyCap;
    private readonly int _projectileCapacity;

    /// <summary>
    /// What turns the plan into enemies in an arena. Never null while a run is running: an arena
    /// with nowhere to put anything gets an inert one rather than none (M2-05 rule 12), so nothing
    /// downstream has to ask whether this run has a director.
    /// </summary>
    private SpawnDirector _director;

    /// <summary>
    /// The stage the run is in the middle of, and what ends it. Null for a mode whose content comes
    /// entirely from its spawn plan — there is nothing to compose, so there are no waves to pace.
    /// </summary>
    /// <remarks>
    /// Nullable where <see cref="_director"/> is not, and the asymmetry is deliberate: an inert
    /// director is still a director, because "no spawn points" is a property of the <em>arena</em>
    /// and every arena has one. An empty roster is a property of the <em>mode</em>, and a mode with
    /// no waves has no stage to pace through — <c>StageFlow</c> would be holding a plan nothing ever
    /// composed into.
    /// </remarks>
    private StageFlow _flow;

    /// <summary>
    /// What a cast put on the player that has to come off again, and the clock it comes off on.
    /// </summary>
    /// <remarks>
    /// Here rather than on <c>RunState</c> because nothing reads it: it is ticked by this class and
    /// called back through <c>EffectRegistry.Remove</c>, and what a view wants to know about a timed
    /// grant arrives as <c>ShieldGranted</c> and <c>ShieldGrantExpired</c> rather than as a read
    /// (AR §18.2 — a twelfth scalar was the last thing that block gained, and this is not a
    /// thirteenth).
    /// </remarks>
    private TimedEffects _timed;

    /// <summary>
    /// This run's simulated seconds, for the one handler that needs them. See
    /// <see cref="SimulatedClock"/> for why it exists at all rather than being a parameter.
    /// </summary>
    private SimulatedClock _clock;

    /// <summary>
    /// A dash was in flight as of the previous tick. The edge <see cref="Tick"/> needs to know when
    /// to hand movement back to the stick — see the remarks there.
    /// </summary>
    /// <remarks>
    /// Here rather than on <c>PlayerCombat</c> because it is about the motor, and the motor is this
    /// object's to tick. <c>PlayerCombat</c> watches the other edge of the same dash, the one where
    /// the i-frames lapse, and the two are deliberately not the same moment.
    /// </remarks>
    private bool _wasCharging;

    /// <param name="catalog">Where the config's mode, class and every archetype it names are resolved.</param>
    /// <param name="random">
    /// The run's generator. Its <see cref="IRandom.Seed"/> is no longer what the state records —
    /// the config states that — but <see cref="Start"/> refuses a config that disagrees with it.
    /// </param>
    /// <param name="events">Where run lifecycle events go.</param>
    /// <param name="intents">Where each tick's <see cref="PlayerMoveIntent"/> is written.</param>
    /// <param name="recorder">
    /// What writes the run down at the two moments a resume can be built from (M2-14a). Injected
    /// rather than constructed here, and that is rule 12 rather than a preference: a recorder holds
    /// the wall clock, so a session that built its own would have to take an <see cref="IClock"/> —
    /// which is the one dependency <c>Soulvail.Core.Run</c> is asserted not to have (AR §18.2).
    /// </param>
    /// <param name="enemyCapacity">
    /// The most enemies a run may hold at once, for the <see cref="EnemySystem"/> each
    /// <see cref="Start"/> builds. It must be the number the <c>WorldSnapshot</c> was built with,
    /// which is why both come from one constant in <c>BootInstaller</c>: an enemy core knows about
    /// but the snapshot cannot carry is one core is blind to the position of.
    /// </param>
    /// <param name="deviceEnemyCap">
    /// The most enemies this device may have standing at once — GD §11.1's tier, not a difficulty
    /// number. It bounds GD §12.2's concurrency curve, so it decides how many bodies a stage is
    /// delivered in and therefore how much of its budget is spent on <em>quality</em> instead
    /// (GD §11.2, <see cref="WaveComposer"/>). A separate argument from
    /// <paramref name="enemyCapacity"/> and always smaller: that one is how many enemies the
    /// snapshot can carry, this one is how many the phone can draw.
    /// </param>
    /// <param name="projectileCapacity">
    /// The most shots that may be in the air at once, for the <see cref="ProjectileSystem"/> each
    /// <see cref="Start"/> builds. From <c>BootInstaller</c> beside the enemy cap, because it is the
    /// same kind of number — what this device is allowed to have happening at once — and
    /// deliberately not derived from the enemy cap: a shot outlives its shooter, so the two counts
    /// are not the same question.
    /// </param>
    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="enemyCapacity"/>, <paramref name="deviceEnemyCap"/> or
    /// <paramref name="projectileCapacity"/> is not positive, or the cap exceeds the capacity — an
    /// arena allowed to hold more bodies than the snapshot can carry is an arena core would be blind
    /// to part of.
    /// </exception>
    /// <remarks>
    /// Guarded, where <see cref="RunState"/>'s constructor is not, and the difference is the
    /// boundary: this one is public and called from another assembly, so a null arrives from code
    /// core cannot see. Without the guards a missing <paramref name="intents"/> would surface a
    /// frame later as a <see cref="NullReferenceException"/> inside <see cref="Tick"/>, pointing
    /// at the tick rather than at the registration that forgot it. The capacity is checked here
    /// rather than at the first <see cref="Start"/>, so a mis-wired scope fails while it is being
    /// built rather than one scene later.
    /// </remarks>
    public RunSession(
        ContentCatalog catalog,
        IRandom random,
        IDomainEvents events,
        IIntentSink intents,
        RunRecorder recorder,
        int enemyCapacity,
        int deviceEnemyCap,
        int projectileCapacity)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));

        if (enemyCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(enemyCapacity),
                enemyCapacity,
                "enemyCapacity must be greater than zero.");
        }

        if (deviceEnemyCap <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceEnemyCap),
                deviceEnemyCap,
                "deviceEnemyCap must be greater than zero. A device allowed no enemies is one "
                    + "every stage composes an empty arena for.");
        }

        if (deviceEnemyCap > enemyCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceEnemyCap),
                deviceEnemyCap,
                $"deviceEnemyCap is {deviceEnemyCap} and enemyCapacity is {enemyCapacity}. A "
                    + "stage may not be allowed more bodies than the snapshot can carry back — "
                    + "the surplus would exist in core and be invisible to it.");
        }

        if (projectileCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projectileCapacity),
                projectileCapacity,
                "projectileCapacity must be greater than zero. A run allowed no shots in the air "
                    + "is one whose Spitters fire in silence — Fire refuses every one of them and "
                    + "says nothing, which is exactly the failure a playtest cannot see.");
        }

        _enemyCapacity = enemyCapacity;
        _deviceEnemyCap = deviceEnemyCap;
        _projectileCapacity = projectileCapacity;
    }

    /// <inheritdoc />
    public bool IsRunning { get; private set; }

    /// <inheritdoc />
    public RunState State { get; private set; }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A run is already running.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The catalog holds no mode or class with the config's ids, or the plan or the mode's roster
    /// names an archetype nobody authored.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The config's seed disagrees with the generator this session was built with.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The mode has no stage <c>config.StageIndex</c>.
    /// </exception>
    /// <remarks>
    /// The order is the contract, and the first half of it is new in M2-02: resolve the mode,
    /// resolve the class, check the seed, check the stage, resolve every archetype the run could
    /// possibly need — and only then build the state, publish <c>RunStarted</c>, flip
    /// <see cref="IsRunning"/> and spawn. Everything from <c>RunStarted</c> onwards is unchanged
    /// and is asserted by a test (AR §18.1).
    /// </remarks>
    public void Start(RunConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (IsRunning)
        {
            throw new InvalidOperationException(
                "A run is already running. End it before starting another.");
        }

        // Everything below this line and above `new RunState` is validation, and the whole block
        // runs before a single thing is assigned or announced (ledger row 3). An unauthored mode,
        // class or archetype therefore leaves the session exactly as it was: not running, no
        // event published, nothing standing, and whatever State the previous run left still
        // readable. Until M2-02 the plan was spawned *after* RunStarted and after IsRunning
        // flipped, so a stranger mid-plan threw with the run announced and half an arena alive —
        // inert while only RunScope authored a plan, and mode data is the first thing that can.
        ModeSpec mode = _catalog.Mode(config.ModeId);

        CharacterSpec character = _catalog.Character(config.CharacterId);

        // One truth, checked at the one place both are visible. The config states the seed and the
        // generator was built from one, so the only way they can differ is a composition mistake —
        // and the symptom of letting it through would be a run whose recorded seed does not replay
        // it, which is the one number a bug report is worth having. Core does not reseed the
        // generator to make them agree: that would be an IRandom.Reseed, widening a port ahead of
        // its caller (AR §6), and it is half of what restoring stream state rides on — M2-13a
        // weighs it with ledger row 1.
        if (config.Seed != _random.Seed)
        {
            throw new ArgumentException(
                $"config.Seed is {config.Seed} but the run's generator was seeded with "
                    + $"{_random.Seed}. A run has one seed; the composition root builds the "
                    + "generator and states the same number here.",
                nameof(config));
        }

        // Asked of the mode, because whether stage 6 exists is the mode's question — an endless
        // Descent has every stage from 1, a finite mode does not. RunConfig already refused a
        // stage below 1 without needing to see a mode.
        if (!mode.HasStage(config.StageIndex))
        {
            throw new ArgumentOutOfRangeException(
                nameof(config),
                config.StageIndex,
                $"'{mode.Id}' has no stage {config.StageIndex}. It runs from "
                    + $"{mode.StartingStage} to "
                    + (mode.IsEndless ? "endless." : $"{mode.FinalStage}."));
        }

        // The third question of the same shape as the two above, and the last one this method can
        // ask before it starts building: the config states a seed and a depth, the snapshot states
        // the ones it was written at, and a resume is only a resume if they are the same run. A
        // mismatch is a composition mistake — the Menu handing over one run's snapshot with another
        // run's seed — and letting it through would put the streams on the right sequence at the
        // wrong position, or replay a stage at a depth it was never composed for. Neither has a
        // symptom that points here.
        //
        // After the content checks and before anything is built, so an unauthored mode named by an
        // old save still fails with the catalog's diagnostic rather than with this one: the
        // question "does this build still ship that mode" comes before "do these two agree".
        if (config.Restore is RunSnapshot restore)
        {
            if (restore.Seed != config.Seed)
            {
                throw new ArgumentException(
                    $"config.Restore was written for seed {restore.Seed} but this config states "
                        + $"{config.Seed}. A resumed run continues one run, and the snapshot's "
                        + "seed is the one the generator must have been built from.",
                    nameof(config));
            }

            if (restore.StageIndex != config.StageIndex)
            {
                throw new ArgumentException(
                    $"config.Restore resumes at stage {restore.StageIndex} but this config starts "
                        + $"at {config.StageIndex}. The saved depth is the depth a resumed run "
                        + "begins at; nothing may start it somewhere else.",
                    nameof(config));
            }
        }

        RequireAuthored(config.SpawnPlan, mode);

        // The class's tree, resolved and cross-checked here rather than at the moment a player is
        // offered a node (M3-03 rule 1). TreeRules asks everything a spec constructor could not:
        // that every id in the tree is a skill somebody authored, that a keystone ends its branch
        // alone, and that an upgrade's parent sits below it in the same branch. All three are
        // author-time mistakes, so all three belong in this block — reported with nothing announced
        // and nothing standing, rather than as a crash in a run (ledger row 3, AR §18.1).
        //
        // **Null is the ordinary answer until M3-12** (rule 10): TryGetTreeFor is false for every
        // class this build ships, and `_flow` below is the precedent for a run holding null where
        // there is nothing to compose. What is deliberately *not* here is the effect sweep — that
        // needs the registry, which cannot exist before the live objects it addresses, so it runs
        // further down where the SkillTree itself is built. Still before RunStarted, which is what
        // the rule actually asks for.
        TreeRules treeRules = null;

        if (_catalog.TryGetTreeFor(config.CharacterId, out SkillTreeSpec treeSpec))
        {
            treeRules = new TreeRules(treeSpec, _catalog);
        }

        // The last of the validation that can be asked before the run's objects exist, and it is
        // here for ledger row 3's reason rather than for tidiness: WaveComposer refuses a mode that
        // introduces nothing at or before this stage,
        // and composing after RunStarted would strand exactly the announcement that row exists to
        // stop being stranded. Held in locals until the run is built, so a throw below still
        // leaves this session's own fields as the previous run left them.
        //
        // A composition that succeeds and a Start that then fails would leave the Spawn stream
        // advanced. That is accepted: the run it was drawn for does not exist, and the alternative
        // — duplicating the composer's eligibility rule here so the real call can happen later —
        // is two copies of one rule, which is how they come to disagree.
        // **Before the composition below, and this line is the whole of M2-14a rule 5.** The
        // opening snapshot has to describe the streams as they stood *before* anything drew for the
        // stage it names, because a resume restores this position and composes that same stage from
        // it — a position read after the composition would deal the resumed run a different opening
        // stage under the same number, which is ledger row 1's bug with the numbers swapped.
        //
        // It is read here and announced much further down rather than both at once, because those
        // are two different moments and cannot be made one: the composition sits inside this
        // validation block so that an ineligible mode throws with nothing announced (ledger row 3,
        // AR §18.1), while a snapshot must not be announced before the run it belongs to is. The
        // gap between the two is exactly the composition the snapshot must not include.
        RandomState opening = _random.Capture();

        WavePlan plan = null;
        WaveComposer composer = null;

        // An empty roster is legal (M2-02): a mode whose content comes entirely from its spawn
        // plan has nothing to schedule, so it is not composed at all rather than composed into
        // nothing — which is what WaveComposer refuses, loudly and correctly.
        if (mode.Roster.Count > 0)
        {
            // Sized once for the whole run, at the wave curve's ceiling by the mode's roster
            // length, because M2-10 recomposes into this same object at every stage boundary and a
            // transition is the worst moment in a run to allocate (WavePlan's own reasoning).
            plan = new WavePlan(mode.Scaling.Waves.Max, mode.Roster.Count);

            composer = new WaveComposer(_catalog, new ThreatBudget(mode.Scaling, _deviceEnemyCap));

            // The first thing in a run to consume the Spawn stream, and it draws from that one and
            // no other (ADR-0011).
            composer.Compose(config.StageIndex, mode, plan, _random.Spawn);
        }

        int seed = config.Seed;

        // +Z, because a run begins with the camera behind the character and nothing yet to aim
        // at. The first stick input turns them within a frame or two at 720°/s.
        var motor = new PlayerMotor(character.Movement, Vector3.UnitZ);

        // The same capacity the registry and the snapshot use, from the same constant, because the
        // candidate buffer it preallocates has to be able to hold every enemy the run may spawn.
        // It gets the intent sink because a damage frame is a question for the body, and the
        // question has to leave on the tick that produced it rather than be relayed through here.
        var combat = new PlayerCombat(character, _events, _intents, _enemyCapacity);

        // One per run, not one per session: End leaves the finished registry readable and a second
        // Start must not inherit the first run's enemies, ids or free list. It takes the run's
        // generator because respawning draws a position (M1-19) — from the Spawn stream and no
        // other, which is the system's own rule to keep rather than this class's to enforce.
        //
        // The scaling is built here and handed straight in, and nothing else in the run holds one:
        // the curves belong to the mode, the mode is resolved above, and the only thing that ever
        // applies them is a spawn. A run's depth starts at the config's stage — a fresh run reads
        // it from the mode's StartingStage, a resumed one from the save (M2-14b) — and M2-10 moves
        // it at each boundary.
        var enemies = new EnemySystem(
            _catalog,
            _events,
            _random,
            new DepthScaling(mode.Scaling),
            _enemyCapacity)
        {
            Depth = config.StageIndex,
        };

        // One per run, like the registry above and for the same reason: a second Start must not
        // inherit the first run's shots or its ids. It takes no generator, because a shot goes
        // exactly where it was aimed and spread would be a change to what a seed means (ADR-0011).
        var projectiles = new ProjectileSystem(_events, _projectileCapacity);

        // One per run, like the two above: a second Start must not inherit the first run's level.
        // The curve comes off the mode rather than off the character or a constant here, because
        // levelling pace is the mode's statement about itself (GD §4.5) — the same argument that
        // put the difficulty curves there in M2-03. It is built at level 1 and a resumed run is put
        // back where it was in the restore block below, before RunStarted.
        var progression = new LevelTracker(mode.Xp, _events);

        // After the objects exist and before RunStarted, which is the whole of M3-05's placement
        // rule: the address table holds this run's live stats, so it cannot be built before them,
        // and the tree that will read the registry (M3-03) validates every effect it holds at
        // Start — so an unregistered primitive has to be reportable before the run is announced.
        //
        // One Register line per primitive, and that is the entire cost of adding the eleventh
        // (ADR-0009). M3-03's tree below is the first thing in a live run to call Apply — until it
        // existed this was a table that was built, filled and never read.
        var playerStats = new PlayerStats(combat, motor, progression);
        var effects = new EffectRegistry();

        effects.Register<ModifyStat>(new ModifyStatHandler(playerStats));

        // One per run like everything above, and the clock with them: a second Start must not
        // inherit the first run's held effects, and a clock that carried the last run's seconds
        // would expire the first grant of this one on the tick it was cast. The clock is written
        // once a tick beside State.Time and read by the handler, because IEffectHandler<T>.Apply is
        // handed no clock and widening that signature would change every handler in the game
        // (M3-11a-ii, correction 2).
        _clock = new SimulatedClock();
        _timed = new TimedEffects(effects);

        effects.Register<GrantShield>(
            new GrantShieldHandler(combat.Health, _timed, _clock, _events));

        // And one per run again, with the same clock. It heals the player's Health and reads the
        // player's position off the blackboard PlayerCombat fills — borrowed, not owned, one writer
        // and many readers (ADR-0005), and this is its second reader after the runner.
        //
        // Here rather than on this class like _timed because something *does* read it: an overlay and
        // M3-11c's decal ask how many zones are standing and where, so it hangs off RunState behind
        // two narrow reads (AR §18.2).
        var zones = new ZoneSystem(combat.Health, combat.Blackboard, _events);

        // The promise TimedEffects was built to keep, called in one task later: a new primitive is one
        // file and one Register line, with nothing in the clock, the registry or Tick changing to
        // admit it — and this one needs *less* than a grant, because a zone is never held (rule 9).
        effects.Register<SpawnHealZone>(new SpawnHealZoneHandler(zones, _clock));

        // The other half of the tree's validation, and the reason it is down here rather than up in
        // the block with TreeRules: the constructor asks CanApply of every take and cast effect in
        // the tree, and the registry it asks cannot exist before the live objects its handlers
        // address. Still before RunStarted and before anything is assigned to State, which is what
        // rule 4 asks for — a node whose primitive nobody registered refuses the run rather than
        // throwing part way through a Take.
        //
        // The one cost of the split is that a bad effect is reported after the opening composition
        // has drawn, so the Spawn stream is left advanced — which is the trade the composition
        // comment above already accepts, and for its reason: the run it was drawn for does not
        // exist.
        SkillTree tree = treeRules is null
            ? null
            : new SkillTree(treeRules, effects, _events);

        // **A tree that would not fit the runner refuses the run** (M3-06 rule 5, and the owner's
        // ruling at M3-06). The alternative was letting SkillRunner.Add throw on the thirteenth,
        // which lands inside M3-08a's ChooseOffer — *after* SkillTree.Take has recorded the node,
        // applied its effects and published NodeTaken — so a mistake a designer made weeks earlier
        // would kill a run at the moment a card is tapped and kill it dirty, with the node owned
        // and unfireable. Asked here it is TreeRules' own argument one class over: an authoring
        // mistake refuses the run, with nothing announced and nothing standing.
        //
        // It is the same shape SkillTree's constructor gives EffectRegistry.Apply — sweep the
        // authored content at Start, keep the throw as the backstop — and it is what makes Add's
        // capacity throw unreachable in a live run rather than merely unlikely.
        if (tree is not null && tree.Rules.ActiveCount > SkillRunner.MaxActives)
        {
            throw new ArgumentException(
                $"'{tree.Rules.Tree.Id}' holds {tree.Rules.ActiveCount} Active nodes and the "
                    + $"runner holds {SkillRunner.MaxActives}. CH §4's ~25 % Active over CH §5's "
                    + "27 nodes is about seven, so a tree this far past it is an authoring mistake "
                    + "rather than a capacity to raise.",
                nameof(config));
        }

        // After the registry and the blackboard it reads, and before State, which is what holds it.
        // One per run like everything above: a second Start must not inherit the first run's
        // cooldowns, and a runner that outlived a run would be casting a dead player's skills.
        //
        // The blackboard is PlayerCombat's and is borrowed rather than owned — one writer, many
        // readers (ADR-0005), and this is the first reader that decides something with it.
        var skills = new SkillRunner(effects, combat.Blackboard, _events);

        // Beside the tree, and null exactly when the tree is: a class with no tree banks its levels
        // and never opens a flow (M3-08a rule 5), which is every run in this build until M3-12
        // authors one. Below the runner because it pushes a chosen Active into it, and below the
        // registry because Overflow's two modifiers go on through it.
        LevelUpFlow levelUp = tree is null
            ? null
            : new LevelUpFlow(tree, progression, skills, effects, _events);

        State = new RunState(
            config.ModeId,
            config.CharacterId,
            seed,
            config.StageIndex,
            character,
            motor,
            combat,
            enemies,
            projectiles,
            progression,
            effects,
            tree,
            skills,
            zones,
            levelUp);

        // With the state, not with the session: a run that ended mid-dash must not make the first
        // tick of the next one think it has a motor to stop.
        _wasCharging = false;

        // **Before RunStarted, and that is the whole of rule 2.** M1-17's HUD draws the bar it is
        // told about from inside that handler — it reads State.PlayerHp there — so applying the
        // restore afterwards would show a resumed run a full bar that drops to 62 % on the next
        // frame. A resumed run's first impression is the one frame nothing gets to be wrong in.
        //
        // Seven values now (rule 3, extended by M3-01b and again here): hit points, shield,
        // simulated seconds, the level, experience and banked picks the save carried, and the tree
        // nodes it was taken with. The generator is already standing where the save left it — the
        // composition root restored it when it built the generator, before this session existed —
        // and everything else is rebuilt rather than read back: the arena from ArenaFor(stage,
        // seed), the wave plan from the restored stream position a few lines above, and the
        // population from nothing at all, because a boundary has none.
        //
        // **The order of the first two is the one thing in this block that is not
        // interchangeable** (M3-03 rule 5, AR §18.1). The rest are independent of each other.
        if (config.Restore is RunSnapshot resumed)
        {
            // **Before Health.Restore, and that is an AR §18.1 row rather than a preference.** The
            // saved hit points are absolute and are clamped against the live maximum, and the tree
            // is what moves that maximum: a `+20 max HP` node replayed *after* the clamp means a run
            // saved at 150 of 160 comes back at 140 of 160. The player loses the difference once per
            // resume, silently, and the only symptom is a bar slightly shorter than the one they put
            // the phone down in front of. It is also the reason this task depends on M3-01b rather
            // than the other way round.
            //
            // Silent and gated: nothing publishes before RunStarted, and a saved order that breaks
            // the tree's own gating is refused rather than absorbed — see SkillTree.Restore. Null
            // for a class with no tree, which ignores the ids the same way this method did between
            // M3-01b and here (rule 10).
            tree?.Restore(resumed.TakenNodeIds);

            // **Immediately after the replay and reading its take order** (M3-06 rule 5). Core
            // pushes a skill into the runner; the runner subscribes to nothing, so a resumed run's
            // actives arrive here exactly as a fresh run's arrive from M3-08a's ChooseOffer. Take
            // order is therefore the runner's order on a resumed run as well as a fresh one, which
            // is what makes the walk in Tick reproducible from a seed.
            //
            // Order-independent with the three restores below it, unlike the pair above: nothing
            // here applies an effect or reads a maximum. It is beside Restore because the list it
            // walks is the one Restore just filled.
            if (tree is not null)
            {
                IReadOnlyList<ContentId> taken = tree.TakenIds;

                for (int i = 0; i < taken.Count; i++)
                {
                    SkillSpec node = tree.Rules.Skill(taken[i]);

                    if (node.Kind == SkillKind.Active)
                    {
                        skills.Add(node);
                    }
                }
            }

            // **Below the loop above and above Health.Restore, and the first half of that is an
            // AR §18.1 row rather than a preference** (M3-07b rule 7). Below the actives, because a
            // slot naming a skill the runner has not been told about yet is indistinguishable from
            // rule 6's stale id: the restore would drop every slot in silence and the run would come
            // back with empty buttons and no error. Above Health.Restore for no reason of its own —
            // it moves no stat — and written there anyway, because this block is read as an order
            // and a line placed outside it invites the next one to be placed anywhere.
            //
            // Silent, for the reason the whole block is: nothing may publish before RunStarted.
            // Unlike the tree's restore it refuses nothing — a slot naming a skill this run does not
            // own leaves that slot empty and the rest come back, because a slot is where a button
            // sits rather than the run's power (SkillRunner.Restore).
            skills.Restore(resumed.ManualSkillIds);

            // **After the tree restore and above Health.Restore, and the second half of that is an
            // AR §18.1 row rather than a preference** (M3-08a rule 9). Overflow puts a `+2 % MaxHp`
            // modifier on per level, so fourteen of them move the maximum by 28 % — and the saved
            // hit points are absolute and are clamped against the live maximum. Replayed *after* the
            // clamp, a run saved at 170 of 179 comes back at 140: the same loss SkillTree.Restore is
            // ordered against, from a second writer.
            //
            // **Derived, because no field carries it and RunSnapshot.CurrentVersion stays 3.** Every
            // pick a run has earned is spent on a node, spent on Overflow, or unspent — the exact
            // mirror of M3-03 rule 6, which says the *pending* count is the one that cannot be
            // derived. Anything that later spends a pick without taking a node owes this identity or
            // owes a field; M6-02's Banish does not, since it removes a node from the pool rather
            // than a pick from the player.
            //
            // Guarded on the flow rather than computed unconditionally, for the reason the tree's
            // own restore is `tree?.Restore`: a class with no tree ignores the saved ids entirely,
            // and asserting an identity over numbers half of which were discarded would refuse a
            // legal save. It costs nothing to skip — with no tree nothing can spend a pick, so every
            // level is banked and the identity is 0 either way (rule 5).
            if (levelUp is not null)
            {
                int overflow = resumed.Level - 1 - resumed.TakenNodeIds.Count - resumed.PendingLevelUps;

                if (overflow < 0)
                {
                    throw new ArgumentException(
                        $"The saved picks do not add up: level {resumed.Level} earns "
                            + $"{resumed.Level - 1} pick(s), of which {resumed.TakenNodeIds.Count} "
                            + $"went on nodes and {resumed.PendingLevelUps} are unspent, leaving "
                            + $"{overflow} for Overflow. It is arithmetic rather than content, so it "
                            + "cannot be rescued by clamping.",
                        nameof(config));
                }

                // Silent, for the reason the whole block is: nothing may publish before RunStarted,
                // and a resumed run's Overflow was earned in a previous session and is not news.
                levelUp.GrantOverflow(overflow);
            }

            combat.Health.Restore(resumed.PlayerHp, resumed.PlayerShield);

            // Silent and settling, for the reason the whole block is here: a presenter reading
            // State.Level from inside RunStarted must already see it, and nothing may publish
            // before the run it belongs to has been announced (M3-01b rule 7).
            progression.Restore(resumed.Level, resumed.Xp, resumed.PendingLevelUps);

            State.Time = resumed.RunTime;
        }

        // Published before IsRunning flips, so a handler that reads the session from inside this
        // event sees a run that is announced and not yet live. The alternative — flip, then
        // publish — would let a listener tick or end a run whose composition is still mid-flight.
        // The same order holds in End: during a lifecycle event the session still reports the
        // state it is leaving.
        _events.Publish(new RunStarted(config.CharacterId, seed));

        IsRunning = true;

        // The run is on disk from its first frame (rule 1), carrying the position captured at the
        // top of this method rather than one read here (rule 5).
        //
        // **The opening write is what stops a stale run being resumed** (rule 2). Without it, a
        // player who abandons a stage-12 run, starts a fresh one and loses the phone in stage 1
        // resumes into stage 12 — the file on disk describes a run nobody is playing until the new
        // one reaches its own first boundary, forty to seventy-five seconds later. The alternative
        // was the Menu deleting the file when Descend is tapped, which puts an ISaveStore and a
        // fire-and-forget delete into a presenter; writing the new run down instead needs no new
        // dependency anywhere and makes the file describe the run in progress at all times.
        //
        // The stage is the config's, never assumed to be 1: a run that begins at 7 resumes at 7.
        _recorder.Take(State, config.StageIndex, opening);

        // After RunStarted, and the order is asserted by a test. Subscribers are wired when the
        // scope is built, well before this — so the reason is not "so that anyone is listening",
        // it is that a run has to be announced before the things inside it are: a view handling
        // EnemySpawned may reasonably assume there is a run to put an enemy in. It is also after
        // IsRunning flips, so a handler that ticks or reads the session from inside a spawn event
        // finds a live run rather than one that has not begun.
        //
        // Every id in the plan was resolved above, so this line can no longer fail on content and
        // the announcement above can no longer be stranded by it (ledger row 3).
        enemies.SpawnAll(config.SpawnPlan);

        // Built after SpawnAll (M2-05 rule 13), so the arena's dressed-in enemies are standing
        // before wave 1 arrives — the ids follow the order the arena reads in, and the director's
        // concurrency check counts them, which it could not do if it had begun first.
        //
        // Built for every run, including one with nowhere to spawn and one whose mode has nothing
        // to compose: an inert director is a director, so nothing downstream has to ask which kind
        // of run it is in (rule 12).
        // Where a body may be put no longer comes in here: an arena's spawn points are a fact about
        // whichever room is standing, so they arrive on the snapshot and reach the director at
        // Begin, one stage at a time (M2-11a rule 6).
        _director = new SpawnDirector(enemies, _events);

        // A mode with an empty roster composed nothing above, so there is no stage to pace: the
        // arena is whatever the spawn plan dressed into it and it stays that way. The director is
        // built anyway, one line up, for rule 12's reason — an inert director is a director.
        if (plan is null)
        {
            _flow = null;

            return;
        }

        _flow = new StageFlow(
            mode,
            composer,
            _director,
            enemies,
            projectiles,
            combat,
            _events,
            plan,
            seed);

        // Last, and after RunStarted and SpawnAll for the reason SpawnAll itself is after them: this
        // publishes StageArrived, and a handler dressing an arena from it may reasonably assume
        // there is a run to dress one for. It does not begin the director — GD §7.1's two seconds
        // of arrival come first, and it is the end of those that hands the plan over (M2-10 rule 2).
        _flow.Begin(config.StageIndex, State.Time);
    }

    /// <inheritdoc />
    public void Tick(WorldSnapshot snapshot)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException(
                "No run is running. Start one before ticking.");
        }

        // No null guard on the snapshot, unlike Start's config, and the asymmetry is deliberate:
        // this runs 60 times a second against one instance the builder owns for the whole run, so
        // a null could only ever be the first tick after a mis-wired scope — a failure that
        // arrives immediately and unmissably either way.
        State.Time += snapshot.Dt;

        // The same second, in the object the effect handlers hold. Here rather than anywhere lower
        // so that it is already this tick's before combat, the skills step or a behaviour can read
        // it — and here rather than nowhere because Apply takes no clock (see SimulatedClock).
        _clock.Now = State.Time;

        // Written down, not decided: Unity resolves collision and reports where the player ended
        // up. Core never assigns a position to move anyone.
        State.PlayerPosition = snapshot.PlayerPosition;

        // Ingest first, always. It is what makes every position in core this frame's rather than
        // last frame's, so anything that reads an enemy — perception, targeting, cone hits in
        // M1-11 — has to come after it, and a target chosen from stale positions is the whole bug
        // the snapshot exists to prevent.
        State.Enemies.Ingest(snapshot);

        // Combat between the two enemy passes, which is the order the rest of the frame hangs off.
        // Before the behaviours, so the target is chosen from the same positions the enemies were
        // just seen at rather than from wherever this tick's AI moved them; and before the motor,
        // because the motor needs the facing this decides.
        //
        // Which is also why the facing handed over is the one the motor is *currently* in, from
        // last tick's turn: combat cannot be given a facing that has not been decided yet. A swing
        // landing mid-turn is therefore aimed up to one frame of rotation behind the pose that gets
        // drawn — see PlayerCombat.Tick's bodyFacing, which is where that trade is argued.
        State.Combat.Tick(
            snapshot.Dt,
            State.Time,
            snapshot,
            State.Enemies.Registry.Alive,
            State.Motor.Facing);

        // **After combat and before the enemy behaviours — which puts it above the projectile step
        // as well, and that is the half worth arguing** (M3-06 rule 7, AR §18.1).
        //
        // *After combat*, because UpdateBlackboard has just filled seven of the nine fields a
        // trigger can read, and a predicate over last tick's HP is a Consecrate that fires a frame
        // after the hit that should have caused it.
        //
        // *Above ProjectileSystem.Tick*, because IncomingProjectiles is written by that step and
        // nowhere else (ProjectileSystem.cs, at the end of its own pass): read here it is the count
        // of bolts still in the air **before this tick's arrivals are resolved**, which is exactly
        // what CC §6.4's Bulwark means by "an enemy projectile is inbound" — a shield raised
        // *before* the bolt lands. Ticked after that step instead, the same field would describe
        // the sky *after* the hit, and the archetype's whole answer would be a shield put up over a
        // wound. The field is therefore deliberately one step old, and that staleness is the
        // mechanic rather than a lag to fix.
        //
        // At most one cast per tick, and the walk stops on it (rule 6).
        State.Skills.Tick(snapshot.Dt, State.Time);

        // **Immediately after the runner and above the projectile step, and both halves are the
        // mechanic's** (M3-11a-ii rule 3, AR §18.1). *After the runner*, because it may cast this
        // frame: expiring first would take a grant back and hand the same one straight over again on
        // the tick a skill recasts, announcing an expiry that never happened. *Above the projectile
        // step*, for the reason the line above sits there — a shield that expired after this tick's
        // bolts were resolved would have absorbed a hit it was no longer entitled to.
        _timed.Tick(State.Time);

        // **Immediately after the timed effects, which keeps the whole skills block above the
        // projectile step** (M3-11b rule 3, AR §18.1). *After the runner*, because a zone cast this
        // tick has to exist this tick — a player who drops below 60 % is standing on healing ground
        // in the same frame. *After the timed effects*, because these are the game's two expiry
        // mechanisms and interleaving them would make the order a shield comes off in depend on
        // whether a zone happened to end on the same tick; they are separated rather than merged
        // because a zone is a place with its own life and a grant is state on the player that
        // something has to take back (rule 9).
        State.Zones.Tick(State.Time);

        // After combat, and the order decides who wins a trade. The player's swing this tick is
        // resolved against enemies as they were seen, and the enemy's strike lands against a player
        // whose i-frames and Aegis have already been advanced — so a dodge that expired this tick
        // has expired by the time the Husk swings, rather than protecting one frame past its own
        // window. The behaviours get the intent sink because a strike is decided here and the walk
        // it interrupts is a question for the body, which has to leave on the tick that produced it.
        //
        // The context is built here and once (M2-07b rule 2), never per agent: every enemy in the
        // arena therefore decides against one reading of the clock, and widening what a behaviour
        // may reach is a change to one struct rather than to every implementer, every dispatch and
        // every test that calls one. M2-08 is the first time that promise was called in: the census
        // is the sixth member, added in one line so that a Bloater's fuse can end its own life
        // through the door damage reaches an enemy through. It is a struct; it allocates nothing.
        var enemies = new EnemyTickContext(
            snapshot.Dt,
            State.Time,
            State.Combat,
            _intents,
            _events,
            State.Projectiles,
            State.Enemies);

        State.Enemies.Tick(enemies);

        // After the behaviours and before the death check (M2-07a rule 10, AR §18.1).
        //
        // After, because a shot fired this tick starts flying now and must not be able to arrive on
        // the tick it left: the behaviours are where a Spitter releases (M2-07b), and landing in the
        // same step would erase the flight the whole archetype exists to make the player walk out
        // of. Before, because a bolt that kills has to end the run on the tick it landed, exactly as
        // a Husk's strike does — put after the check and the player would keep playing for one
        // frame with no hit points.
        //
        // Core decides the arrival and calls PlayerCombat.ApplyDamage itself; nothing is asked of
        // the body (ledger row 7, settled at M2-07a rule 1).
        State.Projectiles.Tick(State.Time, State.PlayerPosition, State.Combat);

        // The first thing that ends a run from inside one (M1-17). Asked here rather than
        // subscribed to, because core has no business listening to its own events: PlayerCombat
        // publishes PlayerDied for everyone with something to say about a death, and this class
        // reads the state it already owns.
        //
        // After both of the passes that can hurt the player — a Husk's strike and a bolt's arrival —
        // because that is where the death was announced, so RunEnded follows PlayerDied on the same
        // tick, in that order, whichever of the two killed them.
        // And before TickBody, because a corpse is not steered: the intent it would write is a
        // velocity for a run that is over, and RunTicker would apply it to a body nobody is
        // driving any more.
        if (State.Combat.IsDead)
        {
            End();
            return;
        }

        // After the death check and before the director (M3-01a rule 6, AR §18.1). Experience is
        // granted on the tick and never on the fact: a kill reported between ticks by
        // ReportConeHits accrues on EnemySystem and is paid here, at most a frame late, which is
        // the lag every fact already has (ADR-0003).
        //
        // After the death check, so a run that ended this tick levels nobody — a LeveledUp
        // published one line below End() would land in a scope that is being torn down, and no
        // screen could ever show it.
        //
        // Before the director and the stage flow, so a LeveledUp earned by a stage's last kill
        // precedes that tick's StageCleared and the boundary snapshot taken with it. That ordering
        // is what lets M3-01b's write carry the level the player just earned rather than the one
        // they had a frame ago.
        //
        // Called unconditionally: a tick with no kills drains zero, and Grant is silent for
        // anything that is not greater than zero, so there is no branch here for a caller to get
        // wrong.
        State.Progression.Grant(State.Enemies.DrainXp());

        // After the death check and before the motor (M2-05 rule 14, AR §18.1). After, because a
        // run that ended this tick must spawn nothing — a wave arriving on the frame the player
        // died would be telegraphed into an arena nobody is playing in. Before the motor, because
        // the director is part of the world the player is moving through rather than part of the
        // move: it sees the same simulated `now` and the same player position everything else this
        // tick did.
        //
        // The Spawn stream and no other, read here rather than held by the director, so that the
        // one thing in a run that consumes spawn randomness does so through the run's own
        // generator (ADR-0011).
        _director.Tick(State.Time, State.PlayerPosition, _random.Spawn);

        // After the director and before the motor (M2-10 rule 13, AR §18.1). After, because the
        // flow ends a stage by reading IsStageComplete, which the director has just this instant
        // finished deciding — asked one step earlier it would be answering about last frame's
        // arena. Before the motor, for the director's own reason: the stage is part of the world
        // the player is moving through rather than part of the move.
        //
        // Null for a mode with nothing to compose, which is a run with no waves rather than a run
        // with no stage — the depth is still the config's and everything priced against it still is.
        if (_flow is not null)
        {
            // Read before the flow moves, compared after: the *edge* into Clear is the write point,
            // and a flow parked in Clear for a second and a half must not write once a frame
            // (M2-14a rule 1). Rejected: a parameter on StageFlow's constructor, which would widen
            // a shape M2-10 fixed for a caller that only wants to know when.
            StagePhase phaseBefore = _flow.Phase;

            _flow.Tick(State.Time, snapshot, _random.Spawn);

            // Copied rather than owned, because the two numbers answer different questions: the
            // flow's is what the stage machine is running, and this is what the run reports and
            // saves (M2-13). They agree because this is the one line that moves the second one.
            State.StageIndex = _flow.Stage;

            // A finite mode that has run out of stages. The flow sets the flag and stays in Clear;
            // ending the run is this class's word and nobody else's (rule 14). Inert for Descent,
            // which is endless — and written anyway, because ModeSpec.FinalStage exists and the
            // alternative is a run walking through a door into a stage its mode does not have.
            if (_flow.IsModeComplete)
            {
                End();

                return;
            }

            // The stage boundary, and GD §7.3's "run state persists to disk at every stage
            // boundary" in one line. The moment is the frame the last body of a stage drops —
            // M2-10 rule 3's — rather than the next stage's arrival, because the beat in between is
            // the gate wait, and that is the natural "put the phone down" point. A phone put down
            // at the door must already be saved.
            //
            // The snapshot describes the stage the player is about to play, so the next one (rule
            // 3) — and it is taken here, upstream of the recompose that happens three phases later
            // at the end of Transition, which is what makes a resumed run compose byte-identical
            // waves (rule 5). Nothing draws during Clear, Gate or Transition: the director's queue
            // is spent, so MaybeTelegraph returns before its one draw, and StageFlowTests'
            // Tick_DrawsNoRandomOutsideComposition is the assertion from the other side.
            //
            // After the IsModeComplete check above, deliberately (rule 6): a finite mode that has
            // just cleared its final stage is a run that is over, and a snapshot of it would be a
            // resume into a stage the mode does not have. Inert while Descent is endless, and
            // written anyway for M2-10 rule 14's reason.
            if (phaseBefore != StagePhase.Clear && _flow.Phase == StagePhase.Clear)
            {
                _recorder.Take(State, _flow.Stage + 1);
            }
        }

        TickBody(snapshot);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A forward, and the shortest method here on purpose: everything a report has to be sceptical
    /// about — whether a swing is owed an answer at all, duplicate ids, ids that have since died —
    /// belongs to the object that asked the question, and putting any of it here would split one
    /// rule across two classes. This one adds only what it alone knows: that there is a run, and
    /// what time it is.
    /// </para>
    /// <para>
    /// Damage lands at <see cref="RunState.Time"/> — the simulated clock as of the last
    /// <see cref="Tick"/> — rather than at some interpolated moment between ticks, for the reason
    /// <see cref="FocusTarget"/> gives: a fact arrives between frames, and core has exactly one
    /// clock. The lag is at most one frame and it is the same lag the i-frames and the Aegis
    /// recharge are already measured against, which matters more than being right to the
    /// millisecond about a swing nobody can see the timing of.
    /// </para>
    /// </remarks>
    public void ReportConeHits(ReadOnlySpan<int> enemyIds)
    {
        RequireRunning(nameof(ReportConeHits));

        State.Combat.ResolveConeHits(enemyIds, State.Time, State.Enemies);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A forward, for the reason <see cref="ReportConeHits"/> is one: every rule about which reports
    /// count — the window, the per-dash dedupe, whether an id is still breathing — belongs to the
    /// object that started the dash. This one adds the clock, and the clock is the same one a swing
    /// lands on: <see cref="RunState.Time"/> as of the last <see cref="Tick"/>, at most a frame
    /// behind the sweep that produced the report and measured against the same simulated seconds the
    /// dash's own window was.
    /// </para>
    /// <para>
    /// Unlike <see cref="ReportConeHits"/>, several of these per dash is the normal case rather than
    /// a mistake — see the port.
    /// </para>
    /// </remarks>
    public void ReportChargeHits(ReadOnlySpan<int> enemyIds)
    {
        RequireRunning(nameof(ReportChargeHits));

        State.Combat.ResolveChargeHits(enemyIds, State.Time, State.Enemies);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Resolved against the enemies as of the last <see cref="Tick"/>, because a command lands
    /// before the frame it belongs to is built (<c>RunTicker</c>'s command phase). Those positions
    /// are therefore up to one frame old — at most a couple of centimetres of walking against a 3 m
    /// tap radius, which no thumb can tell apart from exact. The alternative, running commands
    /// after the ingest, would put the tap *after* the tick it should have influenced and cost a
    /// whole frame of latency on the one input the player expects to be instant.
    /// </para>
    /// <para>
    /// Throws rather than no-ops when nothing is running, unlike <see cref="End"/>: ending a run
    /// that never started is scope teardown being tidy, while commanding one is an input adapter
    /// that is listening when it should not be.
    /// </para>
    /// </remarks>
    public void FocusTarget(Vector3 worldPoint)
    {
        RequireRunning(nameof(FocusTarget));

        State.Combat.FocusAt(worldPoint, State.Enemies.Registry.Alive);
    }

    /// <inheritdoc />
    public void ClearFocus()
    {
        RequireRunning(nameof(ClearFocus));

        State.Combat.ClearFocus();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A press is <em>recorded</em>, never acted on. Nothing about the dash happens here: whether it
    /// fires at all, which way it goes and whether the press is still worth honouring are all
    /// decided on the next <see cref="Tick"/>, by the one object holding CC §5's clock. The reason
    /// is <c>Targeter.Focus</c>'s: a thumb lands between ticks, and a command that resolved itself
    /// where it arrived would put a gameplay decision at whatever point in the frame the input
    /// system happened to read the screen.
    /// </para>
    /// <para>
    /// Stamped with <see cref="RunState.Time"/>, which is the clock the input buffer is measured
    /// against — so a press is at most one frame older than it says it is, and the 0.15 s it stays
    /// live is 0.15 s of simulated time rather than of a wall clock that keeps running while the
    /// game is paused.
    /// </para>
    /// </remarks>
    public void MovementSkill()
    {
        RequireRunning(nameof(MovementSkill));

        State.Combat.Charge.Request(State.Time);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Acted on where it arrives, unlike <see cref="MovementSkill"/> above</b>, and the asymmetry
    /// is CC §6.2 against CC §5 rather than an inconsistency. The dash is <em>recorded</em> because
    /// it needs a direction core has not sampled yet and a buffer that survives a cooldown ending
    /// mid-frame; a skill cast needs neither — its effects land on the player's own stats, there is
    /// no aim, and an early tap is refused rather than kept. So there is nothing for a deferral to
    /// wait for, and deferring anyway would put the cast a frame after the tap for no gain.
    /// </para>
    /// <para>
    /// Stamped with <see cref="RunState.Time"/>, which is the clock the cooldown was scheduled
    /// against — so a tap arriving between ticks is measured against the same simulated seconds the
    /// runner's own <c>Tick</c> uses, rather than against a wall clock that keeps running while the
    /// game is paused.
    /// </para>
    /// </remarks>
    public void CastSkill(int slot)
    {
        RequireRunning(nameof(CastSkill));

        // The return is deliberately dropped. A cooling slot answers false and that is an ordinary
        // early tap (CC §6.2 draws it at 40 % opacity), not something an input adapter can act on —
        // and a command that returned a bool would be a command asking for an answer, which is the
        // one thing ADR-0003 says this direction does not do. A view that wants to know draws
        // `RunState.IsSkillReady`.
        State.Skills.CastSlot(slot, State.Time);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Straight through, with no tick in between: this changes no clock and starts nothing, so there
    /// is no moment in the frame it needs to be at. The one thing it does that the player can see is
    /// publish <c>SkillAutoCastChanged</c>, and a screen redrawing from that wants it on the tap
    /// rather than on the next frame.
    /// </remarks>
    public void SetAutoCast(ContentId skillId, bool auto)
    {
        RequireRunning(nameof(SetAutoCast));

        State.Skills.SetAutoCast(skillId, auto);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A read rather than a command, so it answers <see langword="false"/> outside a run instead of
    /// throwing: <c>RunTicker</c> polls it every frame and is still an <c>ITickable</c> after a run
    /// has ended.
    /// </remarks>
    public bool IsLevelUpPending => IsRunning && State.IsLevelUpPending;

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="IsLevelUpPending"/>'s reasoning — a read, false outside a run.
    /// </remarks>
    public bool HasOffer => IsRunning && State.HasOffer;

    /// <inheritdoc />
    /// <remarks>
    /// <b>The stream is this session's <c>Offers</c> and no other</b> (ADR-0011), handed in here
    /// rather than held by the flow so that the one object which owns the run's randomness stays the
    /// one that hands it out — <c>SpawnDirector.Tick</c>'s shape, one module over.
    /// </remarks>
    public void OpenLevelUp()
    {
        RequireRunning(nameof(OpenLevelUp));

        // Null for a class with no tree, which is every run in this build: the level stays banked
        // and nothing draws, pauses or throws (rule 5).
        State.LevelUp?.Open(_random.Offers);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Throws rather than no-ops when the class has no tree, unlike <see cref="OpenLevelUp"/>: an
    /// open call is the frame loop asking a standing question, where this one is a view reporting a
    /// tap on a card that cannot exist.
    /// </remarks>
    public void ChooseOffer(int index)
    {
        RequireRunning(nameof(ChooseOffer));

        if (State.LevelUp is null)
        {
            throw new InvalidOperationException(
                "This run's class has no tree, so no offer can ever be open and there is nothing to "
                    + "choose. A view sent ChooseOffer without a card to send it for.");
        }

        State.LevelUp.Choose(index, _random.Offers);
    }

    /// <inheritdoc />
    public void End()
    {
        // A no-op rather than a throw, so RunScope's disposal can call it without first asking
        // whether a run got as far as starting.
        if (!IsRunning)
        {
            return;
        }

        _events.Publish(new RunEnded(State.Time));

        IsRunning = false;

        // Cleared without announcing a despawn each, and after RunEnded rather than before: the
        // scope is going away and with it every subscriber a despawn could reach, so 64 farewell
        // events would be noise — while a listener handling RunEnded can still read the census
        // that was live when the run finished.
        State.Enemies.Clear();

        // The same moment and the same silence, for the same reason. It also drops whatever rings
        // were in flight: a telegraph is a promise to the player, and there is no longer a player
        // to keep it to.
        _director.Clear();

        // And the shots that were still in the air, silently again. A bolt that landed on an ended
        // run would hurt a corpse and publish an impact into a scope that is being torn down.
        State.Projectiles.Clear();

        // And whatever a cast was still holding — forgotten rather than taken back, for the same
        // sentence: there is nothing left to take it off, and an expiry announced here would reach a
        // view that is being destroyed (M3-11a-ii rule 8).
        _timed.Clear();

        // And the ground a cast put down, forgotten in the same silence and for the same sentence.
        // This is the *only* caller — a stage boundary deliberately leaves a zone standing and
        // pulsing, which is the mirror of M2-10's rule that a door heals nobody (M3-11b).
        State.Zones.Clear();
    }

    /// <summary>
    /// Moves the player — or deliberately does not, while a dash is doing it instead.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two instructions about where the character goes, and never both at once.</b> A
    /// <see cref="ChargeIntent"/> has already told the body to travel 10 m in a fixed direction over
    /// 0.22 s; a <see cref="PlayerMoveIntent"/> alongside it would be a second opinion arriving
    /// sixty times a second, and the body would have to invent a rule about which of them core
    /// meant. So for the length of the dash core says nothing about movement at all, and the motor
    /// is not ticked either — it would be integrating a stick nobody is being moved by.
    /// </para>
    /// <para>
    /// <b>The tick the dash ends on stops the motor and moves nowhere.</b> A suspended motor still
    /// holds whatever velocity it had when the dash began, and letting that out would fling the
    /// character on for another frame at the speed they were running before they dodged. So
    /// <c>Stop</c> first, and the intent that goes out carries the zero — one frame at rest, which
    /// is 16 ms nobody can feel, against a carry-over everybody can. Acceleration resumes from rest
    /// on the next tick, which is what CC §2.4's 0.06 s ramp is for.
    /// </para>
    /// <para>
    /// The facing survives all of it. <c>PlayerMotor.Stop</c> leaves it alone on purpose, and
    /// nothing here turns the character round: a dash that ended with the body snapping back to
    /// where it was looking before would undo the one thing the player just committed to.
    /// </para>
    /// <para>
    /// Exactly one <see cref="PlayerMoveIntent"/> per tick otherwise, including when the stick is
    /// centred — the body needs the deceleration velocity just as much as the acceleration one, and
    /// a tick that emitted nothing would leave the view applying whatever it last read.
    /// </para>
    /// </remarks>
    private void TickBody(WorldSnapshot snapshot)
    {
        if (State.Combat.Charge.IsActive)
        {
            _wasCharging = true;
            return;
        }

        if (_wasCharging)
        {
            _wasCharging = false;

            State.Motor.Stop();
        }
        else
        {
            // The character now looks at what it is aiming at. Null when there is nothing to aim
            // at, which the motor reads as "face the way you are moving" — M0's behaviour, still
            // correct for an empty arena.
            State.Motor.Tick(snapshot.Dt, snapshot.MoveInput, State.Combat.FaceDirection);
        }

        _intents.PlayerMove(new PlayerMoveIntent(State.Motor.Velocity, State.Motor.Facing));
    }

    /// <summary>
    /// Refuses a run whose plan or roster names an archetype nobody authored, before anything about
    /// the run has been announced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The roster is walked too, not only the plan</b>, and that is the half worth arguing for.
    /// The plan fails on the first frame either way, because <c>SpawnAll</c> would hit it
    /// immediately; the roster is what M2-05's director spawns from, so an unauthored archetype
    /// there would otherwise surface forty seconds into a run as a wave that threw — at a moment
    /// that looks like a director bug and points at nothing. A mode's schedule is a statement of
    /// intent (M2-02 rule 10: Descent ships with Husk alone until M2-06 authors the other two),
    /// and this is what makes the gap between intent and content loud instead of mysterious.
    /// </para>
    /// <para>
    /// <c>TryGetEnemy</c> rather than <c>Enemy</c>, so the message can say <em>where</em> the
    /// stranger was named. The catalog's own "no enemy with id 'x'" is true and unhelpful when
    /// three different lists could have held it.
    /// </para>
    /// </remarks>
    private void RequireAuthored(SpawnPlan plan, ModeSpec mode)
    {
        for (int i = 0; i < plan.Initial.Count; i++)
        {
            RequireArchetype(plan.Initial[i].SpecId, $"the spawn plan's entry {i}");
        }

        for (int i = 0; i < mode.Roster.Count; i++)
        {
            RequireArchetype(mode.Roster[i].SpecId, $"'{mode.Id}'s roster");
        }
    }

    /// <summary>Refuses one archetype id the catalog does not hold, naming who asked for it.</summary>
    private void RequireArchetype(ContentId specId, string source)
    {
        if (_catalog.TryGetEnemy(specId, out _))
        {
            return;
        }

        throw new KeyNotFoundException(
            $"No enemy with id '{specId}' in the catalog, and {source} names it. Nothing about "
                + "this run has been announced; add an EnemyDefinition to BootScope's enemy list "
                + "or correct the id.");
    }

    /// <summary>
    /// Refuses a command or a fact that arrived outside a run, naming what arrived.
    /// </summary>
    /// <remarks>
    /// Named rather than generic, because these reach here from different adapters and
    /// "FocusTarget was called with no run running" points straight at whichever one is listening
    /// when it should not be.
    /// </remarks>
    private void RequireRunning(string member)
    {
        if (IsRunning)
        {
            return;
        }

        throw new InvalidOperationException(
            $"{member} was called with no run running. Player commands and physical facts both " +
            "belong to a live run — the input map is disabled outside one and the ticker stops " +
            "reporting — so this is a wiring mistake rather than something the player or the " +
            "world did.");
    }
}
