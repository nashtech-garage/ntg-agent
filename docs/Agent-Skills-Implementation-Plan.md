# Agent Skills Implementation

> Status: Planned · Branch: `feature/agent-skill`

## Context

[Agent Skills](https://agentskills.io) is an open standard (originally Anthropic, now
adopted by ~44 agent products) for packaging procedural knowledge as a folder containing a
`SKILL.md` file — YAML frontmatter (`name`, `description`) plus Markdown instructions,
optionally bundled with `scripts/`, `references/` and `assets/`. Agents load skills by
**progressive disclosure**: name + description at startup (~100 tokens each), the full body
only on activation, bundled files only when referenced.

This change adds skills to the Orchestrator so that **the active skill decides what the agent
renders**. A "travel planning" skill drives a three-step booking flow as A2UI surfaces in the
chat; a "ticket booking" skill drives a different one. Skills are **imported as `.zip`
packages** through the Admin UI, not baked into the repo, so they can be authored, shared and
swapped without a rebuild.

Scope is a demonstration: skills fabricate plausible data, no real APIs are called, nothing
is booked.

## Two findings that shape the design

### 1. A server-side tool can render A2UI directly

`@ag-ui/a2ui-middleware` inspects every `TOOL_CALL_RESULT` with
`tryParseA2UIOperations(content)`. If a **server-side** tool's result is JSON shaped
`{"a2ui_operations": [...]}`, the middleware converts those ops into `ACTIVITY_SNAPSHOT`
events and renders them — the LLM never emits a component array.

The existing weather-card plumbing already carries this end to end: `RenderableToolCapture` →
`AgentService.DrainRenderableToolCalls` → `AgUiController` `TOOL_CALL_RESULT` (line ~159).

| | Path A — `render_a2ui` (shipped) | Path B — server tool returns ops (new) |
|---|---|---|
| Who writes the component JSON | LLM, every turn | Us, once, as a template |
| Layout consistency | varies per run | identical every run |
| Malformed JSON | whole surface fails | not possible |
| Bindings + `data` seeding | model must get both right | baked into the template |
| Streaming cost | large `TOOL_CALL_ARGS` | model emits a few values |

**Both paths stay.** Path A keeps serving freeform requests ("build me a card with…"),
Path B serves skill-driven surfaces — the same coexistence as the hardcoded weather card.

Path B requires **no frontend changes**: it reuses `createA2UIMessageRenderer` and
`interactiveCatalog` unchanged.

### 2. `A2uiPrompt.RenderGuide` has the input props wrong

Validated against `@a2ui/web_core/src/v0_9/schemas/basic_catalog.json`. The guide's own
signup-form example fails eight ways:

| Guide says | Schema requires |
|---|---|
| `TextField.text: {path}` | `TextField.value: {path}` |
| `TextField.textFieldType` | `TextField.variant` |
| `CheckBox.checked: {path}` | `CheckBox.value: {path}` (required) |
| `ChoicePicker.selections: {path}` | `ChoicePicker.value: {path}` |
| `Slider.minValue` / `maxValue` | `Slider.min` / `max` (`max` required) |
| `Button.variant: secondary\|text` | `default\|primary\|borderless` |

Plus an internal contradiction: the guide says use `Column` as root and not to double-frame,
then both examples use `Card` as root. Models copy examples over prose.

**Why this was never noticed:** the overrides in `interactiveCatalog.tsx` keep local React
state (so inputs are always typeable) and mirror every change to `/__inputs/<id>` (so
submissions always reach the agent). The safety net masks the fact that the data-model
binding never actually binds. The visible consequence is that **pre-filling a form has never
worked**, and a Button's named context paths resolve to the seed value rather than user input.

This matters now because the travel flow pre-fills step 1 from what the user already said and
renders a summary in step 3 — both need real binding.

> The "Known limitations" note in `A2UI-Implementation-Plan.md` ("binding consistency still
> depends partly on the model") understates this. It is not model variance; the prop names are
> wrong. That doc should be corrected alongside Phase 0.

### 3. Surfaces do not update in place

The middleware keys activity messages as `` `a2ui-surface-${surfaceId}-${outerCallId}` ``.
`outerCallId` differs per tool call, so re-rendering the same `surfaceId` produces a **new
card**. Multi-step flows stack. This is accepted, not fought: the flow is designed to read as
a visible wizard trail.

## Architecture (as planned)

```
Admin uploads travel-planning.zip
  │
  ▼
SkillsController (Admin only)
  │  SkillPackageImporter:  archive safety → spec validation → surface validation
  ▼
Skills / SkillAssets / AgentSkills          (SQL, AgentDbContext)
  │
  ▼  per run, for skills bound to this agent
AgentService
  │  injects skill catalog (name + description only) as a system message   ← tier 1
  │  registers load_skill + render_skill_surface tools
  ▼
LLM calls load_skill("travel-planning")     → full SKILL.md body          ← tier 2
LLM calls render_skill_surface(skill, surface, values)
  │
  ▼
SurfaceRenderFunction
  │  loads assets/<surface>.json from SkillAssets, merges values into the data model
  │  → RenderableToolCapture   (full a2ui_operations)
  │  → returns a short ack to the model (NOT the ops — keeps them out of context)
  ▼
AgentService.DrainRenderableToolCalls → AgUiController TOOL_CALL_RESULT (SSE)
  ▼
@ag-ui/a2ui-middleware  tryParseA2UIOperations → ACTIVITY_SNAPSHOT
  ▼
createA2UIMessageRenderer + interactiveCatalog → surface renders in chat
```

**Bundled skills go through the same importer.** The repo keeps `travel-planning.zip` as a
seed artifact and a startup seeder imports it if absent — one code path, exercised on every
cold start rather than only on upload.

## Phases

### Phase 0 — Correct `A2uiPrompt.RenderGuide`

`Services/Agents/A2uiPrompt.cs` — fix the six prop names in the table above, fix the
`Card`/`Column` root contradiction in both examples, correct the `Button` variant enum.
Correct the matching claim in `docs/A2UI-Implementation-Plan.md`.

Affects Path A only, but it is a prerequisite: Path B templates and Path A output must agree
on the catalog or the two will drift.

### Phase 1 — Storage

Three tables in `AgentDbContext`, mirroring patterns already in the codebase:

| Table | Columns | Mirrors |
|---|---|---|
| `Skills` | `Id, Name, Description, Body, Version, License, Compatibility, ImportedByUserId, CreatedAt, UpdatedAt` | `Agents` |
| `SkillAssets` | `SkillId, RelativePath, Content` | — |
| `AgentSkills` | `AgentId, SkillId, IsEnabled` + composite PK | `AgentInnerAgents` |

`AgentSkills` scopes the injected catalog per agent, consistent with how
`AgentFactory.GetInnerAgentToolsAsync` gates inner agents today.

One EF migration.

### Phase 2 — ZIP import

`Services/Skills/SkillPackageImporter.cs` — takes a stream, returns a validated skill or a
list of line-item errors. Package layout per agentskills.io:

```
travel-planning.zip
└── travel-planning/
    ├── SKILL.md          # required
    ├── assets/           # surface templates (spec-conventional home)
    └── references/
```

Lenient on one point, per the spec's client-implementation guidance: if `SKILL.md` sits at the
zip root with no wrapping directory, fall back to the zip filename as the skill name rather
than rejecting.

**Validation gate, in order:**

1. **Archive safety** — see Phase 2b
2. **Spec compliance** — `name` 1–64 chars, lowercase alphanumerics and hyphens only, no
   leading/trailing/consecutive hyphens, matches the parent directory name; `description`
   non-empty and ≤1024 chars; frontmatter parses
3. **A2UI surface validation** — every `assets/*.json` shaped like a surface must pass: root
   component exists with id `root` and is a layout component; all child references resolve;
   no orphans or self-references; component names, required props and enum values check
   against `basic_catalog.json`; and **every `{path}` binding has a matching seed in `data`**

Step 3 is the highest-value item in the plan. A broken surface becomes a **rejected upload
with a readable error list** instead of a blank card during a demo. The same validator backs
the unit tests, and it already caught the eight defects listed in Finding 2.

### Phase 2b — Import security

Not a later hardening pass — written with the importer, because retrofitting path validation is
how zip-slip bugs survive. A `SKILL.md` is instructions injected directly into an LLM's context;
Snyk's February 2026 ToxicSkills audit of 3,984 published skills found ≥1 security flaw in
36.8%, with 91% of confirmed-malicious packages using prompt injection.

#### Three premises this section originally got wrong

1. **There is no extraction root.** We store to SQL and never call `ExtractToDirectory`, so we
   inherit *none* of .NET's built-in traversal protection — and "escaping the root" means
   writing another skill's `SkillAssets` row, not reaching the filesystem.
2. **`Path.GetFullPath`-based validation passes in our CI while being vulnerable.** On Linux
   `\` is an ordinary filename character, so `..\..\evil.md`, `C:\evil.md` and
   `\\server\share\evil.md` normalize to inert single-component names and a `GetFullPath` check
   reports no escape. On Windows the same three are real traversal. Our CI is WSL/Linux.
3. **A per-entry compression-ratio cap has near-zero security value.** Declaring a smaller
   uncompressed size drops a 1028:1 bomb's apparent ratio to 0.0098:1. It catches only honest
   bombs while reading in review as though the bomb class is covered.

A useful measured fact: .NET returns `min(declared_size, actual_deflate_output)`, so forging the
declared size *up* fails safe and forging it *down* truncates the attacker's own payload. The
cheap `sum(entry.Length)` precheck is therefore sound on this runtime — but it depends on
undocumented clamping, so the **streaming byte counter is the actual guarantee**.

> **Confirmed in implementation (Phase 2).** The clamping is real, and it has a consequence worth
> recording: the streaming counter is *unreachable* on .NET 10. A 21 MB zero-bomb declaring its
> true size is stopped by the precheck before a byte is decompressed; the same bomb declaring 64
> bytes is clamped to 64 bytes on read, so the counter never trips. Both cases are covered by
> `SkillPackageImporterTests`, and the second is asserted on *rejection*, not on the mechanism —
> an earlier version of that test asserted the counter fired and failed, which is how the
> behaviour was pinned down. The counter stays as the control that survives the clamping
> changing; it is dead code today and should not be deleted on that basis.

#### Container controls

| Control | Rule |
|---|---|
| Request size | `[RequestSizeLimit]` on the endpoint. **None exists anywhere in the solution today** — the two 50 MB constants live in Blazor `.Client` projects and only constrain the browser. |
| Path validation | Validate the **raw `FullName` string**, never via `Path.*`: reject `\` anywhere, leading `/`, `^[A-Za-z]:`, and any segment that is empty, `.`, `..`, or contains a control char. Assert these reject **on Linux**. |
| Path consistency | Validate and store the **same** value. Validating `Name` while storing `FullName` waves traversal through; the reverse silently flattens `assets/a.json` and `references/a.json` into one row. |
| Entry count | Enforce on **raw upload bytes before constructing `ZipArchive`** — 200k entries cost 22 MB on the wire but ~83 MB of heap merely to enumerate `.Entries`, so an `Entries.Count > N` check runs after the cost is paid. |
| Total size | Streaming byte counter that aborts mid-read. `CopyTo`/`ReadToEnd` defeats this. |
| Duplicate entries | Reject the archive on any duplicate `FullName`, **and** on case-insensitive duplicates. Take neither, not the first. |
| Nested archives | Reject any entry whose first bytes are `PK\x03\x04`, regardless of extension. |
| Encrypted entries | Reject if general-purpose bit 0 is set — used specifically to evade scanners. |
| Compression method | Reject method ∉ {0, 8} explicitly, not via a caught exception. |
| Symlinks / special files | Reject when `(externalAttributes >> 16) & 0xF000 == 0xA000`; reject non-regular files and setuid/setgid bits. No API surface hints this exists, so it is the control most likely to be silently skipped. |
| Extension allowlist | `.md .json .txt .yaml .yml .png .svg`, compared `OrdinalIgnoreCase`. Constrains the *name*, never the *bytes* — an anti-footgun measure, not a security boundary. |
| Path depth | Max 4. A sanity bound, **not** part of the traversal defence. |
| Atomicity | Validate everything, then persist in one transaction. A rejection mid-import must leave zero rows. |
| Authorization | Admin only, `[Authorize(Roles = "Admin")]` as on `AgentAdminController`. |
| Provenance | `ImportedByUserId`, original filename, **and a SHA-256 of the `SKILL.md` body** logged at activation — without it, "which version of this skill was in context that day" is unanswerable given replace-on-reimport. |

**Two duplicate-entry notes.** `ZipArchive.GetEntry` returns the *first* match while dictionary
or EF upsert iteration keeps the *last* — so validating via `GetEntry` and storing via iteration
means **the reviewed content is not the stored content**. That is the sharpest archive-level
attack available here, because it defeats human review of the exact bytes that become LLM
instructions. Separately, `GetEntry` is ordinal/case-sensitive on every platform while SQL
Server's default `..._CI_AS` collation is not, so two legal ZIP entries can collide on the
`(SkillId, RelativePath)` unique index mid-import.

#### Content controls

Every control above operates on the container. The payload is natural language delivered to the
model byte-for-byte intact. These are the parts of that which validation *can* reach:

| Rule | Why |
|---|---|
| Reject runes in `U+E0000–U+E007F` | Unicode tag characters: invisible to a human reviewer, semantically read by the model. No legitimate use in a skill. Highest value per line in the whole matrix. |
| Reject bidi overrides `U+202A–U+202E`, `U+2066–U+2069` | Trojan Source class |
| Reject zero-width `U+200B/C/D`, `U+FEFF` outside a leading BOM | Hidden instruction text |
| Reject control chars `< 0x20` except `\t\r\n` | Truncation and rendering tricks |
| Flag or strip `<!-- -->` in the body | Invisible when rendered, present in the token stream |
| Reject `name`/`description` containing newlines, `---`, or `system:`/`assistant:` | **Widest blast radius of any control here.** Tier-1 descriptions are concatenated into one system message and injected on *every* run for *every* bound skill — no activation needed. The catalog builder must fence or escape rather than raw-concatenate. |
| Normalize NFC before the `name` regex; anchor it `^[a-z0-9-]+$` | Cyrillic homoglyphs pass an unanchored regex, and .NET's `\w` matches Unicode letters by default |
| Render the body in the confirm dialog with invisible characters escaped | A reviewer approving text they cannot see is the documented failure mode |

#### What validation cannot defend

Instruction override, credential-exfiltration directives, tool-misuse steering, cross-skill
activation hijacking, obfuscated payloads, and phishing surface JSON are all **semantic** and
survive every control above. The cause is context flattening: the model processes our
instructions and third-party skill content as the same natural language in the same window.

The mitigations are architectural, and mostly already chosen: **`scripts/` out of v1** removes
the RCE class entirely and is doing more security work than every control in the table above
combined — if it is reversed under demo pressure, this whole section stops covering the threat.
Admin-only upload plus provenance reduces the rest to an insider or compromised-account threat.

#### Implementation order

Request size limit → raw-string path validation → duplicate detection → streaming counter and
pre-parse byte cap → content character filters and frontmatter injection → symlink/encryption/
compression-method → the rest. The importer returns **all** line-item errors, not the first,
matching the surface validator.

### Phase 3 — Surface render tool

`Services/Skills/SurfaceRenderFunction.cs` — a custom `AIFunction`, deliberately **not**
`CapturingAIFunction`. Signature `render_skill_surface(skill, surface, values)`:

1. load `assets/<surface>.json` from `SkillAssets`
2. deep-merge `values` into the template's `data`
3. write the full `{"a2ui_operations": [createSurface, updateComponents, updateDataModel]}`
   into `RenderableToolCapture`
4. return a short ack (`{"status":"rendered"}`) to the model

Step 4 is why `CapturingAIFunction` cannot be reused: it returns the full result to the model,
which would dump the entire surface JSON back into context. The ops go to the browser; the
model gets a receipt.

Add `render_skill_surface` to the frozen set in `RenderableToolCapture`.

### Phase 4 — Admin UI

Following existing Blazor conventions in `NTG.Agent.Admin`:

- **Skills page** — list, import (`InputFile`, modeled on `AddKnowledgeForm.razor`), view,
  delete, and export back to `.zip`. Export is a cheap round-trip and makes the demo
  tellable: import → bind → run → export → share.
- **Skills sub-tab in `ToolManagementTab.razor`** — per-agent binding, mirroring the existing
  Inner Agent bindings sub-tab.

### Phase 5 — Demo packages

`travel-planning.zip` and `ticket-booking.zip` committed as seed artifacts, imported at
startup if absent.

`travel-planning` (drafted, pending the `assets/` move):

| Step | Surface | Agent calls | User submits |
|---|---|---|---|
| 1. Collect | `trip-search` | `render_skill_surface` | `trip_search_submit` |
| 2. Offer | `trip-results` | `render_skill_surface` | `trip_option_selected` |
| 3. Review | `trip-confirm` | `render_skill_surface` | `trip_booking_confirmed` / `trip_change_requested` |

`SKILL.md` carries procedure, not syntax — it never repeats binding rules, which arrive via
the already-injected `A2uiPrompt`. Its load-bearing instruction is *render exactly one surface
per turn, then stop*; without it the model renders all three steps in one go.

### Phase 6 — Tests

`tests/NTG.Agent.Orchestrator.Tests/Services/`:

- `SkillPackageImporterTests` — **security cases first**, grouped as in Phase 2b:
  - *Path* — parent/deep traversal, absolute path, backslash separators, drive letter, UNC,
    embedded dot segments, excess depth, cross-skill asset write, and a
    `Validation_And_Storage_Use_The_Same_Path` test importing both `assets/a.json` and
    `references/a.json` to catch the flattening variant
  - *Exhaustion* — classic bomb, total cap, **abort mid-stream rather than precheck-only**,
    forged-ratio via absolute cap, entry-count cap rejected before `.Entries` is touched,
    nested archive, server-side oversized upload, single huge entry
  - *Structural* — duplicate names, case-only duplicates, missing/zero-byte `SKILL.md`,
    multiple top-level dirs, name/directory mismatch, overlong name, null byte, non-allowlisted
    and double extensions
  - *Content* — Unicode tag characters, bidi overrides, zero-width, control chars, HTML
    comments, frontmatter injection via `description`, homoglyph `name`
  - *Other* — symlink entry, encrypted entry, unsupported compression method, partial-import
    rollback, malformed archive returns a typed error not a 500, seeder shares the importer
- `SurfaceValidatorTests` — including the guide's own broken example as a regression anchor,
  plus a deeply-nested JSON case (`JsonSerializerOptions.MaxDepth`)
- `SkillRegistryTests` — catalog scoping by `AgentSkills`, lenient-vs-strict validation rules

Do **not** write a test asserting that a forged `entry.Length` lets extra bytes through — .NET
clamps, the test fails, and the likely "fix" is deleting the assertion. Test the streaming
counter instead; it is the control that survives a runtime change.

## Decisions

| Decision | Recommendation | Rationale |
|---|---|---|
| Storage: SQL vs disk | **SQL** | Per-agent binding needs a table regardless; disk would mean two sources of truth plus a writable volume under Aspire |
| `scripts/` support | **Out of v1** | Arbitrary code execution from an uploaded archive needs a sandbox story we do not have; the demo needs only templates and instructions |
| Surface template location | **`assets/`** | Spec-conventional and one level deep from `SKILL.md`; supersedes the `surfaces/` directory used in the first draft |
| Skill activation | **Dedicated `load_skill` tool** | The spec's "dedicated tool activation" pattern; maps cleanly onto the existing `AITool` plumbing |
| Catalog gating | **Presence of `render_a2ui`**, as `A2uiPrompt` does today | No admin config step for the demo; per-agent `AgentTools` opt-in is the productionization path |

Both of the first two are reversible and open to challenge before Phase 2 starts.

## Sequencing

**0 → 1 → 2 (+2b) → 3** yields a working demo driven through the API. **4** makes it
demoable by a human clicking buttons. **5** and **6** are packaging and safety net.

## Known limitations / not done

- **No reload rehydration** — inherited from the A2UI implementation; surfaces render live but
  are not replayed on conversation reload. Do not refresh mid-demo.
- **Surfaces stack per step** — see Finding 3. Intended, presented as a wizard trail.
- **No `scripts/` execution** — see Decisions.
- **No real APIs** — itineraries and prices are fabricated by the model. Skills are instructed
  to fabricate freely but never to produce a booking reference, PNR or payment confirmation.
- **No skill versioning or update-in-place** — re-importing a skill of the same name replaces
  it. Version history is out of scope, but the SHA-256 of each imported body is recorded so an
  incident can still be traced to specific content. Replacement must be an explicit, logged,
  confirmed action rather than a silent side effect: an admin upload can otherwise replace a
  bundled seed skill without anyone noticing.
- **Content-level injection is not defended, only bounded** — see Phase 2b § "What validation
  cannot defend". This is accepted for a demo with admin-only import.

## Verification

- Builds: `dotnet build NTG.Agent.Orchestrator`; `npm run build` in `my-copilot-app`
- `dotnet test` — importer security cases and surface validator
- Manual e2e (AppHost running, agent bound to `travel-planning`, `render_a2ui` present):
  1. Import `travel-planning.zip` via the Admin UI → appears in the list, binds to an agent
  2. "Help me plan a trip to Da Nang in September" → `trip-search` renders, fields are
     editable, **pre-filled destination shows** (regression check for Phase 0)
  3. Submit → three differentiated options render with prices
  4. Pick one → submit → `trip-confirm` shows a correct summary and total
  5. Confirm → agent replies in text and states no booking was made
  6. Import a deliberately broken zip (zip slip, bad prop name) → rejected with a readable
     error list

## References

- Agent Skills overview: https://agentskills.io
- Specification: https://agentskills.io/specification
- Adding skills support to an agent: https://agentskills.io/client-implementation/adding-skills-support
- Best practices for skill creators: https://agentskills.io/skill-creation/best-practices
- Spec + reference library: https://github.com/agentskills/agentskills
- A2UI v0.9 spec: https://a2ui.org/specification/v0.9-a2ui/
- Prior work in this repo: `docs/A2UI-Implementation-Plan.md`, `docs/agents-as-tools-architecture.md`
