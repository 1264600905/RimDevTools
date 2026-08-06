using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.SetError(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true });
return await RimDevTools.CliApplication.RunAsync(args);
