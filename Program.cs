using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace untkers_mcserverator
{
    internal class Program
    {
        private static readonly string _ServerDirectory = "server_path.txt";
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

                DateTime serverStartTime = DateTime.MinValue;
                bool wasRunningLastCheck = false;

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

                    TimeSpan maxUptimeLimit = TimeSpan.FromHours(4);

                    bool isRunning = (javaProcess != null && !javaProcess.HasExited) || batIsRunning;

                    if (isRunning && javaProcess != null)
                    {
                        if (!wasRunningLastCheck)
                        {
                            serverStartTime = DateTime.Now;
                            wasRunningLastCheck = true;
                        }

                        try
                        {
                            javaProcess.Refresh();
                            long currentRAM = javaProcess.WorkingSet64 / (1024 * 1024);

                            var uptime = DateTime.Now - serverStartTime;
                            Console.Title = $"Untkers MCServerator | Uptime: {uptime:hh\\:mm\\:ss} | RAM: {currentRAM} MB";

                            if (currentRAM >= maxRamLimit)
                            {
                                Log($"[507] RAM usage exceeded limit: {currentRAM} MB >= {maxRamLimit} MB. Restarting server...");
                                CreateBackup(serverDirectory);
                                SoftRestart(javaProcess);
                                wasRunningLastCheck = false;
                                Thread.Sleep(5000);
                                continue;
                            }

                            if (uptime > maxUptimeLimit)
                            {
                                Log($"[202] Scheduled restart: {uptime:hh\\:mm\\:ss} > {maxUptimeLimit:hh\\:mm\\:ss}. Restarting server...");
                                try
                                {
                                    CreateBackup(serverDirectory);
                                    SoftRestart(javaProcess);
                                }
                                catch (Exception ex)
                                {
                                    Log($"[500] Error during scheduled restart: {ex.Message}");
                                    try { javaProcess.Kill(); } catch { }
                                }
                                wasRunningLastCheck = false;
                                Thread.Sleep(5000);
                                continue;
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        if (wasRunningLastCheck)
                        {
                            Log("[503] Server process stopped or crashed.");
                            wasRunningLastCheck = false;
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
                            Process.Start(psi);
                            serverStartTime = DateTime.Now;
                            wasRunningLastCheck = true;
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
            catch (Exception ex)
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
                if (inputPath.Contains('"')) inputPath = inputPath.Trim('"');

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
                Log("[202] Initiating graceful restart via RCON...");

                SendRconCommand("say §cWarning! Server is restarting in 10 seconds!");
                Thread.Sleep(10000);

                SendRconCommand("save-all flush");
                Thread.Sleep(2000);

                SendRconCommand("stop");

                if (!process.WaitForExit(30000))
                {
                    Log("[504] Server did not stop via RCON in time, forcing termination...");
                    try { process.Kill(); } catch { }
                }
                else
                {
                    Log("[200] Restart and safe shutdown completed successfully.");
                }
            }
            catch (Exception ex)
            {
                Log($"[500] Error occurred during RCON restart: {ex.Message}");
                try { process.Kill(); } catch { }
            }
        }

        static void CreateBackup(string serverDir)
        {
            try
            {
                string worldDir = Path.Combine(serverDir, "world");
                if (!Directory.Exists(worldDir))
                {
                    Log($"[404] World directory not found: {worldDir}. Backup aborted.");
                    return;
                }

                string backupDir = Path.Combine(serverDir, "backups");
                Directory.CreateDirectory(backupDir);

                string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string backupPath = Path.Combine(backupDir, $"world_backup_{timestamp}.zip");

                ZipFile.CreateFromDirectory(worldDir, backupPath, CompressionLevel.Optimal, false);
                Log($"[200] World backup created successfully at {backupPath}");

                CleanOldBackups(backupDir, 5);
            }
            catch (Exception ex)
            {
                Log($"[500] Failed to create backup: {ex.Message}");
            }
        }

        static void CleanOldBackups(string backupDir, int keepCount)
        {
            try
            {
                var directoryInfo = new DirectoryInfo(backupDir);
                var zipFiles = directoryInfo.GetFiles("world_backup_*.zip")
                    .OrderByDescending(f => f.CreationTime)
                    .Skip(keepCount);

                foreach (var file in zipFiles)
                {
                    try
                    {
                        file.Delete();
                        Log($"[200] Deleted old backup: {file.FullName}");
                    }
                    catch (Exception ex)
                    {
                        Log($"[500] Failed to delete backup {file.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[500] Failed to clean old backups: {ex.Message}");
            }
        }

        static void SendRconCommand(string command)
        {
            string host = "26.215.193.197";
            int port = 25575;
            string password = "1206767";

            try
            {
                using (var client = new System.Net.Sockets.TcpClient())
                {
                    client.Connect(host, port);
                    using (var stream = client.GetStream())
                    {
                        if (!RconAuth(stream, password))
                        {
                            Log("[500] RCON Authentication failed. Check password.");
                            return;
                        }

                        RconExecute(stream, 2, command);
                        Log($"[200] RCON command sent successfully: {command}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[500] RCON connection error: {ex.Message}");
            }
        }

        static bool RconAuth(System.Net.Sockets.NetworkStream stream, string password)
        {
            int requestId = 1;
            int packetType = 3;
            byte[] payload = System.Text.Encoding.ASCII.GetBytes(password);

            SendRconPacket(stream, requestId, packetType, payload);
            var response = ReadRconPacket(stream);

            return response.Id == requestId;
        }

        static void RconExecute(System.Net.Sockets.NetworkStream stream, int requestId, string command)
        {
            byte[] payload = System.Text.Encoding.ASCII.GetBytes(command);
            SendRconPacket(stream, requestId, 2, payload);
            ReadRconPacket(stream);
        }

        static void SendRconPacket(System.Net.Sockets.NetworkStream stream, int id, int type, byte[] payload)
        {
            int length = payload.Length + 10;
            byte[] buffer = new byte[length + 4];

            BitConverter.GetBytes(length).CopyTo(buffer, 0);
            BitConverter.GetBytes(id).CopyTo(buffer, 4);
            BitConverter.GetBytes(type).CopyTo(buffer, 8);
            payload.CopyTo(buffer, 12);
            buffer[^2] = 0;
            buffer[^1] = 0;

            stream.Write(buffer, 0, buffer.Length);
        }

        static (int Id, int Type, string Body) ReadRconPacket(System.Net.Sockets.NetworkStream stream)
        {
            byte[] sizeBuffer = new byte[4];
            stream.ReadExactly(sizeBuffer, 0, 4);
            int length = BitConverter.ToInt32(sizeBuffer, 0);

            byte[] dataBuffer = new byte[length];
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = stream.Read(dataBuffer, totalRead, length - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            int id = BitConverter.ToInt32(dataBuffer, 0);
            int type = BitConverter.ToInt32(dataBuffer, 4);
            string body = System.Text.Encoding.ASCII.GetString(dataBuffer, 8, length - 10);

            return (id, type, body);
        }
    }
}