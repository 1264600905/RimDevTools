using RimDevTools;

internal static class WorkshopChangeLogTests
{
    internal static int Run()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1700000100);
        var page = WorkshopChangeLog.Parse("123", "english", 1,
            "<title>Steam Community :: Unit :: Change Notes</title><div class=\"detailBox workshopAnnouncement noFooter changeLogCtn\"><p id=\"1700000100\">Fixes &amp; safety.<br><br>中文</p></div>", now);
        var baseline = new WorkshopChangeLogBaseline(1, "C:\\project", "op", "123", "Fixes & safety.\n中文", now, now, "accepted", [], true);
        Check(page.Status == "available" && page.Entries.Single().RecordId == "1700000100" && page.Entries.Single().Text == "Fixes & safety. 中文");
        Check(WorkshopChangeLog.Verify(page, baseline.Text, baseline).Status == "passed");
        Check(WorkshopChangeLog.Verify(page, baseline.Text, baseline with { PreviousRecordIds = ["1700000100"] }).Status == "pending");
        Check(WorkshopChangeLog.Verify(page, baseline.Text, baseline with { SubmissionStarted = false }).Status == "unknown");
        Check(WorkshopChangeLog.Verify(page, "old text", baseline).Status == "mismatch");
        Check(WorkshopChangeLog.Verify(page, null).Status == "skipped_empty");
        Check(WorkshopChangeLog.Parse("123", "english", 1, "<div class=\"error_ctn\">private</div>", now).Status == "unavailable");
        Check(WorkshopChangeLog.Parse("123", "english", 1, "<title>Steam Community :: Unit :: Change Notes</title>", now).Entries.Length == 0);
        Check(CliApplication.CommandPath(["steamworkshops", "changelog", "--verify"]) == "steamworkshops changelog");
        return 9;

        static void Check(bool success)
        {
            if (!success) throw new Exception("FAIL Workshop change log verification");
        }
    }
}
