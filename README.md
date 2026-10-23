# NEXCRAFT Launcher

An independent Minecraft: Java Edition launcher for Windows, with a dark gaming interface, neon-green highlights, and orange accents.

## Current status

Early development. The WPF interface, settings dialog, official version metadata checks, and a verified download pipeline for the Minecraft client, libraries, and assets are implemented. Microsoft account authentication and starting the game are not implemented yet.

## Implemented foundation

- WPF application targeting .NET 8 for Windows.
- Dark home screen and Minecraft version selector prototype.
- Settings dialog for the game directory, minimum/maximum memory values, and close-on-launch preference.
- Persistent JSON settings stored under the current Windows user's application data directory.
- Official Mojang version metadata lookup and Java version checks.\n- Minecraft installation pipeline downloads the version JSON, client JAR, library artifacts (including native classifiers), asset index, and asset objects over HTTPS; verifies official SHA-1/size metadata where provided; and writes files atomically.\n- Installation progress is shown in the launcher UI.\n- Versioned server modpack manifest model and example.
- Manifest metadata validation for safe relative paths, HTTPS URLs, unique paths, and SHA-256 format.
- A single-file modpack downloader that enforces HTTPS, limits file size, checks SHA-256 before installation, and rejects paths escaping the selected instance directory.
- GitHub Actions workflow that builds and publishes a Windows x64 artifact.

## Planned features

1. Minecraft installation and version metadata handling.
2. Secure Microsoft account sign-in for licensed Minecraft accounts.
3. Game launch with Java/runtime checks and configurable memory.
4. Manifest-driven modpack synchronization with progress and cancellation.
5. Installed-instance library, logs, and actionable error messages.
6. Settings integration with the eventual game launch flow.

## Modpack manifest

See [`docs/modpack-manifest.example.json`](docs/modpack-manifest.example.json). Each file entry has a relative destination path, HTTPS URL, and SHA-256 digest. The downloader currently installs one manifest file at a time; reading a remote manifest, comparing the installed state, batch installation, and UI integration remain to be implemented. Downloaded JARs are never executed by the installer.

## Build

Open `src/NexcraftLauncher/NexcraftLauncher.csproj` in Visual Studio with the **.NET desktop development** workload installed.

A GitHub Actions workflow runs on pushes to `main` and pull requests. If successful, it publishes a self-contained Windows x64 build as the artifact **NEXCRAFT-Windows-x64** in the workflow run.

## Disclaimer

NEXCRAFT is an independent project and is not affiliated with Mojang or Microsoft. Authenticated play requires a valid Minecraft license. Microsoft authentication should use the supported official sign-in flow; do not collect or store account passwords in this launcher.
