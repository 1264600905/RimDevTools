using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace LogSyncMod.AutoTest
{
    public sealed class AutoTestSemanticValueGraph
    {
        private readonly AutoTestSemanticGraphOptions options;

        public AutoTestSemanticValueGraph(AutoTestSemanticGraphOptions options = null)
        {
            this.options = options ?? new AutoTestSemanticGraphOptions();
        }

        public AutoTestSemanticNode Describe(object value, string name = "root")
        {
            return DescribeValue(value, name ?? "root", "$", 0, new HashSet<object>(ReferenceComparer.Instance));
        }

        private AutoTestSemanticNode DescribeValue(object value, string name, string path, int depth, HashSet<object> visited)
        {
            Type type = value == null ? null : value.GetType();
            AutoTestSemanticNode node = new AutoTestSemanticNode
            {
                Name = name,
                Path = path,
                TypeName = type == null ? "null" : type.FullName
            };
            if (value == null)
            {
                node.ValueKind = "null";
                node.Value = "null";
                return node;
            }

            if (TryDescribeScalar(value, type, out string scalar))
            {
                node.ValueKind = "scalar";
                node.Value = Truncate(scalar, options.MaxStringLength, out bool truncated);
                node.Truncated = truncated;
                return node;
            }

            if (IsDangerous(type))
            {
                node.ValueKind = "unsupported";
                node.Value = "Skipped dangerous or oversized runtime type.";
                node.Truncated = true;
                return node;
            }

            if (depth >= Math.Max(0, options.MaxDepth))
            {
                node.ValueKind = "object";
                node.Value = "Maximum depth reached.";
                node.Truncated = true;
                return node;
            }

            if (!type.IsValueType)
            {
                if (!visited.Add(value))
                {
                    node.ValueKind = "reference";
                    node.Value = "Repeated reference";
                    node.IsReference = true;
                    return node;
                }
            }

            try
            {
                if (value is IDictionary dictionary)
                {
                    DescribeDictionary(node, dictionary, depth, visited);
                }
                else if (value is IEnumerable enumerable && !(value is string))
                {
                    DescribeEnumerable(node, enumerable, depth, visited);
                }
                else
                {
                    DescribeObject(node, value, type, depth, visited);
                }
            }
            finally
            {
                if (!type.IsValueType) visited.Remove(value);
            }
            return node;
        }

        private void DescribeDictionary(AutoTestSemanticNode node, IDictionary dictionary, int depth, HashSet<object> visited)
        {
            node.ValueKind = "dictionary";
            int count = 0;
            foreach (DictionaryEntry item in dictionary)
            {
                if (count >= Limit(options.MaxCollectionItems, options.MaxChildrenPerNode)) { node.Truncated = true; break; }
                string childName = Convert.ToString(item.Key, CultureInfo.InvariantCulture) ?? "null";
                node.Children.Add(DescribeValue(item.Value, childName, node.Path + "[" + childName + "]", depth + 1, visited));
                count++;
            }
            node.Value = "Count=" + count + (node.Truncated ? "+" : string.Empty);
        }

        private void DescribeEnumerable(AutoTestSemanticNode node, IEnumerable enumerable, int depth, HashSet<object> visited)
        {
            node.ValueKind = "array";
            int index = 0;
            foreach (object item in enumerable)
            {
                if (index >= Limit(options.MaxCollectionItems, options.MaxChildrenPerNode)) { node.Truncated = true; break; }
                node.Children.Add(DescribeValue(item, index.ToString(CultureInfo.InvariantCulture), node.Path + "[" + index + "]", depth + 1, visited));
                index++;
            }
            node.Value = "Count=" + index + (node.Truncated ? "+" : string.Empty);
        }

        private void DescribeObject(AutoTestSemanticNode node, object value, Type type, int depth, HashSet<object> visited)
        {
            node.ValueKind = "object";
            List<FieldInfo> fields = GetFields(type).Where(field => !field.IsStatic && !field.IsDefined(typeof(CompilerGeneratedAttribute), true)).OrderBy(field => field.Name).ToList();
            List<PropertyInfo> properties = GetProperties(type).Where(property => property.GetIndexParameters().Length == 0 && property.GetGetMethod(true) != null && !property.GetGetMethod(true).IsStatic).OrderBy(property => property.Name).ToList();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            int limit = Math.Max(1, options.MaxChildrenPerNode);

            foreach (FieldInfo field in fields)
            {
                if (node.Children.Count >= limit) { node.Truncated = true; break; }
                names.Add(field.Name);
                AddMember(node, field.Name, depth, visited, delegate { return field.GetValue(value); });
            }
            foreach (PropertyInfo property in properties)
            {
                if (node.Children.Count >= limit) { node.Truncated = true; break; }
                if (names.Contains(property.Name)) continue;
                AddMember(node, property.Name, depth, visited, delegate { return property.GetValue(value, null); });
            }
            node.Value = "Members=" + node.Children.Count + (node.Truncated ? "+" : string.Empty);
        }

        private void AddMember(AutoTestSemanticNode parent, string name, int depth, HashSet<object> visited, Func<object> getter)
        {
            string path = parent.Path + "." + name;
            try
            {
                parent.Children.Add(DescribeValue(getter(), name, path, depth + 1, visited));
            }
            catch (Exception ex)
            {
                parent.Children.Add(new AutoTestSemanticNode
                {
                    Name = name,
                    Path = path,
                    ValueKind = "error",
                    TypeName = ex.GetType().FullName,
                    Value = Truncate(ex.GetBaseException().Message, options.MaxStringLength, out bool truncated),
                    Truncated = truncated
                });
            }
        }

        private static IEnumerable<FieldInfo> GetFields(Type type)
        {
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                foreach (FieldInfo field in current.GetTypeInfo().DeclaredFields) yield return field;
        }

        private static IEnumerable<PropertyInfo> GetProperties(Type type)
        {
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                foreach (PropertyInfo property in current.GetTypeInfo().DeclaredProperties) yield return property;
        }

        private static bool TryDescribeScalar(object value, Type type, out string result)
        {
            if (type.IsEnum || type.IsPrimitive || value is decimal || value is DateTime || value is DateTimeOffset || value is TimeSpan || value is Guid || value is string || value is char)
            {
                result = Convert.ToString(value, CultureInfo.InvariantCulture);
                return true;
            }
            result = null;
            return false;
        }

        private static bool IsDangerous(Type type)
        {
            if (typeof(Delegate).IsAssignableFrom(type) || typeof(Stream).IsAssignableFrom(type) ||
                typeof(System.Threading.Thread).IsAssignableFrom(type)) return true;
            string name = type.FullName ?? type.Name;
            string enginePrefix = "Unity" + "Engine.";
            return name.StartsWith(enginePrefix + "Texture", StringComparison.Ordinal) ||
                name.StartsWith(enginePrefix + "Material", StringComparison.Ordinal) ||
                name.StartsWith(enginePrefix + "Mesh", StringComparison.Ordinal) ||
                name.StartsWith("System.Reflection.", StringComparison.Ordinal);
        }

        private static int Limit(int first, int second) { return Math.Max(1, Math.Min(Math.Max(1, first), Math.Max(1, second))); }
        private static string Truncate(string value, int maxLength, out bool truncated)
        {
            value = value ?? string.Empty;
            maxLength = Math.Max(1, maxLength);
            truncated = value.Length > maxLength;
            return truncated ? value.Substring(0, maxLength) : value;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object obj) { return RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
