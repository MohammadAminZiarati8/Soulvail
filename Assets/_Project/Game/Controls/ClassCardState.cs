namespace Soulvail.Game.Controls;

/// <summary>
/// How a <see cref="ClassCard"/> was last drawn. Three members, and the third is what M5-08a is
/// about: a card that may be read and may not be played (M6-09b).
/// </summary>
public enum ClassCardState
{
    /// <summary>Not in use — <c>ClassCard.Clear</c>'s state.</summary>
    Hidden,

    /// <summary>Owned. Tapping starts a run.</summary>
    Owned,

    /// <summary>Priced. Tapping buys it, or does nothing when the balance is short.</summary>
    Locked,
}
