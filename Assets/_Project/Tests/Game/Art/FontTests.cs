using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Soulvail.Tests.Game.Art;

/// <summary>
/// UI-07c: every player-facing text names <c>LilitaOne_SDF</c> itself, never by inheriting TMP's default.
/// </summary>
[TestFixture]
public sealed class FontTests
{
    private const string FontPath = "Assets/_Project/Art/UI/Fonts/LilitaOne_SDF.asset";
    private const string UiFolder = "Assets/_Project/Prefabs/UI";
    private const string MenuScene = "Assets/_Project/Scenes/Menu.unity";

    /// <summary>
    /// A development readout that never ships.
    /// </summary>
    private const string Exempt = "DebugOverlay";

    private static IEnumerable<string> PrefabPaths() => AssetDatabase.FindAssets("t:Prefab", new[] { UiFolder })
        .Select(AssetDatabase.GUIDToAssetPath)
        .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) != Exempt)
        .OrderBy(p => p);

    [Test]
    public void Fonts_EveryUiPrefabText_IsLilitaOne()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Assert.That(font, Is.Not.Null);
        var wrong = new List<string>();

        foreach (string path in PrefabPaths())
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);

            try
            {
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.font != font)
                    {
                        wrong.Add($"{path} › {text.name}: {(text.font != null ? text.font.name : "none")}");
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Assert.That(wrong, Is.Empty, "Texts not on LilitaOne_SDF:\n" + string.Join("\n", wrong));
    }

    [Test]
    public void Fonts_MenuSceneText_IsLilitaOne()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Scene scene = EditorSceneManager.OpenScene(MenuScene, OpenSceneMode.Additive);
        var wrong = new List<string>();

        try
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.font != font)
                    {
                        wrong.Add($"{text.name}: {(text.font != null ? text.font.name : "none")}");
                    }
                }
            }
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }

        Assert.That(wrong, Is.Empty, "Menu texts not on LilitaOne_SDF:\n" + string.Join("\n", wrong));
    }

    [Test]
    public void Fonts_DefaultFontAsset_IsUnchanged()
    {
        Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null);
        Assert.That(TMP_Settings.defaultFontAsset.name, Does.StartWith("LiberationSans"),
            "TMP Settings' default stays LiberationSans so a text added without a font fails the rows above.");
    }
}
