# NafReDaw — User Manual

NafReDaw is a console-based sample launcher and arranger controlled from the keyboard and a Novation Launchpad. This guide explains how to set up the app and use each mode.

## Requirements

- Windows
- .NET 9
- An ASIO audio interface (for playback and recording)
- Novation Launchpad (recommended; MIDI is optional for console-only use)

## Quick Start

1. Open a terminal in your project folder (or create one).
2. Run:

   ```bash
   dotnet run --project NafReDaw
   ```

3. If a `<foldername>.nafdaw` file exists in the current directory, it loads automatically.
4. Connect your Launchpad and ASIO interface.
5. Assign samples to pads (see below), then play.

On quit, if you have unsaved changes you will be prompted to save, quit anyway, or cancel.

---

## Project Folder Layout

```
my-song/
  my-song.nafdaw      # Project file (samples, arrangement, device IDs)
  samples/            # WAV files (assignments reference files here)
    kick.wav
    snare.wav
    recording_2026-07-16_00-15-30-123.wav
```

The `.nafdaw` file stores pad assignments, trim points, volume, loop flags, arrangement patterns, and MIDI/ASIO device indices. It does **not** embed audio — WAVs live in `samples/`.

---

## Console Commands

Type commands at the `Ready!` prompt. Commands are case-insensitive.

### Modes

| Command | Effect |
|---------|--------|
| `play` | Play mode |
| `record` | Edit mode → Editing sub-mode |
| `arrange` | Arrange mode → Arranging sub-mode |

### Project

| Command | Effect |
|---------|--------|
| `load [file]` | Load a `.nafdaw` file (default: current folder name) |
| `save [file]` | Save project |
| `quit` / `q` | Exit |

### Samples

| Command | Effect |
|---------|--------|
| `sample <note> <file>` | Copy WAV into `samples/` and assign to pad |
| `s <note> <file>` | Short form |
| `remove <note>` | Unassign pad |
| `r <note>` | Short form |

Notes use hex grid values, e.g. `0x0B` for the bottom-left pad, or `row,col` if supported by `Helpers.TryParseNote`.

### Devices

| Command | Effect |
|---------|--------|
| `midi` | List MIDI input devices |
| `midi <in> [out]` | Set MIDI input/output index |
| `audio` | List ASIO drivers |
| `audio <playback> [recording]` | Set ASIO driver indices |

### Other

| Command | Effect |
|---------|--------|
| `dir` / `ls [filter]` | List files in current directory |
| `cls` | Refresh Launchpad display |
| `p <note> [color]` | Set a single pad color (debug) |

---

## Launchpad — Top-Level Modes

Use the three mode buttons on the right:

| Button | Mode | Indicator |
|--------|------|-----------|
| **Session** | Play | Green |
| **Note** | Edit | Red |
| **Device** | Arrange | Amber |

---

## Play Mode

**Goal:** Trigger samples like a drum pad.

1. Press **Session** (or type `play`).
2. Tap any pad that has a sample — it plays.
3. Pad colors:
   - **Dim white** — sample loaded
   - **Bright green** — currently playing
   - **Off** — no sample

Samples play with their saved trim, loop, and volume settings.

---

## Edit Mode

Press **Note** (or type `record`) to enter Edit mode.

### Editing sub-mode (default)

Press **Track Select** if you are in Recording sub-mode.

**Select a sample:** tap a loaded pad (turns **blue**).

#### Trim tools

| Button | Tool |
|--------|------|
| Row 0 (beside grid) | **Start** trim tool |
| Row 1 | **End** trim tool |

With a tool active (button lit green), use the arrow buttons:

| Arrow | Start tool | End tool |
|-------|------------|----------|
| Up | +100 ms | +100 ms |
| Down | −100 ms | −100 ms |
| Right | +10 ms | +10 ms |
| Left | −10 ms | −10 ms |

Each change replays the sample so you can hear the new boundary.

#### Volume tool

| Button | Tool |
|--------|------|
| Row 2 | **Volume** |

Same arrow layout: Up/Down = ±0.1, Left/Right = ±0.01. Replays on each change.

#### Loop

| Button | Action |
|--------|--------|
| Row 7 | Toggle loop on/off (green = looping) |

#### Quantize (silence trim)

| Button | Action |
|--------|--------|
| **Quantize** | Trim silence from start and end of selected sample |

Scans the waveform and sets trim points to the first and last audible frames. Does not delete data from the WAV file — only changes playback boundaries.

#### Delete

| Button | Action |
|--------|--------|
| **Delete** | Unassign sample from the selected pad |

Stops playback if that pad was playing. The WAV file remains in `samples/`.

---

### Recording sub-mode

1. Press **Record Arm** (red).
2. Tap an **empty** pad to arm it (red). Tap again to disarm.
3. Press the round **Record** button to start recording; press again to stop and save.
4. File is saved as `samples/recording_<timestamp>.wav` and assigned to the armed pad.

| Button | Action |
|--------|--------|
| **Undo** | Cancel recording without saving |
| **Row 0** | Toggle **mono** / **stereo** recording |

**Mono indicator:** Record Arm turns **green** in mono mode, **red** in stereo.

**VU meter:** The eight row buttons beside the grid show input level (green → yellow → red from bottom to top).

---

## Arrange Mode

Press **Device** (or type `arrange`) to enter Arrange mode.

### Concepts

- A **pattern** has **8 tracks** and **64 steps**.
- Each step lasts **4 beats** at the project BPM (default 120 → 2 seconds per step).
- Each grid cell can hold one **sample note** (which pad’s sound to fire on that track/step).
- Up to **32 patterns** per project; playback runs pattern 1, then 2, etc., and stops.

### What you see on the 8×8 grid

Only **8 steps at a time** are visible (one “page”). Rows are tracks; columns are steps on the current page.

| Color | Meaning |
|-------|---------|
| Green | Step has a sample assigned |
| White column | Playhead (current step) while playing |
| Off | Empty |

### Navigate patterns and pages

| Button | Action |
|--------|--------|
| **Up** | Next pattern |
| **Down** | Previous pattern |
| **Shift + Down** | Jump to pattern 1 |
| **Left** | Previous step page (8 steps back) |
| **Right** | Next step page (8 steps forward) |
| **Shift + Left** | Jump to step page 1 (steps 1–8) |

Buttons light **green** when you can move in that direction.

### Transport

| Button | Action |
|--------|--------|
| **Click** | Play / stop from **first step of current page** |
| **Shift + Click** | Play from **step 1** of the active pattern |

**To play the whole song from the beginning:**

1. Go to pattern 1 (**Shift + Down** until at the first pattern).
2. Go to step page 1 (**Shift + Left** until at the first page).
3. **Shift + Click** to start.

While playing, Click stops transport.

### Assign steps (paint)

1. **Hold** the round **Record** button.
   - Pads switch to the normal sample layout.
   - Record button lights red.
2. Tap a pad that has a sample — that becomes your **paint note** (you’ll hear a preview).
3. **Release** Record — grid returns to step view.
4. Tap cells on the grid:
   - **First tap** — assign paint note to that track/step.
   - **Tap again** (same note already there) — clear the cell.
   - **Different note** — replace with paint note.

When a step fires, all tracks with notes on that step play **mixed together**.

---

## Tips

- **Save often** — `save` or `s` before quitting.
- **Assign samples first** in Play or Edit before arranging.
- **Use Quantize** after recording to remove silence at the start/end.
- **Mono recording** saves space and is useful for mono sources; stereo keeps L/R for interfaces that need it.
- If audio fails to start, run `audio` to list drivers and set the correct index.

---

## Troubleshooting

| Problem | Things to try |
|---------|----------------|
| No sound | Check ASIO driver with `audio`; ensure interface is on and selected |
| Launchpad not responding | `midi` to list devices; `midi 0` or correct index |
| Pad won’t record | Switch to Edit → Record Arm → arm an **empty** pad first |
| Arrange plays too fast/slow | BPM is in the project file (`Arrangement.Bpm`); default 120, 4 beats per step |
| Sample plays from wrong place after trim | Re-select pad and check Start/End tools; use Quantize to reset boundaries |
| Quit won’t exit | Answer the save prompt: `s` save, `q` quit, `c` cancel |

---

## Further Reading

- [Development summary](CHANGELOG-SUMMARY.md) — architecture and feature history from the recent build session
- [README](../README.md) — build requirements and Cursor agent skills
