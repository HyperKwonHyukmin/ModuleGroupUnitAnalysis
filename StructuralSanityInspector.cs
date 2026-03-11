using System;
using System.Collections.Generic;
using System.Linq;
using ModuleGroupUnitAnalysis.Model.Entities;
using ModuleGroupUnitAnalysis.Utils;
using ModuleGroupUnitAnalysis.Logger;

namespace ModuleGroupUnitAnalysis.Pipeline.Preprocess
{
  public static class StructuralSanityInspector
  {
    /// <summary>
    /// FE 모델이 Nastran 해석에 적합한지 위상학적/기하학적 무결성을 사전 검증하고 힐링합니다.
    /// </summary>
    public static bool Run(FeModelContext context, PipelineLogger logger, bool pipelineDebug = true, bool verboseDebug = false)
    {
      if (pipelineDebug)
      {
        logger.LogInfo("\n==================================================");
        logger.LogInfo("      [Pre-Check] FE 모델 건전성 검증 및 힐링 시작 ");
        logger.LogInfo("==================================================");
      }

      bool isFatalFree = true;

      // 1. 데이터 무결성 검사 (존재하지 않는 노드/속성 참조 여부 검사)
      InspectIntegrity(context, logger, pipelineDebug, verboseDebug);

      // 2. 기하학적 형상 검사 (미세 부재 검사)
      double shortThreshold = 1.0; // 1mm 미만 요소 경고
      InspectGeometry(context, shortThreshold, logger, pipelineDebug, verboseDebug);

      // 3. 요소 중복 검사 (동일 노드를 공유하는 중복 1D 요소 제거)
      InspectDuplicate(context, logger, pipelineDebug, verboseDebug);

      // 4. 위상 연결성 검사 (메인 덩어리 외에 허공에 뜬 부재가 있는지)
      InspectIsolation(context, logger, pipelineDebug);

      // 5. 강체 무결성 검사 (DEP가 없는 빈 껍데기 강체 삭제)
      InspectRigidIntegrity(context, logger, pipelineDebug, verboseDebug);

      // 6. 강체 종속성 검사 (Nastran FATAL 6202 주범: 다중 종속, 순환 종속)
      if (!InspectRigidDependencies(context, logger, pipelineDebug))
      {
        isFatalFree = false; // 치명적 에러이므로 파프라인 중단 플래그 설정
      }

      if (pipelineDebug)
      {
        if (isFatalFree) logger.LogSuccess("[OK] FE 모델 건전성 검증 완료: 해석에 적합한 모델입니다.");
        else logger.LogError("\n[ERROR] FE 모델 건전성 검증 실패: FATAL 오류를 유발할 수 있는 심각한 결함이 발견되었습니다.");
      }

      return isFatalFree;
    }

    private static void InspectIntegrity(FeModelContext context, PipelineLogger logger, bool debug, bool verbose)
    {
      var invalidElements = new List<int>();
      foreach (var kvp in context.Elements)
      {
        var ele = kvp.Value;
        bool valid = ele.NodeIDs.All(nid => context.Nodes.Contains(nid)) && context.Properties.Contains(ele.PropertyID);
        if (!valid) invalidElements.Add(kvp.Key);
      }

      foreach (var id in invalidElements) context.Elements.Remove(id);

      if (debug)
      {
        if (invalidElements.Count == 0) logger.LogInfo("  [OK] 01 - 데이 무결성 : 모든 요소가 유효한 노드/속성을 참조함.");
        else
        {
          logger.LogWarning($"  [복구] 01 - 데이터 무결성 : 불량 요소 {invalidElements.Count}개 삭제됨.");
          if (verbose) logger.LogInfo($"       삭제 IDs: {SummarizeIds(invalidElements, 10)}");
        }
      }
    }

    private static void InspectGeometry(FeModelContext context, double threshold, PipelineLogger logger, bool debug, bool verbose)
    {
      var shortEles = new List<int>();
      foreach (var kvp in context.Elements)
      {
        if (kvp.Value.NodeIDs.Count == 2)
        {
          var p1 = context.Nodes[kvp.Value.NodeIDs[0]];
          var p2 = context.Nodes[kvp.Value.NodeIDs[1]];
          if (Point3dUtils.Dist(p1, p2) < threshold) shortEles.Add(kvp.Key);
        }
      }

      if (debug)
      {
        if (shortEles.Count == 0) logger.LogInfo($"  [OK] 02 - 기하 형상 : {threshold}mm 미만의 짧은 1D 요소 없음.");
        else
        {
          logger.LogWarning($"  [주의] 02 - 기하 형상 : 미세 요소(길이<{threshold}mm) {shortEles.Count}개 발견됨.");
          if (verbose) logger.LogInfo($"       발견 IDs: {SummarizeIds(shortEles, 10)}");
        }
      }
    }

    private static void InspectDuplicate(FeModelContext context, PipelineLogger logger, bool debug, bool verbose)
    {
      var seenNodes = new Dictionary<string, int>();
      var toRemove = new List<int>();

      foreach (var kvp in context.Elements)
      {
        // 순서 무관한 노드 해시 키 생성
        var sortedNodes = kvp.Value.NodeIDs.OrderBy(id => id).ToList();
        string key = string.Join(",", sortedNodes);

        if (seenNodes.ContainsKey(key)) toRemove.Add(kvp.Key);
        else seenNodes[key] = kvp.Key;
      }

      foreach (var id in toRemove) context.Elements.Remove(id);

      if (debug)
      {
        if (toRemove.Count == 0) logger.LogInfo("  [OK] 03 - 요소 중복 : 동일 노드를 잇 중복 요소 없음.");
        else
        {
          logger.LogWarning($"  [복구] 03 - 요소 중복 : 완전히 겹치는 중복 요소 {toRemove.Count}개 영구 삭제됨.");
          if (verbose) logger.LogInfo($"       삭제 IDs: {SummarizeIds(toRemove, 10)}");
        }
      }
    }

    private static void InspectIsolation(FeModelContext context, PipelineLogger logger, bool debug)
    {
      // 노드 ID 기반으로 그래프 연결성(BFS) 검사
      var adj = new Dictionary<int, HashSet<int>>();
      foreach (var kvp in context.Elements)
      {
        var nids = kvp.Value.NodeIDs;
        for (int i = 0; i < nids.Count; i++)
        {
          if (!adj.ContainsKey(nids[i])) adj[nids[i]] = new HashSet<int>();
          for (int j = 0; j < nids.Count; j++)
          {
            if (i != j) adj[nids[i]].Add(nids[j]);
          }
        }
      }

      // 강체(RBE)도 다리로 작용하므로 연결해줌
      foreach (var kvp in context.Rigids)
      {
        var rbe = kvp.Value; // ★ KVP에서 Value를 명시적으로 꺼내도록 변경

        if (!adj.ContainsKey(rbe.IndependentNodeID)) adj[rbe.IndependentNodeID] = new HashSet<int>();
        foreach (int dep in rbe.DependentNodeIDs)
        {
          adj[rbe.IndependentNodeID].Add(dep);
          if (!adj.ContainsKey(dep)) adj[dep] = new HashSet<int>();
          adj[dep].Add(rbe.IndependentNodeID);
        }
      }

      var visited = new HashSet<int>();
      int groupCount = 0;

      foreach (int startNode in adj.Keys)
      {
        if (visited.Contains(startNode)) continue;

        groupCount++;
        var q = new Queue<int>();
        q.Enqueue(startNode);
        visited.Add(startNode);

        while (q.Count > 0)
        {
          int curr = q.Dequeue();
          foreach (int next in adj[curr])
          {
            if (!visited.Contains(next))
            {
              visited.Add(next);
              q.Enqueue(next);
            }
          }
        }
      }

      if (debug)
      {
        if (groupCount <= 1) logger.LogInfo("  [OK] 04 - 위상 연결성 : 전체 구조물이 1개의 그룹으로 잘 연결됨.");
        else logger.LogWarning($"  [주의] 04 - 위상 연결성 : 구조물이 {groupCount}개의 분리된 덩어리로 나뉘어 있음. (추후 강체 구속 필요 가능성)");
      }
    }

    private static void InspectRigidIntegrity(FeModelContext context, PipelineLogger logger, bool debug, bool verbose)
    {
      var emptyRbes = context.Rigids.Where(kv => kv.Value.DependentNodeIDs.Count == 0).Select(kv => kv.Key).ToList();
      foreach (int rbeId in emptyRbes) context.Rigids.Remove(rbeId);

      if (debug)
      {
        if (emptyRbes.Count == 0) logger.LogInfo("  [OK] 05 - 강체 무결성 : 타겟을 잃은 불량 강체 없음.");
        else
        {
          logger.LogWarning($"  [복구] 05 - 강체 무결성 : 종속 노드가 없는 불량 강체 {emptyRbes.Count}개 삭제됨.");
          if (verbose) logger.LogInfo($"       삭제 IDs: {SummarizeIds(emptyRbes, 10)}");
        }
      }
    }

    private static bool InspectRigidDependencies(FeModelContext context, PipelineLogger logger, bool debug)
    {
      bool isFatalFree = true;
      var depNodeToRbes = new Dictionary<int, List<int>>();
      var indepToDeps = new Dictionary<int, HashSet<int>>();

      foreach (var kvp in context.Rigids)
      {
        int rbeId = kvp.Key;
        var rbe = kvp.Value;

        if (!indepToDeps.ContainsKey(rbe.IndependentNodeID)) indepToDeps[rbe.IndependentNodeID] = new HashSet<int>();

        foreach (var depNode in rbe.DependentNodeIDs)
        {
          if (!depNodeToRbes.ContainsKey(depNode)) depNodeToRbes[depNode] = new List<int>();
          depNodeToRbes[depNode].Add(rbeId);
          indepToDeps[rbe.IndependentNodeID].Add(depNode);
        }
      }

      // [다중 종속 (Double Dependency) 검사]
      var doubleDeps = depNodeToRbes.Where(kv => kv.Value.Count > 1).ToList();
      if (doubleDeps.Count > 0)
      {
        logger.LogError($"  [FATAL] 06 - 강체 역학망 : 노드가 여러 마스터(Independent)의 지배를 받는 다중 종속 발견! (Nastran FATAL 6202 발생)");
        foreach (var kvp in doubleDeps.Take(3))
          logger.LogError($"       -> Node {kvp.Key}번은 RBE {string.Join(", ", kvp.Value)}의 슬레이브로 겹침");
        isFatalFree = false;
      }

      // [순환 종속 (Circular Dependency) 꼬리물기 검사]
      var visited = new HashSet<int>();
      var recursionStack = new HashSet<int>();

      bool DetectCycle(int node, List<int> path)
      {
        if (recursionStack.Contains(node)) { path.Add(node); return true; }
        if (visited.Contains(node)) return false;

        visited.Add(node);
        recursionStack.Add(node);
        path.Add(node);

        if (indepToDeps.ContainsKey(node))
        {
          foreach (var neighbor in indepToDeps[node])
            if (DetectCycle(neighbor, path)) return true;
        }

        path.RemoveAt(path.Count - 1);
        recursionStack.Remove(node);
        return false;
      }

      foreach (var node in indepToDeps.Keys)
      {
        var path = new List<int>();
        if (DetectCycle(node, path))
        {
          logger.LogError($"  [FATAL] 06 - 강체 역학망 : 순환 종속(꼬리물기) 발견! (경로: {string.Join(" -> ", path)})");
          isFatalFree = false;
          break;
        }
      }

      if (isFatalFree && debug)
        logger.LogInfo("  [OK] 06 - 강체 역학망 : 다중 종속 및 순환 종속 위험 없음.");

      return isFatalFree;
    }

    private static string SummarizeIds(List<int> ids, int limit)
    {
      if (ids == null || ids.Count == 0) return "";
      var subset = ids.Take(limit);
      string str = string.Join(", ", subset);
      if (ids.Count > limit) str += ", ...";
      return str;
    }
  }
}
