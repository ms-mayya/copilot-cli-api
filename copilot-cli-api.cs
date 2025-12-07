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
    if (string.IsNullOrWhiteSpace(request.Html))
    {
        return Results.BadRequest(new { error = "Html is required" });
    }

    try
    {
        var result = await ExecuteCopilotCommand(request.Html);
        return Results.Ok(new ConvertResponse(result, true));
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

static async Task<string> ExecuteCopilotCommand(string htmlContent)
{
    var tempFile = Path.GetTempFileName();

    try
    {
        await File.WriteAllTextAsync(tempFile, htmlContent);

        var workdir = Directory.GetCurrentDirectory();
        var startInfo = new ProcessStartInfo
        {
            FileName = "copilot",
            Arguments = $"--add-dir {workdir} --prompt \"$(< {tempFile})\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workdir
        };

        using var process = new Process { StartInfo = startInfo };

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                output.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                error.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Copilot command failed: {error}");
        }

        return output.ToString();
    }
    finally
    {
        if (File.Exists(tempFile))
        {
            File.Delete(tempFile);
        }
    }
}

record ConvertRequest(string Html);
record ConvertResponse(string Markdown, bool Success);

[JsonSerializable(typeof(ConvertResponse))]
[JsonSerializable(typeof(ConvertRequest))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(string))]
internal partial class SourceGenerationContext : JsonSerializerContext { }