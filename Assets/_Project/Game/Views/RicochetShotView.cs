using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views;

/// <summary>The cyan-white trail shared by every leg of a Ricochet Shot.</summary>
internal sealed class RicochetShotView
{
    private readonly Gradient _gradient = new();

    public RicochetShotView()
    {
        _gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Palette.Player, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.35f), new GradientAlphaKey(0f, 1f) });
    }

    public void Brighten(TrailRenderer trail, float normalTime, float normalWidth)
    {
        trail.colorGradient = _gradient;
        trail.time = normalTime * 1.8f;
        trail.widthMultiplier = normalWidth * 1.35f;
    }
}
