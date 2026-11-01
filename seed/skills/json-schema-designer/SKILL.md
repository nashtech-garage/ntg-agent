---
name: json-schema-designer
description: Designs basic JSON Schemas in an interactive A2UI surface, generates sample JSON, and deterministically validates JSON instances against Draft 2020-12 schemas.
license: Apache-2.0
compatibility: Requires render_skill_surface, json_schema_generate, json_schema_validate, and a browser client with the generic A2UI skill controls.
metadata:
  author: ntg-agent
  version: "1.0"
---

# JSON Schema designer

Design a schema in one A2UI surface with three tabs: **Design**, **Validation**, and **Results**.
Render `skill: "json-schema-designer"` and `surface: "json-schema-designer"`. Render once to
start, then re-render this same surface to show generated schema or validation results.

Enable `json_schema_generate` and `json_schema_validate` for the agent in the Admin MCP tools
settings before using this skill. Skill binding and MCP tool enablement are separate choices. If
either tool is unavailable, explain that the agent needs the corresponding MCP tool enabled; do not
construct schema or validation results yourself.

The generic `ArrayEditor` and `TextEditor` controls own local editing. The tab strip handles
navigation. These interactions must not invoke the agent; only the two primary actions below call
server tools.

## Start the designer

When the user asks to build a schema:

1. Infer a concise schema name and top-level properties from the request.
2. Infer each property's basic type and required state. Treat properties as required by default
   unless the user marks them optional or the description suggests otherwise.
3. Give every property a unique, stable `id` that does not depend on its name.
4. If the user did not specify enough properties, suggest a small useful starting set rather than
   asking a sequence of questions.
5. Render the surface on tab 0, preserving the user's exact names and requested choices.

Say one short line before rendering, such as "Let's design your Product schema." Do not narrate
the controls or repeat the form contents.

Initial render example:

```json
{
  "skill": "json-schema-designer",
  "surface": "json-schema-designer",
  "values": {
    "__tabs": { "wizard": 0 },
    "design": {
      "schemaName": "Product",
      "importWarnings": "",
      "properties": [
        { "id": "property-product-id", "name": "productId", "type": "string", "required": true },
        { "id": "property-name", "name": "name", "type": "string", "required": true },
        { "id": "property-price", "name": "price", "type": "number", "required": true }
      ]
    }
  }
}
```

The property editor offers `string`, `number`, `integer`, `boolean`, `object`, `array`, and `null`.
Objects and arrays have no nested-structure editor. Never invent additional types.

## Import an existing JSON Schema

If the user's prompt contains a schema, inspect it and populate the design from its title, direct
`properties`, basic types, and `required` array. Use the same initial render as above.

The designer does not represent constraints or advanced structure. If the imported schema uses
features such as `minimum`, `maximum`, `minLength`, `maxLength`, `pattern`, `enum`, `format`,
`default`, `$ref`, `$defs`, `oneOf`, `anyOf`, `allOf`, nested object properties, or array item
schemas, list the unsupported features in `design.importWarnings`. Tell the user that generating
from the simplified design replaces those features; never claim they are preserved. For a union
type or another type the table cannot represent, choose a reasonable basic type and include that
loss in the warning.

## Read surface submissions

Every submitted action arrives through `log_a2ui_event` with the event's named context and a
`formData` object containing the complete surface state. Prefer the submitted values in
`formData.design`, `formData.schema`, and `formData.sample`; the event context also contains the
values named by that button. The generic editors write their current values to the A2UI data model.

Property add/edit/type/required/delete actions, text edits, copy, reset, and tab navigation are
handled in the browser. Do not call a tool or re-render for them.

## Re-rendering and reload persistence

`render_skill_surface` starts each render from the template's default data; it does not carry
values forward from a previous render. After any server-side action, re-render with a complete
snapshot under `values`, including `__tabs`, `design`, `schema`, `sample`, and `results`. Start
with every corresponding branch from the submitted `formData` and change only the fields specified
below. Never send only the branch being updated. Omitted branches return to their template
defaults, so they will be lost when the conversation is reopened.

## Generate Schema

When the `generate_schema` event arrives:

1. Read `schemaName` and the complete property array from the submitted design.
2. Call `json_schema_generate` with `schemaName` and `properties`. Do not construct the schema or
   sample yourself.
3. If the tool returns `succeeded: false`, re-render the same surface on tab 0 with all five
   branches from the submitted `formData` unchanged. Then report its errors and do not claim
   generation succeeded.
4. On success, use its `schemaJson` and `sampleJson` exactly as returned. Re-render the same surface
   with all five branches: set `__tabs.wizard` to 1; preserve the complete submitted `design`;
   preserve other fields in `schema` while setting `schema.json` to `schemaJson`; preserve other
   fields in `sample` while setting both `sample.json` and `sample.originalJson` to `sampleJson`;
   and reset the complete `results` branch to `status: "Not run"`, `schemaValid: false`,
   `jsonValid: false`, `instanceValid: false`, `errorCount: 0`, `errors: []`, `warnings: []`, and
   `report: "Not run"`.

The generated schema must be the shared tool's Draft 2020-12 output. Do not construct it yourself
or change the generated JSON.

## Validate & Test

When the `validate_instance` event arrives:

1. Read the current `schema.json` and current `sample.json` from the submitted form data. The sample
   may have been edited since generation.
2. Call `json_schema_validate` with `schemaJson` and `sampleJson`.
3. Copy the tool's returned `report`, status fields, and structured `errors` exactly into `results`.
   Preserve `results.warnings` from the submitted form data because the tool does not return
   warnings. Do not infer, rewrite, or omit errors.
4. Re-render the same surface with all five branches: set `__tabs.wizard` to 2; preserve the
   complete submitted `design`, `schema`, and `sample`; and replace `results` with the complete
   validation result, including all status fields, `errors`, the preserved `warnings`, and `report`.

The backend distinguishes an invalid schema, invalid sample JSON syntax, a valid JSON instance
that violates the schema, and successful validation. Error entries include `path`, `keyword`, and
`message`.

## Navigation and state

- The tabs navigate locally and preserve all data. Do not re-render or regenerate when the user
  switches tabs.
- Editing the schema name or any property clears the generated schema and prior report locally. The
  user's sample text remains until the next successful generation replaces it.
- Editing the sample clears the prior report locally but preserves the schema and sample.
- The user can validate repeatedly without regenerating the schema.
- On a successful Generate Schema action, use the backend's new sample as both current and original
  sample and clear the previous validation results.
- On a Validate & Test action, preserve the current sample and schema while replacing only results.
- Every server-side re-render must still include all five current branches, even when only one
  branch changed.
- The client advances tabs immediately when a primary action is clicked; the render response should
  set the same requested tab index.

The only surface is `json-schema-designer`. Do not create another surface or send A2UI component
JSON yourself.
