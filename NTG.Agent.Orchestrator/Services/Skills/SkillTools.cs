using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace NTG.Agent.Orchestrator.Services.Skills;

/// <summary>
/// Builds the per-request skill tools handed to a running agent.
/// </summary>
/// <remarks>
/// Both tools are constructed per request and close over the agent id, so the agent a run belongs
/// to is never a model-supplied parameter. That is deliberate: the model names a <em>skill</em>,
/// and the registry re-checks that the skill is bound and enabled for <em>this</em> agent before
/// returning anything. A model that invents or remembers a skill name from another agent gets
/// nothing back.
/// </remarks>
public static class SkillTools
{
    /// <summary>
    /// The tier-2 tool. Returns a skill's full <c>SKILL.md</c> body so the model can follow it.
    /// </summary>
    public static AIFunction CreateLoadSkill(SkillRegistry registry, Guid agentId, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);

        return AIFunctionFactory.Create(
            async ([Description("Name of the skill to read, exactly as listed in your available skills.")] string skill,
                   CancellationToken cancellationToken) =>
            {
                var body = await registry.GetActiveSkillBodyAsync(agentId, skill, cancellationToken);

                if (body is null)
                {
                    logger.LogInformation(
                        "Agent {AgentId} tried to load skill '{SkillName}', which is not bound or not enabled",
                        agentId,
                        skill);

                    return $"No skill named '{skill}' is available to you. "
                           + "Use one of the names listed in the available skills section, or answer without a skill.";
                }

                logger.LogInformation("Agent {AgentId} loaded skill '{SkillName}'", agentId, skill);
                return body;
            },
            name: SkillPrompt.LoadToolName,
            description:
                "Read the full instructions for one of your available skills. Call this with the "
                + "skill's name before acting on it; the catalog description alone is not enough to "
                + "work from.");
    }

    /// <summary>The tier-3 tool: renders one of a skill's bundled A2UI surface templates.</summary>
    public static AIFunction CreateRenderSurface(
        SkillRegistry registry,
        Agents.RenderableToolCapture capture,
        Guid agentId,
        ILogger logger) =>
        new SurfaceRenderFunction(registry, capture, agentId, logger);
}
