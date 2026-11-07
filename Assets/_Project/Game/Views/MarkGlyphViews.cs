using System;
using System.Collections.Generic;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

/// <summary>Draws the Mark over the enemy body independently of its status row.</summary>
public sealed class MarkGlyphViews : ILateTickable, IDisposable
{
    private readonly GameObject _root;
    private readonly SpriteRenderer _prefab;
    private readonly Transform _viewer;
    private readonly Func<int, EnemyView> _findEnemy;
    private readonly Dictionary<int, (Collider Body, SpriteRenderer Glyph)> _active = new();
    private readonly Stack<SpriteRenderer> _free = new();
    private readonly IDisposable[] _subscriptions;

    public MarkGlyphViews(DomainEventHub hub, SpriteRenderer prefab, Transform parent, Transform viewer,
        Func<int, EnemyView> findEnemy, int prewarm)
    {
        _prefab = prefab;
        _viewer = viewer;
        _findEnemy = findEnemy;
        _root = new GameObject("Mark glyphs (pooled)");
        _root.SetActive(false);
        _root.transform.SetParent(parent, false);
        for (int i = 0; i < prewarm; i++)
        {
            _free.Push(CreateGlyph());
        }

        _root.SetActive(true);
        _subscriptions = new[]
        {
            hub.Subscribe<EnemyStatusChanged>(OnStatus),
            hub.Subscribe<EnemyDied>(evt => Hide(evt.Id)),
            hub.Subscribe<EnemyDespawned>(evt => Hide(evt.Id)),
            hub.Subscribe<StageTransitionStarted>(_ => Clear()),
        };
    }

    public void LateTick()
    {
        foreach (var entry in _active.Values)
        {
            if (entry.Body != null)
            {
                Place(entry.Body, entry.Glyph);
            }
        }
    }

    private void OnStatus(EnemyStatusChanged evt)
    {
        if (evt.Status.Value != EnemyStatuses.MarkedKey)
        {
            return;
        }

        if (!(evt.Seconds > 0f))
        {
            Hide(evt.EnemyId);
            return;
        }

        if (_active.ContainsKey(evt.EnemyId))
        {
            return;
        }

        Collider body = _findEnemy(evt.EnemyId)?.Body;
        if (body == null)
        {
            return;
        }

        SpriteRenderer glyph = _free.Count > 0 ? _free.Pop() : CreateGlyph();
        Place(body, glyph);
        glyph.gameObject.SetActive(true);
        _active.Add(evt.EnemyId, (body, glyph));
    }

    private SpriteRenderer CreateGlyph()
    {
        SpriteRenderer glyph = Object.Instantiate(_prefab, _root.transform);
        glyph.gameObject.SetActive(false);
        glyph.color = Palette.Player;
        return glyph;
    }

    private void Place(Collider body, SpriteRenderer glyph)
    {
        Bounds bounds = body.bounds;
        Vector3 head = new(bounds.center.x, bounds.max.y, bounds.center.z);
        glyph.transform.SetPositionAndRotation(head + Vector3.up * 0.42f, _viewer.rotation);
    }

    private void Hide(int id)
    {
        if (_active.Remove(id, out var entry))
        {
            Release(entry.Glyph);
        }
    }

    private void Release(SpriteRenderer glyph)
    {
        glyph.gameObject.SetActive(false);
        _free.Push(glyph);
    }

    private void Clear()
    {
        foreach (var entry in _active.Values)
        {
            Release(entry.Glyph);
        }

        _active.Clear();
    }

    public void Dispose()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        if (_root != null)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(_root);
            }
            else
            {
                Object.DestroyImmediate(_root);
            }
        }
    }
}
