using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using RimDevTools;

internal static class SteamworksNativeApiTests
{
    // In-memory bindings only: this regression suite writes no logs, saves or test artifacts.
    internal static int Run()
    {
        var checks = 0;
        void Check(bool success, string message)
        {
            if (!success) throw new Exception("FAIL Steamworks ABI: " + message);
            checks++;
            Console.WriteLine("PASS Steamworks ABI: " + message);
        }
        var path = Path.GetFullPath("steam_api64.dll");
        var exports = new Dictionary<string, Delegate>();
        SteamworksNativeApi Make() => new(name => exports.TryGetValue(name, out var value)
            ? Marshal.GetFunctionPointerForDelegate(value) : IntPtr.Zero, path);
        void Interfaces(bool modern)
        {
            foreach (var name in modern
                ? new[] { "SteamUser_v023", "SteamUtils_v010", "SteamUGC_v021", "SteamApps_v009", "SteamFriends_v018" }
                : new[] { "SteamUser_v021", "SteamUtils_v010", "SteamUGC_v016", "SteamApps_v008", "SteamFriends_v017" })
                exports["SteamAPI_" + name] = new SteamworksClient.InterfaceCall(() => (IntPtr)1);
        }
        Interfaces(true);
        var flatCalls = 0;
        var legacyCalls = 0;
        exports["SteamAPI_InitFlat"] = new SteamworksNativeApi.InitFlat(error =>
        {
            Check(error.Length == 1024, "InitFlat receives a 1024-byte SteamErrMsg");
            flatCalls++;
            return 0;
        });
        exports["SteamAPI_Init"] = new SteamworksClient.NativeBool(() => { legacyCalls++; return true; });
        var modern = Make(); modern.SelectInterfaces(); modern.Initialize();
        Check(flatCalls == 1 && legacyCalls == 0 && modern.Diagnostics.NativeInitResult == 0, "InitFlat succeeds on enum zero and is preferred over legacy bool init");
        Check(modern.ModernUgc && modern.Diagnostics.ItemDetailsSize == 9784
            && modern.Diagnostics.InterfaceExports["Friends"] == "SteamAPI_SteamFriends_v018"
            && modern.Diagnostics.InterfaceExports["Apps"] == "SteamAPI_SteamApps_v009", "current game interfaces selected together with modern UGC layout");
        foreach (var kind in new[] { "User", "Utils", "UGC", "Apps", "Friends" })
            Check(modern.Interface(kind) == (IntPtr)1, kind + " accessor invoked");

        var detailsBytes = new byte[9784];
        BitConverter.GetBytes(123UL).CopyTo(detailsBytes, 0);
        BitConverter.GetBytes(1).CopyTo(detailsBytes, 8);
        BitConverter.GetBytes(294100U).CopyTo(detailsBytes, 20);
        Encoding.UTF8.GetBytes("中文 / English").CopyTo(detailsBytes, 24);
        BitConverter.GetBytes(42UL).CopyTo(detailsBytes, 8160);
        BitConverter.GetBytes(987654321UL).CopyTo(detailsBytes, 9776);
        exports["SteamAPI_ISteamUGC_GetQueryUGCResult"] = new SteamworksClient.ReadQueryBuffer((self, query, index, buffer) =>
        {
            Marshal.Copy(detailsBytes, 0, buffer, detailsBytes.Length);
            return true;
        });
        var details = modern.ReadDetails((IntPtr)1, 8, 0);
        Check(details.Id == 123 && details.Owner == 42 && details.ConsumerAppId == 294100
            && SteamworksClient.Utf8(details.Title) == "中文 / English", "UGC021 writes the full 9784-byte result and reads the unchanged UTF-8 prefix");
        var tagsCalled = false;
        exports["SteamAPI_ISteamUGC_SetItemTags"] = new SteamworksClient.ModernTagsCall((IntPtr self, ulong update, ref SteamworksClient.TagArray tags, bool admin) =>
        {
            tagsCalled = self == (IntPtr)1 && update == 8 && tags.Count == 2 && !admin;
            return true;
        });
        var tags = new SteamworksClient.TagArray { Count = 2 };
        Check(modern.SetItemTags((IntPtr)1, 8, ref tags) && tagsCalled, "UGC021 SetItemTags passes the additional one-byte false argument");
        Check(Marshal.OffsetOf<SteamworksClient.ItemDetails>(nameof(SteamworksClient.ItemDetails.Owner)).ToInt32() == 8160
            && Marshal.OffsetOf<SteamworksClient.ModernItemDetails>(nameof(SteamworksClient.ModernItemDetails.TotalFilesSize)).ToInt32() == 9776
            && Marshal.OffsetOf<SteamworksClient.QueryCompleted>(nameof(SteamworksClient.QueryCompleted.Cursor)).ToInt32() == 21
            && Marshal.SizeOf<SteamworksClient.QueryCompleted>() == 280, "Windows x64 field offsets and query callback match both game wrappers");

        exports["SteamAPI_InitFlat"] = new SteamworksNativeApi.InitFlat(error =>
        {
            Encoding.UTF8.GetBytes("Steam 客户端不可用").CopyTo(error, 0);
            return 2;
        });
        var failed = Make(); failed.SelectInterfaces();
        try { failed.Initialize(); throw new Exception("expected init failure"); }
        catch (SteamworksNativeException ex)
        {
            Check(ex.Code == "STEAMWORKS_INIT_FAILED" && ex.Diagnostics.NativeInitResult == 2
                && ex.Diagnostics.NativeError == "Steam 客户端不可用" && legacyCalls == 0,
                "native failure preserves enum and UTF-8 error; does not retry another initializer");
        }

        exports.Clear(); Interfaces(false);
        exports["SteamAPI_Init"] = new SteamworksClient.NativeBool(() => { legacyCalls++; return true; });
        var legacy = Make(); legacy.SelectInterfaces(); legacy.Initialize();
        Check(legacyCalls == 1 && !legacy.ModernUgc && legacy.Diagnostics.ItemDetailsSize == 9776
            && legacy.Diagnostics.InitializationEntryPoint == "SteamAPI_Init", "legacy SDK uses bool initializer and UGC016 layout");
        exports["SteamAPI_ISteamUGC_GetQueryUGCResult"] = new SteamworksClient.ReadQueryBuffer((self, query, index, buffer) =>
        {
            Marshal.Copy(detailsBytes, 0, buffer, 9776);
            return true;
        });
        Check(legacy.ReadDetails((IntPtr)1, 8, 0).Owner == 42, "legacy result remains readable with the original buffer size");
        exports["SteamAPI_ISteamUGC_SetItemTags"] = new SteamworksClient.LegacyTagsCall((IntPtr self, ulong update, ref SteamworksClient.TagArray value) => value.Count == 2);
        Check(legacy.SetItemTags((IntPtr)1, 8, ref tags), "legacy SetItemTags retains its three-argument signature");
        exports["SteamAPI_Init"] = new SteamworksClient.NativeBool(() => false);
        var legacyFailure = Make(); legacyFailure.SelectInterfaces();
        try { legacyFailure.Initialize(); throw new Exception("expected legacy failure"); }
        catch (SteamworksNativeException ex) { Check(ex.Code == "STEAMWORKS_INIT_FAILED", "legacy false initialization is a distinct failure"); }

        exports.Remove("SteamAPI_Init");
        var missingInit = Make(); missingInit.SelectInterfaces();
        try { missingInit.Initialize(); throw new Exception("expected missing init"); }
        catch (SteamworksNativeException ex)
        {
            Check(ex.Code == "STEAMWORKS_API_UNSUPPORTED" && ex.Diagnostics.MissingExports.Contains("SteamAPI_InitFlat")
                && ex.Diagnostics.MissingExports.Contains("SteamAPI_Init") && ex.Message.Contains(path), "missing initializers report absolute DLL path and both attempted exports");
        }
        exports.Remove("SteamAPI_SteamUGC_v016");
        exports["SteamAPI_SteamUGC_v999"] = new SteamworksClient.InterfaceCall(() => (IntPtr)1);
        try { Make().SelectInterfaces(); throw new Exception("expected unsupported interface"); }
        catch (SteamworksNativeException ex)
        {
            Check(ex.Code == "STEAMWORKS_API_UNSUPPORTED" && ex.Diagnostics.MissingExports.Contains("SteamAPI_SteamUGC_v021")
                && ex.Diagnostics.MissingExports.Contains("SteamAPI_SteamUGC_v016"), "unknown UGC versions are rejected before native initialization");
            var json = JsonSerializer.Serialize(new SteamworksResponse { ErrorCode = ex.Code, NativeDiagnostics = ex.Diagnostics }, JsonDefaults.Storage);
            var roundTrip = JsonSerializer.Deserialize<SteamworksResponse>(json, JsonDefaults.Storage)!;
            Check(roundTrip.NativeDiagnostics?.MissingExports.Count > 0, "helper JSON retains structured compatibility diagnostics");
            var raw = SteamWorkshopsCommand.Json(new
            {
                publicationState = "unpublished", idSource = "unpublished", environmentReason = ex.Message,
                nativeDiagnostics = roundTrip.NativeDiagnostics,
                permissions = WorkshopPermissionEvaluator.Evaluate(null, null)
            });
            var compact = WorkshopStatusView.Format(raw, null, null, false);
            Check(compact.GetProperty("nativeDiagnostics").GetProperty("missingExports").GetArrayLength() > 0,
                "default status error view retains missing exports without requiring verbose");
        }
        GC.KeepAlive(exports);
        return checks;
    }
}
