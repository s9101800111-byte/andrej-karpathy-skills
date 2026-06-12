using System;
using System.Windows.Forms;

namespace StairClearanceCheck
{
    /// <summary>輸入最小淨高要求與檢測點間距的對話框。</summary>
    public class ClearanceInputForm : Form
    {
        private readonly TextBox _clearanceBox;
        private readonly TextBox _spacingBox;

        /// <summary>最小淨高要求(mm)。</summary>
        public double MinClearanceMm { get; private set; }

        /// <summary>檢測點間距(mm)。</summary>
        public double SampleSpacingMm { get; private set; }

        public ClearanceInputForm()
        {
            Text = "樓梯淨高檢核";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new System.Drawing.Size(290, 125);

            var clearanceLabel = new Label { Text = "最小淨高要求 (mm):", Left = 12, Top = 16, Width = 145 };
            _clearanceBox = new TextBox { Left = 165, Top = 13, Width = 110, Text = "1900" };

            var spacingLabel = new Label { Text = "檢測點間距 (mm):", Left = 12, Top = 46, Width = 145 };
            _spacingBox = new TextBox { Left = 165, Top = 43, Width = 110, Text = "300" };

            var okButton = new Button { Text = "開始檢核", Left = 70, Top = 85, Width = 95, DialogResult = DialogResult.OK };
            var cancelButton = new Button { Text = "取消", Left = 180, Top = 85, Width = 95, DialogResult = DialogResult.Cancel };
            okButton.Click += OnOkClicked;

            Controls.AddRange(new Control[] { clearanceLabel, _clearanceBox, spacingLabel, _spacingBox, okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void OnOkClicked(object sender, EventArgs e)
        {
            if (!double.TryParse(_clearanceBox.Text, out double clearance) || clearance <= 0
                || !double.TryParse(_spacingBox.Text, out double spacing) || spacing <= 0)
            {
                MessageBox.Show("請輸入大於 0 的數值。", "樓梯淨高檢核",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None; // 留在對話框讓使用者修正
                return;
            }

            MinClearanceMm = clearance;
            SampleSpacingMm = spacing;
        }
    }
}
