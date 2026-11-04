using System.Text.Json;
using RimDevTools;

internal static class WorkshopPublishingTests
{
    internal static async Task<int> RunAsync(string fixtureRoot)
    {
        var passed = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL publishing: " + message); passed++; }
        var project = Path.Combine(fixtureRoot, "WorkshopPublishing");
        var game = Path.Combine(project, "game");
        var target = Path.Combine(game, "Mods", "Fixture");
        var configDirectory = Path.Combine(project, "Config");
        foreach (var directory in new[] { project, Path.Combine(project, "About"), target, Path.Combine(target, "About"), Path.Combine(game, "Data"), configDirectory }) Directory.CreateDirectory(directory);
        var managed = Path.Combine(game, "RimWorldWin64_Data", "Managed"); Directory.CreateDirectory(managed);
        foreach (var name in RimWorldGameLayout.LegacyRequiredAssemblies) File.WriteAllText(Path.Combine(managed, name), "unit fixture");
        File.WriteAllText(Path.Combine(game, "RimWorldWin64.exe"), "unit fixture, never executed");
        File.WriteAllText(Path.Combine(game, "steam_api64.dll"), "unit fixture, never loaded");
        const string about = "<ModMetaData><name>Unit</name><packageId>unit.workshop</packageId><description>Unit description</description></ModMetaData>";
        foreach (var root in new[] { project, target })
        {
            File.WriteAllText(Path.Combine(root, "About", "About.xml"), about);
            File.WriteAllText(Path.Combine(root, "About", "PublishedFileId.txt"), "123");
        }
        var modsConfig = Path.Combine(configDirectory, "ModsConfig.xml");
        File.WriteAllText(modsConfig, "<ModsConfigData><version>1.6</version><activeMods><li>ludeon.rimworld</li></activeMods></ModsConfigData>");
        var config = new ProjectConfig { ConfigVersion = 2, ModName = "Fixture", GameVersion = "1.6", RimWorldDir = game, ModsConfigPath = modsConfig, SyncItems = ["About"], CsprojPaths = [], LogKeywords = ["Fixture"] };
        var context = new ProjectContext(project, Path.Combine(project, "mod_config.jsonc"), config);
        void SaveConfig() => File.WriteAllText(context.ConfigPath, JsonSerializer.Serialize(config, JsonDefaults.Storage));
        SaveConfig();
        DeploymentReceiptStore.Save(context, new(1, "unit", "unit-deploy", "completed", "full", project, target, "unit.workshop", "1.6",
            DeploymentReceiptStore.ConfigurationFingerprint(context, false), DateTimeOffset.UtcNow, await DeploymentReceiptStore.CaptureInputsAsync(context, false),
            DeploymentReceiptStore.ScanTarget(target), "123", "not_required", true, false));
        var item = new WorkshopItemInfo("123", "1", 294100, "Unit", "Unit description", "full", "private", 0, null, 123, "", "", 0, null, "Mod,1.6");
        var health = new SteamworksResponse { SteamRunning = true, RimWorldInLibrary = true, WorkshopAvailability = "available", CurrentUserSteamId = "1",
            Page = new(1, 1, false, DateTimeOffset.UtcNow, [item]) };
        var oldProbe = SteamworksSessionProbe.ProbeOverride;
        var oldPublish = WorkshopPublishingService.PublishOverride;
        var oldRunning = ProcessUtil.RimWorldRunningOverride;
        var oldDirectory = Environment.CurrentDirectory;
        var calls = 0;
        WorkshopPublishRequest? lastRequest = null;
        try
        {
            Environment.CurrentDirectory = project;
            ProcessUtil.RimWorldRunningOverride = () => false;
            SteamworksSessionProbe.ProbeOverride = _ => Task.FromResult(health);
            WorkshopPublishingService.PublishOverride = (_, request, _) =>
            {
                calls++; lastRequest = request;
                var result = new WorkshopPublishResult { OperationId = request.OperationId, PublishedFileId = "123", Stage = "submitting", SubmissionStarted = true,
                    OperationStartedAt = DateTimeOffset.UtcNow.AddSeconds(-3), SubmittedAt = DateTimeOffset.UtcNow.AddSeconds(-2), SubmitCallCount = 1, SetItemContentCalled = request.ContentPath is not null };
                if (calls == 1)
                {
                    result = WorkshopPublisher.MarkTimedOut(result, "controlled timeout") with
                    {
                        LastUpdatedAtUnix = 123, CurrentTime = DateTimeOffset.UtcNow,
                        RemoteQuery = new { source = "unit_test", cachedData = false, checkedAt = DateTimeOffset.UtcNow }
                    };
                    return Task.FromResult(new WorkshopPublishExecution(false, ExitCodes.Timeout, result.ErrorCode, result.Message, SteamWorkshopsCommand.Json(result)));
                }
                result = result with { Succeeded = true, Stage = "succeeded", RemoteOutcome = "accepted", SubmitResult = 1 };
                return Task.FromResult(new WorkshopPublishExecution(true, 0, null, null, SteamWorkshopsCommand.Json(result)));
            };
            var noteFile = Path.Combine(project, "change-note.txt");
            const string note = "测试内容更新：预热缩短。\n第二行：保留中文与换行。";
            File.WriteAllText(noteFile, note, new System.Text.UTF8Encoding(true));
            var timeout = await WorkshopPublishingService.UploadAsync(context, ["steamworkshops", "upload", "--change-note-file", noteFile], CancellationToken.None);
            Check(lastRequest?.ChangeNote == note, "UTF-8 BOM multiline note reaches the single content submission request");
            var timeoutJson = SteamWorkshopsCommand.Json(timeout.Details);
            Check(timeout.ExitCode == ExitCodes.Timeout && timeoutJson.GetProperty("timedOut").GetBoolean() && timeoutJson.GetProperty("remoteOutcome").GetString() == "unknown", "timeout remains unknown and nonzero");
            Check(timeoutJson.GetProperty("lastUpdatedAtUnix").GetUInt32() == 123 && timeoutJson.GetProperty("currentTimeUnix").GetInt64() > 123
                && timeoutJson.GetProperty("submittedAtUnix").ValueKind == JsonValueKind.Number, "remote time, local current time and actual local submission time are distinct");
            Check(!EnvironmentLock.IsBusy(configDirectory) && calls == 1, "timeout releases lock and never automatically retries");
            var retry = await WorkshopPublishingService.UploadAsync(context, ["steamworkshops", "upload"], CancellationToken.None);
            Check(retry.Ok && calls == 2, "next explicit call is not blocked by timeout history");
            Check(lastRequest is { Name: null, Description: null, PreviewPath: null, Visibility: null, Tags: null, ContentPath: not null }, "existing upload sets content only");
            Check(lastRequest!.ChangeNote is null, "absent note does not send legacy fixed text");
            var metadata = await WorkshopPublishingService.EditAsync(context, ["steamworkshops", "edit"], CancellationToken.None);
            Check(metadata.Ok && calls == 3 && lastRequest is { ContentPath: null, Files: null, Description: "Unit description", Name: null, Tags: null }, "metadata path cannot set content");
            var added = Path.Combine(project, "About", "new.xml"); File.WriteAllText(added, "<new/>");
            var blocked = await WorkshopPublishingService.UploadAsync(context, ["steamworkshops", "upload"], CancellationToken.None);
            Check(!blocked.Ok && blocked.ErrorCode == "PROJECT_NOT_DEPLOYED" && calls == 3
                && SteamWorkshopsCommand.Json(blocked.Details).GetProperty("submitCallCount").GetInt32() == 0, "changed input rejected before publisher invocation");
            File.Delete(added);
            health = health with { CurrentUserSteamId = "2" };
            var denied = await WorkshopPublishingService.UploadAsync(context, ["steamworkshops", "upload"], CancellationToken.None);
            Check(!denied.Ok && denied.ErrorCode == "WORKSHOP_OWNER_REQUIRED" && calls == 3, "nonowner never reaches submit");
            health = health with { CurrentUserSteamId = "1" };
            using (var gate = EnvironmentLock.TryAcquire(configDirectory, "unit held lock", project, "unit", out _))
            {
                Check(gate is not null, "unit environment lock acquired");
                var readOnly = await WorkshopPublishingService.UploadAsync(context, ["steamworkshops", "upload", "--dry-run"], CancellationToken.None);
                Check(readOnly.Ok && calls == 3 && gate!.Owns(configDirectory), "dry-run needs no new lock and makes no publication call");
            }
            var unknown = WorkshopPublisher.MarkTimedOut(new() { OperationId = "unit-unknown-create", CreationAttempted = true, Stage = "creating_private_item" }, "controlled");
            var unknownJson = SteamWorkshopsCommand.Json(unknown);
            Check(unknown.PublishedFileId is null && unknown.SubmittedAt is null && !unknown.PostTimeoutQueryAttempted
                && unknownJson.GetProperty("lastUpdatedAtUnix").ValueKind == JsonValueKind.Null, "unknown creation does not fabricate an id/time or query a guessed target");
            var queries = 0;
            var unknownEvidence = WorkshopPublisher.CollectTimeoutEvidence(unknown, _ => { queries++; throw new Exception("must not query"); });
            Check(queries == 0 && unknownEvidence.LastUpdatedAtUnix is null && unknownEvidence.RemoteQuery is not null, "unknown id returns explicit unavailable reason without query");
            var known = WorkshopPublisher.MarkTimedOut(new() { OperationId = "unit-known", PublishedFileId = "123", PreviousUpdatedAtUnix = 42 }, "controlled");
            var timeEvidence = WorkshopPublisher.CollectTimeoutEvidence(known, _ => { queries++; return new(1, 1, false, DateTimeOffset.UtcNow, [item]); });
            Check(queries == 1 && timeEvidence.LastUpdatedAtUnix == 123 && timeEvidence.PreviousUpdatedAtUnix == 42, "exactly one fresh query supplies target time without replacing previous time");
            var unavailableTime = WorkshopPublisher.CollectTimeoutEvidence(known, _ => { queries++; throw new IOException("controlled offline"); });
            Check(queries == 2 && unavailableTime.LastUpdatedAtUnix is null && SteamWorkshopsCommand.Json(unavailableTime.RemoteQuery!).GetProperty("reason").GetString() == "controlled offline", "failed time query stays null with reason and no retry");
            var plan = await Capture(["deploy", "--upload", "--dry-run"]);
            Check(plan.GetProperty("ok").GetBoolean() && calls == 3 && plan.GetProperty("result").GetProperty("upload").GetProperty("verification").GetString() == "pending_after_deploy", "combined dry-run does not build/bake/publish: " + plan.GetRawText());
            config.SyncItems = ["MissingInput"]; SaveConfig();
            var deployFailure = await Capture(["deploy", "--upload"]);
            Check(!deployFailure.GetProperty("ok").GetBoolean() && calls == 3 && !deployFailure.GetProperty("error").GetProperty("details").GetProperty("uploadStarted").GetBoolean(), "failed deployment never publishes");
            config.SyncItems = ["About"]; SaveConfig();
            using (var locked = File.Open(DeploymentReceiptStore.PathFor(context), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var receiptFailure = await Capture(["deploy", "--upload"]);
                Check(!receiptFailure.GetProperty("ok").GetBoolean() && calls == 3, "failed receipt persistence never publishes");
            }
            Check(Directory.GetFiles(ProjectDataPaths.DirectoryPath(project)).All(path => !Path.GetFileName(path).Contains("pending", StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(path).Contains("unconfirmed", StringComparison.OrdinalIgnoreCase)), "no persistent timeout blocker");
            var sourceIdPath = Path.Combine(project, "About", "PublishedFileId.txt");
            File.Delete(sourceIdPath); Directory.CreateDirectory(sourceIdPath);
            File.Delete(Path.Combine(target, "About", "PublishedFileId.txt"));
            var unbound = DeploymentReceiptStore.Read(context)!;
            DeploymentReceiptStore.Save(context, unbound with { PublishedFileId = null,
                Inputs = unbound.Inputs.Where(f => f.Source != "identity").ToArray(), Files = unbound.Files.Where(f => f.Source != "identity").ToArray() });
            try { DeploymentReceiptStore.SaveCreatedIdentity(context, target, "456"); throw new Exception("Expected identity write failure"); }
            catch (IOException) { Check(DeploymentReceiptStore.KnownBinding(context) == "456", "genuine created id is retained before an id-file write failure"); }
            var afterIdWriteFailure = await WorkshopPublishingService.UploadAsync(context, ["steamworkshops", "upload", "--dry-run"], CancellationToken.None);
            Check(!afterIdWriteFailure.Ok && calls == 3 && SteamWorkshopsCommand.Json(afterIdWriteFailure.Details).GetProperty("submitCallCount").GetInt32() == 0,
                "missing id files with a known creation fact cannot fall back to duplicate creation");
            var image = Path.Combine(project, "additional.png");
            File.WriteAllBytes(image, [137, 80, 78, 71, 13, 10, 26, 10, 0, 0]);
            var previews = new[] { new WorkshopPreview(0, "original", "old.png", 0), new WorkshopPreview(1, "keep", "keep.png", 0) };
            health = health with { Page = new(1, 1, false, DateTimeOffset.UtcNow, [item with { AdditionalPreviews = previews }]) };
            var edited = await WorkshopPublishingService.EditAsync(context, ["steamworkshops", "edit", "--id", "123", "--title", "中文标题", "--text-language", "schinese",
                "--add-preview", image, "--update-preview", "0=" + image, "--remove-preview", "1", "--change-note", note], CancellationToken.None);
            Check(edited.Ok && lastRequest is { Name: "中文标题", TextLanguage: "schinese", Description: null, ContentPath: null, Files: null, PreviewPath: null }
                && lastRequest.ChangeNote == note && lastRequest.AddPreviews!.Single() == image && lastRequest.ReplacePreviews!.Single().Index == 0 && lastRequest.RemovePreviews!.Single() == 1,
                "explicit title/language/preview edits preserve omitted description, primary cover and all content");
            var expectedPreviews = WorkshopMetadata.ExpectedPreviews(previews, [image], [new(0, image)], [1]);
            Check(expectedPreviews.Length == 2 && expectedPreviews.All(x => x.OriginalFileName == "additional.png") && expectedPreviews[1].Index == 1,
                "preview edits use original indexes, remove then append and reindex");
            Check(WorkshopPublisher.PreviewsMatch(expectedPreviews, expectedPreviews.Select(x => x with { Url = "new" }).ToArray())
                && !WorkshopPublisher.PreviewsMatch(previews, previews.Reverse().ToArray()), "remote preview verification checks order/count/type/name and unchanged URLs");
            foreach (var bad in new[] { "", " ", new string('中', 43), "bad\0title" })
                Reject(() => WorkshopMetadata.Title(bad), "invalid title rejected before native submission");
            Check(WorkshopMetadata.Title(new string('中', 42)).Length == 42, "title limit is measured in UTF-8 bytes");
            Reject(() => WorkshopMetadata.Language(["--text-language", "zh-CN"]), "Web API language code rejected");
            Check(WorkshopMetadata.Language([]) == "english" && WorkshopMetadata.Language(["--text-language", "tchinese"]) == "tchinese", "official language codes and default");
            Reject(() => WorkshopMetadata.ChangeNote(["--change-note", "a", "--change-note-file", noteFile]), "ambiguous change notes rejected");
            Reject(() => WorkshopMetadata.ChangeNote(["--change-note", "bad\0note"]), "NUL note rejected");
            Check(WorkshopMetadata.ChangeNote(["--change-note", ""]) is null, "explicit empty note sends NULL");
            File.WriteAllText(noteFile, "");
            Check(WorkshopMetadata.ChangeNote(["--change-note-file", noteFile]) is null, "empty UTF-8 note file sends NULL");
            Reject(() => WorkshopMetadata.ExpectedPreviews(previews, [], [new(0, image)], [0]), "overlapping preview operations rejected");
            Reject(() => WorkshopMetadata.ExpectedPreviews(previews, [], [], [2]), "out-of-range preview rejected");
            Reject(() => WorkshopMetadata.ExpectedPreviews(previews, [], [], [0, 0]), "duplicate removals rejected");
            Reject(() => WorkshopMetadata.ExpectedPreviews([new(0, "video", "", 1)], [], [new(0, image)], []), "image replacement cannot overwrite a video");
            var dryEdit = await WorkshopPublishingService.EditAsync(context, ["steamworkshops", "edit", "--id", "123", "--title", "Dry title", "--dry-run"], CancellationToken.None);
            Check(dryEdit.Ok && SteamWorkshopsCommand.Json(dryEdit.Details).GetProperty("fields").GetProperty("description").GetBoolean() == false, "title-only dry-run does not sync description");
            void Reject(Action action, string message)
            {
                try { action(); } catch (ArgumentException) { Check(true, message); return; }
                throw new Exception("Expected rejection: " + message);
            }
        }
        finally
        {
            WorkshopPublishingService.PublishOverride = oldPublish; SteamworksSessionProbe.ProbeOverride = oldProbe;
            ProcessUtil.RimWorldRunningOverride = oldRunning; Environment.CurrentDirectory = oldDirectory;
        }
        return passed;
    }
    private static async Task<JsonElement> Capture(string[] args)
    {
        var original = Console.Out; using var writer = new StringWriter();
        try { Console.SetOut(writer); await CliApplication.RunAsync(args); }
        finally { Console.SetOut(original); }
        using var doc = JsonDocument.Parse(writer.ToString()); return doc.RootElement.Clone();
    }
}
