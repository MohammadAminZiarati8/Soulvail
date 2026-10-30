using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>
    /// Mother's spray, seen (JG-13g): a jet of goo drops from her mouth down every ray of the fan,
    /// each to its clipped end and never past it, and a splat there — flat on the floor where the ray
    /// ran out, upright on the stone where it was blocked. It decides nothing: <c>SprayFired</c> has
    /// already said what hit.
    /// </summary>
    /// <remarks>
    /// One opaque vertex-coloured mesh on <c>M_Impact</c>, rewritten each step, <see cref="ImpactView"/>'s
    /// shape: a part that is not showing is folded to a point. Stepped on the snapshot's clock by
    /// <c>BossViews</c> and pooled.
    /// </remarks>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SprayBurstView : MonoBehaviour, IPoolable
    {
        public const int Rays = SprayConeView.Rays;
        public const int DropsPerRay = 3;

        /// <summary>Metres above her root the mouth is, and ahead of it (Mother at body scale 2.5).</summary>
        public const float MouthHeight = 3.2f;
        public const float MouthForward = 0.9f;

        /// <summary>How fast a drop flies, m/s, and how high its path bows, m.</summary>
        public const float Speed = 30f;
        public const float Arc = 0.35f;

        /// <summary>A splat's life from its first drop landing, and its rise and fold inside that.</summary>
        public const float SplatSeconds = 0.35f;
        private const float SplatRise = 0.06f;
        private const float SplatFold = 0.12f;

        private const float DropGap = 0.03f;
        private const float LaunchSpread = 0.06f;
        private const float DropWidth = 0.24f;
        private const float StreakMetres = 0.6f;
        private const float FloorRadius = 0.9f;
        private const float WallRadius = 0.7f;
        private const float WallHeight = 0.6f;
        private const float WallStandOff = 0.15f;
        // Over the fan (0.08 m), each splat a hair above the last so overlaps never fight.
        private const float FloorHeight = SprayConeView.GroundOffset + 0.02f;
        private const float SplatStagger = 0.001f;

        private const int DropVertices = 24;
        private const int SplatPoints = 12;
        private const int SplatVertices = SplatPoints * 3;
        private const int SplatBase = Rays * DropsPerRay * DropVertices;
        private const int VertexCount = SplatBase + (Rays * SplatVertices);

        private readonly Vector3[] _starts = new Vector3[Rays];
        private readonly Vector3[] _targets = new Vector3[Rays];
        private readonly Vector3[] _normals = new Vector3[Rays];
        private readonly float[] _launch = new float[Rays];
        private readonly float[] _flight = new float[Rays];
        private readonly float[] _radius = new float[Rays];
        private readonly float[] _spin = new float[Rays];
        private readonly Vector3[] _vertices = new Vector3[VertexCount];
        private Mesh _mesh;
        private float _age;
        private float _range;
        private int _plays;
        private bool _live;

        public bool IsLive => _live;
        public float Lifetime { get; private set; }

        /// <summary>Drop <paramref name="drop"/> of ray <paramref name="ray"/>, in the world: at the mouth before it leaves, at the end once landed.</summary>
        public Vector3 DropAt(int ray, int drop) => transform.TransformPoint(DropCentre(ray, Progress(ray, drop)));

        public Vector3 SplatAt(int ray) => transform.TransformPoint(_targets[ray]);

        /// <summary>Up on the floor; back toward the mouth on a stone.</summary>
        public Vector3 SplatNormal(int ray) => _normals[ray];

        public float SplatScale(int ray)
        {
            if (!_live)
            {
                return 0f;
            }

            float since = _age - (_launch[ray] + _flight[ray]);
            if (since < 0f || since >= SplatSeconds)
            {
                return 0f;
            }

            if (since < SplatRise)
            {
                float rest = 1f - (since / SplatRise);
                return 1f - (rest * rest);
            }

            return Mathf.Clamp01((SplatSeconds - since) / SplatFold);
        }

        public void Play(SprayConeView cone)
        {
            if (cone == null || !cone.IsLive)
            {
                return;
            }

            EnsureMesh();
            Vector3 origin = cone.transform.position;
            transform.SetPositionAndRotation(new Vector3(origin.x, 0f, origin.z), Quaternion.identity);
            transform.localScale = Vector3.one;
            _plays++;
            _range = cone.Range;
            _age = 0f;
            Lifetime = 0f;
            for (int i = 0; i < Rays; i++)
            {
                Vector3 along = cone.RayDirection(i);
                float length = cone.RayLength(i);
                // Each jet leaves the mouth on its own ray, so even a stone at her lip is never passed.
                _starts[i] = (along * Mathf.Min(MouthForward, length)) + (Vector3.up * MouthHeight);
                bool blocked = cone.IsBlocked(i);
                _targets[i] = blocked
                    ? (along * Mathf.Max(0f, length - WallStandOff)) + (Vector3.up * WallHeight)
                    : (along * length) + (Vector3.up * (FloorHeight + (i * SplatStagger)));
                // Tilted halfway up the stone, so the camera above sees the splash climb it.
                _normals[i] = blocked ? (Vector3.up - along).normalized : Vector3.up;
                _radius[i] = (blocked ? WallRadius : FloorRadius) * (0.85f + (0.3f * Hash(i, 0)));
                _spin[i] = Hash(i, 1) * 360f;
                _launch[i] = Hash(i, 2) * LaunchSpread;
                _flight[i] = Mathf.Max(0.05f, Vector3.Distance(_starts[i], _targets[i]) / (Speed * (0.85f + (0.3f * Hash(i, 15)))));
                Lifetime = Mathf.Max(Lifetime, _launch[i] + _flight[i] + Mathf.Max(SplatSeconds, (DropsPerRay - 1) * DropGap));
            }

            _live = true;
            Draw();
        }

        public void Step(float dt)
        {
            if (!_live || !(dt > 0f) || float.IsInfinity(dt))
            {
                return;
            }

            _age += dt;
            if (_age >= Lifetime)
            {
                Stop();
                return;
            }

            Draw();
        }

        public void OnSpawn() { }
        public void OnDespawn() => Stop();

        private void Stop()
        {
            _live = false;
            _age = 0f;
            Lifetime = 0f;
            if (_mesh != null)
            {
                System.Array.Clear(_vertices, 0, VertexCount);
                _mesh.vertices = _vertices;
            }
        }

        private float Progress(int ray, int drop) =>
            Mathf.Clamp01((_age - _launch[ray] - (drop * DropGap)) / _flight[ray]);

        private Vector3 DropCentre(int ray, float u) =>
            Vector3.Lerp(_starts[ray], _targets[ray], u) + (Vector3.up * (Arc * 4f * u * (1f - u)));

        private void Draw()
        {
            int v = 0;
            for (int i = 0; i < Rays; i++)
            {
                for (int d = 0; d < DropsPerRay; d++)
                {
                    float u = (_age - _launch[i] - (d * DropGap)) / _flight[i];
                    float width = d == 0 ? DropWidth : DropWidth * 0.75f;
                    WriteDrop(v, i, u, width);
                    v += DropVertices;
                }
            }

            for (int i = 0; i < Rays; i++)
            {
                WriteSplat(SplatBase + (i * SplatVertices), i, SplatScale(i) * _radius[i]);
            }

            _mesh.vertices = _vertices;
            _mesh.bounds = new Bounds(Vector3.up * MouthHeight * 0.5f, Vector3.one * (2f * (_range + MouthHeight + 1f)));
        }

        // A streak along its own path: head and tail are points of the path, clamped to the mouth and
        // the end, so it runs into its splat rather than through the stone.
        private void WriteDrop(int at, int ray, float u, float width)
        {
            float reach = StreakMetres / Mathf.Max(0.01f, Vector3.Distance(_starts[ray], _targets[ray]));
            Vector3 head = DropCentre(ray, Mathf.Clamp01(u + reach));
            Vector3 tail = DropCentre(ray, Mathf.Clamp01(u - reach));
            Vector3 centre = DropCentre(ray, Mathf.Clamp01(u));
            if (u <= 0f || u - reach >= 1f)
            {
                Fold(at, DropVertices, centre);
                return;
            }

            centre = (head + tail) * 0.5f;
            Vector3 axis = head - tail;
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(axis, Vector3.up);
            side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
            Vector3 top = Vector3.Cross(side, axis);
            int v = at;
            for (int face = 0; face < 8; face++)
            {
                Vector3 tip = (face & 1) == 0 ? head : tail;
                Vector3 a = (face & 2) == 0 ? side : -side;
                Vector3 b = (face & 4) == 0 ? top : -top;
                _vertices[v++] = tip;
                _vertices[v++] = centre + (a * width);
                _vertices[v++] = centre + (b * width);
            }
        }

        private void WriteSplat(int at, int ray, float radius)
        {
            Vector3 centre = _targets[ray];
            if (!(radius > 0f))
            {
                Fold(at, SplatVertices, centre);
                return;
            }

            Vector3 normal = _normals[ray];
            Vector3 across = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Cross(normal, across).normalized;
            Vector3 w = Vector3.Cross(normal, u);
            int v = at;
            for (int k = 0; k < SplatPoints; k++)
            {
                _vertices[v++] = centre;
                _vertices[v++] = centre + Rim(u, w, ray, k, radius);
                _vertices[v++] = centre + Rim(u, w, ray, (k + 1) % SplatPoints, radius);
            }
        }

        // Long and short spokes alternate, each a little off the last splat's.
        private Vector3 Rim(Vector3 u, Vector3 w, int ray, int k, float radius)
        {
            float angle = (_spin[ray] + (k * (360f / SplatPoints))) * Mathf.Deg2Rad;
            float reach = radius * ((k & 1) == 0 ? 1f : 0.7f) * (0.8f + (0.4f * Hash(ray, 3 + k)));
            return ((u * Mathf.Cos(angle)) + (w * Mathf.Sin(angle))) * reach;
        }

        private void Fold(int at, int count, Vector3 point)
        {
            for (int i = at; i < at + count; i++)
            {
                _vertices[i] = point;
            }
        }

        // A cosmetic scatter, the same for the same play; never the game's IRandom.
        private float Hash(int a, int b)
        {
            float x = Mathf.Sin((a * 12.9898f) + (b * 78.233f) + (_plays * 37.719f)) * 43758.547f;
            return x - Mathf.Floor(x);
        }

        private void EnsureMesh()
        {
            if (_mesh != null)
            {
                return;
            }

            _mesh = new Mesh { name = "Spray burst (runtime)" };
            _mesh.MarkDynamic();
            _mesh.vertices = _vertices;
            var colours = new Color[VertexCount];
            var triangles = new int[VertexCount];
            for (int i = 0; i < VertexCount; i++)
            {
                triangles[i] = i;
            }

            // One upper quarter of a drop catches the acid sheen; the rest is body green.
            for (int i = 0; i < SplatBase; i++)
            {
                int face = (i % DropVertices) / 3;
                colours[i] = Palette.Vertex((face & 6) == 0 ? Palette.MotherAcid : Palette.MotherGoo);
            }

            // A splat is acid at its heart, which reads on grass, and body green at its spokes.
            for (int i = SplatBase; i < VertexCount; i++)
            {
                colours[i] = Palette.Vertex((i - SplatBase) % 3 == 0 ? Palette.MotherAcid : Palette.MotherGoo);
            }

            _mesh.colors = colours;
            _mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = _mesh;
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
