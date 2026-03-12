using System;
using System.Collections.Generic;
using System.Linq;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Model.Geometry;
using ModuleGroupUnitAnalysis.Logger;
using ModuleGroupUnitAnalysis.Utils;

namespace ModuleGroupUnitAnalysis.Pipeline.Modifiers
{
  public static class LiftingInterferenceInspector
  {
    public static bool Run(List<LiftingGroup> liftingGroups, FeModelContext context, PipelineLogger logger, bool debugPrint = true)
    {
      if (debugPrint) logger.LogInfo("\n[Stage 9-2] 와이어-구조물 물리적 간섭(Interference) 검사 시작");

      bool isAllClear = true;
      double wireRadius = 20.0;

      foreach (var group in liftingGroups)
      {
        var topPt = group.CalculatedTopPoint;

        foreach (var lugNode in group.Nodes)
        {
          var lugPt = lugNode.Pos;
          double wireLength = (topPt - lugPt).Magnitude();

          foreach (var kvp in context.Elements)
          {
            var elem = kvp.Value;
            if (elem.NodeIDs.Count < 2) continue;
            if (elem.NodeIDs.Contains(lugNode.NodeID)) continue;

            var pA = context.Nodes[elem.NodeIDs.First()];
            var pB = context.Nodes[elem.NodeIDs.Last()];

            double shortestDist = ShortestDistanceBetweenSegments(lugPt, topPt, pA, pB, out double wireParam);

            double strucRadius = 0.0;
            if (context.Properties.TryGetValue(elem.PropertyID, out var prop))
            {
              strucRadius = PropertyDimensionHelper.GetMaxCrossSectionDim(prop) / 2.0;
            }

            double safeMargin = wireRadius + strucRadius + 10.0;
            double distFromLug = wireParam * wireLength;

            if (shortestDist < safeMargin && distFromLug > (strucRadius + 150.0))
            {
              if (debugPrint)
              {
                // ★ [수정] LogError -> LogWarning 으로 변경 (빨간색 -> 노란색)
                logger.LogWarning($"  -> [간섭 주의] Group {group.GroupId}의 와이어가 구조물 E{kvp.Key}와 간섭됩니다! (최단거리: {shortestDist:F1}mm / 경계기준: {safeMargin:F1}mm / 발생위치: 러그에서 {distFromLug:F1}mm 지점)");
              }
              isAllClear = false;
            }
          }
        }
      }

      if (debugPrint)
      {
        if (isAllClear) logger.LogSuccess("9-2단계 : 와이어 간섭 검사 통과 (구조물 관통 없음)");
        // ★ [수정] LogError -> LogWarning 으로 변경
        else logger.LogWarning("9-2단계 : 와이어가 구조물과 간섭(충돌)하는 구간이 발견되었습니다. (스프레더 바 적용 고려 요망)");
      }

      return isAllClear;
    }

    // ... (이하 ShortestDistanceBetweenSegments 메서드는 기존과 100% 동일) ...
    private static double ShortestDistanceBetweenSegments(Point3D p1, Point3D p2, Point3D p3, Point3D p4, out double wireParam)
    {
      Vector3D u = p2 - p1;
      Vector3D v = p4 - p3;
      Vector3D w = p1 - p3;

      double a = u.Dot(u);
      double b = u.Dot(v);
      double c = v.Dot(v);
      double d = u.Dot(w);
      double e = v.Dot(w);

      double D = a * c - b * b;
      double sc, sN, sD = D;
      double tc, tN, tD = D;

      if (D < 1e-8)
      {
        sN = 0.0; sD = 1.0; tN = e; tD = c;
      }
      else
      {
        sN = (b * e - c * d); tN = (a * e - b * d);
        if (sN < 0.0) { sN = 0.0; tN = e; tD = c; }
        else if (sN > sD) { sN = sD; tN = e + b; tD = c; }
      }

      if (tN < 0.0)
      {
        tN = 0.0;
        if (-d < 0.0) sN = 0.0;
        else if (-d > a) sN = sD;
        else { sN = -d; sD = a; }
      }
      else if (tN > tD)
      {
        tN = tD;
        if ((-d + b) < 0.0) sN = 0.0;
        else if ((-d + b) > a) sN = sD;
        else { sN = (-d + b); sD = a; }
      }

      sc = (Math.Abs(sN) < 1e-8 ? 0.0 : sN / sD);
      tc = (Math.Abs(tN) < 1e-8 ? 0.0 : tN / tD);

      wireParam = sc;

      Vector3D dP = w + (u * sc) - (v * tc);
      return dP.Magnitude();
    }
  }
}
