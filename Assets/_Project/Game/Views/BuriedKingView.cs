using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Game.Views;

/// <summary>Draws the prototype's locked floor geometry and collider-free weapon.</summary>
internal sealed class BuriedKingView : IDisposable
{
    private const int ArcSegments = 24;
    private const float FloorOffset = 0.06f;
    private readonly int _id;
    private readonly GameObject _root;
    private readonly Material _danger;
    private readonly Material _sandstone;
    private readonly LineRenderer _weapon;
    private readonly LineRenderer _tell;
    private readonly LineRenderer _landing;
    private readonly IDisposable _stateSubscription;

    public BuriedKingView(Transform body, int id, DomainEventHub hub)
    {
        _id = id;
        _root = new GameObject("Buried King prototype");
        _root.transform.SetParent(body, false);
        _danger = MakeMaterial(new Color(1f, 0.24f, 0.07f));
        _sandstone = MakeMaterial(new Color(1f, 0.85f, 0.59f));
        _weapon = CreateLine("Scepter", _sandstone, 0.13f);
        _tell = CreateLine("Locked warning", _danger, 0.09f);
        _landing = CreateLine("Landing marker", _danger, 0.09f);
        HoldWeapon();
        _stateSubscription = hub.Subscribe<BuriedKingStateChanged>(OnState);
    }

    public void Dispose()
    {
        _stateSubscription.Dispose();
        if (_root != null)
        {
            _root.SetActive(false);
        }

        Destroy(_root);
        Destroy(_danger);
        Destroy(_sandstone);
    }

    private void OnState(BuriedKingStateChanged state)
    {
        if (state.EnemyId != _id || _root == null)
        {
            return;
        }

        _tell.enabled = false;
        _landing.enabled = false;
        Vector3 start = state.Start.ToUnity();
        Vector3 end = state.End.ToUnity();
        Vector3 planar = Vector3.ProjectOnPlane(end - start, Vector3.up);
        switch (state.Pose)
        {
            case BuriedKingPose.SweepTell:
                DrawSweep(start, planar, state.SweepDegrees);
                break;
            case BuriedKingPose.CastTell:
                DrawLane(start, end, state.Width);
                DrawLanding(end, state.Width * 0.5f);
                break;
            case BuriedKingPose.Outbound:
            case BuriedKingPose.Returning:
                Vector3 axis = planar.normalized;
                Vector3 head = state.WeaponPosition.ToUnity() + Vector3.up * 0.8f;
                DrawWeapon(head - axis * 1.15f, axis, Vector3.up, worldSpace: true);
                break;
            case BuriedKingPose.Embedded:
                DrawEmbedded(end);
                break;
            case BuriedKingPose.RecallTell:
                DrawEmbedded(start);
                DrawLane(start, end, state.Width);
                break;
            case BuriedKingPose.Held:
                HoldWeapon();
                break;
            case BuriedKingPose.Cleared:
                _root.SetActive(false);
                break;
        }
    }

    private void DrawSweep(Vector3 start, Vector3 reach, float degrees)
    {
        start += Vector3.up * FloorOffset;
        _tell.enabled = true;
        _tell.positionCount = ArcSegments + 3;
        _tell.SetPosition(0, start);
        for (int i = 0; i <= ArcSegments; i++)
        {
            float angle = Mathf.Lerp(-degrees * 0.5f, degrees * 0.5f, (float)i / ArcSegments);
            _tell.SetPosition(i + 1, start + Quaternion.AngleAxis(angle, Vector3.up) * reach);
        }

        _tell.SetPosition(ArcSegments + 2, start);
    }

    private void DrawLane(Vector3 start, Vector3 end, float width)
    {
        Vector3 halfWidth = Vector3.Cross(Vector3.up, end - start).normalized * (width * 0.5f);
        start += Vector3.up * FloorOffset;
        end += Vector3.up * FloorOffset;
        _tell.enabled = true;
        _tell.positionCount = 5;
        _tell.SetPosition(0, start - halfWidth);
        _tell.SetPosition(1, start + halfWidth);
        _tell.SetPosition(2, end + halfWidth);
        _tell.SetPosition(3, end - halfWidth);
        _tell.SetPosition(4, start - halfWidth);
    }

    private void DrawLanding(Vector3 end, float radius)
    {
        end += Vector3.up * FloorOffset;
        _landing.enabled = true;
        _landing.positionCount = ArcSegments + 1;
        for (int i = 0; i <= ArcSegments; i++)
        {
            float angle = 360f * i / ArcSegments;
            _landing.SetPosition(i, end + Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * radius);
        }
    }

    private void HoldWeapon() =>
        DrawWeapon(new Vector3(0.58f, 0.35f, 0.22f), new Vector3(0.25f, 1f, 0f).normalized,
            Vector3.forward, worldSpace: false);

    private void DrawEmbedded(Vector3 position) =>
        DrawWeapon(position, Vector3.up, Vector3.right, worldSpace: true);

    private void DrawWeapon(Vector3 basePoint, Vector3 axis, Vector3 side, bool worldSpace)
    {
        Vector3 head = basePoint + axis * 1.15f;
        _weapon.enabled = true;
        _weapon.useWorldSpace = worldSpace;
        _weapon.positionCount = 6;
        _weapon.SetPosition(0, basePoint);
        _weapon.SetPosition(1, head - axis * 0.18f);
        _weapon.SetPosition(2, head - side * 0.23f);
        _weapon.SetPosition(3, head + axis * 0.18f);
        _weapon.SetPosition(4, head + side * 0.23f);
        _weapon.SetPosition(5, head - axis * 0.18f);
    }

    private LineRenderer CreateLine(string name, Material material, float width)
    {
        var child = new GameObject(name);
        child.transform.SetParent(_root.transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.widthMultiplier = width;
        line.positionCount = 0;
        line.enabled = false;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return line;
    }

    private static Material MakeMaterial(Color color) =>
        new(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };

    private static void Destroy(Object resource)
    {
        if (Application.isPlaying)
        {
            Object.Destroy(resource);
        }
        else
        {
            Object.DestroyImmediate(resource);
        }
    }
}
