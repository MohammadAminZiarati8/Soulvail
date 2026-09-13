using Soulvail.Core.Ports;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// An <see cref="IDomainEvents"/> that throws every payload away without touching it. For
/// allocation rows, and for nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RecordingEvents"/> stores each payload in a <c>List&lt;object&gt;</c>, which boxes the
/// struct — so measuring a tick through it reports the fake's allocation as core's, and the row
/// fails for a reason that has nothing to do with the code under test. The real <c>DomainEventHub</c>
/// does not box: it fans out through a typed channel per event type, precisely so that a publish on
/// a hot path is free (ADR-0004). This is the stand-in for that property.
/// </para>
/// <para>
/// It lived nested and private inside <c>ChaserBehaviourTests</c> from M1-18, under a note saying it
/// would move here the day a second fixture needed one. M2-07b is that day:
/// <c>SpitterBehaviourTests</c> measures a behaviour that publishes a telegraph <em>and</em> fires a
/// shot on every cycle, so both of its allocation rows need a sink that costs nothing.
/// </para>
/// </remarks>
public sealed class SilentEvents : IDomainEvents
{
    /// <inheritdoc />
    public void Publish<T>(in T evt)
        where T : struct
    {
    }
}
