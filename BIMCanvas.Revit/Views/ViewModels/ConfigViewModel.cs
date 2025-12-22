using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Core.Models.Shared;
using BIMCanvas.Revit.Services;

namespace BIMCanvas.Revit.Views.ViewModels
{
    /// <summary>
    /// 房间配置项
    /// </summary>
    public class RoomConfigItem : INotifyPropertyChanged
    {
        private RoomType _selectedType;

        /// <summary>
        /// 房间 ID
        /// </summary>
        public string RoomId { get; set; } = string.Empty;

        /// <summary>
        /// 房间名称
        /// </summary>
        public string RoomName { get; set; } = string.Empty;

        /// <summary>
        /// 推断的房间类型
        /// </summary>
        public RoomType InferredType { get; set; }

        /// <summary>
        /// 推断类型的显示名称
        /// </summary>
        public string InferredTypeDisplay => RoomTypeInferrer.GetDisplayName(InferredType);

        /// <summary>
        /// 用户选择的房间类型
        /// </summary>
        public RoomType SelectedType
        {
            get => _selectedType;
            set
            {
                if (_selectedType != value)
                {
                    _selectedType = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(SelectedTypeDisplay));
                }
            }
        }

        /// <summary>
        /// 选择类型的显示名称
        /// </summary>
        public string SelectedTypeDisplay => RoomTypeInferrer.GetDisplayName(SelectedType);

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 房间类型选项（用于 ComboBox）
    /// </summary>
    public class RoomTypeOption
    {
        public RoomType Type { get; set; }
        public string DisplayName { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// 配置窗口 ViewModel
    /// </summary>
    public class ConfigViewModel : INotifyPropertyChanged
    {
        private Action? _closeAction;

        /// <summary>
        /// 房间配置列表
        /// </summary>
        public ObservableCollection<RoomConfigItem> Rooms { get; }

        /// <summary>
        /// 可用的房间类型选项
        /// </summary>
        public ObservableCollection<RoomTypeOption> AvailableTypes { get; }

        /// <summary>
        /// 确认命令
        /// </summary>
        public ICommand ConfirmCommand { get; }

        /// <summary>
        /// 取消命令
        /// </summary>
        public ICommand CancelCommand { get; }

        /// <summary>
        /// 对话框结果（true = 确认，false = 取消）
        /// </summary>
        public bool DialogResult { get; private set; }

        /// <summary>
        /// 创建配置 ViewModel
        /// </summary>
        /// <param name="rooms">房间列表</param>
        public ConfigViewModel(List<Room> rooms)
        {
            // 初始化房间配置列表
            Rooms = new ObservableCollection<RoomConfigItem>(
                rooms.Select(r =>
                {
                    var inferredType = RoomTypeInferrer.InferFromName(r.Name);
                    return new RoomConfigItem
                    {
                        RoomId = r.Id,
                        RoomName = r.Name ?? "(未命名)",
                        InferredType = inferredType,
                        SelectedType = inferredType
                    };
                })
            );

            // 初始化可用类型列表
            AvailableTypes = new ObservableCollection<RoomTypeOption>(
                RoomTypeInferrer.GetAllTypes().Select(t => new RoomTypeOption
                {
                    Type = t,
                    DisplayName = RoomTypeInferrer.GetDisplayName(t)
                })
            );

            // 初始化命令
            ConfirmCommand = new RelayCommand(OnConfirm);
            CancelCommand = new RelayCommand(OnCancel);
        }

        /// <summary>
        /// 设置窗口关闭回调
        /// </summary>
        public void SetCloseAction(Action closeAction)
        {
            _closeAction = closeAction;
        }

        /// <summary>
        /// 获取用户确认的房间类型映射
        /// </summary>
        /// <returns>房间 ID -> 房间类型 的字典</returns>
        public Dictionary<string, RoomType> GetConfirmedTypes()
        {
            return Rooms.ToDictionary(r => r.RoomId, r => r.SelectedType);
        }

        private void OnConfirm()
        {
            DialogResult = true;
            _closeAction?.Invoke();
        }

        private void OnCancel()
        {
            DialogResult = false;
            _closeAction?.Invoke();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
