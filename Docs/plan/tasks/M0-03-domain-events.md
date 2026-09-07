# M0-03 — Domain events: `IDomainEvents`, `DomainEventHub`, `RecordingEvents`

**Size:** M · **Depends on:** M0-01 · **Branch:** `m0-03-domain-events`
**Design refs:** AR §8, ADR-0004

## Goal

Core can publish what happened through an outbound port; Game can subscribe with typed handlers; tests can assert exact event sequences.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ports/IDomainEvents.cs` | Core | The outbound port |
| `Game/Adapters/DomainEventHub.cs` | Game | Run-scoped typed dispatcher |
| `Tests/Core/Fakes/RecordingEvents.cs` | Tests.Core | Fake used by every later core test |
| `Tests/Game/Adapters/DomainEventHubTests.cs` | Tests.Game | Hub behaviour tests |

## Public API

```csharp
namespace Soulvail.Core.Ports;

public interface IDomainEvents
{
    void Publish<T>(in T evt) where T : struct;
}
```

```csharp
namespace Soulvail.Game.Adapters;

public sealed class DomainEventHub : IDomainEvents, IDisposable
{
    public IDisposable Subscribe<T>(Action<T> handler) where T : struct;
    public int SubscriberCount<T>() where T : struct;
    public void Publish<T>(in T evt) where T : struct;
    public void Dispose();                       // removes every subscription
}
```

```csharp
namespace Soulvail.Tests.Core.Fakes;

public sealed class RecordingEvents : IDomainEvents
{
    public IReadOnlyList<object> All { get; }              // boxed, in publish order — tests only
    public IReadOnlyList<T> Of<T>() where T : struct;
    public int Count<T>() where T : struct;
    public T Single<T>() where T : struct;                 // throws if not exactly one
    public void Clear();
}
```

## Behaviour

1. `Publish` with no subscribers for `T` is a no-op.
2. Every subscriber for `T` receives a copy of the payload, in subscription order.
3. Subscribers for other types are not invoked.
4. Disposing the handle returned by `Subscribe` removes exactly that handler. Disposing twice is safe.
5. A `Subscribe` or handle `Dispose` performed **during** a `Publish` of the same `T` takes effect after that publish completes — the in-flight publish sees the subscriber list as it was when publish started.
6. A handler that throws does not prevent later handlers from running; the exception is rethrown after all handlers ran (wrapped in `AggregateException` if more than one).
7. `Hub.Dispose()` removes every subscription; `Publish` afterwards is a no-op; `Subscribe` afterwards throws `ObjectDisposedException`.
8. `Publish` allocates nothing after the first publish of each `T` (per-type channel created lazily, then reused).
9. `RecordingEvents` records every publish in order and never throws.

## Tests

| Test | Given / When / Then |
|---|---|
| `Publish_NoSubscribers_DoesNotThrow` | empty hub / Publish(A) / no exception |
| `Subscribe_ThenPublish_HandlerReceivesPayload` | handler for A / Publish(A{x=3}) / handler saw x == 3 |
| `Publish_InvokesSubscribersInOrder` | h1, h2 for A / Publish / log [h1, h2] |
| `Publish_OtherType_NotInvoked` | handler for A / Publish(B) / handler not called |
| `DisposeHandle_RemovesOnlyThatHandler` | h1, h2 / dispose h1, Publish / only h2 called |
| `DisposeHandle_Twice_IsSafe` | handle / dispose twice / no exception, count == 0 |
| `SubscribeDuringPublish_TakesEffectAfter` | h1 subscribes h2 inside its handler / Publish / h2 not called this publish; called on the next |
| `UnsubscribeDuringPublish_StillReceivesCurrent` | h1 disposes h2's handle during publish / Publish / h2 still called this publish; not on the next |
| `HandlerThrows_LaterHandlersStillRun_ExceptionRethrown` | h1 throws, h2 records / Publish / h2 called; exception propagates |
| `HubDispose_ClearsAll_PublishNoOp_SubscribeThrows` | subs / Dispose / count 0; Publish silent; Subscribe → `ObjectDisposedException` |
| `Publish_AfterWarmup_AllocatesNothing` | one subscriber, one warm-up publish / 10 000 publishes / allocated-bytes delta == 0 |
| `RecordingEvents_RecordsInOrder_AndFiltersByType` | record A, B, A / `Of<A>()` / two A's in order; `Count<B>()` == 1 |
| `RecordingEvents_Single_ThrowsWhenNotExactlyOne` | record A twice / `Single<A>()` / throws |

## Acceptance

- [x] All tests green — 28 passed, 0 failed, via `TestRunnerApi`
- [x] Zero errors, zero new analyzer warnings
- [x] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Any concrete event type — those come with the modules that publish them (first in M0-09).
- Async handlers, filters, priorities. MessagePipe is the upgrade path if ever needed (ADR-0004).
- Registering the hub in a container — M0-12.

## As built

**Files:** 5, not 4. The four in the table, plus `Tests/Core/Fakes/RecordingEventsTests.cs` — the Tests table's two `RecordingEvents_*` rows had no honest fixture to live in, since the only test file listed is the hub's, in a different assembly. Owner approved the fifth file before implementation. One additive edit outside the table: `Soulvail.Tests.Game.asmdef` gained a `Soulvail.Tests.Core` reference so the allocation row can reach `AllocationAssert`.

**Design:** `Publish` walks the live handler list and defers subscribe/unsubscribe until the publish unwinds, rather than snapshotting the list per publish — snapshotting satisfies rule 5 but allocates, breaking rule 8. Deferral also makes re-entrant publish of the same type safe by construction. Channels are created by `Subscribe`, never by `Publish`, so rule 8 holds more strongly than specced: publishing to no subscribers allocates nothing at all, not just nothing after the first time. A lone throwing handler is rethrown via `ExceptionDispatchInfo` to preserve its stack.

**Verification:** compile clean, zero errors, zero warnings. **Full EditMode suite run through `TestRunnerApi`: 28 passed, 0 failed, 0 skipped, 0 inconclusive** — the 13 new tests plus M0-02's 15. The allocation rule was additionally cross-checked with an allocating control body, to confirm `AllocationAssert` was measuring rather than sitting inert.

**Deviation from the Tests table:** the allocation row's "allocated-bytes delta == 0" is measured with `AllocationAssert`; that phrasing names the BCL probe M0-02 proved inert on this runtime. Rule 8 is unchanged.
