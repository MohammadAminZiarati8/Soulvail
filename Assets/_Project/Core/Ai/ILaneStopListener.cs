namespace Soulvail.Core.Ai;

/// <summary>Receives the body's obstacle fact for a pending charge lane.</summary>
public interface ILaneStopListener
{
    void OnLaneStop(bool hit, float distance);
}
