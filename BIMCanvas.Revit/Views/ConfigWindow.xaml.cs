using System.Windows;
using BIMCanvas.Revit.Views.ViewModels;

namespace BIMCanvas.Revit.Views
{
    /// <summary>
    /// ConfigWindow.xaml 的交互逻辑
    /// </summary>
    public partial class ConfigWindow : Window
    {
        /// <summary>
        /// 创建配置窗口
        /// </summary>
        public ConfigWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 设置 ViewModel 并配置关闭回调
        /// </summary>
        /// <param name="viewModel">配置 ViewModel</param>
        public void SetViewModel(ConfigViewModel viewModel)
        {
            DataContext = viewModel;
            viewModel.SetCloseAction(() =>
            {
                DialogResult = viewModel.DialogResult;
                Close();
            });
        }
    }
}
