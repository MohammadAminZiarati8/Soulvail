using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Director;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Arena;

/// <summary>
/// GD §7.2, asserted against the arenas that actually ship — and the mode roster that names them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every rule here is also in <c>ArenaView.OnValidate</c>, and that is the point rather than a
/// duplication.</b> The Inspector warning is what a designer sees while dragging a pillar; these
/// rows are what stops a prefab shipping with the warning ignored. Both read the same
/// <c>DescribeFaults</c>, so the two accounts of what a legal arena is cannot drift.
/// </para>
/// <para>
/// A separate fixture from <c>ArenaPoolTests</c> — a deviation from the spec's Files table, which
/// lists one test file. These rows are about the <em>assets</em> and would fail when nobody had
/// touched the pool; folding them into the pool's fixture would make the failure read as a pool
/// bug.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ArenaViewTests
{
    private const string ArenaFolder = "Assets/_Project/Prefabs/Arenas";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";

    /// <summary>Every arena prefab under <see cref="ArenaFolder"/>, so a new one is covered by being added.</summary>
    private static IEnumerable<string> ArenaPaths()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ArenaFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<ArenaView>() != null)
            {
                yield return path;
            }
        }
    }

    [Test]
    public void Arenas_AreShipped()
    {
        // The list below drives every row here, so an empty one would make all of them pass by
        // measuring nothing.
        Assert.That(ArenaPaths(), Is.Not.Empty);
    }

    [TestCaseSource(nameof(ArenaPaths))]
    public void Arena_HasNoFaults(string path)
    {
        // The whole of GD §7.2 as this class states it, in one assertion: at least three spawn
        // points, every one of them clear of the player start, 3-6 pillars on the Cover layer, a
        // baked surface, a start and an id that parses.
        Assert.That(Arena(path).DescribeFaults(), Is.Null);
    }

    [TestCaseSource(nameof(ArenaPaths))]
    public void Arena_HasAtLeastThreeSpawnPoints(string path)
    {
        Assert.That(
            Arena(path).SpawnPoints.Count,
            Is.GreaterThanOrEqualTo(ArenaView.MinSpawnPoints));
    }

    [TestCaseSource(nameof(ArenaPaths))]
    public void Arena_SpawnPointsClearThePlayerStart(string path)
    {
        ArenaView arena = Arena(path);

        Vector3 start = arena.PlayerStart;

        foreach (Vector3 point in arena.SpawnPoints)
        {
            // XZ, like every other separation in the game (AR §18.4).
            float dx = point.x - start.x;
            float dz = point.z - start.z;

            Assert.That(
                Mathf.Sqrt((dx * dx) + (dz * dz)),
                Is.GreaterThanOrEqualTo(SpawnDirector.MinPlayerDistance),
                $"{point} is inside GD §12.4's clearance, so the director can never use it — half " +
                "this arena's floor would be unavailable to its own waves.");
        }
    }

    [TestCaseSource(nameof(ArenaPaths))]
    public void Arena_HasThreeToSixCoverPillars(string path)
    {
        ArenaView arena = Arena(path);

        Assert.That(arena.CoverCount, Is.InRange(ArenaView.MinCoverPillars, ArenaView.MaxCoverPillars));

        // Counted by layer, which is the same fact M2-11b's raycast mask will read: a pillar that
        // was never put on the layer is invisible to both, and this is where that is caught
        // (ledger row 13).
        Assert.That(LayerMask.NameToLayer(ArenaView.CoverLayerName), Is.GreaterThanOrEqualTo(0),
            "The Cover layer has to exist before anything can be on it (TagManager.asset).");
    }

    [TestCaseSource(nameof(ArenaPaths))]
    public void Arena_HasBakedNavMesh(string path)
    {
        ArenaView arena = Arena(path);

        Assert.That(arena.Surface, Is.Not.Null);
        Assert.That(arena.Surface.navMeshData, Is.Not.Null,
            "Baked per prefab and never at runtime: a BuildNavMesh at a stage boundary is a " +
            "multi-frame hitch behind a 0.3 s fade (rule 7).");
    }

    [TestCaseSource(nameof(ArenaPaths))]
    public void Arena_HasADoor(string path)
    {
        // Not a GD §7.2 rule and deliberately not in DescribeFaults: an arena without a door is
        // legal and parks the stage flow at its gate (M2-10 rule 15). It is asserted of the
        // *shipped* arenas because a run that cannot be left is not a run.
        Assert.That(Arena(path).HasGate, Is.True);
    }

    [TestCaseSource(nameof(ArenaPaths))]
    public void Arena_IdMatchesTheAssetName(string path)
    {
        // Arena_Pillars.prefab <-> arena.pillars, per the asset-naming rule: a data asset's file
        // name matches the last segment of its ContentId.
        string file = System.IO.Path.GetFileNameWithoutExtension(path);
        string expected = file.Replace("Arena_", string.Empty).ToLowerInvariant();

        Assert.That(Arena(path).Id.Value, Is.EqualTo($"arena.{expected}"));
    }

    [Test]
    public void Descent_RostersBothArenas()
    {
        ModeSpec descent = AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath).ToSpec();

        Assert.That(
            descent.Arenas,
            Is.EqualTo(new[] { new ContentId("arena.pillars"), new ContentId("arena.tiered") }),
            "Two, not eight, as a statement rather than a promise: GD §7.2 wants 8-12 per biome " +
            "and that is M7-05's art pass. What this milestone owes is the contract and the " +
            "machinery (rule 11).");
    }

    [Test]
    public void Descent_EveryArenaItRostersIsShipped()
    {
        // Rule 4's real content: an arena a mode names and no prefab carries is a run that reaches
        // a stage boundary and swaps nothing. ArenaPool throws on it at the stage that names it;
        // this is what stops it ever getting that far.
        ModeSpec descent = AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath).ToSpec();

        var shipped = new List<ContentId>();

        foreach (string path in ArenaPaths())
        {
            shipped.Add(Arena(path).Id);
        }

        foreach (ContentId id in descent.Arenas)
        {
            Assert.That(shipped, Does.Contain(id));
        }
    }

    private static ArenaView Arena(string path)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(go, Is.Not.Null, $"No prefab at {path}.");

        return go.GetComponent<ArenaView>();
    }
}
