using System.Runtime.InteropServices;
using PDFtoImage;

namespace Filee.Engines.Tests;

// PDFium is not thread-safe; this collection runs outside the other engine test collections.
[CollectionDefinition("Arabic PDF native", DisableParallelization = true)]
public sealed class ArabicPdfNativeCollection;

internal static class ArabicPdfNative
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr LoadMemory(IntPtr data, UIntPtr length, IntPtr password);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr LoadPage(IntPtr document, int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr LoadText(IntPtr page);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetText(IntPtr text, int start, int count, IntPtr buffer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr FindStart(IntPtr text, IntPtr query, uint flags, int start);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int FindNext(IntPtr search);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Close(IntPtr handle);

    private static readonly Lazy<IntPtr> Library = new(() =>
    {
        var name = OperatingSystem.IsWindows() ? "pdfium.dll" : OperatingSystem.IsMacOS() ? "libpdfium.dylib" : "libpdfium.so";
        foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, name),
            Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", name) })
            if (File.Exists(path))
                return NativeLibrary.Load(path);
        return NativeLibrary.Load("pdfium", typeof(Conversion).Assembly, null);
    });

    private static T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(Library.Value, name));

    public static void AssertSearchAndVowels(string path)
    {
        var bytes = File.ReadAllBytes(path);
#pragma warning disable CA1416
        var pageCount = Conversion.GetPageCount(bytes); // initializes the same native PDFium used for rendering
#pragma warning restore CA1416
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        var document = Function<LoadMemory>("FPDF_LoadMemDocument64")(pin.AddrOfPinnedObject(), (UIntPtr)bytes.Length, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, document);
        var queries = new[] { "السلام عليكم", "الجمهورية العراقية", "١٢٥٬٠٠٠", "2026/125", "بغداد", ArabicPdfTests.Samples[4] };
        var found = queries.ToDictionary(q => q, _ => false);
        var extracted = new List<string>();
        try
        {
            for (var index = 0; index < pageCount; index++)
            {
                var page = Function<LoadPage>("FPDF_LoadPage")(document, index);
                Assert.NotEqual(IntPtr.Zero, page);
                try
                {
                    var text = Function<LoadText>("FPDFText_LoadPage")(page);
                    Assert.NotEqual(IntPtr.Zero, text);
                    try
                    {
                        var count = Function<Count>("FPDFText_CountChars")(text);
                        var buffer = Marshal.AllocHGlobal((count + 1) * 2);
                        try
                        {
                            var written = Function<GetText>("FPDFText_GetText")(text, 0, count, buffer);
                            extracted.Add(Marshal.PtrToStringUni(buffer, Math.Max(0, written - 1)) ?? "");
                        }
                        finally { Marshal.FreeHGlobal(buffer); }
                        foreach (var query in queries)
                        {
                            var needle = Marshal.StringToHGlobalUni(query);
                            try
                            {
                                var search = Function<FindStart>("FPDFText_FindStart")(text, needle, 1, 0);
                                try { found[query] |= Function<FindNext>("FPDFText_FindNext")(search) != 0; }
                                finally { Function<Close>("FPDFText_FindClose")(search); }
                            }
                            finally { Marshal.FreeHGlobal(needle); }
                        }
                    }
                    finally { Function<Close>("FPDFText_ClosePage")(text); }
                }
                finally { Function<Close>("FPDF_ClosePage")(page); }
            }
        }
        finally
        {
            Function<Close>("FPDF_CloseDocument")(document);
            pin.Free();
        }
        foreach (var query in queries)
            Assert.True(found[query], $"Native PDFium could not search for {query}");
        foreach (var sample in ArabicPdfTests.Samples)
            Assert.Contains(sample, string.Join('\n', extracted));
    }
}
