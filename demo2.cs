#region setup

#:package Spectre.Console@0.54.0
#:package Microsoft.Agents.AI@1.0.0-preview.251125.1
#:package Microsoft.Agents.AI.OpenAI@1.0.0-preview.251125.1
#:package OpenAI@2.7.0
#:package Microsoft.Extensions.Configuration.UserSecrets@9.0.0
#:package Microsoft.Extensions.Configuration.Binder@9.0.0
#:package Microsoft.Extensions.Logging.Console@9.0.0

using System.ClientModel;
using System.ClientModel.Primitives;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using Spectre.Console;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

// Load secrets using the same ID you used in the CLI
IConfigurationRoot config = new ConfigurationBuilder()
    .AddUserSecrets("ms-agent-framework-samples-secrets")
    .Build();

string OPENAI_API_KEY = config["OpenAI:ApiKey"]!;
const string CHAT_MODEL_ID = "gpt-5-mini";

// Read command-line flags for optional tracing
string[] commandLineArgs = Environment.GetCommandLineArgs();
bool traceAgent = System.Array.Exists(commandLineArgs,
    a => string.Equals(a, "--trace", System.StringComparison.OrdinalIgnoreCase));

// Configure OpenAI client with pipeline logging so we can see
// the raw JSON requests and responses when verbose mode is enabled.
ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
{
    builder
        .AddConsole()
        .SetMinimumLevel(LogLevel.Debug);
});

OpenAIClientOptions clientOptions = new();
ClientLoggingOptions loggingOptions = clientOptions.ClientLoggingOptions ?? new ClientLoggingOptions();
clientOptions.ClientLoggingOptions = loggingOptions;
loggingOptions.EnableLogging = traceAgent;
// Disable the SDK's own HTTP message/body logging to avoid
// duplicative and noisy "data: {...}" streaming chunks. We rely
// on RawJsonLoggingPolicy below for pretty JSON instead.
loggingOptions.EnableMessageLogging = false;
loggingOptions.EnableMessageContentLogging = false;
loggingOptions.LoggerFactory = loggerFactory;
loggingOptions.MessageContentSizeLimit = 1024 * 1024; // 1 MB per message body

// Add a custom raw JSON logging policy so we can see nicely
// formatted HTTP request and response bodies similar to the
// Semantic Kernel RawWireLogger sample.
// Always show panels for tool calls/results, but only show raw JSON in trace mode.
clientOptions.AddPolicy(new RawJsonLoggingPolicy(showPanels: true, showRawJson: traceAgent), PipelinePosition.BeforeTransport);

#endregion setup


/*
 * Microsoft Agent Framework Demo 2 - Agent as Function Tool
 * ----------------------------------------------------------
 * This sample demonstrates advanced Agent Framework concepts:
 * 
 * 1. Function Tools: Creating C# methods that agents can call to access external data
 * 2. Specialized Agents: Building focused agents with specific capabilities
 * 3. Agent as Tool: Using one agent as a tool for another agent with .AsAIFunction()
 * 4. Agent Composition: Building complex workflows by composing multiple agents
 * 
 * Scenario: A Travel Advisor system where:
 * - Main Agent: Friendly travel advisor that helps plan trips
 * - Specialist Agent: Location expert with access to real country, weather, and travel advisory data
 * 
 * The specialist agent uses real public APIs (REST Countries, Open-Meteo, and GOV.UK Foreign 
 * Travel Advice) to provide accurate information, while the main agent focuses on conversational 
 * interaction.
 * 
 * Reference: https://learn.microsoft.com/en-us/agent-framework/tutorials/agents/agent-as-function-tool
 * Example run: Write-Output "What is the weather in Vietnam?" | dotnet run .\demo2.cs -- --trace
 * Example run: Write-Output "What should i be careful of in Vietnam?" | dotnet run .\demo2.cs -- --trace
 */

AnsiConsole.Write(new FigletText("Travel Advisor").Color(Color.Blue));
AnsiConsole.WriteLine();

OpenAIClient openAIClient = new(
    new ApiKeyCredential(OPENAI_API_KEY),
    clientOptions);

// Create the specialist agent that has access to real-world location data
// This agent is an expert in geography, weather, and travel conditions
AIAgent locationExpertAgent = openAIClient
    .GetChatClient(CHAT_MODEL_ID)
    .CreateAIAgent(
        instructions: @"
            You are a location expert. 
            When asked about any country, you must use the GetLocationInfo function to get real-time data. 
            Always call this function - never respond from memory.
            Only return data relevant to the users questions about the country.
            If the user does not ask about weather then do not include weather data in your response.
            If the user does not ask about travel advisories then do not include travel advisory data in your response.",

        name: "LocationExpert",
        description: "Provides real-time country information, current weather data, and official travel advisories by calling external APIs.",
        tools: [AIFunctionFactory.Create(GetLocationInfo)]
    );

// Create the main travel advisor agent that uses the specialist agent as a tool
// This demonstrates the agent-as-function-tool pattern
AIAgent travelAdvisorAgent = openAIClient
    .GetChatClient(CHAT_MODEL_ID)
    .CreateAIAgent(
        instructions: @"
            You are a friendly and enthusiastic travel advisor. When users ask about specific countries or travel destinations, 
            you MUST call the LocationExpert tool to get accurate, real-time information. 
            Never rely on your training data for country information - always use the LocationExpert tool first. 
            After receiving the information from LocationExpert, provide relevant, helpful travel advice.",

        tools: [locationExpertAgent.AsAIFunction()] // Convert the specialist agent to a function tool
    );


Panel infoPanel = new(
    "[white]Main Agent:[/] Travel Advisor (friendly, conversational)\n" +
    "[white]Tool Agent:[/] LocationExpert (country, weather, travel advisories)\n" +
    "[white]Tool Function:[/] GetLocationInfo(countryName) returns JSON for the LocationExpert agent to reason over.")
{
    Header = new PanelHeader("[yellow]Agent Setup[/]", Justify.Left),
    Border = BoxBorder.Rounded,
    BorderStyle = new Style(Color.Yellow),
    Padding = new Padding(1, 1, 1, 1)
};
AnsiConsole.Write(infoPanel);
AnsiConsole.WriteLine();


// Ask the user for a travel-related question instead of using a hard-coded prompt
AnsiConsole.WriteLine("Please enter your travel question and press Enter:");
AnsiConsole.Write("User: ");
string prompt = Console.ReadLine() ?? string.Empty;
AnsiConsole.WriteLine();

if (traceAgent)
{
    AnsiConsole.MarkupLine($"[dim]🎯 User prompt:[/] [white]{prompt}[/]");
    AnsiConsole.WriteLine();
}

AgentRunResponse result = await travelAdvisorAgent.RunAsync(prompt);

// Display the final response from the agent
AnsiConsole.MarkupLine("[green bold]Travel Advisor Response:[/]");
AnsiConsole.WriteLine();
AnsiConsole.Markup($"[green]{Markup.Escape(result.ToString())}[/]");
AnsiConsole.WriteLine();
AnsiConsole.WriteLine();

// Additional information for the user
AnsiConsole.Write(new Rule().RuleStyle(Style.Parse("yellow dim")));
AnsiConsole.WriteLine();
AnsiConsole.WriteLine();









// ============================================================================
// Helper Functions
// ============================================================================

// Helper function that the specialist agent will use to fetch real-world data
[Description(@"Retrieves real-time country information (capital, population, currencies, languages), 
               current weather conditions (temperature, humidity, precipitation), 
               and the latest official travel advisories for any country by calling public REST APIs.")]
[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026", Justification = "JSON serialization in script files")]
[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "AOT not used in script files")]
static async Task<string> GetLocationInfo([Description("The name of the country (e.g., 'Japan', 'Brazil', 'France')")] string countryName)
{
    using HttpClient httpClient = new();
    string[] commandLineArgs = Environment.GetCommandLineArgs();
    bool traceAgent = Array.Exists(commandLineArgs,
        a => string.Equals(a, "--trace", StringComparison.OrdinalIgnoreCase));

    AnsiConsole.MarkupLine($"[yellow bold]📡 LocationExpert Agent: Fetching data for {countryName}[/]");
    AnsiConsole.WriteLine();

    if (traceAgent)
    {
        AnsiConsole.MarkupLine("[dim yellow]   (Tool entry) LocationExpert agent has invoked GetLocationInfo; starting external API calls...[/]");
        AnsiConsole.WriteLine();
    }

    // --------------------------------------------------------------------
    // 1) REST Countries API - static reference data
    // --------------------------------------------------------------------

    // Call REST Countries API to get country information
    AnsiConsole.MarkupLine($"[yellow]   → API Call: REST Countries API[/]");

    // Show the input data used for this endpoint
    AnsiConsole.MarkupLine("[yellow]   Input for REST Countries API:[/]");
    AnsiConsole.MarkupLine($"[dim yellow]     countryName = '{countryName}'[/]");

    string countryUrl = $"https://restcountries.com/v3.1/name/{Uri.EscapeDataString(countryName)}?fullText=false";
    AnsiConsole.MarkupLine($"[dim yellow]     URL: {countryUrl}[/]");

    string countryResponseText = await httpClient.GetStringAsync(countryUrl);
    using JsonDocument countryDoc = JsonDocument.Parse(countryResponseText);

    AnsiConsole.MarkupLine($"[yellow]   ✓ Received country data[/]");

    if (countryDoc.RootElement.GetArrayLength() == 0)
    {
        return $"Could not find information for country: {countryName}";
    }

    JsonElement country = countryDoc.RootElement[0];

    // Extract key information needed for subsequent API calls
    string? officialName = country.GetProperty("name").GetProperty("official").GetString();
    string? capital = country.GetProperty("capital")[0].GetString();

    // Get coordinates for the capital city (needed for weather API)
    JsonElement capitalInfo = country.GetProperty("capitalInfo");
    JsonElement latlng = capitalInfo.GetProperty("latlng");
    double latitude = latlng[0].GetDouble();
    double longitude = latlng[1].GetDouble();

    // --------------------------------------------------------------------
    // 2) GOV.UK Foreign Travel Advice - live travel advisories
    // --------------------------------------------------------------------

    string? adviceResponseText = null;

    AnsiConsole.MarkupLine("[yellow]   → API Call: GOV.UK Foreign Travel Advice index[/]");

    string adviceIndexUrl = "https://www.gov.uk/api/content/foreign-travel-advice";
    AnsiConsole.MarkupLine($"[dim yellow]     URL: {adviceIndexUrl}[/]");

    string adviceIndexResponseText = await httpClient.GetStringAsync(adviceIndexUrl);
    using JsonDocument adviceIndexDoc = JsonDocument.Parse(adviceIndexResponseText);
    JsonElement adviceIndexRoot = adviceIndexDoc.RootElement;

    AnsiConsole.MarkupLine("[yellow]   ✓ Received travel advice index data[/]");

    if (adviceIndexRoot.TryGetProperty("links", out JsonElement linksElement) &&
        linksElement.TryGetProperty("children", out JsonElement children) &&
        children.ValueKind == JsonValueKind.Array)
    {
        string normalizedInput = countryName.Trim().ToLowerInvariant();
        string normalizedOfficial = (officialName ?? string.Empty).Trim().ToLowerInvariant();

        string? matchedSlug = null;
        string? matchedCountryName = null;

        foreach (JsonElement child in children.EnumerateArray())
        {
            if (!child.TryGetProperty("details", out JsonElement detailsElement))
            {
                continue;
            }

            if (!detailsElement.TryGetProperty("country", out JsonElement countryElement))
            {
                continue;
            }

            if (!countryElement.TryGetProperty("name", out JsonElement nameElement) ||
                !countryElement.TryGetProperty("slug", out JsonElement slugElement))
            {
                continue;
            }

            string govCountryName = nameElement.GetString() ?? string.Empty;
            string govSlug = slugElement.GetString() ?? string.Empty;
            string govCountryNameNorm = govCountryName.Trim().ToLowerInvariant();
            string govSlugNorm = govSlug.Trim().ToLowerInvariant();

            bool isMatch = normalizedInput == govCountryNameNorm ||
                           normalizedOfficial == govCountryNameNorm ||
                           normalizedInput == govSlugNorm ||
                           normalizedOfficial == govSlugNorm;

            if (!isMatch && countryElement.TryGetProperty("synonyms", out JsonElement synonymsElement) &&
                synonymsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement syn in synonymsElement.EnumerateArray())
                {
                    string? synValue = syn.GetString();
                    if (string.IsNullOrWhiteSpace(synValue))
                    {
                        continue;
                    }

                    string synNorm = synValue.Trim().ToLowerInvariant();
                    if (normalizedInput == synNorm || normalizedOfficial == synNorm)
                    {
                        isMatch = true;
                        break;
                    }
                }
            }

            if (isMatch)
            {
                matchedSlug = govSlug;
                matchedCountryName = govCountryName;
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(matchedSlug))
        {
            string adviceUrl = $"https://www.gov.uk/api/content/foreign-travel-advice/{matchedSlug}";
            AnsiConsole.MarkupLine("[yellow]   → API Call: GOV.UK Foreign Travel Advice detail[/]");
            AnsiConsole.MarkupLine($"[dim yellow]     matched country = {matchedCountryName} (slug: {matchedSlug})[/]");
            AnsiConsole.MarkupLine($"[dim yellow]     URL: {adviceUrl}[/]");

            adviceResponseText = await httpClient.GetStringAsync(adviceUrl);

            AnsiConsole.MarkupLine("[yellow]   ✓ Received travel advice detail data[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]   → No matching GOV.UK travel advice entry found for this country; skipping advisories.[/]");
        }
    }
    else
    {
        AnsiConsole.MarkupLine("[yellow]   → GOV.UK travel advice index did not contain a 'children' list; skipping advisories.[/]");
    }

    // --------------------------------------------------------------------
    // 3) Open-Meteo Weather API - live weather
    // --------------------------------------------------------------------

    AnsiConsole.MarkupLine($"[yellow]   → API Call: Open-Meteo Weather API[/]");

    // Show the input data used for this endpoint
    AnsiConsole.MarkupLine("[yellow]   Input for Open-Meteo API:[/]");
    AnsiConsole.MarkupLine($"[dim yellow]     location = {capital} (country: {officialName})[/]");
    AnsiConsole.MarkupLine($"[dim yellow]     latitude = {latitude.ToString(CultureInfo.InvariantCulture)}, longitude = {longitude.ToString(CultureInfo.InvariantCulture)}[/]");
    AnsiConsole.MarkupLine("[dim yellow]     current fields = temperature_2m, relative_humidity_2m, apparent_temperature, precipitation, weather_code, wind_speed_10m[/]");
    AnsiConsole.MarkupLine("[dim yellow]     timezone = auto[/]");

    // Call Open-Meteo API to get current weather
    string weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={latitude.ToString(CultureInfo.InvariantCulture)}&longitude={longitude.ToString(CultureInfo.InvariantCulture)}&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m&timezone=auto";

    string weatherResponseText = await httpClient.GetStringAsync(weatherUrl);

    AnsiConsole.MarkupLine($"[yellow]   ✓ Received weather data[/]");
    AnsiConsole.WriteLine();

    // Build response JSON with raw API responses only (no duplication)
    using MemoryStream stream = new();
    using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
    {
        writer.WriteStartObject();

        writer.WritePropertyName("rest_countries");
        writer.WriteRawValue(countryResponseText);

        writer.WritePropertyName("open_meteo");
        writer.WriteRawValue(weatherResponseText);

        if (!string.IsNullOrEmpty(adviceResponseText))
        {
            writer.WritePropertyName("gov_uk_travel_advice");
            writer.WriteRawValue(adviceResponseText);
        }

        writer.WriteEndObject();
    }
    string payload = Encoding.UTF8.GetString(stream.ToArray());

    if (traceAgent)
    {
        AnsiConsole.MarkupLine("[dim yellow]   (Tool exit) LocationExpert is returning raw API JSON to the agent:[/]");
        AnsiConsole.MarkupLine($"[dim yellow]     Payload contains: rest_countries, open_meteo{(adviceResponseText != null ? ", gov_uk_travel_advice" : "")}[/]");
        AnsiConsole.MarkupLine($"[dim yellow]     Total size: {payload.Length:N0} characters[/]");
        AnsiConsole.WriteLine();
    }

    if (traceAgent)
    {
        AnsiConsole.MarkupLine("[dim yellow]     Raw LocationExpert response JSON:[/]");
        AnsiConsole.WriteLine(payload);
        AnsiConsole.WriteLine();
    }

    return payload;
}



// Custom pipeline policy that logs the raw HTTP JSON request and response
// bodies going to and from OpenAI in a nicely formatted way.
sealed class RawJsonLoggingPolicy(bool showPanels, bool showRawJson, int maxBytes = 1024 * 1024) : PipelinePolicy
{
    private int _toolCallCounter = 0;
    private int _toolResultCounter = 0;
    // Track tool_call_id to function name mapping to correctly label tool results
    private readonly Dictionary<string, string> _toolCallIdToFunctionName = new();

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int index)
    {
        // Synchronously wrap the async core implementation.
        ProcessCoreAsync(message, pipeline, index, async: false).AsTask().GetAwaiter().GetResult();
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int index)
    {
        await ProcessCoreAsync(message, pipeline, index, async: true).ConfigureAwait(false);
    }

    private async ValueTask ProcessCoreAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int index, bool async)
    {
        if (showPanels || showRawJson)
        {
            LogRequest(message);
        }

        // Do NOT set message.BufferResponse here, as it would force the entire
        // streaming response to be buffered before we can iterate over it.
        // We'll conditionally buffer only non-streaming responses after we
        // inspect the Content-Type header.

        if (async)
        {
            await ProcessNextAsync(message, pipeline, index).ConfigureAwait(false);
        }
        else
        {
            ProcessNext(message, pipeline, index);
        }

        if (showPanels || showRawJson)
        {
            PipelineResponse? response = message.Response;
            if (response is null)
            {
                return;
            }

            bool isSse = false;
            if (response.Headers.TryGetValue("Content-Type", out string? contentType) &&
                contentType is not null &&
                contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                isSse = true;
            }

            // For streaming (SSE) responses, do not buffer or log the body,
            // otherwise we would consume the entire stream up-front and break
            // RunStreamingAsync. For non-streaming responses, buffer and log
            // the pretty-printed JSON body as before.
            if (!isSse)
            {
                if (async)
                {
                    await response.BufferContentAsync().ConfigureAwait(false);
                }
                else
                {
                    response.BufferContent();
                }

                LogResponse(response);
            }
            else if (showRawJson)
            {
                AnsiConsole.MarkupLine($"[dim]<< HTTP {response.Status} ({response.ReasonPhrase}) (streaming SSE, body not logged)[/]");
            }
        }
    }

    private void LogRequest(PipelineMessage message)
    {
        PipelineRequest request = message.Request;

        string method = request.Method;
        Uri? uri = request.Uri;

        if (showRawJson)
        {
            AnsiConsole.MarkupLine($"[dim]>> HTTP {method} {uri}[/]");
        }

        BinaryContent? content = request.Content;
        if (content is null)
        {
            return;
        }

        using MemoryStream ms = new();
        content.WriteTo(ms);
        byte[] bytes = ms.ToArray();
        if (bytes.Length == 0)
        {
            return;
        }

        if (bytes.Length > maxBytes)
        {
            bytes = bytes.AsSpan(0, maxBytes).ToArray();
        }

        string text = Encoding.UTF8.GetString(bytes);

        if (showRawJson)
        {
            string pretty = TryPrettyJson(text);
            AnsiConsole.MarkupLine("[dim]>> Request Body:[/]");
            AnsiConsole.WriteLine(pretty);
            AnsiConsole.WriteLine();
        }

        // Extract and display tool results in a nice formatted cyan box (always when showPanels)
        if (showPanels)
        {
            DisplayToolResultsFromRequest(text);
        }
    }

    private void LogResponse(PipelineResponse response)
    {
        // For streaming responses (Server-Sent Events), the body consists of many
        // "data: { ... }" chunks, which are already surfaced by the SDK's
        // higher-level streaming APIs. To avoid overwhelming the console,
        // skip logging those raw SSE frames here.
        if (response.Headers.TryGetValue("Content-Type", out string? contentType) &&
            contentType is not null &&
            contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (showRawJson)
        {
            AnsiConsole.MarkupLine($"[dim]<< HTTP {response.Status} ({response.ReasonPhrase})[/]");
        }

        BinaryData? contentData = response.Content;

        if (contentData is null)
        {
            return;
        }

        string text = contentData.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (text.Length > maxBytes)
        {
            text = text.Substring(0, maxBytes);
        }

        if (showRawJson)
        {
            string pretty = TryPrettyJson(text);
            AnsiConsole.MarkupLine("[dim]<< Response Body:[/]");
            AnsiConsole.WriteLine(pretty);
            AnsiConsole.WriteLine();
        }

        // Extract and display tool calls in a nice formatted box (always when showPanels)
        if (showPanels)
        {
            DisplayToolCallsFromResponse(text);
        }
    }

    private void DisplayToolCallsFromResponse(string jsonText)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(jsonText);
            JsonElement root = doc.RootElement;

            // Look for choices[0].message.tool_calls
            if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
            {
                JsonElement firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out JsonElement message) &&
                    message.TryGetProperty("tool_calls", out JsonElement toolCalls) &&
                    toolCalls.GetArrayLength() > 0)
                {
                    foreach (JsonElement toolCall in toolCalls.EnumerateArray())
                    {
                        if (toolCall.TryGetProperty("function", out JsonElement function))
                        {
                            _toolCallCounter++;
                            string functionName = function.TryGetProperty("name", out JsonElement nameEl) ? nameEl.GetString() ?? "unknown" : "unknown";
                            string arguments = function.TryGetProperty("arguments", out JsonElement argsEl) ? argsEl.GetString() ?? "{}" : "{}";
                            
                            // Store tool_call_id to function name mapping for labeling tool results correctly
                            if (toolCall.TryGetProperty("id", out JsonElement idEl))
                            {
                                string? toolCallId = idEl.GetString();
                                if (!string.IsNullOrEmpty(toolCallId))
                                {
                                    _toolCallIdToFunctionName[toolCallId] = functionName;
                                }
                            }

                            AnsiConsole.WriteLine();;
                            
                            // Determine which agent is calling which based on function name
                            // - LocationExpert function = Main Agent calling LocationExpert Agent
                            // - GetLocationInfo function = LocationExpert Agent calling GetLocationInfo tool
                            if (functionName.Contains("LocationExpert", StringComparison.OrdinalIgnoreCase))
                            {
                                AnsiConsole.MarkupLine($"[magenta bold]🔧 Main Agent → LocationExpert Agent (tool call #{_toolCallCounter})[/]");
                            }
                            else if (functionName.Contains("GetLocationInfo", StringComparison.OrdinalIgnoreCase))
                            {
                                AnsiConsole.MarkupLine($"[yellow bold]🔧 LocationExpert Agent → GetLocationInfo Function (tool call #{_toolCallCounter})[/]");
                            }
                            else
                            {
                                AnsiConsole.MarkupLine($"[magenta bold]🔧 Tool Call #{_toolCallCounter}[/]");
                            }
                            AnsiConsole.MarkupLine($"[dim magenta]   Invoking: {Markup.Escape(functionName)}[/]");

                            // Parse and display arguments
                            try
                            {
                                using JsonDocument argsDoc = JsonDocument.Parse(arguments);
                                StringBuilder argsBuilder = new();
                                foreach (JsonProperty prop in argsDoc.RootElement.EnumerateObject())
                                {
                                    argsBuilder.AppendLine($"{prop.Name}: {prop.Value}");
                                }

                                if (argsBuilder.Length > 0)
                                {
                                    AnsiConsole.MarkupLine($"[dim magenta]   Arguments:[/]");
                                    Panel argsPanel = new(argsBuilder.ToString().TrimEnd())
                                    {
                                        Header = new PanelHeader("[magenta]Tool Call Arguments[/]", Justify.Left),
                                        Border = BoxBorder.Rounded,
                                        BorderStyle = new Style(Color.Magenta),
                                        Padding = new Padding(1, 1, 1, 1)
                                    };
                                    AnsiConsole.Write(argsPanel);
                                }
                            }
                            catch
                            {
                                // If arguments aren't valid JSON, just show them raw
                                if (!string.IsNullOrWhiteSpace(arguments) && arguments != "{}")
                                {
                                    AnsiConsole.MarkupLine($"[dim magenta]   Arguments: {Markup.Escape(arguments)}[/]");
                                }
                            }

                            AnsiConsole.WriteLine();
                        }
                    }
                }
            }
        }
        catch
        {
            // Silently ignore parse errors - this is just for display purposes
        }
    }

    private void DisplayToolResultsFromRequest(string jsonText)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(jsonText);
            JsonElement root = doc.RootElement;

            // Look for messages with role: "tool" in the request
            if (root.TryGetProperty("messages", out JsonElement messages))
            {
                foreach (JsonElement msg in messages.EnumerateArray())
                {
                    if (msg.TryGetProperty("role", out JsonElement role) && role.GetString() == "tool")
                    {
                        _toolResultCounter++;
                        string resultText = msg.TryGetProperty("content", out JsonElement contentEl) ? contentEl.GetString() ?? "" : "";
                        
                        // Get the tool_call_id to determine which function returned this result
                        string toolCallId = msg.TryGetProperty("tool_call_id", out JsonElement idEl) ? idEl.GetString() ?? "" : "";
                        string functionName = "";
                        if (!string.IsNullOrEmpty(toolCallId) && _toolCallIdToFunctionName.TryGetValue(toolCallId, out string? storedName))
                        {
                            functionName = storedName;
                        }
                        
                        // Determine the correct label based on which function returned the result
                        if (functionName.Contains("GetLocationInfo", StringComparison.OrdinalIgnoreCase))
                        {
                            AnsiConsole.MarkupLine($"[yellow bold]✓ GetLocationInfo Function → LocationExpert Agent (tool result #{_toolResultCounter})[/]");
                        }
                        else if (functionName.Contains("LocationExpert", StringComparison.OrdinalIgnoreCase))
                        {
                            AnsiConsole.MarkupLine($"[cyan bold]✓ LocationExpert Agent → Main Agent (tool result #{_toolResultCounter})[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[cyan bold]✓ Tool Result #{_toolResultCounter}[/]");
                        }
                        AnsiConsole.MarkupLine($"[dim cyan]   Returned {resultText.Length:N0} characters of data[/]");

                        // Display the actual tool result content
                        if (!string.IsNullOrWhiteSpace(resultText))
                        {
                            try
                            {
                                // The content is often multiple levels of JSON escaping
                                // Keep unescaping until we get clean JSON
                                string displayContent = resultText;
                                int maxIterations = 10; // Prevent infinite loops
                                bool madeProgress = true;
                                
                                while (maxIterations-- > 0 && madeProgress)
                                {
                                    madeProgress = false;
                                    displayContent = displayContent.Trim();
                                    
                                    // First, handle escape sequences that are literal backslash characters
                                    // (e.g., {\r\n  \"key\": ... where \r\n are actual chars '\', 'r', '\', 'n')
                                    if (displayContent.Contains("\\\"") || displayContent.Contains("\\r\\n") || displayContent.Contains("\\n"))
                                    {
                                        string tryUnescaped = displayContent
                                            .Replace("\\r\\n", "\r\n")
                                            .Replace("\\n", "\n")
                                            .Replace("\\\"", "\"")
                                            .Replace("\\\\", "\\");
                                        if (tryUnescaped != displayContent)
                                        {
                                            displayContent = tryUnescaped;
                                            madeProgress = true;
                                            continue;
                                        }
                                    }
                                    
                                    // If it starts with " and the second char is { or [, strip the leading quote
                                    if (displayContent.StartsWith("\"") && displayContent.Length > 1)
                                    {
                                        string afterQuote = displayContent.Substring(1).TrimStart();
                                        if (afterQuote.StartsWith("{") || afterQuote.StartsWith("["))
                                        {
                                            // Check if it's a properly terminated JSON string (ends with ")
                                            if (displayContent.EndsWith("\""))
                                            {
                                                // It's wrapped in quotes, try to parse as JSON string
                                                try
                                                {
                                                    string? unescaped = JsonSerializer.Deserialize<string>(displayContent, StringJsonContext.Default.String);
                                                    if (unescaped != null && unescaped != displayContent)
                                                    {
                                                        displayContent = unescaped;
                                                        madeProgress = true;
                                                        continue;
                                                    }
                                                }
                                                catch { }
                                            }
                                            // If not a valid JSON string, just strip the leading quote
                                            displayContent = displayContent.Substring(1);
                                            madeProgress = true;
                                            continue;
                                        }
                                        // Try deserializing as a JSON string (might be nested)
                                        try
                                        {
                                            string? unescaped = JsonSerializer.Deserialize<string>(displayContent, StringJsonContext.Default.String);
                                            if (unescaped != null && unescaped != displayContent)
                                            {
                                                displayContent = unescaped;
                                                madeProgress = true;
                                                continue;
                                            }
                                        }
                                        catch { }
                                    }
                                    
                                    // If we get here with no progress, we're done
                                }

                                // Try to parse as JSON to extract summary and pretty-print
                                try
                                {
                                    using JsonDocument jsonDoc = JsonDocument.Parse(displayContent);
                                    JsonElement jsonRoot = jsonDoc.RootElement;

                                    // Extract key fields for a readable summary
                                    StringBuilder sb = new();
                                    
                                    // Determine summary header based on function
                                    string summaryHeader = functionName.Contains("GetLocationInfo", StringComparison.OrdinalIgnoreCase)
                                        ? "Summary of GetLocationInfo JSON seen by LocationExpert agent:"
                                        : "Summary of LocationExpert JSON seen by Main agent:";
                                    sb.AppendLine(summaryHeader);

                                    // Extract country info from rest_countries
                                    if (jsonRoot.TryGetProperty("rest_countries", out JsonElement restCountries) && 
                                        restCountries.ValueKind == JsonValueKind.Array && 
                                        restCountries.GetArrayLength() > 0)
                                    {
                                        JsonElement countryData = restCountries[0];
                                        string? countryName = null;
                                        string? capital = null;
                                        string? population = null;
                                        string? region = null;

                                        if (countryData.TryGetProperty("name", out JsonElement nameEl) && 
                                            nameEl.TryGetProperty("common", out JsonElement commonName))
                                        {
                                            countryName = commonName.GetString();
                                        }
                                        if (countryData.TryGetProperty("capital", out JsonElement capitalEl) && 
                                            capitalEl.ValueKind == JsonValueKind.Array && 
                                            capitalEl.GetArrayLength() > 0)
                                        {
                                            capital = capitalEl[0].GetString();
                                        }
                                        if (countryData.TryGetProperty("population", out JsonElement popEl))
                                        {
                                            population = popEl.GetInt64().ToString("N0", CultureInfo.InvariantCulture);
                                        }
                                        if (countryData.TryGetProperty("region", out JsonElement regionEl))
                                        {
                                            region = regionEl.GetString();
                                        }

                                        sb.AppendLine($"- Country: {countryName ?? "(unknown)"}, Capital: {capital ?? "(unknown)"}");
                                        if (!string.IsNullOrEmpty(population))
                                            sb.AppendLine($"- Population: {population}");
                                        if (!string.IsNullOrEmpty(region))
                                            sb.AppendLine($"- Region: {region}");
                                    }

                                    // Extract weather info from open_meteo
                                    if (jsonRoot.TryGetProperty("open_meteo", out JsonElement openMeteo) && 
                                        openMeteo.TryGetProperty("current", out JsonElement currentWeather))
                                    {
                                        string? temp = null;
                                        string? humidity = null;
                                        string? windSpeed = null;

                                        if (currentWeather.TryGetProperty("temperature_2m", out JsonElement tempEl))
                                        {
                                            temp = $"{tempEl.GetDouble()}°C";
                                        }
                                        if (currentWeather.TryGetProperty("relative_humidity_2m", out JsonElement humEl))
                                        {
                                            humidity = $"{humEl.GetInt32()}%";
                                        }
                                        if (currentWeather.TryGetProperty("wind_speed_10m", out JsonElement windEl))
                                        {
                                            windSpeed = $"{windEl.GetDouble()} km/h";
                                        }

                                        sb.AppendLine($"- Weather: {temp ?? "(n/a)"}, Humidity: {humidity ?? "(n/a)"}, Wind: {windSpeed ?? "(n/a)"}");
                                    }

                                    // Extract travel advisory info
                                    if (jsonRoot.TryGetProperty("gov_uk_travel_advice", out JsonElement travelAdvice))
                                    {
                                        string? advisoryTitle = null;
                                        string? lastUpdated = null;

                                        if (travelAdvice.TryGetProperty("title", out JsonElement titleEl))
                                        {
                                            advisoryTitle = titleEl.GetString();
                                        }
                                        if (travelAdvice.TryGetProperty("details", out JsonElement detailsEl) && 
                                            detailsEl.TryGetProperty("reviewed_at", out JsonElement reviewedEl))
                                        {
                                            lastUpdated = reviewedEl.GetString();
                                        }

                                        sb.AppendLine($"- Travel Advisory: {advisoryTitle ?? "(available)"}");
                                        if (!string.IsNullOrEmpty(lastUpdated))
                                            sb.AppendLine($"- Advisory last reviewed: {lastUpdated}");
                                    }

                                    // Determine the header based on the function
                                    string headerLabel = functionName.Contains("GetLocationInfo", StringComparison.OrdinalIgnoreCase)
                                        ? "GetLocationInfo Result Summary"
                                        : "LocationExpert Result Summary";
                                    
                                    // Display the summary panel
                                    Panel summaryPanel = new(sb.ToString().TrimEnd())
                                    {
                                        Header = new PanelHeader($"[cyan]{headerLabel}[/]", Justify.Left),
                                        Border = BoxBorder.Rounded,
                                        BorderStyle = new Style(Color.Cyan),
                                        Padding = new Padding(1, 1, 1, 1)
                                    };
                                    AnsiConsole.Write(summaryPanel);
                                    AnsiConsole.WriteLine();

                                    // Pretty-print the full JSON data (truncated)
                                    using MemoryStream ms = new();
                                    using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
                                    {
                                        jsonDoc.WriteTo(writer);
                                    }
                                    string formattedContent = Encoding.UTF8.GetString(ms.ToArray());

                                    // Truncate if too long
                                    if (formattedContent.Length > 16000)
                                    {
                                        formattedContent = formattedContent.Substring(0, 16000) + "\n... [truncated]";
                                    }

                                    // Determine the header based on the function
                                    string dataHeaderLabel = functionName.Contains("GetLocationInfo", StringComparison.OrdinalIgnoreCase)
                                        ? "Data Returned from GetLocationInfo"
                                        : "Data Returned from LocationExpert";

                                    Panel dataPanel = new(Markup.Escape(formattedContent))
                                    {
                                        Header = new PanelHeader($"[cyan]{dataHeaderLabel}[/]", Justify.Left),
                                        Border = BoxBorder.Rounded,
                                        BorderStyle = new Style(Color.Cyan),
                                        Padding = new Padding(1, 1, 1, 1)
                                    };
                                    AnsiConsole.Write(dataPanel);
                                }
                                catch
                                {
                                    // Not JSON or unexpected structure, show as plain text
                                    string formattedContent = displayContent;
                                    if (formattedContent.Length > 16000)
                                    {
                                        formattedContent = formattedContent.Substring(0, 16000) + "\n... [truncated]";
                                    }

                                    // Determine the header based on the function
                                    string plainHeaderLabel = functionName.Contains("GetLocationInfo", StringComparison.OrdinalIgnoreCase)
                                        ? "Data Returned from GetLocationInfo"
                                        : "Data Returned from LocationExpert";

                                    Panel dataPanel2 = new(Markup.Escape(formattedContent))
                                    {
                                        Header = new PanelHeader($"[cyan]{plainHeaderLabel}[/]", Justify.Left),
                                        Border = BoxBorder.Rounded,
                                        BorderStyle = new Style(Color.Cyan),
                                        Padding = new Padding(1, 1, 1, 1)
                                    };
                                    AnsiConsole.Write(dataPanel2);
                                }
                            }
                            catch (Exception ex)
                            {
                                // Fallback: just show the raw content length with error info
                                AnsiConsole.MarkupLine($"[dim cyan]   (parse error: {Markup.Escape(ex.Message)})[/]");
                            }
                        }

                        AnsiConsole.WriteLine();
                        
                        // Show appropriate message based on which agent will process the data
                        if (functionName.Contains("GetLocationInfo", StringComparison.OrdinalIgnoreCase))
                        {
                            AnsiConsole.MarkupLine("[dim]LocationExpert agent is now processing the data...[/]");
                        }
                        else if (functionName.Contains("LocationExpert", StringComparison.OrdinalIgnoreCase))
                        {
                            AnsiConsole.MarkupLine("[dim]Main agent is now processing the data...[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine("[dim]Agent is now processing the data...[/]");
                        }
                        AnsiConsole.WriteLine();
                    }
                }
            }
        }
        catch
        {
            // Silently ignore parse errors - this is just for display purposes
        }
    }

    private static string TryPrettyJson(string text)
    {
        using JsonDocument doc = JsonDocument.Parse(text);
        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
        {
            doc.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}

// Source-generated JSON serializer context to avoid AOT/trimming warnings (IL2026, IL3050)
[JsonSerializable(typeof(string))]
internal partial class StringJsonContext : JsonSerializerContext
{
}
