# Unity setup and team workflow

The Unity version for this repository is **6000.6.10f1 (Apple Silicon)**. The playable project lives in `UnityProject/`. The existing Godot project remains isolated in `last-signal/` and must not be moved, converted, or deleted.

## Open the project

Open the `UnityProject` folder itself in Unity Hub, not the repository root and not `last-signal`.

Tracked Unity source:

- `UnityProject/Assets/`
- `UnityProject/Packages/`
- `UnityProject/ProjectSettings/`

Never add `Library`, `Temp`, `Logs`, `UserSettings`, generated solution files, or local builds. The root `.gitignore` already protects these paths.

## Safe GitHub flow

1. Start from an up-to-date `main` and create one branch per task: `feature/...`, `fix/...`, or `content/...`.
2. Keep Unity and Godot changes in separate commits. A Unity task should normally touch only `UnityProject/`, root documentation, and root Git settings.
3. Before committing, let Unity finish importing and confirm the Console has no red errors.
4. Run **Last Signal → Validate Prototype** and enter Play mode for a short smoke test.
5. Inspect the file list before every commit. Unity `.meta` files must travel with their matching assets.
6. Push the feature branch and open a pull request. Do not push directly to `main`.
7. The teammate who merges should reopen the project after the merge and allow Unity to reimport locally.

## Large assets

Before adding voice recordings, music, textures, or 3D source files, configure Git LFS with the team. Do not commit a large audio/art batch until `.gitattributes` is agreed in a dedicated pull request. This avoids rewriting repository history later.

## Merge-conflict rules

- Project settings are serialized as text and visible `.meta` files are enabled.
- Avoid two people editing the same Unity scene or prefab at once.
- Prefer small prefabs and ScriptableObjects over one monolithic scene.
- Resolve scene/prefab conflicts in Unity-aware tooling or by choosing one side and reapplying the smaller change in the Editor; do not hand-edit YAML unless you understand the references.

## Prototype scope

The first playable implementation is intentionally code-driven and asset-independent. It contains the complete structural promise of the design deck: three nights, 24 calls, increasing pressure, seven remembered flags, the hidden `2 of 3` rule, and five endings. Production work can now proceed without blocking on voice, art, or the final 3D room.
