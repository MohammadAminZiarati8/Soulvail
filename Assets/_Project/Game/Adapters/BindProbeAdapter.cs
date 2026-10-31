using System;
using Soulvail.Core.Ports;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>Answers every binding collider question after the frame's transform flush.</summary>
public sealed class BindProbeAdapter
{
    public const float Radius = 0.2f;
    private readonly LayerMask _obstacles;

    public BindProbeAdapter(LayerMask cover)
    {
        if (cover.value == 0)
        {
            throw new ArgumentException("Binding probes require the Cover mask.", nameof(cover));
        }
        // Jungle boundary walls and its existing tree colliders are Default solids. The
        // floor is below the eye-height sphere; enemies and triggers are excluded.
        _obstacles = cover.value | LayerMask.GetMask("Default");
    }

    public bool Probe(in BindProbeIntent probe, out Vector3 point)
    {
        point = default;
        Vector3 direction = probe.Direction.ToUnity();
        if (!(probe.Reach > 0f) || !float.IsFinite(probe.Reach)
            || !(direction.sqrMagnitude > 0f))
        {
            return false;
        }
        Vector3 start = probe.From.ToUnity() + Vector3.up * LineOfSightSense.EyeHeight;
        bool hit = Physics.SphereCast(start, Radius, direction.normalized, out RaycastHit contact,
            probe.Reach, _obstacles, QueryTriggerInteraction.Ignore);
        if (hit)
        {
            point = contact.point;
        }
        return hit;
    }

    public void Resolve(IntentBuffer intents, IRunSession session)
    {
        for (int i = 0; i < intents.BindProbes.Count; i++)
        {
            BindProbeIntent probe = intents.BindProbes[i];
            bool hit = Probe(probe, out Vector3 point);
            session.ReportBindAnchor(probe.TargetId, hit, point.ToNum());
        }
    }
}
