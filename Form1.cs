using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using ModuleGroupUnitAnalysis; // 코어 프로젝트 참조 필요
using System.Windows.Forms;

namespace ModuleGroupUnitAnalysis.Launcher
{
  public partial class Form1 : Form
  {
    // UI 컨트롤
    private TextBox txtBdf;
    private ComboBox cmbAnalysisType;
    private CheckBox chkForceRigid, chkRunSanity, chkRunNastran, chkCheckResult, chkLogExport;
    private Button btnRun;
    private Panel pnlOverlay;
    private ProgressBar prgStatus;

    public Form1()
    {
      InitializeModernUI();
    }

    private void InitializeModernUI()
    {
      // 1. 폼 기본 설정
      this.Text = "Module Group Unit Analysis - Test Launcher";
      this.Size = new Size(750, 540);
      this.StartPosition = FormStartPosition.CenterScreen;
      this.FormBorderStyle = FormBorderStyle.FixedDialog;
      this.MaximizeBox = false;
      this.BackColor = Color.FromArgb(240, 243, 249);
      this.Font = new Font("Segoe UI", 9.5f);

      // 헤더 섹션
      Panel pnlHeader = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.FromArgb(41, 53, 65) };
      Label lblTitle = new Label
      {
        Text = "Module & Group Unit Analysis Pipeline",
        ForeColor = Color.White,
        Font = new Font("Segoe UI Semibold", 16),
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(20, 0, 0, 0)
      };
      pnlHeader.Controls.Add(lblTitle);
      this.Controls.Add(pnlHeader);

      int startY = 90;

      // [Section 1] 데이터 입력 그룹
      GroupBox grpInput = CreateGroup("Input Data Configuration", 20, startY, 695, 120);
      txtBdf = AddModernFileRow(grpInput, "Target BDF File:", 35, out Button btnBrowseBdf);
      btnBrowseBdf.Click += (s, e) => SelectFile(txtBdf, "BDF Files (*.bdf;*.dat)|*.bdf;*.dat|All Files (*.*)|*.*");

      grpInput.Controls.Add(new Label { Text = "Analysis Type:", Bounds = new Rectangle(20, 85, 120, 20), Font = new Font("Segoe UI", 9, FontStyle.Bold) });
      cmbAnalysisType = new ComboBox { Bounds = new Rectangle(140, 80, 200, 25), DropDownStyle = ComboBoxStyle.DropDownList };
      cmbAnalysisType.Items.AddRange(new object[] { AnalysisType.ModuleUnit, AnalysisType.GroupUnit });
      cmbAnalysisType.SelectedIndex = 0; // 기본값: ModuleUnit
      grpInput.Controls.Add(cmbAnalysisType);

      // [Section 2] 옵션 설정 그룹
      GroupBox grpOpt = CreateGroup("Pipeline Execution Switches", 20, startY + 140, 695, 140);

      chkLogExport = new CheckBox { Text = "Log Export", Bounds = new Rectangle(20, 35, 150, 20), Checked = true };
      chkRunSanity = new CheckBox { Text = "Run Sanity Nastran Check", Bounds = new Rectangle(20, 65, 200, 20), Checked = false };
      chkRunNastran = new CheckBox { Text = "Run Nastran Analysis", Bounds = new Rectangle(20, 95, 180, 20), Checked = true };

      chkCheckResult = new CheckBox { Text = "Check Analysis Result", Bounds = new Rectangle(300, 35, 180, 20), Checked = true };
      chkForceRigid = new CheckBox { Text = "Force Rigid DOF 123456", Bounds = new Rectangle(300, 65, 200, 20), Checked = false };

      grpOpt.Controls.AddRange(new Control[] { chkLogExport, chkRunSanity, chkRunNastran, chkCheckResult, chkForceRigid });

      // [Section 3] 실행 버튼
      btnRun = new Button
      {
        Text = "RUN ANALYSIS PIPELINE",
        Bounds = new Rectangle(250, 410, 250, 50),
        BackColor = Color.FromArgb(52, 152, 219),
        ForeColor = Color.White,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI", 11, FontStyle.Bold),
        Cursor = Cursors.Hand
      };
      btnRun.FlatAppearance.BorderSize = 0;
      btnRun.Click += BtnRun_Click;
      this.Controls.Add(btnRun);

      // [Section 4] 작동 중 오버레이
      pnlOverlay = new Panel { Bounds = new Rectangle(0, 70, 750, 470), BackColor = Color.FromArgb(200, 255, 255, 255), Visible = false };
      Label lblWait = new Label
      {
        Text = "Processing Analysis Pipeline... Please wait.",
        Font = new Font("Segoe UI Semibold", 13),
        AutoSize = false,
        TextAlign = ContentAlignment.MiddleCenter,
        Dock = DockStyle.Top,
        Height = 250
      };
      prgStatus = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Bounds = new Rectangle(225, 220, 300, 15) };
      pnlOverlay.Controls.AddRange(new Control[] { lblWait, prgStatus });
      this.Controls.Add(pnlOverlay);
      pnlOverlay.BringToFront();
    }

    private GroupBox CreateGroup(string title, int x, int y, int w, int h)
    {
      GroupBox gb = new GroupBox { Text = title, Bounds = new Rectangle(x, y, w, h), BackColor = Color.White };
      this.Controls.Add(gb);
      return gb;
    }

    private TextBox AddModernFileRow(Control parent, string label, int y, out Button btn)
    {
      parent.Controls.Add(new Label { Text = label, Bounds = new Rectangle(20, y + 5, 120, 20), Font = new Font("Segoe UI", 9, FontStyle.Bold) });
      TextBox tb = new TextBox { Bounds = new Rectangle(140, y, 430, 25), ReadOnly = true, BackColor = Color.FromArgb(245, 245, 245) };
      btn = new Button { Text = "Browse", Bounds = new Rectangle(585, y - 1, 85, 28), FlatStyle = FlatStyle.Flat, BackColor = Color.LightGray };
      parent.Controls.Add(tb); parent.Controls.Add(btn);
      return tb;
    }

    private void SelectFile(TextBox tb, string filter)
    {
      using (OpenFileDialog ofd = new OpenFileDialog { Filter = filter })
        if (ofd.ShowDialog() == DialogResult.OK) tb.Text = ofd.FileName;
    }

    private async void BtnRun_Click(object sender, EventArgs e)
    {
      if (string.IsNullOrWhiteSpace(txtBdf.Text))
      {
        MessageBox.Show("해석을 수행할 BDF 파일을 선택해주세요.", "입력 누락", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return;
      }

      pnlOverlay.Visible = true;
      btnRun.Enabled = false;

      // 옵션 매핑
      var options = new AnalysisOptions
      {
        BdfFilePath = txtBdf.Text,
        Type = (AnalysisType)cmbAnalysisType.SelectedItem,
        LogExport = chkLogExport.Checked,
        RunSanityNastranCheck = chkRunSanity.Checked,
        RunNastranAnalysis = chkRunNastran.Checked,
        CheckAnalysisResult = chkCheckResult.Checked,
        ForceRigidDof123456 = chkForceRigid.Checked,
        PipelineDebug = true, // 디버그는 기본 활성화 처리
        VerboseDebug = false
      };

      try
      {
        // 비동기로 코어 로직 실행
        await Task.Run(() => AnalysisRunner.RunPipeline(options));

        pnlOverlay.Visible = false;
        btnRun.Enabled = true;

        MessageBox.Show("해석 파이프라인 작업이 완료되었습니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
      }
      catch (Exception ex)
      {
        pnlOverlay.Visible = false;
        btnRun.Enabled = true;
        MessageBox.Show("오류 발생: " + ex.Message, "실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
      }
    }
  }
}
