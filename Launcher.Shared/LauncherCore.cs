﻿using ConsoleTables;
using System.Diagnostics;

namespace Launcher.Shared
{
    public enum ShortcutType
    {
        Executable,
        DiskLocation,
        URL,
        Verbatim
    }
    public record Shortcut(string Name, string Path)
    {
        #region Properties
        public ShortcutType Type => GetShortcutType(Path);
        public bool IsURL => Type == ShortcutType.URL;
        public bool IsExecutable => Type == ShortcutType.Executable;
        #endregion

        #region Helper
        public static ShortcutType GetShortcutType(string path)
        {
            if (path.StartsWith("!"))
                return ShortcutType.Verbatim;
            else if (path.StartsWith("http"))
                return ShortcutType.URL;
            else if (path.EndsWith(".exe"))
                return ShortcutType.Executable;
            else
                return ShortcutType.DiskLocation;
        }
        #endregion
    }
    public static class WindowsExplorerHelper
    {
        /// <param name="additionalArgs">Reserved for launching exes</param>
        public static void Launch(this string path, string[] additionalArgs = null, bool launchWithDefaultProgram = false)
        {
            // Verbatim commands
            if (path.StartsWith('!'))
            {
                path = path[1..];
                bool captureOutputs = false;
                if (path.StartsWith('?'))
                {
                    path = path[1..];
                    captureOutputs = true;
                }

                string programName = path.Split(' ').First(); // TODO: Handle with the case that there are spaces in the programName
                string arguments = path[programName.Length..];
                if (captureOutputs)
                    MonitorProcess(programName, arguments);
                else
                    Process.Start(programName, arguments);
                return;
            }

            if (!Directory.Exists(path) && !File.Exists(path) && !path.StartsWith("http"))
                throw new ArgumentException($"Invalid path: {path}");

            ShortcutType shortcutType = Shortcut.GetShortcutType(path);
            switch (shortcutType)
            {
                case ShortcutType.Executable:
                    // Launch exe
                    Process.Start(path, additionalArgs);
                    break;
                case ShortcutType.DiskLocation:
                    if (launchWithDefaultProgram)
                        // Open with default program
                        path.OpenWithDefaultProgram(additionalArgs);
                    else
                        // Open file/folder location
                        Process.Start(new ProcessStartInfo
                        {
                            Arguments = $"/select,\"{path}\"", // Explorer will treat everything after /select as a path, so no quotes is necessasry and in fact, we shouldn't use quotes otherwise explorer will not work
                            FileName = "explorer.exe"
                        });
                    break;
                case ShortcutType.URL:
                    // Open with browser
                    path.OpenWithDefaultProgram(additionalArgs);
                    break;
                default:
                    Console.WriteLine($"Unexpected shortcut type: {shortcutType}");
                    break;
            }
        }
        public static void OpenWithDefaultProgram(this string path, string[] additionalArgs)
        {
            new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = "explorer.exe",
                    Arguments = additionalArgs == null
                        ? $"\"{path}\""
                        : $"\"{path}\" {EscapeArguments(additionalArgs)}"
                }
            }.Start();

            string EscapeArguments(string[] arguments)
                => string.Join(" ", arguments.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument));
        }
        private static void MonitorProcess(string filename, string arguments)
        {
            Process process = new()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = filename,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true,
            };
            process.ErrorDataReceived += OutputDataReceived;
            process.OutputDataReceived += OutputDataReceived;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            static void OutputDataReceived(object sender, DataReceivedEventArgs e)
            {
                string message = e.Data;
                Console.WriteLine(message);
            }
        }
    }
    public static class LauncherCore
    {
        #region Routines
        public static void Launch(string name, string[] args, bool launchWithDefaultProgram)
        {
            Dictionary<string, Shortcut> configurations = ReadConfigurations();
            if (configurations.TryGetValue(name, out Shortcut shortcut))
            {
                try
                {
                    shortcut.Path.Launch(args, launchWithDefaultProgram);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
            else
            {
                Console.WriteLine($"Shortcut {name} is not defined.");
            }
        }
        public static Dictionary<string, Shortcut> ReadConfigurations()
        {
            return File.ReadLines(ConfigurationPath)
                .Where(line => !line.StartsWith('#') && !string.IsNullOrWhiteSpace(line))   // Skip comment and empty lines
                .Select(ParseShortcut)
                .ToDictionary(shortcut => shortcut.Name, shortcut => shortcut);
        }
        #endregion

        #region Helpers
        public static Shortcut ParseShortcut(string line)
        {
            int splitter = line.IndexOf(':');
            string name = line.Substring(0, splitter).Trim();
            string value = line.Substring(splitter + 1).Trim();
            return new Shortcut(name, value.Trim('\"'));
        }
        public static void PrintAsTable(IEnumerable<Shortcut> items)
        {
            ConsoleTable table = new("Name", "Type", "Path");
            foreach (Shortcut item in items)
                table.AddRow(item.Name, item.Type, item.Path);
            table.Write(Format.Minimal);
        }
        public static string ConfigurationFolder
        {
            get
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Launcher");
                Directory.CreateDirectory(path);
                return path;
            }
        }
        public static string ConfigurationPath
        {
            get
            {
                string path = Path.Combine(ConfigurationFolder, "Configurations.yaml"); ;
                if (!File.Exists(path))
                    File.WriteAllText(path, """
                        # Format: <Name>: <Path>
                        # Notes:
                        #   Use ! to start verbatim
                        #   Use !? to monitor process outputs

                        # Configurations
                        """);
                return path;
            }
        }
        #endregion
    }
}
