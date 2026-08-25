# Skill package import — threat model and controls

Why the ZIP importer is shaped the way it is. A `SKILL.md` is instructions injected directly
into an LLM's context, so an uploaded package is untrusted input with an unusually direct path
to the model. Snyk's February 2026 ToxicSkills audit of 3,984 published skills found at least
one security flaw in 36.8%, and 91% of confirmed-malicious packages used prompt injection.

This was written alongside the importer rather than as a later hardening pass, because
retrofitting path validation is how zip-slip bugs survive.

Implementation: `SkillPackageImporter`, `SkillContentGuard`, `SkillPrompt.Sanitize`.
Author-facing rules are in [Writing-a-SKILL.md](Writing-a-SKILL.md) § Forbidden.

## Three premises that look right and are not

1. **There is no extraction root.** Packages are stored to SQL; `ExtractToDirectory` is never
   called, so none of .NET's built-in traversal protection applies. "Escaping the root" here
   means writing another skill's `SkillAssets` row, not reaching the filesystem.
2. **`Path.GetFullPath`-based validation passes in CI while being vulnerable.** On Linux `\` is
   an ordinary filename character, so `..\..\evil.md`, `C:\evil.md` and `\\server\share\evil.md`
   normalize to inert single-component names and a `GetFullPath` check reports no escape. On
   Windows all three are real traversal. CI runs on Linux.
3. **A per-entry compression-ratio cap has near-zero value.** Declaring a smaller uncompressed
   size drops a 1028:1 bomb's apparent ratio to 0.0098:1. It catches only honest bombs while
   reading in review as though the bomb class is covered.

A useful measured fact: .NET returns `min(declared_size, actual_deflate_output)`, so forging the
declared size *up* fails safe and forging it *down* truncates the attacker's own payload. The
cheap `sum(entry.Length)` precheck is therefore sound on this runtime.

> **Consequence, confirmed in implementation.** The streaming byte counter is *unreachable* on
> .NET 10. A 21 MB zero-bomb declaring its true size is stopped by the precheck before a byte is
> decompressed; the same bomb declaring 64 bytes is clamped to 64 bytes on read, so the counter
> never trips. Both cases are covered by `SkillPackageImporterTests`, and the second asserts on
> *rejection*, not on the mechanism — an earlier version of that test asserted the counter fired
> and failed, which is how the behaviour was pinned down. The counter stays as the control that
> survives the clamping changing. It is dead code today and must not be deleted on that basis.

## Container controls

| Control | Rule |
|---|---|
| Request size | `[RequestSizeLimit]` on the endpoint. **None exists anywhere in the solution today** — the two 50 MB constants live in Blazor `.Client` projects and only constrain the browser. |
| Path validation | Validate the **raw `FullName` string**, never via `Path.*`: reject `\` anywhere, leading `/`, `^[A-Za-z]:`, and any segment that is empty, `.`, `..`, or contains a control char. Assert these reject **on Linux**. |
| Path consistency | Validate and store the **same** value. Validating `Name` while storing `FullName` waves traversal through; the reverse silently flattens `assets/a.json` and `references/a.json` into one row. |
| Entry count | Enforce on **raw upload bytes before constructing `ZipArchive`** — 200k entries cost 22 MB on the wire but ~83 MB of heap merely to enumerate `.Entries`, so an `Entries.Count > N` check runs after the cost is paid. |
| Total size | Streaming byte counter that aborts mid-read. `CopyTo` / `ReadToEnd` defeats this. |
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

**The sharpest archive-level attack.** `ZipArchive.GetEntry` returns the *first* match while
dictionary or EF upsert iteration keeps the *last*. Validating via `GetEntry` and storing via
iteration means **the reviewed content is not the stored content** — which defeats human review
of the exact bytes that become LLM instructions. Separately, `GetEntry` is ordinal and
case-sensitive on every platform while SQL Server's default `..._CI_AS` collation is not, so two
legal ZIP entries can collide on the `(SkillId, RelativePath)` unique index mid-import.

## Content controls

Every control above operates on the container. The payload is natural language delivered to the
model byte-for-byte intact. These are the parts of that which validation can reach:

| Rule | Why |
|---|---|
| Reject runes in `U+E0000–U+E007F` | Unicode tag characters: invisible to a human reviewer, semantically read by the model. No legitimate use in a skill. Highest value per line in the whole matrix. |
| Reject bidi overrides `U+202A–U+202E`, `U+2066–U+2069` | Trojan Source class |
| Reject zero-width `U+200B/C/D`, `U+FEFF` outside a leading BOM | Hidden instruction text |
| Reject control chars `< 0x20` except `\t\r\n` | Truncation and rendering tricks |
| Flag or strip `<!-- -->` in the body | Invisible when rendered, present in the token stream |
| Reject `name`/`description` containing newlines, `---`, or `system:` / `assistant:` | **Widest blast radius of any control here.** Tier-1 descriptions are concatenated into one system message and injected on *every* run for *every* bound skill — no activation needed. The catalog builder must fence or escape rather than raw-concatenate. |
| Normalize NFC before the `name` regex; anchor it `^[a-z0-9-]+$` | Cyrillic homoglyphs pass an unanchored regex, and .NET's `\w` matches Unicode letters by default |
| Render the body in the confirm dialog with invisible characters escaped | A reviewer approving text they cannot see is the documented failure mode |

**The newline defence had a hole.** `U+0085`, `U+2028` and `U+2029` are above `0x20`, so they
cleared the control-character check, and they are not zero-width, so they cleared that one too —
while tokenizers and markdown renderers alike still treat them as line breaks. That forges a new
line inside a value whose entire defence is that it cannot contain one. The guard rejects them at
import, and `SkillPrompt.Sanitize` now asks Unicode what a character *is* (`LineSeparator`,
`ParagraphSeparator`, `Control`) rather than checking a hand-maintained list of known offenders,
which closes the class instead of three members of it.

## What validation cannot defend

Instruction override, credential-exfiltration directives, tool-misuse steering, cross-skill
activation hijacking, obfuscated payloads and phishing surface JSON are all **semantic** and
survive every control above. The cause is context flattening: the model processes our
instructions and third-party skill content as the same natural language in the same window.

The mitigations are architectural, and mostly already chosen. **Keeping `scripts/` out of v1**
removes the RCE class entirely and is doing more security work than every control in the tables
above combined — if it is reversed under demo pressure, this document stops covering the threat.
Admin-only upload plus provenance reduces the rest to an insider or compromised-account threat.

## Implementation order

Request size limit → raw-string path validation → duplicate detection → streaming counter and
pre-parse byte cap → content character filters and frontmatter injection → symlink / encryption /
compression-method → the rest. The importer returns **all** line-item errors, not the first,
matching `SurfaceValidator`.

## Open security gaps

- **No import-time cap on the `SKILL.md` body.** `name` and `description` are bounded; the body
  is bounded only by the 5 MB package / 20 MB uncompressed caps, which is not a context budget.
- **`SkillContentGuard.EscapeForDisplay` has no caller.** The control "render the body in the
  confirm dialog with invisible characters escaped" is written but not wired to the UI.
- **Replacement is not confirmed in the UI.** Re-importing a skill of the same name overwrites
  its body and assets in place. This is explicit and logged, but an admin upload can currently
  replace a bundled skill without anyone noticing.
- **Content-level injection is bounded, not defended** — see above. Accepted for a demo with
  admin-only import.

## Verification

- `dotnet test` — `SkillPackageImporterTests` covers the security cases; `SurfaceValidator` tests
  cover template validation.
- Import a deliberately broken package (zip slip, bad prop name) → rejected with a readable
  line-item error list rather than a partial import.
