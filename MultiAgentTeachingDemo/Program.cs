using Azure.AI.Projects;
using Azure.AI.Extensions.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using OpenAI.Responses;

#pragma warning disable OPENAI001

// MultiAgentTeachingDemo
// ----------------------
// This tiny console app demonstrates the core idea behind a multi-agent system:
// one request is passed through several focused agents, and each agent adds a
// small piece of useful work to shared memory.
//
// This version intentionally uses live Azure AI Foundry model calls. That means
// the output is no longer deterministic, but the agent responsibilities remain
// small and visible: clarify the request, explore options, then recommend a plan.

UserRequest request = new(
	Audience: "software team lead",
	Question: "How can we try a one-hour daily pair programming habit?",
	Constraint: "Keep the first experiment lightweight and easy to reverse.");

FoundryConnectionContext foundryConnection = FoundryConnectionContext.Load();

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

if (!foundryConnection.IsReady)
{
	return;
}

// The coordinator owns the workflow. The agents own the specialized decisions.
// This separation is the main teaching point: orchestration and expertise are
// different responsibilities.
MultiAgentCoordinator coordinator = new(new IAgent[]
{
	new IntakeAgent(),
	new OptionsAgent(),
	new RecommendationAgent()
});

AgentWorkingMemory finalMemory;

try
{
	finalMemory = await coordinator.RunAsync(request, foundryConnection);
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
	Console.WriteLine("A live Foundry model call failed.");
	Console.WriteLine(exception.Message);
	return;
}

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
	ProjectResponsesClient? ResponsesClient,
	bool HasProjectUrl,
	bool HasModelDeploymentName,
	bool IsAuthenticated,
	string StatusMessage)
{
	private const string ProjectUrlKey = "AZURE_AI_FOUNDRY_PROJECT_URL";
	private const string ModelDeploymentNameKey = "MODEL_DEPLOYMENT_NAME";
	private const string FoundryTokenScope = "https://ai.azure.com/.default";

	public bool IsReady => HasProjectUrl && HasModelDeploymentName && IsAuthenticated;

	public async Task<string> AskModelAsync(
		string instructions,
		string input,
		CancellationToken cancellationToken = default)
	{
		if (ResponsesClient is null)
		{
			throw new InvalidOperationException("Foundry is not ready. Configure the project URL, model deployment name, and Azure CLI sign-in before running the agents.");
		}

		// Each teaching agent gets its own instruction block. The user's request
		// and previous agent turns are supplied as input so the model can build on
		// the shared working memory.
		CreateResponseOptions options = new()
		{
			Instructions = instructions
		};
		options.InputItems.Add(ResponseItem.CreateUserMessageItem(input));

		var response = await ResponsesClient.CreateResponseAsync(options, cancellationToken);
		return response.Value.GetOutputText() ?? string.Empty;
	}

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
				ResponsesClient: null,
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
				ResponsesClient: null,
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
			ProjectResponsesClient responsesClient = projectClient.ProjectOpenAIClient.GetProjectResponsesClientForModel(modelDeploymentName);

			// Requesting a token proves the local Azure CLI identity is usable without
			// printing or storing the token. The agent turns below will make the live
			// model calls through the ProjectResponsesClient.
			AccessToken accessToken = credential.GetToken(
				new TokenRequestContext(new[] { FoundryTokenScope }),
				CancellationToken.None);

			return new FoundryConnectionContext(
				ProjectUrl: projectUrl,
				ModelDeploymentName: modelDeploymentName,
				ResponsesClient: responsesClient,
				HasProjectUrl: true,
				HasModelDeploymentName: true,
				IsAuthenticated: true,
				StatusMessage:
					$"Created {projectClient.GetType().Name} for {projectUri} using model deployment '{modelDeploymentName}'. " +
					$"Azure CLI authentication succeeded with a token that expires at {accessToken.ExpiresOn:u}. " +
					"Live Foundry model calls are enabled for the three teaching agents.");
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
			ResponsesClient: null,
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

	public string DescribePriorTurns()
	{
		if (Turns.Count == 0)
		{
			return "No prior agent turns yet.";
		}

		return string.Join(
			Environment.NewLine + Environment.NewLine,
			Turns.Select(turn => $"[{turn.AgentName}] {turn.Message}"));
	}

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
// include more structured inputs and outputs, or expose telemetry. This version
// is asynchronous because every agent asks Foundry to do its specialized work.
public interface IAgent
{
	string Name { get; }

	string Goal { get; }

	Task<AgentTurn> RespondAsync(AgentWorkingMemory memory, CancellationToken cancellationToken = default);
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

	public async Task<AgentWorkingMemory> RunAsync(
		UserRequest request,
		FoundryConnectionContext foundryConnection,
		CancellationToken cancellationToken = default)
	{
		AgentWorkingMemory memory = new(request, foundryConnection, Array.Empty<AgentTurn>());

		foreach (IAgent agent in agents)
		{
			AgentTurn turn = await agent.RespondAsync(memory, cancellationToken);
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

	public async Task<AgentTurn> RespondAsync(AgentWorkingMemory memory, CancellationToken cancellationToken = default)
	{
		UserRequest request = memory.Request;
		string instructions =
			"You are the Intake Agent in a three-agent teaching demo. " +
			"Clarify the user's request, identify the main constraint, and define what success should mean. " +
			"Do not propose solutions yet. Keep the response to 3 or 4 concise sentences.";

		string input =
			$"Audience: {request.Audience}\n" +
			$"Question: {request.Question}\n" +
			$"Constraint: {request.Constraint}";

		string message = await memory.FoundryConnection.AskModelAsync(instructions, input, cancellationToken);

		return new AgentTurn(Name, Goal, message);
	}
}

// Agent 2: OptionsAgent
// ---------------------
// The options agent proposes a few candidate actions. It uses the live Foundry
// model, but its instruction block keeps the agent focused on option generation
// rather than final decision making.
public sealed class OptionsAgent : IAgent
{
	public string Name => "Options Agent";

	public string Goal => "Create a small set of practical choices.";

	public async Task<AgentTurn> RespondAsync(AgentWorkingMemory memory, CancellationToken cancellationToken = default)
	{
		UserRequest request = memory.Request;
		string instructions =
			"You are the Options Agent in a three-agent teaching demo. " +
			"Use the intake agent's clarification to create three practical options. " +
			"For each option, include one short tradeoff. Do not make the final recommendation. " +
			"Keep the response compact and easy to read in a console app.";

		string input =
			$"Original question: {request.Question}\n" +
			$"Constraint: {request.Constraint}\n\n" +
			$"Prior agent turns:\n{memory.DescribePriorTurns()}";

		string message = await memory.FoundryConnection.AskModelAsync(instructions, input, cancellationToken);

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

	public async Task<AgentTurn> RespondAsync(AgentWorkingMemory memory, CancellationToken cancellationToken = default)
	{
		UserRequest request = memory.Request;
		string instructions =
			"You are the Recommendation Agent in a three-agent teaching demo. " +
			"Read the prior agent turns, choose one path, and produce the final recommendation. " +
			"Respect the user's constraint that the experiment must stay lightweight and easy to reverse. " +
			"Include a short first step and a short review checkpoint. Keep the response under 180 words.";

		string input =
			$"Original question: {request.Question}\n" +
			$"Constraint: {request.Constraint}\n\n" +
			$"Prior agent turns:\n{memory.DescribePriorTurns()}";

		string message = await memory.FoundryConnection.AskModelAsync(instructions, input, cancellationToken);

		return new AgentTurn(Name, Goal, message);
	}
}

// The user-secrets package needs an assembly marker type. This class carries no
// behavior; it simply tells the configuration builder which project owns the
// UserSecretsId in the .csproj file.
public sealed class UserSecretsMarker
{
}
