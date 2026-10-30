using System.Collections.Generic;
using Soulvail.Game.Arena;
using Soulvail.Game.Views;
using UnityEngine;
using UnityEngine.AI;

namespace Soulvail.Game.Adapters;

/// <summary>A cached floor lattice, measured again only when the room or its cover changes.</summary>
public sealed class OpenGroundSense
{
    public const float Step = 1f, CoverClearance = 1f, FloorProbe = 0.3f;
    public const int MaxSpots = 1024;

    private readonly LayerMask _cover;
    private readonly CoverViews _covers;
    private readonly List<System.Numerics.Vector3> _spots = new List<System.Numerics.Vector3>(MaxSpots);
    private ArenaView _arena;
    private int _version = -1;

    public OpenGroundSense(LayerMask cover, CoverViews covers)
    {
        _cover = cover;
        _covers = covers;
        Spots = _spots.AsReadOnly();
    }

    public IReadOnlyList<System.Numerics.Vector3> Spots { get; }
    public int Senses { get; private set; }

    public void Sense(ArenaView arena)
    {
        int version = _covers is null ? 0 : _covers.Version;
        if (arena == _arena && version == _version && (arena != null || _spots.Count == 0))
        {
            return;
        }

        _arena = arena;
        _version = version;
        Senses++;
        _spots.Clear();
        Physics.SyncTransforms();
        if (arena == null || arena.Surface == null || arena.Surface.navMeshData == null)
        {
            return;
        }

        var surface = arena.Surface;
        Bounds bounds = surface.navMeshData.sourceBounds;
        var filter = new NavMeshQueryFilter { agentTypeID = surface.agentTypeID, areaMask = NavMesh.AllAreas };
        // Bounds include walls, so their centre height is not the floor. PlayerStart is authored
        // on the standing floor. The surface's rigid transform puts its local bounds in the room.
        for (float z = Mathf.Ceil(bounds.min.z / Step) * Step; z <= bounds.max.z; z += Step)
        {
            for (float x = Mathf.Ceil(bounds.min.x / Step) * Step; x <= bounds.max.x; x += Step)
            {
                Vector3 point = surface.transform.position + surface.transform.rotation * new Vector3(x, 0f, z);
                point.y = arena.PlayerStart.y;
                if (!NavMesh.SamplePosition(point, out NavMeshHit hit, FloorProbe, filter)
                    || Physics.CheckCapsule(hit.position, hit.position + Vector3.up * LineOfSightSense.EyeHeight,
                        CoverClearance, _cover, QueryTriggerInteraction.Ignore))
                {
                    continue;
                }

                _spots.Add(hit.position.ToNum());
                if (_spots.Count == MaxSpots)
                {
                    return;
                }
            }
        }
    }
}
