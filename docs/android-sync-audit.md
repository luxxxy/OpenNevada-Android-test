# Initial PC-to-Android synchronization audit

Audit date: 2026-11-17

## Compared source revisions

- PC `main`: `e6998858d04b3356c093c461ce34b2ebcb3872fc`
- Android `main`: `e05e2903efcdc9b3f62891d65725e892ddec76b5`

## Confirmed M1 compiled-script gap

PC PR #354 merged the compiled-script milestone into PC main. Its changed-file
list includes the decoder, command metadata, VM, condition/script adapters,
script values and bindings, plus integrations into dialogue, quest, terminal,
reference-world, inventory-command and speech owners.

The Android main branch does not contain these three core files at the matching
paths:

- `runtime/src/Content/FalloutCompiledScript.cs`
- `runtime/src/Content/FalloutCompiledScriptCommands.cs`
- `runtime/src/Content/FalloutCompiledScriptVm.cs`

Android does contain older versions of shared integration points such as
`FalloutScriptBindings.cs`, `FalloutScriptValues.cs`,
`FalloutReferenceScripts.cs`, and `FalloutReferenceWorld.cs`. Their presence
does not establish M1 parity; their source differs from the PC baseline and
must be reconciled with the compiled VM's actual owners.

The PC PR #354 changed the following additional runtime paths that must be
checked and selectively ported as one dependency-aware M1 workstream:

- `runtime/src/Content/Definitions/FalloutConditionFunction.cs`
- `runtime/src/Content/FalloutConditionRuntime.cs`
- `runtime/src/Content/FalloutConditionRuntime.ScriptFunctions.cs`
- `runtime/src/Content/FalloutDialogueTopic.cs`
- `runtime/src/Content/FalloutNvseNumericExpression.cs`
- `runtime/src/Content/FalloutQuestStages.cs`
- `runtime/src/Content/FalloutScriptBindings.cs`
- `runtime/src/Content/FalloutScriptLocals.cs`
- `runtime/src/Content/FalloutScriptManualSaveRequests.cs`
- `runtime/src/Content/FalloutScriptPackage.cs`
- `runtime/src/Content/FalloutScriptValues.cs`
- `runtime/src/Content/FalloutTerminal.cs`
- `runtime/src/Gameplay/State/FalloutInventoryCommands.cs`
- `runtime/src/World/Actors/RuntimeNativeSpeech.cs`
- `runtime/src/World/Cells/FalloutReferenceScripts.Terminals.cs`
- `runtime/src/World/Cells/FalloutReferenceScripts.cs`
- `runtime/src/World/Cells/FalloutReferenceWorld.Essential.cs`
- `runtime/src/World/Cells/FalloutReferenceWorld.Health.cs`
- `runtime/src/World/Cells/FalloutReferenceWorld.cs`
- `runtime/src/World/Cells/FalloutScriptActorFlags.cs`

The associated PC contracts and development-lab audits also need a platform-
appropriate path. Do not copy PC-only tooling blindly into the Android APK.

## Next implementation block

1. Resolve the Godot/.NET version and Android export compatibility before
   changing shared runtime code.
2. Build a file-by-file dependency map for the complete PC PR #354 runtime
   changes against Android's current owners.
3. Port M1 as a coherent subsystem, preserving Android dialogue, package,
   companion, save and touch behavior.
4. Add or adapt the PC compiled-script synthetic contracts to run in the
   Android-capable shared test pipeline.
5. Run the full runtime gate, Android ARM64 export, package inspection and
   selected owned-data audits. Device-test any affected gameplay and cold saves.
6. Only then mark the M1 parity entries as ported/device verified.

No compiled-script parity is claimed by this audit. No PC runtime code has yet
been imported by this audit.
