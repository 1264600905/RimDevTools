using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestGizmoAdapter
    {
        public List<AutoTestGizmoRef> List() { return AutoTestSelectionProbe.Capture().Gizmos; }

        public AutoTestOperationError Validate(string gizmoId, string currentSelectionFingerprint)
        {
            if (!AutoTestSelectionFingerprint.MatchesSelection(gizmoId, currentSelectionFingerprint))
                return new AutoTestOperationError { Code = "runtime.selection_changed", Message = "Gizmo belongs to a stale selection fingerprint." };
            return null;
        }

        public bool TryExecute(string gizmoId, string currentSelectionFingerprint, Action<Gizmo> execute, out AutoTestOperationError error)
        {
            error = Validate(gizmoId, currentSelectionFingerprint);
            if (error != null) return false;
            AutoTestSelectionSnapshot snapshot = AutoTestSelectionProbe.Capture();
            AutoTestGizmoRef reference = snapshot.Gizmos.FirstOrDefault(item => string.Equals(item.Id, gizmoId, StringComparison.Ordinal));
            if (reference == null) { error = new AutoTestOperationError { Code = "runtime.gizmo_not_found", Message = "Gizmo is not available for the current selection." }; return false; }
            if (reference.Disabled) { error = new AutoTestOperationError { Code = "runtime.gizmo_disabled", Message = reference.DisabledReason }; return false; }
            if (execute == null) { error = new AutoTestOperationError { Code = "runtime.gizmo_executor_missing", Message = "No executor was provided." }; return false; }
            try
            {
                execute(ResolveGizmo(reference.Ordinal));
                return true;
            }
            catch (Exception ex)
            {
                error = new AutoTestOperationError { Code = "runtime.gizmo_execution_failed", Message = ex.Message, ExceptionType = ex.GetType().FullName, StackTrace = ex.ToString() };
                return false;
            }
        }

        private static Gizmo ResolveGizmo(int ordinal)
        {
            AutoTestSelectionSnapshot snapshot = AutoTestSelectionProbe.Capture();
            int index = 0;
            foreach (object selected in snapshot.Targets.Select(target => new AutoTestGameTargetResolver().Resolve(target).ResolvedObject))
            {
                IEnumerable<Gizmo> gizmos = (selected as ISelectable)?.GetGizmos();
                if (gizmos == null) continue;
                foreach (Gizmo gizmo in gizmos) if (gizmo != null && index++ == ordinal) return gizmo;
            }
            return null;
        }
    }
}
