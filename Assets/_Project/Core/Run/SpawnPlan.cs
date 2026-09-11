using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Run;

/// <summary>
/// The enemies a run starts with: which archetype, and where. Carried by <see cref="RunConfig"/>
/// and spawned by <c>EnemySystem.SpawnAll</c> immediately after <c>RunStarted</c>. See AR §4.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>The only way an enemy exists in M1.</b> There is no director yet, so a plan is how a
/// playtest gets something to shoot at — M1-07 authors the archetype, M1-18 gives it a behaviour,
/// and M1-19 decides what happens when one dies. From M2-05 on the <c>SpawnDirector</c> owns
/// spawning and this shrinks to what an arena has standing in it on arrival, which for most
/// arenas is <see cref="Empty"/>.
/// </para>
/// <para>
/// Authored data, not live state: immutable, copied on construction, and safe to hand to two runs
/// at once. It names archetypes by <see cref="ContentId"/> and never holds an
/// <c>EnemySpec</c> — resolving content is the catalog's job, and doing it here would mean a plan
/// could only be built after the catalog existed.
/// </para>
/// </remarks>
public sealed class SpawnPlan
{
    /// <summary>One enemy to spawn.</summary>
    /// <remarks>
    /// A <see langword="readonly"/> struct rather than a class, because a plan is a list of pairs
    /// and a class per pair would be an allocation per enemy at the one moment a run is already
    /// building everything else it needs.
    /// </remarks>
    public readonly struct Entry
    {
        /// <param name="specId">The archetype's id, e.g. <c>enemy.husk</c>.</param>
        /// <param name="position">Where it starts, in world metres.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="specId"/> is <c>default(ContentId)</c> — an entry that names no
        /// archetype. Refused where the plan is built rather than where it is spawned, for the
        /// reason <see cref="CharacterSpec"/> gives about its own id: left alone it would surface
        /// as the catalog's "no enemy with id ''", pointing at content that was never at fault.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="position"/> has a non-finite component. A NaN here is permanent and
        /// silent: it becomes the agent's position, then its blackboard's
        /// <c>DistanceToPlayer</c>, and every comparison against it is false — so the enemy simply
        /// never does anything, with nothing in the log.
        /// </exception>
        public Entry(ContentId specId, Vector3 position)
        {
            if (specId.Value is null)
            {
                throw new ArgumentException(
                    "specId must be a valid ContentId; default(ContentId) names no archetype.",
                    nameof(specId));
            }

            if (!IsFinite(position))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(position),
                    position,
                    $"position for '{specId}' must be finite in every component.");
            }

            SpecId = specId;
            Position = position;
        }

        /// <summary>The archetype to spawn, resolved against the <see cref="ContentCatalog"/>.</summary>
        public ContentId SpecId { get; }

        /// <summary>Where it starts.</summary>
        public Vector3 Position { get; }

        /// <summary>
        /// Whether every component is a real number. Shared with the outer class's spawn points,
        /// which owe the same check for the same reason — hence internal to the file rather than
        /// private to this struct.
        /// </summary>
        internal static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.X) && !float.IsInfinity(v.X)
            && !float.IsNaN(v.Y) && !float.IsInfinity(v.Y)
            && !float.IsNaN(v.Z) && !float.IsInfinity(v.Z);
    }

    private readonly ReadOnlyCollection<Entry> _initial;

    private readonly ReadOnlyCollection<Vector3> _spawnPoints;

    /// <param name="initial">
    /// The enemies to spawn, in the order they should be spawned. Copied; the caller's list is not
    /// retained, so a builder that keeps filling its own list afterwards cannot change what this
    /// plan holds.
    /// </param>
    /// <param name="respawn">
    /// What replaces the dead, or null for an arena that empties and stays empty. Optional rather
    /// than required, unlike <c>RunConfig</c>'s plan: every plan built before M1-19 meant "no
    /// respawn" and still does, so the default is the behaviour that already existed.
    /// </param>
    /// <param name="spawnPoints">
    /// Where the director may put a body, in world metres. Copied, like the entries. Empty — and
    /// omitted, which means the same thing — for an arena with no spawning surface, which makes
    /// the director inert rather than being an error (M2-05 rule 12).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="initial"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// An entry names no archetype. <see cref="Entry"/>'s constructor already refuses that, but
    /// <c>default(Entry)</c> carries a zeroed id straight past it — a struct always has a zeroed
    /// form — so the check is repeated here. The same shape as <c>Stat.Add</c>'s second look at
    /// <c>default(Modifier)</c> (M1-01): a struct with an invariant needs the check at both ends.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A spawn point has a non-finite component. Refused here for the reason an entry's position
    /// is: a NaN becomes a position nothing can ever be far enough from, so the point is silently
    /// never used and the arena appears to have fewer of them than it was dressed with.
    /// </exception>
    public SpawnPlan(
        IReadOnlyList<Entry> initial,
        RespawnPolicy respawn = null,
        IReadOnlyList<Vector3> spawnPoints = null)
    {
        if (initial is null)
        {
            throw new ArgumentNullException(nameof(initial));
        }

        Respawn = respawn;
        _spawnPoints = CopySpawnPoints(spawnPoints);

        if (initial.Count == 0)
        {
            _initial = Array.AsReadOnly(Array.Empty<Entry>());
            return;
        }

        var copy = new Entry[initial.Count];

        for (int i = 0; i < initial.Count; i++)
        {
            Entry entry = initial[i];

            if (entry.SpecId.Value is null)
            {
                throw new ArgumentException(
                    $"initial[{i}] names no archetype. A default(Entry) has no spec id.",
                    nameof(initial));
            }

            copy[i] = entry;
        }

        // Wrapped rather than handed out as the array it is: an array exposed as
        // IReadOnlyList<T> casts straight back to Entry[], and then the copy above protects
        // nothing. The same guard ContentCatalog makes for the same reason.
        _initial = Array.AsReadOnly(copy);
    }

    /// <summary>A plan with nothing in it — a run that starts empty.</summary>
    /// <remarks>
    /// <para>
    /// Static, and safe to be: it is immutable, so there is no mutable static state for a disabled
    /// domain reload to carry between plays. The same shape as <c>Vector3.Zero</c>, and the reason
    /// "no enemies" has a name at all is that <see cref="RunConfig"/> asks for a plan rather than
    /// accepting its absence — a run that starts empty should say so out loud.
    /// </para>
    /// <para>
    /// A property rather than a <c>static readonly</c> field on purpose: <c>.editorconfig</c>
    /// carries an underscore-camel rule for private fields at <em>warning</em> severity and a
    /// PascalCase rule for static readonly ones at <em>suggestion</em>, and which wins is not
    /// obvious from reading it (M1-04). A property's backing field is the compiler's problem.
    /// </para>
    /// </remarks>
    public static SpawnPlan Empty { get; } = new SpawnPlan(Array.Empty<Entry>());

    /// <summary>The enemies to spawn, in spawn order.</summary>
    public IReadOnlyList<Entry> Initial => _initial;

    /// <summary>
    /// Where the director may put a body. Empty for an arena with no spawning surface, which makes
    /// the director inert rather than being an error — see <c>SpawnDirector.Tick</c>, rule 12.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not the same list as <see cref="Initial"/>, and not the same question.</b> An entry is a
    /// body standing in the arena when the player walks in; a spawn point is a place a wave may
    /// arrive at later. An arena can have either without the other — M0's grey box has neither, and
    /// from M2-11 most arenas will have only the second.
    /// </para>
    /// <para>
    /// Empty is a real answer rather than a missing one, for <see cref="Respawn"/>'s reason: it is
    /// what the whole of M1's Run scene meant and what every core fixture that starts a run without
    /// caring about spawning still means. The cost — an arena dressed without a spawn ring is
    /// silently quiet — is bought back in the Editor, where <c>DebugOverlay</c> says
    /// <c>director: —</c> rather than leaving it a mystery.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Vector3> SpawnPoints => _spawnPoints;

    /// <summary>
    /// What replaces the dead, or null for an arena that empties once and stays empty.
    /// </summary>
    /// <remarks>
    /// Adopted by <c>EnemySystem.SpawnAll</c> along with the opening population, so a run's whole
    /// spawning behaviour arrives in one object from one place. Null is a real answer rather than a
    /// missing one — M0's empty grey box and M2's arenas that are cleared for good both mean it.
    /// </remarks>
    public RespawnPolicy Respawn { get; }

    /// <summary>
    /// Copies and checks the spawn points, answering an empty list for the absent case.
    /// </summary>
    /// <remarks>
    /// Wrapped rather than handed out as the array it is, for <see cref="_initial"/>'s reason: an
    /// array exposed as <c>IReadOnlyList&lt;T&gt;</c> casts straight back to <c>Vector3[]</c>, and
    /// then the copy protects nothing — which matters here more than there, because the director
    /// holds this list for the length of a run and indexes its claims by position in it.
    /// </remarks>
    private static ReadOnlyCollection<Vector3> CopySpawnPoints(IReadOnlyList<Vector3> spawnPoints)
    {
        if (spawnPoints is null || spawnPoints.Count == 0)
        {
            return Array.AsReadOnly(Array.Empty<Vector3>());
        }

        var copy = new Vector3[spawnPoints.Count];

        for (int i = 0; i < copy.Length; i++)
        {
            Vector3 point = spawnPoints[i];

            if (!Entry.IsFinite(point))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(spawnPoints),
                    point,
                    $"spawnPoints[{i}] must be finite in every component.");
            }

            copy[i] = point;
        }

        return Array.AsReadOnly(copy);
    }
}
