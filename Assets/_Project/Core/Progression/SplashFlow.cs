using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Progression;

// The moment half a tree is taken and CH §5.4 asks a question the run has never asked before. The
// index space it installs into is `TreeRules` (M5-07a-i); this is *when* it happens, *who* may be
// borrowed from, and the one verb that locks it.

/// <summary>
/// One borrowable branch of a second class, as the screen draws it: where it sits in that class's
/// tree, what it is called, and how many nodes come over once its Keystone is dropped.
/// </summary>
/// <remarks>
/// <para>
/// <b>In this file rather than one of its own</b>, for <see cref="SkillBranchSpec"/>'s reason one
/// module over: a borrowable branch outside the moment that offers it is not a thing the game has.
/// </para>
/// <para>
/// <b><see cref="NodeCount"/> is the post-drop count and that is the whole reason this type
/// exists.</b> <see cref="SkillBranchSpec.NodeCount"/> is what the other class authored, Keystone
/// included, and CH §5.4 does not lend the Keystone (M5-07a-i rule 3) — so a screen drawing the
/// branch's own number would promise a node the player cannot have.
/// </para>
/// </remarks>
public readonly struct SplashOption
{
    /// <param name="branch">The branch's index in its own class's tree, 0-based.</param>
    /// <param name="nameKey">What that branch is called — never the name itself (AR §11.5).</param>
    /// <param name="nodeCount">How many nodes come over, the Keystone already dropped.</param>
    public SplashOption(int branch, LocKey nameKey, int nodeCount)
    {
        Branch = branch;
        NameKey = nameKey;
        NodeCount = nodeCount;
    }

    /// <summary>
    /// The branch's index in the class it belongs to, 0-based — what <see cref="SplashFlow.Choose"/>
    /// takes.
    /// </summary>
    /// <remarks>
    /// <b>The source index, never <see cref="TreeRules.SplashBranch"/>.</b> Once installed the
    /// borrowed branch is 3 whichever one it was, so the run's index cannot say which branch was
    /// chosen and this one is the only thing that can.
    /// </remarks>
    public int Branch { get; }

    /// <summary>Localisation key for the branch's display name.</summary>
    public LocKey NameKey { get; }

    /// <summary>
    /// How many nodes this branch would add to the run — CH §5.4's <em>"one branch minus its
    /// Keystone is 7"</em>.
    /// </summary>
    public int NodeCount { get; }
}

/// <summary>
/// CH §5.4's second discipline: the moment half a tree is taken, what may be borrowed, and the
/// choice that locks it.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="LevelUpFlow"/>'s shape — a small state machine <c>RunState</c> owns, driven from
/// outside, publishing rather than subscribed to — and deliberately not part of it</b> (rule 1).
/// The two look alike and differ in every way that matters: this one <em>spends no pick</em>,
/// <em>draws from no stream</em>, <em>grants no node</em>, <em>happens exactly once a run</em> and
/// <em>may legitimately never happen at all</em> (rule 3). <c>LevelUpFlow.Open</c>'s loop is written
/// around <em>"while picks are owed"</em> and its <c>Choose</c> ends with <c>SkillTree.Take</c>;
/// neither sentence is true here, and folding the moment in would put CH §5.4's <em>"the choice is
/// mandatory"</em> inside a class whose whole contract is <em>"three cards, take one"</em>.
/// </para>
/// <para>
/// <b>Nothing here is stored on the snapshot and <c>RunSnapshot.CurrentVersion</c> stays 3</b>
/// (rule 6). CH §5.4 locks the branch for the run, so every foreign id in <c>TakenNodeIds</c> is in
/// the same branch of the same class — which makes the choice recoverable from what is already
/// written down. <see cref="TryDerive"/> is that recovery and <see cref="Restore"/> replays it,
/// silently, <b>before</b> <c>SkillTree.Restore</c> replays the takes. It is
/// <c>LevelUpFlow.GrantOverflow</c>'s bargain exactly — <em>"its caller derives the count rather
/// than reading it, because no field carries it"</em> — and the cost is one stated case: a run saved
/// after the choice and before its first borrowed pick resumes un-splashed and asks again.
/// </para>
/// <para>
/// <b>This object is never handed out of <c>RunState</c></b> (AR §18.2, the eighth time).
/// <see cref="Choose"/> borrows a branch for the rest of the run, so a public handle would let a
/// view spend CH §5.4's one irreversible decision. <c>RunState</c> exposes scalar reads instead.
/// </para>
/// <para>
/// <b>Nothing here allocates on the frame path</b> (rule 13). <see cref="IsPending"/> is two integer
/// comparisons read once a frame in <c>RunTicker.LevelUpPhase</c>; <see cref="Candidates"/> is built
/// once at construction and wrapped; <see cref="BranchesOf"/> and <see cref="Choose"/> run at most
/// once a run, on a paused frame.
/// </para>
/// </remarks>
public sealed class SplashFlow
{
    /// <summary>
    /// The fraction of the primary tree that opens the moment. A half — CH §5.4.
    /// </summary>
    /// <remarks>
    /// <b>A fraction rather than a level, and CH §5.4 is explicit about why:</b> <em>"a twelve-node
    /// tree fills at level 13 and a twenty-seven-node one at level 28, so any constant is wrong at
    /// one of the two scales."</em> Against v1's twelve that is six nodes, around level 7; against
    /// M7-04's twenty-seven it is fourteen, around level 15 — the same beat at both content sizes,
    /// which is what lets the moment be playtested now and still be right later.
    /// </remarks>
    public const float Threshold = 0.5f;

    private readonly SkillTree _tree;
    private readonly ContentCatalog _catalog;

    /// <summary>
    /// Which handler answers for which effect, this run — asked of every borrowed node before
    /// anything is installed.
    /// </summary>
    /// <remarks>
    /// <b>Not in the spec's <i>Public API</i>, and rule 10's <em>"the branch is not installed"</em>
    /// is why.</b> <c>SkillTree.OnSplashInstalled</c> sweeps the borrowed nodes for handlers before
    /// <em>it</em> commits, but by then <c>TreeRules.InstallSplash</c> has already committed — so a
    /// refusal there would leave the pair disagreeing about how many nodes the run has. Swept here,
    /// before either call, a missing <c>Register</c> line leaves the run exactly as it was, and
    /// <c>OnSplashInstalled</c>'s own sweep stays the backstop.
    /// </remarks>
    private readonly EffectRegistry _effects;

    private readonly IDomainEvents _events;

    /// <summary>The class being played, which is the one class that cannot be borrowed from.</summary>
    private readonly ContentId _ownCharacterId;

    /// <summary>
    /// Whether this run's class raises minions — the other half of M5-07a-i's handed-over finding.
    /// </summary>
    /// <remarks>
    /// Read once at construction because it cannot change: a run plays one class.
    /// <c>RunSession.RequireNoMinionTarget</c> asks the same question of the <em>primary</em> tree
    /// at <c>Start</c> and a branch borrowed mid-run never meets it, which is why the sweep is
    /// repeated here — see <see cref="RequireInstallable"/>.
    /// </remarks>
    private readonly bool _raisesMinions;

    private readonly ReadOnlyCollection<ContentId> _candidates;

    private bool _open;

    /// <param name="tree">The run's live tree — what has been taken, and where a branch is installed.</param>
    /// <param name="catalog">
    /// What classes exist and which trees they own. Retained, unlike <c>TreeRules</c>' copy, because
    /// the borrowed branch is not resolved until the player chooses one.
    /// </param>
    /// <param name="effects">
    /// Which handler answers for which effect, this run. See the field: it is what makes a refused
    /// branch leave the run untouched.
    /// </param>
    /// <param name="ownCharacterId">
    /// The class being played. Resolved against <paramref name="catalog"/> here, which is safe by
    /// construction — <c>RunSession.Start</c> resolves the same id before it builds anything.
    /// </param>
    /// <param name="events">Where the two announcements go out.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="KeyNotFoundException">
    /// <paramref name="ownCharacterId"/> is not a class the catalog holds.
    /// </exception>
    public SplashFlow(
        SkillTree tree,
        ContentCatalog catalog,
        EffectRegistry effects,
        ContentId ownCharacterId,
        IDomainEvents events)
    {
        _tree = tree ?? throw new ArgumentNullException(nameof(tree));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _ownCharacterId = ownCharacterId;

        _raisesMinions = catalog.Character(ownCharacterId).Minions is not null;

        // **The primary's count, never the run's** (rule 2). A borrowed branch must not be able to
        // move a threshold that has already been crossed, and `TreeRules.Count` grows the moment one
        // is installed.
        OpensAt = (int)Math.Ceiling(tree.Rules.Tree.NodeCount * (double)Threshold);

        _candidates = BuildCandidates(catalog, ownCharacterId);
    }

    /// <summary>
    /// How many nodes of the <em>primary</em> tree must be taken before the moment is owed —
    /// <c>ceil(NodeCount × <see cref="Threshold"/>)</c>, computed rather than authored.
    /// </summary>
    /// <remarks>
    /// Six against v1's twelve and fourteen against M7-04's twenty-seven. What
    /// <c>SplashOffered.Threshold</c> carries, so the moment can be traced without reading the tree.
    /// </remarks>
    public int OpensAt { get; }

    /// <summary>Whether the moment is owed and not yet spent. Read by the frame, like <c>HasOffer</c>.</summary>
    /// <remarks>
    /// <b>Three terms, and the third is CH §5.4's own branch</b> (rule 3): a run with nothing to
    /// borrow never sees the screen, so a build with one authored class answers false here for ever
    /// and the moment simply does not happen. It stays true while the screen is up — what says the
    /// screen is up is <see cref="IsOpen"/>.
    /// </remarks>
    public bool IsPending =>
        _candidates.Count > 0 && !HasSplashed && _tree.TakenCount >= OpensAt;

    /// <summary>Whether the screen is up — the moment has been opened and not answered.</summary>
    public bool IsOpen => _open;

    /// <summary>Whether this run has already borrowed. True for the rest of the run.</summary>
    /// <remarks>
    /// Derived off <c>TreeRules</c> rather than stored, because a second place recording that a
    /// branch was borrowed is a second place for it to be wrong — <c>TreeRules.SplashBranch</c>'s
    /// own argument.
    /// </remarks>
    public bool HasSplashed => _tree.Rules.SplashBranch != TreeRules.NoSplash;

    /// <summary>
    /// The classes that may be borrowed from, in catalog order. Empty means rule 3.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><em>"Unlocked"</em> is read as <em>"authored"</em>, named rather than invented</b> (rule
    /// 3). CH §5.4 says <em>"a second unlocked class"</em>; <c>PlayerProfile</c> v3 carries no unlock
    /// set — <c>Version</c>, <c>HapticsEnabled</c>, <c>SeenFirstActiveHint</c>, <c>Shards</c> — and
    /// nothing awards one until M6-09. So this is every class in the catalog with a tree, except this
    /// run's own, and <b>with the shipped two that is exactly one</b>.
    /// </para>
    /// <para>
    /// <b>One candidate still draws the page.</b> Skipping the class page for a single card would be
    /// a special case for a build state; drawn, it is one tap that costs nothing and reads
    /// identically at three classes.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ContentId> Candidates => _candidates;

    /// <summary>
    /// Raises the moment if it is owed. Idempotent, and draws nothing (rule 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>There is no draw and therefore no seed question.</b> Every candidate is offered, in catalog
    /// order, so this is idempotent in the strong sense <c>LevelUpFlow.Open</c> has to work for
    /// (AR §18.3): calling it twice costs no stream position because it never had one.
    /// </para>
    /// <para>
    /// <b>Called from <c>RunTicker.LevelUpPhase</c>, between ticks and never inside one</b> (rule 4,
    /// M3-08a rule 4's placement). A level that crosses the threshold is spent on a node first and
    /// the moment is read at the top of the next frame.
    /// </para>
    /// <para>
    /// <c>SplashOffered</c> is published <b>after</b> the state has settled — <c>NodeTaken</c>'s
    /// rule, for its reason.
    /// </para>
    /// </remarks>
    public void Open()
    {
        if (_open || !IsPending)
        {
            return;
        }

        _open = true;

        _events.Publish(new SplashOffered(_tree.TakenCount, OpensAt));
    }

    /// <summary>
    /// The three branches of <paramref name="characterId"/>, as the screen draws them.
    /// </summary>
    /// <param name="characterId">One of <see cref="Candidates"/>.</param>
    /// <remarks>
    /// <b>The Keystone is already dropped from every count</b> (rules 7, 8), so a player is never
    /// shown a number that includes a node they cannot have. A branch that <see cref="Choose"/> would
    /// refuse is still listed — the refusal names what was wrong, and hiding it would make a
    /// mis-authored tree look like a two-branch class.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="characterId"/> is this run's own, is not in the catalog, or has no tree.
    /// </exception>
    public IReadOnlyList<SplashOption> BranchesOf(ContentId characterId)
    {
        SkillTreeSpec tree = RequireCandidate(characterId, nameof(characterId));

        var options = new SplashOption[tree.Branches.Count];

        for (int b = 0; b < options.Length; b++)
        {
            SkillBranchSpec branch = tree.Branches[b];

            options[b] = new SplashOption(b, branch.NameKey, LentNodeCount(branch));
        }

        return options;
    }

    /// <summary>
    /// Borrows <paramref name="branch"/> of <paramref name="characterId"/>, for the run.
    /// </summary>
    /// <param name="characterId">One of <see cref="Candidates"/>.</param>
    /// <param name="branch">Which of its branches — an index into that class's own tree.</param>
    /// <remarks>
    /// <para>
    /// <b>Everything is checked before anything is installed</b>, which is rule 10's <em>"the branch
    /// is not installed"</em>: <see cref="RequireInstallable"/> sweeps the branch for a primitive
    /// this run never registered and for a <c>ModifyStat</c> aimed at minions a run of this class
    /// cannot raise, and <c>TreeRules.InstallSplash</c> makes its own refusals before it commits.
    /// </para>
    /// <para>
    /// <b>The install is two calls and they are one step</b> (M5-07a-i rule 5):
    /// <c>TreeRules.InstallSplash</c> then <c>SkillTree.OnSplashInstalled</c>, because between them
    /// the pair disagrees about how many nodes the run has.
    /// </para>
    /// <para>
    /// <c>SplashChosen</c> is published last, after the state has settled.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The moment is not open, or one was already taken.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The class is this run's own, is not in the catalog, or has no tree; or the branch carries
    /// something this run cannot apply.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="branch"/> is not an index into that class's branches. House style for an
    /// index (M5-07a-i deviation 4), and an <see cref="ArgumentException"/> all the same.
    /// </exception>
    public void Choose(ContentId characterId, int branch)
    {
        if (!_open)
        {
            throw new InvalidOperationException(
                HasSplashed
                    ? $"This run has already borrowed a branch of '{_tree.Rules.SplashCharacterId}'. "
                        + "A splash is locked for the run (CH §5.4)."
                    : "The half-tree moment is not open, so there is nothing to choose. A view sent "
                        + "ChooseSplash without a screen to send it for.");
        }

        Install(characterId, branch, nameof(characterId), nameof(branch));

        _open = false;

        _events.Publish(new SplashChosen(
            characterId, branch, _tree.Rules.NodeCountOf(_tree.Rules.SplashBranch)));
    }

    /// <summary>
    /// Replays a resumed run's borrowed branch, silently — derived rather than saved (rule 6).
    /// </summary>
    /// <remarks>
    /// <b>Silent, for the reason the whole restore block is</b> (AR §18.1): nothing may publish
    /// before <c>RunStarted</c>, and a branch borrowed in a previous session is not news. It runs
    /// <b>before</b> <c>SkillTree.Restore</c>, or the replay would refuse a node the tree has never
    /// heard of.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A branch is already installed.</exception>
    /// <exception cref="ArgumentException">
    /// The class is this run's own, is not in the catalog, or has no tree; or the branch carries
    /// something this run cannot apply.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="branch"/> is not an index into that class's branches.
    /// </exception>
    public void Restore(ContentId characterId, int branch)
    {
        if (HasSplashed)
        {
            throw new InvalidOperationException(
                $"This run has already borrowed a branch of '{_tree.Rules.SplashCharacterId}', so "
                    + "there is nothing to restore onto. Restore is called once, from "
                    + "RunSession.Start's restore block.");
        }

        Install(characterId, branch, nameof(characterId), nameof(branch));

        _open = false;
    }

    /// <summary>
    /// Reads a saved take order back into the class and branch it was borrowed from (rule 6).
    /// </summary>
    /// <param name="takenNodeIds">What <c>RunSnapshot.TakenNodeIds</c> carried, in take order.</param>
    /// <param name="characterId">The class the first foreign id belongs to.</param>
    /// <param name="branch">That id's branch in that class's own tree.</param>
    /// <returns>
    /// Whether any saved id is foreign to the primary tree. <see langword="false"/> is the ordinary
    /// answer: it is every run that has not splashed, <b>and</b> rule 6's one stated cost — a run
    /// saved after the choice and before its first borrowed pick, which comes back un-splashed and
    /// is asked again.
    /// </returns>
    /// <remarks>
    /// <b>A saved id that belongs to nobody is left alone rather than refused here.</b>
    /// <c>SkillTree.Restore</c> throws naming content validation, which is what a build that has
    /// dropped a class should do — and it is the message that names the id.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="takenNodeIds"/> is null.</exception>
    public bool TryDerive(
        IReadOnlyList<ContentId> takenNodeIds,
        out ContentId characterId,
        out int branch)
    {
        if (takenNodeIds is null)
        {
            throw new ArgumentNullException(nameof(takenNodeIds));
        }

        for (int i = 0; i < takenNodeIds.Count; i++)
        {
            ContentId id = takenNodeIds[i];

            if (_tree.Rules.Tree.TryLocate(id, out _, out _))
            {
                continue;
            }

            for (int c = 0; c < _candidates.Count; c++)
            {
                if (_catalog.TryGetTreeFor(_candidates[c], out SkillTreeSpec tree) &&
                    tree.TryLocate(id, out branch, out _))
                {
                    characterId = _candidates[c];

                    return true;
                }
            }
        }

        characterId = default;
        branch = TreeRules.NoSplash;

        return false;
    }

    /// <summary>The two calls that make a splash, with everything checked before either.</summary>
    private void Install(
        ContentId characterId,
        int branch,
        string characterParam,
        string branchParam)
    {
        SkillTreeSpec tree = RequireCandidate(characterId, characterParam);

        if (branch < 0 || branch >= tree.Branches.Count)
        {
            throw new ArgumentOutOfRangeException(
                branchParam,
                branch,
                $"Branches are indexed from 0 and '{tree.Id}' has {tree.Branches.Count}.");
        }

        RequireInstallable(tree, branch, characterParam);

        _tree.Rules.InstallSplash(tree, branch, _catalog);
        _tree.OnSplashInstalled();
    }

    /// <summary>
    /// The tree of a class that may be borrowed from, or a refusal naming which rule it broke.
    /// </summary>
    private SkillTreeSpec RequireCandidate(ContentId characterId, string paramName)
    {
        if (characterId == _ownCharacterId)
        {
            throw new ArgumentException(
                $"'{characterId}' is the class this run is playing. CH §5.4 borrows a branch from a "
                    + "*second* class; a class splashing itself would offer nodes it already holds.",
                paramName);
        }

        if (!_catalog.TryGetTreeFor(characterId, out SkillTreeSpec tree))
        {
            throw new ArgumentException(
                $"No class with id '{characterId}' has a tree in this catalog, so there is no branch "
                    + "to borrow. The screen offers SplashFlow.Candidates and nothing else.",
                paramName);
        }

        return tree;
    }

    /// <summary>
    /// Refuses a branch this run could not apply, <em>before</em> anything about it is installed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two sweeps in one walk, and both are the same sentence at a different door.</b>
    /// <c>SkillTree</c>'s constructor asks <c>EffectRegistry.CanApply</c> of every node of the
    /// primary tree at <c>Start</c>, and <c>RunSession.RequireNoMinionTarget</c> asks the second
    /// question of the same nodes at the same moment. <b>A branch borrowed mid-run meets neither</b>
    /// — that is M5-07a-i's handed-over finding — so an Oathbound run borrowing the Gravecaller's
    /// Legion branch could take a node whose <c>ModifyStat</c> aims at <see cref="StatTarget.Minions"/>
    /// and <c>ModifyStatHandler</c> would throw at the moment the card is tapped, with the node owned
    /// and its effects half on. <c>SkillTree.OnSplashInstalled</c>'s sweep cannot catch it:
    /// <c>CanApply</c> answers <em>"is there a handler for this type"</em> and a <c>ModifyStat</c>
    /// handler is registered.
    /// </para>
    /// <para>
    /// <b>Both lists, take and cast</b>, for <c>SkillTree.RequireHandlers</c>' reason: an Active's
    /// cast effects are applied by the runner rather than by <c>Take</c>, so a node whose
    /// <em>cast</em> carries the mistake would survive the pick and die on the first firing.
    /// </para>
    /// <para>
    /// <b>The Keystone is skipped</b>, because it does not come over (M5-07a-i rule 3), and an
    /// unresolvable id is skipped too — <c>TreeRules.InstallSplash</c> is what names that one, and
    /// naming it twice in two voices helps nobody.
    /// </para>
    /// </remarks>
    private void RequireInstallable(SkillTreeSpec tree, int branch, string paramName)
    {
        SkillBranchSpec source = tree.Branches[branch];

        for (int t = 1; t <= source.TierCount; t++)
        {
            IReadOnlyList<ContentId> tier = source.Tier(t);

            for (int i = 0; i < tier.Count; i++)
            {
                if (!_catalog.TryGetSkill(tier[i], out SkillSpec spec) ||
                    spec.Kind == SkillKind.Keystone)
                {
                    continue;
                }

                RequireInstallable(spec, spec.Effects, "takes", tree, branch, paramName);

                if (spec.Active is not null)
                {
                    RequireInstallable(spec, spec.Active.OnCast, "casts", tree, branch, paramName);
                }
            }
        }
    }

    private void RequireInstallable(
        SkillSpec spec,
        IReadOnlyList<IEffect> effects,
        string when,
        SkillTreeSpec tree,
        int branch,
        string paramName)
    {
        for (int i = 0; i < effects.Count; i++)
        {
            IEffect effect = effects[i];

            if (!_effects.CanApply(effect))
            {
                throw new ArgumentException(
                    $"'{spec.Id}' {when} a '{effect.GetType().Name}' and this run has no handler "
                        + $"registered for it, so branch {branch} of '{tree.Id}' cannot be borrowed: "
                        + "taking the node would throw part way through. Nothing has been installed.",
                    paramName);
            }

            if (_raisesMinions || effect is not ModifyStat modify ||
                modify.Target != StatTarget.Minions)
            {
                continue;
            }

            throw new ArgumentException(
                $"'{spec.Id}' {when} a ModifyStat aimed at {StatTarget.Minions}, and "
                    + $"'{_ownCharacterId}' has no MinionSpec — so there is no minion recipe for it "
                    + $"to move. Branch {branch} of '{tree.Id}' is refused here rather than at the "
                    + "moment the node is picked (M5-06a rule 5, one door along): EffectRegistry"
                    + ".CanApply keys on an effect's type and a target is a field on one. Nothing "
                    + "has been installed.",
                paramName);
        }
    }

    /// <summary>How many of <paramref name="branch"/>'s nodes would come over.</summary>
    /// <remarks>
    /// Counted through the catalog rather than read off <see cref="SkillBranchSpec.NodeCount"/>,
    /// because the Keystone is dropped and a kind is not something a branch knows — the same reason
    /// <c>TreeRules</c>' cross-checks need a catalog and <c>SkillTreeSpec</c>'s constructor refuses
    /// one. An id nobody authored counts as a node here: <c>InstallSplash</c> refuses that branch
    /// outright, and a screen quietly subtracting one from the number would be the softer answer.
    /// </remarks>
    private int LentNodeCount(SkillBranchSpec branch)
    {
        int count = 0;

        for (int t = 1; t <= branch.TierCount; t++)
        {
            IReadOnlyList<ContentId> tier = branch.Tier(t);

            for (int i = 0; i < tier.Count; i++)
            {
                if (!_catalog.TryGetSkill(tier[i], out SkillSpec spec) ||
                    spec.Kind != SkillKind.Keystone)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Every class with a tree except <paramref name="ownCharacterId"/>, in catalog order.</summary>
    private static ReadOnlyCollection<ContentId> BuildCandidates(
        ContentCatalog catalog,
        ContentId ownCharacterId)
    {
        var candidates = new List<ContentId>(catalog.Characters.Count);

        for (int i = 0; i < catalog.Characters.Count; i++)
        {
            ContentId id = catalog.Characters[i].Id;

            if (id != ownCharacterId && catalog.TryGetTreeFor(id, out _))
            {
                candidates.Add(id);
            }
        }

        return candidates.AsReadOnly();
    }
}
