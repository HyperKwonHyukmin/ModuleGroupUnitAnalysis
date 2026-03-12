using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Pipeline;
using System;
using System.Collections.Generic;
using System.Text;

namespace ModuleGroupUnitAnalysis.Exporter
{
  public class BdfBuilder
  {
    public int Sol;
    public FeModelContext Context;
    public SpcAssignData SpcData;
    public List<string> BdfLines = new List<string>();

    public BdfBuilder(int sol, FeModelContext context, SpcAssignData spcData)
    {
      Sol = sol;
      Context = context;
      SpcData = spcData;
    }

    public void Run()
    {
      ExecutiveControlSection();
      CaseControlSection();
      SubcaseSection();
      NodeElementSection();
      PropertyMaterialSection();
      RigidElementSection();
      PointMassSection();
      VirtualLiftingSection(); // ★ 신규: 와이어 및 SPC 텍스트 주입
      LoadBulkSection();
      EndSigniture();
    }

    public void ExecutiveControlSection()
    {
      BdfLines.Add(BdfFormatFields.FormatField($"SOL {Sol}"));
      BdfLines.Add(BdfFormatFields.FormatField($"CEND"));
    }

    public void CaseControlSection()
    {
      BdfLines.Add("DISPLACEMENT = ALL");
      BdfLines.Add("SPCFORCES = ALL");
      BdfLines.Add("ELFORCE = ALL");
      BdfLines.Add("STRESS = ALL");
    }

    public void SubcaseSection()
    {
      BdfLines.Add("SUBCASE       1");
      BdfLines.Add("LABEL = LC1");
      BdfLines.Add("SPC = 1");
      BdfLines.Add("LOAD = 2");
      BdfLines.Add("ANALYSIS = STATICS");
      BdfLines.Add("BEGIN BULK");
      BdfLines.Add("PARAM,POST,-1");
      BdfLines.Add("PARAM,AUTOSPC,YES"); // 강체 회전 방지용
    }

    public void NodeElementSection()
    {
      foreach (var node in Context.Nodes)
      {
        string nodeText = $"{BdfFormatFields.FormatField("GRID")}"
          + $"{BdfFormatFields.FormatField(node.Key, "right")}"
          + $"{BdfFormatFields.FormatField("")}"
          + $"{BdfFormatFields.FormatField(node.Value.X, "right")}"
          + $"{BdfFormatFields.FormatField(node.Value.Y, "right")}"
          + $"{BdfFormatFields.FormatField(node.Value.Z, "right")}";
        BdfLines.Add(nodeText);
      }

      foreach (var element in Context.Elements)
      {
        double oriX = 0.0, oriY = 0.0, oriZ = 1.0;
        if (element.Value.ExtraData.TryGetValue("OriX", out string sX)) double.TryParse(sX, out oriX);
        if (element.Value.ExtraData.TryGetValue("OriY", out string sY)) double.TryParse(sY, out oriY);
        if (element.Value.ExtraData.TryGetValue("OriZ", out string sZ)) double.TryParse(sZ, out oriZ);

        string elementText = $"{BdfFormatFields.FormatField("CBEAM")}"
         + $"{BdfFormatFields.FormatField(element.Key, "right")}"
         + $"{BdfFormatFields.FormatField(element.Value.PropertyID, "right")}"
         + $"{BdfFormatFields.FormatField(element.Value.NodeIDs[0], "right")}"
         + $"{BdfFormatFields.FormatField(element.Value.NodeIDs[1], "right")}"
         + $"{BdfFormatFields.FormatField(oriX, "right")}"
         + $"{BdfFormatFields.FormatField(oriY, "right")}"
         + $"{BdfFormatFields.FormatField(oriZ, "right")}";
        BdfLines.Add(elementText);
      }
    }

    public void PropertyMaterialSection()
    {
      foreach (var property in Context.Properties)
      {
        string type = property.Value.Type.ToUpper();
        string propertyText = $"{BdfFormatFields.FormatField("PBEAML")}"
          + $"{BdfFormatFields.FormatField(property.Key, "right")}"
          + $"{BdfFormatFields.FormatField(property.Value.MaterialID, "right")}"
          + $"{BdfFormatFields.FormatField("", "right")}"
          + $"{BdfFormatFields.FormatField(type, "right")}";
        BdfLines.Add(propertyText);

        var sb = new StringBuilder($"{BdfFormatFields.FormatField("")}");
        foreach (double dim in property.Value.Dim)
        {
          sb.Append(BdfFormatFields.FormatField(dim, "right"));
        }
        BdfLines.Add(sb.ToString());
      }

      foreach (var material in Context.Materials)
      {
        string materialText = $"{BdfFormatFields.FormatField("MAT1")}"
            + $"{BdfFormatFields.FormatField(material.Key, "right")}"
            + $"{BdfFormatFields.FormatField(material.Value.E, "right")}"
            + $"{BdfFormatFields.FormatField("")}"
            + $"{BdfFormatFields.FormatField(material.Value.Nu, "right")}"
            + $"{BdfFormatFields.FormatField(material.Value.Rho, "right")}";
        BdfLines.Add(materialText);
      }
    }

    public void RigidElementSection()
    {
      foreach (var kv in Context.Rigids)
      {
        var info = kv.Value;
        if (info.DependentNodeIDs == null || info.DependentNodeIDs.Count == 0) continue;

        var sb = new StringBuilder();
        sb.Append(BdfFormatFields.FormatField("RBE2"));
        sb.Append(BdfFormatFields.FormatField(kv.Key, "right"));
        sb.Append(BdfFormatFields.FormatField(info.IndependentNodeID, "right"));
        sb.Append(BdfFormatFields.FormatField(info.Cm, "right"));

        int fieldsUsed = 4;
        for (int i = 0; i < info.DependentNodeIDs.Count; i++)
        {
          if (fieldsUsed >= 9)
          {
            sb.Append(BdfFormatFields.FormatField("+"));
            BdfLines.Add(sb.ToString());
            sb.Clear();
            sb.Append(BdfFormatFields.FormatField("+"));
            fieldsUsed = 1;
          }
          sb.Append(BdfFormatFields.FormatField(info.DependentNodeIDs[i], "right"));
          fieldsUsed++;
        }
        if (sb.Length > 0) BdfLines.Add(sb.ToString());
      }
    }

    public void PointMassSection()
    {
      foreach (var pm in Context.PointMasses)
      {
        string massLine = $"{BdfFormatFields.FormatField("CONM2")}"
                        + $"{BdfFormatFields.FormatField(pm.Key, "right")}"
                        + $"{BdfFormatFields.FormatField(pm.Value.NodeID, "right")}"
                        + $"{BdfFormatFields.FormatField(0, "right")}"
                        + $"{BdfFormatFields.FormatField(pm.Value.Mass, "right")}";
        BdfLines.Add(massLine);
      }
    }

    // ★ 핵심: 파이프라인에서 만든 가상의 와이어(CROD) 및 안정화 SPC 텍스트를 바로 주입합니다.
    public void VirtualLiftingSection()
    {
      if (SpcData != null && SpcData.GeneratedBulkCards.Count > 0)
      {
        BdfLines.Add("$ ====================================================================");
        BdfLines.Add("$ [VIRTUAL] LIFTING WIRES & STABILIZATION SPC CARDS");
        BdfLines.Add("$ ====================================================================");
        foreach (var card in SpcData.GeneratedBulkCards)
        {
          BdfLines.Add(card); // CSV 포맷 그대로 출력되므로 Nastran이 정상 인식합니다.
        }
      }
    }

    public void LoadBulkSection()
    {
      // 권상 해석용 중력 가속도 (-1.0 Z 방향)
      BdfLines.Add("GRAV, 2, , 9800.0, 0.0, 0.0, -1.0");
    }

    public void EndSigniture()
    {
      BdfLines.Add("ENDDATA");
    }
  }
}
