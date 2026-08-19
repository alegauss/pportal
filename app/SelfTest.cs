using System.Buffers.Binary;
using System.Text;
using Google.Protobuf;
using Microsoft.Win32;
using ChiakiNg.Settings;
using ChiakiNg.Native;
using ChiakiNg.Session;
using ChiakiNg.Protocol;

namespace ChiakiNg;

/// <summary>
/// A preference store held in a dictionary, so PP5's translation can be exercised on the branches
/// a user actually takes without writing to HKCU.
///
/// Every key it does not hold falls through to the declared default, which is what makes it a
/// stand-in for the real store rather than a second table: the defaults still come from
/// Preferences, so a row that is wrong there is still wrong here.
/// </summary>
internal sealed class FakePreferences : IPreferences
{
    private readonly Dictionary<string, object> values = new(StringComparer.Ordinal);

    public FakePreferences Set(string key, object value)
    {
        values[key] = value;
        return this;
    }

    private static T Default<T>(string key, T fallback)
        => Preferences.Find(key)?.Default is T d ? d : fallback;

    public string? GetString(string key)
        => values.TryGetValue(key, out object? v) ? (string?)v : Default<string?>(key, null);

    public bool GetBool(string key)
        => values.TryGetValue(key, out object? v) ? (bool)v : Default(key, false);

    public int GetInt(string key)
        => values.TryGetValue(key, out object? v) ? (int)v : Default(key, 0);

    public uint GetUInt(string key)
        => values.TryGetValue(key, out object? v) ? (uint)v : Default(key, 0u);

    public double GetDouble(string key)
        => values.TryGetValue(key, out object? v) ? (double)v : Default(key, 0.0);

    public QRectValue? GetRect(string key)
        => values.TryGetValue(key, out object? v) ? (QRectValue?)v : null;

    public byte[]? GetBytes(string key)
        => values.TryGetValue(key, out object? v) ? (byte[]?)v : null;
}

/// <summary>
/// PP2's assertion, and the shape claude-tray already uses: a selftest inside the executable,
/// run with `ChiakiNg.exe --selftest`, printing one line per case and exiting non-zero on the
/// first failure.
///
/// Why not a test project. There is not one yet, and choosing the framework and the solution
/// layout is PP24's decision with PP35 and PP36 behind it. A flag needs no NuGet, no network and
/// no second csproj, and it moves into whatever those lines settle on without anything here
/// having pre-empted them.
///
/// What is asserted is the decoding, not the registry. Every byte sequence below was read off a
/// real store on a machine that has run the Qt client - `rp_key`, `server_mac` and
/// `rp_regist_key` from HKCU\SOFTWARE\Chiaki\Chiaki\registered_hosts\1 - so these cases hold
/// against what Qt actually wrote rather than against what its source appears to say. That also
/// makes them run anywhere: a machine with no store still checks the part that can be wrong.
/// </summary>
public static class SelfTest
{
    public static int Run()
    {
        int failed = 0;
        int ran = 0;

        void Check(string name, bool ok, string detail = "")
        {
            ran++;
            if (ok)
            {
                Console.WriteLine($"  ok    {name}");
            }
            else
            {
                failed++;
                Console.WriteLine($"  FAIL  {name}{(detail.Length > 0 ? "  " + detail : "")}");
            }
        }

        static string Hex(byte[]? b) => b is null ? "<null>" : Convert.ToHexString(b).ToLowerInvariant();

        // PP22, and it runs first because everything below it can be skipped without failing.
        //
        // Fifteen blocks in this suite read a C or C++ source and compare the port against it, and
        // every one of them prints "no <file> here" and moves on when it cannot find the file -
        // correct in an installed copy, where there is no checkout to read. What that also means
        // is that losing the ability to FIND the checkout costs fifteen blocks in silence.
        //
        // Which is what happened: a single-file publish leaves Assembly.Location empty, the walk
        // upward started nowhere, and the published host skipped every drift check and failed to
        // load the shim - while every build out of the tree stayed green. So the rule is now
        // stated rather than assumed: inside a checkout, nothing may skip.
        string[] driftSources =
        [
            SanitizerSource.RelativePath, SessionSource.RelativePath, CryptoVectors.RelativePath,
            FecVectors.RelativePath, LibSource.RelativePath, LibSource.ShimRelativePath,
            ReorderQueueSource.RelativePath, BangReachability.TakionRelativePath,
            BangReachability.StreamConnectionRelativePath,
            @"gui\src\controllermanager.cpp", @"gui\include\psnaccountid.h",
            @"lib\src\remote\holepunch.c", @"test\bitstream.c", @"test\gkcrypt.c",
            @"test\regist.c", @"test\takion.c",
        ];

        bool inCheckout = SanitizerSource.LocateRelative("roadkeep.toml") is not null;
        string[] unfindable = inCheckout
            ? driftSources.Where(p => SanitizerSource.LocateRelative(p) is null).ToArray()
            : [];

        Console.WriteLine(inCheckout
            ? $"Sources - {driftSources.Length} drift checks, in a checkout"
            : "Sources - not a checkout, so the drift checks below will say so one by one");

        Check("every source a drift check reads is findable, or this is not a checkout",
            unfindable.Length == 0,
            unfindable.Length == 0 ? "" : string.Join(", ", unfindable));

        Console.WriteLine();
        Console.WriteLine("QSettingsValue - the encodings PP2 reads");

        // A plain REG_SZ is itself.
        Check("plain string passes through",
            QSettingsValue.AsString("PS5-385") == "PS5-385");

        // Rule 1: one byte per char, low byte only. This is the real rp_key, 16 bytes.
        var rpKey = new byte[] { 0x57, 0x49, 0xd7, 0x87, 0x8f, 0xce, 0xfd, 0x23,
                                 0x3f, 0x72, 0xfe, 0xf0, 0x7e, 0x30, 0xe7, 0x5a };
        string rpKeyText = "@ByteArray(" + Encoding.Latin1.GetString(rpKey) + ")";
        byte[]? decodedRpKey = QSettingsValue.AsByteArray(rpKeyText);
        Check("rp_key decodes to 16 bytes",
            decodedRpKey is not null && decodedRpKey.SequenceEqual(rpKey), Hex(decodedRpKey));

        // Rule 1 again, negatively: reading the payload as UTF-8 lengthens every byte above
        // 0x7f, which is most of a key. If this ever passes, the decoder switched encodings.
        Check("payload is Latin-1 and not UTF-8",
            Encoding.UTF8.GetBytes(Encoding.Latin1.GetString(rpKey)).Length != rpKey.Length);

        // Rule 2: the payload's own last byte is `)`. A parser that stops at the first one
        // returns five bytes and no error, which is a MAC address that matches no console.
        var mac = new byte[] { 0x90, 0x47, 0x48, 0x82, 0xfc, 0x29 };
        string macText = "@ByteArray(" + Encoding.Latin1.GetString(mac) + ")";
        byte[]? decodedMac = QSettingsValue.AsByteArray(macText);
        Check("server_mac ending in ')' keeps all 6 bytes",
            decodedMac is not null && decodedMac.SequenceEqual(mac), Hex(decodedMac));

        // Rule 3: REG_BINARY holding the UTF-16LE of the same text, because the payload has
        // NULs. These are the exact bytes the registry returned for rp_regist_key.
        var registBinary = new byte[]
        {
            0x40,0x00,0x42,0x00,0x79,0x00,0x74,0x00,0x65,0x00,0x41,0x00,0x72,0x00,0x72,0x00,
            0x61,0x00,0x79,0x00,0x28,0x00,0x33,0x00,0x65,0x00,0x39,0x00,0x31,0x00,0x31,0x00,
            0x30,0x00,0x37,0x00,0x63,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x00,0x00,0x00,0x00,0x00,0x00,0x29,0x00,
        };
        byte[]? decodedRegist = QSettingsValue.AsByteArray(registBinary);
        Check("rp_regist_key unwraps REG_BINARY to 16 bytes",
            decodedRegist is not null && decodedRegist.Length == 16, Hex(decodedRegist));
        Check("rp_regist_key keeps its embedded NULs",
            decodedRegist is not null
            && Encoding.Latin1.GetString(decodedRegist).StartsWith("3e91107c", StringComparison.Ordinal)
            && decodedRegist.Count(b => b == 0) == 8, Hex(decodedRegist));

        // A value that is not a byte array reads as absent rather than throwing, so a store
        // written by a version that spelled a field differently does not crash a first launch.
        Check("a non-@ByteArray value reads as absent",
            QSettingsValue.AsByteArray("3132333435") is null);
        Check("a missing value reads as absent",
            QSettingsValue.AsByteArray(null) is null && QSettingsValue.AsString(null) is null);

        // REG_DWORD and text both mean the same int.
        Check("target reads from REG_DWORD", QSettingsValue.AsInt(1000100) == 1000100);
        Check("size reads from text", QSettingsValue.AsInt("1") == 1);
        Check("a non-numeric value is not an int", QSettingsValue.AsInt("PS5") is null);

        Console.WriteLine();
        Console.WriteLine("QSettingsValue - the rest of the grammar, off a probe store Qt wrote");

        // Rule 4, and the only rule here a user meets rather than a hardware key: `@` escapes the
        // whole scheme, so a nickname of `@home` is stored `@@home`. A reader that skips this
        // hands that user a name they never typed, on every screen, with no error anywhere.
        Check("a string starting with '@' loses its escape",
            QSettingsValue.AsString("@@home") == "@home", QSettingsValue.AsString("@@home") ?? "<null>");
        Check("an ordinary string keeps its text",
            QSettingsValue.AsString("PS5-385") == "PS5-385");
        // â€¦and the escape is one level, not a strip-all: `@@home` typed by a user is `@@@home`.
        Check("the escape is one '@' and not all of them",
            QSettingsValue.AsString("@@@home") == "@@home", QSettingsValue.AsString("@@@home") ?? "<null>");

        // Order matters: the typed forms are read off the raw text, before the escape is undone.
        // Otherwise a string a user typed as `@ByteArray(x)` - stored `@@ByteArray(x)` - would
        // unescape into something that then decodes as bytes.
        Check("an escaped @ByteArray( is a string and not bytes",
            QSettingsValue.AsByteArray("@@ByteArray(x)") is null
            && QSettingsValue.AsString("@@ByteArray(x)") == "@ByteArray(x)");

        // Rule 5. Qt writes lower-case text and not a REG_DWORD; 1/0 is what an older or
        // hand-edited store can hold.
        Check("bool reads Qt's text form",
            QSettingsValue.AsBool("true") == true && QSettingsValue.AsBool("false") == false);
        Check("bool also reads 1 and 0",
            QSettingsValue.AsBool("1") == true && QSettingsValue.AsBool("0") == false
            && QSettingsValue.AsBool(1) == true && QSettingsValue.AsBool(0) == false);
        Check("a non-boolean value is not a bool", QSettingsValue.AsBool("auto") is null);

        // Rule 6. The C locale, and 1.0 comes back as the text "1" - a double that lost its
        // point is still a double, and a parse that rejects it reports the default instead.
        Check("double is parsed in the C locale",
            QSettingsValue.AsDouble("0.05") == 0.05);
        Check("a double that lost its point is still a double",
            QSettingsValue.AsDouble("1") == 1.0);
        // The negative half of rule 6: if this ever reads as 5, the parse picked up the
        // machine's locale and every threshold in the settings is off by a hundred.
        Check("a comma is not a decimal point",
            QSettingsValue.AsDouble("0,05") is null or (not 0.05 and not 5.0),
            QSettingsValue.AsDouble("0,05")?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>");

        // Rule 7. A REG_DWORD arrives signed, and Qt wrote a uint through the same 32 bits.
        Check("a uint above int.MaxValue survives the DWORD",
            QSettingsValue.AsUInt(unchecked((int)0x80000001u)) == 0x80000001u,
            QSettingsValue.AsUInt(unchecked((int)0x80000001u))?.ToString() ?? "<null>");
        Check("an ordinary uint is itself", QSettingsValue.AsUInt(127) == 127u);

        // Rule 8, with the exact string the probe store held for settings/geometry.
        QRectValue? rect = QSettingsValue.AsRect("@Rect(0 23 1920 1010)");
        Check("geometry decodes to four numbers",
            rect == new QRectValue(0, 23, 1920, 1010), rect?.ToString() ?? "<null>");
        Check("a rect missing an edge is not a rect",
            QSettingsValue.AsRect("@Rect(0 23 1920)") is null);
        Check("a non-rect is not a rect",
            QSettingsValue.AsRect("@Size(640 480)") is null && QSettingsValue.AsRect("PS5") is null);

        // Rule 9. A REG_MULTI_SZ is not a scalar, and saying so beats "System.String[]".
        Check("a multi-string is refused rather than rendered",
            QSettingsValue.AsRawText(new[] { "a", "b" }) is null);

        Console.WriteLine();
        Console.WriteLine("QSettingsStore - which of the three stores a value lives in");

        // A user with no profile reads the default store, which is what shipped and what these
        // keep true. The two below are the case that did not: Settings scopes its QSettings per
        // profile, so a user on "work" has their consoles under Chiaki-work, and a reader pinned
        // to Chiaki finds an empty array and reports it as no consoles registered at all.
        Check("no profile reads the default store",
            QSettingsStore.ProfileKeyPath(QSettingsStore.DefaultKeyPath, "")
                == @"SOFTWARE\Chiaki\Chiaki");
        Check("an absent profile reads the default store",
            QSettingsStore.ProfileKeyPath(QSettingsStore.DefaultKeyPath, null)
                == @"SOFTWARE\Chiaki\Chiaki");
        Check("a profile reads its own store",
            QSettingsStore.ProfileKeyPath(QSettingsStore.DefaultKeyPath, "work")
                == @"SOFTWARE\Chiaki\Chiaki-work",
            QSettingsStore.ProfileKeyPath(QSettingsStore.DefaultKeyPath, "work"));

        // The suffix joins the application half of the name, so a profile store is a SIBLING of
        // the default one and not a child. Asserted apart from the equality above because both
        // mistakes produce a path that looks entirely plausible in a debugger.
        Check("a profile store is a sibling and not a child",
            !QSettingsStore.ProfileKeyPath(QSettingsStore.DefaultKeyPath, "work")
                .StartsWith(QSettingsStore.DefaultKeyPath + @"\", StringComparison.Ordinal));

        // The colour pipeline is a third store, not a group inside either of the other two.
        Check("placebo is a store of its own",
            QSettingsStore.PlaceboKeyPath == @"SOFTWARE\Chiaki\pl_render_params"
            && QSettingsStore.PlaceboKeyPath != QSettingsStore.DefaultKeyPath);

        Console.WriteLine();
        Console.WriteLine("Preferences - the transcription, and what it must not lose");

        // The counts. A row that goes missing in a merge takes a default with it, and the
        // preference it named then reads as zero on every store where the user never set it -
        // no error, no screen that shows it, and nothing else in this tree that would notice.
        Check("every declared key is unique",
            Preferences.All.Count == 148, Preferences.All.Count.ToString());
        Check("the three scopes are all populated",
            Preferences.All.Values.Count(p => p.Scope == QSettingsScope.Default) == 2
            && Preferences.All.Values.Count(p => p.Scope == QSettingsScope.Profile) == 81
            && Preferences.All.Values.Count(p => p.Scope == QSettingsScope.Placebo) == 65);

        // A default has to be the type its kind says, or the cast in the reader throws on the
        // one machine that has never set that key - which is every fresh install.
        PreferenceKey[] mistyped = Preferences.All.Values.Where(p => p.Default is not null && p.Kind switch
        {
            QSettingsKind.Bool => p.Default is not bool,
            QSettingsKind.Int => p.Default is not int,
            QSettingsKind.UInt => p.Default is not uint,
            QSettingsKind.Double => p.Default is not double,
            QSettingsKind.String => p.Default is not string,
            _ => false,
        }).ToArray();
        Check("every default matches the kind it is declared with",
            mistyped.Length == 0, string.Join(", ", mistyped.Select(p => p.Key)));

        // The scope of these two is the whole of PP79 restated as data: current_profile is what
        // decides which store the other 146 come out of, so it cannot itself live in one.
        Check("current_profile is not profile-scoped",
            Preferences.Find("settings/current_profile")?.Scope == QSettingsScope.Default);
        Check("the placebo keys are in the placebo store",
            Preferences.All.Values.Where(p => p.Key.StartsWith("placebo_settings/", StringComparison.Ordinal))
                .All(p => p.Scope == QSettingsScope.Placebo));

        // Spot checks against Settings, one per kind, chosen where a wrong default is visible:
        // a decoder that is not "auto" pins a machine to one path, and a 60 that became 30 is a
        // stream at half rate that looks like a network problem.
        Check("hw_decoder defaults to auto",
            Preferences.Find("settings/hw_decoder") is { Kind: QSettingsKind.String, Default: "auto" });
        Check("fps defaults to 60",
            Preferences.Find("settings/fps_local_ps5") is { Kind: QSettingsKind.Int, Default: 60 });
        Check("the packet-loss ceiling carries the fallback chain's 0.05",
            Preferences.Find("settings/packet_loss_max") is { Kind: QSettingsKind.Double, Default: 0.05 });
        Check("geometry is a rect with no default",
            Preferences.Find("settings/geometry") is { Kind: QSettingsKind.Rect, Default: null });
        Check("auto_connect_mac is bytes",
            Preferences.Find("settings/auto_connect_mac") is { Kind: QSettingsKind.ByteArray });

        var prefs = new QSettingsPreferences(new QSettingsStore(@"SOFTWARE\ClaudeAbsent\ClaudeAbsent"));

        // A store that is not there is every fresh install, and it must read as Qt's defaults
        // rather than as zeroes. Pointed at a key path nothing has ever written, on purpose.
        Check("an absent store reads the declared defaults",
            prefs.GetString("settings/hw_decoder") == "auto"
            && prefs.GetInt("settings/fps_local_ps5") == 60
            && prefs.GetBool("settings/keyboard_enabled")
            && prefs.GetUInt("settings/custom_resolution_width") == 1920u
            && prefs.GetDouble("settings/zoom_factor") == -1.0
            && prefs.GetRect("settings/geometry") is null);

        // An undeclared key is a typo or an untranscribed preference. Both are bugs here, and a
        // default would hide the second one for as long as the port lasts.
        bool threwOnUnknown = false;
        try { prefs.GetString("settings/not_a_real_key"); }
        catch (KeyNotFoundException) { threwOnUnknown = true; }
        Check("an undeclared key throws rather than defaulting", threwOnUnknown);

        // Qt writes a bool as the text "true", so reading one as an int gives 0 for both of its
        // values. Refused at the declaration rather than discovered in a screen.
        bool threwOnKind = false;
        try { prefs.GetInt("settings/keyboard_enabled"); }
        catch (InvalidOperationException) { threwOnKind = true; }
        Check("a read at the wrong width is refused", threwOnKind);

        Console.WriteLine();
        Console.WriteLine("QSettingsStore - the three arrays beside registered_hosts");

        // An absent store is every fresh install, and each of these must read as nothing rather
        // than throw - a user with no manual hosts is the common case, not an error.
        var noStore = new QSettingsStore(@"SOFTWARE\ClaudeAbsent\ClaudeAbsent");
        Check("an absent store has no hidden, manual or mapped anything",
            noStore.HiddenHosts().Count == 0 && noStore.ManualHosts().Count == 0
            && noStore.ControllerMappings().Count == 0);

        // A store written here, in QSettings' own array shape, and removed again. Written rather
        // than mocked because the shape IS the thing under test: a `size` value beside subkeys
        // numbered from one, which is not a layout any interface could stand in for.
        const string testRoot = @"SOFTWARE\ClaudeSelfTest\PP81";
        try
        {
            using (RegistryKey root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(testRoot))
            {
                static string Bytes(params byte[] b) => "@ByteArray(" + Encoding.Latin1.GetString(b) + ")";

                using (RegistryKey hidden = root.CreateSubKey("hidden_hosts"))
                {
                    hidden.SetValue("size", 2, RegistryValueKind.DWord);
                    using RegistryKey one = hidden.CreateSubKey("1");
                    one.SetValue("server_nickname", "Bedroom PS5");
                    one.SetValue("server_mac", Bytes(0x90, 0x47, 0x48, 0x82, 0xfc, 0x29));
                    // The second entry has no MAC, which is what an interrupted write leaves.
                    using RegistryKey two = hidden.CreateSubKey("2");
                    two.SetValue("server_nickname", "half written");
                }

                using (RegistryKey manual = root.CreateSubKey("manual_hosts"))
                {
                    manual.SetValue("size", 2, RegistryValueKind.DWord);
                    using RegistryKey one = manual.CreateSubKey("1");
                    one.SetValue("id", 7, RegistryValueKind.DWord);
                    one.SetValue("host", "192.168.1.50");
                    one.SetValue("registered", "true");
                    one.SetValue("registered_mac", Bytes(0x01, 0x02, 0x03, 0x04, 0x05, 0x06));
                    // No id at all, which LoadFromSettings defaults to -1 and drops.
                    using RegistryKey two = manual.CreateSubKey("2");
                    two.SetValue("host", "10.0.0.9");
                }

                using (RegistryKey maps = root.CreateSubKey("controller_mappings"))
                {
                    maps.SetValue("size", 2, RegistryValueKind.DWord);
                    using RegistryKey one = maps.CreateSubKey("1");
                    one.SetValue("vidpid", "0x054c:0x0ce6");
                    one.SetValue("mapping", "030000004c050000e60c000000000000,DualSense,a:b1,");
                    // The OLD spelling, which a store written before the Qt client's migration
                    // still has - and that migration only runs when the Qt client starts.
                    using RegistryKey two = maps.CreateSubKey("2");
                    two.SetValue("guid", "0x0079:0x0011");
                    two.SetValue("mapping", "79000000000000001100000000000000,Generic,a:b2,");
                }
            }

            var store81 = new QSettingsStore(testRoot);

            IReadOnlyList<HiddenHost> hiddenHosts = store81.HiddenHosts();
            Check("a hidden host is read, and one without a MAC is not",
                hiddenHosts.Count == 1 && hiddenHosts[0].ServerNickname == "Bedroom PS5"
                && hiddenHosts[0].MacText == "90:47:48:82:fc:29",
                $"{hiddenHosts.Count}: {(hiddenHosts.Count > 0 ? hiddenHosts[0].MacText : "")}");

            IReadOnlyList<ManualHost> manualHosts = store81.ManualHosts();
            Check("a manual host keeps its id, address and registration",
                manualHosts.Count == 1
                && manualHosts[0] is { Id: 7, Host: "192.168.1.50", Registered: true }
                && manualHosts[0].RegisteredMac is { Length: 6 },
                $"{manualHosts.Count}: {(manualHosts.Count > 0 ? manualHosts[0].ToString() : "")}");

            // The one that would go silently: both spellings of the key, because a store the user
            // has not opened the Qt client with since the migration still says `guid`.
            IReadOnlyList<ControllerMapping> maps81 = store81.ControllerMappings();
            Check("both spellings of a mapping key are read",
                maps81.Count == 2
                && maps81[0].VidPid == "0x054c:0x0ce6"
                && maps81[1].VidPid == "0x0079:0x0011",
                string.Join(", ", maps81.Select(m => m.VidPid)));
            Check("and each carries its SDL mapping string",
                maps81.All(m => m.Mapping.Contains(",a:b", StringComparison.Ordinal)));
        }
        finally
        {
            // The store this port reads is never written; this key is the test's own and goes
            // with it.
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\ClaudeSelfTest", throwOnMissingSubKey: false);
        }

        Console.WriteLine();
        Console.WriteLine("QtPaths - where the Qt client already put the file");

        // Trap 1, and the reason this file exists. Qt's AppDataLocation is Roaming and its
        // ConfigLocation is Local; .NET spells them ApplicationData and LocalApplicationData,
        // which read as near-synonyms. A host that uses one for both writes the session logs
        // where the other build never looks, and nothing reports it.
        Check("app data is roaming and config is local",
            QtPaths.AppDataLocation != QtPaths.ConfigLocation,
            QtPaths.AppDataLocation + " vs " + QtPaths.ConfigLocation);
        Check("app data is under Roaming",
            QtPaths.AppDataLocation.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                StringComparison.OrdinalIgnoreCase));
        Check("config is under Local, and is the local data location",
            QtPaths.ConfigLocation == QtPaths.AppLocalDataLocation
            && QtPaths.ConfigLocation.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                StringComparison.OrdinalIgnoreCase));

        // QStandardPaths puts the organisation and the application in as two segments. Both are
        // "Chiaki" here, so a composer that dropped one would still produce a plausible path -
        // asserted against a root that makes the doubling visible.
        Check("a location is root, organisation, application",
            QtPaths.Compose(@"X:\root", "Org", "App") == @"X:\root\Org\App",
            QtPaths.Compose(@"X:\root", "Org", "App"));
        Check("the two Chiaki segments are both there",
            QtPaths.AppDataLocation.EndsWith(Path.Combine("Chiaki", "Chiaki"), StringComparison.Ordinal),
            QtPaths.AppDataLocation);

        // The three files this port has to find where the Qt build left them.
        Check("the session logs are in log/ under app data",
            QtPaths.LogDirectory == Path.Combine(QtPaths.AppDataLocation, "log"));
        Check("the shader cache is beside them",
            QtPaths.ShaderCacheFile == Path.Combine(QtPaths.AppDataLocation, "pl_shader.cache"));
        // The baseline ledger goes IN the log directory, not beside it. It is the file the two
        // builds are compared with, so a host that appended to a second one would make the
        // comparison meaningless rather than broken.
        Check("the baseline ledger is inside the log directory",
            QtPaths.SessionBaselineFile == Path.Combine(QtPaths.LogDirectory, "chiaki_baseline.jsonl"));
        // Three "Chiaki" in a row is what qmlmainwindow.cpp actually produces: ConfigLocation
        // already ends in two and the literal adds a third. Reproduced, not tidied - tidying it
        // is a relocation, and this line is explicitly not one.
        Check("the placebo conf keeps its third Chiaki",
            QtPaths.PlaceboConfigFile == Path.Combine(QtPaths.ConfigLocation, "Chiaki", "pl_render_params.conf")
            && QtPaths.PlaceboConfigFile.EndsWith(
                Path.Combine("Chiaki", "Chiaki", "Chiaki", "pl_render_params.conf"), StringComparison.Ordinal),
            QtPaths.PlaceboConfigFile);

        // Trap 2: no SpecialFolder exists for Downloads, so this is a known-folder id or it is a
        // guess. What can be checked anywhere is that the call works and that the id is the
        // right one; what cannot is the difference that motivates it, because on a machine where
        // the user never moved Downloads the shell's answer and the guess are the same string.
        // So this fails on a broken P/Invoke or a mistyped FOLDERID, and is silent on the case
        // it exists for - which is worth saying out loud rather than dressing up.
        Check("downloads resolves to a real directory",
            Path.IsPathRooted(QtPaths.DownloadsDirectory) && Directory.Exists(QtPaths.DownloadsDirectory),
            QtPaths.DownloadsDirectory);
        Check("downloads is not the desktop",
            !QtPaths.DownloadsDirectory.Equals(QtPaths.DesktopDirectory, StringComparison.OrdinalIgnoreCase));

        Console.WriteLine();
        Console.WriteLine("ChiakiNative - the seam, called for real");

        // The ABI first, because it is what makes every assertion under it mean anything. A DLL
        // from an older build exports every name this assembly imports and answers all of them.
        bool loaded = false;
        try
        {
            ChiakiNative.CheckAbi();
            loaded = true;
        }
        catch (Exception e) when (e is DllNotFoundException or InvalidOperationException)
        {
            Console.WriteLine($"  FAIL  the shim loads and matches its ABI  {e.Message}");
            failed++;
            ran++;
        }

        if (loaded)
        {
            Check("the shim loads and matches its ABI",
                ChiakiNative.AbiVersion() == ChiakiNative.ExpectedAbi, ChiakiNative.LoadedFrom ?? "");

            // A static string crossing into managed memory, and the smallest real property of
            // the seam. CHIAKI_ERR_SUCCESS is 0 and libchiaki spells it "Success".
            Check("a native string comes back readable",
                ChiakiNative.ErrorString(0) == "Success", ChiakiNative.ErrorString(0) ?? "<null>");
            // An unknown code still answers rather than returning null, which is libchiaki's
            // behaviour and not something this seam decided.
            Check("an unknown error code still answers",
                !string.IsNullOrEmpty(ChiakiNative.ErrorString(9999)));

            // And the reason this function was the first one across: PP51's floor, asserted once
            // in C and now reached from managed code through the same implementation rather than
            // re-derived beside it. If these two ever disagree, the port grew a second answer.
            Check("the non-NVIDIA OpenGL floor holds across the seam",
                ChiakiNative.DecoderChoice(false, false, true, false, ChiakiRenderer.OpenGL, "auto") == "d3d11va",
                ChiakiNative.DecoderChoice(false, false, true, false, ChiakiRenderer.OpenGL, "auto") ?? "<null>");
            Check("vulkan is taken off OpenGL and not on it",
                ChiakiNative.DecoderChoice(true, false, true, false, ChiakiRenderer.Vulkan, "auto") == "vulkan"
                && ChiakiNative.DecoderChoice(true, false, true, false, ChiakiRenderer.OpenGL, "vulkan") == "d3d11va");
            Check("cuda needs the card as well as the decoder",
                ChiakiNative.DecoderChoice(false, true, true, true, ChiakiRenderer.OpenGL, "auto") == "cuda"
                && ChiakiNative.DecoderChoice(false, true, true, false, ChiakiRenderer.OpenGL, "auto") == "d3d11va");
            // A null string across the boundary is a real case - it is what an unset preference
            // hands over - and it must not become "auto" by accident.
            Check("a null request crosses as an absence",
                ChiakiNative.DecoderChoice(false, false, true, false, ChiakiRenderer.OpenGL, null) == "software");
            Check("needs_vulkan_context agrees with the choice",
                ChiakiNative.DecoderChoiceNeedsVulkanContext("vulkan")
                && !ChiakiNative.DecoderChoiceNeedsVulkanContext("d3d11va"));

            // PP78. The port's settings surface offers "none" the way the Qt combo does, and the
            // whole reason it can hand it straight over is that the choice no longer answers with
            // it. Asserted here and not only in C because this is the side that will one day map
            // the answer to an ffmpeg device type, and that mapping must not need a special case.
            Check("none is answered as software, on a machine that could do better",
                ChiakiNative.DecoderChoice(true, true, true, true, ChiakiRenderer.Vulkan, "none") == "software",
                ChiakiNative.DecoderChoice(true, true, true, true, ChiakiRenderer.Vulkan, "none") ?? "<null>");
            Check("the literal none is never an answer",
                ChiakiNative.DecoderChoice(true, true, true, true, ChiakiRenderer.Vulkan, "auto") != "none"
                && ChiakiNative.DecoderChoice(false, false, false, false, ChiakiRenderer.OpenGL, "none") != "none"
                && ChiakiNative.DecoderChoice(false, false, false, false, ChiakiRenderer.OpenGL, "quicksync") != "none");

            Console.WriteLine();
            Console.WriteLine("ChiakiLog - the seam in the other direction");

            var lines = new List<(ChiakiLogLevel Level, string Text)>();
            using (var log = new ChiakiLog(
                ChiakiLogLevel.Info | ChiakiLogLevel.Warning | ChiakiLogLevel.Error,
                (level, text) => lines.Add((level, text))))
            {
                // The mask is read back out of C rather than trusted from the constructor: the
                // filter is what decides whether a callback happens at all, so every assertion
                // below is an assertion about it as much as about the crossing.
                Check("the log reports the mask it was created with",
                    log.LevelMask == (ChiakiLogLevel.Info | ChiakiLogLevel.Warning | ChiakiLogLevel.Error),
                    log.LevelMask.ToString());

                // The point of the whole file. A collection here moves managed objects, and if
                // the `user` pointer had been the instance's address rather than a GCHandle's,
                // this is the line after which the callback lands in somebody else's memory -
                // silently, because the bytes there are still readable.
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                log.Write(ChiakiLogLevel.Info, "the seam holds");
                Check("a native callback reaches a managed handler after a collection",
                    lines.Count == 1 && lines[0] == (ChiakiLogLevel.Info, "the seam holds"),
                    lines.Count == 0 ? "<nothing arrived>" : $"{lines[0].Level} {lines[0].Text}");

                // Filtered in C, before the callback. A level outside the mask that still arrived
                // would mean the port pays for every debug line it has switched off.
                log.Write(ChiakiLogLevel.Debug, "not in the mask");
                Check("a level outside the mask never crosses", lines.Count == 1,
                    lines.Count > 1 ? lines[^1].Text : "");

                // â€¦and the mask is live, which is what a verbosity setting changed mid-session is.
                log.LevelMask = ChiakiLogLevel.All;
                log.Write(ChiakiLogLevel.Debug, "now in the mask");
                Check("re-masking lets the same level through",
                    lines.Count == 2 && lines[^1] == (ChiakiLogLevel.Debug, "now in the mask"),
                    lines.Count < 2 ? "<nothing arrived>" : $"{lines[^1].Level} {lines[^1].Text}");

                // chiaki_log formats into a 0x100 stack buffer and reaches for the heap above it.
                // A message that crosses that line proves what arrives is the pointer the library
                // ended up with rather than the buffer it started in.
                string longLine = new('x', 700);
                log.Write(ChiakiLogLevel.Warning, longLine);
                Check("a message past the library's stack buffer arrives whole",
                    lines[^1] == (ChiakiLogLevel.Warning, longLine), lines[^1].Text.Length.ToString());

                // The message is an argument to "%s" and never the format itself. If this ever
                // comes back as anything else, the shim is reading the stack on every log line a
                // console nickname happens to appear in.
                log.Write(ChiakiLogLevel.Error, "100% of %s and %d");
                Check("a percent sign is text and not a format",
                    lines[^1] == (ChiakiLogLevel.Error, "100% of %s and %d"), lines[^1].Text);

                // Two logs at once, each with its own sink: this is the `void *user` round trip,
                // and it is the property that lets one process hold a log per session.
                var other = new List<string>();
                using (var second = new ChiakiLog(ChiakiLogLevel.All, (_, text) => other.Add(text)))
                {
                    second.Write(ChiakiLogLevel.Info, "second");
                    log.Write(ChiakiLogLevel.Info, "first");
                }
                Check("each log's user pointer reaches its own handler",
                    other.Count == 1 && other[0] == "second" && lines[^1].Text == "first",
                    $"other=[{string.Join(", ", other)}] last={lines[^1].Text}");

                Check("the level char is the one the log file is written with",
                    ChiakiLog.LevelChar(ChiakiLogLevel.Info) == 'I'
                    && ChiakiLog.LevelChar(ChiakiLogLevel.Error) == 'E'
                    && ChiakiLog.LevelChar(ChiakiLogLevel.All) == '?');
            }

            // Disposed twice on purpose: the second free would be a double free in C, and the
            // using block above already did the first one.
            var disposedTwice = new ChiakiLog(ChiakiLogLevel.All, (_, _) => { });
            disposedTwice.Dispose();
            disposedTwice.Dispose();
            Check("disposing twice frees once", disposedTwice.Handle == IntPtr.Zero);

            bool threwOnDisposed = false;
            try { disposedTwice.Write(ChiakiLogLevel.Info, "after the free"); }
            catch (ObjectDisposedException) { threwOnDisposed = true; }
            Check("a write after the free is refused rather than passed a dangling handle",
                threwOnDisposed);

            Console.WriteLine();
            Console.WriteLine("ChiakiSession - the lifecycle's first end, with no console needed");

            // Nothing managed had ever called this, and it is where WSAStartup lives. Twice on
            // purpose: WSAStartup is reference counted and the rest are writes, so a host that
            // calls it from two places must not be the thing that breaks.
            Check("chiaki_lib_init succeeds", ChiakiSession.LibInit() == ChiakiError.Success,
                ChiakiSession.LibInit().ToString());
            Check("chiaki_lib_init is idempotent", ChiakiSession.LibInit() == ChiakiError.Success);

            using (var info = new ChiakiConnectInfo())
            {
                // The default is not zeroes: a 0x0 profile is accepted by chiaki_session_init and
                // then negotiated, so an unset one would be a black stream rather than an error.
                Check("a fresh connect info is 1080p60",
                    info.VideoProfile is { Width: 1920, Height: 1080, MaxFps: 60 },
                    info.VideoProfile.ToString());

                // The bitrate is the number worth not copying into C#: it lives in one switch in
                // session.c, and a second copy here is one nothing would ever compare.
                info.SetVideoPreset(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps30);
                Check("the 720p preset carries the library's own bitrate",
                    info.VideoProfile == new ChiakiVideoProfile(1280, 720, 30, 10000, 0),
                    info.VideoProfile.ToString());
                info.SetVideoPreset(ChiakiVideoResolution.P1080, ChiakiVideoFps.Fps60);

                // A key one byte over would write into the field that sits directly behind it,
                // and the session it built would fail at a handshake step naming neither.
                bool refusedLongKey = false;
                try { info.SetRegistKey(new byte[17]); }
                catch (ArgumentException) { refusedLongKey = true; }
                Check("a regist key that does not fit is refused at the seam", refusedLongKey);

                bool refusedShortMorning = false;
                try { info.SetMorning(new byte[15]); }
                catch (ArgumentException) { refusedShortMorning = true; }
                Check("morning is refused at any length but 16", refusedShortMorning);

                // The real shapes, out of what a registered console actually stores: an 8-byte
                // regist key zero-padded into 16, and a 16-byte morning.
                info.SetRegistKey("12345678"u8);
                info.SetMorning(new byte[16]);
                info.Ps5 = true;
                info.PacketLossMax = 0.05;
                info.SetFlags(autoDowngrade: true, keyboard: false, dualSense: true, idrOnFecFailure: false);

                // A numeric address, so this resolves without a packet leaving the machine - the
                // point being that construction is assertable on a build agent and only Start is
                // not. The log is the one from above, which is what a session is handed.
                info.Host = "127.0.0.1";
                var sessionLines = new List<string>();
                using var sessionLog = new ChiakiLog(ChiakiLogLevel.All, (_, t) => sessionLines.Add(t));

                using (ChiakiSession? session = ChiakiSession.TryCreate(info, sessionLog, out ChiakiError err))
                {
                    Check("a session over a numeric host builds",
                        session is not null && err == ChiakiError.Success, err.ToString());
                    Check("the session handle is not null",
                        session is not null && session.Handle != IntPtr.Zero);
                }

                // A host that cannot resolve is the ordinary failure - a console switched off, an
                // address typed wrong - and it must arrive as a code rather than as a crash. The
                // name below is over the 255 characters DNS allows, so Winsock rejects it before
                // a query goes anywhere: this says the same thing on a machine with no network as
                // on one with, which a name like "nosuchhost.invalid" would not.
                info.Host = new string('a', 300);
                ChiakiSession? refused = ChiakiSession.TryCreate(info, sessionLog, out ChiakiError addrErr);
                Check("a host that does not resolve is CHIAKI_ERR_PARSE_ADDR",
                    refused is null && addrErr == ChiakiError.ParseAddr, addrErr.ToString());
                refused?.Dispose();

                // The trap on the other side of that, found by writing the line above: an EMPTY
                // host is not an error on Winsock - getaddrinfo answers it with the loopback
                // address - so a connect dialog that hands one over builds a perfectly valid
                // session pointed at the machine it is running on. Asserted rather than fixed,
                // because the fix belongs to whichever screen collects the address (PP14), and an
                // assertion is what stops it being discovered again there.
                info.Host = "";
                using (ChiakiSession? empty = ChiakiSession.TryCreate(info, sessionLog, out ChiakiError emptyErr))
                {
                    Check("an empty host is NOT refused by the library, so a screen must refuse it",
                        empty is not null && emptyErr == ChiakiError.Success, emptyErr.ToString());
                }

                // â€¦and the code is the one ErrorString already turns into a sentence, which is
                // what makes the enum above a spelling of libchiaki's numbers and not a parallel
                // set of them.
                Check("the error code names itself through the seam",
                    ChiakiNative.ErrorString((int)ChiakiError.ParseAddr) == "Failed to parse host address",
                    ChiakiNative.ErrorString((int)ChiakiError.ParseAddr) ?? "<null>");
            }

            // The sentence a disconnect screen shows. NONE and an out-of-range reason share
            // "Unknown" in session.c - reproduced rather than tidied, because a screen that says
            // something else than the Qt build's says it for a reason nobody wrote down.
            Check("a quit reason has the sentence the Qt build shows",
                ChiakiSession.QuitReasonString(1) == "Stopped"
                && ChiakiSession.QuitReasonString(0) == "Unknown",
                $"{ChiakiSession.QuitReasonString(1)} / {ChiakiSession.QuitReasonString(0)}");

            Console.WriteLine();
            Console.WriteLine("ChiakiSession - the thread, and the callback a UI is driven by");

            // The loopback, with nothing listening on a remote play port. The session therefore
            // fails on its own and reports it through exactly the path a real disconnect takes,
            // which makes the whole lifecycle - start, event, join - assertable on a build agent.
            using (var runInfo = new ChiakiConnectInfo())
            {
                runInfo.Host = "127.0.0.1";
                runInfo.Ps5 = true;
                runInfo.SetRegistKey("12345678"u8);
                runInfo.SetMorning(new byte[16]);
                runInfo.PacketLossMax = 0.05;

                var events = new List<ChiakiSessionEvent>();
                int callbackThread = 0;
                using var quitArrived = new ManualResetEventSlim(false);
                using var runLog = new ChiakiLog(ChiakiLogLevel.Error, (_, _) => { });
                using ChiakiSession? run = ChiakiSession.TryCreate(runInfo, runLog, out ChiakiError runErr);

                Check("a session for the run builds", run is not null, runErr.ToString());
                if (run is not null)
                {
                    run.SetEventHandler(e =>
                    {
                        lock (events)
                            events.Add(e);
                        if (e.Type != ChiakiEventType.Quit)
                            return;
                        callbackThread = Environment.CurrentManagedThreadId;
                        quitArrived.Set();
                    });

                    Check("the session thread starts", run.Start() == ChiakiError.Success);

                    // Bounded, because nothing in libchiaki promises this ends: a suite that
                    // waits forever on a network stack is a suite that reports nothing at all.
                    bool arrived = quitArrived.Wait(TimeSpan.FromSeconds(45));
                    Check("a quit event arrives from an address nothing answers", arrived);

                    ChiakiSessionEvent quit = default;
                    lock (events)
                    {
                        if (events.Count > 0)
                            quit = events[^1];
                    }

                    Check("the quit carries a reason",
                        arrived && quit.Type == ChiakiEventType.Quit
                        && quit.QuitReason != ChiakiQuitReason.None,
                        $"{quit.QuitReason}: {quit.QuitReasonString ?? "<null>"}");

                    // And the reason's own sentence is what a screen shows. The event's
                    // reason_str is NOT that sentence: session.c sets it only when the console
                    // sent a disconnect reason of its own, so it is null on every failure that
                    // never reached a console - which is this one. qmlbackend.cpp shows
                    // chiaki_quit_reason_string and appends reason_str only when it is there, and
                    // a port that read reason_str as the message would show an empty dialog for
                    // the commonest failure there is.
                    Check("the reason has a sentence even with no reason_str",
                        arrived && quit.QuitReasonString is null
                        && !string.IsNullOrEmpty(ChiakiSession.QuitReasonString((int)quit.QuitReason)),
                        ChiakiSession.QuitReasonString((int)quit.QuitReason) ?? "<null>");

                    // The property PP83's log could not show. That callback ran on the thread
                    // that called it; this one runs on libchiaki's session thread, which the CLR
                    // never created and has to attach on the way in.
                    Check("the callback ran on a thread this side never created",
                        arrived && callbackThread != 0
                        && callbackThread != Environment.CurrentManagedThreadId,
                        $"session={callbackThread} main={Environment.CurrentManagedThreadId}");

                    if (!arrived)
                        run.Stop();
                    Check("join returns once the session thread has ended",
                        run.Join() == ChiakiError.Success);
                }
            }

            Console.WriteLine();
            Console.WriteLine("ChiakiControllerState - the pad, sixty times a second");

            using (var pad = new ChiakiControllerState())
            using (var idle = new ChiakiControllerState())
            {
                // Both start idle, and idle is a state and not zeroes - the touch slots hold -1,
                // which is a finger that is up rather than a finger at the origin.
                Check("a fresh state is idle", pad.Matches(idle));
                Check("idle means both fingers are up",
                    pad.Touch(0).Id == -1 && pad.Touch(1).Id == -1,
                    $"{pad.Touch(0).Id}, {pad.Touch(1).Id}");

                // The library allocates touch ids, not this side. Two slots, and the third finger
                // is refused - a port that numbered its own would disagree with the console about
                // which finger left.
                sbyte first = pad.StartTouch(100, 200);
                sbyte second = pad.StartTouch(300, 400);
                sbyte third = pad.StartTouch(500, 600);
                Check("two touches are allocated and the third is refused",
                    first >= 0 && second >= 0 && first != second && third == -1,
                    $"{first}, {second}, {third}");
                Check("a started touch keeps the position it was given",
                    pad.Touch(0) is { X: 100, Y: 200 } && pad.Touch(0).Id == first,
                    pad.Touch(0).ToString());

                pad.SetTouchPos((byte)first, 111, 222);
                Check("moving a touch moves the slot it is in",
                    pad.Touch(0) is { X: 111, Y: 222 }, pad.Touch(0).ToString());

                pad.StopTouch((byte)first);
                Check("stopping a touch puts the finger up", pad.Touch(0).Id == -1);
                pad.StopTouch((byte)second);

                // The rest of the state, set and read back through the same handle.
                pad.Buttons = ChiakiControllerButton.Cross | ChiakiControllerButton.L1
                    | ChiakiControllerButton.Ps | ChiakiControllerButton.L2;
                pad.Triggers = (255, 128);
                pad.Sticks = (-32768, 32767, 100, -100);
                pad.SetMotion(1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f);

                Check("the button mask survives, analog bits included",
                    pad.Buttons == (ChiakiControllerButton.Cross | ChiakiControllerButton.L1
                        | ChiakiControllerButton.Ps | ChiakiControllerButton.L2),
                    pad.Buttons.ToString());
                Check("the triggers are pressures and not the bits of the same name",
                    pad.Triggers == ((byte)255, (byte)128), pad.Triggers.ToString());
                // The sticks are signed and centred on zero: a reader that took them as unsigned
                // gets 32768 for full left, which is full RIGHT, and the port would steer backwards.
                Check("the sticks keep their sign at both ends",
                    pad.Sticks == ((short)-32768, (short)32767, (short)100, (short)-100),
                    pad.Sticks.ToString());
                Check("a state with anything set is no longer idle", !pad.Matches(idle));

                // The round trip, and the reason the state is built in C at all: it goes into the
                // session through libchiaki's own setter and is compared by libchiaki's own
                // comparator - motion floats included - so nothing here can agree with itself.
                using (var padInfo = new ChiakiConnectInfo())
                {
                    padInfo.Host = "127.0.0.1";
                    padInfo.SetRegistKey("12345678"u8);
                    padInfo.SetMorning(new byte[16]);

                    using ChiakiSession? padSession = ChiakiSession.TryCreate(padInfo, null, out ChiakiError padErr);
                    Check("a session for the pad builds", padSession is not null, padErr.ToString());
                    if (padSession is not null)
                    {
                        Check("the session holds idle before anything is pushed",
                            padSession.ControllerStateMatches(idle));
                        Check("pushing the state succeeds",
                            padSession.SetControllerState(pad) == ChiakiError.Success);
                        Check("what the session holds equals what was pushed",
                            padSession.ControllerStateMatches(pad));
                        Check("and it is no longer idle", !padSession.ControllerStateMatches(idle));
                    }
                }

                pad.SetIdle();
                Check("set_idle returns the state to idle", pad.Matches(idle));
            }

            Console.WriteLine();
            Console.WriteLine("SessionConnectInfo - streamsession.cpp's first Qt-free piece");

            // isLocalAddress is a string test and not an address test, and the negatives are the
            // half that matters: each of these takes the REMOTE profile - lower resolution, lower
            // bitrate - on a machine that may be sitting beside the console.
            Check("the RFC 1918 literals are local",
                SessionConnectInfo.IsLocalAddress("10.0.0.5")
                && SessionConnectInfo.IsLocalAddress("192.168.1.7")
                && SessionConnectInfo.IsLocalAddress("172.16.0.1")
                && SessionConnectInfo.IsLocalAddress("172.31.255.1"));
            Check("172.15 and 172.32 are outside the block",
                !SessionConnectInfo.IsLocalAddress("172.15.0.1")
                && !SessionConnectInfo.IsLocalAddress("172.32.0.1"));
            // The quirk, reproduced rather than fixed: loopback and link-local are not in the
            // literal list, so the Qt client streams them at the remote profile.
            Check("loopback and link-local are NOT local by this rule",
                !SessionConnectInfo.IsLocalAddress("127.0.0.1")
                && !SessionConnectInfo.IsLocalAddress("169.254.1.1"));
            Check("a name with neither dot nor colon is not local",
                !SessionConnectInfo.IsLocalAddress("ps5")
                && !SessionConnectInfo.IsLocalAddress("") && !SessionConnectInfo.IsLocalAddress(null));
            Check("the IPv6 unique-local block is local, either case",
                SessionConnectInfo.IsLocalAddress("fd00::1")
                && SessionConnectInfo.IsLocalAddress("FC00::1")
                && !SessionConnectInfo.IsLocalAddress("fe80::1"));

            // A duid means the session goes through PSN's relay, and that is remote whatever the
            // address says - the address is not the console's in that case.
            Check("a duid makes even a local address remote",
                SessionConnectInfo.IsLocalConnection("192.168.1.7", null)
                && !SessionConnectInfo.IsLocalConnection("192.168.1.7", "some-duid"));

            var untouched = new FakePreferences();

            // The four groups, on a store where nothing was set. These are the numbers a fresh
            // install streams at, and the PS5 pair differs by resolution while the PS4 pair does
            // not - which is settings.cpp's own table and not a simplification here.
            VideoProfileChoice ps5Local = SessionConnectInfo.VideoProfile(untouched, ps5: true, "192.168.1.7", null);
            VideoProfileChoice ps5Remote = SessionConnectInfo.VideoProfile(untouched, ps5: true, "8.8.8.8", null);
            VideoProfileChoice ps4Local = SessionConnectInfo.VideoProfile(untouched, ps5: false, "192.168.1.7", null);
            VideoProfileChoice ps4Remote = SessionConnectInfo.VideoProfile(untouched, ps5: false, "8.8.8.8", null);

            Check("a PS5 on the LAN defaults to 1080p60 h265",
                ps5Local == new VideoProfileChoice(ChiakiVideoResolution.P1080, ChiakiVideoFps.Fps60, 0, ChiakiCodec.H265),
                ps5Local.ToString());
            Check("a PS5 off the LAN drops to 720p and keeps h265",
                ps5Remote == new VideoProfileChoice(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60, 0, ChiakiCodec.H265),
                ps5Remote.ToString());
            Check("a PS4 is 720p60 h264 on either side",
                ps4Local == ps4Remote
                && ps4Local == new VideoProfileChoice(ChiakiVideoResolution.P720, ChiakiVideoFps.Fps60, 0, ChiakiCodec.H264),
                ps4Local.ToString());

            // The branches a user takes, which the default store cannot reach.
            var tuned = new FakePreferences()
                .Set("settings/resolution_local_ps5", "540p")
                .Set("settings/fps_local_ps5", 30)
                .Set("settings/bitrate_local_ps5", 30000u)
                .Set("settings/codec_local_ps5", "h265_hdr");

            VideoProfileChoice tunedChoice = SessionConnectInfo.VideoProfile(tuned, ps5: true, "10.0.0.5", null);
            Check("a set resolution, fps, bitrate and codec all reach the profile",
                tunedChoice == new VideoProfileChoice(ChiakiVideoResolution.P540, ChiakiVideoFps.Fps30, 30000, ChiakiCodec.H265Hdr),
                tunedChoice.ToString());

            // clampCodecForBackend: an OpenGL window cannot present HDR. Applied to the PS5
            // codecs and not to the PS4 one, which is settings.cpp's asymmetry, not a shortcut.
            var openGl = new FakePreferences()
                .Set("settings/render_backend", "opengl")
                .Set("settings/codec_local_ps5", "h265_hdr")
                .Set("settings/codec_ps4", "h265_hdr");
            Check("HDR is clamped away on an OpenGL backend",
                SessionConnectInfo.VideoProfile(openGl, ps5: true, "10.0.0.5", null).Codec == ChiakiCodec.H265,
                SessionConnectInfo.VideoProfile(openGl, ps5: true, "10.0.0.5", null).Codec.ToString());
            Check("the PS4 codec is NOT clamped, as settings.cpp leaves it",
                SessionConnectInfo.VideoProfile(openGl, ps5: false, "10.0.0.5", null).Codec == ChiakiCodec.H265Hdr);
            Check("vulkan keeps HDR",
                SessionConnectInfo.VideoProfile(
                    new FakePreferences().Set("settings/codec_local_ps5", "h265_hdr"),
                    ps5: true, "10.0.0.5", null).Codec == ChiakiCodec.H265Hdr);

            // A value a newer client wrote reads as the default rather than throwing, which is
            // QMap::key's behaviour where the string is not in the table.
            Check("an unknown resolution or codec falls back to the default",
                SessionConnectInfo.VideoProfile(
                    new FakePreferences()
                        .Set("settings/resolution_local_ps5", "1440p")
                        .Set("settings/codec_local_ps5", "av1"),
                    ps5: true, "10.0.0.5", null)
                    == new VideoProfileChoice(ChiakiVideoResolution.P1080, ChiakiVideoFps.Fps60, 0, ChiakiCodec.H265));

            // And the profile as libchiaki ends up holding it. The codec line is the one that
            // matters: chiaki_connect_video_profile_preset writes H264 into every preset, so a
            // caller that stopped there would stream H264 on a PS5 whose default is H265 - a
            // working stream at the wrong codec, reported by nothing.
            using (var applied = new ChiakiConnectInfo())
            {
                SessionConnectInfo.Apply(applied, ps5Local);
                Check("the default PS5 profile lands as 1080p60 at the preset bitrate, h265",
                    applied.VideoProfile == new ChiakiVideoProfile(1920, 1080, 60, 15000, (int)ChiakiCodec.H265),
                    applied.VideoProfile.ToString());

                SessionConnectInfo.Apply(applied, tunedChoice);
                Check("a set bitrate replaces the preset's, and the rest comes from the preset",
                    applied.VideoProfile == new ChiakiVideoProfile(960, 540, 30, 30000, (int)ChiakiCodec.H265Hdr),
                    applied.VideoProfile.ToString());
            }

            // The settings screen stores a one-based index and the session wants the bit. An
            // off-by-one here is a shortcut that fires on the neighbouring button.
            Check("a dpad shortcut of zero stays off",
                SessionConnectInfo.DpadTouchShortcutBit(0) == 0);
            Check("a one-based index becomes its bit",
                SessionConnectInfo.DpadTouchShortcutBit(1) == 1
                && SessionConnectInfo.DpadTouchShortcutBit(2) == 2
                && SessionConnectInfo.DpadTouchShortcutBit(3) == 4
                && SessionConnectInfo.DpadTouchShortcutBit(5) == 16);

            // The increment is zero when the feature is off, which is how the session is told it
            // is off at all - there is no second flag. The feature defaults to ON, so an untouched
            // store carries the increment rather than a zero.
            Check("the dpad increment is zero only once the feature is switched off",
                SessionConnectInfo.DpadTouchIncrement(
                    new FakePreferences().Set("settings/dpad_touch_enabled", false)) == 0);
            Check("an untouched store has the feature on at Qt's 30",
                SessionConnectInfo.DpadTouchIncrement(new FakePreferences()) == 30);
            Check("and a set increment is what crosses",
                SessionConnectInfo.DpadTouchIncrement(
                    new FakePreferences().Set("settings/dpad_touch_increment", 45u)) == 45);

            Console.WriteLine();
            Console.WriteLine("SessionLogFile - the name two clients have to agree on");

            // These six are real filenames the Qt client left on this machine, and they are the
            // whole reason this file exists: the fractional part is a THREE-digit millisecond
            // written TWICE, because Qt reads "zzzzzz" as "zzz" twice rather than as microseconds.
            string[] realNames =
            {
                "chiaki_session_2026-08-11_18-52-48-402402.log",
                "chiaki_session_2026-08-11_20-10-38-088088.log",
                "chiaki_session_2026-08-11_20-39-13-285285.log",
                "chiaki_session_2026-08-11_21-00-13-133133.log",
                "chiaki_session_2026-08-11_21-12-26-220220.log",
                "chiaki_session_2026-08-11_21-16-40-818818.log",
            };

            Check("every name the Qt build wrote parses",
                realNames.All(n => SessionLogFile.TimestampOf(n) is not null),
                string.Join(", ", realNames.Where(n => SessionLogFile.TimestampOf(n) is null)));
            // The negative that makes the point: if this ever fails, somebody read the format as
            // six digits of microseconds and the two clients stopped writing the same names.
            Check("the fraction is one millisecond written twice, on every one of them",
                realNames.All(SessionLogFile.FractionIsDoubledMillisecond));
            Check("a real name decodes to the time it says",
                SessionLogFile.TimestampOf(realNames[1]) == new DateTime(2026, 8, 11, 20, 10, 38, 88),
                SessionLogFile.TimestampOf(realNames[1])?.ToString("O") ?? "<null>");

            // And this port writes the same shape. Asserted against a name Qt produced rather
            // than against the format string, which is the thing that was misread in the first place.
            Check("this port writes the shape Qt wrote",
                SessionLogFile.NameFor(new DateTime(2026, 8, 11, 20, 10, 38, 88)) == realNames[1],
                SessionLogFile.NameFor(new DateTime(2026, 8, 11, 20, 10, 38, 88)));
            Check("the log goes in the directory the Qt build already uses",
                SessionLogFile.PathFor(DateTime.Now).StartsWith(QtPaths.LogDirectory, StringComparison.Ordinal));

            Check("a name that is not a session log has no timestamp",
                SessionLogFile.TimestampOf("chiaki_baseline.jsonl") is null
                && SessionLogFile.TimestampOf("chiaki_session_hello.log") is null);

            // Rotation: newest five stay.
            string[] seven = realNames.Append("chiaki_session_2026-08-12_09-00-00-001001.log").ToArray();
            IReadOnlyList<string> removed = SessionLogFile.ToRemove(seven);
            Check("rotation keeps five and removes the oldest",
                removed.Count == 2
                && removed.Contains("chiaki_session_2026-08-11_18-52-48-402402.log")
                && removed.Contains("chiaki_session_2026-08-11_20-10-38-088088.log"),
                string.Join(", ", removed));

            // The `break` on an unparseable name reads alarming and is not: a dateless entry
            // sorts below every real one, so the loop meets it only after every actual log has
            // been considered. What it does mean is that a stray file matching the wildcard is
            // never deleted - asserted because "it stops rotation" was the first reading of it,
            // and the difference is a directory that grows for ever against one that does not.
            IReadOnlyList<string> withStray = SessionLogFile.ToRemove(seven.Append("chiaki_session_stray.log"));
            Check("a stray file is spared and the rotation still runs",
                withStray.Count == 2 && !withStray.Contains("chiaki_session_stray.log"),
                string.Join(", ", withStray));

            Console.WriteLine();
            Console.WriteLine("SessionLogSanitizer - what a log may be pasted into an issue with");

            Check("a bare address is redacted",
                SessionLogSanitizer.Sanitize("connecting to 192.168.1.7 now")
                    == "connecting to <redacted-ipv4> now",
                SessionLogSanitizer.Sanitize("connecting to 192.168.1.7 now"));

            // The order is the design: the address rule runs first, and then the label rule
            // replaces the marker it left. A reordering would leak the shape of the value.
            Check("a labelled address ends as <redacted> and not as <redacted-ipv4>",
                SessionLogSanitizer.Sanitize("console ip: 10.0.0.1") == "console ip: <redacted>",
                SessionLogSanitizer.Sanitize("console ip: 10.0.0.1"));

            Check("a full IPv6 address is redacted",
                SessionLogSanitizer.Sanitize("bound fd00:1234:5678:9abc:0:0:0:1 ok")
                    == "bound <redacted-ipv6> ok",
                SessionLogSanitizer.Sanitize("bound fd00:1234:5678:9abc:0:0:0:1 ok"));

            // PP88, and the case that was leaking: the old pattern stopped at the first "::" and
            // left the tail in the log behind a marker that said redacted.
            Check("a compressed IPv6 is redacted whole, tail included",
                SessionLogSanitizer.Sanitize("bound fd00:1234:5678:9abc::1 ok")
                    == "bound <redacted-ipv6> ok",
                SessionLogSanitizer.Sanitize("bound fd00:1234:5678:9abc::1 ok"));
            // The brackets go with the address - \[? and \]? are inside the pattern, which is what
            // the old one did too - and the port survives, which is the part worth keeping in a
            // log: a wrong port is a real diagnosis and the address is not.
            Check("a bracketed address loses its brackets and keeps its port",
                SessionLogSanitizer.Sanitize("peer [fe80::1]:9295 up")
                    == "peer <redacted-ipv6>:9295 up",
                SessionLogSanitizer.Sanitize("peer [fe80::1]:9295 up"));
            Check("the shortest forms are still caught",
                SessionLogSanitizer.Sanitize("via ::1 now") == "via <redacted-ipv6> now"
                && SessionLogSanitizer.Sanitize("via fe80::1 now") == "via <redacted-ipv6> now",
                SessionLogSanitizer.Sanitize("via ::1 now"));

            // The floor that keeps the widened pattern off ordinary text. Three runs is what a
            // clock does not reach, and it is the same floor the old pattern had - a rule that
            // redacted timestamps would make the log useless in the other direction.
            Check("a clock is not an address",
                SessionLogSanitizer.Sanitize("at 20:10:38 the stream started")
                    == "at 20:10:38 the stream started",
                SessionLogSanitizer.Sanitize("at 20:10:38 the stream started"));

            Check("account and duid assignments are redacted",
                SessionLogSanitizer.Sanitize("account_id=abcdef duid=0011ZZ")
                    == "account_id=<redacted> duid=<redacted>",
                SessionLogSanitizer.Sanitize("account_id=abcdef duid=0011ZZ"));

            Check("a session id is redacted both spellings",
                SessionLogSanitizer.Sanitize("session id = Zm9vYmFy") == "session id = <redacted>"
                && SessionLogSanitizer.Sanitize("Session ID QUJDRUZH") == "Session ID <redacted>",
                SessionLogSanitizer.Sanitize("Session ID QUJDRUZH"));

            Check("a uuid is redacted",
                SessionLogSanitizer.Sanitize("did 123e4567-e89b-12d3-a456-426614174000 x")
                    == "did <redacted-uuid> x",
                SessionLogSanitizer.Sanitize("did 123e4567-e89b-12d3-a456-426614174000 x"));

            // The blunt one, and the last to run: any sixteen hex digits. It is what catches a
            // console id nobody labelled, and it over-redacts on purpose.
            Check("a long hex run is redacted",
                SessionLogSanitizer.Sanitize("mac 0011223344556677 seen")
                    == "mac <redacted-hex> seen",
                SessionLogSanitizer.Sanitize("mac 0011223344556677 seen"));
            Check("fifteen hex digits are left alone, which is where the line is drawn",
                SessionLogSanitizer.Sanitize("id 001122334455667 seen")
                    == "id 001122334455667 seen",
                SessionLogSanitizer.Sanitize("id 001122334455667 seen"));

            // Ordinary text must survive, or the log stops being worth keeping.
            Check("ordinary text is untouched",
                SessionLogSanitizer.Sanitize("Switched to profile 0, resolution: 1920x1080")
                    == "Switched to profile 0, resolution: 1920x1080",
                SessionLogSanitizer.Sanitize("Switched to profile 0, resolution: 1920x1080"));

            // The half of PP88 that cannot be asserted by running this code: the Qt client's own
            // copy of the same nine patterns. libchiaki has no regex engine, so there is no C
            // translation unit both halves could share without hand-rolling nine matchers -
            // duplication was the choice, and this is the check that it stays a duplication rather
            // than becoming a divergence.
            string? cppSource = SanitizerSource.Locate();
            if (cppSource is null)
            {
                // Not a failure. A published executable has no gui/src beside it, and a check
                // that cannot run should say so rather than pass.
                Console.WriteLine($"  --    the Qt client's patterns  (no {SanitizerSource.RelativePath} here)");
            }
            else
            {
                IReadOnlyList<string> cppPatterns = SanitizerSource.PatternsIn(cppSource);
                Check("the Qt client declares the same nine patterns, character for character",
                    cppPatterns.Count == SessionLogSanitizer.Patterns.Count
                    && cppPatterns.OrderBy(p => p, StringComparer.Ordinal)
                        .SequenceEqual(SessionLogSanitizer.Patterns.OrderBy(p => p, StringComparer.Ordinal)),
                    string.Join(" | ", SessionLogSanitizer.Patterns.Except(cppPatterns, StringComparer.Ordinal)));
            }

            Console.WriteLine();
            Console.WriteLine("SessionBaseline - the ledger the two builds are compared with");

            // The pin. A libchiaki that bumps the schema has to break this rather than let the
            // host append rows a reader silently mixes with the old ones.
            Check("the schema is the one this host was written against",
                SessionBaseline.Schema == SessionBaseline.ExpectedSchema,
                $"{SessionBaseline.Schema} vs {SessionBaseline.ExpectedSchema}");

            using (var baseline = new SessionBaseline())
            {
                baseline.SetStarted(DateTimeOffset.FromUnixTimeSeconds(1_786_000_000));
                baseline.SetDuration(TimeSpan.FromSeconds(90));
                baseline.SetAppVersion("1.10.0");
                baseline.SetVideo("h265", 1920, 1080, 60, 15000);
                baseline.SetConfig("d3d11va", "vulkan", 0.05, idrOnFecFailure: true);
                baseline.SetMeasured(11.25, 0.004, framesPresented: 5400, framesLost: 3,
                    framesDropped: 1, networkRttUs: 12000);

                // The latency estimate is a sum of three terms, and pushing into any of them has
                // to move it. Asserted as a direction rather than as an arithmetic identity,
                // because re-deriving the sum here would be a second implementation of the thing
                // being checked.
                ulong before = baseline.LatencyEstimateUs;
                baseline.PushHandoff(4000);
                baseline.PushHandoff(6000);
                baseline.PushInputToWire(800);
                Check("the handoff average is the library's own fold",
                    baseline.HandoffAverageUs == 5000, baseline.HandoffAverageUs.ToString());
                Check("samples move the latency estimate and the rtt is already in it",
                    before == 12000 && baseline.LatencyEstimateUs > before,
                    $"{before} -> {baseline.LatencyEstimateUs}");

                // The line, produced by libchiaki and parsed back with a real JSON reader - which
                // is what any tool comparing the two builds will do to it.
                string line = baseline.Format();
                Check("the line is one JSON object ending in a newline",
                    line.EndsWith('\n') && line.TrimEnd('\n').Count(c => c == '\n') == 0
                    && line.StartsWith('{'),
                    line.Length.ToString());

                using var doc = System.Text.Json.JsonDocument.Parse(line);
                System.Text.Json.JsonElement root = doc.RootElement;
                Check("the row carries the schema, the timestamp and the app version",
                    root.GetProperty("schema").GetInt32() == (int)SessionBaseline.ExpectedSchema
                    && root.GetProperty("started_utc").GetString() == "2026-08-06T07:06:40Z"
                    && root.GetProperty("app_version").GetString() == "1.10.0",
                    root.GetProperty("started_utc").GetString() ?? "<null>");
                Check("the picture that was asked for is in it",
                    root.GetProperty("video").GetProperty("width").GetInt32() == 1920
                    && root.GetProperty("video").GetProperty("codec").GetString() == "h265"
                    && root.GetProperty("settings").GetProperty("bitrate_kbps").GetInt32() == 15000);
                // The decoder and the renderer travel together: a row naming one without the
                // other cannot be compared with another row (PP72).
                Check("the decoder and the renderer that allowed it are both there",
                    root.GetProperty("settings").GetProperty("hw_decoder").GetString() == "d3d11va"
                    && root.GetProperty("settings").GetProperty("renderer").GetString() == "vulkan");
                // And what it must NOT carry. The identifying fields are the ones the log needs a
                // sanitiser for, so the record does not collect them - asserted here because the
                // row is the thing that would be shared, and "no field to transmit" is the guard.
                Check("no console, address, session id or account is in the row",
                    !line.Contains("nickname", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("host", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("account", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("session_id", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("duid", StringComparison.OrdinalIgnoreCase));

                // Appending, which is what makes the file a ledger rather than a report: two
                // sessions are two lines, and the second must not rewrite the first.
                string ledger = Path.Combine(Path.GetTempPath(), $"chiaki_baseline_selftest_{Environment.ProcessId}.jsonl");
                try
                {
                    File.Delete(ledger);
                    Check("appending a row succeeds", baseline.AppendTo(ledger) == ChiakiError.Success);
                    baseline.SetDuration(TimeSpan.FromSeconds(120));
                    baseline.AppendTo(ledger);

                    string[] rows = File.ReadAllLines(ledger);
                    Check("two sessions are two lines, oldest first",
                        rows.Length == 2 && rows[0] == line.TrimEnd('\n') && rows[1] != rows[0],
                        rows.Length.ToString());
                }
                finally
                {
                    File.Delete(ledger);
                }

                Check("the ledger this host would write to is the one the Qt build uses",
                    SessionBaseline.LedgerPath == QtPaths.SessionBaselineFile
                    && SessionBaseline.LedgerPath.EndsWith("chiaki_baseline.jsonl", StringComparison.Ordinal));
            }

            Console.WriteLine();
            Console.WriteLine("InputTranslation - the keyboard and mouse, off the QEvent");

            using (var keyState = new ChiakiControllerState())
            {
                // The trap that has no symptom but a wrong stream: the vertical half-axes are
                // NEGATIVE up and the horizontal ones are POSITIVE up. A port that gave both the
                // same sense inverts one axis, and a user calls that "the aiming feels wrong".
                InputTranslation.ApplyBinding(keyState, (uint)ControllerButtonExt.AnalogStickLeftYUp, true);
                InputTranslation.ApplyBinding(keyState, (uint)ControllerButtonExt.AnalogStickLeftXUp, true);
                Check("up is negative on Y and positive on X",
                    keyState.Sticks.LeftY == -0x7fff && keyState.Sticks.LeftX == 0x7fff,
                    keyState.Sticks.ToString());

                InputTranslation.ApplyBinding(keyState, (uint)ControllerButtonExt.AnalogStickRightYDown, true);
                InputTranslation.ApplyBinding(keyState, (uint)ControllerButtonExt.AnalogStickRightXDown, true);
                Check("down is the other sign on each axis",
                    keyState.Sticks.RightY == 0x7fff && keyState.Sticks.RightX == -0x7fff,
                    keyState.Sticks.ToString());

                InputTranslation.ApplyBinding(keyState, (uint)ControllerButtonExt.AnalogStickLeftYUp, false);
                Check("releasing a half-axis returns it to centre and leaves the others",
                    keyState.Sticks == ((short)0x7fff, (short)0, (short)-0x7fff, (short)0x7fff),
                    keyState.Sticks.ToString());

                // The second trap: a trigger binding sets a PRESSURE, not the bit of the same
                // name. The bit is what the mapping carries; the console reads l2_state.
                InputTranslation.ApplyBinding(keyState, InputTranslation.AnalogButtonL2, true);
                Check("L2 sets the pressure and not the analog-button bit",
                    keyState.Triggers == ((byte)0xff, (byte)0)
                    && ((uint)keyState.Buttons & InputTranslation.AnalogButtonL2) == 0,
                    $"{keyState.Triggers} buttons={(uint)keyState.Buttons:x}");
                InputTranslation.ApplyBinding(keyState, InputTranslation.AnalogButtonL2, false);
                Check("releasing the trigger returns the pressure to zero",
                    keyState.Triggers == ((byte)0, (byte)0));

                // Everything else is an ordinary bit, set on press and cleared on release.
                InputTranslation.ApplyBinding(keyState, (uint)ChiakiControllerButton.Cross, true);
                InputTranslation.ApplyBinding(keyState, (uint)ChiakiControllerButton.Ps, true);
                Check("an ordinary binding sets its bit",
                    keyState.Buttons == (ChiakiControllerButton.Cross | ChiakiControllerButton.Ps),
                    keyState.Buttons.ToString());
                InputTranslation.ApplyBinding(keyState, (uint)ChiakiControllerButton.Cross, false);
                Check("and releasing clears only that one",
                    keyState.Buttons == ChiakiControllerButton.Ps, keyState.Buttons.ToString());
            }

            // The two touchpads are not the same shape - 1920x942 against 1919x1079 - so one pair
            // used for both puts a touch in the wrong place on one console.
            Check("the PS4 and PS5 touchpads differ on both axes",
                InputTranslation.TouchpadBounds(false) == (1920.0f, 942.0f)
                && InputTranslation.TouchpadBounds(true) == (1919.0f, 1079.0f));
            Check("the middle of the window is the middle of each pad",
                InputTranslation.MouseToTouchpad(640, 360, 1280, 720, ps5: true) == ((ushort)959, (ushort)539)
                && InputTranslation.MouseToTouchpad(640, 360, 1280, 720, ps5: false) == ((ushort)960, (ushort)471),
                InputTranslation.MouseToTouchpad(640, 360, 1280, 720, true).ToString());

            // PP91. Both paths used to read std::clamp(0.0, x, width) - the value first and the
            // bounds after - so 0.0 was what got clamped and the upper bound never applied. The
            // negative side worked by accident; the right edge did not, and a drag off the window
            // told the console the finger was past the end of its own touchpad.
            Check("a coordinate left of the window comes back at zero",
                InputTranslation.MouseToTouchpad(-50, -50, 1280, 720, ps5: true) == ((ushort)0, (ushort)0));
            Check("a coordinate past the right edge stops at the edge of the pad",
                InputTranslation.MouseToTouchpad(2560, 1440, 1280, 720, ps5: true) == ((ushort)1919, (ushort)1079),
                InputTranslation.MouseToTouchpad(2560, 1440, 1280, 720, true).ToString());
            Check("the normalised path is bounded at both ends too",
                InputTranslation.Normalize(2560, 1280) == 1.0f
                && InputTranslation.Normalize(-10, 1280) == 0.0f,
                InputTranslation.Normalize(2560, 1280).ToString(System.Globalization.CultureInfo.InvariantCulture));

            // And the Qt client's own four calls, which is the half this code cannot exercise.
            // The check is narrow on purpose: the value being clamped is never a constant, so a
            // literal in the first position means the value and the bounds were swapped - which is
            // exactly the mistake that was made, four times, in one file.
            string? sessionSource = SessionSource.Locate();
            if (sessionSource is null)
            {
                Console.WriteLine($"  --    the Qt client's clamps  (no {SessionSource.RelativePath} here)");
            }
            else
            {
                IReadOnlyList<string> clamps = SessionSource.ClampCalls(sessionSource);
                Check("the Qt client still has its four clamps",
                    clamps.Count == 4, clamps.Count.ToString());
                Check("and none of them clamps a literal",
                    clamps.All(c => !SessionSource.FirstArgumentIsLiteral(c)),
                    string.Join(" | ", clamps.Where(SessionSource.FirstArgumentIsLiteral)));
            }

            // The outer 5% of any edge is a touchpad click, judged on the normalised coordinate.
            Check("the edges of the window are a touchpad click",
                InputTranslation.IsEdgeTouch(0.02f, 0.5f) && InputTranslation.IsEdgeTouch(0.5f, 0.99f)
                && !InputTranslation.IsEdgeTouch(0.5f, 0.5f));

            Console.WriteLine();
            Console.WriteLine("DpadTouch - the dpad as a finger on the touchpad");

            using (var pad2 = new ChiakiControllerState())
            using (var touch = new ChiakiControllerState())
            {
                var dpad = new DpadTouch(ps5: true) { Increment = 30 };

                // Two directions held at once. The C++ tests left first and returns, so this is a
                // step left - and only the left bit is cleared, so the up bit survives into
                // whatever reads the pad state next.
                pad2.Buttons = ChiakiControllerButton.DpadLeft | ChiakiControllerButton.DpadUp;
                DpadTouchAction first = dpad.Handle(pad2, touch);
                Check("left wins over up, and only left is consumed",
                    first == DpadTouchAction.Started
                    && pad2.Buttons == ChiakiControllerButton.DpadUp,
                    $"{first} / {pad2.Buttons}");

                // A touch starts AT the edge it comes from, not in the middle. 1079/2 is 539.
                Check("a left press starts the finger at the left edge, halfway down",
                    dpad.Value == ((ushort)0, (ushort)539) && dpad.TouchId >= 0,
                    dpad.Value.ToString());
                Check("and libchiaki is holding that finger",
                    touch.Touch(0).Id == dpad.TouchId && touch.Touch(0) is { X: 0, Y: 539 },
                    touch.Touch(0).ToString());

                // â€¦so the second press in the same direction cannot move it. Worth asserting
                // rather than assuming: a port that started in the middle would give the user a
                // different gesture for the same two presses.
                pad2.Buttons = ChiakiControllerButton.DpadLeft;
                Check("pressing left again cannot go past the edge",
                    dpad.Handle(pad2, touch) == DpadTouchAction.Moved
                    && dpad.Value == ((ushort)0, (ushort)539),
                    dpad.Value.ToString());

                // A different direction moves by the increment on its own axis only.
                pad2.Buttons = ChiakiControllerButton.DpadDown;
                dpad.Handle(pad2, touch);
                pad2.Buttons = ChiakiControllerButton.DpadRight;
                dpad.Handle(pad2, touch);
                Check("down then right moves one axis each",
                    dpad.Value == ((ushort)30, (ushort)569), dpad.Value.ToString());
                Check("and the touch libchiaki holds followed it",
                    touch.Touch(0) is { X: 30, Y: 569 }, touch.Touch(0).ToString());

                // Nothing held is not an error, it is a no-op with a warning in the Qt build.
                pad2.Buttons = ChiakiControllerButton.Cross;
                Check("no direction held does nothing and consumes nothing",
                    dpad.Handle(pad2, touch) == DpadTouchAction.None
                    && pad2.Buttons == ChiakiControllerButton.Cross
                    && dpad.Value == ((ushort)30, (ushort)569));

                // The stop timer's job: the finger comes up and the id is free again.
                dpad.Stop(touch);
                Check("stopping lifts the finger",
                    dpad.TouchId == -1 && touch.Touch(0).Id == -1, touch.Touch(0).ToString());
            }

            using (var pad3 = new ChiakiControllerState())
            using (var touch3 = new ChiakiControllerState())
            {
                // The far edge, which is the other half of the clamp and uses the other test.
                var dpad = new DpadTouch(ps5: true) { Increment = 30 };
                pad3.Buttons = ChiakiControllerButton.DpadDown;
                dpad.Handle(pad3, touch3);
                Check("a down press starts at the bottom, halfway across",
                    dpad.Value == ((ushort)959, (ushort)1079), dpad.Value.ToString());

                pad3.Buttons = ChiakiControllerButton.DpadDown;
                dpad.Handle(pad3, touch3);
                Check("and cannot step past it",
                    dpad.Value == ((ushort)959, (ushort)1079), dpad.Value.ToString());
            }

            // PP93, and the whole of it: the walk ends on the connected console's own pad. It used
            // to end on 1920x1079 whichever console was attached - the larger value of each axis,
            // and therefore neither pad - so a PS4 finger was driven to y=1079 on a pad that stops
            // at 942. A seventh of the height, on every dpad-down gesture.
            using (var pad4 = new ChiakiControllerState())
            using (var touch4 = new ChiakiControllerState())
            {
                var ps4Dpad = new DpadTouch(ps5: false);
                pad4.Buttons = ChiakiControllerButton.DpadDown;
                ps4Dpad.Handle(pad4, touch4);
                Check("a PS4 dpad-down stops at 942 and not at 1079",
                    ps4Dpad.Value == ((ushort)960, (ushort)942), ps4Dpad.Value.ToString());

                Check("each console's dpad walks its own pad",
                    (ps4Dpad.MaxX, ps4Dpad.MaxY) == ((ushort)1920, (ushort)942)
                    && (new DpadTouch(ps5: true).MaxX, new DpadTouch(ps5: true).MaxY)
                        == ((ushort)1919, (ushort)1079),
                    $"{ps4Dpad.MaxX}x{ps4Dpad.MaxY}");
            }

            Console.WriteLine();
            Console.WriteLine("AudioRing - drop the oldest, never the newest");

            // The three multipliers are the latency policy, and they are the numbers a port picks
            // differently by accident: eight buffers of ring, fill the sink to two, clear it past
            // three.
            Check("the ring, the drain target and the clear threshold are 8, 2 and 3 buffers",
                AudioRing.CapacityFor(1024) == 8192
                && AudioRing.DrainTargetFor(1024) == 2048
                && AudioRing.ClearThresholdFor(1024) == 3072);

            var ring = new AudioRing(8);
            Check("a fresh ring is empty", ring is { Fill: 0, Capacity: 8, OverflowReported: false });

            Check("what goes in comes out in order",
                !ring.Write([1, 2, 3]) && ring.Fill == 3
                && ring.Read(3).SequenceEqual(new byte[] { 1, 2, 3 }) && ring.Fill == 0);

            // The seam. A read never crosses the end of the storage, so a drain that wants more
            // than the tail holds takes two turns - which is what the Qt client does, and a port
            // that stitched the two halves would take a different number of iterations.
            var seam = new AudioRing(8);
            seam.Write([1, 2, 3, 4, 5, 6]);
            seam.Read(6);
            seam.Write([7, 8, 9, 10]);
            byte[] acrossSeam = seam.Read(4);
            Check("a read stops at the end of the storage",
                acrossSeam.SequenceEqual(new byte[] { 7, 8 }) && seam.Fill == 2,
                string.Join(",", acrossSeam));
            Check("and the rest arrives on the next turn",
                seam.Read(4).SequenceEqual(new byte[] { 9, 10 }) && seam.Fill == 0);

            // A frame that does not fit drops the OLDEST bytes. This is the whole policy: audio
            // is only worth playing if it is current, so what the listener has not heard yet is
            // what goes.
            var tight = new AudioRing(4);
            tight.Write([1, 2, 3]);
            Check("a write that overflows drops the oldest and keeps the newest",
                tight.Write([4, 5, 6]) && tight.Fill == 4
                && tight.Read(4).SequenceEqual(new byte[] { 3, 4 }),
                string.Join(",", tight.Read(4)));

            // A frame larger than the whole ring keeps its own TAIL. Keeping the head would play
            // the oldest slice of a frame that is already too late.
            var small = new AudioRing(3);
            small.Write([9, 9]);
            small.Write([1, 2, 3, 4, 5]);
            Check("a frame bigger than the ring keeps its tail, not its head",
                small.Fill == 3 && small.Read(3).SequenceEqual(new byte[] { 3, 4, 5 }),
                string.Join(",", small.Read(3)));

            // The log fires once per slow patch, not once per frame, and running dry re-arms it.
            var noisy = new AudioRing(4);
            noisy.Write([1, 2, 3, 4]);
            noisy.Write([5, 6]);
            Check("an overflow is reported", noisy.OverflowReported);
            noisy.Write([7, 8]);
            Check("and stays reported while it keeps overflowing", noisy.OverflowReported);
            while (noisy.Fill > 0)
                noisy.Read(4);
            // Emptying the ring is not what re-arms it: the flag is cleared by a DRAIN that finds
            // nothing, which is the turn of the loop after the last byte left. Asserted in two
            // steps because the difference is one log line at the start of the next slow patch.
            Check("the last byte leaving does not re-arm it", noisy.OverflowReported);
            noisy.Read(4);
            Check("a drain that finds nothing re-arms it", !noisy.OverflowReported);

            // Degenerate inputs are no-ops rather than exceptions: an empty frame is what a muted
            // stream sends, and a zero-capacity ring is what exists before InitAudio has run.
            Check("an empty frame and a zero ring do nothing",
                !new AudioRing(0).Write([1, 2, 3]) && !ring.Write([])
                && new AudioRing(0).Read(4).Length == 0);

            // Clearing is what happens when the sink is more than three buffers behind.
            var full = new AudioRing(4);
            full.Write([1, 2, 3, 4]);
            full.Reset();
            Check("a reset empties the ring and re-arms the log",
                full is { Fill: 0, OverflowReported: false } && full.Read(4).Length == 0);

            // The microphone path is this same ring with no target to stop at, which is the only
            // difference between QueueAudioOutData and QueueMicData in the Qt client - the other
            // fifty lines are the same twice. Asserted here so the one ring is known to cover both.
            var mic = new AudioRing(AudioRing.CapacityFor(2));
            Check("both rings are eight frames deep", mic.Capacity == 16);
            mic.Write([1, 2, 3, 4, 5]);
            Check("an unbounded read takes everything contiguous",
                mic.Read().SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }) && mic.Fill == 0,
                mic.Fill.ToString());

            // …and it is still bounded by the seam, which is the property that is easy to lose
            // when a read has no size argument to look at.
            var micSeam = new AudioRing(8);
            micSeam.Write([1, 2, 3, 4, 5, 6]);
            micSeam.Read();
            micSeam.Write([7, 8, 9, 10]);
            Check("an unbounded read still stops at the end of the storage",
                micSeam.Read().SequenceEqual(new byte[] { 7, 8 }) && micSeam.Fill == 2,
                micSeam.Fill.ToString());

            Console.WriteLine();
            Console.WriteLine("RpCrypt - the oracle, read out of the suite that holds it");

            Check("the key size comes from the shim", RpCrypt.KeySize == 16, RpCrypt.KeySize.ToString());

            string? vectorFile = CryptoVectors.Locate();
            if (vectorFile is null)
            {
                // Not a failure. The vectors live in test/rpcrypt.c and a published executable has
                // no test/ beside it; a check that cannot run should say so rather than pass.
                Console.WriteLine($"  --    the recorded key vectors  (no {CryptoVectors.RelativePath} here)");
            }
            else
            {
                // Every byte below is the munit case's, parsed out of the C file rather than
                // copied here. Two copies of an oracle agree with each other long after either
                // agrees with a console, which is the failure PP82 named about the preference
                // table - so there is one copy, and both suites cite it.
                IReadOnlyDictionary<string, byte[]> pre10 =
                    CryptoVectors.InFunction(vectorFile, "test_bright_ambassador_ps4_pre10");
                Check("the pre-10 case's vectors are readable",
                    pre10.Count == 4 && pre10["nonce"].Length == 16,
                    string.Join(",", pre10.Keys));

                (byte[] bright, byte[] ambassador) = RpCrypt.BrightAmbassador(
                    ChiakiTarget.Ps4_9, pre10["nonce"], pre10["morning"]);
                Check("a PS4 before firmware 10 derives the keys a console agreed to",
                    bright.SequenceEqual(pre10["bright_expected"])
                    && ambassador.SequenceEqual(pre10["ambassador_expected"]),
                    Convert.ToHexString(bright));

                // The target is part of the derivation and not a label on it, which is why the
                // vectors come in pairs: the same two inputs give different keys on either side
                // of firmware 10.
                IReadOnlyDictionary<string, byte[]> post10 =
                    CryptoVectors.InFunction(vectorFile, "test_bright_ambassador");
                (byte[] bright10, byte[] amb10) = RpCrypt.BrightAmbassador(
                    ChiakiTarget.Ps4_10, post10["nonce_local"], post10["morning_local"]);
                Check("and firmware 10 derives different ones from its own vectors",
                    bright10.SequenceEqual(post10["bright_expected"])
                    && amb10.SequenceEqual(post10["ambassador_expected"]),
                    Convert.ToHexString(bright10));

                // The negative that makes the pair mean something: the target genuinely changes
                // the answer, so a port that ignored it would pass one case and fail the other.
                (byte[] wrongTarget, _) = RpCrypt.BrightAmbassador(
                    ChiakiTarget.Ps4_10, pre10["nonce"], pre10["morning"]);
                Check("deriving with the wrong target gives the wrong key",
                    !wrongTarget.SequenceEqual(pre10["bright_expected"]));

                IReadOnlyDictionary<string, byte[]> ivCase =
                    CryptoVectors.InFunction(vectorFile, "test_iv_ps4_pre10");
                using var crypt = new RpCrypt(ChiakiTarget.Ps4_9, ivCase["nonce"], ivCase["morning"]);
                Check("counter zero gives the recorded iv",
                    crypt.GenerateIv(0).SequenceEqual(ivCase["iv_a_expected"]),
                    Convert.ToHexString(crypt.GenerateIv(0)));
                Check("and a counter gives its own, repeatably",
                    crypt.GenerateIv(0x0102030405060708).SequenceEqual(ivCase["iv_b_expected"])
                    && crypt.GenerateIv(0x0102030405060708).SequenceEqual(ivCase["iv_b_expected"]),
                    Convert.ToHexString(crypt.GenerateIv(0x0102030405060708)));

                // A round trip through the cipher, which is the property the vectors cannot state:
                // they pin the keys and the ivs, and this pins that the stream they key is one
                // stream and not two.
                byte[] plain = "the seam holds"u8.ToArray();
                byte[] cipher = crypt.Encrypt(7, plain);
                Check("what one counter encrypts, the same counter decrypts",
                    !cipher.SequenceEqual(plain) && crypt.Decrypt(7, cipher).SequenceEqual(plain),
                    Convert.ToHexString(cipher));
                Check("and a different counter does not",
                    !crypt.Decrypt(8, cipher).SequenceEqual(plain));
            }

            Console.WriteLine();
            Console.WriteLine("PlaceboBackends - what PP9's renderer decision rests on");

            // PP9 offered three shapes and all three assume libplacebo means Vulkan. These are the
            // checkable claims behind the fourth: run libplacebo ON D3D11. Nothing here has been
            // built or run - what is asserted is the ground the decision stands on, so that it
            // fails loudly rather than turning out to have been wrong halfway through a screen.
            string? plConfig = PlaceboBackends.LocateHeader("config.h");
            string? plD3d11 = PlaceboBackends.LocateHeader("d3d11.h");
            string? qmlWindow = PlaceboBackends.LocateWindow();

            if (plConfig is null || plD3d11 is null || qmlWindow is null)
            {
                Console.WriteLine("  --    libplacebo's backends  (no MSYS2 toolchain or no checkout here)");
            }
            else
            {
                string config = File.ReadAllText(plConfig);
                string d3d11 = File.ReadAllText(plD3d11);
                string window = File.ReadAllText(qmlWindow);

                // The premise. D3D11 is optional in libplacebo at BUILD time, so this is a fact
                // about the installation this tree links, not about the project - and without it
                // the fourth shape does not exist and PP9 falls back to its original three.
                Check("the libplacebo this tree links has both backends compiled in",
                    PlaceboBackends.Compiled(config, "D3D11") && PlaceboBackends.Compiled(config, "VULKAN"),
                    PlaceboBackends.Compiled(config, "D3D11") ? "vulkan missing" : "d3d11 missing");

                // What a d3d11va frame is worth. PP77 prefers vulkan because it is "the one decoder
                // whose frame the renderer can take without a copy"; wrapping NV12 and P010 is that
                // sentence being about d3d11va instead - and d3d11va is PP51's non-NVIDIA floor.
                Check("the D3D11 backend adopts a decoder's own texture, video formats included",
                    PlaceboBackends.WrapsAVideoTexture(d3d11));

                // The argument itself. Of the backend-named calls the Qt window makes, the ones
                // with no D3D11 counterpart must be exactly the QtQuick ones - hold, release and
                // the timeline semaphore exist to hand the image to Qt's OWN Vulkan renderer so
                // QML can draw over the video. The port has no QtQuick and no such handover; WPF
                // composites instead. If a call outside that set ever loses its counterpart, the
                // substitution stops being a substitution and this is where that shows up.
                IReadOnlySet<string> used = PlaceboBackends.BackendCalls(window, "vulkan");
                IReadOnlySet<string> offered = PlaceboBackends.BackendCalls(d3d11, "d3d11");
                string[] orphans = used.Where(c => !offered.Contains(c)).Order(StringComparer.Ordinal).ToArray();
                string[] qtQuickOnly =
                [
                    "hold_ex", "hold_params", "release_ex", "release_params",
                    "sem_create", "sem_destroy", "sem_params", "unwrap",
                ];

                Check("every backend call the window makes has a D3D11 counterpart, bar QtQuick's",
                    orphans.SequenceEqual(qtQuickOnly.Order(StringComparer.Ordinal)),
                    string.Join(", ", orphans));

                // And the reason none of this is option C: the shaders live above pl_gpu. They do
                // not know which backend is under them, and they are what the picture looks like.
                Check("the renderer work above pl_gpu is backend-agnostic and is most of it",
                    PlaceboBackends.RendererCalls(window) > PlaceboBackends.BackendCalls(window, "vulkan").Count,
                    $"{PlaceboBackends.RendererCalls(window)} agnostic vs {used.Count} backend-named");
            }

            Console.WriteLine();
            Console.WriteLine("Gamepads - SDL, before a pad is plugged in");

            Check("the hint table names the four the input path depends on",
                Gamepads.Hints.Count == 4
                && Gamepads.Hints.All(h => h.Name.StartsWith("SDL_", StringComparison.Ordinal))
                && Gamepads.Hints.Count(h => h.Value == "1") == 3,
                string.Join(", ", Gamepads.Hints.Select(h => $"{h.Name}={h.Value}")));

            // PP117, resolved. SDL2 loads now because the resolver puts the portable tree on the
            // PROCESS search path: SDL resolves a dependency from inside its own initialisation,
            // and LOAD_WITH_ALTERED_SEARCH_PATH - which is what NativeLibrary.TryLoad uses - does
            // not reach that lookup. The failure presented as a hang because Windows reports
            // ERROR_DLL_INIT_FAILED with a modal dialog, and a process with no visible window has
            // nothing to click.
            Check("SDL loads and reports its version",
                Gamepads.LinkedVersion().Major >= 2, Gamepads.LinkedVersion().ToString());
            Check("and it came out of the portable tree, not off PATH",
                ChiakiNative.SdlLoadedFrom?.EndsWith("SDL2.dll", StringComparison.OrdinalIgnoreCase) == true,
                ChiakiNative.SdlLoadedFrom ?? "<null>");

            // The hints are set before SDL_Init reads past them, which is the order the real host
            // uses anyway - so this is not a test-only sequence.
            var unset = Gamepads.Hints.Where(h => !Gamepads.SetHint(h.Name, h.Value)).ToList();
            Check("every hint can be set and reads back as it was set",
                unset.Count == 0 && Gamepads.Hints.All(h => Gamepads.GetHint(h.Name) == h.Value),
                string.Join(", ", Gamepads.Hints.Select(h => $"{h.Name}={Gamepads.GetHint(h.Name) ?? "<unset>"}")));

            // The subsystem itself, now through SdlThread, which is the thing PP8's rationale asks
            // for: SDL's own loop on a thread that does not stall on rendering. Every wait below is
            // bounded, because a suite that hangs reports nothing - which this port learned twice.
            int joysticks = -1;
            int workRanOn = -1;
            var seen = new List<SdlEvent>();

            using var sdl = new SdlThread(ev => seen.Add(ev));
            SdlStart started = sdl.Start(TimeSpan.FromSeconds(30));

            Check("the game controller subsystem starts on its own thread",
                started == SdlStart.Started,
                started == SdlStart.TimedOut ? "no answer within 30s" : $"{started}: {sdl.Error}");

            if (started == SdlStart.Started)
            {
                // Everything that touches a controller handle has to run where SDL was started,
                // so the first thing to assert about Post is WHERE it ran, not that it ran.
                bool invoked = sdl.Invoke(() =>
                {
                    workRanOn = Environment.CurrentManagedThreadId;
                    joysticks = Gamepads.NumJoysticks();
                }, TimeSpan.FromSeconds(5));

                Check("posted work runs on the thread that owns SDL",
                    invoked && workRanOn == sdl.ThreadId,
                    invoked ? $"ran on {workRanOn}, SDL owns {sdl.ThreadId}" : "not invoked within 5s");

                // Zero pads is an ordinary answer on a machine with none: what is asserted is that
                // ASKING works, because a count is not a pad.
                Check("and the joystick count is answerable, whatever it is",
                    joysticks >= 0, joysticks.ToString());

                // The pump, exercised without a controller. The event goes in through SDL and comes
                // back out of SDL, so what survives the trip is the 56-byte layout, the offset of
                // `which`, and the event number - all three against the binary that is loaded
                // rather than against the header they were written from.
                const int Marker = 0x5AFE;
                bool Push(uint type) =>
                    sdl.Invoke(() => Gamepads.PushEvent(type, Marker), TimeSpan.FromSeconds(5))
                    && SpinWait.SpinUntil(() => seen.Any(e => e.Type == type), TimeSpan.FromSeconds(5));

                // The pump itself, exercised on a machine with no pad. A user event is the one SDL
                // queues without interpreting, so what survives the round trip is the port's own
                // marshalling - the 56-byte union, and `which` at offset 8 - measured against the
                // binary that is loaded rather than against the header it was written from.
                Check("an event pushed through SDL comes back out of the pump intact",
                    Push(Gamepads.UserEvent)
                    && seen.Any(e => e.Type == Gamepads.UserEvent && e.Which == Marker),
                    string.Join(", ", seen.Select(e => $"{e.Type:x}=>{e.Which:x}")));

                // And the finding that costs, measured the same way: SDL owns `which` on every
                // joystick and controller event. A device index it cannot resolve comes back as
                // -1, so a synthesised device event is not a stand-in for a plugged-in pad and
                // the rest of PP8 cannot be tested by inventing one. The type still arrives,
                // which is what makes this a rewritten field rather than a dropped event.
                //
                // It is also what pins the offset, and only as a pair with the check above. Move
                // `which` to 12 and the user event still round-trips - 12 is padding in the device
                // structs, so SDL leaves it alone and every push appears to survive. This is the
                // one that then goes red, because reading padding is not reading `which`.
                Check("but SDL rewrites `which` on device events, so one cannot be faked",
                    Push(Gamepads.EventType.ControllerDeviceAdded)
                    && Push(Gamepads.EventType.ControllerDeviceRemoved)
                    && Push(Gamepads.EventType.JoyDeviceRemoved)
                    && seen.Count(e => e.Which == Marker) == 1
                    && seen.Any(e => e.Type == Gamepads.EventType.ControllerDeviceAdded),
                    string.Join(", ", seen.Select(e => $"{e.Type:x}=>{e.Which:x}")));

                Console.WriteLine($"        SDL {Gamepads.LinkedVersion()}, {joysticks} joystick(s), from {ChiakiNative.SdlLoadedFrom}");
            }

            sdl.Stop(TimeSpan.FromSeconds(10));
            Check("stopping quits SDL and is idempotent",
                !sdl.Running && Gamepads.WasInit(Gamepads.InitGameController) == 0,
                Gamepads.WasInit(Gamepads.InitGameController).ToString());
            sdl.Stop(TimeSpan.FromSeconds(1));

            // The half this code cannot exercise: that the Qt client sets the same four. A pad
            // that behaves differently between the two clients is not something a user would
            // report as a port defect.
            string? cmSource = SanitizerSource.LocateRelative(@"gui\src\controllermanager.cpp");
            if (cmSource is null)
            {
                Console.WriteLine(@"  --    the Qt client's SDL hints  (no gui\src\controllermanager.cpp here)");
            }
            else
            {
                // The C++ names the MACRO, not the string it expands to - SDL_HINT_FOO is "SDL_FOO"
                // - so that is what is looked for. Comparing against the string value found none of
                // them and said the Qt client set no hints at all, which was this check being
                // wrong rather than the client.
                string cm = File.ReadAllText(cmSource);
                var missing = Gamepads.Hints
                    .Where(h => !cm.Contains("SDL_HINT_" + h.Name["SDL_".Length..], StringComparison.Ordinal))
                    .Select(h => h.Name)
                    .ToList();
                Check("the Qt client sets the same four hints",
                    missing.Count == 0, string.Join(", ", missing));
                Check("and the buttons-by-position hint is the one it sets separately",
                    cm.Contains("SDL_HINT_" + Gamepads.ButtonLabelsHint["SDL_".Length..], StringComparison.Ordinal));
            }

            Console.WriteLine();
            Console.WriteLine("PsnAuth - the login, minus the browser");

            // The device id comes from libchiaki rather than from a Guid here: it identifies this
            // installation to the relay, so one of the right shape is not one it recognises.
            string duid = PsnAuth.GenerateDeviceUid();
            Check("a device id is generated and is the declared length",
                PsnAuth.DuidSize == 49 && duid.Length == 48 && duid.All(Uri.IsHexDigit),
                $"{duid.Length}: {duid}");
            Check("and a second one is different",
                PsnAuth.GenerateDeviceUid() != duid);

            string loginUrl = PsnAuth.LoginUrl(duid);
            Check("the login url carries the client id, the redirect and the device",
                loginUrl.Contains("client_id=" + PsnAuth.ClientId, StringComparison.Ordinal)
                && loginUrl.Contains("redirect_uri=" + PsnAuth.RedirectPage, StringComparison.Ordinal)
                && loginUrl.EndsWith("duid=" + duid + "&", StringComparison.Ordinal),
                loginUrl.Length.ToString());

            // The redirect is matched by PREFIX, because it arrives with the query attached -
            // which is the whole point of it. An equality test would never fire.
            Check("the redirect is recognised with its query attached",
                PsnAuth.IsRedirect(PsnAuth.RedirectPage + "?code=ABC123")
                && PsnAuth.IsRedirect(PsnAuth.RedirectPage)
                && !PsnAuth.IsRedirect("https://example.invalid/?code=ABC123")
                && !PsnAuth.IsRedirect(null));

            Check("the code comes out of the redirect",
                PsnAuth.CodeFrom(PsnAuth.RedirectPage + "?code=ABC123") == "ABC123"
                && PsnAuth.CodeFrom(PsnAuth.RedirectPage + "?state=x&code=ABC123&y=2") == "ABC123",
                PsnAuth.CodeFrom(PsnAuth.RedirectPage + "?code=ABC123") ?? "<null>");
            // A redirect with no code is a cancelled login, which is not the same as a page that
            // is not the redirect - both give null here and the caller tells them apart with
            // IsRedirect, exactly as the Qt client does.
            Check("a redirect without a code is null, and so is a page that is not the redirect",
                PsnAuth.CodeFrom(PsnAuth.RedirectPage + "?error=access_denied") is null
                && PsnAuth.CodeFrom(PsnAuth.RedirectPage) is null
                && PsnAuth.CodeFrom("https://example.invalid/?code=ABC") is null);

            Check("the token bodies carry the scope and differ only in the grant",
                PsnAuth.TokenRequestBody("C").StartsWith("grant_type=authorization_code&code=C&scope=", StringComparison.Ordinal)
                && PsnAuth.RefreshRequestBody("R").StartsWith("grant_type=refresh_token&refresh_token=R&scope=", StringComparison.Ordinal)
                && PsnAuth.TokenRequestBody("C").Contains(PsnAuth.Scope, StringComparison.Ordinal));

            // Base64 of "id:secret", which is what the endpoint expects and is trivially wrong if
            // the colon or the order goes.
            Check("the basic header is the id and secret, in that order",
                Encoding.UTF8.GetString(Convert.FromBase64String(PsnAuth.BasicAuthHeader()["Basic ".Length..]))
                    == PsnAuth.ClientId + ":" + PsnAuth.ClientSecret,
                PsnAuth.BasicAuthHeader());

            // And the half this code cannot exercise: the Qt client's own copy of these strings.
            // Two clients that log in differently is not a thing anyone would report as a port
            // defect, so the constants are held against the header they came from.
            string? psnHeader = SanitizerSource.LocateRelative(@"gui\include\psnaccountid.h");
            if (psnHeader is null)
            {
                Console.WriteLine(@"  --    the Qt client's PSN constants  (no gui\include\psnaccountid.h here)");
            }
            else
            {
                string cpp = File.ReadAllText(psnHeader);
                Check("the Qt client uses the same client id, redirect, token url and scope",
                    cpp.Contains(PsnAuth.ClientId, StringComparison.Ordinal)
                    && cpp.Contains(PsnAuth.ClientSecret, StringComparison.Ordinal)
                    && cpp.Contains(PsnAuth.RedirectPage, StringComparison.Ordinal)
                    && cpp.Contains(PsnAuth.TokenUrl, StringComparison.Ordinal)
                    && cpp.Contains(PsnAuth.Scope, StringComparison.Ordinal),
                    "one of the five is not in the header");
            }

            Console.WriteLine();
            Console.WriteLine("TakionMessages - one .proto, two generators, the same bytes");

            // The bang: the message that carries the ECDH key and the two flags a session is
            // refused on. Built with the C# types protoc generated from lib/protobuf/takion.proto,
            // then handed to nanopb - which was generated from the same file and is what the
            // console's protocol is actually spoken with today.
            var bang = new Tkproto.TakionMessage
            {
                Type = Tkproto.TakionMessage.Types.PayloadType.Bang,
                BangPayload = new Tkproto.BangPayload
                {
                    ServerVersion = 9,
                    Token = 0x1337BEEF,
                    EncryptedKeyAccepted = true,
                    VersionAccepted = true,
                    SessionKey = "a-session-key",
                },
            };

            byte[] encoded = bang.ToByteArray();
            DecodedTakionMessage? read = TakionMessages.DecodeWithNanopb(encoded);

            Check("nanopb accepts what the managed generator produced",
                read is not null, $"{encoded.Length} bytes");
            Check("and reads back every scalar unchanged",
                read is { Type: 1, HasBang: true, ServerVersion: 9, Token: 0x1337BEEF,
                    EncryptedKeyAccepted: true, VersionAccepted: true },
                read?.ToString() ?? "<null>");

            // The enum is the field most likely to drift between two generators, because it is the
            // one thing that is a NAME on one side and a number on the other. BANG is 1 in the
            // .proto and has to be 1 on the wire.
            Check("the payload type is the number the .proto assigns",
                (int)Tkproto.TakionMessage.Types.PayloadType.Bang == 1
                && read?.Type == (int)Tkproto.TakionMessage.Types.PayloadType.Bang);

            // A round trip through the managed side too, so the encoder and its own decoder are
            // not the only thing agreeing.
            var reparsed = Tkproto.TakionMessage.Parser.ParseFrom(encoded);
            Check("the managed parser reads its own bytes back",
                reparsed.BangPayload.Token == 0x1337BEEF
                && reparsed.BangPayload.SessionKey == "a-session-key",
                reparsed.BangPayload.SessionKey);

            // proto2 required fields: a message missing one is not a message. Both generators have
            // to refuse it, and the managed one refuses at encode rather than at parse - which is
            // the earlier of the two places and the better one.
            var incomplete = new Tkproto.TakionMessage
            {
                Type = Tkproto.TakionMessage.Types.PayloadType.Bang,
                BangPayload = new Tkproto.BangPayload { ServerVersion = 1 },
            };
            Check("nanopb refuses a bang missing its required fields",
                TakionMessages.DecodeWithNanopb(incomplete.ToByteArray()) is null);

            // The other direction, and the one that reaches the string and bytes fields: nanopb
            // does not store those, it asks a callback to write them as the field goes past. So
            // they cannot be checked by decoding alone - which is why this half exists.
            byte[] pubKey = [0x04, 0xde, 0xad, 0xbe, 0xef];
            byte[] bangSig = [0x13, 0x37, 0x42];
            byte[]? fromNanopb = TakionMessages.EncodeBangWithNanopb(
                9, 0x1337BEEF, true, true, "a-session-key", pubKey, bangSig);

            Check("nanopb encodes a bang", fromNanopb is not null, $"{fromNanopb?.Length} bytes");

            var readBack = Tkproto.TakionMessage.Parser.ParseFrom(fromNanopb);
            Check("the managed generator reads back what nanopb wrote",
                readBack.Type == Tkproto.TakionMessage.Types.PayloadType.Bang
                && readBack.BangPayload.ServerVersion == 9
                && readBack.BangPayload.Token == 0x1337BEEF
                && readBack.BangPayload.SessionKey == "a-session-key",
                readBack.BangPayload.SessionKey);
            // The callback fields, which is the whole point of this direction.
            Check("the bytes fields survive nanopb's callbacks",
                readBack.BangPayload.EcdhPubKey.ToByteArray().SequenceEqual(pubKey)
                && readBack.BangPayload.EcdhSig.ToByteArray().SequenceEqual(bangSig),
                Convert.ToHexString(readBack.BangPayload.EcdhPubKey.ToByteArray()));

            // And the round trip closes: what nanopb wrote, re-encoded by the managed generator,
            // is what nanopb reads back the same way. Two encoders and two decoders agreeing on
            // one message is the whole claim PP25 makes.
            Check("the loop closes through both generators",
                TakionMessages.DecodeWithNanopb(readBack.ToByteArray())
                    is { Type: 1, ServerVersion: 9, Token: 0x1337BEEF, EncryptedKeyAccepted: true });

            Console.WriteLine();
            Console.WriteLine("FrameTiming - when a decoded frame is due");

            long noPts = FrameTiming.NoPts;
            Check("the absent-timestamp sentinel is ffmpeg's own",
                noPts == long.MinValue, noPts.ToString());

            // The ordinary case: the best-effort timestamp against the packet's timebase, and the
            // duration from the framerate.
            (double p, double d) = FrameTiming.Of(12345, noPts, (1, 90000), (0, 0), (60, 1));
            Check("a best-effort timestamp is scaled by the packet timebase",
                Math.Abs(p - 12345.0 / 90000.0) < 1e-9 && Math.Abs(d - 1.0 / 60.0) < 1e-9,
                $"{p} / {d}");

            // Fallback one: no best-effort timestamp, so the raw pts is used instead. A stream
            // that carries one and not the other is what this exists for.
            (double p2, _) = FrameTiming.Of(noPts, 9000, (1, 90000), (0, 0), (60, 1));
            Check("an absent best-effort timestamp falls back to the raw one",
                Math.Abs(p2 - 0.1) < 1e-9, p2.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // …and with neither, there is no time to report at all.
            (double p3, _) = FrameTiming.Of(noPts, noPts, (1, 90000), (0, 0), (60, 1));
            Check("with neither timestamp the frame has no presentation time",
                p3 == 0.0, p3.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Fallback two: an invalid packet timebase means the decoder context's is used. The
            // two are different scales, so taking the wrong one is not a rounding error - it is
            // the whole clock.
            (double p4, _) = FrameTiming.Of(12345, noPts, (0, 0), (1, 1000), (60, 1));
            Check("an invalid packet timebase falls back to the context's",
                Math.Abs(p4 - 12.345) < 1e-9, p4.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Fallback three: the framerate decides the duration, and a stream that reports none
            // gets a default rather than a division by zero.
            (_, double d2) = FrameTiming.Of(12345, noPts, (1, 90000), (0, 0), (120, 1));
            (_, double d3) = FrameTiming.Of(12345, noPts, (1, 90000), (0, 0), (0, 0));
            Check("the framerate sets the duration, and its absence does not divide by zero",
                Math.Abs(d2 - 1.0 / 120.0) < 1e-9 && d3 > 0.0 && !double.IsInfinity(d3),
                $"{d2} / {d3}");

            Console.WriteLine();
            Console.WriteLine("Regist - the first thing a fresh install sends");

            string? registFile = SanitizerSource.LocateRelative(@"test\regist.c");
            if (registFile is null)
            {
                Console.WriteLine(@"  --    the recorded registration payload  (no test\regist.c here)");
            }
            else
            {
                // The ambassador is declared once at file scope and every case is derived from it,
                // so it is read from the file rather than from a function - and the pin and the id
                // are cited as literals rather than copied, for the same reason the bytes are.
                IReadOnlyDictionary<string, byte[]> reg = CryptoVectors.InFile(registFile);
                string? pinText = CryptoVectors.ScalarInFile(registFile, "pin");
                string? idText = CryptoVectors.ScalarInFile(registFile, "psn_id");

                Check("the shared vectors and the scalars are readable",
                    reg.ContainsKey("ambassador") && pinText == "13374201"
                    && idText == "\"ChiakiNanami1337\"",
                    $"{pinText} / {idText}");

                uint pin = uint.Parse(pinText!, System.Globalization.CultureInfo.InvariantCulture);
                string psnId = idText!.Trim('"');
                byte[] ambassador = reg["ambassador"];

                // Each case declares its own `expected`, so every one of these is read from ITS
                // function. A file-wide lookup would answer with whichever came last, which is how
                // this assertion first failed while computing the right bytes.
                byte[] aeropauseExpected =
                    CryptoVectors.InFunction(registFile, "test_aeropause_ps4_pre10")["expected"];
                Check("the aeropause is the recorded one",
                    RpCrypt.AeropausePs4Pre10(ambassador).SequenceEqual(aeropauseExpected),
                    Convert.ToHexString(RpCrypt.AeropausePs4Pre10(ambassador)));

                byte[] brightExpected =
                    CryptoVectors.InFunction(registFile, "test_pin_bright_ps4_pre10")["expected"];
                Check("the PIN derives the recorded bright key",
                    RpCrypt.RegistBrightPs4Pre10(ambassador, pin).SequenceEqual(brightExpected),
                    Convert.ToHexString(RpCrypt.RegistBrightPs4Pre10(ambassador, pin)));

                IReadOnlyDictionary<string, byte[]> payloadCase =
                    CryptoVectors.InFunction(registFile, "test_request_payload_ps4_pre10");

                byte[] payload = RpCrypt.RegistRequestPayload(
                    ChiakiTarget.Ps4_9, ambassador, psnId, pin);

                Check("the whole registration request is the recorded payload",
                    payload.SequenceEqual(payloadCase["expected"]),
                    $"{payload.Length} vs {payloadCase["expected"].Length} bytes");

                // The PIN is what a user types off the console's screen, and it keys the payload.
                // A port that dropped it would produce a request of the right shape that no
                // console accepts - which is the failure with no symptom this vector exists for.
                Check("a different PIN produces a different request",
                    !RpCrypt.RegistRequestPayload(ChiakiTarget.Ps4_9, ambassador, psnId, pin + 1)
                        .SequenceEqual(payload));
            }

            Console.WriteLine();
            Console.WriteLine("Alloc budget - what a packet costs on this side of the seam");

            // PP44 measured the C transport and found it allocates ZERO bytes and makes zero
            // allocator calls per packet in steady state: the buffers are sized once from the
            // frame's own header and reused. So the budget the managed side inherits is not
            // "allocate little" but "allocate nothing", because that is what the code being
            // replaced does - and a transport that allocates per packet turns thousands of small
            // packets a second into a collection whose cost is the worst frame of a minute.
            string? takionFileForBudget = SanitizerSource.LocateRelative(@"test\takion.c");
            string? bsFileForBudget = SanitizerSource.LocateRelative(@"test\bitstream.c");

            using (var budgetLog = new ChiakiLog(ChiakiLogLevel.Error, (_, _) => { }))
            using (var fp = new FrameProcessor(budgetLog))
            {
                var unitBuf = new byte[2 + 32];
                var frameBuf = new byte[4096];

                // Warm up: the first frame sizes the buffers, on both sides of the seam. Steady
                // state is what the budget is about, so it is measured after this.
                for (ushort f = 1; f <= 2; f++)
                {
                    fp.AllocFrame(f, 0, 2, 0, unitBuf);
                    fp.PutUnit(f, 0, 2, 0, (ReadOnlySpan<byte>)unitBuf);
                    fp.PutUnit(f, 1, 2, 0, (ReadOnlySpan<byte>)unitBuf);
                    fp.FlushInto(frameBuf, out _);
                }

                const int packets = 200;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (ushort f = 3; f < 3 + packets / 2; f++)
                {
                    fp.AllocFrame(f, 0, 2, 0, unitBuf);
                    fp.PutUnit(f, 0, 2, 0, (ReadOnlySpan<byte>)unitBuf);
                    fp.PutUnit(f, 1, 2, 0, (ReadOnlySpan<byte>)unitBuf);
                    fp.FlushInto(frameBuf, out _);
                }
                long perPacket = (GC.GetAllocatedBytesForCurrentThread() - before) / packets;

                Check("the span path costs nothing per packet, as the C path does",
                    perPacket == 0, $"{perPacket} bytes/packet");

                // And the convenience path, measured rather than assumed: Flush() hands back a
                // fresh array every time. It is the right shape for a test and the wrong one for
                // a stream, and the number is here so that choosing it is a decision.
                before = GC.GetAllocatedBytesForCurrentThread();
                for (ushort f = 200; f < 210; f++)
                {
                    fp.AllocFrame(f, 0, 2, 0, unitBuf);
                    fp.PutUnit(f, 0, 2, 0, unitBuf);
                    fp.PutUnit(f, 1, 2, 0, unitBuf);
                    fp.Flush(4096);
                }
                long convenience = (GC.GetAllocatedBytesForCurrentThread() - before) / 20;

                Check("the array path is the one that costs, which is why both exist",
                    convenience > 0, $"{convenience} bytes/packet");

                // Printed as well as asserted: the budget is a number that has to be readable to
                // be argued with, and "zero against N" is the sentence a reviewer needs.
                Console.WriteLine($"        span path {perPacket} B/packet, array path {convenience} B/packet");
            }

            // The rest of the per-packet path, held to the same number. These are the two calls a
            // real stream makes for every datagram that arrives, so they are where the budget
            // either holds or does not.
            if (takionFileForBudget is not null && bsFileForBudget is not null)
            {
                byte[] recorded = CryptoVectors.InFunction(takionFileForBudget, "test_av_packet_parse")["packet"];
                byte[] profileHdr = CryptoVectors.InFunction(bsFileForBudget, "test_bitstream_parse_h264")["header"];

                using var keys = new KeyState();
                var packetBuf = new byte[recorded.Length];

                // Warmed up first, and re-copied each turn because the parse works IN PLACE: the
                // Span overload says so in its type, and a second parse of a buffer the first one
                // rewrote would not be measuring the same work.
                for (int i = 0; i < 4; i++)
                {
                    recorded.CopyTo(packetBuf, 0);
                    Takion.ParseV9(keys, packetBuf.AsSpan(), out _);
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 200; i++)
                {
                    recorded.CopyTo(packetBuf, 0);
                    Takion.ParseV9(keys, packetBuf.AsSpan(), out _);
                }
                long parseCost = (GC.GetAllocatedBytesForCurrentThread() - before) / 200;

                Check("parsing a packet header costs nothing", parseCost == 0, $"{parseCost} B/packet");

                // The video receiver is deliberately NOT measured in a loop here. Driving it with
                // a second frame index makes it report a corrupt frame into the stream connection,
                // and the session this harness synthesises has none - so the report reaches zeroed
                // memory and the host aborts. test/videoreceiver.c says the same thing in one
                // line: "frame index 1 is the one index that skips the corrupt-frame report".
                //
                // Found by writing that loop and watching the process die rather than fail. The
                // constraint is now stated on VideoReceiver itself; what the delivery costs per
                // packet is the same span pin measured above, and claiming a number for it from a
                // harness that cannot run the loop would be inventing one.
                Console.WriteLine($"        parse {parseCost} B/packet");

                byte[] slicePayload = [0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x80, 0x82, 0x1f, 0x00];
                var unitBuf2 = new byte[2 + slicePayload.Length];
                slicePayload.CopyTo(unitBuf2, 2);

                var delivered = new List<int>();
                using (var vr = new VideoReceiver(
                    (f, _, _) => { delivered.Add(f.Length); return true; },
                    ChiakiNg.Session.ChiakiCodec.H264))
                {
                    vr.StreamInfo(profileHdr, 1920, 1080);

                    before = GC.GetAllocatedBytesForCurrentThread();
                    vr.AvPacket(1, 0, 1, 0, (ReadOnlySpan<byte>)unitBuf2);
                    long oneFrame = GC.GetAllocatedBytesForCurrentThread() - before;

                    // What IS asserted: the span overload delivers, which is the functional half.
                    Check("the span overload delivers a frame like the array one",
                        delivered.Count == 2 && delivered[^1] == slicePayload.Length,
                        string.Join(",", delivered));

                    // What is NOT asserted: a per-packet cost. This is the first frame this
                    // receiver has ever seen, so the figure carries the JIT of the thunk and the
                    // processor sizing its buffers, and the loop that would warm those away is the
                    // one that aborts. Printed rather than asserted, because a budget claimed from
                    // a single first call would be a number invented rather than measured.
                    Console.WriteLine($"        first frame through the receiver {oneFrame} B "
                        + "(first call, not a steady-state figure)");
                }
            }

            Console.WriteLine();
            Console.WriteLine("VideoReceiver - who owns the buffer a frame arrives in");

            string? bsFileForVideo = SanitizerSource.LocateRelative(@"test\bitstream.c");
            if (bsFileForVideo is null)
            {
                Console.WriteLine(@"  --    the video sample callback  (no test\bitstream.c here)");
            }
            else
            {
                // The profile header has to be one the bitstream parser accepts, so it is the real
                // SPS and PPS out of test/bitstream.c. A hand-written approximation is worse than
                // useless: a truncated SPS walks the RBSP reader off the end, and that costs a
                // hang rather than a refusal.
                byte[] profileHeader =
                    CryptoVectors.InFunction(bsFileForVideo, "test_bitstream_parse_h264")["header"];

                var frames = new List<byte[]>();
                var order = new List<string>();
                int lastFramesLost = -1;

                bool Take(ReadOnlySpan<byte> frame, int framesLost, bool recovered)
                {
                    // The profile header goes out through this same callback, so it is identified
                    // by its contents rather than by its position - counting it as a frame would
                    // make everything below pass for a wrong reason.
                    if (frame.SequenceEqual(profileHeader))
                    {
                        order.Add("header");
                        return true;
                    }

                    lastFramesLost = framesLost;
                    frames.Add(frame.ToArray());
                    order.Add("frame");
                    return true;
                }

                // A whole frame in one unit: total of 1 makes this unit the last one, which is
                // what makes the receiver flush inside the call rather than on the next frame.
                byte[] payload = [0x00, 0x00, 0x00, 0x01, 0x65, 0x88, 0x80, 0x82, 0x1f, 0x00];
                var unit = new byte[2 + payload.Length];
                payload.CopyTo(unit, 2);

                using (var vr = new VideoReceiver(Take, ChiakiNg.Session.ChiakiCodec.H264))
                {
                    Check("the stream info is accepted", vr.StreamInfo(profileHeader, 1920, 1080));
                    // Nothing is delivered yet. The header does not go out when the profiles are
                    // registered - it goes out when a packet SELECTS one, which is a switch and
                    // not a setup step. A client that expected it at stream info would draw a
                    // black window until the first packet arrived.
                    Check("registering the profiles delivers nothing on its own",
                        order.Count == 0, string.Join(",", order));

                    vr.AvPacket(1, 0, 1, 0, unit);

                    // PP87, answered. The buffer is libchiaki's, lent for the call, and the
                    // handler read it in place - the copy above is the handler's choice, which is
                    // exactly the ownership rule this was filed to establish.
                    Check("a whole frame reaches a managed handler",
                        frames.Count == 1 && frames[0].SequenceEqual(payload),
                        frames.Count == 0 ? "none" : Convert.ToHexString(frames[0]));
                    Check("and it arrived with no losses to report",
                        lastFramesLost == 0, lastFramesLost.ToString());
                    // The header comes out first, inside the same call: the profile switch runs
                    // before the frame it switched for is flushed. A decoder that took them in
                    // the other order would be given a frame it has no parameter sets for.
                    Check("the first packet delivers the header and then its frame",
                        order.SequenceEqual(["header", "frame"]), string.Join(",", order));
                }

                // Returning false is how a client says it could not take the frame. The receiver
                // treats that as a corrupt frame, which is what asks the console for a keyframe.
                var refused = new List<int>();
                using (var vr = new VideoReceiver(
                    (f, _, _) => { refused.Add(f.Length); return false; },
                    ChiakiNg.Session.ChiakiCodec.H264))
                {
                    vr.StreamInfo(profileHeader, 1920, 1080);
                    vr.AvPacket(1, 0, 1, 0, unit);
                    Check("a handler that declines a frame is still handed it",
                        refused.Count >= 1, string.Join(",", refused));
                }
            }

            Console.WriteLine();
            Console.WriteLine("FrameProcessor - where units become a frame");

            // The unit shape the C suite synthesises: two bytes of size extension, left at zero,
            // then a payload that identifies which unit it is.
            static byte[] Unit(int index)
            {
                var u = new byte[2 + 32];
                for (int i = 0; i < 32; i++)
                    u[2 + i] = (byte)(index * 0x10 + i);
                return u;
            }

            using (var fp = new FrameProcessor())
            {
                // Three units, one of them parity: two source units make a whole frame.
                Check("a fresh processor has charged nothing",
                    fp.StageSamples(FrameStage.Reassemble) == 0
                    && fp.StageSamples(FrameStage.Correct) == 0);

                Check("the frame is sized from its first unit",
                    fp.AllocFrame(1, 0, 3, 1, Unit(0)) == ChiakiError.Success
                    && fp.PutUnit(1, 0, 3, 1, Unit(0)) == ChiakiError.Success
                    && fp.PutUnit(1, 1, 3, 1, Unit(1)) == ChiakiError.Success);

                Check("both source units in means a flush needs no reconstruction",
                    fp.FlushPossible);

                (FrameFlushResult result, byte[] frame) = fp.Flush();
                Check("a whole frame flushes as a plain success",
                    result == FrameFlushResult.Success && frame.Length == 64,
                    $"{result}, {frame.Length} bytes");

                // The accounting the stage timings rest on: a frame that never lost a unit must
                // not appear in the correct stage, or the baseline would report a reconstruction
                // cost that no lossy minute really pays.
                Check("reassembly is charged once and correction not at all",
                    fp.StageSamples(FrameStage.Reassemble) == 1
                    && fp.StageSamples(FrameStage.Correct) == 0,
                    $"{fp.StageSamples(FrameStage.Reassemble)}/{fp.StageSamples(FrameStage.Correct)}");

                // The receiver flushes the same frame again when the next frame's head arrives
                // first. That is one reassembly, not two.
                fp.Flush();
                Check("flushing the same frame twice is still one reassembly",
                    fp.StageSamples(FrameStage.Reassemble) == 1);
            }

            using (var fp = new FrameProcessor())
            {
                // A frame missing a source unit, with the parity unit standing in for it. This is
                // where the FEC already across this seam actually gets driven.
                fp.AllocFrame(1, 0, 3, 1, Unit(0));
                fp.PutUnit(1, 0, 3, 1, Unit(0));
                fp.PutUnit(1, 2, 3, 1, Unit(2));

                (FrameFlushResult result, byte[] frame) = fp.Flush();
                Check("a missing source unit is reconstructed from the parity one",
                    result == FrameFlushResult.FecSuccess && frame.Length == 64,
                    $"{result}, {frame.Length} bytes");
                Check("and the correction is charged exactly once",
                    fp.StageSamples(FrameStage.Correct) == 1
                    && fp.StageSamples(FrameStage.Reassemble) == 1,
                    $"{fp.StageSamples(FrameStage.Reassemble)}/{fp.StageSamples(FrameStage.Correct)}");
            }

            // The failure case gets a log of its own, because libchiaki's default writes "FEC
            // failed" to stdout in red - and a passing run of this suite should not look like one
            // that broke. PP83's log is what makes that a capture rather than a redirect.
            var fecComplaints = new List<string>();
            using (var quietLog = new ChiakiLog(ChiakiLogLevel.All, (_, text) => fecComplaints.Add(text)))
            using (var fp = new FrameProcessor(quietLog))
            {
                // Two units missing with one parity unit: nothing can be recovered, and the
                // processor says so rather than handing back a frame with a hole in it.
                fp.AllocFrame(1, 0, 4, 1, Unit(0));
                fp.PutUnit(1, 0, 4, 1, Unit(0));

                Check("too few units in is not worth a flush", !fp.FlushPossible);
                Check("and flushing anyway does not claim success",
                    fp.Flush().Result is FrameFlushResult.FecFailed or FrameFlushResult.Failed,
                    fp.Flush().Result.ToString());
                Check("and the library said so through the log rather than to stdout",
                    fecComplaints.Any(m => m.Contains("FEC", StringComparison.Ordinal)),
                    string.Join(" | ", fecComplaints));
            }

            Console.WriteLine();
            Console.WriteLine("Takion - the key position, and a real packet's header");

            using (var keys = new KeyState())
            {
                // The C suite's own ladder. The step that matters is the fourth: 0x1337 arriving
                // after 0xffff0000 is 0x1_00001337, because the low half wrapped and the high half
                // went up. A reader that took the wire's 32 bits as the whole number would key the
                // stream four billion bytes back and decrypt noise from there on.
                Check("a position below the last one is a wrap and not a step backwards",
                    keys.RequestPos(0) == 0
                    && keys.RequestPos(0x1337) == 0x1337
                    && keys.RequestPos(0xffff0000) == 0xffff0000
                    && keys.RequestPos(0x1337) == 0x1_00001337,
                    keys.RequestPos(0x1337).ToString("x"));

                // …and it is the NEAREST candidate, not the next one. From 0x1_00001337, a wire
                // value of 0xffff1337 could mean 0x0_ffff1337 (just behind) or 0x1_ffff1337 (far
                // ahead); it takes the near one. So the high half can go DOWN again, which is the
                // half of this that "remember the high bits and increment on wrap" does not
                // describe - and a reorder that arrives a packet late is exactly that case.
                Check("the nearest candidate wins, even when it is behind",
                    keys.RequestPos(0xffff1337) == 0xffff1337,
                    keys.RequestPos(0xffff1337).ToString("x"));

                Check("and the ladder keeps climbing from there",
                    keys.RequestPos(0x50000000) == 0x1_50000000
                    && keys.RequestPos(0xb0000000) == 0x1_b0000000
                    && keys.RequestPos(0x00000000) == 0x2_00000000,
                    keys.RequestPos(0x00000000).ToString("x"));
            }

            using (var keys = new KeyState())
            {
                // Without commit the state does not move, which is what lets a packet be parsed
                // before it is known to be genuine: a corrupt one asks and is thrown away without
                // having dragged the counter forward with it.
                Check("an uncommitted request answers without advancing",
                    keys.RequestPos(0xffff0000, commit: false) == 0xffff0000
                    && keys.RequestPos(0x1337, commit: false) == 0x1337,
                    keys.RequestPos(0x1337, commit: false).ToString("x"));
            }

            string? takionFile = SanitizerSource.LocateRelative(@"test\takion.c");
            if (takionFile is null)
            {
                Console.WriteLine(@"  --    the recorded AV packet  (no test\takion.c here)");
            }
            else
            {
                IReadOnlyDictionary<string, byte[]> av =
                    CryptoVectors.InFunction(takionFile, "test_av_packet_parse");
                using var keys = new KeyState();
                AvPacket? parsed = Takion.ParseV9(keys, av["packet"], out ChiakiError avErr);

                Check("a recorded video packet's header parses",
                    parsed is not null && avErr == ChiakiError.Success, avErr.ToString());
                Check("every field is the one the C suite records",
                    parsed is { IsVideo: true, PacketIndex: 45, FrameIndex: 5, UnitIndex: 6,
                        UnitsInFrameTotal: 8, UnitsInFrameFec: 1, Codec: 3, AdaptiveStreamIndex: 0 },
                    parsed?.ToString() ?? "<null>");
                // The payload is named by where it sits rather than by a pointer: 0x15 in, 0x99
                // long. That is the same ownership rule as the discovery reply, taken further -
                // the buffer is already the caller's, so an offset costs no lifetime at all.
                Check("the payload is an offset into the buffer the caller already has",
                    parsed is { DataOffset: 0x15, DataSize: 0x99 },
                    $"{parsed?.DataOffset:x}/{parsed?.DataSize:x}");
            }

            Console.WriteLine();
            Console.WriteLine("Bitstream - what kind of frame just arrived");

            string? bsFile = SanitizerSource.LocateRelative(@"test\bitstream.c");
            if (bsFile is null)
            {
                Console.WriteLine(@"  --    the recorded slice headers  (no test\bitstream.c here)");
            }
            else
            {
                // The same reader the crypto vectors use: these are real headers and slices off a
                // stream, and they stay in the C file so both suites cite one copy.
                IReadOnlyDictionary<string, byte[]> h264 =
                    CryptoVectors.InFunction(bsFile, "test_bitstream_parse_h264");
                IReadOnlyDictionary<string, byte[]> h265 =
                    CryptoVectors.InFunction(bsFile, "test_bitstream_parse_h265");

                Check("the recorded headers and slices are readable",
                    h264.Count == 4 && h265.Count == 4,
                    $"{h264.Count}/{h265.Count}");

                using (var bs = new Bitstream(ChiakiNg.Session.ChiakiCodec.H264))
                {
                    Check("an H.264 stream's parameter sets parse", bs.ReadHeader(h264["header"]));
                    Check("an I slice is an I slice",
                        bs.ReadSlice(h264["slice_i"]) is (BitstreamSliceType.I, _));
                    // A P slice carries which frame it depends on, and that number is what a
                    // reference rewrite later changes.
                    Check("a P slice names the frame it references",
                        bs.ReadSlice(h264["slice_p"]) == (BitstreamSliceType.P, 0u)
                        && bs.ReadSlice(h264["slice_p_ref_5"]) == (BitstreamSliceType.P, 5u),
                        bs.ReadSlice(h264["slice_p_ref_5"])?.ToString() ?? "<null>");
                }

                using (var bs = new Bitstream(ChiakiNg.Session.ChiakiCodec.H265))
                {
                    Check("an H.265 stream parses the same three ways",
                        bs.ReadHeader(h265["header"])
                        && bs.ReadSlice(h265["slice_i"]) is (BitstreamSliceType.I, _)
                        && bs.ReadSlice(h265["slice_p"]) == (BitstreamSliceType.P, 0u)
                        && bs.ReadSlice(h265["slice_p_ref_5"]) == (BitstreamSliceType.P, 5u));
                }

                // The regression the upstream issue number is attached to, cited by name so a
                // change that reopens it says which bug it is.
                IReadOnlyDictionary<string, byte[]> issue213 =
                    CryptoVectors.InFunction(bsFile, "test_bitstream_issue_213");
                using (var bs = new Bitstream(ChiakiNg.Session.ChiakiCodec.H265))
                {
                    Check("issue 213's slice still reads as a P frame referencing 0",
                        bs.ReadHeader(issue213["header"])
                        && bs.ReadSlice(issue213["slice_p"]) == (BitstreamSliceType.P, 0u),
                        bs.ReadSlice(issue213["slice_p"])?.ToString() ?? "<null>");
                }

                // Rewriting a reference is what lets a frame survive the loss of the one it
                // depended on, and it edits the caller's bytes rather than answering with new
                // ones. The vectors are the C suite's own set-ref case: a slice from an arbitrary
                // stream is not necessarily rewritable, which is what the refusal below is about.
                IReadOnlyDictionary<string, byte[]> setRef =
                    CryptoVectors.InFunction(bsFile, "test_bitstream_set_ref_h265");
                using (var bs = new Bitstream(ChiakiNg.Session.ChiakiCodec.H265))
                {
                    Check("the set-ref case's header parses", bs.ReadHeader(setRef["header"]));

                    byte[] slice = (byte[])setRef["slice_p"].Clone();
                    bool allNine = true;
                    for (uint i = 0; i < 9; i++)
                    {
                        if (!bs.SetReferenceFrame(slice, i) || bs.ReadSlice(slice) != (BitstreamSliceType.P, i))
                            allNine = false;
                    }

                    Check("every one of the nine reference frames can be written and read back",
                        allNine && !slice.SequenceEqual(setRef["slice_p"]),
                        bs.ReadSlice(slice)?.ToString() ?? "<null>");

                    // The slice has nine reference frames, so a tenth is refused rather than
                    // written into whatever bits happen to follow.
                    Check("a tenth reference frame is refused", !bs.SetReferenceFrame(slice, 10));
                }
            }

            Console.WriteLine();
            Console.WriteLine("HttpResponse - two implementations, one set of bytes");

            // PP33's first piece, and the first time PP23's harness has two implementations to
            // compare rather than one to call. Every input below goes through the managed parser
            // AND libchiaki's, and the answers must agree header for header, in order.
            string[] httpCases =
            [
                // The two the C suite records, one CRLF and one LF.
                "HTTP/1.1 200 OK\r\nContent-type: text/html, text, plain\r\nUltimate Ability: Gamer\r\n\r\n",
                "HTTP/1.1 200 Ok\nContent-type: text/html, text, plain\nUltimate Ability:Gamer\n",
                // A discovery reply, which is the shape this parser actually meets.
                "HTTP/1.1 620 Server Standby\nhost-id:0011223344556677\nhost-type:PS5\n",
                // One space is skipped, not a run of them - so the second space survives.
                "HTTP/1.1 200 Ok\nKey:  two spaces\n",
                // A header with no trailing line ending is lost, silently, by both.
                "HTTP/1.1 200 Ok\nFirst:one\nSecond:two",
                // Blank lines between headers are skipped rather than ending the parse.
                "HTTP/1.1 200 Ok\n\nA:1\n\n\nB:2\n",
                // And the refusals: no colon, empty key, empty value, not a response at all.
                "HTTP/1.1 200 Ok\ngarbage\n",
                "HTTP/1.1 200 Ok\n:novalue\n",
                "HTTP/1.1 200 Ok\nKey:\n",
                "HTTP/1.1 zero\nA:1\n",
                "hello\n",
                "HTTP/1.1 200 Ok\n",
            ];

            int agreed = 0;
            var disagreements = new List<string>();
            foreach (string c in httpCases)
            {
                var mine = HttpResponse.Parse(c);
                var theirs = NativeHttp.Parse(c);

                bool same = (mine is null) == (theirs is null)
                    && (mine is null
                        || (mine.Value.Code == theirs!.Value.Code
                            && mine.Value.Headers.SequenceEqual(theirs.Value.Headers)));

                if (same)
                    agreed++;
                else
                    disagreements.Add(Describe(c, mine, theirs));
            }

            Check("the managed parser agrees with the one it replaces, on every case",
                agreed == httpCases.Length,
                disagreements.Count == 0 ? "" : disagreements[0]);

            // The specific behaviours worth naming, so a future change that breaks one of them
            // fails with a sentence rather than as "case 4 disagrees".
            var crlf = HttpResponse.Parse(httpCases[0]);
            Check("headers come out in reverse, because libchiaki prepends them",
                crlf?.Headers.Count == 2
                && crlf.Value.Headers[0] == new HttpHeader("Ultimate Ability", "Gamer")
                && crlf.Value.Headers[1] == new HttpHeader("Content-type", "text/html, text, plain"),
                crlf is null ? "<null>" : string.Join(" | ", crlf.Value.Headers));
            Check("620 parses as a code like any other",
                HttpResponse.Parse(httpCases[2])?.Code == 620);
            Check("exactly one space is skipped after the colon",
                HttpResponse.Parse(httpCases[3])?.Headers[0].Value == " two spaces",
                $"[{HttpResponse.Parse(httpCases[3])?.Headers[0].Value}]");
            // The trap: a last header with no line ending is dropped with no error at all.
            Check("a header with no trailing newline is lost, silently, by both",
                HttpResponse.Parse(httpCases[4])?.Headers.Count == 1
                && NativeHttp.Parse(httpCases[4])?.Headers.Count == 1,
                HttpResponse.Parse(httpCases[4])?.Headers.Count.ToString() ?? "<null>");

            static string Describe(
                string input,
                (int Code, IReadOnlyList<HttpHeader> Headers)? a,
                (int Code, IReadOnlyList<HttpHeader> Headers)? b)
            {
                static string One((int Code, IReadOnlyList<HttpHeader> Headers)? r)
                    => r is null ? "refused" : $"{r.Value.Code} [{string.Join(", ", r.Value.Headers)}]";

                return $"{input.Replace("\r", "\\r").Replace("\n", "\\n")}: managed {One(a)} vs native {One(b)}";
            }

            Console.WriteLine();
            Console.WriteLine("SeqNum - the comparison that survives the counter turning over");

            // The C suite sweeps all 65536 values twice; so does this, because it costs
            // milliseconds and it is the only way to say the wrap is handled EVERYWHERE rather
            // than at the one boundary somebody thought to test.
            bool adjacentHolds = true;
            bool distantHolds = true;
            ushort n = 0;
            do
            {
                ushort next = (ushort)(n + 1);
                if (!SeqNum.Gt(next, n) || SeqNum.Gt(n, next) || !SeqNum.Lt(n, next) || SeqNum.Lt(next, n))
                    adjacentHolds = false;

                ushort far = (ushort)(n + 0xfff);
                if (!SeqNum.Gt(far, n) || SeqNum.Gt(n, far) || !SeqNum.Lt(n, far) || SeqNum.Lt(far, n))
                    distantHolds = false;

                n++;
            }
            while (n != 0);

            Check("every one of 65536 successors is newer than its predecessor", adjacentHolds);
            Check("and so is every value 0xfff ahead, all the way round", distantHolds);

            // The case the whole thing exists for: 1 is newer than 0xfff5, even though the
            // integer is smaller.
            Check("1 is newer than 0xfff5, which a plain comparison denies",
                SeqNum.Gt((ushort)1, (ushort)0xfff5) && !SeqNum.Gt((ushort)0xfff5, (ushort)1));
            Check("32-bit numbers wrap the same way",
                SeqNum.Gt(1u, 0xfffffff5u) && !SeqNum.Gt(0xfffffff5u, 1u)
                && SeqNum.Lt(0u, 1u) && !SeqNum.Lt(1u, 0u));

            // Equality is neither, which is what stops a duplicate being treated as progress.
            Check("a number is neither newer nor older than itself",
                !SeqNum.Gt((ushort)42, (ushort)42) && !SeqNum.Lt((ushort)42, (ushort)42)
                && !SeqNum.Gt(42u, 42u) && !SeqNum.Lt(42u, 42u));

            // And the measurement that says the function is load-bearing rather than decorative:
            // count how far a plain integer comparison would diverge over one sweep. If this ever
            // came out zero, the wrap logic would not be doing anything.
            int naiveDisagreements = 0;
            for (int i = 0; i < 65536; i++)
            {
                var a = (ushort)i;
                var b = (ushort)(i + 0x9000);
                if (SeqNum.Gt(a, b) != a > b)
                    naiveDisagreements++;
            }
            Check("a plain comparison disagrees on tens of thousands of pairs",
                naiveDisagreements > 20000, $"{naiveDisagreements} of 65536");

            Console.WriteLine();
            Console.WriteLine("ReorderQueue - four ways to drop a packet");

            using (var queue = new ReorderQueue(2, 42))
            {
                queue.DropStrategy = ReorderDropStrategy.End;
                Check("a window of 2^2 holds four and starts empty",
                    queue is { Size: 4, Count: 0 }, $"{queue.Size}/{queue.Count}");

                // Out of order in, in order out. Nothing comes out until the head arrives, which
                // is the whole point of the structure.
                queue.Push(44, 3);
                queue.Push(43, 2);
                // Count is the window's SPAN and not its population: begin is 42 and the highest
                // set slot is 44, so it reads 3 with two elements in it. A rewrite that made it a
                // population would be right on every full window and wrong on every gap - which is
                // every window that has a packet outstanding, which is what the queue is for.
                Check("nothing comes out while the head is missing",
                    queue.Pull() is null && queue.Count == 3, queue.Count.ToString());

                queue.Push(42, 1);
                Check("the head arriving releases the run in order",
                    queue.Pull() == (42ul, 1L) && queue.Pull() == (43ul, 2L)
                    && queue.Pull() == (44ul, 3L) && queue.Pull() is null,
                    queue.Count.ToString());

                // Drop one: a sequence number BEHIND the window. The queue has moved past 42, so
                // it can never be delivered and is handed straight to the drop callback.
                queue.Push(42, 99);
                Check("a packet older than the window is dropped, not queued",
                    queue.Drops.Count == 1 && queue.Drops[^1] == new ReorderDrop(42, 99)
                    && queue.Count == 0,
                    queue.Drops.Count.ToString());

                // Drop two: a duplicate of something already sitting in the window.
                queue.Push(46, 10);
                queue.Push(46, 11);
                Check("a duplicate is dropped and the first one stays",
                    queue.Drops.Count == 2 && queue.Drops[^1] == new ReorderDrop(46, 11)
                    && queue.Peek(1) == (46ul, 10L),
                    queue.Drops[^1].ToString());
            }

            // Drop three and four: overflow, from whichever end the strategy names. Same pushes,
            // opposite victims - which is the assertion that says the strategy is read.
            using (var endFirst = new ReorderQueue(1, 0))
            using (var beginFirst = new ReorderQueue(1, 0))
            {
                endFirst.DropStrategy = ReorderDropStrategy.End;
                beginFirst.DropStrategy = ReorderDropStrategy.Begin;

                // A window of two, and a third sequence number beyond it.
                foreach (ReorderQueue q in new[] { endFirst, beginFirst })
                {
                    q.Push(1, 1);
                    q.Push(5, 5);
                }

                Check("overflowing at the end drops the newest",
                    endFirst.Drops.Count == 1 && endFirst.Drops[0].SeqNum == 5,
                    string.Join(",", endFirst.Drops));
                Check("and overflowing at the begin drops the oldest",
                    beginFirst.Drops.Count == 1 && beginFirst.Drops[0].SeqNum == 1,
                    string.Join(",", beginFirst.Drops));
            }

            // The index is an OFFSET and not a sequence number, which is the mistake libchiaki's
            // own parameter comment shouts about - so it is asserted rather than assumed.
            using (var offsets = new ReorderQueue(3, 100))
            {
                offsets.Push(102, 2);
                Check("peek takes an offset from the window, not a sequence number",
                    offsets.Peek(2) == (102ul, 2L) && offsets.Peek(102) is null,
                    offsets.Peek(2)?.ToString() ?? "<null>");

                // PP107. chiaki_reorder_queue_drop announces the element to the drop callback and
                // then does NOT remove it: it never clears entry->set, so its own count-reduction
                // loop - `while(!entry->set)` - cannot run either. The element stays peekable and
                // stays pullable. Asserted as the behaviour it is, because the port must not
                // differ; the consequence is in the roadmap.
                offsets.Drop(2);
                Check("drop reports the element and leaves it in the queue",
                    offsets.Drops.Count == 1 && offsets.Drops[0] == new ReorderDrop(102, 2)
                    && offsets.Peek(2) == (102ul, 2L) && offsets.Count == 3,
                    $"{offsets.Drops.Count} dropped, peek {offsets.Peek(2)?.ToString() ?? "null"}");
            }

            // PP107, decided: accepted, and asserted so the acceptance can expire.
            //
            // The port reproduces both defects because every drift check here asserts that the
            // managed side matches lib/, and patching lib/ would leave them asserting agreement
            // with a libchiaki nobody else runs. What that costs is a reason held in prose, and
            // prose does not go red. These do. The day upstream repairs one of these, the port's
            // faithful copy stops being faithful and becomes the divergence - and this is what
            // says so, on the next run, rather than at the next bug report that will not compare.
            string? rqFile = ReorderQueueSource.Locate();
            string? takionSrcFile = ReorderQueueSource.LocateTakion();
            if (rqFile is null || takionSrcFile is null)
            {
                Console.WriteLine(@"  --    the accepted reorder-queue defects  (no lib\src here)");
            }
            else
            {
                string? dropBody = ReorderQueueSource.BodyOf(rqFile, "chiaki_reorder_queue_drop");
                string? peekBody = ReorderQueueSource.BodyOf(rqFile, "chiaki_reorder_queue_peek");
                string takionText = File.ReadAllText(takionSrcFile);

                Check("both accepted functions are still readable in lib/",
                    dropBody is not null && peekBody is not null,
                    dropBody is null ? "drop not found" : peekBody is null ? "peek not found" : "both");

                if (dropBody is not null && peekBody is not null)
                {
                    Check("drop still clears no entry's set flag, which is why it does not drop",
                        ReorderQueueSource.DropLeavesTheEntrySet(dropBody));
                    Check("drop's count-reduction loop is still guarded by the return above it",
                        ReorderQueueSource.DropCountLoopIsUnreachable(dropBody));
                    Check("peek still writes both out-pointers with no null test, unlike pull",
                        ReorderQueueSource.PeekWritesUnguarded(peekBody));
                }

                // Without these two the defects are present but unreachable, and an accepted
                // defect nobody can reach is a different decision than the one recorded.
                Check("takion still peeks with a NULL sequence number on the re-check-MACs path",
                    ReorderQueueSource.TakionPeeksWithNull(takionText));
                Check("takion still drops the packet whose MAC it rejected",
                    ReorderQueueSource.TakionDropsOnBadMac(takionText));
            }

            Console.WriteLine();
            Console.WriteLine("Handshake - one recorded key agreement, repeated");

            Check("the secret size comes from the shim", Ecdh.SecretSize == 32, Ecdh.SecretSize.ToString());

            string? gkFile = SanitizerSource.LocateRelative(@"test\gkcrypt.c");
            if (gkFile is null)
            {
                Console.WriteLine(@"  --    the recorded key agreement  (no test\gkcrypt.c here)");
            }
            else
            {
                IReadOnlyDictionary<string, byte[]> ex = CryptoVectors.InFunction(gkFile, "test_ecdh");
                Check("the recorded exchange is readable",
                    ex.Count == 7 && ex["secret"].Length == Ecdh.SecretSize,
                    string.Join(",", ex.Keys));

                using var ecdh = new Ecdh();
                ecdh.SetLocalKey(ex["local_private_key"], ex["local_public_key"]);

                // The signature over the local public key is the half a console verifies. It is
                // deterministic under the handshake key, which is why it can be recorded at all.
                (byte[] pub, byte[] sig) = ecdh.LocalPublicKey(ex["handshake_key"]);
                Check("the local public key and its signature are the recorded ones",
                    pub.SequenceEqual(ex["local_public_key"]) && sig.SequenceEqual(ex["local_public_key_sig"]),
                    Convert.ToHexString(sig));

                // And the agreement itself: the console's key and signature in, the secret that
                // keys the whole session out.
                byte[] secret = ecdh.DeriveSecret(
                    ex["remote_public_key"], ex["handshake_key"], ex["remote_public_key_sig"]);
                Check("the derived secret is the one the console agreed to",
                    secret.SequenceEqual(ex["secret"]), Convert.ToHexString(secret));

                // PP105, found by writing this as a negative and watching it pass. The remote
                // signature is NOT checked: chiaki_ecdh_derive_secret takes handshake_key and
                // remote_sig and uses neither, so a signature with a byte flipped still returns
                // success and still yields the recorded secret. Asserted as the behaviour it is,
                // because a port that quietly started verifying would differ from the client every
                // user already has - and because a check that silently agrees is worse than none.
                byte[] tampered = (byte[])ex["remote_public_key_sig"].Clone();
                tampered[0] ^= 0xff;
                Check("a tampered remote signature is accepted, which is libchiaki's behaviour",
                    ecdh.DeriveSecret(ex["remote_public_key"], ex["handshake_key"], tampered)
                        .SequenceEqual(ex["secret"]));
                // …and the same for the handshake key, which is the other argument it ignores.
                byte[] otherHandshake = (byte[])ex["handshake_key"].Clone();
                otherHandshake[0] ^= 0xff;
                Check("and so is a wrong handshake key, on this path",
                    ecdh.DeriveSecret(ex["remote_public_key"], otherHandshake, ex["remote_public_key_sig"])
                        .SequenceEqual(ex["secret"]));
                // The key IS used where it is used: the local signature above changes with it, so
                // this is not a handshake key that does nothing - it is one that does nothing HERE.
                Check("the handshake key does change the local signature",
                    !ecdh.LocalPublicKey(otherHandshake).Signature.SequenceEqual(ex["local_public_key_sig"]));

                // PP105's open half. The three above say the signature is unread; these five say
                // what that costs, which is the question the roadmap could not answer without
                // reading the transport. Each is a step an attacker takes or is spared, and each
                // is a predicate over lib/'s own text - so the conclusion in BangReachability
                // stops being true out loud if the code moves underneath it.
                string? tkFile = BangReachability.LocateTakion();
                string? scFile = BangReachability.LocateStreamConnection();
                string? hpFile = SanitizerSource.LocateRelative(@"lib\src\remote\holepunch.c");
                if (tkFile is null || scFile is null || hpFile is null)
                {
                    Console.WriteLine(@"  --    what a forged bang would have to beat  (no lib\src here)");
                }
                else
                {
                    string tk = File.ReadAllText(tkFile);
                    string sc = File.ReadAllText(scFile);

                    // The two checks that could refuse a forged bang are the one that is missing
                    // and the one that cannot exist yet. This is the second.
                    Check("the MAC check passes everything while gkcrypt_remote is still null",
                        BangReachability.MacPassesWhileUnkeyed(tk));
                    Check("and the bang is what ends that window, so it was never inside it",
                        BangReachability.SecretIsDerivedBeforeCryptInit(sc));

                    // What is left is not cryptography. Off-path it is three things at once.
                    Check("the tag is checked and is 32 random bits",
                        BangReachability.TagIsCheckedAndRandom(tk));
                    Check("but it is sent in the clear in INIT, so on-path it is free",
                        BangReachability.TagIsSentInTheClear(tk));
                    Check("both transports connect() the socket, which is what makes it on-path",
                        BangReachability.SocketIsConnected(tk)
                        && BangReachability.SocketIsConnected(File.ReadAllText(hpFile)));
                }

                // The key stream, from its own recorded case. The position is part of it: the
                // stream is a function of where in the session you are, so a rewrite that got the
                // derivation right and the position wrong would be correct for a packet nobody
                // sent. 0x30 is the position the case records.
                IReadOnlyDictionary<string, byte[]> ks = CryptoVectors.InFunction(gkFile, "test_key_stream");
                using var gk = new GkCrypt(0, 42, ks["handshake_key"], ks["ecdh_secret"]);
                Check("the key stream at the recorded position is the recorded bytes",
                    gk.KeyStream(0x30, ks["key_stream"].Length).SequenceEqual(ks["key_stream"]),
                    Convert.ToHexString(gk.KeyStream(0x30, ks["key_stream"].Length)));
                Check("and another position is a different stream",
                    !gk.KeyStream(0x40, ks["key_stream"].Length).SequenceEqual(ks["key_stream"]));
            }

            Console.WriteLine();
            Console.WriteLine("Fec - sixty-four erasure cases a real stream produced");

            string? fecFile = FecVectors.Locate();
            if (fecFile is null)
            {
                Console.WriteLine($"  --    the recorded erasure cases  (no {FecVectors.RelativePath} here)");
            }
            else
            {
                IReadOnlyList<FecCase> fecCases = FecVectors.Parse(fecFile);
                Check("every recorded case parses out of the C suite",
                    fecCases.Count == 64, fecCases.Count.ToString());
                // The shape of the data, asserted before it is trusted: a case whose buffer did
                // not hold k+m whole units would decode into the wrong places and still pass a
                // comparison against itself.
                Check("each case's buffer is exactly its units",
                    fecCases.All(c => c.FrameBuffer.Length == c.UnitSize * (c.K + c.M))
                    && fecCases.All(c => c.Erasures.Length > 0),
                    fecCases.Count == 0 ? "none" : fecCases[0].FrameBuffer.Length.ToString());
                // The stride is the layout the decoder addresses units at, not a convenience of
                // the test. 1400 rounds to 1408, and a rewrite that packed units tightly would
                // decode the right bytes into the wrong places.
                Check("the stride rounds up to sixteen",
                    FecVectors.StrideFor(1400) == 1408 && FecVectors.StrideFor(1408) == 1408
                    && FecVectors.StrideFor(1) == 16);

                int recovered = fecCases.Count(c => Fec.Recovers(c));
                Check("every recorded erasure is recovered byte for byte",
                    recovered == fecCases.Count, $"{recovered} of {fecCases.Count}");

                // The negative that gives the run its meaning. The unit that is actually blanked
                // stays blanked and the decoder is told a DIFFERENT one was lost - so it repairs
                // the wrong hole and the real one is still garbage. Without this, a decode that
                // returned the buffer untouched would pass all sixty-four cases above.
                FecCase probe = fecCases[0];
                uint lied = (probe.Erasures[0] + 1) % probe.K;
                Check("told the wrong unit was lost, the frame does not come back",
                    !Fec.Recovers(probe, [lied]),
                    $"blanked {probe.Erasures[0]}, declared {lied}");
            }

            Console.WriteLine();
            Console.WriteLine("Discovery - the bytes a console answers, or does not");

            // A console that does not answer looks exactly like a console that is switched off,
            // so the packet is the part of discovery worth pinning byte for byte.
            Check("the ports and protocol versions are the console's",
                Discovery.Port(false) == 987 && Discovery.Port(true) == 9302
                && Discovery.ProtocolVersion(false) == "00020020"
                && Discovery.ProtocolVersion(true) == "00030010",
                $"{Discovery.Port(true)} {Discovery.ProtocolVersion(true)}");
            Check("the local reply ports are 9303 through 9319",
                Discovery.LocalPortRange == (9303, 9319), Discovery.LocalPortRange.ToString());

            // Line-based and newline-terminated, with no carriage returns despite the HTTP/1.1
            // in the request line - a port that "corrected" that would be talking to nothing.
            string srch5 = Discovery.PacketText(DiscoveryCommand.Search, ps5: true);
            Check("a PS5 search is the exact request line and one header",
                srch5 == "SRCH * HTTP/1.1\ndevice-discovery-protocol-version:00030010\n",
                srch5.Replace("\n", "\\n"));
            Check("a PS4 search differs only in the version",
                Discovery.PacketText(DiscoveryCommand.Search, ps5: false)
                    == "SRCH * HTTP/1.1\ndevice-discovery-protocol-version:00020020\n");
            Check("there are no carriage returns in it",
                !srch5.Contains('\r', StringComparison.Ordinal));

            // The wake packet carries the registration key as a DECIMAL number - it is the key
            // reinterpreted as hex, formatted with %llu - and five headers nobody would guess.
            string wake = Discovery.PacketText(DiscoveryCommand.Wakeup, ps5: true, 0x1234ABCD);
            Check("a wake packet carries its credential in decimal",
                wake == "WAKEUP * HTTP/1.1\nclient-type:vr\nauth-type:R\nmodel:w\napp-type:r\n"
                    + "user-credential:305441741\ndevice-discovery-protocol-version:00030010\n",
                wake.Replace("\n", "\\n"));

            // The two-call sizing, which is what stops a wake packet's credential being truncated
            // by a buffer somebody guessed at.
            Check("the packet is sized by asking rather than by guessing",
                Discovery.Packet(DiscoveryCommand.Wakeup, true, ulong.MaxValue).Length
                    > Discovery.Packet(DiscoveryCommand.Wakeup, true, 1).Length);

            // What answered. A PS5 is identified by the protocol version it announced and NOT by
            // its host type, which is what it looks like it should be.
            Check("a PS5 is known by its protocol version",
                Discovery.IsPs5("00030010") && !Discovery.IsPs5("00020020")
                && !Discovery.IsPs5(null));

            // The ladder, including the rung that surprises: both PS5 rungs require the PS5
            // protocol version, so a PS5 system version announced with a PS4 protocol lands on
            // Ps4_10. That is the ladder's own answer, not a fallback.
            Check("the target ladder resolves each rung",
                Discovery.Target("8050001", "00030010") == ChiakiTarget.Ps5_1
                && Discovery.Target("8050000", "00030010") == ChiakiTarget.Ps5Unknown
                && Discovery.Target("8000000", "00020020") == ChiakiTarget.Ps4_10
                && Discovery.Target("7000000", "00020020") == ChiakiTarget.Ps4_9
                && Discovery.Target("1", "00020020") == ChiakiTarget.Ps4_8
                && Discovery.Target("0", "00020020") == ChiakiTarget.Ps4Unknown,
                Discovery.Target("8050001", "00030010").ToString());
            Check("a PS5 version on a PS4 protocol is a PS4",
                Discovery.Target("8050001", "00020020") == ChiakiTarget.Ps4_10,
                Discovery.Target("8050001", "00020020").ToString());

            Check("a host state has the word the list shows",
                Discovery.HostStateString(DiscoveryHostState.Ready) == "ready"
                && Discovery.HostStateString(DiscoveryHostState.Standby) == "standby",
                Discovery.HostStateString(DiscoveryHostState.Ready) ?? "<null>");

            // The reply path. What follows asserts the CROSSING and the status mapping, not that a
            // console spells the headers this way - the header names below are read out of
            // lib/src/discovery.c, so they cannot testify about themselves. That oracle is a
            // console answering, and PP6's remainder is still waiting for one: a search broadcast
            // from this machine on 2026-08-19 got no reply.
            static byte[] Reply(string text) => Encoding.UTF8.GetBytes(text);

            const string readyReply =
                "HTTP/1.1 200 Ok\n"
                + "host-id:0011223344556677\n"
                + "host-type:PS5\n"
                + "host-name:PS5-385\n"
                + "host-request-port:9295\n"
                + "device-discovery-protocol-version:00030010\n"
                + "system-version:8050001\n"
                + "running-app-titleid:CUSA00000\n"
                + "running-app-name:Some Game\n";

            DiscoveredConsole? ready = Discovery.ParseReply(Reply(readyReply), "192.168.1.7", out ChiakiError replyErr);
            Check("a reply parses into a console",
                ready is not null && replyErr == ChiakiError.Success, replyErr.ToString());
            Check("every field crosses the seam intact",
                ready is { Name: "PS5-385", HostType: "PS5", Id: "0011223344556677",
                    SystemVersion: "8050001", ProtocolVersion: "00030010",
                    RunningAppTitleId: "CUSA00000", RunningAppName: "Some Game",
                    RequestPort: 9295 },
                ready?.ToString() ?? "<null>");
            // The address is not in the datagram: it is where the datagram came from, and it is
            // what a session is later opened to.
            Check("the address comes from the sender and not the reply",
                ready?.Address == "192.168.1.7", ready?.Address ?? "<null>");

            // 620 is the piece of protocol knowledge here. It is not an HTTP status anybody would
            // guess, and it is the difference between a console the list offers to wake and one it
            // offers to connect to.
            Check("620 is standby, 200 is ready, anything else is unknown",
                Discovery.ParseReply(Reply("HTTP/1.1 620 Server Standby\nhost-name:PS5-385\n"), "10.0.0.5", out _)
                    ?.State == DiscoveryHostState.Standby
                && ready?.State == DiscoveryHostState.Ready
                && Discovery.ParseReply(Reply("HTTP/1.1 404 Nope\n\n"), "10.0.0.5", out _)
                    ?.State == DiscoveryHostState.Unknown);

            // strtoul with base 0, so the port is read as C would read a literal - a leading 0x is
            // hexadecimal and a leading 0 is octal. Nothing sends those, and a rewrite that used a
            // decimal parse would differ on exactly the input that would say so.
            Check("the request port is parsed as a C literal, base and all",
                Discovery.ParseReply(Reply("HTTP/1.1 200 Ok\nhost-request-port:0x2447\n"), "10.0.0.5", out _)
                    ?.RequestPort == 9287,
                Discovery.ParseReply(Reply("HTTP/1.1 200 Ok\nhost-request-port:0x2447\n"), "10.0.0.5", out _)
                    ?.RequestPort.ToString() ?? "<null>");

            // A datagram that is not a reply is refused rather than guessed at.
            Check("something that is not an HTTP response is refused",
                Discovery.ParseReply(Reply("hello\n"), "10.0.0.5", out ChiakiError junkErr) is null
                && junkErr != ChiakiError.Success, junkErr.ToString());
            Check("a bad sender address is refused too",
                Discovery.ParseReply(Reply(readyReply), "not-an-address", out _) is null);

            // The ownership rule this handle exists for. chiaki_http_response_parse works IN
            // PLACE, so every field of a parsed host points into the datagram - and the shim keeps
            // its own copy alive only until the handle is freed. Parsing a second reply and then
            // reading the first console proves the strings were copied out rather than left
            // pointing at a buffer that has since been freed and reused.
            DiscoveredConsole? kept = Discovery.ParseReply(Reply(readyReply), "192.168.1.7", out _);
            for (int i = 0; i < 16; i++)
                Discovery.ParseReply(Reply($"HTTP/1.1 200 Ok\nhost-name:filler-{i}\nhost-id:{i}\n"), "10.0.0.5", out _);
            Check("a parsed console survives the buffer it was parsed from",
                kept is { Name: "PS5-385", Id: "0011223344556677" },
                kept?.Name ?? "<null>");

            // PP6's remainder, which was filed as needing a console on the network. It needs an
            // address that ANSWERS - so this is one: a socket on the loopback bound to the PS5
            // discovery port, replying to the service's own search. The service, the socket, the
            // timer and the reply callback all run; the only thing the loopback is standing in for
            // is the console's willingness to answer.
            using (var responder = new System.Net.Sockets.UdpClient(
                new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 9302)))
            using (var ps4Drain = new System.Net.Sockets.UdpClient(
                new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 987)))
            {
                // The second socket listens and says nothing, and it is not optional. The service
                // searches for a PS4 on 987 before it searches for a PS5 on 9302, and a UDP
                // datagram sent to a loopback port with no listener comes back as an ICMP port
                // unreachable - which Windows reports on the SENDER's next receive as a reset.
                // Without this the service's own socket fails its read before the reply arrives,
                // and the log says "Discovery thread failed to read from socket".
                //
                // A real network does not show this: the search goes out as a broadcast, and
                // nothing answers a broadcast with ICMP. It is the loopback that makes it visible,
                // which is worth knowing before somebody points this client at one console's
                // address and wonders why discovery stops.
                _ = Task.Run(() =>
                {
                    var any = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                    try { ps4Drain.Receive(ref any); }
                    catch (System.Net.Sockets.SocketException) { }
                    catch (ObjectDisposedException) { }
                });

                var found = new List<DiscoveredConsole>();
                using var announced = new ManualResetEventSlim(false);
                using var searched = new ManualResetEventSlim(false);

                // Answer the first search that arrives, from the port it arrived at, so the
                // service's own socket receives the reply.
                var answering = Task.Run(() =>
                {
                    var from = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                    byte[] search = responder.Receive(ref from);
                    searched.Set();

                    if (!Encoding.UTF8.GetString(search).StartsWith("SRCH", StringComparison.Ordinal))
                        return;

                    byte[] reply = Encoding.UTF8.GetBytes(
                        "HTTP/1.1 200 Ok\n"
                        + "host-id:00112233445566aa\n"
                        + "host-type:PS5\n"
                        + "host-name:LoopbackPS5\n"
                        + "host-request-port:9295\n"
                        + "device-discovery-protocol-version:00030010\n"
                        + "system-version:8050001\n");
                    responder.Send(reply, reply.Length, from);
                });

                using (var service = new DiscoveryService("127.0.0.1", consoles =>
                {
                    lock (found)
                    {
                        found.Clear();
                        found.AddRange(consoles);
                    }
                    if (consoles.Count > 0)
                        announced.Set();
                }, pingMs: 200))
                {
                    Check("the service sends a search to where it is pointed",
                        searched.Wait(TimeSpan.FromSeconds(10)));

                    bool told = announced.Wait(TimeSpan.FromSeconds(10));
                    Check("and a socket that answers is announced as a console", told);

                    DiscoveredConsole console;
                    lock (found)
                        console = found.Count > 0 ? found[0] : default;

                    Check("the console arrives with the fields its reply carried",
                        told && console is { Name: "LoopbackPS5", HostType: "PS5",
                            Id: "00112233445566aa", RequestPort: 9295,
                            State: DiscoveryHostState.Ready },
                        console.ToString());
                    // The address is where the datagram came from, not something in the reply -
                    // which is what a session is later opened to.
                    Check("and the address is the one that answered",
                        told && console.Address == "127.0.0.1", console.Address ?? "<null>");
                }

                answering.Wait(TimeSpan.FromSeconds(2));
            }

            // The shim declares chiaki_discovery_srch_response_parse itself, because libchiaki
            // exports it and declares it in no header - and lib/ is the half of this project that
            // is not edited. A signature no compiler compares is not a build error when it is
            // wrong, it is a corrupted stack at the first reply, so it is compared here.
            string? libSource = LibSource.Locate();
            string? shimSource = LibSource.LocateShim();
            if (libSource is null || shimSource is null)
            {
                Console.WriteLine($"  --    the shim's undeclared import  (no {LibSource.RelativePath} here)");
            }
            else
            {
                const string fn = "chiaki_discovery_srch_response_parse";
                string? defined = LibSource.SignatureOf(libSource, fn);
                string? declared = LibSource.SignatureOf(shimSource, fn);
                Check("the shim's declaration matches libchiaki's definition",
                    defined is not null && declared is not null && defined == declared,
                    $"{defined ?? "<none>"}  ||  {declared ?? "<none>"}");
            }

            Console.WriteLine();
            Console.WriteLine("AudioVolume - mixing into silence, which is scaling");

            // Zero is not silence, it is nothing: the frame returns early and never reaches the
            // ring. Scaling by zero instead would keep feeding the sink, and a muted stream would
            // hold its latency rather than letting the queue drain.
            Check("volume zero drops the frame rather than muting it",
                AudioVolume.ShouldDrop(0) && !AudioVolume.ShouldDrop(1)
                && !AudioVolume.ShouldDrop(AudioVolume.MaxVolume));

            short[] pcm = [32767, -32768, 1000, -1000, 0];
            var scaled = new short[pcm.Length];

            // At 128 and above the scaling is skipped, which is what makes it a ceiling rather
            // than a midpoint - the branch, not the arithmetic.
            AudioVolume.Apply(pcm, scaled, AudioVolume.MaxVolume);
            Check("at the maximum the samples pass through untouched",
                scaled.SequenceEqual(pcm), string.Join(",", scaled));
            AudioVolume.Apply(pcm, scaled, 200);
            Check("and above it they still do, so 128 is a ceiling",
                scaled.SequenceEqual(pcm), string.Join(",", scaled));

            AudioVolume.Apply(pcm, scaled, 64);
            Check("half volume halves the samples, rounding toward zero",
                scaled.SequenceEqual(new short[] { 16383, -16384, 500, -500, 0 }),
                string.Join(",", scaled));

            AudioVolume.Apply(pcm, scaled, 1);
            Check("the quietest step is a divide and not a mute",
                scaled.SequenceEqual(new short[] { 255, -256, 7, -7, 0 }),
                string.Join(",", scaled));

            Console.WriteLine();
            Console.WriteLine("DualSenseIntensity - two events, one byte, two nibbles");

            // The enum is not ordered by strength, and a port that compared these would read as
            // sensible code and invert the ladder.
            Check("the console's numbers do not rank by strength",
                (int)DualSenseEffectIntensity.Strong == 1
                && (int)DualSenseEffectIntensity.Medium == 2
                && (int)DualSenseEffectIntensity.Weak == 3);

            var intensity = new DualSenseIntensity();
            Check("both sides start at Strong",
                intensity is { RumbleCode: 0x00, TriggerCode: 0x00, Packed: 0x00 }
                && intensity.RumbleMultiplier == 1.0,
                intensity.Packed.ToString("x2"));

            // The rumble codes are the enum's own values for Medium and Weak and NOT for Strong,
            // which is 1 as an enum and 0 as a code. Passing the enum straight through would work
            // for three arms out of four and send 0x01 for the fourth.
            intensity.SetRumble(DualSenseEffectIntensity.Medium);
            Check("Medium's code happens to be its enum value", intensity.RumbleCode == 0x02);
            intensity.SetRumble(DualSenseEffectIntensity.Strong);
            Check("Strong's is not, which is the trap",
                intensity.RumbleCode == 0x00 && (int)DualSenseEffectIntensity.Strong == 1,
                intensity.RumbleCode.ToString());

            // The trigger codes are not derivable from anything.
            intensity.SetTrigger(DualSenseEffectIntensity.Weak);
            Check("the trigger ladder is its own table",
                intensity.TriggerCode == 0x90, intensity.TriggerCode.ToString("x2"));
            intensity.SetTrigger(DualSenseEffectIntensity.Medium);
            Check("and Medium is 0x60, not 0x02 or 2",
                intensity.TriggerCode == 0x60, intensity.TriggerCode.ToString("x2"));

            // The packing: trigger high, rumble low, one byte.
            intensity.SetRumble(DualSenseEffectIntensity.Weak);
            Check("the byte is the trigger nibble over the rumble nibble",
                intensity.Packed == 0x63, intensity.Packed.ToString("x2"));

            // Off is not a code. It is a negative that gates a whole path, and only becomes a
            // nibble of ones when the byte is packed - and only for its own half.
            intensity.SetTrigger(DualSenseEffectIntensity.Off);
            Check("an off trigger fills its own nibble and leaves the other",
                intensity.Packed == 0xF3 && !intensity.TriggerEffectsEnabled && intensity.RumbleEnabled,
                intensity.Packed.ToString("x2"));

            intensity.SetRumble(DualSenseEffectIntensity.Off);
            Check("both off is 0xff, and both paths are shut",
                intensity.Packed == 0xFF
                && !intensity.RumbleEnabled && !intensity.TriggerEffectsEnabled
                && intensity.RumbleMultiplier == 0.0,
                intensity.Packed.ToString("x2"));

            // The multiplier ladder, which is what a haptics frame is scaled by before it is
            // folded to a rumble strength.
            var ladder = new DualSenseIntensity();
            ladder.SetRumble(DualSenseEffectIntensity.Medium);
            double medium = ladder.RumbleMultiplier;
            ladder.SetRumble(DualSenseEffectIntensity.Weak);
            Check("the multiplier ladder is 1, 0.5 and 0.33",
                medium == 0.5 && ladder.RumbleMultiplier == 0.33,
                $"{medium} / {ladder.RumbleMultiplier}");

            Console.WriteLine();
            Console.WriteLine("FeedbackState - three latches between the pads and the wire");

            using (var merged = new ChiakiControllerState())
            using (var second = new ChiakiControllerState())
            {
                // chiaki_controller_state_or, and none of its three interesting rules is what
                // "or" suggests. Reached through the seam rather than rewritten for exactly that.
                merged.Buttons = ChiakiControllerButton.Cross;
                merged.Sticks = (-30000, 0, 0, 0);
                merged.Triggers = (10, 200);
                second.Buttons = ChiakiControllerButton.Box;
                second.Sticks = (100, 0, 0, 0);
                second.Triggers = (250, 5);
                merged.Or(second);

                Check("buttons union and triggers take the larger",
                    merged.Buttons == (ChiakiControllerButton.Cross | ChiakiControllerButton.Box)
                    && merged.Triggers == ((byte)250, (byte)200),
                    $"{merged.Buttons} {merged.Triggers}");
                // The trap: a stick takes the larger MAGNITUDE and keeps its sign. A plain max
                // would have +100 beat -30000 and the stick would fall to almost centre whenever
                // a second device reported anything at all.
                Check("a stick takes the larger magnitude, sign and all",
                    merged.Sticks.LeftX == -30000, merged.Sticks.ToString());

                // Motion comes WHOLE from the first state that has any. Averaging two devices
                // produces an orientation that belongs to neither.
                using var still = new ChiakiControllerState();
                using var moving = new ChiakiControllerState();
                moving.SetMotion(1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f);
                using var expected = new ChiakiControllerState();
                expected.SetMotion(1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f);
                still.Or(moving);
                Check("motion is taken from whichever side has any",
                    still.Matches(expected));
            }

            using (var input = new ChiakiControllerState())
            using (var keys = new ChiakiControllerState())
            {
                var feedback = new FeedbackState { InputBlock = 2 };
                input.Buttons = ChiakiControllerButton.Cross;
                keys.Buttons = ChiakiControllerButton.Cross;

                // Blocked while anything is held, and the keyboard state is blanked too - it is
                // sticky, and the key-up that would clear it happened while nobody was listening.
                Check("a block with a button held blanks both states",
                    feedback.ApplyInputBlock(input, keys)
                    && input.Buttons == ChiakiControllerButton.None
                    && keys.Buttons == ChiakiControllerButton.None
                    && feedback.InputBlock == 2);

                // …and lifts only once every button is up.
                Check("an empty mask lifts the block, once",
                    !feedback.ApplyInputBlock(input, keys) && feedback.InputBlock == 0);
            }

            using (var chord = new ChiakiControllerState())
            {
                // Shortcut 4 is zero by default in the Qt settings, and a zero is NOT part of the
                // chord. "At least one set, and every set one held" is neither "any held" nor
                // "all four held", and the two wrong readings both look right in a debugger.
                var feedback = new FeedbackState
                {
                    Shortcuts = [(uint)ChiakiControllerButton.L1, (uint)ChiakiControllerButton.R1, 0, 0],
                };

                chord.Buttons = ChiakiControllerButton.L1;
                Check("half the chord does nothing",
                    !feedback.ApplyDpadShortcut(chord) && feedback.DpadRegular);

                chord.Buttons = ChiakiControllerButton.L1 | ChiakiControllerButton.R1;
                Check("the whole chord toggles the dpad", feedback.ApplyDpadShortcut(chord) && !feedback.DpadRegular);
                // The latch, which is the difference between a setting toggling and a setting
                // flickering sixty times a second while the buttons are held.
                Check("holding it does not toggle again",
                    !feedback.ApplyDpadShortcut(chord) && !feedback.DpadRegular);

                chord.Buttons = ChiakiControllerButton.None;
                feedback.ApplyDpadShortcut(chord);
                chord.Buttons = ChiakiControllerButton.L1 | ChiakiControllerButton.R1;
                Check("releasing and pressing again toggles back",
                    feedback.ApplyDpadShortcut(chord) && feedback.DpadRegular);

                // A configuration with no shortcut at all must never fire, which is what the
                // "at least one is set" half of the guard is for.
                var noChord = new FeedbackState();
                Check("no shortcuts configured means no chord",
                    !noChord.ApplyDpadShortcut(chord) && noChord.DpadRegular);
            }

            using (var gate = new ChiakiControllerState())
            {
                var feedback = new FeedbackState { DpadRegular = false, DpadTouchIncrement = 30 };
                gate.Buttons = ChiakiControllerButton.DpadLeft;
                Check("the dpad drives a finger when all three conditions hold",
                    feedback.ShouldDriveDpadTouch(gate));

                // An increment of zero is how "the feature is off" is expressed - there is no
                // second flag - so it is checked here rather than anywhere a boolean would be.
                feedback.DpadTouchIncrement = 0;
                Check("an increment of zero is the feature being off",
                    !feedback.ShouldDriveDpadTouch(gate));

                feedback.DpadTouchIncrement = 30;
                feedback.DpadRegular = true;
                Check("and a regular dpad stays a dpad", !feedback.ShouldDriveDpadTouch(gate));

                feedback.DpadRegular = false;
                gate.Buttons = ChiakiControllerButton.Cross;
                Check("a button that is not a direction does not drive it",
                    !feedback.ShouldDriveDpadTouch(gate));
            }

            Console.WriteLine();
            Console.WriteLine("HapticsRumble - what a pad with no haptic motors feels");

            static byte[] HapticFrame(short left, short right, int samples)
            {
                var f = new byte[samples * HapticsRumble.SampleSize];
                for (int i = 0; i < samples; i++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(f.AsSpan(i * 4), left);
                    BinaryPrimitives.WriteInt16LittleEndian(f.AsSpan(i * 4 + 2), right);
                }
                return f;
            }

            // Three ways of sending nothing, which the Qt client treats alike by returning early.
            Check("an empty or misaligned frame sends nothing",
                HapticsRumble.Strength([], RumbleHapticsIntensity.Normal) is null
                && HapticsRumble.Strength(new byte[6], RumbleHapticsIntensity.Normal) is null);
            // Silence is the common one: a zero would be a rumble command per frame for a pad
            // that should be still.
            Check("a frame under the floor sends nothing, not a zero",
                HapticsRumble.Strength(HapticFrame(40, 40, 8), RumbleHapticsIntensity.Normal) is null,
                HapticsRumble.Strength(HapticFrame(40, 40, 8), RumbleHapticsIntensity.Normal)?.ToString() ?? "null");

            // The fold is the mean of twice the absolute amplitude, and the louder channel wins.
            Check("the amplitude is doubled and the louder channel decides",
                HapticsRumble.Strength(HapticFrame(1000, 300, 8), RumbleHapticsIntensity.Normal) == 2000,
                HapticsRumble.Strength(HapticFrame(1000, 300, 8), RumbleHapticsIntensity.Normal)?.ToString() ?? "null");
            Check("the sign of the amplitude does not matter",
                HapticsRumble.Strength(HapticFrame(-1000, 0, 8), RumbleHapticsIntensity.Normal) == 2000);

            // The five intensities, on one frame, so the ladder is visible.
            byte[] loud = HapticFrame(10000, 0, 8);
            Check("the intensity ladder scales the same frame",
                HapticsRumble.Strength(loud, RumbleHapticsIntensity.VeryWeak) == 4000
                && HapticsRumble.Strength(loud, RumbleHapticsIntensity.Weak) == 10000
                && HapticsRumble.Strength(loud, RumbleHapticsIntensity.Normal) == 20000
                && HapticsRumble.Strength(loud, RumbleHapticsIntensity.Strong) == 40000
                && HapticsRumble.Strength(loud, RumbleHapticsIntensity.VeryStrong) == 65535,
                HapticsRumble.Strength(loud, RumbleHapticsIntensity.VeryStrong)?.ToString() ?? "null");

            // The nine-bit floor: audible but small is raised, so a controller that shifts the
            // value up to nine bits does not shift it away to nothing.
            Check("a small non-zero strength is raised to the nine-bit floor",
                HapticsRumble.Strength(HapticFrame(120, 0, 8), RumbleHapticsIntensity.VeryWeak) == 512,
                HapticsRumble.Strength(HapticFrame(120, 0, 8), RumbleHapticsIntensity.VeryWeak)?.ToString() ?? "null");

            // PP98. The loudest input there is folds to exactly 65536 - twice the magnitude of
            // short.MinValue, one past a ushort. Three of the five branches used to narrow that
            // bare, so a fully clipped frame wrapped to a rumble of ZERO on Normal while Strong,
            // which saturated, stayed at full. It was a zero sent and not a frame skipped: the
            // silence check runs before the switch.
            byte[] fullScale = HapticFrame(short.MinValue, short.MinValue, 8);
            Check("full scale saturates instead of wrapping",
                HapticsRumble.Strength(fullScale, RumbleHapticsIntensity.Normal) == 65535,
                HapticsRumble.Strength(fullScale, RumbleHapticsIntensity.Normal)?.ToString() ?? "null");
            Check("and every branch agrees at the top of the range",
                HapticsRumble.Strength(fullScale, RumbleHapticsIntensity.Strong) == 65535
                && HapticsRumble.Strength(fullScale, RumbleHapticsIntensity.Weak) == 32768
                && HapticsRumble.Strength(fullScale, RumbleHapticsIntensity.VeryWeak) == 13107,
                HapticsRumble.Strength(fullScale, RumbleHapticsIntensity.VeryWeak)?.ToString() ?? "null");

            // The Qt client's own five branches, which this code cannot exercise. The property is
            // narrow on purpose: an assignment straight from a temp is the shape of the mistake,
            // and a saturating branch does not look like one.
            if (sessionSource is null)
            {
                Console.WriteLine($"  --    the Qt client's haptics fold  (no {SessionSource.RelativePath} here)");
            }
            else
            {
                IReadOnlyList<string> bare = SessionSource.BareRumbleNarrowings(sessionSource);
                Check("no branch of the Qt client's haptics fold narrows without saturating",
                    bare.Count == 0, string.Join(" | ", bare));
            }
        }

        Console.WriteLine();
        Console.WriteLine($"{ran - failed} of {ran} passed.");

        // What the store on THIS machine says, printed and never asserted: a developer with a
        // Qt install sees their own consoles, and one without sees a line saying so. Asserting
        // it would make the suite pass or fail on whether somebody happens to have run Chiaki.
        // Which of the candidate directories the shim actually came from. Printed rather than
        // asserted because it is a property of how this machine was built, and a developer
        // debugging a stale DLL wants to see it before anything else.
        Console.WriteLine();
        Console.WriteLine($"Shim: {ChiakiNative.LoadedFrom ?? "not loaded"}");

        // The paths this machine resolves, printed so they can be held against what a Qt build
        // prints beside them. Not asserted: they are absolute paths on one developer's disk.
        Console.WriteLine();
        Console.WriteLine("Paths on this machine:");
        Console.WriteLine($"  logs      {QtPaths.LogDirectory}");
        Console.WriteLine($"  shaders   {QtPaths.ShaderCacheFile}");
        Console.WriteLine($"  placebo   {QtPaths.PlaceboConfigFile}");
        Console.WriteLine($"  desktop   {QtPaths.DesktopDirectory}");
        Console.WriteLine($"  downloads {QtPaths.DownloadsDirectory}");

        var store = new QSettingsStore();
        Console.WriteLine();
        Console.WriteLine($"Profile: {(store.CurrentProfile.Length == 0 ? "(none)" : store.CurrentProfile)}"
            + $"  known: [{string.Join(", ", store.Profiles())}]");
        if (!store.Exists())
        {
            Console.WriteLine($"No Qt store at HKCU\\{store.KeyPath} on this machine.");
        }
        else
        {
            var hosts = store.RegisteredHosts();
            Console.WriteLine($"HKCU\\{store.KeyPath}: {hosts.Count} registered console(s).");
            foreach (RegisteredHost h in hosts)
            {
                Console.WriteLine($"  {h.ServerNickname}  mac={h.MacText}  target={h.Target}  "
                    + $"regist_key={(h.RpRegistKey is null ? "-" : h.RpRegistKey.Length + "B")}  "
                    + $"rp_key={(h.RpKey is null ? "-" : h.RpKey.Length + "B")}");
            }
            (string? account, string? token) = store.PsnAccount();
            Console.WriteLine($"  psn account id: {(account is null ? "not linked" : "present")}, "
                + $"refresh token: {(token is null ? "not linked" : "present")}");

            // A handful of preferences read through the table, off this machine's own store.
            // Printed and not asserted for the same reason the consoles above are: what they
            // hold depends on what the developer happens to have set.
            var mine = new QSettingsPreferences(store);
            Console.WriteLine($"  decoder={mine.GetString("settings/hw_decoder")}"
                + $"  renderer={mine.GetString("settings/render_backend")}"
                + $"  volume={mine.GetInt("settings/audio_volume")}"
                + $"  fps={mine.GetInt("settings/fps_local_ps5")}"
                + $"  loss_max={mine.GetDouble("settings/packet_loss_max")}"
                + $"  geometry={mine.GetRect("settings/geometry")?.ToString() ?? "unset"}");
        }

        return failed == 0 ? 0 : 1;
    }
}
