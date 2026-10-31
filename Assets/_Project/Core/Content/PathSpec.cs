using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Soulvail.Core.Content;

/// <summary>A skill's ranks, one node per tier, and the level that opens it.</summary>
public sealed class PathSkillSpec
{
    public PathSkillSpec(SkillBranchSpec ranks, int unlockLevel)
    {
        Ranks = ranks ?? throw new ArgumentNullException(nameof(ranks));

        if (unlockLevel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(unlockLevel));
        }

        for (int rank = 1; rank <= ranks.TierCount; rank++)
        {
            if (ranks.Tier(rank).Count != 1)
            {
                throw new ArgumentException("A path skill has exactly one node per rank.", nameof(ranks));
            }
        }

        UnlockLevel = unlockLevel;
    }

    public SkillBranchSpec Ranks { get; }
    public int UnlockLevel { get; }
}

/// <summary>Immutable class specialisation, installed after the class's own branches.</summary>
public sealed class PathSpec
{
    public const int MaxSkills = 8;

    private readonly ReadOnlyCollection<PathSkillSpec> _skills;
    private readonly Dictionary<ContentId, (int Skill, int Rank)> _locations;

    public PathSpec(
        ContentId id,
        ContentId characterId,
        LocKey nameKey,
        LocKey descriptionKey,
        IReadOnlyList<PathSkillSpec> skills)
    {
        if (id.Value is null)
        {
            throw new ArgumentException("A path needs an id.", nameof(id));
        }

        if (characterId.Value is null)
        {
            throw new ArgumentException("A path needs a character id.", nameof(characterId));
        }

        if (nameKey.Key is null)
        {
            throw new ArgumentException("A path needs a name key.", nameof(nameKey));
        }

        if (descriptionKey.Key is null)
        {
            throw new ArgumentException("A path needs a description key.", nameof(descriptionKey));
        }

        if (skills is null)
        {
            throw new ArgumentNullException(nameof(skills));
        }

        if (skills.Count < 1 || skills.Count > MaxSkills)
        {
            throw new ArgumentException($"A path holds 1 to {MaxSkills} skills.", nameof(skills));
        }

        var copy = new PathSkillSpec[skills.Count];
        _locations = new Dictionary<ContentId, (int Skill, int Rank)>();

        for (int skill = 0; skill < skills.Count; skill++)
        {
            PathSkillSpec spec = skills[skill] ?? throw new ArgumentNullException(nameof(skills));

            for (int rank = 1; rank <= spec.Ranks.TierCount; rank++)
            {
                ContentId node = spec.Ranks.Tier(rank)[0];

                if (_locations.ContainsKey(node))
                {
                    throw new ArgumentException($"Path '{id}' lists '{node}' twice.", nameof(skills));
                }

                _locations.Add(node, (skill, rank));
            }

            copy[skill] = spec;
        }

        Id = id;
        CharacterId = characterId;
        NameKey = nameKey;
        DescriptionKey = descriptionKey;
        NodeCount = _locations.Count;
        _skills = Array.AsReadOnly(copy);
    }

    public ContentId Id { get; }
    public ContentId CharacterId { get; }
    public LocKey NameKey { get; }
    public LocKey DescriptionKey { get; }
    public IReadOnlyList<PathSkillSpec> Skills => _skills;
    public int NodeCount { get; }

    /// <summary>Skill index is zero-based; rank is one-based, like a branch tier.</summary>
    public bool TryLocate(ContentId skillId, out int skill, out int rank)
    {
        if (_locations.TryGetValue(skillId, out var location))
        {
            skill = location.Skill;
            rank = location.Rank;
            return true;
        }

        skill = -1;
        rank = 0;
        return false;
    }
}
