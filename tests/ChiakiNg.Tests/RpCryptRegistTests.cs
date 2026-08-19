using ChiakiNg.Native;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP121: test/rpcrypt.c's four registration-mode cases, which nothing could reach before.
///
/// Registration derives its keys from an ambassador and the PIN a user types, not from a nonce
/// and a morning key - a different schedule producing the same struct. It is also the flow with
/// the least room to debug: PP119's request head goes out encrypted under these keys, and a
/// console that dislikes them answers with a refusal that names nothing.
///
/// The vectors existed the whole time; chiaki_rpcrypt_init_regist was simply not on the shim, so
/// the one flow with recorded answers had no comparison. It is there now, at ABI 28.
/// </summary>
public class RpCryptRegistTests
{
    private static string? File => SanitizerSource.LocateRelative(@"test\rpcrypt.c");

    private static IReadOnlyDictionary<string, byte[]> Vectors(string function)
        => CryptoVectors.InFunction(File!, function);

    /// <summary>
    /// The bright key, on both console generations, from the SAME ambassador and the same PIN.
    ///
    /// That is what makes the pair worth having as a pair: the inputs are identical and the
    /// recorded answers are not, so the target is doing real work in the derivation. A port that
    /// ignored it would pass one of these and fail the other rather than passing both.
    /// </summary>
    [Theory]
    [InlineData("test_bright_regist_ps4", (int)ChiakiTarget.Ps4_10)]
    [InlineData("test_bright_regist_ps5", (int)ChiakiTarget.Ps5_1)]
    public void TheRecordedRegistrationBrightMatches(string function, int target)
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors(function);
        using RpCrypt crypt = RpCrypt.ForRegistration(
            (ChiakiTarget)target, v["ambassador"], 0x1e, 78703893);

        Assert.Equal(v["bright_expected"], crypt.Bright());
    }

    /// <summary>
    /// And they really are different answers to the same question, so the theory above is not one
    /// case written twice.
    /// </summary>
    [Fact]
    public void TheTwoGenerationsDeriveDifferentKeysFromTheSameInputs()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> ps4 = Vectors("test_bright_regist_ps4");
        IReadOnlyDictionary<string, byte[]> ps5 = Vectors("test_bright_regist_ps5");

        Assert.Equal(ps4["ambassador"], ps5["ambassador"]);
        Assert.NotEqual(ps4["bright_expected"], ps5["bright_expected"]);
    }

    /// <summary>
    /// The IV a registration crypt generates, on both generations. Recorded at offset 0 and PIN 0,
    /// which is not a degenerate case: it is what a PS5 registration without a PIN actually uses.
    /// </summary>
    [Theory]
    [InlineData("test_iv_regist_ps4", (int)ChiakiTarget.Ps4_10)]
    [InlineData("test_iv_regist_ps5", (int)ChiakiTarget.Ps5_1)]
    public void TheRecordedRegistrationIvMatches(string function, int target)
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors(function);
        using RpCrypt crypt = RpCrypt.ForRegistration((ChiakiTarget)target, v["ambassador"], 0, 0);

        Assert.Equal(v["iv_expected"], crypt.GenerateIv(0));
    }

    /// <summary>
    /// The PIN is an input, not a label. Without this every assertion above passes for a
    /// derivation that ignored it - which would mean every registration on a console deriving the
    /// same keys, and the PIN being decoration.
    /// </summary>
    [Fact]
    public void AnotherPinDerivesAnotherKey()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors("test_bright_regist_ps4");
        using RpCrypt other = RpCrypt.ForRegistration(
            ChiakiTarget.Ps4_10, v["ambassador"], 0x1e, 78703894);

        Assert.NotEqual(v["bright_expected"], other.Bright());
    }

    /// <summary>
    /// And so is the offset, which is the one a reader would take for bookkeeping. regist.c reads
    /// it out of a randomised header byte, so the same PIN on the same console derives different
    /// keys per attempt - a port that pinned it to zero would still register, until it did not.
    /// </summary>
    [Fact]
    public void AnotherKeyOffsetDerivesAnotherKey()
    {
        if (File is null)
            return;

        IReadOnlyDictionary<string, byte[]> v = Vectors("test_bright_regist_ps4");
        using RpCrypt other = RpCrypt.ForRegistration(
            ChiakiTarget.Ps4_10, v["ambassador"], 0x1f, 78703893);

        Assert.NotEqual(v["bright_expected"], other.Bright());
    }
}
