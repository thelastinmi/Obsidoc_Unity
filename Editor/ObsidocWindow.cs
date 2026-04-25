using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Obsi.Doc.Editor
{
    /// <summary>
    /// Editor window for the Obsidoc documentation generator.
    /// Open via menu: Obsi > Documentation
    /// </summary>
    public class ObsidocWindow : EditorWindow
    {
        // ── Scan / generation ─────────────────────────────────────────────────
        private ObsidocScanner.ScanResult _lastScan;
        private GenerationReport          _lastReport;
        private bool                      _hasGenerated;

        // ── Settings ─────────────────────────────────────────────────────────
        private bool             _showSettings;
        private ObsidocSettings  _settings;
        private SerializedObject _serializedSettings;

        // ── Settings sub-tabs ─────────────────────────────────────────────────
        private int     _settingsTab          = 0;   // 0=Générale, 1=Style, 2=Colors
        private Vector2 _settingsScroll;
        private Color   _newPaletteColor      = new Color(0.50f, 0.50f, 0.50f, 0.32f);
        private string  _newPaletteName       = string.Empty;
        private bool    _showDefaultMethods   = false;

        private static readonly (string Group, string[] Methods)[] DefaultMethodGroups =
        {
            ("Cycle de vie",   new[]{ "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy", "Reset" }),
            ("Update",         new[]{ "Update", "FixedUpdate", "LateUpdate" }),
            ("Physique 3D",    new[]{ "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit",
                                      "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit" }),
            ("Physique 2D",    new[]{ "OnCollisionEnter2D", "OnCollisionStay2D", "OnCollisionExit2D",
                                      "OnTriggerEnter2D", "OnTriggerStay2D", "OnTriggerExit2D" }),
            ("Souris",         new[]{ "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton",
                                      "OnMouseEnter", "OnMouseExit", "OnMouseOver", "OnMouseDrag" }),
            ("Rendu",          new[]{ "OnBecameVisible", "OnBecameInvisible",
                                      "OnWillRenderObject", "OnRenderObject",
                                      "OnPreRender", "OnPostRender", "OnRenderImage" }),
            ("Animation",      new[]{ "OnAnimatorMove", "OnAnimatorIK" }),
            ("Editor",         new[]{ "OnValidate", "OnDrawGizmos", "OnDrawGizmosSelected" }),
            ("Application",    new[]{ "OnApplicationPause", "OnApplicationFocus", "OnApplicationQuit" }),
        };

        // ── Split panel ───────────────────────────────────────────────────────
        private float       _splitPosition   = 180f;
        private bool        _isDraggingSplit;
        private const float SplitterWidth    = 5f;
        private const float PanelMinWidth    = 80f;

        // ── Left panel ────────────────────────────────────────────────────────
        private Vector2                  _leftScroll;
        private string                   _selectedFile;
        private Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();

        // ── Right panel ───────────────────────────────────────────────────────
        private Vector2                    _rightScroll;
        private string                     _fileContent;
        private List<MarkdownBlock>        _parsedBlocks;
        private readonly ObsidocMarkdownRenderer _renderer = new ObsidocMarkdownRenderer();
        private bool                       _renderedView = true;

        // ── Edit mode ─────────────────────────────────────────────────────────
        private bool                _editMode       = false;
        private List<MarkdownBlock> _editBlocks;
        private int                 _selectedBlock  = -1;
        private Vector2             _editScroll;
        private string              _editBlockText    = string.Empty;
        private string              _editBlockMeta    = string.Empty;
        private BlockType           _editBlockType    = BlockType.Paragraph;
        private int                 _editBlockLevel   = 1;
        private bool                _editBlockChecked = false;
        private List<(bool IsChecked, int Level, string Text)> _editTaskItems;
        private static readonly Regex _reTaskLineW =
            new Regex(@"^(\s*)[-*+]\s+\[([ xX])\]\s*(.*)$", RegexOptions.Compiled);
        private int                 _dragBlockFrom  = -1;
        private int                 _dragBlockDrop  = -1;
        private Vector2             _dragBlockStart;
        private bool                _isDraggingBlock;
        private List<Rect>          _blockRects     = new List<Rect>();

        // ── Drag & drop ───────────────────────────────────────────────────────────
        private string  _dragCandidatePath;
        private Vector2 _dragStartPos;
        private string  _dropTargetDir;
        private const float DragThreshold = 6f;

        // ── Search ────────────────────────────────────────────────────────────
        private string                           _searchQuery        = string.Empty;
        private string                           _searchTagFilter    = string.Empty;
        private Dictionary<string, List<string>> _fileTags           = new Dictionary<string, List<string>>();
        private List<string>                     _allTags            = new List<string>();
        private List<string>                     _searchResults;
        private string                           _lastSearchQuery;
        private string                           _lastSearchTagFilter;
        private static readonly Regex _reTagsInline =
            new Regex(@"^tags:\s*\[([^\]]*)\]", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex _reTagsBlock =
            new Regex(@"^tags:\s*\n((?:[ \t]+-[ \t]+\S[^\n]*\n?)+)", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex _reTagItem =
            new Regex(@"^[ \t]+-[ \t]+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);

        // ─────────────────────────────────────────────────────────────────────

        [MenuItem("Obsi/Documentation")]
        public static void Open()
        {
            var window = GetWindow<ObsidocWindow>("Obsidoc");
            window.minSize = new Vector2(480, 200);
        }

        private void OnEnable()
        {
            _settings           = ObsidocSettings.GetOrCreate();
            _serializedSettings = new SerializedObject(_settings);
            _renderer.Configure(_settings);
            _renderedView = _settings.DefaultRenderedView;
            BuildTagCache();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_showSettings)
            {
                DrawSettings();
                GUILayout.FlexibleSpace();
            }
            else
            {
                DrawSplitView();
            }

            DrawStatusBar();
        }

        // ── Toolbar ───────────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                if (GUILayout.Button("Generate", EditorStyles.toolbarButton))
                    RunGenerate();

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
                    RefreshView();

                GUILayout.FlexibleSpace();

                _showSettings = GUILayout.Toggle(
                    _showSettings, "Settings",
                    EditorStyles.toolbarButton);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Settings panel ────────────────────────────────────────────────────

        private static readonly Color NavAccent    = new Color(0.30f, 0.50f, 0.90f, 1.00f);
        private static readonly Color NavAccentBg  = new Color(0.30f, 0.50f, 0.90f, 0.18f);

        private void DrawSettings()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            // ── Left navigation ───────────────────────────────────────────────
            EditorGUILayout.BeginVertical(GUILayout.Width(88), GUILayout.ExpandHeight(true));
            GUILayout.Space(6);
            DrawSettingsNavTab("Générale", 0);
            DrawSettingsNavTab("Style",    1);
            DrawSettingsNavTab("Colors",   2);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();

            // ── Separator ─────────────────────────────────────────────────────
            Rect sep = GUILayoutUtility.GetRect(1f, 1f,
                GUILayout.Width(1f), GUILayout.ExpandHeight(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(sep, new Color(0.15f, 0.15f, 0.15f));

            // ── Tab content ───────────────────────────────────────────────────
            _settingsScroll = EditorGUILayout.BeginScrollView(
                _settingsScroll, GUILayout.ExpandWidth(true));
            EditorGUILayout.Space(6);
            switch (_settingsTab)
            {
                case 0: DrawGeneraleSettings(); break;
                case 1: DrawStyleSettings();    break;
                case 2: DrawColorsSettings();   break;
            }
            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSettingsNavTab(string label, int index)
        {
            bool selected = _settingsTab == index;
            Rect r = GUILayoutUtility.GetRect(88f, 24f, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                if (selected)
                {
                    EditorGUI.DrawRect(r, NavAccentBg);
                    EditorGUI.DrawRect(new Rect(r.x, r.y, 3f, r.height), NavAccent);
                }
                GUIStyle style = selected ? EditorStyles.boldLabel : EditorStyles.label;
                GUI.Label(new Rect(r.x + 10f, r.y + 3f, r.width - 12f, r.height), label, style);
            }
            else if (Event.current.type == EventType.MouseDown
                && r.Contains(Event.current.mousePosition))
            {
                _settingsTab = index;
                Repaint();
                Event.current.Use();
            }
        }

        // ── Générale ──────────────────────────────────────────────────────────

        private void DrawGeneraleSettings()
        {
            EditorGUILayout.LabelField("Général", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _serializedSettings.Update();

            DrawFolderField("OutputFolder", "Output Folder");

            EditorGUILayout.PropertyField(
                _serializedSettings.FindProperty("SubFolder"),
                new GUIContent("Sub-folder"));

            EditorGUILayout.PropertyField(
                _serializedSettings.FindProperty("ArchivedFolder"),
                new GUIContent("Archive Sub-folder"));

            EditorGUILayout.PropertyField(
                _serializedSettings.FindProperty("ImagesFolder"),
                new GUIContent("Images Sub-folder"));

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Méthodes exclues", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            // ── Unity par défaut (lecture seule) ──────────────────────────────
            _showDefaultMethods = EditorGUILayout.Foldout(
                _showDefaultMethods,
                $"Unity — par défaut ({ObsidocSettings.UnityDefaultExcludedMethods.Length})",
                true);

            if (_showDefaultMethods)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.indentLevel++;
                foreach (var (group, methods) in DefaultMethodGroups)
                {
                    EditorGUILayout.LabelField(group, EditorStyles.miniLabel);
                    EditorGUI.indentLevel++;
                    foreach (string m in methods)
                        EditorGUILayout.LabelField(m, EditorStyles.label);
                    EditorGUI.indentLevel--;
                }
                EditorGUI.indentLevel--;
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(4);

            // ── Méthodes additionnelles (éditables) ───────────────────────────
            EditorGUILayout.PropertyField(
                _serializedSettings.FindProperty("ExcludedMethods"),
                new GUIContent("Additionnelles"),
                includeChildren: true);

            EditorGUI.indentLevel--;

            if (_serializedSettings.ApplyModifiedProperties())
                _renderer.Configure(_settings);

            EditorGUI.indentLevel--;
        }

        // ── Style ─────────────────────────────────────────────────────────────

        private void DrawStyleSettings()
        {
            EditorGUILayout.LabelField("Style", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _serializedSettings.Update();

            EditorGUILayout.PropertyField(
                _serializedSettings.FindProperty("DefaultRenderedView"),
                new GUIContent("Vue par défaut rendue"));

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Taille des titres", EditorStyles.boldLabel);

            EditorGUILayout.IntSlider(
                _serializedSettings.FindProperty("H1FontSize"), 12, 32,
                new GUIContent("Titre H1"));

            EditorGUILayout.IntSlider(
                _serializedSettings.FindProperty("H2FontSize"), 10, 24,
                new GUIContent("Titre H2"));

            EditorGUILayout.IntSlider(
                _serializedSettings.FindProperty("H3FontSize"), 9, 18,
                new GUIContent("Titre H3"));

            if (_serializedSettings.ApplyModifiedProperties())
                _renderer.Configure(_settings);

            EditorGUI.indentLevel--;
        }

        // ── Colors ────────────────────────────────────────────────────────────

        private void DrawColorsSettings()
        {
            bool changed = false;

            // ── Palette de dossiers ───────────────────────────────────────────
            EditorGUILayout.LabelField("Palette de dossiers", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            for (int i = 0; i < _settings.ColorPalette.Count; i++)
            {
                var entry = _settings.ColorPalette[i];

                EditorGUILayout.BeginHorizontal();

                Color newTint = EditorGUILayout.ColorField(
                    GUIContent.none, entry.Tint,
                    showEyedropper: false, showAlpha: true, hdr: false,
                    GUILayout.Width(44));
                if (newTint != entry.Tint) { entry.Tint = newTint; changed = true; }

                string newName = EditorGUILayout.TextField(entry.Name);
                if (newName != entry.Name) { entry.Name = newName; changed = true; }

                if (GUILayout.Button("x", GUILayout.Width(22)))
                {
                    _settings.ColorPalette.RemoveAt(i);
                    changed = true;
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            // Add row
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            _newPaletteColor = EditorGUILayout.ColorField(
                GUIContent.none, _newPaletteColor,
                showEyedropper: false, showAlpha: true, hdr: false,
                GUILayout.Width(44));
            _newPaletteName = EditorGUILayout.TextField(_newPaletteName);
            EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_newPaletteName));
            if (GUILayout.Button("Ajouter", GUILayout.Width(60)))
            {
                _settings.ColorPalette.Add(new ColorPaletteEntry
                    { Name = _newPaletteName.Trim(), Tint = _newPaletteColor });
                _newPaletteName  = string.Empty;
                changed = true;
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            // Reset to defaults
            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Réinitialiser la palette", GUILayout.Width(150)))
            {
                _settings.ColorPalette = ObsidocSettings.BuildDefaultPalette();
                changed = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;

            // ── Accents de section ────────────────────────────────────────────
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Accents de section (H2)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            Color accSum = EditorGUILayout.ColorField("Summary",  _settings.AccentSummary);
            Color accFld = EditorGUILayout.ColorField("Fields",   _settings.AccentFields);
            Color accMth = EditorGUILayout.ColorField("Methods",  _settings.AccentMethods);
            Color accVal = EditorGUILayout.ColorField("Values",   _settings.AccentValues);

            if (accSum != _settings.AccentSummary) { _settings.AccentSummary = accSum; changed = true; }
            if (accFld != _settings.AccentFields)  { _settings.AccentFields  = accFld; changed = true; }
            if (accMth != _settings.AccentMethods) { _settings.AccentMethods = accMth; changed = true; }
            if (accVal != _settings.AccentValues)  { _settings.AccentValues  = accVal; changed = true; }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Réinitialiser", GUILayout.Width(100)))
            {
                _settings.AccentSummary = new Color(0.30f, 0.60f, 1.00f);
                _settings.AccentFields  = new Color(0.25f, 0.85f, 0.68f);
                _settings.AccentMethods = new Color(0.72f, 0.42f, 1.00f);
                _settings.AccentValues  = new Color(1.00f, 0.72f, 0.25f);
                changed = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;

            // ── Modificateurs d'accès ─────────────────────────────────────────
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Modificateurs d'accès (badges H3)", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            Color modPub  = EditorGUILayout.ColorField("Public",    _settings.ModifierPublic);
            Color modPrv  = EditorGUILayout.ColorField("Private",   _settings.ModifierPrivate);
            Color modPrt  = EditorGUILayout.ColorField("Protected", _settings.ModifierProtected);

            if (modPub != _settings.ModifierPublic)    { _settings.ModifierPublic    = modPub; changed = true; }
            if (modPrv != _settings.ModifierPrivate)   { _settings.ModifierPrivate   = modPrv; changed = true; }
            if (modPrt != _settings.ModifierProtected) { _settings.ModifierProtected = modPrt; changed = true; }

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Réinitialiser", GUILayout.Width(100)))
            {
                _settings.ModifierPublic    = new Color(0.16f, 0.56f, 0.28f);
                _settings.ModifierPrivate   = new Color(0.68f, 0.24f, 0.16f);
                _settings.ModifierProtected = new Color(0.68f, 0.50f, 0.08f);
                changed = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;

            if (changed)
            {
                EditorUtility.SetDirty(_settings);
                AssetDatabase.SaveAssets();
                _renderer.Configure(_settings);
                Repaint();
            }
        }

        /// <summary>Draws a text field + "..." browse button for a folder path property.</summary>
        private void DrawFolderField(string propertyName, string label)
        {
            var prop = _serializedSettings.FindProperty(propertyName);
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.PropertyField(prop, new GUIContent(label));

                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string selected = EditorUtility.OpenFolderPanel(
                        $"Select {label}", prop.stringValue, string.Empty);

                    if (!string.IsNullOrEmpty(selected))
                    {
                        prop.stringValue = selected;
                        _serializedSettings.ApplyModifiedProperties();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Split view ────────────────────────────────────────────────────────

        private void DrawSplitView()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            {
                // Left panel
                EditorGUILayout.BeginVertical(GUILayout.Width(_splitPosition), GUILayout.ExpandHeight(true));
                DrawLeftPanel();
                EditorGUILayout.EndVertical();

                // Splitter
                Rect splitterRect = GUILayoutUtility.GetRect(SplitterWidth, 1f,
                    GUILayout.Width(SplitterWidth), GUILayout.ExpandHeight(true));
                EditorGUI.DrawRect(splitterRect, new Color(0.15f, 0.15f, 0.15f, 1f));
                EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

                // Right panel
                EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                DrawRightPanel();
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();

            HandleSplitterDrag();
        }

        // ── Left panel ────────────────────────────────────────────────────────

        private void DrawLeftPanel()
        {
            DrawSearchBar();

            bool isSearching = !string.IsNullOrEmpty(_searchQuery) || !string.IsNullOrEmpty(_searchTagFilter);

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            {
                string rootPath = Path.GetFullPath(_settings.OutputFolder);

                if (Directory.Exists(rootPath))
                {
                    if (isSearching)
                        DrawSearchResults(rootPath);
                    else
                        DrawDirectory(rootPath);
                }
                else
                    EditorGUILayout.LabelField("Output folder not found.", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();

            string root = Path.GetFullPath(_settings.OutputFolder);

            if (Event.current.type == EventType.MouseUp)
            {
                _dragCandidatePath = null;
            }
            else if (Event.current.type == EventType.DragExited)
            {
                _dropTargetDir     = null;
                _dragCandidatePath = null;
                Repaint();
            }
            else if (Event.current.type == EventType.MouseDown
                && Event.current.button == 1
                && Event.current.mousePosition.x <= _splitPosition
                && Directory.Exists(root))
            {
                ShowFolderContextMenu(root);
                Event.current.Use();
            }
            else if (Event.current.type == EventType.DragUpdated
                && Event.current.mousePosition.x <= _splitPosition
                && Directory.Exists(root))
            {
                string dragSrc = DragAndDrop.GetGenericData("ObsidocDragPath") as string;
                if (dragSrc != null && !IsAncestorOrSelf(dragSrc, root))
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                    if (_dropTargetDir != root) { _dropTargetDir = root; Repaint(); }
                }
            }
            else if (Event.current.type == EventType.DragPerform
                && Event.current.mousePosition.x <= _splitPosition
                && Directory.Exists(root))
            {
                string dragSrc = DragAndDrop.GetGenericData("ObsidocDragPath") as string;
                if (dragSrc != null && !IsAncestorOrSelf(dragSrc, root))
                {
                    DragAndDrop.AcceptDrag();
                    _dropTargetDir = null;
                    MoveItem(dragSrc, root);
                    Event.current.Use();
                }
            }
        }

        /// <summary>Recursively draws foldable sub-folders and selectable .md file entries.
        /// Handles left-click selection, right-click context menus, and drag &amp; drop moves.</summary>
        private void DrawDirectory(string path)
        {
            // Sub-folders
            foreach (string dir in Directory.GetDirectories(path))
            {
                string dirName = Path.GetFileName(dir);
                if (dirName.StartsWith(".")) continue;
                if (!_foldouts.ContainsKey(dir)) _foldouts[dir] = true;

                _foldouts[dir] = EditorGUILayout.Foldout(_foldouts[dir], dirName, true);
                Rect dirRect = GUILayoutUtility.GetLastRect();

                // Folder colour tint, then drop-target highlight on top
                if (Event.current.type == EventType.Repaint)
                {
                    if (_settings.TryGetFolderColor(dir, out Color folderTint))
                        EditorGUI.DrawRect(dirRect, folderTint);
                    if (_dropTargetDir == dir)
                        EditorGUI.DrawRect(dirRect, new Color(0.30f, 0.60f, 1.00f, 0.22f));
                }

                if (Event.current.type == EventType.MouseDrag && _dragCandidatePath == dir
                    && Vector2.Distance(Event.current.mousePosition, _dragStartPos) >= DragThreshold)
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.SetGenericData("ObsidocDragPath", dir);
                    DragAndDrop.objectReferences = new UnityEngine.Object[0];
                    DragAndDrop.StartDrag(dirName);
                    _dragCandidatePath = null;
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseDown
                    && dirRect.Contains(Event.current.mousePosition))
                {
                    if (Event.current.button == 0)
                    {
                        _dragCandidatePath = dir;
                        _dragStartPos      = Event.current.mousePosition;
                    }
                    else if (Event.current.button == 1)
                    {
                        ShowFolderContextMenu(dir);
                        Event.current.Use();
                    }
                }
                else if (Event.current.type == EventType.DragUpdated
                    && dirRect.Contains(Event.current.mousePosition))
                {
                    string src = DragAndDrop.GetGenericData("ObsidocDragPath") as string;
                    if (src != null && !IsAncestorOrSelf(src, dir))
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Move;
                        if (_dropTargetDir != dir) { _dropTargetDir = dir; Repaint(); }
                        Event.current.Use();
                    }
                }
                else if (Event.current.type == EventType.DragPerform
                    && dirRect.Contains(Event.current.mousePosition))
                {
                    string src = DragAndDrop.GetGenericData("ObsidocDragPath") as string;
                    if (src != null && !IsAncestorOrSelf(src, dir))
                    {
                        DragAndDrop.AcceptDrag();
                        _dropTargetDir = null;
                        Event.current.Use();
                        MoveItem(src, dir); // calls RefreshView → _foldouts.Clear(); return immediately
                        return;
                    }
                }

                // TryGetValue: guards against _foldouts being cleared mid-loop by an external call
                if (_foldouts.TryGetValue(dir, out bool folded) && folded)
                {
                    EditorGUI.indentLevel++;
                    DrawDirectory(dir);
                    EditorGUI.indentLevel--;
                }
            }

            // .md files
            foreach (string file in Directory.GetFiles(path, "*.md"))
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                bool isSelected = !string.IsNullOrEmpty(_selectedFile) &&
                    string.Equals(Path.GetFullPath(file), Path.GetFullPath(_selectedFile),
                        System.StringComparison.OrdinalIgnoreCase);

                GUIStyle style = isSelected ? EditorStyles.boldLabel : EditorStyles.label;
                EditorGUILayout.LabelField(fileName, style);
                Rect fileRect = GUILayoutUtility.GetLastRect();

                if (Event.current.type == EventType.MouseDrag && _dragCandidatePath == file
                    && Vector2.Distance(Event.current.mousePosition, _dragStartPos) >= DragThreshold)
                {
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.SetGenericData("ObsidocDragPath", file);
                    DragAndDrop.objectReferences = new UnityEngine.Object[0];
                    DragAndDrop.StartDrag(fileName);
                    _dragCandidatePath = null;
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseDown
                    && fileRect.Contains(Event.current.mousePosition))
                {
                    if (Event.current.button == 0)
                    {
                        _dragCandidatePath = file;
                        _dragStartPos      = Event.current.mousePosition;
                        SelectFile(file);
                        Event.current.Use();
                    }
                    else if (Event.current.button == 1)
                    {
                        ShowFileContextMenu(file);
                        Event.current.Use();
                    }
                }
            }
        }

        // ── Search bar & results ─────────────────────────────────────────────

        private void DrawSearchBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(2);

            string newQuery = GUILayout.TextField(
                _searchQuery, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));
            if (newQuery != _searchQuery) { _searchQuery = newQuery; _searchResults = null; Repaint(); }

            bool hasTag = !string.IsNullOrEmpty(_searchTagFilter);
            string tagLabel = hasTag ? $"#{_searchTagFilter}" : "Tag ▼";

            if (GUILayout.Button(tagLabel, EditorStyles.toolbarButton, GUILayout.Width(hasTag ? 80 : 44)))
            {
                if (hasTag) { _searchTagFilter = string.Empty; _searchResults = null; Repaint(); }
                else ShowTagMenu();
            }

            GUILayout.Space(2);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(1);
        }

        private void ShowTagMenu()
        {
            var menu = new GenericMenu();
            if (_allTags.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("(aucun tag)"));
            }
            else
            {
                foreach (string tag in _allTags)
                {
                    string captured = tag;
                    bool active = string.Equals(_searchTagFilter, tag, StringComparison.OrdinalIgnoreCase);
                    menu.AddItem(new GUIContent(tag), active, () =>
                    {
                        _searchTagFilter = active ? string.Empty : captured;
                        _searchResults   = null;
                        Repaint();
                    });
                }
            }
            menu.ShowAsContext();
        }

        private void DrawSearchResults(string rootPath)
        {
            if (!Directory.Exists(rootPath)) return;

            if (_searchResults == null
                || _lastSearchQuery     != _searchQuery
                || _lastSearchTagFilter != _searchTagFilter)
            {
                _lastSearchQuery     = _searchQuery;
                _lastSearchTagFilter = _searchTagFilter;
                _searchResults = Directory.GetFiles(rootPath, "*.md", SearchOption.AllDirectories)
                    .Where(MatchesSearch)
                    .OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (_searchResults.Count == 0)
            {
                EditorGUILayout.LabelField("Aucun résultat.", EditorStyles.miniLabel);
                return;
            }

            foreach (string file in _searchResults)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                bool isSelected = !string.IsNullOrEmpty(_selectedFile) &&
                    string.Equals(Path.GetFullPath(file), Path.GetFullPath(_selectedFile),
                        StringComparison.OrdinalIgnoreCase);

                EditorGUILayout.LabelField(fileName, isSelected ? EditorStyles.boldLabel : EditorStyles.label);
                Rect fileRect = GUILayoutUtility.GetLastRect();

                if (Event.current.type == EventType.MouseDown && fileRect.Contains(Event.current.mousePosition))
                {
                    if (Event.current.button == 0) { SelectFile(file); Event.current.Use(); }
                    else if (Event.current.button == 1) { ShowFileContextMenu(file); Event.current.Use(); }
                }
            }
        }

        private bool MatchesSearch(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);

            if (!string.IsNullOrEmpty(_searchTagFilter))
            {
                if (!_fileTags.TryGetValue(fullPath, out var tags)
                    || !tags.Any(t => string.Equals(t, _searchTagFilter, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            if (string.IsNullOrEmpty(_searchQuery)) return true;

            if (Path.GetFileNameWithoutExtension(filePath)
                    .IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (_fileTags.TryGetValue(fullPath, out var fileTags))
                return fileTags.Any(t => t.IndexOf(_searchQuery, StringComparison.OrdinalIgnoreCase) >= 0);

            return false;
        }

        private void BuildTagCache()
        {
            _fileTags.Clear();
            _allTags.Clear();
            _searchResults = null;
            if (_settings == null) return;
            string rootPath = Path.GetFullPath(_settings.OutputFolder);
            if (!Directory.Exists(rootPath)) return;

            var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(rootPath, "*.md", SearchOption.AllDirectories))
            {
                string fullPath = Path.GetFullPath(file);
                var tags = ExtractTagsFromFile(fullPath);
                _fileTags[fullPath] = tags;
                foreach (string t in tags) tagSet.Add(t);
            }
            _allTags = tagSet.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> ExtractTagsFromFile(string path)
        {
            var result = new List<string>();
            try
            {
                string content = File.ReadAllText(path, System.Text.Encoding.UTF8);
                if (!content.StartsWith("---")) return result;
                int end = content.IndexOf("\n---", 3);
                if (end < 0) return result;
                string fm = content.Substring(3, end - 3);

                var inline = _reTagsInline.Match(fm);
                if (inline.Success)
                {
                    foreach (string part in inline.Groups[1].Value.Split(','))
                    {
                        string t = part.Trim().Trim('"', '\'');
                        if (!string.IsNullOrWhiteSpace(t)) result.Add(t);
                    }
                    return result;
                }

                var block = _reTagsBlock.Match(fm);
                if (block.Success)
                {
                    foreach (Match item in _reTagItem.Matches(block.Groups[1].Value))
                        result.Add(item.Groups[1].Value.Trim());
                }
            }
            catch { }
            return result;
        }

        // ── Right panel ───────────────────────────────────────────────────────

        private void DrawRightPanel()
        {
            if (string.IsNullOrEmpty(_selectedFile))
            {
                EditorGUILayout.LabelField("Select a file to preview it.", EditorStyles.miniLabel);
                return;
            }

            // Header bar: filename + Edit + Rendered toggles
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField(Path.GetFileName(_selectedFile), EditorStyles.boldLabel);
            Rect titleRect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(titleRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && titleRect.Contains(Event.current.mousePosition))
            {
                PingCorrespondingScript();
                Event.current.Use();
            }
            GUILayout.FlexibleSpace();
            bool newEdit = GUILayout.Toggle(_editMode, "Edit", EditorStyles.toolbarButton, GUILayout.Width(50));
            if (newEdit != _editMode)
            {
                if (newEdit) EnterEditMode();
                else         CancelEdit();
            }
            if (!_editMode)
            {
                _renderedView = GUILayout.Toggle(
                    _renderedView, "Rendered", EditorStyles.toolbarButton, GUILayout.Width(70));
            }
            EditorGUILayout.EndHorizontal();

            if (_editMode)
            {
                DrawEditorView();
            }
            else
            {
                _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
                {
                    EditorGUILayout.Space(4);
                    if (_renderedView && _parsedBlocks != null)
                    {
                        bool dirty = _renderer.Render(_parsedBlocks);
                        if (dirty && !string.IsNullOrEmpty(_selectedFile))
                        {
                            _fileContent = ObsidocMarkdownSerializer.Serialize(_parsedBlocks);
                            File.WriteAllText(_selectedFile, _fileContent, System.Text.Encoding.UTF8);
                        }
                    }
                    else
                        EditorGUILayout.TextArea(
                            _fileContent ?? string.Empty,
                            EditorStyles.wordWrappedLabel,
                            GUILayout.ExpandHeight(true));
                }
                EditorGUILayout.EndScrollView();
            }
        }

        // ── Edit mode logic ───────────────────────────────────────────────────

        private void EnterEditMode()
        {
            _editBlocks = (_parsedBlocks ?? new List<MarkdownBlock>())
                .Select(CloneBlock).ToList();
            _selectedBlock   = -1;
            _editMode        = true;
            _editScroll      = Vector2.zero;
            _editTaskItems   = null;
            _isDraggingBlock = false;
            _dragBlockFrom   = -1;
            _blockRects.Clear();
            Repaint();
        }

        private void CancelEdit()
        {
            _editMode        = false;
            _editBlocks      = null;
            _selectedBlock   = -1;
            _editTaskItems   = null;
            _isDraggingBlock = false;
            _dragBlockFrom   = -1;
            Repaint();
        }

        private void SaveEditedFile()
        {
            CommitCurrentEdit();
            string text = ObsidocMarkdownSerializer.Serialize(_editBlocks);
            File.WriteAllText(_selectedFile, text, System.Text.Encoding.UTF8);
            _editMode = false;
            SelectFile(_selectedFile);
        }

        private void CommitCurrentEdit()
        {
            if (_selectedBlock < 0 || _editBlocks == null || _selectedBlock >= _editBlocks.Count) return;
            var b   = _editBlocks[_selectedBlock];
            b.Type  = _editBlockType;
            b.Level = _editBlockLevel;
            b.Meta  = string.IsNullOrEmpty(_editBlockMeta) ? null : _editBlockMeta;
            if (_editBlockType == BlockType.TaskItem && _editTaskItems != null)
            {
                b.RawText = SerializeTaskItems(_editTaskItems);
            }
            else
            {
                b.RawText   = _editBlockText;
                b.IsChecked = _editBlockChecked;
            }
            b.Spans = ObsidocMarkdownParser.ParseInline(b.RawText);
        }

        private void SelectEditBlock(int idx)
        {
            CommitCurrentEdit();
            _selectedBlock = idx;
            if (idx >= 0 && _editBlocks != null && idx < _editBlocks.Count)
            {
                var b             = _editBlocks[idx];
                _editBlockType    = b.Type;
                _editBlockLevel   = b.Type == BlockType.Heading ? Mathf.Max(1, b.Level) : Mathf.Max(0, b.Level);
                _editBlockMeta    = b.Meta ?? string.Empty;
                _editBlockText    = b.RawText ?? string.Empty;
                _editBlockChecked = b.IsChecked;
                _editTaskItems    = b.Type == BlockType.TaskItem
                    ? ParseTaskItems(b.RawText)
                    : null;
            }
        }

        private void InsertBlock(BlockType type, string meta = "", string rawText = "", int level = 0)
        {
            CommitCurrentEdit();
            var b = new MarkdownBlock(type, rawText)
            {
                Level = level,
                Meta  = string.IsNullOrEmpty(meta) ? null : meta,
            };
            b.Spans = ObsidocMarkdownParser.ParseInline(rawText);
            int insertAt = _selectedBlock >= 0 ? _selectedBlock + 1 : _editBlocks.Count;
            _editBlocks.Insert(insertAt, b);
            SelectEditBlock(insertAt);
            Repaint();
        }

        private static MarkdownBlock CloneBlock(MarkdownBlock b) =>
            new MarkdownBlock(b.Type, b.RawText) { Level = b.Level, Meta = b.Meta, IsChecked = b.IsChecked, Spans = b.Spans, Rows = b.Rows };

        // ── Editor view ───────────────────────────────────────────────────────

        private void DrawEditorView()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Sauvegarder", EditorStyles.toolbarButton, GUILayout.Width(95)))
                SaveEditedFile();
            if (GUILayout.Button("Annuler", EditorStyles.toolbarButton, GUILayout.Width(70)))
                CancelEdit();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            DrawEditorInsertToolbar();

            _editScroll = EditorGUILayout.BeginScrollView(_editScroll);
            EditorGUILayout.Space(2);
            DrawEditorBlockList();
            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        private void DrawEditorInsertToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Insérer :", EditorStyles.miniLabel, GUILayout.Width(52));
            if (GUILayout.Button("H1", EditorStyles.toolbarButton, GUILayout.Width(26))) InsertBlock(BlockType.Heading, level: 1);
            if (GUILayout.Button("H2", EditorStyles.toolbarButton, GUILayout.Width(26))) InsertBlock(BlockType.Heading, level: 2);
            if (GUILayout.Button("H3", EditorStyles.toolbarButton, GUILayout.Width(26))) InsertBlock(BlockType.Heading, level: 3);
            if (GUILayout.Button("H4", EditorStyles.toolbarButton, GUILayout.Width(26))) InsertBlock(BlockType.Heading, level: 4);
            if (GUILayout.Button("¶",  EditorStyles.toolbarButton, GUILayout.Width(22))) InsertBlock(BlockType.Paragraph);
            if (GUILayout.Button("{}",  EditorStyles.toolbarButton, GUILayout.Width(26))) InsertBlock(BlockType.CodeBlock, meta: "csharp");
            if (GUILayout.Button("—",  EditorStyles.toolbarButton, GUILayout.Width(22))) InsertBlock(BlockType.HorizontalRule);
            if (GUILayout.Button(">",  EditorStyles.toolbarButton, GUILayout.Width(22))) InsertBlock(BlockType.Blockquote);
            if (GUILayout.Button("•",  EditorStyles.toolbarButton, GUILayout.Width(22))) InsertBlock(BlockType.ListItem);
            if (GUILayout.Button("1.", EditorStyles.toolbarButton, GUILayout.Width(26))) InsertBlock(BlockType.OrderedListItem);
            if (GUILayout.Button("T",   EditorStyles.toolbarButton, GUILayout.Width(22)))
                InsertBlock(BlockType.Table, rawText: "| Col1 | Col2 |\n|------|------|\n|  |  |");
            if (GUILayout.Button("[ ]", EditorStyles.toolbarButton, GUILayout.Width(30)))
                InsertBlock(BlockType.TaskItem, rawText: "- [ ] Nouvelle tâche");
            if (GUILayout.Button("IMG", EditorStyles.toolbarButton, GUILayout.Width(30)))
                InsertBlock(BlockType.Image);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        // BlockType enum order: Frontmatter=0, Heading=1, CodeBlock=2, HorizontalRule=3,
        //                       Blockquote=4, Table=5, ListItem=6, OrderedListItem=7, Paragraph=8,
        //                       UserContent=9, TaskItem=10, Image=11
        private static readonly Color[] _blockTypeColors =
        {
            new Color(0.50f, 0.50f, 0.50f, 0.70f), // Frontmatter
            new Color(0.30f, 0.60f, 1.00f, 0.80f), // Heading
            new Color(0.25f, 0.80f, 0.45f, 0.80f), // CodeBlock
            new Color(0.40f, 0.40f, 0.40f, 0.55f), // HorizontalRule
            new Color(1.00f, 0.65f, 0.20f, 0.80f), // Blockquote
            new Color(0.20f, 0.80f, 0.90f, 0.80f), // Table
            new Color(0.60f, 0.80f, 1.00f, 0.80f), // ListItem
            new Color(0.80f, 0.60f, 1.00f, 0.80f), // OrderedListItem
            new Color(0.60f, 0.60f, 0.60f, 0.60f), // Paragraph
            new Color(1.00f, 0.85f, 0.30f, 0.80f), // UserContent
            new Color(0.25f, 0.72f, 0.38f, 0.80f), // TaskItem - vert
            new Color(0.90f, 0.55f, 0.18f, 0.80f), // Image - ambre
        };

        private static Color GetBlockTypeColor(BlockType t)
        {
            int i = (int)t;
            return i >= 0 && i < _blockTypeColors.Length ? _blockTypeColors[i] : Color.gray;
        }

        private static string GetBlockTypeLabel(MarkdownBlock b)
        {
            switch (b.Type)
            {
                case BlockType.Heading:         return "H" + b.Level;
                case BlockType.Paragraph:       return "¶";
                case BlockType.CodeBlock:       return "{}";
                case BlockType.HorizontalRule:  return "—";
                case BlockType.Blockquote:      return ">";
                case BlockType.Table:           return "T";
                case BlockType.ListItem:        return "•";
                case BlockType.OrderedListItem: return "1.";
                case BlockType.Frontmatter:     return "FM";
                case BlockType.UserContent:     return "UC";
                case BlockType.TaskItem:        return "[ ]";
                case BlockType.Image:           return "IMG";
                default:                        return "?";
            }
        }

        private static string GetBlockPreview(MarkdownBlock b)
        {
            if (b.Type == BlockType.UserContent)    return $"[user-content: {b.Meta ?? "?"}]";
            if (b.Type == BlockType.HorizontalRule) return "---";
            if (b.Type == BlockType.Frontmatter)    return "(frontmatter)";
            if (b.Type == BlockType.Image)          return b.RawText ?? string.Empty;
            if (b.Type == BlockType.TaskItem)
            {
                string raw = b.RawText ?? string.Empty;
                string firstLine = raw.Split('\n')[0].TrimEnd('\r');
                var m = _reTaskLineW.Match(firstLine);
                string text = m.Success ? m.Groups[3].Value : firstLine;
                return text.Length > 60 ? text.Substring(0, 60) + "…" : text;
            }
            string rawText = b.RawText ?? string.Empty;
            int    nl      = rawText.IndexOf('\n');
            string first   = nl >= 0 ? rawText.Substring(0, nl) : rawText;
            return first.Length > 60 ? first.Substring(0, 60) + "…" : first;
        }

        private void DrawEditorBlockList()
        {
            if (_editBlocks == null) return;

            Event e = Event.current;

            while (_blockRects.Count < _editBlocks.Count) _blockRects.Add(new Rect());
            while (_blockRects.Count > _editBlocks.Count) _blockRects.RemoveAt(_blockRects.Count - 1);

            for (int idx = 0; idx < _editBlocks.Count; idx++)
            {
                bool isSelected = _selectedBlock == idx;
                MarkdownBlock b = _editBlocks[idx];

                // Drop indicator above this block
                if (_isDraggingBlock && _dragBlockDrop == idx)
                    DrawDropIndicator();

                // Row
                EditorGUILayout.BeginHorizontal();

                // Drag handle — 6-dot grid
                Rect handleRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18), GUILayout.Height(18));
                if (e.type == EventType.Repaint)
                {
                    float cx   = handleRect.x + 4f;
                    float cy   = handleRect.y + handleRect.height * 0.5f - 5f;
                    Color dcol = new Color(1f, 1f, 1f, isSelected ? 0.6f : 0.25f);
                    for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 2; col++)
                        EditorGUI.DrawRect(new Rect(cx + col * 4f, cy + row * 4f, 2f, 2f), dcol);
                }

                // Type badge
                Rect badgeRect = GUILayoutUtility.GetRect(28f, 18f, GUILayout.Width(28), GUILayout.Height(18));
                if (e.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(badgeRect, GetBlockTypeColor(b.Type));
                    GUI.Label(badgeRect, GetBlockTypeLabel(b), _badgeLabelStyle ?? (_badgeLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontStyle = FontStyle.Bold,
                        normal    = { textColor = Color.white }
                    }));
                }

                // Preview
                EditorGUILayout.LabelField(GetBlockPreview(b),
                    isSelected ? EditorStyles.boldLabel : EditorStyles.label);

                // Delete button
                EditorGUI.BeginDisabledGroup(b.Type == BlockType.UserContent || b.Type == BlockType.Frontmatter);
                if (GUILayout.Button("x", EditorStyles.miniButton, GUILayout.Width(18)))
                {
                    CommitCurrentEdit();
                    _editBlocks.RemoveAt(idx);
                    if      (_selectedBlock == idx)   _selectedBlock = -1;
                    else if (_selectedBlock > idx)    _selectedBlock--;
                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndHorizontal();
                    while (_blockRects.Count > _editBlocks.Count) _blockRects.RemoveAt(_blockRects.Count - 1);
                    Repaint();
                    return;
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.EndHorizontal();
                Rect rowRect = GUILayoutUtility.GetLastRect();

                if (e.type == EventType.Repaint)
                {
                    _blockRects[idx] = rowRect;
                    if (isSelected)
                        EditorGUI.DrawRect(rowRect, new Color(0.30f, 0.50f, 0.90f, 0.15f));
                }

                // Mouse handling
                if (e.type == EventType.MouseDown && e.button == 0 && rowRect.Contains(e.mousePosition))
                {
                    _dragBlockFrom   = idx;
                    _dragBlockStart  = e.mousePosition;
                    _isDraggingBlock = false;
                    SelectEditBlock(idx);
                    e.Use();
                    Repaint();
                }
                else if (e.type == EventType.MouseDrag && _dragBlockFrom == idx && !_isDraggingBlock)
                {
                    if (Vector2.Distance(e.mousePosition, _dragBlockStart) >= DragThreshold)
                    {
                        _isDraggingBlock = true;
                        UpdateDragDropTarget(e.mousePosition);
                        Repaint();
                        e.Use();
                    }
                }
                else if (e.type == EventType.MouseDrag && _isDraggingBlock)
                {
                    UpdateDragDropTarget(e.mousePosition);
                    Repaint();
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && _isDraggingBlock)
                {
                    FinishBlockDrag();
                    e.Use();
                }

                // Inline editor for selected block
                if (isSelected)
                    DrawBlockEditor(idx);
            }

            // Drop indicator at end of list
            if (_isDraggingBlock && _dragBlockDrop >= _editBlocks.Count)
                DrawDropIndicator();

            // Safety: cancel drag on MouseUp anywhere in the list area
            if (e.type == EventType.MouseUp)
            {
                if (_isDraggingBlock) FinishBlockDrag();
                else                 _dragBlockFrom = -1;
            }
        }

        private GUIStyle _badgeLabelStyle;

        private void DrawBlockEditor(int idx)
        {
            if (_editBlocks == null || idx < 0 || idx >= _editBlocks.Count) return;
            var b = _editBlocks[idx];

            EditorGUI.indentLevel++;

            bool isLocked = b.Type == BlockType.UserContent || b.Type == BlockType.Frontmatter;

            // Type selector
            if (isLocked)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.EnumPopup("Type", _editBlockType);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                BlockType prevType = _editBlockType;
                _editBlockType = (BlockType)EditorGUILayout.EnumPopup("Type", _editBlockType);
                if (_editBlockType != prevType && _editBlockType != BlockType.TaskItem)
                    _editTaskItems = null;
            }

            // Level slider
            if (_editBlockType == BlockType.Heading)
                _editBlockLevel = EditorGUILayout.IntSlider("Niveau (H1–H6)", _editBlockLevel, 1, 6);
            else if (_editBlockType == BlockType.ListItem || _editBlockType == BlockType.OrderedListItem)
                _editBlockLevel = EditorGUILayout.IntSlider("Indentation", _editBlockLevel, 0, 4);

            // Meta field
            if (_editBlockType == BlockType.CodeBlock)
                _editBlockMeta = EditorGUILayout.TextField("Langage", _editBlockMeta);
            else if (_editBlockType == BlockType.Blockquote)
                _editBlockMeta = EditorGUILayout.TextField("Type callout", _editBlockMeta);
            else if (_editBlockType == BlockType.UserContent)
                _editBlockMeta = EditorGUILayout.TextField("Clé zone", _editBlockMeta);
            else if (_editBlockType == BlockType.Image)
                _editBlockMeta = EditorGUILayout.TextField("Texte alternatif", _editBlockMeta);

            // Image — nom du fichier uniquement (copié automatiquement dans ImagesFolder)
            if (_editBlockType == BlockType.Image)
            {
                EditorGUILayout.BeginHorizontal();
                _editBlockText = EditorGUILayout.TextField("Nom de fichier", _editBlockText);
                if (GUILayout.Button("...", GUILayout.Width(28)))
                {
                    string picked = EditorUtility.OpenFilePanel(
                        "Sélectionner une image", string.Empty, "png,jpg,jpeg,gif,webp,svg");
                    if (!string.IsNullOrEmpty(picked))
                    {
                        string fileName = Path.GetFileName(picked);
                        string imagesDir = Path.GetFullPath(
                            Path.Combine(_settings.OutputFolder, _settings.ImagesFolder));
                        string dest = Path.Combine(imagesDir, fileName);

                        if (!File.Exists(dest))
                        {
                            Directory.CreateDirectory(imagesDir);
                            File.Copy(picked, dest);
                            AssetDatabase.Refresh();
                        }

                        _editBlockText = fileName;
                    }
                }
                EditorGUILayout.EndHorizontal();

                // Chemin résolu affiché en lecture seule pour confirmation
                string resolvedDir = Path.GetFullPath(
                    Path.Combine(_settings.OutputFolder, _settings.ImagesFolder));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField("Dossier images", resolvedDir);
                EditorGUI.EndDisabledGroup();
            }
            // TaskItem — liste de tâches avec sous-tâches
            else if (_editBlockType == BlockType.TaskItem)
            {
                if (_editTaskItems == null)
                    _editTaskItems = new List<(bool IsChecked, int Level, string Text)>();

                int toDelete = -1;
                for (int ti = 0; ti < _editTaskItems.Count; ti++)
                {
                    var (isChecked, level, text) = _editTaskItems[ti];
                    bool   newChecked = isChecked;
                    int    newLevel   = level;
                    string newText    = text;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(4f + level * 12f);
                    newChecked = EditorGUILayout.Toggle(isChecked, GUILayout.Width(16));
                    newText    = EditorGUILayout.TextField(text, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("←", EditorStyles.miniButton, GUILayout.Width(18)) && newLevel > 0) newLevel--;
                    GUILayout.Label(newLevel.ToString(), EditorStyles.miniLabel, GUILayout.Width(14));
                    if (GUILayout.Button("→", EditorStyles.miniButton, GUILayout.Width(18)) && newLevel < 4) newLevel++;
                    if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(18))) toDelete = ti;
                    EditorGUILayout.EndHorizontal();

                    _editTaskItems[ti] = (newChecked, newLevel, newText);
                }

                if (toDelete >= 0)
                    _editTaskItems.RemoveAt(toDelete);

                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ Tâche", GUILayout.Width(72)))
                    _editTaskItems.Add((false, 0, string.Empty));
                int suggestedLevel = _editTaskItems.Count > 0
                    ? Mathf.Min(_editTaskItems[_editTaskItems.Count - 1].Level + 1, 4) : 1;
                if (GUILayout.Button("+ Sous-tâche", GUILayout.Width(92)))
                    _editTaskItems.Add((false, suggestedLevel, string.Empty));
                EditorGUILayout.EndHorizontal();
            }
            // Tous les autres blocs avec du texte
            else if (_editBlockType != BlockType.HorizontalRule && _editBlockType != BlockType.Frontmatter)
            {
                EditorGUILayout.LabelField("Contenu :");
                _editBlockText = EditorGUILayout.TextArea(
                    _editBlockText,
                    GUILayout.MinHeight(60),
                    GUILayout.ExpandWidth(true));
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        private List<(bool IsChecked, int Level, string Text)> ParseTaskItems(string rawText)
        {
            var result = new List<(bool, int, string)>();
            if (string.IsNullOrEmpty(rawText)) return result;
            foreach (string line in rawText.Split('\n'))
            {
                var m = _reTaskLineW.Match(line.TrimEnd('\r'));
                if (!m.Success) continue;
                result.Add((
                    m.Groups[2].Value.ToLowerInvariant() == "x",
                    m.Groups[1].Length / 2,
                    m.Groups[3].Value));
            }
            return result;
        }

        private static string SerializeTaskItems(List<(bool IsChecked, int Level, string Text)> items)
        {
            if (items == null || items.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            foreach (var (isChecked, level, text) in items)
                sb.AppendLine(new string(' ', level * 2) + "- [" + (isChecked ? "x" : " ") + "] " + text);
            return sb.ToString().TrimEnd();
        }

        private void DrawDropIndicator()
        {
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(2));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(r, new Color(0.30f, 0.55f, 1.00f, 0.90f));
        }

        private void UpdateDragDropTarget(Vector2 mousePos)
        {
            int drop = _editBlocks != null ? _editBlocks.Count : 0;
            for (int i = 0; i < _blockRects.Count && _editBlocks != null && i < _editBlocks.Count; i++)
            {
                Rect r = _blockRects[i];
                if (mousePos.y < r.y + r.height * 0.5f) { drop = i; break; }
            }
            if (drop != _dragBlockDrop) { _dragBlockDrop = drop; Repaint(); }
        }

        private void FinishBlockDrag()
        {
            int from = _dragBlockFrom;
            int to   = _dragBlockDrop;

            if (_editBlocks != null && from >= 0 && from < _editBlocks.Count && to != from && to != from + 1)
            {
                MarkdownBlock moved = _editBlocks[from];
                _editBlocks.RemoveAt(from);
                int adj = Mathf.Clamp(to > from ? to - 1 : to, 0, _editBlocks.Count);
                _editBlocks.Insert(adj, moved);
                _selectedBlock = adj;
            }

            _isDraggingBlock = false;
            _dragBlockFrom   = -1;
            _dragBlockDrop   = -1;
            Repaint();
        }

        // ── Splitter drag ─────────────────────────────────────────────────────

        private void HandleSplitterDrag()
        {
            Event e = Event.current;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (Mathf.Abs(e.mousePosition.x - _splitPosition) <= SplitterWidth + 1f)
                    {
                        _isDraggingSplit = true;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag when _isDraggingSplit:
                    _splitPosition = Mathf.Clamp(
                        e.mousePosition.x,
                        PanelMinWidth,
                        position.width - PanelMinWidth - SplitterWidth);
                    Repaint();
                    e.Use();
                    break;

                case EventType.MouseUp:
                    _isDraggingSplit = false;
                    break;
            }
        }

        // ── Script ping ───────────────────────────────────────────────────────

        private void PingCorrespondingScript()
        {
            string className = GetClassNameFromFrontmatter();
            if (string.IsNullOrEmpty(className)) return;

            string[] guids = AssetDatabase.FindAssets($"t:MonoScript {className}");
            foreach (string guid in guids)
            {
                string path   = AssetDatabase.GUIDToAssetPath(guid);
                var    script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.name == className)
                {
                    Selection.activeObject = script;
                    EditorGUIUtility.PingObject(script);
                    return;
                }
            }
        }

        private string GetClassNameFromFrontmatter()
        {
            if (_parsedBlocks == null) return null;
            var fm = _parsedBlocks.FirstOrDefault(b => b.Type == BlockType.Frontmatter);
            if (fm == null) return null;

            foreach (string line in fm.RawText.Split('\n'))
            {
                string trimmed = line.TrimEnd('\r').Trim();
                if (!trimmed.StartsWith("class:", StringComparison.Ordinal)) continue;

                string value   = trimmed.Substring("class:".Length).Trim();
                // Strip generic params: "Singleton<T>" → "Singleton"
                int generic    = value.IndexOf('<');
                string basePart = generic >= 0 ? value.Substring(0, generic) : value;
                // Strip namespace: "MyNamespace.Singleton" → "Singleton"
                int lastDot    = basePart.LastIndexOf('.');
                return lastDot >= 0 ? basePart.Substring(lastDot + 1) : basePart;
            }
            return null;
        }

        // ── Status bar ────────────────────────────────────────────────────────

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                string message = _hasGenerated
                    ? $"Scripts found: {_lastScan.Count}  |  {_lastReport}"
                    : "No generation run yet.";

                EditorGUILayout.LabelField(message, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        private void RunGenerate()
        {
            if (_editMode)
                CancelEdit();
            _lastScan     = ObsidocScanner.Scan();
            _lastReport   = ObsidocGenerator.Generate(_lastScan, _settings);
            _hasGenerated = true;
            RefreshView();
        }

        private void RefreshView()
        {
            // Force Unity to detect any external filesystem changes (edits from Obsidian, etc.)
            AssetDatabase.Refresh();
            // Clear foldout cache so the tree is rebuilt from the updated disk state
            _foldouts.Clear();
            BuildTagCache();
            // Reload selected file content in case it changed on disk
            if (!string.IsNullOrEmpty(_selectedFile) && File.Exists(_selectedFile))
            {
                _fileContent  = File.ReadAllText(_selectedFile);
                _parsedBlocks = ObsidocMarkdownParser.Parse(_fileContent);
            }
            Repaint();
        }

        private void SelectFile(string path)
        {
            _editMode         = false;
            _editBlocks       = null;
            _selectedBlock    = -1;
            _selectedFile     = path;
            _fileContent      = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            _parsedBlocks     = ObsidocMarkdownParser.Parse(_fileContent);
            _renderer.BaseDir = Path.GetFullPath(Path.Combine(_settings.OutputFolder, _settings.ImagesFolder));
            _rightScroll      = Vector2.zero;
            Repaint();
        }

        // ── Context menus ─────────────────────────────────────────────────────────

        private void ShowFileContextMenu(string filePath)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Rename"), false, () => PromptRename(filePath, isFolder: false));
            menu.AddItem(new GUIContent("Delete"), false, () => ConfirmDelete(filePath, isFolder: false));
            menu.ShowAsContext();
        }

        private void ShowFolderContextMenu(string folderPath)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Create File"),   false, () => PromptCreate(folderPath, isFolder: false));
            menu.AddItem(new GUIContent("Create Folder"), false, () => PromptCreate(folderPath, isFolder: true));

            // Colour tint submenu
            menu.AddSeparator("");
            bool hasColor = _settings.TryGetFolderColor(folderPath, out Color current);
            if (_settings.ColorPalette != null)
            {
                foreach (var entry in _settings.ColorPalette)
                {
                    string capturedName = entry.Name;
                    Color  capturedTint = entry.Tint;
                    bool   isActive     = hasColor && ColorsApproxEqual(current, capturedTint);
                    menu.AddItem(new GUIContent($"Color Folder/{capturedName}"), isActive,
                        () => ApplyFolderColor(folderPath, capturedTint));
                }
            }
            menu.AddItem(new GUIContent("Color Folder/None"), !hasColor,
                () => ApplyFolderColor(folderPath, Color.clear));

            // Rename and Delete are not offered for the root output folder
            string rootPath = Path.GetFullPath(_settings.OutputFolder);
            if (!string.Equals(Path.GetFullPath(folderPath), rootPath, StringComparison.OrdinalIgnoreCase))
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Rename"), false, () => PromptRename(folderPath, isFolder: true));
                menu.AddItem(new GUIContent("Delete"), false, () => ConfirmDelete(folderPath, isFolder: true));
            }

            menu.ShowAsContext();
        }

        // ── Create ────────────────────────────────────────────────────────────────

        private void PromptCreate(string parentDir, bool isFolder)
        {
            string title = isFolder ? "Create Folder" : "Create File";
            string label = isFolder ? "Folder name:" : "File name:";
            ObsidocInputDialog.Show(title, label, string.Empty, name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return;

                if (isFolder)
                {
                    string newPath = Path.Combine(parentDir, name);
                    if (!Directory.Exists(newPath))
                        Directory.CreateDirectory(newPath);
                }
                else
                {
                    if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        name += ".md";
                    string newPath = Path.Combine(parentDir, name);
                    if (!File.Exists(newPath))
                        File.WriteAllText(newPath, string.Empty, System.Text.Encoding.UTF8);
                    SelectFile(newPath);
                }

                AssetDatabase.Refresh();
                RefreshView();
            });
        }

        // ── Rename ────────────────────────────────────────────────────────────────

        private void PromptRename(string path, bool isFolder)
        {
            string current = isFolder
                ? Path.GetFileName(path)
                : Path.GetFileNameWithoutExtension(path);

            ObsidocInputDialog.Show(
                isFolder ? "Rename Folder" : "Rename File",
                "New name:", current,
                newName =>
                {
                    if (string.IsNullOrWhiteSpace(newName) || newName == current) return;

                    if (isFolder)
                    {
                        string newPath = Path.Combine(Path.GetDirectoryName(path), newName);
                        if (!Directory.Exists(newPath))
                        {
                            Directory.Move(path, newPath);
                            MoveMeta(path, newPath);
                        }
                    }
                    else
                    {
                        if (!newName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                            newName += ".md";
                        string newPath = Path.Combine(Path.GetDirectoryName(path), newName);
                        if (!File.Exists(newPath))
                        {
                            File.Move(path, newPath);
                            MoveMeta(path, newPath);
                            if (_selectedFile != null &&
                                string.Equals(Path.GetFullPath(_selectedFile), Path.GetFullPath(path),
                                    StringComparison.OrdinalIgnoreCase))
                                SelectFile(newPath);
                        }
                    }

                    AssetDatabase.Refresh();
                    RefreshView();
                });
        }

        // ── Delete ────────────────────────────────────────────────────────────────

        private void ConfirmDelete(string path, bool isFolder)
        {
            string itemName = Path.GetFileName(path);
            string message  = isFolder
                ? $"Delete folder \"{itemName}\" and all its contents?"
                : $"Delete file \"{itemName}\"?";

            if (!EditorUtility.DisplayDialog(
                    isFolder ? "Delete Folder" : "Delete File",
                    message, "Delete", "Cancel"))
                return;

            if (isFolder)
            {
                // Clear selection if the selected file lives inside the deleted folder
                if (_selectedFile != null &&
                    Path.GetFullPath(_selectedFile).StartsWith(
                        Path.GetFullPath(path) + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _selectedFile = null;
                    _fileContent  = null;
                    _parsedBlocks = null;
                }
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                DeleteMeta(path);
            }
            else
            {
                if (_selectedFile != null &&
                    string.Equals(Path.GetFullPath(_selectedFile), Path.GetFullPath(path),
                        StringComparison.OrdinalIgnoreCase))
                {
                    _selectedFile = null;
                    _fileContent  = null;
                    _parsedBlocks = null;
                }
                if (File.Exists(path))
                    File.Delete(path);
                DeleteMeta(path);
            }

            AssetDatabase.Refresh();
            RefreshView();
        }

        // ── Drag & drop move ──────────────────────────────────────────────────────

        private void MoveItem(string sourcePath, string targetDir)
        {
            string name     = Path.GetFileName(sourcePath);
            string destPath = Path.Combine(targetDir, name);

            // Already in the target folder — no-op
            if (string.Equals(
                    Path.GetFullPath(Path.GetDirectoryName(sourcePath)),
                    Path.GetFullPath(targetDir),
                    StringComparison.OrdinalIgnoreCase))
                return;

            if (File.Exists(sourcePath))
            {
                if (File.Exists(destPath))
                {
                    EditorUtility.DisplayDialog("Move Failed",
                        $"A file named \"{name}\" already exists in the target folder.", "OK");
                    return;
                }
                File.Move(sourcePath, destPath);
                MoveMeta(sourcePath, destPath);

                if (_selectedFile != null &&
                    string.Equals(Path.GetFullPath(_selectedFile), Path.GetFullPath(sourcePath),
                        StringComparison.OrdinalIgnoreCase))
                    SelectFile(destPath);
            }
            else if (Directory.Exists(sourcePath))
            {
                if (Directory.Exists(destPath))
                {
                    EditorUtility.DisplayDialog("Move Failed",
                        $"A folder named \"{name}\" already exists in the target folder.", "OK");
                    return;
                }
                Directory.Move(sourcePath, destPath);
                MoveMeta(sourcePath, destPath);

                // Update selection if the selected file was inside the moved folder
                if (_selectedFile != null)
                {
                    string srcFull = Path.GetFullPath(sourcePath) + Path.DirectorySeparatorChar;
                    string selFull = Path.GetFullPath(_selectedFile);
                    if (selFull.StartsWith(srcFull, StringComparison.OrdinalIgnoreCase))
                        SelectFile(Path.Combine(destPath, selFull.Substring(srcFull.Length)));
                }
            }

            AssetDatabase.Refresh();
            RefreshView();
        }

        // ── Folder colour helpers ─────────────────────────────────────────────────

        private void ApplyFolderColor(string folderPath, Color tint)
        {
            if (tint == Color.clear)
                _settings.ClearFolderColor(folderPath);
            else
                _settings.SetFolderColor(folderPath, tint);

            UnityEditor.EditorUtility.SetDirty(_settings);
            UnityEditor.AssetDatabase.SaveAssets();
            Repaint();
        }

        private static bool ColorsApproxEqual(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f
            && Mathf.Abs(a.b - b.b) < 0.01f;

        private static void DeleteMeta(string path)
        {
            string meta = path + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
        }

        private static void MoveMeta(string sourcePath, string destPath)
        {
            string srcMeta = sourcePath + ".meta";
            string dstMeta = destPath   + ".meta";
            if (File.Exists(srcMeta) && !File.Exists(dstMeta))
                File.Move(srcMeta, dstMeta);
        }

        private static bool IsAncestorOrSelf(string sourcePath, string targetPath)
        {
            string src = Path.GetFullPath(sourcePath);
            string tgt = Path.GetFullPath(targetPath);
            return string.Equals(src, tgt, StringComparison.OrdinalIgnoreCase)
                || tgt.StartsWith(src + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
}
