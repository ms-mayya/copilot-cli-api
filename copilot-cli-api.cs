#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk.Web

using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Configure JSON serialization to use source generation
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, SourceGenerationContext.Default);
});

var app = builder.Build();

app.MapPost("/api/convert", async ([FromBody] ConvertRequest request) =>
{
    var result = await ExecuteCopilotCommandAsync(request.Html);
    return TypedResults.Ok(new ConvertResponse(result, true));
});

app.MapGet("/api/health", () => TypedResults.Ok());

app.Run();

static async Task<string> ExecuteCopilotCommandAsync(string html)
{
    var workdir = Directory.GetCurrentDirectory();

    var startInfo = new ProcessStartInfo
    {
        FileName = "copilot",
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = true,
        CreateNoWindow = true,
        WorkingDirectory = workdir
    };

    startInfo.ArgumentList.Add("--add-dir");
    startInfo.ArgumentList.Add(workdir);
    startInfo.ArgumentList.Add("--prompt");
    startInfo.ArgumentList.Add(html);

    using var proc = new Process { StartInfo = startInfo };
    proc.Start();

    using var ms = new MemoryStream();
    using (var sOut = proc.StandardOutput.BaseStream)
    {
        byte[] buffer = new byte[4096];
        int read;

        while ((read = sOut.Read(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, read);
        }
    }

    var error = proc.StandardError.ReadToEnd();

    await proc.WaitForExitAsync();

    if (ms.Length == 0)
    {
        throw new Exception($"Copilot command failed: {error}");
    }

    return Encoding.UTF8.GetString(ms.ToArray());
}

record ConvertRequest(string Html);
record ConvertResponse(string Markdown, bool Success);

[JsonSerializable(typeof(ConvertResponse))]
[JsonSerializable(typeof(ConvertRequest))]
internal partial class SourceGenerationContext : JsonSerializerContext { }