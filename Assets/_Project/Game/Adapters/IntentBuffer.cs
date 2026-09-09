using System.Collections.Generic;
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
    /// <summary>
    /// Room for more cone requests than a tick can plausibly produce — one basic attack and a
    /// Charge sweep is two (M1-15). Preallocated so the list stops growing after the first frame
    /// and the per-frame write allocates nothing.
    /// </summary>
    private const int ConeHitCapacity = 4;

    private readonly List<ConeHitIntent> _coneHits = new(ConeHitCapacity);

    private PlayerMoveIntent _playerMove;

    /// <summary>Whether core wrote a player move intent during this tick.</summary>
    public bool HasPlayerMove { get; private set; }

    /// <summary>
    /// The move intent core wrote this tick. Undefined while <see cref="HasPlayerMove"/> is
    /// <c>false</c>: it is the previous tick's leftovers, not a zero. Callers check the flag.
    /// </summary>
    public PlayerMoveIntent PlayerMove => _playerMove;

    /// <summary>
    /// Every cone core asked to have resolved this tick, in the order it asked. Empty on most
    /// ticks — the Censer swings three times a second, not sixty.
    /// </summary>
    /// <remarks>
    /// Read it with a <c>for</c> over <see cref="IReadOnlyCollection{T}.Count"/>. A
    /// <c>foreach</c> over the interface boxes an enumerator, and this is read every frame on a
    /// phone; the same reason nothing in core allocates on a tick path.
    /// </remarks>
    public IReadOnlyList<ConeHitIntent> ConeHits => _coneHits;

    void IIntentSink.PlayerMove(in PlayerMoveIntent intent)
    {
        _playerMove = intent;
        HasPlayerMove = true;
    }

    void IIntentSink.ConeHit(in ConeHitIntent intent)
    {
        _coneHits.Add(intent);
    }

    /// <summary>
    /// Drops every intent, ready for the next tick to refill. Runs once per frame
    /// <em>before</em> <c>session.Tick</c>, never after — clearing afterwards would erase the
    /// intents the views have not read yet.
    /// </summary>
    /// <remarks>
    /// The cone list is genuinely emptied, where <see cref="HasPlayerMove"/> is only lowered, and
    /// the asymmetry is the two shapes rather than an inconsistency: a list has no flag to check
    /// first, so leftovers would read as this tick's swings and the body would sweep the same cone
    /// every frame until the next one replaced it. Emptying keeps the capacity, so it allocates
    /// nothing.
    /// </remarks>
    public void Clear()
    {
        HasPlayerMove = false;
        _coneHits.Clear();
    }
}
