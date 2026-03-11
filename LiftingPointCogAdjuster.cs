using System;
using System.Collections.Generic;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingPointCogAdjuster
  {
    /// <summary>
    /// # HookTrolley-05
    /// 권상 포인트 그룹이 2개일 때, 계산된 정점 중 하나를 무게중심(COG) 축에 맞춰 
    /// 미세 조정(최대 300mm 이하)합니다.
    /// </summary>
    public static void Run(List<LiftingGroup> liftingGroups, Point3D cog, PipelineLogger logger, bool debugPrint = true)
    {
      // 그룹이 2개일 때만 수행하는 로직
      if (liftingGroups.Count != 2) return;

      if (debugPrint) logger.LogInfo("\n[Stage 5] 권상 위치 COG(무게중심) 기준 미세 보정 (Hook to COG) 시작");

      var p1 = liftingGroups[0].CalculatedTopPoint;
      var p2 = liftingGroups[1].CalculatedTopPoint;

      double xDiff = Math.Abs(p1.X - p2.X);
      double yDiff = Math.Abs(p1.Y - p2.Y);

      double[] r = new double[2];
      double[] absMag = new double[2];

      if (xDiff > yDiff)
      {
        // 두 점이 X축 방향으로 길게 배치된 경우 -> Y축을 보정
        r[0] = GetYOnLine(p2, cog, p1.X); // p1을 p2-COG 연장선으로 보냈을 때의 Y값
        r[1] = GetYOnLine(p1, cog, p2.X); // p2를 p1-COG 연장선으로 보냈을 때의 Y값
        absMag[0] = Math.Abs(r[0] - p1.Y);
        absMag[1] = Math.Abs(r[1] - p2.Y);
      }
      else
      {
        // 두 점이 Y축 방향으로 길게 배치된 경우 -> X축을 보정
        r[0] = GetXOnLine(p2, cog, p1.Y); // p1을 p2-COG 연장선으로 보냈을 때의 X값
        r[1] = GetXOnLine(p1, cog, p2.Y); // p2를 p1-COG 연장선으로 보냈을 때의 X값
        absMag[0] = Math.Abs(r[0] - p1.X);
        absMag[1] = Math.Abs(r[1] - p2.X);
      }

      // [파이썬 원본 제약조건] 두 보정량 모두 300mm 이하일 때만 수행
      if (absMag[0] <= 300.0 && absMag[1] <= 300.0)
      {
        // 더 적게 움직이는 점을 선택하여 보정
        if (absMag[0] < absMag[1])
        {
          if (xDiff > yDiff)
          {
            logger.LogWarning($"  -> [보정됨] 권상 포인트(Group {liftingGroups[0].GroupId})의 Y좌표를 {p1.Y:F1} 에서 COG 라인 근처인 {r[0]:F1}(으)로 이동");
            p1.Y = r[0];
          }
          else
          {
            logger.LogWarning($"  -> [보정됨] 권상 포인트(Group {liftingGroups[0].GroupId})의 X좌표를 {p1.X:F1} 에서 COG 라인 근처인 {r[0]:F1}(으)로 이동");
            p1.X = r[0];
          }
          liftingGroups[0].CalculatedTopPoint = p1; // 구조체(struct)이므로 덮어쓰기
        }
        else if (absMag[0] > absMag[1])
        {
          if (xDiff > yDiff)
          {
            logger.LogWarning($"  -> [보정됨] 권상 포인트(Group {liftingGroups[1].GroupId})의 Y좌표를 {p2.Y:F1} 에서 COG 라인 근처인 {r[1]:F1}(으)로 이동");
            p2.Y = r[1];
          }
          else
          {
            logger.LogWarning($"  -> [보정됨] 권상 포인트(Group {liftingGroups[1].GroupId})의 X좌표를 {p2.X:F1} 에서 COG 라인 근처인 {r[1]:F1}(으)로 이동");
            p2.X = r[1];
          }
          liftingGroups[1].CalculatedTopPoint = p2;
        }
        else if (debugPrint)
        {
          logger.LogInfo("  -> 이동량이 동일하여 보정을 수행하지 않습니다.");
        }
      }
      else
      {
        if (debugPrint) logger.LogInfo("  -> 보정 이동량(Deviation)이 300mm를 초과하여 임의 수정을 생략합니다.");
      }
    }

    // 점 A와 점 B를 지나는 직선에서 특정 X값에 대한 Y값 찾기
    private static double GetYOnLine(Point3D pA, Point3D pB, double targetX)
    {
      double dx = pB.X - pA.X;
      if (Math.Abs(dx) < 1e-9) return pA.Y; // 무한대(0으로 나누기) 방지
      double slope = (pB.Y - pA.Y) / dx;
      return slope * (targetX - pA.X) + pA.Y;
    }

    // 점 A와 점 B를 지나는 직선에서 특정 Y값에 대한 X값 찾기
    private static double GetXOnLine(Point3D pA, Point3D pB, double targetY)
    {
      double dy = pB.Y - pA.Y;
      if (Math.Abs(dy) < 1e-9) return pA.X; // 무한대(0으로 나누기) 방지
      double slope = (pB.X - pA.X) / dy;
      return slope * (targetY - pA.Y) + pA.X;
    }
  }
}
