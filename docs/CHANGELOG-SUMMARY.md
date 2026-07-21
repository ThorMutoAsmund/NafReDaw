# NafReDaw — Development Summary

This document summarizes the major work done on NafReDaw during the recent development session. It is not a git changelog; it describes features, architecture, and design decisions.

## Overview

NafReDaw is a console DAW prototype for Windows that uses:

- **Novation Launchpad** for pad and side-button control
- **ASIO** (via NAudio) for low-latency playback and recording
- **`.nafdaw` project files** for saving samples, arrangement data, and device settings

The app has three top-level modes — **Play**, **Edit**, and **Arrange** — with sub-modes and tools layered on top.

---

## Architecture

| Area | Role |
|------|------|
| `App.cs` | Session state (mode, selection, transport, `ChangesMade`, `ActivePatternIndex`, etc.) |
| `Program.cs` | Main loop, console commands, Launchpad handlers, UI refresh |
| `AudioSystem.cs` | Sample load/assign, playback, recording, trim, volume, silence trim |
| `MidiSystem.cs` | Launchpad setup, VU meter |
| `ArrangeSystem.cs` | Pattern transport, step grid, painting, navigation |
| `Project` (`ProjectFiles .cs`) | Load/save `.nafdaw` |
| `DawProject` | Serializable project data |
| `Arrangement` / `Pattern` | POCO arrangement model (BPM, patterns, step grid) |

**Design conventions added:**

- Project skills: `always-use-braces`, `prefer-var`
- `ChangesMade` and `ActivePatternIndex` live on `App`, not in saved project data
- `Arrangement` and `Pattern` are simple POCOs; logic lives in `ArrangeSystem`

---

## Play Mode

- Tap a pad to play its assigned sample
- Loaded pads show dim white; currently playing pad shows bright green
- Samples respect **start/end trim**, **loop**, and **volume**

---

## Edit Mode

### Sub-modes

| Sub-mode | Purpose |
|----------|---------|
| **Editing** | Select and edit samples on pads |
| **Recording** | Arm empty pads and record new samples |

Switch with **Record Arm** (recording) and **Track Select** (editing).

### Sample editing (Editing sub-mode)

1. Tap a loaded pad to **select** it (blue)
2. Use side tools:

| Tool | Button | Action |
|------|--------|--------|
| Start trim | Row 0 | Toggle; arrows nudge start ±100 ms / ±10 ms |
| End trim | Row 1 | Toggle; arrows nudge end ±100 ms / ±10 ms |
| Volume | Row 2 | Toggle; arrows adjust ±0.1 / ±0.01 |
| Loop | Row 7 | Toggle loop on/off |
| Quantize | Quantize | Auto-trim leading/trailing silence |
| Delete | Delete | Unassign sample from pad |

- Trim and volume changes **replay** the sample for audition
- End trim preview plays from ~1 s before the new end; loops wrap to **StartSample**
- **Quantize** scans the in-memory waveform for frames above a threshold and sets `StartSample` / `EndSample` (does not rewrite the WAV file)

### Recording (Recording sub-mode)

- Tap an **empty** pad to arm it (red)
- **Record** button starts/stops capture to `samples/recording_<timestamp>.wav`
- **Undo** cancels an in-progress recording without saving
- **Row 0** toggles **mono / stereo** recording
  - Mono: Record Arm button turns **green**; saved WAV is downmixed mono
  - Stereo: Record Arm stays **red**
- Row side buttons show a **VU meter** while in recording sub-mode

---

## Arrange Mode

### Data model

- **Arrangement**: `Bpm`, list of **Patterns** (max 32)
- **Pattern**: 8 tracks × 64 steps
- Each cell stores a **MIDI note** (which pad’s sample to trigger), or `-1` for empty
- **One step = 4 beats** at the arrangement BPM (e.g. 120 BPM → 2 seconds per step)
- Pattern ends early when no assigned steps remain; transport advances to the **next pattern**; stops after the last pattern

### Grid display

- 8×8 Launchpad shows **8 columns** of the current **step page** (steps 1–8, 9–16, … 57–64)
- Rows = tracks; columns = steps on the visible page
- Assigned cells: green
- Playhead: white vertical column when transport is running

### Navigation

| Control | Action |
|---------|--------|
| Up | Next pattern (creates new pattern up to max 32) |
| Down | Previous pattern |
| Shift + Down | Jump to pattern 0 |
| Left | Previous step page |
| Right | Next step page |
| Shift + Left | Jump to step page 0 |

Arrow buttons light **green** when that direction is available.

### Transport

| Control | Action |
|---------|--------|
| Click | Toggle play/stop; start from **first step of current page** |
| Shift + Click | (Re)start from **step 0** of active pattern |

To play a full song from the beginning: go to pattern 1, step page 0, then **Shift + Click**.

### Step painting

1. **Hold Record** — pads switch to sample layout (like Play mode)
2. Tap a sample pad to pick the **paint note** (auditioned)
3. **Release Record** — return to step grid
4. Tap cells to assign the paint note; tap again on same note to **clear**
5. Multiple notes on one step column are **mixed** together at playback

---

## Audio Engine

- `PlayOneShot` supports volume, loop, and seek-within-region for previews
- Arrange playback uses `replaceCurrent: false` so multiple tracks on one step mix
- Mono recording downmixes stereo PCM at save time

---

## Console Commands

| Command | Description |
|---------|-------------|
| `play` / `record` / `arrange` | Set mode (+ matching sub-mode) |
| `load` / `save` | Project file I/O |
| `sample <note> <file>` | Assign WAV to pad |
| `remove <note>` | Unassign pad |
| `midi` / `audio` | List or set MIDI/ASIO devices |
| `quit` | Exit (prompts if unsaved) |

---

## Files Touched in This Session

- Core: `Program.cs`, `App.cs`, `AudioSystem.cs`, `ArrangeSystem.cs`, `Arrangement.cs`
- Audio: `AsioSampleAudio.cs`
- MIDI: `MidiSystem.cs`, `LaunchpadMidi.cs` (added `QuantizeButtonCc`, `DeleteButtonCc`)
- Project: `DawProject.cs`, `LoadedSample.cs`, `Enums.cs`
- Docs/skills: `README.md`, `.cursor/skills/prefer-var/`

---

## Known Limitations / Future Work

- BPM is stored but not yet adjustable from the Launchpad
- No pattern copy/paste or song chain editor beyond sequential playback
- Arrange paint note is session-only (not persisted as a separate concept)
- `remove` / `delete` unassigns the pad but does not delete the WAV file from disk
- Some Launchpad top-row buttons (Mute, Solo, etc.) are not wired yet
