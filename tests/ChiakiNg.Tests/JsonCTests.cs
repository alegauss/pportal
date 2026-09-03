using System.Text.Json;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the managed json-c, held against json-c.
///
/// Every case here runs the same text through both and compares. That is the point of the shim
/// entry points existing at all - the rules in <see cref="JsonC"/> were measured rather than read,
/// and a test that only asserted the managed side would pin whatever was measured wrongly.
///
/// The divergence set is asserted too, at the bottom. It is not a list of bugs: json-c's tokener is
/// lenient about JSON itself and this is not, deliberately, and writing down which inputs the two
/// disagree on is what makes that a decision rather than a surprise for whoever translates
/// holepunch.c.
/// </summary>
public class JsonCTests
{
    /// <summary>Both sides' answers for one node, so a case is one line.</summary>
    private static void Same(string json, string key = "v")
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        using NativeJson? native = NativeJson.Parse(json);
        using JsonDocument? managed = JsonC.Parse(json);

        Assert.Equal(native is null, managed is null);
        if (native is null || managed is null)
            return;

        IntPtr nativeNode = key.Length == 0 ? native.Root : NativeJson.Get(native.Root, key);
        JsonElement? managedNode = key.Length == 0
            ? managed.RootElement
            : JsonC.Get(managed.RootElement, key);

        Assert.Equal(NativeJson.String(nativeNode), JsonC.String(managedNode));
        Assert.Equal(NativeJson.Int(nativeNode), JsonC.Int(managedNode));
        Assert.Equal(NativeJson.Int64(nativeNode), JsonC.Int64(managedNode));
        Assert.Equal(NativeJson.Bool(nativeNode), JsonC.Bool(managedNode));
        Assert.Equal(NativeJson.ArrayLength(nativeNode), JsonC.ArrayLength(managedNode));
    }

    /// <summary>
    /// get_string answers for every type but null, which is the trap: GetString() throws on all of
    /// these and holepunch reads fields it does not type-check first.
    /// </summary>
    [Theory]
    [InlineData(@"{""v"":""hello""}")]
    [InlineData(@"{""v"":42}")]
    [InlineData(@"{""v"":1.5}")]
    [InlineData(@"{""v"":9.99}")]
    [InlineData(@"{""v"":true}")]
    [InlineData(@"{""v"":false}")]
    [InlineData(@"{""v"":null}")]
    [InlineData(@"{""v"":{""a"":1}}")]
    [InlineData(@"{""v"":[1,2]}")]
    [InlineData(@"{""v"":[]}")]
    [InlineData(@"{""v"":{}}")]
    public void EveryTypeReadsTheSameWayThroughBoth(string json) => Same(json);

    /// <summary>An absent key, which is not the same as a null one and answers the same as it.</summary>
    [Fact]
    public void AnAbsentKeyReadsAsNothing() => Same(@"{""v"":1}", "missing");

    /// <summary>
    /// get_int parses strings, and parses them leniently: "42px" is 42. The same rule PP140 found
    /// in the settings fields, reached from the other end of the tree.
    /// </summary>
    [Theory]
    [InlineData(@"{""v"":""42""}")]
    [InlineData(@"{""v"":""42px""}")]
    [InlineData(@"{""v"":"" 42 ""}")]
    [InlineData(@"{""v"":""-7""}")]
    [InlineData(@"{""v"":""+7""}")]
    [InlineData(@"{""v"":""abc""}")]
    [InlineData(@"{""v"":""""}")]
    [InlineData(@"{""v"":""0""}")]
    [InlineData(@"{""v"":""false""}")]
    [InlineData(@"{""v"":""99999999999999999999""}")]
    public void AStringIsParsedTheWayJsonCParsesIt(string json) => Same(json);

    /// <summary>
    /// get_int saturates rather than wrapping. An unchecked cast would give a small wrong number
    /// where json-c gives a large one, and only one of those looks wrong in a log.
    /// </summary>
    [Theory]
    [InlineData(@"{""v"":99999999999}")]
    [InlineData(@"{""v"":-99999999999}")]
    [InlineData(@"{""v"":2147483647}")]
    [InlineData(@"{""v"":2147483648}")]
    [InlineData(@"{""v"":-2147483649}")]
    [InlineData(@"{""v"":9223372036854775807}")]
    public void AWideNumberSaturatesRatherThanWrapping(string json) => Same(json);

    /// <summary>And a double truncates toward zero on the way to an int.</summary>
    [Theory]
    [InlineData(@"{""v"":9.99}")]
    [InlineData(@"{""v"":-9.99}")]
    [InlineData(@"{""v"":0.4}")]
    [InlineData(@"{""v"":1e3}")]
    public void ADoubleTruncatesTowardZero(string json) => Same(json);

    /// <summary>
    /// get_boolean calls any non-empty string true, so "false" and "0" are both true. This is the
    /// one a caller reading a flag out of a string field has no reason to suspect.
    /// </summary>
    [Fact]
    public void AnyNonEmptyStringIsTrue()
    {
        Same(@"{""v"":""false""}");
        Same(@"{""v"":""0""}");
        Same(@"{""v"":""""}");

        // Stated directly too, since the comparison above would also pass if both were wrong the
        // same way - and this is the assertion that says what the answer IS.
        using JsonDocument? managed = JsonC.Parse(@"{""a"":""false"",""b"":"""",""c"":0,""d"":1}");
        Assert.True(JsonC.Bool(JsonC.Get(managed!.RootElement, "a")));
        Assert.False(JsonC.Bool(JsonC.Get(managed.RootElement, "b")));
        Assert.False(JsonC.Bool(JsonC.Get(managed.RootElement, "c")));
        Assert.True(JsonC.Bool(JsonC.Get(managed.RootElement, "d")));
    }

    /// <summary>object_get_ex is case-sensitive, in both.</summary>
    [Fact]
    public void TheKeyLookupIsCaseSensitive()
    {
        Same(@"{""V"":1}");
        Same(@"{""v"":1}", "V");
    }

    /// <summary>
    /// A repeated key answers with the LAST one. GetProperty answers with the first, so this is the
    /// difference that only a malformed-ish document reveals - and Sony's payloads are not this
    /// port's to guarantee.
    /// </summary>
    [Fact]
    public void ARepeatedKeyAnswersWithTheLastOne()
    {
        Same(@"{""v"":1,""v"":2}");
        Same(@"{""v"":""a"",""v"":""b"",""v"":""c""}");

        using JsonDocument? managed = JsonC.Parse(@"{""v"":1,""v"":2}");
        Assert.Equal(2, JsonC.Int(JsonC.Get(managed!.RootElement, "v")));
    }

    /// <summary>Arrays: the length of a non-array is -1, and an index past the end is nothing.</summary>
    [Fact]
    public void ArrayLengthAndIndexingAgree()
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        const string json = @"{""a"":[10,20,30],""b"":7}";
        using NativeJson? native = NativeJson.Parse(json);
        using JsonDocument? managed = JsonC.Parse(json);

        IntPtr nativeArray = NativeJson.Get(native!.Root, "a");
        JsonElement? managedArray = JsonC.Get(managed!.RootElement, "a");

        Assert.Equal(3, NativeJson.ArrayLength(nativeArray));
        Assert.Equal(3, JsonC.ArrayLength(managedArray));

        Assert.Equal(-1, NativeJson.ArrayLength(NativeJson.Get(native.Root, "b")));
        Assert.Equal(-1, JsonC.ArrayLength(JsonC.Get(managed.RootElement, "b")));
        Assert.Equal(-1, NativeJson.ArrayLength(native.Root));
        Assert.Equal(-1, JsonC.ArrayLength(managed.RootElement));

        for (int i = 0; i < 5; i++)
        {
            IntPtr nativeAt = NativeJson.ArrayAt(nativeArray, i);
            JsonElement? managedAt = JsonC.ArrayAt(managedArray, i);

            Assert.Equal(nativeAt == IntPtr.Zero, managedAt is null);
            Assert.Equal(NativeJson.String(nativeAt), JsonC.String(managedAt));
        }
    }

    /// <summary>
    /// JSON Pointer, which System.Text.Json has no equivalent for at all - so these seven call
    /// sites are the ones with nothing to be naive with, only something to get wrong.
    ///
    /// "/" addresses the empty-string key and not the root, `~1` is a slash, `~0` a tilde, and `-`
    /// resolves to nothing on a get.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("/a")]
    [InlineData("/a/b")]
    [InlineData("/a/b/0")]
    [InlineData("/a/b/1")]
    [InlineData("/a/b/2")]
    [InlineData("/a/b/-")]
    [InlineData("/x~1y")]
    [InlineData("/x~0z")]
    [InlineData("/")]
    [InlineData("/a/missing")]
    [InlineData("a/b")]
    [InlineData("/A")]
    [InlineData("/a/b/00")]
    [InlineData("/a/b/-1")]
    public void ThePointerResolvesTheWayJsonCResolvesIt(string path)
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        const string doc = @"{""a"":{""b"":[10,20]},""x/y"":1,""x~z"":2,"""":3}";

        using NativeJson? native = NativeJson.Parse(doc);
        using JsonDocument? managed = JsonC.Parse(doc);

        IntPtr nativeNode = native!.Pointer(path);
        JsonElement? managedNode = JsonC.Pointer(managed!.RootElement, path);

        Assert.Equal(nativeNode == IntPtr.Zero, managedNode is null);
        Assert.Equal(NativeJson.String(nativeNode), JsonC.String(managedNode));
        Assert.Equal(NativeJson.Int(nativeNode), JsonC.Int(managedNode));
    }

    /// <summary>
    /// The tokener where the two AGREE, which is what a caller can rely on: a bare scalar is a
    /// document, trailing rubbish is ignored, and an empty or whitespace-only text is refused - as
    /// is a bare `null`, which json_tokener_parse cannot express as anything but failure.
    /// </summary>
    [Theory]
    [InlineData("42")]
    [InlineData(@"""bare""")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"{""a"":1} trailing")]
    [InlineData(@"{""a"":1}{""b"":2}")]
    [InlineData(@"[1,2]")]
    [InlineData("{")]
    [InlineData(@"{""a"":}")]
    public void TheTokenerAgreesOnWhatIsADocument(string text)
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        using NativeJson? native = NativeJson.Parse(text);
        using JsonDocument? managed = JsonC.Parse(text);

        Assert.Equal(native is null, managed is null);
        if (native is null || managed is null)
            return;

        Assert.Equal(NativeJson.String(native.Root), JsonC.String(managed.RootElement));
    }

    /// <summary>
    /// And where they do NOT agree, written down. json-c's lexer is lenient about JSON itself; this
    /// is not, because reproducing it means writing a parser to be bug-compatible with one, for
    /// inputs Sony's endpoints do not send. `0x1f` is the sharpest of them: json-c reads it as 0
    /// rather than refusing it.
    ///
    /// The assertion is that the list is exactly this. A json-c that stopped accepting one of these
    /// would make this test fail, which is the point - the decision is recorded, not the guess.
    /// </summary>
    [Theory]
    [InlineData(@"{'a':1}")]
    [InlineData(@"[1,2,]")]
    [InlineData(@"{""a"":1,}")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("0x1f")]
    [InlineData("01")]
    public void JsonCsLexerIsLenientWhereThisIsNot(string text)
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        using NativeJson? native = NativeJson.Parse(text);
        Assert.NotNull(native);

        using JsonDocument? managed = JsonC.Parse(text);
        Assert.Null(managed);
    }

    /// <summary>
    /// json-c's serialisation, which is what get_string returns for an object or an array: spaced,
    /// and with `/` escaped. Both are json-c defaults and neither is System.Text.Json's.
    /// </summary>
    [Theory]
    [InlineData(@"{""a"":1}")]
    [InlineData(@"{""a"":1,""b"":""two""}")]
    [InlineData(@"{""a"":{""b"":[1,2]}}")]
    [InlineData(@"[1,""two"",true,null]")]
    [InlineData(@"[]")]
    [InlineData(@"{}")]
    [InlineData(@"{""url"":""https://example.invalid/a/b""}")]
    [InlineData(@"{""q"":""a\""b\\c""}")]
    [InlineData(@"{""t"":""a\tb\nc""}")]
    public void TheSerialisationIsJsonCsOwn(string json)
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        using NativeJson? native = NativeJson.Parse(json);
        using JsonDocument? managed = JsonC.Parse(json);

        Assert.Equal(NativeJson.String(native!.Root), JsonC.String(managed!.RootElement));
    }

    /// <summary>
    /// A session message the way holepunch.c actually meets one - the payload is a STRING holding
    /// JSON, so it is parsed twice, and the inner read goes through the same accessors.
    /// </summary>
    [Fact]
    public void ASessionMessageShapedDocumentReadsTheSameThroughBoth()
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        const string outer =
            @"{""to"":""ps5"",""action"":""SEND"",""payload"":" +
            @"""{\""accountId\"":\""9999999999999999999999\"",\""localPeerPort\"":\""9295\""}""}";

        using NativeJson? native = NativeJson.Parse(outer);
        using JsonDocument? managed = JsonC.Parse(outer);

        string? nativePayload = NativeJson.String(NativeJson.Get(native!.Root, "payload"));
        string? managedPayload = JsonC.String(JsonC.Get(managed!.RootElement, "payload"));
        Assert.Equal(nativePayload, managedPayload);
        Assert.NotNull(managedPayload);

        using NativeJson? nativeInner = NativeJson.Parse(nativePayload!);
        using JsonDocument? managedInner = JsonC.Parse(managedPayload!);

        // The port is 9295 as a STRING, which get_int reads anyway - and the account id overflows,
        // which is why the wide read and the saturating one are both asserted.
        Assert.Equal(
            NativeJson.Int(NativeJson.Get(nativeInner!.Root, "localPeerPort")),
            JsonC.Int(JsonC.Get(managedInner!.RootElement, "localPeerPort")));
        Assert.Equal(9295, JsonC.Int(JsonC.Get(managedInner.RootElement, "localPeerPort")));

        Assert.Equal(
            NativeJson.Int64(NativeJson.Get(nativeInner.Root, "accountId")),
            JsonC.Int64(JsonC.Get(managedInner.RootElement, "accountId")));
    }

    /// <summary>
    /// The call sites this replaces are still there, and still this many. Not a count for its own
    /// sake: PP33's line says 420 across lib/src, and json_pointer_get having SEVEN of them is why
    /// the pointer is implemented here rather than left for the translation to improvise.
    /// </summary>
    [Fact]
    public void HolepunchStillUsesTheAccessorsThisReplaces()
    {
        string? holepunch = ChiakiNg.Session.SanitizerSource.LocateRelative(
            Path.Combine("lib", "src", "remote", "holepunch.c"));
        if (holepunch is null)
            return;

        string text = File.ReadAllText(holepunch);

        Assert.True(Count(text, "json_object_object_get_ex") >= 20, "object_get_ex");
        Assert.True(Count(text, "json_object_get_string") >= 15, "get_string");
        Assert.True(Count(text, "json_pointer_get") >= 5, "json_pointer_get");
        Assert.True(Count(text, "json_tokener_parse") >= 3, "json_tokener_parse");

        static int Count(string text, string needle)
        {
            int count = 0;
            for (int at = 0; (at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0; at += needle.Length)
                count++;
            return count;
        }
    }
}
