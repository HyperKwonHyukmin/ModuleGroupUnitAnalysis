using ModuleGroupUnitAnalysis.Model.Entities; // 사용 중인 FeModelContext의 네임스페이스에 맞게 수정하세요.
using System;
using System.Collections.Generic;
using System.IO;

namespace ModuleGroupUnitAnalysis.Services.Parsers
{
  public class NastranBdfParser
  {
    private readonly FeModelContext _context;

    public NastranBdfParser(FeModelContext context)
    {
      _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public void Parse(string filePath)
    {
      var lines = File.ReadAllLines(filePath);

      for (int i = 0; i < lines.Length; i++)
      {
        string line = lines[i];
        // 빈 줄이거나 주석($)이면 스킵
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("$")) continue;

        // 1. Nastran 논리적 카드 단위로 병합 (Continuation Line 처리)
        var fields = ReadLogicalCard(lines, ref i);
        if (fields.Count == 0) continue;

        string cardName = fields[0].ToUpper();

        // 2. 카드 종류에 따라 FeModelContext에 객체 생성 및 할당
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
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine($"[경고] {cardName} 카드 파싱 실패 (Line {i + 1}): {ex.Message}");
        }
      }
    }

    /// <summary>
    /// '+' 로 이어지는 연속된 줄(Continuation Lines)을 하나의 필드 리스트로 병합합니다.
    /// </summary>
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

        // 앞 8칸이 비어있거나 '+', '*' 로 시작하면 이어지는 줄로 간주
        string head = nextLine.Length >= 8 ? nextLine.Substring(0, 8) : nextLine;
        if (string.IsNullOrWhiteSpace(head) || head.TrimStart().StartsWith("+") || head.TrimStart().StartsWith("*"))
        {
          index++;
          ExtractFields(nextLine, fields, isContinuation: true);
        }
        else
        {
          break; // 새로운 카드의 시작
        }
      }
      return fields;
    }

    private void ExtractFields(string line, List<string> fields, bool isContinuation)
    {
      if (line.Contains(",")) // Free 포맷 (CSV)
      {
        var parts = line.Split(',');
        int start = isContinuation ? 1 : 0;
        for (int i = start; i < parts.Length; i++) fields.Add(parts[i].Trim());
      }
      else // Small Field 포맷 (8칸 고정)
      {
        int start = isContinuation ? 8 : 0; // 이어지는 줄은 앞 8칸 기호 무시
        for (int i = start; i < line.Length; i += 8)
        {
          int len = Math.Min(8, line.Length - i);
          fields.Add(line.Substring(i, len).Trim());
        }
      }
    }

    // ==========================================
    // 세부 카드 파싱 로직 
    // (기존 Python의 replace 방식과 달리 깔끔하게 객체화)
    // ==========================================

    private void ParseGrid(List<string> fields)
    {
      int id = NastranFormatUtils.ParseInt(fields[1]);
      double x = NastranFormatUtils.ParseDouble(fields[3]);
      double y = NastranFormatUtils.ParseDouble(fields[4]);
      double z = NastranFormatUtils.ParseDouble(fields[5]);

      // 기존 객체에 덮어쓰거나 새로 생성
      _context.Nodes.AddWithID(id, x, y, z);
    }

    private void ParseCbeam(List<string> fields)
    {
      int id = NastranFormatUtils.ParseInt(fields[1]);
      int pid = NastranFormatUtils.ParseInt(fields[2]);
      int nA = NastranFormatUtils.ParseInt(fields[3]);
      int nB = NastranFormatUtils.ParseInt(fields[4]);

      // 방향 벡터 (기본값 설정)
      double x1 = fields.Count > 5 ? NastranFormatUtils.ParseDouble(fields[5]) : 0;
      double x2 = fields.Count > 6 ? NastranFormatUtils.ParseDouble(fields[6]) : 0;
      double x3 = fields.Count > 7 ? NastranFormatUtils.ParseDouble(fields[7]) : 1.0;

      var extraData = new Dictionary<string, string> {
                { "OriX", x1.ToString() },
                { "OriY", x2.ToString() },
                { "OriZ", x3.ToString() }
            };

      // 원본 파일의 ID를 유지하기 위해 AddWithID 메서드 사용 권장
      _context.Elements.AddWithID(id, new List<int> { nA, nB }, pid, new double[] { x1, x2, x3 }, extraData);
    }

    private void ParseConm2(List<string> fields)
    {
      int id = NastranFormatUtils.ParseInt(fields[1]);
      int nodeId = NastranFormatUtils.ParseInt(fields[2]);
      double mass = NastranFormatUtils.ParseDouble(fields[4]);

      var extraData = new Dictionary<string, string> { { "Type", "CONM2" } };
      _context.PointMasses.AddWithID(id, nodeId, mass, extraData);
    }

    private void ParseRbe2(List<string> fields)
    {
      int id = NastranFormatUtils.ParseInt(fields[1]);
      int indepNode = NastranFormatUtils.ParseInt(fields[2]);
      string cm = fields[3];

      var depNodes = new List<int>();
      for (int i = 4; i < fields.Count; i++)
      {
        int depNode = NastranFormatUtils.ParseInt(fields[i]);
        if (depNode > 0) depNodes.Add(depNode);
      }

      var extraData = new Dictionary<string, string> { { "Type", "RBE2" } };
      _context.Rigids.AddWithID(id, indepNode, depNodes, cm, extraData);
    }

    private void ParseMat1(List<string> fields)
    {
      int mid = NastranFormatUtils.ParseInt(fields[1]);
      double e = NastranFormatUtils.ParseDouble(fields[2]);
      double nu = NastranFormatUtils.ParseDouble(fields[4]);
      double rho = NastranFormatUtils.ParseDouble(fields[5]);

      _context.Materials.AddWithID(mid, "Original_MAT1", e, nu, rho);
    }

    private void ParsePbeaml(List<string> fields)
    {
      int pid = NastranFormatUtils.ParseInt(fields[1]);
      int mid = NastranFormatUtils.ParseInt(fields[2]);
      string section = fields.Count > 4 ? fields[4] : "TUBE";

      // 치수(Dim) 데이터는 이어지는 줄(인덱스 5 이후)에 존재합니다.
      var dims = new List<double>();
      for (int i = 5; i < fields.Count; i++)
      {
        if (!string.IsNullOrWhiteSpace(fields[i]))
        {
          dims.Add(NastranFormatUtils.ParseDouble(fields[i]));
        }
      }

      _context.Properties.AddWithID(pid, section, dims.ToArray(), mid);
    }
  }
}
