using ChiakiNg.Native;
using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP581: the function pointers crossing to the shim, against the typedefs they answer.
/// </summary>
public class ShimCallbacksTests(ITestOutputHelper output)
{
    /// <summary>
    /// EVERY THUNK MATCHES ITS TYPEDEF, parameter for parameter and return for return.
    ///
    /// This is the sharpest of the hand-written promises. A missing export throws (PP580) and a
    /// shifted enum mislabels a value (PP577); a wrong signature here corrupts the stack, because
    /// the C pushes what its typedef says and the thunk reads what its argument list says. Nothing
    /// throws, and nothing is wrong until it is very wrong.
    /// </summary>
    [Fact]
    public void EveryThunkMatchesItsTypedef()
    {
        if (ShimCallbacks.LocateHeader() is not { } headerPath)
            return;

        string header = File.ReadAllText(headerPath);

        foreach (ShimCallback callback in ShimCallbacks.All)
        {
            if (SanitizerSource.LocateRelative(callback.ManagedRelativePath) is not { } managedPath)
                continue;

            IReadOnlyList<string>? c = ShimCallbacks.SignatureOf(header, callback.Typedef);
            IReadOnlyList<string>? managed =
                ShimCallbacks.ManagedSignatureIn(File.ReadAllText(managedPath));

            Assert.True(c is not null, $"{callback.Typedef} is not in the header");
            Assert.True(managed is not null, $"{callback.ManagedRelativePath} has no single thunk");

            output.WriteLine($"{callback.Typedef}: {string.Join(", ", c!)}");
            Assert.Equal(c, managed);
        }
    }

    /// <summary>
    /// Six typedefs and six rows. A seventh added to the header without a row here is a thunk
    /// nobody compared, which is the state all six were in.
    /// </summary>
    [Fact]
    public void EveryTypedefHasARow()
    {
        Assert.Equal(6, ShimCallbacks.All.Count);

        if (ShimCallbacks.LocateHeader() is not { } path)
            return;

        string header = File.ReadAllText(path);

        foreach (ShimCallback callback in ShimCallbacks.All)
            Assert.NotNull(ShimCallbacks.SignatureOf(header, callback.Typedef));
    }

    /// <summary>
    /// FIVE RETURN void AND ONE RETURNS bool, which is why a sweep keyed on `typedef void (*` finds
    /// five - as the first pass at this did. The video sample callback is the sixth.
    /// </summary>
    [Fact]
    public void TheVideoSampleCallbackIsTheOneReturningBool()
    {
        if (ShimCallbacks.LocateHeader() is not { } path)
            return;

        IReadOnlyList<string>? video =
            ShimCallbacks.SignatureOf(File.ReadAllText(path), "ChiakiShimVideoSampleCb");

        Assert.NotNull(video);
        Assert.Equal("byte", video[^1]);
    }

    /// <summary>
    /// bool is byte on purpose: C's bool is one byte and .NET marshals bool as the four-byte
    /// Windows BOOL by default. It is the one row where the obvious mapping is the wrong one.
    /// </summary>
    [Fact]
    public void BoolIsByteAndAPointerIsAnAddress()
    {
        Assert.Equal("byte", ShimCallbacks.Widths["bool"]);
        Assert.Equal("int", ShimCallbacks.Widths["int32_t"]);

        IReadOnlyList<string>? read = ShimCallbacks.SignatureOf(
            "typedef bool (*Probe)(const char *msg, uint16_t n, void *user);", "Probe");

        Assert.Equal(["IntPtr", "ushort", "IntPtr", "byte"], read);
    }
}
