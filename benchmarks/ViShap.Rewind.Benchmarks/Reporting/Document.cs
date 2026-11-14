using System.Text;

namespace ViShap.Rewind.Benchmarks.Reporting;

/// <summary>
/// The report, held as structure rather than as text, so the Markdown and the HTML are two renderings of one document
/// and cannot drift apart. Nothing here formats a number: a cell arrives already formatted.
/// </summary>
internal sealed class Document
{
    private readonly List<Node> _nodes = [];

    internal void Heading(int level, string text) => _nodes.Add(new Node.Heading(level, text));

    internal void Paragraph(string text) => _nodes.Add(new Node.Paragraph(text));

    internal void Bullets(IEnumerable<string> items) => _nodes.Add(new Node.Bullets([.. items]));

    internal void Code(string text) => _nodes.Add(new Node.Code(text));

    internal void Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows) =>
        _nodes.Add(new Node.Table(headers, rows));

    internal void Image(string path, string caption) => _nodes.Add(new Node.Image(path, caption));

    internal string ToMarkdown()
    {
        var text = new StringBuilder();

        foreach (var node in _nodes)
        {
            switch (node)
            {
                case Node.Heading heading:
                    text.Append('\n').Append(new string('#', heading.Level)).Append(' ').Append(heading.Text).Append("\n\n");
                    break;

                case Node.Paragraph paragraph:
                    text.Append(paragraph.Text).Append("\n\n");
                    break;

                case Node.Bullets bullets:
                    foreach (var item in bullets.Items)
                    {
                        text.Append("- ").Append(item).Append('\n');
                    }

                    text.Append('\n');
                    break;

                case Node.Code code:
                    text.Append("```text\n").Append(code.Text).Append("\n```\n\n");
                    break;

                case Node.Table table:
                    text.Append("| ").Append(string.Join(" | ", table.Headers)).Append(" |\n");
                    text.Append('|').Append(string.Join("|", table.Headers.Select(_ => "---"))).Append("|\n");

                    // A cell is one line, and a pipe inside it is text, not a column boundary.
                    foreach (var row in table.Rows)
                    {
                        text.Append("| ")
                            .Append(string.Join(" | ", row.Select(cell => cell.ReplaceLineEndings(" ").Replace("|", "\\|", StringComparison.Ordinal))))
                            .Append(" |\n");
                    }

                    text.Append('\n');
                    break;

                case Node.Image image:
                    text.Append("![").Append(image.Caption).Append("](").Append(image.Path).Append(")\n\n");
                    break;
            }
        }

        return text.ToString().TrimStart('\n');
    }

    /// <summary>
    /// The HTML rendering: one self-contained page, light and dark, with a table of contents built from the
    /// second-level headings.
    /// </summary>
    internal string ToHtml(string title)
    {
        var html = new StringBuilder();

        html.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        html.Append("<title>").Append(Svg.Escape(title)).Append("</title>\n<style>\n");
        html.Append("""
            :root { color-scheme: light dark; --ink: #1f2933; --muted: #6b7280; --line: #d9dee5; --panel: #f4f6f9;
                    --accent: #2f6f9f; --page: #ffffff; }
            @media (prefers-color-scheme: dark) {
              :root { --ink: #e6e8eb; --muted: #9aa2ad; --line: #353c46; --panel: #1d2229; --accent: #7fb2dd; --page: #121519; }
            }
            * { box-sizing: border-box; }
            body { margin: 0 auto; padding: 32px 16px 80px; max-width: 1120px; background: var(--page); color: var(--ink);
                   font: 15px/1.6 "Segoe UI", system-ui, Helvetica, Arial, sans-serif; }
            h1 { font-size: 1.7rem; margin: 0 0 .5rem; letter-spacing: -0.01em; }
            h2 { font-size: 1.3rem; margin-top: 2.6rem; padding-top: .6rem; border-top: 1px solid var(--line); }
            h3 { font-size: 1.05rem; margin-top: 1.6rem; color: var(--accent); }
            nav { background: var(--panel); border: 1px solid var(--line); border-radius: 8px; padding: 10px 16px; margin: 20px 0; }
            nav ol { margin: 4px 0; padding-left: 20px; columns: 2; column-gap: 32px; }
            nav a { color: var(--accent); text-decoration: none; } nav a:hover { text-decoration: underline; }
            .table { overflow-x: auto; margin: 12px 0 20px; }
            table { border-collapse: collapse; width: 100%; font-size: 13px; }
            th, td { border-bottom: 1px solid var(--line); padding: 5px 9px; text-align: left; vertical-align: top; }
            th { background: var(--panel); font-weight: 600; position: sticky; top: 0; }
            tbody tr:hover { background: var(--panel); }
            td:not(:first-child) { font-variant-numeric: tabular-nums; }
            code { background: var(--panel); padding: 1px 5px; border-radius: 4px; font-size: 12.5px; }
            pre { background: var(--panel); border: 1px solid var(--line); border-radius: 6px; padding: 10px 12px;
                  overflow-x: auto; font-size: 12px; }
            figure { margin: 18px 0 26px; }
            img { max-width: 100%; height: auto; display: block; border: 1px solid var(--line); border-radius: 6px; background: #fff; }
            figcaption { color: var(--muted); font-size: 12px; margin-top: 6px; }
            strong { font-weight: 600; }
            @media (max-width: 720px) { nav ol { columns: 1; } }
            """);
        html.Append("\n</style>\n</head>\n<body>\n");

        var sections = _nodes.OfType<Node.Heading>().Where(heading => heading.Level == 2).Select(heading => heading.Text).ToList();
        var tocWritten = false;
        var anchor = 0;

        foreach (var node in _nodes)
        {
            // The contents follow the identity table that opens every report, before its first section.
            if (!tocWritten && node is Node.Heading { Level: 2 } && sections.Count > 2)
            {
                html.Append("<nav><strong>Contents</strong>\n<ol>\n");

                for (var index = 0; index < sections.Count; index++)
                {
                    html.Append("<li><a href=\"#s").Append(index + 1).Append("\">").Append(Svg.Escape(sections[index])).Append("</a></li>\n");
                }

                html.Append("</ol>\n</nav>\n");
                tocWritten = true;
            }

            switch (node)
            {
                case Node.Heading heading:
                    var tag = $"h{Math.Clamp(heading.Level, 1, 4)}";
                    var id = heading.Level == 2 ? $" id=\"s{++anchor}\"" : string.Empty;
                    html.Append('<').Append(tag).Append(id).Append('>').Append(Inline(heading.Text)).Append("</").Append(tag).Append(">\n");
                    break;

                case Node.Paragraph paragraph:
                    html.Append("<p>").Append(Inline(paragraph.Text)).Append("</p>\n");
                    break;

                case Node.Bullets bullets:
                    html.Append("<ul>\n");

                    foreach (var item in bullets.Items)
                    {
                        html.Append("<li>").Append(Inline(item)).Append("</li>\n");
                    }

                    html.Append("</ul>\n");
                    break;

                case Node.Code code:
                    html.Append("<pre>").Append(Svg.Escape(code.Text)).Append("</pre>\n");
                    break;

                case Node.Table table:
                    html.Append("<div class=\"table\"><table>\n<thead>\n<tr>");

                    foreach (var header in table.Headers)
                    {
                        html.Append("<th>").Append(Inline(header)).Append("</th>");
                    }

                    html.Append("</tr>\n</thead>\n<tbody>\n");

                    foreach (var row in table.Rows)
                    {
                        html.Append("<tr>");

                        foreach (var cell in row)
                        {
                            html.Append("<td>").Append(Inline(cell)).Append("</td>");
                        }

                        html.Append("</tr>\n");
                    }

                    html.Append("</tbody>\n</table></div>\n");
                    break;

                case Node.Image image:
                    html.Append("<figure>\n<img src=\"").Append(image.Path).Append("\" alt=\"")
                        .Append(Svg.Escape(image.Caption)).Append("\" loading=\"lazy\">\n<figcaption>")
                        .Append(Svg.Escape(image.Caption)).Append("</figcaption>\n</figure>\n");
                    break;
            }
        }

        return html.Append("</body>\n</html>\n").ToString();
    }

    /// <summary>Carries the two inline marks the generator uses, <c>`code`</c> and <c>**bold**</c>, and escapes everything else.</summary>
    private static string Inline(string text)
    {
        var escaped = Pair(Svg.Escape(text), "`", "<code>", "</code>");
        return Pair(escaped, "**", "<strong>", "</strong>");
    }

    private static string Pair(string text, string mark, string open, string close)
    {
        while (true)
        {
            int start = text.IndexOf(mark, StringComparison.Ordinal);

            if (start < 0)
            {
                return text;
            }

            int end = text.IndexOf(mark, start + mark.Length, StringComparison.Ordinal);

            if (end < 0)
            {
                return text;
            }

            text = text[..start] + open + text[(start + mark.Length)..end] + close + text[(end + mark.Length)..];
        }
    }

    private abstract record Node
    {
        internal sealed record Heading(int Level, string Text) : Node;

        internal sealed record Paragraph(string Text) : Node;

        internal sealed record Bullets(IReadOnlyList<string> Items) : Node;

        internal sealed record Code(string Text) : Node;

        internal sealed record Table(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) : Node;

        internal sealed record Image(string Path, string Caption) : Node;
    }
}
