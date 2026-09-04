namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: what the json-c oracle is asked, in ONE list that both the recorder and the tests read.
///
/// The rows used to live in the test that compares, which was right while the comparison was the
/// only consumer. A recording is a second consumer - see <see cref="JsonOracleRecording"/> - and two
/// lists would drift the way every duplicated list in this port has: the recording would answer for
/// rows the differential no longer asks, and the differential would ask rows the recording has no
/// answer for, and neither would fail.
///
/// So the cases are here and both sides walk them. A row added here is recorded and compared; a row
/// removed is neither.
/// </summary>
public static class JsonOracleCases
{
    /// <summary>
    /// The documents and the pointers into them, chosen for what json-c DOES to them.
    ///
    /// Not for shape. json-c coerces without being asked - a string parsed as an int, a double
    /// truncated rather than rounded - and a reimplementation using int.TryParse gets "42" right,
    /// "abc" right for the wrong reason, and "42abc" wrong.
    /// </summary>
    public static IReadOnlyList<(string Json, string Path)> Rows { get; } = Build();

    private static (string, string)[] Build()
    {
        var rows = new List<(string, string)>();

        void Add(string json, params string[] paths)
        {
            foreach (string path in paths)
                rows.Add((json, path));
        }

        // Strings that are not strings to json-c.
        Add("""{"v":"42"}""", "/v");
        Add("""{"v":"abc"}""", "/v");
        Add("""{"v":"42abc"}""", "/v");
        Add("""{"v":""}""", "/v");
        Add("""{"v":"-7"}""", "/v");
        Add("""{"v":"0"}""", "/v");
        Add("""{"v":"true"}""", "/v");
        Add("""{"v":"false"}""", "/v");

        // Numbers asked for as other things.
        Add("""{"v":42}""", "/v");
        Add("""{"v":0}""", "/v");
        Add("""{"v":-1}""", "/v");
        Add("""{"v":3.9}""", "/v");
        Add("""{"v":-3.9}""", "/v");
        Add("""{"v":4294967296}""", "/v");
        Add("""{"v":9223372036854775807}""", "/v");

        // Booleans and null.
        Add("""{"v":true}""", "/v");
        Add("""{"v":false}""", "/v");
        Add("""{"v":null}""", "/v");

        // Containers, and a key that is not there.
        Add("""{"v":[1,2,3]}""", "/v", "/v/0", "/v/2", "/v/3");
        Add("""{"v":{"w":1}}""", "/v", "/v/w", "/v/x");
        Add("""{"v":[]}""", "/v", "/v/0");
        Add("""{"v":{}}""", "/v");
        Add("""{"a":1}""", "/b", "/a/b");

        // Pointer escapes, which are the two characters a key can contain that a path cannot.
        Add("""{"a/b":1}""", "/a~1b");
        Add("""{"a~b":1}""", "/a~0b");

        // Every type through get_string, which answers for all of them but null - the trap, because
        // GetString() throws on each and holepunch reads fields it does not type-check first.
        Add("""{"v":"hello"}""", "/v");
        Add("""{"v":1.5}""", "/v");
        Add("""{"v":9.99}""", "/v");
        Add("""{"v":{"a":1}}""", "/v");
        Add("""{"v":[1,2]}""", "/v");

        // get_int parses strings, and leniently: "42px" is 42, the same rule PP140 found in the
        // settings fields and reached from the other end of the tree.
        Add("""{"v":"42px"}""", "/v");
        Add("""{"v":" 42 "}""", "/v");
        Add("""{"v":"+7"}""", "/v");
        Add("""{"v":"99999999999999999999"}""", "/v");

        // get_int SATURATES rather than wrapping. An unchecked cast gives a small wrong number
        // where json-c gives a large one, and only one of those looks wrong in a log.
        Add("""{"v":99999999999}""", "/v");
        Add("""{"v":-99999999999}""", "/v");
        Add("""{"v":2147483647}""", "/v");
        Add("""{"v":2147483648}""", "/v");
        Add("""{"v":-2147483649}""", "/v");

        // And a double truncates toward zero.
        Add("""{"v":-9.99}""", "/v");
        Add("""{"v":0.4}""", "/v");
        Add("""{"v":1e3}""", "/v");

        // An absent key, a case-sensitive lookup, and a repeated key - which answers with the LAST
        // one where GetProperty answers with the first.
        Add("""{"v":1}""", "/missing", "/v", "/V");
        Add("""{"V":1}""", "/v", "/V");
        Add("""{"v":1,"v":2}""", "/v");
        Add("""{"v":"a","v":"b","v":"c"}""", "/v");
        Add("""{"a":"false","b":"","c":0,"d":1}""", "/a", "/b", "/c", "/d");

        // Array length and indexing, including past the end and on things that are not arrays.
        Add("""{"a":[10,20,30],"b":7}""", "", "/a", "/b", "/a/0", "/a/1", "/a/2", "/a/3", "/a/4");

        // JSON Pointer, which System.Text.Json has no equivalent for at all. "/" addresses the
        // empty-string key and not the root, ~1 is a slash, ~0 a tilde, and "-" resolves to nothing.
        const string pointers = """{"a":{"b":[10,20]},"x/y":1,"x~z":2,"":3}""";
        Add(pointers,
            "", "/a", "/a/b", "/a/b/0", "/a/b/1", "/a/b/2", "/a/b/-",
            "/x~1y", "/x~0z", "/", "/a/missing", "a/b", "/A", "/a/b/00", "/a/b/-1");

        // A session message the way holepunch.c meets one: the payload is a STRING holding JSON, so
        // it is parsed twice and the inner read goes through the same accessors. The port is a
        // string get_int reads anyway, and the account id overflows an int.
        Add(SessionMessage, "/to", "/action", "/payload");
        Add(SessionPayload, "/localPeerPort", "/accountId");

        return [.. rows];
    }

    /// <summary>The outer message, whose payload is a string holding the inner document.</summary>
    public const string SessionMessage =
        """{"to":"ps5","action":"SEND","payload":"{\"accountId\":\"9999999999999999999999\",\"localPeerPort\":\"9295\"}"}""";

    /// <summary>The inner document, as json-c hands it back out of the payload field.</summary>
    public const string SessionPayload =
        """{"accountId":"9999999999999999999999","localPeerPort":"9295"}""";

    /// <summary>
    /// Whole texts, for what json-c makes of them BEFORE any accessor is reached.
    ///
    /// Three claims in one list. Where the two agree - a bare scalar is a document, trailing rubbish
    /// is ignored, an empty text is refused. Where they deliberately do NOT - json-c's lexer accepts
    /// single quotes, trailing commas, NaN, Infinity and 0x1f, and this port refuses every one,
    /// because reproducing them means writing a parser to be bug-compatible with one for inputs
    /// Sony's endpoints do not send. And what get_string returns for a container, which is json-c's
    /// own serialisation: spaced, with a slash escaped, and neither is System.Text.Json's.
    /// </summary>
    public static IReadOnlyList<string> Documents { get; } =
    [
        // Refused by both.
        "", "{", """{"a":}""", "not json at all", "   ",

        // Accepted by both.
        "42", "\"bare\"", "true", "false", "null", """{"a":1} trailing""", """{"a":1}{"b":2}""", "[1,2]",

        // json-c accepts, this port refuses. The list being exactly this is the assertion.
        "{'a':1}", "[1,2,]", """{"a":1,}""", "NaN", "Infinity", "0x1f", "01",

        // Containers, for the serialisation get_string produces.
        """{"a":1}""", """{"a":1,"b":"two"}""", """{"a":{"b":[1,2]}}""", """[1,"two",true,null]""",
        "[]", "{}", """{"url":"https://example.invalid/a/b"}""",
        """{"q":"a\"b\\c"}""", """{"t":"a\tb\nc"}""",
    ];

    /// <summary>
    /// The texts json-c accepts and this port refuses, which is a DECISION rather than a defect.
    ///
    /// Named rather than inferred from the recording: what makes this a decision is that the list is
    /// exactly these, and a json-c that stopped accepting one would be a finding.
    /// </summary>
    public static IReadOnlyList<string> Divergences { get; } =
        ["{'a':1}", "[1,2,]", """{"a":1,}""", "NaN", "Infinity", "0x1f", "01"];

    /// <summary>A whole notification.</summary>
    public const string Good = "{\"a\":1}";

    /// <summary>Another, used to show that the frame AFTER a bad one is the casualty.</summary>
    public const string AlsoGood = "{\"b\":2}";

    /// <summary>A third, for the step after that.</summary>
    public const string Third = "{\"c\":3}";

    /// <summary>An opening brace and a key with no value: not wrong, just not finished.</summary>
    public const string Truncated = "{\"a\":";

    /// <summary>Not JSON in any state of completion.</summary>
    public const string Garbage = "not json at all";

    /// <summary>
    /// The frame sequences, each through ONE tokener, in the order they are fed.
    ///
    /// What is being recorded is a STATE MACHINE, so a sequence is the unit and not a frame: the
    /// finding PP215 rests on is that a truncated frame consumes the next whole one, which no
    /// single frame can demonstrate.
    /// </summary>
    public static IReadOnlyList<(string Name, string[] Frames, bool[] ResetBefore)> Sequences { get; } =
    [
        ("three complete frames all parse",
            [Good, AlsoGood, Third],
            [false, false, false]),

        ("a truncated frame swallows the one after it",
            [Truncated, AlsoGood, Third, Good],
            [false, false, false, false]),

        ("garbage stops the tokener at once and for good",
            [Garbage, Good, AlsoGood],
            [false, false, false]),

        ("a poisoned tokener refuses what is good",
            [Garbage, AlsoGood],
            [false, false]),

        ("a fresh tokener parses what the poisoned one refused",
            [AlsoGood],
            [false]),

        ("a reset is what clears it, and holepunch.c never calls one",
            [Garbage, Good, Good],
            [false, false, true]),

        ("trailing bytes and an empty frame are harmless",
            [Good + "xx", AlsoGood, "", Third],
            [false, false, false, false]),
    ];
}
