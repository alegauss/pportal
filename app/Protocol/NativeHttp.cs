using System.Runtime.InteropServices;
using System.Text;
using ChiakiNg.Native;

namespace ChiakiNg.Protocol;

/// <summary>
/// PP23: libchiaki's own HTTP parser, reachable so the managed one can be held against it.
///
/// This exists to be the other side of a comparison and for no other purpose - the port calls
/// <see cref="HttpResponse"/> in earnest. Keeping the C one reachable is what makes "the managed
/// implementation agrees with the one it replaces" an assertion rather than a hope, and it is the
/// pattern every module in Block F is meant to be replaced under.
/// </summary>
public static class NativeHttp
{
    /// <summary>The same answer, from the parser being replaced. Null where libchiaki refuses.</summary>
    public static (int Code, IReadOnlyList<HttpHeader> Headers)? Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        IntPtr handle = HttpParse(utf8, utf8.Length, out int code, out int _);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            int count = HttpHeaderCount(handle);
            var headers = new List<HttpHeader>(count);
            for (int i = 0; i < count; i++)
            {
                headers.Add(new HttpHeader(
                    Marshal.PtrToStringUTF8(HttpHeaderKey(handle, i)) ?? "",
                    Marshal.PtrToStringUTF8(HttpHeaderValue(handle, i)) ?? ""));
            }

            return (code, headers);
        }
        finally
        {
            HttpFree(handle);
        }
    }

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_http_parse",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr HttpParse(byte[] text, int len, out int code, out int error);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_http_free",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void HttpFree(IntPtr response);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_http_header_count",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern int HttpHeaderCount(IntPtr response);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_http_header_key",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr HttpHeaderKey(IntPtr response, int index);

    [DllImport(ChiakiNative.Library, EntryPoint = "chiaki_shim_http_header_value",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr HttpHeaderValue(IntPtr response, int index);
}
