# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `net472` leg in the test suite and in CI, so the .NET Standard 2.0 asset is executed on .NET
  Framework rather than merely compiled for it.
- `InstructorOptions.MaxStreamBytes` bounds a streamed response; `InstructorOptions.Validators`
  adds rules for a single call.
- `ExtractionStrategy.SupportsStreaming`, so a custom strategy can declare whether it can serve
  a streamed call.

### Changed

- `TryExtractAsync` no longer throws when the token budget is exhausted; it returns an
  unsuccessful result. `ExtractAsync` still raises `TokenBudgetExceededException`.
- Transport faults from the underlying client propagate instead of being reported as a failed
  extraction, so a 401 no longer reads as "the model produced no valid object".
- Streaming refuses tool-call mode even when it is requested explicitly, rather than collecting
  nothing and failing.
- `ExtractionResult<T>`, `ExtractionAttempt` and both exceptions are constructible, so
  `IInstructor` can be implemented outside this assembly.

### Fixed

- The streaming buffer could overflow its capacity calculation past 2 GiB and spin forever.
- The JSON scanner was quadratic on truncated nested output and ignored comments the
  deserializer accepts.
- An explicit extraction mode ignored whether the strategy could serve the provider.
- Options and validator collections are copied on construction, so mutating a builder afterwards
  cannot disturb an instructor already in use.

## [0.1.0] - 2026-09-14

First release. The API may still move before 1.0.0.

### Added

- `IInstructor` over any `Microsoft.Extensions.AI.IChatClient`, with `ExtractAsync`,
  `TryExtractAsync`, `StreamAsync` and `StreamListAsync`.
- Repair loop: validation failures are fed back to the model addressed by JSON path
  (`$.lines[2].amount`) rather than retried blindly.
- Recursive `DataAnnotations` validation across nested objects, collections and dictionaries,
  with cycle and depth guards.
- Custom rules via `IInstructorValidator<T>`, delegates, or the separate
  `InstructorSharp.FluentValidation` package.
- Per-provider request shaping with automatic selection: native JSON schema, forced tool call,
  JSON mode, and a universal prompted fallback. The `ExtractionStrategy` seam is public.
- Envelope wrapping so primitives, enums, arrays and dictionaries work as root types.
- Defensive parsing of JSON out of prose, markdown fences and trailing commentary.
- Streaming partial objects, including salvage of partially-received strings.
- Token budgets across all attempts of one extraction.
- OpenTelemetry `ActivitySource` and `Meter`, both named `InstructorSharp`.
- Targets `net10.0`, `net8.0` and `netstandard2.0` (.NET Framework 4.6.2+).

### Notes

Streaming deliberately does not use tool-call mode, and does not run the repair loop. See
`docs/architecture.md` for the reasoning behind both.

[Unreleased]: https://github.com/FaseehUllahJafar/InstructorSharp/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/FaseehUllahJafar/InstructorSharp/releases/tag/v0.1.0
