using System;
using System.Collections.Generic;

namespace ModuleGroupUnitAnalysis.Pipeline.Postprocess
{
  public class F06ResultData
  {
    public bool IsParsedSuccessfully { get; set; } = false;
    // ★ FATAL 에러 탐지용 프로퍼티 추가
    public bool HasFatalError { get; set; } = false;
    public List<string> FatalMessages { get; set; } = new List<string>();
    public List<DisplacementData> Displacements { get; set; } = new List<DisplacementData>();
    public List<BeamStressData> BeamStresses { get; set; } = new List<BeamStressData>();
    public List<RodForceData> RodForces { get; set; } = new List<RodForceData>();
  }

  public class DisplacementData
  {
    public int NodeID { get; set; }
    public double T1 { get; set; }
    public double T2 { get; set; }
    public double T3 { get; set; }
    public double Magnitude => Math.Sqrt(T1 * T1 + T2 * T2 + T3 * T3);
  }

  public class BeamStressData
  {
    public int ElementID { get; set; }
    public double MaxStress { get; set; }
    public double MinStress { get; set; }
    public double MaxAbsStress => Math.Max(Math.Abs(MaxStress), Math.Abs(MinStress));
  }

  public class RodForceData
  {
    public int ElementID { get; set; }
    public double AxialForce { get; set; } // 단위: N (뉴턴)
  }
}
