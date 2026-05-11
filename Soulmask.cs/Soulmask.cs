using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Net;
using System.Threading.Tasks;
using System.Net.Sockets;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;
using WindowsGSM.GameServer.Query;
using System.Collections.Generic;

namespace WindowsGSM.Plugins
{
    public class Soulmask : SteamCMDAgent
    {
        // - Plugin Details
        public Plugin Plugin = new Plugin
        {
            name = "WindowsGSM.Soulmask.DLC",
            author = "Phisten",
            description = "WindowsGSM plugin for supporting Soulmask Dedicated Server",
            version = "1.1",
            url = "https://github.com/Phisten/WindowsGSM.Soulmask.DLC",
            color = "#1E8449"
        };

        // - Standard Constructor and properties
        public Soulmask(ServerConfig serverData) : base(serverData) => base.serverData = _serverData = serverData;
        private readonly ServerConfig _serverData;
        public string Error, Notice;

        // - Settings properties for SteamCMD installer
        public override bool loginAnonymous => true;
        public override string AppId => "3017310";

        // - Game server Fixed variables
        public override string StartPath => @"WS\Binaries\Win64\WSServer-Win64-Shipping.exe";
        public string FullName = "Soulmask Dedicated Server";
        public bool AllowsEmbedConsole = true;
        public int PortIncrements = 1;
        public object QueryMethod = new A2S();

        // - Game server default values
        public string ServerName = "Soulmask dlc Shifting Sands";
        public string Defaultmap = "DLC_Level01_Main";
        public string Maxplayers = "10";
        public string Port = "8777";
        public string QueryPort = "27015";
        public string Additional = "-UTF8Output -forcepassthrough -server -log -EchoPort=18888 -pve -saving=300 -backup=900";

        // SaveAndExit 倒數秒數，玩家會收到倒數通知
        private const int ShutdownCountdown = 10;

        // Start 時快取 EchoPort（Stop 呼叫時 WindowsGSM 不傳 ServerConfig）
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, int> _echoPortCache
            = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();

        private static int ParseEchoPort(string param)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                param ?? "",
                @"-EchoPort=(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out int port) ? port : 18888;
        }

        private static int GetEchoPort(Process p)
            => p != null && _echoPortCache.TryGetValue(p.Id, out int cached) ? cached : 18888;

        // - Create a default cfg for the game server after installation
        public void CreateServerCFG() { }

        // - Start server function, return its Process to WindowsGSM
        public async Task<Process> Start()
        {
            string shipExePath = Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath);
            if (!File.Exists(shipExePath))
            {
                Error = $"{Path.GetFileName(shipExePath)} not found ({shipExePath})";
                return null;
            }

            string param = $"{_serverData.ServerMap}";
            param += $" -SteamServerName=\"{_serverData.ServerName}\"";
            param += $" -MaxPlayers={_serverData.ServerMaxPlayer}";
            param += $" -Port={_serverData.ServerPort}";
            param += $" -QueryPort={_serverData.ServerQueryPort}";
            param += $" {_serverData.ServerParam}";

            var p = new Process
            {
                StartInfo =
                {
                    WorkingDirectory = ServerPath.GetServersServerFiles(_serverData.ServerID),
                    FileName = shipExePath,
                    Arguments = param,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            if (AllowsEmbedConsole)
            {
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                var serverConsole = new ServerConsole(_serverData.ServerID);
                p.OutputDataReceived += serverConsole.AddOutput;
                p.ErrorDataReceived += serverConsole.AddOutput;
            }

            try
            {
                p.Start();

                // 快取此伺服器的 EchoPort，供 Stop 使用
                _echoPortCache[p.Id] = ParseEchoPort(_serverData?.ServerParam ?? "");

                if (AllowsEmbedConsole)
                {
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }

                return p;
            }
            catch (Exception e)
            {
                Error = e.Message;
                return null;
            }
        }

        // - Stop server function
        //
        // 透過 EchoPort (Telnet) 送出 SaveAndExit，伺服器存檔後倒數關閉
        // 官方指令參考：https://saraserenity.net/soulmask/remote_console.php
        public async Task Stop(Process p)
        {
            await Task.Run(async () =>
            {
                bool telnetSuccess = await SendTelnetShutdown(p);

                if (!telnetSuccess)
                {
                    // Telnet 失敗（EchoPort 未開或連線失敗）時 fallback
                    // 直接等 WaitForExit，讓 WindowsGSM 在 timeout 後自行處理
                }

                try
                {
                    // ShutdownCountdown 倒數 + 最多 30 秒存檔，共等 40 秒
                    p.WaitForExit((ShutdownCountdown + 30) * 1000);
                }
                catch
                {
                    // process 已不存在，忽略
                }
            });
        }

        // 透過 Telnet 連接 EchoPort，送出關服指令
        // 在 Task.Run 內執行，使用同步 Connect 避免 async 計時問題
        // 回傳 true 代表指令成功送出
        private async Task<bool> SendTelnetShutdown(Process p)
        {
            try
            {
                using var tcp = new System.Net.Sockets.TcpClient();
                tcp.Connect("127.0.0.1", GetEchoPort(p));

                var stream = tcp.GetStream();
                await NegotiateTelnetAsync(stream, millisecondsWait: 500);

                byte[] cmd = Encoding.ASCII.GetBytes($"SaveAndExit {ShutdownCountdown}\r\n");
                await stream.WriteAsync(cmd, 0, cmd.Length);

                await Task.Delay(1000);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // 處理 Telnet IAC 協商：WILL → DONT、DO → WONT
        // 伺服器完成協商後才會接受後續的文字命令
        private static async Task NegotiateTelnetAsync(NetworkStream stream, int millisecondsWait)
        {
            const byte IAC = 255, DONT = 254, DO = 253, WONT = 252, WILL = 251;
            var buf = new byte[256];
            var deadline = Task.Delay(millisecondsWait);

            while (!deadline.IsCompleted)
            {
                if (!stream.DataAvailable)
                {
                    await Task.Delay(20);
                    continue;
                }

                int n = await stream.ReadAsync(buf, 0, buf.Length);
                int i = 0;
                while (i < n)
                {
                    if (buf[i] == IAC && i + 2 < n)
                    {
                        byte verb = buf[i + 1];
                        byte opt  = buf[i + 2];
                        byte[] reply = verb == WILL ? new[] { IAC, DONT, opt }
                                     : verb == DO   ? new[] { IAC, WONT, opt }
                                     : null;
                        if (reply != null)
                            await stream.WriteAsync(reply, 0, reply.Length);
                        i += 3;
                    }
                    else i++;
                }
            }
        }

        // - Update server function
        public async Task<Process> Update(bool validate = false, string custom = null)
        {
            var (p, pid) = await Installer.SteamCMD.UpdateEx(
                _serverData.ServerID,
                AppId,
                validate,
                loginAnonymous: loginAnonymous
            );
            await Task.Run(() => { p.WaitForExit(); });

            return p;
        }

        public bool IsInstallValid()
        {
            return File.Exists(Functions.ServerPath.GetServersServerFiles(_serverData.ServerID, StartPath));
        }

        public bool IsImportValid(string path)
        {
            string exePath = Path.Combine(path, "PackageInfo.bin");
            Error = $"Invalid Path! Fail to find {Path.GetFileName(exePath)}";

            return File.Exists(exePath);
        }

        public string GetLocalBuild()
        {
            var steamCMD = new Installer.SteamCMD();

            return steamCMD.GetLocalBuild(_serverData.ServerID, AppId);
        }

        public async Task<string> GetRemoteBuild()
        {
            var steamCMD = new Installer.SteamCMD();

            return await steamCMD.GetRemoteBuild(AppId);
        }
    }
}