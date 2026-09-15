# Contributing

Thanks for taking a look.

## Getting set up

You need the .NET 10 SDK. The .NET 8 targeting pack is also used by the test matrix.

```bash
dotnet build
dotnet test
```

The whole suite runs offline in about a second. It needs no API key and makes no network call,
because every test drives a scripted in-memory `IChatClient`. If a change makes the tests need
the network, that is a bug in the change.

## Running against a real model

Tests under `tests/**/Integration` skip themselves unless the relevant key is present:

```bash
export OPENAI_API_KEY=sk-...
dotnet test
```

See the remarks on `LiveProviderTests` for wiring a provider package.

## What a good pull request looks like

- A test that fails before the change and passes after it. For a parser or validator change this
  is not optional -- those are the parts where a plausible-looking fix quietly breaks an edge case.
- Warnings are errors here, and the trim/AOT analyzers are on. If the build is clean locally it
  will be clean in CI.
- Public API additions need XML documentation; the build enforces it.
- Keep the core package free of provider SDK dependencies. Which model someone talks to is their
  choice, expressed through whichever `IChatClient` they pass in.

## Adding support for a provider

Prefer a new `ExtractionStrategy` over special cases inside the engine. The seam is public
precisely so provider quirks stay at the edge. If a built-in strategy has the wrong provider
list, that is a one-line fix and a welcome pull request.

## Reporting a bug

The most useful bug report includes the model and provider, the type you were extracting, and
the raw text the model returned. `ExtractionFailedException.Attempts` carries all of that, and
`TryExtractAsync` exposes it without throwing.
