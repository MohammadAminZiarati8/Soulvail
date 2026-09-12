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
/// <b>It carried the arena's spawn points until M2-11a and no longer does.</b> They were here
/// because a run had one room; a run has one room per stage now, so a per-run field would have
/// described the arena the player is no longer standing in. They belong to the arena, arrive on
/// <c>WorldSnapshot.SpawnPoints</c>, and reach the director at <c>Begin</c>.
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

        /// <summary>Whether every component is a real number.</summary>
        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.X) && !float.IsInfinity(v.X)
            && !float.IsNaN(v.Y) && !float.IsInfinity(v.Y)
            && !float.IsNaN(v.Z) && !float.IsInfinity(v.Z);
    }

    private readonly ReadOnlyCollection<Entry> _initial;

    /// <param name="initial">
    /// The enemies to spawn, in the order they should be spawned. Copied; the caller's list is not
    /// retained, so a builder that keeps filling its own list afterwards cannot change what this
    /// plan holds.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="initial"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// An entry names no archetype. <see cref="Entry"/>'s constructor already refuses that, but
    /// <c>default(Entry)</c> carries a zeroed id straight past it — a struct always has a zeroed
    /// form — so the check is repeated here. The same shape as <c>Stat.Add</c>'s second look at
    /// <c>default(Modifier)</c> (M1-01): a struct with an invariant needs the check at both ends.
    /// </exception>
    public SpawnPlan(IReadOnlyList<Entry> initial)
    {
        if (initial is null)
        {
            throw new ArgumentNullException(nameof(initial));
        }

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
}
