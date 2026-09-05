using System.Runtime.InteropServices;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP714: packetstats.c driven by the shim, so the managed arithmetic is held to the C's.
///
/// One call rather than a handle with four methods on it. The reset is the behaviour worth a
/// differential and it is only visible ACROSS two reads - the first closing a window, the second
/// showing what the floor was moved to - so the oracle takes the whole scenario and answers both.
/// </summary>
public static class NativePacketStats
{
    /// <summary>
    /// Push the generations, push the sequence numbers, read twice - with the second batch of
    /// sequence numbers arriving AFTER the first read, which is the only way the second window is
    /// anything but empty.
    /// </summary>
    /// <param name="generations">Each frame's received and lost, in order.</param>
    /// <param name="before">Sequence numbers arriving before the first read.</param>
    /// <param name="after">Sequence numbers arriving after it, into the window it opened.</param>
    /// <param name="reset">Whether the FIRST read closes the window. The second never does.</param>
    /// <returns>The first read, then the second.</returns>
    public static (PacketWindow First, PacketWindow Second) Run(
        IReadOnlyList<(ulong Received, ulong Lost)> generations,
        IReadOnlyList<ushort> before,
        IReadOnlyList<ushort> after,
        bool reset)
    {
        ArgumentNullException.ThrowIfNull(generations);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        ulong[] genReceived = [.. generations.Select(one => one.Received)];
        ulong[] genLost = [.. generations.Select(one => one.Lost)];
        ushort[] numbers = [.. before, .. after];

        var received = new ulong[2];
        var lost = new ulong[2];

        int err = PacketStatsRun(
            genReceived, genLost, genReceived.Length,
            numbers, numbers.Length, before.Count, reset, received, lost);

        if (err != (int)ChiakiError.Success)
            throw new InvalidOperationException($"chiaki_shim_packet_stats_run failed: {(ChiakiError)err}.");

        return (new PacketWindow(received[0], lost[0]), new PacketWindow(received[1], lost[1]));
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_packet_stats_run",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int PacketStatsRun(
        ulong[] genReceived, ulong[] genLost, int genCount,
        ushort[] seqs, int seqCount, int seqSplit,
        [MarshalAs(UnmanagedType.I1)] bool reset,
        ulong[] received, ulong[] lost);
}
