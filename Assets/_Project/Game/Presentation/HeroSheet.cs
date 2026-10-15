using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using UnityEngine;

namespace Soulvail.Game.Presentation;

/// <summary>
/// Everything a class's hero page shows (UI-12c): its description, five base numbers, its basic
/// attack, its Space move and every rank of every branch. A pure function's result, the shape of
/// <c>SkillStrip.Build</c>, which it supersedes: no <c>MonoBehaviour</c>, no scene.
/// </summary>
/// <remarks>
/// Lives in Game and not Core because it carries sprites and the Space move's keys, which are on
/// <see cref="CharacterLook"/>. Every number and key it reads comes from core types.
/// </remarks>
public sealed class HeroSheet
{
    /// <summary>The headings the page draws, public so the view and the tests share them.</summary>
    public static readonly LocKey StatsHeading = new LocKey("ui.hero.stats");
    public static readonly LocKey AttackHeading = new LocKey("ui.hero.attack");
    public static readonly LocKey SpaceHeading = new LocKey("ui.hero.space");
    public static readonly LocKey TreeHeading = new LocKey("ui.hero.tree");
    public static readonly LocKey RankFormat = new LocKey("ui.hero.rank");

    private static readonly LocKey WholeFormat = new LocKey("ui.hero.value.whole");
    private static readonly LocKey TenthsFormat = new LocKey("ui.hero.value.tenths");
    private static readonly LocKey MetresFormat = new LocKey("ui.hero.value.metres");
    private static readonly LocKey SpeedFormat = new LocKey("ui.hero.value.speed");

    private readonly Stat[] _stats;
    private readonly Branch[] _branches;

    private HeroSheet(
        LocKey descriptionKey,
        Stat[] stats,
        Entry attack,
        bool hasAttack,
        Entry space,
        bool hasSpace,
        Branch[] branches)
    {
        DescriptionKey = descriptionKey;
        _stats = stats;
        Attack = attack;
        HasAttack = hasAttack;
        Space = space;
        HasSpace = hasSpace;
        _branches = branches;
    }

    /// <summary><see cref="CharacterSpec.DescriptionKey"/>.</summary>
    public LocKey DescriptionKey { get; }

    /// <summary>Always five, in the order of rule 2: health, damage, attacks per second, range, speed.</summary>
    public IReadOnlyList<Stat> Stats => _stats;

    public bool HasAttack { get; }

    /// <summary>The basic attack, or default when <see cref="HasAttack"/> is false.</summary>
    public Entry Attack { get; }

    public bool HasSpace { get; }

    /// <summary>The Space move, or default when <see cref="HasSpace"/> is false.</summary>
    public Entry Space { get; }

    /// <summary>One per tree branch, in branch order; empty for a class with no tree.</summary>
    public IReadOnlyList<Branch> Branches => _branches;

    /// <summary>
    /// The sheet of one class. Reads and never writes; a null <paramref name="icons"/> draws every
    /// icon null, and a default <paramref name="look"/> gives no attack and no Space entry.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> or <paramref name="catalog"/> is null.</exception>
    public static HeroSheet Build(CharacterSpec spec, ContentCatalog catalog, CharacterLook look, SkillIconBook icons)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        // The authored numbers: no node, Pact or Ordeal applied, because the page is outside a run.
        var stats = new[]
        {
            new Stat(new LocKey("ui.hero.stat.health"), WholeFormat, spec.MaxHp),
            new Stat(new LocKey("ui.hero.stat.damage"), WholeFormat, spec.Weapon.Damage),
            new Stat(new LocKey("ui.hero.stat.rate"), TenthsFormat, spec.Weapon.SwingsPerSecond),
            new Stat(new LocKey("ui.hero.stat.range"), MetresFormat, spec.Weapon.Range),
            new Stat(new LocKey("ui.hero.stat.speed"), SpeedFormat, spec.Movement.Speed),
        };

        bool hasAttack = look.AttackNameKey.Key is not null;
        Entry attack = hasAttack
            ? new Entry(look.Emblem, look.AttackNameKey, look.AttackDescriptionKey, 0)
            : default;

        bool hasSpace = spec.MovementSkill.Kind != MovementSkillKind.None && look.SpaceNameKey.Key is not null;
        Entry space = hasSpace
            ? new Entry(look.SpaceIcon, look.SpaceNameKey, look.SpaceDescriptionKey, 0)
            : default;

        return new HeroSheet(
            spec.DescriptionKey,
            stats,
            attack,
            hasAttack,
            space,
            hasSpace,
            BuildBranches(spec, catalog, icons));
    }

    private static Branch[] BuildBranches(CharacterSpec spec, ContentCatalog catalog, SkillIconBook icons)
    {
        if (!catalog.TryGetTreeFor(spec.Id, out SkillTreeSpec tree))
        {
            return Array.Empty<Branch>();
        }

        var branches = new Branch[tree.Branches.Count];

        for (int b = 0; b < branches.Length; b++)
        {
            SkillBranchSpec branch = tree.Branches[b];

            // A node with no icon of its own wears its branch's first, so a rank is never blank
            // beside its siblings.
            Sprite first = icons?.For(branch.Tier(1)[0]);
            var ranks = new List<Entry>(branch.NodeCount);

            for (int tier = 1; tier <= branch.TierCount; tier++)
            {
                IReadOnlyList<ContentId> nodes = branch.Tier(tier);

                for (int n = 0; n < nodes.Count; n++)
                {
                    if (!catalog.TryGetSkill(nodes[n], out SkillSpec skill))
                    {
                        continue;
                    }

                    Sprite icon = icons?.For(nodes[n]);

                    ranks.Add(new Entry(icon != null ? icon : first, skill.NameKey, skill.DescriptionKey, tier));
                }
            }

            branches[b] = new Branch(branch.NameKey, ranks.ToArray());
        }

        return branches;
    }

    /// <summary>One base number: a label, the format that draws it and the value.</summary>
    public readonly struct Stat
    {
        public Stat(LocKey label, LocKey format, float value)
        {
            Label = label;
            Format = format;
            Value = value;
        }

        /// <summary>"Health".</summary>
        public LocKey Label { get; }

        /// <summary>"{0:0}", fed to <c>ILocalizer.Format</c>.</summary>
        public LocKey Format { get; }

        /// <summary>The base number, no modifier applied.</summary>
        public float Value { get; }
    }

    /// <summary>One line of the page: an icon, a name, a description and, for a tree node, its rank.</summary>
    public readonly struct Entry
    {
        public Entry(Sprite icon, LocKey nameKey, LocKey descriptionKey, int rank)
        {
            Icon = icon;
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
            Rank = rank;
        }

        /// <summary>The picture, or null: the page draws Icon_Placeholder.</summary>
        public Sprite Icon { get; }

        public LocKey NameKey { get; }

        public LocKey DescriptionKey { get; }

        /// <summary>The 1-based tier for a tree node; 0 for the attack and the Space move.</summary>
        public int Rank { get; }
    }

    /// <summary>One tree branch: its name and every rank, tier order then node order within a tier.</summary>
    public sealed class Branch
    {
        private readonly Entry[] _ranks;

        public Branch(LocKey nameKey, Entry[] ranks)
        {
            NameKey = nameKey;
            _ranks = ranks;
        }

        /// <summary><see cref="SkillBranchSpec.NameKey"/>.</summary>
        public LocKey NameKey { get; }

        public IReadOnlyList<Entry> Ranks => _ranks;
    }
}
