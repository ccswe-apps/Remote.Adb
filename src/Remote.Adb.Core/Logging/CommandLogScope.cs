using JetBrains.Annotations;

namespace Remote.Adb.Core.Logging;

/// <summary>
/// Marks the commands run within it as routine background work (e.g. a list page's periodic re-list), so they log
/// at Debug instead of Information and don't drown out user-initiated activity. The flag flows across
/// <c>await</c>s via <see cref="AsyncLocal{T}"/>.
/// </summary>
[PublicAPI]
public static class CommandLogScope
{
    private static readonly AsyncLocal<bool> BackgroundFlag = new();

    /// <summary>Whether the current async flow is inside a <see cref="Background"/> scope.</summary>
    internal static bool IsBackground => BackgroundFlag.Value;

    /// <summary>Enters a background scope; dispose it to restore the previous state.</summary>
    public static IDisposable Background()
    {
        var previous = BackgroundFlag.Value;
        BackgroundFlag.Value = true;
        return new Restore(previous);
    }

    private sealed class Restore : IDisposable
    {
        private readonly bool _previous;

        public Restore(bool previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            BackgroundFlag.Value = _previous;
        }
    }
}
