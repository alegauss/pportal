using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace ChiakiNg.Protocol;

/// <summary>Who this end is, for the reply it will build.</summary>
/// <param name="LocalId">The local hashed identifier, twenty bytes.</param>
/// <param name="ConsoleId">The console's.</param>
/// <param name="SidLocal">This side's session id, which is also half the tail's key.</param>
/// <param name="SidConsole">The console's, which is the other half.</param>
public readonly record struct PunchIdentity(
    byte[] LocalId, byte[] ConsoleId, ushort SidLocal, ushort SidConsole);

/// <summary>How one run of the answering loop ended, and what it did on the way.</summary>
/// <param name="Step">The step that left the loop.</param>
/// <param name="Answered">How many requests were answered.</param>
/// <param name="Ignored">How many extra responses were waited past.</param>
/// <param name="Faulted">
/// How many receives failed. Each one re-entered the wait with the WHOLE timeout, so a non-zero count
/// means the run took longer than the timeout it was given - see the type's note.
/// </param>
public readonly record struct PunchAnsweringOutcome(
    PunchStep Step, int Answered, int Ignored, int Faulted);

/// <summary>
/// PP456: PP238's answering loop, running over a socket - so "succeeds by falling quiet" is a thing
/// that happens rather than a sentence.
///
/// PP238 ported the decision and PP455 the reply it sends. Neither ran the loop, and the loop is
/// where the behaviour actually is: there is NO path in which receiving something returns success.
/// Every request that arrives is answered and the wait is re-entered, and the only way out with
/// success is a timeout after at least one was answered. That is not a rule a decision function can
/// demonstrate - <see cref="PunchExchange.Next"/> can only say what one step is - because the claim
/// is about the shape of the whole run.
///
/// AN IGNORED RESPONSE DOES NOT COUNT AS ANSWERING, which is the one place the two could be confused.
/// An extra response is ordinary and is waited past, and if nothing else arrives the loop times out
/// with an error rather than succeeding. So a console that answers and then says nothing more gets a
/// failure, and a console that asks and then says nothing more gets a success.
///
/// A FAILED RECEIVE COSTS NOTHING, AND THAT IS REPRODUCED. It is counted and the wait is re-entered
/// with the full timeout again, so the timeout bounds SILENCE rather than the run. PP238 named this as
/// the same mistake PP212 measured in the notification wait, and it means the only bound on a run is
/// the caller's cancellation token. It is left that way, and <see cref="PunchAnsweringOutcome.Faulted"/>
/// is how a caller can tell it happened - the C has nothing there but a log line.
/// </summary>
public sealed class PunchAnsweringLoop : IDisposable
{
    private readonly Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

    /// <summary>The port this side bound, which is where a console sends its requests.</summary>
    public int LocalPort { get; private set; }

    /// <summary>Binds to an ephemeral loopback port and reports which one.</summary>
    public int Bind()
    {
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        LocalPort = ((IPEndPoint)socket.LocalEndPoint!).Port;
        return LocalPort;
    }

    /// <summary>
    /// Runs the loop until a step leaves it.
    /// </summary>
    /// <param name="identity">Who this end is, for the replies.</param>
    /// <param name="candidateAddress">The candidate address the replies advertise.</param>
    /// <param name="candidatePort">And its port.</param>
    /// <param name="timeout">
    /// How long one wait may be silent. NOT a bound on the run: see the type's note on a failed
    /// receive.
    /// </param>
    public async Task<PunchAnsweringOutcome> RunAsync(
        PunchIdentity identity,
        string candidateAddress,
        ushort candidatePort,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidateAddress);

        var answered = 0;
        var ignored = 0;
        var faulted = 0;

        byte[] buffer = new byte[PunchExchange.RequestLength * 2];

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var timedOut = false;
            var received = 0;
            EndPoint from = new IPEndPoint(IPAddress.Any, 0);

            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(timeout);

            try
            {
                SocketReceiveFromResult got = await socket.ReceiveFromAsync(
                    buffer, SocketFlags.None, from, bounded.Token).ConfigureAwait(false);

                received = got.ReceivedBytes;
                from = got.RemoteEndPoint;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
            }
            catch (SocketException)
            {
                // The core's "Receiving response failed": logged, and waited on again with the whole
                // timeout. Negative so Next reads it as a failure rather than as a short datagram.
                received = -1;
            }

            uint messageType = received >= 4
                ? BinaryPrimitives.ReadUInt32BigEndian(buffer)
                : 0;

            PunchStep step = PunchExchange.Next(timedOut, answered > 0, received, messageType);

            switch (step)
            {
                case PunchStep.Answer:
                    byte[]? reply = PunchResponse.Build(
                        buffer.AsSpan(0, received),
                        identity.LocalId,
                        identity.ConsoleId,
                        identity.SidLocal,
                        identity.SidConsole,
                        candidateAddress,
                        candidatePort);

                    if (reply is null)
                    {
                        // The address will not parse, so there is nothing to send and nothing the
                        // core would have sent either. Fatal rather than a silent extra round.
                        return new PunchAnsweringOutcome(PunchStep.Fatal, answered, ignored, faulted);
                    }

                    await socket.SendToAsync(reply, SocketFlags.None, from, cancellationToken)
                        .ConfigureAwait(false);
                    answered++;
                    break;

                case PunchStep.Ignore:
                    ignored++;
                    break;

                case PunchStep.WaitAgain:
                    faulted++;
                    break;

                default:
                    return new PunchAnsweringOutcome(step, answered, ignored, faulted);
            }
        }
    }

    /// <summary>Releases the socket.</summary>
    public void Dispose()
    {
        socket.Dispose();
        GC.SuppressFinalize(this);
    }
}
