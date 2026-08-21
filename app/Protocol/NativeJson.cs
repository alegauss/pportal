using System.Runtime.InteropServices;
using System.Text;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>How json-c numbers the types it can hold. The order is json_type's, not a choice.</summary>
public enum JsonCType
{
    /// <summary>No node at all - a lookup that missed, which json-c spells as NULL rather than as a type.</summary>
    None = -1,
    Null = 0,
    Boolean = 1,
    Double = 2,
    Int = 3,
    Object = 4,
    Array = 5,
    String = 6,
}

/// <summary>
/// PP33: json-c itself, reachable so the managed replacement can be held against it.
///
/// The same role <see cref="NativeHttp"/> plays for the HTTP parser, for the same reason: json-c is
/// being replaced rather than called, and "the managed side agrees with the library it replaces" is
/// only an assertion if both can be run on the same text.
///
/// A root is disposable and everything reached through it is not. json_tokener_parse hands back a
/// reference the caller owns; object_get_ex, pointer_get and array_get_idx hand back borrowed ones,
/// valid only while the root is. So the lookups return a bare <see cref="IntPtr"/> - there is
/// deliberately no type here that could be disposed twice.
/// </summary>
public sealed class NativeJson : IDisposable
{
    private IntPtr root;

    private NativeJson(IntPtr root) => this.root = root;

    /// <summary>The root node, or null where json-c refused the text.</summary>
    public static NativeJson? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // json_tokener_parse takes a NUL-terminated string, so the bytes are terminated here
        // rather than trusting the marshaller to do it for a byte[].
        byte[] utf8 = Encoding.UTF8.GetBytes(text + "\0");
        IntPtr handle = JsonParse(utf8);
        return handle == IntPtr.Zero ? null : new NativeJson(handle);
    }

    /// <summary>The root, for the accessors below.</summary>
    public IntPtr Root => root;

    public static JsonCType TypeOf(IntPtr node) => (JsonCType)JsonType(node);

    /// <summary>json_object_object_get_ex. Borrowed - never freed.</summary>
    public static IntPtr Get(IntPtr node, string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return JsonGet(node, Encoding.UTF8.GetBytes(key + "\0"));
    }

    /// <summary>json_pointer_get, RFC 6901. Borrowed.</summary>
    public IntPtr Pointer(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return JsonPointer(root, Encoding.UTF8.GetBytes(path + "\0"));
    }

    /// <summary>json_object_array_length, or -1 where the node is not an array.</summary>
    public static int ArrayLength(IntPtr node) => JsonArrayLength(node);

    /// <summary>json_object_array_get_idx. Borrowed.</summary>
    public static IntPtr ArrayAt(IntPtr node, int index) => JsonArrayAt(node, index);

    /// <summary>json_object_get_string, which answers for types other than string too.</summary>
    public static string? String(IntPtr node) => Marshal.PtrToStringUTF8(JsonString(node));

    /// <summary>json_object_get_int, which parses strings and saturates.</summary>
    public static int Int(IntPtr node) => JsonInt(node);

    /// <summary>json_object_get_int64.</summary>
    public static long Int64(IntPtr node) => JsonInt64(node);

    /// <summary>json_object_get_boolean.</summary>
    public static bool Bool(IntPtr node) => JsonBool(node);

    public void Dispose()
    {
        if (root == IntPtr.Zero)
            return;

        JsonFree(root);
        root = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_parse",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr JsonParse(byte[] text);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void JsonFree(IntPtr root);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_type",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int JsonType(IntPtr node);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_get",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr JsonGet(IntPtr node, byte[] key);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_pointer",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr JsonPointer(IntPtr root, byte[] path);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_array_length",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int JsonArrayLength(IntPtr node);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_array_at",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr JsonArrayAt(IntPtr node, int index);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_string",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr JsonString(IntPtr node);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_int",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int JsonInt(IntPtr node);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_int64",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern long JsonInt64(IntPtr node);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_bool",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool JsonBool(IntPtr node);

    /// <summary>Takes ownership of a root handle produced elsewhere, so it is freed once.</summary>
    internal static NativeJson Own(IntPtr handle) => new(handle);
}

/// <summary>
/// PP215: json-c's tokener, KEPT rather than made and thrown away.
///
/// <see cref="NativeJson.Parse"/> is json_tokener_parse - a fresh tokener per document, which is
/// what every json-c call site in holepunch.c does except one. The websocket loop makes a single
/// tokener before its frame loop and feeds it every frame for the life of the socket. Whether such
/// a tokener still works after a frame it could not parse is a question about json-c, and this port
/// answers questions about json-c by MEASURING them rather than by reading the documentation - the
/// same rule <see cref="JsonC"/> was built under.
/// </summary>
public sealed class NativeJsonTokener : IDisposable
{
    private IntPtr tokener;

    /// <summary>A new tokener, or null where json-c would not make one.</summary>
    public static NativeJsonTokener? Create()
    {
        IntPtr handle = TokenerNew();
        return handle == IntPtr.Zero ? null : new NativeJsonTokener { tokener = handle };
    }

    /// <summary>
    /// Feeds one document, the way the frame loop feeds one frame: the bytes and their length,
    /// with no NUL terminator, into the tokener this object holds.
    /// </summary>
    /// <returns>The document, or null where the tokener produced none.</returns>
    public NativeJson? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        IntPtr handle = TokenerParse(tokener, utf8, utf8.Length);

        return handle == IntPtr.Zero ? null : NativeJson.Own(handle);
    }

    /// <summary>json_tokener_get_error, in json-c's own numbering. Zero is success.</summary>
    public int Error => TokenerError(tokener);

    /// <summary>json_tokener_reset - which holepunch.c never calls on the tokener it reuses.</summary>
    public void Reset() => TokenerReset(tokener);

    public void Dispose()
    {
        if (tokener == IntPtr.Zero)
            return;

        TokenerFree(tokener);
        tokener = IntPtr.Zero;
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_tokener_new",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr TokenerNew();

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_tokener_parse",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr TokenerParse(IntPtr tokener, byte[] text, int length);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_tokener_error",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int TokenerError(IntPtr tokener);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_tokener_reset",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void TokenerReset(IntPtr tokener);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_json_tokener_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void TokenerFree(IntPtr tokener);
}
