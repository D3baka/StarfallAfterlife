﻿using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.Threading;
using StarfallAfterlife.Bridge.Launcher;
using StarfallAfterlife.Bridge.Server;
using StarfallAfterlife.Bridge.Server.Characters;
using StarfallAfterlife.Launcher.Controls;
using StarfallAfterlife.Launcher.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace StarfallAfterlife.Launcher.ViewModels
{
    public class CreateServerPageViewModel : ViewModelBase
    {
        public AppViewModel AppVM { get; }

        public SfaLauncher Launcher => AppVM?.Launcher;

        public SfaServer Server { get; protected set; }

        public ObservableCollection<PlayerStatusInfoViewModel> Players { get; } = new();

        public bool ServerStarted
        {
            get => _serverStarted;
            set => SetAndRaise(ref _serverStarted, value);
        }

        public string ServerAddress
        {
            get => Launcher?.ServerAddress;
            set
            {
                if (Launcher is SfaLauncher launcher)
                    SetAndRaise(launcher.ServerAddress, value, v => launcher.ServerAddress = v);
            }
        }

        public ushort ServerPort
        {
            get => Launcher?.ServerPort ?? 0;
            set
            {
                if (Launcher is SfaLauncher launcher)
                    SetAndRaise(launcher.ServerPort, value, v => launcher.ServerPort = v);
            }
        }

        public bool UsePassword
        {
            get => Launcher?.ServerUsePassword ?? false;
            set
            {
                if (Launcher is SfaLauncher launcher)
                    SetAndRaise(launcher.ServerUsePassword, value, v => launcher.ServerUsePassword = v);
            }
        }

        public ObservableCollection<InterfaceInfo> Interfaces { get; } = new();

        private bool _serverStarted;

        private BattlegroundsEditorWindow _battlegroundsEditor;

        public CreateServerPageViewModel(AppViewModel mainWindowViewModel)
        {
            AppVM = mainWindowViewModel;
            UpdateInterfaces();
        }

        public void UpdateInterfaces()
        {
            var result = new List<InterfaceInfo>();

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                   .Where(i => i.OperationalStatus is OperationalStatus.Up);

                foreach (var item in interfaces)
                {
                    if (item is null)
                        continue;

                    var ip = item.GetIPProperties().UnicastAddresses?.FirstOrDefault(
                       x => x.Address.AddressFamily == AddressFamily.InterNetwork);

                    if (ip is null)
                        continue;

                    result.Add(new() { Address = ip.Address, Name = item.Name});
                }
            }
            catch { }

            if (result.FirstOrDefault(i => i.Address.Equals(IPAddress.Loopback)) is InterfaceInfo info)
                info.Name = "Localhost";
            else
                result.Add(new() { Address = IPAddress.Loopback, Name = "Localhost" });

            result.Add(new() { Address = IPAddress.Any, Name = "Any" });

            Interfaces.Clear();

            foreach (var item in result)
                Interfaces.Add(item);
        }

        public void SelectInterface(object info)
        {
            if (info is InterfaceInfo interfaceInfo)
            {
                ServerAddress = interfaceInfo.Address?.ToString();
            }
        }

        public void ShowPasswordDialog(object info)
        {
            if (Launcher is SfaLauncher launcher)
            {
                var dialog = new EnterPasswordDialog();

                dialog.ShowDialog().ContinueWith(t => Dispatcher.UIThread.Invoke(() =>
                {
                    if (dialog.IsDone == true &&
                        dialog.Text is string password &&
                        string.IsNullOrWhiteSpace(password) == false)
                    {
                        launcher.ServerPassword = SfaServer.CreatePasswordHash(password);
                    }
                }));
            }
        }


        public void StartServer()
        {
            if (Launcher is SfaLauncher launcher)
            {
                Players.Clear();

                var server = Server = launcher.StartServer();

                if (server is not null)
                {
                    server.PlayerStatusUpdated += PlayerStatusUpdated;
                    ServerStarted = true;

                    server.Task.ContinueWith(t => Dispatcher.UIThread.Invoke(() =>
                    {
                        ServerStarted = false;
                    }));
                }
            }
        }

        private void PlayerStatusUpdated(object sender, PlayerStatusInfoEventArgs e)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                var newInfo = e.Info;
                var vm = Players.FirstOrDefault(p => p.Info.Auth == newInfo.Auth);

                if (vm is null)
                    Players.Add(new(newInfo));
                else
                    vm.Info = newInfo;
            });
        }

        public void StopServer()
        {
            var server = Server;

            if (server is not null)
                server.PlayerStatusUpdated -= PlayerStatusUpdated;

            Launcher?.StopServer();
            Players.Clear();
        }

        public Task CreateNewRealm()
        {
            return AppVM.ShowCreateNewRealm().ContinueWith(t => Dispatcher.UIThread.Invoke(() =>
            {
                if (AppVM is AppViewModel app &&
                    t.Result is RealmInfoViewModel realm)
                    app.SelectedServerRealm = realm;
            }));
        }

        public Task<bool> DeleteSelectedRealm() =>
            AppVM?.ShowDeleteRealm(AppVM?.SelectedServerRealm) ?? Task.FromResult(false);

        public void ShowBattlegroundsEditor()
        {
            if (Server is null)
                return;

            if (_battlegroundsEditor is not null &&
                _battlegroundsEditor.IsVisible == true)
            {
                if (_battlegroundsEditor.WindowState == WindowState.Minimized)
                    _battlegroundsEditor.WindowState = WindowState.Normal;

                _battlegroundsEditor.Activate();
                return;
            }

            _battlegroundsEditor = new BattlegroundsEditorWindow()
            {
                DataContext = new BGEditorViewModel(this),
            };

            _battlegroundsEditor.Show(App.MainWindow);
        }

        public PlayerStatusInfoViewModel GetCharacterVM(ServerCharacter character) =>
            character is null ? null : GetCharacterVM(character.UniqueId);

        public PlayerStatusInfoViewModel GetCharacterVM(int id)
        {
            return Players.FirstOrDefault(p => p.CharacterId == id);
        }
    }
}
