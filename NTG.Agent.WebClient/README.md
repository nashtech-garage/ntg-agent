# NTG Agent - Web Client

## Project Summary

The **NTG Agent Web Client** is the end-user facing Blazor web application that provides an intuitive chat interface for interacting with AI agents. Built on .NET 10 with hybrid Blazor Server and WebAssembly rendering modes, this application allows users to have conversations with AI agents, share conversations, manage user preferences, and upload documents for context-aware responses.

The Web Client implements a Backend-for-Frontend (BFF) pattern using YARP reverse proxy to securely communicate with backend services while maintaining authentication state.

## Project Structure

```
NTG.Agent.WebClient/
??? NTG.Agent.WebClient/                 # Main server-side Blazor project
?   ??? Components/                      # Blazor components and pages
?   ?   ??? Account/                     # Identity components
?   ?   ??? Layout/                      # Layout components
?   ?   ??? Pages/                       # Chat and user pages
?   ??? Data/                            # Data access layer
?   ?   ??? ApplicationDbContext.cs      # Identity database
?   ??? appsettings.json
?   ??? Program.cs
?   ??? NTG.Agent.WebClient.csproj
?
??? NTG.Agent.WebClient.Client/          # WebAssembly client project
    ??? Services/                        # HTTP client services
    ?   ??? ChatClient.cs                # Chat API client
    ?   ??? ConversationClient.cs        # Conversation management
    ?   ??? SharedConversationClient.cs  # Share conversations
    ?   ??? PreferenceClient.cs          # User preferences
    ??? States/
    ?   ??? ConversationState.cs         # Client-side state management
    ??? Program.cs
    ??? NTG.Agent.WebClient.Client.csproj
```

## Main Components

### Server Project (NTG.Agent.WebClient)

- **ASP.NET Core Identity** - User authentication (optional, supports anonymous)
- **YARP Reverse Proxy** - Routes requests to Orchestrator service
- **Blazor Server & WebAssembly** - Hybrid rendering
- **Bootstrap Blazor** - UI component library
- **Shared Cookie Auth** - Distributed authentication

### Client Project (NTG.Agent.WebClient.Client)

#### HTTP Client Services

- **ChatClient** - Stream AI responses, upload documents
- **ConversationClient** - Create, retrieve, delete conversations
- **SharedConversationClient** - Share conversations publicly
- **PreferenceClient** - Manage user settings (theme, agent selection)

#### State Management

- **ConversationState** - Client-side conversation state

## Features

### Core Features

- ?? **Real-time Chat** - Stream responses from AI agents
- ?? **Document Upload** - Attach files for context-aware answers
- ?? **Conversation Management** - Save, load, and organize chats
- ?? **Share Conversations** - Generate public links with expiration
- ?? **Anonymous & Authenticated Modes** - Optional user accounts
- ?? **User Preferences** - Save preferred agents and settings
- ?? **Responsive Design** - Mobile-friendly interface

### Conversation Sharing

```csharp
// Share a conversation
var shareId = await SharedConversationClient.ShareConversationAsync(new ShareConversationRequest
{
    ConversationId = conversationId,
    ExpiresAt = DateTime.UtcNow.AddDays(7)
});

// Access public shared conversation
var result = await SharedConversationClient.GetPublicSharedConversationAsync(shareId);
```

## Design Patterns Used

1. **Backend-for-Frontend (BFF)** - YARP proxy for API routing
2. **Observer Pattern** - State management with event notifications
3. **Service Layer** - HTTP clients encapsulate backend communication
4. **Streaming Pattern** - Real-time AI response streaming
5. **Repository Pattern** - EF Core for data access

## Dependencies

### NuGet Packages

**Server:**
- Microsoft.AspNetCore.Identity.EntityFrameworkCore (10.0.0)
- Microsoft.EntityFrameworkCore.SqlServer (10.0.0)
- Yarp.ReverseProxy (2.3.0)
- Microsoft.Extensions.ServiceDiscovery.Yarp (10.0.0)

**Client:**
- Microsoft.AspNetCore.Components.WebAssembly (10.0.0)
- Microsoft.AspNetCore.Components.WebAssembly.Authentication (10.0.0)
- BootstrapBlazor (Component library)

### Project References

- **NTG.Agent.ServiceDefaults** - Shared configuration
- **NTG.Agent.Common** - DTOs and utilities

### Backend Services

- **NTG.Agent.Orchestrator** - Chat and conversation APIs

## How to Run

### Prerequisites

- .NET 10 SDK
- SQL Server (optional for authenticated users)
- Visual Studio 2026 / Rider / VS Code

### Setup

1. **Configure Database (Optional)**

For authenticated users, set up the database:

```bash
cd NTG.Agent.WebClient
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.;Database=NTGAgent;Trusted_Connection=True;TrustServerCertificate=true"
```

2. **Run the Application**

```bash
# Option 1: Standalone
cd NTG.Agent.WebClient
dotnet run

# Option 2: Via Aspire (recommended - starts all services)
cd ../NTG.Agent.AppHost
dotnet run
```

3. **Access the Web Client**

Navigate to `https://localhost:7000` (or port shown in console)

### Anonymous Mode

The application works without authentication. Users get a session-based experience with:
- Temporary conversations stored by session ID
- Access to public AI agents
- No account required

### Authenticated Mode

Create an account or log in to get:
- Persistent conversations across devices
- Saved user preferences
- Ability to share conversations
- Private conversation history

## Configuration

### Reverse Proxy

Configure backend routing in `appsettings.json`:

```json
{
  "ReverseProxy": {
    "Routes": {
      "orchestrator-route": {
        "ClusterId": "orchestrator-cluster",
        "Match": {
          "Path": "/api/{**catch-all}"
        }
      }
    },
    "Clusters": {
      "orchestrator-cluster": {
        "Destinations": {
          "orchestrator": {
            "Address": "http://localhost:5002"
          }
        }
      }
    }
  }
}
```

### Data Protection

Shared keys for distributed auth:
- Application name: `NTGAgent`
- Keys location: `../../key/`

## Development

### Project Commands

```bash
# Restore
dotnet restore

# Build
dotnet build

# Run
dotnet run

# Watch mode (hot reload)
dotnet watch

# Publish
dotnet publish -c Release -o ./publish
```

### Client-Side State

```csharp
// Inject conversation state
@inject ConversationState ConversationState

// Subscribe to changes
protected override void OnInitialized()
{
    ConversationState.OnChange += StateHasChanged;
}

// Update state
ConversationState.SetCurrentConversation(conversation);
```

## API Integration

### Streaming Chat

```csharp
var request = new PromptRequest<UploadItemClient>
{
    Prompt = userMessage,
    ConversationId = conversationId,
    AgentId = selectedAgentId,
    Documents = uploadedFiles
};

var responseStream = await ChatClient.InvokeStreamAsync(request);
await foreach (var chunk in responseStream)
{
    // Handle streaming response
    responseText += chunk.Text;
    StateHasChanged();
}
```

### File Upload

```csharp
var files = new List<UploadItemClient>
{
    new UploadItemClient
    {
        Name = file.Name,
        Content = new StreamContent(file.OpenReadStream())
    }
};

await ChatClient.InvokeStreamAsync(new PromptRequest<UploadItemClient>
{
    Documents = files,
    // ...
});
```

## Deployment

### Production Build

```bash
dotnet publish -c Release -o ./publish
```

### Pre-deployment Checklist

- ? Update reverse proxy to production backend URL
- ? Configure production database connection (if using auth)
- ? Set `ASPNETCORE_ENVIRONMENT=Production`
- ? Enable HTTPS with valid certificates
- ? Secure data protection keys
- ? Review CORS policies

## Troubleshooting

### Streaming Issues

Ensure SignalR is properly configured for Blazor Server connections.

### Anonymous Sessions

Sessions are stored in-memory by default. For production, consider:
- Distributed cache (Redis)
- Database-backed sessions

## Additional Resources

- [Main Solution README](../README.md)
- [Bootstrap Blazor](https://www.blazor.zone/)
- [ASP.NET Core Blazor](https://learn.microsoft.com/aspnet/core/blazor)
