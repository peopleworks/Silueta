using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// The MCP protocol owns stdout (JSON-RPC frames). Every log line MUST go to stderr, or it corrupts the
// stream the client is parsing. For this server there is a second reason: it handles transcripts, and a
// log that went to stdout would be a log of identified text on the model's own channel.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
