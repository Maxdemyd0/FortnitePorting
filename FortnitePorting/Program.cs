using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using FortnitePorting.Application;
using FortnitePorting.Services;
using Serilog;

namespace FortnitePorting;

internal static class Program
{
    private static Mutex _programMutex = null!;
    private static bool _ownsProgramMutex;
    
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject.ToString() is not { } exceptionString)
                return;
            
            Log.Fatal(exceptionString);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Fatal(e.Exception.ToString());
            e.SetObserved();
        };
        
        try
        {
            _programMutex = new Mutex(true, "FortnitePortingMutex", out var isNew);

            if (isNew)
            {
                _ownsProgramMutex = true;
                StartApp(args);
            }
            else
            {
                OpenExistingApp(args);
            }
            
        }
        catch (Exception e)
        {
            Debugger.Break();
            Log.Fatal(e.ToString());
        }
        finally
        {
            Log.CloseAndFlush();
            if (_ownsProgramMutex)
                _programMutex.ReleaseMutex();
            _programMutex?.Dispose();
        }
    }

    private static void StartApp(string[] args)
    {
        TaskService.Run(() =>
        {
            using var pipe = new NamedPipeServerStream("FortnitePorting");

            while (true)
            {
                try
                {
                    pipe.WaitForConnection();
                    using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);

                    var argument = reader.ReadString();
                    if (string.IsNullOrEmpty(argument))
                        App.ShowMainWindow();
                    else if (argument.Equals(AppService.BACKGROUND_ARGUMENT, StringComparison.OrdinalIgnoreCase))
                    {
                        // A scheduled background launch should not pop open an already-running instance.
                    }
                    else
                        App.HandleUrlScheme(argument);
                }
                catch (Exception e)
                {
                    Log.Warning(e, "Failed to process a request from a secondary Fortnite Porting instance");
                }
                finally
                {
                    if (pipe.IsConnected)
                        pipe.Disconnect();
                }
            }
        });
        
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    
    private static void OpenExistingApp(string[] args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream("FortnitePorting");
            pipe.Connect(1000);

            var writer = new BinaryWriter(pipe);
            writer.Write(args.FirstOrDefault() ?? string.Empty);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Unable to contact the existing Fortnite Porting instance");
        }
    }

    private static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<FortnitePortingApp>()
            .UsePlatformDetect()
            .LogToTrace()
            .With(new Win32PlatformOptions { CompositionMode = [Win32CompositionMode.WinUIComposition] });
}
