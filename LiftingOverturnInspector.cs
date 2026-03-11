using System;
using System.Collections.Generic;
using System.Linq;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingOverturnInspector
  {
    // 부동소수점 연산 및 미세 오차 허용치 (면적/거리 비교용)
    private const double TOLERANCE = 1.0; // 1.0mm 수준의 오차는 일치하는 것으로 간주

    /// <summary>
    /// # HookTrolley-06 (Overturn)
    /// 계산된 Trolley/Hook 정점들의 XY 평면상 다각형 내부에 COG(무게중심)가 존재하는지 확인하여 전도 위험을 평가합니다.
    /// </summary>
    public static bool Run(List<LiftingGroup> liftingGroups, Point3D cog, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 6] 자세 안정성(전도/Overturn) 평가 시작");

      // 모든 그룹의 계산된 정점(Top Point) 추출
      var topPoints = liftingGroups.Select(g => g.CalculatedTopPoint).ToList();

      bool isSafe = false;

      try
      {
        if (topPoints.Count == 2)
        {
          isSafe = IsPointOnLine(cog, topPoints[0], topPoints[1]);
        }
        else if (topPoints.Count == 3)
        {
          isSafe = IsPointInTriangle(cog, topPoints[0], topPoints[1], topPoints[2]);
        }
        else if (topPoints.Count == 4)
        {
          isSafe = IsPointInRectangle(cog, topPoints);
        }
        else
        {
          logger.LogWarning($"  -> 권상 포인트 개수가 {topPoints.Count}개여서 안정성 평가를 수행할 수 없습니다.");
          return true; // 기본적으로 통과시킴
        }

        if (!isSafe)
        {
          throw new Exception("COG(무게중심)가 권상 지점들이 형성하는 다각형 범위를 벗어났습니다.");
        }
      }
      catch (Exception ex)
      {
        logger.LogError($"  [검증 실패] 자세 안정성 평가: Fail");
        logger.LogError($"     -> 사유: 주어진 권상 위치에서 전도(Overturn)가 발생합니다! ({ex.Message})");
        return false;
      }

      if (debugPrint)
      {
        logger.LogInfo("  -> [OK] 무게중심(COG)이 권상 다각형 내부에 안정적으로 위치해 있습니다.");
        logger.LogSuccess("6단계 : 자세 안정성 평가 통과 (전도 위험 없음)");
      }

      return true;
    }

    // =======================================================================
    // 2D 기하학 수학 함수들 (XY 평면 투영 기준)
    // =======================================================================

    /// <summary>
    /// 점이 선분 위에 존재하는지 판단 (dist(A, P) + dist(B, P) == dist(A, B))
    /// </summary>
    private static bool IsPointOnLine(Point3D pt, Point3D p1, Point3D p2)
    {
      double totalDist = Dist2D(p1, p2);
      double dist1 = Dist2D(pt, p1);
      double dist2 = Dist2D(pt, p2);

      return Math.Abs(totalDist - (dist1 + dist2)) <= TOLERANCE;
    }

    /// <summary>
    /// 점이 삼각형 내부에 존재하는지 판단 (전체 면적 == 세 조각 면적의 합)
    /// </summary>
    private static bool IsPointInTriangle(Point3D pt, Point3D p1, Point3D p2, Point3D p3)
    {
      double mainArea = AreaOfTriangle(p1, p2, p3);
      double area1 = AreaOfTriangle(pt, p2, p3);
      double area2 = AreaOfTriangle(p1, pt, p3);
      double area3 = AreaOfTriangle(p1, p2, pt);

      return Math.Abs(mainArea - (area1 + area2 + area3)) <= TOLERANCE;
    }

    /// <summary>
    /// 점이 사각형 내부에 존재하는지 판단
    /// (Z자 꼬임 방지를 위해 순환 정렬 후, 두 개의 삼각형으로 쪼개서 검사)
    /// </summary>
    private static bool IsPointInRectangle(Point3D pt, List<Point3D> rect)
    {
      // 중심점(Centroid)을 기준으로 각도를 구해 테두리를 따라 순환 배치
      double cx = rect.Average(p => p.X);
      double cy = rect.Average(p => p.Y);
      var ordered = rect.OrderBy(p => Math.Atan2(p.Y - cy, p.X - cx)).ToList();

      // 사각형을 대각선으로 갈라 두 개의 삼각형 중 하나에라도 속하면 내부로 판정
      bool inTri1 = IsPointInTriangle(pt, ordered[0], ordered[1], ordered[2]);
      bool inTri2 = IsPointInTriangle(pt, ordered[0], ordered[2], ordered[3]);

      return inTri1 || inTri2;
    }

    /// <summary>
    /// 2D(XY 평면) 상의 두 점 사이 거리
    /// </summary>
    private static double Dist2D(Point3D p1, Point3D p2)
    {
      return Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));
    }

    /// <summary>
    /// 2D(XY 평면) 상의 삼각형 면적 (신발끈 공식 / Cross Product의 Z성분 활용)
    /// </summary>
    private static double AreaOfTriangle(Point3D p1, Point3D p2, Point3D p3)
    {
      return 0.5 * Math.Abs(
          p1.X * (p2.Y - p3.Y) +
          p2.X * (p3.Y - p1.Y) +
          p3.X * (p1.Y - p2.Y)
      );
    }
  }
}
