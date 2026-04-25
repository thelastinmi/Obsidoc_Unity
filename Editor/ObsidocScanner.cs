using System;
using System.Collections.Generic;
using System.Reflection;

namespace Obsi.Doc.Editor
{
    /// <summary>
    /// Iterates over all loaded assemblies and collects types marked with [ObsidocAttribute].
    /// </summary>
    public static class ObsidocScanner
    {
        /// <summary>Holds the result of a scan operation.</summary>
        public struct ScanResult
        {
            public List<Type> Types;
            public int Count => Types?.Count ?? 0;
        }

        /// <summary>
        /// Runs the scan and returns every type decorated with [ObsidocAttribute].
        /// Handles partial assemblies that throw <see cref="ReflectionTypeLoadException"/>.
        /// </summary>
        public static ScanResult Scan()
        {
            var found = new List<Type>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    // Some types may still be valid even if the assembly failed to load fully
                    types = e.Types;
                }

                if (types == null) continue;

                foreach (Type type in types)
                {
                    if (type == null) continue;
                    if (type.IsDefined(typeof(ObsidocAttribute), inherit: false))
                        found.Add(type);
                }
            }

            return new ScanResult { Types = found };
        }
    }
}
