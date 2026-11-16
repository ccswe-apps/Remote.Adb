namespace Remote.Adb.Core.Common;

/// <summary>
/// The shared whitespace tokenizer for the span-based output parsers: tokens are separated by runs of spaces and tabs.
/// </summary>
internal static class SpanTokenizer
{
    /// <summary>
    /// Returns the next space/tab-delimited token, advancing <paramref name="remaining"/> past it; an empty span when
    /// none remain.
    /// </summary>
    public static ReadOnlySpan<char> NextToken(scoped ref ReadOnlySpan<char> remaining)
    {
        var start = remaining.IndexOfAnyExcept(' ', '\t');
        if (start < 0)
        {
            remaining = [];
            return [];
        }

        var rest = remaining[start..];
        var end = rest.IndexOfAny(' ', '\t');
        var token = end < 0 ? rest : rest[..end];
        remaining = end < 0 ? [] : rest[end..];
        return token;
    }

    /// <summary>
    /// The token following the token <paramref name="keyword"/> in <paramref name="line"/> (e.g. the address after
    /// <c>src</c>), or an empty span when <paramref name="keyword"/> isn't there or is last.
    /// </summary>
    public static ReadOnlySpan<char> TokenAfter(ReadOnlySpan<char> line, string keyword)
    {
        var rest = line;
        for (var token = NextToken(ref rest); !token.IsEmpty; token = NextToken(ref rest))
        {
            if (token.SequenceEqual(keyword))
            {
                return NextToken(ref rest);
            }
        }

        return [];
    }
}
