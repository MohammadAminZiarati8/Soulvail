using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Save;

namespace Soulvail.Core.Progression;

/// <summary>
/// GD §14.2, and the whole of GD §14's job: which classes an install may pick. Pure functions over a
/// profile and the catalog.
/// </summary>
/// <remarks>
/// <para>
/// <b>The starter is unlocked by having no <see cref="UnlockSpec"/>, not by being in the list</b>
/// (M6-09a rule 3). CH §3's Oathbound cell reads <em>"Free — the starter"</em>, so the gate is
/// <em>"this class authors a price"</em> rather than <em>"this id is in the profile"</em> — which is
/// what makes a fresh <see cref="PlayerProfile.Default"/> with an empty list a playable game rather
/// than one with no classes.
/// </para>
/// <para>
/// <b>Static, and not a violation of the no-statics rule</b>, for <c>ShardPayout</c>'s reason and
/// with its caveat (M6-09a rule 9): no fields to mutate, nothing to reset under a disabled domain
/// reload, nothing anyone could reach a dependency through. <c>Unlocks_HoldNoState</c> is what keeps
/// that true rather than remembered, and <b>if it ever needs a collaborator it becomes an injected
/// object that day</b>.
/// </para>
/// <para>
/// <b>There is no path down.</b> GD §14.2 is a ratchet: nothing here re-locks a class, and nothing
/// asks whether one should be.
/// </para>
/// </remarks>
public static class ClassUnlocks
{
    /// <summary>
    /// Whether <paramref name="characterId"/> may be picked. True for any class with no
    /// <see cref="UnlockSpec"/>, and for any id in <see cref="PlayerProfile.UnlockedCharacterIds"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="characterId"/> is <c>default(ContentId)</c>.</exception>
    /// <exception cref="KeyNotFoundException">The catalog holds no such class.</exception>
    public static bool IsUnlocked(ContentId characterId, in PlayerProfile profile, ContentCatalog catalog)
    {
        CharacterSpec character = Resolve(characterId, catalog);

        return character.Unlock is null || Contains(profile.UnlockedCharacterIds, characterId);
    }

    /// <summary>
    /// Whether it could be bought right now: locked, priced, and affordable — the invariant behind
    /// <c>ProfileStore.Unlock</c>.
    /// </summary>
    /// <remarks>
    /// False for the starter at any balance — it has no price, so there is nothing to buy — and
    /// false for anything already owned, for the same reason.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="characterId"/> is <c>default(ContentId)</c>.</exception>
    /// <exception cref="KeyNotFoundException">The catalog holds no such class.</exception>
    public static bool CanBuy(ContentId characterId, in PlayerProfile profile, ContentCatalog catalog)
    {
        CharacterSpec character = Resolve(characterId, catalog);
        UnlockSpec unlock = character.Unlock;

        return unlock is not null
            && !Contains(profile.UnlockedCharacterIds, characterId)
            && profile.Shards >= unlock.ShardPrice;
    }

    /// <summary>
    /// Which locked classes a run just proved — M6-09a rule 8. Writes into
    /// <paramref name="destination"/> in catalog order and returns how many.
    /// </summary>
    /// <param name="deepestStage"><c>ShardsAwarded.DeepestStage</c>.</param>
    /// <param name="mode">The run's mode, for its authored boss roster.</param>
    /// <param name="profile">The profile as it stood before this run's deeds, for what is owned.</param>
    /// <param name="catalog">Every class, and what each one's deed is.</param>
    /// <param name="destination">
    /// Where the ids are written. Size it to the catalog's classes; too short throws rather than
    /// truncating, because a dropped id is a class a player earned and was never given.
    /// </param>
    /// <returns>How many entries were written. Zero on almost every death of almost every run.</returns>
    /// <remarks>
    /// <para>
    /// <b>A depth deed is inclusive of the depth reached</b> — CH §3's <em>"reach stage 20"</em> is
    /// done by arriving, and a player who dies on 20 reached it.
    /// </para>
    /// <para>
    /// <b>A boss deed is exclusive, for <c>ShardPayout.BossesKilled</c>' reason</b>: a boss is only
    /// known to be dead once its stage has been left, so the walk is over the boss stages strictly
    /// below the depth. <b>The Gravecaller's names <c>boss.choirmother</c>, which no mode in this build
    /// authors</b> — GD §9.2's Choirmother is M7-03's — so the walk never matches it, and the
    /// Gravecaller has exactly one route until M7-03 merges (M6-09a rule 4).
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="mode"/> or <paramref name="catalog"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deepestStage"/> is below 1.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public static int Earned(
        int deepestStage,
        ModeSpec mode,
        in PlayerProfile profile,
        ContentCatalog catalog,
        Span<ContentId> destination)
    {
        if (mode is null)
        {
            throw new ArgumentNullException(
                nameof(mode), "Which deeds a run did needs the run's mode for its boss roster.");
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(
                nameof(catalog), "Which classes a run proved needs the catalog that authors them.");
        }

        if (deepestStage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deepestStage),
                deepestStage,
                "Stages are numbered from 1 (GD §8.2), so a depth below 1 is a caller that read a "
                    + "defaulted event rather than a death.");
        }

        IReadOnlyList<CharacterSpec> characters = catalog.Characters;
        int written = 0;

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSpec character = characters[i];
            UnlockSpec unlock = character.Unlock;

            if (unlock is null
                || !unlock.HasDeed
                || Contains(profile.UnlockedCharacterIds, character.Id)
                || !DeedDone(unlock, deepestStage, mode))
            {
                continue;
            }

            if (written == destination.Length)
            {
                throw new ArgumentException(
                    $"destination holds {destination.Length} entries and a death at stage " +
                    $"{deepestStage} proved more classes than that. Size it to the catalog's classes.",
                    nameof(destination));
            }

            destination[written++] = character.Id;
        }

        return written;
    }

    private static bool DeedDone(UnlockSpec unlock, int deepestStage, ModeSpec mode)
    {
        if (unlock.DeedStage > 0)
        {
            return deepestStage >= unlock.DeedStage;
        }

        for (int stage = 1; stage < deepestStage; stage++)
        {
            if (mode.TryGetBossFor(stage, out ContentId bossId) && bossId == unlock.DeedBossId)
            {
                return true;
            }
        }

        return false;
    }

    private static CharacterSpec Resolve(ContentId characterId, ContentCatalog catalog)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(
                nameof(catalog), "Whether a class is unlocked is a question about what it authors.");
        }

        if (characterId.Value is null)
        {
            throw new ArgumentException(
                "characterId is default(ContentId), which names no class.", nameof(characterId));
        }

        return catalog.Character(characterId);
    }

    /// <summary>Indexed rather than <c>foreach</c>ed, so a probe allocates no enumerator.</summary>
    private static bool Contains(IReadOnlyList<ContentId> ids, ContentId id)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return true;
            }
        }

        return false;
    }
}
