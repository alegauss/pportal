using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP406: which of the threading primitives can fail at all, on the one platform this port builds.
///
/// lib/src/thread.c is the Windows implementation. <c>chiaki_mutex_lock</c> is
/// <c>EnterCriticalSection</c> and then a single <c>return CHIAKI_ERR_SUCCESS;</c> - there is no
/// other return statement in the function. The same is true of the mutex init, the unlock, the cond
/// init and the signal. An assert on any of those inspects a value with one possible spelling.
///
/// THAT IS NOT A REASON TO STOP ASSERTING THEM, and this class does not say it is. It is what makes
/// PP404's ceiling readable: a count of 53 that is mostly unreachable branches invites corrections
/// with no effect, and hides the ones that would have one.
///
/// READ, NOT REMEMBERED. Every answer here comes out of the C, so a primitive that grows a failure
/// path moves its call sites into the count without anybody editing this file. That is the whole
/// reason it is a reader rather than a list of names.
/// </summary>
public static class ThreadPrimitives
{
    /// <summary>Where the threading primitives are implemented.</summary>
    public const string ThreadPath = @"lib\src\thread.c";

    /// <summary>And the stop pipe, which lives apart from them.</summary>
    public const string StopPipePath = @"lib\src\stoppipe.c";

    /// <summary>The files a primitive may be defined in.</summary>
    public static IReadOnlyList<string> Sources { get; } = [ThreadPath, StopPipePath];

    /// <summary>The success constant, which is what a function with no failure path returns.</summary>
    public const string Success = "CHIAKI_ERR_SUCCESS";

    /// <summary>
    /// Whether <paramref name="name"/> has any return that is not the success constant.
    ///
    /// A primitive this cannot find is reported as able to fail. That is the safe direction: an
    /// unreadable definition should widen the count that gets looked at, not narrow it.
    /// </summary>
    public static bool CanFail(string name, string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(source);

        string? body = CFunction.Body(source, $"CHIAKI_EXPORT ChiakiErrorCode {name}(");
        if (body is null)
            return true;

        // PP359: the trailing parenthesis in the signature above is what keeps a name from matching
        // a longer one that contains it - the lock against its try-variant, the cond wait against
        // its pred-variant. Neither of those longer names is spelled out here, because PP290 records
        // one of them as an export nothing in the tree refers to and its sweep matches bare
        // identifiers in comments too.
        string code = CCall.Code(body);

        foreach (string statement in code.Split("return ", StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            string returned = statement.Split(';', 2)[0].Trim();
            if (returned != Success)
                return true;
        }

        return false;
    }

    /// <summary>The same, reading whichever of the two files defines it.</summary>
    /// <returns>True where the primitive can fail, or where nothing here defines it.</returns>
    public static bool CanFail(string name, IReadOnlyDictionary<string, string> sources)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(sources);

        foreach (string source in sources.Values)
        {
            if (CFunction.Body(source, $"CHIAKI_EXPORT ChiakiErrorCode {name}(") is not null)
                return CanFail(name, source);
        }

        return true;
    }

    /// <summary>Both files, keyed by path, or null outside a checkout.</summary>
    public static IReadOnlyDictionary<string, string>? Read()
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string relative in Sources)
        {
            string? path = SanitizerSource.LocateRelative(relative);
            if (path is null)
                return null;

            sources[relative] = File.ReadAllText(path);
        }

        return sources;
    }
}
