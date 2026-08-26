using ChiakiNg.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ChiakiNg.Tests;

/// <summary>
/// PP404: fifty-four invariants that are not in the binary, counted.
///
/// PP357 established that Release carries NDEBUG and no assert in lib/src ships. Its check reads one
/// file and one shape. This one reads the tree and a different subject - the error code - so the
/// sites nobody looked at are at least visible, and a fifty-fourth cannot be added quietly.
/// </summary>
public class AssertedErrorCodesTests(ITestOutputHelper output)
{
    /// <summary>THE TASK. The count may fall. It may not rise.</summary>
    [Fact]
    public void NoMoreErrorCodesAreInspectedByAnAssertAlone()
    {
        string? directory = AssertedErrorCodes.Locate();
        if (directory is null)
            return;

        IReadOnlyDictionary<string, IReadOnlyList<string>> census = AssertedErrorCodes.Census(directory);
        int total = AssertedErrorCodes.Total(census);

        foreach ((string file, IReadOnlyList<string> asserts) in census.OrderByDescending(e => e.Value.Count))
            output.WriteLine($"{file,-24} {asserts.Count}");

        // PP271: a sweep that found nothing has not passed - a regex that stopped matching would
        // otherwise read as fifty-three corrections nobody made.
        Assert.True(
            total >= AssertedErrorCodes.Floor,
            $"{total} found, below the floor of {AssertedErrorCodes.Floor} - the census has stopped reading");

        Assert.True(
            total <= AssertedErrorCodes.Ceiling,
            $"{total} error codes are inspected by nothing but an assert, ceiling {AssertedErrorCodes.Ceiling}");

        // And when it falls, the ceiling falls with it in the same commit - a ratchet left loose has
        // given the gain away.
        Assert.Equal(AssertedErrorCodes.Ceiling, total);
    }

    /// <summary>
    /// The one that was a defect rather than a candidate: a failed lock that carried on.
    ///
    /// In the shipped build the assert was absent, so a <c>chiaki_mutex_lock</c> that failed still
    /// enqueued a notification under a lock it did not hold, signalled, and unlocked a mutex it had
    /// never taken.
    /// </summary>
    [Fact]
    public void TheWebsocketThreadChecksItsNotificationLock()
    {
        string? path = DeviceListSource.Locate();
        if (path is null)
            return;

        Assert.True(
            AssertedErrorCodes.TheNotificationLockIsChecked(File.ReadAllText(path)),
            "the notification lock is asserted rather than checked again");
    }

    /// <summary>PP272: and the census answers no to a file with nothing in it.</summary>
    [Fact]
    public void TheCensusFindsNothingInAnEmptyFile()
    {
        Assert.Empty(AssertedErrorCodes.InFile(""));
        Assert.False(AssertedErrorCodes.TheNotificationLockIsChecked(""));

        // What it does find, it finds whole - including across a line break.
        Assert.Single(AssertedErrorCodes.InFile("assert(err == CHIAKI_ERR_SUCCESS);"));
        Assert.Single(AssertedErrorCodes.InFile("assert(err\n    == CHIAKI_ERR_SUCCESS);"));

        // An assert about something else is not one of these.
        Assert.Empty(AssertedErrorCodes.InFile("assert(len < size);"));

        // And a note quoting a corrected site is not the site - PP399, PP400, PP401, PP403.
        Assert.Empty(AssertedErrorCodes.InFile("// was assert(err == CHIAKI_ERR_SUCCESS);"));
        Assert.Empty(AssertedErrorCodes.InFile("/* assert(err == CHIAKI_ERR_SUCCESS); */"));
    }
}
