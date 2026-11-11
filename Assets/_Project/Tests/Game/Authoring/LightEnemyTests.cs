using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Game.Authoring;
using UnityEditor;

namespace Soulvail.Tests.Game.Authoring;

[TestFixture]
public sealed class LightEnemyTests
{
    [Test]
    public void Assets_LightSet_MatchesDesign()
    {
        var light = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/_Project/Data/Enemies" }))
        {
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition.ToSpec().IsLight)
            {
                light.Add(definition.name);
            }
        }

        Assert.That(light, Is.EquivalentTo(new[]
        {
            "Frog", "Rootling", "Puffball", "Spitter", "Pitcher", "Panther", "ShadowPanther", "ThornPitcher", "ChainBomber", "Scorpion", "Beetle", "Viper", "Jackal", "Glassback",
        }));
    }
}
