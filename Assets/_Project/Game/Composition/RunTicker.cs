using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEngine;
using VContainer.Unity;

namespace Soulvail.Game.Composition;

/// <summary>
/// The frame. Senses, then brain, then body — in that order, every frame, for the length of a
/// run. The one place the loop of AR §4.3 is written down.
/// </summary>
/// <remarks>
/// <para>
/// An entry point rather than a MonoBehaviour, for the same reason <see cref="BootFlow"/> is one:
/// its dependencies arrive by constructor and its lifetime is the scope's, so the run ends when
/// <c>RunScope</c> is disposed rather than when some object in the scene happens to be destroyed.
/// </para>
/// <para>
/// It owns the *order* and nothing else. Every line below is a call into something that already
/// has tests; what this class contributes — and what no unit test can check — is that they happen
/// in the one sequence that is correct. That is why it is verified by playing the game.
/// </para>
/// </remarks>
public sealed class RunTicker : IStartable, ITickable, IDisposable
{
    private readonly IRunSession _session;

    /// <summary>
    /// The same run seen through the command port. A second handle rather than a cast, for the
    /// reason <c>TapToFocusAdapter</c> takes one: what the player asks for and what the frame does
    /// to the run are different privileges, and the press below has no business being able to end a
    /// run.
    /// </summary>
    private readonly IPlayerCommands _commands;

    private readonly PendingRun _pending;
    private readonly ContentCatalog _catalog;
    private readonly WorldSnapshot _snapshot;
    private readonly SnapshotBuilder _builder;
    private readonly IntentBuffer _intents;
    private readonly PlayerView _player;
    private readonly ChargeMotion _charge;
    private readonly EnemyViews _enemyViews;
    private readonly InputAdapter _input;
    private readonly SpawnPlan _spawnPlan;
    private readonly TapToFocusAdapter _tapToFocus;
    private readonly ConeOverlapQuery _cone;

    /// <summary>
    /// Where a swing's answer is assembled before it is handed back to core. One array for the
    /// life of the run, sized to what the query can see, so the fact costs no allocation on the
    /// frames that already have the most happening in them (AR §14).
    /// </summary>
    private readonly int[] _coneHitIds;

    /// <exception cref="ArgumentNullException">Any dependency is null.</exception>
    /// <remarks>
    /// Guarded like <c>RunSession</c>'s constructor and unlike the per-frame paths below: this is
    /// called once, from the composition root, where a null means a registration is missing. Left
    /// unguarded, the symptom would be a <see cref="NullReferenceException"/> on the first frame
    /// pointing at the tick rather than at the wiring.
    /// </remarks>
    public RunTicker(
        IRunSession session,
        IPlayerCommands commands,
        PendingRun pending,
        ContentCatalog catalog,
        WorldSnapshot snapshot,
        SnapshotBuilder builder,
        IntentBuffer intents,
        PlayerView player,
        ChargeMotion charge,
        EnemyViews enemyViews,
        InputAdapter input,
        SpawnPlan spawnPlan,
        TapToFocusAdapter tapToFocus,
        ConeOverlapQuery cone)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _enemyViews = enemyViews ?? throw new ArgumentNullException(nameof(enemyViews));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _spawnPlan = spawnPlan ?? throw new ArgumentNullException(nameof(spawnPlan));
        _tapToFocus = tapToFocus ?? throw new ArgumentNullException(nameof(tapToFocus));
        _cone = cone ?? throw new ArgumentNullException(nameof(cone));

        _coneHitIds = new int[cone.Capacity];

        // Unity's == rather than `is null`: RunScope supplies these from serialized fields, so a
        // destroyed or unassigned object is a live reference that only compares equal to null
        // through the engine's operator.
        _player = player == null ? throw new ArgumentNullException(nameof(player)) : player;
        _charge = charge == null ? throw new ArgumentNullException(nameof(charge)) : charge;
    }

    /// <summary>
    /// Starts the run: the screen stays awake, the stick starts being read, and core is told which
    /// class to play and what is standing in the arena.
    /// </summary>
    /// <remarks>
    /// By the time this runs, <c>EnemyViews</c> is already listening. That is guaranteed by
    /// construction rather than by ordering luck: this object takes <c>SnapshotBuilder</c>, which
    /// takes <c>EnemyViews</c>, which subscribes in its own constructor — so the whole chain is
    /// built before VContainer's dispatcher can call this, and none of the plan's spawn events can
    /// be published into an empty room. Anything that later needs to hear <c>RunStarted</c> must
    /// establish its own subscription the same way, not from a <c>Start</c> of its own.
    /// </remarks>
    public void Start()
    {
        // Only for the length of a run, never app-wide — a menu has no business burning battery.
        // Restored in Dispose.
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        _input.Enable();

        // The plan is the scene's, built by RunScope from the dummies dressed into it (M1-07). It
        // is SpawnPlan.Empty when nothing is dressed, never null, so this line always says out
        // loud what the arena starts with — see RunConfig. M2-05's director takes it over.
        _session.Start(new RunConfig(
            _pending.IsSet ? _pending.CharacterId : FallbackCharacterId(),
            _spawnPlan));
    }

    /// <summary>
    /// One frame: ask, sense, decide, act.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CommandPhase"/> is first, before the snapshot exists. What the player asked for
    /// has to be in core's hands before the tick it should influence, or a tap would be acted on a
    /// frame after the one it was made in — the most visible latency in the game, on the one input
    /// that is expected to be instant.
    /// </para>
    /// <para>
    /// The order is the contract. <see cref="IntentBuffer.Clear"/> runs *before*
    /// <c>session.Tick</c>, never after — clearing afterwards would erase the intent the body has
    /// not read yet. The bodies are applied *after* core has written, with the snapshot's clamped
    /// <c>Dt</c> and not <c>Time.deltaTime</c>, so brain and body take the same step. Since M1-18
    /// that is every body in the arena and not only the player's: <see cref="ApplyEnemyMoves"/> sits
    /// beside <c>_player.Apply</c> so the whole population steps once, together, before anything
    /// physical is asked about it.
    /// </para>
    /// <para>
    /// <c>HasPlayerMove</c> is checked rather than assumed. The buffer deliberately leaves the
    /// previous intent in place when cleared (M0-06), so reading it unconditionally would apply
    /// last frame's velocity on any frame core stayed silent — a player who keeps sliding.
    /// </para>
    /// <para>
    /// <b>The fact phase is last, and it is AR §4.3's step 5:</b> the physics a swing or a dash
    /// asked for runs <em>after</em> the body has been moved, so both are resolved against the
    /// arena as it stands at the end of this frame rather than against the one the frame started
    /// with. Both come back in through a fact, and everything they cause is published before this
    /// method returns.
    /// </para>
    /// <para>
    /// <b><see cref="ApplyKnockbacks"/> is last of all, and that is not tidiness.</b> The shoves it
    /// applies are written by core while it answers <c>ReportChargeHits</c> — that is, from inside
    /// <see cref="StepCharge"/>, later in the frame than every other intent in the buffer. A reader
    /// placed anywhere above this line would look at an empty list every single frame and find the
    /// entries already cleared by the time it looked again. <c>IntentBuffer.Knockbacks</c> carries
    /// the same warning from the other end.
    /// </para>
    /// <para>
    /// Allocates nothing: everything below is a call on an object built once per run, the intents
    /// are <c>readonly struct</c>s taken by <c>in</c>, the two lists are walked by index rather than
    /// enumerated, and the swing's answer goes back as a span over an array owned since the run
    /// started.
    /// </para>
    /// <para>
    /// <b>Two guards, and they are the same rule read at two moments (M1-17).</b> A run can now end
    /// from inside itself — the player dies, core publishes <c>RunEnded</c> and stops running —
    /// while this object keeps being ticked until the scene unloads. The first guard is every frame
    /// after that one: nothing is asked, nothing is sensed, and the capsule stands where it fell.
    /// The second is the death frame itself, and it is the one that matters, because the lines below
    /// <c>session.Tick</c> report physical facts back into core: <see cref="ResolveConeHits"/> and
    /// <see cref="StepCharge"/> both call into a session that would now refuse them, loudly, for a
    /// swing that was thrown a few milliseconds before the killing blow landed.
    /// </para>
    /// </remarks>
    public void Tick()
    {
        // Every frame after the run ended. The scope is still alive — the death overlay is on
        // screen waiting for a tap — so this object is still an ITickable with nothing to do.
        if (!_session.IsRunning)
        {
            return;
        }

        CommandPhase();

        _builder.Build(_snapshot, Time.deltaTime);

        _intents.Clear();

        _session.Tick(_snapshot);

        // The death frame. Core ended the run mid-tick, so the swing and the dash still sitting in
        // the buffer have nobody left to report to — see the class remarks. Dropping them costs a
        // single frame of a fight that is already over.
        if (!_session.IsRunning)
        {
            return;
        }

        // Before the move below, so that the dash this starts is already in flight when the guard
        // there asks whether one is. Core suspends the motor on the same tick, so there is no
        // PlayerMove to suppress today — but the two would only have to disagree once.
        if (_intents.HasCharge)
        {
            _charge.Begin(_intents.Charge, _session);
        }

        // The guard is belt and braces, and deliberately so. Core already withholds PlayerMove for
        // every tick of a dash (M1-15 rule 3), so this can only matter if the two clocks ever drift
        // apart by a frame at the end of one — and the cost of them doing so would be the body
        // being pushed in two directions on the frame a dodge finishes, which is precisely the frame
        // a player is judging the dodge on.
        if (_intents.HasPlayerMove && !_charge.IsActive)
        {
            _player.Apply(_intents.PlayerMove, _snapshot.Dt);
        }

        ApplyEnemyMoves();

        StepCharge();

        ResolveConeHits();

        ApplyKnockbacks();
    }

    /// <summary>
    /// Answers every cone core asked about this tick: sweep it, and report who was standing in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A swing with no answer is still answered.</b> A count of zero is reported exactly like a
    /// count of five, because the report is what retires <c>PlayerCombat.PendingConeRequestId</c> —
    /// stay silent on a miss and core waits for an answer that never comes, and the next swing's
    /// hits would be credited to the one before it.
    /// </para>
    /// <para>
    /// A <c>for</c> over the count rather than a <c>foreach</c>: the buffer hands out an
    /// <c>IReadOnlyList</c>, and enumerating that would box an enumerator on every frame the player
    /// is attacking, which on a phone is most of them.
    /// </para>
    /// </remarks>
    private void ResolveConeHits()
    {
        IReadOnlyList<ConeHitIntent> cones = _intents.ConeHits;

        for (int i = 0; i < cones.Count; i++)
        {
            ConeHitIntent cone = cones[i];

            int count = _cone.Query(cone, _coneHitIds);

            _session.ReportConeHits(new ReadOnlySpan<int>(_coneHitIds, 0, count));
        }
    }

    /// <summary>
    /// Walks every enemy core gave a direction to this tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Beside the player's move rather than after the fact phase, and that placement is the whole
    /// point: both bodies take the same step with the same <c>Dt</c>, so the swing and the dash
    /// resolved below are answered against an arena where <em>everything</em> has finished moving.
    /// A cone swept before the Husks walked would be answered against where they were last frame,
    /// which is the class of bug the snapshot exists to prevent — and it would show up as a swing
    /// that misses an enemy standing in it.
    /// </para>
    /// <para>
    /// An id with no body is skipped in silence, exactly as <see cref="ApplyKnockbacks"/> skips one:
    /// core is entitled to decide a walk for an enemy whose view has not been created yet, or one
    /// destroyed earlier in this frame, and neither is an error.
    /// </para>
    /// <para>
    /// A <c>for</c> over the count rather than a <c>foreach</c>: the buffer hands out an
    /// <c>IReadOnlyList</c>, and enumerating that would box an enumerator on every frame with a live
    /// enemy in the arena — which, unlike the cone and knockback lists, is very nearly all of them.
    /// </para>
    /// </remarks>
    private void ApplyEnemyMoves()
    {
        IReadOnlyList<EnemyMoveIntent> moves = _intents.EnemyMoves;

        for (int i = 0; i < moves.Count; i++)
        {
            EnemyMoveIntent move = moves[i];

            if (!_enemyViews.TryGet(move.Id, out EnemyView view) || view == null)
            {
                continue;
            }

            view.Apply(move, _snapshot.Dt);
        }
    }

    /// <summary>
    /// Walks this frame's slice of a dash, if one is in flight, and reports whoever it passed
    /// through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Placed between the body being moved and the swing being resolved, because a dash <em>is</em>
    /// the body being moved on the frames it runs — core sends no <c>PlayerMoveIntent</c> while one
    /// is in flight, so this line and the one above it are the same step of AR §4.3 taken two ways.
    /// A swing resolved before it would be answered against a player who had not finished moving.
    /// </para>
    /// <para>
    /// The snapshot's clamped <c>Dt</c>, never <c>Time.deltaTime</c>: it is the step core's own
    /// dash clock is summed from, and handing the body a different one is how a 10 m dash quietly
    /// becomes 11 m on a hitching frame.
    /// </para>
    /// </remarks>
    private void StepCharge()
    {
        _charge.Step(_snapshot.Dt);
    }

    /// <summary>
    /// Shoves every enemy core decided to shove while it was answering the dash's sweep.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An id with no body is skipped in silence, and that is normal rather than exceptional: the
    /// same report that produced the shove may have killed the enemy, and <c>EnemyViews</c> destroys
    /// a body the moment <c>EnemyDespawned</c> arrives — which happens inside
    /// <see cref="StepCharge"/>, a line above this one. A corpse that is gone cannot be pushed, and
    /// there is nothing wrong with that.
    /// </para>
    /// <para>
    /// A <c>for</c> over the count rather than a <c>foreach</c>: the buffer hands out an
    /// <c>IReadOnlyList</c>, and enumerating that would box an enumerator — rarely, but on exactly
    /// the frames a dash is ploughing through a crowd.
    /// </para>
    /// </remarks>
    private void ApplyKnockbacks()
    {
        IReadOnlyList<EnemyKnockbackIntent> knockbacks = _intents.Knockbacks;

        for (int i = 0; i < knockbacks.Count; i++)
        {
            EnemyKnockbackIntent shove = knockbacks[i];

            if (!_enemyViews.TryGet(shove.Id, out EnemyView view) || view == null)
            {
                continue;
            }

            view.Knockback(shove.DirectionXZ.ToUnity(), shove.Distance);
        }
    }

    /// <summary>
    /// Everything the player asked for this frame, in one list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reason command adapters are not <c>ITickable</c>s of their own: registered that way,
    /// the order commands land in — and whether they land before or after the snapshot — would be
    /// whatever order <c>RunScope</c> happened to register them in, decided by an edit somewhere
    /// else entirely and invisible until something went subtly wrong. Here it is four lines in the
    /// file that already owns the frame.
    /// </para>
    /// <para>
    /// Two members now, and the second is exactly the line the shape was built for. The dash press
    /// is read here rather than acted on by the button that made it, so that "a tap became a
    /// command" happens at a known point in the frame — which is what makes CC §5's 0.15 s input
    /// buffer measure the age of the press against the same clock core ends the cooldown on.
    /// </para>
    /// </remarks>
    private void CommandPhase()
    {
        _tapToFocus.Poll();

        if (_input.MovementSkillPressedThisFrame)
        {
            _commands.MovementSkill();
        }
    }

    /// <summary>
    /// Ends the run. Called by VContainer when <c>RunScope</c> is disposed, which is what leaving
    /// the Run scene does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dash is dropped first, and it is the only line here that is not bookkeeping: a Charge in
    /// flight holds the session it reports into, and a body destroyed mid-dash would otherwise be
    /// carrying a handle to a run that has ended. Nothing moves — the capsule stays wherever the
    /// dodge got to, which is where the run ended.
    /// </para>
    /// <para>
    /// The adapter is disabled but not disposed: it was registered as a type, so the scope that
    /// built it disposes it, and doing it twice from two owners is how a double-free starts.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        // Unity's ==: by the time a scene unloads the component may already be destroyed, which is
        // a live reference that only compares equal to null through the engine's operator.
        if (_charge != null)
        {
            _charge.Cancel();
        }

        _session.End();
        _input.Disable();
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }

    /// <summary>
    /// The class to play when no menu chose one: the first the catalog holds.
    /// </summary>
    /// <remarks>
    /// The same bargain <c>RunInstaller</c> makes with the seed (M0-12 rule 6), for the same
    /// workflow — pressing Play with the Run scene already open is how this game is iterated on,
    /// and throwing there would break the fastest loop in development. Silent rather than warning,
    /// unlike the seed: an unseeded run looks identical to a broken one and has to say so, while
    /// "you got the first class" is visible on screen the moment the run starts.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The catalog holds no characters.</exception>
    private ContentId FallbackCharacterId()
    {
        if (_catalog.Characters.Count == 0)
        {
            throw new InvalidOperationException(
                "No run is pending and the content catalog is empty, so there is no class to " +
                "play. Add a CharacterDefinition to BootScope's character list.");
        }

        return _catalog.Characters[0].Id;
    }
}
