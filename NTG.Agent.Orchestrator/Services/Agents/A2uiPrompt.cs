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
        - Use a Column as the ROOT. The surface is already rendered as a styled card, so do NOT
          wrap everything in another Card (that double-frames it).
        - Give each surface one clear job and a short title (Text variant "h4"). Group related
          fields in a Column in a sensible order; use Divider only to separate real sections.
        - Establish hierarchy with Text variants: "h4" for the title, "body" for content,
          "caption" for hints or secondary notes.
        - Write concise copy in sentence case and active voice. A button names the action and its
          result — "Save changes", "Send message", "Add to list" — never "Submit" or "OK"; keep the
          same wording when you confirm the result afterwards.
        - Prefer a single primary Button (variant "primary"); give secondary actions variant
          "secondary" or "text". Don't crowd a surface — a few well-chosen fields beat a long form.

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
