using Soulvail.Core.Ports;

namespace Soulvail.Core.Save;

/// <summary>Discards level and XP events raised while building a test-start snapshot.</summary>
internal sealed class TestStartEvents : IDomainEvents
{
    public void Publish<T>(in T evt)
        where T : struct
    {
    }
}
