using System.IO;
using System.Threading.Tasks;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// The <see cref="ISaveStore"/> core tests write to: it holds one profile and one run in memory
/// and answers without a disk.
/// </summary>
/// <remarks>
/// <para>
/// A fake on <c>FixedRandom</c> and <c>FixedClock</c>'s shelf, not an adapter. M2-14a and M2-14b
/// both need a store that answers immediately and forgets when the fixture does; a test that wrote
/// to <c>persistentDataPath</c> would be a test that fails differently on the machine that runs it
/// second, and that leaves a file behind for the run after. The real store is
/// <c>LocalJsonSaveStore</c> (M2-13b), and it is tested against a temporary directory of its own.
/// </para>
/// <para>
/// It counts its calls, because the interesting assertions about saving are about <em>how many
/// times</em> — a run that wrote a snapshot per frame instead of per stage boundary would round-trip
/// correctly and still be wrong.
/// </para>
/// </remarks>
public sealed class InMemorySaveStore : ISaveStore
{
    private PlayerProfile? _profile;
    private RunSnapshot? _run;

    private bool _failNextWrite;

    /// <summary>How many times <see cref="SaveRun"/> has been called.</summary>
    /// <remarks>
    /// Calls, not successes: a write armed by <see cref="FailNextWrite"/> counts, because what a
    /// test asserting on this wants to know is how often the run reached for the store.
    /// </remarks>
    public int RunWriteCount { get; private set; }

    /// <summary>How many times <see cref="SaveProfile"/> has been called.</summary>
    public int ProfileWriteCount { get; private set; }

    /// <summary>How many times <see cref="ClearRun"/> has been called.</summary>
    public int ClearCount { get; private set; }

    /// <summary>
    /// Makes the next write — a run, a profile or a clear — fault instead of storing anything.
    /// One write only; everything after it succeeds again.
    /// </summary>
    /// <remarks>
    /// It exists because "a failed write must not end a run" needs a way to fail one, and it fails
    /// the way real I/O does: the call returns a task, and the task is faulted. A fake that threw
    /// from the call itself would let a caller pass this test with a <c>try</c> around the
    /// invocation and still crash on a real device, where the disk is full and the exception
    /// arrives at the <c>await</c>.
    /// </remarks>
    public void FailNextWrite()
    {
        _failNextWrite = true;
    }

    /// <inheritdoc />
    public Task<PlayerProfile?> LoadProfile()
    {
        return Task.FromResult(_profile);
    }

    /// <inheritdoc />
    public Task SaveProfile(PlayerProfile profile)
    {
        ProfileWriteCount++;

        if (TakeArmedFailure(out Task faulted))
        {
            return faulted;
        }

        _profile = profile;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<RunSnapshot?> LoadRun()
    {
        return Task.FromResult(_run);
    }

    /// <inheritdoc />
    public Task SaveRun(RunSnapshot run)
    {
        RunWriteCount++;

        if (TakeArmedFailure(out Task faulted))
        {
            return faulted;
        }

        _run = run;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Leaves the profile alone — the two are separate stores that share one port.</remarks>
    public Task ClearRun()
    {
        ClearCount++;

        if (TakeArmedFailure(out Task faulted))
        {
            return faulted;
        }

        _run = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Disarms <see cref="FailNextWrite"/> and hands back the faulted task it promised, or false
    /// when nothing was armed.
    /// </summary>
    private bool TakeArmedFailure(out Task faulted)
    {
        if (!_failNextWrite)
        {
            faulted = null;
            return false;
        }

        _failNextWrite = false;
        faulted = Task.FromException(
            new IOException("InMemorySaveStore was told to fail this write (FailNextWrite)."));

        return true;
    }
}
