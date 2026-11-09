using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soulvail.Game.Views
{
    public sealed class EntryDoorView : MonoBehaviour
    {
        [SerializeField] private Transform _slab;
        [SerializeField] private Transform _slabMesh;
        [SerializeField] private Material _rimMaterial;
        [SerializeField] private float _openHeightM = 3f;
        [SerializeField] private float _closeS = 0.25f;

        private bool _closing;

        private void Awake()
        {
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", Palette.Player);
            AddRim("Inside Rim", 0.51f, properties);
            AddRim("Outside Rim", -0.51f, properties);
        }

        private void AddRim(string label, float depth, MaterialPropertyBlock properties)
        {
            var rimObject = new GameObject(label);
            rimObject.transform.SetParent(_slabMesh, false);
            var rim = rimObject.AddComponent<LineRenderer>();
            rim.sharedMaterial = _rimMaterial;
            rim.shadowCastingMode = ShadowCastingMode.Off;
            rim.receiveShadows = false;
            rim.useWorldSpace = false;
            rim.alignment = LineAlignment.TransformZ;
            rim.loop = true;
            rim.widthMultiplier = 0.04f;
            rim.positionCount = 4;
            rim.SetPosition(0, new Vector3(-0.47f, -0.47f, depth));
            rim.SetPosition(1, new Vector3(-0.47f, 0.47f, depth));
            rim.SetPosition(2, new Vector3(0.47f, 0.47f, depth));
            rim.SetPosition(3, new Vector3(0.47f, -0.47f, depth));
            rim.SetPropertyBlock(properties);
        }

        public void Open()
        {
            _closing = false;
            SetHeight(_openHeightM);
        }

        public void Close()
        {
            _closing = true;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        private void Step(float dt)
        {
            if (_closing)
            {
                float height = Mathf.MoveTowards(_slab.localPosition.y, 0f, _openHeightM * (dt / _closeS));
                SetHeight(height);
                _closing = height > 0f;
            }
        }

        private void SetHeight(float height)
        {
            Vector3 position = _slab.localPosition;
            position.y = height;
            _slab.localPosition = position;
        }
    }
}
