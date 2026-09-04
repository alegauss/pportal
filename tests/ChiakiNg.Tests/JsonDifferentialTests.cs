using System.Text.Json;
using ChiakiNg.Protocol;
using ChiakiNg.Session;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the same documents through both JSON implementations, accessor by accessor.
///
/// <see cref="JsonC"/> reproduces json-c's ANSWERS in managed code, which is a translation and not
/// a wrapper - so the only correctness test it has is behavioural. The cases in
/// <see cref="JsonOracleCases"/> cover the shapes json-c coerces without being asked, which is
/// where a reimplementation of an accessor quietly disagrees:
///
///   a STRING asked for as an int. json-c parses it rather than returning zero, so "42" is 42 and
///   "abc" is 0 - and a port using int.TryParse gets the first right and the second right for the
///   wrong reason, until it meets "42abc";
///
///   a BOOLEAN asked for as an int, and an int asked for as a boolean;
///
///   a DOUBLE asked for as an int, which truncates rather than rounds;
///
///   and a MISSING key, which is a null node every accessor then has to answer for.
///
/// THE SECOND OPINION IS NOW RECORDED, which is what lets json-c leave. These assertions used to
/// decline on any build without the library - twenty-three of them, reported as passes - so the
/// file PP33 deletes was also the only thing keeping this comparison alive. It is compared against
/// <see cref="JsonOracleRecording"/> on every build, and the recording is compared against the live
/// library on the builds that still have one.
///
/// A row that both get wrong the same way is not caught here and is not meant to be: the claim is
/// that the two clients agree, because that is what lets a reply parsed by one be trusted by the
/// other.
/// </summary>
public class JsonDifferentialTests
{
    /// <summary>The cases, from the one list the recorder also walks.</summary>
    public static TheoryData<string, string> Documents()
    {
        var data = new TheoryData<string, string>();

        foreach ((string json, string path) in JsonOracleCases.Rows)
            data.Add(json, path);

        return data;
    }

    private static JsonOracleRecording Recording()
    {
        JsonOracleRecording? recording = JsonOracleRecording.Read();

        Assert.True(
            recording is not null,
            $"{JsonOracleRecording.RelativePath} is missing or unreadable, so json-c's answers are "
                + "gone and nothing is comparing this port against them. Rebuild with "
                + "CHIAKI_ENABLE_HOLEPUNCH=ON and run --record-json-oracle.");

        return recording;
    }

    /// <summary>
    /// THE COMPARISON: every accessor, on one node, against what json-c said.
    ///
    /// Against the RECORDING rather than the library, so it runs on every build. What the library
    /// is still used for is checking the recording, one test below.
    /// </summary>
    [Theory]
    [MemberData(nameof(Documents))]
    public void ThisPortAnswersWhatJsonCAnswered(string json, string path)
    {
        JsonOracleRow? recorded = Recording().Row(json, path);
        Assert.True(recorded is not null, $"no recorded answer for {json} at {path}");

        JsonOracleRow row = recorded.Value;
        JsonDocument? managed = JsonC.Parse(json);

        using (managed)
        {
            JsonElement? node = managed is null ? null : JsonC.Pointer(managed.RootElement, path);

            // Present or absent has to agree first: everything below is about a node, and the two
            // disagreeing here would make the rest compare different things.
            Assert.Equal(row.Present, node is not null);

            if (node is null)
                return;

            Assert.Equal(row.Type, JsonC.TypeOf(node));
            Assert.Equal(row.String, JsonC.String(node));
            Assert.Equal(row.Int, JsonC.Int(node));
            Assert.Equal(row.Int64, JsonC.Int64(node));
            Assert.Equal(row.Bool, JsonC.Bool(node));
            Assert.Equal(row.ArrayLength, JsonC.ArrayLength(node));
        }
    }

    /// <summary>
    /// A document neither can parse is refused by both. Asserted because "returns null" and
    /// "throws" are both plausible answers and only one of them is json-c's.
    /// </summary>
    /// <remarks>
    /// The trailing comma is deliberately NOT among the cases. json-c's lexer accepts it and this
    /// port does not, which <see cref="JsonCTests.JsonCsLexerIsLenientWhereThisIsNot"/> records as a
    /// decision: matching a lenient lexer means being bug-compatible with a parser, for inputs
    /// Sony's endpoints do not send. This differential re-found it and the decision stood - the line
    /// is drawn at the lexer, and everything past it is matched exactly.
    /// </remarks>
    [Fact]
    public void BothRefuseTheSameRubbish()
    {
        JsonOracleRecording recording = Recording();

        foreach (string rubbish in new[] { "", "{", """{"a":}""", "not json at all" })
        {
            JsonDocumentAnswer? recorded = recording.Document(rubbish);
            Assert.True(recorded is not null, $"no recorded answer for \"{rubbish}\"");
            Assert.False(recorded.Value.Parsed, $"json-c now accepts \"{rubbish}\"");

            JsonDocument? managed = JsonC.Parse(rubbish);
            using (managed)
                Assert.Null(managed);
        }
    }

    /// <summary>
    /// AND THE RECORDING IS STILL WHAT JSON-C SAYS, on a build that can ask.
    ///
    /// This is what keeps the file from becoming a fossil. A recorded second opinion nobody checks
    /// is a table somebody typed, and the whole reason it is worth having is that it came off the
    /// library - so while a build carrying json-c exists, it is re-derived and compared. It
    /// declines where the library is absent, which is the one guard PP33's deletion leaves standing
    /// and the only one whose absence costs nothing: what it protects is already asserted above.
    /// </summary>
    [Fact]
    public void TheRecordingIsWhatTheLibraryStillSays()
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return;

        JsonOracleRecording? taken = JsonOracleRecorder.Take();
        Assert.NotNull(taken);

        JsonOracleRecording recorded = Recording();

        Assert.Equal(recorded.Rows, taken.Rows);
        Assert.Equal(recorded.Documents, taken.Documents);

        // The runs are compared FIELD BY FIELD, because a record's generated equality compares its
        // list members by reference - two identical recordings would differ, and a difference that
        // is always there is a check nobody can act on.
        Assert.Equal(recorded.Runs.Count, taken.Runs.Count);

        for (int i = 0; i < recorded.Runs.Count; i++)
        {
            JsonTokenerRun was = recorded.Runs[i];
            JsonTokenerRun now = taken.Runs[i];

            Assert.Equal(was.Name, now.Name);
            Assert.Equal(was.Frames, now.Frames);
            Assert.Equal(was.ResetBefore, now.ResetBefore);
            Assert.Equal(was.Parsed, now.Parsed);
            Assert.Equal(was.Errors, now.Errors);
        }
    }

    /// <summary>
    /// The comparison has to be able to fail, or every row above passes by asking nothing. Two
    /// documents that genuinely differ must come out different on the accessor that differs.
    /// </summary>
    [Fact]
    public void TheComparisonCanTellTwoDocumentsApart()
    {
        JsonOracleRecording recording = Recording();

        Assert.NotEmpty(recording.Rows);
        Assert.Contains(recording.Rows, row => row.Present);

        // The coercions themselves, named. A recording of all-zeroes would satisfy every row-by-row
        // comparison above, because JsonC would be compared against nothing in particular.
        Assert.Equal(42, recording.Row("""{"v":"42abc"}""", "/v")?.Int);
        Assert.Equal(3, recording.Row("""{"v":3.9}""", "/v")?.Int);
        Assert.Equal(-3, recording.Row("""{"v":-3.9}""", "/v")?.Int);
        Assert.Equal(0, recording.Row("""{"v":"abc"}""", "/v")?.Int);
        Assert.Equal(3, recording.Row("""{"v":[1,2,3]}""", "/v")?.ArrayLength);

        // And a non-array answers -1 rather than 0, which is the difference between "no elements"
        // and "not a thing with elements".
        Assert.Equal(-1, recording.Row("""{"v":42}""", "/v")?.ArrayLength);
    }
}
