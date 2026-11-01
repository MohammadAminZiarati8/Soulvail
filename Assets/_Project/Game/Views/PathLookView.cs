using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>Dresses only the run's body, keeping every replaced slot and property block.</summary>
    public sealed class PathLookView : MonoBehaviour, IDisposable
    {
        public const float ShimmerDuration = 0.4f;
        [SerializeField] private CharacterDefinition _character;
        private readonly List<Original> _originals = new();
        private readonly List<GameObject> _props = new();
        private readonly HashSet<string> _warnings = new();
        private IDisposable _chosen, _started;
        private IRunSession _session;
        private ProjectileViews _projectiles;
        private float _shimmer;

        public PathLook Look { get; private set; }
        public bool IsShimmering => _shimmer > 0f;

        // RunScope constructs this after raising and injecting the body, before RunTicker.Start.
        public void Construct(DomainEventHub hub, IRunSession session, ProjectileViews projectiles)
        {
            Dispose();
            _session = session;
            _projectiles = projectiles;
            _chosen = hub.Subscribe<PathChosen>(evt => ApplyPath(evt.PathId, true));
            _started = hub.Subscribe<RunStarted>(_ => ApplyPath(_session?.State?.PathId ?? default, false));
        }

        public void ApplyPath(ContentId id, bool shimmer)
        {
            PathLook look = null;
            if (_character != null)
            {
                foreach (PathDefinition path in _character.PathSlots)
                {
                    if (path != null && path.Id == id.Value)
                    {
                        look = path.Look;
                        break;
                    }
                }
            }
            Apply(look, shimmer);
        }

        public void Apply(PathLook look, bool shimmer)
        {
            Restore();
            Look = look;
            if (_projectiles != null)
            {
                _projectiles.PlayerPathLook = look;
            }
            if (look == null)
            {
                return;
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            foreach (PathLook.MaterialSwap swap in look.Materials)
            {
                if (swap == null || swap.Material == null || swap.Slot < 0)
                {
                    continue;
                }
                foreach (Renderer renderer in renderers)
                {
                    if (renderer.name != swap.Renderer)
                    {
                        continue;
                    }
                    Original original = Remember(renderer);
                    var skin = renderer as SkinnedMeshRenderer;
                    if (swap.Mesh != null && skin != null)
                    {
                        skin.sharedMesh = swap.Mesh;
                    }
                    Material[] materials = renderer.sharedMaterials;
                    if (swap.Slot >= materials.Length)
                    {
                        int count = skin != null && skin.sharedMesh != null ? skin.sharedMesh.subMeshCount : materials.Length;
                        if (swap.Slot >= count)
                        {
                            continue;
                        }
                        Array.Resize(ref materials, count);
                    }
                    materials[swap.Slot] = swap.Material;
                    renderer.sharedMaterials = materials;
                    if (swap.BowTint && look.BowTint.a > 0f)
                    {
                        var block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block, swap.Slot);
                        original.SlotBlocks.Add((swap.Slot, block));
                        var tint = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(tint, swap.Slot);
                        tint.SetColor("_BaseColor", look.BowTint);
                        tint.SetColor("_EmissionColor", look.BowTint * 1.2f);
                        renderer.SetPropertyBlock(tint, swap.Slot);
                    }
                }
            }

            foreach (PathLook.Prop prop in look.Props)
            {
                if (prop == null)
                {
                    continue;
                }
                Transform socket = null;
                foreach (Transform child in GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == prop.Socket)
                    {
                        socket = child;
                        break;
                    }
                }
                if (prop.Prefab == null || socket == null)
                {
                    string warning = $"PathLook '{look.name}': missing prop or socket '{prop.Socket}'; skipped.";
                    if (_warnings.Add(warning))
                    {
                        Debug.LogWarning(warning, this);
                    }
                    continue;
                }
                GameObject instance = Instantiate(prop.Prefab, socket, false);
                instance.transform.SetLocalPositionAndRotation(prop.Position, Quaternion.Euler(prop.Rotation));
                instance.transform.localScale = prop.Scale;
                _props.Add(instance);
            }

            if (shimmer)
            {
                foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                {
                    Remember(renderer);
                }
                _shimmer = ShimmerDuration;
                Step(0f);
            }
        }

        public void Step(float dt)
        {
            if (!IsShimmering)
            {
                return;
            }
            _shimmer = Mathf.Max(0f, _shimmer - Mathf.Max(0f, dt));
            foreach (Original original in _originals)
            {
                if (original.Renderer == null)
                {
                    continue;
                }
                if (!IsShimmering)
                {
                    original.Renderer.SetPropertyBlock(original.Block);
                    continue;
                }
                original.Renderer.GetPropertyBlock(original.Shimmer);
                original.Shimmer.SetColor("_BaseColor", Color.Lerp(Color.white, Palette.Player, _shimmer / ShimmerDuration));
                original.Renderer.SetPropertyBlock(original.Shimmer);
            }
        }

        private Original Remember(Renderer renderer)
        {
            foreach (Original original in _originals)
            {
                if (original.Renderer == renderer)
                {
                    return original;
                }
            }
            var saved = new Original { Renderer = renderer, Materials = renderer.sharedMaterials };
            var skin = renderer as SkinnedMeshRenderer;
            saved.Mesh = skin != null ? skin.sharedMesh : null;
            renderer.GetPropertyBlock(saved.Block);
            _originals.Add(saved);
            return saved;
        }

        private void Restore()
        {
            _shimmer = 0f;
            foreach (Original original in _originals)
            {
                if (original.Renderer == null)
                {
                    continue;
                }
                foreach (var slot in original.SlotBlocks)
                {
                    original.Renderer.SetPropertyBlock(slot.Block, slot.Slot);
                }
                original.Renderer.sharedMaterials = original.Materials;
                var skin = original.Renderer as SkinnedMeshRenderer;
                if (skin != null)
                {
                    skin.sharedMesh = original.Mesh;
                }
                original.Renderer.SetPropertyBlock(original.Block);
            }
            _originals.Clear();
            foreach (GameObject prop in _props)
            {
                if (prop != null)
                {
                    prop.SetActive(false);
                    if (Application.isPlaying)
                    {
                        Destroy(prop);
                    }
                    else
                    {
                        DestroyImmediate(prop);
                    }
                }
            }
            _props.Clear();
            Look = null;
        }

        public void Dispose()
        {
            _chosen?.Dispose();
            _started?.Dispose();
            _chosen = _started = null;
            Restore();
            if (_projectiles != null)
            {
                _projectiles.PlayerPathLook = null;
            }
        }

        private void Update() => Step(Time.deltaTime);
        private void OnDestroy() => Dispose();

        private sealed class Original
        {
            public Renderer Renderer;
            public Material[] Materials;
            public Mesh Mesh;
            public readonly MaterialPropertyBlock Block = new();
            public readonly MaterialPropertyBlock Shimmer = new();
            public readonly List<(int Slot, MaterialPropertyBlock Block)> SlotBlocks = new();
        }
    }
}
