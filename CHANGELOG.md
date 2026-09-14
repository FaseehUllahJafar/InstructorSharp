# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/faseehjafar/InstructorSharp/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/faseehjafar/InstructorSharp/releases/tag/v0.1.0
