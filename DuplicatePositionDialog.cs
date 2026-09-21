using System;
using System.Drawing;
using System.Windows.Forms;

namespace GerberParserSmartV4._0
{
    /// <summary>
    /// 「同坐标重复点」警告框 —— 保存前的最后一道闸。
    ///
    /// 【为什么需要它】插针机按**坐标**定位打针（`circles.json` 每条记录只带 X / Y 与尺寸，
    /// 图层信息下游不读）。于是同一个坐标上出现多条记录 = 同一个位置被插两次针。
    /// 而本项目的选点模型允许"不同图层、同坐标"各占一条 —— 框选一片区域时（框选不按图层叠放
    /// 过滤，见 MainForm.ApplyRegionSelection）同坐标的多层图形会被一起收进来，重复点就此产生。
    ///
    /// 【为什么拦在"保存"这一步，而不是选点的时候】选点入口有五个（单选 / 多选扩散 / 框选 /
    /// 合并 / 游离点收编），逐个设防必漏；而且隐藏图层场景在选点端根本无法判定
    /// （藏起来的旧点算不算存在？算 → 用户莫名其妙被拒；不算 → 重复照样产生）。
    /// 保存端是**唯一**的写盘点（新建自动落盘 / 就地保存 / 另存为都汇到这里），
    /// 也只有它能覆盖老工程里已经存下的历史重复点。
    ///
    /// 【为什么只警告、不自动去重】"保留哪一条"是业务判断：GBL 与 GBS 常在同一坐标放不同尺寸的
    /// 图形（焊盘与它的外形框），插针机要打的通常是其中某一条。程序按"最上层"或"尺寸最大"去猜
    /// 都可能猜反，猜反就是打错针位 —— 所以把决定权交给用户，程序只负责把冲突摆清楚。
    ///
    /// 本类**不含任何业务判定**：明细文本由 <see cref="MainForm"/> 拼好后传入，这里只负责显示。
    /// </summary>
    internal sealed class DuplicatePositionDialog : Form
    {
        /// <param name="groupCount">冲突处数（一个坐标算一处）</param>
        /// <param name="pointCount">涉及的点数合计</param>
        /// <param name="detailText">逐处明细（已由调用方格式化好，可能被截断）</param>
        public DuplicatePositionDialog(int groupCount, int pointCount, string detailText)
        {
            Font = SystemFonts.MessageBoxFont;   // 系统字体：看起来不像一个自绘的野窗体
            Text = "同坐标重复点";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(700, 470);

            Label lblTitle = new Label();
            lblTitle.AutoSize = false;                 // Label 默认 AutoSize=true，会无视 Size
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblTitle.Location = new Point(20, 16);
            lblTitle.Size = new Size(660, 22);
            lblTitle.Font = new Font(Font, FontStyle.Bold);
            lblTitle.Text = $"发现 {groupCount} 处同坐标的重复点（涉及 {pointCount} 个点）";

            Label lblTip = new Label();
            lblTip.AutoSize = false;
            lblTip.TextAlign = ContentAlignment.TopLeft;
            lblTip.Location = new Point(20, 44);
            lblTip.Size = new Size(660, 46);
            lblTip.Text =
                "一个坐标只能对应一个点：插针机按坐标定位，同坐标上的多条记录会被重复插针。\r\n" +
                (pointCount > 20
                    // 冲突数量异常多几乎不可能是手点出来的 —— 更可能是坐标本身有问题。
                    // 最典型的一条：打开 v2.0~2.4 的老工程，坐标会全部读成 0（见 ReadJsonPosition）。
                    ? "⚠ 重复数量异常多时，请先确认选点位置是否正确（打开旧格式工程时坐标会全部读成 0）。"
                    : "请返回修改（取消多余的选点），或确认这批重复确实是有意为之之后，再选择保存。");

            TextBox txtDetail = new TextBox();
            txtDetail.Location = new Point(20, 98);
            txtDetail.Size = new Size(660, 316);
            txtDetail.Multiline = true;
            txtDetail.ReadOnly = true;
            txtDetail.WordWrap = false;                // 明细按行看，折行反而难读
            txtDetail.ScrollBars = ScrollBars.Both;
            txtDetail.Font = new Font("Consolas", 9f); // 等宽：坐标与尺寸对得齐
            txtDetail.Text = detailText ?? string.Empty;

            Button btnBack = new Button();
            btnBack.Text = "返回修改";
            btnBack.Location = new Point(468, 426);
            btnBack.Size = new Size(100, 28);
            btnBack.DialogResult = DialogResult.No;

            Button btnSave = new Button();
            btnSave.Text = "仍然保存";
            btnSave.Location = new Point(578, 426);
            btnSave.Size = new Size(102, 28);
            btnSave.DialogResult = DialogResult.Yes;

            Controls.Add(lblTitle);
            Controls.Add(lblTip);
            Controls.Add(txtDetail);
            Controls.Add(btnBack);
            Controls.Add(btnSave);

            // 回车 / Esc 一律落到「返回修改」—— 默认动作必须是**不写盘**的那个。
            // （另一个按钮用 Tab 或鼠标到达，正好把"手滑按了回车"挡在门外。）
            AcceptButton = btnBack;
            CancelButton = btnBack;
        }
    }
}
