# Local and Steam Workshop packages

Status: implemented (loader API 1). User-facing guide: [PACKAGES.md](../PACKAGES.md).

## Installation and trust

Install RuinarchModLoader separately with its installer. Workshop items do not install or update the loader. GitHub/Nexus downloads remain supported under `Ruinarch/Mods/<package>/`; subscribed items stay in Steam's installed Workshop directory.

Compatibility means the manifest and managed entry point match the supported loader API. It is not a security certificate or sandbox. Accepted code runs with the game's permissions. Never load an assembly to decide whether a rejected package is compatible.

## Manifest API 1

Every user package has a root `mod.json`:

```json
{
  "id": "author.package",
  "name": "Package name",
  "version": "1.0.0",
  "author": "Author",
  "description": "What this package does",
  "loader": "RuinarchModLoader",
  "loaderApi": 1,
  "type": "code",
  "entryAssembly": "MyMod.dll",
  "entryType": "MyMod.Entry",
  "dependencies": ["lib/MyDependency.dll"]
}
```

`id` uses lowercase letters, digits, dots, underscores or hyphens. `name` and a three-component version are required. `author` and `description` are optional strings. No unknown manifest fields are accepted. `loader` and integer `loaderApi` are mandatory and exact. API 1 is the current interface contract, not the installer release number.

A `code` package names a managed entry assembly and a public concrete entry type implementing `Ruinarch.Modding.IRuinarchMod`, with a public parameterless constructor. Dependencies are optional relative DLL paths. Metadata is read with Mono.Cecil, without executing package code. Every shipped DLL must be the entry assembly or a declared dependency. Game, Unity and loader infrastructure assemblies cannot be bundled or shadowed by user packages. Dependencies resolve only from accepted, enabled packages and shipped infrastructure, never rejected directories.

A `templates` package has `templates/*.json`, optionally `art/`, and no DLLs or code-entry fields. Code packages may also ship templates. Template JSON must be well formed, use `formatVersion: 1`, and have IDs scoped to the package. Asset references must not escape `art/`. Detailed game-asset and geometry validation happens in the framework when the catalogue becomes available, with per-file diagnostics.

Paths are relative to the package root, cannot traverse outside it, and cannot pass through symbolic links/reparse points. Vanilla XML packages, missing/malformed manifests, unsupported API declarations and invalid assemblies remain visible but are rejected before assembly loading or template registration. Existing loader mods must add the explicit API declaration and entry fields. Loose user DLLs are no longer activated.

## Discovery and precedence

Local folders are validated during loader bootstrap. Workshop discovery runs once after the game's Steam API initializes and before world creation. Only subscribed, fully installed items are candidates. Pending downloads need a game restart after Steam finishes them.

The same validator and activation path handle both origins. A local package reserves its ID even when disabled. Local copies win over Workshop copies; deterministic first discovery wins between same-origin duplicates. Losing rows stay visible with their origin and duplicate reason. Disabled packages never load their assemblies or register their templates.

The framework receives an explicit list of accepted template directories by reflection. It has no reference to the loader and does not rescan rejected local or Workshop folders. Workshop contents are read-only in the editor; copy a template and its PNG references into a local pack before editing.

Unsubscribes, downloads and updates apply on the next game launch. The mod manager shows Local or Steam Workshop, running/disabled/restart state, and an exclamation mark with `Not compatible with RuinarchModLoader` plus the exact rejection reason. Rejected/duplicate rows have no enable control.

## Browse, subscribe and upload

The Mods window opens Steam's Workshop browser for Ruinarch (app 909320). Subscribing there uses Steam's normal download flow; restart once installed.

Upload selects a compatible local package. It reruns validation before creating/submitting an item, supplies the package directory, title, description and `RuinarchModLoader` tag through the game's SteamUGC binding, and shows the actual asynchronous Steam result. New uploads default to Private. Public/Friends visibility must be selected explicitly. An existing numeric item ID updates an owned item; blank creates a new item. Steam enforces ownership and its legal agreement. The loader is never included in an upload. No public test items are published for verification.
