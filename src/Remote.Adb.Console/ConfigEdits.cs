namespace Remote.Adb.Console;

/// <summary>The parsed arguments of <c>emulator edit</c>: config keys to set and keys to clear.</summary>
/// <param name="Changes">Keys to set, with their new values.</param>
/// <param name="Removals">Keys to remove.</param>
internal sealed record ConfigEdits(IReadOnlyDictionary<string, string> Changes, IReadOnlyList<string> Removals)
{
    /// <summary>Whether there's nothing to set or clear.</summary>
    public bool IsEmpty => Changes.Count == 0 && Removals.Count == 0;
}
