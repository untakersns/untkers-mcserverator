namespace untkers_mcserverator
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Eventing.Reader;
    using System.IO;
    using System.Linq;
    using System.Threading;

    internal class Program
    {
        private static readonly string _ServerDirectory = "server_path.txt"; //@"..\minecraftserver1-21-1-neo";
        private static readonly string _BatFile = "run.bat";
        private static readonly string _LogPath = "server_monitoring.log";

        static void Main(string[] args)
        {
            Console.Title = "Untkers MCServerator";
            Log($"-! MCServerator started !-");

            Log("# Write please the path to your Minecraft server directory:");
            string serverDirectory = GetServerDirectory();

            GetWindowsStability();

            Process? serverProcess = null;
            DateTime serverStartTime = DateTime.MinValue;

            while (true)
            {
                bool isRunning = false;

                if (serverProcess != null)
                {
                    try
                    {
                        isRunning = !serverProcess.HasExited;
                    }
                    catch
                    {
                        isRunning = false;
                    }
                }

                if (!isRunning)
                {
                    if (serverProcess != null)
                    {
                        Log($"[503] Server process exited with code {GetSafeExitCode(serverProcess)}. Restarting...");
                    }

                    Log("||[202] Server starting...");
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = Path.Combine(serverDirectory, _BatFile),
                            WorkingDirectory = serverDirectory,
                            UseShellExecute = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        serverProcess = Process.Start(psi)!;
                        serverStartTime = DateTime.Now;
                        Log($"[201] Server started successfully in {serverStartTime:HH:mm:ss}.");
                    }
                    catch (Exception ex)
                    {
                        Log($"[500] Failed to start server: {ex.Message}");
                    }
                }
                else
                {
                    TimeSpan uptime = DateTime.Now - serverStartTime;
                    Log($"|| Server is running. Uptime: {uptime:hh\\:mm\\:ss}");
                }

                Thread.Sleep(10000);
            }
        }

        static void Log(string message)
        {
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            Console.WriteLine(logMessage);
            try
            {
                File.AppendAllText(_LogPath, logMessage + Environment.NewLine);
            }
            catch { }
        }

        static int GetSafeExitCode(Process process)
        {
            try
            {
                return process.ExitCode;
            }
            catch { return -1; }
        }

        static void GetWindowsStability()
        {
            try
            {
                string pathQuery = "*[System[(EventID=41 or EventID=6008) and TimeCreated[timediff(@SystemTime) <= 86400000]]]";

                var query = new EventLogQuery("System", PathType.LogName, pathQuery);
                using (var reader = new EventLogReader(query))
                {
                    EventRecord record = reader.ReadEvent();

                    if (record != null)
                    {
                        Log($"[500] Unexpected Windows crash at {record.TimeCreated}: {record.Id}");
                    }
                    else
                    {
                        Log("[200] No recent crashes detected.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[403] Failed to check Windows logs (check if program launched as Administrator): {ex.Message}");
            }
        }

        static string GetServerDirectory()
        {
            if (File.Exists(_ServerDirectory))
            {
                string path = File.ReadAllText(_ServerDirectory).Trim();
                if (Directory.Exists(path))
                {
                    return path;
                }
                else
                {
                    Log($"[404] The path in {_ServerDirectory} does not exist: {path}");
                }
            }
            while (true)
            {
                Console.Write("# Please enter the path to your Minecraft server directory: ");
                string inputPath = Console.ReadLine()?.Trim() ?? string.Empty;
                if(inputPath.Contains('"')) inputPath = inputPath.Trim('"');

                if (Directory.Exists(inputPath))
                {
                    File.WriteAllText(_ServerDirectory, inputPath);
                    Log("[200] Server directory saved successfully and saved in 'server_path.txt'.");
                    return inputPath;
                }
                else
                {
                    Log($"[404] The provided path does not exist: {inputPath}");
                }
            }
        }
    }
}