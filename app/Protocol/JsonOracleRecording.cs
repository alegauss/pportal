using System.Text.Json;
using System.Text.Json.Serialization;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>What json-c answered for one document and one path, accessor by accessor.</summary>
/// <param name="Json">The document, as it was handed to the parser.</param>
/// <param name="Path">The JSON pointer, which is what selects the node.</param>
/// <param name="Present">Whether json-c resolved the pointer at all. Everything below is null when it did not.</param>
/// <param name="Type">json_object_get_type, as this port's enum names it.</param>
/// <param name="String">json_object_get_string, which is not null for a node of any type.</param>
/// <param name="Int">json_object_get_int, which COERCES rather than refusing.</param>
/// <param name="Int64">json_object_get_int64, which is a different coercion at the edges.</param>
/// <param name="Bool">json_object_get_boolean.</param>
/// <param name="ArrayLength">json_object_array_length, which answers for a non-array too.</param>
public readonly record struct JsonOracleRow(
    string Json,
    string Path,
    bool Present,
    JsonCType? Type,
    string? String,
    int? Int,
    long? Int64,
    bool? Bool,
    int? ArrayLength);

/// <summary>One sequence of frames through a single tokener, and what came back from each.</summary>
/// <param name="Name">What the sequence demonstrates, so a failure names the behaviour.</param>
/// <param name="Frames">
/// The frames, in order, through ONE tokener. The order is the whole point: what this records is a
/// state machine, and a frame's answer depends on every frame before it.
/// </param>
/// <param name="ResetBefore">
/// Whether json_tokener_reset is called before each frame. holepunch.c never calls it, which is
/// what makes the poisoning permanent there - so the one sequence that does is the control.
/// </param>
/// <param name="Parsed">Whether each frame yielded a document.</param>
/// <param name="Errors">json_tokener_get_error after each frame.</param>
public readonly record struct JsonTokenerRun(
    string Name,
    IReadOnlyList<string> Frames,
    IReadOnlyList<bool> ResetBefore,
    IReadOnlyList<bool> Parsed,
    IReadOnlyList<int> Errors);

/// <summary>What json-c made of one whole text, before any accessor is reached.</summary>
/// <param name="Text">The document, as it was handed to json_tokener_parse.</param>
/// <param name="Parsed">Whether json-c accepted it at all.</param>
/// <param name="RootString">
/// json_object_get_string of the root, which for an object or an array is json-c's own
/// SERIALISATION - spaced, and with a slash escaped. Neither is System.Text.Json's default.
/// </param>
public readonly record struct JsonDocumentAnswer(string Text, bool Parsed, string? RootString);

/// <summary>Everything the json-c oracle was asked, and everything it said.</summary>
/// <param name="Recorded">When the run happened, so a reader can tell how old the second opinion is.</param>
/// <param name="Rows">The accessor differential.</param>
/// <param name="Documents">
/// What json-c made of each whole text in <see cref="JsonOracleCases.Documents"/>.
///
/// The claims these carry are about the LEXER, which is the one place this port deliberately
/// differs - json-c accepts a trailing comma, `NaN` and `0x1f`, and this refuses all three. So what
/// is recorded is what json-c did, never what the port is expected to do.
/// </param>
/// <param name="Runs">The tokener's state machine across frame sequences.</param>
public sealed record JsonOracleRecording(
    string Recorded,
    IReadOnlyList<JsonOracleRow> Rows,
    IReadOnlyList<JsonDocumentAnswer> Documents,
    IReadOnlyList<JsonTokenerRun> Runs)
{
    /// <summary>
    /// PP33: the second opinion, written down so the library can leave without taking it.
    ///
    /// PP663 put json-c behind a flag and the suite is green either way, which left twenty-three
    /// assertions that decline on an ordinary build. DeletedLibraryOracles says what they are:
    /// "what declines when the oracle goes is the second opinion, which is the only part that needs
    /// json-c present". PP33's fourth criterion then says the FILE stays until those wrappers have
    /// an answer.
    ///
    /// THIS IS THE ANSWER, and it is neither of the two that were on the table. Keeping the library
    /// for a comparison nobody runs by default is what PP663 already decided against; deleting it
    /// and losing the comparison spends a reference implementation to save a dependency. Recording
    /// what it said costs neither: json-c's answers do not change, because json-c is not being
    /// developed here and the rows are inputs this port chose.
    ///
    /// It is the port's own pattern. PP297 records an exchange and replays it against managed
    /// participants; every spike commits its release-*.json rather than asking a reader to trust a
    /// number in prose. A recorded oracle is the same shape: evidence taken once, from the real
    /// thing, and held against forever.
    ///
    /// WHAT KEEPS IT FROM BECOMING A FOSSIL. A build that HAS the oracle checks the recording
    /// against it before using it, so a recording that drifted from json-c fails on the one
    /// configuration that can tell - and until the file is deleted, that configuration exists.
    /// </summary>
    public const string RelativePath = @"tests\oracles\json-c.json";

    /// <summary>The recording, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>Indented and with the enum spelled out, because this file is read by people too.</summary>
    public static JsonSerializerOptions Format { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The recording as it sits in the tree, or null where it is absent or unreadable.</summary>
    public static JsonOracleRecording? Read()
    {
        if (Locate() is not { } path)
            return null;

        try
        {
            return JsonSerializer.Deserialize<JsonOracleRecording>(File.ReadAllText(path), Format);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>The row for one document and path, or null where the recording does not hold it.</summary>
    public JsonOracleRow? Row(string json, string path)
        => Rows.FirstOrDefault(row => row.Json == json && row.Path == path) is { Json: not null } found
            ? found
            : null;

    /// <summary>The run of that name, or null where the recording does not hold it.</summary>
    public JsonTokenerRun? Run(string name)
        => Runs.FirstOrDefault(run => run.Name == name) is { Name: not null } found ? found : null;

    /// <summary>What json-c made of one whole text, or null where the recording does not hold it.</summary>
    public JsonDocumentAnswer? Document(string text)
        => Documents.FirstOrDefault(one => one.Text == text) is { Text: not null } found ? found : null;
}
