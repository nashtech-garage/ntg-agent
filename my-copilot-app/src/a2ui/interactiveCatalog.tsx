"use client";

// Custom A2UI catalog = the official basic catalog with the interactive components
// (TextField, CheckBox, ChoicePicker, Button) replaced so the round-trip works reliably
// even when the agent's generated data-model bindings are imperfect.
//
// The problem: A2UI inputs are controlled components whose setValue only writes to the data
// model when the value is bound to a { path } (see @a2ui/web_core generic-binder), and a
// Button only sends the paths its action context names. So if the agent forgets a binding,
// or the input's path and the button's path don't match, the user's answer never reaches it.
//
// The fix:
//  - Inputs keep local React state (always editable) AND write their value into the data
//    model on every change: through setValue (the bound path, when present) and ALSO at a
//    deterministic fallback path `/__inputs/<id>`, so the value is captured no matter what.
//  - The Button dispatches the COMPLETE data model (as `formData`) alongside the resolved
//    action context, using the correct A2UI payload shape `{ event: { name, context } }`.
//
// Tabs get the same treatment in the other direction. The stock Tabs keeps its selected index in
// plain React state, so the agent can neither read it nor change it. `InteractiveTabs` binds that
// index to the data model over a *convention path* — `/__tabs/<componentId>` — mirroring the
// `/__inputs/<componentId>` convention above. See the component for why a convention path and
// not a declared schema property.
//
// Visual styling comes from the scoped `.a2ui-surface` rules in app/globals.css.
import React from "react";
import { Catalog } from "@a2ui/web_core/v0_9";
import {
  TextFieldApi,
  CheckBoxApi,
  ChoicePickerApi,
  ButtonApi,
  TabsApi,
} from "@a2ui/web_core/v0_9/basic_catalog";
import { basicCatalog, createReactComponent } from "@copilotkit/a2ui-renderer";

/* eslint-disable @typescript-eslint/no-explicit-any */

// Mirror a value into the data model at a stable fallback path, so a Button that sends the
// whole data model always sees it — even when the agent never bound this input to a path.
function captureValue(context: any, value: unknown) {
  const id = context?.componentModel?.id;
  if (!id) return;
  try { context?.dataContext?.dataModel?.set(`/__inputs/${id}`, value); } catch { /* ignore */ }
}

const InteractiveTextField = createReactComponent(TextFieldApi as any, ({ props, context }: any) => {
  const [value, setValue] = React.useState<string>(props.value ?? "");
  const id = React.useId();
  const isLong = props.variant === "longText";
  const type = props.variant === "number" ? "number" : props.variant === "obscured" ? "password" : "text";

  const onChange = (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
    const v = e.target.value;
    setValue(v);
    props.setValue?.(v);     // bound path, when present
    captureValue(context, v); // always-on fallback path
  };

  const fieldProps = { id, value, onChange, style: { width: "100%", boxSizing: "border-box" as const } };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 4, width: "100%", margin: "8px" }}>
      {props.label ? <label htmlFor={id}>{props.label}</label> : null}
      {isLong ? <textarea {...fieldProps} /> : <input type={type} {...fieldProps} />}
    </div>
  );
});

const InteractiveCheckBox = createReactComponent(CheckBoxApi as any, ({ props, context }: any) => {
  const [checked, setChecked] = React.useState<boolean>(!!props.value);
  const id = React.useId();

  const onChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const v = e.target.checked;
    setChecked(v);
    props.setValue?.(v);
    captureValue(context, v);
  };

  return (
    <div style={{ display: "flex", alignItems: "center", gap: 8, margin: "8px" }}>
      <input id={id} type="checkbox" checked={checked} onChange={onChange} />
      {props.label ? <label htmlFor={id}>{props.label}</label> : null}
    </div>
  );
});

const InteractiveChoicePicker = createReactComponent(ChoicePickerApi as any, ({ props, context }: any) => {
  const [selected, setSelected] = React.useState<string[]>(
    Array.isArray(props.value) ? props.value : [],
  );
  const single = props.variant === "mutuallyExclusive";
  const fallbackName = React.useId();
  const groupName = `choice-${context?.componentModel?.id ?? fallbackName}`;

  const toggle = (val: string) => {
    const next = single
      ? [val]
      : selected.includes(val)
        ? selected.filter((v) => v !== val)
        : [...selected, val];
    setSelected(next);
    props.setValue?.(next);
    captureValue(context, next);
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: 8, width: "100%", margin: "8px" }}>
      {props.label ? <strong style={{ fontSize: 14 }}>{props.label}</strong> : null}
      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {(props.options || []).map((opt: any, i: number) => (
          <label key={i} style={{ display: "flex", alignItems: "center", gap: 8, cursor: "pointer" }}>
            <input
              type={single ? "radio" : "checkbox"}
              name={single ? groupName : undefined}
              checked={selected.includes(opt.value)}
              onChange={() => toggle(opt.value)}
            />
            <span style={{ fontSize: 14 }}>{opt.label}</span>
          </label>
        ))}
      </div>
    </div>
  );
});

// Button that dispatches the full surface data model with the action, so the user's answers
// always reach the agent. Uses the correct A2UI payload shape: { event: { name, context } }.
const InteractiveButton = createReactComponent(ButtonApi as any, ({ props, buildChild, context }: any) => {
  const onClick = () => {
    const actionDef = context?.componentModel?.properties?.action?.event;
    if (!actionDef) {
      props.action?.(); // decorative button with no action — keep default behavior
      return;
    }

    const dataModel = context?.dataContext?.dataModel;
    let formData: Record<string, any> = {};
    try { formData = dataModel?.get("/") ?? {}; } catch { /* ignore */ }

    // Resolve the model-defined context paths against the data model (best effort).
    const rawCtx: Record<string, any> = actionDef.context ?? {};
    const resolved: Record<string, any> = {};
    for (const [key, val] of Object.entries(rawCtx)) {
      if (val && typeof val === "object" && "path" in (val as any)) {
        try { resolved[key] = dataModel?.get((val as any).path); } catch { resolved[key] = null; }
      } else {
        resolved[key] = val;
      }
    }

    // Move the wizard on immediately, before the agent has answered.
    //
    // A step button in a tabbed flow reads as "next", not "submit": the user expects the tab to
    // change on click. Waiting for the round trip means ten to twenty seconds sitting on a form
    // they have finished with, and no signal that the click registered. So the tab advances
    // locally here and the submission still goes out; the agent's own /__tabs write lands on the
    // tab we already moved to and is a no-op. The next tab's content is placeholder text until
    // the answer arrives, which is why the template seeds it with something worth reading.
    //
    // `advanceTab` names the Tabs component to move and is UI plumbing, so it is stripped from
    // the payload rather than sent to the agent as if it were an answer.
    const { advanceTab, ...eventContext } = resolved;
    if (typeof advanceTab === "string" && advanceTab.length > 0) {
      try {
        const tabPath = `/__tabs/${advanceTab}`;
        const current = Number(dataModel?.get(tabPath));
        dataModel?.set(tabPath, (Number.isFinite(current) ? current : 0) + 1);
      } catch { /* the tab simply does not move; the submission is unaffected */ }
    }

    try {
      context.dispatchAction({
        event: { name: actionDef.name ?? "submit", context: { ...eventContext, formData } },
      });
    } catch {
      props.action?.(); // fall back to the renderer's default dispatch
    }
  };

  const isPrimary = props.variant === "primary";
  const isBorderless = props.variant === "borderless" || props.variant === "text";
  const style: React.CSSProperties = {
    backgroundColor: isPrimary ? "var(--a2ui-primary-color)" : isBorderless ? "transparent" : "var(--a2ui-card)",
    color: isPrimary ? "#fff" : "inherit",
    border: isPrimary || isBorderless ? "none" : "1px solid var(--a2ui-border)",
  };

  return (
    <button onClick={onClick} disabled={props.isValid === false} style={style}>
      {props.child ? buildChild(props.child) : null}
    </button>
  );
});

// Where the agent parks the tab it wants shown, keyed by component id.
//
// Why a convention path and not a schema property: `TabsApi.schema` is `.strict()` and the
// catalog JSON schemas are `unevaluatedProperties: false`, so a `value`/`selectedIndex` prop
// would be rejected as an unknown property unless we forked the schema — and a forked schema
// drifts from the one the agent is prompted with. An override, by contrast, may read and write
// the data model at ANY path (`dataContext.subscribeDynamicValue` / `dataModel.get` / `.set`),
// with no schema involvement at all. So the channel lives in the data model, out of band, and
// costs nothing when unused: a surface that never mentions `/__tabs` behaves exactly as stock.
const TABS_PATH_PREFIX = "/__tabs/";

function tabsPath(context: any): string | null {
  const id = context?.componentModel?.id;
  return id ? `${TABS_PATH_PREFIX}${id}` : null;
}

function readTabValue(context: any, path: string | null): unknown {
  if (!path) return undefined;
  try { return context?.dataContext?.dataModel?.get(path); } catch { return undefined; }
}

// Coerce whatever sits at /__tabs/<id> into a usable index, or null for "nothing to apply".
// The agent may write a number, a numeric string, or (when it gets it wrong) null, a boolean,
// an object or an index past the end. A throw here would take the whole surface down, so
// garbage is ignored and out-of-range values clamp into the strip.
function toTabIndex(raw: unknown, count: number): number | null {
  if (count <= 0) return null;
  const n =
    typeof raw === "number" ? raw
      : typeof raw === "string" && raw.trim() !== "" ? Number(raw)
        : NaN;
  if (!Number.isFinite(n)) return null;
  return Math.min(Math.max(Math.trunc(n), 0), count - 1);
}

// Tabs the agent can drive and read back. Visually identical to the stock component; the
// difference is entirely in where the selected index comes from and where it goes.
const InteractiveTabs = createReactComponent(TabsApi as any, ({ props, buildChild, context }: any) => {
  const tabs: any[] = Array.isArray(props.tabs) ? props.tabs : [];
  const count = tabs.length;
  const path = tabsPath(context);
  const domId = React.useId();

  // Seeded from the model once, then local interaction owns it — the same shape as
  // InteractiveTextField, which seeds from `props.value` and never re-reads it.
  const [seed] = React.useState(() => readTabValue(context, path));
  const [selectedIndex, setSelectedIndex] = React.useState<number>(() => toTabIndex(seed, count) ?? 0);

  // The last value we saw ARRIVE on the path. Re-syncing is gated on this changing, not on the
  // incoming value differing from what is on screen: if the agent selects tab 1 and the user
  // clicks back to tab 0, every later re-render still reports 1 on the path, and comparing
  // against the screen would drag the user back to 1 on each one. Comparing against the last
  // arrival means only a genuinely NEW instruction moves them.
  //
  // The user's own clicks deliberately do NOT write back to /__tabs: it stays a one-way
  // agent -> user channel (intent), while /__inputs stays the user -> agent one (state).
  const lastArrivedRef = React.useRef<unknown>(seed);
  const countRef = React.useRef<number>(count);
  React.useEffect(() => { countRef.current = count; }, [count]);

  React.useEffect(() => {
    const dataContext = context?.dataContext;
    if (!path || typeof dataContext?.subscribeDynamicValue !== "function") return;

    const apply = (raw: unknown) => {
      if (Object.is(raw, lastArrivedRef.current)) return; // unchanged — leave the user alone
      lastArrivedRef.current = raw;
      const next = toTabIndex(raw, countRef.current);
      if (next !== null) setSelectedIndex(next); // unusable value — stay where we are
    };

    let sub: any;
    try {
      sub = dataContext.subscribeDynamicValue({ path }, apply);
    } catch {
      return; // no reactive channel available — behave exactly like the stock component
    }
    // The surface's data ops land after the components are mounted, so a value that arrived
    // between this render and this effect would otherwise be missed.
    apply(sub?.value);

    return () => { try { sub?.unsubscribe?.(); } catch { /* ignore */ } };
  }, [context, path]);

  const activeIndex = Math.min(selectedIndex, Math.max(count - 1, 0));
  const activeTab = tabs[activeIndex];
  const activeTitle = typeof activeTab?.title === "string" ? activeTab.title : undefined;

  // Report where the user is, through the same fallback path every other input uses, so it rides
  // along in the `formData` InteractiveButton already submits. We mirror `{ index, title }` and
  // not the bare index: an index is meaningless to a model reading formData ("tiers: 2" — two of
  // what?), while the title is the label the user actually saw and is what the agent reasons
  // about. The index is kept alongside it because titles are data-bound and may be blank,
  // duplicated, or still unresolved, and it is what indexes back into `tabs`.
  React.useEffect(() => {
    if (count === 0) return; // nothing on screen yet — do not report a tab the user cannot see
    captureValue(context, { index: activeIndex, ...(activeTitle === undefined ? {} : { title: activeTitle }) });
  }, [context, count, activeIndex, activeTitle]);

  // The stock component ships no accessibility semantics at all; added here (tablist/tab roles,
  // aria-selected, roving tabindex, arrow/Home/End) since it is all invisible to the layout.
  const tabRefs = React.useRef<Array<HTMLButtonElement | null>>([]);
  const onKeyDown = (e: React.KeyboardEvent<HTMLDivElement>) => {
    if (count === 0) return;
    const last = count - 1;
    let next: number | null = null;
    if (e.key === "ArrowRight" || e.key === "ArrowDown") next = activeIndex >= last ? 0 : activeIndex + 1;
    else if (e.key === "ArrowLeft" || e.key === "ArrowUp") next = activeIndex <= 0 ? last : activeIndex - 1;
    else if (e.key === "Home") next = 0;
    else if (e.key === "End") next = last;
    if (next === null) return;
    e.preventDefault();
    setSelectedIndex(next);
    tabRefs.current[next]?.focus();
  };

  return (
    <div style={{ display: "flex", flexDirection: "column", width: "100%", margin: "8px" }}>
      <div
        role="tablist"
        onKeyDown={onKeyDown}
        style={{ display: "flex", borderBottom: "1px solid #ccc", marginBottom: "8px" }}
      >
        {tabs.map((tab: any, i: number) => (
          <button
            key={i}
            type="button"
            role="tab"
            id={`${domId}tab-${i}`}
            aria-selected={activeIndex === i}
            // Only the active panel is rendered, so pointing an inactive tab at
            // panel-${i} would reference an element that is not in the DOM — invalid
            // ARIA, and assistive tech follows the reference before checking it exists.
            aria-controls={activeIndex === i ? `${domId}panel-${i}` : undefined}
            tabIndex={activeIndex === i ? 0 : -1}
            ref={(el) => { tabRefs.current[i] = el; }}
            onClick={() => setSelectedIndex(i)}
            style={{
              padding: "8px 16px",
              border: "none",
              background: "none",
              borderBottom: activeIndex === i ? "2px solid var(--a2ui-primary-color, #007bff)" : "none",
              fontWeight: activeIndex === i ? "bold" : "normal",
              cursor: "pointer",
              color: activeIndex === i ? "var(--a2ui-primary-color, #007bff)" : "inherit",
            }}
          >
            {tab.title}
          </button>
        ))}
      </div>
      <div
        role="tabpanel"
        id={`${domId}panel-${activeIndex}`}
        aria-labelledby={`${domId}tab-${activeIndex}`}
        style={{ flex: 1 }}
      >
        {activeTab ? buildChild(activeTab.child) : null}
      </div>
    </div>
  );
});

// Clone the basic catalog, swapping the interactive components by name.
const overrides: Record<string, any> = {
  TextField: InteractiveTextField,
  CheckBox: InteractiveCheckBox,
  ChoicePicker: InteractiveChoicePicker,
  Button: InteractiveButton,
  Tabs: InteractiveTabs,
};
const components = [...(basicCatalog as any).components.values()].map(
  (c: any) => overrides[c.name] ?? c,
);
const functions = [...(basicCatalog as any).functions.values()];

export const interactiveCatalog = new Catalog(
  (basicCatalog as any).id,
  components as any,
  functions as any,
  (basicCatalog as any).themeSchema,
);
