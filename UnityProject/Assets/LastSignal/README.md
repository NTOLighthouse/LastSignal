# Последний сигнал — playable narrative prototype

This folder contains a self-contained C# prototype derived from the game design deck.

## What is playable

- three nights and 24 sequential calls;
- call patience progression: unlimited, 30 seconds, then 15 seconds;
- the call-sign book, map, and decision journal;
- seven persistent story flags;
- the hidden `2 of 3` rule for the final `ЖДАТЬ` choice;
- all five designed endings;
- save/continue between calls and nights.

The prototype creates its UI at runtime, so it can be started from the template `SampleScene` without manually wiring scene objects.

## Run

1. Open `UnityProject` in Unity 6000.6.10f1.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Press Play.

Use the mouse. The intended viewport is 16:9; the UI scales to the Game view.

## Validate

In Unity, choose **Last Signal → Validate Prototype**. The validation checks the number and ordering of calls, the three-night split, choices, and all five endings.

## Next production steps

The runtime is deliberately asset-independent. The next safe increments are recorded in the repository-level `UNITY_SETUP.md`: replace the IMGUI prototype with production UI, add authored voice/audio, build the 3D switchboard scene, and move call content into ScriptableObjects only after the narrative has stabilized.
