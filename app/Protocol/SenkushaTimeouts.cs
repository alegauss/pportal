namespace ChiakiNg.Protocol;

/// <summary>
/// PP791: senkusha's three waits, which had no managed counterpart until something waited them.
///
/// <c>#define EXPECT_TIMEOUT_MS 5000</c>, <c>#define CONNECT_TIMEOUT_MS 30000</c> and
/// <c>#define EXPECT_PONG_TIMEOUT_MS 1000</c>, at the top of senkusha.c. Quoted rather than
/// paraphrased because that is what PP724's sweep reads: naming a macro proves nothing - two files
/// carry an EXPECT_TIMEOUT_MS and this port's prose discusses both - so the signal that a wait has
/// gained a counterpart is a managed file holding the C's own define.
///
/// THREE NUMBERS AND THEY ARE NOT INTERCHANGEABLE. The connect's thirty seconds is the longest wait
/// in the tree, and it is the one a console that is awake but not listening spends before anything
/// says so. The pong's one second is the shortest, because the round-trip test sends ten of them and
/// a lost pong is a `continue` rather than a failure - a run that gave each ping five seconds would
/// spend fifty on a link that is simply down.
/// </summary>
public static class SenkushaTimeouts
{
    /// <summary>EXPECT_TIMEOUT_MS: the protocol ack, the bang, a data ack, and the client MTU command.</summary>
    public const int ExpectMs = 5000;

    /// <summary>CONNECT_TIMEOUT_MS: the wait on the takion's CONNECTED event, and the tree's longest.</summary>
    public const int ConnectMs = 30000;

    /// <summary>EXPECT_PONG_TIMEOUT_MS: one ping's own wait, which the test takes ten of.</summary>
    public const int ExpectPongMs = 1000;

    /// <summary>Which of the three a state's wait is given, which is the run's own table.</summary>
    public static int For(SenkushaState state) => state switch
    {
        SenkushaState.TakionConnect => ConnectMs,
        SenkushaState.ExpectPong => ExpectPongMs,
        _ => ExpectMs,
    };
}
