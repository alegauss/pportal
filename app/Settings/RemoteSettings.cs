using System.Globalization;
using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>
/// PP16: the four values that together are "logged in to PSN", and the fact that it takes all four.
///
/// The Remote tab has two buttons where a port would put one. Login is shown when ANY of the four
/// is empty and Clear when ALL FOUR are set, so a half-written token set - a refresh token with no
/// expiry, an account id with no auth token - shows Login and offers to start over. That is the
/// useful behaviour and it is not what a port checking the account id alone would produce: that
/// port would show Clear over a set of credentials that cannot be used.
///
/// Clear writes four empty strings rather than removing four keys. The difference matters to the
/// store: PP2's table declares these, and an empty string is a declared key with nothing in it.
/// </summary>
public static class PsnTokens
{
    /// <summary>The refresh token, which is the one that outlives a session.</summary>
    public const string RefreshTokenKey = "settings/psn_refresh_token";

    /// <summary>The access token itself.</summary>
    public const string AuthTokenKey = "settings/psn_auth_token";

    /// <summary>When that token stops working, stored as a string rather than a date.</summary>
    public const string ExpiryKey = "settings/psn_auth_token_expiry";

    /// <summary>The account id, which is the only one of the four a user ever sees.</summary>
    public const string AccountIdKey = "settings/psn_account_id";

    /// <summary>All four, in the order the QML tests them.</summary>
    public static IReadOnlyList<string> Keys { get; } =
        [RefreshTokenKey, AuthTokenKey, ExpiryKey, AccountIdKey];
}

/// <summary>
/// PP16: hole-punching port guessing, whose two sliders default differently from each other.
///
/// The count's default is its slider's MAXIMUM - 75 out of 75 - and the socket count's is half of
/// its 500. A port that took either slider's minimum, or that treated one default as a pattern for
/// the other, would leave port guessing weaker than upstream on a network that needs it most.
///
/// And the clamping is one-sided. settings.cpp forces a negative to zero on the way in and does
/// nothing about a value above the slider's top, so the floor lives in the store and the ceiling
/// lives only on screen: a settings file holding 900 sockets comes back as 900.
/// </summary>
public static class PortGuessing
{
    public const string EnabledKey = "settings/port_guessing_enabled";

    public const string CountKey = "settings/port_guessing_count";

    public const string SocketCountKey = "settings/port_guessing_socket_count";

    /// <summary>Off, which is what the tab's hint says.</summary>
    public const bool EnabledDefault = false;

    /// <summary>The slider's top, and also the default.</summary>
    public const int CountMaximum = 75;

    /// <summary>PORT_GUESS_COUNT_DEFAULT, which is the maximum and not a middle.</summary>
    public const int CountDefault = 75;

    public const int SocketMaximum = 500;

    /// <summary>PORT_GUESS_SOCKS_DEFAULT, which IS a middle - the two sliders do not agree.</summary>
    public const int SocketDefault = 250;

    /// <summary>Both sliders start at zero, which is a real choice rather than an absent one.</summary>
    public const int Minimum = 0;

    /// <summary>
    /// What the store does to a value on the way in: a floor and no ceiling. Reproduced rather
    /// than completed - a port that also clamped the top would disagree with a settings file the
    /// other client wrote and could not say why.
    /// </summary>
    public static int Clamp(int count) => count < Minimum ? Minimum : count;

    /// <summary>The count's label, which is the number and a word.</summary>
    public static string CountCaption(int count)
        => count.ToString(CultureInfo.InvariantCulture) + " guesses";

    /// <summary>And the socket count's, whose word is the only difference.</summary>
    public static string SocketCaption(int count)
        => count.ToString(CultureInfo.InvariantCulture) + " sockets";
}

/// <summary>
/// PP16: the settings screen's Remote tab, the eighth of the nine.
///
/// Two buttons that are never both on show, a checkbox, and two sliders. The buttons are the
/// interesting half - see <see cref="PsnTokens"/> - and the sliders are the half where a default
/// is not where a port would guess it.
/// </summary>
public sealed class RemoteSettingsViewModel : DialogViewModel
{
    private string refreshToken = "";
    private string authToken = "";
    private string expiry = "";
    private string accountId = "";
    private bool portGuessingEnabled = PortGuessing.EnabledDefault;
    private int portGuessCount = PortGuessing.CountDefault;
    private int portGuessSocketCount = PortGuessing.SocketDefault;

    /// <summary>The tab with the defaults and no credentials, which is a fresh install.</summary>
    public RemoteSettingsViewModel()
    {
    }

    /// <summary>The tab as the store holds it.</summary>
    public RemoteSettingsViewModel(IPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        // Null and empty are one state here: a credential that is absent and one that is blank
        // are both "log in again", and the four tests below are length tests for that reason.
        refreshToken = preferences.GetString(PsnTokens.RefreshTokenKey) ?? "";
        authToken = preferences.GetString(PsnTokens.AuthTokenKey) ?? "";
        expiry = preferences.GetString(PsnTokens.ExpiryKey) ?? "";
        accountId = preferences.GetString(PsnTokens.AccountIdKey) ?? "";
        portGuessingEnabled = preferences.GetBool(PortGuessing.EnabledKey);
        portGuessCount = preferences.GetInt(PortGuessing.CountKey);
        portGuessSocketCount = preferences.GetInt(PortGuessing.SocketCountKey);
    }

    protected override string ButtonProperty => nameof(LoginVisible);

    public string RefreshToken
    {
        get => refreshToken;
        set { Set(ref refreshToken, value ?? ""); RaiseButtons(); }
    }

    public string AuthToken
    {
        get => authToken;
        set { Set(ref authToken, value ?? ""); RaiseButtons(); }
    }

    public string Expiry
    {
        get => expiry;
        set { Set(ref expiry, value ?? ""); RaiseButtons(); }
    }

    public string AccountId
    {
        get => accountId;
        set { Set(ref accountId, value ?? ""); RaiseButtons(); }
    }

    public bool PortGuessingEnabled
    {
        get => portGuessingEnabled;
        set => Set(ref portGuessingEnabled, value);
    }

    /// <summary>How many ports to guess. Clamped below and not above, as the store is.</summary>
    public int PortGuessCount
    {
        get => portGuessCount;
        set { Set(ref portGuessCount, PortGuessing.Clamp(value)); Raise(nameof(PortGuessCountCaption)); }
    }

    /// <summary>And how many sockets to guess them on.</summary>
    public int PortGuessSocketCount
    {
        get => portGuessSocketCount;
        set
        {
            Set(ref portGuessSocketCount, PortGuessing.Clamp(value));
            Raise(nameof(PortGuessSocketCountCaption));
        }
    }

    /// <summary>
    /// Whether all four credentials are present. Anything less is not a partial login, it is a
    /// login that has to be done again.
    /// </summary>
    public bool LoggedIn
        => RefreshToken.Length > 0 && AuthToken.Length > 0
            && Expiry.Length > 0 && AccountId.Length > 0;

    /// <summary>The Login button, shown while any of the four is missing.</summary>
    public bool LoginVisible => !LoggedIn;

    /// <summary>And the Clear button, shown only when all four are there. Never both.</summary>
    public bool ClearVisible => LoggedIn;

    public string PortGuessCountCaption => PortGuessing.CountCaption(PortGuessCount);

    public string PortGuessSocketCountCaption => PortGuessing.SocketCaption(PortGuessSocketCount);

    /// <summary>
    /// Clearing: four empty strings, not four removals. Focus then moves to the Login button,
    /// which this call is what makes visible - the QML forces it in the same handler.
    /// </summary>
    public void ClearTokens()
    {
        RefreshToken = "";
        AuthToken = "";
        Expiry = "";
        AccountId = "";
    }

    /// <summary>
    /// Both buttons, on every credential change. They are opposite tests over the same four
    /// values, so raising one of them is a screen with two buttons on it or with none.
    /// </summary>
    private void RaiseButtons()
    {
        Raise(nameof(LoggedIn));
        Raise(nameof(LoginVisible));
        Raise(nameof(ClearVisible));
    }
}

/// <summary>
/// PP16: the Remote tab's rules where the Qt client states them.
/// </summary>
public static class RemoteSettingsSource
{
    /// <summary>The settings screen.</summary>
    public static string? LocateQml() => GeneralSettingsSource.LocateQml();

    /// <summary>Where the defaults and the one-sided clamp live.</summary>
    public static string? LocateSettingsCpp()
        => GeneralSettingsSource.Locate(GeneralSettingsSource.SettingsCpp);

    /// <summary>Whether the two buttons are still opposite tests over the same four values.</summary>
    public static bool TheTwoButtonsTestAllFour(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        return qml.Contains(
                "visible: !Chiaki.settings.psnRefreshToken || !Chiaki.settings.psnAuthToken "
                + "|| !Chiaki.settings.psnAuthTokenExpiry || !Chiaki.settings.psnAccountId",
                StringComparison.Ordinal)
            && qml.Contains(
                "visible: Chiaki.settings.psnRefreshToken && Chiaki.settings.psnAuthToken "
                + "&& Chiaki.settings.psnAuthTokenExpiry && Chiaki.settings.psnAccountId",
                StringComparison.Ordinal);
    }

    /// <summary>Whether clearing still writes empty strings into all four.</summary>
    public static bool ClearingWritesFourEmptyStrings(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);
        return qml.Contains("Chiaki.settings.psnRefreshToken = \"\"", StringComparison.Ordinal)
            && qml.Contains("Chiaki.settings.psnAuthToken = \"\"", StringComparison.Ordinal)
            && qml.Contains("Chiaki.settings.psnAuthTokenExpiry = \"\"", StringComparison.Ordinal)
            && qml.Contains("Chiaki.settings.psnAccountId = \"\"", StringComparison.Ordinal);
    }

    /// <summary>Whether the count's default is still its slider's own maximum.</summary>
    public static bool TheCountDefaultsToItsMaximum(string cpp, string qml)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        ArgumentNullException.ThrowIfNull(qml);

        return cpp.Contains(
                $"static const int PORT_GUESS_COUNT_DEFAULT = {PortGuessing.CountDefault};",
                StringComparison.Ordinal)
            && qml.Contains($"to: {PortGuessing.CountMaximum}", StringComparison.Ordinal);
    }

    /// <summary>And whether the socket count's still is not.</summary>
    public static bool TheSocketDefaultIsNotItsMaximum(string cpp, string qml)
    {
        ArgumentNullException.ThrowIfNull(cpp);
        ArgumentNullException.ThrowIfNull(qml);

        return cpp.Contains(
                $"static const int PORT_GUESS_SOCKS_DEFAULT = {PortGuessing.SocketDefault};",
                StringComparison.Ordinal)
            && qml.Contains($"to: {PortGuessing.SocketMaximum}", StringComparison.Ordinal)
            && PortGuessing.SocketDefault != PortGuessing.SocketMaximum;
    }

    /// <summary>Whether the store still clamps the floor and leaves the ceiling to the slider.</summary>
    public static bool TheClampIsOneSided(string cpp)
    {
        ArgumentNullException.ThrowIfNull(cpp);

        int setter = cpp.IndexOf("void Settings::SetPortGuessCount(int count)", StringComparison.Ordinal);
        if (setter < 0)
            return false;

        int end = cpp.IndexOf("port_guessing_count\", count);", setter, StringComparison.Ordinal);
        if (end < 0)
            return false;

        string body = cpp[setter..end];
        return body.Contains("if(count < 0)", StringComparison.Ordinal)
            && !body.Contains("if(count > ", StringComparison.Ordinal);
    }
}
