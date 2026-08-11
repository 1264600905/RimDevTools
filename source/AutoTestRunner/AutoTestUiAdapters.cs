using System;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal interface IAutoTestUiHandle
    {
        string DebugName { get; }
        bool IsOpen { get; }
        void RequestClose();
        void ForceClose();
    }

    internal abstract class AutoTestWindowHandle<TWindow> : IAutoTestUiHandle where TWindow : Window
    {
        protected AutoTestWindowHandle(TWindow window, string debugName)
        {
            Window = window;
            DebugName = debugName;
        }

        protected TWindow Window { get; }

        public string DebugName { get; }

        public bool IsOpen => Window != null && Window.IsOpen;

        public virtual void RequestClose()
        {
            if (Window == null || !Window.IsOpen) return;
            Window.Close(false);
        }

        public virtual void ForceClose()
        {
            if (Window == null) return;
            Find.WindowStack.TryRemove(Window, false);
        }
    }

    internal sealed class AutoTestInfoCardHandle : AutoTestWindowHandle<Dialog_InfoCard>
    {
        public AutoTestInfoCardHandle(Dialog_InfoCard window, Def targetDef)
            : base(window, targetDef?.defName ?? "InfoCard")
        {
            TargetDef = targetDef;
        }

        public Def TargetDef { get; }

        public override void RequestClose()
        {
            if (!IsOpen) return;
            base.RequestClose();
        }
    }

    internal sealed class AutoTestBillConfigHandle : AutoTestWindowHandle<Dialog_BillConfig>
    {
        public AutoTestBillConfigHandle(Dialog_BillConfig window, Bill_Production bill)
            : base(window, bill?.recipe?.defName ?? "RecipeBillConfig")
        {
            Bill = bill;
        }

        public Bill_Production Bill { get; }

        public override void RequestClose()
        {
            if (!IsOpen) return;
            base.RequestClose();
        }
    }

    internal static class AutoTestInfoCardUiAdapter
    {
        public static AutoTestInfoCardHandle Open(Thing thing)
        {
            Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_InfoCard), false);

            Dialog_InfoCard infoCard;
            if (thing is IConstructible constructible)
            {
                ThingDef entityToBuild = thing.def.entityDefToBuild as ThingDef;
                if (entityToBuild != null)
                {
                    infoCard = new Dialog_InfoCard(entityToBuild, constructible.EntityToBuildStuff(), null);
                }
                else
                {
                    infoCard = new Dialog_InfoCard(thing.def.entityDefToBuild, null);
                }
            }
            else
            {
                infoCard = new Dialog_InfoCard(thing, null);
            }

            infoCard.closeOnClickedOutside = true;
            infoCard.closeOnAccept = false;
            infoCard.closeOnCancel = false;
            Find.WindowStack.Add(infoCard);
            return new AutoTestInfoCardHandle(infoCard, thing.def);
        }

        public static AutoTestInfoCardHandle Open(Def targetDef)
        {
            Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_InfoCard), false);

            Dialog_InfoCard infoCard = new Dialog_InfoCard(targetDef, null);
            infoCard.closeOnClickedOutside = true;
            infoCard.closeOnAccept = false;
            infoCard.closeOnCancel = false;
            Find.WindowStack.Add(infoCard);
            return new AutoTestInfoCardHandle(infoCard, targetDef);
        }

        public static AutoTestInfoCardHandle Open(Hediff hediff)
        {
            Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_InfoCard), false);

            Dialog_InfoCard infoCard = new Dialog_InfoCard(hediff);
            infoCard.closeOnClickedOutside = true;
            infoCard.closeOnAccept = false;
            infoCard.closeOnCancel = false;
            Find.WindowStack.Add(infoCard);
            return new AutoTestInfoCardHandle(infoCard, hediff?.def);
        }
    }

    internal static class AutoTestBillConfigUiAdapter
    {
        public static AutoTestBillConfigHandle Open(Bill_Production bill, IntVec3 billGiverPos)
        {
            if (bill == null)
            {
                return null;
            }

            Find.WindowStack.TryRemoveAssignableFromType(typeof(Dialog_BillConfig), false);

            Dialog_BillConfig dialog = new Dialog_BillConfig(bill, billGiverPos);
            dialog.closeOnClickedOutside = true;
            dialog.closeOnAccept = false;
            dialog.closeOnCancel = false;
            Find.WindowStack.Add(dialog);
            return new AutoTestBillConfigHandle(dialog, bill);
        }
    }
}
