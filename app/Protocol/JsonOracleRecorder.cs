using System.Globalization;
using System.Text.Json;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP33: walks <see cref="JsonOracleCases"/> through the real json-c and writes down every answer.
///
/// Runs only on a build that carries the oracle, which is the point: what it produces is evidence
/// from the library rather than a table somebody typed. A build without it refuses rather than
/// writing a file of nulls, because a recording that silently recorded nothing is worse than none -
/// the tests would then compare against it and pass.
/// </summary>
public static class JsonOracleRecorder
{
    /// <summary>What a recording attempt did.</summary>
    public enum Outcome
    {
        /// <summary>Written, and the file is the second opinion.</summary>
        Recorded,

        /// <summary>This build has no json-c, so there was nothing to ask.</summary>
        NoOracle,

        /// <summary>json-c refused a document the cases expect it to parse.</summary>
        OracleDisagreed,
    }

    /// <summary>
    /// Asks json-c every case and returns what it said.
    ///
    /// Null where this build has no oracle. Every accessor is read for every resolved node, because
    /// the coercions are the finding: <c>json_object_get_int</c> on a string parses it, on a double
    /// truncates it, and on a missing node answers zero rather than failing.
    /// </summary>
    public static JsonOracleRecording? Take()
    {
        if (!DeletedLibraryOracles.JsonOracleIsAvailable())
            return null;

        var rows = new List<JsonOracleRow>();

        foreach ((string json, string path) in JsonOracleCases.Rows)
        {
            using NativeJson? document = NativeJson.Parse(json);
            if (document is null)
            {
                // A case whose document json-c will not parse is a broken case, not a finding. It
                // is recorded as absent so the caller can see WHICH one rather than a count.
                rows.Add(new JsonOracleRow(json, path, false, null, null, null, null, null, null));
                continue;
            }

            IntPtr node = document.Pointer(path);

            // THE ACCESSORS ARE READ FOR AN ABSENT NODE TOO, because json-c answers for one: every
            // one of them takes NULL and returns a value rather than failing - null, 0, false, -1 -
            // and holepunch.c reads fields it never checked for presence. Recording only the
            // presence would lose exactly the behaviour a caller meets by accident.
            rows.Add(new JsonOracleRow(
                json,
                path,
                node != IntPtr.Zero,
                node == IntPtr.Zero ? null : NativeJson.TypeOf(node),
                NativeJson.String(node),
                NativeJson.Int(node),
                NativeJson.Int64(node),
                NativeJson.Bool(node),
                NativeJson.ArrayLength(node)));
        }

        var documents = new List<JsonDocumentAnswer>();

        foreach (string text in JsonOracleCases.Documents)
        {
            using NativeJson? document = NativeJson.Parse(text);
            documents.Add(new JsonDocumentAnswer(
                text,
                document is not null,
                document is null ? null : NativeJson.String(document.Root)));
        }

        var runs = new List<JsonTokenerRun>();

        foreach ((string name, string[] frames, bool[] resetBefore) in JsonOracleCases.Sequences)
        {
            using NativeJsonTokener? tokener = NativeJsonTokener.Create();
            if (tokener is null)
                return null;

            var parsed = new List<bool>();
            var errors = new List<int>();

            for (int i = 0; i < frames.Length; i++)
            {
                if (resetBefore[i])
                    tokener.Reset();

                using NativeJson? got = tokener.Parse(frames[i]);
                parsed.Add(got is not null);
                errors.Add(tokener.Error);
            }

            runs.Add(new JsonTokenerRun(name, frames, resetBefore, parsed, errors));
        }

        return new JsonOracleRecording(
            DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            rows,
            documents,
            runs);
    }

    /// <summary>
    /// Takes a recording and writes it where the tests read it.
    /// </summary>
    /// <param name="path">Where to write, or null for <see cref="JsonOracleRecording.RelativePath"/>.</param>
    public static Outcome Write(string? path = null)
    {
        JsonOracleRecording? taken = Take();
        if (taken is null)
            return Outcome.NoOracle;

        // Every document the cases hand over must parse, apart from the rows whose PATH is the
        // thing that is absent. A recording taken from a json-c that refused the documents would be
        // a file of absences that every comparison then agrees with.
        if (!taken.Rows.Any(row => row.Present))
            return Outcome.OracleDisagreed;

        string destination = path ?? Destination();
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonSerializer.Serialize(taken, JsonOracleRecording.Format));

        return Outcome.Recorded;
    }

    /// <summary>
    /// Where the recording goes: beside the checkout's other oracles, or the working directory.
    ///
    /// The file has to be committed, so the checkout is the only useful destination - but a run from
    /// outside one should produce the file rather than throw, and say where it put it.
    /// </summary>
    public static string Destination()
        => SanitizerSource.RepositoryRoot() is { } root
            ? Path.Combine(root, JsonOracleRecording.RelativePath)
            : Path.GetFullPath(Path.GetFileName(JsonOracleRecording.RelativePath));
}
