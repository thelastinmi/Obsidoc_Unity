using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Obsi.Doc.Editor
{
    /// <summary>
    /// Renders a list of <see cref="MarkdownBlock"/> using Unity IMGUI.
    /// Colour palette and escape strategy inspired by ObsiDocV2's InlineMarkdownRenderer.
    /// Instantiate once per window; styles and badge textures are rebuilt on skin change.
    /// </summary>
    public class ObsidocMarkdownRenderer
    {
        // ── Style cache ───────────────────────────────────────────────────────

        private GUIStyle _h1, _h2, _h3, _h4;
        private GUIStyle _paragraph, _listItem, _listItemDim, _emptyDesc;
        private GUIStyle _codeBlock, _codeInline, _frontmatter, _metaStyle, _tableHeader;
        private bool     _built;
        private bool     _lastProSkin;

        // ── Configurable overrides (set via Configure) ────────────────────────

        private bool  _hasConfig;
        private Color _cfgAccSummary, _cfgAccFields, _cfgAccMethods, _cfgAccValues;
        private Color _cfgBadgePublic, _cfgBadgePrivate, _cfgBadgeProtected;
        private int   _cfgH1Size, _cfgH2Size, _cfgH3Size;

        private readonly Dictionary<string, GUIStyle>   _badgeCache    = new Dictionary<string, GUIStyle>();
        private readonly Dictionary<string, Texture2D>  _textureCache  = new Dictionary<string, Texture2D>();
        private bool   _dirty;              // set true when content is modified this frame
        private bool   _inSummarySection;  // true while rendering blocks after ## Summary
        private bool   _inFieldsSection;      // true while rendering blocks after ## Fields
        private bool   _inPropertiesSection;  // true while rendering blocks after ## Properties
        private bool   _inValuesSection;      // true while rendering blocks after ## Values
        private bool   _inMethodsSection;     // true while rendering blocks after ## Methods
        private string _basedir;           // for resolving relative image paths

        /// <summary>Directory of the currently displayed file, used to resolve relative image paths.</summary>
        public string BaseDir
        {
            get => _basedir;
            set
            {
                if (_basedir == value) return;
                _basedir = value;
                foreach (var t in _textureCache.Values)
                    if (t != null) UnityEngine.Object.DestroyImmediate(t);
                _textureCache.Clear();
            }
        }

        // ── Skin flag ─────────────────────────────────────────────────────────

        private static bool Pro => EditorGUIUtility.isProSkin;

        // ── Palette (V2-inspired) ─────────────────────────────────────────────
        //
        //   Code spans  → warm orange-brown  (V2 HexCode)
        //   Code blocks → light blue         (V2 _codeStyle)
        //   Links/wikis → sky blue           (V2 HexLink)
        //   Highlight   → gold               (V2 HexHigh)
        //   Type column → cyan-blue          (V2 typeStyle)

        private static Color ColText       => Pro ? new Color(0.88f, 0.88f, 0.88f) : new Color(0.10f, 0.10f, 0.10f);
        private static Color ColH1         => Pro ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.08f, 0.08f, 0.08f);
        private static Color ColH2         => Pro ? new Color(0.82f, 0.82f, 0.88f) : new Color(0.15f, 0.15f, 0.22f);
        private static Color ColH3         => Pro ? new Color(0.68f, 0.68f, 0.74f) : new Color(0.28f, 0.28f, 0.35f);
        private static Color ColCodeInline => Pro ? new Color(0.82f, 0.57f, 0.36f) : new Color(0.55f, 0.28f, 0.08f); // warm orange
        private static Color ColCodeBlock  => Pro ? new Color(0.75f, 0.85f, 1.00f) : new Color(0.15f, 0.35f, 0.65f); // light blue
        private static Color ColCodeBg     => Pro ? new Color(0.11f, 0.11f, 0.13f) : new Color(0.92f, 0.92f, 0.94f);
        private static Color ColTypeCol    => Pro ? new Color(0.35f, 0.75f, 0.95f) : new Color(0.08f, 0.42f, 0.70f); // cyan-blue (V2 type col)
        private static Color ColLink       => Pro ? new Color(0.42f, 0.72f, 1.00f) : new Color(0.08f, 0.38f, 0.82f); // sky blue
        private static Color ColHighlight  => Pro ? new Color(1.00f, 0.88f, 0.25f) : new Color(0.70f, 0.52f, 0.00f); // gold
        private static Color ColStrike     => Pro ? new Color(0.50f, 0.50f, 0.50f) : new Color(0.55f, 0.55f, 0.55f);
        private static Color ColDim        => Pro ? new Color(0.44f, 0.44f, 0.44f) : new Color(0.58f, 0.58f, 0.58f);
        private static Color ColHR         => Pro ? new Color(0.72f, 0.72f, 0.72f) : new Color(0.5f, 0.5f, 0.5f);
        private static Color ColBorder     => Pro ? new Color(0.26f, 0.56f, 1.00f) : new Color(0.10f, 0.38f, 0.90f);
        private static Color ColFmBg       => Pro ? new Color(0.09f, 0.12f, 0.18f) : new Color(0.86f, 0.90f, 0.97f);
        private static Color ColFmText     => Pro ? new Color(0.62f, 0.76f, 0.92f) : new Color(0.07f, 0.22f, 0.46f);
        private static Color ColTableHdr   => Pro ? new Color(0.17f, 0.17f, 0.22f) : new Color(0.76f, 0.76f, 0.82f);
        private static Color ColTableAlt   => Pro ? new Color(0.14f, 0.14f, 0.14f) : new Color(0.95f, 0.95f, 0.95f);
        private static Color ColTableRow   => Pro ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.99f, 0.99f, 0.99f);

        // ── Section & access-level accents ────────────────────────────────────

        private static Color AccSummary  => Pro ? new Color(0.30f, 0.60f, 1.00f) : new Color(0.14f, 0.42f, 0.90f);
        private static Color AccFields   => Pro ? new Color(0.25f, 0.85f, 0.68f) : new Color(0.05f, 0.60f, 0.44f);
        private static Color AccMethods  => Pro ? new Color(0.72f, 0.42f, 1.00f) : new Color(0.52f, 0.18f, 0.88f);
        private static Color AccValues   => Pro ? new Color(1.00f, 0.72f, 0.25f) : new Color(0.80f, 0.48f, 0.02f);
        private static Color AccDefault  => ColH2;

        private static Color BadgePublic    => Pro ? new Color(0.16f, 0.56f, 0.28f) : new Color(0.08f, 0.48f, 0.20f);
        private static Color BadgePrivate   => Pro ? new Color(0.68f, 0.24f, 0.16f) : new Color(0.62f, 0.14f, 0.06f);
        private static Color BadgeProtected => Pro ? new Color(0.68f, 0.50f, 0.08f) : new Color(0.58f, 0.38f, 0.00f);
        private static Color BadgeCategory  => Pro ? new Color(0.44f, 0.26f, 0.72f) : new Color(0.34f, 0.12f, 0.66f);
        private static Color BadgeTag       => Pro ? new Color(0.24f, 0.24f, 0.32f) : new Color(0.52f, 0.52f, 0.60f);

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Applies user-defined style and color overrides from <paramref name="settings"/>.
        /// Call this whenever settings change; forces a style rebuild on the next Render.
        /// </summary>
        public void Configure(ObsidocSettings settings)
        {
            if (settings == null) { _hasConfig = false; _built = false; return; }
            _hasConfig           = true;
            _cfgAccSummary       = settings.AccentSummary;
            _cfgAccFields        = settings.AccentFields;
            _cfgAccMethods       = settings.AccentMethods;
            _cfgAccValues        = settings.AccentValues;
            _cfgBadgePublic      = settings.ModifierPublic;
            _cfgBadgePrivate     = settings.ModifierPrivate;
            _cfgBadgeProtected   = settings.ModifierProtected;
            _cfgH1Size           = settings.H1FontSize;
            _cfgH2Size           = settings.H2FontSize;
            _cfgH3Size           = settings.H3FontSize;
            _built               = false; // force style rebuild
        }

        /// <summary>
        /// Renders <paramref name="blocks"/> via IMGUI.
        /// Returns <c>true</c> if a task item was toggled this frame (the caller should save the file).
        /// </summary>
        public bool Render(List<MarkdownBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0) return false;
            _dirty             = false;
            _inSummarySection  = false;
            _inFieldsSection      = false;
            _inPropertiesSection  = false;
            _inValuesSection      = false;
            _inMethodsSection     = false;
            EnsureStyles();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            foreach (var b in blocks) RenderBlock(b);

            GUILayout.Space(16);
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            return _dirty;
        }

        // ── Block dispatch ────────────────────────────────────────────────────

        private void RenderBlock(MarkdownBlock b)
        {
            if (b.Type == BlockType.Heading && b.Level == 2)
            {
                string h = b.RawText ?? string.Empty;
                _inSummarySection    = string.Equals(h, "Summary",    StringComparison.OrdinalIgnoreCase);
                _inFieldsSection     = string.Equals(h, "Fields",     StringComparison.OrdinalIgnoreCase);
                _inPropertiesSection = string.Equals(h, "Properties", StringComparison.OrdinalIgnoreCase);
                _inValuesSection     = string.Equals(h, "Values",     StringComparison.OrdinalIgnoreCase);
                _inMethodsSection    = string.Equals(h, "Methods",    StringComparison.OrdinalIgnoreCase);
            }

            switch (b.Type)
            {
                case BlockType.Frontmatter:     DrawFrontmatter(b);     break;
                case BlockType.Heading:         DrawHeading(b);         break;
                case BlockType.CodeBlock:       DrawCodeBlock(b);       break;
                case BlockType.HorizontalRule:  DrawHR();               break;
                case BlockType.Blockquote:      DrawBlockquote(b);      break;
                case BlockType.Table:           DrawTable(b);           break;
                case BlockType.ListItem:        DrawListItem(b, false); break;
                case BlockType.OrderedListItem: DrawListItem(b, true);  break;
                case BlockType.Paragraph:       DrawParagraph(b);       break;
                case BlockType.UserContent:     DrawUserContent(b);     break;
                case BlockType.TaskItem:        DrawTaskItem(b);        break;
                case BlockType.Image:           DrawImage(b);           break;
            }
        }

        // ── Frontmatter card ──────────────────────────────────────────────────

        private void DrawFrontmatter(MarkdownBlock b)
        {
            var kv = ParseFrontmatterKV(b.RawText);
            kv.TryGetValue("class",     out string className);
            kv.TryGetValue("kind",      out string kind);
            kv.TryGetValue("namespace", out string ns);
            kv.TryGetValue("category",  out string category);
            kv.TryGetValue("tags",      out string tagsRaw);
            kv.TryGetValue("updated",   out string updated);

            EditorGUILayout.Space(4);
            Rect card = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(card, ColFmBg);
                EditorGUI.DrawRect(new Rect(card.x, card.y, card.width, 2f), KindColor(kind));
            }
            GUILayout.Space(10);

            // Row 1 — kind badge + date
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            if (!string.IsNullOrEmpty(kind))
                GUILayout.Label(kind, Badge(KindColor(kind)));
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(updated))
                GUILayout.Label($"updated  {updated}", _metaStyle);
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            // Row 2 — class name (large, clickable → ping script)
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            GUILayout.Label(className ?? "—", _h1, GUILayout.ExpandWidth(true));
            Rect classRect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(classRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && classRect.Contains(Event.current.mousePosition))
            {
                PingScript(className);
                Event.current.Use();
            }
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            // Row 3 — namespace (only if non-empty)
            if (!string.IsNullOrEmpty(ns))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12);
                GUILayout.Label(ns, _metaStyle);
                GUILayout.Space(12);
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(8);

            // Row 4 — category + tags
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            if (!string.IsNullOrEmpty(category))
                GUILayout.Label(category, Badge(BadgeCategory));
            if (!string.IsNullOrEmpty(tagsRaw))
                foreach (string tag in ParseTagList(tagsRaw))
                    if (!string.IsNullOrEmpty(tag))
                        GUILayout.Label(tag.Trim(), Badge(BadgeTag));
            GUILayout.FlexibleSpace();
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(8);
        }

        // ── Headings ──────────────────────────────────────────────────────────

        private void DrawHeading(MarkdownBlock b)
        {
            switch (b.Level)
            {
                case 1:  DrawH1(b);     break;
                case 2:  DrawH2(b);     break;
                case 3:  DrawH3(b);     break;
                default: DrawH4Plus(b); break;
            }
        }

        private void DrawH1(MarkdownBlock b)
        {
            EditorGUILayout.Space(10);
            GUILayout.Label(ToRich(b.Spans), _h1, GUILayout.ExpandWidth(true));
            Rect r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, ColH1 * 0.30f);
            EditorGUILayout.Space(4);
        }

        private void DrawH2(MarkdownBlock b)
        {
            Color accent = SectionAccent(b.RawText);
            EditorGUILayout.Space(14);
            Rect rect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, Tint(accent, Pro ? 0.10f : 0.07f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), accent);
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            GUILayout.Label(ToRich(b.Spans), _h2, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(6);
        }

        private void DrawH3(MarkdownBlock b)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(4);
            GUILayout.Label(b.RawText, Badge(AccessBadgeColor(b.RawText)));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);
        }

        private void DrawH4Plus(MarkdownBlock b)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(4);
            GUILayout.Label(ToRich(b.Spans), _h4, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        // ── Code block ────────────────────────────────────────────────────────

        private void DrawCodeBlock(MarkdownBlock b)
        {
            EditorGUILayout.Space(3);

            // BeginVertical captures the total block rect for background drawing (same as DrawH2).
            Rect block = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && block.width > 1f)
            {
                Color accent = new Color(
                    ColCodeBlock.r * 0.65f,
                    ColCodeBlock.g * 0.65f,
                    ColCodeBlock.b * 0.65f);
                EditorGUI.DrawRect(block, ColCodeBg);
                EditorGUI.DrawRect(new Rect(block.x, block.y, 3f, block.height), accent);
            }

            GUILayout.Space(6);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(14);
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            if (!string.IsNullOrEmpty(b.Meta))
                GUILayout.Label(b.Meta, _metaStyle, GUILayout.ExpandWidth(true));

            GUILayout.Label(b.RawText ?? string.Empty, _codeBlock, GUILayout.ExpandWidth(true));

            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        // ── Horizontal rule ───────────────────────────────────────────────────

        private void DrawHR()
        {
            EditorGUILayout.Space(6);
            Rect r = EditorGUILayout.GetControlRect(false, 2f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(r, new Color(ColHR.r, ColHR.g, ColHR.b, 0.55f));
            EditorGUILayout.Space(6);
        }

        // ── Blockquote / callout ──────────────────────────────────────────────

        private void DrawBlockquote(MarkdownBlock b)
        {
            EditorGUILayout.Space(3);
            Rect rect = EditorGUILayout.BeginVertical();
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, Tint(ColBorder, Pro ? 0.10f : 0.06f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height), ColBorder);
            }
            GUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(10);
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            if (!string.IsNullOrEmpty(b.Meta))
                GUILayout.Label($"[!{b.Meta}]", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label(ToRich(b.Spans), _paragraph, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(3);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        // ── Table — probe-based fixed column widths (V2 style) ────────────────

        private void DrawTable(MarkdownBlock b)
        {
            if (b.Rows == null || b.Rows.Count == 0) return;

            int colCount = 0;
            foreach (var row in b.Rows)
                if (row.Cells.Count > colCount) colCount = row.Cells.Count;
            if (colCount == 0) return;

            EditorGUILayout.Space(4);

            // Probe available width exactly once, outside any horizontal group
            float totalW = EditorGUILayout.GetControlRect(false, 0f).width;

            // Find header row — used both for column sizing and for editable column detection
            TableRow headerRow = null;
            foreach (var r in b.Rows) { if (r.IsHeader) { headerRow = r; break; } }

            float[] widths = ComputeColWidths(headerRow, colCount, totalW);
            float rowH = EditorGUIUtility.singleLineHeight + 3f;

            // Find Description column index for in-place editing
            int descColIdx = -1;
            if ((_inFieldsSection || _inPropertiesSection || _inValuesSection) && headerRow != null)
            {
                for (int ci = 0; ci < headerRow.Cells.Count; ci++)
                {
                    if (string.Equals(headerRow.Cells[ci].RawText, "Description",
                            StringComparison.OrdinalIgnoreCase))
                    { descColIdx = ci; break; }
                }
            }

            bool tableDirty = false;
            bool altRow = false;
            foreach (var row in b.Rows)
            {
                Color bg = row.IsHeader ? ColTableHdr
                         : altRow      ? ColTableAlt
                         : ColTableRow;

                // Fixed-height row rect drawn directly — no GUILayout horizontal needed
                Rect rowRect = EditorGUILayout.GetControlRect(false, rowH);
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(rowRect, bg);

                float xCursor = rowRect.x + 4f;
                for (int ci = 0; ci < widths.Length && ci < row.Cells.Count; ci++)
                {
                    Rect cellRect = new Rect(xCursor, rowRect.y + 1f, widths[ci] - 6f, rowH - 2f);

                    if (row.IsHeader)
                    {
                        EditorGUI.LabelField(cellRect, row.Cells[ci].RawText, _tableHeader);
                    }
                    else if (ci == descColIdx)
                    {
                        var cell = row.Cells[ci];
                        string newDesc = EditorGUI.TextField(cellRect, cell.RawText ?? string.Empty);
                        if (newDesc != (cell.RawText ?? string.Empty))
                        {
                            cell.RawText  = newDesc;
                            cell.Spans    = ObsidocMarkdownParser.ParseInline(newDesc);
                            tableDirty    = true;
                        }
                    }
                    else
                    {
                        string rich = ToRich(row.Cells[ci].Spans);
                        EditorGUI.LabelField(cellRect, rich, _paragraph);
                    }

                    xCursor += widths[ci];
                }

                // 1-px separator between rows
                Rect sep = new Rect(rowRect.x, rowRect.yMax, rowRect.width, 1f);
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(sep, new Color(ColHR.r, ColHR.g, ColHR.b, 0.22f));

                if (!row.IsHeader) altRow = !altRow;
            }

            if (tableDirty)
            {
                b.RawText = SerializeTableRows(b.Rows);
                _dirty    = true;
            }

            EditorGUILayout.Space(4);
        }

        // ── List items ────────────────────────────────────────────────────────

        private void DrawListItem(MarkdownBlock b, bool ordered)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8 + b.Level * 16f);
            GUILayout.Label(ordered ? "•" : "–", GUILayout.Width(14));
            GUILayout.Label(ToRich(b.Spans), _listItem, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }

        // ── Task list item ────────────────────────────────────────────────────

        private static readonly Regex ReTaskLine = new Regex(
            @"^(\s*)[-*+]\s+\[([ xX])\]\s*(.*)$", RegexOptions.Compiled);

        // RawText contains one or more raw task lines: "- [ ] text" / "  - [x] sub-task"
        private void DrawTaskItem(MarkdownBlock b)
        {
            if (string.IsNullOrEmpty(b.RawText)) return;

            string[] lines  = b.RawText.Split('\n');
            bool     changed = false;

            for (int li = 0; li < lines.Length; li++)
            {
                string rawLine = lines[li].TrimEnd('\r');
                var m = ReTaskLine.Match(rawLine);
                if (!m.Success) continue;

                int    level     = m.Groups[1].Length / 2;
                bool   isChecked = m.Groups[2].Value.ToLowerInvariant() == "x";
                string content   = m.Groups[3].Value;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(8 + level * 16f);
                bool newVal = EditorGUILayout.Toggle(isChecked, GUILayout.Width(16));
                if (newVal != isChecked)
                {
                    lines[li] = m.Groups[1].Value + "- [" + (newVal ? "x" : " ") + "] " + content;
                    changed = true;
                }
                GUIStyle style = isChecked ? _listItemDim : _listItem;
                GUILayout.Label(ToRich(ObsidocMarkdownParser.ParseInline(content)), style,
                    GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }

            if (changed)
            {
                b.RawText = string.Join("\n", lines);
                _dirty = true;
            }
        }

        // ── Image ─────────────────────────────────────────────────────────────

        private void DrawImage(MarkdownBlock b)
        {
            string src = b.RawText ?? string.Empty;
            EditorGUILayout.Space(4);

            Texture2D tex = LoadTexture(src);
            if (tex != null)
            {
                // Probe available width, then scale to fit preserving aspect (max height 240 px)
                Rect probe  = EditorGUILayout.GetControlRect(false, 0f, GUILayout.ExpandWidth(true));
                float availW = Mathf.Max(probe.width - 16f, 40f);
                float aspect = (float)tex.width / Mathf.Max(tex.height, 1);
                float dispW  = Mathf.Min(availW, tex.width);
                float dispH  = Mathf.Min(dispW / aspect, 240f);
                dispW        = dispH * aspect;

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(4);
                Rect imgRect = GUILayoutUtility.GetRect(dispW, dispH,
                    GUILayout.Width(dispW), GUILayout.Height(dispH));
                if (Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(imgRect, tex, ScaleMode.ScaleToFit, true);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(b.Meta))
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(4);
                    GUILayout.Label(b.Meta, _metaStyle);
                    EditorGUILayout.EndHorizontal();
                }
            }
            else if (!string.IsNullOrEmpty(src))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(4);
                GUILayout.Label($"[Image: {src}]", _emptyDesc, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
        }

        private Texture2D LoadTexture(string src)
        {
            if (string.IsNullOrEmpty(src)) return null;
            if (_textureCache.TryGetValue(src, out var cached)) return cached;

            Texture2D tex = null;

            // Project-relative path (Assets/...)
            if (src.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
            {
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(src);
            }
            else
            {
                // Absolute or relative-to-BaseDir path
                string full = src;
                if (!Path.IsPathRooted(src) && !string.IsNullOrEmpty(_basedir))
                    full = Path.Combine(_basedir, src);
                if (File.Exists(full))
                {
                    byte[] bytes = File.ReadAllBytes(full);
                    tex = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    if (!tex.LoadImage(bytes))
                    {
                        UnityEngine.Object.DestroyImmediate(tex);
                        tex = null;
                    }
                }
            }

            _textureCache[src] = tex; // cache hit or miss
            return tex;
        }

        // ── User-content zone ─────────────────────────────────────────────────

        private void DrawUserContent(MarkdownBlock b)
        {
            if (string.IsNullOrEmpty(b.RawText)) return;
            EditorGUILayout.Space(3);
            Rect rect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, Tint(ColHighlight, Pro ? 0.06f : 0.04f));
                EditorGUI.DrawRect(new Rect(rect.x, rect.y, 3f, rect.height),
                    new Color(ColHighlight.r, ColHighlight.g, ColHighlight.b, 0.55f));
            }
            GUILayout.Space(4);
            var inner = ObsidocMarkdownParser.Parse(b.RawText);
            foreach (var ib in inner) RenderBlock(ib);
            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        // ── Paragraph ─────────────────────────────────────────────────────────

        private void DrawParagraph(MarkdownBlock b)
        {
            bool isEmpty   = b.RawText.Trim() == "*No description.*";
            bool editable  = (_inSummarySection && !isEmpty) || _inMethodsSection;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(4);
            if (editable)
            {
                // Methods: strip *…* markers for display, re-wrap on save
                string displayText = _inMethodsSection
                    ? (isEmpty ? string.Empty : StripItalicMarkers(b.RawText))
                    : (b.RawText ?? string.Empty);

                string newText = EditorGUILayout.TextArea(
                    displayText,
                    GUILayout.ExpandWidth(true), GUILayout.MinHeight(40));
                if (newText != displayText)
                {
                    b.RawText = _inMethodsSection ? WrapItalicMarkers(newText) : newText;
                    b.Spans   = ObsidocMarkdownParser.ParseInline(b.RawText);
                    _dirty    = true;
                }
            }
            else
            {
                GUILayout.Label(ToRich(b.Spans), isEmpty ? _emptyDesc : _paragraph, GUILayout.ExpandWidth(true));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        private static string StripItalicMarkers(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            string t = raw.Trim();
            if (t.Length >= 2 && t[0] == '*' && t[t.Length - 1] == '*')
                return t.Substring(1, t.Length - 2);
            return t;
        }

        private static string WrapItalicMarkers(string text)
        {
            string t = (text ?? string.Empty).Trim();
            return string.IsNullOrEmpty(t) ? "*No description.*" : "*" + t + "*";
        }

        // ── Inline → rich text ────────────────────────────────────────────────
        //
        //   < and > are replaced with Unicode full-width look-alikes ﹤ ﹥
        //   so Unity's rich-text parser never misreads C# generics (List﹤T﹥).
        //   This matches the strategy from ObsiDocV2's InlineMarkdownRenderer.

        private string ToRich(List<InlineSpan> spans)
        {
            if (spans == null || spans.Count == 0) return string.Empty;

            string linkHex = Hex(ColLink);
            string codeHex = Hex(ColCodeInline);
            string highHex = Hex(ColHighlight);
            string striHex = Hex(ColStrike);
            string typeHex = Hex(ColTypeCol);

            var sb = new StringBuilder();
            foreach (var s in spans)
            {
                string t = Esc(s.Text);
                switch (s.Type)
                {
                    case SpanType.Text:
                        sb.Append(t);
                        break;
                    case SpanType.Bold:
                        sb.Append($"<b>{t}</b>");
                        break;
                    case SpanType.Italic:
                        sb.Append($"<i>{t}</i>");
                        break;
                    case SpanType.BoldItalic:
                        sb.Append($"<b><i>{t}</i></b>");
                        break;
                    case SpanType.Strikethrough:
                        sb.Append($"<color=#{striHex}>{t}</color>");
                        break;
                    case SpanType.Highlight:
                        sb.Append($"<color=#{highHex}>{t}</color>");
                        break;
                    case SpanType.Code:
                        sb.Append($"<color=#{codeHex}>{t}</color>");
                        break;
                    case SpanType.Wikilink:
                        sb.Append($"<color=#{linkHex}><i>{t} ↗</i></color>");
                        break;
                    case SpanType.Link:
                        sb.Append($"<color=#{linkHex}>{t} ↗</color>");
                        break;
                    case SpanType.Embed:
                        sb.Append($"<color=#{linkHex}><i>![[{t}]]</i></color>");
                        break;
                }
            }
            return sb.ToString();
        }

        // ── Badge factory ─────────────────────────────────────────────────────

        private GUIStyle Badge(Color bg)
        {
            string key = Hex(bg);
            if (_badgeCache.TryGetValue(key, out var cached)) return cached;

            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, bg);
            tex.Apply();

            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                padding  = new RectOffset(6, 6, 3, 3),
                margin   = new RectOffset(2, 2, 2, 2),
                richText = false,
                normal   = { background = tex, textColor = new Color(0.94f, 0.94f, 0.94f) }
            };
            _badgeCache[key] = style;
            return style;
        }

        // ── Column widths (V2-style probe approach) ───────────────────────────

        private static float[] ComputeColWidths(TableRow headerRow, int count, float totalW)
        {
            if (count == 0) return new float[0];
            if (count == 1) return new[] { totalW };

            // Assign a width fraction to each column based on its header name.
            // "description" is the flex column — it gets all remaining space.
            float[] fracs   = new float[count];
            int     flexIdx = -1;
            float   fracSum = 0f;

            for (int i = 0; i < count; i++)
            {
                string name = (headerRow != null && i < headerRow.Cells.Count)
                    ? (headerRow.Cells[i].RawText ?? string.Empty).Trim().ToLowerInvariant()
                    : string.Empty;

                switch (name)
                {
                    case "type":        fracs[i] = 0.15f; break;
                    case "name":        fracs[i] = 0.20f; break;
                    case "value":       fracs[i] = 0.10f; break;
                    case "accesseurs":  fracs[i] = 0.14f; break;
                    case "description": flexIdx  = i;     break; // stays 0, filled below
                    default:            fracs[i] = 1f / count; break;
                }
                fracSum += fracs[i];
            }

            if (flexIdx >= 0)
            {
                fracs[flexIdx] = Mathf.Max(0.20f, 1f - fracSum);
            }
            else if (fracSum > 0f && Mathf.Abs(fracSum - 1f) > 0.001f)
            {
                // No flex column — normalize so fracs sum to exactly 1
                for (int i = 0; i < count; i++) fracs[i] /= fracSum;
            }

            const float MinColW = 40f;
            float[] widths = new float[count];
            for (int i = 0; i < count; i++)
                widths[i] = Mathf.Max(MinColW, totalW * fracs[i]);

            return widths;
        }

        // ── Accent / kind / access colour maps ────────────────────────────────

        private Color SectionAccent(string heading)
        {
            if (_hasConfig)
            {
                if (heading == null) return _cfgAccSummary;
                switch (heading.ToLowerInvariant())
                {
                    case "summary": return _cfgAccSummary;
                    case "fields":  return _cfgAccFields;
                    case "methods": return _cfgAccMethods;
                    case "values":  return _cfgAccValues;
                    default:        return _cfgAccSummary;
                }
            }
            if (heading == null) return AccDefault;
            switch (heading.ToLowerInvariant())
            {
                case "summary": return AccSummary;
                case "fields":  return AccFields;
                case "methods": return AccMethods;
                case "values":  return AccValues;
                default:        return AccDefault;
            }
        }

        private static Color KindColor(string kind)
        {
            if (kind == null) return new Color(0.36f, 0.36f, 0.50f);
            switch (kind.ToLowerInvariant())
            {
                case "class":          return new Color(0.24f, 0.52f, 0.90f);
                case "static class":   return new Color(0.20f, 0.76f, 0.82f);
                case "abstract class": return new Color(0.90f, 0.58f, 0.20f);
                case "sealed class":   return new Color(0.60f, 0.30f, 0.90f);
                case "struct":         return new Color(0.20f, 0.80f, 0.60f);
                case "enum":           return new Color(0.90f, 0.72f, 0.16f);
                case "interface":      return new Color(0.22f, 0.80f, 0.42f);
                default:               return new Color(0.36f, 0.36f, 0.50f);
            }
        }

        private Color AccessBadgeColor(string label)
        {
            if (label == null) return BadgeTag;
            string low = label.ToLowerInvariant();
            if (_hasConfig)
            {
                if (low.Contains("public"))    return _cfgBadgePublic;
                if (low.Contains("private"))   return _cfgBadgePrivate;
                if (low.Contains("protected")) return _cfgBadgeProtected;
                return BadgeTag;
            }
            if (low.Contains("public"))    return BadgePublic;
            if (low.Contains("private"))   return BadgePrivate;
            if (low.Contains("protected")) return BadgeProtected;
            return BadgeTag;
        }

        // ── Table serialization helper ────────────────────────────────────────

        private static string SerializeTableRows(List<TableRow> rows)
        {
            var sb = new StringBuilder();
            bool separatorWritten = false;
            foreach (var row in rows)
            {
                sb.Append("|");
                foreach (var cell in row.Cells)
                {
                    sb.Append(" ");
                    sb.Append(cell.RawText ?? string.Empty);
                    sb.Append(" |");
                }
                sb.AppendLine();
                if (row.IsHeader && !separatorWritten)
                {
                    sb.Append("|");
                    foreach (var cell in row.Cells) sb.Append(" --- |");
                    sb.AppendLine();
                    separatorWritten = true;
                }
            }
            return sb.ToString().TrimEnd();
        }

        // ── Frontmatter YAML helpers ──────────────────────────────────────────

        private static string TagsToDisplay(string yamlTags)
        {
            if (string.IsNullOrEmpty(yamlTags)) return string.Empty;
            string raw = yamlTags.Trim();
            if (raw.StartsWith("[") && raw.EndsWith("]"))
                raw = raw.Substring(1, raw.Length - 2);
            return raw.Trim();
        }

        private static string DisplayToTagsYaml(string display)
        {
            if (display == null) return "[]";
            string trimmed = display.Trim();
            if (string.IsNullOrEmpty(trimmed)) return "[]";
            string[] parts = trimmed.Split(',');
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (!first) sb.Append(", ");
                sb.Append(t);
                first = false;
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string UpdateFrontmatterKey(string raw, string key, string newValue)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            string[] lines = raw.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon < 0) continue;
                if (string.Equals(lines[i].Substring(0, colon).Trim(), key, StringComparison.Ordinal))
                {
                    lines[i] = key + ": " + newValue;
                    return string.Join("\n", lines);
                }
            }
            return raw;
        }

        private static Dictionary<string, string> ParseFrontmatterKV(string raw)
        {
            var kv = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(raw)) return kv;
            foreach (string line in raw.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string k = line.Substring(0, colon).Trim();
                string v = line.Substring(colon + 1).Trim();
                if (!string.IsNullOrEmpty(k)) kv[k] = v;
            }
            return kv;
        }

        private static string[] ParseTagList(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return new string[0];
            raw = raw.Trim();
            if (raw.StartsWith("[") && raw.EndsWith("]"))
                raw = raw.Substring(1, raw.Length - 2);
            return raw.Split(',');
        }

        // ── Script ping ───────────────────────────────────────────────────────

        private static void PingScript(string classValue)
        {
            if (string.IsNullOrEmpty(classValue)) return;
            // Strip generics: "Singleton<T>" → "Singleton"
            int    generic  = classValue.IndexOf('<');
            string basePart = generic >= 0 ? classValue.Substring(0, generic) : classValue;
            // Strip namespace: "MyNamespace.Singleton" → "Singleton"
            int    lastDot  = basePart.LastIndexOf('.');
            string name     = lastDot >= 0 ? basePart.Substring(lastDot + 1) : basePart;
            if (string.IsNullOrEmpty(name)) return;

            string[] guids = AssetDatabase.FindAssets($"t:MonoScript {name}");
            foreach (string guid in guids)
            {
                string path   = AssetDatabase.GUIDToAssetPath(guid);
                var    script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.name == name)
                {
                    Selection.activeObject = script;
                    EditorGUIUtility.PingObject(script);
                    return;
                }
            }
        }

        // ── Misc helpers ──────────────────────────────────────────────────────

        /// <summary>Tints a colour for section backgrounds.</summary>
        private static Color Tint(Color c, float s) =>
            new Color(c.r * s, c.g * s, c.b * s, Pro ? 0.85f : 0.60f);

        /// <summary>
        /// Replaces ASCII angle brackets with Unicode full-width look-alikes ﹤ ﹥
        /// so Unity's rich-text parser never misinterprets C# generics.
        /// Strategy borrowed from ObsiDocV2 InlineMarkdownRenderer.
        /// </summary>
        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("<", "﹤").Replace(">", "﹥");
        }

        private static string Hex(Color c) =>
            $"{Mathf.RoundToInt(c.r * 255):X2}{Mathf.RoundToInt(c.g * 255):X2}{Mathf.RoundToInt(c.b * 255):X2}";

        // ── Style factory ─────────────────────────────────────────────────────

        private void EnsureStyles()
        {
            bool pro = EditorGUIUtility.isProSkin;
            if (_built && _lastProSkin == pro) return;

            // Release textures from previous build
            foreach (var s in _badgeCache.Values)
                if (s.normal.background != null)
                    UnityEngine.Object.DestroyImmediate(s.normal.background);
            _badgeCache.Clear();

            _built       = true;
            _lastProSkin = pro;

            // ── Headings
            int sz1 = _hasConfig ? _cfgH1Size : 20;
            int sz2 = _hasConfig ? _cfgH2Size : 15;
            int sz3 = _hasConfig ? _cfgH3Size : 12;
            _h1 = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = sz1, richText = true, wordWrap = true, normal = { textColor = ColH1 } };
            _h2 = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = sz2, richText = true, wordWrap = true, normal = { textColor = ColH2 } };
            _h3 = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = sz3, richText = true, wordWrap = true, normal = { textColor = ColH3 } };
            _h4 = new GUIStyle(EditorStyles.boldLabel)
                { fontSize = Mathf.Max(9, sz3 - 1), richText = true, wordWrap = true, normal = { textColor = ColH3 } };

            // ── Body text
            _paragraph = new GUIStyle(EditorStyles.wordWrappedLabel)
                { richText = true, normal = { textColor = ColText } };
            _listItem  = new GUIStyle(EditorStyles.wordWrappedLabel)
                { richText = true, normal = { textColor = ColText } };
            _listItemDim = new GUIStyle(_listItem) { normal = { textColor = ColDim } };
            _emptyDesc = new GUIStyle(EditorStyles.wordWrappedLabel)
                { richText = true, fontStyle = FontStyle.Italic, normal = { textColor = ColDim } };

            // ── Code — block uses light blue (V2 _codeStyle), inline uses warm orange (V2 HexCode)
            // Note: Font.CreateDynamicFontFromOSFont is avoided here — dynamic fonts delay glyph
            // generation until after the first render, causing GUILayout to compute height=0 and
            // the label to be invisible. We use the default editor font with colour-only styling.
            _codeBlock = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                richText = false,   // raw — ﹤﹥ are already in the text, no rich-text parsing needed
                wordWrap = true,
                normal   = { textColor = ColCodeBlock }
            };

            _codeInline = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                normal   = { textColor = ColTypeCol }   // type column: cyan-blue
            };

            // ── Frontmatter
            _frontmatter = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                richText = false,
                wordWrap = true,
                normal   = { textColor = ColFmText }
            };

            // ── Table header
            _tableHeader = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = ColH3 }
            };

            // ── Meta / dates
            _metaStyle = new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = ColDim } };
        }
    }
}
