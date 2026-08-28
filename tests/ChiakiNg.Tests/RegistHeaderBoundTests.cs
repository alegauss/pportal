using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP484: the bound that keeps request_header_format's dead guards from becoming a write.
///
/// The function bounds both of its guards with payload_size - the size of the BODY, always 0x1e0 or
/// more - rather than with the capacity it writes into, so `cur >= payload_size` cannot fire for a
/// 256-byte buffer. snprintf returns the length it WOULD have written, so a truncated head would
/// leave the cursor above the capacity and be waved through; `size_t s = buf_size - cur` would wrap,
/// and the next write would land at buf + cur, off the end of a stack array, with s bounding
/// nothing.
///
/// It is unreachable, and the arithmetic below is the only reason. That arithmetic was written down
/// nowhere, which is what PP484 is: a longer User-Agent, one more header or a path that grew would
/// eat the margin silently, and the first thing to notice would be the write.
/// </summary>
public class RegistHeaderBoundTests
{
    /// <summary>
    /// THE GATE: the longest head this port can build fits the buffer regist.c declares.
    ///
    /// Read from the file rather than assumed, so shrinking the array fails here.
    /// </summary>
    [Fact]
    public void TheWorstCaseHeadFitsTheBufferRegistDeclares()
    {
        if (RegistRequestSource.Locate() is not { } path)
            return;

        string text = File.ReadAllText(path);
        int? declared = RegistRequestSource.HeaderCapacity(text);
        Assert.NotNull(declared);

        int extent = RegistRequest.WorstCaseWriteExtent();
        Assert.True(
            extent <= declared,
            $"the longest head regist.c can format writes {extent} bytes into a buffer of {declared}");
    }

    /// <summary>
    /// And the cursor at the moment of the subtraction, which is the half that would wrap.
    ///
    /// Strictly less than the capacity, not merely inside it: at exactly the capacity the wrapping
    /// subtraction yields zero, and snprintf with a size of zero writes nothing - safe, but one byte
    /// of head away from not being.
    /// </summary>
    [Fact]
    public void TheWorstCaseCursorStaysBelowTheCapacity()
    {
        if (RegistRequestSource.Locate() is not { } path)
            return;

        int? declared = RegistRequestSource.HeaderCapacity(File.ReadAllText(path));
        Assert.NotNull(declared);

        int cursor = RegistRequest.WorstCaseCursorBeforeRpVersion();
        Assert.True(
            cursor < declared,
            $"regist.c would subtract a cursor of {cursor} from a capacity of {declared}");
    }

    /// <summary>
    /// The two numbers, pinned - 190 at the subtraction and 211 written in total, of 256.
    ///
    /// Derived from the head the port builds rather than counted by hand, so they move when the
    /// template does. Pinned anyway: a change here is a change to the margin, and it should be read
    /// by a person rather than absorbed.
    /// </summary>
    [Fact]
    public void TheMarginIsWhatItIsMeasuredToBe()
    {
        Assert.Equal(190, RegistRequest.WorstCaseCursorBeforeRpVersion());
        Assert.Equal(211, RegistRequest.WorstCaseWriteExtent());
    }

    /// <summary>The worst-case inputs are the longest ones regist.c can actually pass.</summary>
    [Fact]
    public void TheWorstCaseInputsAreTheLongestTheCCanPass()
    {
        // The two PS5/PS4 paths tie at 21; the pre-10 path is shorter.
        Assert.Equal(21, RegistRequest.PathFor(RegistRequest.LongestPath()).Length);

        // INET6_ADDRSTRLEN is 46 with its terminator.
        Assert.Equal(45, RegistRequest.LongestLocalAddress().Length);

        // "10.0", the longest of the four chiaki_rp_version_string answers.
        Assert.Equal("10.0", RegistRequest.LongestRpVersion());
    }

    /// <summary>The capacity is read from the declaration, in either base.</summary>
    [Theory]
    [InlineData("\tchar request_header[0x100];", 256)]
    [InlineData("\tchar request_header[256];", 256)]
    [InlineData("char request_header [ 0X80 ] ;", 128)]
    public void TheCapacityIsReadInEitherBase(string text, int expected)
        => Assert.Equal(expected, RegistRequestSource.HeaderCapacity(text));

    /// <summary>And is null where the declaration is not there - a changed file, not a zero.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("char other_header[0x100];")]
    public void AMissingDeclarationIsNullRatherThanZero(string text)
        => Assert.Null(RegistRequestSource.HeaderCapacity(text));

    /// <summary>The address is still sized by the constant the 45 above comes from.</summary>
    [Fact]
    public void TheAddressIsStillSizedByInet6()
    {
        Assert.True(RegistRequestSource.LocalAddressIsSizedByInet6(
            "\tchar regist_local_addr[INET6_ADDRSTRLEN] = \"10.0.2.15\";"));

        // A literal size would break the derivation above without breaking the format.
        Assert.False(RegistRequestSource.LocalAddressIsSizedByInet6(
            "\tchar regist_local_addr[46] = \"10.0.2.15\";"));
    }
}
