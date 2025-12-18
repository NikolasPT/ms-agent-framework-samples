#region setup

#:package Spectre.Console@0.54.0
#:package Microsoft.Agents.AI@1.0.0-preview.251204.1
#:package Microsoft.Agents.AI.OpenAI@1.0.0-preview.251204.1
#:package Microsoft.Agents.AI.Workflows@1.0.0-preview.251204.1
#:package ModelContextProtocol@0.5.0-preview.1
#:package OpenAI@2.7.0
#:package Microsoft.Extensions.Configuration.UserSecrets@9.0.0

using System.ClientModel;
using Spectre.Console;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using OpenAI;

IConfigurationRoot config = new ConfigurationBuilder()
    .AddUserSecrets("ms-agent-framework-samples-secrets")
    .Build();

string OPENAI_API_KEY = config["OpenAI:ApiKey"]!;
string githubPat = config["GitHub:PAT"]!;

const string CHAT_MODEL_ID = "gpt-5-mini";
const string GITHUB_OWNER = "NikolasPT";
const string GITHUB_REPO = "demo";

#endregion setup


/*
 * Microsoft Agent Framework Demo 3 - AI Dev Team with LLM Orchestrator Agent
 * ---------------------------------------------------------------------------
 * This sample demonstrates the Group Chat orchestration pattern with an 
 * LLM-BASED ORCHESTRATOR AGENT that dynamically decides which agent speaks next.
 * 
 * Architecture:
 * ┌─────────────────────────────────────────────────────────────────┐
 * │                   ORCHESTRATOR AGENT (LLM)                      │
 * │   Uses AI reasoning to decide who speaks next based on context  │
 * │   - Routes to Analyst for initial analysis                      │
 * │   - Routes to Coder after plan is ready                         │
 * │   - Routes to Reviewer after code is written                    │
 * │   - Routes BACK to Coder if Reviewer rejects (not to Analyst!)  │
 * │   - Terminates when Reviewer approves                           │
 * └───────────────────────────────┬─────────────────────────────────┘
 *                                 │ SelectNextAgentAsync()
 *      ┌──────────────────────────┼───────────────────────┐
 *      │                          │                       │
 *      ▼                          ▼                       ▼
 * ┌──────────┐             ┌────────────┐          ┌───────────┐
 * │ Analyst  │             │  Coder     │          │ Reviewer  │
 * │  Agent   │             │  Agent     │          │  Agent    │
 * └──────────┘             └────────────┘          └───────────┘
 *      │                          │                       │
 *      └──────────────────────────┼───────────────────────┘
 *                                 │
 *                                 ▼
 * ┌─────────────────────────────────────────────────────────────────┐
 * │                    GitHub MCP Server (Remote)                   │
 * │  Tools: issue_read, get_file_contents, create_branch,           │
 * │         create_or_update_file, delete_file,                     │
 * │         pull_request_read, create_pull_request                  │
 * └─────────────────────────────────────────────────────────────────┘
 * 
 * Key Concepts Demonstrated:
 * 1. LLM-Based Orchestrator: AI agent decides routing (not hard-coded rules)
 * 2. Smart Rejection Handling: Reviewer rejection → Coder (not Analyst)
 * 3. Group Chat Orchestration: Multiple agents collaborating in rounds
 * 4. MCP Integration: GitHub tools distributed across specialized agents
 * 
 * Why use an Orchestrator Agent vs Code-Based Manager?
 * - Code-based: Predictable, fast, but requires anticipating all scenarios
 * - LLM-based: Flexible, handles ambiguity, can explain decisions
 * 
 * Reference: https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/group-chat
 * Example run: dotnet run .\demo3.cs
 */


AnsiConsole.Write(new FigletText("AI Dev Team").Color(Color.Green));
AnsiConsole.MarkupLine("[green]Group Chat + GitHub MCP Server[/]\n");

// Step 1: OpenAI
IChatClient chatClient = new OpenAIClient(new ApiKeyCredential(OPENAI_API_KEY))
    .GetChatClient(CHAT_MODEL_ID)
    .AsIChatClient();
AnsiConsole.MarkupLine("[cyan]✅ OpenAI configured[/]");

// Step 2: Connect to GitHub MCP Server
AnsiConsole.MarkupLine("[yellow]📡 Connecting to GitHub MCP Server...[/]");

HttpClientTransportOptions transportOptions = new()
{
    Name = "GitHub",
    Endpoint = new Uri("https://api.githubcopilot.com/mcp/"),
    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {githubPat}" }
};
await using HttpClientTransport transport = new(transportOptions);
await using McpClient mcpClient = await McpClient.CreateAsync(transport);
IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();

AnsiConsole.MarkupLine($"[cyan]✅ Connected - {mcpTools.Count} tools available[/]");

// Helper method to find tools
McpClientTool? Tool(string name) => mcpTools.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

// Step 3: Create Agents with GitHub MCP Tools
List<AITool> analystTools = [.. new[]
    {
        Tool("issue_read"),
        Tool("get_file_contents")
    }.Cast<AITool>()];

List<AITool> coderTools = [.. new[]
    {
        Tool("create_branch"),
        Tool("create_or_update_file"),
        Tool("delete_file"),
        Tool("get_file_contents")
    }.Cast<AITool>()];

List<AITool> reviewerTools = [.. new[]
    {
        Tool("get_file_contents"),
        Tool("pull_request_read"),
        Tool("create_pull_request")
    }.Cast<AITool>()];

// Define the context of the system being worked on, all agents share this
string systemContext = """
    Context: The system being built is a Tic Tac Toe web application using only HTML, CSS, and JavaScript.
    The whole app should run in the browser without any backend.
    The app must be contained within a single HTML file named tic-tac-toe.html.
    Never add any new files of any kind.
    Never create any new files of any kind.
    Never create any md files.
    """;

AIAgent analystAgent = chatClient.CreateAIAgent(
    name: "Analyst",
    description: "Reads GitHub issues and creates implementation plans",
    instructions: """
        You are a software analyst. 
        Read the GitHub issue using issue_read, then create a clear implementation plan. 
        Read the code context using get_file_contents as needed.
        Start responses with '📋 ANALYST:'
        You must never ask any questions. 
        You will not suggests any testing.
        You will not make any estimations on how long the work will take. 
        """ + systemContext,

    tools: analystTools);

AIAgent coderAgent = chatClient.CreateAIAgent(
    name: "Coder",
    description: "Expert frontend developer specializing in HTML, CSS, and JavaScript",
    instructions: """
        You are a SENIOR FRONTEND DEVELOPER and expert in HTML, CSS, and JavaScript.
        Start all responses with '💻 CODER:'

        WORKFLOW:
        1. Read the Analyst's implementation plan carefully
        2. Use create_branch to create a feature branch (naming: feature/<issue-number>-<short-description>)
        3. Use get_file_contents to understand existing code structure if modifying files
        4. Use create_or_update_file to implement the code
        5. Use delete_file if you created a file by mistake or need to remove a file
        6. Never ask any questions - just implement based on the plan
        """ + systemContext,

    tools: coderTools);

AIAgent reviewerAgent = chatClient.CreateAIAgent(
    name: "Reviewer",
    description: "Senior code reviewer enforcing quality standards and best practices",
    instructions: """
        You are a SENIOR CODE REVIEWER with strict quality standards.
        Start all responses with '🔍 REVIEWER:'
        
        WORKFLOW:
        1. Use pull_request_read with method 'get_diff' to see ONLY the changes made by the Coder
        2. Use pull_request_read with method 'get_files' to see the list of changed files
        3. If needed, use get_file_contents to see full file context
        4. Perform thorough review against all standards below
        5. If issues found: List specific problems with line references, request changes
        6. If code passes ALL checks: Say 'APPROVED' and use create_pull_request
        7. Never ask any questions - just review and approve or request changes
        
        ═══════════════════════════════════════════════════════════════
        LOGIC & ERROR CHECKING (Critical - must pass)
        ═══════════════════════════════════════════════════════════════
        □ No logic errors or bugs in the code
        □ All edge cases handled (null, undefined, empty arrays, zero values)
        □ Proper error handling with try/catch where needed
        □ No infinite loops or recursion without base cases
        □ Correct conditional logic (no off-by-one errors)
        □ Async operations properly awaited
        □ No race conditions or timing issues
        □ Input validation for all user-provided data
        □ No security vulnerabilities (XSS, injection, etc.)
        
        ═══════════════════════════════════════════════════════════════
        CLEAN CODE PRINCIPLES (Must follow)
        ═══════════════════════════════════════════════════════════════
        KISS (Keep It Simple, Stupid):
        □ Solutions are straightforward, not over-engineered
        □ No unnecessary complexity or clever tricks
        □ Code is easy to understand at first glance
        
        DRY (Don't Repeat Yourself):
        □ No duplicated code blocks - extract to functions
        □ Reusable components for repeated patterns
        □ Constants for repeated magic values
        
        YAGNI (You Aren't Gonna Need It):
        □ No speculative features or unused code
        □ No over-abstraction for future scenarios
        □ Only implement what's required
        
        ═══════════════════════════════════════════════════════════════
        NAMING CONVENTIONS (Strictly enforced)
        ═══════════════════════════════════════════════════════════════
        Variables & Functions:
        □ camelCase for variables and functions: userName, calculateTotal()
        □ Descriptive names that explain purpose: getUserById not gUBI
        □ Boolean variables start with is/has/can/should: isActive, hasPermission
        □ Functions start with verbs: fetchData, validateInput, renderCard
        □ Avoid abbreviations unless universally known (id, url, html)
        
        Constants:
        □ SCREAMING_SNAKE_CASE for constants: MAX_RETRIES, API_BASE_URL
        
        CSS Classes:
        □ BEM convention: .block__element--modifier
        □ Kebab-case for class names: .user-profile, .nav-item--active
        □ No camelCase or underscores alone in CSS class names
        
        Files:
        □ Kebab-case for file names: user-profile.js, main-styles.css
        □ Component files match component name: UserCard.js for UserCard component
        
        ═══════════════════════════════════════════════════════════════
        CODING GUIDELINES (Must follow)
        ═══════════════════════════════════════════════════════════════
        General:
        □ Consistent indentation (2 or 4 spaces, never tabs)
        □ Maximum line length: 100 characters
        □ One statement per line
        □ Blank line between logical sections
        □ No commented-out code (delete it)
        □ No console.log or debug statements in final code
        
        JavaScript Specific:
        □ Use const by default, let only when reassignment needed
        □ Never use var
        □ Use === and !== (strict equality)
        □ Arrow functions for callbacks: items.map(item => item.id)
        □ Template literals for string interpolation: `Hello ${name}`
        □ Destructuring where it improves readability
        □ Optional chaining (?.) and nullish coalescing (??) for safety
        
        HTML Specific:
        □ Semantic elements used appropriately
        □ Proper document structure
        □ All tags properly closed
        □ Attributes in consistent order: id, class, other attributes
        □ Double quotes for attribute values
        
        CSS Specific:
        □ Properties in consistent order (positioning → display → box model → typography → visual)
        □ One property per line
        □ Space after colon: color: red; not color:red;
        □ No unused selectors or properties
        
        ═══════════════════════════════════════════════════════════════
        REVIEW DECISION
        ═══════════════════════════════════════════════════════════════
        If ANY check fails:
        - List each issue with: [ISSUE] Description (file:line if applicable)
        - Be specific about what needs to change
        - Do NOT approve - request changes from Coder
        
        If ALL checks pass:
        - Say 'APPROVED' clearly
        - Use create_pull_request with a descriptive title and summary
        """ + systemContext,

    tools: reviewerTools);

// Step 4: Create the LLM-based Orchestrator Agent
// This agent uses AI reasoning to decide who speaks next - NOT hard-coded rules!
AIAgent orchestratorAgent = chatClient.CreateAIAgent(
    name: "Orchestrator",
    description: "Coordinates the dev team by deciding who should speak next",
    instructions: """
        You are the orchestrator for a software development team. Your ONLY job is to decide 
        which agent should speak next based on the conversation history.
        
        Available agents:
        - Analyst: Reads GitHub issues and creates implementation plans
        - Coder: Writes code based on the plan
        - Reviewer: Reviews code and creates pull requests
        
        Decision rules:
        1. If NO analysis has been done yet → select "Analyst"
        2. If Analyst has provided a plan but NO code written → select "Coder"  
        3. If Coder has written code that needs review → select "Reviewer"
        4. If Reviewer REJECTED/requested changes → select "Coder" (NOT Analyst!)
        5. If Reviewer said "APPROVED" or created a PR → respond "TERMINATE"
        
        IMPORTANT: When code is rejected, the Coder should fix it - don't restart analysis!
        
        Respond with ONLY one word: "Analyst", "Coder", "Reviewer", or "TERMINATE"
        No explanations, no punctuation, just the agent name or TERMINATE.
        """ + systemContext,

    tools: []); // Orchestrator has no tools - it only makes routing decisions

AnsiConsole.MarkupLine("[blue]✅ Analyst[/], [magenta]Coder[/], [yellow]Reviewer[/], [green]Orchestrator[/] agents created\n");

// Step 5: Build Group Chat Workflow with LLM-based Orchestrator
// The orchestrator agent is passed to the manager - it decides who speaks next using AI!
Workflow workflow = AgentWorkflowBuilder
    .CreateGroupChatBuilderWith(agents => new OrchestratorAgentManager(agents, orchestratorAgent))
    .AddParticipants(analystAgent, coderAgent, reviewerAgent)
    .Build();

// Step 6: Get GitHub Issue Number from User
int issue = AnsiConsole.Ask<int>("Issue number:");

AnsiConsole.MarkupLine("\n[green bold]🚀 Starting Group Chat with LLM Orchestrator...[/]\n");

// Step 7: Run Workflow
string task = $"Implement issue #{issue} in {GITHUB_OWNER}/{GITHUB_REPO}. Analyst reads issue, Coder implements, Reviewer approves and creates PR.";
List<ChatMessage> messages = [new(ChatRole.User, task)];

int round = 0;
string currentAgent = "";

// Start the workflow in STREAMING mode - this allows us to receive events in real-time
// as agents process, rather than waiting for the entire workflow to complete.
// InProcessExecution.StreamAsync returns a StreamingRun that we can watch for events.
StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);

// Send a TurnToken to kick off the workflow execution.
// emitEvents: true means we want to receive granular events (AgentRunUpdateEvent, etc.)
// Without this, we wouldn't get the streaming updates from each agent.
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

// WatchStreamAsync() returns an async enumerable of WorkflowEvent objects.
// This is the core streaming loop - we process events as they arrive from the workflow.
// Events include: AgentRunUpdateEvent (agent output), WorkflowOutputEvent (workflow done), etc.
await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    // AgentRunUpdateEvent fires when an agent produces output (streaming tokens, messages, etc.)
    // This is the most common event type during workflow execution.
    if (evt is AgentRunUpdateEvent update)
    {
        // ExecutorId contains the name of the agent currently running (e.g., "Analyst", "Coder")
        // We use this to detect when the workflow switches to a different agent.
        string agent = update.ExecutorId ?? "Unknown";
        
        // Color-code each agent for visual distinction in the terminal
        string color = agent.Contains("Analyst") ?  "blue" :
                       agent.Contains("Coder") ?    "magenta" :
                       agent.Contains("Reviewer") ? "yellow" : "white";
        
        // Detect agent transitions - when a new agent starts speaking, display a header
        if (agent != currentAgent)
        {
            currentAgent = agent;
            round++;

            // Display a Spectre.Console rule as a visual separator between agent turns
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[{color}]Round {round} - {agent}[/]").RuleStyle(color));
            AnsiConsole.WriteLine();
        }

        // Extract and display the actual message content from this update event.
        // AsResponse() converts the update to a ChatCompletion-style response with Messages.
        foreach (ChatMessage msg in update.AsResponse().Messages)
        {
            if (!string.IsNullOrEmpty(msg.Text))
            {
                // Markup.Escape() prevents any special Spectre markup characters in the
                // agent's output from being interpreted (e.g., [red] in code wouldn't break)
                AnsiConsole.Markup($"[{color}]{Markup.Escape(msg.Text)}[/]");
            }
        }
    }
    // WorkflowOutputEvent signals that the entire workflow has completed.
    // This fires when ShouldTerminateAsync returns true (Reviewer approved, or max iterations hit).
    else if (evt is WorkflowOutputEvent)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule("[green]✅ Complete[/]").RuleStyle("green"));
        break; // Exit the streaming loop - we're done!
    }
}

AnsiConsole.MarkupLine($"\n[dim]Completed in {round} rounds[/]\n");




/*
 * ╔═══════════════════════════════════════════════════════════════════════════════════════════════════════╗
 * ║                            OrchestratorAgentManager - Custom Group Chat Manager                       ║
 * ╠═══════════════════════════════════════════════════════════════════════════════════════════════════════╣
 * ║                                                                                                       ║
 * ║  WHY INHERIT FROM RoundRobinGroupChatManager?                                                         ║
 * ║  ─────────────────────────────────────────────                                                        ║
 * ║  The MS Agent Framework provides GroupChatManager as the abstract base class for managing             ║
 * ║  multi-agent conversations. RoundRobinGroupChatManager is the built-in concrete implementation        ║
 * ║  that provides:                                                                                       ║
 * ║                                                                                                       ║
 * ║  1. Agent list management (_agents collection)                                                        ║
 * ║  2. Iteration counting (IterationCount property)                                                      ║
 * ║  3. MaximumIterationCount enforcement (prevents infinite loops)                                       ║
 * ║  4. Default termination logic (base.ShouldTerminateAsync checks iteration limits)                     ║
 * ║  5. Reset() method for conversation restarts                                                          ║
 * ║                                                                                                       ║
 * ║  By inheriting from RoundRobinGroupChatManager, we get all this infrastructure for free and           ║
 * ║  only need to override the selection/termination logic. This is the RECOMMENDED pattern               ║
 * ║  in the official documentation for custom managers.                                                   ║
 * ║                                                                                                       ║
 * ║  Source: RoundRobinGroupChatManager.cs in microsoft/agent-framework repo                              ║
 * ║  https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.Workflows/RoundRobinGroupChatManager.cs
 * ║                                                                                                       ║
 * ║  ─────────────────────────────────────────────────────────────────────────────────────────────────    ║
 * ║                                                                                                       ║
 * ║  WHY NOT OTHER WORKFLOW PATTERNS?                                                                     ║
 * ║  ─────────────────────────────────                                                                    ║
 * ║  The MS Agent Framework offers several orchestration patterns:                                        ║
 * ║                                                                                                       ║
 * ║  • Sequential: Agents run in fixed order (A → B → C). Too rigid for our use case where                ║
 * ║    the Reviewer might reject and we need to loop back to Coder multiple times.                        ║
 * ║                                                                                                       ║
 * ║  • Concurrent: All agents run in parallel. Not suitable - our agents must run sequentially            ║
 * ║    because each depends on the previous agent's output.                                               ║
 * ║                                                                                                       ║
 * ║  • Handoff: Direct agent-to-agent transfers. Would require each agent to know about                   ║
 * ║    the next agent, creating tight coupling. Harder to add new agents or change flow.                  ║
 * ║                                                                                                       ║
 * ║  • Magentic: Complex dynamic planning with a planner agent. More overhead than needed                 ║
 * ║    for our structured dev workflow.                                                                   ║
 * ║                                                                                                       ║
 * ║  • Group Chat (this one): Centralized coordination with a manager. Perfect for iterative              ║
 * ║    refinement (code review cycles) and flexible speaker selection.                                    ║
 * ║                                                                                                       ║
 * ║  Reference: https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/group-chat#when-to-use-group-chat
 * ║                                                                                                       ║
 * ║  ─────────────────────────────────────────────────────────────────────────────────────────────────    ║
 * ║                                                                                                       ║
 * ║  THE TWO KEY OVERRIDE METHODS                                                                         ║
 * ║  ─────────────────────────────                                                                        ║
 * ║                                                                                                       ║
 * ║  GroupChatManager has two abstract/virtual methods that control the conversation flow:                ║
 * ║                                                                                                       ║
 * ║  1. SelectNextAgentAsync(history) → AIAgent  [protected internal abstract in base class]              ║
 * ║     Called by GroupChatHost to determine WHO speaks next.                                             ║
 * ║     In RoundRobinGroupChatManager: cycles through agents in order (A → B → C → A → ...)               ║
 * ║     In OUR implementation: asks an LLM to decide based on conversation context.                       ║
 * ║                                                                                                       ║
 * ║  2. ShouldTerminateAsync(history) → bool  [protected internal virtual in base class]                  ║
 * ║     Called by GroupChatHost BEFORE each turn to check if we should stop.                              ║
 * ║     In GroupChatManager base: only checks MaximumIterationCount (RoundRobin inherits this).           ║
 * ║     In OUR implementation: also checks for explicit Reviewer approval.                                ║
 * ║                                                                                                       ║
 * ║  The GroupChatHost (internal framework class) orchestrates the flow like this:                        ║
 * ║     1. Receive messages → 2. ShouldTerminateAsync? → 3. UpdateHistoryAsync → 4. SelectNextAgentAsync  ║
 * ║     → 5. IterationCount++ → 6. Run agent → repeat                                                     ║
 * ║                                                                                                       ║
 * ║  Source: GroupChatHost.cs in microsoft/agent-framework repo                                           ║
 * ║  https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.Workflows/Specialized/GroupChatHost.cs
 * ║                                                                                                       ║
 * ║  ─────────────────────────────────────────────────────────────────────────────────────────────────    ║
 * ║                                                                                                       ║
 * ║  LLM-BASED ORCHESTRATION FOR BOTH ROUTING AND TERMINATION                                             ║
 * ║  ────────────────────────────────────────────────────────                                             ║
 * ║  The Orchestrator LLM makes ALL decisions in ShouldTerminateAsync:                                    ║
 * ║                                                                                                       ║
 * ║  • ShouldTerminateAsync calls the LLM with conversation history                                       ║
 * ║    → LLM responds: "Analyst", "Coder", "Reviewer", or "TERMINATE"                                     ║
 * ║    → If "TERMINATE" → return true (stop workflow)                                                     ║
 * ║    → If agent name → cache it and return false (continue)                                             ║
 * ║                                                                                                       ║
 * ║  • SelectNextAgentAsync simply returns the cached agent                                               ║
 * ║    → No LLM call needed here - decision was already made                                              ║
 * ║    → This ensures "TERMINATE" takes effect immediately                                                ║
 * ║                                                                                                       ║
 * ║  WHY THIS DESIGN?                                                                                     ║
 * ║  The framework calls ShouldTerminateAsync BEFORE SelectNextAgentAsync.                                ║
 * ║  By moving the LLM call to ShouldTerminateAsync, termination decisions are respected immediately.     ║
 * ║                                                                                                       ║
 * ╚═══════════════════════════════════════════════════════════════════════════════════════════════════════╝
 */
class OrchestratorAgentManager : RoundRobinGroupChatManager
{
    private readonly AIAgent _orchestrator;          // The LLM agent that decides who speaks next
    private readonly IReadOnlyList<AIAgent> _agents; // All participant agents (Analyst, Coder, Reviewer)
    private readonly Dictionary<string, AIAgent> _agentsByName; // Quick lookup by name
    private AIAgent? _cachedNextAgent;               // Cached routing decision from ShouldTerminateAsync

    /// <summary>
    /// Creates a new orchestrator manager that uses an LLM agent for speaker selection.
    /// </summary>
    /// <param name="agents">The participant agents (NOT including the orchestrator itself)</param>
    /// <param name="orchestrator">The LLM agent that decides routing (has no tools, just reasoning)</param>
    /// <remarks>
    /// We pass agents to base(agents) so RoundRobinGroupChatManager tracks them.
    /// The orchestrator is separate - it's not a participant, it's the decision-maker.
    /// MaximumIterationCount = 100 prevents runaway loops if the LLM keeps cycling agents.
    /// </remarks>
    public OrchestratorAgentManager(IReadOnlyList<AIAgent> agents, AIAgent orchestrator) : base(agents)
    {
        _orchestrator = orchestrator;
        _agents = agents;
        _agentsByName = agents
            .Where(a => !string.IsNullOrEmpty(a.Name))
            .ToDictionary(a => a.Name!, StringComparer.OrdinalIgnoreCase);
        MaximumIterationCount = 100;
    }

    /// <summary>
    /// Returns the cached agent decision from ShouldTerminateAsync.
    /// </summary>
    /// <remarks>
    /// The LLM call now happens in ShouldTerminateAsync, which runs BEFORE this method.
    /// This ensures termination decisions take effect immediately.
    /// </remarks>
    protected override ValueTask<AIAgent> SelectNextAgentAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    {
        // Return the cached decision from ShouldTerminateAsync
        if (_cachedNextAgent != null)
        {
            return ValueTask.FromResult(_cachedNextAgent);
        }

        // Fallback: should never happen, but use round-robin if cache is empty
        AnsiConsole.MarkupLine($"[yellow]⚠️ No cached agent, using round-robin[/]");
        return base.SelectNextAgentAsync(history, ct);
    }

    /// <summary>
    /// Builds a prompt for the orchestrator containing conversation history and decision rules.
    /// </summary>
    /// <remarks>
    /// The prompt structure is critical for reliable LLM routing:
    /// 1. Clear role statement ("Your ONLY job is to decide...")
    /// 2. Agent descriptions with capabilities
    /// 3. Explicit decision rules for each scenario
    /// 4. Full conversation history (truncated for very long convos)
    /// 5. Clear instruction for response format
    /// 
    /// The rules here should match the orchestrator agent's instructions defined earlier.
    /// Having them in both places provides redundancy and reinforcement.
    /// </remarks>
    private static string BuildConversationSummary(IReadOnlyList<ChatMessage> history)
    {
        System.Text.StringBuilder sb = new();
        sb.AppendLine("You are the orchestrator for a software development team. Your ONLY job is to decide which agent should speak next.");
        sb.AppendLine();
        sb.AppendLine("Available agents:");
        sb.AppendLine("- Analyst: Reads GitHub issues and creates implementation plans");
        sb.AppendLine("- Coder: Writes code based on the plan");
        sb.AppendLine("- Reviewer: Reviews code and creates pull requests");
        sb.AppendLine();
        sb.AppendLine("Decision rules:");
        sb.AppendLine("1. If NO analysis has been done yet → select Analyst");
        sb.AppendLine("2. If Analyst has provided a plan but NO code written → select Coder");
        sb.AppendLine("3. If Coder has written code that needs review → select Reviewer");
        sb.AppendLine("4. If Reviewer REJECTED/requested changes → select Coder (NOT Analyst!)");
        sb.AppendLine("5. If Reviewer said APPROVED or created a PR → respond TERMINATE");
        sb.AppendLine();
        sb.AppendLine("Conversation history:");
        sb.AppendLine();

        // Include conversation history
        foreach (ChatMessage msg in history)
        {
            string author = msg.AuthorName ?? msg.Role.ToString();
            sb.AppendLine($"[{author}]: {msg.Text}");
        }

        sb.AppendLine();
        sb.AppendLine("Based on this conversation, which agent should speak next? Reply with ONLY: Analyst, Coder, Reviewer, or TERMINATE");

        return sb.ToString();
    }

    /// <summary>
    /// LLM-BASED TERMINATION: Asks the orchestrator if we should terminate or continue.
    /// </summary>
    /// <remarks>
    /// This method now calls the Orchestrator LLM to make ALL decisions:
    /// - If LLM says "TERMINATE" → return true (stop workflow)
    /// - If LLM says an agent name → cache it and return false (continue)
    /// 
    /// The cached agent is then used by SelectNextAgentAsync.
    /// This ensures the LLM's decision is respected immediately.
    /// </remarks>
    protected override async ValueTask<bool> ShouldTerminateAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    {
        // Check base class termination first (MaximumIterationCount safety limit)
        if (await base.ShouldTerminateAsync(history, ct))
        {
            return true;
        }

        // First turn optimization: always start with Analyst
        if (history.Count == 1 && history[0].Role == ChatRole.User)
        {
            _cachedNextAgent = _agents.FirstOrDefault(a => a.Name!.Contains("Analyst", StringComparison.OrdinalIgnoreCase));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[green]🤖 ORCHESTRATOR: Analyst (first turn)[/]");
            return false;
        }

        // Build prompt and ask the Orchestrator LLM
        string prompt = BuildConversationSummary(history);
        AgentThread thread = _orchestrator.GetNewThread();
        AgentRunResponse runResult = await _orchestrator.RunAsync(prompt, thread, cancellationToken: ct);
        string decision = runResult.Messages.LastOrDefault()?.Text?.Trim() ?? "";

        // Display the decision
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]🤖 ORCHESTRATOR: {Markup.Escape(decision)}[/]");
        AnsiConsole.WriteLine();

        // Check for TERMINATE
        if (decision.Contains("TERMINATE", StringComparison.OrdinalIgnoreCase))
        {
            _cachedNextAgent = null;
            return true;  // Stop the workflow
        }

        // Find and cache the next agent
        _cachedNextAgent = _agents.FirstOrDefault(a => 
            !string.IsNullOrEmpty(a.Name) && 
            decision.Contains(a.Name, StringComparison.OrdinalIgnoreCase));

        // Fallback if LLM response doesn't match any agent
        if (_cachedNextAgent == null)
        {
            AnsiConsole.MarkupLine($"[yellow]⚠️ Unclear decision '{Markup.Escape(decision)}', defaulting to Analyst[/]");
            _cachedNextAgent = _agents[0];
        }

        return false;  // Continue the workflow
    }
}
