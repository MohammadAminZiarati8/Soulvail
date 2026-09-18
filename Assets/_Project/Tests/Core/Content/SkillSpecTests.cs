using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// <c>SkillSpec</c>, <c>ActiveSpec</c> and the trigger types, in one fixture: the trigger is the
/// active's and the active is the skill's, so the three read as one contract rather than three.
/// </summary>
/// <remarks>
/// <para>
/// The effects are a private fixture marker rather than <c>ModifyStat</c>, for
/// <c>EffectRegistryTests</c>' reason: a <c>SkillSpec</c> never looks inside an
/// <see cref="IEffect"/>, so a row written against the one shipped primitive would be proving
/// something about <c>ModifyStat</c> instead.
/// </para>
/// <para>
/// The tree rows are next door in <c>SkillTreeSpecTests</c>, and the catalog rows are in
/// <c>ContentTests</c> beside the character and mode ones they mirror.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SkillSpecTests
{
    /// <summary>Kept out of the allocation row's measured body — see <c>EnemyRegistryTests</c>.</summary>
    private bool _sink;

    // ---- Rule 1 and 3: identity, text, and a kind that agrees with its block ---------------------

    [Test]
    public void Skill_RecordsFields()
    {
        var id = new ContentId("skill.oathbound.consecrate");
        var nameKey = new LocKey("skill.oathbound.consecrate.name");
        var descriptionKey = new LocKey("skill.oathbound.consecrate.desc");
        IEffect onTake = new Marker();
        ActiveSpec active = Consecrate();

        var spec = new SkillSpec(
            id,
            nameKey,
            descriptionKey,
            SkillKind.Active,
            new[] { onTake },
            active);

        Assert.That(spec.Id, Is.EqualTo(id));
        Assert.That(spec.NameKey, Is.EqualTo(nameKey));
        Assert.That(spec.DescriptionKey, Is.EqualTo(descriptionKey));
        Assert.That(spec.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(spec.Effects.Count, Is.EqualTo(1));
        Assert.That(spec.Effects[0], Is.SameAs(onTake));
        Assert.That(spec.Active, Is.SameAs(active));
        Assert.That(spec.ParentId, Is.EqualTo(default(ContentId)));

        // The Active block reads back whole, since nothing else in the suite opens one.
        Assert.That(spec.Active.Cooldown, Is.EqualTo(12f));
        Assert.That(spec.Active.Trigger.Clauses.Count, Is.EqualTo(1));
        Assert.That(spec.Active.OnCast.Count, Is.EqualTo(1));

        // ParentId is the one field an Active cannot carry, so the Upgrade case is asserted here
        // rather than in a row of its own: the two kinds are mutually exclusive by rule 1.
        var parentId = new ContentId("skill.oathbound.consecrate");
        var upgrade = new SkillSpec(
            new ContentId("skill.oathbound.consecrate_wider"),
            new LocKey("skill.oathbound.consecrate_wider.name"),
            new LocKey("skill.oathbound.consecrate_wider.desc"),
            SkillKind.Upgrade,
            OneEffect(),
            parentId: parentId);

        Assert.That(upgrade.ParentId, Is.EqualTo(parentId));
        Assert.That(upgrade.Active, Is.Null);
    }

    [Test]
    public void Skill_DefaultId_Throws()
    {
        // A spec with no id would sit in the catalog under a key Skill() then reports as missing —
        // CharacterSpec's argument, and here a tree's node id could never resolve to it either.
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(default, Name(), Description(), SkillKind.Passive, OneEffect()));
    }

    [Test]
    public void Skill_DefaultNameKey_Throws()
    {
        // default(LocKey) carries a null Key past the struct's own constructor (AR §18.3), so the
        // check has to be at this end. Rule 3: the key exists from the first node (ADR-0012) even
        // though the table that resolves it does not.
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(Id(), default, Description(), SkillKind.Passive, OneEffect()));
    }

    [Test]
    public void Skill_DefaultDescriptionKey_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(Id(), Name(), default, SkillKind.Passive, OneEffect()));
    }

    [Test]
    public void Skill_ActiveWithoutBlock_Throws()
    {
        // Both directions, unlike EnemySpec's Spitter: an Active with no block is a skill that
        // cannot fire, and there is nothing to stage here because M3-06's runner lands before the
        // first active does.
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(Id(), Name(), Description(), SkillKind.Active, OneEffect()));
    }

    [Test]
    public void Skill_BlockWithoutActive_Throws()
    {
        // The direction EnemySpec deliberately leaves open. Here it is a cooldown nothing will run.
        foreach (SkillKind kind in new[] { SkillKind.Passive, SkillKind.Upgrade, SkillKind.Keystone })
        {
            ContentId parent = kind == SkillKind.Upgrade ? Parent() : default;

            Assert.Throws<ArgumentException>(
                () => new SkillSpec(Id(), Name(), Description(), kind, OneEffect(), Consecrate(), parent),
                $"A {kind} carrying an ActiveSpec was accepted.");
        }
    }

    [Test]
    public void Skill_UpgradeWithoutParent_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(Id(), Name(), Description(), SkillKind.Upgrade, OneEffect()));
    }

    [Test]
    public void Skill_ParentOnNonUpgrade_Throws()
    {
        // Refused on every other kind, because on any of them the field would be read by nothing —
        // rule 1's second direction, applied to the id as well as to the block.
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(
                Id(), Name(), Description(), SkillKind.Passive, OneEffect(), parentId: Parent()));

        Assert.Throws<ArgumentException>(
            () => new SkillSpec(
                Id(), Name(), Description(), SkillKind.Keystone, OneEffect(), parentId: Parent()));

        Assert.Throws<ArgumentException>(
            () => new SkillSpec(
                Id(), Name(), Description(), SkillKind.Active, Array.Empty<IEffect>(), Consecrate(), Parent()));
    }

    // ---- Rule 2: a node that does nothing is refused ---------------------------------------------

    [Test]
    public void Skill_PassiveWithNoEffects_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(
                Id(), Name(), Description(), SkillKind.Passive, Array.Empty<IEffect>()));
    }

    [Test]
    public void Skill_UpgradeWithNoEffects_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(
                Id(), Name(), Description(), SkillKind.Upgrade, Array.Empty<IEffect>(), parentId: Parent()));
    }

    [Test]
    public void Skill_KeystoneWithNoEffects_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(
                Id(), Name(), Description(), SkillKind.Keystone, Array.Empty<IEffect>()));
    }

    [Test]
    public void Skill_ActiveMayTakeWithNoEffects()
    {
        // The one exception, and it is not a hole: the power is on cast, and ActiveSpec has already
        // refused an empty OnCast — so the node still does something.
        var spec = new SkillSpec(
            Id(), Name(), Description(), SkillKind.Active, Array.Empty<IEffect>(), Consecrate());

        Assert.That(spec.Effects, Is.Empty);
        Assert.That(spec.Active.OnCast.Count, Is.EqualTo(1));
    }

    [Test]
    public void Skill_EffectsAreCopied()
    {
        IEffect effect = new Marker();
        var list = new List<IEffect> { effect };

        var spec = new SkillSpec(Id(), Name(), Description(), SkillKind.Passive, list);
        list.Clear();

        Assert.That(spec.Effects.Count, Is.EqualTo(1), "The caller's list was retained.");
        Assert.That(spec.Effects[0], Is.SameAs(effect));

        // And the copy is wrapped rather than handed out: an array exposed as IReadOnlyList<T>
        // casts straight back and the copy would protect nothing. ContentCatalog's argument.
        Assert.That(spec.Effects, Is.Not.AssignableTo<IEffect[]>());

        // The same bargain on the cast list, which goes through the same helper.
        var castList = new List<IEffect> { effect };
        var active = new ActiveSpec(12f, Trigger(), castList);
        castList.Clear();

        Assert.That(active.OnCast.Count, Is.EqualTo(1));
        Assert.That(active.OnCast, Is.Not.AssignableTo<IEffect[]>());
    }

    [Test]
    public void Skill_NullEffect_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(
                Id(), Name(), Description(), SkillKind.Passive, new IEffect[] { null }));

        // A null entry beside a real one is the version that would otherwise reach the registry
        // one node at a time, so it is named rather than assumed.
        Assert.Throws<ArgumentException>(
            () => new SkillSpec(
                Id(), Name(), Description(), SkillKind.Passive, new IEffect[] { new Marker(), null }));

        // The implied null row: the list itself, which is an ArgumentNullException and not the
        // same exception as a null entry. Assert.Throws is an exact type match (Traps §7).
        Assert.Throws<ArgumentNullException>(
            () => new SkillSpec(Id(), Name(), Description(), SkillKind.Passive, null));
    }

    // ---- Rule 4: the cooldown is the authored base, and it is a real number ----------------------

    [Test]
    public void Active_CooldownGuards()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActiveSpec(0f, Trigger(), OneEffect()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActiveSpec(-1f, Trigger(), OneEffect()));

        // NaN is the case the natural `value <= 0f` spelling lets through, and infinity is the case
        // a `> 0` test lets through — a node the player takes and never gets to use (AR §18.3).
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ActiveSpec(float.NaN, Trigger(), OneEffect()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ActiveSpec(float.PositiveInfinity, Trigger(), OneEffect()));

        // M3-12's Consecrate must pass, or every guard above would be satisfied by a constructor
        // that rejected everything.
        Assert.DoesNotThrow(() => new ActiveSpec(12f, Trigger(), OneEffect()));
    }

    [Test]
    public void Active_EmptyOnCast_Throws()
    {
        // An active that casts nothing goes on cooldown and does nothing — M2-06 rule 11's silence.
        Assert.Throws<ArgumentException>(
            () => new ActiveSpec(12f, Trigger(), Array.Empty<IEffect>()));

        Assert.Throws<ArgumentNullException>(() => new ActiveSpec(12f, Trigger(), null));

        Assert.Throws<ArgumentException>(
            () => new ActiveSpec(12f, Trigger(), new IEffect[] { null }));
    }

    [Test]
    public void Active_NullTrigger_Throws()
    {
        // Every active ships with a condition (CH §4.2). A null would be an active whose toggle
        // still says Auto and which auto-casts on nothing.
        Assert.Throws<ArgumentNullException>(() => new ActiveSpec(12f, null, OneEffect()));
    }

    // ---- Rule 5: one or two clauses, all of which must hold --------------------------------------

    [Test]
    public void Trigger_Below()
    {
        var spec = Trigger(new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.6f));
        var blackboard = new CombatBlackboard();

        blackboard.HpFraction = 0.59f;
        Assert.That(spec.IsMet(blackboard), Is.True);

        // Below is strict, so the boundary itself does not hold — which is what makes AtLeast its
        // exact complement at the threshold.
        blackboard.HpFraction = 0.6f;
        Assert.That(spec.IsMet(blackboard), Is.False);
    }

    [Test]
    public void Trigger_AtLeast()
    {
        var spec = Trigger(
            new TriggerClause(TriggerField.EnemiesWithin6m, TriggerComparison.AtLeast, 3f));
        var blackboard = new CombatBlackboard();

        blackboard.EnemiesWithin6m = 3;
        Assert.That(spec.IsMet(blackboard), Is.True);

        blackboard.EnemiesWithin6m = 2;
        Assert.That(spec.IsMet(blackboard), Is.False);
    }

    [Test]
    public void Trigger_TwoClausesAreAnded()
    {
        // CH §4.2's Rot Nova, which is the reason MaxClauses is 2 rather than 1.
        var spec = new TriggerSpec(new[]
        {
            new TriggerClause(TriggerField.Veilrot, TriggerComparison.AtLeast, 50f),
            new TriggerClause(TriggerField.EnemiesWithin8m, TriggerComparison.AtLeast, 4f),
        });

        var blackboard = new CombatBlackboard { Veilrot = 50f, EnemiesWithin8m = 4 };
        Assert.That(spec.IsMet(blackboard), Is.True);

        blackboard.EnemiesWithin8m = 3;
        Assert.That(spec.IsMet(blackboard), Is.False, "The second clause failed and the spec held.");

        blackboard.Veilrot = 49f;
        blackboard.EnemiesWithin8m = 4;
        Assert.That(spec.IsMet(blackboard), Is.False, "The first clause failed and the spec held.");
    }

    // ---- Rule 6: every field reads, and a broken one casts nothing -------------------------------

    [Test]
    public void Trigger_EveryFieldReads()
    {
        // Written against reflection rather than a hand-kept table, so the row cannot drift: every
        // TriggerField member names a CombatBlackboard field *of exactly that name*, and only that
        // field is set. A member wired to the wrong field reads 0 and fails here; a member wired to
        // nothing throws out of IsMet's loud default; a member naming no field fails the first
        // assertion. That is the whole of "a member added without a case fails here".
        foreach (TriggerField field in Enum.GetValues(typeof(TriggerField)))
        {
            FieldInfo info = typeof(CombatBlackboard).GetField(
                field.ToString(),
                BindingFlags.Public | BindingFlags.Instance);

            Assert.That(
                info,
                Is.Not.Null,
                $"CombatBlackboard has no public field named '{field}'. TriggerField names the "
                    + "blackboard's own fields, so the two lists have to agree.");

            var blackboard = new CombatBlackboard();

            if (info.FieldType == typeof(float))
            {
                info.SetValue(blackboard, 5f);
            }
            else if (info.FieldType == typeof(int))
            {
                info.SetValue(blackboard, 5);
            }
            else
            {
                Assert.Fail(
                    $"CombatBlackboard.{field} is a {info.FieldType.Name}; TriggerClause widens "
                        + "int and reads float, and knows no third shape.");
            }

            var reads = Trigger(new TriggerClause(field, TriggerComparison.AtLeast, 5f));
            var overshoots = Trigger(new TriggerClause(field, TriggerComparison.AtLeast, 6f));

            Assert.That(
                reads.IsMet(blackboard),
                Is.True,
                $"'{field}' was set to 5 and a clause of 'AtLeast 5' did not hold — the switch is "
                    + "reading a different field, or none.");

            Assert.That(
                overshoots.IsMet(blackboard),
                Is.False,
                $"'{field}' was set to 5 and a clause of 'AtLeast 6' held, so the value being read "
                    + "is not the one that was written.");
        }
    }

    [Test]
    public void Trigger_NaNFieldFails()
    {
        // Only writable against a *float* field, because an int has no NaN — five of the nine
        // TriggerField members are counts and cannot express this at all. HpFraction is the one the
        // design actually leans on (Consecrate below 0.6), so it is the one the row uses.
        var blackboard = new CombatBlackboard { HpFraction = float.NaN };

        Assert.That(
            Trigger(new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.6f))
                .IsMet(blackboard),
            Is.False);

        // The half that would go wrong on its own. `AtLeast 0` holds for every real fraction there
        // is, so a NaN that satisfied it would fire every AtLeast skill the player owns at once —
        // which is exactly what spelling AtLeast as the negation of Below, `!(value < threshold)`,
        // would do. The two comparisons are written out rather than derived from each other.
        Assert.That(
            Trigger(new TriggerClause(TriggerField.HpFraction, TriggerComparison.AtLeast, 0f))
                .IsMet(blackboard),
            Is.False,
            "A broken blackboard must fail every clause, not cast every skill at once (AR §18.3).");
    }

    // ---- Rule 5's guards, the copy, and the cost -------------------------------------------------

    [Test]
    public void Trigger_ClauseCountGuards()
    {
        Assert.Throws<ArgumentException>(() => new TriggerSpec(Array.Empty<TriggerClause>()));

        Assert.Throws<ArgumentException>(() => new TriggerSpec(new[]
        {
            new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.6f),
            new TriggerClause(TriggerField.EnemiesWithin6m, TriggerComparison.AtLeast, 3f),
            new TriggerClause(TriggerField.Veilrot, TriggerComparison.AtLeast, 50f),
        }));

        Assert.Throws<ArgumentNullException>(() => new TriggerSpec(null));

        Assert.That(TriggerSpec.MaxClauses, Is.EqualTo(2));
    }

    [Test]
    public void Trigger_ThresholdGuards()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TriggerClause(
                TriggerField.HpFraction, TriggerComparison.Below, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TriggerClause(
                TriggerField.HpFraction, TriggerComparison.AtLeast, float.NegativeInfinity));

        // Zero and negative are both legal thresholds: `StationaryTime AtLeast 0` is a condition
        // that always holds, which is how an unconditional active is spelled.
        Assert.DoesNotThrow(
            () => new TriggerClause(TriggerField.StationaryTime, TriggerComparison.AtLeast, 0f));
    }

    [Test]
    public void Trigger_ClausesAreCopied()
    {
        var clause = new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.6f);
        var list = new List<TriggerClause> { clause };

        var spec = new TriggerSpec(list);
        list.Clear();

        Assert.That(spec.Clauses.Count, Is.EqualTo(1), "The caller's list was retained.");
        Assert.That(spec.Clauses[0].Field, Is.EqualTo(TriggerField.HpFraction));
        Assert.That(spec.Clauses, Is.Not.AssignableTo<TriggerClause[]>());
    }

    [Test]
    public void Trigger_AllocatesNothing()
    {
        // M3-06 asks this of every owned active on every tick, so a boxed enumerator here would be
        // a per-frame allocation on a phone. The two-clause case is the expensive one.
        var spec = new TriggerSpec(new[]
        {
            new TriggerClause(TriggerField.Veilrot, TriggerComparison.AtLeast, 50f),
            new TriggerClause(TriggerField.EnemiesWithin8m, TriggerComparison.AtLeast, 4f),
        });

        var blackboard = new CombatBlackboard { Veilrot = 50f, EnemiesWithin8m = 4 };

        AllocationAssert.None(() => _sink = spec.IsMet(blackboard));

        Assert.That(_sink, Is.True, "The measured body was not evaluating the condition at all.");

        // And the probe is live under this harness, not merely in AllocationAssert's own fixture —
        // M1-01's lesson: a measurement that cannot fail proves nothing.
        Assert.Throws<AssertionException>(
            () => AllocationAssert.None(() => _sink = new int[8].Length == 8, iterations: 8));
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    private static ContentId Id() => new ContentId("skill.oathbound.consecrate");

    private static ContentId Parent() => new ContentId("skill.oathbound.aegis");

    private static LocKey Name() => new LocKey("skill.oathbound.consecrate.name");

    private static LocKey Description() => new LocKey("skill.oathbound.consecrate.desc");

    private static IReadOnlyList<IEffect> OneEffect() => new IEffect[] { new Marker() };

    /// <summary>CC §6.4's Consecrate condition: player HP below 60 %.</summary>
    private static TriggerSpec Trigger() =>
        Trigger(new TriggerClause(TriggerField.HpFraction, TriggerComparison.Below, 0.6f));

    private static TriggerSpec Trigger(TriggerClause clause) => new TriggerSpec(new[] { clause });

    /// <summary>M3-12's Consecrate block: 12 s, HP below 60 %, one effect.</summary>
    private static ActiveSpec Consecrate() => new ActiveSpec(12f, Trigger(), OneEffect());

    /// <summary>
    /// An effect that is nothing but an effect. A <c>SkillSpec</c> never looks inside one, so this
    /// is all a row here needs — and using <c>ModifyStat</c> would tie these rows to the one
    /// primitive that happens to exist.
    /// </summary>
    private sealed class Marker : IEffect
    {
    }
}
