using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Obsi.Doc.Editor
{
    /// <summary>
    /// Generates, updates, and archives .md documentation files
    /// based on the result of an <see cref="ObsidocScanner"/> scan.
    /// </summary>
    public static class ObsidocGenerator
    {
        // ── Public entry point ────────────────────────────────────────────────

        /// <summary>
        /// Runs a full generation pass:
        /// <list type="bullet">
        ///   <item>Creates a .md file for every type that does not have one yet.</item>
        ///   <item>Updates the frontmatter of existing .md files that still have a matching type.</item>
        ///   <item>Moves orphaned .md files (no matching type) to the archive folder.</item>
        /// </list>
        /// </summary>
        public static GenerationReport Generate(ObsidocScanner.ScanResult scan, ObsidocSettings settings)
        {
            string outputRoot  = Path.GetFullPath(Path.Combine(settings.OutputFolder, settings.SubFolder));
            string archiveRoot = Path.GetFullPath(Path.Combine(settings.OutputFolder, settings.ArchivedFolder));

            Directory.CreateDirectory(outputRoot);
            Directory.CreateDirectory(archiveRoot);

            // Build a lookup: class name → Type
            var typeMap = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (Type t in scan.Types)
                typeMap[t.Name] = t;

            var report = new GenerationReport();

            // Compute the excluded method set once for the entire pass
            HashSet<string> excludedMethods = settings.GetExcludedMethodSet();

            // Pre-scan: index every .md file anywhere under OutputFolder by class name.
            // Searching the broader OutputFolder (not just outputRoot/SubFolder) ensures files
            // manually moved outside outputRoot are detected and updated in place instead of duplicated.
            var existingFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string searchRoot = Path.GetFullPath(settings.OutputFolder);
            if (Directory.Exists(searchRoot))
            {
                foreach (string f in Directory.GetFiles(searchRoot, "*.md", SearchOption.AllDirectories))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (!existingFiles.ContainsKey(n))
                        existingFiles[n] = f;
                }
            }

            // ── Generate / update ─────────────────────────────────────────────
            foreach (Type type in scan.Types)
            {
                var attr = type.GetCustomAttribute<ObsidocAttribute>();
                if (attr == null) continue;

                if (existingFiles.TryGetValue(type.Name, out string existingPath))
                {
                    // File found somewhere in outputRoot — update it in its current location
                    UpdateFrontmatter(existingPath, type, attr, excludedMethods);
                    report.Updated++;
                }
                else
                {
                    // No existing file — create at the canonical location
                    string folder = string.IsNullOrEmpty(attr.Category)
                        ? outputRoot
                        : Path.Combine(outputRoot, attr.Category);
                    Directory.CreateDirectory(folder);
                    string filePath = Path.Combine(folder, $"{type.Name}.md");
                    File.WriteAllText(filePath, BuildMarkdown(type, attr, excludedMethods), Encoding.UTF8);
                    report.Created++;
                }
            }

            // ── Archive orphaned files ─────────────────────────────────────────
            if (Directory.Exists(outputRoot))
            {
                foreach (string mdFile in Directory.GetFiles(outputRoot, "*.md", SearchOption.AllDirectories))
                {
                    string className = Path.GetFileNameWithoutExtension(mdFile);
                    if (!typeMap.ContainsKey(className))
                    {
                        string dest = ResolveArchivePath(archiveRoot, Path.GetFileName(mdFile));
                        File.Move(mdFile, dest);
                        report.Archived++;
                    }
                }
            }

            AssetDatabase.Refresh();
            return report;
        }

        // ── Markdown builders ─────────────────────────────────────────────────

        /// <summary>Builds a new .md file with YAML frontmatter, Summary, Fields, and Methods sections.</summary>
        private static string BuildMarkdown(Type type, ObsidocAttribute attr, HashSet<string> excludedMethods)
        {
            var  sb             = new StringBuilder();
            bool includePrivate = type.IsDefined(typeof(ObsidocIncludePrivateAttribute), false);

            sb.AppendLine("---");
            sb.AppendLine($"class: {type.FullName}");
            sb.AppendLine($"kind: {GetTypeKind(type)}");
            sb.AppendLine($"namespace: {type.Namespace}");
            sb.AppendLine($"category: {attr.Category}");
            sb.AppendLine($"tags: [{string.Join(", ", attr.Tags)}]");
            sb.AppendLine($"generated: {DateTime.Now:yyyy-MM-dd}");
            sb.AppendLine($"updated: {DateTime.Now:yyyy-MM-dd}");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine(attr.Summary);

            if (type.IsEnum)
            {
                sb.AppendLine();
                sb.AppendLine("## Values");
                sb.AppendLine();
                sb.Append(BuildValuesSection(type));
            }
            else if (type.IsInterface)
            {
                // Interfaces expose properties and methods (no fields, no private members)
                sb.AppendLine();
                sb.AppendLine("## Properties");
                sb.AppendLine();
                sb.AppendLine(BuildPropertiesSection(type, includePrivate: false));
                sb.AppendLine();
                sb.AppendLine("## Methods");
                sb.AppendLine();
                sb.Append(BuildMethodsSection(type, includePrivate: false, excludedMethods));
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("## Fields");
                sb.AppendLine();
                sb.AppendLine(BuildFieldsSection(type, includePrivate));
                sb.AppendLine();
                sb.AppendLine("## Properties");
                sb.AppendLine();
                sb.AppendLine(BuildPropertiesSection(type, includePrivate));
                sb.AppendLine();
                sb.AppendLine("## Methods");
                sb.AppendLine();
                sb.Append(BuildMethodsSection(type, includePrivate, excludedMethods));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Merges the current attribute properties into the existing file's frontmatter:
        /// - Script-sourced properties (class, namespace, category, tags) are always synced.
        /// - <c>generated</c> is preserved from the existing file.
        /// - <c>updated</c> is set to today.
        /// - Any user-added properties not in the script schema are kept at the end.
        /// - <c>summary</c> is no longer a YAML key; it lives as a "## Summary" body section.
        ///   Migration: if the existing file had a "summary" YAML key and no "## Summary" section,
        ///   the value is injected at the top of the body.
        /// </summary>
        private static void UpdateFrontmatter(string filePath, Type type, ObsidocAttribute attr, HashSet<string> excludedMethods)
        {
            string content      = File.ReadAllText(filePath, Encoding.UTF8);
            var    existingList = ParseFrontmatter(content, out string body);

            // Build a fast lookup of what the file currently contains
            var existingLookup = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in existingList)
                existingLookup[k] = v;

            // Migration: if the old file stored summary in YAML and the body has no ## Summary section, inject it
            if (existingLookup.TryGetValue("summary", out string oldSummary)
                && !body.Contains("## Summary"))
            {
                body = "\n\n## Summary\n\n" + oldSummary + body;
            }

            // Properties sourced from the [Obsidoc] attribute — always authoritative
            var scriptProps = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["class"]     = type.FullName  ?? string.Empty,
                ["kind"]      = GetTypeKind(type),
                ["namespace"] = type.Namespace ?? string.Empty,
                ["category"]  = attr.Category,
                ["tags"]      = $"[{string.Join(", ", attr.Tags)}]",
            };

            // Fixed output order for script-owned keys
            // "summary" is intentionally absent — it is now a body section, not a YAML key
            string[] scriptOrder = { "class", "kind", "namespace", "category", "tags", "generated", "updated" };
            var      scriptSet   = new HashSet<string>(scriptOrder, StringComparer.Ordinal);
            scriptSet.Add("summary"); // exclude legacy "summary" from user-added keys

            var result = new List<(string Key, string Value)>();

            // 1. Script-owned keys in canonical order
            foreach (string key in scriptOrder)
            {
                if (scriptProps.TryGetValue(key, out string val))
                {
                    result.Add((key, val));
                }
                else if (key == "generated")
                {
                    // Keep the original generation date
                    string generated = existingLookup.TryGetValue("generated", out string g)
                        ? g
                        : DateTime.Now.ToString("yyyy-MM-dd");
                    result.Add(("generated", generated));
                }
                else if (key == "updated")
                {
                    result.Add(("updated", DateTime.Now.ToString("yyyy-MM-dd")));
                }
            }

            // 2. User-added keys — appended in their original order
            foreach (var (key, val) in existingList)
            {
                if (!scriptSet.Contains(key))
                    result.Add((key, val));
            }

            // Regenerate body sections from reflection, preserving all user edits
            bool includePrivate = type.IsDefined(typeof(ObsidocIncludePrivateAttribute), false);
            Dictionary<string, MethodUserData> userMethods = ExtractMethodUserData(body);
            if (type.IsEnum)
            {
                body = ReplaceOrAppendSection(body, "## Values",  BuildValuesSection(type));
            }
            else if (type.IsInterface)
            {
                body = ReplaceOrAppendSection(body, "## Properties", BuildPropertiesSection(type, includePrivate: false));
                body = ReplaceOrAppendSection(body, "## Methods",    BuildMethodsSection(type, includePrivate: false, excludedMethods, userMethods));
            }
            else
            {
                body = ReplaceOrAppendSection(body, "## Fields",      BuildFieldsSection(type, includePrivate));
                body = ReplaceOrAppendSection(body, "## Properties",  BuildPropertiesSection(type, includePrivate));
                body = ReplaceOrAppendSection(body, "## Methods",     BuildMethodsSection(type, includePrivate, excludedMethods, userMethods));
            }

            // Rebuild and write the file
            var sb = new StringBuilder();
            sb.AppendLine("---");
            foreach (var (key, val) in result)
                sb.AppendLine($"{key}: {val}");
            sb.Append("---");
            sb.Append(body);

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        }

        // ── Frontmatter parser ────────────────────────────────────────────────

        /// <summary>
        /// Parses the YAML frontmatter of a .md file into an ordered list of key/value pairs.
        /// Returns the content after the closing <c>---</c> in <paramref name="body"/>.
        /// </summary>
        private static List<(string Key, string Value)> ParseFrontmatter(string content, out string body)
        {
            var    props = new List<(string Key, string Value)>();
            body = content;

            string[] lines = content.Split('\n');

            // File must open with ---
            if (lines.Length < 2 || lines[0].TrimEnd('\r') != "---")
                return props;

            int closeIndex = -1;

            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');

                if (line == "---")
                {
                    closeIndex = i;
                    break;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                string key = line.Substring(0, colon).Trim();
                string val = line.Substring(colon + 1).Trim();

                if (!string.IsNullOrEmpty(key))
                    props.Add((key, val));
            }

            if (closeIndex < 0)
            {
                // Malformed frontmatter — treat the whole file as body, no props
                props.Clear();
                return props;
            }

            // Everything after the closing --- (preserve newline)
            body = closeIndex + 1 < lines.Length
                ? "\n" + string.Join("\n", lines, closeIndex + 1, lines.Length - closeIndex - 1)
                : string.Empty;

            return props;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the content (without the "## Fields" header) of the Fields section
        /// by reflecting the instance fields of <paramref name="type"/>.
        /// Private fields are only included when <paramref name="includePrivate"/> is true
        /// (controlled by <see cref="ObsidocIncludePrivateAttribute"/> on the class).
        /// Compiler-generated backing fields (names containing '&lt;') are excluded.
        /// </summary>
        private static string BuildFieldsSection(Type type, bool includePrivate)
        {
            FieldInfo[] allFields = type.GetFields(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public   | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            FieldInfo[] publicFields  = allFields.Where(f =>  f.IsPublic && !f.Name.Contains('<')).ToArray();
            FieldInfo[] privateFields = includePrivate
                ? allFields.Where(f => !f.IsPublic && !f.Name.Contains('<')).ToArray()
                : Array.Empty<FieldInfo>();

            var sb = new StringBuilder();

            if (publicFields.Length > 0)
            {
                sb.AppendLine("### Public");
                sb.AppendLine();
                AppendFieldTable(sb, publicFields);
            }

            if (privateFields.Length > 0)
            {
                if (publicFields.Length > 0) sb.AppendLine();
                sb.AppendLine("### Private");
                sb.AppendLine();
                AppendFieldTable(sb, privateFields);
            }

            if (publicFields.Length == 0 && privateFields.Length == 0)
                sb.Append("*No fields.*");

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Writes a Markdown table for a set of fields.
        /// Always includes a Description column; cells are empty when the field has no <see cref="ObsidocFieldAttribute"/>.
        /// </summary>
        private static void AppendFieldTable(StringBuilder sb, FieldInfo[] fields)
        {
            sb.AppendLine("| Type | Name | Description |");
            sb.AppendLine("|------|------|-------------|");
            foreach (FieldInfo f in fields)
            {
                string desc = f.GetCustomAttribute<ObsidocFieldAttribute>()?.Summary ?? string.Empty;
                sb.AppendLine($"| `{FriendlyTypeName(f.FieldType)}` | `{f.Name}` | {desc} |");
            }
        }

        /// <summary>
        /// Builds the content (without the "## Methods" header) of the Methods section
        /// by reflecting the declared methods of <paramref name="type"/>.
        /// Excluded: compiler-generated methods, special-name accessors (getters/setters/events),
        /// and any name present in <paramref name="excludedMethods"/>.
        /// Private methods are only included when <paramref name="includePrivate"/> is true.
        /// </summary>
        private static string BuildMethodsSection(Type type, bool includePrivate, HashSet<string> excludedMethods, Dictionary<string, MethodUserData> userMethods = null)
        {
            MethodInfo[] allMethods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public   | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            MethodInfo[] visible = allMethods
                .Where(m => !m.IsSpecialName               // exclude property/event accessors
                         && !m.Name.Contains('<')           // exclude compiler-generated
                         && !excludedMethods.Contains(m.Name))
                .ToArray();

            MethodInfo[] publicMethods  = visible.Where(m =>  m.IsPublic).ToArray();
            MethodInfo[] privateMethods = includePrivate
                ? visible.Where(m => !m.IsPublic).ToArray()
                : Array.Empty<MethodInfo>();

            var sb       = new StringBuilder();
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);

            if (publicMethods.Length > 0)
            {
                sb.AppendLine("### Public");
                sb.AppendLine();
                AppendMethodBlocks(sb, publicMethods, userMethods, usedKeys);
            }

            if (privateMethods.Length > 0)
            {
                if (publicMethods.Length > 0) sb.AppendLine();
                sb.AppendLine("### Private");
                sb.AppendLine();
                AppendMethodBlocks(sb, privateMethods, userMethods, usedKeys);
            }

            if (publicMethods.Length == 0 && privateMethods.Length == 0)
                sb.Append("*No methods.*");

            if (userMethods != null)
            {
                var orphans = userMethods
                    .Where(kvp => !usedKeys.Contains(kvp.Key)
                        && (kvp.Value.Description != null || !string.IsNullOrEmpty(kvp.Value.TrailingMarkdown)))
                    .ToList();
                if (orphans.Count > 0)
                {
                    sb.AppendLine("\n\n### Orphelins");
                    foreach (var kvp in orphans)
                    {
                        sb.AppendLine($"\n*`{kvp.Key}`*");
                        if (kvp.Value.Description != null)
                            sb.AppendLine($"\n{kvp.Value.Description}");
                        if (!string.IsNullOrEmpty(kvp.Value.TrailingMarkdown))
                            sb.AppendLine($"\n{kvp.Value.TrailingMarkdown}");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Writes each method as an Obsidian-style code block followed by a description line.
        /// Methods are separated by a horizontal rule.
        /// </summary>
        private static void AppendMethodBlocks(StringBuilder sb, MethodInfo[] methods,
            Dictionary<string, MethodUserData> userMethods, HashSet<string> usedKeys)
        {
            for (int i = 0; i < methods.Length; i++)
            {
                sb.AppendLine("```csharp");
                sb.AppendLine(BuildMethodSignature(methods[i]));
                sb.AppendLine("```");
                sb.AppendLine();

                string zoneKey = GetMethodZoneKey(methods[i]);
                MethodUserData userData = default;
                bool hasUser = userMethods != null && userMethods.TryGetValue(zoneKey, out userData);
                if (hasUser) usedKeys.Add(zoneKey);

                string attrDesc   = methods[i].GetCustomAttribute<ObsidocMethodAttribute>()?.Summary;
                string description = (hasUser && userData.Description != null)
                    ? userData.Description
                    : (string.IsNullOrEmpty(attrDesc) ? "*No description.*" : attrDesc);
                sb.Append(description);

                if (hasUser && !string.IsNullOrEmpty(userData.TrailingMarkdown))
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.Append(userData.TrailingMarkdown);
                }

                if (i < methods.Length - 1)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
            }
        }

        /// <summary>Builds a full C# method signature string (access modifier, static, return type, name, parameters).</summary>
        private static string BuildMethodSignature(MethodInfo m)
        {
            string access     = GetAccessModifier(m);
            string staticMod  = m.IsStatic ? " static" : string.Empty;
            string returnType = FriendlyTypeName(m.ReturnType);
            string parameters = string.Join(", ", m.GetParameters().Select(p =>
            {
                string prefix;
                if (p.GetCustomAttribute<ParamArrayAttribute>() != null)
                    prefix = "params ";
                else if (p.IsOut)
                    prefix = "out ";
                else if (p.ParameterType.IsByRef)
                {
                    bool isIn = p.GetCustomAttributesData()
                        .Any(a => a.AttributeType.Name == "IsReadOnlyAttribute");
                    prefix = isIn ? "in " : "ref ";
                }
                else
                    prefix = string.Empty;
                Type pt = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                return $"{prefix}{FriendlyTypeName(pt)} {p.Name}";
            }));
            return $"{access}{staticMod} {returnType} {m.Name}({parameters})";
        }

        /// <summary>Returns the C# access modifier keyword for a method.</summary>
        private static string GetAccessModifier(MethodInfo m)
        {
            if (m.IsPublic)            return "public";
            if (m.IsPrivate)           return "private";
            if (m.IsFamily)            return "protected";
            if (m.IsAssembly)          return "internal";
            if (m.IsFamilyOrAssembly)  return "protected internal";
            if (m.IsFamilyAndAssembly) return "private protected";
            return "private";
        }

        /// <summary>
        /// Returns a human-readable kind label for a type:
        /// "enum", "struct", "static class", "abstract class", "sealed class", or "class".
        /// </summary>
        private static string GetTypeKind(Type type)
        {
            if (type.IsInterface)                   return "interface";
            if (type.IsEnum)                        return "enum";
            if (type.IsValueType)                   return "struct";
            if (type.IsAbstract && type.IsSealed)   return "static class";
            if (type.IsAbstract)                    return "abstract class";
            if (type.IsSealed)                      return "sealed class";
            return "class";
        }

        /// <summary>
        /// Builds the content (without the "## Properties" header) of the Properties section.
        /// Includes public properties, and optionally private ones when <paramref name="includePrivate"/> is true.
        /// Indexers and compiler-generated properties are always excluded.
        /// </summary>
        private static string BuildPropertiesSection(Type type, bool includePrivate)
        {
            PropertyInfo[] allProps = type.GetProperties(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public   | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(p => p.GetIndexParameters().Length == 0 && !p.Name.Contains('<'))
                .ToArray();

            PropertyInfo[] publicProps = allProps
                .Where(p => p.GetMethod?.IsPublic == true || p.SetMethod?.IsPublic == true)
                .ToArray();

            PropertyInfo[] privateProps = includePrivate
                ? allProps
                    .Where(p => p.GetMethod?.IsPublic != true && p.SetMethod?.IsPublic != true)
                    .ToArray()
                : Array.Empty<PropertyInfo>();

            var sb = new StringBuilder();

            if (publicProps.Length > 0)
            {
                sb.AppendLine("### Public");
                sb.AppendLine();
                AppendPropertyTable(sb, publicProps);
            }

            if (privateProps.Length > 0)
            {
                if (publicProps.Length > 0) sb.AppendLine();
                sb.AppendLine("### Private");
                sb.AppendLine();
                AppendPropertyTable(sb, privateProps);
            }

            if (publicProps.Length == 0 && privateProps.Length == 0)
                sb.Append("*No properties.*");

            return sb.ToString().TrimEnd();
        }

        private static void AppendPropertyTable(StringBuilder sb, PropertyInfo[] props)
        {
            sb.AppendLine("| Type | Name | Accesseurs | Description |");
            sb.AppendLine("|------|------|------------|-------------|");
            foreach (PropertyInfo p in props)
            {
                string typeName  = FriendlyTypeName(p.PropertyType);
                string accessors = BuildAccessorString(p);
                string desc      = p.GetCustomAttribute<ObsidocPropertyAttribute>()?.Summary ?? string.Empty;
                sb.AppendLine($"| `{typeName}` | `{p.Name}` | `{accessors}` | {desc} |");
            }
        }

        private static string BuildAccessorString(PropertyInfo prop)
        {
            var parts = new List<string>();
            if (prop.GetMethod != null) parts.Add("get");
            if (prop.SetMethod != null)
            {
                bool isInit = false;
                try
                {
                    isInit = prop.SetMethod.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
                }
                catch { }
                parts.Add(isInit ? "init" : "set");
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Builds the content (without the "## Values" header) of the Values section for an enum type.
        /// Lists each member with its underlying integer value.
        /// </summary>
        private static string BuildValuesSection(Type enumType)
        {
            string[] names  = Enum.GetNames(enumType);
            Array    values = Enum.GetValues(enumType);

            if (names.Length == 0)
                return "*No values.*";

            var sb = new StringBuilder();
            sb.AppendLine("| Name | Value | Description |");
            sb.AppendLine("|------|-------|-------------|");
            for (int i = 0; i < names.Length; i++)
            {
                FieldInfo member = enumType.GetField(names[i]);
                string desc = member?.GetCustomAttribute<ObsidocValueAttribute>()?.Summary ?? string.Empty;
                sb.AppendLine($"| `{names[i]}` | `{Convert.ToInt64(values.GetValue(i))}` | {desc} |");
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>Returns a C# alias for common CLR type names (e.g. "System.Int32" → "int").</summary>
        private static string FriendlyTypeName(Type type)
        {
            if (type.IsArray)
            {
                string elem  = FriendlyTypeName(type.GetElementType());
                string commas = type.GetArrayRank() > 1 ? new string(',', type.GetArrayRank() - 1) : string.Empty;
                return $"{elem}[{commas}]";
            }
            switch (type.FullName)
            {
                case "System.Void":    return "void";
                case "System.Boolean": return "bool";
                case "System.Char":    return "char";
                case "System.Byte":    return "byte";
                case "System.SByte":   return "sbyte";
                case "System.Int16":   return "short";
                case "System.UInt16":  return "ushort";
                case "System.Int32":   return "int";
                case "System.UInt32":  return "uint";
                case "System.Int64":   return "long";
                case "System.UInt64":  return "ulong";
                case "System.Single":  return "float";
                case "System.Double":  return "double";
                case "System.Decimal": return "decimal";
                case "System.String":  return "string";
                case "System.Object":  return "object";
                default:
                    if (type.IsGenericType)
                    {
                        string baseName = type.Name;
                        int tick = baseName.IndexOf('`');
                        if (tick >= 0) baseName = baseName.Substring(0, tick);
                        Type[] args = type.GetGenericArguments();
                        string[] argNames = new string[args.Length];
                        for (int i = 0; i < args.Length; i++)
                            argNames[i] = FriendlyTypeName(args[i]);
                        return baseName + "<" + string.Join(", ", argNames) + ">";
                    }
                    return type.Name;
            }
        }

        /// <summary>
        /// Finds the section starting with <paramref name="header"/> in <paramref name="body"/>
        /// and replaces its content with <paramref name="newContent"/>.
        /// If the section is absent it is appended at the end of the body.
        /// The next "## " heading after the replaced section is preserved.
        /// </summary>
        private static string ReplaceOrAppendSection(string body, string header, string newContent)
        {
            int idx = body.IndexOf(header, StringComparison.Ordinal);

            if (idx < 0)
            {
                // Section absent — append
                return body.TrimEnd() + "\n\n" + header + "\n\n" + newContent;
            }

            // Find the start of the next "## " heading after this section
            int searchFrom  = idx + header.Length;
            int nextSection = body.IndexOf("\n## ", searchFrom, StringComparison.Ordinal);

            string before = body.Substring(0, idx).TrimEnd();
            string after  = nextSection >= 0 ? body.Substring(nextSection) : string.Empty;

            return before + "\n\n" + header + "\n\n" + newContent + after;
        }

        private static readonly Regex ReMethodSig = new Regex(
            @"([a-zA-Z_]\w*)\s*\(([^)]*)\)\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Converts a raw C# method signature string (as written in a csharp code block)
        /// into a zone key matching <see cref="GetMethodZoneKey"/>.
        /// e.g. "public void Foo(int x, string y)" → "Foo(int,string)"
        /// </summary>
        private static string SignatureToZoneKey(string signature)
        {
            if (string.IsNullOrEmpty(signature)) return null;
            var m = ReMethodSig.Match(signature);
            if (!m.Success) return null;

            string methodName = m.Groups[1].Value;
            string paramsRaw  = m.Groups[2].Value.Trim();

            if (string.IsNullOrEmpty(paramsRaw)) return $"{methodName}()";

            var typeTokens = paramsRaw.Split(',').Select(p =>
            {
                string[] tokens = p.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int start = 0;
                while (start < tokens.Length - 1 &&
                       (tokens[start] == "ref" || tokens[start] == "out" ||
                        tokens[start] == "in"  || tokens[start] == "params"))
                    start++;
                return tokens.Length > start ? tokens[start] : string.Empty;
            }).Where(t => !string.IsNullOrEmpty(t));

            return $"{methodName}({string.Join(",", typeTokens)})";
        }

        /// <summary>
        /// Scans the ## Methods section of <paramref name="body"/> and returns a dictionary
        /// mapping zone key → captured user data (description + trailing blocks).
        /// For each csharp code block found, captures the description paragraph immediately
        /// after it and any additional blocks before the next method separator (---) or heading.
        /// </summary>
        private static Dictionary<string, MethodUserData> ExtractMethodUserData(string body)
        {
            var result = new Dictionary<string, MethodUserData>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(body)) return result;

            // Isolate the ## Methods section
            int sectionStart = body.IndexOf("\n## Methods", StringComparison.Ordinal);
            if (sectionStart < 0) sectionStart = body.IndexOf("## Methods", StringComparison.Ordinal);
            if (sectionStart < 0) return result;

            int sectionEnd = body.IndexOf("\n## ", sectionStart + 4, StringComparison.Ordinal);
            string section = sectionEnd >= 0
                ? body.Substring(sectionStart, sectionEnd - sectionStart)
                : body.Substring(sectionStart);

            string[] lines = section.Split('\n');

            string currentKey     = null;
            bool   inCodeBlock    = false;
            bool   collectingDesc = false;
            var    descLines      = new List<string>();
            var    trailingLines  = new List<string>();
            bool   pastDesc       = false;

            void FlushCurrent()
            {
                if (currentKey == null) return;
                string desc = descLines.Count > 0 ? string.Join("\n", descLines).Trim() : null;
                if (desc == "*No description.*") desc = null;
                string trailing = trailingLines.Count > 0 ? string.Join("\n", trailingLines).Trim() : null;
                if ((desc != null || !string.IsNullOrEmpty(trailing)) && !result.ContainsKey(currentKey))
                    result[currentKey] = new MethodUserData { Description = desc, TrailingMarkdown = trailing };
                currentKey     = null;
                descLines.Clear();
                trailingLines.Clear();
                collectingDesc = false;
                pastDesc       = false;
            }

            string pendingSignature = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');

                if (line.TrimStart().StartsWith("```csharp", StringComparison.Ordinal))
                {
                    FlushCurrent();
                    inCodeBlock       = true;
                    pendingSignature  = null;
                    collectingDesc    = false;
                    continue;
                }

                if (inCodeBlock)
                {
                    if (line.TrimStart() == "```")
                    {
                        inCodeBlock    = false;
                        currentKey     = pendingSignature != null ? SignatureToZoneKey(pendingSignature) : null;
                        collectingDesc = true;
                        pastDesc       = false;
                    }
                    else
                    {
                        pendingSignature = line;
                    }
                    continue;
                }

                // Separator between methods — flush and prepare for next
                if (line.TrimStart() == "---")
                {
                    FlushCurrent();
                    continue;
                }

                // Sub-headings (### Public / ### Private / ### Orphelins)
                if (line.StartsWith("### ", StringComparison.Ordinal))
                {
                    FlushCurrent();
                    continue;
                }

                if (currentKey == null) continue;

                if (collectingDesc && !pastDesc)
                {
                    if (string.IsNullOrEmpty(line)) continue; // skip blank lines before desc
                    descLines.Add(line);
                    // Paragraph ends at blank line
                    if (i + 1 < lines.Length && string.IsNullOrWhiteSpace(lines[i + 1].TrimEnd('\r')))
                        pastDesc = true;
                }
                else
                {
                    pastDesc = true;
                    trailingLines.Add(line);
                }
            }

            FlushCurrent();
            return result;
        }

        /// <summary>Returns the user-content zone key for a method: "MethodName(ParamType1,ParamType2)".</summary>
        private static string GetMethodZoneKey(MethodInfo m)
        {
            string paramList = string.Join(",", m.GetParameters().Select(p =>
            {
                Type pt = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                return FriendlyTypeName(pt);
            }));
            return $"{m.Name}({paramList})";
        }

        /// <summary>
        /// Returns a unique path in the archive folder, appending a timestamp if a file
        /// with the same name already exists.
        /// </summary>
        private static string ResolveArchivePath(string archiveRoot, string fileName)
        {
            string dest = Path.Combine(archiveRoot, fileName);
            if (!File.Exists(dest)) return dest;

            string name      = Path.GetFileNameWithoutExtension(fileName);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            return Path.Combine(archiveRoot, $"{name}_{timestamp}.md");
        }
    }

    // ── Report ────────────────────────────────────────────────────────────────

    /// <summary>Summary of what happened during a generation pass.</summary>
    public struct GenerationReport
    {
        public int Created;
        public int Updated;
        public int Archived;

        public override string ToString() =>
            $"Created: {Created}  |  Updated: {Updated}  |  Archived: {Archived}";
    }

    /// <summary>
    /// Captures per-method user edits extracted from an existing .md file
    /// so they can be re-injected after the next Generate call.
    /// </summary>
    internal struct MethodUserData
    {
        /// <summary>Description paragraph written by the user. null means "use attribute/default".</summary>
        public string Description;
        /// <summary>Any extra blocks the user added below the description. null/empty means none.</summary>
        public string TrailingMarkdown;
    }
}
