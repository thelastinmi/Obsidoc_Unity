using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Obsi.Doc.Editor
{
    /// <summary>
    /// Parses raw Markdown text (Obsidian-flavoured) into a list of typed blocks.
    /// No Unity dependencies — pure C#.
    /// </summary>
    public static class ObsidocMarkdownParser
    {
        // ── Compiled patterns ─────────────────────────────────────────────────

        private static readonly Regex ReHR      = new Regex(@"^(-{3,}|\*{3,}|_{3,})$",            RegexOptions.Compiled);
        private static readonly Regex ReHeading = new Regex(@"^(#{1,6})\s+(.+)$",                  RegexOptions.Compiled);
        private static readonly Regex ReTableSep= new Regex(@"^\|?[\s\-:]+(\|[\s\-:]+)*\|?$",      RegexOptions.Compiled);
        private static readonly Regex ReTask     = new Regex(@"^(\s*)[-*+]\s+\[([ xX])\]\s+(.+)$", RegexOptions.Compiled);
        private static readonly Regex ReUL      = new Regex(@"^(\s*)([-*+])\s+(.+)$",              RegexOptions.Compiled);
        private static readonly Regex ReOL      = new Regex(@"^(\s*)\d+\.\s+(.+)$",                RegexOptions.Compiled);
        private static readonly Regex ReCallout    = new Regex(@"^\[!(\w+)\](.*)$",                RegexOptions.Compiled);
        private static readonly Regex ReUserContent    = new Regex(@"^<!-- user-content:([^>]+) -->$",  RegexOptions.Compiled);
        private static readonly Regex ReImageObsidian  = new Regex(@"^!\[\[([^\]]+)\]\]\s*$",           RegexOptions.Compiled);
        private static readonly Regex ReImageStandard  = new Regex(@"^!\[([^\]]*)\]\(([^)]+)\)\s*$",    RegexOptions.Compiled);

        // Inline: groups 1-12 (see ParseInline for mapping)
        private static readonly Regex ReInline  = new Regex(
            @"\*\*\*(.+?)\*\*\*"           +   // 1  bold+italic
            @"|\*\*(.+?)\*\*"              +   // 2  bold **
            @"|__(.+?)__"                  +   // 3  bold __
            @"|\*(.+?)\*"                  +   // 4  italic *
            @"|_(.+?)_"                    +   // 5  italic _
            @"|~~(.+?)~~"                  +   // 6  strikethrough
            @"|==(.+?)=="                  +   // 7  highlight ==…==  (Obsidian)
            @"|`(.+?)`"                    +   // 8  inline code
            @"|!\[\[(.+?)\]\]"             +   // 9  embed  ![[…]]
            @"|\[\[([^\]]+)\]\]"           +   // 10 wikilink [[…]]
            @"|\[([^\]]+)\]\(([^)]+)\)",       // 11 link text, 12 link target
            RegexOptions.Compiled | RegexOptions.Singleline
        );

        // Strips Obsidian block comments before inline parsing
        private static readonly Regex ReObsComment = new Regex(@"%%.*?%%",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Parses <paramref name="rawText"/> into a list of <see cref="MarkdownBlock"/>.</summary>
        public static List<MarkdownBlock> Parse(string rawText)
        {
            if (string.IsNullOrEmpty(rawText))
                return new List<MarkdownBlock>();

            string[] lines = rawText.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var blocks = new List<MarkdownBlock>();
            int i = 0;

            // ── YAML frontmatter ───────────────────────────────────────────
            if (lines.Length > 0 && lines[0].TrimEnd() == "---")
            {
                int end = -1;
                for (int j = 1; j < lines.Length; j++)
                    if (lines[j].TrimEnd() == "---") { end = j; break; }

                if (end > 0)
                {
                    var sb = new StringBuilder();
                    for (int j = 1; j < end; j++) sb.AppendLine(lines[j]);
                    blocks.Add(new MarkdownBlock(BlockType.Frontmatter, sb.ToString().TrimEnd()));
                    i = end + 1;
                }
            }

            // ── Main loop ──────────────────────────────────────────────────
            while (i < lines.Length)
            {
                string line = lines[i];

                if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

                // Fenced code block  ```lang … ```
                if (line.TrimStart().StartsWith("```"))
                {
                    string lang = line.Trim();
                    lang = lang.Length > 3 ? lang.Substring(3).Trim() : string.Empty;
                    var sb = new StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                        sb.AppendLine(lines[i++]);
                    i++; // closing ```
                    var cb = new MarkdownBlock(BlockType.CodeBlock, sb.ToString().TrimEnd());
                    cb.Meta = lang;
                    blocks.Add(cb);
                    continue;
                }

                // Horizontal rule  --- / *** / ___
                if (ReHR.IsMatch(line.Trim()))
                {
                    blocks.Add(new MarkdownBlock(BlockType.HorizontalRule, line));
                    i++;
                    continue;
                }

                // Heading  ## Title
                var hm = ReHeading.Match(line);
                if (hm.Success)
                {
                    string content = hm.Groups[2].Value;
                    var hb = new MarkdownBlock(BlockType.Heading, content);
                    hb.Level = hm.Groups[1].Length;
                    hb.Spans = ParseInline(content);
                    blocks.Add(hb);
                    i++;
                    continue;
                }

                // Blockquote / Obsidian callout  > …
                if (line.StartsWith(">"))
                {
                    var sb = new StringBuilder();
                    string calloutType = null;
                    bool first = true;
                    while (i < lines.Length && lines[i].StartsWith(">"))
                    {
                        string content = lines[i].Length > 1 ? lines[i].Substring(1).TrimStart() : string.Empty;
                        if (first)
                        {
                            var cm = ReCallout.Match(content);
                            if (cm.Success)
                            {
                                calloutType = cm.Groups[1].Value;
                                content     = cm.Groups[2].Value.Trim();
                            }
                            first = false;
                        }
                        if (!string.IsNullOrWhiteSpace(content)) sb.AppendLine(content);
                        i++;
                    }
                    string bqText = sb.ToString().TrimEnd();
                    var bq = new MarkdownBlock(BlockType.Blockquote, bqText);
                    bq.Meta  = calloutType;
                    bq.Spans = ParseInline(bqText);
                    blocks.Add(bq);
                    continue;
                }

                // Table  | col | col |  (next line is separator)
                if (line.Contains("|") && i + 1 < lines.Length && ReTableSep.IsMatch(lines[i + 1].Trim()))
                {
                    var tableLines = new List<string>();
                    while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].Contains("|"))
                        tableLines.Add(lines[i++]);
                    var tb = new MarkdownBlock(BlockType.Table, string.Join("\n", tableLines));
                    tb.Rows = ParseTableRows(tableLines);
                    blocks.Add(tb);
                    continue;
                }

                // Task list item  - [ ] text  /  - [x] text  (must precede unordered list)
                // Consecutive task lines are grouped into a single TaskItem block.
                // RawText stores the raw lines (with markers) so sub-tasks and multi-item lists are preserved.
                if (ReTask.IsMatch(line))
                {
                    var sb = new StringBuilder();
                    while (i < lines.Length && ReTask.IsMatch(lines[i]))
                    {
                        sb.AppendLine(lines[i]);
                        i++;
                    }
                    blocks.Add(new MarkdownBlock(BlockType.TaskItem, sb.ToString().TrimEnd()));
                    continue;
                }

                // Unordered list item  - text
                var ulm = ReUL.Match(line);
                if (ulm.Success)
                {
                    string content = ulm.Groups[3].Value;
                    var li = new MarkdownBlock(BlockType.ListItem, content);
                    li.Level = ulm.Groups[1].Length / 2;
                    li.Spans = ParseInline(content);
                    blocks.Add(li);
                    i++;
                    continue;
                }

                // Ordered list item  1. text
                var olm = ReOL.Match(line);
                if (olm.Success)
                {
                    string content = olm.Groups[2].Value;
                    var li = new MarkdownBlock(BlockType.OrderedListItem, content);
                    li.Level = olm.Groups[1].Length / 2;
                    li.Spans = ParseInline(content);
                    blocks.Add(li);
                    i++;
                    continue;
                }

                // Standalone image  ![[path]]  or  ![alt](url)
                var imgObs = ReImageObsidian.Match(line);
                if (imgObs.Success)
                {
                    var img = new MarkdownBlock(BlockType.Image, imgObs.Groups[1].Value.Trim());
                    blocks.Add(img);
                    i++;
                    continue;
                }

                var imgStd = ReImageStandard.Match(line);
                if (imgStd.Success)
                {
                    var img  = new MarkdownBlock(BlockType.Image, imgStd.Groups[2].Value.Trim());
                    img.Meta = imgStd.Groups[1].Value; // alt text
                    blocks.Add(img);
                    i++;
                    continue;
                }

                // User-content zone: <!-- user-content:KEY --> ... <!-- /user-content:KEY -->
                var ucm = ReUserContent.Match(line.TrimEnd());
                if (ucm.Success)
                {
                    string key    = ucm.Groups[1].Value.Trim();
                    string endTag = $"<!-- /user-content:{key} -->";
                    var sb = new StringBuilder();
                    i++;
                    while (i < lines.Length && lines[i].TrimEnd() != endTag)
                        sb.AppendLine(lines[i++]);
                    if (i < lines.Length) i++; // skip closing tag
                    var uc  = new MarkdownBlock(BlockType.UserContent, sb.ToString().TrimEnd());
                    uc.Meta = key;
                    blocks.Add(uc);
                    continue;
                }

                // Paragraph — accumulate consecutive non-block lines
                {
                    var sb = new StringBuilder(line);
                    i++;
                    while (i < lines.Length
                        && !string.IsNullOrWhiteSpace(lines[i])
                        && !lines[i].TrimStart().StartsWith("```")
                        && !lines[i].StartsWith("#")
                        && !lines[i].StartsWith(">")
                        && !ReHR.IsMatch(lines[i].Trim())
                        && !ReTask.IsMatch(lines[i])
                        && !ReUL.IsMatch(lines[i])
                        && !ReOL.IsMatch(lines[i])
                        && !ReImageObsidian.IsMatch(lines[i])
                        && !ReImageStandard.IsMatch(lines[i])
                        && !ReUserContent.IsMatch(lines[i].TrimEnd()))
                    {
                        sb.Append(' ').Append(lines[i]);
                        i++;
                    }
                    string paraText = sb.ToString();
                    var para = new MarkdownBlock(BlockType.Paragraph, paraText);
                    para.Spans = ParseInline(paraText);
                    blocks.Add(para);
                }
            }

            return blocks;
        }

        /// <summary>
        /// Parses inline Markdown spans inside a single line of text.
        /// Public so external code (e.g. editor tools) can reuse it.
        /// </summary>
        public static List<InlineSpan> ParseInline(string text)
        {
            var spans = new List<InlineSpan>();
            if (string.IsNullOrEmpty(text)) return spans;

            // Strip Obsidian block comments %%...%%
            text = ReObsComment.Replace(text, string.Empty);

            int last = 0;
            foreach (Match m in ReInline.Matches(text))
            {
                if (m.Index > last)
                    spans.Add(new InlineSpan(SpanType.Text, text.Substring(last, m.Index - last)));

                if      (m.Groups[1].Success)  spans.Add(new InlineSpan(SpanType.BoldItalic,    m.Groups[1].Value));
                else if (m.Groups[2].Success)  spans.Add(new InlineSpan(SpanType.Bold,          m.Groups[2].Value));
                else if (m.Groups[3].Success)  spans.Add(new InlineSpan(SpanType.Bold,          m.Groups[3].Value));
                else if (m.Groups[4].Success)  spans.Add(new InlineSpan(SpanType.Italic,        m.Groups[4].Value));
                else if (m.Groups[5].Success)  spans.Add(new InlineSpan(SpanType.Italic,        m.Groups[5].Value));
                else if (m.Groups[6].Success)  spans.Add(new InlineSpan(SpanType.Strikethrough, m.Groups[6].Value));
                else if (m.Groups[7].Success)  spans.Add(new InlineSpan(SpanType.Highlight,     m.Groups[7].Value));
                else if (m.Groups[8].Success)  spans.Add(new InlineSpan(SpanType.Code,          m.Groups[8].Value));
                else if (m.Groups[9].Success)  spans.Add(new InlineSpan(SpanType.Embed, m.Groups[9].Value, m.Groups[9].Value));
                else if (m.Groups[10].Success)
                {
                    string raw    = m.Groups[10].Value;
                    int    pipe   = raw.IndexOf('|');
                    string alias  = pipe >= 0 ? raw.Substring(pipe + 1) : raw;
                    string target = pipe >= 0 ? raw.Substring(0, pipe)  : raw;
                    spans.Add(new InlineSpan(SpanType.Wikilink, alias, target));
                }
                else if (m.Groups[11].Success)
                    spans.Add(new InlineSpan(SpanType.Link, m.Groups[11].Value, m.Groups[12].Value));

                last = m.Index + m.Length;
            }

            if (last < text.Length)
                spans.Add(new InlineSpan(SpanType.Text, text.Substring(last)));

            return spans;
        }

        // ── Table rows ────────────────────────────────────────────────────────

        private static List<TableRow> ParseTableRows(List<string> lines)
        {
            var rows = new List<TableRow>();
            bool headerPassed = false;

            foreach (string line in lines)
            {
                if (ReTableSep.IsMatch(line.Trim()))
                {
                    headerPassed = true;
                    continue;
                }

                string trimmed = line.Trim();
                if (trimmed.StartsWith("|")) trimmed = trimmed.Substring(1);
                if (trimmed.EndsWith("|"))   trimmed = trimmed.Substring(0, trimmed.Length - 1);

                var row = new TableRow { IsHeader = !headerPassed };
                foreach (string cell in trimmed.Split('|'))
                {
                    string ct = cell.Trim();
                    row.Cells.Add(new TableCell { RawText = ct, Spans = ParseInline(ct) });
                }
                rows.Add(row);
            }

            return rows;
        }
    }

    // ── Data model ─────────────────────────────────────────────────────────────

    /// <summary>Type of a parsed Markdown block element.</summary>
    public enum BlockType
    {
        Frontmatter,
        Heading,
        CodeBlock,
        HorizontalRule,
        Blockquote,
        Table,
        ListItem,
        OrderedListItem,
        Paragraph,
        UserContent,
        TaskItem,
        Image,
    }

    /// <summary>Type of a parsed inline span.</summary>
    public enum SpanType
    {
        Text, Bold, Italic, BoldItalic, Strikethrough, Highlight,
        Code, Wikilink, Link, Embed
    }

    /// <summary>A parsed block-level element.</summary>
    public class MarkdownBlock
    {
        /// <summary>Kind of block.</summary>
        public BlockType        Type;
        /// <summary>Raw text (stripped of markers). For code blocks: the code body. For tables: original lines joined.</summary>
        public string           RawText;
        /// <summary>Heading level (1-6) or list indent level.</summary>
        public int              Level;
        /// <summary>Language identifier for code blocks; callout type for blockquotes; alt text for images.</summary>
        public string           Meta;
        /// <summary>Checked state for TaskItem blocks.</summary>
        public bool             IsChecked;
        /// <summary>Parsed inline spans (null for Frontmatter, CodeBlock, HorizontalRule, Table).</summary>
        public List<InlineSpan> Spans;
        /// <summary>Parsed rows — only set for Table blocks.</summary>
        public List<TableRow>   Rows;

        public MarkdownBlock(BlockType type, string rawText) { Type = type; RawText = rawText; }
    }

    /// <summary>A table row with header flag and cell list.</summary>
    public class TableRow
    {
        public bool            IsHeader;
        public List<TableCell> Cells = new List<TableCell>();
    }

    /// <summary>A single table cell with raw text and inline spans.</summary>
    public class TableCell
    {
        public string           RawText;
        public List<InlineSpan> Spans;
    }

    /// <summary>An inline formatting span.</summary>
    public class InlineSpan
    {
        public SpanType Type;
        public string   Text;
        /// <summary>Link URL or wikilink target. Null for non-link spans.</summary>
        public string   Target;

        public InlineSpan(SpanType type, string text, string target = null)
        {
            Type   = type;
            Text   = text;
            Target = target;
        }
    }
}
