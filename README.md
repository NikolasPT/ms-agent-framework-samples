# Microsoft Agent Framework Samples

Hands-on demos showcasing the **Microsoft Agent Framework** - a library for building AI agents with .NET. These samples progress from basic agent creation to advanced multi-agent orchestration with external tool integration.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (required)
- OpenAI API key
- GitHub PAT (for Demo 3 only)

### Why .NET 10?

These samples use **.NET 10 file-based apps** - a new feature that lets you run `.cs` files directly without creating a project:

```powershell
dotnet run .\demo1.cs
```

### Setup API Keys

```powershell
# Set your OpenAI API key
dotnet user-secrets set "OpenAI:ApiKey" "your-openai-key" --id "ms-agent-framework-samples-secrets"

# For Demo 3: Set GitHub PAT with repo permissions
dotnet user-secrets set "GitHub:PAT" "your-github-pat" --id "ms-agent-framework-samples-secrets"
```

## The Demos

### Demo 1: Basic Agent with Streaming
**File:** [demo1.cs](demo1.cs)

The simplest starting point - create an agent and stream its response.

**Concepts:**
- `AIAgent` creation with OpenAI
- Agent instructions (system prompt)
- Streaming responses with `RunStreamingAsync`

```powershell
dotnet run .\demo1.cs
```

---

### Demo 2: Function Tools & Agent-as-Tool
**File:** [demo2.cs](demo2.cs)

Build a **Travel Advisor** with a specialist agent that calls real-world APIs.

**Concepts:**
- **Function Tools:** C# methods as agent tools (`AIFunctionFactory.Create`)
- **Agent-as-Tool:** One agent available as a tool for another (`.AsAIFunction()`)
- **Agent Composition:** Main agent delegates to specialist for real-time data

**Architecture:**
```
┌──────────────────────┐          ┌─────────────────────────────┐
│   Travel Advisor     │  calls   │    LocationExpert Agent     │
│   (Main Agent)       │ ──────►  │   ┌─────────────────────┐   │
│                      │          │   │  GetLocationInfo()  │   │
└──────────────────────┘          │   │  - REST Countries   │   │
                                  │   │  - Open-Meteo       │   │
                                  │   │  - GOV.UK Travel    │   │
                                  │   └─────────────────────┘   │
                                  └─────────────────────────────┘
```

```powershell
# Interactive mode
dotnet run .\demo2.cs

# With API tracing
Write-Output "What is the weather in Vietnam?" | dotnet run .\demo2.cs -- --trace
```

---

### Demo 3: Multi-Agent Group Chat with LLM Orchestration
**File:** [demo3.cs](demo3.cs)

An **AI Dev Team** that reads GitHub issues, writes code, and creates pull requests.

**Concepts:**
- **Group Chat Orchestration:** Multiple agents collaborating in rounds
- **LLM-Based Orchestrator:** AI decides who speaks next (not hard-coded rules)
- **MCP Integration:** GitHub tools via Model Context Protocol
- **Custom GroupChatManager:** Extending `RoundRobinGroupChatManager`

**Configuration:** Edit these constants in `demo3.cs` to point to your GitHub repo:
```csharp
const string GITHUB_OWNER = "YourUsername";
const string GITHUB_REPO = "your-repo";
```

**Architecture:**
```
                    ┌────────────────────────────────┐
                    │    ORCHESTRATOR AGENT (LLM)    │
                    │   Routes: Analyst → Coder →    │
                    │   Reviewer → (loop or done)    │
                    └───────────────┬────────────────┘
                                    │
          ┌─────────────────────────┼─────────────────────────┐
          │                         │                         │
          ▼                         ▼                         ▼
   ┌────────────┐           ┌─────────────┐           ┌─────────────┐
   │  Analyst   │           │   Coder     │           │  Reviewer   │
   │ issue_read │           │ create_file │           │ create_pr   │
   │ get_file   │           │ get_file    │           │ get_file    │
   └────────────┘           └─────────────┘           └─────────────┘
          │                         │                         │
          └─────────────────────────┴─────────────────────────┘
                                    │
                                    ▼
                    ┌────────────────────────────────┐
                    │     GitHub MCP Server          │
                    │   (Remote via HTTP transport)  │
                    └────────────────────────────────┘
```

```powershell
dotnet run .\demo3.cs
# Then enter a GitHub issue number from your repo
```

## Key Patterns

| Pattern | Demo | Description |
|---------|------|-------------|
| Streaming | 1 | Real-time token-by-token output |
| Function Tools | 2 | C# methods callable by agents |
| Agent-as-Tool | 2 | Agents delegating to other agents |
| Group Chat | 3 | Multi-agent conversation rounds |
| LLM Orchestration | 3 | AI-driven workflow routing |
| MCP Tools | 3 | External capabilities via MCP |

## References

- [Microsoft Agent Framework Documentation](https://learn.microsoft.com/en-us/agent-framework/)
- [Run an Agent Tutorial](https://learn.microsoft.com/en-us/agent-framework/tutorials/agents/run-agent)
- [Agent as Function Tool](https://learn.microsoft.com/en-us/agent-framework/tutorials/agents/agent-as-function-tool)
- [Group Chat Orchestration](https://learn.microsoft.com/en-us/agent-framework/user-guide/workflows/orchestrations/group-chat)
