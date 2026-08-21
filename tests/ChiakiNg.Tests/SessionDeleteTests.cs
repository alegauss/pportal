using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP235: the delete, and the three messages that name the wrong call.
///
/// <see cref="AllThreeMisnamedMessagesAreStillThere"/> is the one that carries the task. Each of
/// the three was found separately while porting a different function; together they are a property
/// of the file rather than three coincidences.
/// </summary>
public class SessionDeleteTests(ITestOutputHelper output)
{
    /// <summary>The verb is a custom request, which is how curl sends a DELETE.</summary>
    [Fact]
    public void TheVerbIsDelete() => Assert.Equal("DELETE", SessionDelete.Method);

    /// <summary>The URL carries the session id and nothing else.</summary>
    [Fact]
    public void TheUrlCarriesTheSessionId()
    {
        string url = SessionDelete.UrlFor("abc123");

        Assert.Contains("abc123", url, StringComparison.Ordinal);
        Assert.StartsWith("https://", url, StringComparison.Ordinal);
    }

    /// <summary>
    /// A JSON content type on a request with no body. Carried because the core carries it: a
    /// DELETE with no entity has nothing for it to describe, and dropping it changes what PSN
    /// receives rather than tidying anything.
    /// </summary>
    [Fact]
    public void ItSendsAContentTypeForABodyItDoesNotHave()
    {
        IReadOnlyList<string> headers = SessionDelete.Headers("Authorization: Bearer t");

        Assert.Equal(2, headers.Count);
        Assert.Contains(PsnEndpoints.JsonContentType, headers);
        Assert.Contains("Bearer t", headers[0], StringComparison.Ordinal);
    }

    /// <summary>Three, and each names something the function it sits in does not do.</summary>
    [Fact]
    public void ThreeMessagesNameTheWrongCall()
    {
        Assert.Equal(3, MisnamedLogs.All.Count);

        Assert.Equal(
            ["deleteSession", "get_stun_servers", "http_check_session"],
            MisnamedLogs.All.Select(m => m.Function).OrderBy(f => f, StringComparer.Ordinal));
    }

    /// <summary>
    /// And every one is still in the file. This is the assertion that keeps them counted: a message
    /// corrected upstream shows up here as a name missing from the list rather than as a silent
    /// drop, and a fourth found later has somewhere to go.
    /// </summary>
    [Fact]
    public void AllThreeMisnamedMessagesAreStillThere()
    {
        string? file = MisnamedLogs.Locate();
        if (file is null)
            return;

        string core = File.ReadAllText(file);

        IReadOnlyList<MisnamedLogs.Misnamed> present = MisnamedLogs.StillPresent(core);

        foreach (MisnamedLogs.Misnamed found in present)
            output.WriteLine($"{found.Function}: names {found.Names}");

        IEnumerable<string> gone = MisnamedLogs.All
            .Select(m => m.Function)
            .Except(present.Select(m => m.Function));

        Assert.True(
            present.Count == MisnamedLogs.All.Count,
            "a message this port reproduces is no longer in the core, so the two now disagree "
                + "about what a failure says: " + string.Join(", ", gone));
    }
}
