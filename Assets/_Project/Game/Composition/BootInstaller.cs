using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Composition;

/// <summary>
/// Everything the app owns for its whole life: the content catalog — characters and enemy
/// archetypes — the look book that says how those archetypes are drawn, and the slot the menu
/// writes the next run into. Scene-free and static so a test
/// can build the real container and resolve from it: a wiring mistake fails in the Test Runner
/// rather than on a phone. See AR §7 and ADR-0002.
/// </summary>
/// <remarks>
/// <para>
/// Static, but not state: this class holds nothing. It is a function from a builder to a set of
/// registrations, which is what lets <c>BootScope</c> (M0-13) and an EditMode test install the
/// same wiring without sharing a MonoBehaviour.
/// </para>
/// <para>
/// The catalog is built here, eagerly, rather than registered as a factory. A broken asset is
/// then a loud failure at boot naming the file, which is what <c>ToSpec</c>'s message was
/// written for (M0-11); deferred to the first resolve it would surface mid-run, one scene later,
/// pointing at whoever asked for content rather than at the asset that is wrong.
/// </para>
/// </remarks>
public static class BootInstaller
{
    /// <summary>
    /// How many enemies a <c>WorldSnapshot</c> can carry — the concurrency cap core is allowed
    /// to see at once. A constant here, a <c>TuningConfig</c> field once M8-03's device tiering
    /// has an opinion about it.
    /// </summary>
    public const int SnapshotEnemyCapacity = 64;

    /// <summary>
    /// The most enemies this device may have alive at once — GD §11.1's mid tier. What
    /// <c>ThreatBudget</c> caps GD §12.2's C(n) at, and so what <c>WaveComposer</c> fills a wave up
    /// to. A constant until M8-03 detects a tier.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Chosen against two measured costs rather than picked</b> (M2-04 rule 1, ledger rows 4
    /// and 5), because the mid tier is a claim about a phone and the two systems that scale with
    /// population are the ones that have to survive it:
    /// </para>
    /// <para>
    /// <b>Ally counting is quadratic and that is fine here.</b> <c>EnemySystem</c>'s
    /// <c>AlliesNearby</c> is n² − n XZ comparisons a frame over the registered count: <b>756 at
    /// 28</b>, 1,560 at GD's high tier of 40, and 4,032 at
    /// <see cref="SnapshotEnemyCapacity"/>. 756 squared-distance comparisons is not worth
    /// restructuring for, so the O(n²) stays and this is the record of the arithmetic. <b>A cap
    /// above 40 needs a spatial hash first</b> — ROADMAP parking lot, M8-03's if a high tier ever
    /// ships.
    /// </para>
    /// <para>
    /// <b>Path refresh cannot currently sustain it, and that is a known debt with an owner.</b>
    /// <c>NavPathSense</c> refreshes at most <c>MaxRefreshesPerFrame</c> = 4 enemies a frame
    /// against a 10 Hz cadence, so it sustains <b>24 at 60 fps and 12 at 30</b> — below this cap
    /// and below GD's low tier of 18. Nothing reports it: routes simply go stale and enemies walk
    /// into pillars. <b>M2-05 makes that budget scale with population and frame time</b>, and 28 is
    /// the number both tasks are written against — which is why this is 28 and not 24. Lowering it
    /// to what the pathfinder manages today would hide the debt in a constant and leave M2-05 with
    /// nothing to fix.
    /// </para>
    /// </remarks>
    public const int DeviceEnemyCap = 28;

    /// <summary>
    /// The most enemy projectiles that may be in the air at once (M2-07a rule 7). A constant here,
    /// beside <see cref="DeviceEnemyCap"/>, because it is the same kind of number: what this device
    /// is allowed to have happening at once, rather than a difficulty one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Chosen against the concurrency cap and deliberately not derived from it.</b> A Spitter
    /// holds one shot in the air at a time, so at <see cref="DeviceEnemyCap"/> = 28 an arena of
    /// nothing but Spitters could not exceed 28 — and 32 is that with headroom, because a shot
    /// <em>outlives its shooter</em> (M2-07a rule 5): the bolts released by a wave that has just
    /// been wiped are still flying, so the two counts are not the same question and an expression
    /// tying them together would read as if they were.
    /// </para>
    /// <para>
    /// Reaching it is already a fault rather than a busy fight, and <c>ProjectileSystem.Fire</c>
    /// treats it that way: the shot is refused in silence and the run carries on, because one lost
    /// bolt is better than an exception that ends it.
    /// </para>
    /// </remarks>
    public const int ProjectileCapacity = 32;

    /// <param name="builder">The root container being built.</param>
    /// <param name="characters">
    /// Every authored character. Converted immediately; the list is not retained.
    /// </param>
    /// <param name="enemies">
    /// Every authored enemy archetype, converted the same way. Required rather than optional,
    /// unlike <see cref="ContentCatalog"/>'s own parameter: this method has two call sites, and
    /// letting one of them omit its enemies silently would produce a catalog whose only symptom
    /// is an arena that never fills — the one failure a playtest cannot tell apart from a broken
    /// spawner (M1-06). An empty list is how a boot list with no enemies says so out loud.
    /// </param>
    /// <param name="modes">
    /// Every authored mode — the assets in <c>Data/Modes/</c>, which is one of them in V1
    /// (GD §4.5). Required for the reason <paramref name="enemies"/> is, one step sharper: a
    /// catalog with no modes cannot start any run at all, because resolving the mode is the
    /// first thing <c>RunSession.Start</c> does.
    /// </param>
    /// <param name="skills">
    /// Every authored tree node — the assets in <c>Data/Skills/</c>, which is none of them until
    /// M3-12. Required for the reason <paramref name="enemies"/> is: a call site that could omit
    /// its skills would produce a catalog whose only symptom is a level-up screen with nothing on
    /// it, which reads as a broken offer rather than as missing content. An empty list is how a
    /// boot list with no skills says so out loud, and it is a legal boot until M3-12.
    /// </param>
    /// <param name="trees">
    /// Every authored skill tree, converted the same way and required for the same reason.
    /// <b>Neither list resolves the other here:</b> a tree carries node ids and the catalog is
    /// built from both at once, so a tier naming a node this list does not hold is
    /// <c>TreeRules</c>' check at <c>Start</c> (M3-03) and M3-14b's over every shipped asset.
    /// </param>
    /// <param name="bosses">
    /// Every authored boss — the assets in <c>Data/Enemies/</c> whose type is
    /// <see cref="BossDefinition"/>, which is one of them in V1 (GD §9.2, M4-02).
    /// <b>Optional and trailing, which is the one of the six lists that is, and the asymmetry is
    /// argued rather than convenient:</b> the reason the other five are required is that omitting
    /// one produces a catalog whose only symptom is a silence a playtest cannot tell from a bug —
    /// an arena that never fills, a level-up screen with nothing on it. Omitting this one has no
    /// silent form at all. A mode with no boss roster never asks for a boss, and a mode with one
    /// is refused by <c>RunSession.Start</c> before the run is announced, naming the mode and the
    /// id. So the five say <em>"an empty list is how a boot with none says so out loud"</em>, and
    /// this one does not need to, which spares a dozen fixtures a parameter that could only ever
    /// be empty.
    /// <b>It does not resolve the enemy it names:</b> a boss carries an archetype id and the
    /// catalog is built from both lists at once, so that check is <c>RunSession.Start</c>'s too.
    /// </param>
    /// <param name="localization">
    /// The one language — <c>Data/Localisation/English.asset</c> (M3-14a rule 4). Converted
    /// immediately by <see cref="TableLocalizer"/>, so a duplicate or an unusable key is a loud
    /// failure at boot naming the asset rather than a missing word on a screen three scenes later.
    /// <b>Required rather than optional, and registered at the root rather than per run:</b> the
    /// Menu needs it as much as a run does (rule 8), and a localizer that existed only during a run
    /// is exactly how <em>"Descend"</em> would have stayed English for another three milestones.
    /// <b>It is the fallback</b> (M6-10 rule 1): its locale is empty, and every other table drops to
    /// it on a miss.
    /// </param>
    /// <param name="languages">
    /// Every other shipped table — <c>Data/Localisation/Pseudo.asset</c>, which is the only one in
    /// V1 (M6-10 rule 3). <b>Optional and trailing, for <paramref name="bosses"/>' reason</b>:
    /// omitting it has no silent form, because every screen still reads English, and a dozen
    /// fixtures that build a container to resolve something else would carry a parameter that could
    /// only ever be empty.
    /// </param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// A definition is an empty slot, or is not valid content. Thrown from here rather than
    /// swallowed: a catalog missing a class would fail later as a missing content id, which
    /// names the wrong culprit.
    /// </exception>
    public static void Install(
        IContainerBuilder builder,
        IReadOnlyList<CharacterDefinition> characters,
        IReadOnlyList<EnemyDefinition> enemies,
        IReadOnlyList<ModeDefinition> modes,
        IReadOnlyList<SkillDefinition> skills,
        IReadOnlyList<SkillTreeDefinition> trees,
        LocalizationTable localization,
        IReadOnlyList<BossDefinition> bosses = null,
        IReadOnlyList<LocalizationTable> languages = null)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (characters is null)
        {
            throw new ArgumentNullException(nameof(characters));
        }

        if (enemies is null)
        {
            throw new ArgumentNullException(nameof(enemies));
        }

        if (modes is null)
        {
            throw new ArgumentNullException(nameof(modes));
        }

        if (skills is null)
        {
            throw new ArgumentNullException(nameof(skills));
        }

        if (trees is null)
        {
            throw new ArgumentNullException(nameof(trees));
        }


        // Unity's operator rather than `is null`, because a ScriptableObject field left empty in the
        // Inspector is a live reference only Unity calls null — Convert's cast, for its reason. The
        // message names the *field* rather than the parameter: what the reader has to go and drag
        // something onto is a slot on a prefab, and "localization is null" points at the wrong file.
        if (localization == null)
        {
            throw new ArgumentNullException(
                nameof(localization),
                "BootScope's Localization field is empty, so every screen in the game would draw "
                    + "its own LocKey — a card reading 'skill.oathbound.consecrate.name'. Drop "
                    + "Data/Localisation/English.asset onto it.");
        }

        EnemySpec[] enemySpecs = Convert(
            enemies, definition => definition.ToSpec(), "enemy", nameof(enemies));

        builder.RegisterInstance(new ContentCatalog(
            Convert(characters, definition => definition.ToSpec(), "character", nameof(characters)),
            enemySpecs,
            Convert(modes, definition => definition.ToSpec(), "mode", nameof(modes)),
            Convert(skills, definition => definition.ToSpec(), "skill", nameof(skills)),
            Convert(trees, definition => definition.ToSpec(), "tree", nameof(trees)),
            Convert(
                bosses ?? Array.Empty<BossDefinition>(),
                definition => definition.ToSpec(),
                "boss",
                nameof(bosses))));

        // After the catalog and not before, so a pair of definitions sharing an id is reported by
        // ContentCatalog — which is the message that names the failure people already know how to
        // read. The look book refuses the same duplicate a line later, and would otherwise get
        // there first with a message about colours.
        builder.RegisterInstance(BuildLookBook(enemies, enemySpecs));

        // The wall clock, at the root: it is a device the whole app shares, not something a run
        // owns — the same argument as the vibrator below, and the opposite of IRandom, which is
        // Scoped because a seed *is* a run (RunInstaller). Nothing consumes it yet; the first
        // reader is the save store's timestamp (M2-13a).
        builder.Register<UnityClock>(Lifetime.Singleton).As<IClock>();

        // Singleton, and deliberately not Scoped: the menu sets it in one scene and the run
        // scope reads it in the next, so it has to outlive both.
        builder.Register<PendingRun>(Lifetime.Singleton);

        // Its sibling, and a different question (M2-14b rule 8): what the disk said at launch,
        // rather than what the player chose. Singleton for a stronger reason than PendingRun's —
        // it is written exactly once per app launch, by BootFlow, and every later reader is asking
        // about that one read.
        builder.Register<SavedRun>(Lifetime.Singleton);

        // Haptics live at the root rather than in the run, both of them. The vibrator is one
        // device and holds one JNI handle for the app's life, and the preference has to survive
        // leaving a run — a toggle that reset itself every time the player descended would be
        // worse than none. What is scoped to a run is the listener, which RunScope registers.
        //
        // The platform decides which vibrator by compilation, not by a runtime check. !UNITY_EDITOR
        // is the load-bearing half: UNITY_ANDROID is defined in the Editor whenever the active build
        // target is Android, where UnityPlayer.currentActivity does not exist.
#if UNITY_ANDROID && !UNITY_EDITOR
        builder.Register<AndroidVibrator>(Lifetime.Singleton).As<IVibrator>();
#else
        builder.Register<NullVibrator>(Lifetime.Singleton).As<IVibrator>();
#endif

        // Persistence, at the root and singleton: one directory, one pair of files, for the app's
        // whole life. A factory because the path is a Unity API and the adapter deliberately takes
        // its directory rather than reading it — which is the only reason it is testable at all
        // (M2-13b). Nothing on disk is touched until something saves.
        builder.Register<ISaveStore>(
            _ => new LocalJsonSaveStore(Application.persistentDataPath), Lifetime.Singleton);

        // The live profile, at the root and singleton, and the only writer of one (M3-09c rules 3
        // and 4). Here rather than in RunScope because a profile outlives a run by definition and
        // the one-time hint is spent *during* one — a store rebuilt per run would forget the write
        // between the level-up that spent it and the boundary that saved it. It reads nothing at
        // construction: BootFlow loads the profile once and hands it over through Adopt.
        builder.Register<ProfileStore>(Lifetime.Singleton);

        // A factory rather than a plain type registration, so the choice between "persisted" and
        // "in memory" is made out loud — the class has no public constructor, exactly so that it
        // has to be (M1-20). **Over ProfileStore rather than ISaveStore as of M3-09c**: the profile
        // has two fields now, and a feature that writes the store directly authors the whole struct
        // from the one field it knows (rule 3). The value it starts at is GD §16.3's default;
        // BootFlow hands the stored profile to the store above before the Menu appears.
        builder.Register<HapticsSettings>(
            resolver => HapticsSettings.FromStore(resolver.Resolve<ProfileStore>()),
            Lifetime.Singleton);

        // The language, at the root. **Built here and not deferred to the first resolve**, which is
        // the catalog's bargain rather than the save store's: a duplicate key is then a loud failure
        // at boot naming the asset, where a factory would surface it on whichever screen happened to
        // ask for a word first. An instance registration is a singleton by construction.
        //
        // **On the device's language, because the profile has not answered yet** (M6-10 rule 6).
        // The container is built before ISaveStore.LoadProfile's Task exists, so the stored choice
        // cannot be read here; BootFlow applies it before the Menu loads. A device language this
        // build has no table for reads English, which in V1 is every device.
        //
        // Registered as the port for every screen, and as itself for BootFlow alone, which is the
        // one caller of SetLocale — Boot_OnlyBootFlowSetsTheLocale keeps it that way.
        var tables = new List<LocalizationTable>(1 + (languages?.Count ?? 0)) { localization };

        if (languages is not null)
        {
            tables.AddRange(languages);
        }

        builder.RegisterInstance(new TableLocalizer(tables, LocaleOf(Application.systemLanguage)))
            .As<ILocalizer>()
            .AsSelf();
    }

    /// <summary>
    /// The BCP-47 tag for a language Unity reports, or empty when it cannot say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A table rather than <c>CultureInfo.CurrentUICulture</c></b>, because Unity does not set
    /// the managed culture from the device on Android: <see cref="Application.systemLanguage"/> is
    /// the one reading the engine promises on every platform. The tags are ISO 639-1, plus the two
    /// scripts Chinese is written in, which is what a translator's file would be named.
    /// </para>
    /// <para>
    /// <b>Every member is mapped, and a member that is not reads as empty</b> — the fallback. So
    /// a device whose language Unity adds next year reads English rather than failing at boot.
    /// <c>Hugarian</c> is not spelled out: it is Unity's obsolete alias with <c>Hungarian</c>'s
    /// value, so one arm answers both.
    /// </para>
    /// </remarks>
    public static string LocaleOf(SystemLanguage language) => language switch
    {
        SystemLanguage.Afrikaans => "af",
        SystemLanguage.Arabic => "ar",
        SystemLanguage.Basque => "eu",
        SystemLanguage.Belarusian => "be",
        SystemLanguage.Bulgarian => "bg",
        SystemLanguage.Catalan => "ca",
        SystemLanguage.Chinese => "zh",
        SystemLanguage.Czech => "cs",
        SystemLanguage.Danish => "da",
        SystemLanguage.Dutch => "nl",
        SystemLanguage.English => "en",
        SystemLanguage.Estonian => "et",
        SystemLanguage.Faroese => "fo",
        SystemLanguage.Finnish => "fi",
        SystemLanguage.French => "fr",
        SystemLanguage.German => "de",
        SystemLanguage.Greek => "el",
        SystemLanguage.Hebrew => "he",
        SystemLanguage.Hungarian => "hu",
        SystemLanguage.Icelandic => "is",
        SystemLanguage.Indonesian => "id",
        SystemLanguage.Italian => "it",
        SystemLanguage.Japanese => "ja",
        SystemLanguage.Korean => "ko",
        SystemLanguage.Latvian => "lv",
        SystemLanguage.Lithuanian => "lt",
        SystemLanguage.Norwegian => "no",
        SystemLanguage.Polish => "pl",
        SystemLanguage.Portuguese => "pt",
        SystemLanguage.Romanian => "ro",
        SystemLanguage.Russian => "ru",
        SystemLanguage.SerboCroatian => "sh",
        SystemLanguage.Slovak => "sk",
        SystemLanguage.Slovenian => "sl",
        SystemLanguage.Spanish => "es",
        SystemLanguage.Swedish => "sv",
        SystemLanguage.Thai => "th",
        SystemLanguage.Turkish => "tr",
        SystemLanguage.Ukrainian => "uk",
        SystemLanguage.Vietnamese => "vi",
        SystemLanguage.ChineseSimplified => "zh-Hans",
        SystemLanguage.ChineseTraditional => "zh-Hant",
        SystemLanguage.Hindi => "hi",
        _ => string.Empty,
    };

    /// <summary>
    /// Builds the archetype → tint-and-scale index the arena draws with (M2-06).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keyed by the <em>spec's</em> id rather than by <c>EnemyDefinition.Id</c>'s raw text, and the
    /// two are only the same string once <c>ToSpec</c> has returned: the raw field is not known to
    /// be a well-formed <see cref="ContentId"/>, which is exactly what that property's own summary
    /// warns about. Reading it here instead would parse every id a second time and get a different
    /// exception for a malformed one.
    /// </para>
    /// <para>
    /// The two arrays are index-parallel by construction — <see cref="Convert"/> walks the
    /// definitions in order and never skips one — which is what lets a spec's id and a definition's
    /// colour be paired without a second lookup.
    /// </para>
    /// </remarks>
    private static EnemyLookBook BuildLookBook(
        IReadOnlyList<EnemyDefinition> definitions,
        EnemySpec[] specs)
    {
        var looks = new Dictionary<ContentId, EnemyLook>(specs.Length);

        for (int i = 0; i < specs.Length; i++)
        {
            looks[specs[i].Id] = definitions[i].ToLook();
        }

        return new EnemyLookBook(looks);
    }

    /// <summary>
    /// Converts one kind's authored assets into the specs core consumes, refusing an empty slot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One method for every kind, taking the conversion as a delegate — the same trade
    /// <see cref="ContentCatalog"/>'s own indexer makes, for the same reason: it runs once per
    /// kind at boot, so the delegate costs nothing that matters, and the third kind inherits the
    /// guard and the message instead of a third copy of this loop.
    /// </para>
    /// <para>
    /// The null check casts to <see cref="UnityEngine.Object"/> first, and that cast is
    /// load-bearing rather than decorative. C# resolves <c>==</c> on a type parameter as
    /// reference equality — a user-defined operator on the constraint's base type is not
    /// considered — so <c>definition == null</c> inside a generic method would compile, read
    /// exactly like the non-generic version it replaced, and quietly stop catching a destroyed
    /// asset, which is a live reference that only Unity's operator calls null.
    /// </para>
    /// </remarks>
    private static TSpec[] Convert<TDefinition, TSpec>(
        IReadOnlyList<TDefinition> definitions,
        Func<TDefinition, TSpec> toSpec,
        string kind,
        string paramName)
        where TDefinition : ScriptableObject
    {
        var specs = new TSpec[definitions.Count];

        for (int i = 0; i < definitions.Count; i++)
        {
            TDefinition definition = definitions[i];

            if ((UnityEngine.Object)definition == null)
            {
                throw new ArgumentException(
                    $"{paramName}[{i}] is an empty slot. Every {kind} in the boot list must " +
                    $"reference a {typeof(TDefinition).Name} asset.",
                    paramName);
            }

            // Each failure already names its asset (M0-11), so nothing is caught or rewrapped
            // here — a second layer of message would bury the file name that matters.
            specs[i] = toSpec(definition);
        }

        return specs;
    }
}
