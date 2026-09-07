# Designing an AG-UI surface template

A design specification for the house style of interactive surfaces in NTG.Agent: what a
template is, the contract it must satisfy, the three shapes it may take, and the conventions
that make one template look and behave like the next.

This is the *design* document. `docs/Writing-a-SKILL.md` is the *authoring* guide — it tells you
how to pack and ship one. `docs/A2UI-and-AG-UI.md` explains the transport underneath. Read this
one when you are deciding what a surface should be; read those when you are building it.

**Contents** — [What a template is](#1-what-a-template-is) · [Design goals](#2-design-goals) ·
[Anatomy](#3-anatomy-of-a-template) · [Naming](#4-naming-conventions) ·
[Data model](#5-the-data-model-contract) · [Actions](#6-the-action-contract) ·
[Three archetypes](#7-the-three-archetypes) · [Visual contract](#8-the-visual-contract) ·
[Accept/reject rules](#9-what-the-validator-will-reject) · [Anti-patterns](#10-anti-patterns) ·
[Checklist](#11-design-checklist) · [Worked skeleton](#12-worked-skeleton)

---

## 1. What a template is

A **surface template** is one static JSON file in a skill's `assets/` directory. It describes a
complete A2UI surface — its component tree, its enumerated data model, and the events its
buttons can raise — with the *content* left blank.

At runtime `render_skill_surface` reads the template, merges the model's `values` into the
`data` block, and emits three A2UI operations in one payload:

```
render_skill_surface(skill, surface, values)
        │
        ├── createSurface     { surfaceId }
        ├── updateComponents  { surfaceId, components }   ← from the template, unchanged
        └── updateDataModel   { surfaceId, contents }     ← template data + model values
                │
                ▼  AG-UI SSE (TOOL_CALL_* frames)
        @ag-ui/a2ui-middleware → ACTIVITY_SNAPSHOT
                ▼
        createA2UIMessageRenderer + interactiveCatalog → a card in the chat
```

The split is the whole point. **The tree is authored by a human and frozen; only the data
changes.** That is what separates a template from Path A's freeform `render_a2ui`, where the
model composes the tree itself and can compose it wrong.

| | Template (`render_skill_surface`) | Freeform (`render_a2ui`) |
|---|---|---|
| Who writes the tree | a person, once, at design time | the model, every turn |
| Validated | at import, by `SurfaceValidator` | never |
| Layout drift between turns | none | expected |
| Survives conversation reload | yes — persisted and replayed | no |
| Right for | repeatable flows, anything a user submits | one-off illustrations |

Everything below concerns templates.

## 2. Design goals

These are the properties a template is being designed *for*. When a decision is close, decide
in this order.

1. **Deterministic layout.** The same surface must look the same on every render. Any variation
   belongs in the data, not the tree.
2. **Fail at import, not on screen.** Every defect that can be caught by reading the JSON should
   be caught by `SurfaceValidator` before an admin ever binds the skill. A blank card during a
   demo is the failure mode being designed out.
3. **The model fills in blanks; it does not design.** A template's `values` contract should be
   describable as a flat list of typed fields. If explaining it to the model requires describing
   layout, the template is doing too little.
4. **Answers always reach the agent.** Even with an imperfect binding, a submission must carry
   the user's input back. The frontend guarantees this via `/__inputs/` mirroring; templates
   should not rely on it, but may assume it as a backstop.
5. **One card, one decision.** A surface asks the user for one thing and offers one primary
   action. Multi-step work is multiple renders, not one crowded card.
6. **Legible at 536px.** That is the real card width. A template that needs more is the wrong
   shape.

## 3. Anatomy of a template

Every template is four ordered regions inside a `Column` root.

```
root  (Column | Row | Card | List — must lay out children)
│
├── HEADER      Text h4 title  +  Text caption subtitle
├── BODY        the fields, the comparison, or the summary
├── DIVIDER     optional, separates body from the decision
└── ACTION BAR  exactly one primary Button (plus at most one secondary)
```

The regions are a convention, not a schema — the validator does not enforce them. They exist so
that two templates written by two people a month apart still read as the same product.

Three rules govern the tree itself, and these *are* enforced:

- The component with id `root` must exist and must be `Column`, `Row`, `Card` or `List`.
- Every component must be reachable from `root` through `child` / `children` / `content` /
  `trigger` / `tabs`. An unreferenced component is an import error, not dead weight.
- A component may not reference itself.

**Buttons have no label property.** A `Button` takes `child`, which is the id of a `Text`
component. Every button therefore costs two components. Keep the label immediately after the
button in the array so they read as a pair.

## 4. Naming conventions

Ids are the only handle anyone has on a component, and they show up in validator errors, in the
`/__inputs/` fallback paths, and in `advanceTab`. Treat them as API.

| Thing | Convention | Example |
|---|---|---|
| Root | literally `root` | `root` |
| A step or region prefix | `<step>-` | `trip-`, `t1-`, `review-` |
| Input | `<step>-<field>` | `trip-destination` |
| Button | `<step>-submit` | `trip-submit` |
| Button label | `<button-id>-label` | `trip-submit-label` |
| Tabs container | one short noun | `wizard`, `tiers` |
| Divider | `sep`, or `<step>-sep` | `sep` |

Keep ids short, lowercase, hyphenated, and stable across template versions. Renaming an id is a
breaking change: it invalidates any `advanceTab` reference and any `/__inputs/<id>` path the
frontend has been writing.

`surfaceId` is separate from the file name and separate from the ids. It identifies the *card*.
Two renders sharing a `surfaceId` update one card in place; two different `surfaceId`s produce
two cards. Choose it deliberately — `tkt-search`, `tkt-tiers`, `trip-wizard` — and never reuse
one across skills.

That in-place behaviour comes from `StableSurfaceIdMiddleware` in the Next.js bridge, which
rewrites the snapshot id to `a2ui-surface-<surfaceId>` for skill surfaces only. It is on by
default and `A2UI_STABLE_SURFACE_IDS=off` reverts to one card per render. Design as if it is on
— that is the shipped default — and see `docs/A2UI-and-AG-UI.md` § 6 for why it exists.

## 5. The data model contract

### Namespacing

The data model is a JSON object addressed by JSON-Pointer-style paths. Give every template a
single top-level namespace named for its step, and put everything under it:

```json
"data": {
  "search": { "event": "", "city": "", "date": "", "tickets": 2, "priority": ["view"], "together": true }
}
```

The namespace is what the model's `values` argument mirrors. A flat, one-level-deep namespace is
the easiest thing to describe in a SKILL.md and the hardest thing to get wrong.

Two prefixes are reserved:

| Path | Direction | Meaning |
|---|---|---|
| `/__inputs/<componentId>` | user → agent | Automatic mirror of every input's current value, written by `interactiveCatalog`. Never author it; never bind to it. It is the safety net that makes a mis-bound field still submit. |
| `/__tabs/<componentId>` | agent → user | The tab index the agent wants shown. One-way: the user's own tab clicks do not write back. Seed it as `{"<tabsId>": 0}` and send it on every render. |

### The seed rule

**Every path bound anywhere in the tree must exist in `data`.** Not "should" — the validator
walks every `{ "path": "..." }` object in every component, including ones nested inside
`action.event.context` and inside `options[].label`, and rejects the import if the path has no
seed.

The reason is that A2UI inputs are controlled components. Their setter is a no-op until the path
exists in the model. An unseeded `TextField` renders, accepts focus, and silently refuses to
accept typing — with no error in the console, no error in the logs, and no error in the SSE
stream. It is the single most expensive bug this system can produce, so it is checked at import.

Seed with the correct *type*, not with `null`:

| Component | Seed with | Note |
|---|---|---|
| `TextField` | `""` | `variant: "number"` still seeds a string |
| `CheckBox` | `true` / `false` | |
| `Slider` | a number inside `[min, max]` | |
| `DateTimeInput` | `""` or `"YYYY-MM-DD"` | |
| `ChoicePicker` | **an array** | `[]` for none, `["view"]` for a default — an array even in `mutuallyExclusive` mode |
| Any `Text` bound to a path | `""` | an empty string, so the slot exists |

The `ChoicePicker` array is the rule people get wrong. Single-select is still an array of one.

### Seeding display text

A template's headings and body copy can be either literal or bound. Bind whatever the model
should be able to change, and seed it with `""`:

```json
{ "id": "heading", "component": "Text", "text": { "path": "/tiers/heading" }, "variant": "h4" },
```
```json
"data": { "tiers": { "heading": "Your seating tiers", "subheading": "" } }
```

Seeding a *sensible default* rather than `""` means the card still reads correctly if the model
omits that value. Prefer that where a default exists.

## 6. The action contract

A submission is a `Button` whose `action` is an event with a name and a context:

```json
{
  "id": "search-submit",
  "component": "Button",
  "variant": "primary",
  "child": "search-submit-label",
  "action": {
    "event": {
      "name": "ticket_search_submit",
      "context": {
        "event": { "path": "/search/event" },
        "city":  { "path": "/search/city" }
      }
    }
  }
}
```

| Element | Rule |
|---|---|
| `name` | `snake_case`, prefixed with the skill's domain — `ticket_search_submit`, `trip_options_selected`. It is the token the SKILL.md instructions dispatch on, so it must be unique within the skill and stable across versions. |
| `context` | Named bindings for the fields this action is *about*. Every path must be seeded. |
| `advanceTab` | A **literal string** (not a binding) naming a `Tabs` component id. Stripped from the outgoing context and used to move the user to the next pane. Optional. |
| `variant` | `primary` for the one action that advances the flow; `default` or `borderless` for anything else. Only `default`, `primary` and `borderless` exist. |

The frontend also appends the entire data model under `formData` in the dispatched payload, so
an answer is recoverable even when a context path was wrong. Design as if that did not exist —
name every field you need in `context` — and treat `formData` as insurance.

**One primary button per surface.** Two primary buttons is two decisions, which is two surfaces.

## 7. The three archetypes

Nearly every useful surface is one of three. Pick one before you start; do not blend them.

### A. Collect — gather facts

A form. Header, a vertical run of inputs, one submit.

- Root: `Column`.
- Six inputs is the practical ceiling at 536px.
- Order fields the way a person would say them, not the way the API wants them.
- Pre-fill from what the user already said, via `values`. Never make them retype.
- Reference: `seed/skills/ticket-booking/assets/event-search.json`.

### B. Choose — compare and pick

Options the user must decide between. This is where `Tabs` earns its place: one tab per option,
each pane a `Card`, then a `ChoicePicker` and a submit *below* the tabs.

- Exactly three options. Two is a false binary; four does not fit.
- Every pane must have **identical structure** — the same rows, the same order, the same
  variants. Comparison only works when the eye can hold position across tabs.
- Put the discriminating number (price, duration, score) in a `Row` with
  `justify: "spaceBetween"` so it lands in the same place in every pane.
- The picker sits outside the tabs. Selecting a tab is browsing; selecting in the picker is
  deciding. Conflating them makes an accidental click a commitment.
- Reference: `seed/skills/ticket-booking/assets/ticket-options.json`.

**Tabs as layout vs tabs as a wizard.** In archetype B, tabs are a comparison view: no
`__tabs`, no `advanceTab`, the user drives. A wizard is the other use — one surface, one `Tabs`,
one pane per step, and the agent moves the user forward with `advanceTab` and `/__tabs`. Both
are legitimate; they are not the same thing, and a template should be one or the other.

### C. Confirm — review and commit

The last step. Read-only summary, then a decision.

- Every line is a bound `Text`. No inputs — if something is still editable, this is a Collect
  surface wearing a hat.
- Two buttons: `primary` to confirm, `default` to go back and change something. Two distinct
  event names.
- State any caveat here, in the card, not in the prose above it. This is the last thing read.
- Reference: `seed/skills/ticket-booking/assets/booking-review.json`.

## 8. The visual contract

Styling is not a template concern. Surfaces are painted by scoped `.a2ui-surface` rules in
`my-copilot-app/app/globals.css` — the "Ink & Iris" theme, iris accent `#5b5bd6`, light and dark
variants, reduced-motion support. Templates never carry colours, sizes or spacing.

What a template *does* control is the semantic slots the CSS keys off:

| Author this | Renders as |
|---|---|
| `Text` `variant: "h3"` | 18px section heading |
| `Text` `variant: "h4"` | 16px card title — the default for a surface title |
| `Text` `variant: "h5"` | heading weight and tracking at the browser's default size; used for the one number that discriminates between options |
| `Text` `variant: "caption"` | muted secondary line |
| `Text` `variant: "body"` | default paragraph |
| `Button` `variant: "primary"` | iris gradient fill |
| `Divider` `axis: "horizontal"` | hairline rule |

Two hard constraints from the layout:

- **The card is `min(536px, 100%)`.** Design for 536 and check at 320.
- **A `Row` with `justify: "spaceBetween"` protects its last child from wrapping.** That is what
  keeps a price or a count on one line while the label beside it flows. Use that row shape
  deliberately for label/value pairs; do not expect the protection anywhere else.

Accessibility: every input needs a `label` (`TextField`, `CheckBox`, `ChoicePicker` and
`DateTimeInput` all take one, and `CheckBox` and `ChoicePicker` require it). Give `Image` a
`description`. Do not rely on placeholder text — there is no placeholder property.

## 9. What the validator will reject

`SurfaceValidator` runs at skill import and returns **every** failure at once, not the first.
Design against this list rather than discovering it one upload at a time.

| Check | Rejected when |
|---|---|
| JSON | not parseable, has comments, has trailing commas, or nests deeper than 32 |
| Top level | not an object |
| `surfaceId` | missing, not a string, or blank |
| `components` | missing, not an array, or empty |
| Component id | missing, blank, or not a string |
| Component type | not one of the 18 basic-catalog names |
| Property | not in that component's property list |
| Required property | absent |
| Enumerated value | outside the allowed set for that property |
| Child reference | points at a missing id, or at itself |
| `root` | absent, or not `Column` / `Row` / `Card` / `List` |
| Reachability | a component no other component references |
| Binding | a `{ "path": … }` with no seed in `data` |

The 18 components are `Text`, `Image`, `Icon`, `Video`, `AudioPlayer`, `Row`, `Column`, `List`,
`Card`, `Tabs`, `Modal`, `Divider`, `Button`, `TextField`, `CheckBox`, `ChoicePicker`, `Slider`,
`DateTimeInput`. There is no nineteenth, and unknown properties are dropped by the catalog
rather than rendered — which is why the validator refuses them.

The authoritative property and enum table is `A2uiCatalog.cs`, a hand-maintained snapshot of
`@a2ui/web_core`'s `basic_catalog.json`. `A2uiCatalogDriftTests` fails if the two diverge, so an
npm bump surfaces as a failing test rather than as a mystery import rejection.

## 10. Anti-patterns

| Don't | Because |
|---|---|
| Generate the tree from data at render time | Then it is Path A with extra steps, and none of the validation applies |
| Bind to `/__inputs/...` | That namespace is the frontend's mirror; writing to it fights the renderer |
| Give two panes of a comparison different structures | The comparison stops working; the eye has to re-find every value |
| Put two `primary` buttons on one card | Two primary actions is two surfaces |
| Seed a `ChoicePicker` with a string | It stores an array; a string seed is a frozen picker |
| Reuse a `surfaceId` across steps | The second render overwrites the first card instead of adding one |
| Rename an id to tidy up | It breaks `advanceTab` and every `/__inputs/<id>` path already in flight |
| Explain the surface in prose above it | The card already says it; the duplication reads as a bug |
| Render two steps in one turn | The user has not answered the first one |

## 11. Design checklist

Before a template goes in `assets/`:

- [ ] One archetype — Collect, Choose or Confirm — and only one.
- [ ] `surfaceId` is unique across the whole skill set.
- [ ] `root` exists and is a layout component.
- [ ] Every component is reachable from `root`.
- [ ] Every bound path is seeded in `data`, with the right type.
- [ ] Every `ChoicePicker` seed is an array.
- [ ] Every input has a `label`.
- [ ] Every `Button` has a `child` label component and a `snake_case` event name.
- [ ] Exactly one `primary` button.
- [ ] If it uses a wizard `Tabs`: `__tabs` is seeded and `advanceTab` names the right id.
- [ ] Ids follow `<step>-<field>` and the labels follow `<button>-label`.
- [ ] It reads correctly at 320px as well as 536px.
- [ ] The SKILL.md documents the `values` shape exactly as the `data` block nests it.

## 12. Worked skeleton

A minimal Collect surface with every convention in place. Copy it and rename.

```json
{
  "surfaceId": "demo-collect",
  "components": [
    { "id": "root", "component": "Column",
      "children": ["title", "hint", "who", "when", "size", "mode", "urgent", "sep", "submit"] },

    { "id": "title", "component": "Text", "text": { "path": "/req/title" }, "variant": "h4" },
    { "id": "hint",  "component": "Text", "text": { "path": "/req/hint" },  "variant": "caption" },

    { "id": "who",  "component": "TextField", "label": "Who is this for?",
      "value": { "path": "/req/who" }, "variant": "shortText" },
    { "id": "when", "component": "DateTimeInput", "label": "When",
      "value": { "path": "/req/when" }, "enableDate": true, "enableTime": false },
    { "id": "size", "component": "Slider", "label": "How many",
      "value": { "path": "/req/size" }, "min": 1, "max": 10 },

    { "id": "mode", "component": "ChoicePicker", "label": "What matters most?",
      "variant": "mutuallyExclusive", "value": { "path": "/req/mode" },
      "options": [
        { "label": "Cost",  "value": "cost" },
        { "label": "Speed", "value": "speed" },
        { "label": "Care",  "value": "care" }
      ] },

    { "id": "urgent", "component": "CheckBox", "label": "This is urgent",
      "value": { "path": "/req/urgent" } },

    { "id": "sep", "component": "Divider", "axis": "horizontal" },

    { "id": "submit", "component": "Button", "variant": "primary", "child": "submit-label",
      "action": { "event": {
        "name": "demo_request_submit",
        "context": {
          "who":    { "path": "/req/who" },
          "when":   { "path": "/req/when" },
          "size":   { "path": "/req/size" },
          "mode":   { "path": "/req/mode" },
          "urgent": { "path": "/req/urgent" }
        }
      } } },
    { "id": "submit-label", "component": "Text", "text": "Continue" }
  ],
  "data": {
    "req": {
      "title": "Tell us what you need",
      "hint": "Four questions, then we'll put options together.",
      "who": "",
      "when": "",
      "size": 2,
      "mode": ["speed"],
      "urgent": false
    }
  }
}
```

The matching `values` contract, as the SKILL.md should state it:

```json
{ "skill": "<skill-name>", "surface": "demo-collect",
  "values": { "req": { "who": "…", "when": "YYYY-MM-DD", "size": 2, "mode": ["speed"], "urgent": false } } }
```

Send only the branch you are filling in. `size` is a number, `mode` is an array, `urgent` is a
boolean — the merge is shape-checked and a mismatch is returned to the model as an error rather
than rendered.

---

## References

| For | Read |
|---|---|
| Packing and shipping a skill | `docs/Writing-a-SKILL.md` |
| How a surface reaches the screen | `docs/A2UI-and-AG-UI.md` |
| Why Path A and Path B both exist | `docs/A2UI-and-AG-UI.md` § 4 |
| Skill package import security | `docs/skill-import-security.md` |
| The catalog, verbatim | `NTG.Agent.Orchestrator/Services/Skills/A2uiCatalog.cs` |
| The rules, verbatim | `NTG.Agent.Orchestrator/Services/Skills/SurfaceValidator.cs` |
| The styling | `my-copilot-app/app/globals.css` |
| A2UI v0.9 spec | https://a2ui.org/specification/v0.9-a2ui/ |
