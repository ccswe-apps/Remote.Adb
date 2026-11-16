using System.Diagnostics.CodeAnalysis;

namespace Remote.Adb.Console.UnitTests;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class ConsoleArgumentsTests
{
    public class When_Operand_Is_Called : ConsoleArgumentsTests
    {
        [Test]
        public void It_returns_the_token_after_the_subcommand()
        {
            Assert.That(ConsoleArguments.Operand(["start", "Pixel_8"]), Is.EqualTo("Pixel_8"));
        }

        [TestCase]
        [TestCase("start")]
        [TestCase("start", "")]
        [TestCase("start", "  ")]
        public void It_returns_null_when_the_operand_is_missing_or_blank(params string[] args)
        {
            Assert.That(ConsoleArguments.Operand(args), Is.Null);
        }
    }

    public class When_StripVerbose_Is_Called : ConsoleArgumentsTests
    {
        [Test]
        public void It_removes_the_flag_wherever_it_appears()
        {
            var args = ConsoleArguments.StripVerbose(["--verbose", "emulator", "list", "--verbose"], out var verbose);

            Assert.Multiple(() =>
            {
                Assert.That(args, Is.EqualTo(new[] { "emulator", "list" }));
                Assert.That(verbose, Is.True);
            });
        }

        [Test]
        public void It_reports_not_verbose_when_the_flag_is_absent()
        {
            var args = ConsoleArguments.StripVerbose(["emulator", "list"], out var verbose);

            Assert.Multiple(() =>
            {
                Assert.That(args, Is.EqualTo(new[] { "emulator", "list" }));
                Assert.That(verbose, Is.False);
            });
        }
    }

    public class When_TryParseCreate_Is_Called : ConsoleArgumentsTests
    {
        [Test]
        public void It_parses_the_required_flags_and_overrides_in_any_order()
        {
            var parsed = ConsoleArguments.TryParseCreate(["--hw.ramSize", "4096", "-d", "pixel_8", "-n", "Test", "-k", "system-images;android-35;google_apis;x86_64"], out var arguments, out var error);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.True);
                Assert.That(error, Is.Null);
                Assert.That(arguments!.Name, Is.EqualTo("Test"));
                Assert.That(arguments.Package, Is.EqualTo("system-images;android-35;google_apis;x86_64"));
                Assert.That(arguments.Device, Is.EqualTo("pixel_8"));
                Assert.That(arguments.Overrides, Is.EqualTo(new Dictionary<string, string> { ["hw.ramSize"] = "4096" }));
            });
        }

        [Test]
        public void It_accepts_an_equals_override_whose_value_starts_with_dashes()
        {
            var parsed = ConsoleArguments.TryParseCreate(["-n", "Test", "-k", "pkg", "-d", "dev", "--extra=--flag"], out var arguments, out _);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.True);
                Assert.That(arguments!.Overrides["extra"], Is.EqualTo("--flag"));
            });
        }

        [TestCase("-n")]
        [TestCase("-k")]
        [TestCase("-d")]
        public void It_reports_a_missing_value_when_a_flag_is_the_last_token(string flag)
        {
            var parsed = ConsoleArguments.TryParseCreate(["-n", "Test", "-k", "pkg", "-d", "dev", flag], out var arguments, out var error);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.False);
                Assert.That(arguments, Is.Null);
                Assert.That(error, Is.EqualTo($"Missing value for {flag}."));
            });
        }

        [Test]
        public void It_does_not_take_the_next_flag_as_a_value()
        {
            var parsed = ConsoleArguments.TryParseCreate(["-n", "-k", "pkg", "-d", "dev"], out _, out var error);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.False);
                Assert.That(error, Is.EqualTo("Missing value for -n."));
            });
        }

        [Test]
        public void It_rejects_an_override_without_a_value()
        {
            var parsed = ConsoleArguments.TryParseCreate(["-n", "Test", "-k", "pkg", "-d", "dev", "--hw.ramSize"], out _, out var error);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.False);
                Assert.That(error, Is.EqualTo("Missing value for --hw.ramSize."));
            });
        }

        [TestCase("--")]
        [TestCase("stray")]
        [TestCase("--=value")]
        public void It_rejects_a_token_that_is_not_an_option(string token)
        {
            var parsed = ConsoleArguments.TryParseCreate(["-n", "Test", "-k", "pkg", "-d", "dev", token], out _, out var error);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.False);
                Assert.That(error, Does.StartWith("Expected --<key>"));
            });
        }

        [Test]
        public void It_returns_the_usage_when_a_required_flag_is_missing()
        {
            var parsed = ConsoleArguments.TryParseCreate(["-n", "Test", "-k", "pkg"], out _, out var error);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.False);
                Assert.That(error, Does.StartWith("Usage: emulator create"));
            });
        }
    }

    public class When_TryParseEdits_Is_Called : ConsoleArgumentsTests
    {
        [Test]
        public void It_parses_changes_and_bare_keys_as_removals()
        {
            var parsed = ConsoleArguments.TryParseEdits(["--hw.ramSize", "4096", "--sdcard.size", "--hw.lcd.density=420"], out var edits, out var error);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.True);
                Assert.That(error, Is.Null);
                Assert.That(edits!.Changes, Is.EqualTo(new Dictionary<string, string> { ["hw.ramSize"] = "4096", ["hw.lcd.density"] = "420" }));
                Assert.That(edits.Removals, Is.EqualTo(new[] { "sdcard.size" }));
            });
        }

        [Test]
        public void It_treats_a_key_followed_by_a_dashed_value_as_a_removal_then_another_key()
        {
            var parsed = ConsoleArguments.TryParseEdits(["--a", "--b", "1"], out var edits, out _);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.True);
                Assert.That(edits!.Removals, Is.EqualTo(new[] { "a" }));
                Assert.That(edits.Changes, Is.EqualTo(new Dictionary<string, string> { ["b"] = "1" }));
            });
        }

        [Test]
        public void It_sets_a_value_starting_with_dashes_through_the_equals_form()
        {
            var parsed = ConsoleArguments.TryParseEdits(["--a=--b"], out var edits, out _);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.True);
                Assert.That(edits!.Changes, Is.EqualTo(new Dictionary<string, string> { ["a"] = "--b" }));
            });
        }

        [Test]
        public void It_reports_no_edits_for_no_tokens()
        {
            var parsed = ConsoleArguments.TryParseEdits([], out var edits, out _);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.True);
                Assert.That(edits!.IsEmpty, Is.True);
            });
        }

        [TestCase("--")]
        [TestCase("value")]
        public void It_rejects_a_token_that_is_not_an_option(string token)
        {
            var parsed = ConsoleArguments.TryParseEdits([token], out var edits, out var error);

            Assert.Multiple(() =>
            {
                Assert.That(parsed, Is.False);
                Assert.That(edits, Is.Null);
                Assert.That(error, Is.EqualTo($"Expected --<key>, got '{token}'."));
            });
        }
    }
}
