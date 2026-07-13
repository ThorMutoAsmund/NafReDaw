# NafReDaw

Console DAW prototype for Novation Launchpad MIDI control, ASIO sample playback/recording, and `.nafdaw` project files.

## Requirements

- .NET 9
- Windows (ASIO via NAudio)
- Novation Launchpad (optional, for MIDI control)

## Run

```bash
dotnet run --project NafReDaw
```

## Cursor Agent Skills

This repo includes project skills under [`.cursor/skills/`](.cursor/skills/).

| Skill | Description |
|-------|-------------|
| [always-use-braces](.cursor/skills/always-use-braces/SKILL.md) | Requires braces on all `if`/`else`/`for`/`foreach`/`while` bodies in C# |

Cursor loads project skills from `.cursor/skills/<skill-name>/SKILL.md`. To follow a skill in chat, reference it by name (`always-use-braces`) or open the linked `SKILL.md` file.
