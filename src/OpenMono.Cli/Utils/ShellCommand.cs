using System.Diagnostics;

namespace OpenMono.Utils;

public static class ShellCommand
{
    public static ProcessStartInfo Create(
        string command,
        string? workingDirectory = null,
        bool redirectOutput = true)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        ConfigureShell(psi, command);

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        ApplyPortableEnvironment(psi);
        return psi;
    }

    public static string ResolveHome()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
            return home;

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
            return profile;

        return OperatingSystem.IsWindows() ? "C:\\" : "/root";
    }

    public static string WrapBackgroundCommand(string command, string logPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var quotedLogPath = QuotePowerShellLiteral(logPath);
            return $"& {{ {command} }} *>> {quotedLogPath}";
        }

        var escapedLogPath = logPath.Replace("'", "'\"'\"'", StringComparison.Ordinal);
        return $"exec >>'{escapedLogPath}' 2>&1; {command}";
    }

    public static string BackgroundFollowUpText(int pid, string logPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var quotedLogPath = logPath.Replace("'", "''", StringComparison.Ordinal);
            return
                "Follow-ups (run foreground):\n" +
                $"  Get-Content -Tail 50 -Path '{quotedLogPath}'\n" +
                $"  Stop-Process -Id {pid}\n";
        }

        return
            "Follow-ups (run foreground):\n" +
            $"  tail -n 50 {logPath}   # peek at output\n" +
            $"  tail -f {logPath}      # stream output (avoid in agent; use sleep+tail -n instead)\n" +
            $"  kill {pid}             # stop the process\n" +
            $"  kill -9 {pid}          # force-kill if the process won't stop\n";
    }

    private static void ConfigureShell(ProcessStartInfo psi, string command)
    {
        var shellOverride = Environment.GetEnvironmentVariable("OPENMONO_SHELL");
        if (!string.IsNullOrWhiteSpace(shellOverride))
        {
            psi.FileName = shellOverride;
            AddShellArguments(psi, shellOverride, command);
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            psi.FileName = "powershell.exe";
            AddPowerShellArguments(psi, command);
            return;
        }

        psi.FileName = "/bin/bash";
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
    }

    private static void AddShellArguments(ProcessStartInfo psi, string shell, string command)
    {
        var shellName = Path.GetFileNameWithoutExtension(shell).ToLowerInvariant();
        if (shellName is "powershell" or "pwsh")
        {
            AddPowerShellArguments(psi, command);
            return;
        }

        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(command);
    }

    private static void AddPowerShellArguments(ProcessStartInfo psi, string command)
    {
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);
    }

    private static void ApplyPortableEnvironment(ProcessStartInfo psi)
    {
        psi.Environment["HOME"] = ResolveHome();

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            psi.Environment["PATH"] = path;
        }
        else if (!OperatingSystem.IsWindows())
        {
            psi.Environment["PATH"] = "/usr/local/bin:/usr/bin:/bin";
        }
    }

    private static string QuotePowerShellLiteral(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
}
