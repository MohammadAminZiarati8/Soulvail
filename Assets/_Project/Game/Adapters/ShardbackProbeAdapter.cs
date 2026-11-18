using System;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Arena;
using Soulvail.Game.Views;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>Measures tagged swept footprints after the frame's single transform flush.</summary>
public sealed class ShardbackProbeAdapter
{
    private readonly LayerMask _cover;
    private readonly ArenaPool _arenas;
    private readonly RaycastHit[] _hits = new RaycastHit[64];
    private readonly Collider[] _overlaps = new Collider[64];

    public ShardbackProbeAdapter(LayerMask cover, ArenaPool arenas)
    {
        if (cover.value == 0)
        {
            throw new ArgumentException("Cover mask required.", nameof(cover));
        }
        _arenas = arenas ?? throw new ArgumentNullException(nameof(arenas));
        _cover = cover;
    }

    public ShardbackStop Probe(in LaneProbeIntent probe, Rect playBounds)
    {
        if (!probe.Key.IsValid || probe.EnemyId <= 0 || probe.Lane < 0 || probe.Lane > 3
            || !Finite(probe.From.ToUnity()) || !Finite(probe.Direction.ToUnity())
            || !(probe.Reach > 0f) || !float.IsFinite(probe.Reach)
            || !(probe.Radius > 0f) || !float.IsFinite(probe.Radius)
            || (probe.Direction.X == 0f && probe.Direction.Z == 0f))
        {
            throw new ArgumentException("A valid tagged probe is required.", nameof(probe));
        }

        var unknown = Answer(probe, ShardbackStopKind.Unknown, 0f);
        if (!float.IsFinite(playBounds.xMin) || !float.IsFinite(playBounds.xMax)
            || !float.IsFinite(playBounds.yMin) || !float.IsFinite(playBounds.yMax)
            || !(playBounds.width > 0f) || !(playBounds.height > 0f))
        {
            return unknown;
        }

        // Scale before normalizing so even finite, very large directions stay usable.
        Vector3 direction = new(probe.Direction.X, 0f, probe.Direction.Z);
        direction /= Mathf.Max(Mathf.Abs(direction.x), Mathf.Abs(direction.z));
        direction.Normalize();
        bool charge = probe.Lane == 0;
        Vector3 start = probe.From.ToUnity() + Vector3.up * LineOfSightSense.EyeHeight;
        Vector3 half = new(0.6f, 0.5f, 0.01f);
        Quaternion rotation = Quaternion.LookRotation(direction);
        Vector3 centre = charge ? start : start - direction * half.z;
        int overlaps = charge
            ? Physics.OverlapSphereNonAlloc(centre, 1f, _overlaps, _cover, QueryTriggerInteraction.Ignore)
            : Physics.OverlapBoxNonAlloc(centre, half, _overlaps, rotation, _cover, QueryTriggerInteraction.Ignore);
        if (overlaps == _overlaps.Length)
        {
            return unknown;
        }
        float extentX = charge ? 1f : Mathf.Abs(direction.z) * half.x + Mathf.Abs(direction.x) * half.z;
        float extentZ = charge ? 1f : Mathf.Abs(direction.x) * half.x + Mathf.Abs(direction.z) * half.z;
        float boundary = Boundary(centre, direction, extentX, extentZ, playBounds);
        ShardbackStop best = boundary <= probe.Reach
            ? Answer(probe, ShardbackStopKind.Boundary, boundary)
            : Answer(probe, ShardbackStopKind.None, probe.Reach);
        for (int i = 0; i < overlaps; i++)
        {
            Consider(probe, _overlaps[i], 0f, ref best);
        }

        int hits = charge
            ? Physics.SphereCastNonAlloc(centre, 1f, direction, _hits, probe.Reach, _cover, QueryTriggerInteraction.Ignore)
            : Physics.BoxCastNonAlloc(centre, half, direction, _hits, rotation, probe.Reach, _cover, QueryTriggerInteraction.Ignore);
        if (hits == _hits.Length)
        {
            return unknown;
        }
        for (int i = 0; i < hits; i++)
        {
            Consider(probe, _hits[i].collider, _hits[i].distance, ref best);
        }
        return best;
    }

    public void Resolve(IntentBuffer intents, IRunSession session)
    {
        for (int i = 0; i < intents.LaneProbes.Count; i++)
        {
            LaneProbeIntent probe = intents.LaneProbes[i];
            if (!probe.Key.IsValid)
            {
                continue;
            }
            session.ReportShardbackStop(Probe(probe, _arenas.Active == null ? default : _arenas.Active.PlayBounds));
        }
    }

    private void Consider(in LaneProbeIntent probe, Collider collider, float distance, ref ShardbackStop best)
    {
        ShardbackPillar pillar = collider.GetComponentInParent<ShardbackPillar>();
        bool owned = pillar != null && pillar.BossId == probe.EnemyId && pillar.Lifetime == probe.Key.Lifetime
            && pillar.Slot >= 0 && pillar.Slot <= 2;
        ShardbackStop candidate = Answer(probe, owned ? ShardbackStopKind.Pillar : ShardbackStopKind.Cover,
            Mathf.Clamp(distance, 0f, probe.Reach), owned ? pillar.Slot : -1);
        if (candidate.Distance < best.Distance || (candidate.Distance == best.Distance
            && (Priority(candidate.Kind) < Priority(best.Kind)
                || (candidate.Kind == best.Kind && candidate.PillarSlot < best.PillarSlot))))
        {
            best = candidate;
        }
    }

    private static int Priority(ShardbackStopKind kind) => kind switch
    {
        ShardbackStopKind.Boundary => 0,
        ShardbackStopKind.Cover => 1,
        ShardbackStopKind.Pillar => 2,
        _ => 3
    };

    private static float Boundary(Vector3 centre, Vector3 direction, float x, float z, Rect bounds)
    {
        float minX = bounds.xMin + x, maxX = bounds.xMax - x;
        float minZ = bounds.yMin + z, maxZ = bounds.yMax - z;
        if (centre.x < minX || centre.x > maxX || centre.z < minZ || centre.z > maxZ)
        {
            return 0f;
        }
        float dx = direction.x > 0f ? (maxX - centre.x) / direction.x
            : direction.x < 0f ? (minX - centre.x) / direction.x : float.PositiveInfinity;
        float dz = direction.z > 0f ? (maxZ - centre.z) / direction.z
            : direction.z < 0f ? (minZ - centre.z) / direction.z : float.PositiveInfinity;
        return Mathf.Max(0f, Mathf.Min(dx, dz));
    }

    private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    private static ShardbackStop Answer(in LaneProbeIntent probe, ShardbackStopKind kind, float distance, int slot = -1) =>
        new(probe.EnemyId, probe.Key, probe.Lane, kind, distance, slot);
}
