# Security Policy

## Supported versions

The latest released version is supported. While the package is below 1.0.0 that means the most
recent tag only.

## Reporting a vulnerability

Please report privately through GitHub Security Advisories:
<https://github.com/FaseehUllahJafar/InstructorSharp/security/advisories/new>

Do not open a public issue for a vulnerability. You should get a first response within a week.

## Notes on the threat model

This library sends your prompts to whichever model provider you configure and parses what comes
back. Two things follow from that, and neither is a defect:

- **Model output is untrusted input.** It is parsed as JSON and deserialized into your types, and
  validation runs before the value is returned. It is never executed, and no type resolution is
  driven by the model's response. Be aware that a value that passes validation can still be
  wrong, and should not be treated as authoritative for a security decision on its own.
- **Prompts and extracted data leave your process.** They go to the provider behind the
  `IChatClient` you supply. This library adds no telemetry, makes no network calls of its own,
  and has no dependency on any provider SDK.
