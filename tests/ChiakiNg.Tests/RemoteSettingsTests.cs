using ChiakiNg.Settings;
using Xunit;

namespace ChiakiNg.Tests;

/// <summary>
/// PP16: the Remote tab, whose two buttons are one condition read from both ends.
/// </summary>
public class RemoteSettingsTests
{
    private static RemoteSettingsViewModel LoggedIn() => new()
    {
        RefreshToken = "r",
        AuthToken = "a",
        Expiry = "2026-08-21",
        AccountId = "id",
    };

    /// <summary>With all four present the tab offers to clear, and never both buttons at once.</summary>
    [Fact]
    public void AllFourCredentialsShowClearAndNotLogin()
    {
        RemoteSettingsViewModel model = LoggedIn();

        Assert.True(model.LoggedIn);
        Assert.True(model.ClearVisible);
        Assert.False(model.LoginVisible);
    }

    /// <summary>
    /// And ANY one of the four missing shows Login. This is the finding: a port testing the
    /// account id alone would offer Clear over credentials that cannot be used.
    /// </summary>
    [Theory]
    [InlineData("RefreshToken")]
    [InlineData("AuthToken")]
    [InlineData("Expiry")]
    [InlineData("AccountId")]
    public void AnyMissingCredentialShowsLogin(string missing)
    {
        RemoteSettingsViewModel model = LoggedIn();

        switch (missing)
        {
            case "RefreshToken": model.RefreshToken = ""; break;
            case "AuthToken": model.AuthToken = ""; break;
            case "Expiry": model.Expiry = ""; break;
            case "AccountId": model.AccountId = ""; break;
        }

        Assert.False(model.LoggedIn);
        Assert.True(model.LoginVisible);
        Assert.False(model.ClearVisible);
    }

    /// <summary>Clearing empties all four rather than the one a user was looking at.</summary>
    [Fact]
    public void ClearingEmptiesAllFour()
    {
        RemoteSettingsViewModel model = LoggedIn();

        model.ClearTokens();

        Assert.Equal("", model.RefreshToken);
        Assert.Equal("", model.AuthToken);
        Assert.Equal("", model.Expiry);
        Assert.Equal("", model.AccountId);
        Assert.True(model.LoginVisible);
    }

    /// <summary>
    /// The count's default is its slider's MAXIMUM and the socket count's is half of its own. The
    /// two sliders sit side by side and do not agree about where a default goes.
    /// </summary>
    [Fact]
    public void TheTwoSlidersDefaultDifferently()
    {
        Assert.Equal(PortGuessing.CountMaximum, PortGuessing.CountDefault);
        Assert.NotEqual(PortGuessing.SocketMaximum, PortGuessing.SocketDefault);
        Assert.Equal(250, PortGuessing.SocketDefault);

        var model = new RemoteSettingsViewModel();
        Assert.Equal("75 guesses", model.PortGuessCountCaption);
        Assert.Equal("250 sockets", model.PortGuessSocketCountCaption);
    }

    /// <summary>
    /// The clamp is one-sided: a negative becomes zero and a value above the slider's top survives.
    /// Reproduced rather than completed, because completing it would disagree with a settings file
    /// the other client wrote and give no reason.
    /// </summary>
    [Fact]
    public void TheClampHasAFloorAndNoCeiling()
    {
        var model = new RemoteSettingsViewModel { PortGuessCount = -5 };
        Assert.Equal(0, model.PortGuessCount);

        model.PortGuessCount = 900;
        Assert.Equal(900, model.PortGuessCount);

        model.PortGuessSocketCount = -1;
        Assert.Equal(0, model.PortGuessSocketCount);
    }

    /// <summary>Port guessing is off by default, which is what the hint beside it says.</summary>
    [Fact]
    public void PortGuessingIsOffByDefault()
        => Assert.False(new RemoteSettingsViewModel().PortGuessingEnabled);

    /// <summary>Every rule above, still stated the same way in the screen and the store.</summary>
    [Fact]
    public void TheRemoteTabsRulesAreStillTheQtClients()
    {
        string? qmlPath = RemoteSettingsSource.LocateQml();
        string? cppPath = RemoteSettingsSource.LocateSettingsCpp();
        if (qmlPath is null || cppPath is null)
            return;

        string qml = File.ReadAllText(qmlPath);
        string cpp = File.ReadAllText(cppPath);

        Assert.True(RemoteSettingsSource.TheTwoButtonsTestAllFour(qml), "four values, both ways");
        Assert.True(RemoteSettingsSource.ClearingWritesFourEmptyStrings(qml), "four empty strings");
        Assert.True(RemoteSettingsSource.TheCountDefaultsToItsMaximum(cpp, qml), "75 of 75");
        Assert.True(RemoteSettingsSource.TheSocketDefaultIsNotItsMaximum(cpp, qml), "250 of 500");
        Assert.True(RemoteSettingsSource.TheClampIsOneSided(cpp), "a floor and no ceiling");
    }
}
