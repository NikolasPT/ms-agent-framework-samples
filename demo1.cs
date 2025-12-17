#region setup

#:package Spectre.Console@0.54.0
#:package Microsoft.Agents.AI@1.0.0-preview.251125.1
#:package Microsoft.Agents.AI.OpenAI@1.0.0-preview.251125.1
#:package OpenAI@2.7.0
#:package Microsoft.Extensions.Configuration.UserSecrets@9.0.0
#:package Microsoft.Extensions.Configuration.Binder@9.0.0

using System.ClientModel;
using Spectre.Console;
using Microsoft.Agents.AI;
using OpenAI;
using Microsoft.Extensions.Configuration;

// Load secrets using the CLI first:
// dotnet user-secrets set "OpenAI:ApiKey" "your-secret-key-here" --id "ms-agent-framework-samples-secrets"

var config = new ConfigurationBuilder()
    .AddUserSecrets("ms-agent-framework-samples-secrets")
    .Build();

string OPENAI_API_KEY = config["OpenAI:ApiKey"]!;
const string CHAT_MODEL_ID = "gpt-5-mini";

#endregion setup


/*
 * Microsoft Agent Framework Demo 1
 * ------------------------------
 * This sample demonstrates the core basics of the Agent Framework:
 * 
 * 1. Agent Creation: How to initialize an `AIAgent` and supply it with instructions.
 * 2. Model Configuration: Setting up the chat client with a specific model (e.g., gpt-5-mini).
 * 3. Streaming Response: Using `RunStreamingAsync` to receive and display tokens in real-time.
 * 
 * Reference: https://learn.microsoft.com/en-us/agent-framework/tutorials/agents/run-agent
 */


// Create the agent using OpenAI
AIAgent agent = new OpenAIClient(new ApiKeyCredential(OPENAI_API_KEY))
    .GetChatClient(CHAT_MODEL_ID)
    .CreateAIAgent(
        // Define agent instructions (This gives the agent a role or behavior)
        instructions: @"You are a pirate from the 1600s.
                        Answer questions in pirate speak.
                        Always refer to your parrot Polly."
    );

// Define the prompt
string prompt = "What is an AI agent?";
AnsiConsole.Markup($"\n[red]{prompt}[/]\n\n");

// Run the agent with streaming
await foreach (var update in agent.RunStreamingAsync(prompt))
{
    if (update?.ToString() is string text)
    {
        AnsiConsole.Markup($"[green]{text}[/]");
    }
}
AnsiConsole.WriteLine();
AnsiConsole.WriteLine();



