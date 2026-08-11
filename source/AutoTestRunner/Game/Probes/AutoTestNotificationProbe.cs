using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestNotificationProbe
    {
        public static AutoTestNotificationSnapshot Capture()
        {
            AutoTestNotificationSnapshot result = new AutoTestNotificationSnapshot { Available = true };
            try { CaptureMessages(result); } catch (Exception ex) { result.Available = false; result.UnavailableReason = Append(result.UnavailableReason, "messages:" + ex.Message); }
            try { CaptureLetters(result); } catch (Exception ex) { result.Available = false; result.UnavailableReason = Append(result.UnavailableReason, "letters:" + ex.Message); }
            try { CaptureAlerts(result); } catch (Exception ex) { result.Available = false; result.UnavailableReason = Append(result.UnavailableReason, "alerts:" + ex.Message); }
            result.Summary = "available=" + result.Available + " messages=" + result.Messages.Count + " letters=" + result.Letters.Count + " alerts=" + result.Alerts.Count;
            return result;
        }

        private static void CaptureMessages(AutoTestNotificationSnapshot result)
        {
            IEnumerable messages = ReadStaticRaw(typeof(Messages), "messages", "liveMessages", "messageHistory") as IEnumerable;
            if (messages == null) return;
            int index = 0;
            foreach (object message in messages)
            {
                if (message == null) continue;
                result.Messages.Add(new AutoTestNotificationItem
                {
                    Kind = "message", Id = "message:" + index++, Label = ReadString(message, "def", "Def") ?? string.Empty,
                    Text = ReadString(message, "text", "Text") ?? Convert.ToString(message),
                    DefName = ReadString(ReadRaw(message, "def", "Def"), "defName") ?? string.Empty,
                    TargetSummary = Convert.ToString(ReadRaw(message, "lookTargets", "lookTarget", "LookTargets")),
                    Tick = ReadInt(message, "startingFrame", "tick", "startingTick") ?? (Find.TickManager?.TicksGame ?? -1)
                });
            }
        }

        private static void CaptureLetters(AutoTestNotificationSnapshot result)
        {
            object letterStack = Find.LetterStack;
            IEnumerable letters = ReadRaw(letterStack, "LettersListForReading", "lettersList", "letters") as IEnumerable;
            if (letters == null) return;
            int index = 0;
            foreach (object letter in letters)
            {
                if (letter == null) continue;
                result.Letters.Add(new AutoTestNotificationItem
                {
                    Kind = "letter",
                    Id = "letter:" + index++, Label = ReadString(letter, "Label", "label") ?? string.Empty,
                    Text = ReadString(letter, "text", "Text") ?? string.Empty,
                    DefName = ReadString(ReadRaw(letter, "def", "Def"), "defName", "defName") ?? string.Empty,
                    TargetSummary = Convert.ToString(ReadRaw(letter, "lookTarget", "LookTarget")),
                    Tick = Find.TickManager?.TicksGame ?? -1,
                    Dismissible = true
                });
            }
        }

        private static void CaptureAlerts(AutoTestNotificationSnapshot result)
        {
            object readout = ReadStaticRaw(typeof(Find), "AlertsReadout", "alertsReadout");
            IEnumerable alerts = ReadRaw(readout, "AllAlerts", "alerts", "activeAlerts") as IEnumerable;
            if (alerts == null) return;
            int index = 0;
            foreach (object alert in alerts)
            {
                if (alert == null) continue;
                result.Alerts.Add(new AutoTestNotificationItem
                {
                    Kind = "alert", Id = "alert:" + index++, Label = ReadString(alert, "Label", "label") ?? alert.GetType().Name,
                    Text = ReadString(alert, "Explanation", "explanation") ?? string.Empty,
                    DefName = alert.GetType().FullName,
                    TargetSummary = Convert.ToString(ReadRaw(alert, "Culprits", "culprits")),
                    Tick = Find.TickManager?.TicksGame ?? -1
                });
            }
        }

        internal static object ReadRaw(object instance, params string[] names)
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
        private static object ReadStaticRaw(Type type, params string[] names)
        {
            foreach (string name in names)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field.GetValue(null);
                PropertyInfo property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.GetIndexParameters().Length == 0) { try { return property.GetValue(null, null); } catch { } }
            }
            return null;
        }
        private static string ReadString(object instance, params string[] names) { object value = ReadRaw(instance, names); return value == null ? null : Convert.ToString(value); }
        private static int? ReadInt(object instance, params string[] names) { object value = ReadRaw(instance, names); try { return value == null ? (int?)null : Convert.ToInt32(value); } catch { return null; } }
        private static string Append(string current, string value) { return string.IsNullOrEmpty(current) ? value : current + " | " + value; }
    }
}
