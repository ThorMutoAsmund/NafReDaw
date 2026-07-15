---
name: prefer-var
description: >-
  Prefer `var` over explicit types for local variables in C# when the type is
  clear from the right-hand side. Use when writing or editing C# in NafReDaw,
  or when the user asks about var style or local variable typing.
---

# Prefer var

Whenever you can use `var`, do not write the actual type.

Never do this:

```csharp
int sampleRate = sample.InMemorySample.WaveFormat.SampleRate;
List<string> names = new List<string>();
```

Always use `var` when the type is apparent from the initializer:

```csharp
var sampleRate = sample.InMemorySample.WaveFormat.SampleRate;
var names = new List<string>();
```

Apply this to local variables, including `for` loop variables, when `var` is valid C#.

Keep explicit types for:

- Fields and properties
- Method parameters and return types
- Locals that cannot use `var` (no initializer, or the compiler cannot infer a single type)
