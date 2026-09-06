using ChiakiNg.Session;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP733: the managed readers of a takion message, counted rather than remembered.
///
/// PP730 found the leniency and fixed the bang; PP732 carried the check to the disconnect and the
/// streaminfo. All three were found by grepping, which is the shape this port has now paid for five
/// times - PP279, PP718, PP720, PP724 and PP735 are the same argument about a hand-kept list.
///
/// BOTH DIRECTIONS, WHICH IS THE POINT. A file that starts parsing a takion message and is not
/// listed fails by name; a listed file that stops fails too. And a row claiming to check the
/// required set is read for the call that does it, so the verdict is not taken on trust.
/// </summary>
public class ProtobufReadersTests(ITestOutputHelper output)
{
    /// <summary>
    /// THE CENSUS: the sweep's files and counts are exactly the rows'.
    ///
    /// Counts and not just names, because a second parse added to a file already listed is the same
    /// silence one level down - the shape DisconnectMessage already has two of.
    /// </summary>
    [Fact]
    public void EveryFileThatParsesAMessageIsListedWithItsCount()
    {
        if (ProtobufReaders.LocateManaged() is not { } managed)
            return;

        IReadOnlyDictionary<string, int> swept = ProtobufReaders.SitesUnder(managed);

        output.WriteLine(string.Join(", ", swept.Select(one => $"{one.Key} x{one.Value}")));

        // A sweep that found nothing would agree with any list at all.
        Assert.NotEmpty(swept);

        Assert.Equal(
            ProtobufReaders.All.Select(one => one.File).Order(StringComparer.OrdinalIgnoreCase),
            swept.Keys.Order(StringComparer.OrdinalIgnoreCase));

        foreach (ProtobufReader row in ProtobufReaders.All)
        {
            Assert.True(
                swept.TryGetValue(row.File, out int sites),
                $"{row.File} is listed and parses nothing");

            Assert.True(
                sites == row.Sites,
                $"{row.File} parses {sites} time(s) and the row says {row.Sites}");
        }
    }

    /// <summary>
    /// A row claiming to check the required set makes the call that does it.
    ///
    /// The half that makes the verdict worth having. Without it a reader could be listed as
    /// checking and have stopped, which is exactly the state PP730 found three files in.
    /// </summary>
    [Fact]
    public void EveryRowThatClaimsTheCheckMakesIt()
    {
        foreach (ProtobufReader row in ProtobufReaders.All)
        {
            if (ProtobufReaders.Locate(row.File) is not { } path)
                continue;

            bool checks = ProtobufReaders.ChecksTheRequiredSet(File.ReadAllText(path));

            Assert.True(
                checks == (row.Reading == ProtobufReading.ChecksRequired),
                $"{row.File} is judged {row.Reading} and {(checks ? "does" : "does not")} check the "
                    + "required set");
        }
    }

    /// <summary>
    /// Four readers decide on a console's message, and one round trip does not.
    ///
    /// PP773 is the fourth: the idle arm switches on the payload type, which the C reaches only past
    /// a pb_decode that has already enforced the required set. A row arriving here is a new place
    /// this port DECIDES on bytes a console sent, which is the count PP733 wanted stated.
    /// </summary>
    [Fact]
    public void FourDecideAndOneComparesGenerators()
    {
        Assert.Equal(4, ProtobufReaders.All.Count(one => one.Reading == ProtobufReading.ChecksRequired));
        Assert.Single(ProtobufReaders.All, one => one.Reading == ProtobufReading.RoundTrip);

        // Five sites decide and two compare, which is the number a reader of the count wants.
        Assert.Equal(
            5,
            ProtobufReaders.All.Where(one => one.Reading == ProtobufReading.ChecksRequired).Sum(one => one.Sites));
    }

    /// <summary>Every row says why, because a mapping with no reason is a table.</summary>
    [Fact]
    public void EveryRowGivesAReason()
        => Assert.All(
            ProtobufReaders.All,
            one => Assert.False(string.IsNullOrWhiteSpace(one.Why)));

    /// <summary>
    /// The census does not report itself, which is the trap this tree keeps meeting.
    ///
    /// It spells the call it looks for, so a sweep reading its own declaration would list it as a
    /// reader - the same fixture-finding shape PP716's locking census hit against the export sweep.
    /// </summary>
    [Fact]
    public void TheCensusIsNotItsOwnFixture()
    {
        if (ProtobufReaders.LocateManaged() is not { } managed)
            return;

        Assert.DoesNotContain(
            ProtobufReaders.SitesUnder(managed).Keys,
            one => one.EndsWith(ProtobufReaders.CensusFileName, StringComparison.OrdinalIgnoreCase));

        // And it really does spell the call, or the exclusion above is guarding nothing.
        if (ProtobufReaders.Locate(@"app\Session\ProtobufReaders.cs") is { } self)
            Assert.Contains(ProtobufReaders.ParseCall, File.ReadAllText(self), StringComparison.Ordinal);
    }
}
