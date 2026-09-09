using System;
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
        InputAdapter input)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _input = input ?? throw new ArgumentNullException(nameof(input));

        // Unity's == rather than `is null`: RunScope supplies this from a serialized field, so a
        // destroyed or unassigned object is a live reference that only compares equal to null
        // through the engine's operator.
        _player = player == null ? throw new ArgumentNullException(nameof(player)) : player;
    }

    /// <summary>
    /// Starts the run: the screen stays awake, the stick starts being read, and core is told which
    /// class to play.
    /// </summary>
    public void Start()
    {
        // Only for the length of a run, never app-wide — a menu has no business burning battery.
        // Restored in Dispose.
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        _input.Enable();

        // SpawnPlan.Empty until something authors one: EnemyDefinition and the first Husk asset
        // arrive in M1-07, the arena's arrival set in M2-05. Empty rather than omitted, so this
        // line says out loud that the arena starts bare — see RunConfig.
        _session.Start(new RunConfig(
            _pending.IsSet ? _pending.CharacterId : FallbackCharacterId(),
            SpawnPlan.Empty));
    }

    /// <summary>
    /// One frame: sense, decide, act.
    /// </summary>
    /// <remarks>
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
    /// Allocates nothing: everything below is a call on an object built once per run, and the
    /// intent is a <c>readonly struct</c> taken by <c>in</c>.
    /// </para>
    /// </remarks>
    public void Tick()
    {
        _builder.Build(_snapshot, Time.deltaTime);

        _intents.Clear();

        _session.Tick(_snapshot);

        if (_intents.HasPlayerMove)
        {
            _player.Apply(_intents.PlayerMove, _snapshot.Dt);
        }
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
