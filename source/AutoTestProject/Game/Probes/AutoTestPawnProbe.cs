using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestPawnProbe
    {
        public static AutoTestPawnSnapshot Capture(Pawn pawn)
        {
            if (pawn == null) return new AutoTestPawnSnapshot { Destroyed = true, Alive = false, Summary = "pawn=missing" };
            AutoTestPawnSnapshot snapshot = new AutoTestPawnSnapshot
            {
                Target = AutoTestThingProbe.Target(pawn),
                Alive = !pawn.Dead && !pawn.Destroyed,
                Dead = pawn.Dead,
                Downed = pawn.Downed,
                Destroyed = pawn.Destroyed,
                Drafted = pawn.drafter?.Drafted ?? false,
                MapId = pawn.Map?.uniqueID ?? -1,
                Position = pawn.PositionHeld.ToString(),
                CurrentJobDefName = pawn.CurJobDef?.defName ?? string.Empty,
                CurrentJobTarget = pawn.CurJob == null ? string.Empty : pawn.CurJob.targetA.ToString(),
                MentalStateDefName = pawn.MentalStateDef?.defName ?? string.Empty,
                HealthPercent = pawn.health?.summaryHealth?.SummaryHealthPercent ?? (pawn.Dead ? 0f : 1f)
            };
            if (pawn.health?.hediffSet?.hediffs != null)
            {
                foreach (Hediff hediff in pawn.health.hediffSet.hediffs.Where(item => item != null))
                {
                    snapshot.Hediffs.Add(new AutoTestHediffSnapshot
                    {
                        DefName = hediff.def?.defName ?? string.Empty,
                        Severity = hediff.Severity,
                        Stage = ReadMember(hediff, "CurStageIndex") ?? ReadMember(hediff, "CurStage") ?? string.Empty,
                        TicksToDisappear = ReadNullableInt(hediff, "ticksToDisappear", "TicksToDisappear")
                    });
                }
            }
            if (pawn.equipment?.AllEquipmentListForReading != null) snapshot.EquipmentDefNames.AddRange(pawn.equipment.AllEquipmentListForReading.Where(item => item?.def != null).Select(item => item.def.defName));
            if (pawn.apparel?.WornApparel != null) snapshot.ApparelDefNames.AddRange(pawn.apparel.WornApparel.Where(item => item?.def != null).Select(item => item.def.defName));
            if (pawn.inventory?.innerContainer != null) snapshot.InventoryDefNames.AddRange(pawn.inventory.innerContainer.Where(item => item?.def != null).Select(item => item.def.defName));
            foreach (Ability ability in GetAbilities(pawn)) snapshot.Abilities.Add(AutoTestAbilityProbe.Capture(ability));
            snapshot.Summary = "id=" + snapshot.Target?.Id +
                " alive=" + snapshot.Alive +
                " downed=" + snapshot.Downed +
                " dead=" + snapshot.Dead +
                " map=" + snapshot.MapId +
                " pos=" + snapshot.Position +
                " drafted=" + snapshot.Drafted +
                " job=" + snapshot.CurrentJobDefName +
                " health=" + snapshot.HealthPercent.ToString("0.000") +
                " equipment=" + string.Join(",", snapshot.EquipmentDefNames.ToArray()) +
                " hediffs=" + string.Join(",", snapshot.Hediffs.Select(item => item.DefName).ToArray());
            return snapshot;
        }

        internal static IEnumerable<Ability> GetAbilities(Pawn pawn)
        {
            object tracker = pawn?.abilities;
            if (tracker == null) yield break;
            object value = ReadRawMember(tracker, "abilities");
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null) yield break;
            foreach (object item in enumerable) if (item is Ability ability) yield return ability;
        }

        internal static object ReadRawMember(object instance, params string[] names)
        {
            if (instance == null) return null;
            Type type = instance.GetType();
            foreach (string name in names)
            {
                for (Type current = type; current != null; current = current.BaseType)
                {
                    FieldInfo field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null) return field.GetValue(instance);
                    PropertyInfo property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null && property.GetIndexParameters().Length == 0)
                    {
                        try { return property.GetValue(instance, null); } catch { }
                    }
                }
            }
            return null;
        }

        private static string ReadMember(object instance, params string[] names)
        {
            object value = ReadRawMember(instance, names);
            return value == null ? null : Convert.ToString(value);
        }

        private static int? ReadNullableInt(object instance, params string[] names)
        {
            object value = ReadRawMember(instance, names);
            if (value == null) return null;
            try { return Convert.ToInt32(value); } catch { return null; }
        }
    }
}
