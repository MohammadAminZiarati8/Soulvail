using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// M2-06's two halves: the look book that tells three archetypes apart on one shared body, and the
/// three shipped assets — pinned to GD §8.1, to GD §8.2's introduction schedule, and to GD §12.4's
/// one-shot rule, so a stray Inspector drag is a red test rather than a balance mystery forty
/// stages into a run.
/// </summary>
/// <remarks>
/// The asset rows are the same bargain <see cref="EnemyDefinitionTests"/> makes for the Husk and
/// exist for the same reason: the numbers here are the only place GD §8.1's table is written down
/// twice on purpose.
/// </remarks>
[TestFixture]
public sealed class EnemyLookTests
{
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private const string SpitterPath = "Assets/_Project/Data/Enemies/Spitter.asset";
    private const string BloaterPath = "Assets/_Project/Data/Enemies/Bloater.asset";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";

    private const float Tolerance = 1e-6f;

    /// <summary>
    /// The Oathbound's max HP (CC §7) and GD §12.4's share of it that one non-boss hit may take.
    /// </summary>
    private const float OathboundMaxHp = 140f;
    private const float OneShotShare = 0.35f;

    /// <summary>
    /// The ceiling GD §12.3's damage curve is capped at — <c>d(n)</c>'s <c>_cap</c> in
    /// <c>Descent.asset</c>. The number that turns a 35 % rule at depth into a rule about what a
    /// designer may type today.
    /// </summary>
    private const float DamageCapMultiplier = 3f;

    /// <summary>
    /// Everything a row created — bodies and loose definitions alike. One list and one teardown,
    /// because <c>DestroyImmediate</c> takes any <see cref="UnityEngine.Object"/> and a fixture
    /// that leaks a GameObject leaks it into every row after it.
    /// </summary>
    private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

    [TearDown]
    public void DestroyCreatedObjects()
    {
        foreach (UnityEngine.Object created in _created)
        {
            if (created != null)
            {
                UnityEngine.Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    // ---- The look and the book -----------------------------------------------------------------

    [Test]
    public void Look_RecordsTintAndScale()
    {
        var look = new EnemyLook(Color.red, 1.35f);

        Assert.That(look.Tint, Is.EqualTo(Color.red));
        Assert.That(look.BodyScale, Is.EqualTo(1.35f).Within(Tolerance));
    }

    [Test]
    public void Look_UnknownId_IsDefault()
    {
        var book = new EnemyLookBook(new Dictionary<ContentId, EnemyLook>());

        EnemyLook look = default;

        // No throw, on purpose: a missing colour is not worth ending a run over (rule 9), and the
        // archetype is still legible as *some enemy* in bone grey.
        Assert.That(() => look = book.For(new ContentId("enemy.ghost")), Throws.Nothing);

        Assert.That(look.Tint, Is.EqualTo(EnemyLook.Default.Tint));
        Assert.That(look.BodyScale, Is.EqualTo(1f).Within(Tolerance));

        // And default(ContentId), which is the shape a caller reaches by accident rather than on
        // purpose — an EnemySpawned built before its spec id was filled in.
        Assert.That(() => book.For(default), Throws.Nothing);
    }

    [Test]
    public void Look_Default_IsNotTheStructDefault()
    {
        // default(EnemyLook) is transparent black at scale zero — an invisible enemy — which is the
        // struct-with-an-invariant trap AR §18.3 names. The row exists because `For` returning the
        // dictionary's out-value on a miss would compile, read correctly, and produce exactly that.
        Assert.That(EnemyLook.Default.BodyScale, Is.Not.EqualTo(default(EnemyLook).BodyScale));
        Assert.That(EnemyLook.Default.Tint.a, Is.EqualTo(1f).Within(Tolerance),
            "An unauthored archetype must be opaque, not invisible.");
    }

    [Test]
    public void Look_DuplicateId_Throws()
    {
        // A Dictionary cannot hold the duplicate this rule is about, so the fixture is a list-backed
        // IReadOnlyDictionary — which is exactly the shape a hand-built boot list would have.
        var duplicate = new DuplicateLooks(
            new ContentId("enemy.husk"), new EnemyLook(Color.grey, 1f),
            new ContentId("enemy.husk"), new EnemyLook(Color.red, 2f));

        var thrown = Assert.Throws<ArgumentException>(() => new EnemyLookBook(duplicate));

        Assert.That(thrown.Message, Does.Contain("enemy.husk"),
            "The message must name the id, or it points at no asset anyone can open.");
    }

    [Test]
    public void Look_NullBook_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EnemyLookBook(null));
    }

    [Test]
    public void Look_DefaultContentIdKey_Throws()
    {
        var looks = new Dictionary<ContentId, EnemyLook> { [default] = EnemyLook.Default };

        // Refused where the book is built, not where it is read: a look under no id would sit there
        // for ever being asked for by nobody, which is a colour silently never applied.
        Assert.Throws<ArgumentException>(() => new EnemyLookBook(looks));
    }

    [Test]
    public void Look_BookCopiesItsSource()
    {
        var looks = new Dictionary<ContentId, EnemyLook>
        {
            [new ContentId("enemy.husk")] = new EnemyLook(Color.grey, 1f),
        };

        var book = new EnemyLookBook(looks);

        looks[new ContentId("enemy.husk")] = new EnemyLook(Color.magenta, 9f);

        Assert.That(book.For(new ContentId("enemy.husk")).BodyScale, Is.EqualTo(1f).Within(Tolerance),
            "The book must not be a window onto a dictionary somebody else can still write to.");
    }

    // ---- The conversion ------------------------------------------------------------------------

    [Test]
    public void Definition_ToSpec_CarriesBothBlocks()
    {
        EnemyDefinition definition = NewDefinition("BothBlocks");

        SetFloat(definition, "_projectileStandoffRange", 14f);
        SetFloat(definition, "_projectileSpeed", 12f);
        SetFloat(definition, "_projectileRadius", 1.6f);
        SetFloat(definition, "_explosionRadius", 3f);

        EnemySpec spec = definition.ToSpec();

        Assert.That(spec.Projectile, Is.Not.Null);
        Assert.That(spec.Projectile.StandoffRange, Is.EqualTo(14f).Within(Tolerance));
        Assert.That(spec.Projectile.Speed, Is.EqualTo(12f).Within(Tolerance));
        Assert.That(spec.Projectile.Radius, Is.EqualTo(1.6f).Within(Tolerance));

        Assert.That(spec.Explosion, Is.Not.Null);
        Assert.That(spec.Explosion.Radius, Is.EqualTo(3f).Within(Tolerance));
    }

    [Test]
    public void Definition_ToSpec_OmitsAnUnauthoredBlock()
    {
        // The defaults are a Husk (M0-11), which throws nothing and does not explode — so the
        // fresh instance is already the case this row is about.
        EnemyDefinition definition = NewDefinition("NeitherBlock");

        EnemySpec spec = definition.ToSpec();

        // Null, not a ProjectileSpec full of zeroes — which the constructor would refuse anyway,
        // and that is the point: zero is the switch, so an unauthored block cannot become a
        // nonsense one (rule 1).
        Assert.That(spec.Projectile, Is.Null);
        Assert.That(spec.Explosion, Is.Null);
    }

    [Test]
    public void Definition_ToLook_CarriesTintAndScale()
    {
        EnemyDefinition definition = NewDefinition("Tinted");

        SetColour(definition, "_tint", Color.red);
        SetFloat(definition, "_bodyScale", 1.35f);

        EnemyLook look = definition.ToLook();

        Assert.That(look.Tint, Is.EqualTo(Color.red));
        Assert.That(look.BodyScale, Is.EqualTo(1.35f).Within(Tolerance));
    }

    [Test]
    public void Husk_BlocksAreNull()
    {
        EnemySpec husk = Load(HuskPath).ToSpec();

        Assert.That(husk.Projectile, Is.Null, "A Husk throws nothing.");
        Assert.That(husk.Explosion, Is.Null, "A Husk does not explode.");
        Assert.That(husk.AggroRange, Is.EqualTo(30f).Within(Tolerance),
            "The 30 m ChaserBehaviour used to hold as a const, now authored (rule 4).");
    }

    [Test]
    public void Husk_TintIsTheSharedMaterialsOwn()
    {
        // Manual step 1's claim, asserted: the look system is a no-op for the archetype whose tint
        // is the prefab's own colour, so a Husk looks exactly as it did before M2-06. If
        // M_BoneGrey is ever recoloured, this is the row that says the Husk's asset has to follow.
        EnemyLook husk = Load(HuskPath).ToLook();

        Assert.That(husk.Tint, Is.EqualTo(EnemyLook.Default.Tint),
            "The Husk authors M_BoneGrey's own base colour, which is also what an unauthored " +
            "archetype falls back to.");
        Assert.That(husk.BodyScale, Is.EqualTo(1f).Within(Tolerance));
    }

    // ---- The shipped archetypes ----------------------------------------------------------------

    [Test]
    public void Spitter_MatchesDesign()
    {
        EnemySpec spitter = Load(SpitterPath).ToSpec();

        Assert.That(spitter.Id.Value, Is.EqualTo("enemy.spitter"));
        Assert.That(spitter.NameKey.Key, Is.EqualTo("enemy.spitter.name"));

        // GD §8.1's three published columns. 28 / 7 / 3 are far enough apart that no two of them
        // could satisfy each other's assertion, which is what makes a transposition visible.
        Assert.That(spitter.MaxHp, Is.EqualTo(28f).Within(Tolerance), "GD §8.1: base HP 28.");
        Assert.That(spitter.ThreatCost, Is.EqualTo(7), "GD §8.1: threat cost 7.");
        Assert.That(spitter.TargetPriority, Is.EqualTo(3), "GD §8.1: priority 3 — above a Bloater's 2.");

        Assert.That(spitter.Projectile, Is.Not.Null,
            "A Spitter carries its projectile block from M2-06, three tasks before anything fires one.");
        Assert.That(spitter.Projectile.StandoffRange, Is.EqualTo(14f).Within(Tolerance),
            "GD §8.1: it fires from 14 m — well outside the Oathbound's 3 m cone.");

        // The other two numbers of the block, pinned from M2-07b because that is the task that made
        // them reachable: SpitterBehaviour reads both off the asset on every release, and together
        // with the standoff they are the dodge window — 14 / 12 is 1.17 s against a 1.6 m blast.
        Assert.That(spitter.Projectile.Speed, Is.EqualTo(12f).Within(Tolerance),
            "M2-06: 12 m/s of flight, which is what makes 14 m a shade under 1.2 s to react in.");
        Assert.That(spitter.Projectile.Radius, Is.EqualTo(1.6f).Within(Tolerance),
            "M2-06: 1.6 m of forgiveness on an arriving bolt.");

        Assert.That(spitter.Explosion, Is.Null, "A Spitter does not explode.");

        // M2-06's invented numbers, which no design document owns — so this is the only thing that
        // cross-checks them. Windup and recover are the transposition risk, as they are on the Husk.
        Assert.That(spitter.MoveSpeed, Is.EqualTo(2.8f).Within(Tolerance));
        Assert.That(spitter.ContactDamage, Is.EqualTo(12f).Within(Tolerance));
        Assert.That(spitter.WindupTime, Is.EqualTo(0.7f).Within(Tolerance), "M2-06: windup 0.7 s, not recover.");
        Assert.That(spitter.RecoverTime, Is.EqualTo(0.9f).Within(Tolerance), "M2-06: recover 0.9 s, not windup.");
        Assert.That(spitter.AggroRange, Is.EqualTo(30f).Within(Tolerance));
    }

    [Test]
    public void Bloater_MatchesDesign()
    {
        EnemySpec bloater = Load(BloaterPath).ToSpec();

        Assert.That(bloater.Id.Value, Is.EqualTo("enemy.bloater"));
        Assert.That(bloater.NameKey.Key, Is.EqualTo("enemy.bloater.name"));

        Assert.That(bloater.MaxHp, Is.EqualTo(24f).Within(Tolerance), "GD §8.1: base HP 24.");
        Assert.That(bloater.ThreatCost, Is.EqualTo(8), "GD §8.1: threat cost 8 — dearer than a Spitter's 7.");
        Assert.That(bloater.TargetPriority, Is.EqualTo(2), "GD §8.1: priority 2.");

        Assert.That(bloater.Explosion, Is.Not.Null);
        Assert.That(bloater.Explosion.Radius, Is.EqualTo(3f).Within(Tolerance), "GD §8.1: a 3 m blast.");

        Assert.That(bloater.Projectile, Is.Null, "A Bloater throws nothing; it arrives.");

        Assert.That(bloater.MoveSpeed, Is.EqualTo(2.2f).Within(Tolerance), "M2-06: it waddles — slower than a Husk's 2.8 peers.");
        Assert.That(bloater.ContactDamage, Is.EqualTo(15f).Within(Tolerance));
        Assert.That(bloater.Reach, Is.EqualTo(2f).Within(Tolerance), "M2-06: where the fuse starts (M2-08).");
        Assert.That(bloater.WindupTime, Is.EqualTo(0.8f).Within(Tolerance), "M2-06: the fuse is 0.8 s.");
        Assert.That(bloater.RecoverTime, Is.EqualTo(0f).Within(Tolerance),
            "Nothing recovers from going off — zero is legal and means exactly that.");
        Assert.That(bloater.AggroRange, Is.EqualTo(30f).Within(Tolerance));
    }

    [Test]
    public void Assets_AuthoredStaticUntilTheirBehaviourExists()
    {
        // M2-06 rule 11, and the row that would fail the moment somebody flipped an asset ahead of
        // its PR: EnemySystem.Tick's dispatch throws on an unhandled kind, deliberately, so an
        // early-authored archetype takes the run down rather than standing there ignoring the
        // player. The Spitter's half of it was flipped by M2-07b — the PR that can run one — and
        // this is the assertion that moved with it rather than being deleted.
        Assert.That(Load(SpitterPath).ToSpec().Behaviour, Is.EqualTo(EnemyBehaviourKind.Spitter),
            "M2-07b: SpitterBehaviour exists and the dispatch ticks it, so the asset says so.");
        Assert.That(Load(BloaterPath).ToSpec().Behaviour, Is.EqualTo(EnemyBehaviourKind.Static),
            "Bloater.asset stays Static until M2-08 can run one.");
    }

    [Test]
    public void Assets_AreTellableApart()
    {
        // Rule 9's whole claim, in the only form a test can make it: three archetypes on one shared
        // body must differ in colour *and* in size, because either alone is a distinction a player
        // loses in a crowd at a glance from the play camera.
        EnemyLook husk = Load(HuskPath).ToLook();
        EnemyLook spitter = Load(SpitterPath).ToLook();
        EnemyLook bloater = Load(BloaterPath).ToLook();

        Assert.That(spitter.Tint, Is.Not.EqualTo(husk.Tint));
        Assert.That(bloater.Tint, Is.Not.EqualTo(husk.Tint));
        Assert.That(bloater.Tint, Is.Not.EqualTo(spitter.Tint));

        Assert.That(spitter.BodyScale, Is.LessThan(husk.BodyScale), "A Spitter is slight.");
        Assert.That(bloater.BodyScale, Is.GreaterThan(husk.BodyScale), "A Bloater is fat — that is the tell.");
    }

    [Test]
    public void Assets_ObeyTheOneShotRule()
    {
        // GD §12.4: no non-boss attack may take more than 35 % of the player's max HP *at any
        // depth*. The Oathbound has 140, so the ceiling is 49 — and d(n) caps at 3.0×, which makes
        // the real ceiling on anything a designer types 16.3. A violation here does not show up in
        // a playtest; it shows up forty stages into somebody's run.
        //
        // The damage read is contactDamage and only contactDamage, because neither block carries
        // one: a shot and a blast both deal the agent's stat, which is the whole reason depth
        // scaling reaches them (rules 5, 8).
        float ceiling = OathboundMaxHp * OneShotShare;

        foreach (string path in new[] { HuskPath, SpitterPath, BloaterPath })
        {
            EnemySpec spec = Load(path).ToSpec();

            float atDepth = spec.ContactDamage * DamageCapMultiplier;

            Assert.That(atDepth, Is.LessThanOrEqualTo(ceiling),
                $"{spec.Id.Value} hits for {spec.ContactDamage} × {DamageCapMultiplier} = " +
                $"{atDepth} at the depth cap, past GD §12.4's {ceiling} ceiling. Anything above " +
                "16.3 contact damage breaks the one-shot rule at depth while looking fine today.");
        }
    }

    [Test]
    public void Descent_RostersAllThree()
    {
        var definition = AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath);

        Assert.That(definition, Is.Not.Null, $"No ModeDefinition at {DescentPath}.");

        ModeSpec descent = definition.ToSpec();

        // GD §8.2's introduction schedule. M2-02 rule 10 named this task as the one that adds the
        // two rows, and without them every wave M2-05 composes is Husks for the whole milestone.
        Assert.That(descent.Roster, Has.Count.EqualTo(3));

        AssertRoster(descent, "enemy.husk", 1);
        AssertRoster(descent, "enemy.spitter", 2);
        AssertRoster(descent, "enemy.bloater", 4);
    }

    // ---- The pooled-look rule, EditMode half (rule 10) ------------------------------------------

    [Test]
    public void Look_SurvivesSetAndRestore()
    {
        (EnemyHitFeedback feedback, Renderer renderer) = NewBody();

        feedback.SetArchetypeLook(Color.red, 1.35f);

        Assert.That(feedback.transform.localScale, Is.EqualTo(Vector3.one * 1.35f),
            "Sanity: the look is applied against the prefab's scale, not added to it.");

        // Stand in for the flash and the swell, which need a run's event hub and an id to reach.
        // What matters to this rule is that *something* moved both properties off the archetype's
        // look and that the reset comes back to it rather than to the prefab's grey and 1.0.
        feedback.transform.localScale = Vector3.one * 1.15f * 1.35f;
        WriteColour(renderer, Color.white);

        feedback.ResetVisuals();

        Assert.That(feedback.transform.localScale, Is.EqualTo(Vector3.one * 1.35f),
            "A reset must return the body to the archetype's size, not to the prefab's.");
        AssertColour(
            ReadColour(renderer),
            Color.red,
            "A reset must return the body to the archetype's colour. Reverting to the prefab's " +
            "would turn a Bloater grey the first time it was hit.");
    }

    [Test]
    public void Look_DoesNotCompoundAcrossRentals()
    {
        (EnemyHitFeedback feedback, _) = NewBody();

        feedback.SetArchetypeLook(Color.red, 1.35f);
        feedback.ResetVisuals();
        feedback.SetArchetypeLook(Color.red, 1.35f);
        feedback.ResetVisuals();
        feedback.SetArchetypeLook(Color.red, 1.35f);

        // 1.35, not 1.35³ = 2.46. The scale is a multiple of the prefab's own, so it has to be
        // applied against a base that a rental never writes to — the failure is a Bloater that
        // grows every time the pool hands its body out.
        Assert.That(feedback.transform.localScale, Is.EqualTo(Vector3.one * 1.35f));
    }

    [Test]
    public void Look_AHuskAfterABloater_IsAHusk()
    {
        // The half of AR §18.4's pooled-reset rule that EditMode can reach: the body is told what it
        // is on every rental, so the previous tenant's rust is overwritten rather than remembered.
        // The full rent → kill → re-rent assertion through the pool is M2-09 rule 10's.
        (EnemyHitFeedback feedback, Renderer renderer) = NewBody();

        feedback.SetArchetypeLook(Color.red, 1.35f);
        feedback.ResetVisuals();

        feedback.SetArchetypeLook(EnemyLook.Default.Tint, 1f);

        Assert.That(feedback.transform.localScale, Is.EqualTo(Vector3.one));

        AssertColour(
            ReadColour(renderer),
            EnemyLook.Default.Tint,
            "The rust has to be gone, not merely overwritten in one channel.");
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static void AssertRoster(ModeSpec mode, string specId, int stage)
    {
        foreach (RosterEntry entry in mode.Roster)
        {
            if (entry.SpecId.Value == specId)
            {
                Assert.That(entry.IntroducedAtStage, Is.EqualTo(stage),
                    $"GD §8.2 introduces {specId} at stage {stage}.");

                return;
            }
        }

        Assert.Fail($"Descent.asset's roster has no entry for {specId}.");
    }

    private static EnemyDefinition Load(string path)
    {
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);

        Assert.That(definition, Is.Not.Null, $"No EnemyDefinition at {path}.");

        return definition;
    }

    /// <summary>
    /// A bare body: the component under test and a renderer for it to write a colour into.
    /// </summary>
    /// <remarks>
    /// <c>Awake</c> never runs in EditMode (Traps §5), so the renderer is assigned through a
    /// <see cref="SerializedObject"/> exactly as it would be by a prefab, and
    /// <c>SetArchetypeLook</c> builds the property block itself. That is what makes this half of
    /// rule 10 provable here at all.
    /// </remarks>
    private (EnemyHitFeedback, Renderer) NewBody()
    {
        var body = new GameObject("Body", typeof(MeshRenderer), typeof(MeshFilter));

        _created.Add(body);

        var renderer = body.GetComponent<MeshRenderer>();

        // RequireComponent adds EnemyView with it, which is what the real prefab carries too.
        var feedback = body.AddComponent<EnemyHitFeedback>();

        var serialized = new SerializedObject(feedback);
        serialized.FindProperty("_renderer").objectReferenceValue = renderer;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return (feedback, renderer);
    }

    /// <summary>
    /// Compares two colours channel by channel within <see cref="Tolerance"/>.
    /// </summary>
    /// <remarks>
    /// <c>Is.EqualTo</c> on a <see cref="Color"/> is exact — NUnit calls <c>Equals</c>, not Unity's
    /// approximate <c>==</c> — and a colour that has been through
    /// <see cref="MaterialPropertyBlock"/> and back does not survive that: the round trip returned
    /// a value that printed identically and compared unequal. Four channels within a tolerance is
    /// the assertion that is actually being made.
    /// </remarks>
    private static void AssertColour(Color actual, Color expected, string because)
    {
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(Tolerance), because);
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(Tolerance), because);
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(Tolerance), because);
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(Tolerance), because);
    }

    private static void WriteColour(Renderer renderer, Color colour)
    {
        var block = new MaterialPropertyBlock();

        renderer.GetPropertyBlock(block);
        block.SetColor(Shader.PropertyToID("_BaseColor"), colour);
        renderer.SetPropertyBlock(block);
    }

    private static Color ReadColour(Renderer renderer)
    {
        var block = new MaterialPropertyBlock();

        renderer.GetPropertyBlock(block);

        return block.GetColor(Shader.PropertyToID("_BaseColor"));
    }

    private EnemyDefinition NewDefinition(string assetName)
    {
        var definition = ScriptableObject.CreateInstance<EnemyDefinition>();

        // CreateInstance leaves `name` empty, and an empty name makes a Does.Contain assertion
        // pass against any message at all (M0-11).
        definition.name = assetName;

        _created.Add(definition);

        return definition;
    }

    private static void SetFloat(EnemyDefinition definition, string field, float value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetColour(EnemyDefinition definition, string field, Color value)
    {
        var serialized = new SerializedObject(definition);
        serialized.FindProperty(field).colorValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Two entries under one key — which a <see cref="Dictionary{TKey,TValue}"/> cannot hold, and
    /// which is exactly what a hand-built boot list can hand the book.
    /// </summary>
    private sealed class DuplicateLooks : IReadOnlyDictionary<ContentId, EnemyLook>
    {
        private readonly KeyValuePair<ContentId, EnemyLook>[] _entries;

        public DuplicateLooks(ContentId first, EnemyLook firstLook, ContentId second, EnemyLook secondLook)
        {
            _entries = new[]
            {
                new KeyValuePair<ContentId, EnemyLook>(first, firstLook),
                new KeyValuePair<ContentId, EnemyLook>(second, secondLook),
            };
        }

        public int Count => _entries.Length;

        public IEnumerable<ContentId> Keys
        {
            get
            {
                foreach (KeyValuePair<ContentId, EnemyLook> entry in _entries)
                {
                    yield return entry.Key;
                }
            }
        }

        public IEnumerable<EnemyLook> Values
        {
            get
            {
                foreach (KeyValuePair<ContentId, EnemyLook> entry in _entries)
                {
                    yield return entry.Value;
                }
            }
        }

        public EnemyLook this[ContentId key]
            => TryGetValue(key, out EnemyLook look) ? look : throw new KeyNotFoundException(key.Value);

        public bool ContainsKey(ContentId key) => TryGetValue(key, out _);

        public bool TryGetValue(ContentId key, out EnemyLook value)
        {
            foreach (KeyValuePair<ContentId, EnemyLook> entry in _entries)
            {
                if (entry.Key.Equals(key))
                {
                    value = entry.Value;

                    return true;
                }
            }

            value = default;

            return false;
        }

        public IEnumerator<KeyValuePair<ContentId, EnemyLook>> GetEnumerator()
        {
            foreach (KeyValuePair<ContentId, EnemyLook> entry in _entries)
            {
                yield return entry;
            }
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
