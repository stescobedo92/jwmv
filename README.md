# jwmv — Java Version Manager for Windows

A fast, lightweight CLI tool to install, manage, and switch between multiple Java (JDK) versions on Windows. Built with .NET 8, inspired by tools like [SDKMAN!](https://sdkman.io/) and [nvm](https://github.com/nvm-sh/nvm).

[![CI](https://github.com/stescobedo92/jwmv/actions/workflows/ci.yml/badge.svg)](https://github.com/stescobedo92/jwmv/actions/workflows/ci.yml)

---

## Features

- **Install any JDK** — Temurin, Zulu, GraalVM, Microsoft, Corretto, Liberica, SAP, Oracle and more via [Foojay Disco API](https://api.foojay.io/)
- **Switch versions instantly** — per-session, per-project (`.jwmvrc`), or set a persistent default
- **Shell integration** — PowerShell profile bootstrap for automatic version switching
- **Self-update** — update jwmv itself from GitHub Releases
- **Diagnostics** — `doctor` command detects PATH conflicts and misconfigurations
- **Single-file executable** — no runtime dependencies, just download and run
- **Windows x64 & ARM64** support

---

## Installation

### Download from GitHub Releases

```powershell
# Download the latest release for your architecture
# https://github.com/stescobedo92/jwmv/releases

# Extract and place jwmv.exe somewhere in your PATH, for example:
Expand-Archive jwmv-win-x64.zip -DestinationPath "$HOME\.jwmv\bin"

# Add to PATH (run once)
[Environment]::SetEnvironmentVariable(
    "Path",
    "$HOME\.jwmv\bin;" + [Environment]::GetEnvironmentVariable("Path", "User"),
    "User"
)
```

### Build from source

```powershell
git clone https://github.com/stescobedo92/jwmv.git
cd jwmv
dotnet publish src/Jwmv.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
# Binary at: src/Jwmv.Cli/bin/Release/net8.0/win-x64/publish/jwmv.exe
```

---

## Quick Start

```powershell
# See what's available
jwmv list

# Install Java 21 (Temurin) and set it as default
jwmv install 21-tem --default

# Verify
jwmv current
java -version

# Install another version
jwmv install 17-zulu

# Switch for this session
jwmv use 17-zulu
```

---

## Commands

### `jwmv list [filter]`

List available JDK distributions from the Foojay catalog.

```powershell
# List all available JDKs
jwmv list

# Filter by major version
jwmv list 21

# Filter by distribution
jwmv list tem

# Filter by version + distribution
jwmv list 17-zulu

# Force catalog refresh
jwmv list --refresh
```

**Aliases:** `ls`

| Option          | Description                        |
|-----------------|------------------------------------|
| `[filter]`      | Optional version/distribution filter |
| `-r, --refresh` | Force refresh the catalog cache    |

---

### `jwmv install [identifier]`

Install a JDK version. Runs interactively if no identifier is provided.

```powershell
# Interactive mode — prompts for filter and selection
jwmv install

# Install a specific version
jwmv install 21-tem

# Install and set as the default JAVA_HOME
jwmv install 21.0.4-tem --default

# Install with forced catalog refresh
jwmv install 17-zulu --refresh
```

| Option          | Description                                |
|-----------------|--------------------------------------------|
| `[identifier]`  | Version identifier (e.g. `21-tem`, `17-zulu`) |
| `-d, --default` | Set as default JAVA_HOME after install     |
| `-r, --refresh` | Force refresh the catalog before install   |

**Identifier format:** `<version>-<distribution>` where distribution is a short alias:

| Alias     | Distribution        |
|-----------|---------------------|
| `tem`     | Eclipse Temurin     |
| `zulu`    | Azul Zulu           |
| `ms`      | Microsoft OpenJDK   |
| `graalvm` | GraalVM Community   |
| `cor`     | Amazon Corretto     |
| `lib`     | Liberica            |
| `sap`     | SAP Machine         |
| `ojdk`    | Oracle OpenJDK      |
| `oracle`  | Oracle JDK          |

---

### `jwmv uninstall [identifier]`

Remove an installed JDK version.

```powershell
# Interactive selection
jwmv uninstall

# Remove a specific version
jwmv uninstall 17-zulu
```

**Aliases:** `remove`, `delete`, `rm`

---

### `jwmv installed`

List all locally installed JDK versions.

```powershell
jwmv installed
```

**Aliases:** `local`

---

### `jwmv use <identifier>`

Activate a JDK for the current shell session. Does **not** modify the persistent default.

```powershell
# Switch to Java 17 for this session
jwmv use 17-zulu

# Specify shell explicitly
jwmv use 21-tem --shell powershell
```

> **Note:** When running as an executable, `use` emits a PowerShell script to stdout. For seamless switching, set up [shell integration](#shell-integration).

| Option           | Description                   |
|------------------|-------------------------------|
| `<identifier>`   | Version to activate (required) |
| `--shell <SHELL>` | Target shell (default: powershell) |

---

### `jwmv default <identifier>`

Set the persistent default JAVA_HOME for all new shell sessions.

```powershell
# Set Java 21 Temurin as the system-wide default
jwmv default 21-tem
```

This updates the Windows **User** environment variables (`JAVA_HOME`, `PATH`) and broadcasts the change so new terminals pick it up immediately.

---

### `jwmv current`

Show the currently active Java version and how it was resolved.

```powershell
jwmv current
```

Output shows:
- Active version alias
- Resolution source: **Default**, **Session**, or **Project**
- Resolved `JAVA_HOME` and `bin` paths
- Project `.jwmvrc` path (if applicable)

---

### `jwmv home [identifier]`

Print the `JAVA_HOME` path for a version. Useful for scripting.

```powershell
# Current JAVA_HOME
jwmv home

# JAVA_HOME for a specific version
jwmv home 17-zulu

# Use in scripts
$env:JAVA_HOME = $(jwmv home 21-tem)
```

---

### `jwmv upgrade [identifier]`

Upgrade installed JDK(s) to the latest patch in the same major/vendor track.

```powershell
# Upgrade a specific installation
jwmv upgrade 21-tem

# Upgrade all installed versions
jwmv upgrade
```

---

### `jwmv update`

Refresh the local catalog cache from the Foojay API.

```powershell
jwmv update
```

The catalog is cached locally and auto-refreshes every 6 hours (configurable). Use this to force an immediate refresh.

---

### `jwmv doctor`

Run diagnostics to detect common issues.

```powershell
jwmv doctor
```

Checks for:
- `JAVA_HOME` correctness
- `PATH` conflicts (e.g. system Java taking precedence)
- PowerShell profile integration status
- `java.exe` resolution via `where.exe`

---

### `jwmv config`

Display the current jwmv configuration.

```powershell
jwmv config
```

Shows: root directory, config file path, preferred distribution, catalog refresh interval, auto-env setting, default shell, and self-update repository.

---

### `jwmv integrate`

Install the PowerShell profile hook for automatic version switching.

```powershell
# Auto-detect profile
jwmv integrate

# Specify shell
jwmv integrate --shell powershell

# Custom profile path
jwmv integrate --profile "C:\Users\me\Documents\PowerShell\Microsoft.PowerShell_profile.ps1"
```

| Option              | Description                     |
|---------------------|---------------------------------|
| `--shell <SHELL>`   | Target shell (default: powershell) |
| `--profile <PATH>`  | Custom profile file path        |

---

### `jwmv env`

Print environment activation scripts or show project configuration.

```powershell
# Show project .jwmvrc
jwmv env

# Emit initialization script
jwmv env --init

# Emit for a specific directory
jwmv env --cwd ./my-project
```

| Option           | Description                             |
|------------------|-----------------------------------------|
| `--shell <SHELL>` | Target shell                           |
| `--cwd <PATH>`   | Working directory to scan for .jwmvrc   |
| `--init`          | Emit shell initialization script       |

---

### `jwmv flush`

Clear cached files selectively.

```powershell
# Clear downloaded archives
jwmv flush --archives

# Clear temporary files
jwmv flush --temp

# Clear catalog cache
jwmv flush --catalog

# Clear everything
jwmv flush --archives --temp --catalog
```

| Option        | Description                      |
|---------------|----------------------------------|
| `--archives`  | Delete downloaded ZIP archives   |
| `--temp`      | Delete temporary extraction files |
| `--catalog`   | Delete the catalog cache         |

---

### `jwmv selfupdate`

Update jwmv itself from GitHub Releases.

```powershell
# Check for updates
jwmv selfupdate --check

# Apply update
jwmv selfupdate

# Skip confirmation
jwmv selfupdate --yes

# Force reinstall current version
jwmv selfupdate --force

# Update and restart
jwmv selfupdate --restart

# Use a different repository
jwmv selfupdate --repository owner/repo
```

**Aliases:** `self-update`

| Option                    | Description                              |
|---------------------------|------------------------------------------|
| `-c, --check`             | Only check, don't apply                  |
| `-f, --force`             | Force update even if same version        |
| `-y, --yes`               | Skip confirmation prompt                 |
| `--restart`               | Restart jwmv after update                |
| `-r, --repository <REPO>` | GitHub `owner/repo` for releases        |
| `-t, --tag <TAG>`         | Specific release tag to install          |

---

## Shell Integration

jwmv can automatically switch Java versions when you `cd` into a project with a `.jwmvrc` file.

### Setup

```powershell
jwmv integrate
```

This adds a managed block to your PowerShell profile (`$PROFILE`) that bootstraps jwmv on every new terminal session. The integration:

1. Reads the current or project-specific Java version
2. Sets `JAVA_HOME` and `PATH` automatically
3. Switches versions seamlessly as you navigate between projects

### Manual integration

Add this to your PowerShell profile (`$PROFILE`):

```powershell
# >>> jwmv initialize >>>
$jwmvInit = & jwmv env --init --shell powershell 2>$null
if ($jwmvInit) { $jwmvInit | Invoke-Expression }
# <<< jwmv initialize <<<
```

---

## Per-Project Java Version

Create a `.jwmvrc` file in your project root:

```
21-tem
```

When shell integration is active, jwmv automatically activates this version when you enter the directory. The resolution order is:

1. **Session** — set via `jwmv use`
2. **Project** — from `.jwmvrc` (walks up the directory tree)
3. **Default** — set via `jwmv default`

---

## Configuration

jwmv stores its data under `~/.jwmv/`:

```
~/.jwmv/
├── config.json          # User configuration
├── candidates/
│   └── java/            # Installed JDKs
├── archives/            # Downloaded ZIP files
├── tmp/                 # Temporary extraction files
└── var/
    ├── catalog.json     # Cached Foojay catalog
    └── manifests/
        └── java/        # Installation metadata (one JSON per version)
```

### `config.json`

```json
{
  "preferredDistributionAlias": "tem",
  "catalogRefreshHours": 6,
  "autoEnvEnabled": true,
  "defaultShell": "powershell",
  "defaultJavaAlias": "21-tem",
  "selfUpdateRepository": "stescobedo92/jwmv"
}
```

| Key                         | Default        | Description                                  |
|-----------------------------|----------------|----------------------------------------------|
| `preferredDistributionAlias` | `"tem"`        | Default distribution when not specified       |
| `catalogRefreshHours`        | `6`            | Hours before catalog auto-refreshes           |
| `autoEnvEnabled`             | `true`         | Enable `.jwmvrc` auto-switching               |
| `defaultShell`               | `"powershell"` | Shell for script generation                   |
| `defaultJavaAlias`           | —              | Persistent default Java version               |
| `selfUpdateRepository`       | —              | GitHub `owner/repo` for self-update           |

---

## Usage Examples

### Managing a multi-project workflow

```powershell
# Project A needs Java 17
cd C:\Projects\legacy-app
echo "17-cor" > .jwmvrc

# Project B needs Java 21
cd C:\Projects\modern-api
echo "21-tem" > .jwmvrc

# Install both versions
jwmv install 17-cor
jwmv install 21-tem --default

# With shell integration, Java switches automatically
cd C:\Projects\legacy-app
java -version   # → Corretto 17

cd C:\Projects\modern-api
java -version   # → Temurin 21
```

### Scripting with jwmv

```powershell
# Set JAVA_HOME for a build script
$env:JAVA_HOME = $(jwmv home 21-tem)
./gradlew build

# Check if a version is installed
if (jwmv installed | Select-String "21-tem") {
    Write-Host "Java 21 Temurin is ready"
}
```

### CI/CD usage

```yaml
# GitHub Actions example
- name: Install jwmv
  run: |
    Invoke-WebRequest -Uri "https://github.com/stescobedo92/jwmv/releases/latest/download/jwmv-win-x64.zip" -OutFile jwmv.zip
    Expand-Archive jwmv.zip -DestinationPath "$HOME\.jwmv\bin"
    echo "$HOME\.jwmv\bin" | Out-File -Append $env:GITHUB_PATH

- name: Install Java
  run: jwmv install 21-tem --default
```

---

## Supported Distributions

| Distribution        | Alias     | Vendor                |
|---------------------|-----------|-----------------------|
| Eclipse Temurin     | `tem`     | Adoptium              |
| Azul Zulu           | `zulu`    | Azul Systems          |
| Microsoft OpenJDK   | `ms`      | Microsoft             |
| GraalVM Community   | `graalvm` | Oracle                |
| Amazon Corretto     | `cor`     | Amazon                |
| Liberica            | `lib`     | BellSoft              |
| SAP Machine         | `sap`     | SAP                   |
| Oracle OpenJDK      | `ojdk`    | Oracle                |
| Oracle JDK          | `oracle`  | Oracle                |

---

## Architecture

```
Jwmv.Cli            → Spectre.Console CLI commands and DI setup
Jwmv.Core            → Interfaces, models, utilities (no dependencies)
Jwmv.Infrastructure  → Foojay API client, storage, Windows integration
Jwmv.Tests           → XUnit tests
```

The project follows a clean architecture pattern with dependency inversion. All services are behind interfaces and injected via `Microsoft.Extensions.DependencyInjection`.

---

## Building & Testing

```powershell
# Restore
dotnet restore

# Build
dotnet build -c Release

# Test
dotnet test

# Publish single-file executable
dotnet publish src/Jwmv.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true
```

---

## License

This project is open source. See the repository for license details.
