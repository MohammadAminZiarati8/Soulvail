using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Progression;

/// <summary>A free, irreversible specialisation, derived from taken nodes when resuming.</summary>
public sealed class PathFlow
{
    private readonly SkillTree _tree;
    private readonly ContentCatalog _catalog;
    private readonly CharacterSpec _character;
    private readonly IDomainEvents _events;

    public PathFlow(SkillTree tree, ContentCatalog catalog, CharacterSpec character, IDomainEvents events)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _events = events ?? throw new ArgumentNullException(nameof(events));

        var candidates = new List<ContentId>(character.PathIds.Count);
        for (int i = 0; i < character.PathIds.Count; i++)
        {
            ContentId id = character.PathIds[i];
            if (catalog.TryGetPath(id, out _))
            {
                candidates.Add(id);
            }
        }

        Candidates = candidates.AsReadOnly();
    }

    public bool IsOpen => Candidates.Count > 0 && ChosenId.Value is null &&
        _tree.Level >= _character.PathOpensAtLevel;

    public IReadOnlyList<ContentId> Candidates { get; }
    public ContentId ChosenId => _tree.Rules.PathId;

    public void Choose(ContentId pathId)
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("The path choice is not open.");
        }

        Restore(pathId);
        _events.Publish(new PathChosen(pathId));
    }

    public bool TryDerive(IReadOnlyList<ContentId> takenNodeIds, out ContentId pathId)
    {
        if (takenNodeIds is null)
        {
            throw new ArgumentNullException(nameof(takenNodeIds));
        }

        for (int node = 0; node < takenNodeIds.Count; node++)
        {
            for (int candidate = 0; candidate < Candidates.Count; candidate++)
            {
                PathSpec path = _catalog.Path(Candidates[candidate]);
                if (path.TryLocate(takenNodeIds[node], out _, out _))
                {
                    pathId = path.Id;
                    return true;
                }
            }
        }

        pathId = default;
        return false;
    }

    /// <summary>Installs silently, before the saved takes are replayed and without a level gate.</summary>
    public void Restore(ContentId pathId)
    {
        bool candidate = false;
        for (int i = 0; i < Candidates.Count; i++)
        {
            candidate |= Candidates[i] == pathId;
        }

        if (!candidate)
        {
            throw new ArgumentException("The path is not a candidate for this class.", nameof(pathId));
        }

        _tree.Rules.InstallPath(_catalog.Path(pathId), _catalog);
        _tree.OnPathInstalled();
    }
}
