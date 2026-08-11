using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RimDevTools;

public sealed record IsolatedDesktopSelection(bool Enabled, string Source);

public static class IsolatedDesktopOptions
{
    public static IsolatedDesktopSelection? Resolve(ProjectConfig config, string mode, string[] args, out CliError? error)
    {
        error = null;
        var enable = CliArgs.Has(args, "--isolated-desktop");
        var disable = CliArgs.Has(args, "--no-isolated-desktop");
        if (enable && disable)
        {
            error = new CliError("ISOLATED_DESKTOP_OPTIONS_CONFLICT", "--isolated-desktop 与 --no-isolated-desktop 不能同时使用");
            return null;
        }
        if (enable) return new IsolatedDesktopSelection(true, "command_line");
        if (disable) return new IsolatedDesktopSelection(false, "command_line");
        if (mode.Equals("manual", StringComparison.OrdinalIgnoreCase))
            return new IsolatedDesktopSelection(false, "mode_default");
        return new IsolatedDesktopSelection(config.IsolatedDesktop, "project_config");
    }
}

public sealed class GameProcessLaunchException : Exception
{
    public GameProcessLaunchException(string code, string message, int? nativeError = null, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
        NativeError = nativeError;
    }

    public string Code { get; }
    public int? NativeError { get; }
}

public sealed class GameProcessSession : IDisposable
{
    private IntPtr _desktop;
    private bool _disposed;

    internal GameProcessSession(Process process, bool isolated, string? desktopName, IntPtr desktop)
    {
        Process = process;
        Isolated = isolated;
        DesktopName = desktopName;
        _desktop = desktop;
    }

    public Process Process { get; }
    public bool Isolated { get; }
    public string? DesktopName { get; }
    public int? CleanupError { get; private set; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Process.Dispose();
        if (_desktop == IntPtr.Zero) return;
        if (!GameProcessLauncher.CloseDesktop(_desktop)) CleanupError = Marshal.GetLastWin32Error();
        _desktop = IntPtr.Zero;
    }
}

public static class GameProcessLauncher
{
    internal static bool CloseDesktop(IntPtr desktop) => NativeMethods.CloseDesktop(desktop);

    public static GameProcessSession Start(string executable, string? workingDirectory, IEnumerable<string> arguments, bool isolated, string runId)
        => isolated
            ? StartIsolated(executable, workingDirectory, arguments, runId)
            : StartNormal(executable, workingDirectory, arguments);

    public static string? CurrentDesktopName()
    {
        var desktop = NativeMethods.GetThreadDesktop(NativeMethods.GetCurrentThreadId());
        if (desktop == IntPtr.Zero) return null;
        NativeMethods.GetUserObjectInformation(desktop, NativeMethods.UoiName, null, 0, out var needed);
        if (needed <= 2) return null;
        var value = new StringBuilder(needed / sizeof(char));
        return NativeMethods.GetUserObjectInformation(desktop, NativeMethods.UoiName, value, needed, out _)
            ? value.ToString()
            : null;
    }

    private static GameProcessSession StartNormal(string executable, string? workingDirectory, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        if (!process.Start())
        {
            process.Dispose();
            throw new GameProcessLaunchException("GAME_START_FAILED", "无法启动 RimWorld");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new GameProcessSession(process, false, null, IntPtr.Zero);
    }

    private static GameProcessSession StartIsolated(string executable, string? workingDirectory, IEnumerable<string> arguments, string runId)
    {
        if (!OperatingSystem.IsWindows())
            throw new GameProcessLaunchException("ISOLATED_DESKTOP_CREATE_FAILED", "隔离桌面仅支持 Windows");

        var suffix = new string(runId.Where(char.IsLetterOrDigit).Take(32).ToArray());
        if (suffix.Length == 0) suffix = Guid.NewGuid().ToString("N");
        var desktopName = $"RimDevTools.{Environment.ProcessId}.{suffix}";
        var desktop = NativeMethods.CreateDesktop(desktopName, IntPtr.Zero, IntPtr.Zero, 0, NativeMethods.DesktopAllAccess, IntPtr.Zero);
        if (desktop == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new GameProcessLaunchException("ISOLATED_DESKTOP_CREATE_FAILED", new Win32Exception(error).Message, error);
        }

        var startup = new NativeMethods.StartupInfo
        {
            Cb = Marshal.SizeOf<NativeMethods.StartupInfo>(),
            Desktop = desktopName
        };
        var commandLine = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments).Select(QuoteArgument)));
        NativeNullHandles? nullHandles = null;
        try
        {
            nullHandles = NativeNullHandles.Create();
            startup.Flags = NativeMethods.StartfUseStdHandles;
            startup.StdInput = nullHandles.Input;
            startup.StdOutput = nullHandles.Output;
            startup.StdError = nullHandles.Output;
            if (!NativeMethods.CreateProcess(
                    executable,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    0,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startup,
                    out var processInfo))
            {
                var error = Marshal.GetLastWin32Error();
                throw new GameProcessLaunchException("ISOLATED_GAME_START_FAILED", new Win32Exception(error).Message, error);
            }

            try
            {
                var process = Process.GetProcessById(unchecked((int)processInfo.ProcessId));
                return new GameProcessSession(process, true, desktopName, desktop);
            }
            finally
            {
                NativeMethods.CloseHandle(processInfo.Thread);
                NativeMethods.CloseHandle(processInfo.Process);
            }
        }
        catch
        {
            NativeMethods.CloseDesktop(desktop);
            throw;
        }
        finally
        {
            nullHandles?.Dispose();
        }
    }

    private static string QuoteArgument(string value)
    {
        if (value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"')) return value;
        var builder = new StringBuilder().Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                builder.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
                continue;
            }
            builder.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        return builder.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private static class NativeMethods
    {
        internal const int UoiName = 2;
        internal const uint DesktopAllAccess = 0x01FF;
        internal const int StartfUseStdHandles = 0x00000100;
        internal const uint GenericRead = 0x80000000;
        internal const uint GenericWrite = 0x40000000;
        internal const uint FileShareRead = 0x00000001;
        internal const uint FileShareWrite = 0x00000002;
        internal const uint OpenExisting = 3;
        internal const uint FileAttributeNormal = 0x00000080;
        internal static readonly IntPtr InvalidHandleValue = new(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct StartupInfo
        {
            public int Cb;
            public string? Reserved;
            public string? Desktop;
            public string? Title;
            public int X;
            public int Y;
            public int XSize;
            public int YSize;
            public int XCountChars;
            public int YCountChars;
            public int FillAttribute;
            public int Flags;
            public short ShowWindow;
            public short Reserved2;
            public IntPtr Reserved2Pointer;
            public IntPtr StdInput;
            public IntPtr StdOutput;
            public IntPtr StdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public uint ProcessId;
            public uint ThreadId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SecurityAttributes
        {
            public int Length;
            public IntPtr SecurityDescriptor;
            [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
        }

        [DllImport("user32.dll", EntryPoint = "CreateDesktopW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateDesktop(string desktop, IntPtr device, IntPtr devMode, int flags, uint desiredAccess, IntPtr securityAttributes);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetThreadDesktop(uint threadId);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", EntryPoint = "GetUserObjectInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder? info, int length, out int needed);

        [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateProcess(
            string? applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string? currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            ref SecurityAttributes securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);
    }

    private sealed class NativeNullHandles : IDisposable
    {
        private NativeNullHandles(IntPtr input, IntPtr output)
        {
            Input = input;
            Output = output;
        }

        public IntPtr Input { get; private set; }
        public IntPtr Output { get; private set; }

        public static NativeNullHandles Create()
        {
            var security = new NativeMethods.SecurityAttributes
            {
                Length = Marshal.SizeOf<NativeMethods.SecurityAttributes>(),
                InheritHandle = true
            };
            var output = NativeMethods.CreateFile(
                "NUL",
                NativeMethods.GenericWrite,
                NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                ref security,
                NativeMethods.OpenExisting,
                NativeMethods.FileAttributeNormal,
                IntPtr.Zero);
            if (output == NativeMethods.InvalidHandleValue)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建隔离进程空输出句柄");

            var input = NativeMethods.CreateFile(
                "NUL",
                NativeMethods.GenericRead,
                NativeMethods.FileShareRead | NativeMethods.FileShareWrite,
                ref security,
                NativeMethods.OpenExisting,
                NativeMethods.FileAttributeNormal,
                IntPtr.Zero);
            if (input == NativeMethods.InvalidHandleValue)
            {
                NativeMethods.CloseHandle(output);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建隔离进程空输入句柄");
            }
            return new NativeNullHandles(input, output);
        }

        public void Dispose()
        {
            if (Input != IntPtr.Zero && Input != NativeMethods.InvalidHandleValue) NativeMethods.CloseHandle(Input);
            if (Output != IntPtr.Zero && Output != NativeMethods.InvalidHandleValue) NativeMethods.CloseHandle(Output);
            Input = IntPtr.Zero;
            Output = IntPtr.Zero;
        }
    }
}
