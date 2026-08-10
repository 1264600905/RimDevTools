using System;
using System.Collections.Generic;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestGeneDependencyResolver
    {
        public static bool TryResolve<T>(
            T target,
            Func<T, T> prerequisiteSelector,
            Func<T, string> identitySelector,
            Func<T, bool> isResolvable,
            out List<T> orderedDependencies,
            out string errorCode,
            out string errorMessage)
            where T : class
        {
            orderedDependencies = new List<T>();
            errorCode = null;
            errorMessage = null;
            if (target == null || prerequisiteSelector == null || identitySelector == null)
            {
                errorCode = "gene_dependency_missing";
                errorMessage = "Target Gene or dependency resolver was missing.";
                return false;
            }

            HashSet<string> visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return Visit(target, false, prerequisiteSelector, identitySelector, isResolvable, visiting, visited,
                orderedDependencies, out errorCode, out errorMessage);
        }

        private static bool Visit<T>(
            T gene,
            bool include,
            Func<T, T> prerequisiteSelector,
            Func<T, string> identitySelector,
            Func<T, bool> isResolvable,
            ISet<string> visiting,
            ISet<string> visited,
            ICollection<T> orderedDependencies,
            out string errorCode,
            out string errorMessage)
            where T : class
        {
            errorCode = null;
            errorMessage = null;
            string identity = identitySelector(gene);
            if (string.IsNullOrWhiteSpace(identity) || (include && isResolvable != null && !isResolvable(gene)))
            {
                errorCode = "gene_dependency_missing";
                errorMessage = "A Gene prerequisite could not be resolved.";
                return false;
            }

            if (visiting.Contains(identity))
            {
                errorCode = "gene_dependency_cycle";
                errorMessage = "Gene prerequisite cycle detected at " + identity + ".";
                return false;
            }

            if (visited.Contains(identity))
            {
                return true;
            }

            visiting.Add(identity);
            T prerequisite = prerequisiteSelector(gene);
            if (prerequisite != null && !Visit(prerequisite, true, prerequisiteSelector, identitySelector, isResolvable,
                visiting, visited, orderedDependencies, out errorCode, out errorMessage))
            {
                return false;
            }

            visiting.Remove(identity);
            visited.Add(identity);
            if (include)
            {
                orderedDependencies.Add(gene);
            }

            return true;
        }
    }
}
