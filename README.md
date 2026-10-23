# NEXCRAFT Launcher

An independent Minecraft: Java Edition launcher for Windows, with a dark gaming interface, neon-green highlights, and orange accents.

## Current status

Early development. The WPF interface prototype is in place. The launcher does **not yet download, authenticate, or start Minecraft**.

## Implemented foundation

- WPF application targeting .NET 8 for Windows.
- Dark home screen and Minecraft version selector prototype.
- Versioned server modpack manifest model.
- Manifest validation for safe relative paths, HTTPS download URLs, unique paths, and SHA-256 hash format.
- GitHub Actions workflow that builds and publishes a Windows x64 artifact.

## Planned features

1. Minecraft installation and version metadata handling.
2. Secure Microsoft account sign-in for licensed Minecraft accounts.
3. Game launch with Java/runtime checks and configurable memory.
4. Server modpack installation, hash verification, and synchronization.
5. Progress reporting, logs, and actionable error messages.
6. Settings, installed-instance library, and update flow.

## Modpack manifest

See [`docs/modpack-manifest.example.json`](docs/modpack-manifest.example.json). Each file entry has a relative destination path, HTTPS URL, and SHA-256 digest. The validator currently checks manifest metadata; the downloader, hash verification against downloaded bytes, and installation flow still need to be implemented. Never run downloaded JARs as part of installation.

## Build

Open `src/NexcraftLauncher/NexcraftLauncher.csproj` in Visual Studio with the **.NET desktop development** workload installed.

A GitHub Actions workflow runs on pushes to `main` and pull requests. If successful, it publishes a self-contained Windows x64 build as the artifact **NEXCRAFT-Windows-x64** in the workflow run.

## Disclaimer

NEXCRAFT is an independent project and is not affiliated with Mojang or Microsoft. Authenticated play requires a valid Minecraft license. Microsoft authentication should use the supported official sign-in flow; do not collect or store account passwords in this launcher.
