using System;
using System.Collections.Generic;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingWireGenerator
  {
    /// <summary>
    /// # HookTrolley-09
    /// 계산된 정점에 새로운 Node를 생성하고, 기존 Lifting Point들과 CROD(와이어)로 연결합니다.
    /// BDF 출력 전, Nastran 형식의 텍스트 카드(String)로 만들어 보관합니다.
    /// </summary>
    public static void Run(List<LiftingGroup> liftingGroups, SpcAssignData spcData, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 9] 권상 정점(Node) 및 가상 와이어(CROD) 네트워크 생성 시작");

      // 기존 모델과 ID가 겹치지 않도록 990만 번대역의 안전한 ID 사용
      int startId = 9900001;
      int currentGridId = startId;
      int currentElemId = startId;
      int propId = startId;
      int matId = startId;

      // 1. 와이어(Wire)용 재질(MAT1) 및 프로퍼티(PROD) 카드 생성 (강철 와이어 가정)
      spcData.GeneratedBulkCards.Add($"MAT1,{matId},2.1E5,,0.3,7.85E-9");
      spcData.GeneratedBulkCards.Add($"PROD,{propId},{matId},1963.5"); // 단면적 A=1963.5 (R=25mm 기준)

      // 2. 각 그룹별 정점(GRID)과 와이어(CROD) 생성
      foreach (var group in liftingGroups)
      {
        int topNodeId = currentGridId++;

        // Hook/Trolley 정점 노드 생성
        spcData.GeneratedBulkCards.Add($"GRID,{topNodeId},,{group.CalculatedTopPoint.X:F2},{group.CalculatedTopPoint.Y:F2},{group.CalculatedTopPoint.Z:F2}");

        // 정점이 허공으로 날아가지 않고 지지점이 되도록 123456(모든 자유도) 고정
        spcData.GeneratedBulkCards.Add($"SPC1,1,123456,{topNodeId}");

        // 정점과 대상 유닛 포인트들을 잇는 와이어(CROD) 생성
        foreach (var node in group.Nodes)
        {
          spcData.GeneratedBulkCards.Add($"CROD,{currentElemId++},{propId},{topNodeId},{node.NodeID}");
        }

        if (debugPrint) logger.LogInfo($"  -> [Group {group.GroupId}] 정점 노드({topNodeId}) 및 권상 와이어 연결 완료");
      }

      // 3. Stage 8에서 확보한 해석 안정화용 SPC 카드 병합
      foreach (int pNode in spcData.PipeSpcNodes)
      {
        spcData.GeneratedBulkCards.Add($"SPC1,1,1,{pNode}");
      }
      foreach (int cNode in spcData.CogSpcNodes)
      {
        spcData.GeneratedBulkCards.Add($"SPC1,1,12,{cNode}");
      }

      if (debugPrint) logger.LogSuccess($"9단계 : 가상 와이어 네트워크 및 SPC 카드 텍스트({spcData.GeneratedBulkCards.Count}줄) 생성 완료");
    }
  }
}
