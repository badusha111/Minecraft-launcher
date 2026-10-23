# NEXCRAFT Launcher

A custom Minecraft: Java Edition launcher for Windows, inspired by a dark gaming interface with neon green highlights and subtle orange accents.

## Project status

Early development — initial project structure and UI prototype.

## Planned features

- Dark, neon-accented home screen
- Minecraft version selection
- Launcher settings and memory allocation
- Microsoft account authentication for licensed accounts
- Server modpack installation and synchronization
- Download progress, logs, and error handling

## Modpack architecture

Server modpacks will be defined by a versioned manifest. The launcher will download approved files, verify hashes, and update only files that differ from the manifest. Mod distribution will be implemented later; do not place private server credentials or secrets in the repository.

## Development

The initial UI prototype uses WPF and .NET on Windows. Install the current .NET SDK with the Windows desktop workload, then open `src/NexcraftLauncher/NexcraftLauncher.csproj` in Visual Studio or run `dotnet run --project src/NexcraftLauncher/NexcraftLauncher.csproj`.

## Disclaimer

NEXCRAFT is an independent project and is not affiliated with Mojang or Microsoft. A valid Minecraft license is required for authenticated play.
