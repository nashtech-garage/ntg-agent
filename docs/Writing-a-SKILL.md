# Writing a SKILL.md

A practical guide to authoring an Agent Skill for this system: the package, the frontmatter, the
`SKILL.md` body, and the A2UI surface templates that go with it. Everything here is checked against
the importer and the renderer, so if the guide says a rule exists, it is a rule that will reject
your upload or break your surface.

For *how* the protocols work — A2UI operations, AG-UI events, why a surface round-trips the way it
does — see [A2UI-and-AG-UI.md](A2UI-and-AG-UI.md). This document stays practical: what to type,
what will be rejected, and why.

Two skills ship in the repo and are the reference implementations. Read them; almost every pattern
below is lifted from one of them.

| | `seed/skills/travel-planning/` | `seed/skills/ticket-booking/` |
|---|---|---|
| Surfaces | one (`assets/trip-planner.json`) | three (`event-search`, `ticket-options`, `booking-review`) |
| Shape | one tabbed wizard, updated in place | one surface per turn, stacking |
| Step channel | `__tabs` + `advanceTab` | none — a new card *is* the step |

---

## 1. Pick a shape first

This is the decision that determines everything else in the skill, and it is easier to get right at
the start than to retrofit.

**One surface, many tabs** (`travel-planning`). Every turn re-renders the *same* `surfaceId`, so the
card already in the chat updates in place. There is never more than one planner on screen. The agent
moves the user between tabs by writing an index to `/__tabs/<componentId>`.

Use it when the steps are parts of one artefact the user should be able to move back and forth
inside — a form, a wizard, a configuration. The user can click back to tab 1 to re-read what they
typed, and it is still there.

**One surface per turn** (`ticket-booking`). Each step renders a different template, so each becomes
a new card and the earlier ones stay visible above it as a trail of the conversation.

Use it when the steps are genuinely different things and the history is worth keeping on screen — a
search, then results, then a receipt. There is no going back: an earlier card cannot be updated or
removed.

The trade is visible in the two skills' "Gotchas" sections, and it is real:

- The tabbed shape must warn the model **never to render a second surface** and must seed every tab
  with readable placeholder text, because the user can click ahead to a tab you have not filled yet.
  `trip-planner.json` seeds `options.o1.detail` with `"Still working…"` for exactly this reason.
- The stacking shape must warn the model **never to try to update an earlier surface**, and must
  carry state forward in the `values` of each new render, because nothing on the previous card is
  reachable any more.

A hybrid is legal — `ticket-booking` uses a `Tabs` component *inside* `ticket-options` purely as a
comparison view, with no `__tabs` and no `advanceTab`. Tabs as a layout device and tabs as a wizard
are separate uses; only the second needs the convention paths.

---

## 2. Package layout and packing

The zip must contain **one top-level directory** whose name matches the skill name, holding
`SKILL.md`:

```
travel-planning.zip
└── travel-planning/
    ├── SKILL.md
    └── assets/
        └── trip-planner.json
```

A bare `SKILL.md` at the archive root is also accepted — the spec's leniency guidance — in which
case there is no directory to match against and the frontmatter `name` is taken as-is. (The importer
computes a name from the zip filename in this case but does **not** enforce it; the directory-match
check only runs when there is a directory.) Prefer the directory form: it is what `pack-skills.py`
produces and the only form the seeder handles.

Build the zips with the packer, never by hand:

```bash
python3 scripts/pack-skills.py           # writes seed/skills/<name>.zip for every directory
python3 scripts/pack-skills.py --check   # verifies the zips are current; exit 1 if stale (CI)
```

It sorts entries, pins every timestamp to 1980-01-01 and sets `0o644` attributes, so repacking
unchanged sources produces a byte-identical file and does not churn git. It also enforces the
extension allowlist at pack time, so a stray `.py` or `.ts` in your skill directory fails there
rather than as a rejected upload. Dotfiles are skipped.

**Commit the zip.** `seed/skills/*.zip` is what the seeder reads on startup; the unpacked directory
is the source you edit. Change one, repack, commit both.

---

## 3. Frontmatter

```yaml
---
name: travel-planning
description: Plans a trip interactively in the chat using one rendered A2UI surface with three tabs — …
license: Apache-2.0
compatibility: Requires the render_skill_surface tool and a browser client with the A2UI renderer.
metadata:
  author: ntg-agent
  version: "2.0"
---
```

| Field | Required | Limit | Notes |
|---|---|---|---|
| `name` | yes | 64 chars | NFC-normalised, then must match `^[a-z0-9]+(-[a-z0-9]+)*$`, and must equal the wrapping directory name (ordinal comparison) |
| `description` | yes | 1024 chars | Single line. See §5 — this is the field doing the work |
| `license` | no | — | Stored; **not length-checked by the importer** (see the footgun below) |
| `compatibility` | no | 500 chars | Content-guarded like `name`/`description` |
| `metadata.version` | no | — | A top-level `version:` is accepted as a fallback |

**Everything else is parsed and discarded.** Both seed skills set `metadata.author`; it is read into
the frontmatter dictionary and then never looked at. Setting a field the importer does not know
about is not an error, it is simply a no-op.

### Only a fraction of YAML is accepted

`SkillFrontmatter.cs` parses by hand rather than pulling in a YAML library, because deserialising an
untrusted upload would import that library's whole attack surface to read five scalars. The grammar
is `key: value` scalars plus **one** level of nesting under a bare `key:`. Anything else is
*rejected*, not skipped:

| Rejected | Example |
|---|---|
| Lists | `tags:` then `  - travel` |
| Anchors / aliases | `&base`, `*base` |
| Flow collections | `[a, b]`, `{a: 1}` |
| Block scalars | `description: >` or `description: \|` |
| Tabs as indentation | any line containing a tab character |
| Duplicate keys | two `name:` lines — last-wins would let a second copy override a reviewed value |
| Indented line with no open section | two spaces before a key with no bare `key:` above it |

A few sharper edges:

- **A key with an empty value opens a section.** `description:` with nothing after it does not set an
  empty description — it opens a nested section named `description`, and you get
  `description: frontmatter field is required` plus errors on whatever follows.
- **Keys are case-insensitive.** `Name:` works; `name:` *and* `Name:` in the same block is a
  duplicate-key error.
- **Quotes are stripped naively.** A matching pair of leading/trailing `"` or `'` is removed; there
  is no escape processing inside. `version: "2.0"` gives `2.0`.
- **The value is everything after the first colon**, so a colon inside a value is fine — but see §9,
  because `user:` and `system:` are banned substrings in `name`, `description` and `compatibility`.
- **The closing `---` must be alone on its line**, and the document must open with `---` on line 1
  (a leading BOM is tolerated).

**Footgun not enforced at import:** `license` is stored in a `nvarchar(256)` column, `version` in
`nvarchar(64)` and the source filename in `nvarchar(260)`, but the importer only length-checks
`name`, `description` and `compatibility`. An over-long `license` therefore fails at `SaveChanges`
as a database error rather than as a line-item validation message. Keep them short.

---

## 4. How the body is used — the three tiers

Progressive disclosure is not a style guide here, it is the runtime. Three tiers, three costs:

| Tier | What the model gets | When | Cost |
|---|---|---|---|
| 1 | `name: description`, for **every** bound skill | injected as a system message on **every run** | paid always |
| 2 | the whole `SKILL.md` body | only when the model calls `load_skill` | paid on activation |
| 3 | a surface template's operations | only when the model calls `render_skill_surface` | paid on render, and only to the browser |

Two consequences that change how you write.

**The body has no length cap.** There is no character limit in `SkillFrontmatter`, none in
`SkillPackageImporter`, and the `Body` column is unbounded. The only ceiling is the 5 MB package /
20 MB uncompressed package limit. That is a context-budget footgun, not a licence: the body is
injected **whole** the moment the skill activates, and it stays in that reply's context alongside
everything else. A 3,000-word body is a 3,000-word tax on every turn of the flow. Both seed skills
land around 1,200 words and that is a reasonable target.

**A loaded skill lasts only for the current reply.** The body is never persisted into conversation
history. On the turn *after* the user submits a surface, the model holds the tier-1 catalog and
nothing else. `SkillPrompt.BuildCatalog` carries the paragraph that has to win:

> **A loaded skill lasts only for the current reply.** Its instructions are not carried into later
> turns. Whenever you continue a task you began under a skill — above all when the user has just
> submitted a surface you rendered — call `load_skill` for that skill again, first, before doing
> anything else.

This was a real regression, and `scripts/check-skill-flow.py` exists as its regression test. What it
means for you as an author: **write the body so it works when read cold at any step.** Do not write
"as I said in step 1"; the model did not read step 1 in this context. Every step section should
name the surface, name the incoming action, and state what to send — `ticket-booking`'s "Step 3"
section is self-contained and that is deliberate.

---

## 5. Writing a good description

The description is the only thing the model sees when deciding whether your skill applies. It is
prompt engineering, not documentation. Both seed skills follow the same three-part shape:

```
Plans a trip interactively in the chat using one rendered A2UI surface with three tabs — collects
destination, dates, travellers and trip style, presents three fabricated itinerary options with
prices, then a review and confirmation, moving the user from tab to tab as they answer.  ← what
Use when the user wants to plan, book, price or compare a trip, holiday, vacation, flight or hotel
stay, or asks for help choosing between travel options.                                   ← when
Demonstration only — all itineraries and prices are invented, nothing is booked.          ← caveat
```

- **What it does**, concretely enough that the model can tell it apart from a neighbouring skill.
- **When to use it**, as a spray of the words a user would actually type. "trip, holiday, vacation,
  flight or hotel stay" is not padding — it is the matching surface.
- **The caveat**, if the skill fabricates data. Say it in tier 1 so it is true even on a turn where
  the body was never loaded.

Write it as one flowing sentence per clause and keep it under 1024 characters. It must be a single
line: a newline in a description lets it forge a separate instruction in the catalog, and the
importer rejects it for that reason.

---

## 6. Surface templates

A template is a JSON file under `assets/`. Only `assets/*.json` is validated as a surface, and only
`assets/*.json` is reachable from `render_skill_surface` — a JSON file anywhere else is stored and
inert.

The shape is three keys:

```json
{
  "surfaceId": "trip-wizard",
  "components": [ … ],
  "data": { … }
}
```

**The file name and the `surfaceId` are different things and both matter.**
`assets/trip-planner.json` is what the model names in `render_skill_surface` (`"surface":
"trip-planner"`), and what your `SKILL.md` must document. `"surfaceId": "trip-wizard"` is the
identity of the card in the browser: re-rendering a template with the same `surfaceId` updates the
card already on screen instead of adding another. Two templates sharing a `surfaceId` would
overwrite each other's card — which is a legitimate technique, but only if you mean it.

### The 18 components

`A2uiCatalog.cs` is a flattened snapshot of the A2UI v0.9 basic catalog. Every component and every
property below is checked at import; anything not on this list is a rejected upload.

| Component | Required | Other props | Enumerated values |
|---|---|---|---|
| `Text` | `text` | `variant` | `variant`: h1 h2 h3 h4 h5 caption body |
| `Image` | `url` | `description`, `fit`, `variant` | `fit`: contain cover fill none scaleDown · `variant`: icon avatar smallFeature mediumFeature largeFeature header |
| `Icon` | `name` | — | — |
| `Video` | `url` | — | — |
| `AudioPlayer` | `url` | `description` | — |
| `Row` | `children` | `align`, `justify` | `align`: start center end stretch · `justify`: start center end spaceBetween spaceAround spaceEvenly stretch |
| `Column` | `children` | `align`, `justify` | as `Row` |
| `List` | `children` | `align`, `direction` | `direction`: vertical horizontal |
| `Card` | `child` | — | — |
| `Tabs` | `tabs` | — | — |
| `Modal` | `content`, `trigger` | — | — |
| `Divider` | — | `axis` | `axis`: horizontal vertical |
| `Button` | `action`, `child` | `checks`, `variant` | `variant`: default primary borderless |
| `TextField` | `label` | `checks`, `validationRegexp`, `value`, `variant` | `variant`: shortText longText number obscured |
| `CheckBox` | `label`, `value` | `checks` | — |
| `ChoicePicker` | `options`, `value` | `checks`, `displayStyle`, `filterable`, `label`, `variant` | `displayStyle`: checkbox chips · `variant`: multipleSelection mutuallyExclusive |
| `Slider` | `max`, `value` | `checks`, `label`, `min` | — |
| `DateTimeInput` | `value` | `checks`, `enableDate`, `enableTime`, `label`, `max`, `min` | — |

Every component also accepts `id`, `accessibility` and `weight`.

**Templates can use components freeform A2UI cannot.** The render guide the model is prompted with
for hand-authored surfaces (`A2uiPrompt.RenderGuide`) documents 14 components — it omits `Tabs`,
`Modal`, `Video` and `AudioPlayer`. Those four are in the catalog and validate fine in a template.
That is one of the concrete reasons to ship a skill with templates rather than let the model draw
the UI: the tabbed wizard in `travel-planning` is not something the model could have produced
freehand.

An enumerated property may also hold a binding instead of a literal — the check only fires when the
value is a string.

### Every rule `SurfaceValidator` enforces

Each of these is a rejected upload with a line-item message. The validator reports **all** failures
at once, not the first.

| Rule | Message you get |
|---|---|
| Valid JSON, no comments, no trailing commas, max depth **32** | `not valid JSON — …` |
| Top level is an object | `top level must be a JSON object` |
| `surfaceId` present and non-empty | `missing or empty 'surfaceId'` |
| `components` present, an array, non-empty | `missing required 'components' array` / `'components' is empty` |
| Every component is an object with a non-empty string `id` | `components[3] has no non-empty string 'id'` |
| Ids unique | `duplicate component id 'x'` |
| `component` present and a known name | `uses unknown component 'Accordion'` |
| Required properties present | `'x' (Slider) is missing required property 'max'` |
| No unknown properties | `'x' (Text) has unknown property 'color'` |
| Enum values from the set | `has variant='h6', expected one of h1\|h2\|…` |
| Child references resolve | `'x' references child 'y', which does not exist` |
| No self-reference | `'x' references itself as a child` |
| No orphans | `'x' is never referenced and will not render` |
| A component with id `root` exists | `no component with id 'root'` |
| `root` is `Column`, `Row`, `Card` or `List` | `root is 'Text', which cannot lay out children` |
| Every `{"path": …}` binding has a seed in `data` | `'x' binds '/trip/style', which has no seed in 'data'` |

The child-reference properties are **`child`, `children`, `content`, `trigger`, `tabs`**. `tabs` is
the odd one: its entries are `{ "title": …, "child": "pane-id" }` objects, so the reference sits one
level down, and the validator reaches into it. `title` may be a literal or a binding —
`ticket-options.json` binds tab titles to `/tiers/t1/name` so the agent can rename the tabs at
render time.

A `children` value may also be the ChildList template form `{ "componentId": "row-template", "path":
"/items" }`, generating children from a data list. The `componentId` is checked; the `path` in that
object is **not** seed-checked, because a binding is defined as an object with *exactly* one key
named `path`. Neither seed skill uses this form.

Two caveats on the orphan check, so you know what it does and does not buy you:

- It flags anything nothing else references. It does **not** compute reachability from `root`, so a
  disconnected subtree whose members reference each other reports only its top node as an orphan.
- Note the asymmetry it prevents: `tabs` is in the reference list precisely because leaving it out
  would report every tab pane as an orphan while letting a genuinely dangling `child` through.

### The seed rule, and why it is the important one

> Every `{"path": …}` binding anywhere in a component — including nested inside
> `action.event.context` and `options[].label` — must have a seed in `data`.

A2UI inputs are controlled components. Their setter is a no-op until the bound path exists in the
data model. An input bound to an unseeded path therefore **renders frozen**: it looks like a working
form and silently ignores the user. There is no error, no console warning, nothing at runtime. Import
is the only place this can be caught, which is why the validator walks every nested object of every
component looking for bindings.

The same applies to a button's context. An unseeded path in `action.event.context` resolves to
`null`, so the answer the button was supposed to carry arrives empty.

Seeding is cheap and reads as documentation of the data model. `event-search.json`:

```json
"data": {
  "search": {
    "event": "", "city": "", "date": "",
    "tickets": 2, "priority": ["view"], "together": true
  }
}
```

Seed with the **right kind**, not just any value: `""` for text, a number for a `Slider`, an array
for a `ChoicePicker` (single-select included), a boolean for a `CheckBox`. §7 explains why the kind
is load-bearing at render time as well.

Seed placeholder *content* too, wherever the user can see a pane before you have filled it. That is
what `"detail": "Still working…"` is doing in `trip-planner.json` — the user clicks **Find trips**,
the tab moves immediately, and they are looking at that string while the agent works.

### Layout that reads well

The house rules from the render guide apply to templates too:

- The surface is already drawn as a styled card, so a `Card` at `root` double-frames it. Use a
  `Column` root and reserve `Card` for grouping a section inside — `ticket-options.json` puts a
  `Card` inside each tab, which is the right use.
- Hierarchy comes only from `Text` `variant`: `h4` for the surface title, `h5` for a price or total,
  `caption` for hints and labels, default `body` for everything else.
- A label-and-value pair is a `Row` with `justify: "spaceBetween"`. Repeat that row down a `Column`
  and you have a summary. Both review surfaces are built entirely from this one idiom.
- `Divider` between real sections only. Two or three is structure; six is noise.
- Exactly one `variant: "primary"` button per surface, and put it last.

---

## 7. The tools a skill can call

Two tools, both constructed per request and both closed over the agent id — the model names a
*skill*, never an agent, and the registry re-checks that the skill is bound and enabled for this
agent before returning anything.

### `load_skill`

One argument, `skill`. Returns the full `SKILL.md` body, or a message saying the skill is not
available. That is the whole of it. What matters is §4: it must be called again on every turn that
continues the flow.

### `render_skill_surface`

```json
{
  "skill": "travel-planning",
  "surface": "trip-planner",
  "values": { "__tabs": { "wizard": 1 }, "options": { … } }
}
```

`surface` is the file name without `assets/` and without `.json`. Models pass all three forms anyway,
so the tool normalises `trip-planner`, `trip-planner.json` and `assets/trip-planner.json` to the
same lookup. `values` is optional — omit it to render the template's defaults.

The tool emits exactly three kinds of operation to the browser: `createSurface`, `updateComponents`,
and then **one `updateDataModel` per top-level branch** of the merged data. It never writes the root
path. That is the mechanism behind the advice both seed skills give.

**Why per-branch, and what it means for you.** Writing `/` would replace the entire data model — and
the model holds more than your template seeds. The client mirrors every input the user touches to
`/__inputs/<componentId>` (§8), and that is what a Button's `formData` carries back. A root write on
a re-render would discard the user's own answers while the inputs on screen still showed them.
Per-branch writes leave every branch you did not send untouched.

So: **send only the branch you are filling in.** A branch you *do* send is replaced by your values
merged over the template's defaults, which means re-sending a branch you have nothing new to say
about overwrites live answers with blanks. `travel-planning` states this as a rule and then names its
single exception — when filling `review`, re-send `options` unchanged, because tab 2 is still
reachable behind tab 3.

**How `values` merges.** Objects merge key by key, recursively. Everything else — scalars, arrays —
replaces wholesale, so a shorter array replaces the array rather than leaving stale trailing entries.

**Kind changes are refused.** If a path already has a seed, its *kind* (object / array / scalar)
cannot change. The tool returns a message naming the path rather than rendering:

```
The values did not match the shape 'trip-planner' expects: '/trip' expects an object with keys
destination, departDate, returnDate, travellers, style but received scalar. Send the values nested
exactly as the skill's instructions show, then try again.
```

That refusal exists because swapping an object for a scalar removes every path beneath it, which
re-creates the frozen-input failure the surface validator exists to prevent — at run time, past the
point validation reaches. It is also why a `ChoicePicker` must be seeded and sent as an array: a bare
string in its place stops the binding resolving.

**Unknown keys are not refused.** The kind check only fires on a key that already exists. A typo in a
branch name creates a new, unused top-level branch and renders successfully with the intended branch
untouched. Nothing warns you. Document your branch names precisely in the `SKILL.md` — the table in
`travel-planning`'s "The data model" section is doing exactly this job.

The model gets a one-line receipt back, not the surface JSON: *"Rendered the 'x' surface. Do not
describe it — the user can see it. Stop now and wait for them to submit it."*

---

## 8. The conventions

These live in `my-copilot-app/src/a2ui/interactiveCatalog.tsx`, which replaces five components of
the stock catalog with versions that round-trip reliably. None of it is in the A2UI schema; it is
all data-model convention, which is precisely why it costs nothing when unused.

### `/__inputs/<componentId>` — the user → agent channel

`TextField`, `CheckBox`, `ChoicePicker` and `Tabs` write their value to `/__inputs/<id>` on every
change, **in addition to** the bound path, and regardless of whether a binding exists at all. It is
the always-on fallback: if the binding is missing or the button's path and the input's path disagree,
the answer still reaches the agent.

More usefully, **nothing you render ever overwrites it**. Your `values` fill named branches; the
mirror sits in its own branch that no template seeds and no render touches. In a multi-turn flow it
is the one place an answer from an earlier step is guaranteed to still be sitting. Tell the model the
component ids — both seed skills list them explicitly.

### `/__tabs/<componentId>` — the agent → user channel

Where the agent parks the tab it wants shown, keyed by the `Tabs` component's id. It is a convention
path rather than a schema property because `TabsApi.schema` is strict and the catalog schemas set
`unevaluatedProperties: false`, so a `selectedIndex` prop would be rejected as unknown unless the
schema were forked — and a forked schema drifts from the one the agent is prompted with.

Behaviour worth knowing exactly:

- `Tabs` reads the path once at mount for its initial index, then subscribes.
- **A re-sync happens only when the value that arrives on the path changes.** The comparison is
  against the last value that *arrived*, not against what is on screen. So if the agent selects tab 1
  and the user clicks back to tab 0, every later render still reporting `1` leaves them alone. Only a
  genuinely new instruction moves them. This is why `travel-planning` says "sending the same index
  twice does nothing, which is deliberate".
- Garbage is ignored rather than thrown: a non-numeric value leaves the tab where it is, and an
  out-of-range index clamps into the strip. A numeric string works.
- **The user's own clicks do not write back.** `/__tabs` stays one-way (agent intent); `/__inputs`
  stays the other way (user state).
- Seed `__tabs` in the template's `data` — `trip-planner.json` seeds `{"wizard": 0}` — and send it on
  every render. The validator does not force this (nothing *binds* `/__tabs`), but seeding gives the
  initial index a defined value and gives the render function a branch to write.

### `advanceTab` — the undocumented one

Put `"advanceTab": "<tabsComponentId>"` as a **literal string** in a Button's `action.event.context`:

```json
{
  "id": "trip-submit",
  "component": "Button",
  "variant": "primary",
  "child": "trip-submit-label",
  "action": {
    "event": {
      "name": "trip_search_submit",
      "context": {
        "destination": { "path": "/trip/destination" },
        "style": { "path": "/trip/style" },
        "advanceTab": "wizard"
      }
    }
  }
}
```

On click, before anything is dispatched, the client reads `/__tabs/wizard`, coerces it to a number
(non-numeric becomes `0`) and writes back **current + 1**. Then it strips `advanceTab` from the
context and dispatches the rest. So:

- It **increments**; it does not jump to a named tab. It is "next", not "go to step 3".
- It is UI plumbing and never reaches the agent — the payload the model sees has no `advanceTab` key.
- The agent's own `__tabs` write for the same step lands on the tab the user is already on and is a
  no-op by the change-gate above. Send it anyway: it keeps the agent's idea of the step and the
  user's in agreement, and costs one line.
- Failure is silent and harmless — if the write throws, the tab simply does not move and the
  submission is unaffected.

Why it exists: a step button in a tabbed flow reads as "next". Waiting for the agent round trip means
ten to twenty seconds sitting on a form the user has finished with, with no signal the click
registered. The tab moves locally and the placeholder text on the next tab covers the wait — which is
why that placeholder needs to be worth reading.

This is documented nowhere else, including in the `SKILL.md` that depends on it. If you build a
tabbed wizard, `advanceTab` on every step button is the difference between a responsive flow and a
dead one.

### The Button payload, and reading answers

`InteractiveButton` dispatches:

```js
{ event: { name: "trip_search_submit", context: { ...resolvedContext, formData } } }
```

`resolvedContext` is your `action.event.context` with every `{path}` resolved against the data model
(and `advanceTab` removed). `formData` is the **complete data model** — every branch, plus
`__inputs`, plus `__tabs`. This arrives at the agent as a `log_a2ui_event` tool call.

Tell the model to read answers in this order — both seed skills spell it out, and it should be a
section of yours:

1. **The named value** — `destination`, `choice`, whatever the button's context called it.
2. **`formData` under the bound path** — `formData.trip.destination`, `formData.options.choice`.
3. **`formData.__inputs.<componentId>`** — the always-on mirror.

Step 3 is not a last resort. It is often where the truth is, for the overwrite reason above. Instruct
the model never to tell the user nothing was selected without checking all three.

Value shapes, which are the other half of reading answers correctly:

| Component | Comes back as |
|---|---|
| `TextField` | string |
| `CheckBox` | boolean — an untouched box holds the seeded value, not `null` |
| `Slider` | number |
| `DateTimeInput` | `YYYY-MM-DD` string (with `enableTime: false`) |
| `ChoicePicker` | **always an array**, single-select included: `["balanced"]`, `["o2"]` |
| `Tabs` | `{ "index": 1, "title": "2. Options" }` at `__inputs.<id>` — position, not a choice |

The picker one catches everyone. Say it in your `SKILL.md` in as many words: *"Picker values are
arrays — take the first element."*

The `Tabs` one matters for a different reason. Which tab is open is **not** a selection. A user can
read all three tiers in `ticket-options` without picking one. Use `__inputs.<tabsId>` to tell a
submission from the tab the user has since wandered to, and never to infer an answer — if `choice`
comes back empty, ask.

---

## 9. What is forbidden

The full control matrices are in
[Agent-Skills-Implementation-Plan.md](Agent-Skills-Implementation-Plan.md) — see **Phase 2b → Container
controls** and **Content controls**, plus **What validation cannot defend**. What follows is only what
an author trips over in practice.

### No code, by decision

The extension allowlist is **`.md .json .txt .yaml .yml .png .svg`**, compared case-insensitively.
There is no `scripts/` support and none is planned: that omission removes the remote-code-execution
class entirely and is doing more security work than every other control combined. A skill is
instructions and data. If your design needs to run something, it is not a skill.

### Archive rules you can hit by accident

- Max 200 entries; max 512-character paths; max 4 path segments deep (`skill/assets/x.json` is 3).
- No backslash anywhere in a path, no leading `/`, no `C:` drive letter, no `.` or `..` segment, no
  empty segment, no control characters. These are checked as raw strings, never through `Path.*`,
  because `..\..\evil.md` is inert on Linux and real traversal on Windows.
- No duplicate entries — ordinal **or** case-insensitive. `Assets/x.json` and `assets/x.json` in the
  same archive is a rejection.
- No nested archives (detected by the `PK\x03\x04` magic bytes, whatever the extension), no encrypted
  entries, no symlinks, no non-regular files, no setuid/setgid bits, no compression method other than
  stored or deflate.
- 5 MB package, 20 MB total uncompressed, enforced by a streaming counter that aborts mid-read.
- Text files must be **strict UTF-8**. Invalid bytes are an error, not a silent `U+FFFD`.

`pack-skills.py` gives you all of these for free. You hit them by hand-rolling a zip.

### Content rules — the ones that surprise people

Applied to `SKILL.md` (frontmatter included) and to every text asset:

- **No invisible characters.** Unicode tag characters `U+E0000–U+E007F`, bidi overrides
  (`U+202A–U+202E`, `U+2066–U+2069`), zero-width `U+200B/C/D`, `U+FEFF` anywhere but as a leading BOM,
  control characters below `0x20` other than tab/CR/LF, and the line separators `U+0085`, `U+2028`,
  `U+2029`. The threat is not a reviewer approving malicious text — it is a reviewer approving text
  they cannot see.
- **No HTML comments.** `<!--` anywhere in a text file is a rejection. It renders as nothing in the
  confirm dialog and is fully present in the model's context. Comment your JSON by naming things
  well; JSON comments are rejected by the surface parser anyway.

Applied additionally to `name`, `description` and `compatibility`, which are concatenated into one
system message injected on every run with no activation step:

- **Must be a single line.**
- **Must not contain** (case-insensitive substring match): `---`, `system:`, `assistant:`, `user:`,
  `<|`, `|>`, or a triple backtick.

That last list is the one that bites. `"Use when the user: wants…"` is rejected for `user:`.
`"Plans a trip --- interactively"` is rejected for `---`. Both seed skills use an em dash (`—`) as
their separator, and both write "Use when the user wants" with no colon. Copy that.

Errors are reported **all at once**, as line items, in the Admin import dialog. An author fixing one
error per upload round-trip starts disabling checks, so the importer is built not to make you do that.

---

## 10. Testing a skill end to end

```bash
# 1. Pack
python3 scripts/pack-skills.py

# 2. Load it
#    Either: Admin → Skills Management (/skills-management) → Import, and read the error list.
#    Or:     leave the zip in seed/skills/ and restart the Orchestrator.

# 3. Bind it
#    Admin → Agents → your agent → Tools tab → Skills sub-tab → tick it → Save Skills.

# 4. Drive it (travel-planning only)
python3 scripts/check-skill-flow.py
```

Notes on each step.

**Importing.** Re-importing a package whose `name` already exists **replaces** the stored skill in
place — same row id, same agent bindings, but the previous body and *every* asset are gone. That is
the fast iteration loop, and it is destructive; the Admin form confirms before doing it.

**Seeding.** `SkillSeeder` runs on startup and imports `seed/skills/*.zip` through exactly the same
importer an upload goes through. But it **skips any package whose skill name is already stored** —
nothing is even staged. So dropping an updated zip in `seed/skills/` and restarting will *not* pick
up your changes if the skill is already in the database. Use the Admin import for iteration, or delete
the skill first. Seeding also never binds: binding stays an explicit admin decision.

**`check-skill-flow.py`** runs three real turns against a running orchestrator, reproducing byte for
byte what the browser posts when a user clicks a surface button, and asserts per turn: no run error,
`load_skill` fired, `render_skill_surface` fired for the expected surface, and the render's result is
`{"a2ui_operations": [...]}` with the expected operations. It is hard-wired to `travel-planning` and
its `trip-planner` surface — it is the regression test for the reload rule in §4, not a general
harness. For your own skill, the equivalent manual check is: **click through the whole flow twice**,
watching that the agent reloads the skill on every turn and that an answer given on step 1 is still
correct on step 3.

The unit tests are worth reading as executable specification when a rule here is unclear:
`tests/NTG.Agent.Orchestrator.Tests/Services/Skills/` — `SurfaceValidatorTests`,
`SkillPackageImporterTests`, `SurfaceRenderFunctionTests`, `SkillSeederTests`.

---

## Checklist

Before you pack:

- [ ] Directory name, `name:` frontmatter and the `<name>.zip` filename all agree.
- [ ] `description` says what / when / caveat, is one line, and contains no `---`, `user:`, `system:`.
- [ ] Body is self-contained per step — it will be read cold on every turn.
- [ ] Body is closer to 1,000 words than 3,000.
- [ ] Every step section names its surface, its incoming action and the exact `values` to send.
- [ ] The `SKILL.md` documents the branch names, the component ids, and the three-step answer-reading
      order.
- [ ] "Picker values are arrays" appears in the body, in those words.
- [ ] Every `{"path": …}` in every template has a seed of the right kind in `data`.
- [ ] Placeholder text is worth reading on any pane the user can reach early.
- [ ] Tabbed wizard: `__tabs` seeded, sent on every render, and `advanceTab` on every step button.
- [ ] Stacking flow: the body forbids re-rendering an earlier surface and carries state forward.
- [ ] `python3 scripts/pack-skills.py --check` is clean.
