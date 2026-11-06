using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.SetError(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true });
if (args.Length == 2 && args[0] is "--internal-update-helper" or "--internal-update-recover")
    return await RimDevTools.ToolUpdateTransaction.RunHelperAsync(args[1], args[0] == "--internal-update-recover");
if (args.Length == 2 && args[0] == "--official-asset-library-probe")
{
    RimDevTools.CliResult.Write(RimDevTools.OfficialAssetLibraryProbe.Inspect(args[1]));
    return 0;
}
if (args.Length == 1 && args[0] == "--steamworkshops-helper")
    return RimDevTools.SteamworksSessionProbe.RunHelper();
if (args.Length == 1 && args[0] == "--official-bake-helper")
    return RimDevTools.OfficialAssetBakeAdapter.RunHelper();
if (args.Length == 1 && args[0] == "--workshop-publish-helper")
    return RimDevTools.WorkshopPublisher.RunHelper();
if (args.Length > 0 && args[0] == "--steamworks-probe-helper")
    return RimDevTools.WorkshopCommand.RunProbeHelper(args);
return await RimDevTools.CliApplication.RunAsync(args);
