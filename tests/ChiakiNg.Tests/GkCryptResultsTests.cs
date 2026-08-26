using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP367: no caller of the gkcrypt cipher discards what it answered.
/// </summary>
public class GkCryptResultsTests
{
    /// <summary>
    /// THE CHECK, over every caller rather than the one that was wrong.
    ///
    /// The two in takion.c already assigned and tested their result, so the AV route's discard was
    /// the odd one out - which is the shape a second one would take.
    /// </summary>
    [Theory]
    [InlineData(@"lib\src\streamconnection.c")]
    [InlineData(@"lib\src\takion.c")]
    public void NoCallerDiscardsTheCiphersResult(string relative)
    {
        string? path = GkCryptResults.Locate(relative);
        if (path is null)
            return;

        IReadOnlyList<string> discarded =
            GkCryptResults.DiscardedResults(File.ReadAllText(path));

        Assert.True(
            discarded.Count == 0,
            $"{relative} throws away what the cipher answered:\n  " + string.Join("\n  ", discarded));
    }

    /// <summary>Both callers are on the list, so it cannot quietly go empty.</summary>
    [Fact]
    public void BothCallersAreOnTheList()
    {
        Assert.Equal(
            [@"lib\src\streamconnection.c", @"lib\src\takion.c"],
            GkCryptResults.Callers);
    }

    /// <summary>And the reader finds the discard, so the check above means something.</summary>
    [Fact]
    public void TheReaderFindsADiscardedResult()
    {
        const string asItWas = """
            	chiaki_gkcrypt_decrypt(stream_connection->gkcrypt_remote, packet->key_pos + CHIAKI_GKCRYPT_BLOCK_SIZE, packet->data, packet->data_size);

            	if(packet->is_video)
            		chiaki_video_receiver_av_packet(stream_connection->video_receiver, packet);
            """;

        string found = Assert.Single(GkCryptResults.DiscardedResults(asItWas));

        Assert.Contains("chiaki_gkcrypt_decrypt", found, StringComparison.Ordinal);
    }

    /// <summary>And ignores a call whose result goes somewhere.</summary>
    [Theory]
    [InlineData("\terr = chiaki_gkcrypt_encrypt(takion->gkcrypt_local, key_pos, buf, size);")]
    [InlineData("\tChiakiErrorCode e = chiaki_gkcrypt_decrypt(gk, pos, buf, size);")]
    [InlineData("\tif(chiaki_gkcrypt_decrypt(gk, pos, buf, size) != CHIAKI_ERR_SUCCESS)")]
    [InlineData("\treturn chiaki_gkcrypt_decrypt(gk, pos, buf, size);")]
    public void TheReaderIgnoresAResultThatGoesSomewhere(string line)
    {
        Assert.Empty(GkCryptResults.DiscardedResults(line));
    }

    /// <summary>And the cipher's own definition is not a call.</summary>
    [Fact]
    public void TheDefinitionIsNotACall()
    {
        const string definition =
            "CHIAKI_EXPORT ChiakiErrorCode chiaki_gkcrypt_decrypt(ChiakiGKCrypt *gkcrypt, uint64_t key_pos)";

        Assert.Empty(GkCryptResults.DiscardedResults(definition));
    }

    /// <summary>A call on the very first line is still found, which is what the anchor is for.</summary>
    [Fact]
    public void ACallOnTheFirstLineIsFound()
    {
        Assert.Single(GkCryptResults.DiscardedResults("chiaki_gkcrypt_decrypt(gk, pos, buf, size);"));
    }
}
