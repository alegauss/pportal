using System.Text.Json;
using ChiakiNg.Protocol;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP33: the push envelope, and the flags a caller waits on.
/// </summary>
public class PushNotificationTests
{
    private static PushNotificationType TypeOf(string json)
    {
        using JsonDocument? document = JsonC.Parse(json);
        Assert.NotNull(document);
        return PushNotification.TypeOf(document.RootElement);
    }

    /// <summary>Each of the six identifiers reads as its own type.</summary>
    [Fact]
    public void EachIdentifierReadsAsItsOwnType()
    {
        foreach ((PushNotificationType type, string identifier) in PushNotification.DataTypes)
        {
            string json = $$"""{"dataType":"{{identifier}}"}""";
            Assert.Equal(type, TypeOf(json));
        }

        Assert.Equal(6, PushNotification.DataTypes.Count);
    }

    /// <summary>
    /// The types are POWERS OF TWO, because a caller waits on a set of them. A port that numbered
    /// them 0..5 would still have six distinct values and would make every wait match the wrong
    /// notifications.
    /// </summary>
    [Fact]
    public void TheTypesAreAMaskAndNotAnEnumeration()
    {
        foreach (PushNotificationType type in PushNotification.DataTypes.Keys)
        {
            int value = (int)type;
            Assert.True(value > 0 && (value & (value - 1)) == 0, $"{type} is {value}, not a single bit");
        }

        // And they are six DIFFERENT bits, so a mask of all six is 0b111111.
        int all = PushNotification.DataTypes.Keys.Aggregate(0, (acc, t) => acc | (int)t);
        Assert.Equal(0b111111, all);
    }

    /// <summary>
    /// Unknown is zero, so it belongs to no mask: a notification nobody recognises wakes nobody up
    /// rather than waking everybody.
    /// </summary>
    [Fact]
    public void UnknownBelongsToNoMask()
    {
        Assert.Equal(0, (int)PushNotificationType.Unknown);

        PushNotificationType everything =
            PushNotification.DataTypes.Keys.Aggregate(
                PushNotificationType.Unknown, (acc, t) => acc | t);

        Assert.False(PushNotification.Matches(PushNotificationType.Unknown, everything));
    }

    /// <summary>A caller waiting on a set is woken by any member of it and by nothing else.</summary>
    [Fact]
    public void AWaitOnASetIsWokenByAnyMemberOfIt()
    {
        PushNotificationType waiting =
            PushNotificationType.MemberCreated | PushNotificationType.MemberDeleted;

        Assert.True(PushNotification.Matches(PushNotificationType.MemberCreated, waiting));
        Assert.True(PushNotification.Matches(PushNotificationType.MemberDeleted, waiting));
        Assert.False(PushNotification.Matches(PushNotificationType.SessionCreated, waiting));
    }

    /// <summary>
    /// A missing dataType, one that is not a string, and one that is null are ONE outcome - which
    /// is what lets a caller have a single path for "not for me".
    /// </summary>
    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"dataType":42}""")]
    [InlineData("""{"dataType":true}""")]
    [InlineData("""{"dataType":null}""")]
    [InlineData("""{"dataType":{"a":1}}""")]
    [InlineData("""{"dataType":"psn:something:else"}""")]
    public void EverythingElseIsUnknown(string json)
        => Assert.Equal(PushNotificationType.Unknown, TypeOf(json));

    /// <summary>
    /// The key is camel case. The core's own error message spells it lower case, so the message is
    /// the misleading half - a port copying the message rather than the lookup finds nothing, ever.
    /// </summary>
    [Fact]
    public void TheKeyIsCamelCaseAndTheMessageIsNot()
    {
        Assert.Equal("dataType", PushNotification.TypeField);
        Assert.Equal(PushNotificationType.Unknown, TypeOf("""{"datatype":"psn:sessionManager:sys:rps:members:created"}"""));
    }

    /// <summary>Every rule above, still stated the same way in the core.</summary>
    [Fact]
    public void TheEnvelopesRulesAreStillTheQtCores()
    {
        string? path = PushNotificationSource.Locate();
        if (path is null)
            return;

        string core = File.ReadAllText(path);

        Assert.True(PushNotificationSource.TheIdentifiersAreStillThese(core), "six identifiers");
        Assert.True(PushNotificationSource.TheTypesAreStillFlags(core), "powers of two");
        Assert.True(PushNotificationSource.TheKeyIsStillCamelCase(core), "the camel-cased key");
        Assert.True(PushNotificationSource.ANonStringIsStillUnknown(core), "not-a-string is unknown");
    }
}
