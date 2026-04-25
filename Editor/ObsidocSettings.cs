using System.Collections.Generic;
using UnityEngine;

namespace Obsi.Doc.Editor
{
    /// <summary>
    /// Persistent settings for the Obsidoc tool, stored as a project asset.
    /// Default path: Assets/ObsidocSettings.asset
    /// </summary>
    public class ObsidocSettings : ScriptableObject
    {
        private const string AssetPath = "Assets/ObsidocSettings.asset";

        /// <summary>Absolute or relative path of the root folder where documentation will be generated.</summary>
        [Tooltip("Root output folder path (e.g. C:/Docs or Assets/Documentation)")]
        public string OutputFolder = "Assets/Documentation";

        /// <summary>Sub-folder inside the output folder where generated .md files will be placed.</summary>
        [Tooltip("Sub-folder inside the output folder for the generated .md files")]
        public string SubFolder = "Scripts";

        /// <summary>Sub-folder name (inside OutputFolder) where orphaned .md files are moved.</summary>
        [Tooltip("Sub-folder name inside the output folder used to store archived .md files")]
        public string ArchivedFolder = "_Archived";

        /// <summary>Sub-folder name (inside OutputFolder) where images referenced in .md files are stored.</summary>
        [Tooltip("Sub-folder inside the output folder used to store images (files are copied here automatically)")]
        public string ImagesFolder = "_assets";

        // ── Method exclusions ────────────────────────────────────────────────

        /// <summary>
        /// Unity lifecycle and event methods always excluded from documentation,
        /// regardless of user settings.
        /// </summary>
        public static readonly string[] UnityDefaultExcludedMethods =
        {
            // Core lifecycle
            "Awake", "Start", "OnEnable", "OnDisable", "OnDestroy", "Reset",

            // Update loops
            "Update", "FixedUpdate", "LateUpdate",

            // Physics 3D
            "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit",
            "OnTriggerEnter",   "OnTriggerStay",   "OnTriggerExit",

            // Physics 2D
            "OnCollisionEnter2D", "OnCollisionStay2D", "OnCollisionExit2D",
            "OnTriggerEnter2D",   "OnTriggerStay2D",   "OnTriggerExit2D",

            // Mouse events
            "OnMouseDown", "OnMouseUp", "OnMouseUpAsButton",
            "OnMouseEnter", "OnMouseExit", "OnMouseOver", "OnMouseDrag",

            // Rendering
            "OnBecameVisible", "OnBecameInvisible",
            "OnWillRenderObject", "OnRenderObject",
            "OnPreRender", "OnPostRender", "OnRenderImage",

            // Animation
            "OnAnimatorMove", "OnAnimatorIK",

            // Editor callbacks
            "OnValidate", "OnDrawGizmos", "OnDrawGizmosSelected",

            // Application
            "OnApplicationPause", "OnApplicationFocus", "OnApplicationQuit",
        };

        /// <summary>
        /// Additional method names to exclude from the generated documentation.
        /// Merged with <see cref="UnityDefaultExcludedMethods"/> at generation time.
        /// </summary>
        [Tooltip("Extra method names to exclude from documentation (in addition to the built-in Unity methods).")]
        public string[] ExcludedMethods = new string[0];

        /// <summary>
        /// Returns a set containing all excluded method names (Unity defaults + user-defined).
        /// </summary>
        public HashSet<string> GetExcludedMethodSet()
        {
            var set = new HashSet<string>(UnityDefaultExcludedMethods, System.StringComparer.Ordinal);

            if (ExcludedMethods != null)
                foreach (string m in ExcludedMethods)
                    if (!string.IsNullOrWhiteSpace(m))
                        set.Add(m.Trim());

            return set;
        }

        // ── Style ────────────────────────────────────────────────────────────

        [Tooltip("Vue par défaut du panneau de prévisualisation (rendu ou brut)")]
        public bool DefaultRenderedView = true;

        [Range(12, 32), Tooltip("Taille de police du titre H1")]
        public int H1FontSize = 20;

        [Range(10, 24), Tooltip("Taille de police du titre H2")]
        public int H2FontSize = 15;

        [Range(9, 18), Tooltip("Taille de police du titre H3")]
        public int H3FontSize = 12;

        // ── Section accent colors ─────────────────────────────────────────────

        public Color AccentSummary = new Color(0.30f, 0.60f, 1.00f);
        public Color AccentFields  = new Color(0.25f, 0.85f, 0.68f);
        public Color AccentMethods = new Color(0.72f, 0.42f, 1.00f);
        public Color AccentValues  = new Color(1.00f, 0.72f, 0.25f);

        // ── Access modifier badge colors ──────────────────────────────────────

        public Color ModifierPublic    = new Color(0.16f, 0.56f, 0.28f);
        public Color ModifierPrivate   = new Color(0.68f, 0.24f, 0.16f);
        public Color ModifierProtected = new Color(0.68f, 0.50f, 0.08f);

        // ── Folder colour entries (path → color) ──────────────────────────────

        /// <summary>Per-folder background colour tints, keyed by absolute path.</summary>
        public List<FolderColorEntry> FolderColors = new List<FolderColorEntry>();

        public bool TryGetFolderColor(string path, out Color color)
        {
            foreach (var e in FolderColors)
                if (string.Equals(e.Path, path, System.StringComparison.OrdinalIgnoreCase))
                { color = e.Color; return true; }
            color = default;
            return false;
        }

        public void SetFolderColor(string path, Color color)
        {
            foreach (var e in FolderColors)
                if (string.Equals(e.Path, path, System.StringComparison.OrdinalIgnoreCase))
                { e.Color = color; return; }
            FolderColors.Add(new FolderColorEntry { Path = path, Color = color });
        }

        public void ClearFolderColor(string path)
        {
            FolderColors.RemoveAll(e =>
                string.Equals(e.Path, path, System.StringComparison.OrdinalIgnoreCase));
        }

        // ── Configurable color palette ────────────────────────────────────────

        /// <summary>Named colors available in the "Color Folder" context menu.</summary>
        public List<ColorPaletteEntry> ColorPalette = new List<ColorPaletteEntry>();

        // ── Load / Create ────────────────────────────────────────────────────

        /// <summary>
        /// Loads the settings asset from the project, or creates a new one if it does not exist.
        /// </summary>
        public static ObsidocSettings GetOrCreate()
        {
#if UNITY_EDITOR
            var settings = UnityEditor.AssetDatabase.LoadAssetAtPath<ObsidocSettings>(AssetPath);

            if (settings == null)
            {
                settings = CreateInstance<ObsidocSettings>();
                UnityEditor.AssetDatabase.CreateAsset(settings, AssetPath);
                UnityEditor.AssetDatabase.SaveAssets();
            }

            if (settings.ColorPalette == null || settings.ColorPalette.Count == 0)
            {
                settings.ColorPalette = BuildDefaultPalette();
                UnityEditor.EditorUtility.SetDirty(settings);
                UnityEditor.AssetDatabase.SaveAssets();
            }

            return settings;
#else
            return null;
#endif
        }

        internal static List<ColorPaletteEntry> BuildDefaultPalette() => new List<ColorPaletteEntry>
        {
            new ColorPaletteEntry { Name = "Red",    Tint = new Color(0.85f, 0.22f, 0.18f, 0.32f) },
            new ColorPaletteEntry { Name = "Orange", Tint = new Color(0.90f, 0.52f, 0.10f, 0.32f) },
            new ColorPaletteEntry { Name = "Yellow", Tint = new Color(0.88f, 0.80f, 0.08f, 0.32f) },
            new ColorPaletteEntry { Name = "Green",  Tint = new Color(0.18f, 0.72f, 0.30f, 0.32f) },
            new ColorPaletteEntry { Name = "Teal",   Tint = new Color(0.10f, 0.68f, 0.70f, 0.32f) },
            new ColorPaletteEntry { Name = "Blue",   Tint = new Color(0.18f, 0.44f, 0.92f, 0.32f) },
            new ColorPaletteEntry { Name = "Purple", Tint = new Color(0.60f, 0.20f, 0.90f, 0.32f) },
            new ColorPaletteEntry { Name = "Pink",   Tint = new Color(0.90f, 0.30f, 0.62f, 0.32f) },
        };
    }

    /// <summary>Associates an absolute folder path with a background tint colour.</summary>
    [System.Serializable]
    public class FolderColorEntry
    {
        public string Path;
        public Color  Color;
    }

    /// <summary>Named color entry in the configurable folder color palette.</summary>
    [System.Serializable]
    public class ColorPaletteEntry
    {
        public string Name;
        public Color  Tint;
    }
}
