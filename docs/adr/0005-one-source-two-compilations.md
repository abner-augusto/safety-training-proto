# 0005: One source, two compilations

- **Status**: Accepted
- **Date**: 2026-10-16
- **Code**: `Tools/SafetyProto.Shared/SafetyProto.Shared.csproj`

## Context
The Unity build and the CLI harness must run identical orchestration, rule, scoring, and logging code, or headless runs stop predicting what the headset does. Copying the files or porting them would let the two drift. `Core/` mixes engine-free files with ScriptableObjects and `UnityEngine` facades, so it cannot be compiled wholesale outside Unity.

## Decision
`SafetyProto.Shared.csproj` links the Unity source files into a standalone .NET assembly. It globs `Scripts\Domain\**\*.cs`, because the domain assembly definition sets `noEngineReferences` and everything under it is engine-free by construction. It lists the `Core/` files one by one, so only the engine-free part crosses. There is no duplicate and no port.

## Consequences
The harness and the Unity runtime execute the same code, differing only in the adapters wired around it. A new file under `Domain/` joins the shared assembly automatically. A new engine-free `Core/` file that the harness needs must be added to the csproj list by hand. A linked file that gains an engine dependency breaks the harness build. Reviewers must keep engine types out of `Domain/` and out of the listed `Core/` files.

## Revisit when
The Domain gains a file that needs an engine type, or the Unity package manager can consume `SafetyProto.Shared.dll` directly.
