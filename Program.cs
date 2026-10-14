namespace untkers_mcserverator
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Eventing.Reader;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;

    internal class Program
    {
        private static readonly string _ServerDirectory = "server_path.txt"; //@"..\minecraftserver1-21-1-neo or else versions";
        private static readonly string _BatFile = "run.bat";
        private static readonly string _LogPath = "server_monitoring.log";

        static void Main()
        {
            try
            {
                Console.Title = "Untkers MCServerator";
                Log($"-! MCServerator started !-");

                Log("# Write please the path to your Minecraft server directory:");
                string serverDirectory = GetServerDirectory();

                long maxRamLimit = GetDynamicMaxRam(serverDirectory) / (1024L * 1024L);

                GetWindowsStability();

                Process? serverProcess = null;
                DateTime serverStartTime = DateTime.MinValue;

                while (true)
                {
                    Process? javaProcess = Process.GetProcessesByName("java")
                        .Concat(Process.GetProcessesByName("javaw"))
                        .OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0L; } })
                        .FirstOrDefault();

                    bool batIsRunning = Process.GetProcessesByName("cmd")
                        .Any(p => {
                            try { return p.MainWindowTitle.Contains("run.bat", StringComparison.OrdinalIgnoreCase); }
                            catch { return false; }
                        });

                    bool isRunning = (javaProcess != null && !javaProcess.HasExited) || batIsRunning;

                    if (isRunning && javaProcess != null)
                    {
                        try
                        {
                            javaProcess.Refresh();
                            long currentRAM = javaProcess.WorkingSet64 / (1024 * 1024); 

                            var uptime = DateTime.Now - serverStartTime;
                            Log($"|| Server is running. Uptime: {uptime:hh\\:mm\\:ss}, RAM Usage: {currentRAM} MB");

                            if (currentRAM >= maxRamLimit)
                            {
                                Log($"[507] RAM usage exceeded limit: {currentRAM} MB >= {maxRamLimit} MB. Restarting server...");
                                SoftRestart(serverProcess);
                                serverProcess = null;
                                continue;
                            }
                        }
                        catch { }
                    }

                    else if (!isRunning)
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
                                UseShellExecute = true
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

                    Thread.Sleep(10000);
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"[500] An unexpected error occurred: {ex.Message}");
                Console.WriteLine($"{ex.StackTrace}");
            }
            finally
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
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

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        static void GetWindowsStability()
        {
            //todo Linux port
            if (!OperatingSystem.IsWindows())
            {
                Log("[403] Windows stability check skipped: not running on Windows.");
                return;
            }

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

        static long GetDynamicMaxRam(string serverDir)
        {
            try
            {
                string[] filesToCheck = { "user_jvm_args.txt", "run.bat" };
                foreach (var file in filesToCheck)
                {
                    string path = Path.Combine(serverDir, file);
                    if (File.Exists(path))
                    {
                        string content = File.ReadAllText(path);
                        var match = Regex.Match(content, @"-Xmx(\d+)([gGmMkKbB])");
                        if (match.Success)
                        {
                            long value = long.Parse(match.Groups[1].Value);
                            string unit = match.Groups[2].Value.ToUpper();

                            long multiplier = 1;
                            if (unit == "G") multiplier = 1024L * 1024L * 1024L;
                            else if (unit == "M") multiplier = 1024L * 1024L;
                            else if (unit == "K") multiplier = 1024L;

                            long totalXmx = value * multiplier;

                            long safetyBuffer = 500L * 1024L * 1024L;
                            long calculatedLimit = totalXmx - safetyBuffer;

                            if (calculatedLimit > 0)
                            {
                                long limitMb = calculatedLimit / (1024L * 1024L);
                                Log($"[200] Successfully read limit from {file}: -Xmx{value}{unit}. Limit set to {limitMb} MB.");
                                return calculatedLimit;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[403] Error while reading memory parameter: {ex.Message}");
            }

            Log("[404] Parameter -Xmx not found, using default limit 9216 MB.");
            return 9216L * 1024L * 1024L;
        }

        static void SoftRestart(Process process)
        {
            try
            {
                Log("[202] Initiating restart...");
                process.StandardInput.WriteLine("stop");
                process.StandardInput.Flush();

                if (!process.WaitForExit(30000))
                {
                    Log("[504] Server did not respond in time, forcing termination...");
                    process.Kill();
                }
                else
                {
                    Log("[200] Restart completed successfully.");
                }
            } 
            catch (Exception ex) {
                Log($"[500] Error occurred during restart: {ex.Message}");
            }
        }
    }
}