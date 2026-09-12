using System;

namespace Soulvail.Core.Save;

/// <summary>
/// Whether a file this build did not write can still be read, and what it becomes. See AR §11.6
/// and <see href="../../../../Docs/adr/0007-save-store-async-local-first-versioned.md">ADR-0007</see>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Static, and not a violation of the no-statics rule.</b> What ADR-0002 and AR §13 ban is
/// static <em>mutable</em> state and service location — reachable-from-anywhere handles that make
/// composition a lie. This class has no fields to mutate, nothing to reset under a disabled domain
/// reload, and ADR-0007 specifies migrations as <em>"pure core functions"</em> in as many words.
/// <c>Migrations_HoldsNoState</c> is what keeps that true rather than remembered.
/// </para>
/// <para>
/// <b>At v1 the chain is empty and the chain test is why this file ships now.</b> There is nothing
/// to migrate — the gate admits exactly one version and both <c>Migrate</c> methods are the
/// identity — so what exists here is the shape and the assertion around it. The day someone bumps
/// a <c>CurrentVersion</c> to 2 without writing a step,
/// <c>Chain_IsUnbrokenFromOldestToCurrent</c> fails, which is the only mechanism in the project
/// that makes AR §11.6's promise self-enforcing.
/// </para>
/// <para>
/// <b>The signature takes and returns the current DTO</b>, so a future version that only
/// <em>adds</em> fields is decoded with documented defaults and fixed up by its step. A version
/// that renames or retypes a field needs a versioned mirror in the adapter and a wider signature
/// than this, and that is a deliberate future change this shape does not pretend to have solved.
/// </para>
/// </remarks>
public static class SaveMigrations
{
    /// <summary>The oldest run format on disk this build still understands.</summary>
    public const int OldestSupportedRunVersion = 1;

    /// <summary>The oldest profile format on disk this build still understands.</summary>
    public const int OldestSupportedProfileVersion = 1;

    /// <summary>
    /// Whether there is an unbroken chain of steps from <paramref name="version"/> up to
    /// <see cref="RunSnapshot.CurrentVersion"/>.
    /// </summary>
    /// <remarks>
    /// False for 0 — the version <c>default(RunSnapshot)</c> carries and no writer can produce, so
    /// it means <em>never written</em> — false for anything below
    /// <see cref="OldestSupportedRunVersion"/>, and false for anything <em>newer</em> than this
    /// build writes. The last of those is the one worth saying out loud: a downgraded build must
    /// refuse a v2 file rather than read it as though the fields it does not know were absent.
    /// </remarks>
    public static bool CanReadRun(int version)
    {
        return version >= OldestSupportedRunVersion && version <= RunSnapshot.CurrentVersion;
    }

    /// <summary>
    /// Whether there is an unbroken chain of steps from <paramref name="version"/> up to
    /// <see cref="PlayerProfile.CurrentVersion"/>. Mirrors <see cref="CanReadRun"/> exactly.
    /// </summary>
    /// <remarks>
    /// A second method rather than one taking a version floor, because the two formats version
    /// independently: a profile that gains Shards (M4-06) has no reason to bump the run format,
    /// and a shared gate would make every reader guess which number it was being asked about.
    /// </remarks>
    public static bool CanReadProfile(int version)
    {
        return version >= OldestSupportedProfileVersion && version <= PlayerProfile.CurrentVersion;
    }

    /// <summary>
    /// Brings a decoded run of <paramref name="version"/> up to
    /// <see cref="RunSnapshot.CurrentVersion"/>. The identity at the current version.
    /// </summary>
    /// <param name="version">The format <paramref name="decoded"/> was read in.</param>
    /// <param name="decoded">The snapshot as the adapter decoded it.</param>
    /// <exception cref="NotSupportedException">
    /// <see cref="CanReadRun"/> is false for <paramref name="version"/>. A throw rather than a
    /// null, because by here the caller has already asked the gate: reaching this with a refused
    /// version is a bug in the adapter, not a corrupt file.
    /// </exception>
    public static RunSnapshot MigrateRun(int version, in RunSnapshot decoded)
    {
        if (!CanReadRun(version))
        {
            throw new NotSupportedException(
                $"Run format version {version} cannot be read by this build, which understands " +
                $"{OldestSupportedRunVersion} to {RunSnapshot.CurrentVersion}. Ask CanReadRun " +
                "before migrating.");
        }

        // v1 is the current version, so there is no step to run. Each later version adds one
        // `if (version < n) { ... }` here, in order, and a fixture test beside it.
        return decoded;
    }

    /// <summary>
    /// Brings a decoded profile of <paramref name="version"/> up to
    /// <see cref="PlayerProfile.CurrentVersion"/>. The identity at the current version.
    /// </summary>
    /// <param name="version">The format <paramref name="decoded"/> was read in.</param>
    /// <param name="decoded">The profile as the adapter decoded it.</param>
    /// <exception cref="NotSupportedException">
    /// <see cref="CanReadProfile"/> is false for <paramref name="version"/>.
    /// </exception>
    public static PlayerProfile MigrateProfile(int version, in PlayerProfile decoded)
    {
        if (!CanReadProfile(version))
        {
            throw new NotSupportedException(
                $"Profile format version {version} cannot be read by this build, which understands " +
                $"{OldestSupportedProfileVersion} to {PlayerProfile.CurrentVersion}. Ask " +
                "CanReadProfile before migrating.");
        }

        return decoded;
    }
}
