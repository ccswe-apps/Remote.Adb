using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Moq;
using Remote.Adb.Core.Logging;
using Remote.Adb.Core.Settings;
using Remote.Adb.Core.Tunnel;
using Remote.Adb.Desktop.Shell;
using Remote.Adb.Desktop.UnitTests.Fakes;

namespace Remote.Adb.Desktop.UnitTests.Shell;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class StatusBarViewModelTests
{
    protected Mock<ISettingsService> Settings = null!;
    protected LogStore Store = null!;
    protected Mock<ITunnelService> Tunnel = null!;
    protected TunnelStatus TunnelStatus = null!;

    [SetUp]
    public void SetUp()
    {
        Store = new LogStore();
        TunnelStatus = new TunnelStatus(TunnelState.Disconnected, null);
        Tunnel = new Mock<ITunnelService>();
        Tunnel.Setup(tunnel => tunnel.Status).Returns(() => TunnelStatus);
        Settings = new Mock<ISettingsService>();
        Settings.Setup(settings => settings.TunnelHost).Returns("devbox");
    }

    protected StatusBarViewModel CreateViewModel() => new(Store, Tunnel.Object, Settings.Object, new FakeUiDispatcher());

    protected void LogCompleted(string command, int exitCode, long elapsedMilliseconds) =>
        Store.Add(
            exitCode == 0 ? LogLevel.Information : LogLevel.Warning,
            "Remote.Adb.Core.Common.ProcessRunner",
            new EventId(LogEvents.CommandCompleted),
            $"{command} exited with code {exitCode} in {elapsedMilliseconds} ms",
            null,
            new Dictionary<string, object?>
            {
                [LogEvents.CommandProperty] = command,
                [LogEvents.ExitCodeProperty] = exitCode,
                [LogEvents.ElapsedMillisecondsProperty] = elapsedMilliseconds,
            });

    protected void SetTunnel(TunnelState state, string? message = null)
    {
        TunnelStatus = new TunnelStatus(state, message);
        Tunnel.Raise(tunnel => tunnel.StatusChanged += null, Tunnel.Object, TunnelStatus);
    }

    public class When_A_Command_Completes : StatusBarViewModelTests
    {
        [Test]
        public void It_shows_the_command_and_its_duration()
        {
            var viewModel = CreateViewModel();

            LogCompleted("adb devices -l", 0, 120);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.LastCommand, Is.EqualTo("adb devices -l"));
                Assert.That(viewModel.LastCommandResult, Is.EqualTo("✓ 120 ms"));
                Assert.That(viewModel.LastCommandFailed, Is.False);
            });
        }

        [Test]
        public void It_flags_a_non_zero_exit()
        {
            var viewModel = CreateViewModel();

            LogCompleted("adb connect 10.0.0.5", 1, 40);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.LastCommandResult, Is.EqualTo("✗ exit 1 · 40 ms"));
                Assert.That(viewModel.LastCommandFailed, Is.True);
            });
        }

        [Test]
        public void It_shows_the_last_command_logged_before_it_existed()
        {
            LogCompleted("ssh -N -R 5037:127.0.0.1:5037 devbox", 0, 900);

            var viewModel = CreateViewModel();

            Assert.That(viewModel.LastCommand, Is.EqualTo("ssh -N -R 5037:127.0.0.1:5037 devbox"));
        }

        [Test]
        public void It_ignores_entries_that_are_not_command_outcomes()
        {
            var viewModel = CreateViewModel();

            Store.Add(LogLevel.Information, "Category", default, "something else", null);

            Assert.That(viewModel.HasLastCommand, Is.False);
        }
    }

    public class When_Problems_Are_Logged : StatusBarViewModelTests
    {
        [Test]
        public void It_counts_warnings_and_errors_while_the_log_is_hidden()
        {
            var viewModel = CreateViewModel();

            Store.Add(LogLevel.Information, "Category", default, "fine", null);
            Store.Add(LogLevel.Warning, "Category", default, "hmm", null);
            Store.Add(LogLevel.Error, "Category", default, "bad", null);

            Assert.That(viewModel.UnseenProblemCount, Is.EqualTo(2));
        }

        [Test]
        public void It_clears_the_count_when_the_log_is_shown()
        {
            var viewModel = CreateViewModel();
            Store.Add(LogLevel.Error, "Category", default, "bad", null);

            viewModel.SetLogVisible(true);

            Assert.That(viewModel.HasUnseenProblems, Is.False);
        }

        [Test]
        public void It_does_not_count_while_the_log_is_shown()
        {
            var viewModel = CreateViewModel();
            viewModel.SetLogVisible(true);

            Store.Add(LogLevel.Error, "Category", default, "bad", null);

            Assert.That(viewModel.UnseenProblemCount, Is.Zero);
        }

        [Test]
        public void It_caps_the_badge_text()
        {
            var viewModel = CreateViewModel();

            for (var index = 0; index < 120; index++)
            {
                Store.Add(LogLevel.Warning, "Category", default, "hmm", null);
            }

            Assert.That(viewModel.UnseenProblemText, Is.EqualTo("99+"));
        }
    }

    public class When_The_Tunnel_Changes : StatusBarViewModelTests
    {
        [Test]
        public void It_names_the_host_when_connected()
        {
            var viewModel = CreateViewModel();

            SetTunnel(TunnelState.Connected, "5037 → 127.0.0.1:5037");

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.TunnelText, Is.EqualTo("Tunnel · devbox"));
                Assert.That(viewModel.IsTunnelConnected, Is.True);
                Assert.That(viewModel.TunnelToolTip, Is.EqualTo("5037 → 127.0.0.1:5037"));
            });
        }

        [TestCase(TunnelState.Connecting, true, false)]
        [TestCase(TunnelState.Reconnecting, true, false)]
        [TestCase(TunnelState.Faulted, false, true)]
        [TestCase(TunnelState.Disconnected, false, false)]
        public void It_reflects_the_state(TunnelState state, bool busy, bool faulted)
        {
            var viewModel = CreateViewModel();

            SetTunnel(state);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.IsTunnelBusy, Is.EqualTo(busy));
                Assert.That(viewModel.IsTunnelFaulted, Is.EqualTo(faulted));
            });
        }
    }
}
