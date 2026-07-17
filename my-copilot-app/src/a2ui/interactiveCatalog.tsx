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
// Visual styling comes from the scoped `.a2ui-surface` rules in app/globals.css.
import React from "react";
import { Catalog } from "@a2ui/web_core/v0_9";
import {
  TextFieldApi,
  CheckBoxApi,
  ChoicePickerApi,
  ButtonApi,
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

    try {
      context.dispatchAction({
        event: { name: actionDef.name ?? "submit", context: { ...resolved, formData } },
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

// Clone the basic catalog, swapping the interactive components by name.
const overrides: Record<string, any> = {
  TextField: InteractiveTextField,
  CheckBox: InteractiveCheckBox,
  ChoicePicker: InteractiveChoicePicker,
  Button: InteractiveButton,
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
