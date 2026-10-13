using System.Text.Json;
using Sshocked.Interfaces;
using Sshocked.Models;

namespace Sshocked.Services;

internal static class DiagnosticPresentation
{
    public static void Preview(DiagnosticReport report, IConsoleWriterService console)
    {
        console.WriteLine(JsonSerializer.Serialize(report, DiagnosticJsonContext.Default.DiagnosticReport));
        console.WriteLine("Local report only. No upload or sharing was performed. Paths and command text require explicit inclusion and audit privacy permission.");
    }
}
