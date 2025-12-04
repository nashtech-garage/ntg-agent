# NTG Agent - Admin Portal

## Project Summary

The **NTG Agent Admin Portal** is a secure, role-based Blazor web application that provides administrative capabilities for managing AI agents, documents, and knowledge bases within the NTG Agent platform. Built on .NET 10 and leveraging both Blazor Server and WebAssembly rendering modes, this application serves as the administrative interface for configuring AI agents, managing document repositories, organizing content with tags and folders, and controlling user access through role-based authorization.

The Admin Portal implements a Backend-for-Frontend (BFF) pattern using YARP reverse proxy to securely route requests to backend orchestration services while maintaining a unified authentication context across distributed services.

## Project Structure

```
NTG.Agent.Admin/
??? NTG.Agent.Admin/                    # Main server-side Blazor project
?   ??? Components/                      # Blazor components and pages
?   ?   ??? Account/                     # Identity and authentication components
?   ?   ??? Layout/                      # Layout components
?   ?   ??? Pages/                       # Application pages
?   ??? Data/                            # Data access layer
?   ?   ??? ApplicationDbContext.cs      # EF Core DbContext for Identity
?   ?   ??? ApplicationUser.cs           # Custom Identity user model
?   ?   ??? Migrations/                  # EF Core migrations
?   ??? appsettings.json                 # Application configuration
?   ??? Program.cs                       # Application entry point
?   ??? NTG.Agent.Admin.csproj
?
??? NTG.Agent.Admin.Client/              # WebAssembly client project
    ??? Services/                        # HTTP client services
    ?   ??? DocumentClient.cs            # Document management
    ?   ??? AgentClient.cs               # Agent management
    ?   ??? FolderClient.cs              # Folder management
    ?   ??? TagClient.cs                 # Tag management
    ??? _Imports.razor
    ??? NTG.Agent.Admin.Client.csproj
```

## Main Components

### Server Project (NTG.Agent.Admin)

- **ASP.NET Core Identity** - User authentication and role-based authorization
- **YARP Reverse Proxy** - Routes API requests to backend services (BFF pattern)
- **Data Protection** - Shared cookie authentication across services
- **Blazor Server & WebAssembly** - Hybrid rendering modes

### Client Project (NTG.Agent.Admin.Client)

- **HTTP Client Services** - API communication layers:
  - `DocumentClient` - Upload, download, delete documents
  - `AgentClient` - Manage AI agent configurations
  - `FolderClient` - Organize documents in folders
  - `TagClient` - Categorize content with tags

## Design Patterns Used

1. **Backend-for-Frontend (BFF)** - YARP proxy centralizes auth and routing
2. **Repository Pattern** - EF Core abstracts data access
3. **Dependency Injection** - Constructor injection throughout
4. **Service Layer** - HTTP clients encapsulate API calls
5. **Identity Pattern** - ASP.NET Core Identity for auth/authz

## Dependencies

### NuGet Packages
- Microsoft.AspNetCore.Components.WebAssembly.Server (10.0.0)
- Microsoft.AspNetCore.Identity.EntityFrameworkCore (10.0.0)
- Microsoft.EntityFrameworkCore.SqlServer (10.0.0)
- Yarp.ReverseProxy (2.3.0)
- Microsoft.Extensions.ServiceDiscovery.Yarp (10.0.0)

### Project References
- **NTG.Agent.ServiceDefaults** - Shared configuration and telemetry
- **NTG.Agent.Common** - Shared DTOs and utilities

### Backend Services (via Reverse Proxy)
- **NTG.Agent.Orchestrator** - Main API service

## How to Run

### Prerequisites
- .NET 10 SDK
- SQL Server (LocalDB or full instance)
- Visual Studio 2026 / Rider / VS Code

### Setup

1. **Configure Database Connection**

```bash
# Using User Secrets (recommended)
cd NTG.Agent.Admin
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.;Database=NTGAgent;Trusted_Connection=True;TrustServerCertificate=true"
```

2. **Apply Database Migrations**

```bash
cd NTG.Agent.Admin
dotnet ef database update
```

This creates the database with:
- Identity tables (Users, Roles, etc.)
- Seeded admin user: `admin@ntgagent.com` / `Ntg@123`
- Pre-configured roles: Admin, Anonymous

3. **Run the Application**

```bash
# Option 1: Standalone
cd NTG.Agent.Admin
dotnet run

# Option 2: Via Aspire (recommended)
cd ../NTG.Agent.AppHost
dotnet run
```

4. **Access the Admin Portal**

Navigate to `https://localhost:7001` (or port shown in console)

### Default Credentials
- **Email**: admin@ntgagent.com
- **Password**: Ntg@123

?? **Change the password immediately in production!**

## Features

### Agent Management
- Create and configure AI agents
- Set custom instructions and behavior
- Configure LLM providers (GitHub Models, Azure OpenAI, Google Gemini)
- Manage agent tools and capabilities
- Connect to MCP (Model Context Protocol) servers

### Document Management
- Upload files (PDF, Office docs, images, etc.)
- Import web pages
- Create text documents
- Organize in hierarchical folders
- Tag with role-based access control
- Preview and download documents

### User & Role Management
- Manage admin users
- Configure role-based permissions
- Control document access via tags

## Configuration

### Reverse Proxy Routes

Configure backend services in `appsettings.json`:

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

### Authentication

The application uses shared cookie authentication:
- Application name: `NTGAgent`
- Keys stored in: `../../key/` directory
- Default policy: Requires `Admin` role

## Development

### Project Commands

```bash
# Restore packages
dotnet restore

# Build
dotnet build

# Run
dotnet run

# Create migration
dotnet ef migrations add MigrationName

# Update database
dotnet ef database update
```

### WebAssembly Debugging

The app enables WASM debugging in development automatically. Use browser dev tools to debug client-side code.

## Deployment

### Production Build

```bash
dotnet publish -c Release -o ./publish
```

### Pre-deployment Checklist

- ? Update connection strings to production database
- ? Configure reverse proxy to production backend
- ? Set `ASPNETCORE_ENVIRONMENT=Production`
- ? Change default admin password
- ? Secure data protection keys
- ? Enable HTTPS with valid certificates
- ? Review authorization policies

## Additional Resources

- [Main Solution README](../README.md)
- [ASP.NET Core Blazor](https://learn.microsoft.com/aspnet/core/blazor)
- [ASP.NET Core Identity](https://learn.microsoft.com/aspnet/core/security/authentication/identity)
- [YARP Documentation](https://microsoft.github.io/reverse-proxy/)
