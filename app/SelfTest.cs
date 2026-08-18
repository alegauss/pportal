using System.Text;
using ChiakiNg.Settings;

namespace ChiakiNg;

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
            QSettingsValue.AsString("@@home") == "@home", QSettingsValue.AsString("@@home"));
        Check("an ordinary string keeps its text",
            QSettingsValue.AsString("PS5-385") == "PS5-385");
        // …and the escape is one level, not a strip-all: `@@home` typed by a user is `@@@home`.
        Check("the escape is one '@' and not all of them",
            QSettingsValue.AsString("@@@home") == "@@home", QSettingsValue.AsString("@@@home"));

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
        Console.WriteLine($"{ran - failed} of {ran} passed.");

        // What the store on THIS machine says, printed and never asserted: a developer with a
        // Qt install sees their own consoles, and one without sees a line saying so. Asserting
        // it would make the suite pass or fail on whether somebody happens to have run Chiaki.
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
        }

        return failed == 0 ? 0 : 1;
    }
}
