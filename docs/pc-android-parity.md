# PC-to-Android feature parity plan

## User direction

The Android product must ultimately contain every capability present in the PC
OpenNV product, including capabilities added to PC after this plan is written.
Android is not a permanently reduced New Vegas edition. This is a standing
synchronization requirement, not a one-time port.

## Source snapshots

- PC reference repository: https://github.com/nikamigaming-create/OpenNV
- PC reference branch: `main`
- PC baseline inspected for this plan: `e6998858d04b3356c093c461ce34b2ebcb3872fc`
- Android repository: https://github.com/luxxxy/OpenNevada-Android-test
- Android starting branch: `main`
- Android starting commit: `e05e2903efcdc9b3f62891d65725e892ddec76b5`

The PC baseline uses Godot 4.8-dev7 Mono and .NET 10. The Android fork's
README still describes a Godot 4.7.2 baseline. Resolve the engine/SDK/export
toolchain difference before importing runtime changes. Do not assume that
copying PC binaries or project settings makes an Android build compatible.

## Non-negotiable integration rules

1. PC `main` is the feature reference. Recheck it before every Android
   implementation block and after each PC milestone/merge.
2. Port shared C# runtime owners and authoritative state, not a second Android
   simulation. Flat desktop and Android must produce the same gameplay results
   for the same supported source state and player actions.
3. Preserve Android-specific code: touch controls, mobile UI adaptations,
   Android lifecycle, SAF/storage access, package identity/signing, export
   configuration, memory budgets, rendering adaptations, and device diagnostics.
4. Never overwrite Android work with a wholesale PC branch merge. Import
   reviewed source changes by subsystem, reconcile conflicts deliberately, and
   keep Android platform code intact.
5. Every imported capability needs Android build coverage, synthetic/owned-data
   regression coverage where applicable, and device acceptance for behavior
   that depends on touch, lifecycle, rendering, storage, or performance.
6. Do not mark a feature complete because it compiles, is registered, has a
   menu entry, or appears in a screenshot. It must work through ordinary input,
   authoritative runtime state, and cold save/load.
7. Keep saves compatible. Add explicit versioned migrations when shared PC
   state changes; never discard Android-only or older save data silently.
8. Retail files remain user-owned, read-only inputs. Never commit, upload,
   package, or distribute game assets, saves, executables, or derivatives.
9. Keep platform-specific limitations visible. If a PC feature genuinely
   requires hardware unavailable on Android, record the adaptation and its
   acceptance criteria rather than silently omitting the feature.

## Parity workstreams

Track each item as **not audited**, **missing**, **in progress**, **ported**,
**device verified**, or **blocked**. "Ported" requires automated/runtime
evidence; "device verified" additionally requires a real Android device test.

### A. Runtime foundation and toolchain

- Reconcile Godot Mono version, .NET SDK, C# language/runtime APIs, native
  dependencies, export templates and Android ABI support.
- Keep PC and Android runtime source aligned while retaining Android-specific
  build targets and platform conditionals.
- Port current source readers, winning plugin/archive precedence, resource
  identity/provenance, in-process indexes and decoded-resource lifetime rules.
- Verify cold startup, install discovery, missing/corrupt input handling and
  lifecycle interruption/resume.

### B. Shared gameplay and simulation

- Compiled and text script execution, command/operand semantics, VM state,
  locals, references, globals, events, scheduling, nested calls and continuation.
- Conditions and queries, quest stages/objectives, quest/reference scripts,
  dialogue eligibility, greetings, choices, result scripts, interruptions and
  ordering.
- Player creation and progression: SPECIAL, skills, traits, perks, levels,
  actor values, factions, reputation, karma and disguises.
- Inventory, ownership, containers, loot, equipment, weapons, ammunition,
  durability, repair, crafting, recipes, barter and services.
- Locks, terminals, hacking, crime, detection, alarms, radiation, addiction,
  consumables, hardcore needs, sleep/wait, time and source-authored minigames.
- World/cell/reference loading, doors, portals, terrain, collision, navigation,
  hazards, ownership, resets, respawn and streaming.
- NPC/creature AI, schedules, packages, companions, perception, combat decisions,
  weapons, projectiles, damage, armor, limbs, critical/sneak rules, VATS and death.

### C. Content, campaigns and compatibility

- Fallout: New Vegas standalone campaign and all official DLC.
- Fallout 3 standalone campaign and all official DLC.
- Tale of Two Wastelands, with distinct campaign/save identity and supported
  version combinations.
- Declared supported plugin configurations, including the PC project's
  xNVSE/JIP/JohnnyGuitar/kNVSE/Stewie/UIO/JAM compatibility work.
- Fallout 1 and Fallout 2: native DAT/MAP/PRO/FRM readers, source-driven hex
  worlds, inventory/equipment, interactions, scripts, combat, quests and
  complete campaign execution as those PC systems mature.
- Do not infer support from identity recognition, parsing, launchability or
  isolated demonstrations. Record exact supported versions and remaining gaps.

### D. Presentation, input and output

- Port PC's native UI and presentation capabilities: launcher, character
  creation, HUD, Pip-Boy, maps, journal, inventory, dialogue, containers,
  barter, crafting, terminals, lockpicking, VATS, messages, endings and settings.
- Preserve authored text, localization, icons, animation, audio, voice, lipsync,
  particles, lighting, shadows, weather, water, terrain, LOD and effects.
- Adapt mouse/keyboard/controller interactions to Android touch and gamepads.
  Touch controls must expose all relevant actions and remain usable around
  Pip-Boy, dialogue, inventory and modal screens.
- Port the PC OpenXR feature set where Android OpenXR runtimes and hardware
  support it. Keep flat Android gameplay complete even when XR is unavailable.
- Keep UI scaling, orientation, safe areas, focus, pause and accessibility usable
  on handheld screens.

### E. Persistence and lifecycle

- Share authoritative save schemas and migration rules with PC.
- Preserve quests, scripts/events, locals, inventory, actor/world state, timers,
  effects, relationships, package state, deaths/spawns and interrupted actions.
- Test save -> process kill -> cold launch -> Continue -> ordinary gameplay.
- Use Android Storage Access Framework for user-visible import/export where
  filesystem sandboxing prevents ordinary file pickers from reaching saves.
- Never uninstall/replace a package or change signing identity in a way that
  risks existing saves without a tested migration/backup path.

### F. Mobile engineering and acceptance

- Android memory/performance budgets, texture/resource residency, streaming,
  loading stalls, frame pacing, thermal behavior, battery and backgrounding.
- Android rendering adaptations must preserve gameplay visibility and state;
  quality reductions are configurable and must not alter simulation rules.
- Verify APK ABI, package identity, signing, export contents and clean install /
  update behavior.
- Run shared C# and headless Godot tests, Android export/package checks, then
  physical-device tests for every mobile-sensitive feature.
- Keep the user's known-good APK/save checkpoint recoverable throughout testing.

## PC-to-Android synchronization procedure

For every PC feature or merged PC change:

1. Identify changed files and classify them as shared runtime, desktop-only,
   editor/tooling, test, content, or platform-specific.
2. Trace shared changes to their C# owners and dependencies; do not import
   desktop launcher/UI code as a substitute for Android presentation.
3. Import the smallest coherent shared subsystem into a fresh
   `codex/pc-feature-*` branch in this repository.
4. Reconcile API/toolchain/platform differences and preserve Android adaptations.
5. Add or update cross-platform contracts so PC and Android exercise the same
   authoritative owner and expected state transitions.
6. Build and export the ARM64 APK; inspect the packaged assemblies/resources.
7. Run the required automated checks and selected owned-install audits.
8. Device-test the affected behavior, including save/cold-resume when relevant.
9. Publish a checked PR. Do not merge without the repository owner's approval.
10. Update the parity ledger and record the exact PC source commit and Android
    commit used.

A PC feature added after the baseline automatically becomes a new parity
obligation. Revisit this plan at each sync; do not let the fixed list become a
ceiling.

## Initial synchronization gap

The PC baseline has advanced to Godot 4.8-dev7 / .NET 10 and contains shared
script/dialogue execution work plus experimental classic Fallout OpenXR
presentation. The Android fork's documented baseline still names Godot 4.7.2,
and its current work includes Android-specific New Vegas dialogue/package and
companion fixes. The first source-level findings are recorded in
[the initial synchronization audit](android-sync-audit.md): the compiled-script
M1 decoder/command table/VM are absent on Android, and several shared integration
owners are older Android variants. First resolve the runtime/toolchain
compatibility, then port M1 as a dependency-aware subsystem without losing those
Android fixes.

No parity percentage or completion claim is made by creating this plan.
