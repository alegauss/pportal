using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP358: no response parser in lib/src matches a header name case-sensitively.
///
/// PP296 asserted this for one function and the rule was broken next door for as long as anybody
/// looked. Same lesson as PP348 and the quit reason, so this one is written over every parser.
/// </summary>
public class HeaderMatchingTests
{
    /// <summary>
    /// THE CHECK, over both parsers rather than the one that was wrong.
    /// </summary>
    [Theory]
    [InlineData(@"lib\src\session.c")]
    [InlineData(@"lib\src\ctrl.c")]
    public void NoParserMatchesAHeaderNameCaseSensitively(string relative)
    {
        string? path = HeaderMatching.Locate(relative);
        if (path is null)
            return;

        IReadOnlyList<string> sensitive =
            HeaderMatching.CaseSensitiveHeaderComparisons(File.ReadAllText(path));

        Assert.True(
            sensitive.Count == 0,
            $"{relative} compares header names with strcmp:\n  " + string.Join("\n  ", sensitive));
    }

    /// <summary>Both files are checked, so the list itself is asserted not to have gone empty.</summary>
    [Fact]
    public void BothParsersAreOnTheList()
    {
        Assert.Equal([@"lib\src\session.c", @"lib\src\ctrl.c"], HeaderMatching.Parsers);
    }

    /// <summary>And the reader finds one where there is one, so the check above means something.</summary>
    [Fact]
    public void TheReaderFindsACaseSensitiveComparison()
    {
        const string asItWas = """
            		if(strcmp(header->key, "RP-Server-Type") == 0)
            		{
            			decode(header->value);
            		}
            		else if(strcmp(header->key, "RP-Prohibit") == 0)
            			response->rp_prohibit = atoi(header->value) == 1;
            """;

        IReadOnlyList<string> found = HeaderMatching.CaseSensitiveHeaderComparisons(asItWas);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, f => f.Contains("RP-Server-Type", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("RP-Prohibit", StringComparison.Ordinal));
    }

    /// <summary>And ignores the fixed version.</summary>
    [Fact]
    public void TheReaderIgnoresACaseInsensitiveComparison()
    {
        const string fixedUp = """
            		if(strcasecmp(header->key, "RP-Server-Type") == 0)
            			decode(header->value);
            """;

        Assert.Empty(HeaderMatching.CaseSensitiveHeaderComparisons(fixedUp));
    }

    /// <summary>
    /// A strcmp against something that is not a field name is left alone.
    ///
    /// session.c compares the remote disconnect reason with strcmp and PP336 asserts that it stays
    /// exact - a reason merely containing the phrase is a different quit reason. So the shape has to
    /// tell a header name from a sentence, or fixing this would break that.
    /// </summary>
    [Fact]
    public void AComparisonAgainstSomethingElseIsNotAHeader()
    {
        const string notHeaders = """
            	if(!strcmp(session->stream_connection.remote_disconnect_reason, "Server shutting down"))
            		reason = SHUTDOWN;
            	if(!strcmp(response.rp_version, "5.0"))
            		target = PS4_9;
            """;

        Assert.Empty(HeaderMatching.CaseSensitiveHeaderComparisons(notHeaders));
    }
}
