using System.Net;
using System.Net.Sockets;

namespace ChiakiNg.Protocol;

/// <summary>What one probe exchange produced.</summary>
/// <param name="Verdict">How the answer was judged, or null where none came.</param>
/// <param name="Step">What the core's rules would do with it.</param>
/// <param name="Echo">The five bytes that came back, where an answer did.</param>
/// <param name="Faulted">
/// Whether the receive itself failed. Separate from <paramref name="Step"/> because the core's
/// answer for this is to go round again - see <see cref="ProbeExchange"/>.
/// </param>
public readonly record struct ExchangeResult(
    ResponseVerdict? Verdict, FollowupStep Step, byte[]? Echo, bool Faulted = false);

/// <summary>
/// PP268: one probe, sent and answered, over a real socket.
///
/// PP243 builds the probe and PP236 the reply. Six classes decide what to do with each -
/// <see cref="ProbeSend"/>, <see cref="CandidateWait"/>, <see cref="ResponseIntake"/>,
/// <see cref="ResponseCheck"/> among them - and not one of them sends a datagram. So the layout
/// those two builders agree on has never crossed a socket, and a byte at the wrong offset is exactly
/// the mistake their agreement cannot catch: both would be wrong together.
///
/// This is the exchange. Build, send, wait, judge, and answer in the verdict PP247 named. Nothing
/// here chooses between candidates or opens more than one socket - the race, the port guessing and
/// the event loop are what turn one exchange into a choice, and they belong to PP28. This is what
/// they are made of.
///
/// The measured behaviour is carried: a response whose echo does not match is dropped without an
/// error, a derived candidate keeps the exemption PP247 measured, and silence after an answer is
/// still success rather than a timeout.
/// </summary>
public sealed class ProbeExchange : IDisposable
{
    private readonly Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    /// <summary>The port this side bound, which is what a local candidate advertises.</summary>
    public int LocalPort { get; private set; }

    /// <summary>Binds to an ephemeral port and reports which one.</summary>
    public int Bind()
    {
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        LocalPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
        return LocalPort;
    }

    /// <summary>
    /// Sends one probe and waits for what answers it.
    /// </summary>
    /// <param name="to">Where the candidate is.</param>
    /// <param name="requestId">The five bytes this probe will be recognised by.</param>
    /// <param name="localId">This side's hashed identifier.</param>
    /// <param name="consoleId">The console's.</param>
    /// <param name="sidLocal">This side's session id.</param>
    /// <param name="sidConsole">The console's.</param>
    /// <param name="timeout">How long to wait, which is PP245's short window unless given.</param>
    public async Task<ExchangeResult> ExchangeAsync(
        IPEndPoint to,
        byte[] requestId,
        byte[] localId,
        byte[] consoleId,
        ushort sidLocal,
        ushort sidConsole,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(requestId);

        byte[] probe = PunchProbe.Build(requestId, localId, consoleId, sidLocal, sidConsole);

        await socket.SendToAsync(probe, SocketFlags.None, to, cancellationToken).ConfigureAwait(false);

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(timeout ?? TimeSpan.FromMicroseconds(CandidateWait.ShortWindowUs));

        byte[] buffer = new byte[PunchProbe.Length * 2];

        SocketReceiveFromResult received;
        try
        {
            received = await socket.ReceiveFromAsync(
                buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), bounded.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Nothing came. PP256's ordinary ending, and which of the two it is depends on whether
            // anything ever had - a question this single exchange answers with "no".
            return new ExchangeResult(null, FollowupStep.TimedOut, null);
        }
        catch (SocketException)
        {
            // PP256 FROM THE OTHER SIDE. A datagram to a closed port draws an ICMP rejection, which
            // Windows delivers as a failure on the NEXT receive of a connected socket - the exact
            // condition PP256 measured the core continuing on, with no exit behind it. The step is
            // reported as the core's, and the fault is reported beside it so a caller here does not
            // have to spin to find out.
            return new ExchangeResult(null, FollowupStep.Retry, null, Faulted: true);
        }

        return Judge(buffer.AsSpan(0, received.ReceivedBytes), requestId);
    }

    /// <summary>
    /// What one datagram is, by the rules already ported.
    /// </summary>
    public static ExchangeResult Judge(ReadOnlySpan<byte> datagram, ReadOnlySpan<byte> sent)
    {
        uint messageType = datagram.Length >= 4
            ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(datagram)
            : 0;

        ReadOnlySpan<byte> echo = datagram.Length >= ResponseCheck.EchoAt + ResponseCheck.EchoLength
            ? datagram.Slice(ResponseCheck.EchoAt, ResponseCheck.EchoLength)
            : [];

        ResponseVerdict verdict = ResponseCheck.Verdict(datagram.Length, messageType, echo, sent);

        FollowupStep step = verdict switch
        {
            ResponseVerdict.Accepted => FollowupStep.Done,
            ResponseVerdict.ConsoleProbing => FollowupStep.Answer,
            _ => FollowupStep.Retry,
        };

        return new ExchangeResult(verdict, step, echo.IsEmpty ? null : echo.ToArray());
    }

    /// <summary>
    /// The reply this side would send to a console's own probe, which is PP236's packet.
    /// </summary>
    public static byte[]? ReplyTo(
        ReadOnlySpan<byte> request,
        byte[] localId,
        byte[] consoleId,
        ushort sidLocal,
        ushort sidConsole,
        string address,
        ushort port)
        => PunchResponse.Build(request, localId, consoleId, sidLocal, sidConsole, address, port);

    /// <summary>Releases the socket.</summary>
    public void Dispose()
    {
        socket.Dispose();
        GC.SuppressFinalize(this);
    }
}
