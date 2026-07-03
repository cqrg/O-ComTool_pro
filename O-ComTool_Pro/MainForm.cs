using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Management;
using System.IO;
using System.Xml;
using System.Text.RegularExpressions;
using System.IO.Ports;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FastColoredTextBoxNS;

namespace O_ComTool_Pro
{
    public partial class MainForm : Form
    {
        string server_url = "https://www.ifreehub.com/octservice"; // 服务器地址，用于检查更新
        public static UpdateHelper.check_value check_version_value;

        bool FrameOrByte = true;
        int spTxCount = 0, spRxCount = 0;   //串口接收发送字节数
        int spFrameTxCount = 0, spFrameRxCount = 0;   //串口接收发送帧数
        int AutoCountNum = 0;// 自动计数值
        
        

        FileStream log_fs;
        StreamWriter log_sw;             // 长生命周期的日志写入器，避免每帧 new StreamWriter 重复写 UTF-8 BOM
        readonly object logLock = new object();   // log_fs / log_sw 跨线程访问的同步锁
        string log_save_path = "";// 日志文件保存路径
        string load_file_path = "";// 加载文件路径
        List<QuickSend> quicksend_list = new List<QuickSend>();

        // 显示颜色

        Color cur_color;// 当前文字前景色

        public Font ReceFont1, SendFont1;
        public Color ReceForeColor1, ReceBackColor1, SendForeColor1, SendBackColor1;

        public Font ReceFont2, SendFont2;
        public Color ReceForeColor2, ReceBackColor2, SendForeColor2, SendBackColor2;

        bool display_plan1_active = app.Default.DisplayPlan1Enable;   // true=当前显示方案1

        //
        public int frame_interval;
        public bool comment_enable;

        //highlight
        public bool hight_light_enable;
        public string hl_red_regex_str;
        public string hl_green_regex_str;
        public string hl_yellow_regex_str;
        public string hl_blue_regex_str;
        public string hl_magenta_regex_str;
        public string hl_cyan_regex_str;
        public string hl_orange_regex_str;

        //发送回显
        public bool send_display_enable;

        //显示时间追加新行
        public bool time_newline_enable;

        //发送回显写入文件使能
        public bool send_2_file_enable;

        //发送时接受窗口自动跳转至新行
        public bool send_2_newline_enable;


        public MainForm()
        {
            
            InitializeComponent();
            // 不再禁用跨线程检查；所有 UI 写入必须经 Invoke/BeginInvoke 编组到 UI 线程
        }
        

        /// <summary>
        /// 启动检查软件版本
        /// </summary>
        void StartCheckVersion()
        {
            short check_cycle_days = 1; // 版本检查周期，暂定每天进行一次检查
            DateTime CurrentDateTime = DateTime.Now;
            DateTime LastDateTime = Convert.ToDateTime(app.Default.LastCheckTime);

            DateTime LastDateTime_Cycle = LastDateTime;// LastDateTime.AddDays(check_cycle_days);

            if (CurrentDateTime.CompareTo(LastDateTime_Cycle) >= 0)
            {

                Task check_version = new Task(() =>
                {
                    UpdateHelper.check_value ret_update = UpdateHelper.check_update(server_url);
                    if ((ret_update.valid == true) && (app.Default.SkipVersion != ret_update.version) && (ret_update.version != Application.ProductVersion.Substring(0, 5)))
                    {
                        check_version_value.version = ret_update.version;
                        check_version_value.link = ret_update.link;
                        app.Default.LastCheckTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        app.Default.Save();
                        // ShowDialog 必须在 UI 线程执行
                        this.Invoke((Action)(() =>
                        {
                            if (this.IsDisposed) return;
                            CheckUpdate check_update = new CheckUpdate();
                            check_update.ShowDialog();
                        }));
                    }
                    else
                    {
                        return;
                    }

                });
                check_version.Start();
            }
            else
            {
                return;
            }
        }

        /// <summary>
        /// 显示右下角状态
        /// </summary>
        /// <param name="ok"></param>
        /// <param name="message"></param>
        void ShowCurStatus(bool ok, string message)
        {
            tssCurstatus.Text = message;
            if (ok)
            {
                tssCurstatus.ForeColor = Color.Green;
            }
            else
            {
                tssCurstatus.ForeColor = Color.Red;
            }
        }

        /// <summary>
        /// 刷新右下角收发计数与收发比。统一各处计数显示逻辑，含除零保护。
        /// </summary>
        void UpdateCounters()
        {
            string unit = FrameOrByte ? "frames" : "bytes";
            int rx = FrameOrByte ? spFrameRxCount : spRxCount;
            int tx = FrameOrByte ? spFrameTxCount : spTxCount;
            tssRxCount.Text = "RX: " + rx + " " + unit;
            tssTxCount.Text = "TX: " + tx + " " + unit;
            tssLabRateValue.Text = (tx > 0) ? Math.Round(rx * 100.0 / tx, 2) + "%" : "N/A";
        }

        /// <summary>
        /// 从文本中提取连续的 2 位十六进制字节序列（忽略非 hex 字符）。
        /// 统一右键菜单与发送路径的解析逻辑。
        /// </summary>
        static byte[] ParseHexBytes(string text)
        {
            MatchCollection mc = Regex.Matches(text, @"(?i)[\da-f]{2}");
            byte[] bytes = new byte[mc.Count];
            int i = 0;
            foreach (Match m in mc)
            {
                bytes[i++] = byte.Parse(m.Value, System.Globalization.NumberStyles.HexNumber);
            }
            return bytes;
        }

        /// <summary>
        /// 应用显示方案（true=方案1，false=方案2）到接收区与发送区。
        /// 消除 plan1/plan2 在多处复制的字体/颜色赋值，避免极性反转 bug。
        /// </summary>
        void ApplyDisplayPlan(bool plan1)
        {
            Font receFont, sendFont;
            Color receFg, receBg, sendFg, sendBg;
            if (plan1)
            {
                receFont = ReceFont1; receFg = ReceForeColor1; receBg = ReceBackColor1;
                sendFont = SendFont1; sendFg = SendForeColor1; sendBg = SendBackColor1;
            }
            else
            {
                receFont = ReceFont2; receFg = ReceForeColor2; receBg = ReceBackColor2;
                sendFont = SendFont2; sendFg = SendForeColor2; sendBg = SendBackColor2;
            }
            fctbReceive.Font = receFont;
            fctbReceive.ForeColor = receFg;
            fctbReceive.BackColor = receBg;
            cur_color = receFg;
            txbSend.Font = sendFont;
            txbSend.ForeColor = sendFg;
            txbSend.BackColor = sendBg;
        }


        /// <summary>
        /// 加载文件
        /// </summary>
        /// <param name="file_path"></param>
        private void load_file(string file_path)
        {
            StreamReader file = new StreamReader(file_path, Encoding.Default);
            string tmp_str = "";

            cmbLoadFile.Items.Clear();
            while (tmp_str != null)
            {
                tmp_str = file.ReadLine();
                if (!string.IsNullOrEmpty(tmp_str))
                    cmbLoadFile.Items.Add(tmp_str);
            }
            if (cmbLoadFile.Items.Count > 0)
                cmbLoadFile.SelectedIndex = 0;
            file.Close();
        }

        void refresh_quick_send_ui()
        {
            int tmp_position_y = -tbpQuick.VerticalScroll.Value - 24;
            
            for (short i = 0; i < quicksend_list.Count; i++)
            {
                //MessageBox.Show("tbpQuick.VerticalScroll.Value = " + tbpQuick.VerticalScroll.Value +
                //            "\ntmp_position_y=" + tmp_position_y);
                quicksend_list[i].Index = i; // 刷新索引值
                quicksend_list[i].Location = new Point(6, tmp_position_y + 30);
                tmp_position_y = quicksend_list[i].Location.Y;
                //add_quick_send(i, quicksend_list[i].Name, quicksend_list[i].Data);
            }
        }

        /// <summary>
        /// 快捷发送删除回调函数
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void quicksend_del_Click(object sender, EventArgs e)
        {
            PictureBox tmp_send_btn = (PictureBox)sender;
            O_ComTool_Pro.QuickSend tmp_quicksend = (O_ComTool_Pro.QuickSend)tmp_send_btn.Parent;
            quicksend_list.Remove(tmp_quicksend);
            tbpQuick.Controls.Remove(tmp_quicksend);
            refresh_quick_send_ui();
            //MessageBox.Show(tmp_quicksend.Data);
        }

        /// <summary>
        /// 快速发送发送回调函数，不支持重复发送、自动回复等功能
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void quicksend_send_Click(object sender, EventArgs e)
        {
            Button tmp_send_btn = (Button)sender;
            O_ComTool_Pro.QuickSend tmp_quicksend = (O_ComTool_Pro.QuickSend)tmp_send_btn.Parent;

            // 检查串口是否打开
            if (serialPort1.IsOpen == false)
            {
                chkRepeatSend.Checked = false;
                chkAutoReply.Checked = false;
                MessageBox.Show("串口未打开！", "O-ComTool 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 检查发送区是否为空
            if (tmp_quicksend.Data == "")
            {
                MessageBox.Show("发送区不能为空！", "O-ComTool 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            serial_send(tmp_quicksend.Data);
            
            //MessageBox.Show(tmp_quicksend.Data);
        }

        /// <summary>
        /// 添加快速发送控件
        /// </summary>
        /// <param name="index"></param>
        /// <param name="title"></param>
        /// <param name="data"></param>
        void add_quick_send(short index, string title, string data)
        {
            int last_position_y = 0;
            if (quicksend_list.Count > 0)
            {
                QuickSend tmp_quicksend = new QuickSend();
                tmp_quicksend = quicksend_list[quicksend_list.Count - 1];
                last_position_y = tmp_quicksend.Location.Y;
            }
            else {
                last_position_y = -24;
            }

            QuickSend quicksend = new QuickSend();
            quicksend.Name = "quicksend" + index;
            quicksend.Index = index;
            quicksend.ItemName = title;
            quicksend.Data = data;
            quicksend.Width = tbpQuick.Width - 65;
            //quicksend.Anchor = AnchorStyles.Right | AnchorStyles.Left | AnchorStyles.Top;
            quicksend.Location = new Point(6, last_position_y + 30);
            if (index > 2) {
                quicksend.DelVisible = true;
                quicksend.DelClicked += new O_ComTool_Pro.QuickSend.BtnClickHandle(quicksend_del_Click);
            }
            quicksend.SendClicked += new O_ComTool_Pro.QuickSend.BtnClickHandle(quicksend_send_Click);

            tbpQuick.Controls.Add(quicksend);
            quicksend_list.Add(quicksend);

        }

        /// <summary>
        /// 加载上次关闭时的配置
        /// </summary>
        void LoadLastConfig()
        {
            bool tmp;
            // 串口设置
            cmbBaudRate.Text = app.Default.cmbBaudRate;
            cmbDataBit.SelectedIndex = app.Default.cmbDataBitIndex;
            cmbStopBit.SelectedIndex = app.Default.cmbStopBitIndex;
            cmbParityBit.SelectedIndex = app.Default.cmbParityBitIndex;
            cmbFlowCtrl.SelectedIndex = app.Default.cmbFlowCtrlIndex;

            // 接收设置
            tmp = app.Default.receEncodeAscii == true ? radAsciiReceive.Checked = true : radAsciiReceive.Checked = false;
            tmp = app.Default.receEncodeAscii == false ? radHexReceive.Checked = true : radHexReceive.Checked = false;
            tmp = app.Default.chkAutoLine == true ? chkAutoLine.Checked = true : chkAutoLine.Checked = false;
            tmp = app.Default.chkShowTime == true ? chkShowTime.Checked = true : chkShowTime.Checked = false;
            tmp = app.Default.chkAutoReply == true ? chkAutoReply.Checked = true : chkAutoReply.Checked = false;
            nudReplyDelay.Value = app.Default.AutoReplyDelay;

            // 发送设置
            tmp = app.Default.sendEncodeAscii == true ? radAsciiSend.Checked = true : radAsciiSend.Checked = false;
            tmp = app.Default.sendEncodeAscii == false ? radHexSend.Checked = true : radHexSend.Checked = false;
            tmp = app.Default.chkAutoCount == true ? chkAutoCount.Checked = true : chkAutoCount.Checked = false;
            tmp = app.Default.chkAppendNewLine == true ? chkNewLine.Checked = true : chkNewLine.Checked = false;
            tmp = app.Default.chkRepeatSend == true ? chkRepeatSend.Checked = true : chkRepeatSend.Checked = false;
            nudRepeatInterval.Value = app.Default.RepeatInterval;

            // 接收
                // 方案1
            ReceFont1 = app.Default.ReceFont1;
            ReceForeColor1 = app.Default.ReceForeColor1;
            ReceBackColor1 = app.Default.ReceBackColor1;
                // 方案2
            ReceFont2 = app.Default.ReceFont2;
            ReceForeColor2 = app.Default.ReceForeColor2;
            ReceBackColor2 = app.Default.ReceBackColor2;

            // 通用发送
                // 方案1
            SendFont1 =app.Default.SendFont1;
            SendForeColor1 = app.Default.SendForeColor1;
            SendBackColor1 = app.Default.SendBackColor1;
                // 方案2
            SendFont2 = app.Default.SendFont2;
            SendForeColor2 = app.Default.SendForeColor2;
            SendBackColor2 = app.Default.SendBackColor2;

                // 是否为方案1
            ApplyDisplayPlan(app.Default.DisplayPlan1Enable == true);

            // 选项
                // 基本
            frame_interval = app.Default.FrameInterval;
            comment_enable = app.Default.CommentEnable;
            hight_light_enable = app.Default.HightLightEnable;
            send_display_enable = app.Default.SendDisplayEnable;
            time_newline_enable = app.Default.chkTimeNewLine;
            send_2_file_enable = app.Default.Send2FileEnable;
            send_2_newline_enable = app.Default.Send2NewLineEnable;

            if (app.Default.LoadFileEnable == true ) 
            {
                if (File.Exists(app.Default.LoadFilePath) == true)
                {
                    load_file(app.Default.LoadFilePath);
                    load_file_path = app.Default.LoadFilePath;
                    ShowCurStatus(true, "文件加载成功");
                }
                else
                {
                    ShowCurStatus(false, "文件加载失败");
                }
            }

            //highlight
            hl_red_regex_str = app.Default.HighLightRed;
            hl_green_regex_str = app.Default.HighLightGreen;
            hl_yellow_regex_str = app.Default.HighLightYellow;
            hl_blue_regex_str = app.Default.HighLightBlue;
            hl_magenta_regex_str = app.Default.HighLightMagenta;
            hl_cyan_regex_str = app.Default.HighLightCyan;
            hl_orange_regex_str = app.Default.HighLightOrange;

            // 通用发送
            txbSend.Text = app.Default.GeneralSendData;

            //快捷发送
            tbpQuick.Controls.Clear();
            quicksend_list.Clear();
            for (short i = 0; i < app.Default.QuickSendCount; i++)
            {
                add_quick_send(i, app.Default.QuickSendTitle[i], app.Default.QuickSendData[i]);
            }
        }

        private void tsmCheck_Click(object sender, EventArgs e)
        {
            Check check = new Check();
            check.Show();
        }

        private void tsmAscii_Click(object sender, EventArgs e)
        {
            ASCII ascii = new ASCII();
            ascii.Show();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            this.Text = "O-ComTool V" + Application.ProductVersion.Substring(0, 5);
            StartCheckVersion();
            LoadLastConfig();
        }

        private void quickSend3_Load(object sender, EventArgs e)
        {
            //string strColor = Color.Red.Name.ToString();

            //Color slateBlue = Color.FromName(strColor);
        }

        private void tsmAbout_Click(object sender, EventArgs e)
        {
            About about = new About();
            about.ShowDialog();
        }

        private void tsmUpdate_Click(object sender, EventArgs e)
        {
            Update update = new Update();
            update.ShowDialog();
        }

        private void tsmFormat_Click(object sender, EventArgs e)
        {
            Format format = new Format();
            format.Show();
        }

        private void tsmOption_Click(object sender, EventArgs e)
        {
            Option option = new Option(this);
            option.ShowDialog();

            // 选项关闭后按当前方案刷新字体/颜色
            ApplyDisplayPlan(display_plan1_active);
        }

        private void tsmDonate_Click(object sender, EventArgs e)
        {
            Donate donate = new Donate();
            donate.ShowDialog();
        }

        private void tsmAddNote_Click(object sender, EventArgs e)
        {
            Note note = new Note(this);
            note.ShowDialog();
        }

        private void quickSend1_SendClicked(object sender, EventArgs e)
        {

        }

        void tsProgressBar_start()
        {
            tssProgressBar.Value = 0;
            tssProgressBar.Maximum = 100;
            timerProcessBar.Start();
        }

        void tsProgressBar_stop()
        {
            tssProgressBar.Value = 100;
            timerProcessBar.Stop();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="len"></param>
        void SendDisplay(byte[] data, int len)
        {
            string display_str = "";
            if (chkShowTime.Checked == true)
            {
                string data_str = DateTime.Now.ToString("yyyy-MM-dd ");//写入文件时添加年月
                WriteLog(data_str);
                string TimeStamp = DateTime.Now.ToString("HH:mm:ss:fff-> ");
                if (time_newline_enable == true) TimeStamp += "\r\n";

                //fctbReceive.AppendText(TimeStamp);
                display_str += TimeStamp;
            }
            if (radHexReceive.Checked == true)
            {
                display_str += GetHexString(data, 0, len).ToString();
                //fctbReceive.AppendText(GetHexString(data, 0, len).ToString());
            }
            else if (radAsciiReceive.Checked == true)
            {
                string tmp_str = Encoding.UTF8.GetString(data, 0, len);
                display_str += tmp_str;
                //fctbReceive.AppendText(tmp_str);
            }
            if (chkAutoLine.Checked == true)
            {
                display_str += "\r\n";
                //fctbReceive.AppendText("\r\n");
            }
            fctbReceive.AppendText(display_str);
            if ((send_2_newline_enable == true) || (o_ScrollBar1.Maximum - (o_ScrollBar1.Value + o_ScrollBar1.ThumbSize)) < 20) fctbReceive.GoEnd(); // 发送自动跳转新行使能或滚动条位于接收框底部

            if (send_2_file_enable == true)
            {
                WriteLog(display_str);
            }
        }

        void serial_send(string txb_data)
        {
            string TempStr;
            int index;
            index = txb_data.IndexOf("//");
            tsProgressBar_start();

            
            if (comment_enable == true && index != -1)
            {
                TempStr = txb_data.Substring(0, index);//获得“//”之前的内容，实现注释功能
            }
            else
            {
                TempStr = txb_data;
            }


            if (chkAutoCount.Checked == true)
            {
                TempStr = TempStr + (++AutoCountNum).ToString().PadLeft(6, '0');
            }

            if (radHexSend.Checked)//十六进制发送
            {
                byte[] bytesToWrite = ParseHexBytes(TempStr);
                serialPort1.Write(bytesToWrite, 0, bytesToWrite.Length);

                if (send_display_enable == true && chkAutoLine.Checked == true)
                {
                    SendDisplay(bytesToWrite, bytesToWrite.Length);
                }

                ShowCurStatus(true, bytesToWrite.Length + "字节已发送");
                spTxCount += bytesToWrite.Length;
                spFrameTxCount += 1;
                UpdateCounters();
            }
            else//ascii码发送
            {
                if (chkNewLine.Checked == true) TempStr += "\r\n";
                byte[] bytesToWrite = Encoding.UTF8.GetBytes(TempStr);//
                serialPort1.Write(bytesToWrite, 0, bytesToWrite.Length);

                if (send_display_enable == true && chkAutoLine.Checked == true)
                {
                    SendDisplay(bytesToWrite, bytesToWrite.Length);
                }

                ShowCurStatus(true, bytesToWrite.Length + "字节已发送");
                spTxCount += bytesToWrite.Length;
                spFrameTxCount += 1;
                UpdateCounters();
            }

            

            tsProgressBar_stop();
        }


        private void btnSend_Click(object sender, EventArgs e)
        {
            // 检查串口是否打开
            if (serialPort1.IsOpen == false)
            {
                chkRepeatSend.Checked = false;
                MessageBox.Show("串口未打开！", "O-ComTool 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 检查发送区是否为空
            if (txbSend.Text == "")
            {
                timerAutoReply.Stop();
                chkAutoReply.Checked = false;
                timerRepeat.Stop();
                chkRepeatSend.Checked = false;
                MessageBox.Show("发送区不能为空！", "O-ComTool 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnSend.Enabled = true;
                return;
            }

            // 发送数据时屏蔽发送按钮
            btnSend.Enabled = false;
            serial_send(txbSend.Text);

            // 检查重复发送是否可用
            if (chkRepeatSend.Checked)
            {
                nudRepeatInterval.Enabled = false;
                timerRepeat.Interval = (int)nudRepeatInterval.Value;
                timerRepeat.Start();
                tsProgressBar_stop();
                return;
            }
            
            btnSend.Enabled = true;
        }

        private void btnOpenCom_Click(object sender, EventArgs e)
        {
            if (serialPort1.IsOpen == false)
            {
                try
                {
                    serialPort1.PortName = cmbCom.Text;
                    serialPort1.BaudRate = int.Parse(cmbBaudRate.Text);
                    serialPort1.DataBits = int.Parse(cmbDataBit.Text);
                    serialPort1.Parity = (Parity)Enum.Parse(typeof(Parity), cmbParityBit.Text);
                    serialPort1.StopBits = (StopBits)Enum.Parse(typeof(StopBits), (cmbStopBit.SelectedIndex + 1).ToString());
                    serialPort1.Handshake = (Handshake)Enum.Parse(typeof(Handshake), cmbFlowCtrl.SelectedIndex.ToString());
                    serialPort1.ReceivedBytesThreshold = 1;//接收到一个字节就触发事件
                    //serialPort1.DataReceived += new SerialDataReceivedEventHandler(serialPort1_DataReceived);// New一个接收事件SP_DataReceived
                    serialPort1.Open();
                    btnOpenCom.Text = "关闭串口";

                    tssLabCom.ForeColor = Color.Green;
                    tssLabCom.Text = cmbCom.Text + ": " + cmbBaudRate.Text + ", " + cmbDataBit.Text + ", " + cmbParityBit.Text + ", " + cmbStopBit.Text;
                    //ShowCurStatus(true, cmbCom.Text + ": " + cmbBaudRate.Text + ", " + cmbDataBit.Text + ", " + cmbParityBit.Text + ", " + cmbStopBit.Text);
                    picConnectStatus.Image = Properties.Resources.Connected_48px;
                    cmbCom.Enabled = false;
                    cmbBaudRate.Enabled = false;
                    cmbDataBit.Enabled = false;
                    cmbParityBit.Enabled = false;
                    cmbStopBit.Enabled = false;
                    cmbFlowCtrl.Enabled = false;
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("错误:" + ex.Message, "O-ComTool 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    btnOpenCom.Text = "打开串口";
                    picConnectStatus.Image = Properties.Resources.Disconnected_48px;
                    cmbCom.Enabled = true;
                    cmbBaudRate.Enabled = true;
                    cmbDataBit.Enabled = true;
                    cmbParityBit.Enabled = true;
                    cmbStopBit.Enabled = true;
                    cmbFlowCtrl.Enabled = true;
                    serialPort1.Close();
                    return;
                }
            }
            else
            {
                try
                {
                    btnOpenCom.Text = "打开串口";
                    picConnectStatus.Image = Properties.Resources.Disconnected_48px;
                    tssLabCom.ForeColor = Color.Red;
                    tssLabCom.Text = "COMx: Closed";
                    cmbCom.Enabled = true;
                    cmbBaudRate.Enabled = true;
                    cmbDataBit.Enabled = true;
                    cmbParityBit.Enabled = true;
                    cmbStopBit.Enabled = true;
                    cmbFlowCtrl.Enabled = true;
                    serialPort1.Close();
                    
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show("错误:" + ex.Message, "O-ComTool 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
        }

        private static StringBuilder GetHexString(byte[] data, int offset, int length)
        {
            StringBuilder sb = new StringBuilder(length * 3);
            for (int i = offset; i < (offset + length); i++)
            {
                sb.Append(data[i].ToString("X2") + " ");
            }
            return sb;
        }

        void WriteLog(string str)
        {
            // 接收线程与 UI 线程都会调用，必须加锁；使用单个长生命周期 StreamWriter，避免每次写入产生 BOM
            lock (logLock)
            {
                try
                {
                    if (!chkAutoSave.Checked || log_sw == null) return;
                    log_sw.Write(str);
                    log_sw.Flush();
                }
                catch (ObjectDisposedException)
                {
                    // 日志正被关闭，忽略
                }
                catch (System.IO.IOException)
                {
                    // 磁盘 IO 异常，忽略以免拖垮接收
                }
            }
        }

        private void serialPort1_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            // 关闭串口/退出时直接返回，避免回调命中已释放的句柄
            if (serialPort1 == null || !serialPort1.IsOpen) return;
            if (this.IsDisposed || !this.IsHandleCreated) return;

            try
            {
                int RecLen = serialPort1.BytesToRead;
                if (RecLen <= 0) return;

                // 读取当前缓冲中所有可用字节；SerialPort.Read 可能少于请求量，需循环读满
                byte[] RecBuf = new byte[RecLen];
                int totalRead = 0;
                while (totalRead < RecLen && serialPort1.IsOpen)
                {
                    int n = serialPort1.Read(RecBuf, totalRead, RecLen - totalRead);
                    if (n <= 0) break;
                    totalRead += n;
                }
                if (totalRead == 0) return;
                RecLen = totalRead;

                spRxCount += RecLen;
                spFrameRxCount += 1;

                this.Invoke((EventHandler)(delegate
                {
                    if (this.IsDisposed) return;
                    UpdateCounters();

                    StringBuilder tmp_rx_sb = new StringBuilder(50);
                    if (chkShowTime.Checked == true)
                    {
                        string TimeStamp = DateTime.Now.ToString("HH:mm:ss:fff-< ");
                        if (time_newline_enable == true) TimeStamp += "\r\n";
                        tmp_rx_sb.Append(TimeStamp);
                        TimeStamp = DateTime.Now.ToString("yyyy-MM-dd ");//写入文件时添加年月
                        WriteLog(TimeStamp);
                    }
                    if (radHexReceive.Checked == true)
                    {
                        tmp_rx_sb.Append(GetHexString(RecBuf, 0, RecLen).ToString());
                    }
                    else if (radAsciiReceive.Checked == true)
                    {
                        tmp_rx_sb.Append(Encoding.UTF8.GetString(RecBuf, 0, RecLen));
                    }

                    if (chkAutoLine.Checked == true)
                    {
                        tmp_rx_sb.Append("\r\n");
                    }

                    if (chkAutoReply.Checked == true)
                    {
                        timerAutoReply.Interval = (int)nudReplyDelay.Value;
                        timerAutoReply.Start();
                    }

                    WriteLog(tmp_rx_sb.ToString());

                    string tmp_str = tmp_rx_sb.ToString();

                    fctbReceive.AppendText(tmp_str);
                    if (o_ScrollBar1.Maximum - (o_ScrollBar1.Value + o_ScrollBar1.ThumbSize) < 20) fctbReceive.GoEnd();



                    picReceiveLed.Image = Properties.Resources.Led_Green_50px;
                    timerReceiveLed.Interval = 100;
                    timerReceiveLed.Start();
                }));
            }
            catch (InvalidOperationException)
            {
                // 串口关闭/竞态期间偶发，忽略即可，不要吞掉其他异常
            }
            catch (System.IO.IOException)
            {
                // 串口 IO 异常（如 USB 拔出），忽略
            }
        }

        private void cmbCom_DropDown(object sender, EventArgs e)
        {
            cmbCom.Items.Clear();
            foreach (string com in System.IO.Ports.SerialPort.GetPortNames())  //自动获取串行口名称
                this.cmbCom.Items.Add(com);
        }

        private void picDisplay_Click(object sender, EventArgs e)
        {
          
        }

        /// <summary>
        /// 接收led闪烁
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void timerReceiveLed_Tick(object sender, EventArgs e)
        {
            picReceiveLed.Image = Properties.Resources.Led_Red_50px;
            timerReceiveLed.Stop();
        }

        /// <summary>
        /// 自动回复功能，仅通用发送支持
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void timerAutoReply_Tick(object sender, EventArgs e)
        {
            this.btnSend_Click(null, null);
            timerAutoReply.Stop();
        }

        private void timerRepeat_Tick(object sender, EventArgs e)
        {
            this.btnSend_Click(null, null);
        }

        private void chkAutoSave_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutoSave.Checked == true)
            {
                //设置文件类型  
                saveFileDialog1.Filter = "文本文件|*.txt|所有文件(*.*)|*.*";
                saveFileDialog1.FileName = DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + "Log.txt";
                //保存对话框是否记忆上次打开的目录  
                saveFileDialog1.RestoreDirectory = true;

                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    log_save_path = saveFileDialog1.FileName.ToString();
                    toolTip1.SetToolTip(chkAutoSave, log_save_path);
                    lock (logLock)
                    {
                        log_fs = new FileStream(log_save_path, FileMode.Append, FileAccess.Write, FileShare.Read);
                        log_sw = new StreamWriter(log_fs);   // 复用同一个 writer，UTF-8 BOM 只写一次
                    }
                    WriteLog(fctbReceive.Text);
                }
                else
                {
                    chkAutoSave.Checked = false;
                    return;
                }
            }
            else
            {
                lock (logLock)
                {
                    toolTip1.SetToolTip(chkAutoSave, "保存路径为空");
                    if (log_sw != null) { log_sw.Dispose(); log_sw = null; }
                    log_fs = null;
                }
            }
        }

        private void btnLoadFile_Click(object sender, EventArgs e)
        {
            openFileDialog1.Title = "请选择文件";
            openFileDialog1.Filter = "文本文件|*.txt|所有文件(*.*)|*.*";
            openFileDialog1.FileName = "";
            openFileDialog1.RestoreDirectory = true;
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                btnEditFile.Enabled = true;
                load_file_path = openFileDialog1.FileName;
                load_file(load_file_path);
            }
            else
            {
                //btnEditFile.Enabled = false;
                //cmbLoadFile.Items.Clear();
                return;
            }
        }

        private void cmbLoadFile_SelectedIndexChanged(object sender, EventArgs e)
        {
            txbSend.Clear();
            txbSend.AppendText(cmbLoadFile.Text);
        }

#region
        private const int WS_HSCROLL = 0x100000;
        private const int WS_VSCROLL = 0x200000;
        private const int GWL_STYLE = (-16);
         [System.Runtime.InteropServices.DllImport("user32",CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int GetWindowLong(IntPtr hwnd, int nIndex);
        /// <summary>
        /// 判断是否出现垂直滚动条
        /// </summary>
        /// <param name="ctrl"></param>
        /// <returns></returns>
        internal static bool IsVerticalScrollBarVisible(Control ctrl)
        {
            if (!ctrl.IsHandleCreated)
                return false;

            return (GetWindowLong(ctrl.Handle, GWL_STYLE) & WS_VSCROLL) != 0;
        }
        /// <summary>
        /// 判断是否出现水平滚动条
        /// </summary>
        /// <param name="ctrl"></param>
        /// <returns></returns>
        internal static bool IsHorizontalScrollBarVisible(Control ctrl)
        {
            if (!ctrl.IsHandleCreated)
                return false;
            return (GetWindowLong(ctrl.Handle, GWL_STYLE) & WS_HSCROLL) != 0;
        }
#endregion
        private void picAddQuickSend_Click(object sender, EventArgs e)
        {
            if (quicksend_list.Count >= 30)
            {
                MessageBox.Show("达到最大条数！", "O-ComTool 警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            add_quick_send((short)quicksend_list.Count, "Name", "Data");
            // 判断是否出现滚动条
            if (IsVerticalScrollBarVisible(tbpQuick))
            {
                tbpQuick.VerticalScroll.Value = tbpQuick.VerticalScroll.Maximum;
            }
            else
            {
                tbpQuick.VerticalScroll.Value = 0;
            }

        }

        private void tbpQuick_Scroll(object sender, ScrollEventArgs e)
        {
            //MessageBox.Show(tbpQuick.VerticalScroll.Maximum + "");
            //e.NewValue = e.OldValue + 1;
        }

        private void tbpQuick_MouseWheel(object sender, MouseEventArgs e)
        {
            //MessageBox.Show(tbpQuick.VerticalScroll.Value+"");
            //tbpQuick.VerticalScroll.Value++;
        }
        private void tbpQuick_SizeChanged(object sender, EventArgs e)
        {
            foreach (QuickSend tmp_quick_send in quicksend_list)
            {
                tmp_quick_send.Width = tbpQuick.Width - 65;
            }
            
        }

        private void tbpQuick_Click(object sender, EventArgs e)
        {
            tbpQuick.Focus();
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (tabControl1.SelectedIndex == 1)
            {
                picAddQuickSend.Visible = true;
            }
            else
            {
                picAddQuickSend.Visible = false;
            }
        }

        private void chkShowTime_CheckedChanged(object sender, EventArgs e)
        {
            if ((chkShowTime.Checked == true))
            {
                chkAutoLine.Checked = true;
                if (fctbReceive.TextLength > 0)
                fctbReceive.AppendText("\r\n");
            }
        }

        private void tssImageChange_Click(object sender, EventArgs e)
        {
            FrameOrByte = !FrameOrByte;
            UpdateCounters();
        }

        private void tssLabReset_Click(object sender, EventArgs e)
        {
            spRxCount = 0;
            spTxCount = 0;
            spFrameRxCount = 0;
            spFrameTxCount = 0;
            UpdateCounters();
        }

        private void chkRepeatSend_CheckedChanged(object sender, EventArgs e)
        {
            if (chkRepeatSend.Checked == false)
            {
                timerRepeat.Stop();
                btnSend.Enabled = true;
                nudRepeatInterval.Enabled = true;
            }
        }

        private void load_file_exit(object sender, EventArgs e)
        {
            if (load_file_path != "")
            {
                load_file(load_file_path);
            }

        }

        private void btnEditFile_Click(object sender, EventArgs e)
        {
            Process Proc = new Process();
            ProcessStartInfo Info = new ProcessStartInfo();
            Info.FileName = "notepad.exe";
            Info.Arguments = load_file_path;
            Info.WorkingDirectory = "C://";

            try
            {
                Proc = Process.Start(Info);
                //MessageBox.Show("pid:"+Proc.Id);
                Proc.EnableRaisingEvents = true;//设置进程终止时触发
                Proc.Exited += new EventHandler(load_file_exit);//发现外部程序关闭即触发方法load_file_exit
            }
            catch
            {
                MessageBox.Show("无法找到notepad.exe，文件打开失败！","O-ComTool 错误", MessageBoxButtons.OK,MessageBoxIcon.Error);
                return;
            }
        }

        private void btnClearReceive_Click(object sender, EventArgs e)
        {
            fctbReceive.Clear();
        }

        private void btnClearSend_Click(object sender, EventArgs e)
        {
            txbSend.Clear();
        }

        private void chkAutoCount_CheckedChanged(object sender, EventArgs e)
        {
            if (chkAutoCount.Checked == true)
            {
                AutoCountNum = 0;
            }
        }

        private void tssImageChange_MouseDown(object sender, MouseEventArgs e)
        {
            tssImageChange.Image = Properties.Resources.ReplaceDown_48px;
        }

        private void tssImageChange_MouseUp(object sender, MouseEventArgs e)
        {
            tssImageChange.Image = Properties.Resources.Replace_48px;
        }

        private void picAddQuickSend_MouseDown(object sender, MouseEventArgs e)
        {
            picAddQuickSend.Image = Properties.Resources.AddDown_52px;
        }

        private void picAddQuickSend_MouseUp(object sender, MouseEventArgs e)
        {
            picAddQuickSend.Image = Properties.Resources.Add_48px;
        }

        bool IsP1Collapsed = false;
        private void splitContainer1_Click(object sender, EventArgs e)
        {
            if (IsP1Collapsed == false)
            {
                splitContainer1.SplitterDistance = 0;
                tsmClearSend.Visible = true;
                tsmClearRece.Visible = true;
                IsP1Collapsed = true;
            }
            else
            {
                splitContainer1.SplitterDistance = 140;
                tsmClearSend.Visible = false;
                tsmClearRece.Visible = false;
                IsP1Collapsed = false;
            }
        }

        private void splitContainer2_SplitterMoved(object sender, SplitterEventArgs e)
        {
            if (splitContainer2.SplitterDistance > splitContainer2.Height - 60)
            {
                splitContainer2.SplitterDistance = splitContainer2.Height - 5;
            }
            else if ((splitContainer2.SplitterDistance < (splitContainer2.Height - 60)) && (splitContainer2.SplitterDistance > (splitContainer2.Height - 120)))
            {
                splitContainer2.SplitterDistance =  splitContainer2.Height - 120;
            }
        }

        private void tsmCalc_Click(object sender, EventArgs e)
        {
            Process.Start(@"c:\windows\system32\calc.exe");
        }

        private void tsmDevMgmt_Click(object sender, EventArgs e)
        {
            Process.Start("devmgmt.msc");
        }

        private void tsmOpenLogPath_Click(object sender, EventArgs e)
        {
            if (log_save_path != "")
            {
                System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(log_save_path));
            }
            else
            {
                MessageBox.Show("日志目录未指定！","O-ComTool 错误", MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
        }

        private void llabAd_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {

        }

        private void tsmHomePage_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://www.ifreehub.com");
        }

        private void tsmChangeDisplay_Click(object sender, EventArgs e)
        {
            display_plan1_active = !display_plan1_active;
            ApplyDisplayPlan(display_plan1_active);
        }

        private void MainForm_SizeChanged(object sender, EventArgs e)
        {
            if (app.Default.MinToTray == true && this.WindowState == FormWindowState.Minimized)
            {
                //this.ShowInTaskbar = false;
                notifyIcon1.Text = this.Text;
                notifyIcon1.Visible = true;
            }
        }

        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (this.WindowState == System.Windows.Forms.FormWindowState.Minimized)
            {
                notifyIcon1.Visible = false;
                this.WindowState = System.Windows.Forms.FormWindowState.Normal;
                //this.Show();
                this.ShowInTaskbar = true;
            }
            else
            {
                this.Activate();
            }
        }

        void SaveConfig()
        {
            // 串口设置
            app.Default.cmbBaudRate = cmbBaudRate.Text;
            app.Default.cmbDataBitIndex = (short)cmbDataBit.SelectedIndex;
            app.Default.cmbParityBitIndex = (short)cmbParityBit.SelectedIndex;
            app.Default.cmbStopBitIndex = (short)cmbStopBit.SelectedIndex;
            app.Default.cmbFlowCtrlIndex = (short)cmbFlowCtrl.SelectedIndex;

            // 接收设置
            app.Default.receEncodeAscii = radAsciiReceive.Checked == true ? true : false;
            app.Default.chkAutoLine = chkAutoLine.Checked == true ? true : false;
            app.Default.chkShowTime = chkShowTime.Checked == true ? true : false;
            app.Default.chkAutoReply = chkAutoReply.Checked == true ? true : false;
            app.Default.AutoReplyDelay = (int)nudReplyDelay.Value;

            // 发送设置
            app.Default.sendEncodeAscii = radAsciiSend.Checked == true ? true : false;
            app.Default.chkAppendNewLine = chkNewLine.Checked == true ? true : false;
            app.Default.chkAutoCount = chkAutoCount.Checked == true ? true : false;
            app.Default.chkRepeatSend = chkRepeatSend.Checked == true ? true : false;
            app.Default.RepeatInterval = (int)nudRepeatInterval.Value;

            // 发送数据
            app.Default.GeneralSendData = txbSend.Text;
            
            // 快捷发送保存
            app.Default.QuickSendCount = (short)quicksend_list.Count;
            app.Default.QuickSendTitle.Clear();
            app.Default.QuickSendData.Clear();

            app.Default.DisplayPlan1Enable = display_plan1_active;

            for (int i = 0; i < quicksend_list.Count; i++)
            {
                app.Default.QuickSendTitle.Add(quicksend_list[i].ItemName);
                app.Default.QuickSendData.Add(quicksend_list[i].Data); 
            }
            app.Default.Save();

        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {

            if (app.Default.CloseToTray == true)
            {
                e.Cancel = true;
                notifyIcon1.Text = this.Text;
                this.WindowState = FormWindowState.Minimized;
                //this.Hide();
                notifyIcon1.Visible = true;
                this.ShowInTaskbar = false;
            }
            else
            {
                if (app.Default.QuitConfirm == true)
                {
                    if (MessageBox.Show("确认退出当前串口工具？", "O-ComTool 确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.Cancel)
                    {
                        e.Cancel = true;
                    }
                }
                
                SaveConfig();
                if (serialPort1.IsOpen == true) serialPort1.Close();
                lock (logLock)
                {
                    if (log_sw != null) { log_sw.Dispose(); log_sw = null; }
                    log_fs = null;
                }
                
                notifyIcon1.Dispose();

            }
        }

        private void tsmExit_Click(object sender, EventArgs e)
        {
            SaveConfig();
            notifyIcon1.Dispose();
            System.Environment.Exit(0);
        }

        private void cmsExit_Click(object sender, EventArgs e)
        {
            SaveConfig();
            notifyIcon1.Dispose();
            System.Environment.Exit(0);
        }

        private void cmsAbout_Click(object sender, EventArgs e)
        {
            About about = new About();
            about.ShowDialog();
        }

        private void cmsTitle_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("http://www.ifreehub.com");
        }

        private void cmsSelectAll_Click(object sender, EventArgs e)
        {
            ((FastColoredTextBox)contextMenuStrip2.SourceControl).SelectAll();
        }

        private void cmsCopy_Click(object sender, EventArgs e)
        {
            string selectText = ((FastColoredTextBox)contextMenuStrip2.SourceControl).SelectedText;
            if (selectText != "")
            {
                Clipboard.SetText(selectText);
            }
        }

        private void cmsPaste_Click(object sender, EventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                FastColoredTextBox txtBox = (FastColoredTextBox)contextMenuStrip2.SourceControl;
                int index = txtBox.SelectionStart;  //记录下粘贴前的光标位置
                string text = txtBox.Text;
                //删除选中的文本
                text = text.Remove(txtBox.SelectionStart, txtBox.SelectionLength);
                //在当前光标输入点插入剪切板内容
                text = text.Insert(txtBox.SelectionStart, Clipboard.GetText());
                txtBox.Text = text;
                //重设光标位置
                txtBox.SelectionStart = index;
            }
        }

        byte RightKeyCheckSum(byte[] buffer, int length)
        {
            byte CS = 0;
            for (int i = 0; i < length; i++)
                CS += buffer[i];
            return CS;
        }

        private void cmsCheckSum_Click(object sender, EventArgs e)
        {
            string selectText = ((FastColoredTextBox)contextMenuStrip2.SourceControl).SelectedText;
            byte[] bytesToCheck = ParseHexBytes(selectText);
            MessageBox.Show("校验和：0x" + RightKeyCheckSum(bytesToCheck, bytesToCheck.Length).ToString("X2"), "O-ComTool 校验和", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        byte RightKeyXOR(byte[] buffer, int length)
        {
            byte xor = 0;
            for (int i = 0; i < length; i++)
                xor ^= buffer[i];
            return xor;
        }
        private void cmsXor_Click(object sender, EventArgs e)
        {
            string selectText = ((FastColoredTextBox)contextMenuStrip2.SourceControl).SelectedText;
            byte[] bytesToCheck = ParseHexBytes(selectText);
            MessageBox.Show("异或值：0x" + RightKeyXOR(bytesToCheck, bytesToCheck.Length).ToString("X2"), "O-ComTool 异或值", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        private void contextMenuStrip2_Opening(object sender, CancelEventArgs e)
        {
            //没有选择文本时，复制菜单禁用
            string selectText = ((FastColoredTextBox)contextMenuStrip2.SourceControl).SelectedText;
            if (selectText != "")
            {
                cmsCopy.Enabled = true;
                cmsCheckSum.Enabled = true;
                cmsXor.Enabled = true;
                cmsA2H.Enabled = true;
                cmsH2A.Enabled = true;
                cmsHexFormat.Enabled = true;
                cmsCalcLength.Enabled = true;
            }
            else
            {
                cmsCopy.Enabled = false;
                cmsCheckSum.Enabled = false;
                cmsXor.Enabled = false;
                cmsA2H.Enabled = false;
                cmsH2A.Enabled = false;
                cmsHexFormat.Enabled = false;
                cmsCalcLength.Enabled = false;
            }
            //剪切板没有文本内容时，粘贴菜单禁用
            if (Clipboard.ContainsText())
            {
                cmsPaste.Enabled = true;
            }
            else
            {
                cmsPaste.Enabled = false;
            }
        }

        private void cmsCalcLength_Click(object sender, EventArgs e)
        {
            string selectText = ((FastColoredTextBox)contextMenuStrip2.SourceControl).SelectedText;
            int hexLen = ParseHexBytes(selectText).Length;
            string str = "ASCII长度：" + selectText.Length + " (0x" + selectText.Length.ToString("X2") + ")" + " Bytes\n";
            str += "  HEX长度：" + hexLen + " (0x" + hexLen.ToString("X2") + ")" + " Bytes\n";

            MessageBox.Show(str, "O-ComTool 字符长度", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        private void cmsH2A_Click(object sender, EventArgs e)
        {
            string selectText = ((FastColoredTextBox)contextMenuStrip2.SourceControl).SelectedText;
            byte[] bytesToCheck = ParseHexBytes(selectText);
            string str = Encoding.UTF8.GetString(bytesToCheck, 0, bytesToCheck.Length);
            if (MessageBox.Show("Hex2Ascii：" + str, "O-ComTool Hex2Ascii", MessageBoxButtons.OKCancel, MessageBoxIcon.None) == DialogResult.OK)
            {
                Clipboard.SetText(str);
            }

        }

        private void cmsA2H_Click(object sender, EventArgs e)
        {
            string selectText = ((FastColoredTextBox)contextMenuStrip2.SourceControl).SelectedText;
            byte[] ba = System.Text.ASCIIEncoding.Default.GetBytes(selectText);
            StringBuilder sb = new StringBuilder();
            foreach (byte b in ba)
            {
                sb.Append(b.ToString("X2") + " ");
            }
            string str = sb.ToString();
            if (MessageBox.Show("Ascii2Hex：" + str, "O-ComTool Ascii2Hex", MessageBoxButtons.OKCancel, MessageBoxIcon.None) == DialogResult.OK)
            {
                Clipboard.SetText(str);
            }
        }

        private void cmsHexFormat_Click(object sender, EventArgs e)
        {
            string selectText = ((FastColoredTextBox)contextMenuStrip2.SourceControl).SelectedText;
            byte[] bytesToCheck = ParseHexBytes(selectText);

            fctbReceive.SelectedText = GetHexString(bytesToCheck, 0, bytesToCheck.Length).ToString();
        }

        private void contextMenuStrip3_Opening(object sender, CancelEventArgs e)
        {
            //没有选择文本时，复制菜单禁用
            string selectText = ((TextBox)contextMenuStrip3.SourceControl).SelectedText;
            if (selectText != "")
            {
                cmstbCopy.Enabled = true;
                cmstbCheckSum.Enabled = true;
                cmstbXor.Enabled = true;
                cmstbA2H.Enabled = true;
                cmstbH2A.Enabled = true;
                cmstbHexFormat.Enabled = true;
                cmstbCalcLength.Enabled = true;
            }
            else
            {
                cmstbCopy.Enabled = false;
                cmstbCheckSum.Enabled = false;
                cmstbXor.Enabled = false;
                cmstbA2H.Enabled = false;
                cmstbH2A.Enabled = false;
                cmstbHexFormat.Enabled = false;
                cmstbCalcLength.Enabled = false;
            }
            // 注意：文本框右键菜单（contextMenuStrip3）没有粘贴项；
            // 此前误改的是 FCTB 菜单（contextMenuStrip2）的 cmsPaste，已移除该错误逻辑。
        }

        private void cmstbSelectAll_Click(object sender, EventArgs e)
        {
            ((TextBox)contextMenuStrip3.SourceControl).SelectAll();
        }

        private void cmstbCopy_Click(object sender, EventArgs e)
        {
            string selectText = ((TextBox)contextMenuStrip3.SourceControl).SelectedText;
            if (selectText != "")
            {
                Clipboard.SetText(selectText);
            }
        }

        private void toolStripMenuItem3_Click(object sender, EventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                TextBox txtBox = (TextBox)contextMenuStrip3.SourceControl;
                int index = txtBox.SelectionStart;  //记录下粘贴前的光标位置
                string text = txtBox.Text;
                //删除选中的文本
                text = text.Remove(txtBox.SelectionStart, txtBox.SelectionLength);
                //在当前光标输入点插入剪切板内容
                text = text.Insert(txtBox.SelectionStart, Clipboard.GetText());
                txtBox.Text = text;
                //重设光标位置
                txtBox.SelectionStart = index;
            }
        }

        private void cmstbH2A_Click(object sender, EventArgs e)
        {
            string selectText = ((TextBox)contextMenuStrip3.SourceControl).SelectedText;
            byte[] bytesToCheck = ParseHexBytes(selectText);
            string str = Encoding.UTF8.GetString(bytesToCheck, 0, bytesToCheck.Length);
            if (MessageBox.Show("Hex2Ascii：" + str, "O-ComTool Hex2Ascii", MessageBoxButtons.OKCancel, MessageBoxIcon.None) == DialogResult.OK)
            {
                Clipboard.SetText(str);
            }

        }

        private void cmstbA2H_Click(object sender, EventArgs e)
        {
            //string selectText = ((TextBox)contextMenuStrip3.SourceControl).SelectedText;
            //byte[] ba = System.Text.ASCIIEncoding.Default.GetBytes(selectText);
            //StringBuilder sb = new StringBuilder();
            //foreach (byte b in ba)
            //{
            //    sb.Append(b.ToString("X2") + " ");
            //}
            ////rtbReceive.SelectedText = sb.ToString();
            //MessageBox.Show("Ascii2Hex：" + sb.ToString(), "O-ComTool Ascii2Hex", MessageBoxButtons.OK, MessageBoxIcon.None);

            string selectText = ((TextBox)contextMenuStrip3.SourceControl).SelectedText;
            byte[] ba = System.Text.ASCIIEncoding.Default.GetBytes(selectText);
            StringBuilder sb = new StringBuilder();
            foreach (byte b in ba)
            {
                sb.Append(b.ToString("X2") + " ");
            }
            string str = sb.ToString();
            if (MessageBox.Show("Ascii2Hex：" + str, "O-ComTool Ascii2Hex", MessageBoxButtons.OKCancel, MessageBoxIcon.None) == DialogResult.OK)
            {
                Clipboard.SetText(str);
            }

        }

        private void cmstbHexFormat_Click(object sender, EventArgs e)
        {
            string selectText = ((TextBox)contextMenuStrip3.SourceControl).SelectedText;
            byte[] bytesToCheck = ParseHexBytes(selectText);

            txbSend.SelectedText = GetHexString(bytesToCheck, 0, bytesToCheck.Length).ToString();
        }

        private void cmstbCalcLength_Click(object sender, EventArgs e)
        {
            string selectText = ((TextBox)contextMenuStrip3.SourceControl).SelectedText;
            int hexLen = ParseHexBytes(selectText).Length;
            string str = "ASCII长度：" + selectText.Length + " (0x" + selectText.Length.ToString("X2") + ")" + " Bytes\n";
            str += "  HEX长度：" + hexLen + " (0x" + hexLen.ToString("X2") + ")" + " Bytes\n";

            MessageBox.Show(str, "O-ComTool 字符长度", MessageBoxButtons.OK, MessageBoxIcon.None);

        }

        private void cmstbCheckSum_Click(object sender, EventArgs e)
        {
            string selectText = ((TextBox)contextMenuStrip3.SourceControl).SelectedText;
            byte[] bytesToCheck = ParseHexBytes(selectText);
            MessageBox.Show("校验和：0x" + RightKeyCheckSum(bytesToCheck, bytesToCheck.Length).ToString("X2"), "O-ComTool 校验和", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        private void cmstbXor_Click(object sender, EventArgs e)
        {
            string selectText = ((TextBox)contextMenuStrip3.SourceControl).SelectedText;
            byte[] bytesToCheck = ParseHexBytes(selectText);
            MessageBox.Show("异或值：0x" + RightKeyXOR(bytesToCheck, bytesToCheck.Length).ToString("X2"), "O-ComTool 异或值", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        private void cmsSaveCur_Click(object sender, EventArgs e)
        {
            string tmp_path = "";
            FileStream tmp_fs;
            
            //设置文件类型  
            saveFileDialog1.Filter = "文本文件|*.txt|所有文件(*.*)|*.*";
            saveFileDialog1.FileName = DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + "Log.txt";
            //保存对话框是否记忆上次打开的目录  
            saveFileDialog1.RestoreDirectory = true;

            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                tmp_path = saveFileDialog1.FileName.ToString();
                tmp_fs = new FileStream(tmp_path, FileMode.Append);
                StreamWriter fsw = new StreamWriter(tmp_fs);
                fsw.Write(fctbReceive.Text);
                fsw.Flush();
                tmp_fs.Close();
            }
            else
            {
                return;
            }
        }

        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section, string key, string val, string filepath);

        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string def, StringBuilder retval, int size, string filePath);

        string Color2Hex(Color s_color)
        {
            string d_color_str;
            d_color_str = "#" + /*s_color.A.ToString("x2") + */s_color.R.ToString("x2") + s_color.G.ToString("x2") + s_color.B.ToString("x2");
            return d_color_str.ToUpper();
        }
        string Font2Str(Font s_font)
        {
            string d_font_str = s_font.Name + "," + s_font.SizeInPoints + "pt";
            return d_font_str;
        }
        // INI 配置项的单一定义点：导出/导入共用同一张表，杜绝两侧字段漂移。
        private class IniField
        {
            public string Section;
            public string Key;
            public Func<string> Get;
            public Action<string> Set;
        }

        private static IniField StrF(string section, string key, Func<string> get, Action<string> set)
        {
            return new IniField { Section = section, Key = key, Get = get, Set = set };
        }
        private static IniField BoolF(string section, string key, Func<bool> get, Action<bool> set)
        {
            return new IniField { Section = section, Key = key, Get = () => get().ToString(), Set = v => set(Convert.ToBoolean(v)) };
        }
        private static IniField IntF(string section, string key, Func<int> get, Action<int> set)
        {
            return new IniField { Section = section, Key = key, Get = () => get().ToString(), Set = v => set(int.Parse(v)) };
        }
        private static IniField ShortF(string section, string key, Func<short> get, Action<short> set)
        {
            return new IniField { Section = section, Key = key, Get = () => get().ToString(), Set = v => set(short.Parse(v)) };
        }
        private IniField ColorF(string section, string key, Func<Color> get, Action<Color> set)
        {
            return new IniField { Section = section, Key = key, Get = () => Color2Hex(get()), Set = v => set(System.Drawing.ColorTranslator.FromHtml(v)) };
        }
        private static IniField FontF(string section, string key, Func<Font> get, Action<Font> set, FontConverter cvt)
        {
            return new IniField { Section = section, Key = key, Get = () => cvt.ConvertToString(get()), Set = v => set(cvt.ConvertFromString(v) as Font) };
        }

        private List<IniField> BuildIniFields(FontConverter cvt)
        {
            return new List<IniField>
            {
                // SerialPort
                StrF("SerialPort", "cmbBaudRate", () => cmbBaudRate.Text, v => app.Default.cmbBaudRate = v),
                ShortF("SerialPort", "cmbDataBitIndex", () => (short)cmbDataBit.SelectedIndex, v => app.Default.cmbDataBitIndex = v),
                ShortF("SerialPort", "cmbParityBitIndex", () => (short)cmbParityBit.SelectedIndex, v => app.Default.cmbParityBitIndex = v),
                ShortF("SerialPort", "cmbStopBitIndex", () => (short)cmbStopBit.SelectedIndex, v => app.Default.cmbStopBitIndex = v),
                ShortF("SerialPort", "cmbFlowCtrlIndex", () => (short)cmbFlowCtrl.SelectedIndex, v => app.Default.cmbFlowCtrlIndex = v),

                // Receive
                BoolF("Receive", "receEncodeAscii", () => radAsciiReceive.Checked, v => app.Default.receEncodeAscii = v),
                BoolF("Receive", "chkAutoLine", () => chkAutoLine.Checked, v => app.Default.chkAutoLine = v),
                BoolF("Receive", "chkShowTime", () => chkShowTime.Checked, v => app.Default.chkShowTime = v),
                BoolF("Receive", "chkAutoReply", () => chkAutoReply.Checked, v => app.Default.chkAutoReply = v),
                IntF("Receive", "AutoReplyDelay", () => (int)nudReplyDelay.Value, v => app.Default.AutoReplyDelay = v),

                // Send
                BoolF("Send", "sendEncodeAscii", () => radAsciiSend.Checked, v => app.Default.sendEncodeAscii = v),
                BoolF("Send", "chkAppendNewLine", () => chkNewLine.Checked, v => app.Default.chkAppendNewLine = v),
                BoolF("Send", "chkAutoCount", () => chkAutoCount.Checked, v => app.Default.chkAutoCount = v),
                BoolF("Send", "chkRepeatSend", () => chkRepeatSend.Checked, v => app.Default.chkRepeatSend = v),
                IntF("Send", "RepeatInterval", () => (int)nudRepeatInterval.Value, v => app.Default.RepeatInterval = v),

                // GeneralSend
                StrF("GeneralSend", "GeneralSendData", () => txbSend.Text, v => app.Default.GeneralSendData = v),
                BoolF("GeneralSend", "LoadFileEnable", () => app.Default.LoadFileEnable, v => app.Default.LoadFileEnable = v),
                StrF("GeneralSend", "LoadFilePath", () => load_file_path, v => app.Default.LoadFilePath = v),

                // Option
                BoolF("Option", "MinToTray", () => app.Default.MinToTray, v => app.Default.MinToTray = v),
                BoolF("Option", "CloseToTray", () => app.Default.CloseToTray, v => app.Default.CloseToTray = v),
                ColorF("Option", "ReceBackColor1", () => app.Default.ReceBackColor1, v => app.Default.ReceBackColor1 = v),
                ColorF("Option", "ReceForeColor1", () => app.Default.ReceForeColor1, v => app.Default.ReceForeColor1 = v),
                ColorF("Option", "SendBackColor1", () => app.Default.SendBackColor1, v => app.Default.SendBackColor1 = v),
                ColorF("Option", "SendForeColor1", () => app.Default.SendForeColor1, v => app.Default.SendForeColor1 = v),
                FontF("Option", "ReceFont1", () => app.Default.ReceFont1, v => app.Default.ReceFont1 = v, cvt),
                FontF("Option", "SendFont1", () => app.Default.SendFont1, v => app.Default.SendFont1 = v, cvt),
                ColorF("Option", "ReceBackColor2", () => app.Default.ReceBackColor2, v => app.Default.ReceBackColor2 = v),
                ColorF("Option", "ReceForeColor2", () => app.Default.ReceForeColor2, v => app.Default.ReceForeColor2 = v),
                ColorF("Option", "SendBackColor2", () => app.Default.SendBackColor2, v => app.Default.SendBackColor2 = v),
                ColorF("Option", "SendForeColor2", () => app.Default.SendForeColor2, v => app.Default.SendForeColor2 = v),
                FontF("Option", "ReceFont2", () => app.Default.ReceFont2, v => app.Default.ReceFont2 = v, cvt),
                FontF("Option", "SendFont2", () => app.Default.SendFont2, v => app.Default.SendFont2 = v, cvt),
                BoolF("Option", "QuitConfirm", () => app.Default.QuitConfirm, v => app.Default.QuitConfirm = v),
                BoolF("Option", "HightLightEnable", () => app.Default.HightLightEnable, v => app.Default.HightLightEnable = v),
                BoolF("Option", "SendDisplayEnable", () => app.Default.SendDisplayEnable, v => app.Default.SendDisplayEnable = v),
                BoolF("Option", "CommentEnable", () => app.Default.CommentEnable, v => app.Default.CommentEnable = v),
                IntF("Option", "FrameInterval", () => app.Default.FrameInterval, v => app.Default.FrameInterval = v),
                BoolF("Option", "TimeNewline", () => app.Default.chkTimeNewLine, v => app.Default.chkTimeNewLine = v),
                BoolF("Option", "Send2FileEnable", () => app.Default.Send2FileEnable, v => app.Default.Send2FileEnable = v),
                BoolF("Option", "Send2NewLineEnable", () => app.Default.Send2NewLineEnable, v => app.Default.Send2NewLineEnable = v),
                BoolF("Option", "DisplayPlan1Enable", () => app.Default.DisplayPlan1Enable, v => app.Default.DisplayPlan1Enable = v),

                // 高亮正则
                StrF("Option", "HighLightRed", () => app.Default.HighLightRed, v => app.Default.HighLightRed = v),
                StrF("Option", "HighLightGreen", () => app.Default.HighLightGreen, v => app.Default.HighLightGreen = v),
                StrF("Option", "HighLightYellow", () => app.Default.HighLightYellow, v => app.Default.HighLightYellow = v),
                StrF("Option", "HighLightBlue", () => app.Default.HighLightBlue, v => app.Default.HighLightBlue = v),
                StrF("Option", "HighLightMagenta", () => app.Default.HighLightMagenta, v => app.Default.HighLightMagenta = v),
                StrF("Option", "HighLightCyan", () => app.Default.HighLightCyan, v => app.Default.HighLightCyan = v),
                StrF("Option", "HighLightOrange", () => app.Default.HighLightOrange, v => app.Default.HighLightOrange = v),
            };
        }

        private void tsmExportConfig_Click(object sender, EventArgs e)
        {
            //设置文件类型
            saveFileDialog1.Filter = "文本文件|*.ini|所有文件(*.*)|*.*";
            saveFileDialog1.FileName = "O-ComTool_cfg.ini";
            //保存对话框是否记忆上次打开的目录
            saveFileDialog1.RestoreDirectory = true;

            if (saveFileDialog1.ShowDialog() != DialogResult.OK)
            {
                chkAutoSave.Checked = false;
                return;
            }
            string file_path = saveFileDialog1.FileName.ToString();

            // 标量配置项统一写出
            var cvt = new FontConverter();
            foreach (var f in BuildIniFields(cvt))
            {
                WritePrivateProfileString(f.Section, f.Key, f.Get() ?? "", file_path);
            }

            // 快捷发送列表（数量不定，逐条存储）
            const string qs = "QuickSend";
            WritePrivateProfileString(qs, "QuickSendCount", quicksend_list.Count.ToString(), file_path);
            for (int i = 0; i < quicksend_list.Count; i++)
            {
                WritePrivateProfileString(qs, "QuickSendTitle" + i, quicksend_list[i].ItemName, file_path);
                WritePrivateProfileString(qs, "QuickSendData" + i, quicksend_list[i].Data, file_path);
            }

            MessageBox.Show("配置文件导出成功！", "O-ComTool 提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        TextStyle redStyle = new TextStyle(Brushes.Red, null, FontStyle.Regular);
        TextStyle greenStyle = new TextStyle(Brushes.LightGreen, null, FontStyle.Regular);
        TextStyle yellowStyle = new TextStyle(Brushes.Yellow, null, FontStyle.Regular);
        TextStyle blueStyle = new TextStyle(Brushes.Blue, null, FontStyle.Regular);
        TextStyle magentaStyle = new TextStyle(Brushes.Magenta, null, FontStyle.Regular);
        TextStyle cyanStyle = new TextStyle(Brushes.Cyan, null, FontStyle.Regular);
        TextStyle orangeStyle = new TextStyle(Brushes.Orange, null, FontStyle.Regular);

        private void fctbReceive_VisibleRangeChanged(object sender, EventArgs e)
        {
            if (hight_light_enable == false) return;//为使能，则退出
            try
            {
                var range = fctbReceive.VisibleRange;
                range.ClearStyle(StyleIndex.All);

                //highlight tags
                if (hl_red_regex_str != null)
                    fctbReceive.VisibleRange.SetStyle(redStyle, hl_red_regex_str, RegexOptions.IgnoreCase);
                if (hl_green_regex_str != null)
                    fctbReceive.VisibleRange.SetStyle(greenStyle, hl_green_regex_str, RegexOptions.IgnoreCase);
                if (hl_yellow_regex_str != null)
                    fctbReceive.VisibleRange.SetStyle(yellowStyle, hl_yellow_regex_str, RegexOptions.IgnoreCase);
                if (hl_blue_regex_str != null)
                    fctbReceive.VisibleRange.SetStyle(blueStyle, hl_blue_regex_str, RegexOptions.IgnoreCase);
                if (hl_cyan_regex_str != null)
                    fctbReceive.VisibleRange.SetStyle(cyanStyle, hl_cyan_regex_str, RegexOptions.IgnoreCase);
                if (hl_magenta_regex_str != null)
                    fctbReceive.VisibleRange.SetStyle(magentaStyle, hl_magenta_regex_str, RegexOptions.IgnoreCase);
                if (hl_orange_regex_str != null)
                    fctbReceive.VisibleRange.SetStyle(orangeStyle, hl_orange_regex_str, RegexOptions.IgnoreCase);


            }
            catch
            {
                MessageBox.Show("Highlight Regex Error, Please Check!");
            }

        }

        private void o_ScrollBar1_Scroll(object sender, ScrollEventArgs e)
        {
            fctbReceive.OnScroll(e, e.Type != ScrollEventType.ThumbTrack && e.Type != ScrollEventType.ThumbPosition);
        }

        private void fctbReceive_ScrollbarsUpdated(object sender, EventArgs e)
        {
                AdjustScrollbars();
        }

        private string GetPrivateProfileStringFake(string Section, string key, string path)
        {

            StringBuilder temp = new StringBuilder(1024);
            GetPrivateProfileString(Section, key, "", temp, 1024, path);
            return temp.ToString();
        }

        private void tsmImportConfig_Click(object sender, EventArgs e)
        {
            openFileDialog1.Title = "请选择文件";
            openFileDialog1.Filter = "文本文件|*.ini|所有文件(*.*)|*.*";
            openFileDialog1.FileName = "";
            openFileDialog1.RestoreDirectory = true;
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
            {
                return;
            }
            string file_path = openFileDialog1.FileName;

            try
            {
                // 标量配置项统一读入
                var cvt = new FontConverter();
                foreach (var f in BuildIniFields(cvt))
                {
                    f.Set(GetPrivateProfileStringFake(f.Section, f.Key, file_path));
                }

                // 快捷发送列表
                const string qs = "QuickSend";
                app.Default.QuickSendTitle.Clear();
                app.Default.QuickSendData.Clear();
                app.Default.QuickSendCount = short.Parse(GetPrivateProfileStringFake(qs, "QuickSendCount", file_path));
                for (short i = 0; i < app.Default.QuickSendCount; i++)
                {
                    app.Default.QuickSendTitle.Add(GetPrivateProfileStringFake(qs, "QuickSendTitle" + i, file_path));
                    app.Default.QuickSendData.Add(GetPrivateProfileStringFake(qs, "QuickSendData" + i, file_path));
                }

                LoadLastConfig();
                MessageBox.Show("导入配置文件成功！", "O-ComTool 提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                MessageBox.Show("导入配置文件失败！", "O-ComTool 错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void cmsOption_Click(object sender, EventArgs e)
        {
            tsmOption_Click(sender, e);
        }

        private void tsmClearSend_Click(object sender, EventArgs e)
        {
            txbSend.Clear();
        }

        private void tsmClearRece_Click(object sender, EventArgs e)
        {
            fctbReceive.Clear();
        }

        private void AdjustScrollbars()
        {
            AdjustScrollbar(o_ScrollBar1, fctbReceive.VerticalScroll.Maximum, fctbReceive.VerticalScroll.Value, fctbReceive.ClientSize.Height);
            //AdjustScrollbar(fctbReceive, fctbReceive.VerticalScroll.Maximum, fctbReceive.VerticalScroll.Value, fctbReceive.ClientSize.Height);
        }

        /// <summary>
        /// This method for MyScrollBar
        /// </summary>
        private void AdjustScrollbar(O_ScrollBar scrollBar, int max, int value, int clientSize)
        {
            scrollBar.Maximum = max;
            scrollBar.Visible = max > 0;
            scrollBar.Value = Math.Min(scrollBar.Maximum, value);
        }

        /// <summary>
        /// This method for System.Windows.Forms.ScrollBar and inherited classes
        /// </summary>
        private void AdjustScrollbar(ScrollBar scrollBar, int max, int value, int clientSize)
        {
            scrollBar.LargeChange = clientSize / 3;
            scrollBar.SmallChange = clientSize / 11;
            scrollBar.Maximum = max + scrollBar.LargeChange;
            scrollBar.Visible = max > 0;
            scrollBar.Value = Math.Min(scrollBar.Maximum, value);
        }

        

        

    }
}
