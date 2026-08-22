using System.Text.RegularExpressions;
using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP107: the check that makes an ACCEPTED defect expire loudly.
///
/// chiaki_reorder_queue_drop and chiaki_reorder_queue_peek are the two functions of that module
/// the C suite never calls, and both are broken - drop announces an element to the callback and
/// then leaves it in the queue, and peek writes through a seq_num pointer that takion hands it as
/// NULL. Neither is fixed here. lib/ is the half of this project that is not being ported, and
/// every drift check in this port asserts that the managed side matches it; patching it would
/// leave those checks asserting agreement with a libchiaki nobody else runs, which is a worse
/// thing to be wrong about than two functions on a path that is reached only when the data queue
/// is non-empty at the moment crypt initialises.
///
/// So the behaviour is reproduced, and the ACCEPTANCE is what gets asserted. Each fact below is
/// a reason the port behaves as it does. If upstream repairs one, the reason is gone and the
/// port's copy of the defect becomes the divergence instead - which is the moment to resume
/// PP107, and the only way to notice it is to have written it down as an assertion rather than
/// as a sentence in a rationale file nobody re-reads.
///
/// Read from the source and never run. Running the peek defect is the crash, and a test that
/// crashes the host reports nothing at all - which this port has already learned twice.
/// </summary>
public static partial class ReorderQueueSource
{
    /// <summary>Where the two functions live, relative to the repository root.</summary>
    public const string RelativePath = @"lib\src\reorderqueue.c";

    /// <summary>Where the one caller that passes NULL lives.</summary>
    public const string TakionRelativePath = @"lib\src\takion.c";

    /// <summary>The file, or null when this is not running out of a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>takion.c, or null outside a checkout.</summary>
    public static string? LocateTakion() => SanitizerSource.LocateRelative(TakionRelativePath);

    /// <summary>
    /// The body of a CHIAKI_EXPORT function, from its opening brace to the first closing brace in
    /// column zero. Crude, and sufficient: every function in this file is written that way, and a
    /// brace counter would be a second thing that can be wrong about a file the compiler already
    /// agrees with.
    /// </summary>
    public static string? BodyOf(string filePath, string function)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(function);

        string text = File.ReadAllText(filePath);
        Match open = Regex.Match(text, Regex.Escape(function) + @"\s*\([^)]*\)\s*\r?\n\{");
        if (!open.Success)
            return null;

        int start = open.Index + open.Length;
        Match close = Regex.Match(text[start..], @"\r?\n\}");
        return close.Success ? text.Substring(start, close.Index) : null;
    }

    /// <summary>The port's own seam, which is where the fini callbacks are deliberately lost.</summary>
    public const string ShimRelativePath = @"shim\chiaki_shim.c";

    /// <summary>The shim, or null outside a checkout.</summary>
    public static string? LocateShim() => SanitizerSource.LocateRelative(ShimRelativePath);

    /// <summary>
    /// Whether fini still reports every element left in the queue.
    ///
    /// PP23 needs this stated in the source rather than measured, because it is the one thing the
    /// oracle cannot answer: the shim clears the drop callback before calling fini, on purpose, so
    /// the native side reports nothing at teardown no matter what libchiaki does. See
    /// <see cref="ShimSuppressesFiniCallbacks"/> - the two predicates are a pair, and the managed
    /// queue follows libchiaki here rather than the seam.
    /// </summary>
    public static bool FiniReportsWhatIsStillQueued(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return body.Contains("for(uint64_t i=0; i<queue->count; i++)", StringComparison.Ordinal)
            && Regex.IsMatch(
                body,
                @"if\(entry->set\)\s*\r?\n\s*queue->drop_cb\(seq_num, entry->user, queue->drop_cb_user\);",
                RegexOptions.None,
                TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Whether the shim still clears the drop callback before fini, which is what makes the native
    /// queue silent at teardown. Deliberate, and documented there: a callback into managed code
    /// that is about to stop being interested is a lifetime bug waiting to happen.
    /// </summary>
    public static bool ShimSuppressesFiniCallbacks(string shimText)
    {
        ArgumentNullException.ThrowIfNull(shimText);

        return Regex.IsMatch(
            shimText,
            @"chiaki_reorder_queue_set_drop_cb\(&self->queue, NULL, NULL\);\s*\r?\n\s*"
                + @"chiaki_reorder_queue_fini\(&self->queue\);",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Whether drop still leaves the element in the queue: it clears no entry's set flag.
    ///
    /// This is the whole defect in one predicate. entry->set is what push tests for a duplicate,
    /// what pull tests before handing an element over, and what peek tests before reporting one -
    /// so an element dropped without clearing it is dropped only as far as the callback.
    /// </summary>
    public static bool DropLeavesTheEntrySet(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        // PP272: the entry has to be in what was handed over before "leaves it set" describes drop.
        return body.Contains("set", StringComparison.Ordinal)
            && !SetClearedRegex().IsMatch(body);
    }

    /// <summary>
    /// Whether drop's count-reduction loop is still unreachable.
    ///
    /// It is written `while(!entry->set)`, and the function has already returned when that was
    /// true. So the branch that would shrink the queue after dropping its last element cannot
    /// run - which is why count is unchanged as well as the element still being there. Both
    /// halves have to be present for this to be the same defect: a loop that stopped being
    /// guarded, or a guard that stopped being an early return, is a different function.
    /// </summary>
    public static bool DropCountLoopIsUnreachable(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        int guard = body.IndexOf("if(!entry->set)", StringComparison.Ordinal);
        int loop = body.IndexOf("while(!entry->set)", StringComparison.Ordinal);
        if (guard < 0 || loop < 0 || guard > loop)
            return false;

        // The guard has to be the early return, not a test that falls through to the loop.
        return Regex.IsMatch(
            body[guard..loop], @"if\(!entry->set\)\s*\r?\n\s*return;", RegexOptions.Singleline);
    }

    /// <summary>
    /// Whether peek still writes through both out-pointers with no null test.
    ///
    /// pull, in the same file and four lines above, guards both of its own with `if(seq_num)` and
    /// `if(user)`. peek does not, which is what makes this a slip rather than a contract: the two
    /// functions were written to the same shape and only one of them kept it.
    /// </summary>
    public static bool PeekWritesUnguarded(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return UnguardedWriteRegex().IsMatch(body)
            && !body.Contains("if(seq_num)", StringComparison.Ordinal)
            && !body.Contains("if(user)", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether takion still hands peek a NULL for the pointer it writes through unconditionally.
    ///
    /// This is the half that turns a missing guard into a crash, and it is on the re-check-MACs
    /// path: when crypt becomes available, everything already queued has its MAC re-checked, and
    /// the loop asks for the packet without wanting its sequence number.
    /// </summary>
    public static bool TakionPeeksWithNull(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return NullPeekRegex().IsMatch(takionText);
    }

    /// <summary>
    /// Whether takion still drops on a failed MAC, which is what makes the drop defect reachable
    /// rather than merely present: a packet the MAC rejected is announced as dropped and then
    /// delivered anyway.
    /// </summary>
    public static bool TakionDropsOnBadMac(string takionText)
    {
        ArgumentNullException.ThrowIfNull(takionText);
        return takionText.Contains("chiaki_reorder_queue_drop(&takion->data_queue, i)", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"entry\s*->\s*set\s*=\s*false")]
    private static partial Regex SetClearedRegex();

    [GeneratedRegex(@"\*\s*seq_num\s*=\s*seq_num_val\s*;\s*\r?\n\s*\*\s*user\s*=")]
    private static partial Regex UnguardedWriteRegex();

    [GeneratedRegex(@"chiaki_reorder_queue_peek\s*\(\s*&takion->data_queue\s*,\s*i\s*,\s*NULL\s*,")]
    private static partial Regex NullPeekRegex();
}
