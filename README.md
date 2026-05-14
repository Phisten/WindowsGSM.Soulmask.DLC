# WindowsGSM.Soulmask.DLC

🧩 WindowsGSM plugin that provides Soulmask Dedicated Server (DLC: Shifting Sands)

> Forked from [ohmcodes/WindowsGSM.Soulmask](https://github.com/ohmcodes/WindowsGSM.Soulmask)  
> Fixed: server rollback on restart — world data now saves correctly on shutdown

## PLEASE ⭐STAR⭐ THE REPO IF YOU LIKE IT! THANKS!

---

### WindowsGSM Installation

1. Download WindowsGSM from https://windowsgsm.com/
2. Create a folder where you want all servers to be installed and run
3. Drag `WindowsGSM.exe` into the folder and execute it

### Plugin Installation

1. Download the [latest release](https://github.com/Phisten/WindowsGSM.Soulmask.DLC/releases/latest)
2. Extract and move **Soulmask.cs** into the **plugins** folder
3. OR press the Puzzle Icon in the bottom-left and install by selecting the zip file
4. Click **[RELOAD PLUGINS]** or restart WindowsGSM
5. Navigate to "Servers" → "Install Game Server" → find **Soulmask dlc Shifting Sands Dedicated Server [Soulmask.cs]**

---

### Official Documentation

🗃️ https://soulmask.fandom.com/wiki/Private_Server

### The Game

🕹️ https://store.steampowered.com/app/2646460/Soulmask/

### Dedicated Server Info

🖥️ https://steamdb.info/app/3017310/info/

---

### Port Forwarding

| Port | Protocol | Description |
|------|----------|-------------|
| 8777 | UDP | Game port (default) |
| 27015 | UDP | Steam query port |
| 18888 | TCP | EchoPort / Telnet (optional, for manual console access) |

---

### Available Parameters

All core params are automatically set by WindowsGSM. These can be overridden via the "Additional" field in the server settings.

| Parameter | Description |
|-----------|-------------|
| `DLC_Level01_Main` | Map: Shifting Sands (DLC). Use `Level01_Main` for original map |
| `-SteamServerName=""` | Server name shown in browser |
| `-MaxPlayers=10` | Max player count |
| `-Port=8777` | Game port |
| `-QueryPort=27015` | Steam query port |
| `-EchoPort=18888` | Telnet maintenance port (optional, for manual console access) |
| `-pve` | Enable PvE mode. Replace with `-pvp` for PvP |
| `-PSW=""` | Server join password (optional) |
| `-adminpsw=""` | Admin/GM password |
| `-saving=120` | Auto-save interval in seconds (default: 300) |
| `-backup=600` | Backup interval in seconds (default: 900) |

---

### Shutdown / Rollback Fix

The original plugin sent shutdown signals via `stdin`, which caused **world data (world.db) to not be saved** on restart — only player data was stored, resulting in rollbacks.

This fork fixes the issue by sending **Ctrl+C directly to the server process window**, which triggers the UE5 engine's graceful shutdown sequence — saving all world data before the process exits.

The plugin waits up to 60 seconds for the process to fully exit before returning, ensuring no data loss.

---

### Shifting Sands DLC Notes

- Map parameter: `DLC_Level01_Main`
- To run both maps as a cluster, you need two separate server instances linked via `-serverid` and `-clientserverconnect`
- The Shifting Sands DLC was free to claim from April 10 to May 10, 2026

---

### Config Files

| File | Path | Description |
|------|------|-------------|
| `Engine.ini` | `WS\Saved\Config\WindowsServer\Engine.ini` | Port, server name, saving/backup intervals |
| `GameXishu.json` | `WS\Saved\GameplaySettings\GameXishu.json` | Gameplay coefficients (XP rate, drop rate, etc.) |
| `world.db` | `WS\Saved\Worlds\Dedicated\DLC_Level01_Main\world.db` | Complete world save — back this up regularly |

---

### Manual Save Command

If you need to force a save while the server is running, connect via Telnet:

```
telnet 127.0.0.1 18888
saveworld 10
```

Or use the in-game admin console:

```
gm BaoCun
```

---

### Support

- [Soulmask Discord](https://discord.gg/soulmask)
- [WindowsGSM Discord](https://discord.com/channels/590590698907107340/645730252672335893)

---

### License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
