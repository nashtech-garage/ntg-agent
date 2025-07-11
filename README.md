# NTG Agent
This project aims to practice building a multi-agent chatbot in C#

## Tentative technologies and frameworks
- .NET 9
- .NET Aspire
- Semantic Kernel
- Azure AI Foundry
- Native Vector Support in Azure SQL Database

## Getting started

- Create your Fine-grained personal access tokens in GitHub https://github.com/settings/personal-access-tokens. The token needs to have **models:read** permissions.
- Update file secrets.json for the NTG.Agent.Orchestrator with content below Or run the cli command `dotnet user-secrets set "GitHub:Models:GitHubToken" "<your_token_here>"`. Read [this link](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) if you don't know how to set the secrets

```json
{
  "GitHub": {
    "Models": {
      "GitHubToken": "your GitHub token"
    }
  }
}
```

- In the NTG.Agent.Admin project, update the connection string if needed. Then run Update-Database if you are using Visual Studio, or dotnet ef database update if you are using the CLI.

- Repeat the same steps for the NTG.Agent.Orchestrator project.

- Start the NTG.Agent.AppHost
  - NTG.Agent.WebClient is the website for end users.
  - NTG.Agent.Admin is the website for administrators.
  - NTG.Agent.Orchestrator is the backend API.

You can read more about GitHub model at https://docs.github.com/en/github-models/use-github-models/prototyping-with-ai-models

## How authentication work

We use the shared cookies approach. In NTG.Agent.Admin, we add YARP as a BFF (Backend for Frontend), which forwards API requests to NTG.Agent.Orchestrator.
Currently, it only works for Blazor WebAssembly. Cookies are not included when the request is made from the server.

## Contributing
- Give us a star
- Reporting a bug
- Participate discussions
- Propose new features
- Submit pull requests. If you are new to GitHub, consider to [learn how to contribute to a project through forking](https://docs.github.com/en/get-started/quickstart/contributing-to-projects)

By contributing, you agree that your contributions will be licensed under Apache-2.0 license. 


