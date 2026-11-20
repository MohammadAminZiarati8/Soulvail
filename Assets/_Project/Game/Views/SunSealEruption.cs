using Soulvail.Game.Presentation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soulvail.Game.Views;

/// <summary>Reuses the same world-space meshes for every Sun Seal damage frame.</summary>
internal sealed class SunSealEruption
{
    private const int Stations = 24;
    private const int DustSides = 8;
    private const int VerticesPerStation = 24;
    private const float Duration = 0.78f;
    private readonly Transform _root;
    private readonly Mesh _crack;
    private readonly Mesh _stones;
    private readonly Mesh _dust;
    private readonly Vector3[] _vertices = new Vector3[Stations * VerticesPerStation];
    private readonly Color[] _colors = new Color[Stations * VerticesPerStation];
    private readonly int[] _triangles = new int[Stations * DustSides * 3];
    private float _age;
    private float _reach;
    private float _width;
    private int _count;

    public SunSealEruption(Transform parent, Material material)
    {
        _root = new GameObject("Sandstone eruption").transform;
        _root.SetParent(parent, false);
        _crack = CreateLayer("Ground crack", material, 3);
        _dust = CreateLayer("Rolling dust", material, 4);
        _stones = CreateLayer("Rising sandstone", material, 5);
        Clear();
    }

    public void Begin(Transform footprint, float reach, float halfWidth)
    {
        _root.SetPositionAndRotation(footprint.position, footprint.rotation);
        _reach = reach;
        _width = halfWidth;
        _count = Mathf.Clamp(Mathf.CeilToInt(reach / 1.65f), 3, Stations);
        _age = 0f;
        _root.gameObject.SetActive(true);
        DrawCrack();
        DrawDebris();
    }

    public void Step(float dt)
    {
        if (!_root.gameObject.activeSelf)
            return;

        _age += dt;
        if (_age >= Duration)
        {
            Clear();
            return;
        }

        DrawCrack();
        DrawDebris();
    }

    public void Clear() => _root.gameObject.SetActive(false);

    public void Dispose()
    {
        DestroyMesh(_crack);
        DestroyMesh(_stones);
        DestroyMesh(_dust);
    }

    private Mesh CreateLayer(string label, Material material, int order)
    {
        var layer = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
        layer.transform.SetParent(_root, false);
        var renderer = layer.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = order;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var mesh = new Mesh { name = label };
        mesh.MarkDynamic();
        layer.GetComponent<MeshFilter>().sharedMesh = mesh;
        return mesh;
    }

    private void DrawCrack()
    {
        float alpha = 1f - Mathf.SmoothStep(0f, 1f, _age / Duration);
        Color edge = Palette.Vertex(new Color(0.31f, 0.085f, 0.035f, alpha));
        Color hot = Palette.Vertex(new Color(1f, 0.88f, 0.50f, alpha));
        int vertex = 0;
        int triangle = 0;
        for (int i = 0; i < _count; i++)
        {
            Vector3 a = CrackPoint(i);
            Vector3 b = CrackPoint(i + 1);
            float rim = Mathf.Min(_width * 0.38f, Mathf.Lerp(0.28f, 0.50f, Variation(i)));
            Quad(a, b, rim, edge, ref vertex, ref triangle);
            Quad(a + Vector3.up * 0.01f, b + Vector3.up * 0.01f, rim * 0.43f, hot, ref vertex, ref triangle);

            // Alternate the sides of the broken stone so the line remains visible over its full length.
            float side = i % 2 == 0 ? -1f : 1f;
            Vector3 fork = Vector3.Lerp(a, b, 0.35f);
            Vector3 tip = new(side * _width * Mathf.Lerp(0.56f, 0.9f, Variation(i + 9)), fork.y,
                Mathf.Lerp(a.z, b.z, 0.88f));
            Quad(fork, tip, rim * 0.30f, edge, ref vertex, ref triangle);
            Quad(fork + Vector3.up * 0.01f, tip + Vector3.up * 0.01f, rim * 0.11f, hot, ref vertex, ref triangle);
        }
        Upload(_crack, vertex, triangle);
    }

    private Vector3 CrackPoint(int i) => new(
        (Variation(i + 2) * 2f - 1f) * _width * 0.32f, 0.11f, _reach * i / _count);

    private void Quad(Vector3 a, Vector3 b, float halfWidth, Color color, ref int vertex, ref int triangle)
    {
        // Lateral offsets keep the end caps on the exact locked start and end planes.
        Vector3 side = Vector3.right * halfWidth;
        _vertices[vertex] = a - side;
        _vertices[vertex + 1] = b - side;
        _vertices[vertex + 2] = a + side;
        _vertices[vertex + 3] = b + side;
        for (int j = 0; j < 4; j++)
            _colors[vertex + j] = color;

        _triangles[triangle++] = vertex;
        _triangles[triangle++] = vertex + 1;
        _triangles[triangle++] = vertex + 2;
        _triangles[triangle++] = vertex + 2;
        _triangles[triangle++] = vertex + 1;
        _triangles[triangle++] = vertex + 3;
        vertex += 4;
    }

    private void DrawDebris()
    {
        // Start every station on the damage frame. Different heights and spins break up the silhouette.
        float t = _age / Duration;
        float lift = 4f * t * (1f - t);
        float opacity = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, t));
        int vertex = 0;
        int triangle = 0;
        for (int i = 0; i < _count; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float variation = Variation(i + 1);
            float size = Mathf.Min(_width * 0.42f, Mathf.Lerp(0.40f, 0.77f, variation));
            float along = _reach * (i + 0.5f) / _count;
            var at = new Vector3(side * _width * Mathf.Lerp(0.27f, 0.54f, variation),
                lift * Mathf.Lerp(0.55f, 1.7f, Variation(i + 4)), along);
            Quaternion spin = Quaternion.Euler(i * 34f + _age * 170f, i * 75f, side * 24f);
            Vector3 a = at + spin * (new Vector3(-0.74f, -0.25f, -0.25f) * size);
            Vector3 b = at + spin * (new Vector3(0.59f, -0.27f, -0.50f) * size);
            Vector3 c = at + spin * (new Vector3(0.24f, -0.31f, 0.64f) * size);
            Vector3 topA = at + spin * (new Vector3(-0.49f, 0.65f, -0.18f) * size);
            Vector3 topB = at + spin * (new Vector3(0.42f, 1.13f, -0.41f) * size);
            Vector3 topC = at + spin * (new Vector3(0.35f, 0.84f, 0.46f) * size);
            Face(a, c, b, new Color(0.45f, 0.24f, 0.14f, opacity), ref vertex, ref triangle);
            Face(topA, topB, topC, new Color(0.91f, 0.68f, 0.41f, opacity), ref vertex, ref triangle);
            Side(a, b, topA, topB, new Color(0.53f, 0.29f, 0.16f, opacity), ref vertex, ref triangle);
            Side(b, c, topB, topC, new Color(0.74f, 0.46f, 0.25f, opacity), ref vertex, ref triangle);
            Side(c, a, topC, topA, new Color(0.83f, 0.55f, 0.32f, opacity), ref vertex, ref triangle);
        }
        Upload(_stones, vertex, triangle);

        vertex = 0;
        triangle = 0;
        for (int i = 0; i < _count; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            var centre = new Vector3(side * _width * Mathf.Lerp(0.16f, 0.58f, t), 0.2f + 0.6f * t,
                _reach * (i + 0.5f) / _count);
            float span = Mathf.Min(_width - Mathf.Abs(centre.x), Mathf.Lerp(0.54f, 1.4f, t));
            float length = Mathf.Min(_reach * 0.5f / _count, span * 0.82f);
            Color sand = Palette.Vertex(new Color(1f, 0.87f, 0.65f, 0.76f * (1f - t)));
            _vertices[vertex] = centre;
            _colors[vertex] = sand;
            sand.a = 0f;
            for (int j = 0; j < DustSides; j++)
            {
                float angle = j * (2f * Mathf.PI / DustSides);
                _vertices[vertex + 1 + j] = centre + new Vector3(Mathf.Cos(angle) * span,
                    Mathf.Sin(angle) * length * 0.44f, Mathf.Sin(angle) * length);
                _colors[vertex + 1 + j] = sand;
                _triangles[triangle++] = vertex;
                _triangles[triangle++] = vertex + 1 + j;
                _triangles[triangle++] = vertex + 1 + (j + 1) % DustSides;
            }
            vertex += DustSides + 1;
        }
        Upload(_dust, vertex, triangle);
    }

    private void Side(Vector3 a, Vector3 b, Vector3 topA, Vector3 topB, Color tint, ref int vertex, ref int triangle)
    {
        Face(a, b, topA, tint, ref vertex, ref triangle);
        Face(topA, b, topB, tint, ref vertex, ref triangle);
    }

    private void Face(Vector3 a, Vector3 b, Vector3 c, Color color, ref int vertex, ref int triangle)
    {
        _vertices[vertex] = a;
        _vertices[vertex + 1] = b;
        _vertices[vertex + 2] = c;
        Color tint = Palette.Vertex(color);
        for (int i = 0; i < 3; i++)
        {
            _colors[vertex] = tint;
            _triangles[triangle++] = vertex++;
        }
    }

    private void Upload(Mesh mesh, int vertices, int indices)
    {
        if (mesh.vertexCount != vertices)
            mesh.Clear();
        mesh.SetVertices(_vertices, 0, vertices);
        mesh.SetColors(_colors, 0, vertices);
        mesh.SetTriangles(_triangles, 0, indices, 0, calculateBounds: false);
        mesh.RecalculateBounds();
    }

    private static float Variation(int i) => Mathf.Repeat(i * 0.618034f + 0.17f, 1f);

    private static void DestroyMesh(Mesh mesh)
    {
        if (Application.isPlaying)
            Object.Destroy(mesh);
        else
            Object.DestroyImmediate(mesh);
    }
}
