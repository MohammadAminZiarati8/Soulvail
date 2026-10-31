using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    /// <summary>A class path, with ordered skills and one asset per rank.</summary>
    [CreateAssetMenu(menuName = "Soulvail/Content/Path", fileName = "Path")]
    public sealed class PathDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "path.new";
        [SerializeField] private string _nameKey = "path.new.name";
        [SerializeField] private string _descriptionKey = "path.new.description";
        [SerializeField] private SkillField[] _skills = Array.Empty<SkillField>();

        public string Id => _id;
        public IReadOnlyList<SkillField> Skills => Array.AsReadOnly(_skills);

        public PathSpec ToSpec(ContentId characterId)
        {
            try
            {
                var skills = new PathSkillSpec[_skills.Length];

                for (int i = 0; i < skills.Length; i++)
                {
                    SkillField skill = _skills[i];

                    if (skill is null || skill.Ranks.Count == 0)
                    {
                        throw new ArgumentException($"Skill slot {i} has no ranks.");
                    }

                    var tiers = new IReadOnlyList<ContentId>[skill.Ranks.Count];

                    for (int rank = 0; rank < tiers.Length; rank++)
                    {
                        SkillDefinition definition = skill.Ranks[rank];

                        if (definition == null)
                        {
                            throw new ArgumentException($"Skill slot {i}, rank {rank + 1} is empty.");
                        }

                        tiers[rank] = new[] { new ContentId(definition.Id) };
                    }

                    skills[i] = new PathSkillSpec(
                        new SkillBranchSpec(skill.Ranks[0].ToSpec().NameKey, tiers), skill.UnlockLevel);
                }

                return new PathSpec(
                    new ContentId(_id), characterId, new LocKey(_nameKey), new LocKey(_descriptionKey), skills);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException($"PathDefinition '{name}' is not valid content: {inner.Message}", inner);
            }
        }

        [Serializable]
        public sealed class SkillField
        {
            [SerializeField] private SkillDefinition[] _ranks = Array.Empty<SkillDefinition>();
            [SerializeField, Min(1)] private int _unlockLevel = 11;

            public IReadOnlyList<SkillDefinition> Ranks => Array.AsReadOnly(_ranks);
            public int UnlockLevel => _unlockLevel;
        }
    }
}
