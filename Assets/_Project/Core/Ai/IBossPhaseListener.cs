namespace Soulvail.Core.Ai;

/// <summary>An inner behaviour that abandons its current move on a phase crossing.</summary>
public interface IBossPhaseListener
{
    void OnPhaseEntered(int phase);
}
