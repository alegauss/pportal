using System.Reflection;

namespace ChiakiNg.Protocol;

/// <summary>How one of PP429's nine call sites reaches the managed interface.</summary>
/// <param name="Callee">The C function, as PP429 names it.</param>
/// <param name="Method">The <see cref="IHolepunchSession"/> method that stands for it.</param>
/// <param name="SharesTheMethod">
/// Whether another of the nine reaches the same method - true for the two socket calls and the two
/// finis, which is why nine sites are seven methods.
/// </param>
public readonly record struct SeamJoin(string Callee, string Method, bool SharesTheMethod);

/// <summary>
/// PP480, under PP340: the join between PP429's nine call sites and PP479's interface.
///
/// PP429 wrote down the nine and said what the list was for: "A TENTH WOULD CHANGE PP33 IN SILENCE...
/// A call site added grows that job without either line moving, and PP33's own `remaining` query would
/// not notice." PP479 then introduced <see cref="IHolepunchSession"/> and left it joined to nothing -
/// so a tenth site could now also arrive with no method to answer it, or a method could be added that
/// answers nothing.
///
/// NINE SITES ARE SEVEN METHODS, AND THE TWO COLLAPSES ARE THE POINT. The two finis are one call on two
/// teardown paths, so they are one method. The two socket getters are the same C function told apart
/// only by its port argument - PP429 says so: "The two chiaki_get_holepunch_sock calls are told apart
/// by their port type alone, and the seam needs that same distinction - a managed side returning one
/// socket for both would compile."
///
/// So the interface takes a port type rather than offering two methods, and that is the distinction
/// PP429 asked for. This asserts it survived: seven methods, nine sites, and exactly two of the sites
/// sharing.
/// </summary>
public static class HolepunchSeamJoin
{
    /// <summary>Every one of PP429's nine sites, and the method that answers it.</summary>
    public static IReadOnlyList<SeamJoin> Joins { get; } =
    [
        new("chiaki_holepunch_session_fini", nameof(IHolepunchSession.Fini), SharesTheMethod: true),
        new("chiaki_holepunch_session_fini", nameof(IHolepunchSession.Fini), SharesTheMethod: true),

        new("chiaki_get_holepunch_sock", nameof(IHolepunchSession.GetSocket), SharesTheMethod: true),
        new("chiaki_get_holepunch_sock", nameof(IHolepunchSession.GetSocket), SharesTheMethod: true),

        new("chiaki_get_regist_info", nameof(IHolepunchSession.GetRegistInfo), SharesTheMethod: false),
        new("holepunch_session_create_offer", nameof(IHolepunchSession.CreateOffer), SharesTheMethod: false),
        new(
            "chiaki_holepunch_session_punch_hole",
            nameof(IHolepunchSession.PunchHole),
            SharesTheMethod: false),
        new(
            "chiaki_get_ps_selected_addr",
            nameof(IHolepunchSession.GetSelectedAddress),
            SharesTheMethod: false),
        new("chiaki_get_ps_ctrl_port", nameof(IHolepunchSession.GetCtrlPort), SharesTheMethod: false),
    ];

    /// <summary>How many distinct methods the nine reach. Seven.</summary>
    public static int MethodCount
        => Joins.Select(j => j.Method).Distinct(StringComparer.Ordinal).Count();

    /// <summary>Every method the interface declares, by reflection rather than by list.</summary>
    public static IReadOnlyList<string> DeclaredMethods { get; } =
        [.. typeof(IHolepunchSession)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)];

    /// <summary>
    /// The two callees that reach a shared method, which is what turns nine into seven.
    /// </summary>
    public static IReadOnlyList<string> Collapsed { get; } =
        [.. Joins.Where(j => j.SharesTheMethod)
            .Select(j => j.Callee)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)];

    /// <summary>
    /// Whether the socket getter is still distinguished by an argument rather than by two methods,
    /// which is the distinction PP429 asked the seam to keep.
    /// </summary>
    public static bool TheSocketIsToldApartByItsArgument()
    {
        MethodInfo? getSocket = typeof(IHolepunchSession).GetMethod(nameof(IHolepunchSession.GetSocket));
        if (getSocket is null)
            return false;

        ParameterInfo[] parameters = getSocket.GetParameters();

        return parameters.Length == 1 && parameters[0].ParameterType == typeof(HolepunchPortType);
    }
}
