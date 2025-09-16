using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace NTG.Agent.Orchestrator.Plugins;

/// <summary>
/// A plugin that provides Mermaid diagram generation capabilities.
/// This plugin generates Mermaid syntax for various types of diagrams including flowcharts,
/// sequence diagrams, class diagrams, ER diagrams, and more.
/// </summary>
public class MermaidPlugin
{
    [KernelFunction("create_flowchart")]
    [Description("Creates a Mermaid flowchart for processes, workflows, or decision trees")]
    public string CreateFlowchart(
        [Description("Description of the process or workflow to diagram")] string processDescription,
        [Description("Type of flowchart: process, decision, workflow")] string flowchartType = "process")
    {
        return $@"Create a Mermaid flowchart diagram for: {processDescription}

Generate a flowchart of type '{flowchartType}' using proper Mermaid syntax.
Use appropriate shapes:
- Rectangles for processes/actions
- Diamonds for decisions
- Circles for start/end points
- Proper arrows and connections

Format as valid Mermaid flowchart syntax starting with 'flowchart TD' or 'flowchart LR'.
Include meaningful node IDs and descriptive labels.";
    }

    [KernelFunction("create_sequence_diagram")]
    [Description("Creates a Mermaid sequence diagram showing interactions between systems/actors")]
    public string CreateSequenceDiagram(
        [Description("Description of the system interactions or API flow")] string interactionDescription,
        [Description("List of actors/systems involved, separated by commas")] string actors)
    {
        return $@"Create a Mermaid sequence diagram for: {interactionDescription}

Actors/Systems involved: {actors}

Generate a sequence diagram using proper Mermaid syntax:
- Start with 'sequenceDiagram'
- Define participants
- Show message flows with arrows
- Include activation boxes where appropriate
- Add notes for important details

Format as valid Mermaid sequence diagram syntax.";
    }

    [KernelFunction("create_class_diagram")]
    [Description("Creates a Mermaid class diagram for object-oriented design")]
    public string CreateClassDiagram(
        [Description("Description of classes, properties, methods, and relationships")] string classDescription)
    {
        return $@"Create a Mermaid class diagram for: {classDescription}

Generate a class diagram using proper Mermaid syntax:
- Start with 'classDiagram'
- Define classes with properties and methods
- Show relationships (inheritance, composition, association)
- Use proper notation for visibility (+ public, - private, # protected)
- Include data types where relevant

Format as valid Mermaid class diagram syntax.";
    }

    [KernelFunction("create_er_diagram")]
    [Description("Creates a Mermaid Entity Relationship diagram for database design")]
    public string CreateERDiagram(
        [Description("Description of entities, attributes, and relationships")] string databaseDescription)
    {
        return $@"Create a Mermaid Entity Relationship diagram for: {databaseDescription}

Generate an ER diagram using proper Mermaid syntax:
- Start with 'erDiagram'
- Define entities with their attributes
- Show relationships between entities
- Use proper cardinality notation (||--o{{ , }}|..||{{ , etc.)
- Include primary keys and foreign keys where relevant

Format as valid Mermaid ER diagram syntax.";
    }

    [KernelFunction("create_gantt_chart")]
    [Description("Creates a Mermaid Gantt chart for project timelines and tasks")]
    public string CreateGanttChart(
        [Description("Project description with tasks, durations, and dependencies")] string projectDescription)
    {
        return $@"Create a Mermaid Gantt chart for: {projectDescription}

Generate a Gantt chart using proper Mermaid syntax:
- Start with 'gantt'
- Include title and date format
- Define sections for different work areas
- List tasks with durations and dependencies
- Use appropriate task statuses (done, active, future, crit)

Format as valid Mermaid Gantt chart syntax.";
    }

    [KernelFunction("create_state_diagram")]
    [Description("Creates a Mermaid state diagram showing state transitions")]
    public string CreateStateDiagram(
        [Description("Description of states, transitions, and triggers")] string stateDescription)
    {
        return $@"Create a Mermaid state diagram for: {stateDescription}

Generate a state diagram using proper Mermaid syntax:
- Start with 'stateDiagram-v2'
- Define states and their transitions
- Include start and end states where appropriate
- Label transitions with triggers/conditions
- Use proper state notation

Format as valid Mermaid state diagram syntax.";
    }

    [KernelFunction("create_journey_map")]
    [Description("Creates a Mermaid user journey map")]
    public string CreateJourneyMap(
        [Description("Description of user journey, touchpoints, and emotions")] string journeyDescription)
    {
        return $@"Create a Mermaid user journey diagram for: {journeyDescription}

Generate a journey map using proper Mermaid syntax:
- Start with 'journey'
- Define the journey title
- List journey sections with tasks and scores
- Include emotional scores (1-5 scale)
- Show the journey flow and touchpoints

Format as valid Mermaid journey diagram syntax.";
    }

    [KernelFunction("suggest_diagram_type")]
    [Description("Analyzes requirements and suggests the best Mermaid diagram type")]
    public string SuggestDiagramType(
        [Description("What you want to visualize or explain")] string requirements)
    {
        return $@"Analyze the following visualization requirements and suggest the best Mermaid diagram type: {requirements}

Consider these diagram types:
- Flowchart: For processes, workflows, decision trees
- Sequence Diagram: For system interactions, API flows
- Class Diagram: For object-oriented design, code structure
- ER Diagram: For database design, data relationships
- Gantt Chart: For project timelines, task scheduling
- State Diagram: For state machines, status flows
- Journey Map: For user experiences, customer journeys

Provide:
1. Recommended diagram type
2. Why it's the best choice
3. Key elements to include
4. Brief example of how it would look";
    }

    [KernelFunction("create_diagram_from_description")]
    [Description("Creates the most appropriate Mermaid diagram based on natural language description")]
    public string CreateDiagramFromDescription(
        [Description("Natural language description of what needs to be diagrammed")] string description)
    {
        return $@"Based on this description: {description}

Analyze the requirements and:
1. Determine the most appropriate Mermaid diagram type
2. Generate the complete Mermaid diagram syntax
3. Ensure all key elements are included
4. Use proper Mermaid formatting and syntax

Create a comprehensive diagram that best visualizes the described concept, process, or system.";
    }
}
