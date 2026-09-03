namespace ChiakiNg.Session;

/// <summary>
/// PP33: the second flip, which is the file itself leaving rather than session.c's asks.
///
/// <see cref="HolepunchDeletionOrder"/> is the first, and it has landed two of its three steps:
/// PP630 and PP631 taught the models both shapes, PP632 edited the C and the build. That order was
/// about session.c's nine ASKS. This one is about holepunch.c, and PP653 measured what is left of
/// it: everything compiles without the file, exactly one target fails to link, and the ten
/// references it fails on are all in this port's own shim.
///
/// SO THE BLOCKER IS A TESTING SEAM, WHICH IS THE WHOLE SHAPE OF THIS PLAN. PP654 moved the one
/// wrapper the host actually ran - the device id, whose entire content is a format - so the nine
/// that remain exist to let PP481's tests drive the C against a live console. They are not a
/// feature depending on the file; they are the oracle depending on it, and an oracle can be behind
/// the same option as the hardware it needs. PP632 did exactly that for gui/ and PP32 for speexdsp:
/// the thing that needs what a checkout does not have moves behind the flag that admits it.
///
/// THE HAZARD IS PP437's OWN, and it is why the first step is not optional. NativeSeam holds the
/// host's DllImports against what the shim HEADERS declare, because a header declaration is the
/// contract and the definition's spelling is the compiler's business. Put an #if around the nine
/// wrapper bodies and leave the header alone and that check stays green while the DLL loses nine
/// exports - and an EntryPoint is a string, so the first call throws
/// EntryPointNotFoundException inside a live session rather than at startup. That is the exact
/// failure PP437 was built to catch, arriving through the one door it cannot see.
///
/// The header and the bodies move together, therefore, and the host's imports with them. All twelve
/// declarations are in chiaki_shim.h today, which is what makes the hazard reachable and the fix
/// mechanical.
///
/// This is a plan and not a promise about effort, in PP623's words, and for its reason: a session
/// picking PP33 should meet three landable commits rather than one diff it cannot finish.
/// </summary>
public static class HolepunchFileOrder
{
    /// <summary>
    /// How far this order has got. Nothing yet - PP653 and PP654 are what made it writable, not
    /// steps of it.
    /// </summary>
    public static int Landed { get; }

    /// <summary>The option the file and its wrappers would sit behind.</summary>
    public const string ProposedOption = "CHIAKI_ENABLE_HOLEPUNCH";

    /// <summary>The header whose declarations are the contract NativeSeam reads.</summary>
    public const string ShimHeaderRelativePath = @"shim\chiaki_shim.h";

    /// <summary>The steps, in the order they land.</summary>
    public static IReadOnlyList<DeletionStage> Stages { get; } =
    [
        new(
            "Two-state the seam",
            "the nine wrappers' declarations, the host's DllImports for them and PP481's live tests "
                + "each learn a shape where the C is absent; nothing in lib/ moves, so the suite is "
                + "green after each",
            TouchesTheC: false),
        new(
            "Flip the build",
            "holepunch.c out of lib's default sources behind " + ProposedOption + ", curl and "
                + "json-c unlinked with it, and the nine wrappers gated in the shim's HEADER as well "
                + "as its body - one commit, no test file",
            TouchesTheC: true),
        new(
            "Turn the seam's prose",
            "the census entries and the predicates stay, because they are what notices the file "
                + "coming back; what is stale is the present tense around them",
            TouchesTheC: false),
    ];

    /// <summary>The one step that edits the C, named by its property as the first order names it.</summary>
    public static DeletionStage Flip => Stages.Single(one => one.TouchesTheC);

    /// <summary>
    /// What the flip has to carry, through the types that own each piece rather than as literals.
    ///
    /// The same reason the first order gives: a reference makes deleting one of these without
    /// revisiting the plan a build error rather than a plan that quietly stopped describing the work.
    /// </summary>
    public static IReadOnlyList<string> FlipCarries { get; } =
    [
        HolepunchShimSurface.SourceEntry,
        HolepunchShimSurface.ShimRelativePath,
        ShimHeaderRelativePath,
        ProposedOption,
    ];

    /// <summary>
    /// Why the header cannot be left behind, stated so a later reader meets the reason and not the
    /// rule.
    /// </summary>
    public const string HeaderHazard =
        "NativeSeam reads the shim's headers, so gating only the wrapper bodies leaves the census "
            + "green while the DLL loses the exports - and an EntryPoint is a string, so the first "
            + "call throws inside a live session rather than at startup";
}
