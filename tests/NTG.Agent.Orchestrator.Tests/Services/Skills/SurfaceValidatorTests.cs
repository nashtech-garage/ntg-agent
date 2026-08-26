using NTG.Agent.Orchestrator.Services.Skills;

namespace NTG.Agent.Orchestrator.Tests.Services.Skills;

/// <summary>
/// Template tests for <see cref="SurfaceValidator"/>, grouped by the way a surface fails to render.
/// </summary>
/// <remarks>
/// Every case here is a surface that a JSON parser accepts and a renderer draws as a blank card, a
/// missing element, or a frozen input — there is no runtime error to fall back on, so the import
/// check is the only place the defect can surface. Each rejection asserts on the reported reason
/// rather than on "some error was returned": a test that only counts errors passes just as happily
/// when the validator rejects for the wrong reason, which is exactly how the broken render guide
/// survived for months.
/// </remarks>
[TestFixture]
public class SurfaceValidatorTests
{
    private const string SurfaceName = "assets/fixture.json";

    /// <summary>
    /// A valid Column root wrapping one component under test, so a fixture can isolate a single
    /// defect instead of tripping the root, reachability and required-property rules at once.
    /// </summary>
    private const string SurfaceTemplate = """
        {
          "surfaceId": "fixture",
          "components": [
            { "id": "root", "component": "Column", "children": ["subject"] },
            __SUBJECT__
          ],
          "data": __DATA__
        }
        """;

    private static readonly string SeedSurfaceDirectory = Path.Combine("seed", "skills", "travel-planning", "assets");

    private static IReadOnlyList<string> Validate(string json) => SurfaceValidator.Validate(SurfaceName, json);

    private static string SurfaceAround(string subjectJson, string dataJson = "{}") =>
        SurfaceTemplate
            .Replace("__SUBJECT__", subjectJson, StringComparison.Ordinal)
            .Replace("__DATA__", dataJson, StringComparison.Ordinal);

    /// <summary>Lets fixtures live in <c>[TestCase]</c> arguments without escaping every quote.</summary>
    private static string Json(string singleQuoted) => singleQuoted.Replace('\'', '"');

    private static string Describe(IReadOnlyList<string> errors) =>
        errors.Count == 0 ? "(no errors)" : string.Join(" | ", errors);

    private static void AssertOnlyError(IReadOnlyList<string> errors, string expectedFragment)
    {
        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Count.EqualTo(1), $"expected exactly one error, got: {Describe(errors)}");
            Assert.That(string.Join(" | ", errors), Does.Contain(expectedFragment));
        });
    }

    private static void AssertReports(IReadOnlyList<string> errors, string expectedFragment) =>
        Assert.That(
            errors.Any(e => e.Contains(expectedFragment, StringComparison.Ordinal)),
            Is.True,
            $"expected an error mentioning '{expectedFragment}', got: {Describe(errors)}");

    // ---------------------------------------------------------------- document structure

    [Test]
    public void Validate_MalformedJson_IsReportedReadably()
    {
        var errors = Validate("{ \"surfaceId\": \"fixture\", ");

        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Count.EqualTo(1), Describe(errors));
            Assert.That(errors[0], Does.StartWith($"{SurfaceName}: not valid JSON — "));
            Assert.That(errors[0], Does.Contain("LineNumber"), "the parser's position is what makes the error actionable");
        });
    }

    /// <summary>
    /// Depth is enforced by the parser, not by a check further down, so the whole surface is
    /// rejected as unparseable. The control case is the point: the same shape one notch under the
    /// limit parses fine, which proves the rejection came from the depth cap rather than from the
    /// fixture being malformed in some other way.
    /// </summary>
    [Test]
    public void Validate_JsonNestedBeyondMaxJsonDepth_IsRejectedByTheParser()
    {
        var tooDeep = Validate(NestedObject(SurfaceValidator.MaxJsonDepth + 4));
        var withinLimit = Validate(NestedObject(SurfaceValidator.MaxJsonDepth - 4));

        Assert.Multiple(() =>
        {
            Assert.That(tooDeep, Has.Count.EqualTo(1), Describe(tooDeep));
            Assert.That(string.Join(" | ", tooDeep), Does.Contain("not valid JSON"));
            Assert.That(
                withinLimit.Any(e => e.Contains("not valid JSON", StringComparison.Ordinal)),
                Is.False,
                $"the control fixture must parse: {Describe(withinLimit)}");
        });
    }

    private static string NestedObject(int depth) =>
        string.Concat(Enumerable.Repeat("{\"nested\":", depth)) + "1" + new string('}', depth);

    [TestCase("[]")]
    [TestCase("\"a surface\"")]
    [TestCase("null")]
    public void Validate_TopLevelNotAnObject_IsRejected(string json)
    {
        var errors = Validate(json);

        AssertOnlyError(errors, "top level must be a JSON object");
    }

    // ---------------------------------------------------------------- surface identity

    [Test]
    public void Validate_MissingSurfaceId_IsReported()
    {
        const string surface = """
            {
              "components": [
                { "id": "root", "component": "Column", "children": ["hello"] },
                { "id": "hello", "component": "Text", "text": "Hello" }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "missing or empty 'surfaceId'");
    }

    /// <summary>
    /// An empty or non-string id is worse than an absent one: it survives serialization and the
    /// surface is addressed by that id when the client patches it, so the update targets nothing.
    /// </summary>
    [TestCase("''")]
    [TestCase("'   '")]
    [TestCase("42")]
    [TestCase("null")]
    public void Validate_UnusableSurfaceId_IsReported(string surfaceIdJson)
    {
        var surface = Json($$"""
            {
              "surfaceId": {{surfaceIdJson}},
              "components": [
                { "id": "root", "component": "Column", "children": ["hello"] },
                { "id": "hello", "component": "Text", "text": "Hello" }
              ]
            }
            """);

        var errors = Validate(surface);

        AssertOnlyError(errors, "missing or empty 'surfaceId'");
    }

    // ---------------------------------------------------------------- the component list

    [TestCase("""{ "surfaceId": "fixture" }""")]
    [TestCase("""{ "surfaceId": "fixture", "components": {} }""")]
    [TestCase("""{ "surfaceId": "fixture", "components": "root" }""")]
    public void Validate_MissingComponentsArray_IsReported(string surface)
    {
        var errors = Validate(surface);

        AssertOnlyError(errors, "missing required 'components' array");
    }

    [Test]
    public void Validate_EmptyComponents_IsReported()
    {
        var errors = Validate("""{ "surfaceId": "fixture", "components": [] }""");

        AssertOnlyError(errors, "'components' is empty");
    }

    /// <summary>
    /// Duplicate ids are the surface-level version of the importer's duplicate-entry case: the
    /// renderer indexes by id, so the later definition is the one that never draws while the author
    /// is looking at it in the file.
    /// </summary>
    [Test]
    public void Validate_DuplicateComponentIds_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["twin"] },
                { "id": "twin", "component": "Text", "text": "first" },
                { "id": "twin", "component": "Text", "text": "second" }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "duplicate component id 'twin'");
    }

    /// <summary>Reported by array position, because an id-less component has no other handle.</summary>
    [Test]
    public void Validate_ComponentWithoutId_IsReportedByPosition()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["hello"] },
                { "id": "hello", "component": "Text", "text": "Hello" },
                { "component": "Text", "text": "nameless" }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "components[2] has no non-empty string 'id'");
    }

    [TestCase("Widget")]
    [TestCase("TextInput")]
    [TestCase("text")]
    public void Validate_UnknownComponentName_IsReported(string component)
    {
        var errors = Validate(SurfaceAround(Json($"{{ 'id': 'subject', 'component': '{component}' }}")));

        AssertOnlyError(errors, $"'subject' uses unknown component '{component}'");
    }

    [Test]
    public void Validate_ComponentWithoutComponentProperty_IsReported()
    {
        var errors = Validate(SurfaceAround(Json("{ 'id': 'subject', 'text': 'Hello' }")));

        AssertOnlyError(errors, "'subject' has no string 'component' property");
    }

    // ---------------------------------------------------------------- component properties

    [TestCase("{ 'id': 'subject', 'component': 'Text' }", "{}", "text")]
    [TestCase("{ 'id': 'subject', 'component': 'Slider', 'label': 'Travellers', 'value': 2 }", "{}", "max")]
    [TestCase("{ 'id': 'subject', 'component': 'Slider', 'label': 'Travellers', 'max': 8 }", "{}", "value")]
    [TestCase("{ 'id': 'subject', 'component': 'CheckBox', 'label': 'Bags' }", "{}", "value")]
    [TestCase("{ 'id': 'subject', 'component': 'Image', 'variant': 'header' }", "{}", "url")]
    public void Validate_MissingRequiredProperty_IsReported(string subject, string data, string property)
    {
        var errors = Validate(SurfaceAround(Json(subject), Json(data)));

        AssertOnlyError(errors, $"is missing required property '{property}'");
    }

    /// <summary>
    /// The regression anchor for this file. Every name below appeared in the A2UI render guide that
    /// shipped with the project and none of them exists in the v0.9 catalog: the renderer drops an
    /// unknown property silently, so a <c>TextField</c> written with <c>text</c> instead of
    /// <c>value</c> rendered as an empty box, and a <c>Slider</c> written with
    /// <c>minValue</c>/<c>maxValue</c> rendered with the catalog defaults. Nothing logged, nothing
    /// threw, and the guide was copied into new surfaces for months. If any of these ever validates
    /// clean again, the same silent failure is back.
    /// </summary>
    [TestCase("{ 'id': 'subject', 'component': 'TextField', 'label': 'Where to?', 'text': 'Paris' }", "text")]
    [TestCase("{ 'id': 'subject', 'component': 'CheckBox', 'label': 'Bags', 'value': false, 'checked': true }", "checked")]
    [TestCase("{ 'id': 'subject', 'component': 'ChoicePicker', 'options': [], 'value': [], 'selections': [] }", "selections")]
    [TestCase("{ 'id': 'subject', 'component': 'ChoicePicker', 'options': [], 'value': [], 'maxAllowedSelections': 2 }", "maxAllowedSelections")]
    [TestCase("{ 'id': 'subject', 'component': 'Slider', 'max': 8, 'value': 2, 'minValue': 1 }", "minValue")]
    [TestCase("{ 'id': 'subject', 'component': 'Slider', 'max': 8, 'value': 2, 'maxValue': 8 }", "maxValue")]
    public void Validate_PropertyFromTheOldRenderGuide_IsReportedAsUnknown(string subject, string property)
    {
        var errors = Validate(SurfaceAround(Json(subject)));

        AssertOnlyError(errors, $"has unknown property '{property}'");
    }

    [Test]
    public void Validate_UnknownPropertyOnALayoutComponent_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["hello"], "gap": 8 },
                { "id": "hello", "component": "Text", "text": "Hello" }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "has unknown property 'gap'");
    }

    /// <summary>
    /// <c>secondary</c> and <c>text</c> are the Material-flavoured variant names an author reaches
    /// for by reflex; the v0.9 catalog offers default|primary|borderless, and an out-of-set variant
    /// falls back to the default style rather than failing, so the button simply looks wrong.
    /// </summary>
    [TestCase("secondary")]
    [TestCase("text")]
    [TestCase("Primary")]
    public void Validate_ValueOutsideAnEnumeratedSet_IsReported(string variant)
    {
        var surface = Json($$"""
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["subject"] },
                {
                  "id": "subject",
                  "component": "Button",
                  "variant": "{{variant}}",
                  "child": "label",
                  "action": { "event": { "name": "go" } }
                },
                { "id": "label", "component": "Text", "text": "Go" }
              ],
              "data": {}
            }
            """);

        var errors = Validate(surface);

        AssertOnlyError(errors, $"has variant='{variant}', expected one of");
    }

    /// <summary>
    /// An enumerated property may also carry a binding, which resolves to a member of the set at
    /// runtime. Checking the literal set against an object would reject every dynamically styled
    /// component, so the check applies only when a literal string was written.
    /// </summary>
    [Test]
    public void Validate_EnumeratedPropertyBoundToData_IsAccepted()
    {
        var errors = Validate(
            SurfaceAround(
                Json("{ 'id': 'subject', 'component': 'Text', 'text': 'Hello', 'variant': { 'path': '/style/heading' } }"),
                Json("{ 'style': { 'heading': 'h4' } }")));

        Assert.That(errors, Is.Empty, Describe(errors));
    }

    // ---------------------------------------------------------------- child references

    [Test]
    public void Validate_ChildReferenceThatDoesNotResolve_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["ghost"] }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "'root' references child 'ghost', which does not exist");
    }

    /// <summary>
    /// Reported as its own case rather than as an unresolved reference: a self-reference resolves
    /// perfectly well, and would recurse until the renderer runs out of stack.
    /// </summary>
    [Test]
    public void Validate_ComponentReferencingItself_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["root"] }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "'root' references itself as a child");
    }

    [Test]
    public void Validate_OrphanComponent_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["hello"] },
                { "id": "hello", "component": "Text", "text": "Hello" },
                { "id": "forgotten", "component": "Text", "text": "Never drawn" }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "'forgotten' is never referenced and will not render");
    }

    [Test]
    public void Validate_NoComponentNamedRoot_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "main", "component": "Column", "children": ["hello"] },
                { "id": "hello", "component": "Text", "text": "Hello" }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertReports(errors, "no component with id 'root'");
    }

    /// <summary>
    /// A leaf root renders exactly one element and silently discards the rest of the surface, which
    /// looks like "only the title showed up" rather than like a broken template.
    /// </summary>
    [TestCase("Text", "{ 'id': 'root', 'component': 'Text', 'text': 'Hello' }")]
    [TestCase("Divider", "{ 'id': 'root', 'component': 'Divider' }")]
    [TestCase("Modal", "{ 'id': 'root', 'component': 'Modal', 'trigger': 'root-a', 'content': 'root-b' }")]
    public void Validate_NonLayoutRoot_IsRejected(string component, string rootJson)
    {
        var surface = Json($$"""
            {
              "surfaceId": "fixture",
              "components": [
                {{rootJson}},
                { "id": "root-a", "component": "Text", "text": "a" },
                { "id": "root-b", "component": "Text", "text": "b" }
              ]
            }
            """);

        var errors = Validate(surface);

        AssertReports(errors, $"root is '{component}', which cannot lay out children");
    }

    [TestCase("Column", "children", "[\"hello\"]")]
    [TestCase("Row", "children", "[\"hello\"]")]
    [TestCase("List", "children", "[\"hello\"]")]
    [TestCase("Card", "child", "\"hello\"")]
    public void Validate_LayoutRoot_IsAccepted(string component, string childProperty, string childJson)
    {
        var surface = $$"""
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "{{component}}", "{{childProperty}}": {{childJson}} },
                { "id": "hello", "component": "Text", "text": "Hello" }
              ]
            }
            """;

        var errors = Validate(surface);

        Assert.That(errors, Is.Empty, Describe(errors));
    }

    /// <summary>
    /// The <c>ChildList</c> template form generates children from a data list, and it is the one
    /// place where a <c>path</c> key is not a data binding. Reading it as one would demand a seed
    /// for a list that only exists once the agent fills it, so every templated surface would be
    /// rejected at import; reading it as a child reference is what makes the template resolve.
    /// </summary>
    [Test]
    public void Validate_ChildListTemplate_ResolvesTheTemplateAndIsNotReadAsABinding()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "List", "children": { "componentId": "row", "path": "/results/items" } },
                { "id": "row", "component": "Text", "text": "An option" }
              ]
            }
            """;

        var errors = Validate(surface);

        Assert.That(errors, Is.Empty, Describe(errors));
    }

    [Test]
    public void Validate_ChildListTemplateNamingAMissingComponent_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "List", "children": { "componentId": "row", "path": "/results/items" } }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "'root' references child 'row', which does not exist");
    }

    [TestCase("42")]
    [TestCase("true")]
    [TestCase("{ 'path': '/results/items' }")]
    public void Validate_ChildReferenceOfAnUnusableShape_IsReported(string childrenJson)
    {
        var surface = Json($$"""
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": {{childrenJson}} }
              ],
              "data": { "results": { "items": [] } }
            }
            """);

        var errors = Validate(surface);

        AssertReports(errors, "neither an id, a list of ids, nor a template");
    }

    [Test]
    public void Validate_ChildListContainingANonString_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["hello", { "id": "inline" }] },
                { "id": "hello", "component": "Text", "text": "Hello" }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "lists a child that is not a component id string");
    }

    /// <summary>
    /// Regression: <c>Tabs.tabs</c> is an array of <c>{ title, child }</c> objects, so its child
    /// reference sits one level below every other component's. While the traversal only looked at
    /// top-level reference properties, this surface was rejected — both panes were reported as
    /// orphans that "will not render" — making Tabs unusable in a skill package.
    /// </summary>
    [Test]
    public void Validate_TabsResolvingItsPanes_IsAccepted()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["tabset"] },
                {
                  "id": "tabset",
                  "component": "Tabs",
                  "tabs": [
                    { "title": "First", "child": "pane-a" },
                    { "title": "Second", "child": "pane-b" }
                  ]
                },
                { "id": "pane-a", "component": "Text", "text": "First pane" },
                { "id": "pane-b", "component": "Text", "text": "Second pane" }
              ]
            }
            """;

        Assert.That(Validate(surface), Is.Empty);
    }

    /// <summary>
    /// The other half of the same regression: a typo'd pane id inside <c>tabs</c> produced no error
    /// at all, so the surface imported clean and rendered an empty tab.
    /// </summary>
    [Test]
    public void Validate_TabsReferencingAMissingPane_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["tabset"] },
                {
                  "id": "tabset",
                  "component": "Tabs",
                  "tabs": [{ "title": "First", "child": "typo" }]
                },
                { "id": "pane-a", "component": "Text", "text": "First pane" }
              ]
            }
            """;

        var errors = Validate(surface);

        Assert.Multiple(() =>
        {
            Assert.That(
                errors.Any(e => e.Contains("references child 'typo', which does not exist", StringComparison.Ordinal)),
                Is.True,
                string.Join(" | ", errors));
            Assert.That(
                errors.Any(e => e.Contains("'pane-a' is never referenced", StringComparison.Ordinal)),
                Is.True,
                "the unreachable pane is still an orphan");
        });
    }

    // ---------------------------------------------------------------- data bindings

    /// <summary>
    /// The subtle one. A2UI inputs are controlled components whose setter writes back to the bound
    /// path, and the write is a no-op while the path is absent from the data model — so the field
    /// renders, accepts focus, and refuses to change. Nothing about it looks like a template bug.
    /// </summary>
    [Test]
    public void Validate_BindingWithNoSeedInData_IsReported()
    {
        var errors = Validate(
            SurfaceAround(Json("{ 'id': 'subject', 'component': 'TextField', 'label': 'Where to?', 'value': { 'path': '/trip/destination' } }")));

        AssertOnlyError(errors, "'subject' binds '/trip/destination', which has no seed in 'data'");
    }

    [Test]
    public void Validate_SeededBindings_AreAccepted()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["title", "destination", "travellers"] },
                { "id": "title", "component": "Text", "text": { "path": "/title" } },
                { "id": "destination", "component": "TextField", "label": "Where to?", "value": { "path": "/trip/destination" } },
                { "id": "travellers", "component": "Slider", "label": "Travellers", "min": 1, "max": 8, "value": { "path": "/trip/party/adults" } }
              ],
              "data": {
                "title": "Plan your trip",
                "trip": { "destination": "", "party": { "adults": 2 } }
              }
            }
            """;

        var errors = Validate(surface);

        Assert.That(errors, Is.Empty, Describe(errors));
    }

    /// <summary>
    /// A seed of <c>""</c>, <c>0</c>, <c>false</c>, <c>[]</c> or <c>null</c> still creates the path,
    /// which is all the data model needs — treating falsy seeds as missing would push authors into
    /// inventing placeholder content that then renders.
    /// </summary>
    [TestCase("''")]
    [TestCase("0")]
    [TestCase("false")]
    [TestCase("[]")]
    [TestCase("null")]
    public void Validate_FalsySeed_StillCountsAsSeeded(string seed)
    {
        var errors = Validate(
            SurfaceAround(
                Json("{ 'id': 'subject', 'component': 'TextField', 'label': 'Where to?', 'value': { 'path': '/trip/destination' } }"),
                Json($"{{ 'trip': {{ 'destination': {seed} }} }}")));

        Assert.That(errors, Is.Empty, Describe(errors));
    }

    /// <summary>
    /// Bindings are found by walking the whole component, not by inspecting known properties: the
    /// ones inside <c>action.event.context</c> are the ones that decide what the agent receives
    /// when the button is pressed, and an unseeded one arrives as null.
    /// </summary>
    [Test]
    public void Validate_UnseededBindingNestedInsideAnAction_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["subject"] },
                {
                  "id": "subject",
                  "component": "Button",
                  "child": "label",
                  "action": {
                    "event": {
                      "name": "trip_option_selected",
                      "context": { "choice": { "path": "/results/choice" } }
                    }
                  }
                },
                { "id": "label", "component": "Text", "text": "Continue" }
              ],
              "data": {}
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "'subject' binds '/results/choice', which has no seed in 'data'");
    }

    [Test]
    public void Validate_UnseededBindingInsideAChoicePickerOption_IsReported()
    {
        var errors = Validate(
            SurfaceAround(
                Json("""
                    {
                      'id': 'subject',
                      'component': 'ChoicePicker',
                      'label': 'Which one?',
                      'value': { 'path': '/results/choice' },
                      'options': [ { 'label': { 'path': '/results/o1/name' }, 'value': 'o1' } ]
                    }
                    """),
                Json("{ 'results': { 'choice': [] } }")));

        AssertOnlyError(errors, "'subject' binds '/results/o1/name', which has no seed in 'data'");
    }

    [Test]
    public void Validate_BindingWhenTheSurfaceHasNoDataObjectAtAll_IsReported()
    {
        const string surface = """
            {
              "surfaceId": "fixture",
              "components": [
                { "id": "root", "component": "Column", "children": ["subject"] },
                { "id": "subject", "component": "TextField", "label": "Where to?", "value": { "path": "/trip/destination" } }
              ]
            }
            """;

        var errors = Validate(surface);

        AssertOnlyError(errors, "binds '/trip/destination', which has no seed in 'data'");
    }

    // ---------------------------------------------------------------- error accumulation

    /// <summary>
    /// Reporting only the first defect turns a broken template into one upload round-trip per
    /// error, which is how a skill author ends up disabling the check instead of fixing the file.
    /// Three unrelated defects — an unusable id, a missing required property and an orphan — must
    /// come back as three lines.
    /// </summary>
    [Test]
    public void Validate_SurfaceWithSeveralDefects_ReportsAllOfThem()
    {
        const string surface = """
            {
              "surfaceId": "",
              "components": [
                { "id": "root", "component": "Column", "children": ["hello"] },
                { "id": "hello", "component": "Text" },
                { "id": "forgotten", "component": "Text", "text": "Never drawn" }
              ],
              "data": {}
            }
            """;

        var errors = Validate(surface);

        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Count.EqualTo(3), Describe(errors));
            AssertReports(errors, "missing or empty 'surfaceId'");
            AssertReports(errors, "'hello' (Text) is missing required property 'text'");
            AssertReports(errors, "'forgotten' is never referenced and will not render");
        });
    }

    [Test]
    public void Validate_EveryErrorIsPrefixedWithTheSurfaceName()
    {
        var errors = Validate("""{ "surfaceId": "", "components": [ { "id": "x", "component": "Nope" } ] }""");

        Assert.Multiple(() =>
        {
            Assert.That(errors, Is.Not.Empty);
            Assert.That(errors.All(e => e.StartsWith($"{SurfaceName}: ", StringComparison.Ordinal)), Is.True, Describe(errors));
        });
    }

    // ---------------------------------------------------------------- happy path

    [Test]
    public void Validate_MinimalValidSurface_HasNoErrors()
    {
        const string surface = """
            {
              "surfaceId": "minimal",
              "components": [
                { "id": "root", "component": "Column", "children": ["hello"] },
                { "id": "hello", "component": "Text", "text": "Hello" }
              ]
            }
            """;

        var errors = Validate(surface);

        Assert.That(errors, Is.Empty, Describe(errors));
    }

    /// <summary>
    /// The surfaces actually shipped in <c>seed/skills/</c>. Synthetic fixtures can only prove that
    /// the rules fire; these prove the rules do not fire on the templates the demo renders, which is
    /// the half that a stricter validator quietly breaks.
    /// </summary>
    [TestCase("trip-planner.json")]
            public void Validate_SeededTravelPlanningSurface_IsClean(string fileName)
    {
        var path = FindRepositoryFile(Path.Combine(SeedSurfaceDirectory, fileName));
        if (path is null)
        {
            Assert.Ignore($"seed/skills/travel-planning/assets/{fileName} not found from the test output directory");
            return;
        }

        var errors = SurfaceValidator.Validate(fileName, File.ReadAllText(path));

        Assert.That(errors, Is.Empty, Describe(errors));
    }

    private static string? FindRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
