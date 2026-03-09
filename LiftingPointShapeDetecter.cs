using System;
using System.Collections.Generic;
using System.Linq;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingPointShapeDetecter
  {
    /// <summary>
    /// # HookTrolley-02
    /// LiftingPoint들이 이루는 형태가 어떤 형태인지 확인하여 ShapeType을 기록합니다.
    /// (4개점 일직선, 4개점 사각형, 3개점, 2개점)
    /// </summary>
    public static void Run(List<LiftingGroup> liftingGroups, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 2] 유닛 포인트 형태 판별 (Shape Detector) 시작");

      int setIndex = 1;
      foreach (var group in liftingGroups)
      {
        if (group.Nodes.Count == 4)
        {
          string direction = GetAlmostLineDirection(group.Nodes, 0.1);
          if (direction != "None")
          {
            group.ShapeType = $"4개점 일직선 형태, 방향: {direction}";

            // 일직선인 경우 Stage 1의 사각형 스왑을 취소하고 순서대로 재정렬
            if (direction == "X")
              group.Nodes = group.Nodes.OrderBy(n => n.Pos.X).ToList();
            else if (direction == "Y")
              group.Nodes = group.Nodes.OrderBy(n => n.Pos.Y).ToList();
          }
          else if (IsQuadrilateral(group.Nodes))
          {
            group.ShapeType = "4개점 사각형 형태";
          }
        }
        else if (group.Nodes.Count == 3)
        {
          group.ShapeType = "3개점";
        }
        else if (group.Nodes.Count == 2)
        {
          group.ShapeType = "2개점";
        }

        if (debugPrint)
        {
          logger.LogInfo($"  -> SET{setIndex} : {group.ShapeType}");
        }
        setIndex++;
      }
    }

    /// <summary>
    /// 4개의 점이 허용 오차(Tolerance) 내에서 X 또는 Y 축과 평행한 일직선을 이루는지 판별합니다.
    /// </summary>
    private static string GetAlmostLineDirection(List<LiftingNode> nodes, double tolerance)
    {
      var unitVectors = new List<Vector3D>();

      // 모든 점 쌍(Pair) 사이의 벡터를 구함
      for (int i = 0; i < nodes.Count; i++)
      {
        for (int j = 0; j < nodes.Count; j++)
        {
          if (i == j) continue;
          var v = nodes[j].Pos - nodes[i].Pos;
          if (v.Magnitude() > 1e-9)
          {
            unitVectors.Add(v.Normalize());
          }
        }
      }

      bool almostLineInX = true;
      bool almostLineInY = true;

      foreach (var v in unitVectors)
      {
        if (Math.Abs(v.X) < 1.0 - tolerance) almostLineInX = false;
        if (Math.Abs(v.Y) < 1.0 - tolerance) almostLineInY = false;
      }

      if (almostLineInX) return "X";
      if (almostLineInY) return "Y";
      return "None";
    }

    /// <summary>
    /// 4개의 점이 사각형(Quadrilateral)을 구성하는지 판별합니다.
    /// (Python 원본 로직: 모든 쌍의 거리의 합 - 단일 거리 > 단일 거리)
    /// </summary>
    private static bool IsQuadrilateral(List<LiftingNode> nodes)
    {
      var distances = new List<double>();

      for (int i = 0; i < nodes.Count; i++)
      {
        for (int j = i + 1; j < nodes.Count; j++)
        {
          double dist = (nodes[i].Pos - nodes[j].Pos).Magnitude();
          distances.Add(dist);
        }
      }

      double sum = distances.Sum();
      // 모든 단일 거리에 대해, 전체 합에서 자신을 뺀 값이 자신보다 커야 함 (다각형 성립 조건)
      return distances.All(d => (sum - d) > d);
    }
  }
}
