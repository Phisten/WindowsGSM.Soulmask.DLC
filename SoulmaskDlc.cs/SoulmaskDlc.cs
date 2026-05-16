using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using WindowsGSM.Functions;
using WindowsGSM.GameServer.Engine;
using WindowsGSM.GameServer.Query;

namespace WindowsGSM.Plugins
{
    public class SoulmaskDlc : SteamCMDAgent
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
        public SoulmaskDlc(ServerConfig serverData) : base(serverData) => base.serverData = _serverData = serverData;
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
        // 對伺服器視窗送 Ctrl+C 觸發 graceful shutdown
        public async Task Stop(Process p)
        {
            await Task.Run(() =>
            {
                Functions.ServerConsole.SetMainWindow(p.MainWindowHandle);
                Functions.ServerConsole.SendWaitToMainWindow("^c");

                try
                {
                    p.WaitForExit(60000);

                }
                catch
                {
                    // process 已不存在，忽略
                }
                    
                // 額外等待10秒防止重啟過快
                await Task.Delay(10000);
            });
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
