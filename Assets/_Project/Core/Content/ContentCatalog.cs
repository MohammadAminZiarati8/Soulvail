using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Soulvail.Core.Content;

/// <summary>
/// Everything authored, in one place, addressed by <see cref="ContentId"/>. Built once at boot
/// from the ScriptableObject definitions and registered in <c>BootScope</c> (M0-12); core reads
/// it and never sees an asset. See AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// Characters, enemies, modes, skills and trees. <b>Skills arrived in M3-02a, and cost exactly the
/// two lines this shape promised they would</b> — one dictionary per kind rather than one
/// dictionary of <c>object</c>, so a lookup returns the type it names and a caller cannot ask for a
/// skill and be handed a mode. Modes were the third kind, added in M2-02, and made the same point
/// a milestone earlier.
/// </para>
/// <para>
/// <b>Trees are the one kind with a second index</b>, by <see cref="SkillTreeSpec.CharacterId"/>
/// rather than by id, because a run resolves its tree from the class it is playing and never from a
/// tree id anyone typed. See <see cref="TryGetTreeFor"/>.
/// </para>
/// <para>
/// Immutable after construction, which is what makes it safe to share across every scope for
/// the life of the app: the input list is copied, so a caller that keeps building in its own
/// list afterwards cannot change what the catalog holds.
/// </para>
/// </remarks>
public sealed class ContentCatalog
{
    private readonly Dictionary<ContentId, CharacterSpec> _charactersById;
    private readonly ReadOnlyCollection<CharacterSpec> _characters;
    private readonly Dictionary<ContentId, EnemySpec> _enemiesById;
    private readonly ReadOnlyCollection<EnemySpec> _enemies;
    private readonly Dictionary<ContentId, ModeSpec> _modesById;
    private readonly ReadOnlyCollection<ModeSpec> _modes;
    private readonly Dictionary<ContentId, SkillSpec> _skillsById;
    private readonly ReadOnlyCollection<SkillSpec> _skills;
    private readonly Dictionary<ContentId, SkillTreeSpec> _treesById;
    private readonly ReadOnlyCollection<SkillTreeSpec> _trees;
    private readonly Dictionary<ContentId, BossSpec> _bossesById;
    private readonly ReadOnlyCollection<BossSpec> _bosses;

    /// <summary>
    /// The trees again, keyed by the class they belong to — see <see cref="TryGetTreeFor"/>.
    /// </summary>
    private readonly Dictionary<ContentId, SkillTreeSpec> _treesByCharacter;

    /// <param name="characters">
    /// The character specs to register. Copied; the caller's list is not retained.
    /// </param>
    /// <param name="enemies">
    /// The enemy archetypes to register, or null for none. Optional because "no enemies are
    /// authored yet" is a real state of this project — <c>EnemyDefinition</c> arrives in M1-07 —
    /// and because every later kind adds another list, which would otherwise leave every call
    /// site restating the ones it does not care about. The omission is not silent for long: the
    /// first <see cref="Enemy"/> lookup fails loudly, naming the id it could not find.
    /// </param>
    /// <param name="modes">
    /// The modes to register, or null for none. Optional for the reason
    /// <paramref name="enemies"/> is, and it earned the argument: a project that ran for two
    /// milestones with no <c>ModeSpec</c> at all is what "every later kind adds another list"
    /// was about. A catalog with no modes fails at the first <see cref="Mode"/> lookup, naming
    /// the id — which is <c>RunSession.Start</c>'s first line and so the loudest possible place.
    /// </param>
    /// <param name="skills">
    /// The tree nodes to register, or null for none. Optional for the reason
    /// <paramref name="enemies"/> is, and it is null in every catalog between M3-02a and M3-02b —
    /// the specs land a task before anything authors one.
    /// </param>
    /// <param name="trees">
    /// The skill trees to register, or null for none. Optional for the same reason, and indexed
    /// twice: by id like every other kind, and by the class each belongs to.
    /// </param>
    /// <param name="bosses">
    /// The bosses to register, or null for none. Optional for the reason
    /// <paramref name="enemies"/> is, and it is null in every catalog the game ships until M4-02
    /// authors <c>WardenBoss.asset</c> — M4-01b builds the mechanism and deliberately authors no
    /// content, the same gap <c>Spitter.asset</c> sat in between M2-06 and M2-07b. A mode whose
    /// boss roster names one fails at the first <see cref="Boss"/> lookup, naming the id.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="characters"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// An entry is null, or two entries of one kind share an id — thrown with the duplicated id
    /// in the message, because "one of your assets collides" is not something a person can act
    /// on. Also when two trees name the same character, which is the one collision that is not a
    /// duplicate id: a class has exactly one tree (CH §5), so the second is either a copy nobody
    /// meant to ship or a disagreement about what the class levels into.
    /// </exception>
    public ContentCatalog(
        IReadOnlyList<CharacterSpec> characters,
        IReadOnlyList<EnemySpec> enemies = null,
        IReadOnlyList<ModeSpec> modes = null,
        IReadOnlyList<SkillSpec> skills = null,
        IReadOnlyList<SkillTreeSpec> trees = null,
        IReadOnlyList<BossSpec> bosses = null)
    {
        if (characters is null)
        {
            throw new ArgumentNullException(nameof(characters));
        }

        _characters = Index(
            characters,
            spec => spec.Id,
            "character",
            nameof(characters),
            out _charactersById);

        _enemies = Index(
            enemies ?? Array.Empty<EnemySpec>(),
            spec => spec.Id,
            "enemy",
            nameof(enemies),
            out _enemiesById);

        _modes = Index(
            modes ?? Array.Empty<ModeSpec>(),
            spec => spec.Id,
            "mode",
            nameof(modes),
            out _modesById);

        _skills = Index(
            skills ?? Array.Empty<SkillSpec>(),
            spec => spec.Id,
            "skill",
            nameof(skills),
            out _skillsById);

        _trees = Index(
            trees ?? Array.Empty<SkillTreeSpec>(),
            spec => spec.Id,
            "tree",
            nameof(trees),
            out _treesById);

        _treesByCharacter = IndexTreesByCharacter(_trees, nameof(trees));

        _bosses = Index(
            bosses ?? Array.Empty<BossSpec>(),
            spec => spec.Id,
            "boss",
            nameof(bosses),
            out _bossesById);
    }

    /// <summary>Every registered character, in the order they were supplied.</summary>
    public IReadOnlyList<CharacterSpec> Characters => _characters;

    /// <summary>Every registered enemy archetype, in the order they were supplied.</summary>
    public IReadOnlyList<EnemySpec> Enemies => _enemies;

    /// <summary>Every registered mode, in the order they were supplied.</summary>
    /// <remarks>
    /// One in V1 (GD §4.5). The order is what <c>MenuPresenter</c> means by "the first mode the
    /// catalog holds" — the same stand-in it makes for the class, and for the same reason: there
    /// is no screen to choose either with yet, and naming <c>mode.descent</c> in code is exactly
    /// the assumption GD §4.5 forbids.
    /// </remarks>
    public IReadOnlyList<ModeSpec> Modes => _modes;

    /// <summary>Every registered tree node, in the order they were supplied.</summary>
    /// <remarks>
    /// Order is meaningful to nothing: where a node sits is its <see cref="SkillTreeSpec"/>'s, and
    /// M3-04 draws its offer from what the tree makes available rather than from this list.
    /// </remarks>
    public IReadOnlyList<SkillSpec> Skills => _skills;

    /// <summary>Every registered skill tree, in the order they were supplied.</summary>
    /// <remarks>
    /// One per class (CH §5), so this is as long as <see cref="Characters"/> once M3-12 has
    /// authored the Oathbound's and M5/M7 the rest. <b>A class with no tree is a legal catalog</b> —
    /// it is every catalog between this task and M3-12 — and M3-03 rule 10 says what a run does
    /// with one. M3-14b pins that every <em>shipped</em> character has one.
    /// </remarks>
    public IReadOnlyList<SkillTreeSpec> Trees => _trees;

    /// <summary>Every registered boss, in the order they were supplied.</summary>
    /// <remarks>
    /// <b>Empty in every build until M4-02</b>, which authors the Warden. That is the same gap
    /// every other kind has sat in — no enemies until M1-07, no modes until M2-02, no skills until
    /// M3-02b — and it is why a mode's boss roster is read through <see cref="Boss"/> rather than
    /// assumed to resolve.
    /// </remarks>
    public IReadOnlyList<BossSpec> Bosses => _bosses;

    /// <summary>The character with this id.</summary>
    /// <exception cref="KeyNotFoundException">
    /// No character has that id — including <c>default(ContentId)</c>, which is unknown like any
    /// other id the catalog does not hold. The message names the id, since the caller that asked
    /// is usually several layers from the code that chose it.
    /// </exception>
    public CharacterSpec Character(ContentId id)
    {
        if (!_charactersById.TryGetValue(id, out CharacterSpec spec))
        {
            throw new KeyNotFoundException($"No character with id '{id}' in the catalog.");
        }

        return spec;
    }

    /// <summary>
    /// Looks up a character without throwing. <paramref name="spec"/> is null when this returns
    /// false.
    /// </summary>
    public bool TryGetCharacter(ContentId id, out CharacterSpec spec) =>
        _charactersById.TryGetValue(id, out spec);

    /// <summary>The enemy archetype with this id.</summary>
    /// <exception cref="KeyNotFoundException">
    /// No enemy has that id — including <c>default(ContentId)</c>, which is unknown like any
    /// other id the catalog does not hold. This is the exception <c>EnemySystem.Spawn</c> lets
    /// through rather than translating: a spawn plan naming an archetype nobody authored is
    /// missing content, and the message names the id.
    /// </exception>
    public EnemySpec Enemy(ContentId id)
    {
        if (!_enemiesById.TryGetValue(id, out EnemySpec spec))
        {
            throw new KeyNotFoundException($"No enemy with id '{id}' in the catalog.");
        }

        return spec;
    }

    /// <summary>
    /// Looks up an enemy archetype without throwing. <paramref name="spec"/> is null when this
    /// returns false.
    /// </summary>
    public bool TryGetEnemy(ContentId id, out EnemySpec spec) =>
        _enemiesById.TryGetValue(id, out spec);

    /// <summary>The mode with this id.</summary>
    /// <exception cref="KeyNotFoundException">
    /// No mode has that id — including <c>default(ContentId)</c>, which is unknown like any other
    /// id the catalog does not hold. This is the first thing <c>RunSession.Start</c> asks, so an
    /// unauthored mode fails before a run is announced rather than a stage into one.
    /// </exception>
    public ModeSpec Mode(ContentId id)
    {
        if (!_modesById.TryGetValue(id, out ModeSpec spec))
        {
            throw new KeyNotFoundException($"No mode with id '{id}' in the catalog.");
        }

        return spec;
    }

    /// <summary>
    /// Looks up a mode without throwing. <paramref name="spec"/> is null when this returns false.
    /// </summary>
    public bool TryGetMode(ContentId id, out ModeSpec spec) =>
        _modesById.TryGetValue(id, out spec);

    /// <summary>The tree node with this id.</summary>
    /// <exception cref="KeyNotFoundException">
    /// No skill has that id — including <c>default(ContentId)</c>, which is unknown like any other
    /// id the catalog does not hold. This is what a <see cref="SkillTreeSpec"/>'s node ids resolve
    /// through, so a tree naming a node nobody authored fails at M3-03's <c>Start</c> sweep rather
    /// than at the moment a player is offered it.
    /// </exception>
    public SkillSpec Skill(ContentId id)
    {
        if (!_skillsById.TryGetValue(id, out SkillSpec spec))
        {
            throw new KeyNotFoundException($"No skill with id '{id}' in the catalog.");
        }

        return spec;
    }

    /// <summary>
    /// Looks up a tree node without throwing. <paramref name="spec"/> is null when this returns
    /// false.
    /// </summary>
    public bool TryGetSkill(ContentId id, out SkillSpec spec) =>
        _skillsById.TryGetValue(id, out spec);

    /// <summary>The boss with this id.</summary>
    /// <exception cref="KeyNotFoundException">
    /// No boss has that id — including <c>default(ContentId)</c>, which is unknown like any other
    /// id the catalog does not hold. This is what a mode's boss roster resolves through, so a mode
    /// naming a boss nobody authored fails on the first boss stage it reaches, with the id in the
    /// message.
    /// </exception>
    public BossSpec Boss(ContentId id)
    {
        if (!_bossesById.TryGetValue(id, out BossSpec spec))
        {
            throw new KeyNotFoundException($"No boss with id '{id}' in the catalog.");
        }

        return spec;
    }

    /// <summary>
    /// Looks up a boss without throwing. <paramref name="spec"/> is null when this returns false.
    /// </summary>
    public bool TryGetBoss(ContentId id, out BossSpec spec) =>
        _bossesById.TryGetValue(id, out spec);

    /// <summary>The skill tree with this id.</summary>
    /// <exception cref="KeyNotFoundException">
    /// No tree has that id — including <c>default(ContentId)</c>, which is unknown like any other
    /// id the catalog does not hold.
    /// </exception>
    public SkillTreeSpec Tree(ContentId id)
    {
        if (!_treesById.TryGetValue(id, out SkillTreeSpec spec))
        {
            throw new KeyNotFoundException($"No tree with id '{id}' in the catalog.");
        }

        return spec;
    }

    /// <summary>
    /// Looks up a skill tree without throwing. <paramref name="spec"/> is null when this returns
    /// false.
    /// </summary>
    public bool TryGetTree(ContentId id, out SkillTreeSpec spec) =>
        _treesById.TryGetValue(id, out spec);

    /// <summary>
    /// The tree belonging to <paramref name="characterId"/>, or false if that class has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The door a run actually uses.</b> A run knows the class it is playing and has no reason
    /// to know a tree id, so this is the lookup and <see cref="Tree"/> is the one for tooling and
    /// tests. A class has exactly one tree (CH §5), which is what makes a single answer honest —
    /// two trees naming one character are refused at construction.
    /// </para>
    /// <para>
    /// <b>False is a legal answer, not an error</b>, which is why this is the <c>Try</c> shape with
    /// no throwing twin. Every catalog between M3-02a and M3-12 answers false for the Oathbound,
    /// and M3-03 rule 10 says what a run does with that.
    /// </para>
    /// </remarks>
    public bool TryGetTreeFor(ContentId characterId, out SkillTreeSpec spec) =>
        _treesByCharacter.TryGetValue(characterId, out spec);

    /// <summary>
    /// Indexes the already-copied trees by the class each belongs to, refusing a second tree for
    /// one class.
    /// </summary>
    /// <remarks>
    /// Its own method rather than a second call to <see cref="Index{T}"/>, because the key is not
    /// the entry's id: the message has to name the <em>character</em>, which is the thing a person
    /// can act on, and "duplicate tree id" would point at two assets that are correctly named.
    /// </remarks>
    private static Dictionary<ContentId, SkillTreeSpec> IndexTreesByCharacter(
        IReadOnlyList<SkillTreeSpec> trees,
        string paramName)
    {
        var byCharacter = new Dictionary<ContentId, SkillTreeSpec>(trees.Count);

        for (int i = 0; i < trees.Count; i++)
        {
            SkillTreeSpec tree = trees[i];

            if (byCharacter.ContainsKey(tree.CharacterId))
            {
                throw new ArgumentException(
                    $"Two trees name character '{tree.CharacterId}'; '{tree.Id}' is the second. A "
                        + "class has exactly one tree (CH §5), so the second is either a copy "
                        + "nobody meant to ship or a disagreement about what the class levels into.",
                    paramName);
            }

            byCharacter.Add(tree.CharacterId, tree);
        }

        return byCharacter;
    }

    /// <summary>
    /// Copies <paramref name="source"/>, indexes it by id, and refuses a null entry or a
    /// duplicate id.
    /// </summary>
    /// <remarks>
    /// One method for every kind, taking the id selector as a delegate. It runs once per kind at
    /// boot, so the delegate costs nothing that matters, and it means the two lookups cannot
    /// drift — a third kind gets the same copy, the same guards and the same messages by calling
    /// it rather than by being written again.
    /// </remarks>
    private static ReadOnlyCollection<T> Index<T>(
        IReadOnlyList<T> source,
        Func<T, ContentId> idOf,
        string kind,
        string paramName,
        out Dictionary<ContentId, T> byId)
        where T : class
    {
        var copy = new T[source.Count];
        byId = new Dictionary<ContentId, T>(source.Count);

        for (int i = 0; i < source.Count; i++)
        {
            T spec = source[i];
            if (spec is null)
            {
                throw new ArgumentException($"{paramName}[{i}] is null.", paramName);
            }

            ContentId id = idOf(spec);

            if (byId.ContainsKey(id))
            {
                throw new ArgumentException(
                    $"Duplicate {kind} id '{id}'. Content ids must be unique.",
                    paramName);
            }

            byId.Add(id, spec);
            copy[i] = spec;
        }

        // Wrapped rather than handed out as the array it is: an array exposed as
        // IReadOnlyList<T> casts straight back to T[], and then the copy above protects nothing.
        // One object, once, for the life of the app.
        return Array.AsReadOnly(copy);
    }
}
