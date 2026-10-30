using Soulvail.Game.Adapters;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>A floor fan clipped against cover once, then filled on simulated time.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SprayConeView : MonoBehaviour, IPoolable
    {
        public const int Rays = 32;
        public const float FlashSeconds = 0.15f;
        private const float GroundOffset = 0.03f;
        private const float EdgeWidth = 0.04f;

        private readonly float[] _lengths = new float[Rays];
        private readonly Vector3[] _ends = new Vector3[Rays];
        // Rim outer/inner, fill edge, fill centre, then two side strips.
        private readonly Vector3[] _vertices = new Vector3[(Rays * 3) + 5];
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _properties;
        private float _seconds;
        private float _elapsed;
        private bool _fired;

        public float Fill { get; private set; }
        public bool IsLive { get; private set; }
        public float RayLength(int i) => _lengths[i];

        public void Arm(Vector3 origin, Vector2 directionXZ, float halfAngleDeg, float range, float seconds)
        {
            EnsureMesh();
            Clear();
            transform.SetPositionAndRotation(new Vector3(origin.x, GroundOffset, origin.z), Quaternion.identity);
            transform.localScale = Vector3.one;
            _seconds = seconds;
            float bearing = Mathf.Atan2(directionXZ.x, directionXZ.y);
            int cover = LayerMask.GetMask("Cover");
            var eye = new Vector3(origin.x, LineOfSightSense.EyeHeight, origin.z);
            for (int i = 0; i < Rays; i++)
            {
                float angle = bearing + (Mathf.Lerp(-halfAngleDeg, halfAngleDeg, i / (float)(Rays - 1)) * Mathf.Deg2Rad);
                var ray = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                _lengths[i] = Physics.Raycast(eye, ray, out RaycastHit hit, range, cover, QueryTriggerInteraction.Ignore)
                    ? hit.distance : range;
                _ends[i] = ray * _lengths[i];
            }

            IsLive = true;
            _renderer.enabled = true;
            Draw();
        }

        public void Fire()
        {
            if (!IsLive)
            {
                return;
            }

            _fired = true;
            _elapsed = 0f;
            Fill = 1f;
            Draw();
        }

        public void Clear()
        {
            IsLive = false;
            Fill = 0f;
            _fired = false;
            _elapsed = 0f;
            _seconds = 0f;
            System.Array.Clear(_lengths, 0, Rays);
            System.Array.Clear(_ends, 0, Rays);
            if (_renderer == null)
            {
                TryGetComponent(out _renderer);
            }

            if (_renderer != null)
            {
                _renderer.enabled = false;
            }

            if (_mesh != null)
            {
                System.Array.Clear(_vertices, 0, _vertices.Length);
                _mesh.vertices = _vertices;
                _mesh.RecalculateBounds();
            }
        }

        public void Step(float dt)
        {
            if (!IsLive || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _elapsed += dt;
            if (_fired)
            {
                if (_elapsed >= FlashSeconds)
                {
                    Clear();
                }
            }
            else
            {
                Fill = _seconds > 0f ? Mathf.Clamp01(_elapsed / _seconds) : 1f;
                Draw();
            }
        }

        public void OnSpawn() { }
        public void OnDespawn() => Clear();

        private void EnsureMesh()
        {
            if (_mesh != null)
            {
                return;
            }

            _renderer = GetComponent<MeshRenderer>();
            _properties = new MaterialPropertyBlock();
            _mesh = new Mesh { name = "Spray fan (runtime)" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            var triangles = new int[((Rays - 1) * 9) + 12];
            int t = 0;
            for (int i = 0; i < Rays - 1; i++)
            {
                triangles[t++] = Rays * 3;
                triangles[t++] = (Rays * 2) + i;
                triangles[t++] = (Rays * 2) + i + 1;
                triangles[t++] = i;
                triangles[t++] = Rays + i;
                triangles[t++] = i + 1;
                triangles[t++] = i + 1;
                triangles[t++] = Rays + i;
                triangles[t++] = Rays + i + 1;
            }

            for (int side = 0; side < 2; side++)
            {
                int end = side == 0 ? 0 : Rays - 1;
                int corner = (Rays * 3) + 1 + (side * 2);
                triangles[t++] = Rays * 3;
                triangles[t++] = corner;
                triangles[t++] = end;
                triangles[t++] = end;
                triangles[t++] = corner;
                triangles[t++] = corner + 1;
            }

            _mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        private void Draw()
        {
            for (int i = 0; i < Rays; i++)
            {
                Vector3 end = _ends[i];
                _vertices[i] = end;
                _vertices[Rays + i] = end.normalized * Mathf.Max(0f, _lengths[i] - EdgeWidth);
                // Grow toward the clipped rim without repainting the safe shadows.
                _vertices[(Rays * 2) + i] = end * Fill;
            }

            for (int side = 0; side < 2; side++)
            {
                int end = side == 0 ? 0 : Rays - 1;
                int corner = (Rays * 3) + 1 + (side * 2);
                Vector3 along = _ends[end].normalized;
                Vector3 inset = new Vector3(along.z, 0f, -along.x) * (side == 0 ? EdgeWidth : -EdgeWidth);
                _vertices[corner] = inset;
                _vertices[corner + 1] = _ends[end] + inset;
            }

            _mesh.vertices = _vertices;
            _mesh.RecalculateBounds();
            Color colour = Palette.Danger;
            float alpha = _fired ? 0.95f : 0.6f;
            _properties.SetColor("_BaseColor", new Color(colour.r * alpha, colour.g * alpha, colour.b * alpha, alpha));
            // The shared ring material has a disc texture; the fan itself supplies the silhouette.
            _properties.SetTexture("_BaseMap", Texture2D.whiteTexture);
            _renderer.SetPropertyBlock(_properties);
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_mesh);
                }
                else
                {
                    DestroyImmediate(_mesh);
                }
            }
        }
    }
}
