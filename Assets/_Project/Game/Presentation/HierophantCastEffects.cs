using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Soulvail.Game.Presentation;

internal sealed class HierophantCastEffects : IDisposable
{
    private const int FlameCount = 13;
    private const int RingSegments = 32;
    private const int TongueVertices = 7;
    private const int TongueIndices = 15;
    private readonly Transform _footprint;
    private readonly MeshRenderer _fire;
    private readonly Mesh _fireMesh;
    private readonly Vector3[] _fireVertices = new Vector3[FlameCount * 2 * TongueVertices];
    private readonly LineRenderer _staffFlare;
    private readonly LineRenderer _launch;
    private readonly LineRenderer _healRing;
    private readonly LineRenderer _healBeam;
    private readonly Color _ember = Palette.Vertex(new Color(1f, 0.17f, 0.015f, 0.96f));
    private readonly Color _flame = Palette.Vertex(new Color(1f, 0.53f, 0.05f, 0.99f));
    private readonly Color _flameCore = Palette.Vertex(new Color(1f, 0.93f, 0.52f, 1f));
    private readonly Color _mend = Palette.Vertex(new Color(0.28f, 1f, 0.48f, 1f));
    private readonly Color _mendCore = Palette.Vertex(new Color(0.87f, 1f, 0.76f, 1f));
    private Vector3 _launchOrigin;
    private bool _hasLaunchOrigin;

    internal HierophantCastEffects(Transform floor, Transform footprint, Material material)
    {
        _footprint = footprint;
        var crest = new GameObject("Fire wave crest", typeof(MeshFilter), typeof(MeshRenderer));
        crest.transform.SetParent(footprint, false);
        _fire = crest.GetComponent<MeshRenderer>();
        _fire.sharedMaterial = material;
        _fire.sortingOrder = 7;
        _fire.shadowCastingMode = ShadowCastingMode.Off;
        _fire.receiveShadows = false;
        _fireMesh = new Mesh { name = "Hierophant fire wave" };
        _fireMesh.MarkDynamic();
        crest.GetComponent<MeshFilter>().sharedMesh = _fireMesh;

        var indices = new int[FlameCount * 2 * TongueIndices];
        var colors = new Color[_fireVertices.Length];
        int[] tongue = { 0, 1, 2, 2, 1, 3, 2, 3, 4, 4, 3, 5, 4, 5, 6 };
        var tip = _flame;
        tip.a = 0.25f;
        for (int i = 0; i < FlameCount * 2; i++)
        {
            int start = i * TongueVertices;
            for (int j = 0; j < tongue.Length; j++)
                indices[i * TongueIndices + j] = start + tongue[j];
            colors[start] = colors[start + 1] = i % 2 == 0 ? _ember : _flameCore;
            colors[start + 2] = colors[start + 3] = i % 2 == 0 ? _flame : _flameCore;
            colors[start + 4] = colors[start + 5] = _flame;
            colors[start + 6] = tip;
        }
        _fireMesh.vertices = _fireVertices;
        _fireMesh.colors = colors;
        _fireMesh.triangles = indices;

        _staffFlare = Line(floor, material, "Staff fire", 2, _flame, _flameCore);
        _launch = Line(floor, material, "Staff fire launch", 3, _flameCore, _flame);
        _launch.widthCurve = new AnimationCurve(new Keyframe(0f, 0.34f), new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0.48f));
        _healRing = Line(floor, material, "Priest heal ring", RingSegments, _mend, _mendCore);
        _healRing.loop = true;
        _healBeam = Line(floor, material, "Priest heal beam", 3, _mend, _mendCore);
        ClearFire();
    }

    private LineRenderer Line(Transform parent, Material material, string label, int count, Color start, Color end)
    {
        var lineObject = new GameObject(label);
        lineObject.transform.SetParent(parent, false);
        var line = lineObject.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.sortingOrder = 8;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.useWorldSpace = true;
        line.positionCount = count;
        line.numCapVertices = 4;
        line.startColor = start;
        line.endColor = end;
        line.enabled = false;
        return line;
    }

    internal void Charge(Vector3 staffTip, float progress)
    {
        float grow = 0.3f + progress * 0.7f;
        _staffFlare.enabled = true;
        _staffFlare.widthMultiplier = 0.7f * grow;
        _staffFlare.SetPosition(0, staffTip);
        _staffFlare.SetPosition(1, staffTip + Vector3.up * (0.5f * grow));
    }

    internal void Fire(float halfWidth, float rear, float front, float fraction)
    {
        _fire.enabled = front > rear;
        if (!_fire.enabled)
            return;

        float spacing = halfWidth * 2f / FlameCount;
        for (int i = 0; i < FlameCount; i++)
        {
            float centre = -halfWidth + (i + 0.5f) * spacing;
            float flicker = i * 2.4f + fraction * 24f;
            float height = 1.06f + 0.46f * Mathf.Sin(flicker);
            float bend = spacing * (0.14f + 0.17f * Mathf.Cos(flicker));
            int vertex = i * 2 * TongueVertices;
            float tip = Mathf.Lerp(rear, front, 0.11f + 0.22f * Mathf.Cos(flicker) * Mathf.Cos(flicker));
            DrawTongue(vertex, centre, spacing * 0.5f, height, bend, front, tip, 0.10f);
            DrawTongue(vertex + TongueVertices, centre, spacing * 0.25f, height * 0.66f, bend,
                front, Mathf.Lerp(tip, front, 0.24f), 0.18f);
        }
        _fireMesh.vertices = _fireVertices;
        _fireMesh.RecalculateBounds();
    }

    private void DrawTongue(int vertex, float centre, float halfWidth, float height, float bend,
        float front, float rear, float floorHeight)
    {
        _fireVertices[vertex] = new Vector3(centre - halfWidth, floorHeight, front);
        _fireVertices[vertex + 1] = new Vector3(centre + halfWidth, floorHeight, front);
        float middle = centre - bend * 0.3f;
        float shoulder = Mathf.Lerp(front, rear, 0.45f);
        _fireVertices[vertex + 2] = new Vector3(middle - halfWidth * 0.86f, height * 0.40f, shoulder);
        _fireVertices[vertex + 3] = new Vector3(middle + halfWidth * 0.86f, height * 0.40f, shoulder);
        middle = centre + bend * 0.35f;
        float taper = Mathf.Lerp(front, rear, 0.80f);
        _fireVertices[vertex + 4] = new Vector3(middle - halfWidth * 0.47f, height * 0.75f, taper);
        _fireVertices[vertex + 5] = new Vector3(middle + halfWidth * 0.47f, height * 0.75f, taper);
        _fireVertices[vertex + 6] = new Vector3(centre + bend, height, rear);
    }

    internal void Launch(Vector3 staffTip, float front, float fraction)
    {
        if (!_hasLaunchOrigin)
        {
            _hasLaunchOrigin = true;
            _launchOrigin = staffTip;
        }

        const float launchFraction = 0.28f;
        float strength = Mathf.Clamp01(1f - fraction / launchFraction);
        _staffFlare.enabled = strength > 0f;
        if (_staffFlare.enabled)
            Charge(_launchOrigin, strength);

        _launch.enabled = front > 0f && strength > 0f;
        if (!_launch.enabled)
            return;

        Vector3 crest = _footprint.TransformPoint(new Vector3(0f, 0.32f, front));
        _launch.widthMultiplier = Mathf.Lerp(0.23f, 0.88f, strength);
        _launch.SetPosition(0, _launchOrigin);
        _launch.SetPosition(1, Vector3.Lerp(_launchOrigin, crest, 0.5f) + Vector3.up * 0.38f);
        _launch.SetPosition(2, crest);
    }

    internal void Heal(Vector3 priest, Vector3 boss, float fraction)
    {
        _healRing.enabled = _healBeam.enabled = true;
        float radius = 0.65f + fraction * 0.55f;
        _healRing.widthMultiplier = 0.2f;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = i * (2f * Mathf.PI / RingSegments);
            _healRing.SetPosition(i, priest + new Vector3(Mathf.Cos(angle) * radius, 0.12f, Mathf.Sin(angle) * radius));
        }
        Vector3 from = priest + Vector3.up * 1.25f;
        _healBeam.widthMultiplier = Mathf.Lerp(0.32f, 0.59f, fraction);
        _healBeam.SetPosition(0, from);
        _healBeam.SetPosition(1, (from + boss) * 0.5f + Vector3.up * (0.72f + fraction * 0.65f));
        _healBeam.SetPosition(2, boss);
    }

    internal void ClearHeal() => _healRing.enabled = _healBeam.enabled = false;

    internal void ClearFire()
    {
        _fire.enabled = _staffFlare.enabled = _launch.enabled = false;
        _hasLaunchOrigin = false;
    }

    public void Dispose()
    {
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(_fireMesh);
        else
            UnityEngine.Object.DestroyImmediate(_fireMesh);
    }
}
