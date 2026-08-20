using ChiakiNg.Session;

namespace ChiakiNg.Settings;

/// <summary>Which console's half of the tab is showing. Dialog state, not a preference.</summary>
public enum StreamConsole { Ps4 = 0, Ps5 = 1 }

/// <summary>Which connection a row is about.</summary>
public enum StreamNetwork { Local, Remote }

/// <summary>
/// PP16: the resolution, which has THREE representations and a different label list per row.
///
/// The combo is 0-based. The QML property is the C++ enum, which is 1-based -
/// `currentIndex: resolutionLocalPS4 - 1` and `= index + 1`. And the STORE holds neither: it holds
/// "360p", "540p", "720p" or "1080p" as a string, read back through `QMap::key(s, default)`.
///
/// So an index has to travel through two conversions to reach the store, and the bitrate default
/// table below is keyed on the MIDDLE one. A port that skipped a layer writes a number where the
/// Qt client writes a word, and the Qt client then falls back to its default - which is the
/// preference-resets-silently failure the General tab found, one screen further on and with an extra
/// step to get wrong.
/// </summary>
public sealed class StreamResolution
{
    /// <summary>The four presets, in enum order. The stored word is the label without a suffix.</summary>
    public static IReadOnlyList<string> Stored { get; } = ["360p", "540p", "720p", "1080p"];

    /// <summary>The lowest value the enum takes, which is what makes the index off by one.</summary>
    public const int FirstPreset = 1;

    private StreamResolution(string key, IReadOnlyList<string> labels, int defaultPreset)
    {
        Key = key;
        Labels = labels;
        DefaultPreset = defaultPreset;
    }

    /// <summary>The preference.</summary>
    public string Key { get; }

    /// <summary>
    /// What this row's combo shows. THREE distinct lists across the four rows: the PS4 pair marks
    /// 720p as the default and calls 1080p "(PS5 and PS4 Pro)", local PS5 marks 1080p, and remote
    /// PS5 marks 720p with no note on 1080p. One shared list would put the default marker on the
    /// wrong entry for at least one row.
    /// </summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>The enum value used when the store holds nothing or something unrecognised.</summary>
    public int DefaultPreset { get; }

    public static StreamResolution LocalPs4 { get; } = new(
        "settings/resolution_local_ps4",
        ["360p", "540p", "720p (Default)", "1080p (PS5 and PS4 Pro)"],
        3);

    public static StreamResolution RemotePs4 { get; } = new(
        "settings/resolution_remote_ps4",
        ["360p", "540p", "720p (Default)", "1080p (PS5 and PS4 Pro)"],
        3);

    public static StreamResolution LocalPs5 { get; } = new(
        "settings/resolution_local_ps5",
        ["360p", "540p", "720p", "1080p (Default)"],
        4);

    /// <summary>
    /// Remote PS5, whose default is 720p and NOT 1080p - the only row where the two PS5 defaults
    /// differ, and the reason the four rows cannot share one list.
    /// </summary>
    public static StreamResolution RemotePs5 { get; } = new(
        "settings/resolution_remote_ps5",
        ["360p", "540p", "720p (Default)", "1080p"],
        3);

    /// <summary>The row for a console and a connection.</summary>
    public static StreamResolution For(StreamConsole console, StreamNetwork network)
        => (console, network) switch
        {
            (StreamConsole.Ps4, StreamNetwork.Local) => LocalPs4,
            (StreamConsole.Ps4, StreamNetwork.Remote) => RemotePs4,
            (StreamConsole.Ps5, StreamNetwork.Local) => LocalPs5,
            _ => RemotePs5,
        };

    /// <summary>The enum value a combo index means. Index plus one, and nothing subtler.</summary>
    public static int PresetForIndex(int index) => index + FirstPreset;

    /// <summary>The combo index an enum value shows at.</summary>
    public static int IndexForPreset(int preset) => preset - FirstPreset;

    /// <summary>The word the store holds for an enum value.</summary>
    public static string StoredForPreset(int preset)
        => preset >= FirstPreset && preset < FirstPreset + Stored.Count
            ? Stored[preset - FirstPreset]
            : "";

    /// <summary>
    /// The enum value a stored word means, or this row's default where the word is not one of the
    /// four - `QMap::key(s, default)`, so an unreadable setting is the default and not an error.
    /// </summary>
    public int PresetForStored(string? stored)
    {
        if (stored is null)
            return DefaultPreset;

        int at = Stored.ToList().IndexOf(stored);
        return at < 0 ? DefaultPreset : at + FirstPreset;
    }
}

/// <summary>
/// PP16: the frame rate, which is arithmetic rather than a table.
///
/// `currentIndex: (fps / 30) - 1` and `fps = (index + 1) * 30`, so the store holds 30 or 60 and the
/// combo holds 0 or 1. Written out because the division is what a port drops: storing the index
/// gives a stream asked to run at 1 frame per second, and the console's answer to that is not an
/// error message.
/// </summary>
public static class StreamFps
{
    /// <summary>The step the arithmetic is in.</summary>
    public const int Step = 30;

    /// <summary>What the combo shows. The same two for every row, unlike the resolutions.</summary>
    public static IReadOnlyList<string> Labels { get; } = ["30 fps", "60 fps (Default)"];

    /// <summary>The four keys.</summary>
    public static string KeyFor(StreamConsole console, StreamNetwork network)
        => $"settings/fps_{Suffix(console, network)}";

    /// <summary>The stored rate a combo index means.</summary>
    public static int RateForIndex(int index) => (index + 1) * Step;

    /// <summary>The combo index a stored rate shows at. A rate of zero reads as index -1.</summary>
    public static int IndexForRate(int rate) => (rate / Step) - 1;

    internal static string Suffix(StreamConsole console, StreamNetwork network)
        => $"{(network == StreamNetwork.Local ? "local" : "remote")}_{(console == StreamConsole.Ps4 ? "ps4" : "ps5")}";
}

/// <summary>
/// PP16: the bitrate, where ZERO means "follow the resolution".
///
/// Three things at once, and the middle one is the finding.
///
/// It is stored in KBPS and shown in MBPS - the slider reads `stored / 1000` and writes
/// `value * 1000`.
///
/// A stored ZERO is not a bitrate of nothing, it is the absence of a choice: the slider falls back
/// to a per-resolution default, 2/6/10/15 Mbps for 360p/540p/720p/1080p. Which is why changing a
/// resolution WRITES ZERO to the matching bitrate - the two are one operation, and a port that only
/// wrote the resolution would leave a bitrate tuned for the old one. Nothing on screen would say so.
///
/// And the fallback is a JavaScript truthiness test on the DIVISION, not on the stored value. So a
/// stored 500 kbps gives 0.5, which is truthy, so the slider takes 0.5 - below its own floor of 2.
/// Only an exact zero falls back.
/// </summary>
public static class StreamBitrate
{
    /// <summary>The slider's bounds, in Mbps.</summary>
    public const int MinimumMbps = 2;

    public const int MaximumMbps = 100;

    public const int StepMbps = 1;

    /// <summary>What a stored kbps value is divided by to reach the slider.</summary>
    public const int KbpsPerMbps = 1000;

    /// <summary>The four keys.</summary>
    public static string KeyFor(StreamConsole console, StreamNetwork network)
        => $"settings/bitrate_{StreamFps.Suffix(console, network)}";

    /// <summary>
    /// The default for a resolution preset, in Mbps. Keyed on the ENUM value - the middle of the
    /// resolution's three representations - because that is what the QML switches on.
    /// </summary>
    public static int DefaultMbpsFor(int preset) => preset switch
    {
        1 => 2,    // 360p
        2 => 6,    // 540p
        3 => 10,   // 720p
        4 => 15,   // 1080p
        _ => 0,
    };

    /// <summary>
    /// Where the slider sits for a stored value and a resolution: the stored rate in Mbps, or the
    /// resolution's default when the stored value is exactly zero.
    /// </summary>
    public static double SliderValue(uint storedKbps, int preset)
    {
        double mbps = (double)storedKbps / KbpsPerMbps;

        // Truthiness on the quotient, as the QML has it - so 500 kbps is 0.5 and not a fallback.
        return mbps != 0 ? mbps : DefaultMbpsFor(preset);
    }

    /// <summary>What the store receives when the slider moves.</summary>
    public static uint StoredFor(int mbps) => (uint)(mbps * KbpsPerMbps);

    /// <summary>The label the row prints: the value, then the resolution's default in brackets.</summary>
    public static string Caption(double mbps, int preset)
        => $"{mbps} Mbps ({DefaultMbpsFor(preset)} Mbps)";
}

/// <summary>
/// PP16: the settings screen's Stream tab - twelve controls that are really one matrix.
///
/// Resolution, frame rate and bitrate, for each of two consoles and two connections. Only one
/// console's six are on screen: `selectedConsole` is dialog state rather than a preference, so which
/// half is showing is not remembered between visits.
///
/// The three findings are in <see cref="StreamResolution"/>, <see cref="StreamFps"/> and
/// <see cref="StreamBitrate"/>. The one that spans them is that setting a resolution also zeroes the
/// matching bitrate, which is how "follow the resolution" is written down - see
/// <see cref="SetResolutionIndex"/>.
/// </summary>
public sealed class StreamSettingsViewModel : DialogViewModel
{
    private readonly Dictionary<(StreamConsole, StreamNetwork), int> presets = [];
    private readonly Dictionary<(StreamConsole, StreamNetwork), int> rates = [];
    private readonly Dictionary<(StreamConsole, StreamNetwork), uint> bitrates = [];

    private StreamConsole selectedConsole = StreamConsole.Ps4;

    /// <summary>Every row, in the order the grid shows them.</summary>
    public static IReadOnlyList<(StreamConsole Console, StreamNetwork Network)> Rows { get; } =
    [
        (StreamConsole.Ps4, StreamNetwork.Local),
        (StreamConsole.Ps4, StreamNetwork.Remote),
        (StreamConsole.Ps5, StreamNetwork.Local),
        (StreamConsole.Ps5, StreamNetwork.Remote),
    ];

    /// <summary>A tab with the Qt defaults.</summary>
    public StreamSettingsViewModel()
    {
        foreach ((StreamConsole console, StreamNetwork network) in Rows)
        {
            presets[(console, network)] = StreamResolution.For(console, network).DefaultPreset;
            rates[(console, network)] = 60;
            bitrates[(console, network)] = 0;
        }
    }

    /// <summary>The tab as the store holds it.</summary>
    public StreamSettingsViewModel(IPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        foreach ((StreamConsole console, StreamNetwork network) in Rows)
        {
            StreamResolution row = StreamResolution.For(console, network);
            presets[(console, network)] = row.PresetForStored(preferences.GetString(row.Key));
            rates[(console, network)] = preferences.GetInt(StreamFps.KeyFor(console, network));
            bitrates[(console, network)] = preferences.GetUInt(StreamBitrate.KeyFor(console, network));
        }
    }

    protected override string ButtonProperty => nameof(SelectedConsole);

    /// <summary>Which console's six controls are showing. Not stored anywhere.</summary>
    public StreamConsole SelectedConsole
    {
        get => selectedConsole;
        set
        {
            Set(ref selectedConsole, value);
            Raise(nameof(Ps4Visible));
            Raise(nameof(Ps5Visible));
            Raise(nameof(SelectedConsoleIndex));
            RaiseVisibleRows();
        }
    }

    /// <summary>
    /// The same, as the combo's index. The enum's values ARE the indices, which is why the console
    /// selector is the one control on this tab with no conversion in it.
    /// </summary>
    public int SelectedConsoleIndex
    {
        get => (int)SelectedConsole;
        set => SelectedConsole = (StreamConsole)value;
    }

    /// <summary>What the console selector offers.</summary>
    public static IReadOnlyList<string> ConsoleLabels { get; } = ["PS4", "PS5"];

    public bool Ps4Visible => SelectedConsole == StreamConsole.Ps4;

    public bool Ps5Visible => SelectedConsole == StreamConsole.Ps5;

    /// <summary>The resolution preset for a row - the 1-based enum value, not the index.</summary>
    public int Preset(StreamConsole console, StreamNetwork network) => presets[(console, network)];

    /// <summary>The combo index for a row.</summary>
    public int ResolutionIndex(StreamConsole console, StreamNetwork network)
        => StreamResolution.IndexForPreset(Preset(console, network));

    /// <summary>The word the store receives for a row.</summary>
    public string ResolutionStored(StreamConsole console, StreamNetwork network)
        => StreamResolution.StoredForPreset(Preset(console, network));

    /// <summary>
    /// A resolution chosen on screen - which also ZEROES the row's bitrate.
    ///
    /// One operation, not two. The zero is what makes the bitrate slider follow the new resolution's
    /// default, so a port that wrote only the resolution would leave the old resolution's bitrate in
    /// place with nothing on screen to say so.
    /// </summary>
    public void SetResolutionIndex(StreamConsole console, StreamNetwork network, int index)
    {
        // A negative index is "nothing selected", not a resolution. Refused rather than recorded,
        // because assigning a combo's ItemsSource resets SelectedIndex to -1 and the two-way binding
        // pushes that back - which is the General tab's hazard arriving through a BOUND list rather
        // than an assigned one. Recorded, it became preset 0, whose stored word is the empty string:
        // a resolution the Qt client cannot read, written by switching console and touching nothing.
        if (index < 0 || index >= StreamResolution.Stored.Count)
        {
            RaiseVisibleRows();
            return;
        }

        presets[(console, network)] = StreamResolution.PresetForIndex(index);
        bitrates[(console, network)] = 0;

        RaiseVisibleRows();
    }

    /// <summary>The stored frame rate for a row, 30 or 60.</summary>
    public int Rate(StreamConsole console, StreamNetwork network) => rates[(console, network)];

    /// <summary>The frame-rate combo's index for a row.</summary>
    public int FpsIndex(StreamConsole console, StreamNetwork network)
        => StreamFps.IndexForRate(Rate(console, network));

    /// <summary>A frame rate chosen on screen. This one does not touch the bitrate.</summary>
    public void SetFpsIndex(StreamConsole console, StreamNetwork network, int index)
    {
        // Refused for the same reason as the resolution: index -1 would store a rate of zero.
        if (index < 0 || index >= StreamFps.Labels.Count)
        {
            RaiseVisibleRows();
            return;
        }

        rates[(console, network)] = StreamFps.RateForIndex(index);
        RaiseVisibleRows();
    }

    /// <summary>The stored bitrate for a row, in kbps. Zero means "follow the resolution".</summary>
    public uint StoredBitrate(StreamConsole console, StreamNetwork network)
        => bitrates[(console, network)];

    /// <summary>Where the row's slider sits, in Mbps.</summary>
    public double BitrateMbps(StreamConsole console, StreamNetwork network)
        => StreamBitrate.SliderValue(StoredBitrate(console, network), Preset(console, network));

    /// <summary>The row's default bitrate for its current resolution, which the caption prints.</summary>
    public int DefaultBitrateMbps(StreamConsole console, StreamNetwork network)
        => StreamBitrate.DefaultMbpsFor(Preset(console, network));

    /// <summary>A bitrate dragged on screen, in Mbps.</summary>
    public void SetBitrateMbps(StreamConsole console, StreamNetwork network, int mbps)
    {
        bitrates[(console, network)] = StreamBitrate.StoredFor(mbps);
        RaiseVisibleRows();
    }

    // ALL TWELVE rows are bindable, one property each, rather than six that follow SelectedConsole.
    //
    // That is the QML's own structure - it declares eight combos and four sliders and hides the half
    // that is not showing - and it was arrived at the hard way. Six properties meant re-assigning a
    // combo's ItemsSource when the console changed, because the four resolution rows have three
    // different label lists between them. Re-assigning it inside the property-changed cascade left
    // the combo with a correct SelectedIndex and a BLANK SelectedItem: the right value stored and an
    // empty box on screen. No list here ever changes after construction, so that cannot happen.

    public int Ps4LocalResolutionIndex
    {
        get => ResolutionIndex(StreamConsole.Ps4, StreamNetwork.Local);
        set => SetResolutionIndex(StreamConsole.Ps4, StreamNetwork.Local, value);
    }

    public int Ps4RemoteResolutionIndex
    {
        get => ResolutionIndex(StreamConsole.Ps4, StreamNetwork.Remote);
        set => SetResolutionIndex(StreamConsole.Ps4, StreamNetwork.Remote, value);
    }

    public int Ps5LocalResolutionIndex
    {
        get => ResolutionIndex(StreamConsole.Ps5, StreamNetwork.Local);
        set => SetResolutionIndex(StreamConsole.Ps5, StreamNetwork.Local, value);
    }

    public int Ps5RemoteResolutionIndex
    {
        get => ResolutionIndex(StreamConsole.Ps5, StreamNetwork.Remote);
        set => SetResolutionIndex(StreamConsole.Ps5, StreamNetwork.Remote, value);
    }

    public int Ps4LocalFpsIndex
    {
        get => FpsIndex(StreamConsole.Ps4, StreamNetwork.Local);
        set => SetFpsIndex(StreamConsole.Ps4, StreamNetwork.Local, value);
    }

    public int Ps4RemoteFpsIndex
    {
        get => FpsIndex(StreamConsole.Ps4, StreamNetwork.Remote);
        set => SetFpsIndex(StreamConsole.Ps4, StreamNetwork.Remote, value);
    }

    public int Ps5LocalFpsIndex
    {
        get => FpsIndex(StreamConsole.Ps5, StreamNetwork.Local);
        set => SetFpsIndex(StreamConsole.Ps5, StreamNetwork.Local, value);
    }

    public int Ps5RemoteFpsIndex
    {
        get => FpsIndex(StreamConsole.Ps5, StreamNetwork.Remote);
        set => SetFpsIndex(StreamConsole.Ps5, StreamNetwork.Remote, value);
    }

    public double Ps4LocalBitrateMbps
    {
        get => BitrateMbps(StreamConsole.Ps4, StreamNetwork.Local);
        set => SetBitrateMbps(StreamConsole.Ps4, StreamNetwork.Local, (int)value);
    }

    public double Ps4RemoteBitrateMbps
    {
        get => BitrateMbps(StreamConsole.Ps4, StreamNetwork.Remote);
        set => SetBitrateMbps(StreamConsole.Ps4, StreamNetwork.Remote, (int)value);
    }

    public double Ps5LocalBitrateMbps
    {
        get => BitrateMbps(StreamConsole.Ps5, StreamNetwork.Local);
        set => SetBitrateMbps(StreamConsole.Ps5, StreamNetwork.Local, (int)value);
    }

    public double Ps5RemoteBitrateMbps
    {
        get => BitrateMbps(StreamConsole.Ps5, StreamNetwork.Remote);
        set => SetBitrateMbps(StreamConsole.Ps5, StreamNetwork.Remote, (int)value);
    }

    /// <summary>The four captions, value then the row's resolution default.</summary>
    public string Ps4LocalBitrateCaption
        => StreamBitrate.Caption(Ps4LocalBitrateMbps, Preset(StreamConsole.Ps4, StreamNetwork.Local));

    public string Ps4RemoteBitrateCaption
        => StreamBitrate.Caption(Ps4RemoteBitrateMbps, Preset(StreamConsole.Ps4, StreamNetwork.Remote));

    public string Ps5LocalBitrateCaption
        => StreamBitrate.Caption(Ps5LocalBitrateMbps, Preset(StreamConsole.Ps5, StreamNetwork.Local));

    public string Ps5RemoteBitrateCaption
        => StreamBitrate.Caption(Ps5RemoteBitrateMbps, Preset(StreamConsole.Ps5, StreamNetwork.Remote));

    /// <summary>
    /// Every row, because one change can move two: choosing a resolution moves that row's bitrate
    /// and its caption with it.
    /// </summary>
    private void RaiseVisibleRows()
    {
        Raise(nameof(Ps4LocalResolutionIndex));
        Raise(nameof(Ps4RemoteResolutionIndex));
        Raise(nameof(Ps5LocalResolutionIndex));
        Raise(nameof(Ps5RemoteResolutionIndex));
        Raise(nameof(Ps4LocalFpsIndex));
        Raise(nameof(Ps4RemoteFpsIndex));
        Raise(nameof(Ps5LocalFpsIndex));
        Raise(nameof(Ps5RemoteFpsIndex));
        Raise(nameof(Ps4LocalBitrateMbps));
        Raise(nameof(Ps4RemoteBitrateMbps));
        Raise(nameof(Ps5LocalBitrateMbps));
        Raise(nameof(Ps5RemoteBitrateMbps));
        Raise(nameof(Ps4LocalBitrateCaption));
        Raise(nameof(Ps4RemoteBitrateCaption));
        Raise(nameof(Ps5LocalBitrateCaption));
        Raise(nameof(Ps5RemoteBitrateCaption));
    }
}

/// <summary>
/// PP16: the Stream tab's rules where the QML and settings.cpp state them.
/// </summary>
public static class StreamSettingsSource
{
    /// <summary>The settings screen, or null outside a checkout.</summary>
    public static string? LocateQml() => GeneralSettingsSource.LocateQml();

    /// <summary>Whether a resolution row still reads and writes with the off-by-one.</summary>
    public static bool ResolutionIsOffByOne(string qml, StreamConsole console, StreamNetwork network)
    {
        ArgumentNullException.ThrowIfNull(qml);

        string property = PreferenceNames.For(
            Preferences.Find(StreamResolution.For(console, network).Key)!)!;

        return qml.Contains($"currentIndex: Chiaki.settings.{property} - 1", StringComparison.Ordinal)
            && qml.Contains($"Chiaki.settings.{property} = index + 1", StringComparison.Ordinal);
    }

    /// <summary>Whether choosing a resolution still zeroes the matching bitrate.</summary>
    public static bool ResolutionZeroesTheBitrate(string qml, StreamConsole console, StreamNetwork network)
    {
        ArgumentNullException.ThrowIfNull(qml);

        string property = PreferenceNames.For(
            Preferences.Find(StreamBitrate.KeyFor(console, network))!)!;

        return qml.Contains($"Chiaki.settings.{property} = 0", StringComparison.Ordinal);
    }

    /// <summary>Whether the frame rate is still the divide-by-thirty arithmetic.</summary>
    public static bool FpsIsArithmetic(string qml, StreamConsole console, StreamNetwork network)
    {
        ArgumentNullException.ThrowIfNull(qml);

        string property = PreferenceNames.For(Preferences.Find(StreamFps.KeyFor(console, network))!)!;

        return qml.Contains(
                $"currentIndex: (Chiaki.settings.{property} / {StreamFps.Step}) - 1",
                StringComparison.Ordinal)
            && qml.Contains(
                $"Chiaki.settings.{property} = (index + 1) * {StreamFps.Step}", StringComparison.Ordinal);
    }

    /// <summary>Whether the bitrate is still kbps in the store and Mbps on the slider.</summary>
    public static bool BitrateIsKbpsStoredAndMbpsShown(
        string qml, StreamConsole console, StreamNetwork network)
    {
        ArgumentNullException.ThrowIfNull(qml);

        string property = PreferenceNames.For(
            Preferences.Find(StreamBitrate.KeyFor(console, network))!)!;

        return qml.Contains(
                $"Chiaki.settings.{property} / {StreamBitrate.KbpsPerMbps} ? "
                    + $"(Chiaki.settings.{property} / {StreamBitrate.KbpsPerMbps}) : bitrate",
                StringComparison.Ordinal)
            && qml.Contains(
                $"Chiaki.settings.{property} = value * {StreamBitrate.KbpsPerMbps};",
                StringComparison.Ordinal);
    }

    /// <summary>Whether the per-resolution bitrate defaults are still these four.</summary>
    public static bool TheBitrateDefaultsAre(string qml)
    {
        ArgumentNullException.ThrowIfNull(qml);

        for (int preset = 1; preset <= 4; preset++)
        {
            if (!qml.Contains(
                    $"case {preset}: rate = {StreamBitrate.DefaultMbpsFor(preset)}; break;",
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether the store still holds the resolution as a word rather than a number.</summary>
    public static bool TheStoreHoldsAWord(string settingsCpp, StreamConsole console, StreamNetwork network)
    {
        ArgumentNullException.ThrowIfNull(settingsCpp);

        string key = StreamResolution.For(console, network).Key;
        return settingsCpp.Contains(
                $"settings.setValue(\"{key}\", resolutions[resolution]);", StringComparison.Ordinal)
            && settingsCpp.Contains("return resolutions.key(s, ", StringComparison.Ordinal);
    }

    /// <summary>Whether the four preset words are still these.</summary>
    public static bool ThePresetWordsAre(string settingsCpp)
    {
        ArgumentNullException.ThrowIfNull(settingsCpp);

        return StreamResolution.Stored.All(
            word => settingsCpp.Contains($"\"{word}\" }}", StringComparison.Ordinal)
                || settingsCpp.Contains($"\"{word}\" }},", StringComparison.Ordinal));
    }
}

