using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Adb;
using Remote.Adb.Core.Common;
using Remote.Adb.Testing;

namespace Remote.Adb.Core.UnitTests.Adb;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public class AdbServerProcessesTests
{
    private protected const int Port = 5037;
    private protected const int ServerProcessId = 4242;

    private protected Mock<IListeningProcessFinder> ListeningProcesses = null!;
    private protected Mock<IPortProbe> PortProbe = null!;
    private protected Mock<ISystemProcesses> Processes = null!;

    private protected AdbServerProcesses CreateProcesses() =>
        new(ListeningProcesses.Object, Processes.Object, PortProbe.Object, new LoggerFake<AdbServerProcesses>());

    private protected void ListenerIs(int? processId, string? name = "adb")
    {
        ListeningProcesses.Setup(finder => finder.FindAsync(Port, It.IsAny<CancellationToken>())).ReturnsAsync(processId);
        if (processId is not null)
        {
            Processes.Setup(processes => processes.GetName(processId.Value)).Returns(name);
        }
    }

    // What the port probe reports on successive checks.
    private protected void PortListening(params bool[] listening)
    {
        var sequence = PortProbe.SetupSequence(probe => probe.IsListeningAsync(Port, It.IsAny<CancellationToken>()));
        foreach (var value in listening)
        {
            sequence = sequence.ReturnsAsync(value);
        }
    }

    [SetUp]
    public void SetUp()
    {
        ListeningProcesses = new Mock<IListeningProcessFinder>();
        PortProbe = new Mock<IPortProbe>();
        Processes = new Mock<ISystemProcesses>();
        Processes.Setup(processes => processes.KillAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    private protected void VerifyKills(Times times) =>
        Processes.Verify(processes => processes.KillAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), times);

    public class When_KillAsync_Is_Called : AdbServerProcessesTests
    {
        [Test]
        public async Task It_fails_when_the_port_is_still_taken_after_the_kill()
        {
            ListenerIs(ServerProcessId);
            PortListening(true);

            var result = await CreateProcesses().KillAsync(Port);

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task It_fails_when_the_server_does_not_exit_in_time()
        {
            ListenerIs(ServerProcessId);
            Processes.Setup(processes => processes.KillAsync(ServerProcessId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var result = await CreateProcesses().KillAsync(Port);

            Assert.That(result, Is.False);
            PortProbe.Verify(probe => probe.IsListeningAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task It_fails_without_killing_when_a_listener_cannot_be_identified()
        {
            ListenerIs(null);
            PortListening(true);

            var result = await CreateProcesses().KillAsync(Port);

            Assert.That(result, Is.False);
            VerifyKills(Times.Never());
        }

        [Test]
        public async Task It_kills_only_the_adb_process_listening_on_the_port_and_confirms_the_port_is_free()
        {
            ListenerIs(ServerProcessId);
            PortListening(false);
            var processes = CreateProcesses();

            var result = await processes.KillAsync(Port);

            Assert.That(result, Is.True);
            Processes.Verify(p => p.KillAsync(ServerProcessId, processes.KillTimeout, It.IsAny<CancellationToken>()), Times.Once);
            VerifyKills(Times.Once());
            PortProbe.Verify(probe => probe.IsListeningAsync(Port, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task It_leaves_a_listener_that_is_not_adb_alone()
        {
            ListenerIs(ServerProcessId, name: "java");

            var result = await CreateProcesses().KillAsync(Port);

            Assert.That(result, Is.False);
            VerifyKills(Times.Never());
        }

        [TestCase("adb")]
        [TestCase("ADB")]
        public async Task It_matches_the_adb_process_name_case_insensitively(string name)
        {
            ListenerIs(ServerProcessId, name);
            PortListening(false);

            var result = await CreateProcesses().KillAsync(Port);

            Assert.That(result, Is.True);
        }

        [Test]
        public async Task It_succeeds_without_killing_when_nothing_is_listening()
        {
            ListenerIs(null);
            PortListening(false);

            var result = await CreateProcesses().KillAsync(Port);

            Assert.That(result, Is.True);
            VerifyKills(Times.Never());
        }

        [Test]
        public async Task It_succeeds_when_the_listener_exited_before_it_could_be_inspected()
        {
            ListenerIs(ServerProcessId, name: null);
            PortListening(false);

            var result = await CreateProcesses().KillAsync(Port);

            Assert.That(result, Is.True);
            VerifyKills(Times.Never());
        }
    }
}
