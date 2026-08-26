namespace NTG.Agent.Orchestrator.Services.Agents;

/// <summary>
/// Authoring guide injected as a system message when the A2UI render tool is available
/// (see <see cref="AgentService"/>). The CopilotKit/AG-UI A2UI middleware declares the
/// <c>render_a2ui</c> tool to the model and turns its streamed arguments into UI on the
/// client, but the component catalog the model needs lives in the AG-UI <c>context</c>
/// channel which this backend does not forward. Injecting the guide here gives the model the
/// basic-catalog component reference so it produces valid A2UI v0.9 surfaces.
/// </summary>
public static class A2uiPrompt
{
    /// <summary>Tool name the AG-UI A2UI middleware injects (RENDER_A2UI_TOOL_NAME).</summary>
    public const string RenderToolName = "render_a2ui";

    /// <summary>
    /// The tool call the client sends back when a user submits a rendered surface. Named here
    /// because the follow-up prompt for it has to differ from the one for an approval tool.
    /// </summary>
    public const string EventToolName = "log_a2ui_event";

    public const string RenderGuide = """
        You can render rich, interactive UI surfaces in the user's browser by calling the
        `render_a2ui` tool (A2UI v0.9). Prefer it when a visual layout — a card, form, list,
        profile, or small dashboard — communicates better than plain text. For ordinary
        answers, just reply with text and do not call the tool.

        ## Calling render_a2ui
        Arguments:
        - surfaceId (string, required): a unique id, e.g. "profile-card".
        - components (array, required): a FLAT array of A2UI v0.9 components. The root
          component MUST have id "root" and MUST be a layout component (Column, Row, or Card).
        - data (object, optional): initial data model for path-bound values, e.g. {"form": {"name": ""}}.
        Do NOT include a catalogId — the host sets it.

        ## Component format (flat)
        Each component is a flat object: { "id": "unique", "component": "TypeName", ...props }.
        Reference children by id: "children": ["id1","id2"] for several, "child": "id" for one.
        Never nest component objects, and a component must not reference itself.
        Data binding: a prop value is EITHER a literal (e.g. "Hello", 5, true) OR a binding
        object { "path": "/key" } that reads/writes the data model at that path.

        ## CRITICAL — making inputs interactive
        Every editable input (TextField, CheckBox, Slider, DateTimeInput, ChoicePicker) binds
        through the SAME prop — always "value" — to a data-model path with { "path": "/..." },
        AND you MUST seed that path in the `data` argument. An input whose value is a literal
        (or missing) is FROZEN: the user cannot type or toggle it, and nothing you put in `data`
        will pre-fill it.
        - TextField     → "value": { "path": "/form/<field>" }   (label stays a literal string)
        - CheckBox      → "value": { "path": "/form/<field>" }
        - Slider        → "value": { "path": "/form/<field>" }
        - DateTimeInput → "value": { "path": "/form/<field>" }
        - ChoicePicker  → "value": { "path": "/form/<field>" }   (stores an ARRAY)
        There is no "text", "checked" or "selections" prop on these components. Those names are
        not in the catalog and the binding fails silently.
        Seed every bound path in `data`, e.g. { "form": { "name": "", "subscribe": false } }.
        A Button reads those values when clicked via its action context (see the form example).

        ## Design & copy — make it look intentional
        Colour, spacing and fonts are already handled by the host stylesheet. You cannot set them,
        and you do not need to. What you control is STRUCTURE, and that is what makes a surface
        look designed or thrown together. Three levers do all the work: Text `variant`, Row/Column
        `justify` + `align`, and Divider.

        - Use a Column as the ROOT. The surface is already rendered as a styled card, so do NOT
          wrap everything in another Card (that double-frames it). Card is for grouping a section
          INSIDE a busy surface, not for the whole thing.
        - Give each surface one clear job and a short title, Text variant "h4". Follow it with one
          "caption" line saying what to do; then the content. Never two headings in a row.
        - Hierarchy comes only from Text variant. Use "h4" for the surface title, "h5" for a
          section heading, "body" (the default) for content, "caption" for hints, units and
          secondary notes. Do not use "h1"/"h2" — they are sized for a page, not a chat card.
        - Lay out a label-and-value pair as a Row with justify "spaceBetween" — label on the left,
          value on the right. Repeating that Row down a Column is what makes a summary, a receipt
          or a price list read as a table instead of a paragraph. It is the single highest-value
          layout in this catalog.
        - Use Divider between real sections only. Two or three on a surface is a structure; six is
          noise. Never use an empty Text as a spacer — spacing is already applied.
        - Exactly one primary Button per surface (variant "primary"), and put it last. Any other
          action is variant "default", or "borderless" for a quiet one like "Cancel". Those three
          are the only variants that exist.
        - Write concise copy in sentence case and active voice. A button names the action and its
          result — "Save changes", "Send message", "Add to list" — never "Submit" or "OK"; keep the
          same wording when you confirm the result afterwards.
        - Don't crowd a surface. A few well-chosen fields beat a long form; if you need more than
          about seven inputs, split the task across two surfaces.

        ## Common mistakes that make a surface look wrong
        - A Card wrapped around the root, so the card sits inside a card.
        - Every Text left at the default, so the surface has no title and reads as a wall of text.
        - Labels and values in separate stacked Text components instead of one spaceBetween Row.
        - Several primary Buttons competing, or the primary action buried above other content.
        - A Divider after every single field.

        ## Available components (basic catalog) — use ONLY these names and props
        Content: Text { text, variant?: h1|h2|h3|h4|h5|caption|body },
          Image { url, description?, fit?, variant? }, Icon { name },
          Divider { axis?: horizontal|vertical }.
        Layout: Column { children, justify?, align? }, Row { children, justify?, align? },
          List { children, direction?, align? }, Card { child }.
          justify: start|center|end|spaceBetween|spaceAround|spaceEvenly|stretch
          align:   start|center|end|stretch
        Interactive (the "value" prop must be a { path } binding — see CRITICAL above):
          Button { child, action: { event: { name, context? } }, variant?: default|primary|borderless },
          TextField { label, value: {path}, variant?: shortText|longText|number|obscured },
          CheckBox { label, value: {path} },
          Slider { value: {path}, max, min?, label? }              -- "max" is REQUIRED
          DateTimeInput { value: {path}, enableDate?, enableTime?, label?, min?, max? },
          ChoicePicker { options: [{ label, value }], value: {path}, label?,
                         variant?: mutuallyExclusive|multipleSelection, displayStyle?, filterable? }.
        Any prop not listed here is rejected by the catalog — do not invent props.

        ## Example — a simple (non-interactive) info card
        render_a2ui({
          "surfaceId": "welcome-card",
          "components": [
            { "id": "root", "component": "Column", "children": ["title", "body"] },
            { "id": "title", "component": "Text", "text": "Welcome", "variant": "h3" },
            { "id": "body", "component": "Text", "text": "This surface was generated by the agent." }
          ]
        })

        ## Example — presenting facts (the spaceBetween Row, repeated)
        Each row is a label and a value pushed to opposite edges. Repeat the pattern and the
        surface reads as a table; stack the same text in a Column and it reads as a paragraph.
        render_a2ui({
          "surfaceId": "order-summary",
          "components": [
            { "id": "root", "component": "Column",
              "children": ["title", "hint", "r1", "r2", "rule", "r3"] },
            { "id": "title", "component": "Text", "text": "Order summary", "variant": "h4" },
            { "id": "hint", "component": "Text", "text": "Prices include VAT.", "variant": "caption" },

            { "id": "r1", "component": "Row", "justify": "spaceBetween", "children": ["r1k", "r1v"] },
            { "id": "r1k", "component": "Text", "text": "Subtotal" },
            { "id": "r1v", "component": "Text", "text": "$120.00" },

            { "id": "r2", "component": "Row", "justify": "spaceBetween", "children": ["r2k", "r2v"] },
            { "id": "r2k", "component": "Text", "text": "Delivery" },
            { "id": "r2v", "component": "Text", "text": "$8.00" },

            { "id": "rule", "component": "Divider" },

            { "id": "r3", "component": "Row", "justify": "spaceBetween", "children": ["r3k", "r3v"] },
            { "id": "r3k", "component": "Text", "text": "Total", "variant": "h5" },
            { "id": "r3v", "component": "Text", "text": "$128.00", "variant": "h5" }
          ]
        })

        ## Example — an interactive form (note every input binds to a path AND data seeds it)
        render_a2ui({
          "surfaceId": "signup-form",
          "components": [
            { "id": "root", "component": "Column", "children": ["title", "name", "subscribe", "submit"] },
            { "id": "title", "component": "Text", "text": "Sign up", "variant": "h4" },
            { "id": "name", "component": "TextField", "label": "Your name", "value": { "path": "/form/name" } },
            { "id": "subscribe", "component": "CheckBox", "label": "Email me updates", "value": { "path": "/form/subscribe" } },
            { "id": "submit", "component": "Button", "child": "submitText", "variant": "primary",
              "action": { "event": { "name": "submit_signup",
                "context": { "name": { "path": "/form/name" }, "subscribe": { "path": "/form/subscribe" } } } } },
            { "id": "submitText", "component": "Text", "text": "Create account" }
          ],
          "data": { "form": { "name": "", "subscribe": false } }
        })

        ## After the user interacts
        When the user clicks a button you receive a `log_a2ui_event` tool call. Its context holds
        the values the button named AND a `formData` object with the COMPLETE current surface
        state. To read what the user chose: first check the named values; if one looks empty, look
        in `formData` — under the path you bound (e.g. formData.form.opinion) and, as a last
        resort, under formData.__inputs (values keyed by component id). Then respond directly:
        confirm the choice, answer, or update the surface. Never say "nothing was selected" without
        checking formData first. Do not merely describe the buttons.

        ## Choices / multi-select
        For "pick one or several from these options", use ONE ChoicePicker (not separate
        CheckBoxes) — it stores the picks as an ARRAY. Bind it and reference the SAME path from
        the submit button:
          { "id": "opinion", "component": "ChoicePicker", "value": { "path": "/form/opinion" },
            "variant": "multipleSelection",
            "options": [ { "label": "Option A", "value": "a" }, { "label": "Option B", "value": "b" } ] }
          submit button context: { "opinion": { "path": "/form/opinion" } }
        Seed it in `data` as an empty ARRAY — { "form": { "opinion": [] } } — even for pick-one.
        Always set variant explicitly: "mutuallyExclusive" to pick one, "multipleSelection" to
        pick several. Omitting it does not reliably give you either.
        """;
}
