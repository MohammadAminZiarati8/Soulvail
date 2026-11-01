using System;
using Soulvail.Core.Ports;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>Answers every shield-charge lane question after the frame's transform flush.</summary>
/// <remarks>
/// Cover only: a stone is what dazes the Warlord, so boundary walls and trees must not shorten the
/// lane. Core decides the lane length and the daze; this reports how far the sphere travelled.
/// </remarks>
public sealed class LaneProbeAdapter
{
    private readonly LayerMask _cover;

    public LaneProbeAdapter(LayerMask cover)
    {
        if (cover.value == 0)
        {
            throw new ArgumentException("Lane probes require the Cover mask.", nameof(cover));
        }
        _cover = cover;
    }

    public bool Probe(in LaneProbeIntent probe, out float distance)
    {
        distance = 0f;
        Vector3 direction = probe.Direction.ToUnity();
        if (!(probe.Reach > 0f) || !float.IsFinite(probe.Reach)
            || !(probe.Radius > 0f) || !float.IsFinite(probe.Radius)
            || !(direction.sqrMagnitude > 0f))
        {
            return false;
        }
        Vector3 start = probe.From.ToUnity() + Vector3.up * LineOfSightSense.EyeHeight;
        bool hit = Physics.SphereCast(start, probe.Radius, direction.normalized, out RaycastHit contact,
            probe.Reach, _cover, QueryTriggerInteraction.Ignore);
        if (hit)
        {
            distance = contact.distance;
        }
        return hit;
    }

    public void Resolve(IntentBuffer intents, IRunSession session)
    {
        for (int i = 0; i < intents.LaneProbes.Count; i++)
        {
            LaneProbeIntent probe = intents.LaneProbes[i];
            bool hit = Probe(probe, out float distance);
            session.ReportLaneStop(probe.EnemyId, hit, distance);
        }
    }
}
