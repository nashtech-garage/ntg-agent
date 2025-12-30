using Microsoft.EntityFrameworkCore;
using NTG.Agent.Common.Dtos.Memory;
using NTG.Agent.Orchestrator.Data;
using NTG.Agent.Orchestrator.Models.Memory;
using NTG.Agent.Orchestrator.Services.Agents;
using System.Globalization;
using System.Text.Json;

namespace NTG.Agent.Orchestrator.Services.Memory;

public class UserMemoryService : IUserMemoryService
{
    private readonly AgentDbContext _dbContext;
    private readonly IAgentFactory _agentFactory;
    private readonly ILogger<UserMemoryService> _logger;
    private static readonly JsonSerializerOptions _llmJsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private const string MemoryExtractionPrompt = @"You are a memory extraction assistant. Your job is to analyze user messages and identify details that should be stored in the user's long-term profile.

        ### RULES
        1. **Analyze for:**
           - User Preferences (favorite topics, hobbies, likes/dislikes)
           - Profile Details (profession, education, name, location, hardware/software stack)
           - Goals & Projects (current focus, long-term aspirations)
           - Relationships (names of coworkers, family mentioned)
           - Important life facts
           - Communication style preferences

        2. **Ignore:**
           - Transient requests (""write a function for this"", ""translate this"", ""fix my code"")
           - General knowledge questions (""who is the president?"")
           - Greetings or small talk (""hi"", ""how are you"")
           - Context-dependent statements that lose meaning without history (""I agree"", ""That works"")
           - One-time questions
           - Temporary context

        3. **Extraction Guidelines:**
           - **Third-Person Only:** Extract facts about the user. Do not use ""I"" or ""You"". Use ""User"".
             - BAD: ""I like using C#""
             - GOOD: ""User prefers using C#""
           - **Standalone:** The extracted memory must make sense entirely on its own without the original conversation context.

        ### OUTPUT FORMAT
        Response must be valid JSON only. Do not use Markdown blocks (```json).
        {{
            ""shouldWriteMemory"": true/false,
            ""memoryToWrite"": ""User-centric, standalone fact in third person. Extracted information in clear, concise form"" (or null if false),
            ""confidence"": 0.1 to 1.0 (float),
            ""category"": ""preference|profile|goal|project|general"",
            ""tags"": ""comma-separated tags or null""
        }}

        User message: {0}

        Response (JSON only):";

    public UserMemoryService(
        AgentDbContext dbContext,
        IAgentFactory agentFactory,
        ILogger<UserMemoryService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MemoryExtractionResultDto> ExtractMemoryAsync(
    string userMessage,
    Guid userId,
    CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Extracting memory from message for user {UserId}", userId);

            string prompt = string.Format(CultureInfo.InvariantCulture, MemoryExtractionPrompt, userMessage);

            // Note: The system prompt here reinforces the instruction, which is good practice.
            var agent = await _agentFactory.CreateBasicAgent("You are a memory extraction assistant. Respond only with valid JSON.");
            var runResults = await agent.RunAsync(prompt);
            var responseText = runResults.Text.Trim();

            // Even if the prompt forbids it, LLMs love Markdown. Keep this safety net.
            if (responseText.Contains("```json", StringComparison.OrdinalIgnoreCase))
            {
                var startIndex = responseText.IndexOf("```json", StringComparison.OrdinalIgnoreCase) + 7;
                var endIndex = responseText.LastIndexOf("```", StringComparison.Ordinal);
                if (endIndex > startIndex)
                {
                    responseText = responseText.Substring(startIndex, endIndex - startIndex).Trim();
                }
            }
            else if (responseText.Contains("```", StringComparison.Ordinal))
            {
                var startIndex = responseText.IndexOf("```", StringComparison.Ordinal) + 3;
                var endIndex = responseText.LastIndexOf("```", StringComparison.Ordinal);
                if (endIndex > startIndex)
                {
                    responseText = responseText.Substring(startIndex, endIndex - startIndex).Trim();
                }
            }

            var result = JsonSerializer.Deserialize<MemoryExtractionResultDto>(responseText, _llmJsonOptions);

            if (result == null)
            {
                _logger.LogWarning("Failed to parse memory extraction response. Raw Text: {RawText}", responseText);
                return new MemoryExtractionResultDto(false, null, null, null, null);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extraction memory");
            return new MemoryExtractionResultDto(false, null, null, null, null);
        }
    }

    public async Task<UserMemory> StoreMemoryAsync(
        Guid userId,
        string content,
        string category,
        string? tags = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Storing memory for user {UserId}", userId);

        // Check for similar existing memories to avoid exact duplicates
        var existingMemory = await _dbContext.UserMemories
            .Where(m => m.UserId == userId && m.Content == content && m.IsActive)
            .FirstOrDefaultAsync(ct);

        if (existingMemory != null)
        {
            // Update access information
            existingMemory.UpdatedAt = DateTime.UtcNow;
            existingMemory.AccessCount++;
            await _dbContext.SaveChangesAsync(ct);
            return existingMemory;
        }

        var memory = new UserMemory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Content = content,
            Category = category,
            Tags = tags,
            IsActive = true,
            AccessCount = 0
        };

        _dbContext.UserMemories.Add(memory);
        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Memory stored successfully with ID {MemoryId}", memory.Id);
        return memory;
    }

    public async Task<List<UserMemory>> RetrieveMemoriesAsync(
        Guid userId,
        string? query = null,
        int topN = 5,
        string? category = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Retrieving memories for user {UserId}", userId);

        var memoriesQuery = _dbContext.UserMemories
            .Where(m => m.UserId == userId && m.IsActive);

        if (!string.IsNullOrWhiteSpace(category))
        {
            memoriesQuery = memoriesQuery.Where(m => m.Category == category);
        }

        // Simple text-based search if query is provided
        if (!string.IsNullOrWhiteSpace(query))
        {
            memoriesQuery = memoriesQuery.Where(m => m.Content.Contains(query) || (m.Tags != null && m.Tags.Contains(query)));
        }

        var memories = await memoriesQuery
            .OrderByDescending(m => m.UpdatedAt)
            .Take(topN)
            .ToListAsync(ct);

        // Update access information
        foreach (var memory in memories)
        {
            memory.AccessCount++;
            memory.LastAccessedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation("Retrieved {Count} memories for user {UserId}", memories.Count, userId);
        return memories;
    }

    public async Task<UserMemory?> UpdateMemoryAsync(
        Guid memoryId,
        string content,
        string category,
        string? tags,
        bool isActive,
        CancellationToken ct = default)
    {
        var memory = await _dbContext.UserMemories.FindAsync([memoryId], ct);
        if (memory == null)
        {
            return null;
        }

        memory.Content = content;
        memory.Category = category;
        memory.Tags = tags;
        memory.IsActive = isActive;
        memory.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(ct);
        return memory;
    }

    public async Task<bool> DeleteMemoryAsync(Guid memoryId, CancellationToken ct = default)
    {
        var memory = await _dbContext.UserMemories.FindAsync([memoryId], ct);
        if (memory == null)
        {
            return false;
        }

        _dbContext.UserMemories.Remove(memory);
        await _dbContext.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> DeleteAllMemoriesAsync(Guid userId, CancellationToken ct = default)
    {
        var memories = await _dbContext.UserMemories
            .Where(m => m.UserId == userId)
            .ToListAsync(ct);

        _dbContext.UserMemories.RemoveRange(memories);
        await _dbContext.SaveChangesAsync(ct);
        return memories.Count;
    }

    public async Task<UserMemory?> GetMemoryByIdAsync(Guid memoryId, CancellationToken ct = default)
    {
        return await _dbContext.UserMemories.FindAsync([memoryId], ct);
    }

    public string FormatMemoriesForPrompt(List<UserMemory> memories)
    {
        if (memories == null || memories.Count == 0)
        {
            return string.Empty;
        }

        var formattedMemories = new System.Text.StringBuilder();
        formattedMemories.AppendLine("=== USER PROFILE AND MEMORIES ===");
        formattedMemories.AppendLine("The following information has been remembered from previous conversations:");
        formattedMemories.AppendLine();

        var groupedByCategory = memories.GroupBy(m => m.Category);
        foreach (var group in groupedByCategory)
        {
            formattedMemories.AppendLine(CultureInfo.InvariantCulture, $"[{group.Key.ToUpperInvariant()}]");
            foreach (var memory in group)
            {
                formattedMemories.AppendLine(CultureInfo.InvariantCulture, $"- {memory.Content}");
            }
            formattedMemories.AppendLine();
        }

        formattedMemories.AppendLine("Use this information naturally in the conversation to provide personalized responses.");
        formattedMemories.AppendLine("=== END OF USER MEMORIES ===");
        formattedMemories.AppendLine();

        return formattedMemories.ToString();
    }
}
