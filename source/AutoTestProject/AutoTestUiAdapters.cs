using System;
using System.Diagnostics;
using RimWorld;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
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

            AutoTestUiClickSimulator.TryClickWindowCloseX(Window);
        }
    }

    internal static class AutoTestUiClickSimulator
    {
        private const uint MouseEventLeftDown = 0x0002;
        private const uint MouseEventLeftUp = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        public static Vector2 GetDefaultOutsideClickPosition()
        {
            return new Vector2(10f, Screen.height * 0.5f);
        }

        public static bool TryClickWindowCloseX(Window window)
        {
            if (window == null || !window.IsOpen || !window.doCloseX) return false;

            Rect windowRect = window.windowRect;
            const float closeXSize = 18f;
            const float closeXMargin = 4f;

            Vector2 clickPosition = new Vector2(
                windowRect.xMax - closeXMargin - (closeXSize * 0.5f),
                windowRect.y + closeXMargin + (closeXSize * 0.5f));

            return TryLeftClickAtClientPosition(clickPosition);
        }

        public static bool TryCloseByOutsideClick(Vector2 clientPosition)
        {
            return TryLeftClickAtClientPosition(clientPosition);
        }

        private static bool TryLeftClickAtClientPosition(Vector2 clientPosition)
        {
            IntPtr windowHandle = GetGameWindowHandle();
            if (windowHandle == IntPtr.Zero) return false;

            Point screenPoint = new Point
            {
                X = Mathf.RoundToInt(clientPosition.x),
                Y = Mathf.RoundToInt(clientPosition.y)
            };

            if (!ClientToScreen(windowHandle, ref screenPoint)) return false;

            Point originalCursor;
            bool hadOriginalCursor = GetCursorPos(out originalCursor);

            if (!SetCursorPos(screenPoint.X, screenPoint.Y)) return false;

            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(16);
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);

            if (hadOriginalCursor)
            {
                Thread.Sleep(16);
                SetCursorPos(originalCursor.X, originalCursor.Y);
            }

            return true;
        }

        private static IntPtr GetGameWindowHandle()
        {
            IntPtr mainWindowHandle = Process.GetCurrentProcess().MainWindowHandle;
            if (mainWindowHandle != IntPtr.Zero) return mainWindowHandle;
            return GetForegroundWindow();
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
    }
}
