using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using CUE4Parse.Utils;
using FluentAvalonia.UI.Controls;
using FortnitePorting.Application;
using FortnitePorting.Extensions;
using FortnitePorting.Framework;
using FortnitePorting.Models.Information;
using FortnitePorting.Models.Leaderboard;
using FortnitePorting.ViewModels;
using FortnitePorting.Views;
using FortnitePorting.Windows;
using Microsoft.Win32;
using RestSharp;
using Serilog;

namespace FortnitePorting.Services;

public class AppService : IService
{
    public IClassicDesktopStyleApplicationLifetime Lifetime;
    public IStorageProvider StorageProvider => Lifetime.MainWindow!.StorageProvider;
    public IClipboard Clipboard => Lifetime.MainWindow!.Clipboard!;

    private readonly SemaphoreSlim _reloadSemaphore = new(1, 1);
    private TrayIcon? _trayIcon;
    private bool _isShuttingDown;
    public bool IsShuttingDown => _isShuttingDown;

    public DirectoryInfo ApplicationDataFolder => AppSettings.Application.UseAppDataPath && Directory.Exists(AppSettings.Application.AppDataPath)
        ? new DirectoryInfo(AppSettings.Application.AppDataPath) 
        : new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortnitePorting"));
    
    public DirectoryInfo DataFolder => new(Path.Combine(App.ApplicationDataFolder.FullName, ".data"));
    public DirectoryInfo AssetsFolder => new(Path.Combine(App.ApplicationDataFolder.FullName, "Assets"));
    public DirectoryInfo PluginsFolder => new(Path.Combine(App.ApplicationDataFolder.FullName, "Plugins"));
    
    private const string SCHEME_NAME = "fortniteporting";
    private const string STARTUP_VALUE_NAME = "FortnitePorting";
    public const string BACKGROUND_ARGUMENT = "--background";
    
    public void InitializeDesktop(IClassicDesktopStyleApplicationLifetime desktop)
    {
        Lifetime = desktop;
        
        Initialize();
    }

    public void Initialize()
    {
        AppSettings.Load();
        
        Info.CreateLogger();
        Dependencies.Ensure();

        DataFolder.Create();
        AssetsFolder.Create();
        PluginsFolder.Create();

        RegisterUrlScheme();
        UpdateStartupRegistration();

        Lifetime.Startup += OnAppStart;
        Lifetime.Exit += OnAppExit;
        Lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        var menu = new NativeMenu();
        var versionItem = new NativeMenuItem(Globals.VersionString) { IsEnabled = false };
        var openItem = new NativeMenuItem("Open");
        var exitItem = new NativeMenuItem("Exit");

        openItem.Click += (_, _) => ShowMainWindow();
        exitItem.Click += (_, _) => Shutdown();

        menu.Add(versionItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(openItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);

        var iconStream = AssetLoader.Open(new Uri("avares://FortnitePorting/Assets/LogoRebrand.ico"));
        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "Fortnite Porting",
            Menu = menu,
            IsVisible = AppSettings.Application.MinimizeToTray
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();

        TrayIcon.SetIcons(Avalonia.Application.Current!, new TrayIcons { _trayIcon });
    }

    public void UpdateTrayIconVisibility()
    {
        if (_trayIcon is not null)
            _trayIcon.IsVisible = AppSettings.Application.MinimizeToTray;
    }

    public void RequireLogin()
    {
        if (SupaBase.IsLoggedIn)
            return;

        Info.Dialog("Login Required", "Sign in with Discord to use Fortnite Porting's online features.", buttons:
        [
            new DialogButton
            {
                Text = "Sign In",
                Action = () => TaskService.Run(async () => await SupaBase.SignIn())
            },
            new DialogButton { Text = "Not Now" }
        ]);
    }

    public void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var window = Lifetime.MainWindow ?? new AppWindow();
            Lifetime.MainWindow ??= window;

            window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;

            window.Activate();
            window.BringToTop();

            StartContentLoadingIfNeeded();
        });
    }

    public void HideMainWindow(WindowClosingEventArgs e)
    {
        if (_isShuttingDown || !AppSettings.Application.MinimizeToTray)
            return;

        e.Cancel = true;
        Lifetime.MainWindow?.Hide();
    }
    
    
    public async Task ReloadInstallationAsync()
    {
        if (!await _reloadSemaphore.WaitAsync(0)) 
            return;
        
        try
        {
            await TaskService.RunDispatcherAsync(() =>
            {
                // TODO add caching service to handle this information
                LeaderboardExport.ClearCache();
                ImageExtensions.ClearCachedBitmaps();
            
                WindowManager.CloseAllPreviews();

                var resettableTypes = Assembly.GetAssembly(typeof(IResettable))?
                    .GetTypes()
                    .Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(IResettable))) ?? [];

                foreach (var type in resettableTypes)
                {
                    if (AppServices.Services.GetService(type) is IResettable resettable)
                        resettable.Reset();
                }
            });

           await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            // get outta here memory!!
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            await UEParse.LoadCoreSessionAsync();
        }
        finally
        {
            _reloadSemaphore.Release();
        }
    }

    public void HandleUrlScheme(string url)
    {
        var request = new RestRequest(url);
        var path = url.Replace("fortniteporting://", string.Empty).SubstringBefore("?");
        var queryParameters = request.Parameters.GetParameters(ParameterType.QueryString);

        switch (path)
        {
            case "auth/callback":
            {
                var codeParameter = queryParameters.FirstOrDefault(param => param.Name?.Equals("code") ?? false);
                if (codeParameter?.Value is not string code) break;
                
                TaskService.Run(async () => await SupaBase.ExchangeCode(code));
                break;
            }
            case var _ when path.StartsWith("route"):
            {
                var routePath = path.Replace("route/", string.Empty);
                Navigation.OpenRoute(routePath);
                break;
            }
        }
    }

    private void OnAppStart(object? sender, ControlledApplicationLifetimeStartupEventArgs e)
    {
        var isBackgroundLaunch = e.Args.Contains(BACKGROUND_ARGUMENT, StringComparer.OrdinalIgnoreCase);
        if (!isBackgroundLaunch)
            Lifetime.MainWindow = new AppWindow();

        if (AppSettings.Account.UseDiscordRichPresence)
            Discord.Initialize();

        TaskService.Run(AppWM.Initialize);

        if (AppSettings.Installation is { FinishedSetup: true, CurrentProfile: null })
        {
            AppSettings.Installation.Profiles.FirstOrDefault()?.IsSelected = true;
        }
        
        if (AppSettings.Plugin.Blender.AutomaticallySync && Dependencies.FinishedEnsuring)
        {
            TaskService.Run(async () => await AppSettings.Plugin.Blender.SyncInstallations(verbose: false));
        }
        
        if (AppSettings.Installation.FinishedSetup)
        {
            Navigation.App.Open<HomeView>();

            if (!isBackgroundLaunch || AppSettings.Application.LoadContentInBackground)
                StartContentLoadingIfNeeded();
        }
    }

    private void StartContentLoadingIfNeeded()
    {
        if (AppSettings.Installation.FinishedSetup && !UEParse.IsLoading && !UEParse.FinishedLoading)
            TaskService.Run(UEParse.LoadCoreSessionAsync);
    }

    private void OnAppExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (AppSettings.ShouldSaveOnExit)
            AppSettings.Save();
        
        var viewModelTypes = Assembly.GetAssembly(typeof(ViewModelBase))?
            .GetTypes()
            .Where(type => !type.IsAbstract && type.IsAssignableTo(typeof(ViewModelBase))) ?? [];

        foreach (var viewModelType in viewModelTypes)
        {
            if (viewModelType.GetCustomAttribute<TransientAttribute>() is not null)
                continue;

            var viewModel = AppServices.Services.GetService(viewModelType) as ViewModelBase;
            viewModel?.OnApplicationExit();
        }
    }

    private void RegisterUrlScheme()
    {
        try
        {
            var applicationPath = Environment.ProcessPath;

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{SCHEME_NAME}");
            key.SetValue("", $"URL:{SCHEME_NAME} Protocol");
            key.SetValue("URL Protocol", "");
                
            using var commandKey = key.CreateSubKey(@"shell\open\command");
            commandKey.SetValue("", $"\"{applicationPath}\" \"%1\"");
        }
        catch (Exception e)
        {
            Info.Message("URL Scheme", "Failed to register URL scheme, authentication will not work", InfoBarSeverity.Error, closeTime: 5);
        }
    }
    
    public void Launch(string location, bool shellExecute = true)
    {
        Process.Start(new ProcessStartInfo { FileName = location, UseShellExecute = shellExecute });
    }
    
    public void LaunchSelected(string location)
    {
        var argument = "/select, \"" + location +"\"";
        Process.Start("explorer", argument);
    }
    
    public async Task<string?> BrowseFolderDialog(string startLocation = "")
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(startLocation)});
        var folder = folders.ToArray().FirstOrDefault();

        return folder?.Path.AbsolutePath.Replace("%20", " ");
    }

    public async Task<string?> BrowseFileDialog(string suggestedFileName = "", params FilePickerFileType[] fileTypes)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false, FileTypeFilter = fileTypes, SuggestedFileName = suggestedFileName});
        var file = files.ToArray().FirstOrDefault();

        return file?.Path.AbsolutePath.Replace("%20", " ");
    }

    public async Task<string?> SaveFileDialog(string suggestedFileName = "", params FilePickerFileType[] fileTypes)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {FileTypeChoices = fileTypes, SuggestedFileName = suggestedFileName});
        return file?.Path.AbsolutePath.Replace("%20", " ");
    }

    public void RestartWithMessage(string title, string content, Action? onRestart = null, bool mandatory = false)
    {
        Info.Dialog(title, content, canClose: !mandatory, buttons:
        [
            new DialogButton
            {
                Text = "Restart",
                IsPrimary = true,
                Action = () =>
                {
                    onRestart?.Invoke();
                    Restart();
                }
            },
            new DialogButton
            {
                Text = "Cancel"
            }
        ]);
    }
    
    public void Restart()
    {
        Launch(AppDomain.CurrentDomain.FriendlyName, false);
        Shutdown();
    }

    public void Shutdown()
    {
        _isShuttingDown = true;
        if (_trayIcon is not null)
            _trayIcon.IsVisible = false;

        Lifetime.Shutdown();
    }

    public void UpdateStartupRegistration()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (AppSettings.Application is { MinimizeToTray: true, LaunchOnStartup: true })
                key.SetValue(STARTUP_VALUE_NAME, $"\"{Environment.ProcessPath}\" {BACKGROUND_ARGUMENT}");
            else
                key.DeleteValue(STARTUP_VALUE_NAME, false);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to register Fortnite Porting to launch at Windows startup");
        }
    }
}
