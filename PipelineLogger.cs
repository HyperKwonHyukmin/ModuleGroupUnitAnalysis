using System;
using System.IO;

namespace ModuleGroupUnitAnalysis.Logger
{
  public class PipelineLogger
  {
    private readonly bool _logExport;
    private string? _logFilePath;

    public PipelineLogger(bool logExport)
    {
      _logExport = logExport;
    }

    /// <summary>
    /// BDF 파일 위치를 기반으로 로그 파일 경로를 최종 확정합니다.
    /// </summary>
    public void InitializeFile(string bdfPath)
    {
      if (!_logExport) return;

      string directory = Path.GetDirectoryName(bdfPath) ?? "";
      string fileName = Path.GetFileNameWithoutExtension(bdfPath) + "_Debug.log";
      _logFilePath = Path.Combine(directory, fileName);

      // 기존 로그 파일 초기화
      if (File.Exists(_logFilePath)) File.Delete(_logFilePath);
      LogInfo($"[System] 로그 파일이 생성되었습니다: {_logFilePath}");
    }

    public void Log(string message, ConsoleColor color = ConsoleColor.White)
    {
      Console.ForegroundColor = color;
      Console.WriteLine(message);
      Console.ResetColor();

      if (_logExport && _logFilePath != null)
      {
        File.AppendAllText(_logFilePath, message + Environment.NewLine);
      }
    }

    public void LogInfo(string message) => Log(message, ConsoleColor.Gray);
    public void LogSuccess(string message) => Log("[OK] " + message, ConsoleColor.Green);
    public void LogWarning(string message) => Log("[WARN] " + message, ConsoleColor.Yellow);
    public void LogError(string message) => Log("[ERROR] " + message, ConsoleColor.Red);
  }
}
