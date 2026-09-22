using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script
// importer parses a file to find the type it declares and does not understand `namespace X;`,
// so a ScriptableObject declared that way is never linked to a MonoScript: Descent.asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// One mode as a designer tunes it: the Inspector half of <see cref="ModeSpec"/>. Converted
    /// once at boot into the immutable spec core consumes and registered in the
    /// <c>ContentCatalog</c>. See AR §10.1 and ADR-0006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third definition type in the project, and deliberately the same shape as the first
    /// two: <c>[SerializeField] private</c> fields, one <see cref="ToSpec"/> that is the only way
    /// out, an <see cref="OnValidate"/> that checks the id, and every failure rewrapped as a plain
    /// <see cref="ArgumentException"/> naming the asset. A designer who has learned to read one
    /// Console message has learned to read all of them.
    /// </para>
    /// <para>
    /// <b>It validates nothing <see cref="ModeSpec"/> already validates.</b> The roster's
    /// one-introduction-per-stage rule, the duplicate check, the stage bounds — all of them live
    /// in the spec's constructor, which is the single account of what a legal mode is. A second
    /// copy here would be a second set of messages to keep in step, and the one that fired first
    /// would be the one nobody had updated.
    /// </para>
    /// <para>
    /// <b>Descent ships with the Husk alone.</b> GD §8.2's schedule is Husk 1, Spitter 2,
    /// Bloater 4 and that is what the mode's roster will say — but <c>RunSession.Start</c>
    /// resolves every roster id against the catalog before a run is announced, so a row naming an
    /// archetype nobody has authored yet would refuse to start the game. M2-06 adds the other two
    /// rows in the same change that authors them (M2-02 rule 10).
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Content/Mode", fileName = "Mode")]
    public sealed class ModeDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "mode.new";
        [SerializeField] private string _nameKey = "mode.new.name";

        [Tooltip("The depth a fresh run of this mode begins at. Stages are numbered from 1.")]
        [SerializeField, Min(1)] private int _startingStage = 1;

        [Tooltip("Whether the mode runs until the player dies. True for Descent (GD §4.5); the " +
                 "final stage below is ignored when this is on.")]
        [SerializeField] private bool _isEndless = true;

        [Tooltip("The last stage a finite mode has. Ignored while Is Endless is on.")]
        [SerializeField, Min(1)] private int _finalStage = 1;

        [Tooltip("GD §12's difficulty curves for this mode: what a stage may cost, how it is " +
                 "paced, and how much depth adds to each enemy.")]
        [SerializeField] private ScalingBlock _scaling = new ScalingBlock();

        [Tooltip("CH §5.2's levelling curve for this mode: what each level costs. How fast a " +
                 "mode levels you is the mode's own statement (GD §4.5) — a Boss Rush would " +
                 "level differently, or not at all.")]
        [SerializeField] private XpBlock _xp = new XpBlock();

        [Tooltip("CH §5.2's Overflow for this mode: what a level is worth once the tree is full " +
                 "and there is nothing left to buy. The same sentence as the curve above, " +
                 "finished — so it lives beside it (GD §4.5).")]
        [SerializeField] private OverflowBlock _overflow = new OverflowBlock();

        [Tooltip("GD §15's income for this mode: what a stage clear, an Elite and a boss pay in " +
                 "Essence. What a mode pays is the mode's own statement (GD §4.5) — a Boss Rush " +
                 "would pay per boss and nothing per stage.")]
        [SerializeField] private EssenceBlock _essence = new EssenceBlock();

        [Tooltip("GD §13.3's Sanctum for this mode: what its four services cost, and what Heal and " +
                 "Cleanse are worth. The other half of the income block above — one says what a " +
                 "run is paid, this what it can buy.")]
        [SerializeField] private SanctumBlock _sanctum = new SanctumBlock();

        [Tooltip("Every archetype this mode may spawn, and the stage each is introduced at " +
                 "(GD §8.2). At most one introduction per stage, and each archetype once.")]
        [SerializeField] private RosterRow[] _roster = Array.Empty<RosterRow>();

        [Tooltip("The arenas this mode's stages are fought in (GD §7.2's pool of 8–12 per biome, " +
                 "two in V1). Which one a stage uses is derived from the run's seed and the " +
                 "depth — never drawn — so a resumed run lands in the room it left. Empty leaves " +
                 "every stage in whatever the scene was dressed with.")]
        [SerializeField] private string[] _arenas = Array.Empty<string>();

        [Tooltip("Which stages this mode holds a boss on, and which boss (GD §9). 'Every N " +
                 "Stages' = 5 means stages 5, 10, 15 and so on. The first row that matches wins, " +
                 "so a rarer boss goes above a more frequent one. Empty means the mode never " +
                 "reaches a boss stage.")]
        [SerializeField] private BossRosterRow[] _bossRoster = Array.Empty<BossRosterRow>();

        /// <summary>
        /// The authored id text, exactly as it sits in the asset — for grouping and diagnostics
        /// before conversion. It is <em>not</em> known to be well-formed: only a
        /// <see cref="ToSpec"/> that returned tells you that.
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// Builds the immutable spec core consumes. A fresh instance every call — this asset
        /// holds no runtime state and hands out nothing it keeps a reference to.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Any authored field is invalid. Always this exact type, never one of its subclasses:
        /// the caller cannot act on <em>which</em> field failed, only on <em>which asset</em>
        /// failed, and that is what the message leads with. The original is kept as the inner
        /// exception, so the field and its value survive into the log.
        /// </exception>
        public ModeSpec ToSpec()
        {
            try
            {
                return new ModeSpec(
                    new ContentId(_id),
                    new LocKey(_nameKey),
                    _startingStage,
                    _isEndless,
                    _finalStage,
                    BuildScaling(),
                    BuildXp(),
                    BuildRoster(),
                    BuildArenas(),
                    BuildBossRoster(),
                    BuildOverflow(),
                    BuildEssence(),
                    BuildSanctum());
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching
                // here. Uncaught, a designer reading the Console sees "introduces two archetypes
                // at stage 2" with a stack trace through the boot installer and no way to tell
                // which of the catalog's assets to open.
                throw new ArgumentException(
                    $"ModeDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }

        /// <summary>
        /// Turns the authored curve block into the <see cref="ScalingSpec"/> core consumes.
        /// </summary>
        /// <remarks>
        /// A missing block is refused rather than defaulted, unlike a missing roster. Unity never
        /// serialises a null nested object, so the only ways here are a hand-edited asset or a
        /// <c>SerializedProperty</c> write — and a mode with no difficulty model is not an empty
        /// mode, it is one whose stages cost nothing and whose enemies never get harder. The
        /// message says which asset, like every other failure this type produces.
        /// </remarks>
        private ScalingSpec BuildScaling()
        {
            if (_scaling is null)
            {
                throw new ArgumentException(
                    "its scaling block is missing. GD §12's curves are not optional — a mode "
                        + "without them affords nothing at every depth.",
                    nameof(_scaling));
            }

            return _scaling.ToSpec();
        }

        /// <summary>
        /// Turns the authored levelling block into the <see cref="XpCurve"/> core consumes.
        /// </summary>
        /// <remarks>
        /// A missing block is refused rather than defaulted, for <see cref="BuildScaling"/>'s
        /// reason and with a sharper consequence: a <c>default(XpCurve)</c> costs nothing per
        /// level, and a run on one would level without end on its first kill.
        /// </remarks>
        private XpCurve BuildXp()
        {
            if (_xp is null)
            {
                throw new ArgumentException(
                    "its xp block is missing. CH §5.2's curve is not optional — a mode without "
                        + "one charges nothing per level.",
                    nameof(_xp));
            }

            return _xp.ToCurve();
        }

        /// <summary>
        /// Turns the authored Overflow block into the <see cref="OverflowSpec"/> core consumes.
        /// </summary>
        /// <remarks>
        /// A missing block is refused rather than defaulted, for <see cref="BuildScaling"/>'s
        /// reason — and the consequence here is the quietest of the three, which is why it is
        /// refused rather than shrugged at: a mode with no Overflow block would level a player past
        /// a full tree and give them nothing, with no error and nothing on screen to say the grant
        /// had stopped meaning anything. A block that is <em>present</em> and says zero is a
        /// different statement and is legal (<see cref="OverflowSpec"/>).
        /// </remarks>
        private OverflowSpec BuildOverflow()
        {
            if (_overflow is null)
            {
                throw new ArgumentException(
                    "its overflow block is missing. CH §5.2's Overflow is not optional — a mode "
                        + "without one pays nothing for a level with nothing left to buy, and says "
                        + "nothing about it.",
                    nameof(_overflow));
            }

            return _overflow.ToSpec();
        }

        /// <summary>
        /// Turns the authored income block into the <see cref="EssenceSpec"/> core consumes.
        /// </summary>
        /// <remarks>
        /// A missing block is refused rather than defaulted, for <see cref="BuildOverflow"/>'s
        /// reason and with its consequence: a mode with no Essence block would clear stage after
        /// stage and pay nothing, with no error and nothing on screen to say the economy had
        /// stopped meaning anything. A block that is <em>present</em> and says zero is a different
        /// statement and is legal (<see cref="EssenceSpec"/>) — what stops a <em>shipped</em> mode
        /// making it is <c>ContentValidationTests.EveryShippedMode_PricesItsEssence</c>.
        /// </remarks>
        private EssenceSpec BuildEssence()
        {
            if (_essence is null)
            {
                throw new ArgumentException(
                    "its essence block is missing. GD §15's income is not optional — a mode "
                        + "without one clears every stage and pays nothing, and says nothing about "
                        + "it.",
                    nameof(_essence));
            }

            return _essence.ToSpec();
        }

        /// <summary>
        /// Turns the authored shop block into the <see cref="SanctumSpec"/> core consumes.
        /// </summary>
        /// <remarks>
        /// A missing block is refused rather than defaulted, for <see cref="BuildEssence"/>'s reason
        /// and with a louder consequence: <c>default(SanctumSpec)</c> is a shop that gives
        /// everything away, and heals and cleanses for nothing.
        /// </remarks>
        private SanctumSpec BuildSanctum()
        {
            if (_sanctum is null)
            {
                throw new ArgumentException(
                    "its sanctum block is missing. GD §13.3's prices are not optional — a mode "
                        + "without them sells every service for nothing.",
                    nameof(_sanctum));
            }

            return _sanctum.ToSpec();
        }

        /// <summary>
        /// Turns the authored rows into <see cref="RosterEntry"/>s, in the order they were
        /// authored.
        /// </summary>
        /// <remarks>
        /// A null array is treated as an empty one. Unity never serialises one, but a freshly
        /// <c>CreateInstance</c>d asset in a test is a real path here, and an empty roster is a
        /// legal mode (see <see cref="ModeSpec"/>) — so this is not somewhere to invent a
        /// failure the spec does not have.
        /// </remarks>
        private IReadOnlyList<RosterEntry> BuildRoster()
        {
            if (_roster is null || _roster.Length == 0)
            {
                return Array.Empty<RosterEntry>();
            }

            var entries = new RosterEntry[_roster.Length];

            for (int i = 0; i < _roster.Length; i++)
            {
                entries[i] = _roster[i].ToEntry();
            }

            return entries;
        }

        /// <summary>
        /// Turns the authored arena ids into <see cref="ContentId"/>s, in the order authored.
        /// </summary>
        /// <remarks>
        /// A null or empty array is a legal mode, for <see cref="BuildRoster"/>'s reason: a mode
        /// with no arena roster leaves every stage in whatever the scene was dressed with, which is
        /// what every M0 and M1 grey box was. <see cref="ContentId"/>'s constructor refuses a
        /// malformed id and <see cref="ModeSpec"/> refuses a duplicate — neither is repeated here.
        /// </remarks>
        private IReadOnlyList<ContentId> BuildArenas()
        {
            if (_arenas is null || _arenas.Length == 0)
            {
                return Array.Empty<ContentId>();
            }

            var ids = new ContentId[_arenas.Length];

            for (int i = 0; i < _arenas.Length; i++)
            {
                ids[i] = new ContentId(_arenas[i]);
            }

            return ids;
        }

        /// <summary>
        /// Turns the authored boss rows into <see cref="BossRosterEntry"/>s, in the order authored.
        /// </summary>
        /// <remarks>
        /// A null or empty array is a legal mode, for <see cref="BuildRoster"/>'s reason, and is
        /// what every mode this build ships is: <b>M4-01b builds the schedule and authors no rows
        /// into it</b>, because <c>RunSession.Start</c> would refuse a run naming a boss nobody has
        /// authored yet. M4-02 adds Descent's row in the same change that authors the Warden — the
        /// bargain M2-02 rule 10 made for the enemy roster, made again.
        /// </remarks>
        private IReadOnlyList<BossRosterEntry> BuildBossRoster()
        {
            if (_bossRoster is null || _bossRoster.Length == 0)
            {
                return Array.Empty<BossRosterEntry>();
            }

            var entries = new BossRosterEntry[_bossRoster.Length];

            for (int i = 0; i < _bossRoster.Length; i++)
            {
                entries[i] = _bossRoster[i].ToEntry();
            }

            return entries;
        }

        /// <remarks>
        /// Only the id, and only its shape — the same bargain <c>CharacterDefinition</c> and
        /// <c>EnemyDefinition</c> make. A bad stage number is visibly a bad stage number in the
        /// Inspector, while <c>Mode.Descent</c> looks perfectly reasonable and fails at boot. The
        /// asset is passed as the log context so clicking the warning selects it.
        /// </remarks>
        private void OnValidate()
        {
            if (!ContentId.IsValid(_id))
            {
                Debug.LogWarning(
                    $"ModeDefinition '{name}': '{_id}' is not a valid content id. Expected " +
                    "lowercase dot-separated segments, at least two, e.g. 'mode.descent'.",
                    this);
            }
        }

        /// <summary>
        /// GD §12's five curves as a designer tunes them. The Inspector half of
        /// <see cref="ScalingSpec"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A <c>[Serializable]</c> <b>class</b> rather than a struct, unlike
        /// <see cref="RosterRow"/>: it carries field initialisers, and a struct's are not
        /// something Unity's serialiser can be relied on to have run. It also draws as one
        /// foldout in the Inspector, which is what stops twenty-one loose numbers from filling a
        /// mode's whole inspector above its roster.
        /// </para>
        /// <para>
        /// <b>The initialisers are GD §12's own numbers</b>, so a mode created from the Create
        /// menu is playable rather than invalid. That does mean a test asserting "the asset carries
        /// these values" cannot tell a bound YAML key from a dropped one — Traps §7 — which is
        /// exactly what <c>ModeDefinitionTests.Descent_EveryYamlKeyBindsToAField</c> exists to
        /// cover, and it covers this block along with everything else.
        /// </para>
        /// <para>
        /// It validates nothing the curves already validate. Every bound is in
        /// <see cref="ScalingSpec"/>'s constructors, which are the single account of what a legal
        /// curve is; <c>[Min]</c> here only clamps the Inspector GUI (Traps §5).
        /// </para>
        /// </remarks>
        [Serializable]
        private sealed class ScalingBlock
        {
            [Header("Threat budget — GD §12.1: B(n) = base + linear·(n−1) + quadratic·(n−1)²")]
            [Tooltip("The stage-1 budget.")]
            [SerializeField, Min(0.01f)] private float _budgetBase = 40f;

            [Tooltip("Added per stage past the first.")]
            [SerializeField, Min(0f)] private float _budgetLinear = 12f;

            [Tooltip("Added per stage-past-the-first squared. This is what makes deep stages bite.")]
            [SerializeField, Min(0f)] private float _budgetQuadratic = 0.9f;

            [Header("Pacing — GD §12.2")]
            [Tooltip("Waves at stage 0, before any step.")]
            [SerializeField, Min(1)] private int _waveBase = 2;

            [Tooltip("Stages per extra wave.")]
            [SerializeField, Min(1)] private int _waveStagesPerStep = 5;

            [Tooltip("Fewest waves a stage may have.")]
            [SerializeField, Min(1)] private int _waveMin = 2;

            [Tooltip("Most waves a stage may have.")]
            [SerializeField, Min(1)] private int _waveMax = 5;

            [Tooltip("Concurrent enemies at stage 0, before any step.")]
            [SerializeField, Min(1)] private int _concurrencyBase = 10;

            [Tooltip("Stages per extra concurrent enemy. The device cap is not authored here — " +
                     "it is a property of the phone (GD §11.1), chosen in code.")]
            [SerializeField, Min(1)] private int _concurrencyStagesPerStep = 2;

            [Header("Stat scaling — GD §12.3, deliberately shallow")]
            [Tooltip("h(n): the hit-point multiplier. GD ships +0.06 a stage, capped at 4.0.")]
            [SerializeField] private StatCurveRow _hp = new StatCurveRow(0.06f, 4f, 1, 1);

            [Tooltip("d(n): the damage multiplier. GD ships +0.035 a stage, capped at 3.0. " +
                     "GD §12.4's one-shot rule is checked against this at M2-15.")]
            [SerializeField] private StatCurveRow _damage = new StatCurveRow(0.035f, 3f, 1, 1);

            [Tooltip("s(n): the move-speed multiplier. GD ships +0.02 every fifth stage, capped " +
                     "at 1.3 — a staircase rather than a ramp, and the step is meant to be felt.")]
            [SerializeField] private StatCurveRow _speed = new StatCurveRow(0.02f, 1.3f, 5, 0);

            /// <summary>Builds the immutable spec, letting the curves refuse a bad number.</summary>
            public ScalingSpec ToSpec() => new ScalingSpec(
                new BudgetCurve(_budgetBase, _budgetLinear, _budgetQuadratic),
                new WaveCurve(_waveBase, _waveStagesPerStep, _waveMin, _waveMax),
                new ConcurrencyCurve(_concurrencyBase, _concurrencyStagesPerStep),
                _hp.ToCurve(),
                _damage.ToCurve(),
                _speed.ToCurve());
        }

        /// <summary>
        /// CH §5.2's levelling curve as a designer tunes it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A <c>[Serializable]</c> class for <see cref="ScalingBlock"/>'s reason — it renders as
        /// one foldout, so three more numbers do not push the roster further down a mode's
        /// inspector — and the initialisers are CH §5.2's own, so a mode created from the Create
        /// menu levels rather than being invalid content.
        /// </para>
        /// <para>
        /// <b>20 / 12 / 1.4 disagrees with CH §5.2's own table past stage 10 and ships anyway.</b>
        /// The tree fills around stage 20 where the table says 30; the owner ruled at M3-00a that
        /// the exponent stays until M3-15 has measured a real run, and
        /// <c>XpCurveTests.Pacing_TreeFullByStageTwenty</c> pins the divergence so a retune shows
        /// up as a diff. Changing the number here is therefore a deliberate act with a test to
        /// update, not a tuning pass.
        /// </para>
        /// <para>
        /// It validates nothing <see cref="XpCurve"/> already validates; <c>[Min]</c> clamps the
        /// Inspector GUI and nothing else (Traps §5).
        /// </para>
        /// </remarks>
        [Serializable]
        private sealed class XpBlock
        {
            [Header("Levelling — CH §5.2: ToReach(N) = base + perLevel · N^exponent")]
            [Tooltip("Added to every level's cost. 20 in CH §5.2.")]
            [SerializeField, Min(0f)] private float _base = 20f;

            [Tooltip("Multiplies the exponentiated level. 12 in CH §5.2.")]
            [SerializeField, Min(0.01f)] private float _perLevel = 12f;

            [Tooltip("How sharply the cost climbs. 1.4 in CH §5.2 — above 1, so each level " +
                     "costs more than the last by more than a constant.")]
            [SerializeField, Min(0.01f)] private float _exponent = 1.4f;

            /// <summary>Builds the immutable curve, letting it refuse a bad number.</summary>
            public XpCurve ToCurve() => new XpCurve(_base, _perLevel, _exponent);
        }

        /// <summary>
        /// CH §5.2's Overflow as a designer tunes it — what a level is worth when the tree is full.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A <c>[Serializable]</c> class for <see cref="XpBlock"/>'s reason, and it draws as the
        /// foldout beside it: one says what a level costs, the other what a spare one buys.
        /// </para>
        /// <para>
        /// <b>The initialisers are the two numbers that shipped as <c>const</c>s from M3-08a to
        /// M5-06b</b>, so a mode created from the Create menu pays for a full tree rather than being
        /// silently worth nothing — <see cref="ScalingBlock"/>'s bargain, third of three. Traps §7
        /// applies exactly as it does there: <c>Descent.asset</c> ships the same 0.02 and 0.02, so
        /// <c>ModeDefinitionTests.Descent_EveryYamlKeyBindsToAField</c> is the row that can tell a
        /// bound key from a dropped one, and the row that asserts the values cannot.
        /// </para>
        /// <para>
        /// It validates nothing <see cref="OverflowSpec"/> already validates; <c>[Min]</c> clamps
        /// the Inspector GUI and nothing else (Traps §5), which is why a hand-edited negative still
        /// meets a door at conversion.
        /// </para>
        /// </remarks>
        [Serializable]
        private sealed class OverflowBlock
        {
            [Header("Overflow — CH §5.2: a level with nothing left to buy")]
            [Tooltip("What one Overflow level adds to weapon damage, as a fraction pooled with " +
                     "every other percentage — 0.02 is +2 %, and ten levels are ×1.20 rather " +
                     "than 1.02^10 (ADR-0008).")]
            [SerializeField, Min(0f)] private float _damage = 0.02f;

            [Tooltip("What one Overflow level adds to maximum hit points, on the same terms. " +
                     "Raising the ceiling is deliberately not a heal.")]
            [SerializeField, Min(0f)] private float _maxHp = 0.02f;

            /// <summary>Builds the immutable spec, letting it refuse a bad number.</summary>
            public OverflowSpec ToSpec() => new OverflowSpec(_damage, _maxHp);
        }

        /// <summary>
        /// GD §15's income table as a designer tunes it — what a run is paid for getting through
        /// things.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A <c>[Serializable]</c> class for <see cref="OverflowBlock"/>'s reason, and it draws as
        /// the foldout beside it: one says what a level is worth, the other what a stage is.
        /// </para>
        /// <para>
        /// <b>The initialisers are GD §15's own numbers</b>, so a mode created from the Create menu
        /// pays for its stages rather than being silently worth nothing —
        /// <see cref="ScalingBlock"/>'s bargain, fourth of four. Traps §7 applies exactly as it does
        /// there: <c>Descent.asset</c> ships the same 20 / 4 / 15 / 60, so
        /// <c>ModeDefinitionTests.Descent_EveryYamlKeyBindsToAField</c> is the row that can tell a
        /// bound key from a dropped one and <c>Descent_CarriesItsEssence</c> cannot.
        /// </para>
        /// <para>
        /// <b>Per Elite is authored with no payer and that is deliberate</b> (M6-01a rule 2):
        /// Elites are M7-02's, and a blank here would read as <em>"Elites pay nothing"</em> rather
        /// than as <em>"nothing is an Elite yet"</em>.
        /// </para>
        /// <para>
        /// It validates nothing <see cref="EssenceSpec"/> already validates; <c>[Min]</c> clamps
        /// the Inspector GUI and nothing else (Traps §5), which is why a hand-edited negative still
        /// meets a door at conversion.
        /// </para>
        /// </remarks>
        [Serializable]
        private sealed class EssenceBlock
        {
            [Header("Essence — GD §15: a stage clear pays base + depth·n, a boss pays more")]
            [Tooltip("The flat half of a stage clear. 20 in GD §15.")]
            [SerializeField, Min(0)] private int _perStageBase = 20;

            [Tooltip("What each stage of depth adds to it. 4 in GD §15 — so stage 1 pays 24 and " +
                     "stage 10 pays 60, and the step is meant to be visible as a run gets deeper.")]
            [SerializeField, Min(0)] private int _perStageDepth = 4;

            [Tooltip("What one Elite is worth. 15 in GD §15. Nothing pays it until M7-02 authors " +
                     "an Elite — the number is here so the table is complete, not because it is " +
                     "reachable.")]
            [SerializeField, Min(0)] private int _perElite = 15;

            [Tooltip("What clearing a boss stage adds on top of the stage itself. 60 in GD §15 — " +
                     "on top, because a boss stage is a stage clear.")]
            [SerializeField, Min(0)] private int _perBoss = 60;

            /// <summary>Builds the immutable spec, letting it refuse a bad number.</summary>
            public EssenceSpec ToSpec() =>
                new EssenceSpec(_perStageBase, _perStageDepth, _perElite, _perBoss);
        }

        /// <summary>
        /// GD §13.3's shop as a designer tunes it — four prices and two magnitudes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A <c>[Serializable]</c> class for <see cref="EssenceBlock"/>'s reason, drawn as the
        /// foldout beside it. <b>The initialisers are GD §13.3's own numbers</b> — the same bargain
        /// and the same Traps §7 caveat: <c>Descent.asset</c> ships 25 / 40 / 40 / 30 / 60 / 15 too,
        /// so <c>Descent_EveryYamlKeyBindsToAField</c> is the row that can tell a bound key from a
        /// dropped one.
        /// </para>
        /// <para>
        /// The reroll's doubling is not a field (M6-02b rule 2): it is the shape of the economy, and
        /// lives as <c>SanctumShop.RerollDoubling</c>. It validates nothing <see cref="SanctumSpec"/>
        /// already validates; <c>[Min]</c> clamps the Inspector GUI and nothing else (Traps §5).
        /// </para>
        /// </remarks>
        [Serializable]
        private sealed class SanctumBlock
        {
            [Header("Sanctum — GD §13.3: four services between stages")]
            [Tooltip("The first reroll's price. 25 in GD §13.3, and it doubles with every reroll " +
                     "bought — the doubling is a rule, not a field.")]
            [SerializeField, Min(0)] private int _rerollPrice = 25;

            [Tooltip("What taking one untaken node out of this run's offers costs. 40 in GD §13.3.")]
            [SerializeField, Min(0)] private int _banishPrice = 40;

            [Tooltip("What a heal costs. 40 in GD §13.3.")]
            [SerializeField, Min(0)] private int _healPrice = 40;

            [Tooltip("Hit points a heal restores. 30 in GD §13.3. Never overfills the bar.")]
            [SerializeField, Min(0f)] private float _healAmount = 30f;

            [Tooltip("What a cleanse costs. 60 in GD §13.3.")]
            [SerializeField, Min(0)] private int _cleansePrice = 60;

            [Tooltip("Veilrot a cleanse removes. 15 in GD §13.3. Clamps at zero, and never ends " +
                     "the Claiming.")]
            [SerializeField, Min(0f)] private float _cleanseAmount = 15f;

            /// <summary>Builds the immutable spec, letting it refuse a bad number.</summary>
            public SanctumSpec ToSpec() => new SanctumSpec(
                _rerollPrice, _banishPrice, _healPrice, _healAmount, _cleansePrice, _cleanseAmount);
        }

        /// <summary>
        /// One of GD §12.3's three stat multipliers as a designer tunes it.
        /// </summary>
        /// <remarks>
        /// A <c>[Serializable]</c> class for <see cref="ScalingBlock"/>'s reason — the four
        /// numbers have initialisers, which differ per curve — and one type for all three because
        /// GD §12.3's formulas are two spellings of one arithmetic, which is the argument
        /// <see cref="StatCurve"/> makes at length.
        /// </remarks>
        [Serializable]
        private sealed class StatCurveRow
        {
            [Tooltip("Added to the multiplier per step. Zero means depth does not touch this stat.")]
            [SerializeField, Min(0f)] private float _perStep;

            [Tooltip("The multiplier's ceiling. At least 1 — below it, deep enemies get weaker.")]
            [SerializeField, Min(1f)] private float _cap = 1f;

            [Tooltip("Stages per step. 1 for a ramp, 5 for GD §12.3's speed staircase.")]
            [SerializeField, Min(1)] private int _stageStep = 1;

            [Tooltip("1 for a curve that starts at stage 1 (HP, damage); 0 for one that steps on " +
                     "the stage number itself (speed). Nothing else is legal.")]
            [SerializeField, Range(0, 1)] private int _stageOffset = 1;

            /// <remarks>
            /// The parameterless constructor Unity's serialiser needs is not written out: a
            /// constructor with arguments removes it, so the four defaults above are what a
            /// deserialised row starts from and this one exists purely so the field initialisers
            /// in <see cref="ScalingBlock"/> can state GD §12.3's three shapes in one line each.
            /// </remarks>
            public StatCurveRow()
            {
            }

            public StatCurveRow(float perStep, float cap, int stageStep, int stageOffset)
            {
                _perStep = perStep;
                _cap = cap;
                _stageStep = stageStep;
                _stageOffset = stageOffset;
            }

            /// <summary>Converts this row, letting <see cref="StatCurve"/> refuse a bad one.</summary>
            public StatCurve ToCurve() => new StatCurve(_perStep, _cap, _stageStep, _stageOffset);
        }

        /// <summary>
        /// One authored roster row: an archetype id and the stage it is introduced at.
        /// </summary>
        /// <remarks>
        /// A serializable struct rather than two parallel arrays, so a designer editing the
        /// schedule sees the id and its stage side by side and cannot get them out of step. It
        /// carries the id as a <see cref="string"/> because <see cref="ContentId"/> validates in
        /// its constructor and Unity's serialiser does not call one — the conversion, and the
        /// failure, belong in <see cref="ToEntry"/>.
        /// </remarks>
        [Serializable]
        private struct RosterRow
        {
            [SerializeField] private string _specId;

            [Tooltip("The first stage this mode may spawn it at. 1 = from the first stage.")]
            [SerializeField, Min(1)] private int _introducedAtStage;

            /// <summary>Converts this row, letting <see cref="RosterEntry"/> refuse a bad one.</summary>
            public RosterEntry ToEntry() =>
                new RosterEntry(new ContentId(_specId), _introducedAtStage);
        }

        /// <summary>
        /// One authored boss row: a boss id and how often it comes round.
        /// </summary>
        /// <remarks>
        /// <see cref="RosterRow"/>'s shape exactly, and for its reasons — a serializable struct so
        /// the two numbers cannot get out of step, carrying the id as a <see cref="string"/>
        /// because <see cref="ContentId"/> validates in a constructor Unity's serialiser never
        /// calls.
        /// </remarks>
        [Serializable]
        private struct BossRosterRow
        {
            [SerializeField] private string _bossId;

            [Tooltip("How often this boss comes round. 5 = stages 5, 10, 15, …")]
            [SerializeField, Min(1)] private int _everyNStages;

            /// <summary>Converts this row, letting <see cref="BossRosterEntry"/> refuse a bad one.</summary>
            public BossRosterEntry ToEntry() =>
                new BossRosterEntry(new ContentId(_bossId), _everyNStages);
        }
    }
}
