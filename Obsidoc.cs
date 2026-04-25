using System;

namespace Obsi.Doc
{
    /// <summary>
    /// Marks a type (class, static class, abstract class, sealed class, struct, enum)
    /// for automatic Markdown documentation generation.
    /// Usage: [Obsidoc("Short description", category: "UI", tags: new[]{"input","player"})]
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface,
        AllowMultiple = false, Inherited = false)]
    public class ObsidocAttribute : Attribute
    {
        /// <summary>Short description of the script, written as the Summary section of the generated .md file.</summary>
        public string Summary { get; }

        /// <summary>Classification category (e.g. "UI", "Gameplay", "Audio"). Empty by default.</summary>
        public string Category { get; }

        /// <summary>Free-form keywords used for filtering and cross-linking.</summary>
        public string[] Tags { get; }

        public ObsidocAttribute(string summary, string category = "", params string[] tags)
        {
            Summary  = summary  ?? string.Empty;
            Category = category ?? string.Empty;
            Tags     = tags     ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Adds a description to a field, displayed in the Description column of the Fields table.
    /// Usage: [ObsidocField("Vitesse de déplacement du joueur.")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class ObsidocFieldAttribute : Attribute
    {
        /// <summary>Description of the field, shown in the generated documentation.</summary>
        public string Summary { get; }

        public ObsidocFieldAttribute(string summary)
        {
            Summary = summary ?? string.Empty;
        }
    }

    /// <summary>
    /// Adds a description to a method, displayed below its code block in the generated documentation.
    /// Usage: [ObsidocMethod("Moves the character in the given direction.")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class ObsidocMethodAttribute : Attribute
    {
        /// <summary>Description of the method, shown in the generated documentation.</summary>
        public string Summary { get; }

        public ObsidocMethodAttribute(string summary)
        {
            Summary = summary ?? string.Empty;
        }
    }

    /// <summary>
    /// Adds a description to a property, displayed in the Description column of the Properties table.
    /// Usage: [ObsidocProperty("Vitesse de déplacement en unités/seconde.")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class ObsidocPropertyAttribute : Attribute
    {
        /// <summary>Description of the property, shown in the generated documentation.</summary>
        public string Summary { get; }

        public ObsidocPropertyAttribute(string summary)
        {
            Summary = summary ?? string.Empty;
        }
    }

    /// <summary>
    /// Adds a description to an enum member, displayed in the Description column of the Values table.
    /// Usage: [ObsidocValue("Description de la valeur.")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public class ObsidocValueAttribute : Attribute
    {
        /// <summary>Description of the enum member, shown in the generated documentation.</summary>
        public string Summary { get; }

        public ObsidocValueAttribute(string summary)
        {
            Summary = summary ?? string.Empty;
        }
    }

    /// <summary>
    /// Opts the type into full private-member documentation.
    /// When present, private fields and private methods are included in the generated .md file.
    /// Applicable to classes and structs.
    /// Usage: [ObsidocIncludePrivate]
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface,
        AllowMultiple = false, Inherited = false)]
    public class ObsidocIncludePrivateAttribute : Attribute { }
}
