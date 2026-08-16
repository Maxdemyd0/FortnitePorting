using System;
using Avalonia.Controls;
using FortnitePorting.Controls.Navigation.Sidebar;
using FortnitePorting.Framework;
using FortnitePorting.Services;
using FortnitePorting.Views;
using AppWindowModel = FortnitePorting.WindowModels.AppWindowModel;

namespace FortnitePorting.Windows;

public partial class AppWindow : WindowBase<AppWindowModel>
{
    public AppWindow() : base(initializeWindowModel: false)
    {
        InitializeComponent();
        DataContext = WindowModel;
        
        Navigation.App.Initialize(Sidebar, ContentFrame);
        
        KeyDownEvent.AddClassHandler<TopLevel>((sender, args) => BlackHole.HandleKey(args.Key), handledEventsToo: true);

        WindowModel.SupaBase.LevelUp += (sender, level) =>
        {
            TaskService.RunDispatcher(async () => await LevelUpOverlay.ShowLevelUp(level));
        };
    }

    private void OnSidebarItemSelected(object? sender, SidebarItemSelectedArgs args)
    {
        if (!AppSettings.Installation.FinishedSetup) return;

        if (!WindowModel.SupaBase.IsLoggedIn && args.Tag is Type type &&
            (type == typeof(ChatView) || type == typeof(LeaderboardView)))
        {
            App.RequireLogin();
            return;
        }
        
        Navigation.App.Open(args.Tag);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        App.HideMainWindow(e);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (!App.IsShuttingDown)
            App.Shutdown();
    }
}
