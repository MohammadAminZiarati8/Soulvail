using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The Unity side of <see cref="IIntentSink"/>: a per-frame mailbox core fills during
/// <c>Tick</c> and the views empty straight after. One instance per run, registered in
/// <c>RunScope</c> (AR §7).
/// </summary>
/// <remarks>
/// <para>
/// The intent lives in a field, so a frame's write-then-read costs nothing and there is
/// nothing to preallocate beyond the buffer itself. M0-16's <c>RunTicker</c> owns the order:
/// <see cref="Clear"/>, then <c>session.Tick</c>, then the views read.
/// </para>
/// <para>
/// <see cref="Clear"/> lowers <see cref="HasPlayerMove"/> and deliberately leaves the stored
/// intent alone — the same bargain <c>WorldSnapshot.Clear</c> makes with its enemy array. The
/// value is undefined while the flag is down and every reader checks the flag first; zeroing
/// it would trade one wrong answer for another while suggesting the check were optional.
/// </para>
/// <para>
/// The writing side is an explicit implementation so that <see cref="PlayerMove"/> can be the
/// property the views read. The shared name is intentional and legal — an explicit member's
/// name is <c>IIntentSink.PlayerMove</c>, which never enters this class's declaration space —
/// and it makes the type enforce the direction of the boundary: core, holding an
/// <see cref="IIntentSink"/>, can only write; a view, holding an <see cref="IntentBuffer"/>,
/// can only read.
/// </para>
/// </remarks>
public sealed class IntentBuffer : IIntentSink
{
    private PlayerMoveIntent _playerMove;

    /// <summary>Whether core wrote a player move intent during this tick.</summary>
    public bool HasPlayerMove { get; private set; }

    /// <summary>
    /// The move intent core wrote this tick. Undefined while <see cref="HasPlayerMove"/> is
    /// <c>false</c>: it is the previous tick's leftovers, not a zero. Callers check the flag.
    /// </summary>
    public PlayerMoveIntent PlayerMove => _playerMove;

    void IIntentSink.PlayerMove(in PlayerMoveIntent intent)
    {
        _playerMove = intent;
        HasPlayerMove = true;
    }

    /// <summary>
    /// Drops every intent, ready for the next tick to refill. Runs once per frame
    /// <em>before</em> <c>session.Tick</c>, never after — clearing afterwards would erase the
    /// intents the views have not read yet.
    /// </summary>
    public void Clear()
    {
        HasPlayerMove = false;
    }
}
