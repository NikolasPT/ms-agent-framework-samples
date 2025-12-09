#region setup

#:package Spectre.Console@0.54.0
#:package Microsoft.Agents.AI@1.0.0-preview.251125.1
#:package Microsoft.Agents.AI.OpenAI@1.0.0-preview.251125.1
#:package Azure.AI.OpenAI@2.1.0
#:package Azure.Identity@1.17.1
#:package OpenAI@2.7.0
#:package Microsoft.Extensions.Configuration.UserSecrets@9.0.0
#:package Microsoft.Extensions.Configuration.Binder@9.0.0
#:package Microsoft.Extensions.Configuration.Json@9.0.0

using System.ClientModel;
using Spectre.Console;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Models;

// Save a secret using the CLI first, example below:
// dotnet user-secrets set "AzureAIFoundry:GPT41:APIKey" "your-secret-key-here" --id "my-script-secrets-id"

// Load secrets using the same ID you used in the CLI
var config = new ConfigurationBuilder()
    .AddUserSecrets("ms-agent-framework-demo-secrets") // Secrets ID
    .Build();

string AzureAIFoundry_GPT41_APIKey = config["AzureAIFoundry:GPT41:APIKey"]!;
string AzureAIFoundry_GPT41_Endpoint = config["AzureAIFoundry:GPT41:Endpoint"]!;

#endregion setup


/*
 * Microsoft Agent Framework Demo 1
 * ------------------------------
 * This sample demonstrates the core basics of the Agent Framework:
 * 
 * 1. Agent Creation: How to initialize an `AIAgent` and supply it with instructions.
 * 2. Model Configuration: Setting up the chat client with a specific model (e.g., gpt-4.1).
 * 3. Streaming Response: Using `RunStreamingAsync` to receive and display tokens in real-time.
 * 
 * Reference: https://learn.microsoft.com/en-us/agent-framework/tutorials/agents/run-agent
 */


// Define agent instructions (This gives the agent a role or behavior)
string instructions = "You are a funny stand-up comedian. You love jokes. You will always reply with at least one joke.";

// Create the agent
AIAgent agent = new AzureOpenAIClient(
  new Uri(AzureAIFoundry_GPT41_Endpoint),
  new ApiKeyCredential(AzureAIFoundry_GPT41_APIKey))
    .GetChatClient("gpt-4.1")
    .CreateAIAgent(instructions: instructions);

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



