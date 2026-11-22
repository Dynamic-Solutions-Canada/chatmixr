using System.IO;
using System.Xml.Linq;
using ChatMixr.Models;
using Microsoft.Win32;

namespace ChatMixr.Services;

/// <summary>
/// The installed apps the chat app search offers, found the way Little Launcher finds apps: Start
/// menu shortcuts, the uninstall entries in the registry, and Microsoft Store packages. Each is
/// reduced to the program name its sound plays under (<see cref="AppSearch.ProgramOf"/>), since that
/// is what the chat list matches. Loading is slow (shortcuts are read through a COM object that needs
/// a single-threaded apartment), so it runs once on its own thread and is kept for the session.
/// </summary>
internal static class InstalledApps
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private static Task<IReadOnlyList<InstalledApp>>? _loading;

    /// <summary>Every installed app, loaded on first use. Never throws; a source that fails is skipped.</summary>
    public static Task<IReadOnlyList<InstalledApp>> GetAsync() => _loading ??= LoadOnStaThread();

    private static Task<IReadOnlyList<InstalledApp>> LoadOnStaThread()
    {
        var done = new TaskCompletionSource<IReadOnlyList<InstalledApp>>();
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(Load());
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Listing installed apps failed");
                done.SetResult([]);
            }
        })
        { IsBackground = true, Name = "ChatMixr app list" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    /// <summary>All three sources, one entry per program (the first name found wins), sorted by name.</summary>
    private static IReadOnlyList<InstalledApp> Load()
    {
        var byProgram = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string? program)
        {
            if (program == null || string.IsNullOrWhiteSpace(name) || AppSearch.IsNotAnApp(name, program))
            {
                return;
            }

            byProgram.TryAdd(program, new InstalledApp(name.Trim(), program));
        }

        foreach (var (name, program) in FromStartMenu())
        {
            Add(name, program);
        }

        foreach (var (name, program) in FromUninstallEntries())
        {
            Add(name, program);
        }

        foreach (var (name, program) in FromPackages())
        {
            Add(name, program);
        }

        Logger.Info("Found {Count} installed apps for the chat app search", byProgram.Count);
        return byProgram.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Start menu shortcuts (all users and this user) to a program.</summary>
    private static IEnumerable<(string Name, string? Program)> FromStartMenu()
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType == null)
        {
            yield break;
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        ];

        foreach (string folder in folders.Where(Directory.Exists))
        {
            foreach (string link in SafeEnumerate(folder, "*.lnk"))
            {
                string? program = null;
                try
                {
                    dynamic shortcut = shell.CreateShortcut(link);
                    program = AppSearch.ProgramOf((string)shortcut.TargetPath, (string?)shortcut.Arguments);
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
                {
                    // An unreadable shortcut is skipped.
                }

                yield return (Path.GetFileNameWithoutExtension(link), program);
            }
        }
    }

    /// <summary>
    /// The registry's uninstall entries whose icon is the app's own program, which for most desktop
    /// apps is the program that plays their sound.
    /// </summary>
    private static IEnumerable<(string Name, string? Program)> FromUninstallEntries()
    {
        const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        const string Uninstall32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
        var results = new List<(string, string?)>();

        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (string path in new[] { Uninstall, Uninstall32 })
            {
                using var key = root.OpenSubKey(path);
                if (key == null)
                {
                    continue;
                }

                foreach (string sub in key.GetSubKeyNames())
                {
                    using var entry = key.OpenSubKey(sub);
                    if (entry?.GetValue("DisplayName") is not string name || entry.GetValue("SystemComponent") is 1)
                    {
                        continue;
                    }

                    // DisplayIcon is "C:\path\app.exe" or "C:\path\app.exe,0".
                    string? icon = (entry.GetValue("DisplayIcon") as string)?.Split(',')[0].Trim('"', ' ');
                    results.Add((name, icon == null ? null : AppSearch.ProgramOf(icon, null)));
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Microsoft Store and other packaged apps (the new Teams, WhatsApp): each application's program
    /// from its package manifest.
    /// </summary>
    private static IEnumerable<(string Name, string? Program)> FromPackages()
    {
        var results = new List<(string, string?)>();
        try
        {
            var manager = new global::Windows.Management.Deployment.PackageManager();
            foreach (var package in manager.FindPackagesForUser(string.Empty))
            {
                if (package.IsFramework || package.IsResourcePackage)
                {
                    continue;
                }

                string manifest = Path.Combine(package.InstalledPath, "AppxManifest.xml");
                foreach (string executable in ExecutablesIn(manifest))
                {
                    results.Add((package.DisplayName, AppSearch.ProgramOf(executable, null)));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Info(ex, "Packaged apps unavailable for the chat app search");
        }

        return results;
    }

    /// <summary>The Executable of each Application in a package manifest; none if it can't be read.</summary>
    private static IEnumerable<string> ExecutablesIn(string manifestPath)
    {
        try
        {
            var document = XDocument.Load(manifestPath);
            return document.Descendants()
                .Where(e => e.Name.LocalName == "Application")
                .Select(e => (string?)e.Attribute("Executable"))
                .OfType<string>()
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return [];
        }
    }

    /// <summary>Files under a folder, skipping subfolders that can't be read.</summary>
    private static IEnumerable<string> SafeEnumerate(string folder, string pattern)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(folder, pattern, options);
    }
}
