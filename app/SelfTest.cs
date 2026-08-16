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
        Console.WriteLine($"{ran - failed} of {ran} passed.");

        // What the store on THIS machine says, printed and never asserted: a developer with a
        // Qt install sees their own consoles, and one without sees a line saying so. Asserting
        // it would make the suite pass or fail on whether somebody happens to have run Chiaki.
        var store = new QSettingsStore();
        Console.WriteLine();
        if (!store.Exists())
        {
            Console.WriteLine($"No Qt store at HKCU\\{QSettingsStore.DefaultKeyPath} on this machine.");
        }
        else
        {
            var hosts = store.RegisteredHosts();
            Console.WriteLine($"Qt store: {hosts.Count} registered console(s).");
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
