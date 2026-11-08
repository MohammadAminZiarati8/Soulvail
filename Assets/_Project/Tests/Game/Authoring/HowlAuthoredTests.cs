using System;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using static Soulvail.Tests.Game.Authoring.PackleaderPathTests;

namespace Soulvail.Tests.Game.Authoring;

public sealed class HowlAuthoredTests
{
    private static readonly ContentId PathId = new("path.packleader");
    private static readonly ContentId HowlId = new("skill.packleader.howl");

    [Test]
    public void Howl_FourRanks_Numbers_Trigger()
    {
        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        PathSkillSpec row = catalog.Path(PathId).Skills[1];
        Assert.That(row.Ranks.TierCount, Is.EqualTo(4));

        var icon = Load<Sprite>("Art/UI/Icons/Icon_Placeholder.png");
        var skills = new SkillSpec[4];
        ContentId parent = default;
        for (int i = 0; i < skills.Length; i++)
        {
            ContentId id = row.Ranks.Tier(i + 1).Single();
            skills[i] = catalog.Skill(id);
            string file = i == 0 ? "Howl" : $"Howl{i + 1}";
            Assert.That(Load<SkillDefinition>($"Data/Skills/Packleader/{file}.asset").Icon, Is.SameAs(icon));
            Assert.That(skills[i].Kind, Is.EqualTo(i == 0 ? SkillKind.Active : SkillKind.Upgrade));
            Assert.That(skills[i].ParentId, Is.EqualTo(parent));
            parent = id;
        }

        ActiveSpec active = skills[0].Active;
        Assert.That(active.Cooldown, Is.EqualTo(14f));
        var cast = (Howl)active.OnCast.Single();
        Assert.That(cast.AttackSpeedBonus, Is.EqualTo(0.25f));
        TriggerClause clause = active.Trigger.Clauses.Single();
        Assert.That((clause.Field, clause.Comparison, clause.Threshold),
            Is.EqualTo((TriggerField.LowestPetHpFraction, TriggerComparison.Below, 0.5f)));

        var heal = (ModifyStat)skills[1].Effects.Single();
        var duration = (ModifyStat)skills[2].Effects.Single();
        Assert.That((heal.Stat, heal.Kind, heal.Value, heal.Target),
            Is.EqualTo((PlayerStat.HowlHeal, ModifierKind.Flat, 0.1f, StatTarget.Player)));
        Assert.That((duration.Stat, duration.Kind, duration.Value, duration.Target),
            Is.EqualTo((PlayerStat.HowlSeconds, ModifierKind.Flat, 2f, StatTarget.Player)));
        var cooldown = (ModifySkillCooldown)skills[3].Effects.Single();
        Assert.That((cooldown.SkillId, cooldown.Kind, cooldown.Value),
            Is.EqualTo((HowlId, ModifierKind.Flat, -3f)));

        var definition = ScriptableObject.CreateInstance<HowlDefinition>();
        try
        {
            definition.name = "Invalid Howl";
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_attackSpeedBonus").floatValue = float.NaN;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(() => definition.ToEffect(), Throws.ArgumentException.With.Message.Contains(definition.name));
        }
        finally
        {
            Object.DestroyImmediate(definition);
        }

        using var hub = new DomainEventHub();
        RunSession session = StartRun(catalog, hub, Load<CharacterDefinition>("Data/Characters/Beastmaster.asset"), level: 11);
        session.ChoosePath(PathId);
        session.TakeNode(HowlId);
        Assert.That(session.State.IsAutoCast(HowlId), Is.True);
    }

    [Test]
    public void Path_FirstTwoRows_PackHoundsThenHowl_At11()
    {
        using IObjectResolver boot = Boot();
        var rows = boot.Resolve<ContentCatalog>().Path(PathId).Skills.Take(2);
        Assert.That(rows.Select(row => row.Ranks.Tier(1).Single()),
            Is.EqualTo(new[] { new ContentId("skill.packleader.pack-hounds"), HowlId }));
        Assert.That(rows.Select(row => row.UnlockLevel), Is.EqualTo(new[] { 11, 11 }));
    }

    [Test]
    public void LearnedHowl_UnlocksItsActiveArcCircle()
    {
        using IObjectResolver boot = Boot();
        var catalog = boot.Resolve<ContentCatalog>();
        using var hub = new DomainEventHub();
        RunSession session = StartRun(catalog, hub, Load<CharacterDefinition>("Data/Characters/Beastmaster.asset"), level: 11);
        session.ChoosePath(PathId);

        GameObject hud = Object.Instantiate(Load<GameObject>("Prefabs/UI/Hud.prefab"));
        try
        {
            var arc = hud.GetComponentInChildren<SkillArc>(true);
            arc.Construct(session, catalog, boot.Resolve<SkillIconBook>(), boot.Resolve<CharacterLookBook>(),
                new SkillArcInput(session, session), boot.Resolve<ILocalizer>());
            SkillArcButton circle = arc.Actives.Single(button => button.Id == HowlId);
            Assert.That(circle.IsLocked, Is.True);

            session.TakeNode(HowlId);
            arc.Refresh();

            Assert.That(circle.IsLocked, Is.False);
            Assert.That(circle.AutoBadge, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(hud);
        }
    }

    [Test]
    public void HowlRanks_HaveLocKeys()
    {
        PathDefinition path = Load<PathDefinition>("Data/Paths/Packleader.asset");
        var ranks = path.Skills[1].Ranks.Select(rank => rank.ToSpec()).ToArray();
        var english = Load<LocalizationTable>("Data/Localisation/English.asset").ToDictionary();
        var pseudo = Load<LocalizationTable>("Data/Localisation/Pseudo.asset").ToDictionary();

        foreach (SkillSpec rank in ranks)
        {
            Assert.That(english[rank.NameKey], Does.StartWith("Howl"));
            Assert.That(english[rank.DescriptionKey], Is.Not.Empty.And.Not.Contains("\n"));
            Assert.That(pseudo[rank.NameKey], Does.StartWith("[").And.EndWith("]"));
            Assert.That(pseudo[rank.DescriptionKey], Does.StartWith("[").And.EndWith("]").And.Not.Contains("\n"));
        }

        Assert.That(ranks.Select(rank => rank.NameKey).Distinct().Count(), Is.EqualTo(4));
        Assert.That(ranks.Select(rank => rank.DescriptionKey).Distinct().Count(), Is.EqualTo(4));
    }
}
