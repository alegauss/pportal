using System.Text.Json;
using ChiakiNg.Protocol;
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
///
/// PP33: JSON-C'S HALF IS NOW READ FROM <see cref="JsonOracleRecording"/> rather than called. PP663
/// put the library behind a flag, so every one of these declined on an ordinary build and was
/// reported as a pass - and the file carrying the library is the one PP33 deletes, which would have
/// made the decline permanent. The recording is taken from the real library by
/// <see cref="JsonOracleRecorder"/> and re-derived by
/// <see cref="JsonDifferentialTests.TheRecordingIsWhatTheLibraryStillSays"/> wherever a build can.
/// </summary>
public class JsonCTests
{
    private static JsonOracleRecording Recording()
    {
        JsonOracleRecording? recording = JsonOracleRecording.Read();
        Assert.True(recording is not null, $"{JsonOracleRecording.RelativePath} is missing");
        return recording;
    }

    /// <summary>Both sides' answers for one node, so a case is one line.</summary>
    private static void Same(string json, string key = "v")
    {
        // The key as a pointer, which is the same node for every key in these cases: none contains
        // a slash or a tilde, and the empty key is the root in both spellings.
        string path = key.Length == 0 ? "" : "/" + key;

        JsonOracleRow? recorded = Recording().Row(json, path);
        Assert.True(recorded is not null, $"no recorded answer for {json} at {path}");

        JsonOracleRow row = recorded.Value;
        using JsonDocument? managed = JsonC.Parse(json);

        Assert.Equal(row.Present || managed is not null, managed is not null);
        if (managed is null)
            return;

        JsonElement? node = JsonC.Pointer(managed.RootElement, path);

        // Present or absent first, then every accessor - for an ABSENT node too, because json-c
        // answers for one and holepunch.c reads fields it never checked.
        Assert.Equal(row.Present, node is not null);

        Assert.Equal(row.String, JsonC.String(node));
        Assert.Equal(row.Int, JsonC.Int(node));
        Assert.Equal(row.Int64, JsonC.Int64(node));
        Assert.Equal(row.Bool, JsonC.Bool(node));
        Assert.Equal(row.ArrayLength, JsonC.ArrayLength(node));
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
        const string json = @"{""a"":[10,20,30],""b"":7}";
        JsonOracleRecording recording = Recording();

        Assert.Equal(3, recording.Row(json, "/a")?.ArrayLength);
        Assert.Equal(-1, recording.Row(json, "/b")?.ArrayLength);
        Assert.Equal(-1, recording.Row(json, "")?.ArrayLength);

        using JsonDocument? managed = JsonC.Parse(json);
        JsonElement? array = JsonC.Get(managed!.RootElement, "a");

        Assert.Equal(3, JsonC.ArrayLength(array));
        Assert.Equal(-1, JsonC.ArrayLength(JsonC.Get(managed.RootElement, "b")));
        Assert.Equal(-1, JsonC.ArrayLength(managed.RootElement));

        // Indexing past the end, through the pointer - which is how the recording addresses it and
        // is the same node ArrayAt reaches.
        for (int i = 0; i < 5; i++)
        {
            JsonOracleRow? recorded = recording.Row(json, $"/a/{i}");
            Assert.True(recorded is not null, $"no recorded answer for index {i}");

            JsonElement? at = JsonC.ArrayAt(array, i);

            Assert.Equal(recorded.Value.Present, at is not null);
            Assert.Equal(recorded.Value.String, JsonC.String(at));
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
        const string doc = @"{""a"":{""b"":[10,20]},""x/y"":1,""x~z"":2,"""":3}";

        JsonOracleRow? recorded = Recording().Row(doc, path);
        Assert.True(recorded is not null, $"no recorded answer for the pointer \"{path}\"");

        using JsonDocument? managed = JsonC.Parse(doc);
        JsonElement? node = JsonC.Pointer(managed!.RootElement, path);

        // The accessors are compared for an absent node too - json-c answers for NULL, and what it
        // answers is the whole reason a caller reading an unchecked field gets 0 rather than a
        // failure.
        Assert.Equal(recorded.Value.Present, node is not null);
        Assert.Equal(recorded.Value.String, JsonC.String(node));
        Assert.Equal(recorded.Value.Int, JsonC.Int(node));
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
        JsonDocumentAnswer? recorded = Recording().Document(text);
        Assert.True(recorded is not null, $"no recorded answer for \"{text}\"");

        using JsonDocument? managed = JsonC.Parse(text);

        Assert.Equal(recorded.Value.Parsed, managed is not null);
        if (managed is null)
            return;

        Assert.Equal(recorded.Value.RootString, JsonC.String(managed.RootElement));
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
        Assert.Contains(text, JsonOracleCases.Divergences);

        JsonDocumentAnswer? recorded = Recording().Document(text);
        Assert.True(recorded is not null, $"no recorded answer for \"{text}\"");
        Assert.True(recorded.Value.Parsed, $"json-c no longer accepts \"{text}\"");

        using JsonDocument? managed = JsonC.Parse(text);
        Assert.Null(managed);
    }

    /// <summary>And the divergence set is exactly those, so a new one cannot arrive unremarked.</summary>
    [Fact]
    public void TheDivergenceSetIsExactlyThose()
    {
        JsonOracleRecording recording = Recording();

        IReadOnlyList<string> diverging =
        [
            .. recording.Documents
                .Where(one => one.Parsed && JsonC.Parse(one.Text) is null)
                .Select(one => one.Text),
        ];

        Assert.Equal(JsonOracleCases.Divergences.Order(), diverging.Order());
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
        JsonDocumentAnswer? recorded = Recording().Document(json);
        Assert.True(recorded is not null, $"no recorded answer for {json}");

        using JsonDocument? managed = JsonC.Parse(json);
        Assert.NotNull(managed);

        Assert.Equal(recorded.Value.RootString, JsonC.String(managed.RootElement));
    }

    /// <summary>
    /// A session message the way holepunch.c actually meets one - the payload is a STRING holding
    /// JSON, so it is parsed twice, and the inner read goes through the same accessors.
    /// </summary>
    [Fact]
    public void ASessionMessageShapedDocumentReadsTheSameThroughBoth()
    {
        JsonOracleRecording recording = Recording();

        using JsonDocument? managed = JsonC.Parse(JsonOracleCases.SessionMessage);
        string? payload = JsonC.String(JsonC.Get(managed!.RootElement, "payload"));

        Assert.Equal(recording.Row(JsonOracleCases.SessionMessage, "/payload")?.String, payload);
        Assert.NotNull(payload);

        // The payload json-c handed back IS the inner document, which is what makes recording the
        // two separately a comparison rather than two unrelated readings.
        Assert.Equal(JsonOracleCases.SessionPayload, payload);

        using JsonDocument? inner = JsonC.Parse(payload);

        // The port is 9295 as a STRING, which get_int reads anyway - and the account id overflows,
        // which is why the wide read and the saturating one are both asserted.
        Assert.Equal(
            recording.Row(JsonOracleCases.SessionPayload, "/localPeerPort")?.Int,
            JsonC.Int(JsonC.Get(inner!.RootElement, "localPeerPort")));
        Assert.Equal(9295, JsonC.Int(JsonC.Get(inner.RootElement, "localPeerPort")));

        Assert.Equal(
            recording.Row(JsonOracleCases.SessionPayload, "/accountId")?.Int64,
            JsonC.Int64(JsonC.Get(inner.RootElement, "accountId")));
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
