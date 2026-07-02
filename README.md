# MultiAgentTeachingDemo

This repository contains a very small C# 10 console application that demonstrates a multi-agent solution in the simplest possible way.

The app does not call an AI model. That is intentional. The goal is to teach the structure of a multi-agent workflow before adding prompts, model calls, persistence, or distributed systems concerns.

The app can also connect to an Azure AI Foundry project. It uses Azure CLI authentication, so local development signs in with `az login`. The Foundry project URL and model deployment name are read from .NET user secrets or same-named environment variables, not from source code.

## What This Demo Teaches

A multi-agent solution can be explained as a workflow where several focused agents collaborate on one request. Each agent has a role, reads shared context, adds its own contribution, and passes the updated context forward.

This sample demonstrates four core ideas:

- A user request starts the workflow.
- A coordinator controls the order of agent execution.
- Each agent has one clear responsibility.
- Shared working memory carries context from one agent to the next.

It also demonstrates one practical Azure setup idea:

- Infrastructure configuration, such as a Foundry project URL and model deployment name, should live outside source control.

## Scenario

The demo uses one deliberately simple scenario:

> A software team lead wants to try a one-hour daily pair programming habit, but wants the first experiment to stay lightweight and easy to reverse.

The request flows through three agents:

- `IntakeAgent` clarifies the request and success criteria.
- `OptionsAgent` proposes a few lightweight choices.
- `RecommendationAgent` chooses one path and produces the final recommendation.

Those are the three separate aspects of the problem: understand it, design options, and choose a next step.

## Why The App Is Deterministic

Many real multi-agent systems use AI models, search tools, retrieval, APIs, or human approval steps. Those are useful, but they can distract from the basic pattern when someone is learning it for the first time.

This demo uses deterministic C# classes so that every run produces the same agent decision flow. That makes it easier to teach, debug, and discuss.

Once learners understand the shape, you can replace one deterministic agent with a real model-backed agent while keeping the same orchestration idea.

## Azure AI Foundry Connection

The app uses the Azure AI Projects SDK to create an `AIProjectClient` when a Foundry project URL and model deployment name are configured.

Local authentication uses `AzureCliCredential`, which means the identity comes from the Azure CLI. Sign in before running the connected version:

```powershell
az login
az account show
```

The endpoint format is:

```text
https://<resource-name>.services.ai.azure.com/api/projects/<project-name>
```

You can find this URL in the Microsoft Foundry portal for your project. Look in the project overview or the Libraries/SDK connection details for the Foundry project URL. You can find the model deployment name in the Models and endpoints area for your Foundry project.

### Store Required Values With .NET User Secrets

Use these commands from the repository root:

```powershell
dotnet user-secrets set "AZURE_AI_FOUNDRY_PROJECT_URL" "https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
dotnet user-secrets set "MODEL_DEPLOYMENT_NAME" "<your-model-deployment-name>"
az login
```

You can confirm the value is stored with:

```powershell
dotnet user-secrets list
```

User secrets are stored in your local user profile, not in this repository. They are appropriate for local development settings such as the Foundry project URL and model deployment name.

### Environment Variable Alternative

The app also supports same-named environment variables.

For the current PowerShell session:

```powershell
$env:AZURE_AI_FOUNDRY_PROJECT_URL = "https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
$env:MODEL_DEPLOYMENT_NAME = "<your-model-deployment-name>"
```

Use either .NET user secrets or environment variables. Environment variables are loaded after user secrets, so they override user secrets for the current process when both are set.

### What The Connection Does

When the endpoint is configured, the app:

1. Reads `AZURE_AI_FOUNDRY_PROJECT_URL` and `MODEL_DEPLOYMENT_NAME` from user secrets or same-named environment variables.
2. Creates an `AIProjectClient` for that endpoint.
3. Requests a token through `AzureCliCredential`, which uses your `az login` identity.
4. Prints whether the Foundry connection setup succeeded and which model deployment name is configured.

The three teaching agents still run deterministically. They do not call a model. This keeps the first lesson focused on multi-agent orchestration while still showing the correct Azure connection pattern.

## Project Structure

```text
.
|-- .github/
|   `-- copilot-instructions.md
|-- .gitignore
|-- MultiAgentTeachingDemo.csproj
|-- Program.cs
`-- README.md
```

The main demo is intentionally contained in `Program.cs`. For a larger application, these classes would normally move into separate files and possibly separate projects. Keeping them together makes the teaching flow easier to read from top to bottom.

## Requirements

- .NET SDK 8.0 or later
- Azure CLI, if you want to run the Foundry-connected path
- Access to an Azure AI Foundry project, if you want the Foundry connection to authenticate successfully
- A terminal capable of running `dotnet` commands

The project targets `net8.0` and pins `<LangVersion>10.0</LangVersion>` so the code is built as C# 10.

## Run The Demo

From the repository root, run:

```powershell
dotnet run
```

Expected output will show:

1. The original request.
2. The constraint.
3. The Foundry connection status.
4. Each agent turn.
5. The final recommendation.

## Build The Demo

```powershell
dotnet build
```

## How The Code Works

The workflow starts by creating a `UserRequest`:

```csharp
UserRequest request = new(
    Audience: "software team lead",
    Question: "How can we try a one-hour daily pair programming habit?",
    Constraint: "Keep the first experiment lightweight and easy to reverse.");
```

Then the app loads the optional Foundry connection context:

```csharp
FoundryConnectionContext foundryConnection = FoundryConnectionContext.Load();
```

Then the app creates a `MultiAgentCoordinator` with three agents:

```csharp
MultiAgentCoordinator coordinator = new(new IAgent[]
{
    new IntakeAgent(),
    new OptionsAgent(),
    new RecommendationAgent()
});
```

The coordinator calls each agent in order. Each agent receives the current `AgentWorkingMemory`, returns an `AgentTurn`, and the coordinator adds that turn back into memory.

The important teaching idea is that the coordinator does not need to know how each agent thinks. It only knows how to run agents in sequence.

## Key Types

### `UserRequest`

Represents the original request from the user or caller. Real systems might include more metadata, but this demo keeps the request small.

### `AgentTurn`

Represents one agent's contribution to the conversation or workflow.

### `AgentWorkingMemory`

Represents shared state. It contains the original request, the Foundry connection context, and all agent turns produced so far.

### `FoundryConnectionContext`

Represents Azure setup state. It reads `AZURE_AI_FOUNDRY_PROJECT_URL` and `MODEL_DEPLOYMENT_NAME` from .NET user secrets or same-named environment variables, creates an `AIProjectClient`, and verifies that Azure CLI authentication can obtain a token.

### `IAgent`

Defines the common contract for every agent. Each agent has a name, a goal, and a `Respond` method.

### `MultiAgentCoordinator`

Runs the workflow. It is intentionally simple because orchestration should be easy to see in a teaching demo.

## Teaching Notes

Use this sequence when explaining the code:

1. Start with the user request.
2. Show the Foundry connection status and explain that configuration is separate from agent behavior.
3. Show that the coordinator receives a list of agents.
4. Explain that each agent has a narrow responsibility.
5. Step through the `Run` method.
6. Show how working memory grows after each agent turn.
7. Point out that the final answer is just the last agent's turn.

This helps learners separate three concerns:

- What the user asked for.
- How infrastructure context is loaded.
- Which agents participate.
- How information moves between agents.

## How To Extend The Demo

Good next steps for a lesson are:

- Add a `RiskAgent` that identifies possible downsides.
- Add a `ReviewerAgent` that checks whether the final recommendation respects the original constraint.
- Add a simple unit test project.
- Move each agent into its own file after learners understand the single-file version.
- Replace one deterministic agent with a real AI-backed implementation that uses the configured Foundry project.

When extending the sample, keep each agent focused on one responsibility. If an agent starts doing several jobs, split it into smaller agents.

## What This Demo Is Not

This project is not a production architecture. It does not include:

- Production authentication strategy
- Authorization design
- Persistence
- Observability
- Retries
- Distributed execution
- Model selection
- Prompt management
- Tool calling
- Human approval workflows

Those topics are important, but they belong after the learner understands the basic collaboration pattern.

## Troubleshooting

If `dotnet run` fails, check the installed SDKs:

```powershell
dotnet --list-sdks
```

If no SDK is installed, install the .NET SDK and retry the build.

If the Foundry connection reports that setup is incomplete, set the required values with:

```powershell
dotnet user-secrets set "AZURE_AI_FOUNDRY_PROJECT_URL" "https://<your-resource>.services.ai.azure.com/api/projects/<your-project>"
dotnet user-secrets set "MODEL_DEPLOYMENT_NAME" "<your-model-deployment-name>"
az login
```

If the Foundry connection reports an Azure CLI authentication failure, run:

```powershell
az login
az account show
```

Also confirm that your signed-in identity has access to the Foundry project.

If the project builds but the output differs from the README, inspect `Program.cs` to see whether the scenario or agent messages were changed.