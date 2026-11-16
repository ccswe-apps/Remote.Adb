using Remote.Adb.Desktop.Common.Threading;

namespace Remote.Adb.Desktop.UnitTests.Fakes;

/// <summary>An <see cref="IUiDispatcher"/> that queues posted actions until <see cref="RunAll"/> is called.</summary>
public sealed class DeferredUiDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _pending = new();

    public int PendingCount => _pending.Count;

    public void Post(Action action) => _pending.Enqueue(action);

    public void RunAll()
    {
        while (_pending.TryDequeue(out var action))
        {
            action();
        }
    }
}
