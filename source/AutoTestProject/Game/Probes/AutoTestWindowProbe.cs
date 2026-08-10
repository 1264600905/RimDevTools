using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestWindowProbe
    {
        public static AutoTestWindowSnapshot Capture(Window expected = null, string captureId = null)
        {
            AutoTestWindowSnapshot snapshot = new AutoTestWindowSnapshot { CaptureId = captureId ?? Guid.NewGuid().ToString("N") };
            try
            {
                List<Window> windows = Find.WindowStack?.Windows?.Where(window => window != null).ToList() ?? new List<Window>();
                snapshot.WindowTypes.AddRange(windows.Select(window => window.GetType().FullName ?? window.GetType().Name));
                Window topmost = windows.LastOrDefault();
                Window selected = expected ?? topmost;
                snapshot.TopmostWindowType = topmost?.GetType().FullName ?? string.Empty;
                snapshot.IsOpen = selected != null && windows.Contains(selected);
                if (selected != null)
                {
                    snapshot.WindowRect = ReadMember(selected, "windowRect") ?? string.Empty;
                    snapshot.CloseOnAccept = ReadBool(selected, "closeOnAccept");
                    snapshot.CloseOnCancel = ReadBool(selected, "closeOnCancel");
                    snapshot.CloseOnClickedOutside = ReadBool(selected, "closeOnClickedOutside");
                    snapshot.DoCloseX = ReadBool(selected, "doCloseX");
                    snapshot.WindowTarget = ResolveTarget(selected);
                    snapshot.BoundDefName = ReadString(selected, "infoCardDef", "def", "thingDef") ?? snapshot.WindowTarget?.DefName;
                    object bill = ReadRaw(selected, "bill", "billInt");
                    snapshot.BoundBillId = bill == null ? string.Empty : ReadString(bill, "loadID", "BillId") ?? bill.GetType().Name;
                    snapshot.BoundRecipeDefName = ReadString(bill, "recipe", "recipeDef") ?? string.Empty;
                }
                snapshot.Summary = "open=" + snapshot.IsOpen + " top=" + snapshot.TopmostWindowType + " windows=" + string.Join(",", snapshot.WindowTypes.ToArray()) + " def=" + snapshot.BoundDefName + " bill=" + snapshot.BoundBillId + " recipe=" + snapshot.BoundRecipeDefName;
            }
            catch (Exception ex)
            {
                snapshot.Summary = "window probe unavailable: " + ex.Message;
            }
            return snapshot;
        }

        private static AutoTestTargetRef ResolveTarget(Window window)
        {
            foreach (FieldInfo field in EnumerateFields(window.GetType()))
            {
                object value;
                try { value = field.GetValue(window); } catch { continue; }
                if (value is Thing thing) return AutoTestThingProbe.Target(thing);
                if (value is Def def) return new AutoTestTargetRef { Id = "def:" + def.defName, Kind = "def", Label = def.label, DefName = def.defName, MapId = -1, Fingerprint = AutoTestSelectionFingerprint.Create(new[] { "def", def.defName }) };
            }
            return null;
        }

        private static IEnumerable<FieldInfo> EnumerateFields(Type type)
        {
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
                foreach (FieldInfo field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) yield return field;
        }

        private static object ReadRaw(object instance, params string[] names)
        {
            if (instance == null) return null;
            foreach (string name in names)
            {
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

        private static string ReadMember(object instance, params string[] names)
        {
            object value = ReadRaw(instance, names);
            return value == null ? null : Convert.ToString(value);
        }
        private static string ReadString(object instance, params string[] names)
        {
            object value = ReadRaw(instance, names);
            if (value == null) return null;
            if (value is Def def) return def.defName;
            return Convert.ToString(value);
        }
        private static bool ReadBool(object instance, params string[] names)
        {
            object value = ReadRaw(instance, names);
            try { return value != null && Convert.ToBoolean(value); } catch { return false; }
        }
    }
}
