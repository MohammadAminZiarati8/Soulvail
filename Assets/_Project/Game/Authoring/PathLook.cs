using System;
using System.Collections.Generic;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    /// <summary>Optional, run-local dress for a path. Materials and meshes stay shared and immutable.</summary>
    [CreateAssetMenu(menuName = "Soulvail/Content/Path Look", fileName = "PathLook")]
    public sealed class PathLook : ScriptableObject
    {
        [SerializeField] private MaterialSwap[] _materials = Array.Empty<MaterialSwap>();
        [Tooltip("Optional swaps for every live pet body, matched by renderer name like the hero's.")]
        [SerializeField] private MaterialSwap[] _petMaterials = Array.Empty<MaterialSwap>();
        [SerializeField] private Prop[] _props = Array.Empty<Prop>();
        [SerializeField] private Color _bowTint = Color.clear;
        [SerializeField] private Color _arrowTrailTint = Color.clear;

        public IReadOnlyList<MaterialSwap> Materials => _materials;
        public IReadOnlyList<MaterialSwap> PetMaterials => _petMaterials;
        public IReadOnlyList<Prop> Props => _props;
        /// <summary>Alpha zero leaves the bow alone; tinted material slots opt in below.</summary>
        public Color BowTint => _bowTint;
        public Color ArrowTrailTint => _arrowTrailTint;

        [Serializable]
        public sealed class MaterialSwap
        {
            [SerializeField] private string _renderer;
            [SerializeField, Min(0)] private int _slot;
            [SerializeField] private Material _material;
            [Tooltip("Optional atlas mesh with split material slots, preserving face, trim and blend shapes.")]
            [SerializeField] private Mesh _mesh;
            [SerializeField] private bool _bowTint;

            public string Renderer => _renderer;
            public int Slot => _slot;
            public Material Material => _material;
            public Mesh Mesh => _mesh;
            public bool BowTint => _bowTint;
        }

        [Serializable]
        public sealed class Prop
        {
            [SerializeField] private GameObject _prefab;
            [SerializeField] private string _socket;
            [SerializeField] private Vector3 _position;
            [SerializeField] private Vector3 _rotation;
            [SerializeField] private Vector3 _scale = Vector3.one;

            public GameObject Prefab => _prefab;
            public string Socket => _socket;
            public Vector3 Position => _position;
            public Vector3 Rotation => _rotation;
            public Vector3 Scale => _scale;
        }
    }
}
