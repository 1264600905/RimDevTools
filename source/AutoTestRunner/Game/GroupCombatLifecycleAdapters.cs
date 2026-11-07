using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace LogSyncMod.AutoTest
{
    // Mods with private ownership or deferred resurrection systems can expose their
    // state here. Native faction/Lord ownership requires no adapter.
    public static class GroupCombatLifecycleAdapters
    {
        private sealed class Adapter : IDisposable
        {
            internal string Id;
            internal Func<Pawn, Faction> Owner;
            internal Func<Pawn, bool> Pending;
            public void Dispose() { if (adapters.TryGetValue(Id, out var current) && current == this) adapters.Remove(Id); }
        }
        private static readonly Dictionary<string, Adapter> adapters = new Dictionary<string, Adapter>();
        public static IDisposable Register(string id, Func<Pawn, Faction> ownerFaction = null, Func<Pawn, bool> pendingRevival = null)
        {
            if (string.IsNullOrWhiteSpace(id) || ownerFaction == null && pendingRevival == null) throw new ArgumentException("Adapter requires an ID and at least one resolver");
            if (adapters.ContainsKey(id)) throw new ArgumentException("Duplicate group combat lifecycle adapter: " + id);
            var adapter = new Adapter { Id = id, Owner = ownerFaction, Pending = pendingRevival };
            adapters.Add(id, adapter); return adapter;
        }
        internal static Faction OwnerFaction(Pawn pawn) => adapters.Values.ToList().Select(a => a.Owner?.Invoke(pawn)).FirstOrDefault(f => f != null);
        internal static bool PendingRevival(Pawn pawn) => adapters.Values.ToList().Any(a => a.Pending?.Invoke(pawn) == true);
    }
}
