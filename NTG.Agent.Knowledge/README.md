# NTG Agent - Knowledge Service

## Project Summary

The **NTG Agent Knowledge Service** is a specialized microservice built on Microsoft Kernel Memory that handles document ingestion, embedding generation, vector storage, and semantic search for the NTG Agent platform. This service processes various document formats (PDFs, Office documents, web pages, text files), extracts content, generates embeddings using configured LLM providers, and enables context-aware AI responses through Retrieval-Augmented Generation (RAG).

The service supports multiple storage backends for documents and vectors, configurable embedding models, and provides a RESTful API for document management and semantic search operations.

## Project Structure

```
NTG.Agent.Knowledge/
??? appsettings.json                     # Configuration
??? appsettings.Development.json          # Dev settings
??? Program.cs                           # Service entry point
??? NTG.Agent.Knowledge.csproj
```

**Note:** This project uses the Microsoft Kernel Memory SDK which provides:
- Document ingestion pipelines
- Content extraction handlers
- Embedding generation
- Vector storage
- Search APIs

## Main Components

### Kernel Memory Service

The service is built on **Microsoft.KernelMemory.Service** which provides:

1. **Document Ingestion Pipeline**
   - Extract text from various formats
   - Split content into chunks
   - Generate embeddings
   - Store in vector database

2. **Search Engine**
   - Semantic search across documents
   - Tag-based filtering
   - Relevance scoring
   - Citation generation

3. **Storage Abstractions**
   - **Document Storage** - Raw file storage
   - **Memory Storage** - Vector database
   - **Text Generation** - LLM for embeddings
   - **Embedding Generation** - Vector models

### Supported Document Types

- **Documents**: PDF, Word (.doc/.docx), Excel, PowerPoint, EPUB, RTF
- **Text**: Plain text, Markdown, JSON, XML, HTML, CSV
- **Code**: JavaScript, CSS, Shell scripts
- **Web**: Web pages via URL
- **Archives**: ZIP, RAR, 7z, tar, gz
- **Images**: JPEG, PNG, GIF, BMP, TIFF, WebP, SVG (for OCR/analysis)

### Pipeline Handlers

Configurable processing steps:
1. **Extract** - Content extraction from files
2. **Partition** - Split into chunks
3. **GenerateEmbeddings** - Create vector representations
4. **SaveRecords** - Store in vector DB
5. **SummarizeEmbedding** - Generate summaries
6. **DeleteDocument** - Remove documents

## Design Patterns Used

1. **Pipeline Pattern** - Document processing through stages
2. **Strategy Pattern** - Pluggable storage and LLM providers
3. **Factory Pattern** - Handler instantiation
4. **Repository Pattern** - Storage abstractions
5. **Observer Pattern** - Asynchronous processing notifications

## Configuration

### Storage Backends

The service supports multiple storage configurations:

#### Simple (Development)
```json
{
  "KernelMemory": {
    "DataIngestion": {
      "OrchestrationType": "InProcess",
      "EmbeddingGeneratorTypes": ["OpenAI"],
      "VectorDbTypes": ["SimpleVectorDb"]
    },
    "Services": {
      "SimpleVectorDb": {
        "StorageType": "Disk",
        "Directory": "_vectors"
      }
    }
  }
}
```

#### Azure Production
```json
{
  "KernelMemory": {
    "DataIngestion": {
      "OrchestrationType": "Distributed",
      "DistributedOrchestration": {
        "QueueType": "AzureQueue"
      }
    },
    "Services": {
      "AzureBlobs": {
        "Account": "your-account",
        "Container": "km-documents"
      },
      "AzureAISearch": {
        "Endpoint": "https://your-search.search.windows.net",
        "APIKey": "..."
      }
    }
  }
}
```

### LLM Providers

#### GitHub Models (Free for Development)
```json
{
  "KernelMemory": {
    "Services": {
      "OpenAI": {
        "Endpoint": "https://models.github.ai/inference",
        "APIKey": "your-github-token",
        "TextModel": "gpt-4o",
        "EmbeddingModel": "text-embedding-3-small"
      }
    }
  }
}
```

#### Azure OpenAI
```json
{
  "KernelMemory": {
    "DataIngestion": {
      "EmbeddingGeneratorTypes": ["AzureOpenAI"]
    },
    "Services": {
      "AzureOpenAIText": {
        "Endpoint": "https://your-openai.openai.azure.com/",
        "APIKey": "...",
        "Deployment": "gpt-4"
      },
      "AzureOpenAIEmbedding": {
        "Endpoint": "https://your-openai.openai.azure.com/",
        "APIKey": "...",
        "Deployment": "text-embedding-ada-002"
      }
    }
  }
}
```

### Service Configuration

```json
{
  "KernelMemory": {
    "Service": {
      "RunWebService": true,
      "RunHandlers": true,
      "OpenApiEnabled": false,
      "MaxUploadSizeMb": 50,
      "Handlers": {
        "extract": {
          "Assembly": "Microsoft.KernelMemory.Core",
          "Type": "Microsoft.KernelMemory.Handlers.TextExtractionHandler"
        },
        "partition": { ... },
        "gen_embeddings": { ... },
        "save_records": { ... }
      }
    },
    "ServiceAuthorization": {
      "Enabled": true,
      "AccessKey1": "your-api-key",
      "AccessKey2": "your-api-key-2"
    }
  }
}
```

## API Endpoints

### Document Management

```bash
# Upload document
POST /upload
Content-Type: multipart/form-data
Authorization: <api-key>

# Import web page
POST /upload
Content-Type: application/json
{ "url": "https://example.com/page" }

# Delete document
DELETE /documents/{documentId}
Authorization: <api-key>

# Export document
GET /documents/{documentId}/export
Authorization: <api-key>
```

### Search

```bash
# Semantic search
POST /search
Content-Type: application/json
Authorization: <api-key>

{
  "query": "What is RAG?",
  "limit": 3,
  "filters": [
    {
      "tags": { "agentId": "..." }
    }
  ]
}
```

### Service Status

```bash
GET /
GET /health
```

## How to Run

### Prerequisites

- .NET 10 SDK
- Storage backend (local disk for dev, Azure/AWS for production)
- LLM API access (GitHub Models, Azure OpenAI, etc.)

### Setup

1. **Configure LLM Provider**

For GitHub Models (free):
```bash
dotnet user-secrets set "KernelMemory:Services:OpenAI:APIKey" "your-github-token"
```

Create token at: https://github.com/settings/personal-access-tokens
- Required permissions: **models:read**

2. **Configure Storage (Optional)**

Default uses local disk storage. For production, configure Azure Blobs or other providers in `appsettings.json`.

3. **Configure API Authorization**

```bash
dotnet user-secrets set "KernelMemory:ServiceAuthorization:AccessKey1" "your-secure-key"
dotnet user-secrets set "KernelMemory:ServiceAuthorization:AccessKey2" "your-backup-key"
```

4. **Run the Service**

```bash
# Standalone
dotnet run

# Via Aspire
cd ../NTG.Agent.AppHost
dotnet run
```

5. **Verify**

```bash
curl https://localhost:5003/health
```

## Integration with Orchestrator

The Orchestrator service uses `MemoryWebClient` to communicate:

```csharp
builder.Services.AddScoped<IKernelMemory>(serviceProvider =>
{
    var endpoint = "https://localhost:5003";
    var apiKey = "your-api-key";
    return new MemoryWebClient(endpoint, apiKey);
});
```

Service discovery in Aspire automatically resolves the endpoint.

## Document Processing Flow

1. **Upload** ? Document received via API
2. **Extract** ? Text extracted from file format
3. **Partition** ? Content split into chunks (configurable size)
4. **Generate Embeddings** ? Vector representations created
5. **Save Records** ? Stored in vector database with metadata
6. **Search** ? Semantic search queries vectors, returns relevant chunks

### Tagging Strategy

Documents are tagged for filtering:
```json
{
  "tags": {
    "agentId": "uuid",
    "tags": ["tag1", "tag2"]
  }
}
```

Search filters by agent and user role tags:
```csharp
var filters = tags.Select(tag => {
    var filter = MemoryFilters.ByTag("tags", tag);
    filter.Add("agentId", agentId.ToString());
    return filter;
});
```

## Development

### Custom Handlers

Add custom processing steps:

```csharp
public class MyCustomHandler : IPipelineStepHandler
{
    public string StepName => "my_custom_step";
    
    public async Task<(bool success, DataPipeline updatedPipeline)> InvokeAsync(
        DataPipeline pipeline,
        CancellationToken cancellationToken)
    {
        // Custom processing logic
        return (true, pipeline);
    }
}
```

Register in configuration:
```json
{
  "Handlers": {
    "my_custom_step": {
      "Assembly": "MyAssembly",
      "Type": "MyNamespace.MyCustomHandler"
    }
  }
}
```

### Testing Locally

```bash
# Upload a document
curl -X POST https://localhost:5003/upload \
  -H "Authorization: your-api-key" \
  -F "file=@document.pdf" \
  -F "index=default"

# Search
curl -X POST https://localhost:5003/search \
  -H "Authorization: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
    "query": "test query",
    "limit": 3
  }'
```

## Performance Tuning

### Embedding Generation

- Choose appropriate embedding model size (balance speed/quality)
- Batch processing for multiple documents
- Parallel handler execution

### Chunking Strategy

```json
{
  "KernelMemory": {
    "TextPartitioning": {
      "MaxTokensPerParagraph": 1000,
      "MaxTokensPerLine": 300,
      "OverlappingTokens": 100
    }
  }
}
```

### Caching

Enable caching for frequently accessed documents:
```json
{
  "Caching": {
    "Enabled": true,
    "TTLSeconds": 3600
  }
}
```

## Deployment

### Production Build

```bash
dotnet publish -c Release -o ./publish
```

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY ./publish .
ENTRYPOINT ["dotnet", "NTG.Agent.Knowledge.dll"]
```

### Environment Variables

```bash
# LLM Configuration
KernelMemory__Services__OpenAI__APIKey="..."

# Azure Storage
KernelMemory__Services__AzureBlobs__ConnectionString="..."

# API Security
KernelMemory__ServiceAuthorization__AccessKey1="..."

# Environment
ASPNETCORE_ENVIRONMENT=Production
```

### Pre-deployment Checklist

- ? Configure production LLM provider
- ? Set up persistent storage (Azure Blobs/AI Search recommended)
- ? Secure API keys using Key Vault
- ? Configure API authorization
- ? Set appropriate max upload size
- ? Enable monitoring and logging
- ? Test document processing pipeline
- ? Verify search quality and performance

## Monitoring

### Logs

The service provides detailed logging:
- Environment and configuration
- Memory database type
- Document storage type
- Embedding generator
- Handler execution
- API requests

### Metrics

Monitor:
- Document ingestion rate
- Search query latency
- Embedding generation time
- Storage usage
- API request count

### Health Checks

- `/health` - Service health status
- `/` - Service uptime

## Troubleshooting

### Common Issues

**Documents not processing:**
- Check handler configuration
- Verify LLM API key and quota
- Review logs for extraction errors

**Search returns no results:**
- Verify document was successfully ingested
- Check tag filters match
- Ensure embeddings were generated

**API authentication fails:**
- Verify `Authorization` header contains valid API key
- Check `ServiceAuthorization:Enabled` configuration

## Additional Resources

- [Main Solution README](../README.md)
- [Microsoft Kernel Memory](https://github.com/microsoft/kernel-memory)
- [Kernel Memory Documentation](https://microsoft.github.io/kernel-memory/)
- [RAG Pattern Overview](https://learn.microsoft.com/azure/search/retrieval-augmented-generation-overview)
