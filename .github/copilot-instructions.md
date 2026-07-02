# GitHub Copilot Instructions

## Project Purpose

This repository is a teaching demo for the simplest possible multi-agent solution in a C# 10 console application. The code should remain small, readable, mostly deterministic, and easy to explain in a classroom, workshop, or mentoring session.

The app also demonstrates how to configure an Azure AI Foundry project connection using Azure CLI authentication, a project URL, and a model deployment name stored outside source control.

## Audience

Assume the reader is learning the multi-agent pattern for the first time. Favor clarity over cleverness. Avoid production frameworks unless the user explicitly asks to expand the sample.

## Technical Constraints

- Use C# 10 language features only.
- Keep the application as a console app.
- Keep the agent behavior deterministic unless the user explicitly asks for a model-backed agent.
- The Foundry connection setup may use Azure SDK packages. Do not add other third-party packages unless necessary for a requested lesson.
- Use `AzureCliCredential` for the local teaching path so authentication clearly comes from `az login`.
- Read the Foundry project URL from `AZURE_AI_FOUNDRY_PROJECT_URL` and the model deployment name from `MODEL_DEPLOYMENT_NAME`. These values may come from .NET user secrets or same-named environment variables.
- Do not store project endpoints, keys, tokens, or credentials in source files.
- Preserve nullable reference types.
- Keep comments extensive because this repository is designed for teaching.

## Design Guidelines

- Keep orchestration separate from agent behavior.
- Represent each agent with a small class that implements the shared `IAgent` contract.
- Pass shared state through `AgentWorkingMemory`.
- Keep Foundry configuration in `FoundryConnectionContext`, separate from individual agent responsibilities.
- Use immutable records for simple data shapes where practical.
- Add new concepts only when they teach a clear lesson.
- Keep method names direct and intention revealing.

## Style Guidelines

- Prefer simple, explicit code over abstractions that hide the teaching flow.
- Use descriptive variable names.
- Avoid one-letter variable names.
- Add comments that explain why the pattern exists, not comments that merely repeat each line of code.
- Keep console output readable and workshop friendly.

## Documentation Guidelines

- Update `README.md` when changing the scenario, agent roles, commands, or teaching points.
- Document changes to Foundry setup commands, secret names, endpoint format, or authentication behavior.
- Explain any new agent by documenting its responsibility, input, output, and place in the workflow.
- If adding tests later, document how to run them.

## What Not To Do Without Asking

- Do not convert the app to ASP.NET, worker services, Orleans, Semantic Kernel, AutoGen, LangChain, or any other framework without user approval.
- Do not add package dependencies unless they are necessary for a requested lesson.
- Do not remove the teaching comments just to make the file shorter.
- Do not replace the deterministic agent demo with a live model call unless the user asks for an AI-backed version.