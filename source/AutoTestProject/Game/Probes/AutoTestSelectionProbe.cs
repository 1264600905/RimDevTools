using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestSelectionProbe
    {
        public static AutoTestSelectionSnapshot Capture(IEnumerable<object> selectedObjects = null)
        {
            List<object> selected = selectedObjects?.ToList() ?? ReadSelectedObjects();
            AutoTestSelectionSnapshot snapshot = new AutoTestSelectionSnapshot();
            snapshot.Targets.AddRange(selected.Select(item => AutoTestThingProbe.Target(item as Thing)).Where(item => item != null));
            snapshot.Fingerprint = AutoTestSelectionFingerprint.Create(snapshot.Targets.Select(item => item.Id));
            snapshot.SelectionId = AutoTestTargetIds.Selection(snapshot.Fingerprint);
            snapshot.Gizmos.AddRange(ListGizmos(selected, snapshot.Fingerprint));
            snapshot.Summary = "selection=" + snapshot.SelectionId + " targets=" + string.Join(",", snapshot.Targets.Select(item => item.Id).ToArray()) + " gizmos=" + snapshot.Gizmos.Count;
            return snapshot;
        }

        public static List<AutoTestGizmoRef> ListGizmos(IEnumerable<object> selectedObjects, string fingerprint)
        {
            List<AutoTestGizmoRef> result = new List<AutoTestGizmoRef>();
            int ordinal = 0;
            foreach (object selected in selectedObjects ?? Enumerable.Empty<object>())
            {
                object value = ReadRaw(selected, "GetGizmos");
                IEnumerable enumerable = value as IEnumerable;
                if (enumerable == null) continue;
                foreach (object gizmo in enumerable)
                {
                    if (!(gizmo is Gizmo)) continue;
                    string semantic = (gizmo.GetType().FullName ?? gizmo.GetType().Name) + "|" + ReadString(gizmo, "defaultLabel", "label") + "|" + ReadString(gizmo, "desc");
                    result.Add(new AutoTestGizmoRef
                    {
                        Id = AutoTestSelectionFingerprint.CreateGizmoId(fingerprint, ordinal, semantic),
                        SelectionFingerprint = fingerprint,
                        Ordinal = ordinal++,
                        SemanticFingerprint = AutoTestSelectionFingerprint.Create(new[] { semantic }),
                        Label = ReadString(gizmo, "defaultLabel", "label") ?? gizmo.GetType().Name,
                        Disabled = ReadBool(gizmo, "disabled"),
                        DisabledReason = ReadString(gizmo, "disabledReason") ?? string.Empty
                    });
                }
            }
            return result;
        }

        private static List<object> ReadSelectedObjects()
        {
            List<object> result = new List<object>();
            object selector = Find.Selector;
            object selected = ReadRaw(selector, "SelectedObjects", "selectedObjects", "SelectedObjectsListForReading", "selectedObjectsListForReading");
            IEnumerable enumerable = selected as IEnumerable;
            if (enumerable != null) foreach (object item in enumerable) result.Add(item);
            return result;
        }

        private static object ReadRaw(object instance, params string[] names)
        {
            if (instance == null) return null;
            foreach (string name in names)
            {
                if (name == "GetGizmos")
                {
                    MethodInfo method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (method != null) { try { return method.Invoke(instance, null); } catch { } }
                }
                for (Type current = instance.GetType(); current != null; current = current.BaseType)
                {
                    FieldInfo field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null) return field.GetValue(instance);
                    PropertyInfo property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null && property.GetIndexParameters().Length == 0) { try { return property.GetValue(instance, null); } catch { } }
                }
            }
            return null;
        }
        private static string ReadString(object instance, params string[] names) { object value = ReadRaw(instance, names); return value == null ? null : Convert.ToString(value); }
        private static bool ReadBool(object instance, params string[] names) { object value = ReadRaw(instance, names); try { return value != null && Convert.ToBoolean(value); } catch { return false; } }
    }
}
