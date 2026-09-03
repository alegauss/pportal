using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP32: libopus has two consumers, so porting the decoder removes nothing.
///
/// PP651 measured the decode side and found cost decides nothing. That left the dependency as the
/// deciding half - and the dependency does not leave with the decoder, because the microphone's
/// encoder is holding it too.
/// </summary>
public class OpusDependencyTests(ITestOutputHelper output)
{
    /// <summary>
    /// Exactly two files call into libopus, and they are the decoder and the encoder.
    ///
    /// This is the number the decision turns on. One consumer would have made managed Opus a
    /// straight trade - 9us a frame for 488 KB and one fewer native binary. Two means porting the
    /// playback side alone buys a decoder that costs more and jitters more, and leaves the library
    /// linked for the other one.
    /// </summary>
    [Fact]
    public void TheLibraryHasTwoConsumersAndNotOne()
    {
        IReadOnlyList<string> calling = OpusDependency.CallingFiles();
        if (calling.Count == 0)
            return;

        output.WriteLine(string.Join(", ", calling));
        Assert.Equal(OpusDependency.Consumers, calling);
    }

    /// <summary>
    /// And the file that looks like a third is not one.
    ///
    /// audiosender.c names its parameter opus_sender and copies it into three buffers, and calls
    /// nothing in the library: it carries frames somebody else encoded. A census taken by searching
    /// for the word gets three consumers and concludes the encoder is one of two rather than the
    /// only other one - which changes what porting the decoder is worth.
    /// </summary>
    [Fact]
    public void TheFileThatNamesOpusEverywhereCallsItNowhere()
    {
        if (OpusDependency.LocateSource() is not { } root)
            return;

        string path = Path.Combine(root, OpusDependency.CarriesEncodedFramesOnly);
        if (!File.Exists(path))
            return;

        string source = File.ReadAllText(path);

        // The premise: it really does say "opus" all over itself.
        Assert.Contains("opus", source, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            OpusDependency.CallsOpus(source),
            $"{OpusDependency.CarriesEncodedFramesOnly} now calls into libopus, so the dependency "
                + "has a third consumer and PP32's trade is a different one");
    }

    /// <summary>
    /// The reader tells a call from a name, which is the whole of what it is for.
    ///
    /// Both directions. A variable whose name starts with the library's prefix is not a call, and a
    /// call is not made not-a-call by sitting next to one.
    /// </summary>
    [Theory]
    [InlineData("size_t opus_sender_size = 0;", false)]
    [InlineData("memcpy(buf, opus_sender, opus_sender_size);", false)]
    [InlineData("// opus_decode( would go here", false)]
    [InlineData("int n = opus_decode(st, data, len, pcm, 480, 0);", true)]
    [InlineData("decoder->opus_decoder = opus_decoder_create(rate, channels, &error);", true)]
    public void ANameIsNotACall(string source, bool calls)
        => Assert.Equal(calls, OpusDependency.CallsOpus(source));

    /// <summary>The option and the link are still where this says they are.</summary>
    [Fact]
    public void TheOptionAndTheLinkAreStillDeclaredWhereTheyWere()
    {
        if (OpusDependency.Locate(OpusDependency.RootCMakeRelativePath) is { } rootPath)
        {
            Assert.Contains(
                OpusDependency.Option, File.ReadAllText(rootPath), StringComparison.Ordinal);
        }

        if (OpusDependency.Locate(OpusDependency.LibCMakeRelativePath) is { } libPath)
        {
            string lib = File.ReadAllText(libPath);
            Assert.Contains("find_package(Opus", lib, StringComparison.Ordinal);
            Assert.Contains("${Opus_LIBRARIES}", lib, StringComparison.Ordinal);
        }
    }
}
