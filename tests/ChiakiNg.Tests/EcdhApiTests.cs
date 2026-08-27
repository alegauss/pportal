using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP427: ecdh.c is written against EVP, and none of the eight deprecated calls remain.
///
/// The port is verified by the recorded vector - the selftest asserts the local public key, its
/// signature and the derived secret byte for byte against test_ecdh in test/gkcrypt.c. What this adds
/// is the other direction: that the API the port left behind has not crept back.
/// </summary>
public class EcdhApiTests(ITestOutputHelper output)
{
    private static string? Ecdh()
    {
        string? path = EcdhSource.Locate();
        return path is null ? null : File.ReadAllText(path);
    }

    /// <summary>
    /// THE RULE. None of the eight is called any more.
    /// </summary>
    [Fact]
    public void NoneOfTheEightIsStillCalled()
    {
        if (Ecdh() is not { } source)
            return;

        IReadOnlyList<string> still = EcdhSource.DeprecatedStillUsed(source);
        output.WriteLine(still.Count == 0 ? "none" : string.Join(", ", still));

        Assert.True(
            still.Count == 0,
            "these were deprecated in OpenSSL 3.0 and PP427 removed them: "
                + string.Join(", ", still));
    }

    /// <summary>
    /// And the replacements are there, so the absence above is a port rather than a deletion.
    ///
    /// PP271: an empty file would satisfy the rule above about nothing.
    /// </summary>
    [Fact]
    public void TheReplacementsAreAllThere()
    {
        if (Ecdh() is not { } source)
            return;

        IReadOnlyList<string> used = EcdhSource.ReplacementsUsed(source);
        output.WriteLine(string.Join(", ", used));

        Assert.Equal(EcdhSource.Replacements.Count, used.Count);
    }

    /// <summary>
    /// PP400, and this file is the case the rule was made for: the port's own comments name all eight
    /// to say what replaced each. A reader of flat text would report the deprecated API as still in
    /// use, by the prose explaining that it is not.
    /// </summary>
    [Fact]
    public void ThePortsOwnCommentsDoNotCountAsCalls()
    {
        if (Ecdh() is not { } source)
            return;

        // The names ARE in the file - just not in its code.
        foreach (string name in EcdhSource.Deprecated)
        {
            if (source.Contains(name, StringComparison.Ordinal))
            {
                output.WriteLine($"{name} appears in the file, and not in its code");
                return;
            }
        }

        Assert.Fail("no deprecated name appears even in a comment, so this test proves nothing - "
            + "the port's rationale used to name each one it replaced");
    }

    /// <summary>A commented call is not a call, asserted directly on synthetic text.</summary>
    [Fact]
    public void ACommentedCallIsNotACall()
    {
        Assert.Empty(EcdhSource.DeprecatedStillUsed(
            "\t// EC_KEY_set_private_key(key, bn) is what this replaced.\n"));

        Assert.Empty(EcdhSource.DeprecatedStillUsed(
            "/* ECDH_compute_key(out, 32, point, key, NULL); */\n"));

        // And a real call is found.
        Assert.Equal(
            ["ECDH_compute_key"],
            EcdhSource.DeprecatedStillUsed("\tint r = ECDH_compute_key(out, 32, p, k, NULL);\n"));
    }

    /// <summary>
    /// The curve is named once. Under EVP it is a per-key parameter rather than a shared object, so
    /// two builders could disagree without a constant to share.
    /// </summary>
    [Fact]
    public void TheCurveIsNamedOnce()
    {
        if (Ecdh() is not { } source)
            return;

        Assert.True(
            EcdhSource.NamesTheCurveOnce(source),
            "the curve is spelled per use, or NID_secp256k1 is back");
    }

    /// <summary>
    /// PP105's behaviour survives the port: derive_secret still takes the remote signature and still
    /// does not verify it.
    ///
    /// Asserted because a rewrite is exactly when a port would "fix" this - and a client that started
    /// verifying would differ from the one every user already has.
    /// </summary>
    [Fact]
    public void TheRemoteSignatureIsStillNotVerified()
    {
        if (Ecdh() is not { } source)
            return;

        string code = CCall.Code(source);

        // The parameter is taken.
        Assert.Contains("remote_sig", code, StringComparison.Ordinal);

        // And nothing verifies it: no HMAC and no comparison in the derive path.
        int derive = code.IndexOf("chiaki_ecdh_derive_secret", StringComparison.Ordinal);
        Assert.True(derive >= 0);

        string body = code[derive..];
        Assert.DoesNotContain("HMAC(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("memcmp(remote_sig", body, StringComparison.Ordinal);
    }

    /// <summary>PP272: and an empty file calls nothing, either way.</summary>
    [Fact]
    public void AnEmptyFileCallsNothing()
    {
        Assert.Empty(EcdhSource.DeprecatedStillUsed(""));
        Assert.Empty(EcdhSource.ReplacementsUsed(""));
        Assert.False(EcdhSource.NamesTheCurveOnce(""));
    }
}
