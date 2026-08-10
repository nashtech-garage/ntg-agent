using System.Text.Json;
using NTG.Agent.Orchestrator.Services.Skills;

namespace NTG.Agent.Orchestrator.Tests.Services.Skills;

/// <summary>
/// Re-derives the A2UI basic catalog from the real schema and fails when
/// <see cref="A2uiCatalog"/> — a hand-maintained snapshot of it — has drifted.
/// </summary>
/// <remarks>
/// <para>
/// <c>A2uiCatalog</c> is a snapshot because the schema ships inside the frontend's
/// <c>node_modules</c>, which the Orchestrator cannot reference. The cost of a snapshot is that
/// bumping <c>@a2ui/web_core</c> changes what the renderer accepts while the importer keeps
/// checking against the old table: a surface using a newly added property is rejected at upload
/// with "unknown property", and a surface using a removed one is accepted and then renders
/// nothing. Both are mystery bugs at a distance from their cause. This fixture turns them into a
/// failing test on the commit that bumps the package.
/// </para>
/// <para>
/// The derivation mirrors the flattening the snapshot documents, and the two deliberate
/// subtractions are called out at <see cref="Flatten"/>. When <c>node_modules</c> is absent the
/// fixture ignores rather than fails — CI without an <c>npm install</c> must not turn red for a
/// check it cannot perform.
/// </para>
/// </remarks>
[TestFixture]
public class A2uiCatalogDriftTests
{
    private const string CatalogFileName = "basic_catalog.json";
    private const string CommonTypesFileName = "common_types.json";

    /// <summary>Held as a field rather than inlined so the path is built once, on any platform.</summary>
    private static readonly string[] SchemaDirectorySegments =
        ["my-copilot-app", "node_modules", "@a2ui", "web_core", "src", "v0_9", "schemas"];

    private static readonly string SchemaDirectory = Path.Combine(SchemaDirectorySegments);

    /// <summary>The two <c>$ref</c> targets that make a property a reference to another component.</summary>
    private static readonly string[] ReferenceTypeNames = ["ChildList", "ComponentId"];

    private static readonly Lazy<DerivedCatalog?> Schema = new(Derive);

    private sealed record DerivedComponent(
        SortedSet<string> Properties,
        SortedSet<string> Required,
        SortedDictionary<string, List<string>> EnumeratedValues,
        SortedDictionary<string, string> PropertyRefs);

    private sealed record DerivedCatalog(
        string Path,
        SortedDictionary<string, DerivedComponent> Components);

    // ---------------------------------------------------------------- drift checks

    [Test]
    public void Catalog_ListsExactlyTheComponentsTheSchemaDefines()
    {
        var derived = RequireSchema();

        Assert.That(
            A2uiCatalog.Components.Keys.Order(StringComparer.Ordinal),
            Is.EqualTo(derived.Components.Keys),
            $"component set drifted from {derived.Path}");
    }

    /// <summary>
    /// One case per component so a bump reports which component moved, not just that the table is
    /// wrong. Properties, required properties and enumerated values are each checked: a dropped
    /// property rejects valid surfaces, a dropped required property accepts surfaces that render
    /// blank, and a stale enum member is the failure the Button variants already caused once.
    /// </summary>
    [TestCaseSource(nameof(SnapshotComponentNames))]
    public void Catalog_ComponentMatchesTheSchema(string component)
    {
        var derived = RequireSchema();

        if (!derived.Components.TryGetValue(component, out var expected))
        {
            Assert.Fail($"'{component}' is in the snapshot but the schema no longer defines it");
            return;
        }

        var actual = A2uiCatalog.Components[component];

        Assert.Multiple(() =>
        {
            Assert.That(
                actual.Properties.Order(StringComparer.Ordinal),
                Is.EqualTo(expected.Properties),
                $"{component}: property set drifted");

            Assert.That(
                actual.RequiredProperties.Order(StringComparer.Ordinal),
                Is.EqualTo(expected.Required),
                $"{component}: required properties drifted");

            Assert.That(
                actual.EnumeratedValues.Keys.Order(StringComparer.Ordinal),
                Is.EqualTo(expected.EnumeratedValues.Keys),
                $"{component}: the set of enumerated properties drifted");

            foreach (var (property, values) in expected.EnumeratedValues)
            {
                var snapshotValues = actual.EnumeratedValues.TryGetValue(property, out var set)
                    ? set.Order(StringComparer.Ordinal)
                    : Enumerable.Empty<string>();

                Assert.That(
                    snapshotValues,
                    Is.EqualTo(values.Order(StringComparer.Ordinal)),
                    $"{component}.{property}: enumerated values drifted");
            }
        });
    }

    [Test]
    public void LayoutComponents_AllExistAndCanHoldChildren()
    {
        var derived = RequireSchema();

        Assert.Multiple(() =>
        {
            foreach (var layout in A2uiCatalog.LayoutComponents.Order(StringComparer.Ordinal))
            {
                if (!derived.Components.TryGetValue(layout, out var component))
                {
                    Assert.Fail($"'{layout}' is listed as a layout component but the schema does not define it");
                    continue;
                }

                Assert.That(
                    component.PropertyRefs.Values.Any(IsComponentReference),
                    Is.True,
                    $"'{layout}' is allowed as a surface root but takes no child reference");
            }
        });
    }

    /// <summary>
    /// Keeps <see cref="A2uiCatalog.ChildReferenceProperties"/> in step with the schema in both
    /// directions. A reference-typed property missing from that set is the quiet failure: the
    /// validator stops resolving it, so a typo'd id passes import and the child renders as nothing,
    /// and every component reachable only through it is reported as an orphan instead.
    /// </summary>
    /// <remarks>
    /// The scan reaches one level into array items, which is what <c>Tabs.tabs[].child</c> needs.
    /// An earlier top-level-only scan reported <c>tabs</c> as carrying no reference, and the
    /// validator matched it — so a Tabs surface had its panes reported as orphans while a genuinely
    /// dangling <c>child</c> produced no error at all.
    /// </remarks>
    [Test]
    public void ChildReferenceProperties_CoverEveryReferenceTypedPropertyInTheSchema()
    {
        var derived = RequireSchema();

        var fromSchema = derived.Components
            .SelectMany(entry => entry.Value.PropertyRefs
                .Where(property => IsComponentReference(property.Value))
                .Select(property => (Component: entry.Key, property.Key)))
            .ToList();

        Assert.Multiple(() =>
        {
            foreach (var (component, property) in fromSchema)
            {
                Assert.That(
                    A2uiCatalog.ChildReferenceProperties.Contains(property),
                    Is.True,
                    $"{component}.{property} references another component but is not resolved as a child reference");
            }

            foreach (var property in A2uiCatalog.ChildReferenceProperties.Order(StringComparer.Ordinal))
            {
                Assert.That(
                    fromSchema.Any(entry => entry.Key == property),
                    Is.True,
                    $"'{property}' is resolved as a child reference but no component in the schema declares it");
            }
        });
    }

    // ---------------------------------------------------------------- snapshot invariants

    /// <summary>
    /// Runs with or without the schema. A required property that is not also a declared property
    /// makes every use of the component fail twice — once as missing, once as unknown — which reads
    /// as a broken surface rather than as a typo in this table.
    /// </summary>
    [Test]
    public void Catalog_EveryRequiredPropertyIsAlsoADeclaredProperty()
    {
        Assert.Multiple(() =>
        {
            foreach (var (component, spec) in A2uiCatalog.Components)
            {
                foreach (var required in spec.RequiredProperties.Order(StringComparer.Ordinal))
                {
                    Assert.That(
                        spec.Properties.Contains(required),
                        Is.True,
                        $"{component}.{required} is required but is not a declared property");
                }
            }
        });
    }

    [Test]
    public void Catalog_EveryEnumeratedPropertyIsAlsoADeclaredProperty()
    {
        Assert.Multiple(() =>
        {
            foreach (var (component, spec) in A2uiCatalog.Components)
            {
                foreach (var (property, values) in spec.EnumeratedValues)
                {
                    Assert.That(
                        spec.Properties.Contains(property),
                        Is.True,
                        $"{component}.{property} has enumerated values but is not a declared property");
                    Assert.That(values, Is.Not.Empty, $"{component}.{property} has an empty value set");
                }
            }
        });
    }

    // ---------------------------------------------------------------- derivation

    private static IEnumerable<string> SnapshotComponentNames() =>
        A2uiCatalog.Components.Keys.Order(StringComparer.Ordinal);

    private static bool IsComponentReference(string reference) =>
        ReferenceTypeNames.Any(name => reference.EndsWith($"/{name}", StringComparison.Ordinal));

    /// <summary>
    /// The <c>$ref</c> of an array property whose items carry a component reference in one of their
    /// own properties, or <see langword="null"/>. Covers the <c>Tabs.tabs</c> shape.
    /// </summary>
    private static string? NestedComponentReference(JsonElement property)
    {
        if (!property.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Object
            || !items.TryGetProperty("properties", out var itemProperties)
            || itemProperties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var itemProperty in itemProperties.EnumerateObject())
        {
            if (itemProperty.Value.ValueKind == JsonValueKind.Object
                && itemProperty.Value.TryGetProperty("$ref", out var reference)
                && reference.ValueKind == JsonValueKind.String
                && IsComponentReference(reference.GetString()!))
            {
                return reference.GetString()!;
            }
        }

        return null;
    }

    private static DerivedCatalog RequireSchema()
    {
        var derived = Schema.Value;
        if (derived is null)
        {
            Assert.Ignore(
                $"{Path.Combine(SchemaDirectory, CatalogFileName)} not found from the test output directory — " +
                "run 'npm install' in my-copilot-app to enable the catalog drift check");
        }

        return derived!;
    }

    private static DerivedCatalog? Derive()
    {
        var catalogPath = FindRepositoryFile(Path.Combine(SchemaDirectory, CatalogFileName));
        if (catalogPath is null)
        {
            return null;
        }

        var commonTypesPath = Path.Combine(Path.GetDirectoryName(catalogPath)!, CommonTypesFileName);
        if (!File.Exists(commonTypesPath))
        {
            return null;
        }

        using var catalog = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
        using var commonTypes = JsonDocument.Parse(File.ReadAllBytes(commonTypesPath));

        var components = new SortedDictionary<string, DerivedComponent>(StringComparer.Ordinal);
        foreach (var component in catalog.RootElement.GetProperty("components").EnumerateObject())
        {
            components.Add(
                component.Name,
                Flatten(component.Value, catalog.RootElement, commonTypes.RootElement));
        }

        return new DerivedCatalog(catalogPath, components);
    }

    /// <summary>
    /// Collapses one component's <c>allOf</c> composition into a single set of properties, required
    /// properties, enumerated values and <c>$ref</c> targets.
    /// </summary>
    /// <remarks>
    /// <para>Two subtractions make the result comparable to the snapshot:</para>
    /// <para>
    /// <c>component</c> — the schema declares it as a <c>const</c> discriminator and requires it on
    /// every component. <c>SurfaceValidator</c> reads it before looking a spec up and skips it when
    /// checking properties, so the table never lists it.
    /// </para>
    /// <para>
    /// <c>id</c> — required by <c>ComponentCommon</c>, but the validator indexes components by id
    /// before any per-component check runs, and reports a missing one against the array position
    /// rather than against a component that cannot be named. It stays a permitted property; it is
    /// only dropped from the required set.
    /// </para>
    /// <para>
    /// Enumerated values are taken only from properties that declare <c>type: string</c> alongside
    /// <c>enum</c>, which is what the snapshot flattened. <c>Icon.name</c> is the one property that
    /// hides its enum inside a <c>oneOf</c> (a name or a binding), and it carries no value set in
    /// the snapshot either.
    /// </para>
    /// </remarks>
    private static DerivedComponent Flatten(JsonElement schema, JsonElement catalog, JsonElement commonTypes)
    {
        var properties = new SortedSet<string>(StringComparer.Ordinal);
        var required = new SortedSet<string>(StringComparer.Ordinal);
        var enumeratedValues = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var propertyRefs = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var part in Compose(schema, catalog, commonTypes))
        {
            if (part.TryGetProperty("properties", out var declared) && declared.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in declared.EnumerateObject())
                {
                    properties.Add(property.Name);

                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (property.Value.TryGetProperty("enum", out var values)
                        && property.Value.TryGetProperty("type", out var type)
                        && type.ValueEquals("string"))
                    {
                        enumeratedValues[property.Name] = [.. values.EnumerateArray().Select(value => value.GetString()!)];
                    }

                    if (property.Value.TryGetProperty("$ref", out var reference)
                        && reference.ValueKind == JsonValueKind.String)
                    {
                        propertyRefs[property.Name] = reference.GetString()!;
                        continue;
                    }

                    // Tabs.tabs is an array of { title, child } objects, so its reference sits one
                    // level down. Scanning only top-level $refs would report `tabs` as carrying no
                    // reference and leave the validator's traversal of it unchecked.
                    if (NestedComponentReference(property.Value) is { } nested)
                    {
                        propertyRefs[property.Name] = nested;
                    }
                }
            }

            if (part.TryGetProperty("required", out var requiredNames) && requiredNames.ValueKind == JsonValueKind.Array)
            {
                foreach (var name in requiredNames.EnumerateArray())
                {
                    required.Add(name.GetString()!);
                }
            }
        }

        properties.Remove("component");
        required.Remove("component");
        required.Remove("id");
        propertyRefs.Remove("id");

        return new DerivedComponent(properties, required, enumeratedValues, propertyRefs);
    }

    /// <summary>
    /// Yields a schema and every subschema its <c>allOf</c> composes it from, resolving
    /// <c>$ref</c>s. Skipping this step is what would produce false failures: <c>id</c> and
    /// <c>accessibility</c> come from <c>ComponentCommon</c>, <c>weight</c> from
    /// <c>CatalogComponentCommon</c>, and <c>checks</c> from <c>Checkable</c> — none of them appear
    /// on the component's own schema.
    /// </summary>
    private static IEnumerable<JsonElement> Compose(JsonElement schema, JsonElement catalog, JsonElement commonTypes)
    {
        yield return schema;

        if (!schema.TryGetProperty("allOf", out var allOf) || allOf.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var part in allOf.EnumerateArray())
        {
            var resolved = part.TryGetProperty("$ref", out var reference) && reference.ValueKind == JsonValueKind.String
                ? Resolve(reference.GetString()!, catalog, commonTypes)
                : part;

            foreach (var nested in Compose(resolved, catalog, commonTypes))
            {
                yield return nested;
            }
        }
    }

    private static JsonElement Resolve(string reference, JsonElement catalog, JsonElement commonTypes)
    {
        const string CommonTypesPrefix = "common_types.json#/$defs/";
        const string LocalPrefix = "#/$defs/";

        if (reference.StartsWith(CommonTypesPrefix, StringComparison.Ordinal))
        {
            return commonTypes.GetProperty("$defs").GetProperty(reference[CommonTypesPrefix.Length..]);
        }

        if (reference.StartsWith(LocalPrefix, StringComparison.Ordinal))
        {
            return catalog.GetProperty("$defs").GetProperty(reference[LocalPrefix.Length..]);
        }

        throw new NotSupportedException(
            $"the schema composes a component from '{reference}', which this derivation cannot resolve");
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
