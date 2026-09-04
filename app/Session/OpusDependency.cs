namespace ChiakiNg.Session;

/// <summary>PP694: why one file calls libopus, which is what a deletion has to answer for.</summary>
public enum OpusCallerRole
{
    /// <summary>The playback path, unported: PP32 chose not to adopt managed decode on its own.</summary>
    Playback,

    /// <summary>The microphone's, which is what PP694 replaces.</summary>
    Microphone,

    /// <summary>
    /// A wrapper that exists to compare the port against the C, so it leaves with the C.
    ///
    /// A different kind of holding, and the census would lie by counting it the same way: an oracle
    /// is not a consumer the port has to replace, it is one the port needs until it has.
    /// </summary>
    Oracle,
}

/// <summary>One file that calls into libopus, and why it is there.</summary>
public readonly record struct OpusCaller(string File, OpusCallerRole Role);

/// <summary>
/// PP32: what libopus is actually holding, which is not what the decoder question assumed.
///
/// PP651 measured the decode side and found cost decides nothing: managed Opus costs 1.58x the
/// native median at 24.9us a frame, which is a quarter of one percent of a 10ms frame. So the
/// choice falls to the dependency - one native binary in the package against one managed reference -
/// and that half looked like arithmetic until it was counted.
///
/// IT IS NOT ONE CONSUMER, IT IS TWO. opusdecoder.c is the playback path and opusencoder.c is the
/// microphone's, and both call into libopus. Porting the decoder removes no dependency at all: the
/// library stays for the encoder, the DLL stays in the package, and what the port would have bought
/// is a decoder that costs more and jitters more for nothing.
///
/// AND THE ENCODER IS ON THE PATH THAT HAS NO INPUT. §PP32's other criterion is that the managed
/// host captures no microphone, so the second consumer cannot be ported by porting anything - there
/// is nothing to encode. The dependency leaves when the microphone question is answered, and not
/// before.
///
/// SO THE MEASUREMENT DECIDED LESS THAN IT LOOKED LIKE DECIDING, and that is the finding rather
/// than a disappointment: managed Opus is adequate, and adopting it for the decoder alone is a cost
/// with no saving attached. The two halves of the audio path move together or neither moves.
///
/// audiosender.c is NOT a third consumer, and it reads like one. It names its parameter
/// <c>opus_sender</c> and its buffers frame this and frame that, and it calls nothing in the
/// library: it carries already-encoded frames. A count taken by grepping for "opus" gets three.
///
/// PP694: AND THE COUNT WAS TAKEN OVER lib/src ALONE, which is one directory short of the question.
/// A census of what holds a library has to read everything that links it, the way PP692's did for
/// gf-complete: <see cref="SweptDirectories"/> is lib/src, shim and test now, and the answer changed
/// when it was. <see cref="Callers"/> carries the roles, and <see cref="StillHoldingIt"/> is the
/// sentence PP32 was waiting for - what remains after the microphone's encoder is managed.
/// </summary>
public static class OpusDependency
{
    /// <summary>Where the option is declared.</summary>
    public const string RootCMakeRelativePath = "CMakeLists.txt";

    /// <summary>Where the library is found and linked.</summary>
    public const string LibCMakeRelativePath = @"lib\CMakeLists.txt";

    /// <summary>The option that carries it, default ON.</summary>
    public const string Option = "CHIAKI_LIB_ENABLE_OPUS";

    /// <summary>lib/src, where the consumers are.</summary>
    public const string SourceRelativePath = @"lib\src";

    /// <summary>
    /// The two files that call into libopus, and the reason the dependency does not leave with one.
    /// </summary>
    public static IReadOnlyList<string> Consumers { get; } = ["opusdecoder.c", "opusencoder.c"];

    /// <summary>
    /// The file that names opus everywhere and calls it nowhere.
    ///
    /// Named because it is the trap: a census taken by searching for the word finds three consumers
    /// and concludes the encoder is one of two rather than one of one.
    /// </summary>
    public const string CarriesEncodedFramesOnly = "audiosender.c";

    /// <summary>
    /// PP694: everything that links libopus, which is three directories and not one.
    ///
    /// lib/src is where PP32 looked. The shim links chiaki-lib and can therefore call the library
    /// directly, and the C suite links it too - so a census that stopped at lib/src was answering
    /// about a module rather than about a build. PP692 made the same correction one library over.
    /// </summary>
    public static IReadOnlyList<string> SweptDirectories { get; } = [@"lib\src", "shim", "test"];

    /// <summary>
    /// Every file that calls into libopus, with what holds it there.
    ///
    /// Declared and then swept against: being a consumer is a fact the sweep finds, and WHY each one
    /// is there is a judgement about what the deletion has to answer for.
    /// </summary>
    public static IReadOnlyList<OpusCaller> Callers { get; } =
    [
        new("opusdecoder.c", OpusCallerRole.Playback),
        new("opusencoder.c", OpusCallerRole.Microphone),
        new("chiaki_shim.c", OpusCallerRole.Oracle),
    ];

    /// <summary>
    /// What still holds libopus once the microphone's encoder is managed.
    ///
    /// The sentence PP32's own line was waiting for, and it is not "nothing". The playback path is
    /// unported - PP651 measured managed decode at a quarter of a percent of a frame and PP32 chose
    /// not to adopt it for the decoder alone, because doing so removed no dependency - so
    /// opusdecoder.c holds the library on its own. The shim's wrappers hold it too and are a
    /// different kind of holding: they exist to compare the port against the C, so they leave with
    /// the C rather than before it, which is the shape PP663 gave every other oracle here.
    /// </summary>
    public static IReadOnlyList<OpusCaller> StillHoldingIt { get; } =
    [
        .. Callers.Where(one => one.Role != OpusCallerRole.Microphone),
    ];

    /// <summary>A file, or null outside a checkout.</summary>
    public static string? Locate(string relative) => SanitizerSource.LocateRelative(relative);

    /// <summary>lib/src, or null outside a checkout.</summary>
    public static string? LocateSource() => SanitizerSource.LocateDirectory(SourceRelativePath);

    /// <summary>One of the swept directories, or null outside a checkout.</summary>
    public static string? LocateDirectory(string relative)
        => SanitizerSource.LocateDirectory(relative);

    /// <summary>
    /// The files across every swept directory that CALL libopus, by name and ordered.
    ///
    /// The same reader <see cref="CallingFiles"/> uses, over three directories instead of one. A
    /// directory that is not in the checkout is skipped rather than reported empty, because an
    /// absent tree and a tree with no callers are different answers.
    /// </summary>
    public static IReadOnlyList<string> CallingFilesEverywhere()
    {
        var found = new List<string>();

        foreach (string relative in SweptDirectories)
        {
            if (LocateDirectory(relative) is not { } root)
                continue;

            foreach (string path in Directory.EnumerateFiles(root, "*.c", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal))
            {
                if (CallsOpus(File.ReadAllText(path)))
                    found.Add(Path.GetFileName(path));
            }
        }

        return [.. found.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The files under lib/src that CALL libopus, by name and ordered.
    ///
    /// Found by the call prefix rather than by the word: <c>opus_</c> followed by a letter is a
    /// function in the library's namespace, and a variable called opus_sender is not one because
    /// nothing calls it. Compacted first, so a comment naming a call is not a call.
    /// </summary>
    public static IReadOnlyList<string> CallingFiles()
    {
        if (LocateSource() is not { } root)
            return [];

        var found = new List<string>();

        foreach (string path in Directory.EnumerateFiles(root, "*.c", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            if (CallsOpus(File.ReadAllText(path)))
                found.Add(Path.GetFileName(path));
        }

        return found;
    }

    /// <summary>
    /// Whether a source calls into libopus, as opposed to naming it.
    ///
    /// The four entry points this port's two consumers use, plus the two destroys. A list rather
    /// than a pattern: <c>opus_</c> as a prefix matches opus_sender_size, which is the whole
    /// distinction this method exists to make.
    ///
    /// <see cref="CCall.Code"/> BEFORE <see cref="CCall.Compact"/>, and the difference is the one
    /// CCall documents: Compact collapses whitespace and keeps everything it is given, so a comment
    /// naming a call reads as one. Code is the reader that drops comments, and a census of who calls
    /// a library is exactly the case it was written for.
    /// </summary>
    public static bool CallsOpus(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Compact(CCall.Code(source));

        return Entries.Any(entry => code.Contains(entry, StringComparison.Ordinal));
    }

    /// <summary>The library entry points this port's C reaches, spelled as calls.</summary>
    public static IReadOnlyList<string> Entries { get; } =
    [
        "opus_decoder_create(",
        "opus_decoder_destroy(",
        "opus_decode(",
        "opus_encoder_create(",
        "opus_encoder_destroy(",
        "opus_encode(",
    ];
}
