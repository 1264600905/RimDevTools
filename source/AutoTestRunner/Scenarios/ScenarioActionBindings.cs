#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LogSyncMod.AutoTest.Scenarios
{
    // Bindings are frozen into each Case with its input hash, never read from disk during execution.
    public static class ScenarioActionBindings
    {
        public static readonly IReadOnlyDictionary<string, Type> Scalars = new Dictionary<string, Type> {
            { "int", typeof(int) }, { "float", typeof(float) }, { "double", typeof(double) },
            { "bool", typeof(bool) }, { "string", typeof(string) }
        };
        public static XElement Find(XElement step)
        {
            var owner = step.Ancestors("Case").FirstOrDefault();
            var id = ScenarioLoader.A(step, "actionId");
            return (owner?.Element("ActionBindings")?.Elements("ActionBinding") ?? Enumerable.Empty<XElement>()).FirstOrDefault(b => ScenarioLoader.A(b, "id") == id)
                ?? (step.Ancestors("TestSuite").FirstOrDefault()?.Element("ActionBindings")?.Elements("ActionBinding") ?? Enumerable.Empty<XElement>()).FirstOrDefault(b => ScenarioLoader.A(b, "id") == id);
        }

        public static void Freeze(XElement c, XElement suiteBindings, string path)
        {
            var definitions = Definitions(suiteBindings, path);
            foreach (var entry in Definitions(c.Element("ActionBindings"), path)) definitions[entry.Key] = entry.Value;
            var used = c.Element("Steps")?.Elements("DebugAction").Select(s => ScenarioLoader.A(s, "actionId")).Distinct().Where(id => id != null && definitions.ContainsKey(id)).ToList() ?? new List<string>();
            c.Element("ActionBindings")?.Remove();
            if (used.Count == 0) return;
            c.Add(new XElement("ActionBindings", used.Select(id => new XElement(definitions[id]))));
            var requirements = c.Element("Requirements");
            if (requirements == null) { requirements = new XElement("Requirements"); c.Add(requirements); }
            foreach (var package in used.Select(id => ScenarioLoader.A(definitions[id], "packageId")).Distinct(StringComparer.OrdinalIgnoreCase))
                if (!requirements.Elements().Any(r => string.Equals(ScenarioLoader.A(r, "packageId"), package, StringComparison.OrdinalIgnoreCase))) requirements.Add(new XElement("Mod", new XAttribute("packageId", package)));
        }

        private static Dictionary<string, XElement> Definitions(XElement container, string path)
        {
            var result = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var b in container?.Elements("ActionBinding") ?? Enumerable.Empty<XElement>())
            {
                ValidateDefinition(b, path);
                var id = ScenarioLoader.A(b, "id");
                if (result.ContainsKey(id)) ScenarioLoader.Fail(b, path, "duplicate action binding " + id);
                result.Add(id, b);
            }
            return result;
        }

        private static string Need(XElement e, string name, string path)
        {
            var value = ScenarioLoader.A(e, name);
            if (string.IsNullOrWhiteSpace(value)) ScenarioLoader.Fail(e, path, "binding requires " + name);
            return value;
        }
        private static void Identifier(XElement e, string name, string path, bool qualified = false)
        {
            if (!Regex.IsMatch(Need(e, name, path), qualified ? @"^[A-Za-z_][A-Za-z0-9_.+]*$" : @"^[A-Za-z_][A-Za-z0-9_]*$")) ScenarioLoader.Fail(e, path, "invalid binding " + name);
        }
        public static void ValidateDefinition(XElement b, string path)
        {
            var id = Need(b, "id", path); Need(b, "packageId", path);
            if (ScenarioDebugActions.Parameters.ContainsKey(id)) ScenarioLoader.Fail(b, path, "binding cannot replace built-in action " + id);
            Identifier(b, "type", path, true); Identifier(b, "method", path);
            var parameters = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var p in b.Element("Parameters")?.Elements("Parameter") ?? Enumerable.Empty<XElement>())
            {
                Identifier(p, "name", path); Identifier(p, "type", path, true);
                var name = ScenarioLoader.A(p, "name");
                if (parameters.ContainsKey(name)) ScenarioLoader.Fail(p, path, "duplicate binding parameter " + name);
                parameters.Add(name, p);
                if (p.Attribute("min") != null || p.Attribute("max") != null)
                {
                    if (!new[] { "int", "float", "double" }.Contains(ScenarioLoader.A(p, "type"))) ScenarioLoader.Fail(p, path, "bounds require a numeric binding parameter");
                    if (ScenarioLoader.D(p, "min", double.MinValue) > ScenarioLoader.D(p, "max", double.MaxValue)) ScenarioLoader.Fail(p, path, "reversed binding parameter bounds");
                }
            }
            if (b.Element("Arguments") == null) ScenarioLoader.Fail(b, path, "binding requires Arguments (empty for a parameterless method)");
            var used = new HashSet<string>();
            foreach (var a in b.Element("Arguments").Elements("Argument"))
            {
                Identifier(a, "type", path, true);
                if (new[] { "source", "parameter", "value", "def" }.Count(n => a.Attribute(n) != null) != 1) ScenarioLoader.Fail(a, path, "Argument requires exactly one source/parameter/value/def");
                if (a.Attribute("source") != null && ScenarioLoader.A(a, "source") != "selected") ScenarioLoader.Fail(a, path, "Argument source must be selected");
                var parameter = ScenarioLoader.A(a, "parameter");
                if (parameter != null)
                {
                    if (!parameters.TryGetValue(parameter, out var p)) ScenarioLoader.Fail(a, path, "unknown binding parameter " + parameter);
                    if (ScenarioLoader.A(p, "type") != ScenarioLoader.A(a, "type")) ScenarioLoader.Fail(a, path, "Argument and parameter types differ");
                    used.Add(parameter);
                }
                if (a.Attribute("value") != null && Scalars.TryGetValue(ScenarioLoader.A(a, "type"), out var scalar)) ParseScalar(scalar, ScenarioLoader.A(a, "value"), a, path);
                if (a.Attribute("def") != null) Need(a, "def", path);
            }
            if (parameters.Keys.Any(p => !used.Contains(p))) ScenarioLoader.Fail(b, path, "binding declares an unused parameter");
            foreach (var expected in b.Elements("ExpectHediff"))
            {
                if (new[] { "type", "def" }.Count(n => expected.Attribute(n) != null) != 1) ScenarioLoader.Fail(expected, path, "ExpectHediff requires exactly one type/def");
                if (expected.Attribute("type") != null) Identifier(expected, "type", path, true); else Need(expected, "def", path);
                if (ScenarioLoader.I(expected, "count", 1) < 1) ScenarioLoader.Fail(expected, path, "ExpectHediff count must be positive");
                var checks = new[] { "equals", "equalsParameter" }.Count(n => expected.Attribute(n) != null);
                if ((expected.Attribute("property") != null ? checks != 1 : checks != 0)) ScenarioLoader.Fail(expected, path, "property check requires property and exactly one equals/equalsParameter");
                if (expected.Attribute("property") != null) Identifier(expected, "property", path);
                if (expected.Attribute("equalsParameter") != null && !parameters.ContainsKey(ScenarioLoader.A(expected, "equalsParameter"))) ScenarioLoader.Fail(expected, path, "unknown postcondition parameter");
            }
            if (b.Element("Result") != null && b.Element("Result").Attribute("equals") == null) ScenarioLoader.Fail(b, path, "Result requires equals");
        }

        public static void ValidateCall(XElement step, XElement binding, string path)
        {
            ValidateDefinition(binding, path);
            if (ScenarioLoader.A(step, "actionId") != ScenarioLoader.A(binding, "id")) ScenarioLoader.Fail(step, path, "action binding id mismatch");
            var parameters = binding.Element("Parameters")?.Elements("Parameter").ToDictionary(p => ScenarioLoader.A(p, "name")) ?? new Dictionary<string, XElement>();
            var seen = new HashSet<string>();
            foreach (var arg in step.Elements("Arg"))
            {
                var name = Need(arg, "name", path); var value = Need(arg, "value", path);
                if (!seen.Add(name)) ScenarioLoader.Fail(arg, path, "duplicate action argument " + name);
                if (!parameters.TryGetValue(name, out var p)) ScenarioLoader.Fail(arg, path, "unknown action argument " + name);
                if (Scalars.TryGetValue(ScenarioLoader.A(p, "type"), out var type))
                {
                    var parsed = ParseScalar(type, value, arg, path);
                    if (p.Attribute("min") != null || p.Attribute("max") != null)
                    {
                        var number = Convert.ToDouble(parsed, CultureInfo.InvariantCulture);
                        if (number < ScenarioLoader.D(p, "min", double.MinValue) || number > ScenarioLoader.D(p, "max", double.MaxValue)) ScenarioLoader.Fail(arg, path, "action argument outside declared bounds: " + name);
                    }
                }
            }
            if (parameters.Keys.Any(p => !seen.Contains(p))) ScenarioLoader.Fail(step, path, "missing action argument: " + string.Join(",", parameters.Keys.Where(p => !seen.Contains(p))));
        }

        public static object ParseScalar(Type type, string text, XElement location, string path)
        {
            try
            {
                if (type.IsEnum)
                {
                    if (!Enum.GetNames(type).Contains(text)) throw new FormatException("expected a named enum value");
                    return Enum.Parse(type, text);
                }
                if (!Scalars.Values.Contains(type)) throw new FormatException("unsupported scalar type " + type.FullName);
                var value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
                if (value is float f && (float.IsNaN(f) || float.IsInfinity(f)) || value is double d && (double.IsNaN(d) || double.IsInfinity(d))) throw new FormatException("nonfinite number");
                return value;
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException)
            { ScenarioLoader.Fail(location, path, "invalid " + type.Name + " binding value '" + text + "': " + ex.Message); return null; }
        }
        internal static string Text(object value) => value == null ? "null" : value is bool b ? (b ? "true" : "false") : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    // Shared with the CLI tests; game objects and loaded-mod lookup enter through typed callbacks.
    public sealed class ScenarioBoundAction
    {
        private readonly XElement binding;
        private readonly MethodInfo method;
        private readonly Type[] argumentTypes;
        private readonly Dictionary<string, Type> parameterTypes;
        private readonly Func<Type, string, object> resolveDef;
        private readonly Func<object, IEnumerable<object>> hediffs;
        private readonly Func<object, string> hediffDef;
        private readonly List<Tuple<XElement, Type, PropertyInfo>> checks = new List<Tuple<XElement, Type, PropertyInfo>>();

        public ScenarioBoundAction(XElement definition, Func<string, Type> resolveType, Func<Type, bool> ownedType,
            Func<Type, string, object> resolveDef, Type hediffBaseType, Func<object, IEnumerable<object>> hediffs, Func<object, string> hediffDef)
        {
            binding = new XElement(definition); ScenarioActionBindings.ValidateDefinition(binding, "binding");
            this.resolveDef = resolveDef; this.hediffs = hediffs; this.hediffDef = hediffDef;
            Type Resolve(string name)
            {
                if (ScenarioActionBindings.Scalars.TryGetValue(name, out var scalar)) return scalar;
                var type = resolveType(name);
                if (type == null || !type.IsVisible || type.ContainsGenericParameters || type.IsByRef || type.IsPointer) throw new ScenarioConfigurationException("Binding type is unavailable or unsupported: " + name);
                return type;
            }
            var owner = Resolve(ScenarioLoader.A(binding, "type"));
            if (!ownedType(owner)) throw new ScenarioConfigurationException("Binding method type does not belong to declared package: " + owner.FullName);
            var arguments = binding.Element("Arguments").Elements("Argument").ToList();
            argumentTypes = arguments.Select(a => Resolve(ScenarioLoader.A(a, "type"))).ToArray();
            method = owner.GetMethod(ScenarioLoader.A(binding, "method"), BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly, null, argumentTypes, null);
            if (method == null || method.IsGenericMethod || method.GetParameters().Any(p => p.IsOut || p.ParameterType.IsByRef)
                || !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(argumentTypes)) throw new ScenarioConfigurationException("Binding requires an exact public static method signature: " + owner.FullName + "." + ScenarioLoader.A(binding, "method"));
            if (method.ReturnType != typeof(void) && !method.ReturnType.IsEnum && !ScenarioActionBindings.Scalars.Values.Contains(method.ReturnType)) throw new ScenarioConfigurationException("Binding return type must be void, a scalar or an enum");
            if (binding.Element("Result") != null) ScenarioActionBindings.ParseScalar(method.ReturnType, ScenarioLoader.A(binding.Element("Result"), "equals"), binding, "binding");
            parameterTypes = binding.Element("Parameters")?.Elements("Parameter").ToDictionary(p => ScenarioLoader.A(p, "name"), p => Resolve(ScenarioLoader.A(p, "type"))) ?? new Dictionary<string, Type>();
            foreach (var p in parameterTypes.Values) if (!p.IsEnum && !ScenarioActionBindings.Scalars.Values.Contains(p)) throw new ScenarioConfigurationException("Binding parameters must be scalars or enums");
            for (int i = 0; i < arguments.Count; i++)
            {
                var a = arguments[i];
                if (a.Attribute("value") != null) ScenarioActionBindings.ParseScalar(argumentTypes[i], ScenarioLoader.A(a, "value"), a, "binding");
                if (a.Attribute("def") != null && !argumentTypes[i].IsInstanceOfType(resolveDef(argumentTypes[i], ScenarioLoader.A(a, "def")))) throw new ScenarioConfigurationException("Binding Def has the wrong type");
            }
            foreach (var e in binding.Elements("ExpectHediff"))
            {
                var type = e.Attribute("type") == null ? hediffBaseType : Resolve(ScenarioLoader.A(e, "type"));
                if (!hediffBaseType.IsAssignableFrom(type)) throw new ScenarioConfigurationException("ExpectHediff type must derive from Hediff");
                var property = e.Attribute("property") == null ? null : type.GetProperty(ScenarioLoader.A(e, "property"), BindingFlags.Public | BindingFlags.Instance);
                if (e.Attribute("property") != null && (property?.GetGetMethod() == null || property.GetIndexParameters().Length != 0)) throw new ScenarioConfigurationException("Postcondition requires a public readable property");
                if (property != null)
                {
                    if (e.Attribute("equalsParameter") != null && parameterTypes[ScenarioLoader.A(e, "equalsParameter")] != property.PropertyType) throw new ScenarioConfigurationException("Postcondition property and parameter types differ");
                    if (e.Attribute("equals") != null) ScenarioActionBindings.ParseScalar(property.PropertyType, ScenarioLoader.A(e, "equals"), e, "binding");
                }
                checks.Add(Tuple.Create(e, type, property));
            }
        }

        public void Execute(XElement step, object selected, Action<string, string> trace)
        {
            ScenarioDebugActions.Validate(step, "runtime", binding);
            var values = step.Elements("Arg").ToDictionary(a => ScenarioLoader.A(a, "name"), a => ScenarioActionBindings.ParseScalar(parameterTypes[ScenarioLoader.A(a, "name")], ScenarioLoader.A(a, "value"), a, "runtime"));
            var arguments = binding.Element("Arguments").Elements("Argument").Select((a, i) => {
                object value = a.Attribute("source") != null ? selected : a.Attribute("parameter") != null ? values[ScenarioLoader.A(a, "parameter")] : a.Attribute("def") != null ? resolveDef(argumentTypes[i], ScenarioLoader.A(a, "def")) : ScenarioActionBindings.ParseScalar(argumentTypes[i], ScenarioLoader.A(a, "value"), a, "runtime");
                if (value == null || !argumentTypes[i].IsInstanceOfType(value)) throw new ScenarioConfigurationException("Binding argument " + i + " rejects selected object/value; expected " + argumentTypes[i].FullName);
                return value;
            }).ToArray();
            object result;
            try { result = method.Invoke(null, arguments); }
            catch (TargetInvocationException ex) { throw new ScenarioConfigurationException("Bound method threw: " + method.DeclaringType.FullName + "." + method.Name + ": " + ex.InnerException); }
            trace?.Invoke("debug.binding.result", "action=" + ScenarioLoader.A(binding, "id") + "; method=" + method.DeclaringType.FullName + "." + method.Name + "; arguments=" + string.Join(",", values.Select(p => p.Key + "=" + ScenarioActionBindings.Text(p.Value))) + "; result=" + ScenarioActionBindings.Text(result));
            if (binding.Element("Result") != null && !Equals(result, ScenarioActionBindings.ParseScalar(method.ReturnType, ScenarioLoader.A(binding.Element("Result"), "equals"), binding, "runtime"))) throw new ScenarioConfigurationException("Bound action returned an unexpected result: " + ScenarioActionBindings.Text(result));
            foreach (var check in checks)
            {
                var e = check.Item1; var type = check.Item2; var property = check.Item3;
                var matches = hediffs(selected).Where(h => type.IsInstanceOfType(h) && (e.Attribute("def") == null || hediffDef(h) == ScenarioLoader.A(e, "def"))).ToList();
                if (matches.Count != ScenarioLoader.I(e, "count", 1)) throw new ScenarioConfigurationException("Bound action Hediff count postcondition failed: " + e);
                foreach (var h in matches)
                {
                    var actual = property?.GetValue(h);
                    var expected = property == null ? null : e.Attribute("equalsParameter") != null ? values[ScenarioLoader.A(e, "equalsParameter")] : ScenarioActionBindings.ParseScalar(property.PropertyType, ScenarioLoader.A(e, "equals"), e, "runtime");
                    trace?.Invoke("debug.binding.postcondition", "action=" + ScenarioLoader.A(binding, "id") + "; hediff=" + hediffDef(h) + "; property=" + ScenarioLoader.A(e, "property") + "; actual=" + ScenarioActionBindings.Text(actual) + "; expected=" + ScenarioActionBindings.Text(expected));
                    if (property != null && !Equals(actual, expected)) throw new ScenarioConfigurationException("Bound action Hediff property postcondition failed: " + e + "; actual=" + ScenarioActionBindings.Text(actual));
                }
            }
        }
    }
}
