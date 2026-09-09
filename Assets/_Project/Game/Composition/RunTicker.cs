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
    private readonly PendingRun _pending;
    private readonly ContentCatalog _catalog;
    private readonly WorldSnapshot _snapshot;
    private readonly SnapshotBuilder _builder;
    private readonly IntentBuffer _intents;
    private readonly PlayerView _player;
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
        PendingRun pending,
        ContentCatalog catalog,
        WorldSnapshot snapshot,
        SnapshotBuilder builder,
        IntentBuffer intents,
        PlayerView player,
        InputAdapter input,
        SpawnPlan spawnPlan,
        TapToFocusAdapter tapToFocus,
        ConeOverlapQuery cone)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _spawnPlan = spawnPlan ?? throw new ArgumentNullException(nameof(spawnPlan));
        _tapToFocus = tapToFocus ?? throw new ArgumentNullException(nameof(tapToFocus));
        _cone = cone ?? throw new ArgumentNullException(nameof(cone));

        _coneHitIds = new int[cone.Capacity];

        // Unity's == rather than `is null`: RunScope supplies this from a serialized field, so a
        // destroyed or unassigned object is a live reference that only compares equal to null
        // through the engine's operator.
        _player = player == null ? throw new ArgumentNullException(nameof(player)) : player;
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
    /// not read yet. The body is applied *after* core has written, with the snapshot's clamped
    /// <c>Dt</c> and not <c>Time.deltaTime</c>, so brain and body take the same step.
    /// </para>
    /// <para>
    /// <c>HasPlayerMove</c> is checked rather than assumed. The buffer deliberately leaves the
    /// previous intent in place when cleared (M0-06), so reading it unconditionally would apply
    /// last frame's velocity on any frame core stayed silent — a player who keeps sliding.
    /// </para>
    /// <para>
    /// <see cref="ResolveConeHits"/> is last, and it is AR §4.3's step 5: the physics a swing
    /// asked for runs <em>after</em> the body has been moved, so the sweep is resolved against the
    /// arena as it stands at the end of this frame rather than against the one the frame started
    /// with. It comes back in through a fact, and the damage it causes is published before this
    /// method returns.
    /// </para>
    /// <para>
    /// Allocates nothing: everything below is a call on an object built once per run, the intent
    /// is a <c>readonly struct</c> taken by <c>in</c>, and the swing's answer goes back as a span
    /// over an array owned since the run started.
    /// </para>
    /// </remarks>
    public void Tick()
    {
        CommandPhase();

        _builder.Build(_snapshot, Time.deltaTime);

        _intents.Clear();

        _session.Tick(_snapshot);

        if (_intents.HasPlayerMove)
        {
            _player.Apply(_intents.PlayerMove, _snapshot.Dt);
        }

        ResolveConeHits();
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
    /// One member today; M1-16's movement-skill press is the next, and the shape is what makes
    /// that a line rather than a decision.
    /// </para>
    /// </remarks>
    private void CommandPhase()
    {
        _tapToFocus.Poll();
    }

    /// <summary>
    /// Ends the run. Called by VContainer when <c>RunScope</c> is disposed, which is what leaving
    /// the Run scene does.
    /// </summary>
    /// <remarks>
    /// The adapter is disabled but not disposed: it was registered as a type, so the scope that
    /// built it disposes it, and doing it twice from two owners is how a double-free starts.
    /// </remarks>
    public void Dispose()
    {
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
