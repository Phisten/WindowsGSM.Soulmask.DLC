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

        // EchoPort 設定（必須與啟動指令的 -EchoPort 一致）
        private const int EchoPort = 18888;
        // SaveAndExit 倒數秒數，玩家會收到倒數通知
        private const int ShutdownCountdown = 60;

        private Dictionary<string, string> configData = new Dictionary<string, string>();

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
        // 透過 EchoPort (Telnet) 送出官方關服指令：
        //   1. SayToSystemChannel — 廣播關服通知給所有在線玩家
        //   2. SaveAndExit 60     — 存檔並在 60 秒後關閉，玩家畫面會顯示倒數
        //
        // 官方指令參考：https://saraserenity.net/soulmask/remote_console.php
        public async Task Stop(Process p)
        {
            await Task.Run(async () =>
            {
                bool telnetSuccess = await SendTelnetShutdown();

                if (!telnetSuccess)
                {
                    // Telnet 失敗（EchoPort 未開或連線失敗）時 fallback
                    // 直接等 WaitForExit，讓 WindowsGSM 在 timeout 後自行處理
                }

                try
                {
                    // 等待時間 = ShutdownCountdown + 存檔時間 + 緩衝
                    // 60 秒倒數 + 最多 30 秒存檔 = 最多等 90 秒
                    p.WaitForExit((ShutdownCountdown + 30) * 1000);
                }
                catch
                {
                    // process 已不存在，忽略
                }
            });
        }

        // 透過 Telnet 連接 EchoPort，送出廣播與關服指令
        // 回傳 true 代表指令成功送出
        private async Task<bool> SendTelnetShutdown()
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();

                // 嘗試連線，3 秒逾時
                var connectTask = client.ConnectAsync("127.0.0.1", EchoPort);
                if (await Task.WhenAny(connectTask, Task.Delay(3000)) != connectTask
                    || !client.Connected)
                {
                    return false;
                }

                var stream = client.GetStream();
                var writer = new System.IO.StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

                // 廣播關服通知，所有在線玩家都會收到
                await writer.WriteLineAsync($"SayToSystemChannel Server will restart in {ShutdownCountdown} seconds. Please find a safe location.");

                // 短暫等待確保廣播送達
                await Task.Delay(500);

                // 存檔並倒數關閉，玩家畫面會顯示倒數計時
                await writer.WriteLineAsync($"SaveAndExit {ShutdownCountdown}");

                return true;
            }
            catch
            {
                return false;
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