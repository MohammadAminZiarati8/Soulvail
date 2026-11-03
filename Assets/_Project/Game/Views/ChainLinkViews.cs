using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

/// <summary>
/// The line a chain blast runs down to the bomber it lit (JG-21e rule 4): from the blast to the lit
/// body, 0.15 m wide and 0.3 m above the floor, fading over the fuse.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TelegraphRings"/>' shape: a listener subscribed in its constructor, no id index and no
/// un-rent path, because a link's whole life is decided when it is drawn. Every body is built in the
/// constructor, under a root that is inactive while they are made (AR §18.2), so nothing
/// instantiates once the run has begun; past capacity the oldest link is redrawn (rule 6).
/// </para>
/// <para>
/// The fade is the material's colour scaled toward black through a property block — the material is
/// additive, so black is invisible — rather than an alpha, which URP's unlit additive ignores.
/// </para>
/// </remarks>
public sealed class ChainLinkViews : IDisposable
{
    /// <summary>A link's width, in metres.</summary>
    public const float Width = 0.15f;

    /// <summary>How far above the floor a link runs, in metres.</summary>
    public const float Lift = 0.3f;

    private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

    private readonly GameObject _holder;
    private readonly LineRenderer[] _lines;
    private readonly float[] _elapsed;
    private readonly float[] _seconds;
    private readonly int[] _order;
    private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
    private readonly IDisposable _litSubscription;

    private int _nextOrder;
    private bool _disposed;

    /// <param name="hub">The run's event hub, subscribed for this object's life.</param>
    /// <param name="prefab">The one link body: a two-point world-space line with the link material.</param>
    /// <param name="root">Where the links are parented, or null for the scene root.</param>
    /// <param name="capacity">How many links can be drawn at once.</param>
    /// <exception cref="ArgumentNullException"><paramref name="hub"/> or <paramref name="prefab"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity"/> is not positive.</exception>
    public ChainLinkViews(DomainEventHub hub, LineRenderer prefab, Transform root, int capacity)
    {
        if (hub is null)
        {
            throw new ArgumentNullException(nameof(hub));
        }

        if (prefab == null)
        {
            throw new ArgumentNullException(nameof(prefab));
        }

        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "A link pool needs room for one.");
        }

        _holder = new GameObject("Chain links (pooled)");
        _holder.SetActive(false);

        if (root != null)
        {
            _holder.transform.SetParent(root, false);
        }

        _lines = new LineRenderer[capacity];
        _elapsed = new float[capacity];
        _seconds = new float[capacity];
        _order = new int[capacity];

        for (int i = 0; i < capacity; i++)
        {
            LineRenderer line = Object.Instantiate(prefab, _holder.transform, false);
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = Width;
            line.endWidth = Width;
            line.gameObject.SetActive(false);
            _lines[i] = line;
        }

        _holder.SetActive(true);

        _litSubscription = hub.Subscribe<EnemyChainLit>(OnChainLit);
    }

    /// <summary>How many links are drawn right now.</summary>
    public int ActiveCount { get; private set; }

    /// <summary>Fades every drawn link by <paramref name="dt"/> and puts away the spent ones.</summary>
    /// <remarks>Called once a frame by <c>RunTicker</c> with <c>snapshot.Dt</c>, the fuse's own clock.</remarks>
    public void Tick(float dt)
    {
        if (_disposed || !(dt > 0f) || float.IsInfinity(dt))
        {
            return;
        }

        for (int i = 0; i < _lines.Length; i++)
        {
            LineRenderer line = _lines[i];

            if (line == null || !line.gameObject.activeSelf)
            {
                continue;
            }

            _elapsed[i] += dt;

            if (_elapsed[i] >= _seconds[i])
            {
                line.gameObject.SetActive(false);
                ActiveCount--;

                continue;
            }

            Fade(line, 1f - (_elapsed[i] / _seconds[i]));
        }
    }

    /// <summary>Stops listening and destroys every link.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _litSubscription.Dispose();

        // Unity's ==: the scene may already have destroyed it on the way out.
        if (_holder != null)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(_holder);
            }
            else
            {
                Object.DestroyImmediate(_holder);
            }
        }

        ActiveCount = 0;
    }

    private void OnChainLit(EnemyChainLit evt)
    {
        if (_disposed || !(evt.Seconds > 0f))
        {
            return;
        }

        int slot = FreeOrOldest();
        LineRenderer line = _lines[slot];

        if (line == null)
        {
            return;
        }

        Vector3 from = evt.From.ToUnity();
        Vector3 to = evt.To.ToUnity();
        from.y = Lift;
        to.y = Lift;

        line.SetPosition(0, from);
        line.SetPosition(1, to);

        if (!line.gameObject.activeSelf)
        {
            line.gameObject.SetActive(true);
            ActiveCount++;
        }

        _elapsed[slot] = 0f;
        _seconds[slot] = evt.Seconds;
        _order[slot] = ++_nextOrder;

        Fade(line, 1f);
    }

    /// <summary>The first idle link, or else the one drawn longest ago (rule 6).</summary>
    private int FreeOrOldest()
    {
        int oldest = 0;

        for (int i = 0; i < _lines.Length; i++)
        {
            if (_lines[i] != null && !_lines[i].gameObject.activeSelf)
            {
                return i;
            }

            if (_order[i] < _order[oldest])
            {
                oldest = i;
            }
        }

        return oldest;
    }

    private void Fade(LineRenderer line, float strength)
    {
        Color colour = Palette.Danger * strength;
        colour.a = 1f;

        line.GetPropertyBlock(_block);
        _block.SetColor(_baseColorId, colour);
        line.SetPropertyBlock(_block);
    }
}
