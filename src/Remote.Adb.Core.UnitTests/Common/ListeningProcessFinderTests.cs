using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Moq;
using Remote.Adb.Core.Common;

namespace Remote.Adb.Core.UnitTests.Common;

/// <summary>The pure parsing behind each platform's <see cref="IListeningProcessFinder"/>.</summary>
[SuppressMessage("ReSharper", "InconsistentNaming")]
public class ListeningProcessFinderTests
{
    public class When_LinuxListeningProcessFinder_Parses : ListeningProcessFinderTests
    {
        private const string ProcNetTcp =
            "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\n" +
            "   0: 0100007F:13AD 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 98765 1 0000000000000000 100 0 0 10 0\n" +
            "   1: 0100007F:13AD 0100007F:9C40 01 00000000:00000000 00:00000000 00000000  1000        0 11111 1 0000000000000000 20 4 30 10 -1\n" +
            "   2: 00000000:0016 00000000:0000 0A 00000000:00000000 00:00000000 00000000     0        0 22222 1 0000000000000000 100 0 0 10 0\n";

        [Test]
        public void It_finds_the_inode_of_the_listening_socket_on_the_port()
        {
            Assert.That(LinuxListeningProcessFinder.ParseListeningInode(ProcNetTcp, 5037), Is.EqualTo(98765UL));
        }

        [Test]
        public void It_handles_crlf_line_endings()
        {
            Assert.That(LinuxListeningProcessFinder.ParseListeningInode(ProcNetTcp.Replace("\n", "\r\n"), 22), Is.EqualTo(22222UL));
        }

        [Test]
        public void It_returns_null_when_nothing_listens_on_the_port()
        {
            Assert.That(LinuxListeningProcessFinder.ParseListeningInode(ProcNetTcp, 5038), Is.Null);
        }

        [TestCase("adb\0-L\0tcp:5037\0fork-server\0server\0--reply-fd\04\0", 5037, ExpectedResult = true)]
        [TestCase("adb\0-L\0tcp:localhost:5038\0fork-server\0server\0", 5038, ExpectedResult = true)]
        [TestCase("adb\0-L\0tcp:5037\0fork-server\0server\0", 5038, ExpectedResult = false)]
        [TestCase("adb\0-L\0tcp:15037\0fork-server\0server\0", 5037, ExpectedResult = false)]
        [TestCase("adb\0devices\0", 5037, ExpectedResult = false)]
        public bool It_recognizes_the_adb_server_command_line_for_the_port(string commandLine, int port) =>
            LinuxListeningProcessFinder.IsServerCommandLine(commandLine, port);
    }

    public class When_MacListeningProcessFinder_Runs_Lsof : ListeningProcessFinderTests
    {
        [Test]
        public async Task It_returns_the_first_listening_process_id()
        {
            var runner = new Mock<IProcessRunner>();
            runner
                .Setup(r => r.RunAsync(It.IsAny<string>(), It.Is<IReadOnlyList<string>>(args => args.Contains("-iTCP:5037")), null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult(0, "4242\n4243\n", string.Empty));

            var processId = await new MacListeningProcessFinder(runner.Object).FindAsync(5037);

            Assert.That(processId, Is.EqualTo(4242));
        }

        [Test]
        public async Task It_returns_null_when_lsof_finds_nothing()
        {
            var runner = new Mock<IProcessRunner>();
            runner
                .Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ProcessResult(1, string.Empty, string.Empty));

            var processId = await new MacListeningProcessFinder(runner.Object).FindAsync(5037);

            Assert.That(processId, Is.Null);
        }

        [Test]
        public async Task It_returns_null_when_lsof_cannot_be_launched()
        {
            var runner = new Mock<IProcessRunner>();
            runner
                .Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), null, null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ProcessLaunchException("lsof", new InvalidOperationException()));

            var processId = await new MacListeningProcessFinder(runner.Object).FindAsync(5037);

            Assert.That(processId, Is.Null);
        }
    }

    public class When_WindowsListeningProcessFinder_Reads_The_Tcp_Table : ListeningProcessFinderTests
    {
        // A MIB_TCPTABLE_OWNER_PID: the row count, then six little-endian DWORDs per row with the port in network order.
        private static byte[] Table(params (int Port, int ProcessId)[] rows)
        {
            var table = new byte[sizeof(uint) + rows.Length * 24];
            BinaryPrimitives.WriteUInt32LittleEndian(table, (uint)rows.Length);

            for (var index = 0; index < rows.Length; index++)
            {
                var row = table.AsSpan(sizeof(uint) + index * 24, 24);
                BinaryPrimitives.WriteUInt32LittleEndian(row, 2);
                BinaryPrimitives.WriteUInt32LittleEndian(row[4..], 0x0100007F);
                BinaryPrimitives.WriteUInt16BigEndian(row[8..], (ushort)rows[index].Port);
                BinaryPrimitives.WriteUInt32LittleEndian(row[20..], (uint)rows[index].ProcessId);
            }

            return table;
        }

        [Test]
        public void It_finds_the_owner_of_the_row_on_the_port()
        {
            var table = Table((135, 900), (5037, 4242), (5555, 7));

            Assert.That(WindowsListeningProcessFinder.FindInTable(table, 5037), Is.EqualTo(4242));
        }

        [Test]
        public void It_returns_null_when_no_row_listens_on_the_port()
        {
            var table = Table((135, 900));

            Assert.That(WindowsListeningProcessFinder.FindInTable(table, 5037), Is.Null);
        }

        [Test]
        public void It_returns_null_for_a_truncated_table()
        {
            Assert.That(WindowsListeningProcessFinder.FindInTable(new byte[2], 5037), Is.Null);
        }
    }
}
