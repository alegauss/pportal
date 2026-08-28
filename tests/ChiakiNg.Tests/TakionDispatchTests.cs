using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP490, under PP27: takion_handle_packet's branches, and which of them keeps the datagram past the
/// call.
///
/// The C's rule is who frees. Over PP485's rented buffer nobody frees, so the rule becomes who
/// copies - and the branch that borrows where it should copy reads bytes the next datagram has
/// overwritten, which is a failure that does not happen where it is written.
/// </summary>
public class TakionDispatchTests
{
    /// <summary>The base type is the low nibble, so the flag bits above it change nothing.</summary>
    [Theory]
    [InlineData(0x00, TakionDispatch.Control)]
    [InlineData(0xf0, TakionDispatch.Control)]
    [InlineData(0x02, TakionDispatch.Video)]
    [InlineData(0x92, TakionDispatch.Video)]
    [InlineData(0x03, TakionDispatch.Audio)]
    [InlineData(0xa3, TakionDispatch.Audio)]
    public void TheBaseTypeIsTheLowNibble(int firstByte, int expected)
        => Assert.Equal(expected, TakionDispatch.BaseTypeOf([(byte)firstByte, 0, 0]));

    /// <summary>takion_handle_packet asserts buf_size &gt; 0, so an empty span is refused.</summary>
    [Fact]
    public void AnEmptyDatagramHasNoBaseType()
        => Assert.Throws<ArgumentException>(() => TakionDispatch.BaseTypeOf([]));

    /// <summary>A failed MAC is decided ahead of the switch, whatever the type would have been.</summary>
    [Theory]
    [InlineData(TakionDispatch.Control)]
    [InlineData(TakionDispatch.Video)]
    [InlineData(TakionDispatch.Audio)]
    [InlineData(7)]
    public void AFailedMacIsDecidedBeforeTheType(int baseType)
    {
        TakionDispatchVerdict verdict =
            TakionDispatch.Decide(baseType, macOk: false, enableCrypt: true, cryptAvailable: true);

        Assert.Equal(TakionDispatchBranch.MacRejected, verdict.Branch);
        Assert.Equal(DatagramLifetime.Borrowed, verdict.Lifetime);
    }

    /// <summary>
    /// THE ASYMMETRY THE DISPATCH CANNOT SEE: video and audio share one case label and one guard, and
    /// only one of them keeps the bytes.
    ///
    /// Video goes into a reorder queue entry. Audio's callback runs inside the handler and the buffer
    /// is freed on the way out. Reading the shared branch and concluding "AV copies" costs a memcpy
    /// per audio packet forever; concluding "AV borrows" corrupts every reordered frame.
    /// </summary>
    [Fact]
    public void VideoKeepsTheBytesAndAudioDoesNot()
    {
        TakionDispatchVerdict video = TakionDispatch.Decide(
            TakionDispatch.Video, macOk: true, enableCrypt: true, cryptAvailable: true);
        TakionDispatchVerdict audio = TakionDispatch.Decide(
            TakionDispatch.Audio, macOk: true, enableCrypt: true, cryptAvailable: true);

        Assert.Equal(TakionDispatchBranch.Video, video.Branch);
        Assert.Equal(DatagramLifetime.Copied, video.Lifetime);

        Assert.Equal(TakionDispatchBranch.Audio, audio.Branch);
        Assert.Equal(DatagramLifetime.Borrowed, audio.Lifetime);
    }

    /// <summary>
    /// Both AV types postpone before the cipher, and the guard is the pair - enable_crypt and the
    /// cipher's absence - rather than the triple the MAC re-check uses.
    /// </summary>
    [Theory]
    [InlineData(TakionDispatch.Video)]
    [InlineData(TakionDispatch.Audio)]
    public void BeforeTheCipherBothAvTypesArePostponed(int baseType)
    {
        TakionDispatchVerdict verdict =
            TakionDispatch.Decide(baseType, macOk: true, enableCrypt: true, cryptAvailable: false);

        Assert.Equal(TakionDispatchBranch.Postponed, verdict.Branch);
        Assert.Equal(DatagramLifetime.Copied, verdict.Lifetime);
    }

    /// <summary>
    /// With crypt disabled nothing is ever postponed, however absent the cipher is.
    ///
    /// The mirror of PP487's asymmetry from the other side: the postpone FLUSH ignores enable_crypt,
    /// but the postpone itself does not, so a session with crypt off fills no array to flush.
    /// </summary>
    [Theory]
    [InlineData(TakionDispatch.Video, TakionDispatchBranch.Video)]
    [InlineData(TakionDispatch.Audio, TakionDispatchBranch.Audio)]
    public void WithCryptDisabledNothingIsPostponed(int baseType, TakionDispatchBranch expected)
    {
        TakionDispatchVerdict verdict =
            TakionDispatch.Decide(baseType, macOk: true, enableCrypt: false, cryptAvailable: false);

        Assert.Equal(expected, verdict.Branch);
    }

    /// <summary>
    /// A control packet keeps the bytes, because the data case pushes packet_buf into a queue entry
    /// and is the only one of the message handler's three that leaves without a free.
    /// </summary>
    [Fact]
    public void AControlPacketKeepsTheBytes()
    {
        TakionDispatchVerdict verdict = TakionDispatch.Decide(
            TakionDispatch.Control, macOk: true, enableCrypt: true, cryptAvailable: true);

        Assert.Equal(TakionDispatchBranch.Control, verdict.Branch);
        Assert.Equal(DatagramLifetime.Copied, verdict.Lifetime);
    }

    /// <summary>Every base type the switch does not name lands on the default, which keeps nothing.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(0xf)]
    public void AnUnknownTypeIsLoggedAndDropped(int baseType)
    {
        TakionDispatchVerdict verdict =
            TakionDispatch.Decide(baseType, macOk: true, enableCrypt: true, cryptAvailable: true);

        Assert.Equal(TakionDispatchBranch.UnknownType, verdict.Branch);
        Assert.Equal(DatagramLifetime.Borrowed, verdict.Lifetime);
    }

    /// <summary>
    /// Three of the six branches keep the bytes, and MayBorrow is the negation of that set rather
    /// than a second list that can disagree with it.
    /// </summary>
    [Fact]
    public void ThreeOfTheSixBranchesKeepTheBytes()
    {
        Assert.Equal(3, TakionDispatch.KeepsTheBytes.Count);
        Assert.Equal(6, Enum.GetValues<TakionDispatchBranch>().Length);

        foreach (TakionDispatchBranch branch in Enum.GetValues<TakionDispatchBranch>())
            Assert.Equal(!TakionDispatch.KeepsTheBytes.Contains(branch), TakionDispatch.MayBorrow(branch));
    }

    /// <summary>Every reachable verdict agrees with MayBorrow, so the two cannot drift apart.</summary>
    [Fact]
    public void EveryVerdictAgreesWithMayBorrow()
    {
        foreach (bool macOk in new[] { true, false })
        foreach (bool enableCrypt in new[] { true, false })
        foreach (bool cryptAvailable in new[] { true, false })
        for (int baseType = 0; baseType <= TakionDispatch.BaseTypeMask; baseType++)
        {
            TakionDispatchVerdict verdict =
                TakionDispatch.Decide(baseType, macOk, enableCrypt, cryptAvailable);

            Assert.Equal(
                TakionDispatch.MayBorrow(verdict.Branch)
                    ? DatagramLifetime.Borrowed
                    : DatagramLifetime.Copied,
                verdict.Lifetime);
        }
    }

    /// <summary>
    /// THE DRIFT CHECK: the C still spells the dispatch the way the table above reads it.
    ///
    /// Four joins, and the last two are the ones that matter: they are the sub-branch facts the
    /// dispatch cannot see, so nothing else in this port would notice them moving.
    /// </summary>
    [Fact]
    public void TheCStillSpellsTheDispatchThisWay()
    {
        if (TakionDispatchSource.Locate() is not { } path)
            return;

        string source = File.ReadAllText(path);

        Assert.Equal(TakionDispatch.BaseTypeMask, TakionDispatchSource.MaskIn(source));

        string handle = Assert.IsType<string>(TakionDispatchSource.HandleBody(source));
        Assert.True(TakionDispatchSource.TheMacGateIsBeforeTheSwitch(handle));
        Assert.True(TakionDispatchSource.TheBaseTypeIsTheMaskedFirstByte(handle));
        Assert.True(TakionDispatchSource.VideoAndAudioShareTheOneGuard(handle));

        string av = Assert.IsType<string>(TakionDispatchSource.AvBody(source));
        Assert.True(TakionDispatchSource.AudioIsFreedWhereVideoIsQueued(av));

        string message = Assert.IsType<string>(TakionDispatchSource.MessageBody(source));
        Assert.True(TakionDispatchSource.OnlyTheDataCaseKeepsTheBuffer(message));
    }

    /// <summary>
    /// The three packet types the switch names still have the values this port compiled in.
    ///
    /// The constants live in takionreceive.h rather than takion.c, so they are read from there and
    /// not from the dispatch's own file.
    /// </summary>
    [Fact]
    public void TheThreeNamedTypesStillHaveTheseValues()
    {
        if (SanitizerSource.LocateRelative(@"lib\src\takionreceive.h") is not { } path)
            return;

        string header = File.ReadAllText(path);

        Assert.Contains($"TAKION_PACKET_TYPE_CONTROL = {TakionDispatch.Control},", header, StringComparison.Ordinal);
        Assert.Contains($"TAKION_PACKET_TYPE_VIDEO = {TakionDispatch.Video},", header, StringComparison.Ordinal);
        Assert.Contains($"TAKION_PACKET_TYPE_AUDIO = {TakionDispatch.Audio},", header, StringComparison.Ordinal);
    }
}
