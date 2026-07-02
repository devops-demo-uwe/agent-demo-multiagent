using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

// MultiAgentTeachingDemo
// ----------------------
// This tiny console app demonstrates the core idea behind a multi-agent system:
// one request is passed through several focused agents, and each agent adds a
// small piece of useful work to shared memory.
//
// The agents themselves stay deterministic so the teaching flow is easy to
// explain. The app also shows how to connect to an Azure AI Foundry project with
// Azure CLI sign-in. That keeps the important configuration and authentication
// lesson visible without requiring a live model call for the demo to run.

UserRequest request = new(
	Audience: "software team lead",
	Question: "How can we try a one-hour daily pair programming habit?",
	Constraint: "Keep the first experiment lightweight and easy to reverse.");

FoundryConnectionContext foundryConnection = FoundryConnectionContext.Load();

// The coordinator owns the workflow. The agents own the specialized decisions.
// This separation is the main teaching point: orchestration and expertise are
// different responsibilities.
MultiAgentCoordinator coordinator = new(new IAgent[]
{
	new IntakeAgent(),
	new OptionsAgent(),
	new RecommendationAgent()
});

AgentWorkingMemory finalMemory = coordinator.Run(request, foundryConnection);

Console.WriteLine("Multi-agent teaching demo");
Console.WriteLine("=========================");
Console.WriteLine();
Console.WriteLine($"User request: {request.Question}");
Console.WriteLine($"Constraint:   {request.Constraint}");
Console.WriteLine();
Console.WriteLine("Foundry connection");
Console.WriteLine("------------------");
Console.WriteLine(foundryConnection.StatusMessage);
Console.WriteLine();

// Printing each turn makes the collaboration visible. This is useful when
// teaching because learners can see the input, the agent role, and the final
// outcome without needing a debugger.
foreach (AgentTurn turn in finalMemory.Turns)
{
	Console.WriteLine($"[{turn.AgentName}]");
	Console.WriteLine($"Goal: {turn.Goal}");
	Console.WriteLine(turn.Message);
	Console.WriteLine();
}

Console.WriteLine("Final answer");
Console.WriteLine("------------");
Console.WriteLine(finalMemory.LastMessage);

// A request is the original problem statement from the user or caller. Real
// systems often add identifiers, timestamps, authentication context, or source
// channel metadata. This demo keeps only the fields that help explain the flow.
public sealed record UserRequest(string Audience, string Question, string Constraint);

// Each agent writes one turn into shared memory. The coordinator does not need
// to know how an agent produced the message. That makes agents easy to replace.
public sealed record AgentTurn(string AgentName, string Goal, string Message);

// This record is the only place where the demo knows about Azure AI Foundry
// configuration. Keeping it separate from the agents makes the teaching point
// clear: authentication and project configuration are infrastructure concerns,
// while the agents stay focused on solving the user problem.
public sealed record FoundryConnectionContext(
	string? ProjectUrl,
	string? ModelDeploymentName,
	bool HasProjectUrl,
	bool HasModelDeploymentName,
	bool IsAuthenticated,
	string StatusMessage)
{
	private const string ProjectUrlKey = "AZURE_AI_FOUNDRY_PROJECT_URL";
	private const string ModelDeploymentNameKey = "MODEL_DEPLOYMENT_NAME";
	private const string FoundryTokenScope = "https://ai.azure.com/.default";

	public bool IsReady => HasProjectUrl && HasModelDeploymentName && IsAuthenticated;

	public static FoundryConnectionContext Load()
	{
		IConfiguration configuration = new ConfigurationBuilder()
			.AddUserSecrets<UserSecretsMarker>(optional: true)
			.AddEnvironmentVariables()
			.Build();

		string? projectUrl = configuration[ProjectUrlKey];
		string? modelDeploymentName = configuration[ModelDeploymentNameKey];

		if (string.IsNullOrWhiteSpace(projectUrl) || string.IsNullOrWhiteSpace(modelDeploymentName))
		{
			return new FoundryConnectionContext(
				ProjectUrl: projectUrl,
				ModelDeploymentName: modelDeploymentName,
				HasProjectUrl: !string.IsNullOrWhiteSpace(projectUrl),
				HasModelDeploymentName: !string.IsNullOrWhiteSpace(modelDeploymentName),
				IsAuthenticated: false,
				StatusMessage:
					"Foundry setup is incomplete. Set the required local development values with: " +
					"dotnet user-secrets set \"AZURE_AI_FOUNDRY_PROJECT_URL\" \"https://<your-resource>.services.ai.azure.com/api/projects/<your-project>\"; " +
					"dotnet user-secrets set \"MODEL_DEPLOYMENT_NAME\" \"<your-model-deployment-name>\"; " +
					"az login");
		}

		if (!Uri.TryCreate(projectUrl, UriKind.Absolute, out Uri? projectUri))
		{
			return new FoundryConnectionContext(
				ProjectUrl: projectUrl,
				ModelDeploymentName: modelDeploymentName,
				HasProjectUrl: true,
				HasModelDeploymentName: true,
				IsAuthenticated: false,
				StatusMessage:
					"AZURE_AI_FOUNDRY_PROJECT_URL was configured, but it is not a valid absolute URL. " +
					"Expected format: https://<resource-name>.services.ai.azure.com/api/projects/<project-name>");
		}

		try
		{
			// AzureCliCredential uses the identity from `az login`. That makes the
			// local development authentication path explicit for a teaching sample.
			AzureCliCredential credential = new();
			AIProjectClient projectClient = new(projectUri, credential);

			// Constructing the client proves the SDK can be configured. Requesting a
			// token proves the local Azure CLI identity is usable without printing or
			// storing the token. The agents remain deterministic and do not call a model.
			AccessToken accessToken = credential.GetToken(
				new TokenRequestContext(new[] { FoundryTokenScope }),
				CancellationToken.None);

			return new FoundryConnectionContext(
				ProjectUrl: projectUrl,
				ModelDeploymentName: modelDeploymentName,
				HasProjectUrl: true,
				HasModelDeploymentName: true,
				IsAuthenticated: true,
				StatusMessage:
					$"Created {projectClient.GetType().Name} for {projectUri} using model deployment '{modelDeploymentName}'. " +
					$"Azure CLI authentication succeeded with a token that expires at {accessToken.ExpiresOn:u}. " +
					"This demo stops before calling a model so the three-agent teaching flow stays deterministic.");
		}
		catch (CredentialUnavailableException exception)
		{
			return AuthenticationFailed(projectUrl, modelDeploymentName, "Azure CLI credentials are unavailable", exception);
		}
		catch (AuthenticationFailedException exception)
		{
			return AuthenticationFailed(projectUrl, modelDeploymentName, "Azure CLI authentication failed", exception);
		}
	}

	private static FoundryConnectionContext AuthenticationFailed(
		string projectUrl,
		string modelDeploymentName,
		string problem,
		Exception exception)
	{
		return new FoundryConnectionContext(
			ProjectUrl: projectUrl,
			ModelDeploymentName: modelDeploymentName,
			HasProjectUrl: true,
			HasModelDeploymentName: true,
			IsAuthenticated: false,
			StatusMessage:
				$"{problem}. Run az login, confirm az account show works, and make sure your identity has access to the Foundry project. " +
				$"Details: {exception.Message}");
	}
}

// Working memory is the shared state passed from agent to agent. It contains the
// original request, the Foundry connection context, and the turns produced so far.
//
// The Add method returns a new record instead of mutating the existing one. That
// immutable style makes the sequence easier to reason about in a teaching demo:
// every step receives memory, adds one turn, and returns updated memory.
public sealed record AgentWorkingMemory(
	UserRequest Request,
	FoundryConnectionContext FoundryConnection,
	IReadOnlyList<AgentTurn> Turns)
{
	public string LastMessage => Turns.Count == 0 ? string.Empty : Turns[^1].Message;

	public AgentWorkingMemory Add(AgentTurn turn)
	{
		List<AgentTurn> updatedTurns = new(Turns)
		{
			turn
		};

		return this with { Turns = updatedTurns };
	}
}

// All agents implement the same tiny contract. A larger system might make this
// asynchronous, include cancellation tokens, add structured inputs and outputs,
// or expose telemetry. The teaching version uses one method so the pattern is
// easy to read from top to bottom.
public interface IAgent
{
	string Name { get; }

	string Goal { get; }

	AgentTurn Respond(AgentWorkingMemory memory);
}

// The coordinator is intentionally boring. It does not contain the domain logic.
// It simply creates the initial memory and passes that memory through each agent
// in order. This is the simplest useful form of multi-agent orchestration.
public sealed class MultiAgentCoordinator
{
	private readonly IReadOnlyList<IAgent> agents;

	public MultiAgentCoordinator(IReadOnlyList<IAgent> agents)
	{
		this.agents = agents;
	}

	public AgentWorkingMemory Run(UserRequest request, FoundryConnectionContext foundryConnection)
	{
		AgentWorkingMemory memory = new(request, foundryConnection, Array.Empty<AgentTurn>());

		foreach (IAgent agent in agents)
		{
			AgentTurn turn = agent.Respond(memory);
			memory = memory.Add(turn);
		}

		return memory;
	}
}

// Agent 1: IntakeAgent
// --------------------
// The intake agent restates the problem and extracts the success criteria. This
// mirrors a common real-world agent responsibility: reduce ambiguity before more
// expensive or specialized work begins.
public sealed class IntakeAgent : IAgent
{
	public string Name => "Intake Agent";

	public string Goal => "Clarify the request and name the success criteria.";

	public AgentTurn Respond(AgentWorkingMemory memory)
	{
		UserRequest request = memory.Request;
		string foundryNote = memory.FoundryConnection.IsReady
			? $"The app is also authenticated to the configured Foundry project and has model deployment '{memory.FoundryConnection.ModelDeploymentName}', so a later lesson could replace one deterministic agent with a model-backed agent."
			: "The app can still run the deterministic teaching workflow while Foundry configuration or Azure CLI sign-in is completed.";

		string message =
			$"The {request.Audience} wants guidance on: '{request.Question}' " +
			$"The plan should honor this constraint: {request.Constraint} " +
			"Success means the team can try the habit without creating a large process. " +
			foundryNote;

		return new AgentTurn(Name, Goal, message);
	}
}

// Agent 2: OptionsAgent
// ---------------------
// The options agent proposes a few candidate actions. In a real app, this agent
// might search documents, compare policies, or call tools. Here it uses simple
// deterministic text so the example can run anywhere.
public sealed class OptionsAgent : IAgent
{
	public string Name => "Options Agent";

	public string Goal => "Create a small set of practical choices.";

	public AgentTurn Respond(AgentWorkingMemory memory)
	{
		string configurationOption = memory.FoundryConnection.HasProjectUrl && memory.FoundryConnection.HasModelDeploymentName
			? "The Foundry project URL and model deployment name are configured outside source control."
			: "The Foundry project URL and model deployment name still need to be supplied through user secrets or same-named environment variables.";

		string message =
			"Three lightweight options are available: " +
			"1. run a two-week opt-in pilot, " +
			"2. pair only on risky work, or " +
			"3. schedule one shared problem-solving hour each day. " +
			"The third option best matches the user's one-hour habit request. " +
			configurationOption;

		return new AgentTurn(Name, Goal, message);
	}
}

// Agent 3: RecommendationAgent
// ----------------------------
// The recommendation agent turns earlier turns into a final answer. Notice that
// it reads memory.LastMessage instead of reaching into the options agent. This
// keeps agents loosely coupled: each agent depends on shared memory, not on the
// concrete implementation details of another agent.
public sealed class RecommendationAgent : IAgent
{
	public string Name => "Recommendation Agent";

	public string Goal => "Choose one path and describe the next step.";

	public AgentTurn Respond(AgentWorkingMemory memory)
	{
		string optionsSummary = memory.LastMessage;
		string selectedOption = optionsSummary.Contains("third option", StringComparison.OrdinalIgnoreCase)
			? "the third option from the prior agent"
			: "the strongest option from the prior agent";

		string message =
			$"Recommendation: use {selectedOption}: run a two-week experiment with one daily shared " +
			"problem-solving hour. Ask for volunteers, rotate pairs, and capture " +
			"one sentence of feedback after each session. At the end, keep, tune, " +
			"or stop the habit based on whether the team reports better knowledge " +
			"sharing without slower delivery. " +
			"This keeps the three agent aspects clear: frame the problem, design options, then recommend a next step.";

		return new AgentTurn(Name, Goal, message);
	}
}

// The user-secrets package needs an assembly marker type. This class carries no
// behavior; it simply tells the configuration builder which project owns the
// UserSecretsId in the .csproj file.
public sealed class UserSecretsMarker
{
}
