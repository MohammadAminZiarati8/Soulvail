using System;
using System.IO;
using Soulvail.Game.Adapters;
using UnityEngine;
using UnityEngine.TestTools;

namespace Soulvail.Tests.PlayMode.Support;

/// <summary>
/// Moves the machine's saves out of the way before a PlayMode run enters Play and puts them back
/// after it leaves, so the suite plays a fresh install and leaves <c>run.json</c> and
/// <c>profile.json</c> byte-identical to what was there before (RS-03g).
/// </summary>
/// <remarks>
/// <para>
/// <b>The files move, not the store.</b> The runner builds boot's root on its own first scene, before
/// any row runs, so its <see cref="LocalJsonSaveStore"/> over <c>persistentDataPath</c> is the one
/// every run and every profile write goes through. The framework calls <see cref="Setup"/> before it
/// enters Play and <see cref="Cleanup"/> after it leaves, the second even when the run fails, and no
/// store exists at either moment.
/// </para>
/// <para>
/// <b>Named on every fixture rather than on the assembly</b> (rule 1). The framework reads an
/// assembly-level <c>[PrebuildSetup]</c> only from an assembly whose compiled references include
/// <c>UnityEditor.TestRunner</c>, and this one's do not, so the attribute would compile and do
/// nothing. <c>SaveShelterTests.EveryFixture_SheltersTheSaves</c> keeps the next fixture honest.
/// </para>
/// <para>
/// <b>Absence is recorded, not inferred</b>, which is what makes a killed Editor safe at every step.
/// A file with neither a copy nor a marker in the shelter is one <see cref="Setup"/> never reached,
/// and it is still the player's.
/// </para>
/// </remarks>
public sealed class SaveShelter : IPrebuildSetup, IPostBuildCleanup
{
    /// <summary>The folder, under the save directory, that holds the saves while a run plays.</summary>
    public const string FolderName = "PlayModeShelter";

    /// <summary>What marks a save that was not there, beside the name it would have had.</summary>
    private const string AbsentSuffix = ".absent";

    private readonly string _directory;
    private readonly string _shelter;
    private readonly string[] _names = { LocalJsonSaveStore.RunFileName, LocalJsonSaveStore.ProfileFileName };

    /// <summary>Over <see cref="Application.persistentDataPath"/>: the constructor the runner calls.</summary>
    public SaveShelter()
        : this(Application.persistentDataPath)
    {
    }

    /// <param name="directory">
    /// Where the saves live. Taken rather than read, for <see cref="LocalJsonSaveStore"/>'s reason: a
    /// row that sheltered <c>persistentDataPath</c> would move the owner's files (rule 6).
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is null, empty or blank.</exception>
    public SaveShelter(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "A shelter needs the save directory — Application.persistentDataPath for the runner, " +
                "a temporary folder in a test.",
                nameof(directory));
        }

        _directory = directory;
        _shelter = Path.Combine(directory, FolderName);
    }

    /// <summary>
    /// Moves each save that exists into the shelter and marks each that does not, leaving a fresh
    /// install (rule 2). A shelter an earlier run left behind is restored first (rule 5).
    /// </summary>
    public void Setup()
    {
        if (Directory.Exists(_shelter))
        {
            Debug.LogWarning(
                $"A PlayMode run left the saves sheltered in {_shelter} and never put them back. " +
                "Restoring them before this run shelters them again.");

            Cleanup();
        }

        Directory.CreateDirectory(_shelter);

        foreach (string name in _names)
        {
            string live = Path.Combine(_directory, name);
            string kept = Path.Combine(_shelter, name);

            if (File.Exists(live))
            {
                File.Move(live, kept);
            }
            else
            {
                File.WriteAllBytes(kept + AbsentSuffix, Array.Empty<byte>());
            }
        }
    }

    /// <summary>
    /// Puts back what <see cref="Setup"/> recorded over whatever the run wrote, then removes the
    /// shelter (rules 3 and 4). Does nothing when there is no shelter.
    /// </summary>
    public void Cleanup()
    {
        if (!Directory.Exists(_shelter))
        {
            return;
        }

        foreach (string name in _names)
        {
            string live = Path.Combine(_directory, name);
            string kept = Path.Combine(_shelter, name);
            string absent = kept + AbsentSuffix;

            if (File.Exists(kept))
            {
                File.Delete(live);
                File.Move(kept, live);
            }
            else if (File.Exists(absent))
            {
                File.Delete(live);
                File.Delete(absent);
            }

            // Neither: Setup stopped before this file, so the one in place was never moved.
        }

        // Not recursive: anything else in here is not the shelter's to delete, and says so loudly.
        Directory.Delete(_shelter);
    }
}
