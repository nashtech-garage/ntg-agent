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
- Start the NTG.Agent.AppHost, then open the NTG.Agent.WebClient

You can read more about GitHub model at https://docs.github.com/en/github-models/use-github-models/prototyping-with-ai-models


