using System.Collections.Frozen;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// The A2UI v0.9 basic catalog, as the component names, properties, required properties and
/// enumerated values that <see cref="SurfaceValidator"/> checks surface templates against.
/// </summary>
/// <remarks>
/// <para>
/// This is a snapshot of <c>@a2ui/web_core/src/v0_9/schemas/basic_catalog.json</c>, flattened:
/// the schema composes each component from <c>ComponentCommon</c> (<c>id</c>,
/// <c>accessibility</c>), <c>CatalogComponentCommon</c> (<c>weight</c>) and, for inputs,
/// <c>Checkable</c> (<c>checks</c>) via <c>allOf</c>, so those properties are folded into each
/// entry below rather than being re-derived at runtime.
/// </para>
/// <para>
/// It is a snapshot rather than a parse of the JSON because the schema lives in the frontend's
/// <c>node_modules</c>, which the Orchestrator has no access to. <c>A2uiCatalogDriftTests</c>
/// re-derives this table from the schema when that file is present and fails on divergence, so
/// bumping the npm package surfaces here rather than as a mystery import rejection.
/// </para>
/// <para>
/// Regenerate the table body with the generator documented in
/// <c>docs/Agent-Skills-Implementation-Plan.md</c>.
/// </para>
/// </remarks>
public static class A2uiCatalog
{
    public sealed record ComponentSpec(
        FrozenSet<string> Properties,
        FrozenSet<string> RequiredProperties,
        FrozenDictionary<string, FrozenSet<string>> EnumeratedValues);

    /// <summary>Components valid as a surface root. A root must lay out children, not be a leaf.</summary>
    public static readonly FrozenSet<string> LayoutComponents =
        new[] { "Column", "Row", "Card", "List" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Props that reference other components by id, and so must resolve.
    /// </summary>
    /// <remarks>
    /// <c>tabs</c> is the odd one out: it is an array of <c>{ title, child }</c> objects rather than
    /// ids or an id list, so the reference sits one level down. It is listed here because omitting
    /// it does not fail loudly — unreferenced panes are reported as orphans while a genuinely
    /// dangling <c>child</c> goes unreported, which is wrong in both directions.
    /// </remarks>
    public static readonly FrozenSet<string> ChildReferenceProperties =
        new[] { "child", "children", "content", "trigger", "tabs" }.ToFrozenSet(StringComparer.Ordinal);

    public static readonly FrozenDictionary<string, ComponentSpec> Components = BuildCatalog();

    public static bool TryGet(string component, out ComponentSpec spec) =>
        Components.TryGetValue(component, out spec!);

    private static ComponentSpec Spec(string[] properties, string[] required, params (string Property, string[] Values)[] enums) =>
        new(properties.ToFrozenSet(StringComparer.Ordinal),
            required.ToFrozenSet(StringComparer.Ordinal),
            enums.ToFrozenDictionary(
                e => e.Property,
                e => e.Values.ToFrozenSet(StringComparer.Ordinal),
                StringComparer.Ordinal));

    private static FrozenDictionary<string, ComponentSpec> BuildCatalog() =>
        new Dictionary<string, ComponentSpec>(StringComparer.Ordinal)
        {
            ["Text"] = Spec(
                ["accessibility", "id", "text", "variant", "weight"], ["text"],
                ("variant", ["h1", "h2", "h3", "h4", "h5", "caption", "body"])),

            ["Image"] = Spec(
                ["accessibility", "description", "fit", "id", "url", "variant", "weight"], ["url"],
                ("fit", ["contain", "cover", "fill", "none", "scaleDown"]),
                ("variant", ["icon", "avatar", "smallFeature", "mediumFeature", "largeFeature", "header"])),

            ["Icon"] = Spec(["accessibility", "id", "name", "weight"], ["name"]),

            ["Video"] = Spec(["accessibility", "id", "url", "weight"], ["url"]),

            ["AudioPlayer"] = Spec(["accessibility", "description", "id", "url", "weight"], ["url"]),

            ["Row"] = Spec(
                ["accessibility", "align", "children", "id", "justify", "weight"], ["children"],
                ("align", ["start", "center", "end", "stretch"]),
                ("justify", ["center", "end", "spaceAround", "spaceBetween", "spaceEvenly", "start", "stretch"])),

            ["Column"] = Spec(
                ["accessibility", "align", "children", "id", "justify", "weight"], ["children"],
                ("align", ["center", "end", "start", "stretch"]),
                ("justify", ["start", "center", "end", "spaceBetween", "spaceAround", "spaceEvenly", "stretch"])),

            ["List"] = Spec(
                ["accessibility", "align", "children", "direction", "id", "weight"], ["children"],
                ("align", ["start", "center", "end", "stretch"]),
                ("direction", ["vertical", "horizontal"])),

            ["Card"] = Spec(["accessibility", "child", "id", "weight"], ["child"]),

            ["Tabs"] = Spec(["accessibility", "id", "tabs", "weight"], ["tabs"]),

            ["Modal"] = Spec(["accessibility", "content", "id", "trigger", "weight"], ["content", "trigger"]),

            ["Divider"] = Spec(
                ["accessibility", "axis", "id", "weight"], [],
                ("axis", ["horizontal", "vertical"])),

            ["Button"] = Spec(
                ["accessibility", "action", "checks", "child", "id", "variant", "weight"], ["action", "child"],
                ("variant", ["default", "primary", "borderless"])),

            ["TextField"] = Spec(
                ["accessibility", "checks", "id", "label", "validationRegexp", "value", "variant", "weight"], ["label"],
                ("variant", ["longText", "number", "shortText", "obscured"])),

            ["CheckBox"] = Spec(["accessibility", "checks", "id", "label", "value", "weight"], ["label", "value"]),

            ["ChoicePicker"] = Spec(
                ["accessibility", "checks", "displayStyle", "filterable", "id", "label", "options", "value", "variant", "weight"],
                ["options", "value"],
                ("displayStyle", ["checkbox", "chips"]),
                ("variant", ["multipleSelection", "mutuallyExclusive"])),

            ["Slider"] = Spec(["accessibility", "checks", "id", "label", "max", "min", "value", "weight"], ["max", "value"]),

            ["DateTimeInput"] = Spec(
                ["accessibility", "checks", "enableDate", "enableTime", "id", "label", "max", "min", "value", "weight"],
                ["value"]),
        }.ToFrozenDictionary(StringComparer.Ordinal);
}
