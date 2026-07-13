---
name: always-use-braces
description: >-
  Requires braces on all if/else control-flow statements in C# code for this
  project. Use when writing or editing C# in NafReDaw, or when the user asks
  about brace style or formatting.
---

# Always Use Braces

Never do this

```csharp
if (newMode is not null)
    SetMode(_mode, newMode.Value, _project, _launchpad);
```

always use braces, like this

```csharp
if (newMode is not null)
{
    SetMode(_mode, newMode.Value, _project, _launchpad);
}
```

Apply this to every `if`, `else`, `for`, `foreach`, and `while` body in C# code for this project, including single-line statements.
