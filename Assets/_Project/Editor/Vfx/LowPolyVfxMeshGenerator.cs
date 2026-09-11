using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Editor.Vfx;

/// <summary>
/// Generates the faceted primitives the low-poly VFX are built from, so the meshes are a
/// repeatable product of code rather than an FBX nobody can regenerate.
/// </summary>
/// <remarks>
/// <para>
/// Every mesh is <em>flat shaded</em>: each triangle gets its own three vertices and a single face
/// normal. That is the whole trick behind the KayKit look — a smooth-shaded icosphere reads as a
/// ball, a flat-shaded one reads as a faceted rock. It costs vertices (240 for an 80-face sphere)
/// and those vertices are free at this scale.
/// </para>
/// <para>
/// Re-running the menu item updates the existing mesh assets in place rather than replacing them,
/// so materials and prefabs keep pointing at the same GUIDs. Tweak a radius here, re-run, and every
/// prefab in the project picks the change up.
/// </para>
/// </remarks>
internal static class LowPolyVfxMeshGenerator
{
    private const string ParentFolder = "Assets/_Project";
    private const string FolderName = "Meshes";
    private const string OutputFolder = ParentFolder + "/" + FolderName;

    /// <summary>Unit radius: prefabs scale these down, so every mesh is 1 unit across.</summary>
    private const float Radius = 0.5f;

    [MenuItem("Soulvail/VFX/Generate Low Poly Meshes")]
    public static void Generate()
    {
        if (!AssetDatabase.IsValidFolder(OutputFolder))
        {
            AssetDatabase.CreateFolder(ParentFolder, FolderName);
        }

        Save(BuildIcosphere(Radius, 1), "Icosphere");
        Save(BuildTetrahedron(Radius), "Shard");
        Save(BuildRing(Radius * 0.62f, Radius, 16), "Ring");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Low poly VFX meshes written to {OutputFolder}.");
    }

    /// <summary>An icosahedron subdivided <paramref name="subdivisions"/> times: 20 faces per level of 4.</summary>
    private static Mesh BuildIcosphere(float radius, int subdivisions)
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var positions = new List<Vector3>
        {
            new(-1f, t, 0f), new(1f, t, 0f), new(-1f, -t, 0f), new(1f, -t, 0f),
            new(0f, -1f, t), new(0f, 1f, t), new(0f, -1f, -t), new(0f, 1f, -t),
            new(t, 0f, -1f), new(t, 0f, 1f), new(-t, 0f, -1f), new(-t, 0f, 1f),
        };

        var triangles = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        for (int i = 0; i < subdivisions; i++)
        {
            Subdivide(positions, triangles);
        }

        for (int i = 0; i < positions.Count; i++)
        {
            positions[i] = positions[i].normalized * radius;
        }

        return FlatShade(positions, triangles);
    }

    /// <summary>Four triangles: the cheapest solid that still reads as a chunk of debris.</summary>
    private static Mesh BuildTetrahedron(float radius)
    {
        var positions = new List<Vector3>
        {
            new Vector3(1f, 1f, 1f).normalized * radius,
            new Vector3(1f, -1f, -1f).normalized * radius,
            new Vector3(-1f, 1f, -1f).normalized * radius,
            new Vector3(-1f, -1f, 1f).normalized * radius,
        };

        var triangles = new List<int> { 0, 1, 2, 0, 2, 3, 0, 3, 1, 1, 3, 2 };

        return FlatShade(positions, triangles);
    }

    /// <summary>
    /// A flat annulus in the XZ plane, bright at the outer edge and transparent at the inner one,
    /// so that scaling it up reads as a shockwave travelling outwards. Colour lives in the vertices
    /// because a flat disc seen from directly above has no rim for a fresnel to catch.
    /// </summary>
    private static Mesh BuildRing(float innerRadius, float outerRadius, int segments)
    {
        var vertices = new Vector3[segments * 4];
        var normals = new Vector3[segments * 4];
        var colors = new Color[segments * 4];
        var triangles = new int[segments * 6];

        Color inner = new(1f, 1f, 1f, 0f);
        Color outer = Color.white;

        for (int i = 0; i < segments; i++)
        {
            float a0 = i / (float)segments * Mathf.PI * 2f;
            float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            int v = i * 4;

            vertices[v] = new Vector3(Mathf.Cos(a0) * innerRadius, 0f, Mathf.Sin(a0) * innerRadius);
            vertices[v + 1] = new Vector3(Mathf.Cos(a0) * outerRadius, 0f, Mathf.Sin(a0) * outerRadius);
            vertices[v + 2] = new Vector3(Mathf.Cos(a1) * outerRadius, 0f, Mathf.Sin(a1) * outerRadius);
            vertices[v + 3] = new Vector3(Mathf.Cos(a1) * innerRadius, 0f, Mathf.Sin(a1) * innerRadius);

            colors[v] = inner;
            colors[v + 1] = outer;
            colors[v + 2] = outer;
            colors[v + 3] = inner;

            for (int n = 0; n < 4; n++)
            {
                normals[v + n] = Vector3.up;
            }

            int t = i * 6;
            triangles[t] = v;
            triangles[t + 1] = v + 1;
            triangles[t + 2] = v + 2;
            triangles[t + 3] = v;
            triangles[t + 4] = v + 2;
            triangles[t + 5] = v + 3;
        }

        var mesh = new Mesh
        {
            vertices = vertices,
            normals = normals,
            colors = colors,
        };
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void Subdivide(List<Vector3> positions, List<int> triangles)
    {
        var midpoints = new Dictionary<long, int>();
        var subdivided = new List<int>(triangles.Count * 4);

        for (int i = 0; i < triangles.Count; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];

            int ab = Midpoint(positions, midpoints, a, b);
            int bc = Midpoint(positions, midpoints, b, c);
            int ca = Midpoint(positions, midpoints, c, a);

            subdivided.Add(a);
            subdivided.Add(ab);
            subdivided.Add(ca);
            subdivided.Add(b);
            subdivided.Add(bc);
            subdivided.Add(ab);
            subdivided.Add(c);
            subdivided.Add(ca);
            subdivided.Add(bc);
            subdivided.Add(ab);
            subdivided.Add(bc);
            subdivided.Add(ca);
        }

        triangles.Clear();
        triangles.AddRange(subdivided);
    }

    private static int Midpoint(List<Vector3> positions, Dictionary<long, int> cache, int a, int b)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (cache.TryGetValue(key, out int existing))
        {
            return existing;
        }

        positions.Add((positions[a] + positions[b]) * 0.5f);
        int index = positions.Count - 1;
        cache[key] = index;
        return index;
    }

    /// <summary>
    /// Splits every triangle into its own vertices and gives it a single face normal. Winding is
    /// corrected against the origin, which is safe because both solids here are convex and centred:
    /// a face whose normal points back at the centre is inside out.
    /// </summary>
    private static Mesh FlatShade(List<Vector3> positions, List<int> triangles)
    {
        int count = triangles.Count;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var colors = new Color[count];
        var indices = new int[count];

        for (int i = 0; i < count; i += 3)
        {
            Vector3 a = positions[triangles[i]];
            Vector3 b = positions[triangles[i + 1]];
            Vector3 c = positions[triangles[i + 2]];

            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vector3 center = (a + b + c) / 3f;
            if (Vector3.Dot(normal, center) < 0f)
            {
                (b, c) = (c, b);
                normal = -normal;
            }

            vertices[i] = a;
            vertices[i + 1] = b;
            vertices[i + 2] = c;

            for (int n = 0; n < 3; n++)
            {
                normals[i + n] = normal;
                colors[i + n] = Color.white;
                indices[i + n] = i + n;
            }
        }

        var mesh = new Mesh
        {
            vertices = vertices,
            normals = normals,
            colors = colors,
        };
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Writes into the existing asset when there is one, so prefab references survive.</summary>
    private static void Save(Mesh mesh, string name)
    {
        string path = $"{OutputFolder}/{name}.asset";
        mesh.name = name;

        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return;
        }

        existing.Clear();
        existing.vertices = mesh.vertices;
        existing.normals = mesh.normals;
        existing.colors = mesh.colors;
        existing.triangles = mesh.triangles;
        existing.RecalculateBounds();
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(mesh);
    }
}
