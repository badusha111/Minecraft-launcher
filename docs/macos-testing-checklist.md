# NEXCRAFT for macOS — validation checklist

The macOS port lives in `src/NexcraftLauncher.Mac` on branch `feature/macos-port`.
The Windows launcher is not modified by this branch.

## What CI checks

GitHub Actions builds self-contained app bundles for both Apple Silicon (`osx-arm64`) and Intel (`osx-x64`). It checks the app bundle structure, `Info.plist`, executable presence, and executable architecture. These checks do not prove that the GUI, Microsoft sign-in, or Minecraft itself works on a real Mac.

## Test on a real Mac when available

1. Download the matching `NEXCRAFT-osx-arm64.app` or `NEXCRAFT-osx-x64.app` artifact from the latest successful run:
   https://github.com/badusha111/Minecraft-launcher/actions
2. Unzip the artifact. Move `NEXCRAFT-osx-*.app` to Applications or another writable folder.
3. Open it. If macOS blocks an unsigned build, do not bypass a warning unless you trust the source and understand the security implications; production distribution should be signed and notarized.
4. Confirm the main page opens and the sidebar navigates to Home, Mods, Library, and Settings.
5. Confirm the official Minecraft release list loads, and that a selected version survives closing and reopening the launcher.
6. Sign in with a Microsoft account that owns Minecraft: Java Edition. Confirm that a user without the entitlement receives a clear error.
7. Press Play and allow the launcher to download the selected official game version. Confirm Minecraft opens.
8. In Settings, change the game directory and RAM values, save, restart the launcher, and confirm the values persist.
9. Open Mods and Library. Confirm the app lists local `.jar` mods and installed versions, and that folder-opening actions open Finder.
10. Connect to a multiplayer server from inside Minecraft, including a server that supports the account's normal authentication mode.

## Known limitations to validate before calling the port complete

- CI does not perform interactive Microsoft authentication or start a real Minecraft game.
- The current Mods page lists local `.jar` files and opens the mods folder; automatic modpack download/install is not implemented there yet.
- The app bundle is currently unsigned/notarized, so it is intended for testing rather than polished public distribution.
- The UI and gameplay still need comparison against the Windows version on an actual Mac. Report any mismatch with a screenshot and the exact step.
