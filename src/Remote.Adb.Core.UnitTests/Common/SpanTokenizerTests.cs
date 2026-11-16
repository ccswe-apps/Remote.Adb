using System.Diagnostics.CodeAnalysis;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.UnitTests.Common;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class SpanTokenizerTests
{
    public class When_NextToken_Is_Called : SpanTokenizerTests
    {
        [Test]
        public void It_splits_on_runs_of_spaces_and_tabs()
        {
            var rest = "  serial \t device  model:Pixel ".AsSpan();
            var tokens = new List<string>();

            for (var token = SpanTokenizer.NextToken(ref rest); !token.IsEmpty; token = SpanTokenizer.NextToken(ref rest))
            {
                tokens.Add(token.ToString());
            }

            Assert.That(tokens, Is.EqualTo(new[] { "serial", "device", "model:Pixel" }));
        }
    }

    public class When_TokenAfter_Is_Called : SpanTokenizerTests
    {
        [TestCase("default via 192.168.1.1 dev wlan0 src 192.168.1.20", "src", ExpectedResult = "192.168.1.20")]
        [TestCase("default via 192.168.1.1 dev wlan0 src 192.168.1.20", "dev", ExpectedResult = "wlan0")]
        [TestCase("inet\t10.0.0.2/24 brd", "inet", ExpectedResult = "10.0.0.2/24")]
        [TestCase("src", "src", ExpectedResult = "")]
        [TestCase("nosrc 1.2.3.4", "src", ExpectedResult = "")]
        public string It_returns_the_token_following_the_keyword(string line, string keyword) =>
            SpanTokenizer.TokenAfter(line, keyword).ToString();
    }
}
