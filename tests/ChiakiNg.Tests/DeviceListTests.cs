using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: reading the console list, where the failures are more interesting than the fields.
/// </summary>
public class DeviceListTests
{
    private const string Duid =
        "0011223344556677889900112233445566778899001122334455667788990011";

    private static string Body(string clients)
        => "{\"clients\":[" + clients + "]}";

    private static string Client(
        string duid = Duid, string name = "Console", string features = "[\"remotePlay\"]")
        => "{\"duid\":\"" + duid + "\",\"device\":{\"enabledFeatures\":" + features
            + ",\"name\":\"" + name + "\"}}";

    /// <summary>A client with the duid filled in and whatever device object is being tested.</summary>
    private static string ClientWithDevice(string device)
        => "{\"duid\":\"" + Duid + "\"" + device + "}";

    private static IReadOnlyList<HolepunchDevice> Read(string body)
    {
        IReadOnlyList<HolepunchDevice>? devices = DeviceListReader.Read(body, "PS5", out DeviceListResult result);

        Assert.Equal(DeviceListResult.Ok, result);
        Assert.NotNull(devices);
        return devices;
    }

    private static DeviceListResult Refused(string body)
    {
        Assert.Null(DeviceListReader.Read(body, "PS5", out DeviceListResult result));
        Assert.NotEqual(DeviceListResult.Ok, result);
        return result;
    }

    /// <summary>An ordinary console reads.</summary>
    [Fact]
    public void AConsoleReads()
    {
        HolepunchDevice device = Assert.Single(Read(Body(Client())));

        Assert.Equal(32, device.DeviceUid.Length);
        Assert.Equal("Console", device.Name);
        Assert.True(device.RemotePlayEnabled);
    }

    /// <summary>
    /// AN EMPTY LIST LOOKS LIKE RUNNING OUT OF MEMORY in the core: it asks the allocator for zero
    /// bytes and treats a NULL answer as CHIAKI_ERR_MEMORY.
    ///
    /// "You have no consoles" and "this machine is out of memory" arrive as the same error, and the
    /// first is the common case for a new account. Here an empty list is an empty list.
    /// </summary>
    [Fact]
    public void AnEmptyListIsEmptyRatherThanAMemoryFailure()
    {
        IReadOnlyList<HolepunchDevice> devices = Read(Body(""));

        Assert.Empty(devices);
        Assert.True(DeviceListReader.WouldLookLikeAMemoryFailure(0));
        Assert.False(DeviceListReader.WouldLookLikeAMemoryFailure(1));
    }

    /// <summary>
    /// ONE BAD DEVICE LOSES THE WHOLE LIST - every missing field jumps out of the function, so one
    /// console PSN describes oddly costs the account every other console it has.
    ///
    /// Reproduced: a port that skipped the bad one would show a list the Qt client never shows.
    /// </summary>
    [Theory]
    [InlineData("""{"device":{"enabledFeatures":[],"name":"n"}}""")]
    [InlineData("""{"duid":42,"device":{"enabledFeatures":[],"name":"n"}}""")]
    [InlineData("""{"duid":"00","device":{"enabledFeatures":[],"name":"n"}}""")]
    [InlineData("""{"duid":"zz","device":{"enabledFeatures":[],"name":"n"}}""")]
    public void OneBadDeviceLosesEveryGoodOne(string bad)
    {
        Assert.Equal(DeviceListResult.BadClient, Refused(Body($"{Client()},{bad}")));

        // The good one on its own is fine, which is what makes the loss the bad one's doing.
        Assert.Single(Read(Body(Client())));
    }

    /// <summary>A missing device object, feature list or name each lose the list too.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(""",\"device\":{\"name\":\"n\"}""")]
    [InlineData(""",\"device\":{\"enabledFeatures\":[]}""")]
    [InlineData(""",\"device\":{\"enabledFeatures\":{},\"name\":\"n\"}""")]
    public void EveryMissingFieldLosesTheList(string device)
        => Assert.Equal(
            DeviceListResult.BadClient,
            Refused(Body(ClientWithDevice(device.Replace("\\\"", "\"", StringComparison.Ordinal)))));

    /// <summary>
    /// EXCEPT INSIDE enabledFeatures, WHERE NOTHING IS STRICT. Entries that are not strings are
    /// passed over rather than fatal - the one place in the function an unexpected type survives.
    /// </summary>
    [Fact]
    public void RubbishInTheFeatureListIsSteppedOverRatherThanFatal()
    {
        HolepunchDevice device = Assert.Single(
            Read(Body(Client(features: """[42,null,{"a":1},"remotePlay"]"""))));

        Assert.True(device.RemotePlayEnabled);
    }

    /// <summary>And a feature list without it simply says no.</summary>
    [Fact]
    public void AConsoleWithoutTheFeatureIsListedAsDisabled()
    {
        HolepunchDevice device = Assert.Single(Read(Body(Client(features: """["somethingElse"]"""))));

        Assert.False(device.RemotePlayEnabled);
    }

    /// <summary>The feature name is matched exactly, not case-insensitively.</summary>
    [Fact]
    public void TheFeatureNameIsMatchedExactly()
    {
        HolepunchDevice device = Assert.Single(Read(Body(Client(features: """["RemotePlay"]"""))));

        Assert.False(device.RemotePlayEnabled);
    }

    /// <summary>
    /// THE CONSOLE TYPE IS NOT READ. It is stamped onto every device from the argument the caller
    /// passed, so the list says what was asked for rather than what came back.
    /// </summary>
    [Fact]
    public void TheTypeIsWhateverTheCallerAskedFor()
    {
        IReadOnlyList<HolepunchDevice>? devices = DeviceListReader.Read(
            Body(Client()), "SOMETHING_ELSE", out DeviceListResult result);

        Assert.Equal(DeviceListResult.Ok, result);
        Assert.Equal("SOMETHING_ELSE", Assert.Single(devices!).Type);
    }

    /// <summary>A name longer than the buffer is truncated, which is behaviour worth keeping.</summary>
    [Fact]
    public void ALongNameIsTruncatedToTheBuffer()
    {
        string long33 = new('x', DeviceListReader.DeviceNameBuffer + 1);

        HolepunchDevice device = Assert.Single(Read(Body(Client(name: long33))));

        Assert.Equal(DeviceListReader.DeviceNameBuffer, device.Name.Length);
    }

    /// <summary>
    /// And one that exactly fills it keeps every character - where the core's strncpy left the
    /// buffer with no terminator at all, which was not behaviour to reproduce. PP403 corrected the
    /// core to match; this stays as the statement of what the port does.
    /// </summary>
    [Fact]
    public void ANameThatExactlyFillsTheBufferIsKeptWhole()
    {
        string exact = new('x', DeviceListReader.DeviceNameBuffer);

        HolepunchDevice device = Assert.Single(Read(Body(Client(name: exact))));

        Assert.Equal(exact, device.Name);
    }

    /// <summary>A body that is not JSON, and one with no clients array, each say so.</summary>
    [Fact]
    public void ABodyWithoutAClientsArrayIsRefused()
    {
        Assert.Equal(DeviceListResult.NotJson, Refused("not json at all"));
        Assert.Equal(DeviceListResult.NoClients, Refused("""{"something":[]}"""));
        Assert.Equal(DeviceListResult.NoClients, Refused("""{"clients":{}}"""));
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheListsRulesAreStillTheQtCores()
    {
        string? path = DeviceListSource.Locate();
        string? header = DeviceListSource.LocateHeader();
        if (path is null || header is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(DeviceListSource.AnEmptyListStillAsksForNothing(core), "zero bytes, tested for null");
        Assert.True(
            DeviceListSource.TheMemoryPathStillReturnsWithoutCleanup(core), "the one return in a function of gotos");
        Assert.True(DeviceListSource.TheCountIsStillSetFirst(core), "counted before filled");
        Assert.True(DeviceListSource.OneBadDeviceStillLosesTheList(core), "no recovery in the loop");
        Assert.True(DeviceListSource.TheFeatureScanIsStillLenient(core), "the one lenient place");
        Assert.True(
            DeviceListSource.TheTypeIsStillStampedFromTheArgument(core), "stamped, not read");
        // PP403: inverted. The class note already said the truncation was behaviour and the missing
        // terminator was not, which is an argument for correcting the C rather than reproducing it -
        // so it was, and this now watches for the old shape returning.
        Assert.False(
            DeviceListSource.TheNameIsStillCopiedWithoutATerminator(core), "strncpy into its own size again");
        Assert.True(
            DeviceListSource.TheNameGoesThroughTheBoundedCopy(core), "the name no longer goes through copy_bounded");
        Assert.True(DeviceListSource.TheNameBufferIsStillThatSize(File.ReadAllText(header)), "thirty-two");
    }

    /// <summary>
    /// PP403: the rule over the file, not just over the one call that was found.
    ///
    /// All three sites were written the same way and a fourth would be, so what is asserted is the
    /// shape: no copy in holepunch.c may be bounded by the size of what it writes into. That bound
    /// is the one that cannot terminate.
    /// </summary>
    [Fact]
    public void NoCopyInTheFileIsBoundedByItsOwnDestination()
    {
        string? path = DeviceListSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        // PP271: a sweep that found nothing to look at has not passed. Two copies remain - both
        // bounded by lengths taken from the source, into a buffer memset first - and they are what
        // keeps this rule from being vacuous.
        Assert.True(DeviceListSource.CopiesInTheFile(core) > 0, "no copies left to rule on");
        Assert.True(
            DeviceListSource.NoCopyIsBoundedByItsDestinationSize(core),
            "a copy is bounded by the size of what it writes into, which cannot terminate");
    }

    /// <summary>PP272: and the rule answers no to a file that has the shape.</summary>
    [Fact]
    public void TheRuleRefusesACopyBoundedByItsDestination()
    {
        Assert.False(
            DeviceListSource.NoCopyIsBoundedByItsDestinationSize(
                "void f(void) { strncpy(dst, src, sizeof(dst)); }"));
        Assert.True(
            DeviceListSource.NoCopyIsBoundedByItsDestinationSize(
                "void f(void) { strncpy(dst, src, len); }"));

        // A comment quoting the old shape is not the old shape - the mistake made three times over
        // PP399, PP400 and PP401.
        Assert.True(
            DeviceListSource.NoCopyIsBoundedByItsDestinationSize(
                "// was strncpy(dst, src, sizeof(dst));\nvoid f(void) { strncpy(dst, src, len); }"));

        // And a file with no copies in it answers no, rather than passing on an absence.
        Assert.Equal(0, DeviceListSource.CopiesInTheFile(""));
        Assert.False(DeviceListSource.NoCopyIsBoundedByItsDestinationSize(""));
    }
}
