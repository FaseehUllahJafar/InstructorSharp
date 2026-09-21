# How InstructorSharp works

A map of the pipeline and, more usefully, why each piece exists. Most of these components are
solving a failure mode that only shows up once real models are involved.

## The pipeline

```
 messages ──▶ SchemaGenerator ──▶ ExtractionStrategy ──▶ IChatClient
                                                            │
                                      ┌─────────────────────┘
                                      ▼
                              strategy.ExtractPayload
                                      │
                                      ▼
                               JsonExtractor            find the JSON in the prose
                                      │
                                      ▼
                            JsonSerializer.Deserialize  via JsonTypeInfo<T>
                                      │
                                      ▼
                            DataAnnotations + custom validators
                                      │
                         ┌────────────┴────────────┐
                    valid│                         │invalid
                         ▼                         ▼
                   return T          RepairPromptBuilder ──▶ back to IChatClient
```

## The components

**`SchemaGenerator`** turns a CLR type into the schema sent to the provider. It does two things a
plain schema exporter does not.

*Strict shaping.* Providers that constrain decoding natively accept only a narrow subset of JSON
Schema: every property must be listed in `required`, every object must set
`additionalProperties: false`, and keywords like `pattern`, `minimum` and `format` are rejected
outright. Those keywords still carry intent, so rather than dropping them they are folded into
the property description, where the model reads them. Where the keyword came from a validation
attribute it is also enforced afterwards by the validator; `format`, `uniqueItems` and
`multipleOf` have no attribute behind them, so for those the description is the only enforcement
and the model is being asked rather than compelled.

*Envelope wrapping.* A strict schema root must be an object, so `List<Person>`, `int` and enums
cannot be requested directly. They are wrapped under a single `value` property and unwrapped on
the way back. This is why primitives and collections work here and do not work through the
built-in structured-output path.

Schemas are cached, keyed by type plus strictness plus the caller's `JsonSerializerOptions`
instance. The cache is capped, because that last key component is a reference: an application
that builds fresh options per request would otherwise never hit the cache and would grow it for
the life of the process.

**`ExtractionStrategy`** shapes the request per provider and pulls the payload back out. Four
built-ins, selected by priority from the client's advertised metadata, with a prompted fallback
that works against anything. The type is public because provider behaviour is the part of this
that rots fastest, and waiting for a release to fix someone else's API change is not reasonable.

**`JsonExtractor`** finds the JSON document inside whatever the model actually sent — leading
prose, markdown fences, trailing commentary. It brace-matches with awareness of string literals
and escapes, so a `{` inside a string value never unbalances the scan, and it parses each
candidate before accepting it, because a preamble like `the { object } you asked for` is
balanced without being JSON.

**`PartialJsonCompleter`** turns a prefix of a document into a valid document, which is what
makes streaming snapshots possible. It leans on `Utf8JsonReader` with `isFinalBlock: false` —
the same machinery the BCL uses to read JSON off a socket — to find the last complete token, then
re-closes the open containers. A trailing partial string is salvaged so text fields visibly fill
in; a trailing partial number is withheld, because a half-written `123` looks exactly like a
confident `1`.

**`StreamingJsonBuffer`** accumulates streamed text as UTF-8 so each chunk is encoded once rather
than the whole response being re-encoded per chunk. It holds a stateful `Encoder`, which is what
keeps a surrogate pair split across a chunk boundary from being corrupted into a replacement
character.

**`DataAnnotationsValidator`** walks the whole object graph rather than just the top level, and
reports failures by JSON path because that is the document the model wrote. Its visited set is
permanent for the duration of a walk: that stops reference cycles from recursing forever, and
stops a node reachable by many paths from being re-walked once per path, which is exponential on
a wide graph.

**`RepairPromptBuilder`** is the part that makes this a repair loop rather than a retry loop. The
model's own rejected answer goes back as an assistant turn, followed by the specific per-field
reasons, so the next call reads as a correction task rather than a fresh attempt at the original.

## A known difference on .NET Framework

`[Range]` lower bounds of zero or less do not reach the schema on netstandard2.0. The exporter
there emits `minimum` only when it is greater than zero, so `[Range(0, 130)]` arrives at the
model as "maximum 130" with no lower bound, while on .NET 8 and .NET 10 it arrives as both. Upper
bounds, string lengths and every other constraint are unaffected.

This costs a round trip, not correctness. The model is simply not told about that lower bound and
may guess below it; validation still rejects the value and the repair loop still corrects it, so
an out-of-range number is never returned. The schema-transform hook exposes only a property name
and path, not the attribute, so there is no way to re-insert the bound from inside this library.

`KnownFrameworkDifferences` in the test suite pins this in both directions and will fail if
upstream fixes it, at which point this section should be deleted.

## Deliberate non-goals

**No repair loop while streaming.** Output a user has already watched appear cannot be silently
retried. The final object is validated and an invalid one throws.

**No tool calling while streaming.** Providers deliver partial tool arguments inconsistently, so
streaming drops to the strongest text-producing mode rather than guessing which convention is in
use. This holds even when a caller sets `Mode = ExtractionMode.ToolCall` explicitly, because
honouring it would attach a forced tool and then read only the text of each update, which is
empty for tool-call updates. Non-streaming calls are unaffected. A custom strategy can opt in by
overriding `SupportsStreaming`.

**No token budget while streaming.** Usage is not reported until a stream ends, so there is
nothing to meter against mid-flight. `MaxStreamBytes` bounds a streaming call instead.

**No provider SDK dependency.** No target references any provider SDK. On .NET 8 and .NET 10 the
only dependencies are `Microsoft.Extensions.AI.Abstractions` and
`Microsoft.Extensions.DependencyInjection.Abstractions`; the netstandard2.0 target adds three BCL
polyfill packages (`Microsoft.Bcl.AsyncInterfaces`, `System.Diagnostics.DiagnosticSource`,
`System.ComponentModel.Annotations`) for types that are in-box on modern .NET.

**No transport retry.** A 503 ends the attempt loop rather than consuming the budget against a
dead endpoint. Compose `Microsoft.Extensions.Http.Resilience` underneath if you want that, where
it belongs.
