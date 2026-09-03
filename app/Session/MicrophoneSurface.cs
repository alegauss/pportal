namespace ChiakiNg.Session;

/// <summary>Where the microphone already exists in this port, and what each place is.</summary>
/// <param name="Where">The file, relative to the repository root.</param>
/// <param name="Names">The text that proves it is there.</param>
/// <param name="What">What that place does about the microphone.</param>
public readonly record struct MicrophonePlace(string Where, string Names, string What);

/// <summary>
/// PP32: the microphone is ported everywhere except where the samples would come from.
///
/// §PP32's remaining criterion asks whether the managed host captures a microphone or whether this
/// line says it will not. The answer is neither, and the third case is the interesting one: the port
/// has already committed to the feature in four subsystems and captures nothing in any of them.
///
/// WHAT IS ALREADY HERE. A setting, start_mic_unmuted, declared in the preferences and bound to a
/// checkbox on the audio screen. A button on the in-stream menu, with the inversion the Qt client
/// had - lit when the microphone is NOT muted. A rule in the ring buffer, which drains the
/// microphone differently from playback because the capture path has no target queue size to stop
/// at. And the DualSense report, where the mic light and the mic mute travel as one write because a
/// pad left lit with an open mic is the state that matters to a person.
///
/// WHAT IS NOT. Nothing in app/ opens a capture device. No WASAPI, no NAudio, no MediaCapture, no
/// managed counterpart to chiaki_audio_sender. Four subsystems assume a stream of samples that
/// nothing produces.
///
/// SO THE LINE DOES NOT SAY IT WILL NOT - it says the opposite, four times, in shipped code. That
/// settles §PP32's question and turns it into work rather than a decision: the missing piece is a
/// capture device, and once there is one the encoder has an input, libopus's second consumer becomes
/// portable, and the speex stages PP32 opened with have something to run on.
///
/// This is a census and not a plan. Which capture API, and whether a noise stage follows it at all,
/// are separate questions - and the first of them is the one PP31's boundary is silent about,
/// because audio capture on Windows has several managed answers and video decode has none.
/// </summary>
public static class MicrophoneSurface
{
    /// <summary>
    /// The four places, each with the text that proves it.
    ///
    /// Text rather than a file list, because a file existing says nothing: what makes this a census
    /// is that each place demonstrably does something about the microphone, and the string is what
    /// a reader checks.
    /// </summary>
    public static IReadOnlyList<MicrophonePlace> Places { get; } =
    [
        new(
            @"app\Settings\QSettingsPreferences.cs",
            "settings/start_mic_unmuted",
            "the setting, declared with the rest of the profile's preferences"),
        new(
            @"app\Views\AudioSettingsView.xaml",
            "Start Mic Unmuted",
            "the checkbox that binds it, on the audio screen"),
        new(
            @"app\Views\StreamMenuView.xaml",
            "MicButton",
            "the in-stream button, lit when the microphone is NOT muted"),
        new(
            @"app\Session\DualSenseEffects.cs",
            "public static byte[] Microphone(bool muted)",
            "the pad report, where the light and the mute move together"),
    ];

    /// <summary>
    /// The ways a managed host could open a capture device, none of which appear.
    ///
    /// Named rather than counted, so a run that starts finding one says which - and so the list is
    /// something a later commit adds to on purpose rather than a pattern that quietly stops
    /// matching.
    /// </summary>
    public static IReadOnlyList<string> CaptureApis { get; } =
        ["NAudio", "WasapiCapture", "WaveInEvent", "IAudioCaptureClient", "MediaCapture"];

    /// <summary>The managed half, where the capture would be.</summary>
    public const string ManagedRelativePath = "app";

    /// <summary>This file, excluded: it names every API it is looking for.</summary>
    public const string CensusFileName = "MicrophoneSurface.cs";

    /// <summary>A file, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>app/, or null outside a checkout.</summary>
    public static string? LocateManaged() => SanitizerSource.LocateDirectory(ManagedRelativePath);

    /// <summary>The places whose proof text is no longer where this says it is.</summary>
    public static IReadOnlyList<MicrophonePlace> Missing()
    {
        var gone = new List<MicrophonePlace>();

        foreach (MicrophonePlace place in Places)
        {
            if (Locate(place.Where) is not { } path)
                continue;

            if (!File.ReadAllText(path).Contains(place.Names, StringComparison.Ordinal))
                gone.Add(place);
        }

        return gone;
    }

    /// <summary>
    /// The files under app/ that open a capture device, which is none of them.
    ///
    /// bin/ and obj/ skipped, and this file with them: a build output carrying a copy of a
    /// docstring is the same text counted twice, and the docstring here names every API by design.
    /// </summary>
    public static IReadOnlyList<string> FilesThatCapture()
    {
        if (LocateManaged() is not { } root)
            return [];

        var found = new List<string>();

        foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => !Path.GetFileName(p).Equals(CensusFileName, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            string source = File.ReadAllText(path);
            if (CaptureApis.Any(api => source.Contains(api, StringComparison.Ordinal)))
                found.Add(Path.GetRelativePath(root, path));
        }

        return found;
    }
}
