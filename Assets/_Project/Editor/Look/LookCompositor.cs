using System;
using System.Globalization;
using UnityEngine;

namespace Soulvail.Editor.Look;

/// <summary>
/// The pure half of <see cref="LookCapture"/>: lays the UI pass over the world pass and names the
/// file (UI-07b).
/// </summary>
public static class LookCompositor
{
    /// <summary>
    /// <paramref name="ui"/> over <paramref name="world"/>, pixel by pixel, both in straight alpha.
    /// </summary>
    /// <exception cref="ArgumentException">The arrays are of unequal length.</exception>
    public static Color32[] Over(Color32[] world, Color32[] ui)
    {
        if (world == null)
            throw new ArgumentNullException(nameof(world));
        if (ui == null)
            throw new ArgumentNullException(nameof(ui));
        if (world.Length != ui.Length)
        {
            throw new ArgumentException(
                $"The world pass has {world.Length} pixels and the UI pass {ui.Length}.", nameof(ui));
        }

        var result = new Color32[world.Length];
        for (int i = 0; i < world.Length; i++)
        {
            result[i] = Over(world[i], ui[i]);
        }

        return result;
    }

    /// <summary>
    /// Undoes premultiplied alpha in place: UGUI's and TMP's shaders blend <c>One OneMinusSrcAlpha</c>
    /// over the transparent clear, so the UI pass reads back premultiplied, and <see cref="Over"/>
    /// wants it straight.
    /// </summary>
    public static void Unpremultiply(Color32[] pixels)
    {
        if (pixels == null)
            throw new ArgumentNullException(nameof(pixels));

        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 p = pixels[i];
            if (p.a == 255)
                continue;
            if (p.a == 0)
            {
                pixels[i] = default;
                continue;
            }

            pixels[i] = new Color32(Divide(p.r, p.a), Divide(p.g, p.a), Divide(p.b, p.a), p.a);
        }
    }

    /// <summary>The capture's file name, e.g. <c>Run_1080x2400_420dpi_20261014-153012.png</c>.</summary>
    public static string FileName(string scene, int width, int height, float dpi, DateTime at)
    {
        int roundedDpi = (int)Math.Round(dpi, MidpointRounding.AwayFromZero);
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}_{1}x{2}_{3}dpi_{4:yyyyMMdd-HHmmss}.png",
            scene, width, height, roundedDpi, at);
    }

    private static Color32 Over(Color32 world, Color32 ui)
    {
        if (ui.a == 255)
            return ui;
        if (ui.a == 0)
            return world;

        float ua = ui.a / 255f;
        float wa = world.a / 255f * (1f - ua);
        float outA = ua + wa;
        if (outA <= 0f)
            return default;

        return new Color32(
            Channel(ui.r, world.r, ua, wa, outA),
            Channel(ui.g, world.g, ua, wa, outA),
            Channel(ui.b, world.b, ua, wa, outA),
            (byte)Math.Round(outA * 255f));
    }

    private static byte Channel(byte ui, byte world, float ua, float wa, float outA)
    {
        float value = (ui * ua + world * wa) / outA;
        return (byte)Math.Min(255, Math.Round(value));
    }

    private static byte Divide(byte channel, byte alpha) =>
        (byte)Math.Min(255, Math.Round(channel * 255f / alpha));
}
