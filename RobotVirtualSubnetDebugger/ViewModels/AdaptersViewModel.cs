using System.Collections.ObjectModel;
using RobotNet.Windows.Wpf.Commands;
using RobotNet.Windows.Wpf.Models;
using RobotNet.Windows.Wpf.Services.Platform;
using RobotNet.Windows.Wpf.Utils;

namespace RobotNet.Windows.Wpf.ViewModels;

public sealed class AdaptersViewModel : ObservableObject
{
    private readonly INetworkAdapterService _networkAdapterService;
    private string _lastRefreshMessage = string.Empty;

    public AdaptersViewModel(INetworkAdapterService networkAdapterService)
    {
        _networkAdapterService = networkAdapterService;
        RefreshCommand = new RelayCommand(RefreshAdapters);
        RefreshAdapters();
    }

    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public int AdapterCount => Adapters.Count;

    public string LastRefreshMessage
    {
        get => _lastRefreshMessage;
        private set => SetProperty(ref _lastRefreshMessage, value);
    }

    private void RefreshAdapters()
    {
        Adapters.Clear();

        foreach (var adapter in _networkAdapterService.GetAdapters())
        {
            Adapters.Add(adapter);
        }

        LastRefreshMessage = $"{DateTime.Now:HH:mm:ss} 已刷新 {Adapters.Count} 个网卡";
        OnPropertyChanged(nameof(AdapterCount));
    }
}
