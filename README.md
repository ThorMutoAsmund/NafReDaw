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
| [prefer-var](.cursor/skills/prefer-var/SKILL.md) | Prefer `var` over explicit types for local variables when the type is clear |

Cursor loads project skills from `.cursor/skills/<skill-name>/SKILL.md`. To follow a skill in chat, reference it by name (`always-use-braces`, `prefer-var`) or open the linked `SKILL.md` file.

## Documentation

| Document | Description |
|----------|-------------|
| [User manual](docs/USER-MANUAL.md) | How to use NafReDaw (Launchpad + console) |
| [Development summary](docs/CHANGELOG-SUMMARY.md) | Feature and architecture summary from recent work |
