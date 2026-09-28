namespace Jellyfin.Plugin.AutoLut;

public static class WorkerRuntime
{
    public static void EnsureExecutable(string node)
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            throw new PlatformNotSupportedException("This preview bundles a Linux x64 worker only");
        // Repository installers may not preserve the executable bit from a ZIP.
        // Change only our bundled binary, owned by the Jellyfin process after GUI installation.
        var mode = File.GetUnixFileMode(node);
        if ((mode & UnixFileMode.UserExecute) == 0)
            File.SetUnixFileMode(node, mode | UnixFileMode.UserExecute);
    }
}
