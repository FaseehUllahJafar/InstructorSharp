# InstructorSharp — what I built, and exactly what you do next

Written for someone starting from zero. Every command is copy-pasteable. Where you need an
account or a key, I say so and tell you what it costs (all of it is free).

---

## Part 0 — Read this first: the research changed the plan

The strategy document said this idea had **"Competition: None in C#."** I checked that before
writing any code, and it is no longer true. Two things exist now:

**1. `Instructor.NET` is taken on NuGet.**
Published by ElderJames (a Microsoft MVP, creator of Ant Design Blazor) in June 2025. Version
0.0.1, three commits, six stars, OpenAI-only, no repair loop. It is abandoned — but **the package
ID is permanently burned**. That is why your library is called **InstructorSharp**.

**2. A real competitor shipped: [Ingot](https://github.com/landsharkiest/Ingot).**
Zero stars, but it is not a toy. It targets the same thesis: typed extraction over
`IChatClient`, provider-aware request shaping, a repair loop with JSON-path errors. If you had
started building blind, you would have found this in week six.

**This is good news, not bad.** Nobody has *won* this space — Ingot has zero stars and no
traction. And Ingot's own README lists its gaps, which became your specification:

| Ingot's roadmap says "future work" | InstructorSharp v1 |
|---|---|
| Streaming partial extraction | **shipped** — `StreamAsync<T>` and `StreamListAsync<T>` |
| FluentValidation adapter | **shipped** — separate package |
| AOT / trimming support | **shipped** — analyzers on, annotated |
| Public strategy-registration hook | **shipped** — public from v1 |
| .NET 8+ only | **you also run on .NET Framework 4.6.2+** |

That last row is the one nobody can copy in a weekend, and it is the one that maps onto your
actual CV: you have led .NET Framework 4.8 migrations. A very large amount of enterprise .NET is
still on Framework and **cannot install Ingot at all**.

**Be honest about this in public.** The README already links to both. Acknowledging prior art
makes you look like a senior engineer; pretending you are first makes you look careless the
moment someone comments "have you seen Ingot?".

---

## Part 1 — What exists on your machine right now

Everything is at `C:\Users\Dev\Documents\Claude\InstructorSharp`.

```
InstructorSharp/
├── src/
│   ├── InstructorSharp/                  the library
│   └── InstructorSharp.FluentValidation/ optional adapter package
├── tests/InstructorSharp.Tests/          107 passing tests
├── samples/InstructorSharp.Sample/       runnable demo, no API key needed
├── .github/workflows/                    ci.yml + release.yml
├── README.md                             the public face
├── STEPS.md                              this file
├── LICENSE                               MIT, in your name
└── InstructorSharp.slnx
```

**Verified state — I ran all of this, it is not a claim:**

- Release build, **warnings treated as errors**, clean on `net10.0`, `net8.0`, `netstandard2.0`
- **107 tests pass** on both .NET 10 and .NET 8 (3 more skip themselves without an API key)
- Trim/AOT analyzers enabled and clean
- NuGet packages build correctly with all three targets, README and XML docs inside
- The sample app runs and demonstrates the repair loop working

**Four real bugs the tests caught while building** (this is why the tests exist):

1. `InstructorJson.Default` froze its serializer options with no type resolver — this would have
   thrown on the **very first call for every single user**.
2. The strategy appended the schema to the durable conversation on every attempt, so a
   3-attempt extraction paid for the schema three times and buried the repair request under it.
3. `StreamListAsync` withheld the final element and never released it — the last item of every
   streamed list was silently lost.
4. Validation ran sync-over-async (`.GetAwaiter().GetResult()`) — the exact deadlock pattern your
   own `dotnet-firefighting-skills` repo documents.

---

## Part 2 — Verify it yourself (5 minutes)

Open PowerShell. You already have the .NET 10 SDK.

```powershell
cd C:\Users\Dev\Documents\Claude\InstructorSharp

dotnet build -c Release
dotnet test
dotnet run --project samples/InstructorSharp.Sample
```

The sample prints the repair loop happening: attempt 1 rejected with a specific reason, the
correction message sent to the model, attempt 2 accepted. **That output is your launch demo.**

---

## Part 3 — Try it against a real model (15 minutes, ~$0.01)

Everything so far ran against a fake. Before publishing anything, prove it works against a real
provider. This is the single most important step in this document.

**3.1 — Get an API key.** Go to <https://platform.openai.com/api-keys>, create a key, and put
$5 on the account. The tests below cost well under one cent.

**3.2 — Make a scratch project:**

```powershell
cd C:\Users\Dev\Documents\Claude
dotnet new console -o LiveTest
cd LiveTest
dotnet add package InstructorSharp --source C:\Users\Dev\Documents\Claude\InstructorSharp\artifacts
dotnet add package Microsoft.Extensions.AI.OpenAI
```

**3.3 — Replace `Program.cs`:**

```csharp
using System.ComponentModel.DataAnnotations;
using InstructorSharp;
using Microsoft.Extensions.AI;
using OpenAI;

var key = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
IChatClient client = new OpenAIClient(key).GetChatClient("gpt-4o-mini").AsIChatClient();
var instructor = client.AsInstructor();

// 1. A plain object.
var user = await instructor.ExtractAsync<UserInfo>(
    "Faseeh is a 29 year old engineer living in Lahore.");
Console.WriteLine($"{user.Name}, {user.Age}, {user.City}");

// 2. A LIST as the root type. This is the case Microsoft.Extensions.AI cannot request.
var people = await instructor.ExtractAsync<List<UserInfo>>(
    "Ali is 29 and Sara is 31, both in Karachi.");
Console.WriteLine($"got {people.Count} people");

// 3. An INT as the root type. Also impossible through the built-in path.
var n = await instructor.ExtractAsync<int>("How many days in a leap year?");
Console.WriteLine($"n = {n}");

// 4. Streaming — watch it fill in.
await foreach (var p in instructor.StreamAsync<UserInfo>("Describe a fictional person in Karachi."))
    Console.WriteLine($"  ...{p.Name} / {p.Age} / {p.City}");

public class UserInfo
{
    [Required] public string Name { get; set; } = "";
    [Range(0, 130)] public int Age { get; set; }
    public string? City { get; set; }
}
```

**3.4 — Run it:**

```powershell
$env:OPENAI_API_KEY = "sk-your-key-here"
dotnet run
```

**If anything fails here, stop and fix it before publishing.** Tell me what broke and I will fix
it. Cases 2 and 3 are your headline claims — they must work.

---

## Part 4 — Put it on GitHub (20 minutes)

**4.1 — Install the GitHub CLI.** You do not have it yet:

```powershell
winget install --id GitHub.cli
```

Close and reopen PowerShell, then:

```powershell
gh auth login
```

Choose: GitHub.com → HTTPS → authenticate via browser.

**4.2 — Fix the URLs.** Two files contain `faseehjafar` as a placeholder. If your GitHub username
is different, change it in:

- `Directory.Build.props` — `PackageProjectUrl` and `RepositoryUrl`
- `README.md` — the CI and NuGet badge links

**4.3 — Commit and push.** I have already made the first commit locally.

```powershell
cd C:\Users\Dev\Documents\Claude\InstructorSharp
git log --oneline          # confirm the commit is there
gh repo create InstructorSharp --public --source=. --remote=origin --push
```

**4.4 — Set the repo description and topics.** These drive GitHub search, which is how people
find you:

```powershell
gh repo edit --description "Validated, strongly-typed objects from any LLM in C#. Automatic retry with validation feedback. The Instructor pattern for .NET."
gh repo edit --add-topic csharp --add-topic dotnet --add-topic llm --add-topic openai --add-topic anthropic --add-topic structured-outputs --add-topic json-schema --add-topic ai --add-topic instructor --add-topic validation
```

**4.5 — Check CI went green.** Go to the Actions tab. It builds on Linux, Windows and macOS.
If it is red, send me the log.

---

## Part 5 — Publish to NuGet (20 minutes)

**5.1 — Create a NuGet account.** <https://www.nuget.org> → Sign in with Microsoft. Free.

**5.2 — Reserve the package ID.** Do this **now**, before anything else — IDs are first-come.
Publishing in 5.4 claims it automatically, but you can also reserve the `InstructorSharp.*`
prefix at <https://www.nuget.org/account/Manage> under "Reserved ID prefixes" (ask for
`InstructorSharp`, so nobody else can publish `InstructorSharp.Anything`).

**5.3 — Create an API key.** <https://www.nuget.org/account/apikeys> → Create:

- Key name: `InstructorSharp-release`
- Scopes: **Push new packages and package versions**
- Glob pattern: `InstructorSharp*`

Copy the key immediately — it is shown once.

**5.4 — Add it to GitHub so releases are automatic:**

```powershell
gh secret set NUGET_API_KEY
# paste the key when prompted
```

**5.5 — Ship v0.1.0:**

```powershell
git tag v0.1.0
git push origin v0.1.0
```

That tag triggers `.github/workflows/release.yml`, which runs the tests, packs, pushes to NuGet
and creates a GitHub release. It appears on nuget.org within about 15 minutes.

> **Start at 0.1.0, not 1.0.0.** It signals the API may still move, which is honest for a library
> with no production users yet. Go to 1.0.0 once a few people are actually using it.

---

## Part 6 — Launch (the part that decides whether this gets stars)

**A repo nobody hears about gets zero stars regardless of quality.** The code is the easy half.

Do not launch the day you publish. Wait until Part 3 passed against a real model and CI is green.

### 6.1 — The demo asset

Record a 30-second terminal GIF of `dotnet run --project samples/InstructorSharp.Sample`. It
shows the model getting it wrong, being told exactly what was wrong, and fixing it. Use
[ScreenToGif](https://www.screentogif.com/) (free). Put it at the top of the README. **This one
asset will do more work than everything else here.**

### 6.2 — Where to post, in order

1. **LinkedIn — you already have 2,368 followers.** This is your single biggest advantage and it
   costs nothing. Do not write "I built a library." Write the problem story:

   > Every .NET team doing LLM extraction writes the same broken retry loop. The model returns
   > an invalid object, you retry the identical request, and it fails again at the same rate —
   > because a retry is a re-roll, not a correction.
   >
   > Python solved this in 2023 with Instructor. So did TypeScript, Go, Ruby, Elixir and Rust.
   > C# never got it.
   >
   > So I wrote it. It feeds the exact validation errors back to the model — `$.lines[2].amount`,
   > not "try again" — and it runs on .NET Framework 4.8, which matters if you maintain the kind
   > of enterprise service I spent five years on.
   >
   > [GIF] [link]

2. **r/dotnet on Reddit.** Read the self-promotion rules first. Lead with the problem, not the
   package. Title: *"There was no Instructor for .NET, so I built one — typed LLM output with
   automatic validation repair"*.

3. **Hacker News**, Show HN. Post Tuesday–Thursday, 9–11am US Eastern. Title:
   *"Show HN: InstructorSharp – structured LLM output for C#, with validation-driven retry"*.
   Be in the comments for the first three hours or it dies.

4. **The Instructor community.** Open an issue on
   [567-labs/instructor](https://github.com/567-labs/instructor) telling them a C# port exists,
   and ask to be listed. Their README links every other language port. **This is the highest-value
   single action in this document** — it puts you in front of the exact audience, permanently.

5. **`awesome-dotnet` and `awesome-dotnet-core`.** Submit a PR adding your library. Low effort,
   permanent backlink.

6. **The .NET AI Community Standup.** They feature community projects. Mention it in their chat.

### 6.3 — What to do when someone opens an issue

Reply within 24 hours, every time, even if only "thanks, looking at this tonight." Maintainer
responsiveness is what converts a curious visitor into a star, and it is visible forever in the
issue history that a hiring manager will read.

---

## Part 7 — What to build next

Do **not** add features at random. In rough priority:

1. **A recorded-response test suite.** Capture real responses from OpenAI, Anthropic and Ollama
   once, replay them in CI. This is how you avoid a provider change silently breaking you.
2. **Source-generated schema support.** Full AOT story, closing the last gap.
3. **`net472` in the test matrix.** You *claim* Framework support; prove it in CI. Until then the
   claim rests on the netstandard2.0 build compiling, not on tests running there.
4. **Retry with backoff on transport errors.** Right now a 503 ends the loop. `Polly` or
   `Microsoft.Extensions.Http.Resilience` would handle it.
5. **A `docs/` site.** Only once there is traffic to justify it.

### Known limitations — be upfront about these

- **No live provider test has run yet.** Part 3 is not optional.
- Framework 4.8 support is compiled and analyzer-clean but not yet executed under CI.
- Streaming has no repair loop, by design: you cannot silently retry output a user has already
  seen. This is documented in the XML docs.
- Nested `$defs` in enveloped list schemas are handled, but with unusual generic types it is
  worth eyeballing the generated schema once.

---

## Part 8 — The honest expectation

The strategy doc estimated 2,000–8,000 stars. Treat that as the ceiling of a wide range, not a
forecast. Most good libraries get under 100 stars in year one. What is much more reliably true:

- **This is a far better interview artifact than another CRUD app.** "I found a two-year gap in
  the .NET ecosystem, checked the competition properly, and shipped a tested library with a
  public extension seam" is a senior-engineer story regardless of star count.
- **The four bugs the tests caught are themselves an interview story.** Especially the
  sync-over-async one, because it is the failure mode you already write about.
- **NuGet downloads matter more than stars for a library**, and they are invisible on your
  profile. Put the download badge in your GitHub profile README.

Ship it, then go back to finding #1 and #3 on the list. The constellation is the strategy — this
is one piece of it.

---

## Quick command reference

```powershell
# build and test everything
dotnet build -c Release ; dotnet test

# see the repair loop work, no API key needed
dotnet run --project samples/InstructorSharp.Sample

# build packages locally
dotnet pack -c Release -p:IsTrimmable=true -o ./artifacts

# ship a new version
git tag v0.2.0 ; git push origin v0.2.0
```
