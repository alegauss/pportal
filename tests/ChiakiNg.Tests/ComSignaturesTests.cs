using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP693: a COM method without PreserveSig answers with an uninitialised int.
///
/// PP652's spike declared sixteen WASAPI methods without the attribute. The CLR then read the
/// declared int as an [out, retval] and turned the real HRESULT into exceptions, so every hr == 0
/// compared against an uninitialised local. It did not crash - it answered, and the answer was the
/// opposite of the truth about every capture device on this machine.
///
/// THE CHECK IS HERE BECAUSE THE FAILURE IS SILENT. PP650's spike is clean and backs a shipped
/// decision about the video decoder, so the tree already carries a COM surface a decision rests on.
/// One attribute separates a reading from a fabrication, and until now nothing looked.
/// </summary>
public class ComSignaturesTests(ITestOutputHelper output)
{
    /// <summary>THE CHECK: no method in the tree returns a status without preserving its signature.</summary>
    [Fact]
    public void EveryComMethodReturningAStatusPreservesItsSignature()
    {
        IReadOnlyList<UnpreservedComMethod> found = ComSignatures.UnpreservedInTheTree();

        Assert.True(
            found.Count == 0,
            "these COM methods return a status the caller reads and would get an uninitialised "
                + "one instead:\n  "
                + string.Join(
                    "\n  ",
                    found.Select(one => $"{one.Where}: {one.Interface}.{one.Method} returns {one.Returns}")));
    }

    /// <summary>
    /// And the sweep is reading something, so the check above is not passing over an empty tree.
    ///
    /// PP271's rule, which bites hardest here: a sweep that found no COM interfaces at all would
    /// satisfy the check on a tree full of broken ones.
    /// </summary>
    [Fact]
    public void TheSweepFindsTheFilesThatDeclareComInterfaces()
    {
        IReadOnlyList<string> declaring = ComSignatures.FilesDeclaringComInterfaces();
        if (declaring.Count == 0)
            return;

        output.WriteLine("declaring COM interfaces: " + string.Join(", ", declaring));

        // The two spikes, which are the whole COM surface this tree has: app/ reaches native code
        // through the shim's C ABI and declares no COM at all.
        Assert.Contains(declaring, one => one.Contains("mic-capture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(declaring, one => one.Contains("decoder-choice", StringComparison.OrdinalIgnoreCase));

        // And this file is NOT among them, because it declares the defect on purpose. The first run
        // reported four of its own fixtures, which is the failure the exemption exists against.
        Assert.DoesNotContain(
            declaring, one => one.EndsWith(ComSignatures.FixtureFileName, StringComparison.Ordinal));
    }

    /// <summary>
    /// THE DEFECT ITSELF, written out as PP652's spike had it.
    ///
    /// A check that cannot demonstrate what it catches is one nobody can review, and this one's
    /// subject is an absence - the hardest kind to show by describing.
    /// </summary>
    [Fact]
    public void ThePP652ShapeIsCaught()
    {
        const string source = """
            [ComImport]
            [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IMMDeviceEnumerator
            {
                int EnumAudioEndpoints(EDataFlow flow, int stateMask, out IMMDeviceCollection? devices);

                int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice? device);
            }
            """;

        IReadOnlyList<UnpreservedComMethod> found = ComSignatures.UnpreservedIn("spike.cs", source);

        Assert.Equal(2, found.Count);
        Assert.All(found, one => Assert.Equal("IMMDeviceEnumerator", one.Interface));
        Assert.Equal(["EnumAudioEndpoints", "GetDefaultAudioEndpoint"], found.Select(one => one.Method));
    }

    /// <summary>And the fix is not caught, which is what says the check reads the attribute.</summary>
    [Fact]
    public void TheCorrectedShapeIsNotCaught()
    {
        const string source = """
            [ComImport]
            [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
            private interface IMMDeviceEnumerator
            {
                [PreserveSig]
                int EnumAudioEndpoints(EDataFlow flow, int stateMask, out IMMDeviceCollection? devices);

                [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice? device);
            }
            """;

        Assert.Empty(ComSignatures.UnpreservedIn("spike.cs", source));
    }

    /// <summary>
    /// A void return is allowed, because it claims no status for the CLR to rewrite.
    ///
    /// This is the case that would make the check a rule about attributes rather than about
    /// correctness, and getting it wrong in the strict direction is how a check gets disabled.
    /// </summary>
    [Fact]
    public void AVoidMethodNeedsNothing()
    {
        const string source = """
            [ComImport]
            private interface ISomething
            {
                void DoIt(int value);

                [PreserveSig] int AndThis();
            }
            """;

        Assert.Empty(ComSignatures.UnpreservedIn("x.cs", source));
    }

    /// <summary>
    /// The attribute applies to ONE method, not to the rest of the interface.
    ///
    /// The reader clears its flag at each method, so a single decorated declaration cannot vouch
    /// for the ones after it - which is exactly the drift a per-interface flag would allow.
    /// </summary>
    [Fact]
    public void TheAttributeDoesNotCarryToTheNextMethod()
    {
        const string source = """
            [ComImport]
            private interface ISomething
            {
                [PreserveSig] int First();

                int Second();
            }
            """;

        UnpreservedComMethod one = Assert.Single(ComSignatures.UnpreservedIn("x.cs", source));
        Assert.Equal("Second", one.Method);
    }

    /// <summary>An interface with no COM marker is not this check's subject.</summary>
    [Fact]
    public void APlainInterfaceIsNotCom()
    {
        const string source = """
            public interface IStreamRunHost
            {
                int Steps();
            }
            """;

        Assert.Empty(ComSignatures.UnpreservedIn("x.cs", source));
    }

    /// <summary>
    /// And the marker has to reach a declaration, so a ComImport class does not open an interface.
    ///
    /// The reader arms on the marker and disarms when it finds the declaration; a marker followed
    /// by something else must not leave it armed for a plain interface further down.
    /// </summary>
    [Fact]
    public void AMarkerThatReachesNoInterfaceArmsNothing()
    {
        const string source = """
            [ComImport]
            [Guid("00000000-0000-0000-0000-000000000000")]
            private class Something
            {
            }

            public interface IPlain
            {
                int Ordinary();
            }
            """;

        Assert.Empty(ComSignatures.UnpreservedIn("x.cs", source));
    }

    /// <summary>PP272: the reader says no about nothing.</summary>
    [Fact]
    public void AnEmptySourceSaysNo()
        => Assert.Empty(ComSignatures.UnpreservedIn("x.cs", ""));
}
