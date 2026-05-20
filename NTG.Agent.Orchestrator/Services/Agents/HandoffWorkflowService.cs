using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using NTG.Agent.Orchestrator.Data;

namespace NTG.Agent.Orchestrator.Services.Agents;

public interface IHandoffWorkflowService
{
    /// <summary>
    /// Checks if the given agent has any outgoing handoff connections configured.
    /// </summary>
    Task<bool> HasHandoffWorkflowAsync(Guid agentId);

    /// <summary>
    /// Builds an AgentWorkflow from the database handoff graph, starting from the given agent.
    /// Returns null if no connections exist for this agent.
    /// </summary>
    Task<Workflow?> BuildWorkflowAsync(Guid startAgentId);
}

public class HandoffWorkflowService : IHandoffWorkflowService
{
    private readonly AgentDbContext _dbContext;
    private readonly IAgentFactory _agentFactory;
    private readonly ILogger<HandoffWorkflowService> _logger;

    public HandoffWorkflowService(
        AgentDbContext dbContext,
        IAgentFactory agentFactory,
        ILogger<HandoffWorkflowService> logger)
    {
        _dbContext = dbContext;
        _agentFactory = agentFactory;
        _logger = logger;
    }

    public async Task<bool> HasHandoffWorkflowAsync(Guid agentId)
    {
        return await _dbContext.AgentHandoffs
            .AnyAsync(h => h.SourceAgentId == agentId);
    }

    public async Task<Workflow?> BuildWorkflowAsync(Guid startAgentId)
    {
        // Load all handoff edges where the start agent participates (directly or transitively)
        var allHandoffs = await _dbContext.AgentHandoffs.ToListAsync();

        // Find all agents reachable from the start agent through the handoff graph
        var participantIds = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(startAgentId);
        participantIds.Add(startAgentId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var targetIds = allHandoffs
                .Where(h => h.SourceAgentId == currentId)
                .Select(h => h.TargetAgentId);

            foreach (var targetId in targetIds)
            {
                if (participantIds.Add(targetId))
                {
                    queue.Enqueue(targetId);
                }
            }
        }

        // Only the start agent — no workflow needed
        if (participantIds.Count <= 1)
            return null;

        // Filter edges to only those between participant agents
        var relevantEdges = allHandoffs
            .Where(h => participantIds.Contains(h.SourceAgentId) && participantIds.Contains(h.TargetAgentId))
            .ToList();

        if (relevantEdges.Count == 0)
            return null;

        // Create AIAgent instances for all participants
        var agentMap = new Dictionary<Guid, AIAgent>();
        foreach (var id in participantIds)
        {
            try
            {
                agentMap[id] = await _agentFactory.CreateAgent(id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create agent {AgentId} for handoff workflow. Skipping.", id);
            }
        }

        if (!agentMap.TryGetValue(startAgentId, out var startAgent))
        {
            _logger.LogError("Start agent {AgentId} could not be created for handoff workflow.", startAgentId);
            return null;
        }

        // Build the workflow using AgentWorkflowBuilder
        #pragma warning disable MAAIW001
        var builder = AgentWorkflowBuilder.CreateHandoffBuilderWith(startAgent);

        // Group edges by source → list of targets, then call WithHandoffs for each group
        var edgeGroups = relevantEdges
            .GroupBy(e => e.SourceAgentId)
            .Where(g => agentMap.ContainsKey(g.Key));

        foreach (var group in edgeGroups)
        {
            var sourceAgent = agentMap[group.Key];
            var targetAgents = group
                .Where(e => agentMap.ContainsKey(e.TargetAgentId))
                .Select(e => agentMap[e.TargetAgentId])
                .ToArray();

            if (targetAgents.Length > 0)
            {
                builder.WithHandoffs(sourceAgent, targetAgents);
            }
        }

        var workflow = builder.Build();
        #pragma warning restore MAAIW001

        _logger.LogInformation(
            "Built handoff workflow starting from agent {StartAgentId} with {ParticipantCount} participants and {EdgeCount} connections.",
            startAgentId, agentMap.Count, relevantEdges.Count);

        return workflow;
    }
}
