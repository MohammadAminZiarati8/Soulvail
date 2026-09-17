using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// Covers all four of M3-02b's definition types in one fixture — the effect base, the first
/// primitive's authoring half, the node and the tree — plus the two boot lists they exist to fill.
/// One fixture because they are one unit of authoring: a node references effect assets and a tree
/// references nodes, so no row about one can be written without the other two.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="EnemyDefinitionTests"/> and <see cref="CharacterDefinitionTests"/>:
/// fixtures are built with <see cref="ScriptableObject.CreateInstance{T}()"/> and poked through
/// <see cref="SerializedObject"/>, because the fields are <c>[SerializeField] private</c> and their
/// <c>[Min]</c> attributes only clamp the Inspector GUI — a <c>SerializedProperty</c> write goes
/// straight past them, which is exactly the hole <c>ToSpec</c> exists to close (M0-11, Traps §5).
/// </para>
/// <para>
/// <b>Nothing here loads an asset from <c>Data/</c>, and that is the point.</b> M3-02b ships no
/// content (its rule 7) — the Oathbound's nodes are M3-12's — so every fixture below is built in
/// memory and destroyed at teardown. The one asset this fixture does read is
/// <c>BootScope.prefab</c>, which is where the two empty lists live.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SkillAuthoringTests
{
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";

    /// <summary>
    /// Tight enough that no two of a node's numbers could satisfy each other's assertion, loose
    /// enough to survive Unity writing a float back as decimal text.
    /// </summary>
    private const float Tolerance = 1e-6f;

    private readonly List<ScriptableObject> _created = new List<ScriptableObject>();
    private readonly List<IDisposable> _containers = new List<IDisposable>();

    [TearDown]
    public void DestroyCreatedInstances()
    {
        for (int i = _containers.Count - 1; i >= 0; i--)
        {
            _containers[i].Dispose();
        }

        _containers.Clear();

        foreach (ScriptableObject definition in _created)
        {
            if (definition != null)
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        _created.Clear();
    }

    // ---------------------------------------------------------------- ModifyStatDefinition

    [Test]
    public void ModifyStat_ToEffect_RoundTrip()
    {
        ModifyStatDefinition definition = NewEffect(
            "FireRateNode", PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.15f);

        IEffect effect = definition.ToEffect();

        Assert.That(effect, Is.InstanceOf<ModifyStat>());

        var modify = (ModifyStat)effect;
        Assert.That(modify.Stat, Is.EqualTo(PlayerStat.WeaponDamage));
        Assert.That(modify.Kind, Is.EqualTo(ModifierKind.PercentAdd));
        Assert.That(modify.Value, Is.EqualTo(0.15f).Within(Tolerance));

        // Traps §7: `_kind` initialises to PercentAdd, so the assertion above passes identically
        // whether the field binds or holds its C# initialiser — it is the one of the three whose
        // authored value equals its default. Flipping it is what proves the key is live, and it is
        // the same shape as Husk_EveryYamlKeyBindsToAField one layer along.
        SetEnum(definition, "_kind", (int)ModifierKind.Flat);

        var flat = (ModifyStat)definition.ToEffect();
        Assert.That(flat.Kind, Is.EqualTo(ModifierKind.Flat));

        // ADR-0006: the SO holds no runtime state. A cached effect handed out twice would be one
        // object shared by every run that takes the node.
        Assert.That(definition.ToEffect(), Is.Not.SameAs(flat));
    }

    [Test]
    public void ModifyStat_Invalid_NamesTheAsset()
    {
        ModifyStatDefinition definition = NewEffect(
            "BrokenEffectValue", PlayerStat.MaxHp, ModifierKind.Flat, float.NaN);

        // ToEffect promises a plain ArgumentException whatever the inner failure was — Assert.Throws
        // is an exact type match (M0-08), and that is the contract rather than an accident of it.
        var thrown = Assert.Throws<ArgumentException>(() => definition.ToEffect());

        Assert.That(thrown.Message, Does.StartWith("ModifyStatDefinition 'BrokenEffectValue'"),
            "The message must lead with the asset, or the Console points at no file to open.");

        // The inner exception is what says *which* field, and dropping it would leave the log
        // saying only that some asset is wrong. ModifyStat refuses NaN, not this type (rule 2).
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ModifyStat_UndefinedAddress_NamesTheAsset()
    {
        // **The seam M3-05 left open, closed here by the owner's ruling at M3-02b.**
        // EffectRegistry.CanApply answers "is there a handler for this type", not "does this
        // effect's address resolve", and ModifyStat deliberately does not validate the address
        // either — so without this door a node authored against an int that is no longer a
        // PlayerStat member passes every check in the game and throws out of PlayerStats.Resolve
        // at the moment a player picks it. The live vector is a removed or reordered enum member
        // leaving its old number behind in an asset, which is what setting the raw int reproduces.
        ModifyStatDefinition definition = NewEffect(
            "StaleAddress", PlayerStat.MaxHp, ModifierKind.Flat, 2f);

        SetEnum(definition, "_stat", 99);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToEffect());

        Assert.That(thrown.Message, Does.StartWith("ModifyStatDefinition 'StaleAddress'"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ModifyStat_OnValidate_UndefinedAddress_Warns()
    {
        ModifyStatDefinition definition = NewEffect(
            "WarnsOnStaleAddress", PlayerStat.MaxHp, ModifierKind.Flat, 2f);

        // LogAssert fails at teardown if this warning never arrives, which is what makes the row
        // load-bearing: a silent OnValidate would go unnoticed otherwise, since a stray warning
        // does not fail a test on its own (M0-11).
        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("WarnsOnStaleAddress")));

        SetEnum(definition, "_stat", 99);
    }

    [Test]
    public void Effect_IsPolymorphic()
    {
        // Rule 1, and the row is written so that the *caller* is the assertion: the array is typed
        // as the abstract base, the loop knows nothing about ModifyStatDefinition, and there is no
        // cast and no `is` anywhere in it. The day a second primitive is authored, this loop reads
        // it with no edit — which is the whole of what a ScriptableObject per primitive buys over
        // a union struct whose ToEffect would be a switch (ADR-0009).
        EffectDefinition[] effects =
        {
            NewEffect("PolyA", PlayerStat.MaxHp, ModifierKind.Flat, 20f),
            NewEffect("PolyB", PlayerStat.MoveSpeed, ModifierKind.PercentAdd, 0.1f),
        };

        var built = new IEffect[effects.Length];

        for (int i = 0; i < effects.Length; i++)
        {
            built[i] = effects[i].ToEffect();
        }

        Assert.That(built, Has.Length.EqualTo(2));
        Assert.That(built[0], Is.Not.Null);
        Assert.That(built[1], Is.Not.Null);
        Assert.That(built[1], Is.Not.SameAs(built[0]));
    }

    // -------------------------------------------------------------- GrantShieldDefinition

    [Test]
    public void GrantShield_ToEffect_RoundTrip()
    {
        // 22 and 3, not the 35 and 5 the spec names: both fields carry those as C# initialisers, so
        // asserting them would pass identically whether the YAML key binds or the field is holding
        // its initialiser (Traps §7). The numbers are arbitrary; being different from the defaults
        // is not.
        GrantShieldDefinition definition = NewGrantShield("Bulwark", amount: 22f, duration: 3f);

        IEffect effect = definition.ToEffect();

        Assert.That(effect, Is.InstanceOf<GrantShield>());

        var grant = (GrantShield)effect;

        Assert.That(grant.Amount, Is.EqualTo(22f).Within(Tolerance));
        Assert.That(grant.Duration, Is.EqualTo(3f).Within(Tolerance));

        // ADR-0006: the SO holds no runtime state. A cached effect handed out twice would be one
        // object shared by every run that takes the node — and for a *timed* effect that is sharper
        // than for a passive, because the pair (effect, source) is what TimedEffects books a
        // deadline against.
        Assert.That(definition.ToEffect(), Is.Not.SameAs(grant));
    }

    [Test]
    public void GrantShield_Invalid_NamesTheAsset()
    {
        // Zero seconds: legal as a float, refused as content. GrantShield's constructor is the one
        // account of what a legal grant is and this type copies none of it — what it owes is the
        // asset's name at the front of the message (M0-11).
        GrantShieldDefinition definition = NewGrantShield("BrokenBulwark", amount: 35f, duration: 0f);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToEffect());

        Assert.That(
            thrown.Message,
            Does.StartWith("GrantShieldDefinition 'BrokenBulwark'"),
            "The message must lead with the asset, or the Console points at no file to open.");

        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());

        // And the other door, so the row is not one field's story told twice.
        GrantShieldDefinition none = NewGrantShield("EmptyBulwark", amount: 0f, duration: 5f);

        Assert.That(
            Assert.Throws<ArgumentException>(() => none.ToEffect()).Message,
            Does.StartWith("GrantShieldDefinition 'EmptyBulwark'"));
    }

    [Test]
    public void GrantShield_IsLinkedToAMonoScript()
    {
        // **M3-02b's actual check, and the failure mode it catches reports nothing anywhere**
        // (Traps §5). Unity 6.3's script importer does not understand `namespace X;`, so a
        // ScriptableObject declared file-scoped compiles, passes every row above — they build the
        // instance in code — and then loads as null off any asset that references it, with
        // `m_Script: {fileID: 0}` and no error. This is the only row that would go red for it.
        GrantShieldDefinition definition = NewGrantShield("LinkedBulwark", amount: 35f, duration: 5f);

        Assert.That(
            MonoScript.FromScriptableObject(definition),
            Is.Not.Null,
            "GrantShieldDefinition is not linked to a MonoScript — the usual cause is a file-scoped "
                + "namespace on a UnityEngine.Object type (Traps §5).");
    }

    // ---------------------------------------------------------- SpawnHealZoneDefinition

    [Test]
    public void Definition_ToEffect_RoundTrip()
    {
        // 2.5, 4, 7 and 0.25, not Consecrate's 3.5 / 6 / 3 / 0.5: all four fields carry those as C#
        // initialisers, so asserting them would pass identically whether the YAML key binds or the
        // field is holding its initialiser (Traps §7). The numbers are arbitrary; being different
        // from the defaults is not.
        SpawnHealZoneDefinition definition = NewHealZone(
            "Consecrate", radius: 2.5f, duration: 4f, healPerPulse: 7f, pulseInterval: 0.25f);

        IEffect effect = definition.ToEffect();

        Assert.That(effect, Is.InstanceOf<SpawnHealZone>());

        var zone = (SpawnHealZone)effect;

        Assert.That(zone.Radius, Is.EqualTo(2.5f).Within(Tolerance));
        Assert.That(zone.Duration, Is.EqualTo(4f).Within(Tolerance));
        Assert.That(zone.HealPerPulse, Is.EqualTo(7f).Within(Tolerance));
        Assert.That(zone.PulseInterval, Is.EqualTo(0.25f).Within(Tolerance));

        // ADR-0006: the SO holds no runtime state. A cached effect handed out twice would be one
        // object shared by every run that takes the node.
        Assert.That(definition.ToEffect(), Is.Not.SameAs(zone));
    }

    [Test]
    public void Definition_Invalid_NamesTheAsset()
    {
        // A radius of zero: legal as a float, refused as content. SpawnHealZone's constructor is the
        // one account of what a legal zone is and this type copies none of it — what it owes is the
        // asset's name at the front of the message (M0-11).
        SpawnHealZoneDefinition definition = NewHealZone(
            "BrokenConsecrate", radius: 0f, duration: 6f, healPerPulse: 3f, pulseInterval: 0.5f);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToEffect());

        Assert.That(
            thrown.Message,
            Does.StartWith("SpawnHealZoneDefinition 'BrokenConsecrate'"),
            "The message must lead with the asset, or the Console points at no file to open.");

        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());

        // And another door, so the row is not one field's story told twice.
        SpawnHealZoneDefinition still = NewHealZone(
            "StillConsecrate", radius: 3.5f, duration: 6f, healPerPulse: 3f, pulseInterval: 0f);

        Assert.That(
            Assert.Throws<ArgumentException>(() => still.ToEffect()).Message,
            Does.StartWith("SpawnHealZoneDefinition 'StillConsecrate'"));
    }

    [Test]
    public void Definition_IsLinkedToAMonoScript()
    {
        // **M3-02b's actual check, and the failure mode it catches reports nothing anywhere**
        // (Traps §5). Unity 6.3's script importer does not understand `namespace X;`, so a
        // ScriptableObject declared file-scoped compiles, passes every row above — they build the
        // instance in code — and then loads as null off any asset that references it, with
        // `m_Script: {fileID: 0}` and no error. This is the only row that would go red for it.
        SpawnHealZoneDefinition definition = NewHealZone(
            "LinkedConsecrate", radius: 3.5f, duration: 6f, healPerPulse: 3f, pulseInterval: 0.5f);

        Assert.That(
            MonoScript.FromScriptableObject(definition),
            Is.Not.Null,
            "SpawnHealZoneDefinition is not linked to a MonoScript — the usual cause is a "
                + "file-scoped namespace on a UnityEngine.Object type (Traps §5).");
    }

    // -------------------------------------------------- ModifySkillCooldownDefinition

    [Test]
    public void SkillCooldown_ToEffect_RoundTrip()
    {
        // 'skill.test.bulwark', Flat and -1.5, none of which is this type's C# initialiser
        // ('skill.new', PercentMult, -0.25): all three fields carry defaults, so asserting the
        // defaults would pass identically whether the YAML key binds or the field is holding its
        // initialiser (Traps §7). Flat in particular, because PercentMult is the authored default
        // and CH §4.1's kind — a round trip that asserted it would prove nothing about the field.
        ModifySkillCooldownDefinition definition = NewSkillCooldown(
            "ShorterBulwark", "skill.test.bulwark", ModifierKind.Flat, -1.5f);

        IEffect effect = definition.ToEffect();

        Assert.That(effect, Is.InstanceOf<ModifySkillCooldown>());

        var cooldown = (ModifySkillCooldown)effect;

        Assert.That(cooldown.SkillId, Is.EqualTo(new ContentId("skill.test.bulwark")));
        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(cooldown.Value, Is.EqualTo(-1.5f).Within(Tolerance));

        // ADR-0006: the SO holds no runtime state. A cached effect handed out twice would be one
        // object shared by every run that takes the node.
        Assert.That(definition.ToEffect(), Is.Not.SameAs(cooldown));
    }

    [Test]
    public void SkillCooldown_Invalid_NamesTheAsset()
    {
        // A malformed id: legal as a string, refused as content. ContentId's grammar is the one
        // account of a legal id and this type copies none of it — what it owes is the asset's name
        // at the front of the message (M0-11).
        ModifySkillCooldownDefinition bad = NewSkillCooldown(
            "BrokenCooldown", "Skill Cooldown", ModifierKind.PercentMult, -0.25f);

        var thrown = Assert.Throws<ArgumentException>(() => bad.ToEffect());

        Assert.That(
            thrown.Message,
            Does.StartWith("ModifySkillCooldownDefinition 'BrokenCooldown'"),
            "The message must lead with the asset, or the Console points at no file to open.");

        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentException>());

        // And another door, so the row is not one field's story told twice — this one out of
        // ModifySkillCooldown's own constructor rather than out of the id check above.
        ModifySkillCooldownDefinition infinite = NewSkillCooldown(
            "InfiniteCooldown",
            "skill.test.bulwark",
            ModifierKind.PercentMult,
            float.PositiveInfinity);

        Assert.That(
            Assert.Throws<ArgumentException>(() => infinite.ToEffect()).Message,
            Does.StartWith("ModifySkillCooldownDefinition 'InfiniteCooldown'"));
    }

    [Test]
    public void SkillCooldown_OnValidate_MalformedId_Warns()
    {
        ModifySkillCooldownDefinition definition = NewSkillCooldown(
            "WarnsOnBadSkillId", "skill.test.bulwark", ModifierKind.PercentMult, -0.25f);

        // LogAssert fails at teardown if this warning never arrives, which is what makes the row
        // load-bearing: a silent OnValidate would go unnoticed otherwise, since a stray warning
        // does not fail a test on its own (M0-11). A malformed id is exactly the field that needs
        // warning about — it fails at boot and looks like nothing in the Inspector.
        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("WarnsOnBadSkillId")));

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_skillId").stringValue = "Skill Cooldown";
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [Test]
    public void SkillCooldown_IsLinkedToAMonoScript()
    {
        // **The failure mode this catches reports nothing anywhere** (Traps §5). Unity 6.3's script
        // importer does not understand `namespace X;`, so a ScriptableObject declared file-scoped
        // compiles, passes every row above — they build the instance in code — and then loads as
        // null off any asset that references it, with `m_Script: {fileID: 0}` and no error.
        ModifySkillCooldownDefinition definition = NewSkillCooldown(
            "LinkedCooldown", "skill.test.bulwark", ModifierKind.PercentMult, -0.25f);

        Assert.That(
            MonoScript.FromScriptableObject(definition),
            Is.Not.Null,
            "ModifySkillCooldownDefinition is not linked to a MonoScript — the usual cause is a "
                + "file-scoped namespace on a UnityEngine.Object type (Traps §5).");
    }

    // ------------------------------------------------------ KnockbackOnSwingDefinition

    [Test]
    public void Knockback_ToEffect_RoundTrip()
    {
        // 2.25, not the 1.5 the field carries as its C# initialiser (Traps §7).
        KnockbackOnSwingDefinition definition = NewKnockback("Shove", distance: 2.25f);

        IEffect effect = definition.ToEffect();

        Assert.That(effect, Is.InstanceOf<KnockbackOnSwing>());

        Assert.That(((KnockbackOnSwing)effect).Distance, Is.EqualTo(2.25f).Within(Tolerance));

        Assert.That(definition.ToEffect(), Is.Not.SameAs(effect));
    }

    [Test]
    public void Knockback_Invalid_NamesTheAsset()
    {
        // Zero metres: legal as a float, refused as content, and the mistake worth catching — a
        // node the player spends a pick on and gets nothing for.
        KnockbackOnSwingDefinition none = NewKnockback("BrokenShove", distance: 0f);

        var thrown = Assert.Throws<ArgumentException>(() => none.ToEffect());

        Assert.That(
            thrown.Message,
            Does.StartWith("KnockbackOnSwingDefinition 'BrokenShove'"),
            "The message must lead with the asset, or the Console points at no file to open.");

        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());

        // And the other door: a pull, which nothing in the design has ever asked for.
        KnockbackOnSwingDefinition pull = NewKnockback("PullingShove", distance: -1f);

        Assert.That(
            Assert.Throws<ArgumentException>(() => pull.ToEffect()).Message,
            Does.StartWith("KnockbackOnSwingDefinition 'PullingShove'"));
    }

    [Test]
    public void Knockback_IsLinkedToAMonoScript()
    {
        KnockbackOnSwingDefinition definition = NewKnockback("LinkedShove", distance: 1.5f);

        Assert.That(
            MonoScript.FromScriptableObject(definition),
            Is.Not.Null,
            "KnockbackOnSwingDefinition is not linked to a MonoScript — the usual cause is a "
                + "file-scoped namespace on a UnityEngine.Object type (Traps §5).");
    }

    // ---------------------------------------------------------------------- SkillDefinition

    [Test]
    public void Skill_Passive_ToSpec()
    {
        ModifyStatDefinition hp = NewEffect("PassiveHp", PlayerStat.MaxHp, ModifierKind.Flat, 20f);
        ModifyStatDefinition speed = NewEffect(
            "PassiveSpeed", PlayerStat.MoveSpeed, ModifierKind.PercentAdd, 0.1f);

        SkillDefinition definition = NewSkill(
            "UnbrokenVow", "skill.oathbound.unbroken-vow", SkillKind.Passive, hp, speed);

        SkillSpec spec = definition.ToSpec();

        Assert.That(spec.Id, Is.EqualTo(new ContentId("skill.oathbound.unbroken-vow")));
        Assert.That(spec.NameKey.Key, Is.EqualTo("skill.oathbound.unbroken-vow.name"));
        Assert.That(spec.DescriptionKey.Key, Is.EqualTo("skill.oathbound.unbroken-vow.description"));
        Assert.That(spec.Kind, Is.EqualTo(SkillKind.Passive));

        // Two effects, in the order they were authored — a node granting "+20 HP and +10 % speed"
        // is two ModifyStats from one source, not one effect carrying a list.
        Assert.That(spec.Effects, Has.Count.EqualTo(2));
        Assert.That(((ModifyStat)spec.Effects[0]).Stat, Is.EqualTo(PlayerStat.MaxHp));
        Assert.That(((ModifyStat)spec.Effects[1]).Stat, Is.EqualTo(PlayerStat.MoveSpeed));

        // Rule 3: the two optional halves are built only for the kind that uses them, which is what
        // makes SkillSpec's both-directions rule true by construction.
        Assert.That(spec.Active, Is.Null);
        Assert.That(spec.ParentId.Value, Is.Null);
    }

    [Test]
    public void Skill_Active_ToSpec()
    {
        ModifyStatDefinition onCast = NewEffect(
            "ConsecrateHeal", PlayerStat.MaxHp, ModifierKind.Flat, 3f);

        SkillDefinition definition = NewSkill(
            "Consecrate", "skill.oathbound.consecrate", SkillKind.Active);

        // 5, not the 8 the spec's row names: `_cooldown` initialises to 8, so asserting 8 would
        // pass identically whether the YAML key binds or the field is holding its C# initialiser
        // (Traps §7). The number is arbitrary; being different from the default is not.
        SetFloat(definition, "_cooldown", 5f);
        SetClause(definition, TriggerField.HpFraction, TriggerComparison.Below, 0.6f);
        SetEffects(definition, "_onCast", onCast);

        SkillSpec spec = definition.ToSpec();

        Assert.That(spec.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(spec.Active, Is.Not.Null);
        Assert.That(spec.Active.Cooldown, Is.EqualTo(5f).Within(Tolerance));

        Assert.That(spec.Active.Trigger.Clauses, Has.Count.EqualTo(1));
        Assert.That(spec.Active.Trigger.Clauses[0].Field, Is.EqualTo(TriggerField.HpFraction));
        Assert.That(spec.Active.Trigger.Clauses[0].Comparison, Is.EqualTo(TriggerComparison.Below));
        Assert.That(spec.Active.Trigger.Clauses[0].Threshold, Is.EqualTo(0.6f).Within(Tolerance));

        Assert.That(spec.Active.OnCast, Has.Count.EqualTo(1));

        // An Active is the one kind that may take with no effects, because its power is on cast.
        Assert.That(spec.Effects, Is.Empty);
    }

    [Test]
    public void Skill_PassiveIgnoresActiveFields()
    {
        ModifyStatDefinition take = NewEffect("TakeHp", PlayerStat.MaxHp, ModifierKind.Flat, 15f);
        ModifyStatDefinition stray = NewEffect(
            "StrayCast", PlayerStat.MaxHp, ModifierKind.Flat, 3f);

        SkillDefinition definition = NewSkill("StrayFields", "skill.test.stray", SkillKind.Passive, take);

        // Rule 3: a Passive carrying a cooldown and a cast effect is not an error. A designer who
        // authored an Active, filled the block in and changed their mind should not have to clear
        // three fields to make the asset legal again — and SkillSpec would refuse the block outright
        // if it were passed, so the "ignored" has to happen on this side of the seam.
        SetFloat(definition, "_cooldown", 12f);
        SetClause(definition, TriggerField.Veilrot, TriggerComparison.AtLeast, 50f);
        SetEffects(definition, "_onCast", stray);

        SkillSpec spec = null;
        Assert.That(() => spec = definition.ToSpec(), Throws.Nothing,
            "Fields the kind does not use are ignored, never refused (rule 3).");

        Assert.That(spec.Kind, Is.EqualTo(SkillKind.Passive));
        Assert.That(spec.Active, Is.Null);
        Assert.That(spec.Effects, Has.Count.EqualTo(1));
    }

    [Test]
    public void Skill_Upgrade_ParentIdFollowsTheAsset()
    {
        ModifyStatDefinition effect = NewEffect(
            "UpgradeEffect", PlayerStat.MaxHp, ModifierKind.Flat, 5f);

        SkillDefinition parent = NewSkill(
            "Consecrate", "skill.oathbound.consecrate", SkillKind.Passive, effect);

        SkillDefinition child = NewSkill(
            "DeeperConsecration", "skill.oathbound.deeper-consecration", SkillKind.Upgrade, effect);

        SetObject(child, "_parent", parent);

        Assert.That(
            child.ToSpec().ParentId,
            Is.EqualTo(new ContentId("skill.oathbound.consecrate")));

        // Rule 4: the id is read off the referenced asset at conversion, so renaming the parent
        // carries every child with it. A string field here would be a second copy of that id, and
        // this is the edit that would have made the two disagree.
        SetString(parent, "_id", "skill.oathbound.hallow");

        Assert.That(
            child.ToSpec().ParentId,
            Is.EqualTo(new ContentId("skill.oathbound.hallow")),
            "A renamed parent must follow, or a node points at content that no longer exists.");
    }

    [Test]
    public void Skill_EmptyEffectSlot_NamesTheAssetAndIndex()
    {
        ModifyStatDefinition effect = NewEffect("Slot0", PlayerStat.MaxHp, ModifierKind.Flat, 5f);

        SkillDefinition definition = NewSkill(
            "HollowNode", "skill.test.hollow", SkillKind.Passive, effect, effect);

        // An empty slot in the middle, which is what a designer leaves behind after deleting an
        // effect asset. Core could only ever say "effects[1] is null", which points at no file.
        SetEffects(definition, "_effects", effect, null);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.StartWith("SkillDefinition 'HollowNode'"));
        Assert.That(thrown.Message, Does.Contain("_effects[1]"),
            "The index is half the message: with twelve effects on a keystone, 'an effect is " +
            "missing' is not something a person can act on.");
    }

    [Test]
    public void Skill_Invalid_NamesTheAsset()
    {
        // An Active with nothing to cast. ActiveSpec refuses it — an active that casts nothing goes
        // on cooldown and produces nothing — and this row is that refusal arriving with a file name
        // in front of it (rule 2).
        SkillDefinition definition = NewSkill(
            "SilentActive", "skill.test.silent", SkillKind.Active);

        SetClause(definition, TriggerField.HpFraction, TriggerComparison.Below, 0.5f);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.StartWith("SkillDefinition 'SilentActive'"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Skill_NonFiniteCooldown_NamesTheAsset()
    {
        ModifyStatDefinition onCast = NewEffect("CastIt", PlayerStat.MaxHp, ModifierKind.Flat, 1f);

        SkillDefinition definition = NewSkill(
            "InfiniteCooldown", "skill.test.infinite", SkillKind.Active);

        SetClause(definition, TriggerField.HpFraction, TriggerComparison.Below, 0.5f);
        SetEffects(definition, "_onCast", onCast);

        // [Min(0.01f)] clamps the Inspector GUI and nothing else, so a SerializedProperty write is
        // how a non-finite cooldown actually reaches the spec — from a merge, a hand-edited YAML,
        // or this. An infinite cooldown is a node the player takes and never gets to use.
        SetFloat(definition, "_cooldown", float.PositiveInfinity);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.StartWith("SkillDefinition 'InfiniteCooldown'"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Skill_NonFiniteThreshold_NamesTheAsset()
    {
        ModifyStatDefinition onCast = NewEffect("CastIt", PlayerStat.MaxHp, ModifierKind.Flat, 1f);

        SkillDefinition definition = NewSkill("NaNTrigger", "skill.test.nan", SkillKind.Active);

        SetEffects(definition, "_onCast", onCast);

        // A NaN threshold makes every comparison against it false, so the skill simply never fires
        // and nothing on screen says why — the silence M2-06 rule 11 refuses. TriggerClause refuses
        // it where the condition is *authored*, and this row is that refusal naming the node.
        SetClause(definition, TriggerField.HpFraction, TriggerComparison.Below, float.NaN);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.StartWith("SkillDefinition 'NaNTrigger'"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Skill_ToSpec_ReturnsNewInstanceEachCall()
    {
        ModifyStatDefinition effect = NewEffect("Fresh", PlayerStat.MaxHp, ModifierKind.Flat, 5f);

        SkillDefinition definition = NewSkill(
            "FreshNode", "skill.test.fresh", SkillKind.Passive, effect);

        SkillSpec first = definition.ToSpec();
        SkillSpec second = definition.ToSpec();

        // ADR-0006: the SO holds no runtime state. It matters more here than on any earlier
        // definition type, because the SkillSpec is the *source* M3-03 hands EffectRegistry.Apply
        // — two instances would be two sources, and removing one would leave the other's modifiers
        // on the player for the rest of the run.
        Assert.That(second, Is.Not.SameAs(first));
        Assert.That(second.Id, Is.EqualTo(first.Id));
    }

    [Test]
    public void Skill_OnValidate_InvalidId_Warns()
    {
        SkillDefinition definition = NewSkill(
            "WarnsOnSkillId", "skill.test.warns", SkillKind.Passive);

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("WarnsOnSkillId")));

        SetString(definition, "_id", "Skill.Oathbound.Consecrate");
    }

    [Test]
    public void Skill_OnValidate_ActiveWithNoCastEffect_Warns()
    {
        SkillDefinition definition = NewSkill(
            "WarnsOnEmptyCast", "skill.test.emptycast", SkillKind.Passive);

        // Rule 8's first kind mismatch, and it is worth a warning rather than being left to boot
        // because a designer who flips the dropdown to Active has an asset that is legal in the
        // Inspector and refused one scene later.
        LogAssert.Expect(LogType.Warning, new Regex("On Cast"));

        SetEnum(definition, "_kind", (int)SkillKind.Active);
    }

    [Test]
    public void Skill_OnValidate_UpgradeWithNoParent_Warns()
    {
        SkillDefinition definition = NewSkill(
            "WarnsOnNoParent", "skill.test.noparent", SkillKind.Passive);

        LogAssert.Expect(LogType.Warning, new Regex("no Parent"));

        SetEnum(definition, "_kind", (int)SkillKind.Upgrade);
    }

    // ------------------------------------------------------------------ SkillTreeDefinition

    [Test]
    public void Tree_ToSpec_Shape()
    {
        SkillDefinition[] nodes = NewNodes(27);
        SkillTreeDefinition definition = NewTree("OathboundTree", "tree.oathbound", nodes);

        SkillTreeSpec spec = definition.ToSpec();

        Assert.That(spec.Id, Is.EqualTo(new ContentId("tree.oathbound")));

        // Rule 5: the CharacterDefinition reference becomes the tree's CharacterId, which is what
        // ContentCatalog.TryGetTreeFor keys on — a run resolves its tree from the class it is
        // playing, never from a tree id anyone typed.
        Assert.That(spec.CharacterId, Is.EqualTo(new ContentId("character.oathbound")));

        Assert.That(spec.Branches, Has.Count.EqualTo(SkillTreeSpec.BranchCount));
        Assert.That(spec.NodeCount, Is.EqualTo(27),
            "2 / 2 / 2 / 2 / 1 is nine a branch and twenty-seven a class — the shape M3-00a ruled " +
            "and M3-02a pinned.");

        for (int b = 0; b < SkillTreeSpec.BranchCount; b++)
        {
            SkillBranchSpec branch = spec.Branches[b];

            Assert.That(branch.TierCount, Is.EqualTo(5), $"branch {b} has five tiers.");
            Assert.That(branch.NodeCount, Is.EqualTo(9), $"branch {b} holds nine nodes.");
            Assert.That(branch.Tier(1), Has.Count.EqualTo(2));
            Assert.That(branch.Tier(5), Has.Count.EqualTo(1), "The last tier is the keystone's.");
        }

        // Every node resolves to a position, which is what M3-03's gating and M3-04's draw both ask
        // per candidate — and what would silently be wrong if the ids were read off the wrong slot.
        Assert.That(spec.TryLocate(new ContentId(nodes[0].Id), out int branchIndex, out int tier),
            Is.True);
        Assert.That(branchIndex, Is.Zero);
        Assert.That(tier, Is.EqualTo(1), "Tiers are 1-based in the spec, CH §5's own numbering.");
    }

    [Test]
    public void Tree_EmptyNodeSlot_NamesTheAsset()
    {
        SkillDefinition[] nodes = NewNodes(27);
        SkillTreeDefinition definition = NewTree("HollowTree", "tree.hollow", nodes);

        ClearNode(definition, branch: 1, tier: 2, slot: 0);

        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.StartWith("SkillTreeDefinition 'HollowTree'"));
        Assert.That(thrown.Message, Does.Contain("branches[1].tiers[2].nodes[0]"),
            "A missing reference has no id, so core could only report a default(ContentId) and " +
            "point at nothing a designer can open.");
    }

    [Test]
    public void Tree_Invalid_NamesTheAsset()
    {
        SkillDefinition[] nodes = NewNodes(27);
        SkillTreeDefinition definition = NewTree("TwoBranchTree", "tree.two-branch", nodes);

        SetArraySize(definition, "_branches", 2);

        // CH §5's identical skeleton is what lets the tree UI be built once and the content vary,
        // so two branches is refused by the spec rather than drawn as a gap (rule 2).
        var thrown = Assert.Throws<ArgumentException>(() => definition.ToSpec());

        Assert.That(thrown.Message, Does.StartWith("SkillTreeDefinition 'TwoBranchTree'"));

        // Asserted rather than assumed: the id above is a well-formed one on purpose, so this row
        // cannot pass on a ContentId failure while the branch-count rule quietly does nothing.
        Assert.That(thrown.Message, Does.Contain("2 branches"));
        Assert.That(thrown.InnerException, Is.InstanceOf<ArgumentException>());
    }

    [Test]
    public void Tree_OnValidate_InvalidId_Warns()
    {
        SkillTreeDefinition definition = NewTree("WarnsOnTreeId", "tree.warns", NewNodes(27));

        LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("WarnsOnTreeId")));

        SetString(definition, "_id", "Tree Oathbound");
    }

    // ------------------------------------------------------------------------- the boot lists

    [Test]
    public void Install_NullSkills_Throws()
    {
        var builder = new ContainerBuilder();

        // Required rather than optional, the Boot_NullXList_Throws shape one kind along (rule 6).
        // Omitted, it would build a container that resolves everything, boots to the menu, plays a
        // run and puts an empty level-up screen in front of the player.
        Assert.Throws<ArgumentNullException>(() =>
            BootInstaller.Install(
                builder,
                new[] { LoadOathbound() },
                Array.Empty<EnemyDefinition>(),
                Array.Empty<ModeDefinition>(),
                null,
                Array.Empty<SkillTreeDefinition>()));
    }

    [Test]
    public void Install_NullTrees_Throws()
    {
        var builder = new ContainerBuilder();

        Assert.Throws<ArgumentNullException>(() =>
            BootInstaller.Install(
                builder,
                new[] { LoadOathbound() },
                Array.Empty<EnemyDefinition>(),
                Array.Empty<ModeDefinition>(),
                Array.Empty<SkillDefinition>(),
                null));
    }

    [Test]
    public void Install_EmptySkillSlot_Throws()
    {
        var builder = new ContainerBuilder();

        var thrown = Assert.Throws<ArgumentException>(() =>
            BootInstaller.Install(
                builder,
                new[] { LoadOathbound() },
                Array.Empty<EnemyDefinition>(),
                Array.Empty<ModeDefinition>(),
                new SkillDefinition[] { null },
                Array.Empty<SkillTreeDefinition>()));

        Assert.That(thrown.Message, Does.Contain("skills[0]"),
            "An empty slot in a boot list is named by its index, or 'one of your skills is " +
            "missing' is not something a person can act on.");
    }

    [Test]
    public void Boot_EmptyListsAreLegal()
    {
        var builder = new ContainerBuilder();

        // **Rule 7, as a test.** Nothing ships in Data/ until M3-12, so the two arrays on
        // BootScope.prefab are empty — and this row is what says that is a boot before M3-12
        // rather than a broken one. A catalog with no skills and no trees builds, resolves, and
        // starts a run; a class with no tree is legal content (M3-02a rule 11) and M3-03 rule 10
        // says what a run does with one.
        Assert.That(
            () => BootInstaller.Install(
                builder,
                new[] { LoadOathbound() },
                Array.Empty<EnemyDefinition>(),
                Array.Empty<ModeDefinition>(),
                Array.Empty<SkillDefinition>(),
                Array.Empty<SkillTreeDefinition>()),
            Throws.Nothing);

        var catalog = Track(builder.Build()).Resolve<ContentCatalog>();

        Assert.That(catalog.Skills, Is.Empty);
        Assert.That(catalog.Trees, Is.Empty);
        Assert.That(
            catalog.TryGetTreeFor(new ContentId("character.oathbound"), out SkillTreeSpec _),
            Is.False,
            "A class with no tree is a legal catalog, which is every catalog until M3-12.");
    }

    [Test]
    public void Boot_ScopeCarriesTheTwoLists()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath);
        Assert.That(prefab, Is.Not.Null, $"No prefab at {BootScopePath}.");

        // Traps §5: a MonoBehaviour declared with a file-scoped namespace compiles and is never
        // linked to a MonoScript, so the component here would come back null with nothing
        // reporting an error. GetComponent answering is half of what this row proves.
        var scope = prefab.GetComponent<BootScope>();
        Assert.That(scope, Is.Not.Null,
            "BootScope did not load off its own prefab — the usual cause is a file-scoped " +
            "namespace on a UnityEngine.Object type (Traps §5).");

        var serialized = new SerializedObject(scope);

        SerializedProperty skills = serialized.FindProperty("_skills");
        SerializedProperty trees = serialized.FindProperty("_trees");

        // Present: a field the prefab has no key for reads back as null here, which is what a
        // serialized array added in code and never written to the asset looks like.
        Assert.That(skills, Is.Not.Null,
            "BootScope.prefab carries no _skills field. The asset needs re-serialising after the " +
            "field was added, or the boot list is one the Inspector cannot fill.");
        Assert.That(trees, Is.Not.Null, "BootScope.prefab carries no _trees field.");

        // And empty, which is rule 7 again: the shipped prefab offers nothing because there is
        // nothing to offer until M3-12.
        Assert.That(skills.arraySize, Is.Zero, "Nothing ships in Data/Skills until M3-12.");
        Assert.That(trees.arraySize, Is.Zero, "Nothing ships in Data/Trees until M3-12.");
    }

    // ------------------------------------------------------------------------------- helpers

    private static CharacterDefinition LoadOathbound()
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(OathboundPath);
        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {OathboundPath}.");
        return definition;
    }

    private T Track<T>(T container) where T : IObjectResolver
    {
        _containers.Add(container);
        return container;
    }

    private T New<T>(string assetName) where T : ScriptableObject
    {
        var definition = ScriptableObject.CreateInstance<T>();

        // CreateInstance leaves `name` empty, and an empty name makes Does.StartWith pass against
        // a message that names no asset at all (M0-11). Naming it is what gives the assertion
        // something to find.
        definition.name = assetName;
        _created.Add(definition);
        return definition;
    }

    private ModifyStatDefinition NewEffect(
        string assetName,
        PlayerStat stat,
        ModifierKind kind,
        float value)
    {
        var definition = New<ModifyStatDefinition>(assetName);

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_stat").intValue = (int)stat;
        serialized.FindProperty("_kind").intValue = (int)kind;
        serialized.FindProperty("_value").floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return definition;
    }

    private SkillDefinition NewSkill(
        string assetName,
        string id,
        SkillKind kind,
        params EffectDefinition[] effects)
    {
        var definition = New<SkillDefinition>(assetName);

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_id").stringValue = id;
        serialized.FindProperty("_nameKey").stringValue = $"{id}.name";
        serialized.FindProperty("_descriptionKey").stringValue = $"{id}.description";
        serialized.FindProperty("_kind").intValue = (int)kind;

        SerializedProperty slots = serialized.FindProperty("_effects");
        slots.arraySize = effects.Length;

        for (int i = 0; i < effects.Length; i++)
        {
            slots.GetArrayElementAtIndex(i).objectReferenceValue = effects[i];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return definition;
    }

    /// <summary>
    /// <paramref name="count"/> throwaway Passive nodes with distinct ids, for the tree rows —
    /// a tree lists an id once, so twenty-seven positions need twenty-seven nodes.
    /// </summary>
    private SkillDefinition[] NewNodes(int count)
    {
        ModifyStatDefinition effect = NewEffect(
            "NodeEffect", PlayerStat.MaxHp, ModifierKind.Flat, 1f);

        var nodes = new SkillDefinition[count];

        for (int i = 0; i < count; i++)
        {
            nodes[i] = NewSkill($"Node{i}", $"skill.test.node{i}", SkillKind.Passive, effect);
        }

        return nodes;
    }

    /// <summary>
    /// Three branches of 2 / 2 / 2 / 2 / 1, filled from <paramref name="nodes"/> in order — the
    /// shape M3-00a ruled and the one M3-12 authors against.
    /// </summary>
    private SkillTreeDefinition NewTree(string assetName, string id, SkillDefinition[] nodes)
    {
        int[] tierSizes = { 2, 2, 2, 2, 1 };

        var definition = New<SkillTreeDefinition>(assetName);

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_id").stringValue = id;
        serialized.FindProperty("_character").objectReferenceValue = LoadOathbound();

        SerializedProperty branches = serialized.FindProperty("_branches");
        branches.arraySize = SkillTreeSpec.BranchCount;

        int next = 0;

        for (int b = 0; b < SkillTreeSpec.BranchCount; b++)
        {
            SerializedProperty branch = branches.GetArrayElementAtIndex(b);
            branch.FindPropertyRelative("_nameKey").stringValue = $"{id}.branch{b}";

            SerializedProperty tiers = branch.FindPropertyRelative("_tiers");
            tiers.arraySize = tierSizes.Length;

            for (int t = 0; t < tierSizes.Length; t++)
            {
                SerializedProperty slots = tiers.GetArrayElementAtIndex(t)
                    .FindPropertyRelative("_nodes");
                slots.arraySize = tierSizes[t];

                for (int i = 0; i < tierSizes[t]; i++)
                {
                    slots.GetArrayElementAtIndex(i).objectReferenceValue = nodes[next++];
                }
            }
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return definition;
    }

    private static void ClearNode(SkillTreeDefinition definition, int branch, int tier, int slot)
    {
        var serialized = new SerializedObject(definition);

        serialized.FindProperty("_branches")
            .GetArrayElementAtIndex(branch)
            .FindPropertyRelative("_tiers")
            .GetArrayElementAtIndex(tier)
            .FindPropertyRelative("_nodes")
            .GetArrayElementAtIndex(slot)
            .objectReferenceValue = null;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Replaces a node's trigger with one clause. The array is rebuilt rather than appended to,
    /// so a row that calls this twice says what it means.
    /// </summary>
    private static void SetClause(
        SkillDefinition definition,
        TriggerField field,
        TriggerComparison comparison,
        float threshold)
    {
        var serialized = new SerializedObject(definition);

        SerializedProperty clauses = serialized.FindProperty("_trigger");
        clauses.arraySize = 1;

        SerializedProperty clause = clauses.GetArrayElementAtIndex(0);
        clause.FindPropertyRelative("_field").intValue = (int)field;
        clause.FindPropertyRelative("_comparison").intValue = (int)comparison;
        clause.FindPropertyRelative("_threshold").floatValue = threshold;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetEffects(
        SkillDefinition definition,
        string field,
        params EffectDefinition[] effects)
    {
        var serialized = new SerializedObject(definition);

        SerializedProperty slots = serialized.FindProperty(field);
        slots.arraySize = effects.Length;

        for (int i = 0; i < effects.Length; i++)
        {
            slots.GetArrayElementAtIndex(i).objectReferenceValue = effects[i];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArraySize(ScriptableObject definition, string field, int size)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).arraySize = size;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetString(ScriptableObject definition, string field, string value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private GrantShieldDefinition NewGrantShield(string assetName, float amount, float duration)
    {
        var definition = New<GrantShieldDefinition>(assetName);

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_amount").floatValue = amount;
        serialized.FindProperty("_duration").floatValue = duration;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return definition;
    }

    private ModifySkillCooldownDefinition NewSkillCooldown(
        string assetName,
        string skillId,
        ModifierKind kind,
        float value)
    {
        var definition = New<ModifySkillCooldownDefinition>(assetName);

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_skillId").stringValue = skillId;
        serialized.FindProperty("_kind").intValue = (int)kind;
        serialized.FindProperty("_value").floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return definition;
    }

    private KnockbackOnSwingDefinition NewKnockback(string assetName, float distance)
    {
        var definition = New<KnockbackOnSwingDefinition>(assetName);

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_distance").floatValue = distance;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return definition;
    }

    private SpawnHealZoneDefinition NewHealZone(
        string assetName,
        float radius,
        float duration,
        float healPerPulse,
        float pulseInterval)
    {
        var definition = New<SpawnHealZoneDefinition>(assetName);

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_radius").floatValue = radius;
        serialized.FindProperty("_duration").floatValue = duration;
        serialized.FindProperty("_healPerPulse").floatValue = healPerPulse;
        serialized.FindProperty("_pulseInterval").floatValue = pulseInterval;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return definition;
    }

    private static void SetFloat(ScriptableObject definition, string field, float value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetObject(
        ScriptableObject definition,
        string field,
        UnityEngine.Object value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Writes an enum field by its underlying number rather than through
    /// <c>enumValueIndex</c>, which cannot express a value that is no longer a member — and a
    /// value that is no longer a member is exactly what two of the rows above are about.
    /// </summary>
    private static void SetEnum(ScriptableObject definition, string field, int value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
