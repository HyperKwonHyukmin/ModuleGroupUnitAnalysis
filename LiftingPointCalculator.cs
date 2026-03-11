using System;
using System.Collections.Generic;
using System.Linq;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Utils;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingPointCalculator
  {
    /// <summary>
    /// # HookTrolley-04
    /// 권상 방식(Hydro=0, Goliat=1)과 형태에 따라 Hook/Trolley의 3D 정점 좌표를 계산합니다.
    /// </summary>
    public static bool Run(List<LiftingGroup> liftingGroups, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 4] 권상 정점(Hook/Trolley) 3D 좌표 수학 역산 시작");

      bool isAllCalculated = true;

      foreach (var group in liftingGroups)
      {
        // 줄 길이: 입력값(m)을 mm로 변환 후 100mm(여유분) 차감
        double valL = group.LineLength * 1000.0 - 100.0;

        try
        {
          if (group.LiftingMethod == 0) // Hydro (Hook)
          {
            group.CalculatedTopPoint = CalculateHookLocation(group, valL);
          }
          else if (group.LiftingMethod == 1) // Goliat (Trolley)
          {
            group.CalculatedTopPoint = CalculateTrolleyLocation(group, valL);
          }

          if (debugPrint)
          {
            string methodStr = group.LiftingMethod == 0 ? "Hook" : "Trolley";
            logger.LogInfo($"  -> [Group {group.GroupId}] {methodStr} 정점 계산 완료: X={group.CalculatedTopPoint.X:F1}, Y={group.CalculatedTopPoint.Y:F1}, Z={group.CalculatedTopPoint.Z:F1}");
          }
        }
        catch (Exception ex)
        {
          logger.LogError($"  -> [Group {group.GroupId}] 좌표 계산 중 수학적 오류 발생! (사유: {ex.Message})");
          logger.LogError($"     입력된 줄 길이({group.LineLength}m)가 너무 짧거나, 좌표가 비정상적일 수 있습니다.");
          isAllCalculated = false;
        }
      }

      if (isAllCalculated && debugPrint)
      {
        logger.LogSuccess("4단계 : 권상 포인트(Hook/Trolley) 계산 완료");
      }

      return isAllCalculated;
    }

    // =======================================================================
    // Hydro (Hook) 위치 계산
    // =======================================================================
    private static Point3D CalculateHookLocation(LiftingGroup group, double valL)
    {
      var nodes = group.Nodes;

      if (nodes.Count == 4 && group.ShapeType == "4개점 사각형 형태")
      {
        var p0 = nodes[0].Pos; var p1 = nodes[1].Pos;
        var p2 = nodes[2].Pos; var p3 = nodes[3].Pos;

        Vector3D v12 = p1 - p0;
        Vector3D v23 = p2 - p1;

        // 중심점 C
        Point3D C = new Point3D((p0.X + p1.X + p2.X + p3.X) / 4.0, (p0.Y + p1.Y + p2.Y + p3.Y) / 4.0, (p0.Z + p1.Z + p2.Z + p3.Z) / 4.0);

        double valS = (C - p0).Magnitude();
        if (valL < valS) throw new Exception("지정된 줄 길이가 묶이는 지점 간의 대각선 길이보다 짧습니다.");

        double valH = Math.Sqrt(valL * valL - valS * valS);

        // 수직 방향 단위 벡터 (위로 향하도록 Z축 부호 보정)
        Vector3D unitH = Vector3dUtils.Cross(v12, v23).Normalize();
        if (unitH.Z < 0) unitH = unitH * -1.0;

        return C + (unitH * valH);
      }
      else if (nodes.Count == 4 && group.ShapeType.Contains("일직선"))
      {
        // 일직선일 경우 가운데 2점만 사용하여 2개점 로직으로 넘김
        return CalculateHook2Points(nodes[1].Pos, nodes[2].Pos, valL);
      }
      else if (nodes.Count == 3)
      {
        var p0 = nodes[0].Pos; var p1 = nodes[1].Pos; var p2 = nodes[2].Pos;
        Vector3D v12 = p1 - p0; Vector3D v13 = p2 - p0; Vector3D v23 = p2 - p1;

        Point3D K = new Point3D((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0, (p0.Z + p1.Z) / 2.0);

        double crossMagSq = Math.Pow(Vector3dUtils.Cross(v13, v12).Magnitude(), 2);
        Vector3D term2 = Vector3dUtils.Cross(v12, Vector3dUtils.Cross(v13, v12));

        double scalar = 0.5 * (v13.Dot(v13) - v13.Dot(v12)) / crossMagSq;
        Point3D C = K + (term2 * scalar);

        double valS = (C - p0).Magnitude();
        if (valL < valS) throw new Exception("줄 길이가 세 점의 외심 반경보다 짧습니다.");
        double valH = Math.Sqrt(valL * valL - valS * valS);

        Vector3D unitH = Vector3dUtils.Cross(v12, v23).Normalize();
        if (unitH.Z < 0) unitH = unitH * -1.0;

        return C + (unitH * valH);
      }
      else if (nodes.Count == 2)
      {
        return CalculateHook2Points(nodes[0].Pos, nodes[1].Pos, valL);
      }

      throw new Exception("지원되지 않는 형태입니다.");
    }

    private static Point3D CalculateHook2Points(Point3D p0, Point3D p1, double valL)
    {
      Point3D K = new Point3D((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0, (p0.Z + p1.Z) / 2.0);
      Vector3D v12 = K - p0;
      Vector3D unit12 = v12.Normalize();

      double v12Mag = v12.Magnitude();
      if (valL < v12Mag) throw new Exception("줄 길이가 두 점 사이의 반경보다 짧습니다.");

      double valH = Math.Sqrt(valL * valL - v12Mag * v12Mag);
      double xyDenom = Math.Sqrt(unit12.X * unit12.X + unit12.Y * unit12.Y);

      // Z축 방향으로 세우기 위한 특수 벡터
      Vector3D unitH = new Vector3D(
          -unit12.X * unit12.Z / xyDenom,
          -unit12.Y * unit12.Z / xyDenom,
          xyDenom
      );

      // xyDenom이 0에 가까운 경우(완전 수직선) 예외 처리
      if (xyDenom < 1e-6) unitH = new Vector3D(0, 0, 1);

      return K + (unitH * valH);
    }

    // =======================================================================
    // Goliat (Trolley) 위치 계산
    // =======================================================================
    private static Point3D CalculateTrolleyLocation(LiftingGroup group, double valL)
    {
      var nodes = group.Nodes;

      if (nodes.Count == 2)
      {
        var p0 = nodes[0].Pos; var p1 = nodes[1].Pos;
        Point3D K = new Point3D((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0, Math.Min(p0.Z, p1.Z) + valL);
        return K;
      }
      else if (nodes.Count == 4)
      {
        if (group.ShapeType == "4개점 사각형 형태")
        {
          // 트롤리 사각형의 경우 훅과 동일한 계산 사용
          return CalculateHookLocation(group, valL);
        }
        else if (group.ShapeType.Contains("일직선"))
        {
          var p0 = nodes[0].Pos; var p1 = nodes[1].Pos;
          var p2 = nodes[2].Pos; var p3 = nodes[3].Pos;
          Point3D K = new Point3D(
              (p0.X + p1.X + p2.X + p3.X) / 4.0,
              (p0.Y + p1.Y + p2.Y + p3.Y) / 4.0,
              new[] { p0.Z, p1.Z, p2.Z, p3.Z }.Min() + valL);
          return K;
        }
      }

      throw new Exception("트롤리(Goliat) 연산에 지원되지 않는 노드 개수/형태입니다.");
    }
  }
}
