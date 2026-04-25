using System.Collections.Generic;
using System.Text;

namespace Obsi.Doc.Editor
{
    /// <summary>
    /// Converts a list of <see cref="MarkdownBlock"/> back to a Markdown string.
    /// Inverse of <see cref="ObsidocMarkdownParser.Parse"/>.
    /// </summary>
    public static class ObsidocMarkdownSerializer
    {
        public static string Serialize(List<MarkdownBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < blocks.Count; i++)
            {
                if (i > 0) sb.AppendLine();
                SerializeBlock(sb, blocks[i]);
            }
            return sb.ToString();
        }

        private static void SerializeBlock(StringBuilder sb, MarkdownBlock b)
        {
            switch (b.Type)
            {
                case BlockType.Frontmatter:
                    sb.AppendLine("---");
                    sb.AppendLine(b.RawText ?? string.Empty);
                    sb.AppendLine("---");
                    break;

                case BlockType.Heading:
                    sb.AppendLine(new string('#', System.Math.Max(1, b.Level)) + " " + (b.RawText ?? string.Empty));
                    break;

                case BlockType.CodeBlock:
                    sb.AppendLine("```" + (b.Meta ?? string.Empty));
                    if (!string.IsNullOrEmpty(b.RawText))
                        sb.AppendLine(b.RawText);
                    sb.AppendLine("```");
                    break;

                case BlockType.HorizontalRule:
                    sb.AppendLine("---");
                    break;

                case BlockType.Blockquote:
                    if (!string.IsNullOrEmpty(b.Meta))
                        sb.AppendLine($"> [!{b.Meta}]");
                    if (!string.IsNullOrEmpty(b.RawText))
                        foreach (string line in b.RawText.Split('\n'))
                            sb.AppendLine("> " + line.TrimEnd('\r'));
                    break;

                case BlockType.Table:
                    // RawText stores the original joined table lines — emit as-is
                    sb.AppendLine(b.RawText ?? string.Empty);
                    break;

                case BlockType.ListItem:
                    sb.AppendLine(new string(' ', b.Level * 2) + "- " + (b.RawText ?? string.Empty));
                    break;

                case BlockType.OrderedListItem:
                    sb.AppendLine(new string(' ', b.Level * 2) + "1. " + (b.RawText ?? string.Empty));
                    break;

                case BlockType.TaskItem:
                    // RawText stores the complete formatted lines (including - [ ] markers).
                    sb.AppendLine(b.RawText ?? string.Empty);
                    break;

                case BlockType.Image:
                    if (string.IsNullOrEmpty(b.Meta))
                        sb.AppendLine($"![[{b.RawText ?? string.Empty}]]");
                    else
                        sb.AppendLine($"![{b.Meta}]({b.RawText ?? string.Empty})");
                    break;

                case BlockType.Paragraph:
                    sb.AppendLine(b.RawText ?? string.Empty);
                    break;

                case BlockType.UserContent:
                    sb.AppendLine($"<!-- user-content:{b.Meta ?? "custom"} -->");
                    if (!string.IsNullOrEmpty(b.RawText))
                        sb.AppendLine(b.RawText);
                    sb.AppendLine($"<!-- /user-content:{b.Meta ?? "custom"} -->");
                    break;
            }
        }
    }
}
