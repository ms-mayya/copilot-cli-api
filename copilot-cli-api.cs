#!/usr/bin/env dotnet
#:sdk Microsoft.NET.Sdk.Web

using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapPost("/api/convert", async ([FromBody] ConvertRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.HtmlContent))
    {
        return Results.BadRequest(new { error = "HtmlContent is required" });
    }

    try
    {
        var result = await ExecuteCopilotCommand(request.HtmlContent, request.Model ?? "claude-haiku-4.5");
        return Results.Ok(new ConvertResponse(result, true));
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();

static async Task<string> ExecuteCopilotCommand(string htmlContent, string model)
{
    var tempFile = Path.GetTempFileName();

    try
    {
        await File.WriteAllTextAsync(tempFile, htmlContent);

        var startInfo = new ProcessStartInfo
        {
            FileName = "copilot",
            Arguments = $"--model {model} --prompt \"$(< {tempFile})\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.GetCurrentDirectory()
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

record ConvertRequest(string HtmlContent, string? Model);
record ConvertResponse(string Markdown, bool Success);