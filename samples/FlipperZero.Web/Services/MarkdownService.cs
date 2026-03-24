using System.Reflection;
using Markdig;
using Microsoft.AspNetCore.Components;

namespace FlipperZero.Web.Services;

/// <summary>
/// Loads embedded markdown documentation files and renders them to HTML via Markdig.
/// Rendered output is cached — each document is converted once on first access.
/// </summary>
public sealed class MarkdownService
{
    private readonly Dictionary<string, DocEntry> _docs = new(StringComparer.OrdinalIgnoreCase);
    private readonly MarkdownPipeline _pipeline;

    public MarkdownService()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions() // GFM tables, pipe tables, task lists, auto-links, etc.
            .Build();

        LoadEmbeddedDocs();
    }

    /// <summary>
    /// Known documentation pages with their metadata.
    /// Order determines navigation display order.
    /// </summary>
    public static IReadOnlyList<DocInfo> AllDocs { get; } =
    [
        new("README",       "FlipperZero.NET",  "/"),
        new("ARCHITECTURE", "Architecture",     "/docs/architecture"),
        new("PROTOCOL",     "Wire Protocol",    "/docs/protocol"),
        new("SCHEMA",       "Schema Reference", "/docs/schema"),
        new("DIAGNOSTICS",  "Diagnostics",      "/docs/diagnostics"),
    ];

    /// <summary>Returns the rendered HTML for the given document key, or null if not found.</summary>
    public MarkupString? GetMarkup(string key)
    {
        if (!_docs.TryGetValue(key, out var entry))
            return null;

        entry.Html ??= Markdown.ToHtml(entry.Markdown, _pipeline);
        return new MarkupString(entry.Html);
    }

    /// <summary>Returns the document title (first H1), or null if not found.</summary>
    public string? GetTitle(string key)
    {
        if (!_docs.TryGetValue(key, out var entry))
            return null;

        // Extract first heading from the markdown source.
        entry.Title ??= ExtractTitle(entry.Markdown);
        return entry.Title;
    }

    private void LoadEmbeddedDocs()
    {
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith("Docs.", StringComparison.Ordinal) || !name.EndsWith(".md", StringComparison.Ordinal))
                continue;

            // "Docs.README.md" → "README"
            var key = name["Docs.".Length..^".md".Length];

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) continue;

            using var reader = new StreamReader(stream);
            var markdown = reader.ReadToEnd();

            _docs[key] = new DocEntry(markdown);
        }
    }

    private static string ExtractTitle(string markdown)
    {
        // Find first line starting with "# "
        using var reader = new StringReader(markdown);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal))
                return trimmed[2..].Trim();
        }

        return "Documentation";
    }

    private sealed class DocEntry(string markdown)
    {
        public string Markdown { get; } = markdown;
        public string? Html { get; set; }
        public string? Title { get; set; }
    }

    /// <summary>Metadata for a documentation page.</summary>
    public sealed record DocInfo(string Key, string NavLabel, string Route);
}
