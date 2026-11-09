using UnityEngine;
using UnityEngine.Rendering;

namespace Soulvail.Game.Arena
{
    [DefaultExecutionOrder(100)]
    public sealed class SunscarSandstormView : MonoBehaviour
    {
        private const int GrainCount = 128;
        private const float FootprintPadding = 1.5f;

        [SerializeField, Min(13f)] private float _clearRadius = 13f;
        [SerializeField] private Color _sandTint = new(0.92f, 0.80f, 0.58f);
        [SerializeField] private Shader _shader;
        [SerializeField] private Texture2D _softGrain;

        private readonly ParticleSystem.Particle[] _grains = new ParticleSystem.Particle[GrainCount];
        private ArenaView _arena;
        private Transform _hero;
        private Camera _camera;
        private ParticleSystem _system;
        private Material _material;
        private float _drift;

        public float ClearRadius => _clearRadius;
        public Color SandTint => _sandTint;

        public void Bind(ArenaView arena, Transform hero)
        {
            if (_system != null || arena == null || hero == null || _shader == null || _softGrain == null || !float.IsFinite(_clearRadius) || _clearRadius < 13f)
            {
                return;
            }

            _arena = arena;
            _hero = hero;
            _system = gameObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = _system.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = GrainCount;
            main.startSize3D = true;

            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;

            _material = new Material(_shader) { mainTexture = _softGrain, color = SandTint };
            _material.SetFloat("_Surface", 1f);
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_Cull", (int)CullMode.Off);
            // Depth-test against opaque geometry, then let transparent gameplay cues draw above it.
            _material.renderQueue = (int)RenderQueue.Transparent - 100;

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _material;
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void OnEnable()
        {
            if (_system != null)
            {
                _camera = Camera.main;
                _system.Play(withChildren: false);
            }
        }

        private void OnDisable()
        {
            if (_system != null)
            {
                _system.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_material);
                }
                else
                {
                    DestroyImmediate(_material);
                }
            }
        }

        private void LateUpdate()
        {
            if (_system == null || _hero == null || _camera == null)
            {
                return;
            }

            Rect bounds = _arena.PlayBounds;
            Vector3 hero = _hero.position;
            Vector3 portal = _arena.GatePosition;
            float floor = transform.position.y + 0.008f;
            _drift = Mathf.Repeat(_drift + Time.deltaTime * 0.075f, 4f);

            int visible = 0;
            for (int i = 0; i < GrainCount; i++)
            {
                // Evenly distributed phases keep the whole arena moving without random state.
                float x = Mathf.Repeat(i * 0.618034f + _drift, 1f);
                float z = Mathf.Repeat(i * 0.414214f + _drift * 0.25f, 1f);
                Vector3 position = new(bounds.xMin + x * bounds.width, floor, bounds.yMin + z * bounds.height);
                Vector3 viewport = _camera.WorldToViewportPoint(position);
                float edge = Mathf.Max(Mathf.Abs(viewport.x - 0.5f), Mathf.Abs(viewport.y - 0.5f));
                float fade = Mathf.InverseLerp(0.32f, 0.48f, edge)
                    * GroundFade(position, hero, ClearRadius)
                    * GroundFade(position, portal, 4f)
                    * Mathf.InverseLerp(0f, 0.075f, Mathf.Min(Mathf.Min(x, 1f - x), Mathf.Min(z, 1f - z)));
                if (fade <= 0f || viewport.z <= 0f)
                {
                    continue;
                }

                _grains[visible++] = new ParticleSystem.Particle
                {
                    position = position,
                    startSize3D = new Vector3(2.4f, 0.62f, 1f),
                    rotation = -14f,
                    startColor = new Color32(255, 255, 255, (byte)(92f * fade)),
                    startLifetime = 2f,
                    remainingLifetime = 2f
                };
            }

            _system.SetParticles(_grains, visible);
        }

        private static float GroundFade(Vector3 position, Vector3 centre, float radius)
        {
            float dx = position.x - centre.x;
            float dz = position.z - centre.z;
            return Mathf.InverseLerp(radius + FootprintPadding, radius + FootprintPadding + 3f,
                Mathf.Sqrt(dx * dx + dz * dz));
        }
    }
}
