using System.Text.Json;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the same documents through both JSON implementations, accessor by accessor.
///
/// <see cref="JsonC"/> reproduces json-c's ANSWERS in managed code, which is a translation and not
/// a wrapper - so the only correctness test it has is behavioural. The cases already written cover
/// the shapes somebody thought of; this covers the ones json-c coerces without being asked, which
/// is where a reimplementation of an accessor quietly disagrees:
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
/// Every row is run through both and compared. A row that both get wrong the same way is not
/// caught here and is not meant to be: the claim is that the two clients agree, because that is
/// what lets a reply parsed by one be trusted by the other.
/// </summary>
public class JsonDifferentialTests
{
    /// <summary>The documents, chosen for what json-c does to them rather than for shape.</summary>
    public static TheoryData<string, string> Documents()
    {
        var data = new TheoryData<string, string>();

        void Add(string json, params string[] paths)
        {
            foreach (string path in paths)
                data.Add(json, path);
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

        return data;
    }

    /// <summary>
    /// Every accessor, on one node, from both sides. Compared as a tuple so a failure names the
    /// document and the path rather than only the accessor that happened to be checked first.
    /// </summary>
    [Theory]
    [MemberData(nameof(Documents))]
    public void BothImplementationsAnswerAlike(string json, string path)
    {
        using NativeJson? native = NativeJson.Parse(json);
        Assert.NotNull(native);

        JsonDocument? managed = JsonC.Parse(json);
        Assert.NotNull(managed);

        using (managed)
        {
            IntPtr nativeNode = native.Pointer(path);
            JsonElement? managedNode = JsonC.Pointer(managed.RootElement, path);

            // Present or absent has to agree first: everything below is about a node, and the two
            // disagreeing here would make the rest compare different things.
            Assert.Equal(nativeNode == IntPtr.Zero, managedNode is null);

            if (nativeNode == IntPtr.Zero)
                return;

            Assert.Equal(NativeJson.TypeOf(nativeNode), JsonC.TypeOf(managedNode));
            Assert.Equal(NativeJson.String(nativeNode), JsonC.String(managedNode));
            Assert.Equal(NativeJson.Int(nativeNode), JsonC.Int(managedNode));
            Assert.Equal(NativeJson.Int64(nativeNode), JsonC.Int64(managedNode));
            Assert.Equal(NativeJson.Bool(nativeNode), JsonC.Bool(managedNode));
            Assert.Equal(NativeJson.ArrayLength(nativeNode), JsonC.ArrayLength(managedNode));
        }
    }

    /// <summary>
    /// A document neither can parse is refused by both. Asserted because "returns null" and
    /// "throws" are both plausible answers and only one of them is json-c's.
    /// </summary>
    /// <remarks>
    /// The trailing comma is deliberately NOT here. json-c's lexer accepts it and this port does
    /// not, which <see cref="JsonCTests.JsonCsLexerIsLenientWhereThisIsNot"/> records as a decision:
    /// matching a lenient lexer means being bug-compatible with a parser, for inputs Sony's
    /// endpoints do not send. This differential re-found it and the decision stood - the line is
    /// drawn at the lexer, and everything past it is matched exactly.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("{\"a\":}")]
    [InlineData("not json at all")]
    public void BothRefuseTheSameRubbish(string json)
    {
        using NativeJson? native = NativeJson.Parse(json);
        JsonDocument? managed = JsonC.Parse(json);

        using (managed)
            Assert.Equal(native is null, managed is null);
    }

    /// <summary>
    /// The comparison has to be able to fail, or every row above passes by asking nothing. Two
    /// documents that genuinely differ must come out different on the accessor that differs.
    /// </summary>
    [Fact]
    public void TheComparisonCanTellTwoDocumentsApart()
    {
        using NativeJson? one = NativeJson.Parse("""{"v":1}""");
        using NativeJson? two = NativeJson.Parse("""{"v":2}""");

        Assert.NotNull(one);
        Assert.NotNull(two);

        Assert.NotEqual(
            NativeJson.Int(one.Pointer("/v")),
            NativeJson.Int(two.Pointer("/v")));
    }
}
