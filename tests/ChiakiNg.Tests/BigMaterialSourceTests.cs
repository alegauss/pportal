using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP777: the BIG's four session-owned values, held against the root that builds it.
///
/// A live PS5 acknowledged a managed BIG twice and answered nothing. That is what a message a
/// console cannot read looks like from this side - the launch spec is hidden under a key stream,
/// and a console with the wrong one has nothing to refuse and no way to say so.
///
/// FOUR PLAUSIBLE CONSTANTS, WHICH IS WHY THIS IS A CHECK. The crypt was thirty-two zero bytes, the
/// MTU was the one senkusha measured in the other direction, the resolution was the preset this
/// tree happens to ask for, and the target was the only PS5 target there is. Every one of them
/// produces a well-formed message, and every one of them costs a console to find.
/// </summary>
public class BigMaterialSourceTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE ROOT READS THE SESSION FOR ALL FOUR, and spells none of them.
    ///
    /// Both directions: a reader that went missing is the defect coming back, and an invented form
    /// that reappeared is the same defect arriving beside a reader that is no longer used.
    /// </summary>
    [Fact]
    public void TheRootReadsAllFourOffTheSession()
    {
        if (BigMaterialSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);

        output.WriteLine($"{BigMaterialSource.Required.Count} required, reading {path}");

        Assert.Empty(BigMaterialSource.MissingIn(source));
        Assert.Empty(BigMaterialSource.InventedIn(source));
    }

    /// <summary>
    /// AND THE CHECK IS READ AS CODE, which this file is the reason for.
    ///
    /// PP735's trap, met a fifth time: <see cref="BigMaterialSource"/> spells every invented form in
    /// its own docstrings, so a reader keyed on flat text would report the root as still inventing
    /// all four. The negative side is exercised over text this test writes, because a tree that has
    /// the readers cannot demonstrate what happens to one that does not.
    /// </summary>
    [Fact]
    public void ACommentIsNotAnInvention()
    {
        const string Commented = """
            // The crypt was new RpCrypt(ChiakiTarget.Ps5_1, new byte[16], new byte[16]) once.
            var crypt = new RpCrypt(auth.Target, auth.Nonce, auth.Morning);
            var fields = new LaunchSpecFields(Mtu: transport.MtuIn, Target: auth.Target);
            SessionBigMaterial.AuthOf(session);
            SessionBigMaterial.ProfileOf(session);
            """;

        Assert.Empty(BigMaterialSource.MissingIn(Commented));
        Assert.Empty(BigMaterialSource.InventedIn(Commented));

        // And a root that really does invent one is reported, by the field it invents.
        const string Inventing = """
            var crypt = new RpCrypt(ChiakiTarget.Ps5_1, new byte[16], new byte[16]);
            SessionBigMaterial.ProfileOf(session);
            var fields = new LaunchSpecFields(Mtu: transport.MtuIn, Target: auth.Target);
            """;

        Assert.Contains("session->rpcrypt", BigMaterialSource.InventedIn(Inventing));
        Assert.Contains("session->rpcrypt", BigMaterialSource.MissingIn(Inventing));
    }

    /// <summary>
    /// Each row names a field the C's own send_big reads, so the list is the C's and not a wish.
    ///
    /// Held against streamconnection.c rather than remembered: a row naming something the C stopped
    /// reading would be a check about this port's habits instead of about the message.
    /// </summary>
    [Fact]
    public void EveryRowNamesSomethingTheCsSendBigReads()
    {
        if (SanitizerSource.LocateRelative(@"lib\src\streamconnection.c") is not { } path)
            return;

        string body = CFunction.Body(
            File.ReadAllText(path),
            "static ChiakiErrorCode stream_connection_send_big(ChiakiStreamConnection *stream_connection)")
            ?? throw new InvalidOperationException("send_big is gone");

        Assert.Contains("&session->rpcrypt", body, StringComparison.Ordinal);
        Assert.Contains("session->mtu_in", body, StringComparison.Ordinal);
        Assert.Contains("session->connect_info.video_profile", body, StringComparison.Ordinal);
        Assert.Contains("session->target", body, StringComparison.Ordinal);
    }
}
