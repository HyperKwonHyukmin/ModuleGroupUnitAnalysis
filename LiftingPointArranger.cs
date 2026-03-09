using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Services.Utils; // PipelineLogger 사용
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingPointArranger
  {
    /// <summary>
    /// # HookTrolley-01
    /// 권상 포인트를 계산하기 위해 기하학적 배치 순서를 변경(정렬 및 스왑)합니다.
    /// </summary>
    public static void Run(List<LiftingGroup> liftingGroups, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 1] 권상 포인트 정렬 (Lifting Point Setting) 시작");

      foreach (var group in liftingGroups)
      {
        if (group.Nodes.Count == 4)
        {
          // 1. SQRT (X^2 + Y^2) 계산
          foreach (var n in group.Nodes)
          {
            n.SqrtVal = Math.Sqrt(n.Pos.X * n.Pos.X + n.Pos.Y * n.Pos.Y);
          }

          // 2. 최대 SQRT 값을 가진 노드 찾기
          var maxSqrtNode = group.Nodes.OrderBy(n => n.SqrtVal).Last();

          // 3. P 값 계산 (최대 SQRT 노드와의 XY 평면 거리)
          foreach (var n in group.Nodes)
          {
            double dx = n.Pos.X - maxSqrtNode.Pos.X;
            double dy = n.Pos.Y - maxSqrtNode.Pos.Y;
            n.PVal = Math.Sqrt(dx * dx + dy * dy);
          }

          // 4. X좌표 기준으로 오름차순 정렬 (Python: sort(key=lambda x: x[1]))
          var sorted = group.Nodes.OrderBy(n => n.Pos.X).ToList();

          // 5. 인덱스 1과 2 스왑 (Z형 배열을 사각형 순환 배열로 변경)
          var temp = sorted[1];
          sorted[1] = sorted[2];
          sorted[2] = temp;

          group.Nodes = sorted;
        }
        else if (group.Nodes.Count == 3)
        {
          // 3개점의 경우: X 기준 정렬 후 1, 2 스왑
          var sorted = group.Nodes.OrderBy(n => n.Pos.X).ToList();
          var temp = sorted[1];
          sorted[1] = sorted[2];
          sorted[2] = temp;
          group.Nodes = sorted;
        }
        else if (group.Nodes.Count == 2)
        {
          // 2개점의 경우: X 기준 오름차순 정렬
          group.Nodes = group.Nodes.OrderBy(n => n.Pos.X).ToList();
        }
      }

      if (debugPrint)
      {
        logger.LogSuccess("1단계 : 유닛 포인트 정렬 완료");
        foreach (var group in liftingGroups)
        {
          string nodeIds = string.Join(", ", group.Nodes.Select(n => n.NodeID));
          logger.LogInfo($"  -> Group {group.GroupId}: [{nodeIds}]");
        }
      }
    }
  }
}
