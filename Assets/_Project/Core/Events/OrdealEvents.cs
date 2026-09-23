using Soulvail.Core.Content;

namespace Soulvail.Core.Events;

// GD §13.4's Ordeals, as domain events. Grouped per module, `VeilrotEvents.cs`' precedent: an event
// is three lines, and reading a module's vocabulary in one place is worth more than the rule. See
// AR §5, §8 and <../../../../Docs/adr/0004-scoped-domain-events.md>.
//
// **Nothing here is published by a restore** (M6-06a rule 7): a resume is not news, and an
// `OrdealApplied` raised inside `RunSession.Start` would reach a HUD that has not subscribed yet.

/// <summary>
/// One more Ordeal is in force. Carries the depth, because GD §13.4's whole point is how deep.
/// </summary>
/// <remarks>
/// Published by <c>Ordeals.OnStageEntered</c> on a scheduled boundary with stock left, and by
/// nothing else. It goes out inside <c>StageFlow.Advance</c>, above the composition and above that
/// stage's <see cref="StageArrived"/>, so a reader sees the Ordeal before the arena it arrived with.
/// </remarks>
public readonly struct OrdealApplied
{
    public OrdealApplied(ContentId id, int stage, int count)
    {
        Id = id;
        Stage = stage;
        Count = count;
    }

    /// <summary>Which Ordeal — resolve its name and description against the mode's pool.</summary>
    public readonly ContentId Id;

    /// <summary>The stage it was dealt on entering.</summary>
    public readonly int Stage;

    /// <summary>How many are now in force, this one included.</summary>
    public readonly int Count;
}
