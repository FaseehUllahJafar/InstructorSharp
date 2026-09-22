# LiveCheck

Runs InstructorSharp against a real model. The unit suite proves the library does what it
intends; this proves that what it intends is what providers actually accept.

Both providers below are free and neither requires a payment card.

## Gemini (nothing to install)

1. Sign in at <https://aistudio.google.com/apikey> with a Google account and create a key.
   The free tier serves the Flash models, has no expiry and asks for no card.
2. ```powershell
   $env:GEMINI_API_KEY = "your-key"
   dotnet run --project samples/InstructorSharp.LiveCheck
   ```

Override the model with `GEMINI_MODEL` if you want a different one. Note that free-tier requests
may be used by Google to improve their products, so do not paste anything confidential into it.

## Ollama (no account at all)

1. Install from <https://ollama.com/download>.
2. ```powershell
   ollama pull llama3.1:8b
   $env:OLLAMA_MODEL = "llama3.1:8b"
   dotnet run --project samples/InstructorSharp.LiveCheck
   ```

Everything stays on your machine. A smaller model such as `llama3.2:3b` works too and downloads
faster, though weaker models need the repair loop more often, which is itself worth seeing.

## What it checks

| Check | Why it is here |
| --- | --- |
| object | The ordinary case. |
| `List<T>` as the root | The built-in `Microsoft.Extensions.AI` path cannot request this. |
| `int` as the root | Same. A strict schema root must be an object, so this is enveloped. |
| enum as the root | Same again, and confirms enums arrive by name. |
| repair loop | Applies a rule the schema cannot express, so the model must be corrected. |
| streaming | Drives the partial-JSON completer with a real token stream. |

Exit code is 0 when every check passes, 1 on failure, 2 when no provider is configured. On
failure it prints what the model actually said and why each answer was rejected.
