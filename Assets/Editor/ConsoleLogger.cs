using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using System.IO;
using System;

[InitializeOnLoad]
public class ConsoleLogger
{
    private static readonly string logPath = Path.Combine(Application.dataPath, "..", "console_log.txt");

    static ConsoleLogger()
    {
        File.WriteAllText(logPath, $"--- Log started {DateTime.Now} ---\n");
        Application.logMessageReceived += OnLog;
        CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
    }

    private static void OnLog(string message, string stackTrace, LogType type)
    {
        // Capture ALL log types including Debug.Log
        string prefix = type == LogType.Error ? "[Error]"
            : type == LogType.Exception ? "[Exception]"
            : type == LogType.Warning ? "[Warning]"
            : type == LogType.Assert ? "[Assert]"
            : "[Log]";

        // Skip noisy per-frame or irrelevant messages
        if (message.Contains("UAC") || message.Contains("Sentis") || message.Contains("ConvGeneric"))
            return;

        string entry = string.IsNullOrEmpty(stackTrace)
            ? $"{prefix} {message}"
            : $"{prefix} {message}\n{stackTrace}";

        Append(entry);
    }

    private static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
    {
        foreach (var msg in messages)
            Append($"[Compile {msg.type}] {msg.message}");
    }

    private static void Append(string line)
    {
        try { File.AppendAllText(logPath, line + "\n"); } catch { }
    }
}
