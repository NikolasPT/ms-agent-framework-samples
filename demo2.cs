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
#:package Microsoft.Extensions.Logging.Console@9.0.0

using System.ClientModel;
using System.ClientModel.Primitives;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Spectre.Console;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using System.Net;

// Load secrets using the same ID you used in the CLI
var config = new ConfigurationBuilder()
    .AddUserSecrets("ms-agent-framework-samples-secrets")
    .Build();

string AzureAIFoundry_GPT41_APIKey = config["AzureAIFoundry:GPT41:APIKey"]!;
string AzureAIFoundry_GPT41_Endpoint = config["AzureAIFoundry:GPT41:Endpoint"]!;

// Read command-line flags for optional tracing
var commandLineArgs = Environment.GetCommandLineArgs();
bool traceAgent = System.Array.Exists(commandLineArgs,
    a => string.Equals(a, "--trace", System.StringComparison.OrdinalIgnoreCase));

// Configure Azure OpenAI client with pipeline logging so we can see
// the raw JSON requests and responses when verbose mode is enabled.
ILoggerFactory loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
{
    builder
        .AddConsole()
        .SetMinimumLevel(LogLevel.Debug);
});

var clientOptions = new AzureOpenAIClientOptions();
var loggingOptions = clientOptions.ClientLoggingOptions ?? new ClientLoggingOptions();
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
clientOptions.AddPolicy(new RawJsonLoggingPolicy(traceAgent), PipelinePosition.BeforeTransport);

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
 * - Specialist Agent: Location expert with access to real country and weather data
 * 
 * The specialist agent uses real public APIs (REST Countries + Open-Meteo) to provide
 * accurate information, while the main agent focuses on conversational interaction.
 * 
 * Reference: https://learn.microsoft.com/en-us/agent-framework/tutorials/agents/agent-as-function-tool
 */

AnsiConsole.Write(new FigletText("Travel Advisor").Color(Color.Blue));
AnsiConsole.WriteLine();

var azureOpenAIClient = new AzureOpenAIClient(
    new Uri(AzureAIFoundry_GPT41_Endpoint),
    new ApiKeyCredential(AzureAIFoundry_GPT41_APIKey),
    clientOptions);

// Create the specialist agent that has access to real-world location data
// This agent is an expert in geography, weather, and travel conditions
AIAgent locationExpertAgent = azureOpenAIClient
    .GetChatClient("gpt-4.1")
    .CreateAIAgent(
        instructions: @"
            You are a location expert. 
            When asked about any country, you must use the GetLocationInfo function to get real-time data. 
            Always call this function - never respond from memory.",

        name: "LocationExpert",
        description: "Provides real-time country information, current weather data, and official travel advisories by calling external APIs.",
        tools: [AIFunctionFactory.Create(GetLocationInfo)]);

AnsiConsole.MarkupLine("[cyan]✅ Location Expert Agent created with access to country and weather data[/]");

// Create the main travel advisor agent that uses the specialist agent as a tool
// This demonstrates the agent-as-function-tool pattern
AIAgent travelAdvisorAgent = azureOpenAIClient
    .GetChatClient("gpt-4.1")
    .CreateAIAgent(
        instructions: @"
            You are a friendly and enthusiastic travel advisor. When users ask about specific countries or travel destinations, 
            you MUST call the LocationExpert tool to get accurate, real-time information. 
            Never rely on your training data for country information - always use the LocationExpert tool first. 
            After receiving the information from LocationExpert, provide warm, helpful travel advice 
            based on the current weather and the latest official travel advisories.",
            
        tools: [locationExpertAgent.AsAIFunction()]); // Convert the specialist agent to a function tool

AnsiConsole.MarkupLine("[cyan]✅ Travel Advisor Agent created with LocationExpert as a tool[/]");
AnsiConsole.WriteLine();

if (traceAgent)
{
    var infoPanel = new Panel(
        "[white]Main Agent:[/] Travel Advisor (friendly, conversational)\n" +
        "[white]Tool Agent:[/] LocationExpert (country, weather, travel advisories)\n" +
        "[white]Tool Function:[/] GetLocationInfo(countryName) returned as JSON for the main agent to reason over.")
    {
        Header = new PanelHeader("[yellow]Agent Setup[/]", Justify.Left),
        Border = BoxBorder.Rounded,
        BorderStyle = new Style(Color.Yellow),
        Padding = new Padding(1, 1, 1, 1)
    };
    AnsiConsole.Write(infoPanel);
    AnsiConsole.WriteLine();
}

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

// Run the main agent with streaming to see the response in real-time
// We'll inspect each update to show when agents communicate with each other
int toolCallCounter = 0;
int toolResultCounter = 0;
int updateCounter = 0;
int streamedTokenCount = 0;
await foreach (var update in travelAdvisorAgent.RunStreamingAsync(prompt))
{
    updateCounter++;

    // First, stream any plain text returned in this update.
    // The Agent Framework populates update.Text with the text fragment
    // corresponding to this update, so writing it directly gives true
    // incremental streaming behavior.
    if (!string.IsNullOrEmpty(update.Text))
    {
        streamedTokenCount += update.Text.Length;
        AnsiConsole.Markup($"[green]{Markup.Escape(update.Text)}[/]");
    }

    // Inspect the contents of each update to detect function calls and results
    if (update.Contents != null)
    {
        foreach (var content in update.Contents)
        {
            // Detect when the main agent calls the LocationExpert agent
            if (content is FunctionCallContent functionCall)
            {
                toolCallCounter++;
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[magenta bold]🔧 Main Agent → LocationExpert Agent (tool call #{toolCallCounter})[/]");
                AnsiConsole.MarkupLine($"[dim magenta]   Invoking: {functionCall.Name}[/]");
				
                if (functionCall.Arguments != null && functionCall.Arguments.Count > 0)
                {
                    AnsiConsole.MarkupLine($"[dim magenta]   Arguments:[/]");

                    // Build a simple multi-line representation of the arguments
                    var argsBuilder = new StringBuilder();
                    foreach (var arg in functionCall.Arguments)
                    {
                        argsBuilder.AppendLine($"{arg.Key}: {arg.Value}");
                    }

                    var argsPanel = new Panel(argsBuilder.ToString().TrimEnd())
                    {
                        Header = new PanelHeader("[magenta]Tool Call Arguments[/]", Justify.Left),
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(Color.Magenta),
                        Padding = new Padding(1, 1, 1, 1)
                    };
                    AnsiConsole.Write(argsPanel);
                }
				
                AnsiConsole.WriteLine();
            }
            // Detect when the LocationExpert returns data to the main agent
            else if (content is FunctionResultContent functionResult)
            {
                toolResultCounter++;
                var resultText = functionResult.Result?.ToString() ?? "";
                var resultLength = resultText.Length;
				
                AnsiConsole.MarkupLine($"[cyan bold]✓ LocationExpert Agent → Main Agent (tool result #{toolResultCounter})[/]");
                AnsiConsole.MarkupLine($"[dim cyan]   Returned {resultLength:N0} characters of location data[/]");

                // If possible, parse and summarize key fields so you can see
                // what the main agent is reasoning over.
                if (traceAgent && !string.IsNullOrWhiteSpace(resultText))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(resultText);
                        var root = doc.RootElement;

                        string? country = root.TryGetProperty("country", out var c) ? c.GetString() : null;
                        string? capital = root.TryGetProperty("capital", out var cap) ? cap.GetString() : null;
                        string? temperature = null;
                        string? conditions = null;
                        string? advisorySource = null;

                        if (root.TryGetProperty("weather", out var weatherElement) && weatherElement.ValueKind == JsonValueKind.Object)
                        {
                            if (weatherElement.TryGetProperty("temperature", out var tEl))
                            {
                                temperature = tEl.GetString();
                            }
                            if (weatherElement.TryGetProperty("conditions", out var condEl))
                            {
                                conditions = condEl.GetString();
                            }
                        }

                        if (root.TryGetProperty("travel_advisories", out var advElement) && advElement.ValueKind == JsonValueKind.Object)
                        {
                            if (advElement.TryGetProperty("source", out var srcEl))
                            {
                                advisorySource = srcEl.GetString();
                            }
                        }

                        var sb = new StringBuilder();
                        sb.AppendLine("Summary of LocationExpert JSON seen by main agent:");
                        if (!string.IsNullOrEmpty(country) || !string.IsNullOrEmpty(capital))
                        {
                            sb.AppendLine($"- Country: {country ?? "(unknown)"}, Capital: {capital ?? "(unknown)"}");
                        }
                        if (!string.IsNullOrEmpty(temperature) || !string.IsNullOrEmpty(conditions))
                        {
                            sb.AppendLine($"- Weather: {temperature ?? "(n/a)"}, Conditions: {conditions ?? "(n/a)"}");
                        }
                        if (!string.IsNullOrEmpty(advisorySource))
                        {
                            sb.AppendLine($"- Travel advisories source: {advisorySource}");
                        }

                        var summaryPanel = new Panel(sb.ToString().TrimEnd())
                        {
                            Header = new PanelHeader("[cyan]LocationExpert Result Summary[/]", Justify.Left),
                            Border = BoxBorder.Rounded,
                            BorderStyle = new Style(Color.Cyan),
                            Padding = new Padding(1, 1, 1, 1)
                        };
                        AnsiConsole.Write(summaryPanel);
                    }
                    catch
                    {
                        // If parsing fails, just skip the structured summary and fall back to raw panel below
                    }
                }
				
                // Display a preview of the returned data
                if (resultLength > 0)
                {
                    AnsiConsole.WriteLine();
                    var panel = new Panel(Markup.Escape(resultText))
                    {
                        Header = new PanelHeader("[cyan]Data Returned from LocationExpert[/]", Justify.Left),
                        Border = BoxBorder.Rounded,
                        BorderStyle = new Style(Color.Cyan),
                        Padding = new Padding(1, 1, 1, 1)
                    };
                    AnsiConsole.Write(panel);
                }
				
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"[dim]Main agent is now processing the data...[/]");
                AnsiConsole.WriteLine();
            }
        }
    }
}

AnsiConsole.WriteLine();
AnsiConsole.WriteLine();

// End-of-run summary so you can see how much back-and-forth happened
var summaryTable = new Table().Border(TableBorder.Rounded).BorderColor(Color.Yellow);
summaryTable.AddColumn("Metric");
summaryTable.AddColumn("Value");
summaryTable.AddRow("Tool calls (Main → LocationExpert)", toolCallCounter.ToString(CultureInfo.InvariantCulture));
summaryTable.AddRow("Tool results (LocationExpert → Main)", toolResultCounter.ToString(CultureInfo.InvariantCulture));
summaryTable.AddRow("Streaming updates from main agent", updateCounter.ToString(CultureInfo.InvariantCulture));
summaryTable.AddRow("Approx. streamed text characters", streamedTokenCount.ToString("N0", CultureInfo.InvariantCulture));
AnsiConsole.MarkupLine("[yellow]Run summary:[/]");
AnsiConsole.Write(summaryTable);
AnsiConsole.WriteLine();

// Additional information for the user
AnsiConsole.Write(new Rule().RuleStyle(Style.Parse("yellow dim")));
AnsiConsole.WriteLine();
AnsiConsole.WriteLine();

// ============================================================================
// Helper Functions
// ============================================================================

// Helper function that the specialist agent will use to fetch real-world data
[Description("Retrieves real-time country information (capital, population, currencies, languages), current weather conditions (temperature, humidity, precipitation), and the latest official travel advisories for any country by calling public REST APIs.")]
[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026", Justification = "JSON serialization in script files")]
[UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "AOT not used in script files")]
static async Task<string> GetLocationInfo([Description("The name of the country (e.g., 'Japan', 'Brazil', 'France')")] string countryName)
{
    using var httpClient = new HttpClient();
    var commandLineArgs = Environment.GetCommandLineArgs();
    bool showRaw = System.Array.Exists(commandLineArgs,
        a => string.Equals(a, "--show-raw", System.StringComparison.OrdinalIgnoreCase));
    bool traceAgent = System.Array.Exists(commandLineArgs,
        a => string.Equals(a, "--trace", System.StringComparison.OrdinalIgnoreCase));
    
    void PrintPrettyJson(JsonDocument doc)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        doc.WriteTo(writer);
        writer.Flush();
        AnsiConsole.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    try
    {
        AnsiConsole.MarkupLine($"[yellow bold]📡 LocationExpert Agent: Fetching data for {countryName}[/]");
        AnsiConsole.WriteLine();

        if (traceAgent)
        {
            AnsiConsole.MarkupLine("[dim yellow]   (Tool entry) Main agent has invoked GetLocationInfo; starting external API calls...[/]");
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

        var countryUrl = $"https://restcountries.com/v3.1/name/{Uri.EscapeDataString(countryName)}?fullText=false";
        AnsiConsole.MarkupLine($"[dim yellow]     URL: {countryUrl}[/]");

        var countryResponseText = await httpClient.GetStringAsync(countryUrl);
        using var countryDoc = JsonDocument.Parse(countryResponseText);

        AnsiConsole.MarkupLine($"[yellow]   ✓ Received country data[/]");
        if (showRaw)
        {
            AnsiConsole.MarkupLine("[dim yellow]     Raw REST Countries response JSON:[/]");
            PrintPrettyJson(countryDoc);
            AnsiConsole.WriteLine();
        }

        if (countryDoc.RootElement.GetArrayLength() == 0)
        {
            return $"Could not find information for country: {countryName}";
        }

        var country = countryDoc.RootElement[0];

        // Extract key information
        var officialName = country.GetProperty("name").GetProperty("official").GetString();
        var capital = country.GetProperty("capital")[0].GetString();
        var population = country.GetProperty("population").GetInt64();
        var region = country.GetProperty("region").GetString();
        var subregion = country.GetProperty("subregion").GetString();

        // Get coordinates for the capital city
        var capitalInfo = country.GetProperty("capitalInfo");
        var latlng = capitalInfo.GetProperty("latlng");
        var latitude = latlng[0].GetDouble();
        var longitude = latlng[1].GetDouble();

        // Get currencies
        var currencies = new List<string>();
        if (country.TryGetProperty("currencies", out var currenciesObj))
        {
            foreach (var currency in currenciesObj.EnumerateObject())
            {
                currencies.Add($"{currency.Value.GetProperty("name").GetString()} ({currency.Value.GetProperty("symbol").GetString()})");
            }
        }

        // Get languages
        var languages = new List<string>();
        if (country.TryGetProperty("languages", out var languagesObj))
        {
            foreach (var language in languagesObj.EnumerateObject())
            {
                languages.Add(language.Value.GetString()!);
            }
        }

        // --------------------------------------------------------------------
        // 2) GOV.UK Foreign Travel Advice - live travel advisories
        // --------------------------------------------------------------------

        string? travelAdviceSource = null;
        string? travelAdviceUpdatedAt = null;
        string? travelAdviceLatestChange = null;
        var travelAdviceRecentChanges = new List<(string Date, string Summary)>();
        var travelAdviceParts = new List<(string Title, string Body)>();
        string? adviceResponseText = null;

        try
        {
            AnsiConsole.MarkupLine("[yellow]   → API Call: GOV.UK Foreign Travel Advice index[/]");

            var adviceIndexUrl = "https://www.gov.uk/api/content/foreign-travel-advice";
            AnsiConsole.MarkupLine($"[dim yellow]     URL: {adviceIndexUrl}[/]");

            var adviceIndexResponseText = await httpClient.GetStringAsync(adviceIndexUrl);
            using var adviceIndexDoc = JsonDocument.Parse(adviceIndexResponseText);
            var adviceIndexRoot = adviceIndexDoc.RootElement;

            AnsiConsole.MarkupLine("[yellow]   ✓ Received travel advice index data[/]");
            if (showRaw)
            {
                AnsiConsole.MarkupLine("[dim yellow]     Raw GOV.UK index response JSON:[/]");
                PrintPrettyJson(adviceIndexDoc);
                AnsiConsole.WriteLine();
            }

            JsonElement children;
            if (adviceIndexRoot.TryGetProperty("links", out var linksElement) &&
                linksElement.TryGetProperty("children", out children) &&
                children.ValueKind == JsonValueKind.Array)
            {
                string normalizedInput = countryName.Trim().ToLowerInvariant();
                string normalizedOfficial = (officialName ?? string.Empty).Trim().ToLowerInvariant();

                string? matchedSlug = null;
                string? matchedCountryName = null;

                foreach (var child in children.EnumerateArray())
                {
                    if (!child.TryGetProperty("details", out var detailsElement))
                    {
                        continue;
                    }

                    if (!detailsElement.TryGetProperty("country", out var countryElement))
                    {
                        continue;
                    }

                    if (!countryElement.TryGetProperty("name", out var nameElement) ||
                        !countryElement.TryGetProperty("slug", out var slugElement))
                    {
                        continue;
                    }

                    var govCountryName = nameElement.GetString() ?? string.Empty;
                    var govSlug = slugElement.GetString() ?? string.Empty;
                    var govCountryNameNorm = govCountryName.Trim().ToLowerInvariant();
                    var govSlugNorm = govSlug.Trim().ToLowerInvariant();

                    bool isMatch = normalizedInput == govCountryNameNorm ||
                                   normalizedOfficial == govCountryNameNorm ||
                                   normalizedInput == govSlugNorm ||
                                   normalizedOfficial == govSlugNorm;

                    if (!isMatch && countryElement.TryGetProperty("synonyms", out var synonymsElement) &&
                        synonymsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var syn in synonymsElement.EnumerateArray())
                        {
                            var synValue = syn.GetString();
                            if (string.IsNullOrWhiteSpace(synValue))
                            {
                                continue;
                            }

                            var synNorm = synValue.Trim().ToLowerInvariant();
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
                    var adviceUrl = $"https://www.gov.uk/api/content/foreign-travel-advice/{matchedSlug}";
                    AnsiConsole.MarkupLine("[yellow]   → API Call: GOV.UK Foreign Travel Advice detail[/]");
                    AnsiConsole.MarkupLine($"[dim yellow]     matched country = {matchedCountryName} (slug: {matchedSlug})[/]");
                    AnsiConsole.MarkupLine($"[dim yellow]     URL: {adviceUrl}[/]");

                    adviceResponseText = await httpClient.GetStringAsync(adviceUrl);
                    using var adviceDoc = JsonDocument.Parse(adviceResponseText);
                    var adviceRoot = adviceDoc.RootElement;

                    AnsiConsole.MarkupLine("[yellow]   ✓ Received travel advice detail data[/]");
                    if (showRaw)
                    {
                        AnsiConsole.MarkupLine("[dim yellow]     Raw GOV.UK detail response JSON:[/]");
                        PrintPrettyJson(adviceDoc);
                        AnsiConsole.WriteLine();
                    }

                    travelAdviceSource = "UK Foreign, Commonwealth & Development Office (FCDO) travel advice";

                    if (adviceRoot.TryGetProperty("updated_at", out var updatedAtElement) &&
                        updatedAtElement.ValueKind == JsonValueKind.String)
                    {
                        var updatedAtRaw = updatedAtElement.GetString();
                        if (!string.IsNullOrWhiteSpace(updatedAtRaw))
                        {
                            travelAdviceUpdatedAt = updatedAtRaw;
                        }
                    }

                    if (adviceRoot.TryGetProperty("details", out var detailsRoot))
                    {
                        if (detailsRoot.TryGetProperty("change_description", out var changeDescElement) &&
                            changeDescElement.ValueKind == JsonValueKind.String)
                        {
                            travelAdviceLatestChange = changeDescElement.GetString();
                        }

                        if (detailsRoot.TryGetProperty("change_history", out var historyElement) &&
                            historyElement.ValueKind == JsonValueKind.Array)
                        {
                            int count = 0;
                            foreach (var entry in historyElement.EnumerateArray())
                            {
                                if (count >= 3)
                                {
                                    break;
                                }

                                if (!entry.TryGetProperty("note", out var noteElement) ||
                                    noteElement.ValueKind != JsonValueKind.String)
                                {
                                    continue;
                                }

                                var note = noteElement.GetString() ?? string.Empty;
                                string dateString = string.Empty;

                                if (entry.TryGetProperty("public_timestamp", out var tsElement) &&
                                    tsElement.ValueKind == JsonValueKind.String)
                                {
                                    var tsRaw = tsElement.GetString();
                                    if (!string.IsNullOrWhiteSpace(tsRaw) &&
                                        DateTime.TryParse(tsRaw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
                                    {
                                        dateString = dt.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
                                    }
                                    else if (!string.IsNullOrWhiteSpace(tsRaw))
                                    {
                                        dateString = tsRaw;
                                    }
                                }

                                travelAdviceRecentChanges.Add((string.IsNullOrEmpty(dateString) ? string.Empty : dateString, note));
                                count++;
                            }
                        }

                        if (detailsRoot.TryGetProperty("parts", out var partsElement) &&
                            partsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var part in partsElement.EnumerateArray())
                            {
                                var title = part.GetProperty("title").GetString() ?? "";
                                var body = part.GetProperty("body").GetString() ?? "";
                                // Strip HTML
                                body = Regex.Replace(body, "<.*?>", " ");
                                body = WebUtility.HtmlDecode(body);
                                // Normalize whitespace
                                body = Regex.Replace(body, "\\s+", " ").Trim();
                                
                                travelAdviceParts.Add((title, body));
                            }
                        }
                    }
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
        }
        catch (Exception exAdvice)
        {
            AnsiConsole.MarkupLine($"[yellow]   → Warning: Failed to fetch or parse GOV.UK travel advice: {exAdvice.Message}[/]");
            AnsiConsole.WriteLine($"   Stack trace: {exAdvice.StackTrace}");
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
        var weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={latitude.ToString(CultureInfo.InvariantCulture)}&longitude={longitude.ToString(CultureInfo.InvariantCulture)}&current=temperature_2m,relative_humidity_2m,apparent_temperature,precipitation,weather_code,wind_speed_10m&timezone=auto";

        var weatherResponseText = await httpClient.GetStringAsync(weatherUrl);
        using var weatherDoc = JsonDocument.Parse(weatherResponseText);
        var weatherResponse = weatherDoc.RootElement;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]   ✓ Received weather data[/]");
        if (showRaw)
        {
            AnsiConsole.MarkupLine("[dim yellow]     Raw Open-Meteo response JSON:[/]");
            PrintPrettyJson(weatherDoc);
            AnsiConsole.WriteLine();
        }

        // Parse weather data in a robust way
        // The Open-Meteo API may return either a single object or
        // an array of location objects. Handle both shapes.
        if (weatherResponse.ValueKind == JsonValueKind.Array)
        {
            if (weatherResponse.GetArrayLength() == 0)
            {
                AnsiConsole.MarkupLine("[red]   ✗ Weather API returned an empty array[/]");
                AnsiConsole.WriteLine($"   Weather response: {weatherResponse.GetRawText()}");
                throw new InvalidOperationException("Weather API returned an empty array.");
            }

            weatherResponse = weatherResponse[0];
        }
        else if (weatherResponse.ValueKind != JsonValueKind.Object)
        {
            AnsiConsole.MarkupLine("[red]   ✗ Unexpected weather API response format (root is not an object or array)[/]");
            AnsiConsole.WriteLine($"   Weather response: {weatherResponse.GetRawText()}");
            throw new InvalidOperationException("Unexpected weather API response format.");
        }

        // Optional metadata used for nicer formatting
        string? timezone = null;
        string? timezoneAbbreviation = null;
        if (weatherResponse.TryGetProperty("timezone", out var tzElement))
        {
            timezone = tzElement.GetString();
        }
        if (weatherResponse.TryGetProperty("timezone_abbreviation", out var tzAbbrevElement))
        {
            timezoneAbbreviation = tzAbbrevElement.GetString();
        }

        if (!weatherResponse.TryGetProperty("current", out var currentElement))
        {
            AnsiConsole.MarkupLine("[red]   ✗ Weather API response does not contain 'current' property[/]");
            AnsiConsole.WriteLine($"   Weather response: {weatherResponse.GetRawText()}");
            throw new InvalidOperationException("Weather API response missing 'current' property.");
        }

        JsonElement current = currentElement;
        if (current.ValueKind == JsonValueKind.Array)
        {
            if (current.GetArrayLength() == 0)
            {
                AnsiConsole.MarkupLine("[red]   ✗ Weather API 'current' array is empty[/]");
                AnsiConsole.WriteLine($"   Weather response: {weatherResponse.GetRawText()}");
                throw new InvalidOperationException("Weather API 'current' array is empty.");
            }

            current = current[0];
        }

        if (current.ValueKind != JsonValueKind.Object)
        {
            AnsiConsole.MarkupLine("[red]   ✗ Weather API 'current' value is not an object[/]");
            AnsiConsole.WriteLine($"   Weather response: {weatherResponse.GetRawText()}");
            throw new InvalidOperationException("Weather API 'current' value is not an object.");
        }

        var temperature = current.GetProperty("temperature_2m").GetDouble();
        var feelsLike = current.GetProperty("apparent_temperature").GetDouble();
        var humidity = current.GetProperty("relative_humidity_2m").GetInt32();
        var windSpeed = current.GetProperty("wind_speed_10m").GetDouble();
        var precipitation = current.GetProperty("precipitation").GetDouble();

        // Build comprehensive response JSON without reflection-based serialization
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();

            writer.WriteString("country", officialName);
            writer.WriteString("capital", capital);
            writer.WriteString("region", $"{region} - {subregion}");
            writer.WriteString("population", population.ToString("N0", CultureInfo.InvariantCulture));
            writer.WriteString("currencies", string.Join(", ", currencies));
            writer.WriteString("languages", string.Join(", ", languages));

            writer.WritePropertyName("coordinates");
            writer.WriteStartObject();
            writer.WriteNumber("latitude", latitude);
            writer.WriteNumber("longitude", longitude);
            writer.WriteEndObject();

            writer.WritePropertyName("weather");
            writer.WriteStartObject();

            // Rounded, consistently formatted values
            var roundedTemp = Math.Round(temperature, 1);
            var roundedFeelsLike = Math.Round(feelsLike, 1);
            var roundedWind = Math.Round(windSpeed, 1);
            var roundedPrecip = Math.Round(precipitation, 1);

            writer.WriteString("temperature", $"{roundedTemp}°C");
            writer.WriteString("feels_like", $"{roundedFeelsLike}°C");
            writer.WriteString("humidity", $"{humidity}%");
            writer.WriteString("wind_speed", $"{roundedWind} km/h");
            writer.WriteString("precipitation", $"{roundedPrecip} mm");
            writer.WriteString("conditions", GetWeatherDescription(current.GetProperty("weather_code").GetInt32()));

            if (!string.IsNullOrEmpty(timezone))
            {
                writer.WriteString("timezone", timezone);
            }

            if (!string.IsNullOrEmpty(timezoneAbbreviation))
            {
                writer.WriteString("timezone_abbreviation", timezoneAbbreviation);
            }

            writer.WriteEndObject();

            // Optional: travel advisories from GOV.UK FCDO
            if (!string.IsNullOrEmpty(travelAdviceLatestChange) ||
                !string.IsNullOrEmpty(travelAdviceUpdatedAt) ||
                travelAdviceRecentChanges.Count > 0)
            {
                writer.WritePropertyName("travel_advisories");
                writer.WriteStartObject();

                if (!string.IsNullOrEmpty(travelAdviceSource))
                {
                    writer.WriteString("source", travelAdviceSource);
                }

                if (!string.IsNullOrEmpty(travelAdviceUpdatedAt))
                {
                    writer.WriteString("updated_at", travelAdviceUpdatedAt);
                }

                if (!string.IsNullOrEmpty(travelAdviceLatestChange))
                {
                    writer.WriteString("latest_change", travelAdviceLatestChange);
                }

                if (travelAdviceRecentChanges.Count > 0)
                {
                    writer.WritePropertyName("recent_changes");
                    writer.WriteStartArray();

                    foreach (var change in travelAdviceRecentChanges)
                    {
                        writer.WriteStartObject();
                        if (!string.IsNullOrEmpty(change.Date))
                        {
                            writer.WriteString("date", change.Date);
                        }
                        writer.WriteString("summary", change.Summary);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }

                if (travelAdviceParts.Count > 0)
                {
                    writer.WritePropertyName("detailed_advice");
                    writer.WriteStartArray();
                    foreach (var part in travelAdviceParts)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("title", part.Title);
                        writer.WriteString("details", part.Body);
                        writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }

                writer.WriteEndObject();
            }

            // Add raw API responses
            writer.WritePropertyName("raw_api_responses");
            writer.WriteStartObject();
            
            writer.WritePropertyName("rest_countries");
            writer.WriteRawValue(countryResponseText);

            writer.WritePropertyName("open_meteo");
            writer.WriteRawValue(weatherResponseText);

            if (!string.IsNullOrEmpty(adviceResponseText))
            {
                writer.WritePropertyName("gov_uk_detail");
                writer.WriteRawValue(adviceResponseText);
            }
            
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
        var payload = Encoding.UTF8.GetString(stream.ToArray());

        if (traceAgent)
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                string? countryOut = root.TryGetProperty("country", out var cOut) ? cOut.GetString() : null;
                string? capitalOut = root.TryGetProperty("capital", out var capOut) ? capOut.GetString() : null;
                string? tempOut = null;
                string? condOut = null;

                if (root.TryGetProperty("weather", out var weatherOut) && weatherOut.ValueKind == JsonValueKind.Object)
                {
                    if (weatherOut.TryGetProperty("temperature", out var tOut))
                    {
                        tempOut = tOut.GetString();
                    }
                    if (weatherOut.TryGetProperty("conditions", out var condOutEl))
                    {
                        condOut = condOutEl.GetString();
                    }
                }

                AnsiConsole.MarkupLine("[dim yellow]   (Tool exit) LocationExpert is returning summarized JSON back to the main agent:[/]");
                AnsiConsole.MarkupLine($"[dim yellow]     Country: {countryOut ?? "(unknown)"}, Capital: {capitalOut ?? "(unknown)"}[/]");
                if (!string.IsNullOrEmpty(tempOut) || !string.IsNullOrEmpty(condOut))
                {
                    AnsiConsole.MarkupLine($"[dim yellow]     Weather: {tempOut ?? "(n/a)"}, Conditions: {condOut ?? "(n/a)"}[/]");
                }
                AnsiConsole.WriteLine();
            }
            catch
            {
                // If we somehow fail here, just skip the extra summary.
            }
        }

        if (showRaw)
        {
            AnsiConsole.MarkupLine("[dim yellow]     Raw LocationExpert response JSON:[/]");
            AnsiConsole.WriteLine(payload);
            AnsiConsole.WriteLine();
        }

        return payload;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]   ✗ ERROR: {ex.Message}[/]");
        // Use plain WriteLine for stack trace and any raw JSON to avoid
        // Spectre.Console markup parsing errors when arbitrary characters appear.
        AnsiConsole.WriteLine($"   Stack trace: {ex.StackTrace}");
        return $"Error fetching location data: {ex.Message}\n\nStack trace: {ex.StackTrace}";
    }
}

// Helper to convert weather codes to descriptions
static string GetWeatherDescription(int code)
{
    return code switch
    {
        0 => "Clear sky",
        1 or 2 or 3 => "Partly cloudy",
        45 or 48 => "Foggy",
        51 or 53 or 55 => "Drizzle",
        61 or 63 or 65 => "Rain",
        71 or 73 or 75 => "Snow",
        80 or 81 or 82 => "Rain showers",
        95 or 96 or 99 => "Thunderstorm",
        _ => "Variable conditions"
    };
}

// Custom pipeline policy that logs the raw HTTP JSON request and response
// bodies going to and from Azure OpenAI in a nicely formatted way.
sealed class RawJsonLoggingPolicy : PipelinePolicy
{
    private readonly bool _enabled;
    private readonly int _maxBytes;

    public RawJsonLoggingPolicy(bool enabled, int maxBytes = 1024 * 1024)
    {
        _enabled = enabled;
        _maxBytes = maxBytes;
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int index)
    {
        // Synchronously wrap the async core implementation.
        ProcessCoreAsync(message, pipeline, index, async: false).GetAwaiter().GetResult();
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int index)
    {
        await ProcessCoreAsync(message, pipeline, index, async: true).ConfigureAwait(false);
    }

    private async ValueTask ProcessCoreAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int index, bool async)
    {
        if (_enabled)
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

        if (_enabled)
        {
            PipelineResponse? response = message.Response;
            if (response is null)
            {
                return;
            }

            bool isSse = false;
            try
            {
                if (response.Headers.TryGetValue("Content-Type", out var contentType) &&
                    contentType is not null &&
                    contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    isSse = true;
                }
            }
            catch
            {
                // If header inspection fails, fall back to normal logging behavior.
            }

            // For streaming (SSE) responses, do not buffer or log the body,
            // otherwise we would consume the entire stream up-front and break
            // RunStreamingAsync. For non-streaming responses, buffer and log
            // the pretty-printed JSON body as before.
            if (!isSse)
            {
                try
                {
                    if (async)
                    {
                        await response.BufferContentAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        response.BufferContent();
                    }
                }
                catch
                {
                    // If buffering fails, just skip logging the body.
                }

                LogResponse(response);
            }
            else
            {
                try
                {
                    AnsiConsole.MarkupLine($"[dim]<< HTTP {response.Status} ({response.ReasonPhrase}) [streaming SSE, body not logged][/]");
                }
                catch
                {
                    // If logging fails, do not affect the response.
                }
            }
        }
    }

    private void LogRequest(PipelineMessage message)
    {
        var request = message.Request;

        try
        {
            var method = request.Method;
            var uri = request.Uri;

            AnsiConsole.MarkupLine($"[dim]>> HTTP {method} {uri}[/]");

            var content = request.Content;
            if (content is null)
            {
                return;
            }

            using var ms = new MemoryStream();
            content.WriteTo(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
            {
                return;
            }

            if (bytes.Length > _maxBytes)
            {
                bytes = bytes.AsSpan(0, _maxBytes).ToArray();
            }

            var text = Encoding.UTF8.GetString(bytes);
            var pretty = TryPrettyJson(text);

            AnsiConsole.MarkupLine("[dim]>> Request Body:[/]");
            AnsiConsole.WriteLine(pretty);
            AnsiConsole.WriteLine();
        }
        catch
        {
            // If anything goes wrong while logging, do not affect the request.
        }
    }

    private void LogResponse(PipelineResponse response)
    {
        try
        {
            // For streaming responses (Server-Sent Events), the body consists of many
            // "data: { ... }" chunks, which are already surfaced by the SDK's
            // higher-level streaming APIs. To avoid overwhelming the console,
            // skip logging those raw SSE frames here.
            try
            {
                if (response.Headers.TryGetValue("Content-Type", out var contentType) &&
                    contentType is not null &&
                    contentType.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch
            {
                // If header inspection fails, fall through and attempt normal logging.
            }

            AnsiConsole.MarkupLine($"[dim]<< HTTP {response.Status} ({response.ReasonPhrase})[/]");

            BinaryData contentData;
            try
            {
                contentData = response.Content;
            }
            catch
            {
                // Response was not buffered; nothing to log.
                return;
            }

            if (contentData is null)
            {
                return;
            }

            var text = contentData.ToString();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (text.Length > _maxBytes)
            {
                text = text.Substring(0, _maxBytes);
            }

            var pretty = TryPrettyJson(text);

            AnsiConsole.MarkupLine("[dim]<< Response Body:[/]");
            AnsiConsole.WriteLine(pretty);
            AnsiConsole.WriteLine();
        }
        catch
        {
            // If anything goes wrong while logging, do not affect the response.
        }
    }

    private static string TryPrettyJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                doc.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch
        {
            // Not valid JSON; just return the original text.
            return text;
        }
    }
}
