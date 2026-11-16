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


## Toolchain and CI verification

The Android repository's actual pinned build configuration is more specific
than the README summary:

- `global.json` pins .NET SDK `9.0.311`.
- `runtime/OpenNV.csproj` uses `Godot.NET.Sdk/4.7.2`, with `net8.0`
  generally and `net9.0` when `GodotTargetPlatform=android`.
- `.github/workflows/android-apk.yml` installs .NET `9.0.311`, Godot
  `4.7.2` Mono, and the matching `4.7.2.stable.mono` Android export
  templates. It then restores/builds the Android target and exports an ARM64
  APK.
- PC `main` pins .NET SDK `10.0.401` and
  `Godot.NET.Sdk/4.8.0-dev.7`, targeting `net10.0`.

This means the toolchain mismatch is confirmed in executable CI configuration,
not merely inferred from README text. Updating only `global.json` or only the
Godot SDK package would break reproducibility. The migration must move as one
tested unit: SDK, Godot Mono editor/runtime, export templates, Android export
templates, C# target framework, Android workload/tooling, and CI.

## M1 import boundary

PC PR #354 is the correct source snapshot for the first runtime port. Its
summary confirms that the VM is not a standalone file: it dispatches through
`FalloutReferenceScripts` and relies on script bindings/values, locals,
condition functions, numeric expression handling, quest stages, dialogue,
terminal, inventory, speech and reference-world owners.

The Android project already has a distinct Android packaging and input path.
Therefore the M1 port must not replace the Android workflow or merge the PC
project wholesale. First create a compatibility branch that proves the PC
Godot/.NET toolchain can still export an ARM64 APK; only then import the M1
runtime dependency closure. If Godot 4.8-dev7 does not provide a matching
Android Mono export template, evaluate a stable Godot version supported by
both projects or isolate shared runtime code in a portable .NET library. Do
not claim parity by leaving the Android APK on the old VM.

## Acceptance gates for the first code port

- Android CI uses the same selected Godot/.NET baseline as the imported runtime.
- Existing Preview 3 startup, character creation, furniture interaction,
  Pip-Boy/touch UI, archive loading and save import/export paths remain covered.
- Synthetic compiled-script tests cover operand decoding, expressions,
  branching, loops, variable assignment, commands and event dispatch.
- The Android export contains the updated runtime assembly and ARM64 native
  libraries.
- Device testing confirms a real owned FNV save can continue after a cold
  process restart; no old package or save is removed during testing.
