using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Soulvail.Core.Content;

/// <summary>
/// One archetype a mode may spawn, and the depth it is first allowed at (GD §8.2).
/// </summary>
/// <remarks>
/// <para>
/// A <see langword="readonly"/> struct rather than a class, for the reason
/// <c>SpawnPlan.Entry</c> is one: a roster is a list of pairs, and a class per pair would be an
/// allocation per archetype in a type the composer reads once a stage.
/// </para>
/// <para>
/// <b>The schedule belongs to the mode, not to the archetype.</b> GD §8.2's table is Descent's
/// statement about how it paces itself; a Boss Rush would have neither that table nor this
/// roster. What an archetype <em>costs</em> stays on <see cref="EnemySpec"/> (M2-04, GD §8.1),
/// because that is a fact about the creature rather than about who is willing to spawn it.
/// </para>
/// </remarks>
public readonly struct RosterEntry
{
    /// <param name="specId">The archetype's id, e.g. <c>enemy.husk</c>.</param>
    /// <param name="introducedAtStage">
    /// The first stage this mode may spawn it at. 1 means "from the first stage".
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="specId"/> is <c>default(ContentId)</c> — an entry that names no archetype.
    /// Refused where the roster is built rather than where it is spawned, for the reason
    /// <c>SpawnPlan.Entry</c> gives: left alone it would surface one layer down as the catalog's
    /// "no enemy with id ''", pointing at content that was never at fault.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="introducedAtStage"/> is not positive. Stages are numbered from 1
    /// (GD §8.2), so a zero or negative depth is an archetype that is eligible before the game
    /// starts — which reads as "always" and is spelled 1.
    /// </exception>
    public RosterEntry(ContentId specId, int introducedAtStage)
    {
        if (specId.Value is null)
        {
            throw new ArgumentException(
                "specId must be a valid ContentId; default(ContentId) names no archetype.",
                nameof(specId));
        }

        if (introducedAtStage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(introducedAtStage),
                introducedAtStage,
                $"introducedAtStage for '{specId}' must be at least 1. Stages are numbered from 1, "
                    + "and an archetype available from the start is introduced at 1.");
        }

        SpecId = specId;
        IntroducedAtStage = introducedAtStage;
    }

    /// <summary>The archetype, resolved against the <see cref="ContentCatalog"/>.</summary>
    public ContentId SpecId { get; }

    /// <summary>The first stage this mode may spawn it at; 1 = from the first stage.</summary>
    public int IntroducedAtStage { get; }
}

/// <summary>
/// A mode, as authored data: which stages it has, whether it ever ends, and the enemies it is
/// willing to spawn at each depth. Descent is the only instance in V1 (GD §4.5). Converted once
/// at boot from a <c>ModeDefinition</c> ScriptableObject and registered in the
/// <see cref="ContentCatalog"/>. See AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// <b>A mode is a data object, not an assumption baked into the code</b> — GD §4.5's rule, and
/// the whole reason this type exists a milestone before a second mode does. Nothing in the run
/// loop or the director may hard-code "endless" or "starts at stage 1": both are questions asked
/// of this object, by <see cref="IsEndless"/> and <see cref="StartingStage"/>.
/// </para>
/// <para>
/// <b>It lives in <c>Core/Content/</c>, not <c>Core/Run/</c>.</b> AR §5's module table lists it
/// under <c>Run</c>; AR §10.1 puts it in the catalog beside <see cref="CharacterSpec"/> and
/// <see cref="EnemySpec"/>, and the catalog is what a <see cref="ContentId"/> resolves against.
/// §10.1 wins, and §5's row was corrected in M2-02 — a sketch is fixed when it misleads.
/// </para>
/// <para>
/// Immutable and shared, like every other spec: the roster is copied on construction, so a
/// builder that keeps filling its own list afterwards cannot change what the mode holds. A run's
/// live depth is <c>RunState.StageIndex</c>, never a field here.
/// </para>
/// <para>
/// <b>The difficulty curve arrived in M2-03</b>, as <see cref="Scaling"/> — deliberately left out
/// of M2-02 because the curve types did not exist yet and inventing their shape ahead of a caller
/// would have fixed it by guess. Two of the things GD §4.5 lists as the mode's are still absent:
/// which classes are legal, and what starting modifiers a mode carries, both waiting for M5-08 and
/// M6 because one class exists and no effect system does. AR §6's rule for ports is the rule here
/// too — a field with no reader is a guess about what its reader will want.
/// </para>
/// </remarks>
public sealed class ModeSpec
{
    /// <summary>
    /// The roster as an array, for the loops below. <see cref="Roster"/> hands out the wrapper.
    /// </summary>
    /// <remarks>
    /// Both, and not one: <see cref="RosterFor"/> must allocate nothing, and walking an
    /// <see cref="IReadOnlyList{T}"/> is an interface call per element where walking the array is
    /// not. The wrapper exists so the public property cannot be cast back to the array and
    /// written through.
    /// </remarks>
    private readonly RosterEntry[] _roster;

    private readonly ReadOnlyCollection<RosterEntry> _rosterView;

    /// <param name="id">The mode's stable content id, e.g. <c>mode.descent</c>.</param>
    /// <param name="nameKey">Localisation key for the display name.</param>
    /// <param name="startingStage">The depth a fresh run of this mode begins at. Usually 1.</param>
    /// <param name="isEndless">
    /// Whether the mode runs until the player dies. Taken as a flag rather than inferred from
    /// <paramref name="finalStage"/>, because "endless" is a designer's statement about the mode
    /// and <c>int.MaxValue</c> in an asset is not — it is a number somebody typed, and a finite
    /// mode whose last stage happened to be large would silently become endless.
    /// </param>
    /// <param name="finalStage">
    /// The last stage a finite mode has. Ignored — and replaced by <c>int.MaxValue</c> — when
    /// <paramref name="isEndless"/> is true.
    /// </param>
    /// <param name="scaling">
    /// GD §12's five curves for this mode — how much threat a stage costs, how many waves it comes
    /// in, how many enemies may stand in it, and how much tougher, harder-hitting and faster each
    /// of them is for being deep. The mode's, not the game's: a Boss Rush would have a flat budget
    /// and no concurrency ramp at all (GD §4.5).
    /// </param>
    /// <param name="roster">
    /// Every archetype the mode may spawn, with the depth each is introduced at. Copied; the
    /// caller's list is not retained. Order is meaningful: <see cref="RosterFor"/> answers in it.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is <c>default(ContentId)</c>; an entry is <c>default(RosterEntry)</c>
    /// and so names no archetype; two entries share an id; or two entries are introduced at the
    /// same stage. The last is GD §8.2's rule — <em>"new enemies arrive one at a time, in a wave
    /// where they're the only new thing"</em> — and it is what lets
    /// <see cref="TryGetIntroduction"/> answer with a single id rather than a list.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="startingStage"/> is not positive, or a finite mode's
    /// <paramref name="finalStage"/> is before it — a mode with no stages at all.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="scaling"/> or <paramref name="roster"/> is null.
    /// </exception>
    public ModeSpec(
        ContentId id,
        LocKey nameKey,
        int startingStage,
        bool isEndless,
        int finalStage,
        ScalingSpec scaling,
        IReadOnlyList<RosterEntry> roster)
    {
        if (id.Value is null)
        {
            throw new ArgumentException(
                "id must be a valid ContentId; default(ContentId) names no mode.",
                nameof(id));
        }

        if (startingStage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startingStage),
                startingStage,
                $"startingStage for '{id}' must be at least 1. Stages are numbered from 1.");
        }

        // int.MaxValue for an endless mode, so every stage comparison reads the same way and no
        // caller has to branch on IsEndless to ask whether a stage exists. See HasStage.
        int effectiveFinal = isEndless ? int.MaxValue : finalStage;

        if (effectiveFinal < startingStage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(finalStage),
                finalStage,
                $"finalStage for '{id}' is before startingStage ({startingStage}), so the mode "
                    + "has no stages at all. An endless mode passes isEndless: true instead.");
        }

        if (scaling is null)
        {
            throw new ArgumentNullException(nameof(scaling));
        }

        if (roster is null)
        {
            throw new ArgumentNullException(nameof(roster));
        }

        Id = id;
        NameKey = nameKey;
        StartingStage = startingStage;
        IsEndless = isEndless;
        FinalStage = effectiveFinal;
        Scaling = scaling;

        _roster = CopyRoster(roster, id);

        // Wrapped rather than handed out as the array it is: an array exposed as
        // IReadOnlyList<T> casts straight back to RosterEntry[], and then the copy protects
        // nothing. The same guard ContentCatalog and SpawnPlan make, for the same reason.
        _rosterView = Array.AsReadOnly(_roster);
    }

    /// <summary>The mode's stable content id, e.g. <c>mode.descent</c>.</summary>
    public ContentId Id { get; }

    /// <summary>Localisation key for the display name.</summary>
    public LocKey NameKey { get; }

    /// <summary>The depth a fresh run of this mode begins at.</summary>
    /// <remarks>
    /// What <c>RunTicker</c> passes as <c>RunConfig.StageIndex</c> for a new run; a resumed run
    /// passes the saved depth instead (M2-14b). Neither of them knows the number — they ask.
    /// </remarks>
    public int StartingStage { get; }

    /// <summary>Whether the mode runs until the player dies. True for Descent (GD §4.5).</summary>
    public bool IsEndless { get; }

    /// <summary>
    /// The last stage the mode has, or <c>int.MaxValue</c> when <see cref="IsEndless"/>.
    /// </summary>
    public int FinalStage { get; }

    /// <summary>
    /// GD §12's difficulty model for this mode. Shared and immutable; a stage number is always an
    /// argument to it, never a field on it.
    /// </summary>
    /// <remarks>
    /// Read by <c>RunSession.Start</c>, which builds the run's one <c>DepthScaling</c> from it, and
    /// by M2-04's composer through a <c>ThreatBudget</c>. Nothing in the director holds a curve of
    /// its own.
    /// </remarks>
    public ScalingSpec Scaling { get; }

    /// <summary>Every archetype the mode may spawn, in the order they were authored.</summary>
    public IReadOnlyList<RosterEntry> Roster => _rosterView;

    /// <summary>
    /// Whether <paramref name="stage"/> is a stage this mode has.
    /// </summary>
    /// <remarks>
    /// No branch on <see cref="IsEndless"/>, and that is the point of storing
    /// <c>int.MaxValue</c> for an endless mode rather than the authored number: there is one
    /// comparison here and every caller asking the same question gets one too.
    /// </remarks>
    public bool HasStage(int stage) => stage >= StartingStage && stage <= FinalStage;

    /// <summary>
    /// Fills <paramref name="destination"/> with every entry eligible at <paramref name="stage"/>,
    /// in roster order, and returns how many were written.
    /// </summary>
    /// <param name="stage">The depth being composed.</param>
    /// <param name="destination">
    /// Where the eligible entries go. Must be at least as long as the eligible count — a stack
    /// buffer sized to the roster always is.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="destination"/> is shorter than the eligible count. Thrown rather than
    /// truncating: a short buffer would silently narrow the wave's vocabulary, and a director
    /// that quietly stops being allowed to spawn a Bloater is a balance bug with no symptom.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Allocates nothing. The composer (M2-04) calls it once a stage against a
    /// <c>stackalloc</c> buffer, so the array is walked by index and no enumerator is created.
    /// </para>
    /// <para>
    /// Counted first and written second, so a refused call leaves the caller's buffer untouched
    /// rather than half-filled with entries from a wave that was never composed.
    /// </para>
    /// </remarks>
    public int RosterFor(int stage, Span<RosterEntry> destination)
    {
        int eligible = 0;

        for (int i = 0; i < _roster.Length; i++)
        {
            if (_roster[i].IntroducedAtStage <= stage)
            {
                eligible++;
            }
        }

        if (destination.Length < eligible)
        {
            throw new ArgumentException(
                $"destination holds {destination.Length} entries but {eligible} of '{Id}'s "
                    + $"archetypes are eligible at stage {stage}. Size the buffer to the roster.",
                nameof(destination));
        }

        int written = 0;

        for (int i = 0; i < _roster.Length; i++)
        {
            RosterEntry entry = _roster[i];

            if (entry.IntroducedAtStage <= stage)
            {
                destination[written++] = entry;
            }
        }

        return written;
    }

    /// <summary>
    /// The archetype introduced <em>at</em> <paramref name="stage"/> (GD §8.2), or false when the
    /// stage introduces nothing.
    /// </summary>
    /// <remarks>
    /// A single id rather than a list, and the constructor is what makes that honest: two
    /// archetypes may not share an introduction stage, so "the new thing this wave" is always one
    /// thing or none. Most stages introduce nothing — GD §8.2 names ten out of an endless run.
    /// </remarks>
    public bool TryGetIntroduction(int stage, out ContentId specId)
    {
        for (int i = 0; i < _roster.Length; i++)
        {
            if (_roster[i].IntroducedAtStage == stage)
            {
                specId = _roster[i].SpecId;
                return true;
            }
        }

        specId = default;
        return false;
    }

    /// <summary>
    /// Copies the roster, refusing an entry that names nothing, a duplicated id and a stage that
    /// introduces two archetypes.
    /// </summary>
    /// <remarks>
    /// The two dictionaries a <see cref="ContentCatalog"/> would use are two <c>HashSet</c>s here
    /// and neither survives the call: this runs once per mode at boot, over a roster GD §8.2
    /// caps at ten, so the allocation is a boot cost and the alternative — a nested loop — reads
    /// worse for no measurable gain.
    /// </remarks>
    private static RosterEntry[] CopyRoster(IReadOnlyList<RosterEntry> roster, ContentId id)
    {
        if (roster.Count == 0)
        {
            // Legal, and not an oversight: a mode whose arena content comes entirely from its
            // spawn plan has nothing to schedule. RosterFor already answers 0 for any stage
            // before the first introduction, so an empty roster is not a new shape for a caller
            // to handle — it is the same one, for the whole run.
            return Array.Empty<RosterEntry>();
        }

        var copy = new RosterEntry[roster.Count];
        var ids = new HashSet<ContentId>();
        var introductions = new HashSet<int>();

        for (int i = 0; i < roster.Count; i++)
        {
            RosterEntry entry = roster[i];

            // default(RosterEntry) carries a zeroed id straight past the struct's own constructor
            // — a struct always has a zeroed form — so the check is repeated here. The same shape
            // as SpawnPlan's second look at default(Entry): a struct with an invariant needs the
            // check at both ends (AR §18.3).
            if (entry.SpecId.Value is null)
            {
                throw new ArgumentException(
                    $"roster[{i}] of '{id}' names no archetype. A default(RosterEntry) has no "
                        + "spec id.",
                    nameof(roster));
            }

            if (!ids.Add(entry.SpecId))
            {
                throw new ArgumentException(
                    $"'{id}' lists '{entry.SpecId}' twice. An archetype is introduced once and is "
                        + "eligible from then on, so a second row could only disagree with the "
                        + "first.",
                    nameof(roster));
            }

            if (!introductions.Add(entry.IntroducedAtStage))
            {
                throw new ArgumentException(
                    $"'{id}' introduces two archetypes at stage {entry.IntroducedAtStage}, and "
                        + $"'{entry.SpecId}' is the second. GD §8.2: new enemies arrive one at a "
                        + "time, in a wave where they are the only new thing.",
                    nameof(roster));
            }

            copy[i] = entry;
        }

        return copy;
    }
}
