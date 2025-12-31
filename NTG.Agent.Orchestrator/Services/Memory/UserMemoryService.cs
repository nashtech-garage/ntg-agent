using Microsoft.KernelMemory;
using NTG.Agent.Common.Dtos.Memory;
using NTG.Agent.Orchestrator.Services.Agents;
using System.Globalization;
using System.Text.Json;

namespace NTG.Agent.Orchestrator.Services.Memory;

public class UserMemoryService : IUserMemoryService
{
    private readonly IKernelMemory _kernelMemory;
    private readonly IAgentFactory _agentFactory;
    private readonly ILogger<UserMemoryService> _logger;
    private const string MEMORY_INDEX = "user-memories";
    private static readonly JsonSerializerOptions _llmJsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private const string MemoryExtractionPrompt = @"You are a memory extraction assistant. Your job is to analyze user messages and identify details that should be stored in the user's long-term profile.

        ### RULES
        1. **Analyze for:**
           - User Preferences (favorite topics, hobbies, likes/dislikes, sports teams)
           - Profile Details (NAME, age, profession, education, location, hardware/software stack)
           - Personal Info (marital status, children, family, birthdate)
           - Goals & Projects (current focus, long-term aspirations)
           - Relationships (names of coworkers, family, friends)
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
           - **CRITICAL:** If user provides MULTIPLE pieces of information in ONE message, extract ALL of them. Create one consolidated memory with all facts.
           - **Third-Person Only:** Extract facts about the user. Do not use ""I"" or ""You"". Use ""User"".
             - BAD: ""I like using C#""
             - GOOD: ""User prefers using C#""
           - **Standalone:** The extracted memory must make sense entirely on its own without the original conversation context.
           - **Updates/Corrections:** If the user corrects or updates previous information (e.g., ""Actually my name is..."", ""Oops, I meant...""), use searchQuery to find and replace the old memory.
           - **New Information:** If this is the FIRST time the user mentions something, set searchQuery to null (no need to search for conflicts).

        ### EXAMPLES
        
        Example 1 - Multiple NEW facts (searchQuery should be null):
        User: ""My name is John, I am 35 years old, I work as a software engineer""
        Output: [
          {{
            ""shouldWriteMemory"": true,
            ""memoryToWrite"": ""User's name is John"",
            ""confidence"": 0.95,
            ""category"": ""profile"",
            ""tags"": ""name"",
            ""searchQuery"": null
          }},
          {{
            ""shouldWriteMemory"": true,
            ""memoryToWrite"": ""User is 35 years old"",
            ""confidence"": 0.95,
            ""category"": ""profile"",
            ""tags"": ""age"",
            ""searchQuery"": null
          }},
          {{
            ""shouldWriteMemory"": true,
            ""memoryToWrite"": ""User works as a software engineer"",
            ""confidence"": 0.95,
            ""category"": ""profile"",
            ""tags"": ""profession,job"",
            ""searchQuery"": null
          }}
        ]
        
        ### CRITICAL RULES FOR TAGS:
        - **EACH MEMORY MUST HAVE ITS OWN UNIQUE TAGS** based on what it contains!
        - Name memory → tags: ""name""
        - Age memory → tags: ""age""
        - Profession memory → tags: ""profession,job""
        - Marital status → tags: ""married,marital_status""
        - Children → tags: ""children,family""
        - **DO NOT reuse the same tag for different facts!**

        Example 2 - Family information (NEW facts):
        User: ""I got married in 2014 and have 2 kids (1 son, 1 daughter)""
        Output: [
          {{
            ""shouldWriteMemory"": true,
            ""memoryToWrite"": ""User married since 2014"",
            ""confidence"": 0.9,
            ""category"": ""profile"",
            ""tags"": ""married,marital_status"",
            ""searchQuery"": null
          }},
          {{
            ""shouldWriteMemory"": true,
            ""memoryToWrite"": ""User has 2 children: 1 son and 1 daughter"",
            ""confidence"": 0.9,
            ""category"": ""profile"",
            ""tags"": ""children,family"",
            ""searchQuery"": null
          }}
        ]

        Example 3 - CORRECTION (searchQuery to find old value):
        User: ""Actually, I am 38 years old"" (correcting previous age)
        Output: [
          {{
            ""shouldWriteMemory"": true,
            ""memoryToWrite"": ""User is 38 years old"",
            ""confidence"": 0.95,
            ""category"": ""profile"",
            ""tags"": ""age"",
            ""searchQuery"": ""age""
          }}
        ]

        ### OUTPUT FORMAT
        Response must be a JSON ARRAY containing one object per fact. Each fact should be stored separately. Do not use Markdown blocks (```json).
        [
            {{
                ""shouldWriteMemory"": true/false,
                ""memoryToWrite"": ""User-centric, standalone SINGLE fact in third person (or null if false)"",
                ""confidence"": 0.1 to 1.0,
                ""category"": ""preference|profile|goal|project|general"",
                ""tags"": ""comma-separated tags or null"",
                ""searchQuery"": ""Keywords to find OLD memories to REPLACE. ONLY use when user says 'Actually...', 'I meant...', 'Correction:'. For NEW information, ALWAYS use null.""
            }}
        ]

        **CRITICAL RULES for searchQuery:**
        - NEW information (first time mentioned) → searchQuery: null
        - CORRECTION (user says 'actually', 'I meant', 'mistake') → searchQuery: ""field name""
        - If unsure → searchQuery: null (safer to keep both than lose one)

        **CRITICAL:** Extract each fact as a SEPARATE object in the array. If user says ""My name is John, I am 35, I work as engineer"", return 3 objects: one for name, one for age, one for profession.

        User message: {0}

        Response (JSON array only):";

    public UserMemoryService(
        IKernelMemory kernelMemory,
        IAgentFactory agentFactory,
        ILogger<UserMemoryService> logger)
    {
        _kernelMemory = kernelMemory ?? throw new ArgumentNullException(nameof(kernelMemory));
        _agentFactory = agentFactory ?? throw new ArgumentNullException(nameof(agentFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<MemoryExtractionResultDto>> ExtractMemoryAsync(
    string userMessage,
    Guid userId,
    CancellationToken ct = default)
    {
        try
        {
            string prompt = string.Format(CultureInfo.InvariantCulture, MemoryExtractionPrompt, userMessage);

            var agent = await _agentFactory.CreateBasicAgent("You are a memory extraction assistant. Respond only with valid JSON array.");
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

            var results = JsonSerializer.Deserialize<List<MemoryExtractionResultDto>>(responseText, _llmJsonOptions);

            if (results == null || results.Count == 0)
            {
                _logger.LogWarning("Failed to parse memory extraction response or empty array. Raw Text: {RawText}", responseText);
                return new List<MemoryExtractionResultDto>();
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extraction memory");
            return new List<MemoryExtractionResultDto>();
        }
    }

    public async Task<UserMemoryDto> StoreMemoryAsync(
        Guid userId,
        string content,
        string category,
        string? tags = null,
        CancellationToken ct = default)
    {
        var memoryId = Guid.NewGuid();
        var documentId = $"memory-{userId}-{memoryId}";
        
        var tagCollection = new TagCollection
        {
            { "userId", userId.ToString() },
            { "category", category },
            { "memoryId", memoryId.ToString() },
            { "createdAt", DateTime.UtcNow.ToString("O") }
        };

        if (!string.IsNullOrWhiteSpace(tags))
        {
            tagCollection.Add("tags", tags);
        }

        await _kernelMemory.ImportTextAsync(
            content,
            documentId: documentId,
            index: MEMORY_INDEX,
            tags: tagCollection,
            cancellationToken: ct);

        return new UserMemoryDto(
            Id: memoryId,
            UserId: userId,
            Content: content,
            Category: category,
            Tags: tags,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow
        );
    }

    public async Task<List<UserMemoryDto>> RetrieveMemoriesAsync(
        Guid userId,
        string? query = null,
        int topN = 5,
        string? category = null,
        CancellationToken ct = default)
    {
        var filter = new MemoryFilter();
        filter.Add("userId", userId.ToString());

        if (!string.IsNullOrWhiteSpace(category))
        {
            filter.Add("category", category);
        }

        SearchResult searchResult;

        if (!string.IsNullOrWhiteSpace(query))
        {
            searchResult = await _kernelMemory.SearchAsync(
                query: query,
                index: MEMORY_INDEX,
                filters: new List<MemoryFilter> { filter },
                limit: topN,
                cancellationToken: ct);
        }
        else
        {
            searchResult = await _kernelMemory.SearchAsync(
                query: "user information profile preferences goals",
                index: MEMORY_INDEX,
                filters: new List<MemoryFilter> { filter },
                limit: topN,
                cancellationToken: ct);
        }

        var memories = ParseSearchResults(searchResult, userId);
        return memories;
    }

    private static List<UserMemoryDto> ParseSearchResults(SearchResult searchResult, Guid userId)
    {
        var memories = new List<UserMemoryDto>();

        foreach (var citation in searchResult.Results)
        {
            foreach (var partition in citation.Partitions)
            {
                var tags = partition.Tags;
                
                var memoryId = tags.ContainsKey("memoryId") && Guid.TryParse(tags["memoryId"].FirstOrDefault(), out var mid)
                    ? mid
                    : Guid.NewGuid();

                var categoryValue = tags.ContainsKey("category") 
                    ? tags["category"].FirstOrDefault() ?? "general" 
                    : "general";

                var tagsValue = tags.ContainsKey("tags") 
                    ? tags["tags"].FirstOrDefault() 
                    : null;

                var createdAt = tags.ContainsKey("createdAt") && DateTime.TryParse(tags["createdAt"].FirstOrDefault(), out var dt)
                    ? dt
                    : DateTime.UtcNow;

                memories.Add(new UserMemoryDto(
                    Id: memoryId,
                    UserId: userId,
                    Content: partition.Text,
                    Category: categoryValue,
                    Tags: tagsValue,
                    CreatedAt: createdAt,
                    UpdatedAt: createdAt,
                    LastAccessedAt: DateTime.UtcNow
                ));
            }
        }

        return memories;
    }

    public async Task<List<UserMemoryDto>> RetrieveMemoriesByFieldAsync(
        Guid userId,
        string fieldTag,
        string? category = null,
        CancellationToken ct = default)
    {
        var filter = new MemoryFilter();
        filter.Add("userId", userId.ToString());
        filter.Add("tags", fieldTag);

        if (!string.IsNullOrWhiteSpace(category))
        {
            filter.Add("category", category);
        }

        var searchResult = await _kernelMemory.SearchAsync(
            query: "*",
            index: MEMORY_INDEX,
            filters: new List<MemoryFilter> { filter },
            limit: 10,
            cancellationToken: ct);

        var memories = ParseSearchResults(searchResult, userId);
        return memories;
    }

    public async Task<bool> DeleteMemoryAsync(Guid memoryId, CancellationToken ct = default)
    {
        var filters = new List<MemoryFilter>
        {
            MemoryFilters.ByTag("memoryId", memoryId.ToString())
        };

        var searchResult = await _kernelMemory.SearchAsync(
            query: "*",
            index: MEMORY_INDEX,
            filters: filters,
            limit: 1,
            cancellationToken: ct);

        if (searchResult.Results.Count > 0)
        {
            var documentId = searchResult.Results[0].DocumentId;
            await _kernelMemory.DeleteDocumentAsync(documentId, index: MEMORY_INDEX, cancellationToken: ct);
            return true;
        }

        return false;
    }

    public string FormatMemoriesForPrompt(List<UserMemoryDto> memories)
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
