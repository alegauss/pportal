using ChiakiNg.Session;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP427: which OpenSSL EC API ecdh.c is written against.
///
/// The eight EC_KEY and ECDH calls were all deprecated in OpenSSL 3.0, and they were the whole of the
/// key agreement - one use each, and no other file in lib touched the API. They are gone: the key is
/// an EVP_PKEY built by EVP_EC_gen or EVP_PKEY_fromdata, and the secret comes from EVP_PKEY_derive.
///
/// COMMENTS ARE STRIPPED, and this class is why the rule exists rather than an example of it. The
/// port's own comments name every one of the eight to say what replaced it - "EC_KEY_set_private_key
/// and EC_KEY_set_public_key amended a key in place" is a sentence in the file. A reader of flat text
/// would report the deprecated API as still in use, by the prose explaining that it is not.
///
/// THE mbedtls BRANCH USES NONE OF THEM and is untouched. It sits in the same file behind an #ifdef,
/// so it is in the text this reads either way - which costs nothing, because it never used the eight.
/// </summary>
public static class EcdhSource
{
    /// <summary>The file the key agreement lives in.</summary>
    public const string RelativePath = @"lib\src\ecdh.c";

    /// <summary>The file, or null outside a checkout.</summary>
    public static string? Locate() => SanitizerSource.LocateRelative(RelativePath);

    /// <summary>
    /// The eight, as §PP427 listed them: seven EC_KEY calls and ECDH_compute_key.
    ///
    /// EC_GROUP and EC_POINT are deliberately absent. They were not among the eight and are not
    /// deprecated - they went because the objects they operated on went, not because they had to.
    /// </summary>
    public static IReadOnlyList<string> Deprecated { get; } =
    [
        "EC_KEY_new",
        "EC_KEY_free",
        "EC_KEY_set_group",
        "EC_KEY_generate_key",
        "EC_KEY_set_private_key",
        "EC_KEY_set_public_key",
        "EC_KEY_get0_public_key",
        "ECDH_compute_key",
    ];

    /// <summary>
    /// The EVP calls that replaced them, each doing the work of one or more.
    ///
    /// EVP_EC_gen alone covers four: the group by name, the key, its group and its generation.
    /// </summary>
    public static IReadOnlyList<string> Replacements { get; } =
    [
        "EVP_EC_gen",
        "EVP_PKEY_fromdata",
        "EVP_PKEY_get1_encoded_public_key",
        "EVP_PKEY_derive",
        "EVP_PKEY_free",
    ];

    /// <summary>
    /// Every deprecated call the file still makes, read from code and not from prose.
    ///
    /// PP400: <see cref="CCall.Code"/> first, because the port's comments name all eight.
    /// </summary>
    public static IReadOnlyList<string> DeprecatedStillUsed(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);

        return [.. Deprecated.Where(name => code.Contains(name + "(", StringComparison.Ordinal))];
    }

    /// <summary>Every replacement the file makes, likewise from code.</summary>
    public static IReadOnlyList<string> ReplacementsUsed(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);

        return [.. Replacements.Where(name => code.Contains(name + "(", StringComparison.Ordinal))];
    }

    /// <summary>
    /// Whether the curve is still named once rather than spelled at each use.
    ///
    /// EC_GROUP_new_by_curve_name took NID_secp256k1 and every key shared the object; EVP takes the
    /// name per key, so a constant is what stops the two builders drifting apart.
    /// </summary>
    public static bool NamesTheCurveOnce(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        string code = CCall.Code(source);

        return code.Contains("#define CHIAKI_ECDH_CURVE", StringComparison.Ordinal)
            && !code.Contains("NID_secp256k1", StringComparison.Ordinal);
    }
}
