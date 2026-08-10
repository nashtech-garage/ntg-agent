# Agent Skills Implementation

> Status: Built · Branch: `feature/agent-skill`

## Status

| Phase | State | Commits |
|---|---|---|
| 0 — Correct `A2uiPrompt.RenderGuide` | Done | `843f711`; standing regression check `f2d28d2` |
| 1 — Storage | Done | `2b61da8` |
| 2 / 2b — ZIP import and its security controls | Done | `13279d0`; surface validator `6e51e1e`, `b2cc9c9` |
| 3 — Runtime (three tiers) | Done | `292194b`, wired into the chat path in `8244027` |
| 4 — Admin UI | Done | API `418785b`, Blazor `d33e8b6` |
| 5 — Demo packages | Partial | `6d32bce` ships `travel-planning` and the packer; no startup seeder, no `ticket-booking` |
| 6 — Tests | Done | `b2cc9c9`, `418785b`, `382655d` |

### Still open

- **No rehydration renderer for `render_skill_surface`.** The call *is* persisted and
  `agentMessages.ts` rebuilds it on reload, but only `get_weather` has a frontend renderer, and the
  A2UI middleware only sees the live SSE stream. A reloaded conversation drops the surface silently.
- **`render_a2ui` is declared on every run of every agent**, so `A2uiPrompt.RenderGuide` — ~130
  lines telling the model to hand-author a component tree — is prepended even when the agent has a
  skill bound that owns the surface, competing with the skill catalog. `route.ts` now reads
  `A2UI_FREEFORM_TOOL` so a deployment can turn the declaration off, but that switch is
  per-deployment: the route knows the agent id and nothing else, skill bindings are server state,
  and the only endpoint exposing them is Admin-only. The per-agent fix belongs in `AgentService`,
  which holds `activeSkills` and the `frontendToolNames.Contains(A2uiPrompt.RenderToolName)` gate a
  few lines apart. Note the middleware only *declares* the tool — it recognises and renders a
  `render_a2ui` call regardless of who declared it (`a2uiToolNames` defaults to `["render_a2ui"]`),
  so the declaration can move server-side without touching the frontend.
- **No import-time cap on the `SKILL.md` body.** `name` and `description` are bounded; the body is
  bounded only by the 5 MB package / 20 MB uncompressed caps, which is not a context budget.
- **`values` can still freeze an input.** `SurfaceRenderFunction.Merge` replaces wholesale for
  anything that is not an object-into-object, so `{"form": "text"}` overwrites a seeded `/form`
  object with a scalar and every path under it stops resolving. That is the unseeded-path failure
  the surface validator exists to prevent, reintroduced at run time by the model, past the point
  validation reaches. Garbage `values` are covered by tests; a *well-formed* value of the wrong
  shape is not.
- **Skill tests all use `UseInMemoryDatabase`.** `GetActiveSkillAssetAsync` runs a `SelectMany`
  from `AgentSkills` through `Skill.Assets` filtered on `RelativePath`; its SQL Server translation
  is unproven.
- **`SkillContentGuard.EscapeForDisplay` has no caller.** The Phase 2b control "render the body in
  the confirm dialog with invisible characters escaped" is written but not wired to the UI.
- **Replacement is logged but not confirmed.** `ImportOutcome.Replaced` is computed and never
  reaches the client, so re-importing over an existing skill looks identical to a first import —
  the silent side effect the Known limitations section says it must not be.

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

## Three findings that shape the design

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
> depends partly on the model") understated this. It is not model variance; the prop names are
> wrong. That doc was corrected alongside Phase 0 in `843f711`.

### 3. Surfaces do not update in place

The middleware keys activity messages as `` `a2ui-surface-${surfaceId}-${outerCallId}` ``.
`outerCallId` differs per tool call, so re-rendering the same `surfaceId` produces a **new
card**. Multi-step flows stack. This is accepted, not fought: the flow is designed to read as
a visible wizard trail.

## Architecture

Planned before Phase 1 and unchanged by implementation, so it is left as written.

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

### Phase 3 — Runtime: three tiers, not one

This phase was written as a single work item — the render tool — which was an error of omission
rather than of design: the architecture diagram above already shows three tiers, and progressive
disclosure does not work with any of them missing. All three shipped.

**Tier 1 — the catalog.** `SkillPrompt.BuildCatalog` renders the bound skills' names and
descriptions into one system message; `AgentService` inserts it at the head of the history and
attaches the two skill tools — all of it only when the agent has at least one skill bound and
enabled. An agent with none gets nothing, so its runs are byte-identical to before.

Two things about the insert are load-bearing. It goes in *before* the `A2uiPrompt` render guide,
because both use `Insert(0, …)` and each insert pushes the previous one further from the user's
turn — writing them in the intuitive order buries the catalog behind 131 lines of A2UI guidance.
And the whole block is wrapped in a `try`: this runs before the first yield of an async iterator,
so an exception escapes `ChatStreamingAsync` entirely and surfaces as `RUN_ERROR` with no answer at
all. Skills are decoration on a run; they degrade, they do not abort it.

This is also the widest blast radius in the feature, exactly as the Phase 2b content table
predicted: descriptions come from uploaded packages, are concatenated into one system message, and
are injected on every run with no activation step anyone can decline. `SkillContentGuard` rejects
newlines and structural markers at import; `BuildCatalog` fences the list on both sides and
sanitizes each entry regardless, so the property holds for rows stored before that check existed.

**Tier 2 — `load_skill`.** Returns the full `SKILL.md` body. `SkillTools` closes over the agent id
at construction rather than accepting it as a tool parameter, and
`SkillRegistry` re-checks the binding on every lookup — so a model that invents or remembers a
skill name belonging to another agent gets a refusal, not a body.

**Tier 3 — `render_skill_surface`.** `Services/Skills/SurfaceRenderFunction.cs`, a custom
`AIFunction`, deliberately **not** `CapturingAIFunction`:

1. load `assets/<surface>.json` from `SkillAssets`, scoped to the agent's own bindings
2. deep-merge `values` into the template's `data`
3. write the full `{"a2ui_operations": [createSurface, updateComponents, updateDataModel]}`
   into `RenderableToolCapture`
4. return a short ack to the model

Step 4 is why `CapturingAIFunction` cannot be reused: it returns the full result to the model,
which would dump the entire surface JSON back into context. The ops go to the browser; the
model gets a receipt.

Failures return as text rather than throwing, for the same reason the tier-1 injection is guarded:
a throw aborts the run, a message lets the model correct the surface name or fall back to prose.

**`RenderableToolCapture` is deliberately left unchanged.** The plan said to add
`render_skill_surface` to its frozen set. That set has exactly one consumer — `AgentFactory`, which
uses `IsRenderable` to decide whether to wrap an *MCP* tool in `CapturingAIFunction`.
`SurfaceRenderFunction` writes to the capture itself and is never wrapped, so an entry there would
change no behaviour while reading, to the next person, as though it were what makes the surface
render. Dead configuration that looks live is worse than none.

### Phase 4 — Admin UI

Following existing Blazor conventions in `NTG.Agent.Admin`:

- **Skills page** — list, import (`InputFile`, modeled on `AddKnowledgeForm.razor`), view,
  delete, and export back to `.zip`. Export is a cheap round-trip and makes the demo
  tellable: import → bind → run → export → share.
- **Skills sub-tab in `ToolManagementTab.razor`** — per-agent binding, mirroring the existing
  Inner Agent bindings sub-tab.

### Phase 5 — Demo packages

`travel-planning.zip` is committed under `seed/skills/`, built by `scripts/pack-skills.py` from
the checked-in source tree so the zip and its sources cannot drift (`--check` fails CI when they
have). The zips are byte-reproducible — sorted entries, pinned timestamps — so repacking an
unchanged skill produces no git churn.

Neither the startup seeder nor `ticket-booking` shipped: the package is imported through the Admin
UI like any other. The seeder is worth having for the reason originally given — it exercises the
same importer on every cold start — but nothing depends on it.

`travel-planning`:

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
    rollback, malformed archive returns a typed error not a 500
- `SurfaceValidatorTests` — including one case per dead prop name from the old render guide as a
  regression anchor, plus a deeply-nested JSON case (`JsonSerializerOptions.MaxDepth`)
- `SkillRegistryTests` — catalog scoping by `AgentSkills`, lenient-vs-strict validation rules
- `SkillPromptTests` / `SurfaceRenderFunctionTests` — added with the runtime. The ones worth
  knowing about assert *absences*: no bound skills produces no catalog and no tools, and garbage
  `values` leave the template's defaults in place rather than throwing
- `A2uiCatalogDriftTests` — not in the original plan. `A2uiCatalog` is a hand-flattened snapshot of
  `basic_catalog.json`, which lives in the frontend's `node_modules` where the Orchestrator cannot
  reach it. This test re-derives the catalog from the schema, resolving the `allOf`/`$ref`
  composition, and fails on divergence — so bumping the npm package surfaces here rather than as a
  mystery import rejection months later

Do **not** write a test asserting that a forged `entry.Length` lets extra bytes through — .NET
clamps, the test fails, and the likely "fix" is deleting the assertion. Test the streaming
counter instead; it is the control that survives a runtime change.

All skill tests use `UseInMemoryDatabase`, which is the gap noted under "Still open": the LINQ
compiles and runs, but its SQL Server translation is untested.

## Corrections found during implementation

Each of these contradicts or sharpens something written above, and each is documented at its site
in the code.

- **The dead A2UI prop names are upstream, not ours.** Finding 2 reads as though the render guide
  was written carelessly. It was not. `@ag-ui/a2ui-middleware` ships its own catalog block inside
  its bundled prompt, and that block names `text`, `textFieldType`, `checked`, `selections`,
  `minValue`/`maxValue` and `maxAllowedSelections` — the same seven dead props, still wrong in
  `dist/index.mjs` at v0.0.6. Anyone writing a guide from the middleware's own text lands on
  exactly this set. **The wrong copy never reaches a model here, though** — verified against
  v0.0.6: `A2UI_PROMPT` is exported but referenced nowhere inside the package, and the guidance the
  middleware *does* inject (`RENDER_A2UI_TOOL_GUIDELINES`, ~2 KB, correct as far as it goes) is
  written to `RunAgentInput.context`, which `AgUiRunRequest` does not bind. `A2uiPrompt.RenderGuide`
  is the only A2UI guidance on the wire, which is what the comments on `A2uiPrompt` and
  `AgentService` already say. An earlier revision of this bullet claimed the wrong prompt was
  injected on every run; it is not.
- **`SurfaceValidator` could not validate `Tabs`.** `Tabs.tabs` is an array of `{ title, child }`
  objects, so its reference sits one level below every other component's, and the traversal only
  looked at top-level reference properties. It was wrong in both directions at once: a *valid* Tabs
  surface was rejected with every pane reported as an orphan that "will not render", while a
  genuinely dangling `child` produced no error at all. Tabs was unusable in a skill package until
  `b2cc9c9`. The same commit made the walk iterate the component array rather than the id map, so a
  duplicated id no longer hides the shadowed copy's own defects.
- **Re-import updates `Skill` in place rather than delete-and-insert.** `AgentSkill` rows key off
  `Skill.Id`, so replacing the row would silently unbind the skill from every agent using it —
  turning "re-upload a fixed version" into "re-upload, then remember to re-bind everywhere", which
  is the step someone forgets before a demo.
- **The content guard's newline defence had a hole.** `U+0085`, `U+2028` and `U+2029` are above
  `0x20`, so they cleared the control-character check, and they are not zero-width, so they cleared
  that one too — while tokenizers and markdown renderers alike still treat them as line breaks.
  That is a way to forge a new line inside a value whose entire defence is that it cannot contain
  one. The guard rejects them at import, and `SkillPrompt.Sanitize` now asks Unicode what a
  character *is* (`LineSeparator`, `ParagraphSeparator`, `Control`) rather than checking it against
  a hand-maintained list of known offenders, which closes the class instead of three members of it.
- **The streaming byte counter is unreachable on .NET 10** — see the blockquote in Phase 2b. Kept
  deliberately as the control that survives the clamping changing.

## Decisions

| Decision | Outcome | Rationale |
|---|---|---|
| Storage: SQL vs disk | **SQL — settled** | Per-agent binding needs a table regardless; disk would mean two sources of truth plus a writable volume under Aspire |
| `scripts/` support | **Out of v1 — settled** | Arbitrary code execution from an uploaded archive needs a sandbox story we do not have; the extension allowlist admits no executable type, so this is enforced rather than merely intended |
| Surface template location | **`assets/`** | Spec-conventional and one level deep from `SKILL.md`; supersedes the `surfaces/` directory used in the first draft |
| Skill activation | **Dedicated `load_skill` tool** | The spec's "dedicated tool activation" pattern; maps cleanly onto the existing `AITool` plumbing |
| Catalog gating | **Per-agent `AgentSkills` bindings** | Changed during Phase 3. Gating on the presence of `render_a2ui` would have put every skill in front of every A2UI-capable agent; binding is an explicit admin action, is what the Admin UI already exposes, and makes "no skills bound" a genuine no-op rather than a smaller prompt |

The first two were the reversible ones, and neither was reversed.

## Sequencing

**0 → 1 → 2 (+2b) → 3** yields a working demo driven through the API. **4** makes it
demoable by a human clicking buttons. **5** and **6** are packaging and safety net.

## Known limitations / not done

- **No reload rehydration** — inherited from the A2UI implementation; surfaces render live but
  are not replayed on conversation reload. Do not refresh mid-demo. The gap is narrower than it
  looks: the `render_skill_surface` call is persisted and rebuilt into the message list on reload,
  so what is missing is only a frontend renderer for it. See "Still open".
- **Surfaces stack per step** — see Finding 3. Intended, presented as a wizard trail.
- **No `scripts/` execution** — see Decisions.
- **No real APIs** — itineraries and prices are fabricated by the model. Skills are instructed
  to fabricate freely but never to produce a booking reference, PNR or payment confirmation.
- **No skill versioning** — re-importing a skill of the same name overwrites its body and assets
  in place, keeping the row (see Corrections). Version history is out of scope, but the SHA-256 of
  each imported body is logged so an incident can still be traced to specific content.
  Replacement is explicit and logged; it is **not yet confirmed** in the UI, which is the one part
  of this bullet that is still an open defect rather than an accepted limit — an admin upload can
  currently replace a bundled skill without anyone noticing.
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
