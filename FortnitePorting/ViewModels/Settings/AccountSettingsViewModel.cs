using CommunityToolkit.Mvvm.ComponentModel;
using FortnitePorting.Application;
using FortnitePorting.Framework;
using FortnitePorting.Models.Supabase.User;
using FortnitePorting.Services;
using Newtonsoft.Json;

namespace FortnitePorting.ViewModels.Settings;

public partial class AccountSettingsViewModel : SettingsViewModelBase
{
   [JsonIgnore] public SupabaseService SupaBase => AppServices.SupaBase;

   [ObservableProperty] private string? _sessionInfoEncrypted = null;

   [ObservableProperty] private bool _stayLoggedIn = true;

   [ObservableProperty] private bool _useDiscordRichPresence = true;

   partial void OnUseDiscordRichPresenceChanged(bool value)
   {
      if (value)
         Discord.Initialize();
      else
         Discord.Deinitialize();
   }

   partial void OnStayLoggedInChanged(bool value)
   {
      if (value)
         SupaBase.UpdatePersistedSession();
      else
         SessionInfoEncrypted = null;
   }
}
