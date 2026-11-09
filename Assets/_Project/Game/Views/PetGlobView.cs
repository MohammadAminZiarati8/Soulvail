using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

/// <summary>
/// BT-07b: draws the boar's globs. One pooled body per glob core holds in flight, built up front:
/// a toxic-green core with a thin cyan trail, and a small green splat where a glob hits.
/// </summary>
public sealed class PetGlobView : ILateTickable, IDisposable
{
    public delegate ReadOnlySpan<PetGlob> ReadFlight();

    /// <summary>CH §3.7's toxic green: darker than the Shaman's lime so a glob never reads as an enemy shot.</summary>
    public static readonly Color CoreColour = new Color32(0x16, 0xA3, 0x4A, 0xFF);

    /// <summary>The Packleader's cyan, so the glob reads as the player's side.</summary>
    public static readonly Color TrailColour = Palette.Player;

    public const float SplatSeconds = 0.45f;
    public const float Lift = 0.6f;
    private const float SplatLift = 0.03f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private readonly GameObject _root;
    private readonly GameObject[] _bodies = new GameObject[PetSpit.Capacity];
    private readonly Transform[] _cores = new Transform[PetSpit.Capacity];
    private readonly TrailRenderer[] _trails = new TrailRenderer[PetSpit.Capacity];
    private readonly Transform[] _splats = new Transform[PetSpit.Capacity];
    private readonly Vector3[] _splatScales = new Vector3[PetSpit.Capacity];
    private readonly int[] _ids = new int[PetSpit.Capacity];
    private readonly float[] _splatLeft = new float[PetSpit.Capacity];
    private readonly ReadFlight _readFlight;
    private readonly IDisposable[] _subscriptions;

    public PetGlobView(DomainEventHub hub, GameObject prefab, Transform parent, ReadFlight readFlight)
    {
        if (prefab == null)
        {
            throw new ArgumentNullException(nameof(prefab), "PetGlobView needs the PetGlob prefab.");
        }

        _readFlight = readFlight;
        _root = new GameObject("Pet globs (pooled)");
        _root.SetActive(false);
        _root.transform.SetParent(parent, false);

        var block = new MaterialPropertyBlock();
        block.SetColor(BaseColorId, CoreColour);
        block.SetColor(ColorId, CoreColour);
        Color trailEnd = TrailColour;
        trailEnd.a = 0f;

        for (int i = 0; i < _bodies.Length; i++)
        {
            GameObject body = Object.Instantiate(prefab, _root.transform, false);
            Transform core = body.transform.Find("Core");
            Transform splat = body.transform.Find("Splat");
            TrailRenderer trail = core != null ? core.GetComponent<TrailRenderer>() : null;
            if (core == null || splat == null || trail == null)
            {
                throw new ArgumentException(
                    "The PetGlob prefab needs a 'Core' child with a TrailRenderer and a 'Splat' child.",
                    nameof(prefab));
            }

            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is not TrailRenderer)
                {
                    renderer.SetPropertyBlock(block);
                }
            }

            trail.startColor = TrailColour;
            trail.endColor = trailEnd;
            _bodies[i] = body;
            _cores[i] = core;
            _trails[i] = trail;
            _splats[i] = splat;
            _splatScales[i] = splat.localScale;
            Hide(i);
        }

        _root.SetActive(true);
        _subscriptions = new[]
        {
            hub.Subscribe<PetSpat>(OnSpat),
            hub.Subscribe<GlobLanded>(OnLanded),
            hub.Subscribe<StageTransitionStarted>(_ => Clear()),
            hub.Subscribe<StageArrived>(_ => Clear()),
            hub.Subscribe<RunEnded>(_ => Clear()),
        };
    }

    /// <summary>The pooled bodies, for tests: the same objects for the whole run.</summary>
    public ReadOnlySpan<GameObject> Bodies => _bodies;

    public int ActiveCount
    {
        get
        {
            int count = 0;
            foreach (int id in _ids)
            {
                count += id != 0 ? 1 : 0;
            }

            return count;
        }
    }

    public void LateTick() => Draw(Time.deltaTime);

    public void Draw(float dt)
    {
        ReadOnlySpan<PetGlob> flight = _readFlight != null ? _readFlight() : default;
        for (int i = 0; i < _bodies.Length; i++)
        {
            if (_ids[i] != 0)
            {
                bool found = false;
                foreach (ref readonly PetGlob glob in flight)
                {
                    if (glob.Id == _ids[i])
                    {
                        _cores[i].position = glob.Position.ToUnity() + Vector3.up * Lift;
                        found = true;
                        break;
                    }
                }

                // Core clears flight on stage clear without landing each glob.
                if (!found)
                {
                    Hide(i);
                }
            }
            else if (_splatLeft[i] > 0f)
            {
                _splatLeft[i] -= dt;
                if (_splatLeft[i] > 0f)
                {
                    _splats[i].localScale = _splatScales[i] * (0.5f + 0.5f * (_splatLeft[i] / SplatSeconds));
                }
                else
                {
                    Hide(i);
                }
            }
        }
    }

    private void OnSpat(PetSpat evt)
    {
        int slot = FreeSlot();
        if (slot < 0)
        {
            return;
        }

        _ids[slot] = evt.GlobId;
        _splatLeft[slot] = 0f;
        _splats[slot].gameObject.SetActive(false);
        _cores[slot].position = evt.From.ToUnity() + Vector3.up * Lift;
        _cores[slot].gameObject.SetActive(true);
        _trails[slot].Clear();
        _trails[slot].emitting = true;
        _bodies[slot].SetActive(true);
    }

    private void OnLanded(GlobLanded evt)
    {
        for (int i = 0; i < _ids.Length; i++)
        {
            if (_ids[i] != evt.GlobId)
            {
                continue;
            }

            _ids[i] = 0;
            _trails[i].emitting = false;
            _cores[i].gameObject.SetActive(false);
            // BT-07b rule 4: a splat only where the glob struck; cover just swallows it.
            if (evt.Hit)
            {
                Vector3 at = evt.Position.ToUnity();
                at.y = SplatLift;
                _splats[i].position = at;
                _splats[i].localScale = _splatScales[i];
                _splats[i].gameObject.SetActive(true);
                _splatLeft[i] = SplatSeconds;
            }
            else
            {
                Hide(i);
            }

            return;
        }
    }

    private int FreeSlot()
    {
        for (int i = 0; i < _ids.Length; i++)
        {
            if (_ids[i] == 0 && !(_splatLeft[i] > 0f))
            {
                return i;
            }
        }

        return -1;
    }

    private void Hide(int i)
    {
        _ids[i] = 0;
        _splatLeft[i] = 0f;
        _trails[i].emitting = false;
        _trails[i].Clear();
        _cores[i].gameObject.SetActive(false);
        _splats[i].gameObject.SetActive(false);
        _bodies[i].SetActive(false);
    }

    private void Clear()
    {
        for (int i = 0; i < _bodies.Length; i++)
        {
            Hide(i);
        }
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
