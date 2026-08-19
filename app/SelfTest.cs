using System.Text;
using ChiakiNg.Settings;
using ChiakiNg.Native;

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
            QSettingsValue.AsString("@@home") == "@home", QSettingsValue.AsString("@@home") ?? "<null>");
        Check("an ordinary string keeps its text",
            QSettingsValue.AsString("PS5-385") == "PS5-385");
        // …and the escape is one level, not a strip-all: `@@home` typed by a user is `@@@home`.
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

                // …and the mask is live, which is what a verbosity setting changed mid-session is.
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
