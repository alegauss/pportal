namespace ChiakiNg.Protocol;

/// <summary>What a dequeue releases, in the order it releases them.</summary>
public enum Released
{
    /// <summary>The parsed document.</summary>
    Document,

    /// <summary>The text it was parsed from.</summary>
    TextBuffer,

    /// <summary>And the node holding both.</summary>
    Node,
}

/// <summary>Where a node's forward link is cleared.</summary>
public enum LinkClearedBy
{
    /// <summary>The constructor, before the node is handed over.</summary>
    Constructor,

    /// <summary>The enqueue, as it takes ownership.</summary>
    Enqueue,

    /// <summary>Neither.</summary>
    Nothing,
}

/// <summary>
/// PP262: what the notification queue owns.
///
/// PP212 ported what the queue DOES - the waiting, the waking, the scan. This is the other half.
///
/// THE LINK INVARIANT LIVES IN THE CONSTRUCTOR, NOT THE ENQUEUE. Adding to an empty queue assigns
/// both ends from the new node without touching its forward link; adding to a non-empty one writes
/// the tail and leaves it alone as well. Nothing in the enqueue makes the new node the last. That
/// property comes entirely from the constructor, which clears the link before handing the node over
/// - so a node built any other way joins the queue with whatever it pointed at still attached, and
/// nothing before the walk would notice. <see cref="ClearsTheLink"/> says which one does it.
///
/// DEQUEUING RELEASES THREE THINGS AND WRITES TO TWO OF THEM FIRST. Two fields of the node are set
/// to null on the lines before the node itself is freed. Harmless, and carried as written because a
/// reader hunting a use-after-free finds these first and has to rule them out.
///
/// A dequeue from an empty queue returns quietly - nothing separates "removed one" from "there was
/// none", which is why PP212's own dequeue answers with a bool and this one returns void.
///
/// AND THE SUBSTRING REMOVER TAKES ONLY THE FIRST MATCH. It is called twice in a row, once per URL
/// scheme, which is what PP239 measured as a scheme being removed from wherever it appears: two
/// calls, one match each. See <see cref="RemoveFirst"/>.
/// </summary>
public static class NotificationOwnership
{
    /// <summary>Which step clears a new node's forward link.</summary>
    public const LinkClearedBy ClearsTheLink = LinkClearedBy.Constructor;

    /// <summary>Whether the enqueue makes a node the last one. It does not.</summary>
    public static bool EnqueueMakesItLast => ClearsTheLink == LinkClearedBy.Enqueue;

    /// <summary>What a dequeue releases, in order.</summary>
    public static IReadOnlyList<Released> ReleasedInOrder { get; } =
        [Released.Document, Released.TextBuffer, Released.Node];

    /// <summary>Whether a field of the node is written before the node is freed.</summary>
    public static bool WrittenBeforeTheFree(Released what) => what != Released.Node;

    /// <summary>Whether a dequeue tells the caller anything. It does not.</summary>
    public const bool DequeueReports = false;

    /// <summary>
    /// The substring removal, which takes the FIRST match and no others.
    /// </summary>
    public static string RemoveFirst(string text, string substring)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(substring);

        if (substring.Length == 0)
            return text;

        int at = text.IndexOf(substring, StringComparison.Ordinal);
        return at < 0 ? text : text.Remove(at, substring.Length);
    }

    /// <summary>The two schemes stripped, in the order the core strips them.</summary>
    public static IReadOnlyList<string> SchemesStripped { get; } = ["https://", "http://"];

    /// <summary>
    /// Both calls, in order - which is what PP239's stripping is made of.
    /// </summary>
    public static string StripSchemes(string url)
    {
        ArgumentNullException.ThrowIfNull(url);

        string stripped = url;
        foreach (string scheme in SchemesStripped)
            stripped = RemoveFirst(stripped, scheme);

        return stripped;
    }
}

/// <summary>
/// PP262: the queue's primitives where the core writes them.
/// </summary>
public static class NotificationOwnershipSource
{
    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => PortGuessingSource.Locate();

    /// <summary>
    /// THE FINDING. Whether the enqueue still leaves the forward link alone on both branches.
    /// </summary>
    public static bool TheEnqueueStillNeverClearsTheLink(string core)
    {
        string body = Between(core, "static void enqueueNq(", "\n/**");

        return body.Length > 0
            && body.Contains("nq->front = nq->rear = notif;", StringComparison.Ordinal)
            && body.Contains("nq->rear->next = notif;", StringComparison.Ordinal)
            && !body.Contains("notif->next = NULL;", StringComparison.Ordinal);
    }

    /// <summary>And whether the constructor still does it instead.</summary>
    public static bool TheConstructorStillClearsIt(string core)
        => Between(core, "static Notification* newNotification(", "\n/**")
            .Contains("notif->next = NULL;", StringComparison.Ordinal);

    /// <summary>Whether the dequeue still releases the three, in that order.</summary>
    public static bool TheDequeueStillReleasesThree(string core)
    {
        string body = Between(core, "static void dequeueNq(", "\n/**");

        int document = body.IndexOf("json_object_put(notif->json);", StringComparison.Ordinal);
        int text = body.IndexOf("free(notif->json_buf);", StringComparison.Ordinal);
        int node = body.IndexOf("free(notif);", StringComparison.Ordinal);

        return document >= 0 && text > document && node > text;
    }

    /// <summary>And whether it still writes to the node before freeing it.</summary>
    public static bool TheDequeueStillWritesBeforeFreeing(string core)
    {
        string body = Between(core, "static void dequeueNq(", "\n/**");

        int node = body.IndexOf("free(notif);", StringComparison.Ordinal);
        if (node < 0)
            return false;

        string before = body[..node];

        return before.Contains("notif->json = NULL;", StringComparison.Ordinal)
            && before.Contains("notif->json_buf = NULL;", StringComparison.Ordinal);
    }

    /// <summary>Whether an empty dequeue still says nothing.</summary>
    public static bool AnEmptyDequeueStillSaysNothing(string core)
    {
        string body = Between(core, "static void dequeueNq(", "\n/**");

        return body.Contains("if(nq->front == NULL)", StringComparison.Ordinal)
            && body.Contains("        return;", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the remover still takes one match, and is still called once per scheme.
    /// </summary>
    public static bool TheRemoverIsStillCalledOncePerScheme(string core)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        foreach (string scheme in NotificationOwnership.SchemesStripped)
        {
            if (!text.Contains($"remove_substring(host_url, \"{scheme}\");", StringComparison.Ordinal))
                return false;
        }

        // One strstr, one move - no loop.
        string body = Between(core, "static void remove_substring(", "\n}");

        return body.Contains("char *start = strstr(str, substring);", StringComparison.Ordinal)
            && !body.Contains("while", StringComparison.Ordinal)
            && !body.Contains("for(", StringComparison.Ordinal);
    }

    /// <summary>One function's body, by its definition and what ends it.</summary>
    private static string Between(string core, string opens, string closes)
    {
        ArgumentNullException.ThrowIfNull(core);

        string text = core.Replace("\r\n", "\n", StringComparison.Ordinal);

        // LAST, and spelled as the definition spells it - PP258's lesson.
        int start = text.LastIndexOf(opens, StringComparison.Ordinal);
        if (start < 0)
            return "";

        int end = text.IndexOf(closes, start + opens.Length, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
