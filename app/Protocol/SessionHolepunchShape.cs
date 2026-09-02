using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>Which of the two shapes session.c is in.</summary>
public enum SessionShape
{
    /// <summary>It still makes the nine holepunch asks, which is every model's subject today.</summary>
    Asking,

    /// <summary>It names the handle nowhere, which is what PP33's flip commit leaves behind.</summary>
    Silent,
}

/// <summary>
/// PP630: the one question PP623's first step needs answered, asked once.
///
/// PP621 counted ten models that read session.c and quote its holepunch text as a specification.
/// PP623 settled that the deletion lands in three commits and that the middle one edits the C and no
/// test file - which is only possible if every one of those models already accepts BOTH shapes when
/// it arrives. Without something to share, ten models answer one question ten ways.
///
/// THE QUESTION IS ONE LINE OF TEXT. session.c either names <see cref="HolepunchDirection.Handle"/>
/// or it does not, and everything else follows: the nine asks, the guards around them, the fields
/// they read. There is no half-converted shape to model, because the flip is one commit.
///
/// WHAT MUST NOT HAPPEN IS A CHECK THAT STOPS ASKING. Every reader here already returns early
/// outside a checkout - a published host has no lib/ beside it - and a shape guard bolted on
/// carelessly makes that same early return happen on a tree that IS a checkout. That is a green
/// report from a check that declined to look, which is what PP56 and PP226 were both filed for. So
/// <see cref="AskingSource"/> is paired with <see cref="SilentSource"/>: on any tree exactly one of
/// them answers, and a model converted through this pair has assertions running either way.
/// </summary>
public static class SessionHolepunchShape
{
    /// <summary>
    /// The file both shapes are about.
    ///
    /// A named constant and not a literal in the call, which is PP278's rule: the corpus sweeps this
    /// assembly's string constants and asserts each repository path among them is on disk, and a
    /// path handed straight to a resolver is unreachable by any sweep.
    /// </summary>
    public const string SessionRelativePath = @"lib\src\session.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(SessionRelativePath);

    /// <summary>
    /// The shape a given source is in.
    ///
    /// Keyed on the handle rather than on any one of the nine calls, because the handle is what
    /// every one of them is reached through - the assignment, the guards, the fini sites. A file
    /// that had lost one call and kept the field is not a shape this models: PP623 makes the flip
    /// one commit precisely so that no such tree exists.
    /// </summary>
    public static SessionShape Of(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        return sessionSource.Contains(HolepunchDirection.Handle, StringComparison.Ordinal)
            ? SessionShape.Asking
            : SessionShape.Silent;
    }

    /// <summary>
    /// session.c while it still asks, or null - which a caller reads as "not for me to check".
    ///
    /// The whole point of the pair: a predicate written against the asking shape is never run
    /// against the silent one, and the model carrying it needs no branch of its own.
    /// </summary>
    public static string? AskingSource()
    {
        string? source = Read();
        return source is not null && Of(source) == SessionShape.Asking ? source : null;
    }

    /// <summary>
    /// session.c once it has stopped, or null.
    ///
    /// The counterpart, and the reason the guard is not a way of not looking: the assertions that
    /// run on this side are what say the deletion actually happened, and they are a check rather
    /// than an absence.
    /// </summary>
    public static string? SilentSource()
    {
        string? source = Read();
        return source is not null && Of(source) == SessionShape.Silent ? source : null;
    }

    /// <summary>
    /// Whether exactly one of the two answers on this tree.
    ///
    /// Asserted rather than assumed, because the failure it guards against is silent in both
    /// directions: two answers is a shape nothing modelled, and none at all is every check on both
    /// sides declining to look while the file sits there.
    /// </summary>
    public static bool ExactlyOneShapeAnswers()
        => Locate() is null || (AskingSource() is null) != (SilentSource() is null);

    /// <summary>
    /// What must be gone from the silent shape: the handle, and the export that carries no prefix.
    ///
    /// PP564's finding is why the second is here. `holepunch_session_create_offer` has no `chiaki_`
    /// in front of it, so a sweep keyed on that prefix - which is how a reader finds these - walks
    /// straight past it, and a flip commit that missed it would leave session.c calling into the
    /// file it was deleting.
    /// </summary>
    public static IReadOnlyList<string> GoneWhenSilent { get; } =
        [HolepunchDirection.Handle, HolepunchConsumers.UnprefixedExport];

    /// <summary>Whatever is still there that the silent shape must not have.</summary>
    public static IReadOnlyList<string> StillPresentIn(string sessionSource)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);

        return
        [
            .. GoneWhenSilent.Where(text =>
                sessionSource.Contains(text, StringComparison.Ordinal))
        ];
    }

    private static string? Read() => Locate() is { } path ? File.ReadAllText(path) : null;
}
