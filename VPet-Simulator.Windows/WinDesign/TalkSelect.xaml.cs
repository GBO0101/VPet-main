using LinePutScript;
using LinePutScript.Localization.WPF;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows
{
    /// <summary>
    /// TalkSelect.xaml 的交互逻辑
    /// </summary>
    public partial class TalkSelect : UserControl
    {// 使用新的选项方式的聊天框

        /// <summary>
        /// 当前存在在列表的选项
        /// </summary>
        List<SelectText> textList = new List<SelectText>();
        /// <summary>
        /// 已经说过的话
        /// </summary>
        HashSet<string> textSaid = new HashSet<string>();
        /// <summary>
        /// 下次刷新时间
        /// </summary>
        public DateTime RelsTime;
        private DateTime lastAddTime;

        MainWindow mw;
        public TalkSelect(MainWindow mw)
        {
            InitializeComponent();
            tbTalk.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(tbTalk_TextChanged));
            this.mw = mw;
            mw.Main.ToolBar.EventShow += RelsSelect;
            RelsSelect();
        }


        /// <summary>
        /// 刷新当前所有选项
        /// </summary>
        public void RelsSelect()
        {
            if (RelsTime < DateTime.Now)
            {
                //刷新选项
                RelsTime = DateTime.Now.AddMinutes(10);//10分钟刷新一次, 每次聊天增加5分钟
                lastAddTime = DateTime.Now;
                textList.Clear();
                textSaid.Clear();
                //随机选取选项
                var list = mw.SelectTexts.ToList();
                while (list.Count > 0 && textList.Count < 5)
                {
                    int sid = Function.Rnd.Next(list.Count);
                    var select = list[sid];
                    list.RemoveAt(sid);
                    if (textList.Find(x => x.Choose == select.Choose) == null && select.CheckState(mw.Main))
                    {
                        textList.Add(select);
                    }
                }
            }
            //刷新显示
            if (textList.Count > 0)
            {
                tbTalk.Items.Clear();
                foreach (var item in textList)
                {
                    if (!textSaid.Contains(item.Choose))
                    {
                        tbTalk.Items.Add(item.TranslateChoose);
                    }
                }
                btn_Send.IsEnabled = true;
            }
            else
            {
                tbTalk.Items.Clear();
                tbTalk.Items.Add("没有可以说的话".Translate());
                btn_Send.IsEnabled = false;
            }
            double min = (RelsTime - DateTime.Now).TotalMinutes;
            double interval = (RelsTime - lastAddTime).TotalMinutes;
            double progress = 1 - min / interval;
            progress = Math.Min(1, Math.Max(0, progress));
            PrograssUsed.Value = progress;
            PrograssUsed.ToolTip = "下次刷新剩余时间: {0:f1}分钟".Translate(min);
            UpdateSendButtonState();
        }

        private void btn_Send_Click(object sender, RoutedEventArgs e)
        {
            var input = tbTalk.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(input) || input == "没有可以说的话".Translate())
            {
                return;
            }

            if (!IsSelectedPreset(input))
            {
                SendToAiAgent(input);
                return;
            }

            mw.Main.ToolBar.Visibility = Visibility.Collapsed;
            var say = textList[tbTalk.SelectedIndex];
            textList.RemoveAt(tbTalk.SelectedIndex);

            //添加日志
            mw.ActivityLogs.Add(new ActivityLog("hostsay",say.TranslateChoose));

            //聊天效果
            if (say.Exp != 0)
            {
                if (say.Exp > 0)
                {
                    mw.GameSavesData.Statistics[(gint)"stat_say_exp_p"]++;
                }
                else
                    mw.GameSavesData.Statistics[(gint)"stat_say_exp_d"]++;
            }
            if (say.Likability != 0)
            {
                if (say.Likability > 0)
                    mw.GameSavesData.Statistics[(gint)"stat_say_like_p"]++;
                else
                    mw.GameSavesData.Statistics[(gint)"stat_say_like_d"]++;
            }
            if (say.Money != 0)
            {
                if (say.Money > 0)
                    mw.GameSavesData.Statistics[(gint)"stat_say_money_p"]++;
                else
                    mw.GameSavesData.Statistics[(gint)"stat_say_money_d"]++;
            }
            mw.Main.Core.Save.EatFood(say);
            mw.Main.Core.Save.Money += say.Money;

            
            textSaid.Add(say.Choose);
            RelsTime = RelsTime.AddMinutes(5);
            lastAddTime = DateTime.Now;

            mw.Main.SayRnd(say.TranslateTextConvert(mw.Main), desc: say.FoodToDescription());
            if (say.ToTags.Count > 0)
            {
                var list = mw.SelectTexts.FindAll(x => x.ContainsTag(say.ToTags)).ToList();
                while (list.Count > 0)
                {
                    int sid = Function.Rnd.Next(list.Count);
                    var select = list[sid];
                    list.RemoveAt(sid);
                    if (textList.Find(x => x.Choose == select.Choose) == null && !textSaid.Contains(select.Choose) && select.CheckState(mw.Main))
                    {
                        textList.Add(select);
                        break;
                    }
                }
            }
            RelsSelect();
        }

        private void tbTalk_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return)
                return;

            e.Handled = true;
            btn_Send_Click(btn_Send, new RoutedEventArgs(Button.ClickEvent, btn_Send));
        }

        private void tbTalk_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSendButtonState();
        }

        private void UpdateSendButtonState()
        {
            var input = tbTalk.Text?.Trim() ?? "";
            btn_Send.IsEnabled = textList.Count > 0
                || (!string.IsNullOrWhiteSpace(input) && input != "没有可以说的话".Translate());
        }

        private bool IsSelectedPreset(string input)
        {
            return tbTalk.SelectedIndex >= 0
                && tbTalk.SelectedIndex < textList.Count
                && tbTalk.Items.Count > tbTalk.SelectedIndex
                && string.Equals(input, tbTalk.Items[tbTalk.SelectedIndex]?.ToString(), StringComparison.Ordinal);
        }

        private void SendToAiAgent(string input)
        {
            var aiTalk = mw.TalkAPI.Find(x => x.APIName == "AI Agent") as TalkBox;
            if (aiTalk == null)
            {
                mw.Main.SayRnd("AI Agent \u9084\u6c92\u6e96\u5099\u597d\u55b5\u3002", true, "AI Agent");
                return;
            }

            mw.Main.ToolBar.Visibility = Visibility.Collapsed;
            tbTalk.Text = "";
            mw.ActivityLogs.Add(new ActivityLog("hostsay", input));
            Task.Run(() => aiTalk.Responded(input));
        }
    }
}
