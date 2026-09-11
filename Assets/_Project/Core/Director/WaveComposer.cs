using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Director;

/// <summary>
/// Turns a stage's threat budget into a fixed list of waves: what arrives, how many of it, and in
/// which wave — deterministically from the seed, inside the concurrency the device can draw.
/// GD §12.1–12.2 and §11.2.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides what a stage is made of and nothing about when.</b> Spacing waves out, placing
/// bodies in the arena and telegraphing them are the director's (M2-05). The split is what makes
/// a stage's contents testable without a clock: a composition is a pure function of the stage, the
/// mode and the <c>Spawn</c> stream's position.
/// </para>
/// <para>
/// <b>The seed decides <em>what</em> arrives, never <em>how much</em>.</b> The budget split across
/// waves is arithmetic with no draw in it (rule 3), so two runs from one seed see the same rising
/// pressure curve and differ only in which archetypes fill it — and a mode's difficulty cannot be
/// rerolled by killing the app. Every draw comes from <see cref="IRandom.Spawn"/> and no other
/// stream (ADR-0011): an offer reroll or a drop must never shift what a stage is made of.
/// </para>
/// <para>
/// <b>The vocabulary is the mode's, never the catalog's.</b> Eligibility comes from
/// <see cref="ModeSpec.RosterFor"/> and costs from <see cref="EnemySpec.ThreatCost"/>, so the
/// composer can only ever buy something the mode listed at a depth the mode allows — which is
/// what makes a Boss Rush or a Trial a data change rather than a branch in here (GD §8.2, §4.5).
/// </para>
/// <para>
/// <b>Quality over quantity, GD §11.2.</b> When the concurrency cap binds — from stage 36 upward
/// on the mid tier, and immediately on a low-tier phone — surplus budget is spent on more
/// expensive archetypes rather than on more bodies, so a stage-60 wave differs from a stage-30 one
/// on a device that cannot draw more of them. What no upgrade can absorb is reported as
/// <see cref="WavePlan.UnspentThreat"/> for M7-02's Elites to spend.
/// </para>
/// </remarks>
public sealed class WaveComposer
{
    private readonly ContentCatalog _catalog;
    private readonly ThreatBudget _budget;

    /// <summary>The archetypes eligible at the stage being composed, roster order.</summary>
    /// <remarks>
    /// <para>
    /// <b>A field rather than a <c>stackalloc</c>, and that is forced rather than chosen.</b>
    /// <see cref="RosterEntry"/> holds a <see cref="ContentId"/>, which holds a
    /// <see cref="string"/>, so the type is managed and <c>stackalloc RosterEntry[n]</c> does not
    /// compile — the spec's "stack-allocated eligibility buffer" is not available for this type.
    /// Grown on the first call with a roster this size and reused for every call after, which is
    /// what rule 9's "allocates nothing after the first call" asks for either way.
    /// </para>
    /// <para>
    /// Four parallel buffers rather than one of a tuple type, because three of them are
    /// <see cref="int"/> and the composer's inner loops read the costs far more often than the
    /// ids: a cost lookup per draw through <see cref="ContentCatalog.Enemy"/> would be a dictionary
    /// probe inside the hottest loop in the type, for a number that cannot change during a stage.
    /// </para>
    /// </remarks>
    private RosterEntry[] _eligible = Array.Empty<RosterEntry>();

    /// <summary>Each eligible archetype's threat cost, parallel to <see cref="_eligible"/>.</summary>
    private int[] _costs = Array.Empty<int>();

    /// <summary>How many of each eligible archetype the wave being built holds.</summary>
    private int[] _counts = Array.Empty<int>();

    /// <summary>Indices into <see cref="_eligible"/> of what the remaining budget can afford.</summary>
    private int[] _affordable = Array.Empty<int>();

    /// <param name="catalog">Where an archetype's <see cref="EnemySpec.ThreatCost"/> is read from.</param>
    /// <param name="budget">The run's budget, waves and concurrency curves bound to its device cap.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public WaveComposer(ContentCatalog catalog, ThreatBudget budget)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _budget = budget ?? throw new ArgumentNullException(nameof(budget));
    }

    /// <summary>
    /// Fills <paramref name="destination"/> with stage <paramref name="stage"/>'s waves.
    /// </summary>
    /// <param name="stage">The depth being composed. Stages are numbered from 1.</param>
    /// <param name="mode">The mode whose roster, schedule and curves the stage is composed from.</param>
    /// <param name="destination">
    /// The plan to overwrite — built once per run with <c>maxWaves</c> at the wave curve's
    /// maximum and <c>maxEntriesPerWave</c> at the mode's roster length.
    /// </param>
    /// <param name="spawn">
    /// The <c>Spawn</c> stream, and only ever that one. Every draw here advances it, so the same
    /// stream position and the same stage always produce the same composition.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="mode"/>, <paramref name="destination"/> or <paramref name="spawn"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="mode"/>'s roster is empty, it introduces nothing at or before
    /// <paramref name="stage"/>, or <paramref name="destination"/> is too small for the roster. A
    /// plan sized for one mode and handed another is a caller bug, and truncating it would
    /// silently narrow a wave's vocabulary.
    /// </exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">
    /// The mode lists an archetype the catalog does not hold. Never reachable through a real run —
    /// <c>RunSession.Start</c> resolves every roster entry before <c>RunStarted</c> (M2-02) — and
    /// left untranslated here on purpose, so the catalog's own message names the id.
    /// </exception>
    public void Compose(int stage, ModeSpec mode, WavePlan destination, IRandomStream spawn)
    {
        if (mode is null)
        {
            throw new ArgumentNullException(nameof(mode));
        }

        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (spawn is null)
        {
            throw new ArgumentNullException(nameof(spawn));
        }

        if (stage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "Stages are numbered from 1 (GD §8.2).");
        }

        int eligible = PrepareEligible(stage, mode, destination);
        int waves = _budget.Waves(stage);
        int concurrency = _budget.Concurrency(stage);
        float total = _budget.Budget(stage);

        destination.Begin(stage, waves, concurrency);

        // The triangular denominator: wave i of W is worth i shares out of W(W+1)/2, so a stage
        // opens gently and closes hard (GD §4.2's shape).
        float shares = waves * (waves + 1) / 2f;

        // Whole numbers, and that is load-bearing rather than tidy. Threat costs are integers, so
        // a stage's spend is exactly representable — while a running float remainder is not, and
        // stage 1 comes out at *nine* Husks if you write one: B·1/3 = 13.333334, less 12 bought,
        // carried onto B·2/3 = 26.666667 gives 27.999999, and the tenth Husk costs 4 of the 4 that
        // are not quite there. Tracked as ints and differenced against the cumulative share
        // instead, the last wave's fraction is exactly 1, so its allowance is exactly what the
        // stage has left and GD §12.1's ten Husks land. Every float here is on the budget side.
        int spent = 0;
        int stageBodies = 0;

        for (int wave = 1; wave <= waves; wave++)
        {
            // Rule 3's two halves in one expression: the wave's own share is B·i/T, and what an
            // earlier wave could not spend carries into it — which "cumulative share minus
            // cumulative spend" is, without a second variable that could disagree with this one.
            int cumulativeShares = wave * (wave + 1) / 2;
            float allowance = (total * (cumulativeShares / shares)) - spent;

            Array.Clear(_counts, 0, eligible);
            int bodies = 0;
            int waveSpend = 0;

            // Rule 5: the stage's introduction goes first, before any draw, so GD §8.2's "in a
            // wave where they're the only new thing" is true of wave 1 by construction rather than
            // by luck. Unaffordable is not a reason to skip it — it is the same one body rule 8
            // lets a stage overspend on, and the debt it leaves is what stops anything else being
            // bought.
            if (wave == 1 && mode.TryGetIntroduction(stage, out ContentId introduced))
            {
                int index = IndexOf(introduced, eligible);

                if (index >= 0)
                {
                    waveSpend += Buy(index, ref bodies);
                }
            }

            // Rule 6: buy one archetype at a time, uniformly among what is both eligible and
            // affordable, until the money or the arena runs out.
            while (bodies < concurrency)
            {
                int affordable = Affordable(eligible, allowance - waveSpend);

                if (affordable == 0)
                {
                    break;
                }

                waveSpend += Buy(_affordable[spawn.NextInt(0, affordable)], ref bodies);
            }

            // Rule 8: a stage is never empty. Once per stage and not once per wave — "one body,
            // not zero" is a statement about the run the player is in, and a forced buy in every
            // wave would turn a mis-authored budget into a trickle of free enemies instead of one
            // declared overspend. A stage whose cheapest archetype costs more than its whole
            // budget still opens with one of them; the alternative is an arena the player waits in
            // for a wave that never comes, with nothing reporting a fault.
            if (bodies == 0 && stageBodies == 0)
            {
                waveSpend += Buy(Cheapest(eligible), ref bodies);
            }

            // Rule 7: at the body cap with money left, stop adding and start upgrading. This is
            // the whole mechanism by which GD §11.2's device independence holds — a low-tier phone
            // gets the same difficulty in fewer, nastier bodies.
            if (bodies >= concurrency)
            {
                waveSpend += Upgrade(eligible, allowance - waveSpend);
            }

            destination.SetWave(wave, _eligible, _counts, eligible);

            stageBodies += bodies;
            spent += waveSpend;
        }

        destination.Complete(total - spent);
    }

    /// <summary>
    /// Sizes the working buffers, collects what is eligible at <paramref name="stage"/> and reads
    /// each one's cost. Returns how many archetypes are eligible.
    /// </summary>
    /// <remarks>
    /// The costs are read once a stage rather than once a draw, and that is the only reason this
    /// is a separate step: <see cref="ContentCatalog.Enemy"/> is a dictionary probe, and an
    /// archetype's cost cannot change while a stage is being composed.
    /// </remarks>
    private int PrepareEligible(int stage, ModeSpec mode, WavePlan destination)
    {
        int roster = mode.Roster.Count;

        if (roster == 0)
        {
            throw new ArgumentException(
                $"Mode '{mode.Id}' has an empty roster, so stage {stage} has no vocabulary to be "
                    + "composed from. An empty roster is legal for a mode whose content comes "
                    + "entirely from its spawn plan (M2-02) — such a mode is not composed.",
                nameof(mode));
        }

        if (roster > destination.MaxEntriesPerWave)
        {
            throw new ArgumentException(
                $"This plan holds {destination.MaxEntriesPerWave} archetypes per wave and mode "
                    + $"'{mode.Id}' has {roster}. Size maxEntriesPerWave to the roster length once "
                    + "per run.",
                nameof(destination));
        }

        if (_eligible.Length < roster)
        {
            _eligible = new RosterEntry[roster];
            _costs = new int[roster];
            _counts = new int[roster];
            _affordable = new int[roster];
        }

        int eligible = mode.RosterFor(stage, _eligible);

        if (eligible == 0)
        {
            throw new ArgumentException(
                $"Mode '{mode.Id}' introduces nothing at or before stage {stage}, so the stage has "
                    + "no vocabulary to be composed from. Rule 8's 'a stage is never empty' has no "
                    + "cheapest archetype to reach for, so this is refused rather than answered "
                    + "with an arena the player waits in. Check the roster's introduction stages "
                    + "against the mode's StartingStage.",
                nameof(mode));
        }

        for (int i = 0; i < eligible; i++)
        {
            _costs[i] = _catalog.Enemy(_eligible[i].SpecId).ThreatCost;
        }

        return eligible;
    }

    /// <summary>Adds one body of eligible archetype <paramref name="index"/>, and returns its cost.</summary>
    private int Buy(int index, ref int bodies)
    {
        _counts[index]++;
        bodies++;

        return _costs[index];
    }

    /// <summary>
    /// Fills <see cref="_affordable"/> with the eligible archetypes costing no more than
    /// <paramref name="allowance"/>, and returns how many there are.
    /// </summary>
    /// <remarks>
    /// Rebuilt every draw rather than narrowed as the allowance falls, because the list is at most
    /// the roster's ten entries and a narrowing one would have to be re-widened for the next wave.
    /// </remarks>
    private int Affordable(int eligible, float allowance)
    {
        int count = 0;

        for (int i = 0; i < eligible; i++)
        {
            if (_costs[i] <= allowance)
            {
                _affordable[count++] = i;
            }
        }

        return count;
    }

    /// <summary>The eligible archetype with the lowest threat cost; ties go to the roster's order.</summary>
    private int Cheapest(int eligible)
    {
        int cheapest = 0;

        for (int i = 1; i < eligible; i++)
        {
            if (_costs[i] < _costs[cheapest])
            {
                cheapest = i;
            }
        }

        return cheapest;
    }

    /// <summary>
    /// Spends <paramref name="allowance"/> on quality instead of quantity: swaps the cheapest
    /// planned body for the most expensive affordable archetype, repeatedly. Returns the extra
    /// threat that cost — the body count is unchanged by construction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No draw in here, deliberately.</b> Upgrading is forced — it is what the budget says the
    /// wave must become once the arena is full — so making it random would let the seed change a
    /// capped stage's difficulty, which is the one thing GD §11.2 says a device must not do.
    /// Ties go to the roster's order for the same reason.
    /// </para>
    /// <para>
    /// Terminates because every swap is strictly more expensive than what it replaces and costs
    /// are whole numbers: each pass spends at least 1 of a finite allowance, and the loop ends at
    /// the latest when every body is the most expensive archetype the wave can afford.
    /// </para>
    /// </remarks>
    private int Upgrade(int eligible, float allowance)
    {
        int extra = 0;

        while (true)
        {
            int cheapest = CheapestPlanned(eligible);

            if (cheapest < 0)
            {
                // Only reachable for a wave with no bodies at all, which the cap cannot produce:
                // concurrency is at least 1, so a wave that reached it has something in it.
                break;
            }

            int best = BestUpgrade(eligible, _costs[cheapest], allowance - extra);

            if (best < 0)
            {
                break;
            }

            _counts[cheapest]--;
            _counts[best]++;
            extra += _costs[best] - _costs[cheapest];
        }

        return extra;
    }

    /// <summary>The cheapest archetype the wave currently holds, or −1 if it holds none.</summary>
    private int CheapestPlanned(int eligible)
    {
        int cheapest = -1;

        for (int i = 0; i < eligible; i++)
        {
            if (_counts[i] > 0 && (cheapest < 0 || _costs[i] < _costs[cheapest]))
            {
                cheapest = i;
            }
        }

        return cheapest;
    }

    /// <summary>
    /// The most expensive archetype that is strictly dearer than <paramref name="replacedCost"/>
    /// and whose extra cost fits in <paramref name="allowance"/>, or −1 if there is none.
    /// </summary>
    private int BestUpgrade(int eligible, int replacedCost, float allowance)
    {
        int best = -1;

        for (int i = 0; i < eligible; i++)
        {
            if (_costs[i] <= replacedCost || _costs[i] - replacedCost > allowance)
            {
                continue;
            }

            if (best < 0 || _costs[i] > _costs[best])
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>Where <paramref name="specId"/> sits among the eligible archetypes, or −1.</summary>
    /// <remarks>
    /// A linear walk over at most the roster's ten entries, once a stage. Returning −1 rather than
    /// throwing is what lets rule 5 ask the question of a stage whose introduction is not eligible
    /// — which cannot happen while <see cref="ModeSpec.TryGetIntroduction"/> only answers for the
    /// stage being composed, and is a silent no-op rather than a crash if that ever changes.
    /// </remarks>
    private int IndexOf(ContentId specId, int eligible)
    {
        for (int i = 0; i < eligible; i++)
        {
            if (_eligible[i].SpecId == specId)
            {
                return i;
            }
        }

        return -1;
    }
}
