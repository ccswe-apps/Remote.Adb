using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Remote.Adb.Core.Common;

/// <summary>
/// Finds a port's listener on Windows from the IPv4 TCP table (<c>GetExtendedTcpTable</c> with
/// <c>TCP_TABLE_OWNER_PID_LISTENER</c>), which reports each listening socket's owning process ID. IPv4 is enough: the
/// adb server binds <c>127.0.0.1</c>. Only <see cref="ListeningProcessFinder.Create"/> on Windows constructs it; the
/// table parsing (<see cref="FindInTable"/>) is pure and testable anywhere.
/// </summary>
internal sealed class WindowsListeningProcessFinder : IListeningProcessFinder
{
    private const int AddressFamilyInet = 2;
    private const uint ErrorInsufficientBuffer = 122;

    // MIB_TCPROW_OWNER_PID is six DWORDs: dwState, dwLocalAddr, dwLocalPort, dwRemoteAddr, dwRemotePort, dwOwningPid.
    private const int LocalPortOffset = 8;
    private const int MaxAttempts = 4;
    private const uint NoError = 0;
    private const int OwningPidOffset = 20;
    private const int RowSize = 24;
    private const int TableClassOwnerPidListener = 3;

    private static int? Find(int port)
    {
        var size = 0;

        // The first call asks for the size; the table can grow before the read, so retry with the size it reports.
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var buffer = size == 0 ? IntPtr.Zero : Marshal.AllocHGlobal(size);
            try
            {
                var result = GetExtendedTcpTable(buffer, ref size, false, AddressFamilyInet, TableClassOwnerPidListener, 0);
                if (result == NoError && buffer != IntPtr.Zero)
                {
                    var table = new byte[size];
                    Marshal.Copy(buffer, table, 0, size);
                    return FindInTable(table, port);
                }

                if (result != ErrorInsufficientBuffer)
                {
                    return null;
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public Task<int?> FindAsync(int port, CancellationToken cancellationToken = default) => Task.FromResult(Find(port));

    /// <summary>
    /// Finds the owning process of <paramref name="port"/>'s row in a raw <c>MIB_TCPTABLE_OWNER_PID</c>
    /// (<paramref name="table"/>, little-endian like every Windows target), or <see langword="null"/> when no row
    /// listens on it.
    /// </summary>
    internal static int? FindInTable(ReadOnlySpan<byte> table, int port)
    {
        if (table.Length < sizeof(uint))
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        var rows = table[sizeof(uint)..];

        for (var index = 0; index < count && (index + 1) * RowSize <= rows.Length; index++)
        {
            var row = rows.Slice(index * RowSize, RowSize);

            // The port is the low 16 bits of dwLocalPort, in network (big-endian) byte order.
            var localPort = BinaryPrimitives.ReadUInt16BigEndian(row.Slice(LocalPortOffset, sizeof(ushort)));
            if (localPort == port)
            {
                return (int)BinaryPrimitives.ReadUInt32LittleEndian(row.Slice(OwningPidOffset, sizeof(uint)));
            }
        }

        return null;
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int addressFamily, int tableClass, uint reserved);
}
