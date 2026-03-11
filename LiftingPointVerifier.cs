using System;
using System.Collections.Generic;
using System.Linq;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingPointVerifier
  {
    // 실무를 고려한 넉넉한 허용 오차 (Tolerance)
    private const double MAX_Z_DIFF_TOLERANCE = 200.0;     // 권상 포인트 간 최대 높이(Z) 차이 허용치 (mm)
    private const double RECT_ANGLE_DEVIATION_TOLERANCE = 20.0; // 사각형 내각의 90도 대비 최대 편차 허용치 (도)
    private const double MAX_TRIANGLE_ANGLE = 120.0;       // 삼각형이 너무 납작해지는 것을 방지하는 최대 각도 (도)

    /// <summary>
    /// # HookTrolley-03
    /// LiftingPoint들이 이루는 형태가 기하학적으로 타당한지 상세 수치와 함께 검증합니다.
    /// </summary>
    public static bool Run(List<LiftingGroup> liftingGroups, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 3] 유닛 포인트 형태 유효성 상세 검증 (Verifier) 시작");

      bool isAllValid = true;
      int setIndex = 1;

      foreach (var group in liftingGroups)
      {
        if (group.Nodes.Count == 4 && group.ShapeType == "4개점 사각형 형태")
        {
          // 1. 대각선 꼬임 방지를 위한 외곽선 순환 정렬 (Atan2)
          double cx = group.Nodes.Average(n => n.Pos.X);
          double cy = group.Nodes.Average(n => n.Pos.Y);
          var orderedNodes = group.Nodes.OrderBy(n => Math.Atan2(n.Pos.Y - cy, n.Pos.X - cx)).ToList();
          group.Nodes = orderedNodes;

          // 2. Z축 단차 계산
          double maxZ = orderedNodes.Max(n => n.Pos.Z);
          double minZ = orderedNodes.Min(n => n.Pos.Z);
          double zDiff = Math.Abs(maxZ - minZ);

          // 3. 내각 계산
          double[] angles = new double[4];
          angles[0] = GetAngleDeg(orderedNodes[3].Pos, orderedNodes[0].Pos, orderedNodes[1].Pos);
          angles[1] = GetAngleDeg(orderedNodes[0].Pos, orderedNodes[1].Pos, orderedNodes[2].Pos);
          angles[2] = GetAngleDeg(orderedNodes[1].Pos, orderedNodes[2].Pos, orderedNodes[3].Pos);
          angles[3] = GetAngleDeg(orderedNodes[2].Pos, orderedNodes[3].Pos, orderedNodes[0].Pos);
          double maxDeviation = angles.Max(a => Math.Abs(a - 90.0));

          // ★ 상세 정보 로깅 (통과 여부와 상관없이 무조건 출력)
          if (debugPrint)
          {
            logger.LogInfo($"  ▶ [SET{setIndex} : 사각형 상세 정보]");
            logger.LogInfo($"     - 노드 순서 : {string.Join(" -> ", orderedNodes.Select(n => n.NodeID))}");
            logger.LogInfo($"     - Z축 단차  : {zDiff:F1} mm (허용치: {MAX_Z_DIFF_TOLERANCE} mm)");
            logger.LogInfo($"     - 내각 분 분포 : {angles[0]:F1}°, {angles[1]:F1}°, {angles[2]:F1}°, {angles[3]:F1}°");
            logger.LogInfo($"     - 직각 편차 : {maxDeviation:F1}° (허용치: {RECT_ANGLE_DEVIATION_TOLERANCE} °)");
          }

          // 4. 검증 로직
          if (zDiff > MAX_Z_DIFF_TOLERANCE)
          {
            logger.LogError($"     [Fail] 네 점의 높이(Z) 차이가 너무 큽니다!");
            isAllValid = false;
          }
          if (maxDeviation > RECT_ANGLE_DEVIATION_TOLERANCE)
          {
            logger.LogError($"     [Fail] 사각형이 직사각형에서 너무 많이 일그러졌습니다!");
            isAllValid = false;
          }
        }
        else if (group.Nodes.Count == 3)
        {
          double[] angles = new double[3];
          angles[0] = GetAngleDeg(group.Nodes[2].Pos, group.Nodes[0].Pos, group.Nodes[1].Pos);
          angles[1] = GetAngleDeg(group.Nodes[0].Pos, group.Nodes[1].Pos, group.Nodes[2].Pos);
          angles[2] = GetAngleDeg(group.Nodes[1].Pos, group.Nodes[2].Pos, group.Nodes[0].Pos);
          double maxAngle = angles.Max();

          if (debugPrint)
          {
            logger.LogInfo($"  ▶ [SET{setIndex} : 삼각형 상세 정보]");
            logger.LogInfo($"     - 노드 순서 : {string.Join(" -> ", group.Nodes.Select(n => n.NodeID))}");
            logger.LogInfo($"     - 내각 분포 : {angles[0]:F1}°, {angles[1]:F1}°, {angles[2]:F1}°");
            logger.LogInfo($"     - 최대 내각 : {maxAngle:F1}° (허용치: {MAX_TRIANGLE_ANGLE} °)");
          }

          if (maxAngle > MAX_TRIANGLE_ANGLE)
          {
            logger.LogError($"     [Fail] 삼각형이 너무 납작한 둔각 삼각형에 가깝습니다!");
            isAllValid = false;
          }
        }
        else if (debugPrint)
        {
          // 4개점 일직선 또는 2개점인 경우
          logger.LogInfo($"  ▶ [SET{setIndex} : {group.ShapeType}]");
          logger.LogInfo($"     - 노드 순서 : {string.Join(" -> ", group.Nodes.Select(n => n.NodeID))}");
          logger.LogInfo($"     - (각도 검증이 불필요한 형태입니다)");
        }

        setIndex++;
      }

      if (debugPrint)
      {
        if (isAllValid)
          logger.LogSuccess("\n[OK] 3단계 : 모든 유닛 포인트의 기하학적 형태가 안전 기준을 통과했습니다.");
        else
          logger.LogError("\n[ERROR] 3단계 : 일부 유닛 포인트가 기하학적 안전 기준을 통과하지 못했습니다.");
      }

      return isAllValid;
    }

    /// <summary>
    /// 
    /// 세 점 (A, B, C)가 이루는 B 꼭지점의 내각을 '도(Degree)' 단위로 반환합니다.
    /// </summary>
    private static double GetAngleDeg(Point3D pA, Point3D pB, Point3D pC)
    {
      Vector3D vBA = pA - pB;
      Vector3D vBC = pC - pB;

      double magA = vBA.Magnitude();
      double magC = vBC.Magnitude();

      if (magA < 1e-9 || magC < 1e-9) return 0.0;

      double dot = vBA.Dot(vBC);
      double cosTheta = dot / (magA * magC);

      cosTheta = Math.Max(-1.0, Math.Min(1.0, cosTheta));

      return Math.Acos(cosTheta) * (180.0 / Math.PI);
    }
  }
}
