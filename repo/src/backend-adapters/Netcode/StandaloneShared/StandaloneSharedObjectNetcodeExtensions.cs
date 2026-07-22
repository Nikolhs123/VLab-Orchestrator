using System.Collections.Generic;
using UnityEngine;

namespace Thesis.Orchestration.Backends.Netcode.StandaloneShared
{
    public static class StandaloneSharedObjectNetcodeExtensions
    {
        public static string GetHierarchyPathFromRoot(this Transform current)
        {
            if (current == null)
                return string.Empty;

            Stack<string> parts = new Stack<string>();
            Transform walker = current;

            while (walker != null)
            {
                parts.Push(walker.name);
                walker = walker.parent;
            }

            return string.Join("/", parts.ToArray());
        }
    }
}
