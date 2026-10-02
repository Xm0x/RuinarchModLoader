# In-game loader updates

Status: implemented in loader 0.6.0. Release steps: [RELEASING.md](../RELEASING.md).

## Goal

Players update RuinarchModLoader from the main menu instead of running the installer for
every release. The menu says when a newer loader exists; **Update** downloads it, and it
installs the next time the game starts.

The installer is still needed:

- once, to install a loader that has the boot step (the first release with this feature);
- after every Steam update of Ruinarch, because Steam replaces `Assembly-CSharp.dll`,
  the file that starts the loader;
- for a release whose `boot` number is higher than the installed boot step.

## Why the update waits for a restart

The game holds its loaded assemblies open, so the loader cannot replace itself while it
runs. Downloaded files are staged; a small boot assembly applies them on the next start,
before the loader is loaded.

## Boot step

`Ruinarch.Boot.dll` lives in `Ruinarch_Data/Managed/`. The installer injects a call to
`Ruinarch.Boot.Boot.Initialize()` into the game assembly's module initializer, replacing the
direct call to `ModLoader.Initialize()`. The boot assembly references only game-supplied
framework assemblies (`mscorlib`, `System.Core`, `UnityEngine.CoreModule`), never the files it replaces:

1. If `<game>/ModLoaderUpdate/staged/apply.txt` exists, apply the staged update:
   - each line is `<sha256> <file name>`; the first line is `version <x.y.z>`;
   - names are plain `*.dll` file names; `Ruinarch.Boot.dll` is refused;
   - every staged file must match its hash, or nothing is applied;
   - `Ruinarch.Modding.dll` goes to `Managed/`, every other file to `Mods/`;
   - current files are copied to `ModLoaderUpdate/previous/` first; on any copy error
     they are restored;
   - the staged folder is deleted either way, and the result is written to
     `ModLoaderUpdate/result.txt`.
2. Load `Ruinarch.Modding` by name and call `ModLoader.Initialize()` by reflection.

Boot never throws into the game: a failed update leaves the old loader running.
`Boot.Version` (an integer, now 1) names the boot step's capabilities.

## Release manifest

Each release publishes, next to the installer zips:

- `update.json`:

      { "formatVersion": 1, "version": "0.6.0", "boot": 1,
        "page": "https://github.com/Xm0x/RuinarchModLoader/releases/tag/v0.6.0",
        "files": [ { "name": "Ruinarch.Modding.dll", "sha256": "...", "size": 123 }, ... ] }

- `update.json.sig`: RSA (PKCS#1 v1.5) SHA-256 signature of the exact bytes of
  `update.json`;
- every file the manifest lists.

The release owner holds the private key. The public key is compiled into the mod menu.
A manifest whose signature does not verify is ignored, as is any downloaded file whose hash
differs from the signed manifest. Only versions newer than the running loader are offered,
so an old signed manifest cannot downgrade a player.

## Menu

Once per launch, at the main menu, the mod menu downloads
`https://github.com/Xm0x/RuinarchModLoader/releases/latest/download/update.json` and its
signature, then shows at most one notice:

- **Update available**: version and buttons **Update** and **What's new** (release page);
- **Downloading**: progress;
- **Update ready**: "Installs when you next start the game", with **Quit game**;
- **Installer needed**: the release needs a newer boot step (or the install has none), with
  **Download page**;
- **Updated to x.y.z**: shown once after the boot step applied an update;
- **Update failed**: the reason and **Try again**. A failed check (offline, no manifest,
  bad signature) is only logged, never shown.

A file `ModLoaderUpdate/source.txt` replaces the download base URL. It exists for testing;
signatures are still required.

## Signing tool

`src/UpdateSigner` (a .NET 8 console tool): `keygen <private-key.xml>` writes a new key
pair and prints the public key; `sign <private-key.xml> <file>` writes `<file>.sig`.
`tools/package-release.sh` builds the update assets into `dist/update-<version>/` and signs
them with the key at `$RUIN_UPDATE_KEY`.
