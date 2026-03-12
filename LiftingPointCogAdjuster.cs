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
    /// 모델의 무게중심(COG)을 확인하고, 
    /// 권상 포인트 그룹이 2개일 때 계산된 정점 중 하나를 COG 축에 맞춰 미세 조정합니다.
    /// </summary>
    public static void Run(List<LiftingGroup> liftingGroups, Point3D cog, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 5] 권상 위치 COG(무게중심) 기준 평가 및 미세 보정 시작");

      // ★ [추가된 부분] 권상 포인트 개수와 무관하게 항상 COG 위치를 출력하여 안심시킵니다.
      if (debugPrint) logger.LogInfo($"  -> 현재 모델의 무게중심(COG) 위치: X={cog.X:F1}, Y={cog.Y:F1}, Z={cog.Z:F1}");

      // 그룹이 2개가 아니면 억지로 보정(Shift)할 필요가 없으므로 알림만 띄우고 넘깁니다.
      if (liftingGroups.Count != 2)
      {
        if (debugPrint)
        {
          logger.LogInfo($"  -> 권상 포인트 그룹이 {liftingGroups.Count}개이므로 좌표 임의 보정(Hook to COG)은 생략합니다.");
          logger.LogSuccess("5단계 : 무게중심(COG) 위치 확인 완료");
        }
        return;
      }

      // -------------------------------------------------------------
      // 아래부터는 2점 권상(2 Groups)일 경우에만 작동하는 미세 보정 로직
      // -------------------------------------------------------------
      var p1 = liftingGroups[0].CalculatedTopPoint;
      var p2 = liftingGroups[1].CalculatedTopPoint;

      double xDiff = Math.Abs(p1.X - p2.X);
      double yDiff = Math.Abs(p1.Y - p2.Y);

      double[] r = new double[2];
      double[] absMag = new double[2];

      if (xDiff > yDiff)
      {
        r[0] = GetYOnLine(p2, cog, p1.X);
        r[1] = GetYOnLine(p1, cog, p2.X);
        absMag[0] = Math.Abs(r[0] - p1.Y);
        absMag[1] = Math.Abs(r[1] - p2.Y);
      }
      else
      {
        r[0] = GetXOnLine(p2, cog, p1.Y);
        r[1] = GetXOnLine(p1, cog, p2.Y);
        absMag[0] = Math.Abs(r[0] - p1.X);
        absMag[1] = Math.Abs(r[1] - p2.X);
      }

      // [파이썬 원본 제약조건] 두 보정량 모두 300mm 이하일 때만 수행
      if (absMag[0] <= 300.0 && absMag[1] <= 300.0)
      {
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

      if (debugPrint) logger.LogSuccess("5단계 : 무게중심(COG) 위치 확인 및 미세 보정 완료");
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
