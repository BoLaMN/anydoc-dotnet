using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace AnyDoc;

/// <summary>P/Invoke surface of the <c>anydoc_ffi</c> native library.</summary>
internal static unsafe partial class Native
{
    private const string Library = "anydoc_ffi";
    private const int Ok = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Buffer
    {
        public byte* Ptr;
        public nuint Len;
    }

    static Native()
    {
        NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);
    }

    // NuGet consumers get the native library next to the app; project
    // references (and `dotnet test`) only get the `runtimes/<rid>/native`
    // tree copied to the output, which the default probing ignores.
    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library)
        {
            return IntPtr.Zero;
        }
        var file = OperatingSystem.IsWindows() ? $"{Library}.dll"
            : OperatingSystem.IsMacOS() ? $"lib{Library}.dylib"
            : $"lib{Library}.so";
        var candidate = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", file);
        return NativeLibrary.TryLoad(candidate, out var handle) ? handle : IntPtr.Zero;
    }

    [LibraryImport(Library)]
    private static partial void anydoc_buffer_free(Buffer buffer);

    [LibraryImport(Library)]
    private static partial int anydoc_to_markdown(byte* path, nuint pathLen, Buffer* output);

    [LibraryImport(Library)]
    private static partial int anydoc_to_markdown_bytes(byte* data, nuint len, int format, Buffer* output);

    [LibraryImport(Library)]
    private static partial int anydoc_to_document_json(byte* data, nuint len, int format, Buffer* output);

    [LibraryImport(Library)]
    private static partial int anydoc_format_from_bytes(byte* data, nuint len);

    [LibraryImport(Library)]
    private static partial int anydoc_format_from_extension(byte* ext, nuint len);

    public static string ToMarkdown(string path)
    {
        var utf8 = Encoding.UTF8.GetBytes(path);
        fixed (byte* p = utf8)
        {
            Buffer output;
            var status = anydoc_to_markdown(p, (nuint)utf8.Length, &output);
            return Take(status, output, Encoding.UTF8.GetString);
        }
    }

    public static string ToMarkdown(ReadOnlySpan<byte> data, Format? format)
    {
        fixed (byte* p = data)
        {
            Buffer output;
            var status = anydoc_to_markdown_bytes(p, (nuint)data.Length, Index(format), &output);
            return Take(status, output, Encoding.UTF8.GetString);
        }
    }

    public static Document ToDocument(ReadOnlySpan<byte> data, Format? format)
    {
        fixed (byte* p = data)
        {
            Buffer output;
            var status = anydoc_to_document_json(p, (nuint)data.Length, Index(format), &output);
            return Take(status, output,
                json => JsonSerializer.Deserialize(json, ModelJsonContext.Default.Document)
                    ?? throw new ConvertException("invalidArgument", "native library returned no document"));
        }
    }

    public static Format? FormatFromBytes(ReadOnlySpan<byte> data)
    {
        fixed (byte* p = data)
        {
            return Format(anydoc_format_from_bytes(p, (nuint)data.Length));
        }
    }

    public static Format? FormatFromExtension(string extension)
    {
        var utf8 = Encoding.UTF8.GetBytes(extension);
        fixed (byte* p = utf8)
        {
            return Format(anydoc_format_from_extension(p, (nuint)utf8.Length));
        }
    }

    private static int Index(Format? format) => format is { } f ? (int)f : -1;

    private static Format? Format(int index) => index < 0 ? null : (Format)index;

    private delegate T Reader<T>(ReadOnlySpan<byte> bytes);

    /// <summary>Read a result buffer, free it, and throw if it carries an error.</summary>
    private static T Take<T>(int status, Buffer output, Reader<T> read)
    {
        try
        {
            var bytes = new ReadOnlySpan<byte>(output.Ptr, checked((int)output.Len));
            if (status != Ok)
            {
                throw ConvertException.FromNative(
                    JsonSerializer.Deserialize(bytes, ModelJsonContext.Default.NativeError)!);
            }
            return read(bytes);
        }
        finally
        {
            anydoc_buffer_free(output);
        }
    }
}
