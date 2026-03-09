using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Model.Entities;
using System;
using System.Collections.Generic;
using System.IO;

namespace ModuleGroupUnitAnalysis.Services.Parsers
{
  public class NastranBdfParser
  {
    private readonly FeModelContext _context;
    private readonly PipelineLogger _logger;
    private readonly bool _pipelineDebug;
    private readonly bool _verboseDebug;
    private int _errorCount = 0;

    public NastranBdfParser(FeModelContext context, PipelineLogger logger, bool pipelineDebug = true, bool verboseDebug = false)
    {
      _context = context ?? throw new ArgumentNullException(nameof(context));
      _logger = logger ?? throw new ArgumentNullException(nameof(logger));
      _pipelineDebug = pipelineDebug;
      _verboseDebug = verboseDebug;
    }

    public void Parse(string filePath)
    {
      if (_pipelineDebug) _logger.LogInfo($"\n▶ BDF 파싱 시작: {filePath}");

      var lines = File.ReadAllLines(filePath);

      for (int i = 0; i < lines.Length; i++)
      {
        string line = lines[i];
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("$")) continue;

        var fields = ReadLogicalCard(lines, ref i);
        if (fields.Count == 0) continue;

        string cardName = fields[0].ToUpper();

        // ★ 엄격한 예외 처리: 파싱 중 발생하는 모든 에러를 포획하여 누락 방지
        try
        {
          switch (cardName)
          {
            case "GRID": ParseGrid(fields); break;
            case "CBEAM": ParseCbeam(fields); break;
            case "CONM2": ParseConm2(fields); break;
            case "RBE2": ParseRbe2(fields); break;
            case "PBEAML": ParsePbeaml(fields); break;
            case "MAT1": ParseMat1(fields); break;
            default:
              if (_verboseDebug) _logger.LogWarning($"[Skip] 지원하지 않는 카드 (Line {i + 1}): {cardName}");
              break;
          }
        }
        catch (Exception ex)
        {
          _errorCount++;
          _logger.LogError($"파싱 실패 (Line {i + 1}) - 카드: {cardName}");
          _logger.LogError($"  -> 원인: {ex.Message}");
          if (_verboseDebug) _logger.LogError($"  -> 데이터: {string.Join(", ", fields)}");
        }
      }

      if (_pipelineDebug)
      {
        if (_errorCount > 0)
          _logger.LogError($"\n❌ BDF 파싱 완료: 총 {_errorCount}개의 치명적 파싱 오류/누락이 발생했습니다. (로그 확인 요망)");
        else
          _logger.LogSuccess($"\n✅ BDF 파싱 완료: 단 1건의 오류도 없이 완벽하게 로드되었습니다.");
      }
    }

    private List<string> ReadLogicalCard(string[] lines, ref int index)
    {
      var fields = new List<string>();
      ExtractFields(lines[index], fields, isContinuation: false);

      while (index + 1 < lines.Length)
      {
        string nextLine = lines[index + 1];
        if (string.IsNullOrWhiteSpace(nextLine) || nextLine.StartsWith("$"))
        {
          index++;
          continue;
        }

        string head = nextLine.Length >= 8 ? nextLine.Substring(0, 8) : nextLine;
        if (string.IsNullOrWhiteSpace(head) || head.TrimStart().StartsWith("+") || head.TrimStart().StartsWith("*"))
        {
          index++;
          ExtractFields(nextLine, fields, isContinuation: true);
        }
        else break;
      }
      return fields;
    }

    private void ExtractFields(string line, List<string> fields, bool isContinuation)
    {
      if (line.Contains(","))
      {
        var parts = line.Split(',');
        int start = isContinuation ? 1 : 0;
        for (int i = start; i < parts.Length; i++) fields.Add(parts[i].Trim());
      }
      else
      {
        int start = isContinuation ? 8 : 0;
        for (int i = start; i < line.Length; i += 8)
        {
          int len = Math.Min(8, line.Length - i);
          fields.Add(line.Substring(i, len).Trim());
        }
      }
    }

    // --- 세부 드 파싱 로직 (필드 개수 부족 시 강제 예외 발생) ---
    private void ParseGrid(List<string> fields)
    {
      if (fields.Count < 6) throw new Exception("GRID 카드 데이터 필드가 부족합니다.");
      int id = NastranFormatUtils.ParseInt(fields[1]);
      double x = NastranFormatUtils.ParseDouble(fields[3]);
      double y = NastranFormatUtils.ParseDouble(fields[4]);
      double z = NastranFormatUtils.ParseDouble(fields[5]);
      _context.Nodes.AddWithID(id, x, y, z);
    }

    private void ParseCbeam(List<string> fields)
    {
      if (fields.Count < 5) throw new Exception("CBEAM 카드 데이터 필드가 부족합니다.");
      int id = NastranFormatUtils.ParseInt(fields[1]);
      int pid = NastranFormatUtils.ParseInt(fields[2]);
      int nA = NastranFormatUtils.ParseInt(fields[3]);
      int nB = NastranFormatUtils.ParseInt(fields[4]);

      double x1 = fields.Count > 5 ? NastranFormatUtils.ParseDouble(fields[5]) : 0;
      double x2 = fields.Count > 6 ? NastranFormatUtils.ParseDouble(fields[6]) : 0;
      double x3 = fields.Count > 7 ? NastranFormatUtils.ParseDouble(fields[7]) : 1.0;

      var extraData = new Dictionary<string, string> {
                { "OriX", x1.ToString() }, { "OriY", x2.ToString() }, { "OriZ", x3.ToString() }
            };

      _context.Elements.AddWithID(id, new List<int> { nA, nB }, pid, new double[] { x1, x2, x3 }, extraData);
    }

    private void ParseConm2(List<string> fields)
    {
      if (fields.Count < 5) throw new Exception("CONM2 카드 데이터 필드가 부족합니다.");
      int id = NastranFormatUtils.ParseInt(fields[1]);
      int nodeId = NastranFormatUtils.ParseInt(fields[2]);
      double mass = NastranFormatUtils.ParseDouble(fields[4]);
      var extraData = new Dictionary<string, string> { { "Type", "CONM2" } };
      _context.PointMasses.AddWithID(id, nodeId, mass, extraData);
    }

    private void ParseRbe2(List<string> fields)
    {
      if (fields.Count < 5) throw new Exception("RBE2 카드의 종속 노드(Dependent Nodes)가 누락되었습니다.");
      int id = NastranFormatUtils.ParseInt(fields[1]);
      int indepNode = NastranFormatUtils.ParseInt(fields[2]);
      string cm = fields[3];

      var depNodes = new List<int>();
      for (int i = 4; i < fields.Count; i++)
      {
        int depNode = NastranFormatUtils.ParseInt(fields[i]);
        if (depNode > 0) depNodes.Add(depNode);
      }
      if (depNodes.Count == 0) throw new Exception("RBE2 종속 노드가 0개입니다.");

      var extraData = new Dictionary<string, string> { { "Type", "RBE2" } };
      _context.Rigids.AddWithID(id, indepNode, depNodes, cm, extraData);
    }

    private void ParseMat1(List<string> fields)
    {
      if (fields.Count < 6) throw new Exception("MAT1 카드 데이터 필드가 부족합니다.");
      int mid = NastranFormatUtils.ParseInt(fields[1]);
      double e = NastranFormatUtils.ParseDouble(fields[2]);
      double nu = NastranFormatUtils.ParseDouble(fields[4]);
      double rho = NastranFormatUtils.ParseDouble(fields[5]);
      _context.Materials.AddWithID(mid, "Original_MAT1", e, nu, rho);
    }

    private void ParsePbeaml(List<string> fields)
    {
      if (fields.Count < 6) throw new Exception("PBEAML 카드 치수 데이터가 부족합니다.");
      int pid = NastranFormatUtils.ParseInt(fields[1]);
      int mid = NastranFormatUtils.ParseInt(fields[2]);
      string section = fields.Count > 4 ? fields[4] : "TUBE";

      var dims = new List<double>();
      for (int i = 5; i < fields.Count; i++)
      {
        if (!string.IsNullOrWhiteSpace(fields[i]))
          dims.Add(NastranFormatUtils.ParseDouble(fields[i]));
      }
      _context.Properties.AddWithID(pid, section, dims.ToArray(), mid);
    }
  }
}
