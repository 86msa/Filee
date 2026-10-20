using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Filee.Engines.Vector;

/// <summary>Original logical text, before the SVG compositor splits and visually orders font runs.</summary>
internal sealed record SvgTextSource(string Text, float? X, string Anchor, bool Rtl, bool Simple)
{
    public static IReadOnlyList<SvgTextSource> Read(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var xml = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var document = XDocument.Load(xml);
            return document.Descendants().Where(e => e.Name.LocalName == "text").Select(e =>
            {
                string? Inherited(XName attribute) => e.AncestorsAndSelf().Select(a => (string?)a.Attribute(attribute)).FirstOrDefault(v => v is not null);
                var text = Inherited(XNamespace.Xml + "space") == "preserve" ? e.Value : Regex.Replace(e.Value, @"\s+", " ").Trim();
                var x = float.TryParse((string?)e.Attribute("x"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : (float?)null;
                return new SvgTextSource(text, x, Inherited("text-anchor") ?? "start", Inherited("direction") == "rtl", !e.HasElements
                    && !text.Contains('\n') && !text.Contains('\r')
                    && new[] { "x", "y" }.All(a => e.Attribute(a) is null || float.TryParse((string?)e.Attribute(a), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    && new[] { "dx", "dy", "rotate", "textLength", "lengthAdjust", "letter-spacing", "word-spacing" }.All(a => e.Attribute(a) is null)
                    && !e.AncestorsAndSelf().Any(a => ((string?)a.Attribute("style"))?.Contains("spacing", StringComparison.OrdinalIgnoreCase) == true));
            }).ToArray();
        }
        catch (XmlException)
        {
            // Svg.Skia can also read repaired SVGs and SVG font definitions. Their original playback is retained.
            return [];
        }
    }

    public static string Characters(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)).Order());
}
