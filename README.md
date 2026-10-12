# sshocked

[![CI](https://github.com/ms-sk/sshocked/actions/workflows/ci.yml/badge.svg)](https://github.com/ms-sk/sshocked/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/ms-sk/sshocked?logo=github)](https://github.com/ms-sk/sshocked/releases)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)

**sshocked** – Shelling into servers and containers with lightning speed.

A .NET console application that provides a fast, interactive terminal UI for managing and connecting to SSH servers and containers. Organize hosts into groups, import from your OpenSSH config, and launch SSH sessions — all from a single, keyboard-driven interface.

---

## Features

- **Server Management** — Add, edit, and delete SSH hosts with aliases, addresses, ports, and authentication methods
- **Group Organization** — Group servers by project, environment, or any category; connect to all servers in a group at once
- **SSH Config Import** — Parse your existing `~/.ssh/config` and import hosts automatically
- **Multiple Auth Methods** — SSH key (with optional identity file), password, SSH agent, or custom SSH options
- **Interactive TUI** — Keyboard-driven terminal UI with sub-menu navigation and a server overview table
- **Native AOT** — Published as a native binary for fast startup and zero runtime dependencies
- **Favorites and history** — Favorite hosts/containers, recent targets, and a structured audit CLI
- **SSH config synchronization** — Guarded import/export, backups, conflict resolution, and optional monitoring
- **File transfers** — SCP upload/download, remote browser, and per-host group results
- **Azure connections and secrets** — Native Bastion or managed TCP tunnels, named Azure Key Vault providers, and temporary SSH credentials
- **Connection hooks** — Global/per-host local actions and remote shell setup/cleanup
- **Session output recordings** — Opt-in timed cast files with replay, export, compression, and retention

See [the implementation guide](docs/ISSUE_IMPLEMENTATION.md) for the new menus, CLI examples, validation, and platform limitations. Detailed guides cover [SSH keys/config sync](docs/FEATURES.md), [hooks](docs/CONNECTION_HOOKS.md), [recordings](docs/SESSION_RECORDING.md), and [display settings](docs/VISUALS.md).

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Runtime | .NET 10 |
| UI Framework | [Spectre.Console](https://spectreconsole.net/) |
| DI | `Microsoft.Extensions.DependencyInjection` |
| Logging | `Microsoft.Extensions.Logging` (file-based, no console output) |
| Serialization | `System.Text.Json` (source-generated) |
| Publishing | Native AOT (`PublishAot`) |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for development/building from source)
- [OpenSSH Client](https://learn.microsoft.com/en-us/windows-server/administration/openssh/openssh_install_firstuse) (for SSH connections)

## Quickstart

### One-Line Install

**Linux & macOS:**
```bash
curl -fsSL https://github.com/ms-sk/sshocked/releases/latest/download/install.sh | sudo sh
```

**Windows (PowerShell as Administrator):**
```powershell
irm https://github.com/ms-sk/sshocked/releases/latest/download/install.ps1 | iex
```

This automatically detects your OS and architecture, downloads the correct binary, and installs it to `/usr/local/bin/ssk` (Linux/macOS) or `%ProgramFiles%\sshocked\ssk.exe` (Windows, added to PATH).

### Install via .deb Package (Debian/Ubuntu)

Download the `.deb` for your architecture from the [Releases page](https://github.com/ms-sk/sshocked/releases) and install:

```bash
sudo apt install ./sshocked-linux-x64.deb
# or
sudo dpkg -i sshocked-linux-x64.deb
```

### Manual Install from Release

1. Download the latest archive for your platform from the [Releases page](https://github.com/ms-sk/sshocked/releases)
2. Extract the binary and add it to your `PATH`

### Build from Source

```bash
# Clone the repository
git clone https://github.com/ms-sk/sshocked.git
cd sshocked

# Restore dependencies
dotnet restore

# Build
dotnet build --configuration Release

# Run
dotnet run --project src/Sshocked/Sshocked.csproj
```

### Publish Native Binary

```bash
dotnet publish src/Sshocked/Sshocked.csproj \
  -c Release \
  -r <RID> \
  --self-contained true \
  -o ./publish
```

Replace `<RID>` with your platform runtime identifier (e.g., `linux-x64`, `win-x64`, `osx-x64`, `linux-arm64`).

## Usage

Launch the application:

```bash
sshocked
# or the shorter alias:
ssk
```

### CLI Arguments

`sshocked` (or `ssk`) supports command-line arguments for direct SSH connections and non-interactive usage:

| Command | Description |
|---------|-------------|
| `ssk` | Launch the interactive TUI menu |
| `ssk <alias>` | Connect directly to a server by alias |
| `ssk <hostname>` | Connect directly to a server by hostname |
| `ssk <group>` | Connect to all servers in a group |
| `ssk --list, -l` | List all configured servers and groups (pipe-friendly) |
| `ssk --help, -h` | Show usage guide |
| `ssk --version, -v` | Show version information |

**Examples:**

```bash
# Direct connection to a server
ssk prod-db-01

# Connect to all servers in a group
ssk production

# Non-interactive listing (great for scripting)
ssk --list | grep prod
```

### Interactive TUI

When launched without arguments, the main menu displays a table of all configured servers grouped by category. Navigate using the keyboard:

- **Arrow keys** — Move selection
- **Enter** — Select a server or action
- **`[C] Connect`** - Connect to a server / docker container
- **`[G] Connect Group`** - Connect to a server / docker
- **`[X] Run command`** - Run command on the groups
- **`[S] Servers`** - CRUD Servers
- **`[R] Groups`** - CRUD Groups 
- **`[B] Back`** — Return to the previous menu
- **`[E] Exit`** — Exit the application

### Commands

| Action | Description |
|--------|-------------|
| **Connect** | Launch an interactive SSH session to the selected server |
| **Add Server** | Add a new SSH host (alias, host, port, user, auth method) |
| **Edit Server** | Modify an existing server's configuration |
| **Delete Server** | Remove a server from the configuration |
| **Create Group** | Create a new server group |
| **Import SSH Config** | Parse `~/.ssh/config` and import matching hosts |

See [SKILLS.md](.github/skills/SKILLS.md) for the full command reference and details on adding new commands.

## Configuration

Configuration is stored in a local JSON file:

| Platform | Path |
|----------|------|
| Windows | `%APPDATA%\sshocked\appconfig.json` |
| Linux/macOS | `~/.config/sshocked/appconfig.json` |

The file is auto-created on first run. Logs are written to `logs/sshocked.log` in the same directory (rotated at 5 MB, up to 3 backups).

## Project Structure

```
Sshocked.slnx              -- Solution file (.slnx format)
src/Sshocked/
├── Program.cs             -- Entry point with DI bootstrapping
├── Sshocked.csproj        -- Project file (net10.0, AOT-ready)
├── Models/                -- Domain models (AppConfig, ServerHost, ServerGroup)
├── Interfaces/            -- Core interfaces (IConfigRepository, ISshConfigImporter, IAppRunner, ...)
└── Services/              -- Implementations (JsonConfigRepository, AppRunner, ...)
```

## License

This project is licensed under the **GNU General Public License v3.0** — see the [LICENSE](LICENSE) file for details.

### Third-Party Dependencies

This project uses the following third-party libraries. See the [NOTICE](NOTICE) file for full attribution and license details.

| Dependency | License |
|---|---|
| Spectre.Console | MIT |
| SixLabors.ImageSharp | Apache 2.0 / Six Labors Split License |
| Microsoft.Extensions.* | MIT |
