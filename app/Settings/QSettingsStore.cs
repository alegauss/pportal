using Microsoft.Win32;

namespace ChiakiNg.Settings;

/// <summary>One console the user has already registered, as the Qt client stored it.</summary>
public sealed record RegisteredHost
{
    public required string ServerNickname { get; init; }
    public required byte[] ServerMac { get; init; }
    /// <summary>Chiaki's target enum - 1000100 is a PS5 on the store this was read from.</summary>
    public required int Target { get; init; }
    public byte[]? RpRegistKey { get; init; }
    public byte[]? RpKey { get; init; }
    public int RpKeyType { get; init; }
    public string? ConsolePin { get; init; }
    public string? ApName { get; init; }

    /// <summary>The MAC as the UI spells it, which is how a user recognises their console.</summary>
    public string MacText => string.Join(':', ServerMac.Select(b => b.ToString("x2")));
}

/// <summary>
/// PP2: the store the port inherits, read where it already is.
///
/// The decision this implements is which side moves. Reading QSettings from .NET is a registry
/// read; writing a new store and migrating on first run costs a migration nobody can test
/// against every version that ever shipped. So this reads what is there and never writes: the
/// old keys stay untouched, and a user who rolls back to the Qt build still has their consoles.
///
/// Read-only is enforced by opening the key without write access, not by convention.
/// </summary>
public sealed class QSettingsStore
{
    /// <summary>
    /// HKCU\SOFTWARE\Chiaki\Chiaki. Both halves are "Chiaki" because main.cpp sets
    /// setOrganizationName("Chiaki") and setApplicationName("Chiaki"), and QSettings joins them.
    /// </summary>
    public const string DefaultKeyPath = @"SOFTWARE\Chiaki\Chiaki";

    private readonly string keyPath;

    public QSettingsStore(string? keyPath = null) => this.keyPath = keyPath ?? DefaultKeyPath;

    /// <summary>Whether a Qt client has ever run for this user. A fresh install has no key.</summary>
    public bool Exists()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key is not null;
    }

    /// <summary>
    /// Every console the Qt client has registered, in the order it stored them.
    ///
    /// QSettings writes an array as a `size` value beside numbered subkeys starting at 1. The
    /// size is trusted only as far as a subkey actually exists: a store interrupted mid-write
    /// has a size that runs past its entries, and a user with three consoles should see three
    /// rather than an exception.
    /// </summary>
    public IReadOnlyList<RegisteredHost> RegisteredHosts()
    {
        var hosts = new List<RegisteredHost>();
        using RegistryKey? array = Registry.CurrentUser.OpenSubKey($@"{keyPath}\registered_hosts");
        if (array is null)
            return hosts;

        int size = QSettingsValue.AsInt(array.GetValue("size")) ?? 0;
        for (int i = 1; i <= size; i++)
        {
            using RegistryKey? entry = array.OpenSubKey(i.ToString());
            if (entry is null)
                continue;

            byte[]? mac = QSettingsValue.AsByteArray(entry.GetValue("server_mac"));
            string? nickname = QSettingsValue.AsString(entry.GetValue("server_nickname"));
            // A console with no MAC cannot be woken and cannot be told apart from another, so it
            // is not a console this can hand to a screen. Skipped rather than surfaced empty.
            if (mac is null || nickname is null)
                continue;

            hosts.Add(new RegisteredHost
            {
                ServerNickname = nickname,
                ServerMac = mac,
                Target = QSettingsValue.AsInt(entry.GetValue("target")) ?? 0,
                RpRegistKey = QSettingsValue.AsByteArray(entry.GetValue("rp_regist_key")),
                RpKey = QSettingsValue.AsByteArray(entry.GetValue("rp_key")),
                RpKeyType = QSettingsValue.AsInt(entry.GetValue("rp_key_type")) ?? 0,
                ConsolePin = QSettingsValue.AsString(entry.GetValue("console_pin")),
                ApName = QSettingsValue.AsString(entry.GetValue("ap_name")),
            });
        }
        return hosts;
    }

    /// <summary>
    /// The PSN account id and refresh token, or nulls where the user has not linked an account.
    ///
    /// These are the credential half of the store and the reason this class never writes: a
    /// token copied into a second place is a token that has to be revoked in two.
    /// </summary>
    public (string? AccountId, string? RefreshToken) PsnAccount()
    {
        using RegistryKey? settings = Registry.CurrentUser.OpenSubKey($@"{keyPath}\settings");
        if (settings is null)
            return (null, null);
        return (
            QSettingsValue.AsString(settings.GetValue("psn_account_id")),
            QSettingsValue.AsString(settings.GetValue("psn_refresh_token")));
    }
}
