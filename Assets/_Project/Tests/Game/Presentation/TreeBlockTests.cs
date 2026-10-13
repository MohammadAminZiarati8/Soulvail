using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Soulvail.Tests.Game.Presentation;

/// <summary>
/// UI-04c: the class's tree as one stone block — tiles, icons, badges, numerals, links, row labels and
/// the class plate.
/// </summary>
/// <remarks>
/// A third file of <see cref="TreeScreenTests"/> for <c>TreeFrameTests</c>' reason: the run, the
/// catalog and both prefabs are that fixture's private helpers.
/// </remarks>
public sealed partial class TreeScreenTests
{
    private const string IconPath = "Assets/_Project/Art/UI/Icons/";

    [Test]
    public void Tile_MatchesState()
    {
        ContentId first = TreeFirst(Ranger());

        StartRun(Ranger(), free: true, pending: 1, taken: new[] { first });
        Build();

        _screen.Open();

        Assert.That(TileName(CellFor(first)), Is.EqualTo("TreeTile_Taken"));
        Assert.That(TileName(CellFor(_tree.Branches[1].Tier(1)[0])), Is.EqualTo("TreeTile_Available"));
        Assert.That(TileName(CellFor(LockedNode())), Is.EqualTo("TreeTile_Locked"));
        Assert.That(Field<Image>(CellFor(first), "_tile").color, Is.EqualTo(Color.white), "the art carries the colour.");
    }

    [Test]
    public void Tile_Banished_IsLocked()
    {
        StartRun(Ranger(), free: false, pending: 1);
        Build();

        _screen.Open(null);

        TreeNodeButton cell = Shown()[0];

        cell.Show(_catalog.Skill(cell.Node), NodeState.Banished, null, 1);

        Assert.That(TileName(cell), Is.EqualTo("TreeTile_Locked"));
    }

    [Test]
    public void Icon_FromBook_ElsePlaceholder()
    {
        StartRun(Ranger(), free: true, pending: 1);

        ContentId withIcon = _tree.Branches[0].Tier(1)[0];
        Sprite volley = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath + "Icon_Volley.png");

        _iconMap[withIcon] = volley;
        Build();

        _screen.Open();

        Assert.That(Field<Image>(CellFor(withIcon), "_icon").sprite, Is.SameAs(volley));
        Assert.That(
            Field<Image>(CellFor(_tree.Branches[1].Tier(1)[0]), "_icon").sprite.name,
            Is.EqualTo("Icon_Placeholder"));
    }

    [Test]
    public void Icon_Locked_IsDimmed()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        _screen.Open();

        Assert.That(Field<Image>(CellFor(LockedNode()), "_icon").color, Is.EqualTo(Palette.NodeLocked));
        Assert.That(Field<Image>(CellFor(TreeFirst(_tree)), "_icon").color, Is.EqualTo(Color.white));
    }

    [Test]
    public void Badge_ByStateAndPoints()
    {
        ContentId first = TreeFirst(Ranger());

        StartRun(Ranger(), free: true, pending: 1, taken: new[] { first });
        Build();

        _screen.Open();

        Assert.That(BadgeName(CellFor(first)), Is.EqualTo("TreeBadge_Check"));
        Assert.That(BadgeName(CellFor(_tree.Branches[1].Tier(1)[0])), Is.EqualTo("TreeBadge_Plus"));
        Assert.That(BadgeName(CellFor(LockedNode())), Is.EqualTo("TreeBadge_Lock"));

        DestroyWorld();
        CreateWorld();
        StartRun(Ranger(), free: true, pending: 0, taken: new[] { first });
        Build();

        _screen.Open();

        Assert.That(BadgeName(CellFor(_tree.Branches[1].Tier(1)[0])), Is.Null, "a plus with no point to spend.");
        Assert.That(BadgeName(CellFor(first)), Is.EqualTo("TreeBadge_Check"));
    }

    [Test]
    public void Numeral_IsTierInBranch()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        _screen.Open();

        string[] expected = { "I", "II", "III" };

        foreach (SkillBranchSpec branch in _tree.Branches)
        {
            for (int t = 1; t <= branch.TierCount; t++)
            {
                Assert.That(Field<TMP_Text>(CellFor(branch.Tier(t)[0]), "_numeral").text, Is.EqualTo(expected[t - 1]));
            }
        }
    }

    [Test]
    public void Link_LitWhenLowerTaken()
    {
        ContentId first = TreeFirst(Ranger());

        StartRun(Ranger(), free: true, pending: 0, taken: new[] { first });
        Build();

        _screen.Open();

        TreeBlockView block = Field<TreeBlockView>(_screen, "_block");
        SkillBranchSpec branch = _tree.Branches[0];

        Assert.That(block.LinkAbove(branch.Tier(1)[0]).name, Is.EqualTo("TreeLink_Lit"));
        Assert.That(block.LinkAbove(branch.Tier(2)[0]).name, Is.EqualTo("TreeLink_Dark"));
        Assert.That(block.LinkAbove(branch.Tier(3)[0]), Is.Null, "the top of a branch has nothing above it.");
        Assert.That(block.LinkAbove(_tree.Branches[1].Tier(1)[0]).name, Is.EqualTo("TreeLink_Dark"));
    }

    [Test]
    public void Rows_OnePerTier()
    {
        StartRun(Ranger(), free: true, pending: 1);
        Build(Shipped());

        _screen.Open();

        TreeBlockView block = Field<TreeBlockView>(_screen, "_block");

        Assert.That(block.RowCount, Is.EqualTo(3));
        Assert.That(Enumerable.Range(0, 3).Select(block.RowText), Is.EqualTo(new[] { "LV 1", "LV 2", "LV 3" }));

        DestroyWorld();
        CreateWorld();
        StartRun(Oathbound(), free: true, pending: 1);
        Build(Shipped());

        _screen.Open();

        Assert.That(Field<TreeBlockView>(_screen, "_block").RowCount, Is.EqualTo(_tree.Branches.Max(b => b.TierCount)));
    }

    [Test]
    public void Plate_NameAndEmblem()
    {
        Sprite emblem = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/UI/Tree/Emblem_Ranger.png");

        _lookMap[new ContentId(CharacterId)] = new CharacterLook(null, emblem: emblem);

        StartRun(Ranger(), free: true, pending: 1);
        Build();

        OpenAt(Portrait);

        TreeBlockView block = Field<TreeBlockView>(_screen, "_block");
        var image = Field<Image>(block, "_emblem");
        var name = Field<TMP_Text>(block, "_className");

        Assert.That(image.gameObject.activeSelf, Is.True);
        Assert.That(image.sprite, Is.SameAs(emblem));
        Assert.That(name.text, Is.Not.Empty);
        Assert.That(
            WorldRect(name.rectTransform).xMin,
            Is.GreaterThanOrEqualTo(WorldRect(image.rectTransform).xMax - 0.5f),
            "the name runs under the emblem.");

        DestroyWorld();
        CreateWorld();
        StartRun(Ranger(), free: true, pending: 1);
        Build();

        OpenAt(Portrait);

        block = Field<TreeBlockView>(_screen, "_block");
        name = Field<TMP_Text>(block, "_className");

        Assert.That(Field<Image>(block, "_emblem").gameObject.activeSelf, Is.False);
        Assert.That(
            WorldRect(name.rectTransform).center.x,
            Is.EqualTo(WorldRect(Field<RectTransform>(block, "_plate")).center.x).Within(0.5f),
            "a nameless-emblem plate centres its name.");
    }

    [Test]
    public void Take_RedrawsTileBadgeLink()
    {
        StartRun(Ranger(), free: true, pending: 2);
        Build();

        _screen.Open();

        TreeBlockView block = Field<TreeBlockView>(_screen, "_block");
        ContentId tier1 = _tree.Branches[0].Tier(1)[0];
        ContentId tier2 = _tree.Branches[0].Tier(2)[0];

        Assert.That(TileName(CellFor(tier1)), Is.EqualTo("TreeTile_Available"));
        Assert.That(block.LinkAbove(tier1).name, Is.EqualTo("TreeLink_Dark"));

        Tap(CellFor(tier1));

        Assert.That(TileName(CellFor(tier1)), Is.EqualTo("TreeTile_Taken"));
        Assert.That(BadgeName(CellFor(tier1)), Is.EqualTo("TreeBadge_Check"));
        Assert.That(block.LinkAbove(tier1).name, Is.EqualTo("TreeLink_Lit"));
        Assert.That(TileName(CellFor(tier2)), Is.EqualTo("TreeTile_Available"));
        Assert.That(BadgeName(CellFor(tier2)), Is.EqualTo("TreeBadge_Plus"));
    }

    [Test]
    public void Block_FitsPortrait()
    {
        float px = StickShaper.PixelsPerDp(Screen.dpi);

        foreach (SkillTreeSpec shape in new[] { Ranger(), Oathbound() })
        {
            DestroyWorld();
            CreateWorld();
            StartRun(shape, free: true, pending: 1);
            Build(Shipped());

            OpenAt(Portrait);

            Rect frame = WorldRect(Piece("_frame"));
            TreeBlockView block = Field<TreeBlockView>(_screen, "_block");
            Rect plate = WorldRect(Field<RectTransform>(block, "_plate"));

            var pieces = Shown().Select(c => WorldRect(c)).ToList();

            pieces.Add(plate);
            pieces.AddRange(Field<TMP_Text[]>(_screen, "_branchLabels").Select(l => WorldRect(l.rectTransform)));
            pieces.AddRange(Enumerable.Range(0, block.RowCount).Select(i => WorldRect(Field<System.Collections.Generic.List<TMP_Text>>(block, "_rows")[i].rectTransform)));

            foreach (Rect piece in pieces)
            {
                Assert.That(
                    piece.xMin >= frame.xMin - 0.5f && piece.xMax <= frame.xMax + 0.5f
                        && piece.yMin >= frame.yMin - 0.5f && piece.yMax <= frame.yMax + 0.5f,
                    Is.True,
                    $"{piece} leaves the frame {frame}.");
            }

            // The card's room under the block (rule 10).
            Assert.That(plate.yMin, Is.GreaterThanOrEqualTo(frame.yMin + (190f * px) - 0.5f));

            foreach (TMP_Text text in _screen.GetComponentsInChildren<TMP_Text>(false))
            {
                Assert.That(text.fontSize / px, Is.GreaterThanOrEqualTo(12f - 1e-3f), $"{text.name} is under the floor.");
            }
        }
    }

    [Test]
    public void RangerSkills_HaveBranchIcons()
    {
        foreach ((string family, string icon) in new[]
        {
            ("QuickDraw", "Icon_QuickDraw"), ("Volley", "Icon_Volley"), ("FleetFoot", "Icon_FleetFoot"),
        })
        {
            foreach (string tier in new[] { "", "2", "3" })
            {
                var skill = AssetDatabase.LoadAssetAtPath<SkillDefinition>($"Assets/_Project/Data/Skills/Ranger/{family}{tier}.asset");

                Assert.That(skill, Is.Not.Null, $"{family}{tier} is missing.");
                Assert.That(skill.Icon, Is.Not.Null, $"{family}{tier} has no icon.");
                Assert.That(skill.Icon.name, Is.EqualTo(icon), $"{family}{tier}.");
            }
        }
    }

    private static string TileName(TreeNodeButton cell) => Field<Image>(cell, "_tile").sprite.name;

    private static string BadgeName(TreeNodeButton cell)
    {
        var badge = Field<Image>(cell, "_badge");

        return badge.gameObject.activeSelf ? badge.sprite.name : null;
    }
}
