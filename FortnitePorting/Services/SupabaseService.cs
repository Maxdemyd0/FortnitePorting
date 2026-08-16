using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using FortnitePorting.Application;
using FortnitePorting.Extensions;
using FortnitePorting.Models.API.Responses;
using FortnitePorting.Models.Information;
using FortnitePorting.Models.Levels;
using FortnitePorting.Models.Supabase.Tables;
using FortnitePorting.Models.Supabase.User;
using FortnitePorting.Views.Settings;
using Mapster;
using Serilog;
using Supabase;
using Supabase.Gotrue;
using Supabase.Gotrue.Exceptions;
using Supabase.Realtime.PostgresChanges;
using Supabase.Realtime.Interfaces;
using Client = Supabase.Client;

namespace FortnitePorting.Services;

public partial class SupabaseService : ObservableObject, IService
{
    [ObservableProperty] private APIService _api;

    public SupabaseService(APIService api)
    {
        Api = api;
        
        TaskService.Run(async () =>
        {
            var auth = await Api.FortnitePorting.AuthInfo();
            if (auth is null)
            {
                Info.Message("Online Services", "Failed to retrieve authentication information.", InfoBarSeverity.Error);
                return;
            }

            Client = new Client(auth.SupabaseURL, auth.SupabaseAnonKey, DefaultOptions);
            
            Client.Auth.AddStateChangedListener(async (client, state) =>
            {
                if (client.CurrentSession is { } session)
                {
                    var sessionInfo = new UserSessionInfo(
                        session.AccessToken!, 
                        session.RefreshToken!
                    );

                    if (AppSettings.Account.StayLoggedIn)
                        AppSettings.Account.SessionInfoEncrypted = sessionInfo.ToEncryptedString();
                }

                if (state == Constants.AuthState.SignedIn && !IsLoggedIn)
                {
                    await OnLoggedIn();
                }
            });
            
            await Client.InitializeAsync();

            if (AppSettings.Account.StayLoggedIn && AppSettings.Account.SessionInfoEncrypted is { } encryptedSession)
            {
                var sessionInfo = UserSessionInfo.FromEncryptedString(encryptedSession);
                if (sessionInfo is not null)
                    await SetSession(sessionInfo);
            }
        });
    }
    
    [ObservableProperty] private Client _client;
    [ObservableProperty] private bool _isLoggedIn;
    
    [ObservableProperty] private UserInfoResponse? _userInfo;
    [ObservableProperty] private LevelStats? _levelStats;
    [ObservableProperty] private LevelStats? _prevLevelStats;
    [ObservableProperty] private UserPermissions _permissions = new();

    public event EventHandler<int> LevelUp; 
    
    private ProviderAuthState? _currentAuthState;
    private CancellationTokenSource? _signInCancellation;
    private TaskCompletionSource<bool>? _signInCompletion;
    private const int SignInTimeoutSeconds = 120;
    private bool _postedLogin;
    private readonly ConcurrentDictionary<string, UserInfoResponse> _userInfoCache = [];
    private IRealtimeChannel? _permissionsChannel;
    private IRealtimeChannel? _levelsChannel;
    
    private static readonly SupabaseOptions DefaultOptions = new()
    {
        AutoRefreshToken = true,
        AutoConnectRealtime = true,
    };

    public async Task SetSession(UserSessionInfo sessionInfo)
    {
        try
        {
            if (await Client.Auth.SetSession(sessionInfo.AccessToken, sessionInfo.RefreshToken) is { } session)
            {
                var refreshedSessionInfo = new UserSessionInfo(
                    session.AccessToken!, 
                    session.RefreshToken!
                );

                if (AppSettings.Account.StayLoggedIn)
                    AppSettings.Account.SessionInfoEncrypted = refreshedSessionInfo.ToEncryptedString();
            }
        }
        catch (GotrueException e) when (e.Message.Contains("refresh_token_already_used"))
        {
            AppSettings.Account.SessionInfoEncrypted = null;
            IsLoggedIn = false;
            Info.Dialog("Session Expired", "Your session has expired. Please log in again.");
            Log.Warning("Refresh token already used, session cleared");
        }
        catch (Exception e)
        {
            AppSettings.Account.SessionInfoEncrypted = null;
            IsLoggedIn = false;
            Info.Dialog("Online Services", "The online session is invalid, please log in again.");
            Log.Error(e.ToString());
        }
    }

    public async Task<bool> SignIn()
    {
        if (IsLoggedIn)
            return true;
        if (IsSigningIn)
            return false;

        IsSigningIn = true;
        using var cancellation = new CancellationTokenSource();
        _signInCancellation = cancellation;
        _signInCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _currentAuthState = await Client.Auth.SignIn(Constants.Provider.Discord, new SignInOptions
            {
                FlowType = Constants.OAuthFlowType.PKCE,
                RedirectTo = "https://api.fortniteporting.app/v2/auth/redirect",
            });

            App.Launch(_currentAuthState.Uri.AbsoluteUri);
            Info.Dialog("Sign in with Discord", "Finish signing in in your browser. This window will stop waiting after two minutes.",
                buttons:
                [
                    new DialogButton { Text = "Close", Action = CancelSignIn }
                ], canClose: false, id: "discord-oauth");

            var completed = await Task.WhenAny(
                _signInCompletion.Task,
                Task.Delay(TimeSpan.FromSeconds(SignInTimeoutSeconds), cancellation.Token));

            if (completed == _signInCompletion.Task)
                return await _signInCompletion.Task;

            if (!cancellation.IsCancellationRequested)
                Info.Message("Sign In Timed Out", "Discord sign in took too long. You can try again whenever you're ready.", InfoBarSeverity.Warning);

            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to start Discord sign in");
            Info.Message("Sign In", "Unable to start Discord sign in. Please try again.", InfoBarSeverity.Error);
            return false;
        }
        finally
        {
            if (ReferenceEquals(_signInCancellation, cancellation))
            {
                _signInCancellation = null;
                _signInCompletion = null;
            }

            IsSigningIn = false;
        }
    }

    [ObservableProperty] private bool _isSigningIn;

    public void CancelSignIn()
    {
        _currentAuthState = null;
        _signInCompletion?.TrySetResult(false);
        _signInCancellation?.Cancel();
    }

    public void UpdatePersistedSession()
    {
        if (!AppSettings.Account.StayLoggedIn || Client?.Auth.CurrentSession is not { } session)
            return;

        AppSettings.Account.SessionInfoEncrypted = new UserSessionInfo(
            session.AccessToken!,
            session.RefreshToken!
        ).ToEncryptedString();
    }
    
    public async Task SignOut()
    {
        // TODO on sign out event to decouple ui responsiblities
        if (Navigation.Settings.IsTabOpen<AccountSettingsView>())
            Navigation.Settings.Open<ApplicationSettingsView>();

        AppSettings.Account.SessionInfoEncrypted = null;
        UserInfo = null;
        IsLoggedIn = false;
        await Chat.Uninitialize();
        await UninitializeOnlineSubscriptions();
        _userInfoCache.Clear();
        LevelStats = null;
        PrevLevelStats = null;
        Permissions = new UserPermissions();
        _postedLogin = false;
        
        await Client.Auth.SignOut();
    }

    public async Task ExchangeCode(string code)
    {
        if (_currentAuthState?.PKCEVerifier is not { } verifier || !IsSigningIn)
        {
            Log.Information("Ignoring an expired Discord sign-in callback");
            return;
        }

        var session = await Client.Auth.ExchangeCodeForSession(verifier, code);
        if (session is null)
        {
            Info.Message("Discord Integration", "Failed to sign in with discord.", severity: InfoBarSeverity.Error);
        }
    }

    public async Task PostExports(IEnumerable<string> objectPaths)
    {
        await Api.FortnitePorting.PostExports(objectPaths);
    }

    private async Task OnLoggedIn()
    {
        IsLoggedIn = true;
        try
        {
            Permissions = (await Client.Rpc<Permissions>("permissions", new { })).Adapt<UserPermissions>();

            _permissionsChannel = await Client.From<Permissions>().On(PostgresChangesOptions.ListenType.All, (channel, response) =>
            {
                Permissions = response.Model<Permissions>().Adapt<UserPermissions>();
            });

            LevelStats = await Client.CallObjectFunction<LevelStats>("get_level_stats");
            
            _levelsChannel = await Client.From<Levels>().On(PostgresChangesOptions.ListenType.All, async (channel, response) =>
            {
                PrevLevelStats = LevelStats;
                LevelStats = await Client.CallObjectFunction<LevelStats>("get_level_stats");

                if (PrevLevelStats?.Level != LevelStats?.Level)
                    LevelUp?.Invoke(this, LevelStats?.Level ?? 0);
            });

            await LoadUserInfo();

            await PostLogin();

            await Chat.Initialize();
            await TaskService.RunDispatcherAsync(() => Info.CloseDialog("discord-oauth"));
            _signInCompletion?.TrySetResult(true);
        }
        catch (Exception e)
        {
            IsLoggedIn = false;
            await Chat.Uninitialize();
            await UninitializeOnlineSubscriptions();
            _signInCompletion?.TrySetResult(false);
            Info.Message("Online Services", "Unable to connect to chat. Please sign in again.", InfoBarSeverity.Error);
            Log.Error(e, "Failed to initialize online services");
        }
    }

    private async Task UninitializeOnlineSubscriptions()
    {
        if (_permissionsChannel is not null)
        {
            _permissionsChannel.Unsubscribe();
            _permissionsChannel = null;
        }

        if (_levelsChannel is not null)
        {
            _levelsChannel.Unsubscribe();
            _levelsChannel = null;
        }
    }
    
    private async Task PostLogin()
    {
        if (_postedLogin) return;

        await Api.FortnitePorting.PostLogin();
        _postedLogin = true;
    }

    private async Task LoadUserInfo()
    {
        UserInfo = await GetUserAsync(Client.Auth.CurrentUser!.Id!);
    }

    public async Task<UserInfoResponse?> GetUserAsync(string? id)
    {
        if (id is null) return null;
        if (_userInfoCache.TryGetValue(id, out var cached)) return cached;

        var userInfo = await Api.FortnitePorting.UserInfo(id);
        if (userInfo is not null)
            _userInfoCache[id] = userInfo;

        return userInfo;
    }
}
