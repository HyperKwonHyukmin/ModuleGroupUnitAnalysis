using System;
using System.Collections.Generic;
using System.Linq;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingPointTrolleySplitter
  {
    /// <summary>
    /// # HookTrolley-07
    /// Goliat (Trolley) 크레인일 경우, 계산된 정점을 900mm(±450mm) 간격으로 벌려줍니다.
    /// 4개의 권상 포인트를 2개씩 쪼개어 새로운 LiftingGroup으로 재편성합니다.
    /// </summary>
    public static void Run(List<LiftingGroup> liftingGroups, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 7] 트롤리(Goliat) 권상 간격 분할 (Trolley Splitter) 시작");

      // ★ [수정된 부분] Goliat(Trolley) 방식(1)이 아니면 생략 로그를 명확히 출력하고 종료
      if (!liftingGroups.Any(g => g.LiftingMethod == 1))
      {
        if (debugPrint)
        {
          logger.LogInfo("  -> 현재 권상 방식이 Hydro (Hook) 이므로 트롤리 간격 분할(±450mm)을 수행하지 않습니다.");
          logger.LogSuccess("7단계 : 트롤리 간격 분할 건너뜀 (해당 없음)");
        }
        return;
      }

      // 트롤리를 벌릴 전체 방향(X 또는 Y) 결정
      int countX = liftingGroups.Count(g => g.ShapeType.Contains("방향: X"));
      int countY = liftingGroups.Count(g => g.ShapeType.Contains("방향: Y"));
      string globalSplitDirection = countX >= countY ? "Y" : "X";

      if (debugPrint) logger.LogInfo($"  -> Goliat 권상 방식이 감지되었습니다. 지정된 다각형 형태에 따라 {globalSplitDirection}축 방향으로 간격 분할을 시작합니다.");

      var splitGroups = new List<LiftingGroup>();

      foreach (var g in liftingGroups)
      {
        if (g.LiftingMethod != 1)
        {
          splitGroups.Add(g);
          continue;
        }

        if (g.ShapeType == "4개점 사각형 형태")
        {
          Point3D p1 = g.CalculatedTopPoint;
          Point3D p2 = g.CalculatedTopPoint;

          if (globalSplitDirection == "X")
          {
            p1.X -= 450.0;
            p2.X += 450.0;
          }
          else
          {
            p1.Y -= 450.0;
            p2.Y += 450.0;
          }

          var sortedByY = g.Nodes.OrderBy(n => n.Pos.Y).ToList();
          var lowerEdge = sortedByY.Take(2).OrderBy(n => n.Pos.X).ToList();
          var upperEdge = sortedByY.Skip(2).Take(2).OrderBy(n => n.Pos.X).ToList();

          var sortedNodes = lowerEdge.Concat(upperEdge).ToList();
          List<LiftingNode> nodes1, nodes2;

          if (globalSplitDirection == "X")
          {
            nodes1 = new List<LiftingNode> { sortedNodes[0], sortedNodes[2] };
            nodes2 = new List<LiftingNode> { sortedNodes[1], sortedNodes[3] };
          }
          else
          {
            nodes1 = new List<LiftingNode> { sortedNodes[0], sortedNodes[1] };
            nodes2 = new List<LiftingNode> { sortedNodes[2], sortedNodes[3] };
          }

          splitGroups.Add(CreateSplitGroup(g, p1, nodes1, 1));
          splitGroups.Add(CreateSplitGroup(g, p2, nodes2, 2));
        }
        else if (g.ShapeType.Contains("4개점 일직선 형태"))
        {
          Point3D p1 = g.CalculatedTopPoint;
          Point3D p2 = g.CalculatedTopPoint;

          if (g.ShapeType.Contains("방향: X"))
          {
            p1.X -= 450.0;
            p2.X += 450.0;
          }
          else
          {
            p1.Y -= 450.0;
            p2.Y += 450.0;
          }

          var nodes1 = new List<LiftingNode> { g.Nodes[0], g.Nodes[1] };
          var nodes2 = new List<LiftingNode> { g.Nodes[2], g.Nodes[3] };

          splitGroups.Add(CreateSplitGroup(g, p1, nodes1, 1));
          splitGroups.Add(CreateSplitGroup(g, p2, nodes2, 2));
        }
        else
        {
          splitGroups.Add(g);
        }
      }

      liftingGroups.Clear();
      liftingGroups.AddRange(splitGroups);

      if (debugPrint)
      {
        logger.LogSuccess($"7단계 : 트롤리 권상 간격 분할 완료. (총 {liftingGroups.Count}개 그룹으로 재편성됨)");
        foreach (var g in liftingGroups)
        {
          logger.LogInfo($"  -> [Group {g.GroupId}] Top=({g.CalculatedTopPoint.X:F1}, {g.CalculatedTopPoint.Y:F1}, {g.CalculatedTopPoint.Z:F1}), Nodes=[{string.Join(", ", g.Nodes.Select(n => n.NodeID))}]");
        }
      }
    }

    private static LiftingGroup CreateSplitGroup(LiftingGroup original, Point3D newTop, List<LiftingNode> newNodes, int suffix)
    {
      return new LiftingGroup
      {
        GroupId = original.GroupId * 10 + suffix,
        Nodes = newNodes,
        LineLength = original.LineLength,
        LiftingMethod = original.LiftingMethod,
        ShapeType = original.ShapeType,
        CalculatedTopPoint = newTop
      };
    }
  }
}
