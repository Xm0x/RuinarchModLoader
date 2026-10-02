# Releasing RuinarchModLoader

Players get new versions two ways: the installer zips, and the in-game **Update** notice.
The notice trusts a release only when its manifest is signed with the release key.

## The signing key

The private key is `~/.config/ruinarch-modloader/update-signing-key.xml` on the release
author's machine (another path can be given in `RUIN_UPDATE_KEY`). Keep a backup somewhere
safe and never commit it. The matching public key is the `PublicKey` constant in
`src/Ruinarch.ModMenu/Updater.cs`.

If the key is lost, installed loaders cannot accept a new in-game update. Make a new key
(`dotnet build/signer/RuinarchModLoader.UpdateSigner.dll keygen <file>`), put its public key
in `Updater.cs`, and tell players to update once with the installer.

## Steps

1. Set the version in `src/Ruinarch.Modding/ModLoader.cs` (`Version`).
2. Run `tools/package-release.sh`. It builds everything and writes to `dist/`:
   - `RuinarchModLoader-<version>.zip` and the two installer zips;
   - `update-<version>/`: the files the game downloads, `update.json`, and its signature
     `update.json.sig`.
3. Tag `v<version>`, push the tag, and create the GitHub release from it.
4. Upload the three zips **and every file in `update-<version>/`** as release assets.
   The game reads `releases/latest/download/update.json`, so a release without these
   assets is not offered in-game.

## When the installer is required

The game downloads only the loader files (`Ruinarch.Modding.dll` and the helper DLLs in
`Mods/`). If a release changes `Ruinarch.Boot.dll`, raise `Boot.Version` in
`src/Ruinarch.Boot/Boot.cs` and the `"boot"` number that `package-release.sh` writes into
`update.json`. Installed games with an older boot step then show "needs the installer"
instead of updating.

## Testing an update locally

A file `<game>/ModLoaderUpdate/source.txt` holding a URL (for example
`http://127.0.0.1:8765/`) makes the game read `update.json`, its signature and the files
from there instead of GitHub. Signatures are still checked. Delete the file afterwards.
