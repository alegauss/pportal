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
/// A console the user told the client to stop showing. Keyed by MAC, which is why an entry
/// without one hides nothing.
/// </summary>
public sealed record HiddenHost(string? ServerNickname, byte[] ServerMac)
{
    public string MacText => string.Join(':', ServerMac.Select(b => b.ToString("x2")));
}

/// <summary>
/// A console the user added by address, because discovery never found it - which makes it exactly
/// the console they cannot re-add by discovery if this list is lost.
/// </summary>
public sealed record ManualHost(int Id, string? Host, bool Registered, byte[]? RegisteredMac);

/// <summary>
/// One SDL mapping string for one vid:pid. A user who remapped a third-party pad did it because
/// the client mishandles that pad, so losing this is losing the fix and keeping the problem.
/// </summary>
public sealed record ControllerMapping(string VidPid, string Mapping);

/// <summary>
/// PP2: the store the port inherits, read where it already is.
///
/// The decision this implements is which side moves. Reading QSettings from .NET is a registry
/// read; writing a new store and migrating on first run costs a migration nobody can test
/// against every version that ever shipped. So this reads what is there and never writes: the
/// old keys stay untouched, and a user who rolls back to the Qt build still has their consoles.
///
/// Read-only is enforced by opening the key without write access, not by convention.
///
/// There is not one store, there are three
/// ---------------------------------------
/// Settings::Settings opens three QSettings, and which one a value lives in is not a detail a
/// reader can skip. The application half of the name is what changes:
///
///   Chiaki\Chiaki             the default store. Holds settings/current_profile and the
///                             profiles array, and nothing reads those anywhere else.
///   Chiaki\Chiaki-&lt;profile&gt;   the active profile's store, when one is set. Registered consoles,
///                             the PSN account and every preference live HERE, not above.
///   Chiaki\pl_render_params   the libplacebo colour pipeline, a store of its own.
///
/// Reading consoles out of the default store is therefore right only for a user with no profile,
/// and silently empty for one with a profile - which is exactly the reinstall this task exists to
/// prevent. So the profile is resolved first and every other read follows it.
///
/// Qt takes the same two steps in main.cpp: it builds a Settings on the default store purely to
/// ask GetCurrentProfile, then builds a second one on the answer and runs the client off that.
/// </summary>
public sealed class QSettingsStore
{
    /// <summary>
    /// HKCU\SOFTWARE\Chiaki\Chiaki. Both halves are "Chiaki" because main.cpp sets
    /// setOrganizationName("Chiaki") and setApplicationName("Chiaki"), and QSettings joins them.
    /// </summary>
    public const string DefaultKeyPath = @"SOFTWARE\Chiaki\Chiaki";

    /// <summary>
    /// The libplacebo render parameters, which Settings keeps in an application of their own
    /// rather than under a group. Not read yet - it is the 67 keys still open on this line - but
    /// named here so the next reader does not go looking for them under the profile store.
    /// </summary>
    public const string PlaceboKeyPath = @"SOFTWARE\Chiaki\pl_render_params";

    /// <summary>The store holding current_profile and the profile list. Never profile-scoped.</summary>
    public string DefaultPath { get; }

    /// <summary>
    /// The active profile, or "" when the user has never set one. Read once at construction,
    /// because every other path on this object is derived from it.
    /// </summary>
    public string CurrentProfile { get; }

    /// <summary>
    /// The store this object actually reads: the profile's when there is one, the default
    /// store's when there is not.
    /// </summary>
    public string KeyPath { get; }

    public QSettingsStore(string? defaultKeyPath = null)
    {
        DefaultPath = defaultKeyPath ?? DefaultKeyPath;
        CurrentProfile = ReadCurrentProfile(DefaultPath);
        KeyPath = ProfileKeyPath(DefaultPath, CurrentProfile);
    }

    /// <summary>
    /// Where a profile's store sits, given the default one. Qt builds the application name as
    /// `%1-%2` of the application name and the profile, and QSettings turns that into the last
    /// path component - so "work" beside SOFTWARE\Chiaki\Chiaki is SOFTWARE\Chiaki\Chiaki-work.
    ///
    /// Pure, and separated from the registry for that reason: it is the half that can be wrong
    /// on a machine that has never run the Qt client, and the half a selftest can assert.
    ///
    /// A profile whose name contains a separator would land somewhere else entirely, and that is
    /// reproduced rather than defended against: QSettings does the same thing with the same name,
    /// so the store this points at is the store Qt wrote.
    /// </summary>
    public static string ProfileKeyPath(string defaultKeyPath, string? profile)
        => string.IsNullOrEmpty(profile) ? defaultKeyPath : $"{defaultKeyPath}-{profile}";

    private static string ReadCurrentProfile(string defaultKeyPath)
    {
        using RegistryKey? settings = Registry.CurrentUser.OpenSubKey($@"{defaultKeyPath}\settings");
        return QSettingsValue.AsString(settings?.GetValue("current_profile")) ?? string.Empty;
    }

    /// <summary>
    /// Whether there is a store to read - the profile's, when one is active.
    ///
    /// Scoped rather than asking whether a Qt client ever ran, because the caller's next move is
    /// to read consoles out of it, and a default store that exists says nothing about whether the
    /// profile store beside it does.
    /// </summary>
    public bool Exists()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key is not null;
    }

    /// <summary>
    /// Every profile the user has made, in the order Qt stored them. Empty for a user who never
    /// made one, which is not the same as a user whose current profile is "".
    ///
    /// The key inside each array entry is "settings/profile_name", and QSettings turns the slash
    /// into a subkey - so the name is at profiles\&lt;i&gt;\settings\profile_name and not one level up.
    /// </summary>
    public IReadOnlyList<string> Profiles()
    {
        var names = new List<string>();
        using RegistryKey? array = Registry.CurrentUser.OpenSubKey($@"{DefaultPath}\profiles");
        if (array is null)
            return names;

        int size = QSettingsValue.AsInt(array.GetValue("size")) ?? 0;
        for (int i = 1; i <= size; i++)
        {
            using RegistryKey? entry = array.OpenSubKey($@"{i}\settings");
            string? name = QSettingsValue.AsString(entry?.GetValue("profile_name"));
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }
        return names;
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
        using RegistryKey? array = Registry.CurrentUser.OpenSubKey($@"{KeyPath}\registered_hosts");
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
    /// PP81: the three arrays that sit beside registered_hosts, and what each costs if it is
    /// dropped.
    ///
    /// A MANUAL host is a console the user added by address because discovery never found it -
    /// which is exactly the console they cannot re-add by discovery. A HIDDEN host is one they
    /// told the client to stop showing, and losing that list makes the console list grow back. A
    /// controller MAPPING is an SDL mapping string per vid:pid, and a user who remapped a
    /// third-party pad has to do it again with a pad the client currently mishandles.
    ///
    /// None of them is the reinstall PP2 was about. Together they are the difference between a
    /// port that opens on the user's setup and one that opens on a default.
    /// </summary>
    public IReadOnlyList<HiddenHost> HiddenHosts()
    {
        var hosts = new List<HiddenHost>();
        foreach (RegistryKey entry in ArrayEntries("hidden_hosts"))
        {
            using (entry)
            {
                byte[]? mac = QSettingsValue.AsByteArray(entry.GetValue("server_mac"));
                // The MAC is the key this list is indexed by in the Qt client, so an entry
                // without one cannot hide anything and is not one.
                if (mac is null)
                    continue;

                hosts.Add(new HiddenHost(
                    QSettingsValue.AsString(entry.GetValue("server_nickname")), mac));
            }
        }
        return hosts;
    }

    /// <summary>
    /// Manual hosts, keyed by an id the Qt client also uses to pick the next one.
    ///
    /// An id below zero is skipped rather than kept: LoadFromSettings defaults it to -1, so a
    /// missing id is an entry that was never written properly, and the Qt client drops it too.
    /// </summary>
    public IReadOnlyList<ManualHost> ManualHosts()
    {
        var hosts = new List<ManualHost>();
        foreach (RegistryKey entry in ArrayEntries("manual_hosts"))
        {
            using (entry)
            {
                int id = QSettingsValue.AsInt(entry.GetValue("id")) ?? -1;
                if (id < 0)
                    continue;

                hosts.Add(new ManualHost(
                    id,
                    QSettingsValue.AsString(entry.GetValue("host")),
                    QSettingsValue.AsBool(entry.GetValue("registered")) ?? false,
                    QSettingsValue.AsByteArray(entry.GetValue("registered_mac"))));
            }
        }
        return hosts;
    }

    /// <summary>
    /// Controller mappings, keyed by vid:pid.
    ///
    /// The key is read as `vidpid` AND as `guid`, because a store written before the Qt client's
    /// own migration spells it the second way - and that migration only runs when the Qt client
    /// starts. A port that read one spelling would silently lose every mapping on a store the
    /// user has not opened the other client with since.
    /// </summary>
    public IReadOnlyList<ControllerMapping> ControllerMappings()
    {
        var mappings = new List<ControllerMapping>();
        foreach (RegistryKey entry in ArrayEntries("controller_mappings"))
        {
            using (entry)
            {
                string? key = QSettingsValue.AsString(entry.GetValue("vidpid"))
                    ?? QSettingsValue.AsString(entry.GetValue("guid"));
                string? mapping = QSettingsValue.AsString(entry.GetValue("mapping"));
                if (key is null || mapping is null)
                    continue;

                mappings.Add(new ControllerMapping(key, mapping));
            }
        }
        return mappings;
    }

    /// <summary>
    /// The entries of a QSettings array, in the order it stored them.
    ///
    /// The size is trusted only as far as a subkey actually exists: a store interrupted mid-write
    /// has a size that runs past its entries, and a user with three consoles should see three
    /// rather than an exception. Callers dispose each key.
    /// </summary>
    private IEnumerable<RegistryKey> ArrayEntries(string name)
    {
        using RegistryKey? array = Registry.CurrentUser.OpenSubKey($@"{KeyPath}\{name}");
        if (array is null)
            yield break;

        int size = QSettingsValue.AsInt(array.GetValue("size")) ?? 0;
        for (int i = 1; i <= size; i++)
        {
            RegistryKey? entry = array.OpenSubKey(i.ToString());
            if (entry is not null)
                yield return entry;
        }
    }

    /// <summary>
    /// The PSN account id and refresh token, or nulls where the user has not linked an account.
    ///
    /// These are the credential half of the store and the reason this class never writes: a
    /// token copied into a second place is a token that has to be revoked in two.
    ///
    /// Read from the profile store, because Settings::GetPsnAccountId reads the profile-scoped
    /// QSettings - two profiles can be linked to two different accounts, and that is the point
    /// of having them.
    /// </summary>
    public (string? AccountId, string? RefreshToken) PsnAccount()
    {
        using RegistryKey? settings = Registry.CurrentUser.OpenSubKey($@"{KeyPath}\settings");
        if (settings is null)
            return (null, null);
        return (
            QSettingsValue.AsString(settings.GetValue("psn_account_id")),
            QSettingsValue.AsString(settings.GetValue("psn_refresh_token")));
    }
}
