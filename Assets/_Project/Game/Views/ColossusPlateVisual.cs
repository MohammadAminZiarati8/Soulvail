using System;
using System.Collections.Generic;
using UnityEngine;

namespace Soulvail.Game.Views;

internal sealed class ColossusPlateVisual : IDisposable
{
    private readonly GameObject _plate;
    private readonly GameObject _crack;
    private readonly GameObject _split;
    private readonly GameObject _repair;
    private readonly Transform[] _fragments = new Transform[6];
    private readonly ParticleSystem _chips;
    private readonly Mesh _stoneMesh;
    private readonly Mesh _crackMesh;
    private readonly Mesh _splitMesh;
    private readonly Material _stone;
    private readonly Material _fissure;
    private readonly Transform _shoulder;
    private readonly float _side;

    public ColossusPlateVisual(Transform shoulder, Transform pivot, float side, Material sandstone)
    {
        _shoulder = shoulder;
        _side = side;
        _stone = new Material(sandstone.shader) { name = "Colossus plate sandstone", hideFlags = HideFlags.DontSave };
        _stone.color = new Color(0.78f, 0.54f, 0.29f);
        _stone.SetFloat("_Smoothness", 0.12f);
        _fissure = new Material(_stone) { name = "Colossus plate fissure", color = new Color(0.19f, 0.10f, 0.057f) };
        _stoneMesh = BuildPlate();
        _plate = Render(side < 0f ? "Left shoulder plate" : "Right shoulder plate", shoulder, _stoneMesh, _stone);
        _plate.transform.SetPositionAndRotation(
            shoulder.position + pivot.TransformVector(new Vector3(side * 0.24f, 0.14f, 0.04f)),
            pivot.rotation * Quaternion.Euler(0f, side * 12f, side * -18f));

        _crackMesh = BuildCrack(new Vector2(-0.24f, 0.30f), new Vector2(-0.08f, 0.11f),
            new Vector2(-0.12f, -0.02f), new Vector2(0.10f, -0.15f), new Vector2(0.15f, -0.32f));
        _splitMesh = BuildCrack(new Vector2(0.39f, 0.19f), new Vector2(0.16f, 0.16f),
            new Vector2(0.09f, 0.03f), new Vector2(-0.12f, -0.02f), new Vector2(-0.31f, -0.12f));
        _crack = Render("Shoulder crack", _plate.transform, _crackMesh, _fissure);
        _split = Render("Shoulder split", _plate.transform, _splitMesh, _fissure);
        _chips = CreateChips(shoulder, _stoneMesh, _stone);
        _repair = new GameObject("Shoulder repair") { hideFlags = HideFlags.DontSave };
        _repair.transform.SetParent(shoulder, false);
        _repair.SetActive(false);
        for (int i = 0; i < _fragments.Length; i++)
        {
            Transform fragment = Render("Repairing sandstone", _repair.transform, _stoneMesh, _stone).transform;
            fragment.localScale = Vector3.one * (0.30f + (i % 3) * 0.09f);
            _fragments[i] = fragment;
        }
        Reset();
    }

    public Vector3 Position => _plate.transform.position;

    public void Reset()
    {
        CancelRepair();
        _plate.SetActive(true);
        _crack.SetActive(false);
        _split.SetActive(false);
        if (Application.isPlaying)
        {
            _chips.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    public void BeginRepair()
    {
        AnimateRepair(0f);
        _repair.SetActive(true);
    }

    public void AnimateRepair(float progress)
    {
        float gather = Mathf.SmoothStep(0f, 1f, progress);
        float radius = Mathf.Lerp(1.55f, 0.12f, gather);
        _repair.transform.SetPositionAndRotation(Position, _plate.transform.rotation);
        for (int i = 0; i < _fragments.Length; i++)
        {
            float angle = (_side * progress * 1.6f + (float)i / _fragments.Length) * (2f * Mathf.PI);
            float height = Mathf.Lerp(0.85f + (i % 3) * 0.36f, 0.13f, gather);
            _fragments[i].SetLocalPositionAndRotation(
                new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius),
                Quaternion.Euler(i * 39f + progress * 300f, angle * Mathf.Rad2Deg, i * 71f));
        }
    }

    public void CancelRepair() => _repair.SetActive(false);

    public void Hit(float remaining)
    {
        bool broken = remaining == 0f;
        _plate.SetActive(!broken);
        _crack.SetActive(remaining < 1f);
        _split.SetActive(remaining <= 0.5f);
        if (!Application.isPlaying)
        {
            return;
        }

        _chips.transform.position = Position;
        if (!_chips.isPlaying)
        {
            _chips.Play(false);
        }
        Vector3 outward = (Position - _shoulder.position).normalized;
        Vector3 origin = Position + Vector3.up * 0.14f;
        int count = broken ? 22 : 5;
        for (int i = 0; i < count; i++)
        {
            float angle = i * 137.5f * Mathf.Deg2Rad;
            Vector3 scatter = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float speed = broken ? 2.0f + (i % 7) * 0.37f : 1.4f;
            float lift = broken ? 1.5f + (i % 5) * 0.48f : 1.4f;
            _chips.Emit(new ParticleSystem.EmitParams
            {
                position = origin,
                velocity = scatter * speed + outward + Vector3.up * lift,
                startSize = (broken ? 0.30f : 0.17f) * (0.8f + (i % 4) * 0.13f),
                startLifetime = (broken ? 0.66f : 0.25f) + (i % 5) * 0.025f,
                rotation3D = new Vector3(i * 36f, _side * i * 69f, i * 47f)
            }, 1);
        }
    }

    private static Mesh BuildPlate()
    {
        Vector2[] outline =
        {
            new(-0.54f, -0.22f), new(-0.38f, -0.46f), new(0.26f, -0.47f), new(0.57f, -0.28f),
            new(0.59f, 0.19f), new(0.33f, 0.46f), new(-0.26f, 0.45f), new(-0.55f, 0.24f)
        };
        int sides = outline.Length;
        var vertices = new Vector3[sides * 3 + 2];
        var triangles = new List<int>(sides * 18);
        int top = sides * 3;
        int bottom = top + 1;
        vertices[top] = Vector3.up * 0.18f;
        vertices[bottom] = Vector3.down * 0.22f;
        for (int i = 0; i < sides; i++)
        {
            Vector2 point = outline[i];
            vertices[i] = new Vector3(point.x * 0.83f, 0.18f, point.y * 0.83f);
            vertices[sides + i] = new Vector3(point.x, 0.04f, point.y);
            vertices[2 * sides + i] = new Vector3(point.x * 0.92f, -0.22f, point.y * 0.92f);
            int next = (i + 1) % sides;
            triangles.AddRange(new[] { top, next, i, bottom, 2 * sides + i, 2 * sides + next });
            for (int ring = 0; ring < 2; ring++)
            {
                int a = ring * sides + i;
                int b = ring * sides + next;
                triangles.AddRange(new[] { a, b, a + sides, b, b + sides, a + sides });
            }
        }
        var faces = new Vector3[triangles.Count];
        var indices = new int[triangles.Count];
        for (int i = 0; i < triangles.Count; i++)
        {
            faces[i] = vertices[triangles[i]];
            indices[i] = i;
        }
        return MakeMesh("Beveled shoulder sandstone", faces, indices);
    }

    private static Mesh BuildCrack(params Vector2[] points)
    {
        var vertices = new Vector3[points.Length * 2];
        var triangles = new List<int>((points.Length - 1) * 6);
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 direction = (points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(i - 1, 0)]).normalized;
            float width = i == points.Length - 1 ? 0.014f : 0.035f;
            Vector2 perpendicular = new Vector2(-direction.y, direction.x) * width;
            Vector2 left = points[i] + perpendicular;
            Vector2 right = points[i] - perpendicular;
            vertices[2 * i] = new Vector3(left.x, 0.184f, left.y);
            vertices[2 * i + 1] = new Vector3(right.x, 0.184f, right.y);
            if (i > 0)
            {
                int start = (i - 1) * 2;
                triangles.AddRange(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
            }
        }
        return MakeMesh("Shoulder fissure", vertices, triangles.ToArray());
    }

    private static Mesh MakeMesh(string name, Vector3[] vertices, int[] triangles)
    {
        var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave, vertices = vertices, triangles = triangles };
        mesh.RecalculateNormals();
        return mesh;
    }

    private static GameObject Render(string name, Transform parent, Mesh mesh, Material material)
    {
        var obj = new GameObject(name) { hideFlags = HideFlags.DontSave };
        obj.transform.SetParent(parent, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterial = material;
        return obj;
    }

    private static ParticleSystem CreateChips(Transform shoulder, Mesh mesh, Material material)
    {
        var obj = new GameObject("Shoulder stone chips") { hideFlags = HideFlags.DontSave };
        obj.SetActive(false);
        obj.transform.SetParent(shoulder, false);
        var particles = obj.AddComponent<ParticleSystem>();
        var main = particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.76f;
        main.maxParticles = 64;
        var emission = particles.emission;
        emission.enabled = false;
        var shape = particles.shape;
        shape.enabled = false;
        var renderer = obj.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.sharedMaterial = material;
        obj.SetActive(true);
        return particles;
    }

    public void Dispose()
    {
        Release(_stoneMesh);
        Release(_crackMesh);
        Release(_splitMesh);
        Release(_stone);
        Release(_fissure);
    }

    private static void Release(UnityEngine.Object resource)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(resource);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(resource);
        }
    }
}
