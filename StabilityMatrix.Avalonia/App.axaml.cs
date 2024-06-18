using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AsyncAwaitBestPractices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Interop;
using FluentAvalonia.UI.Controls;
using MessagePipe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using Octokit;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Extensions.Http;
using Polly.Timeout;
using Refit;
using Sentry;
using StabilityMatrix.Avalonia.Helpers;
using StabilityMatrix.Avalonia.Languages;
using StabilityMatrix.Avalonia.Services;
using StabilityMatrix.Avalonia.ViewModels;
using StabilityMatrix.Avalonia.ViewModels.Base;
using StabilityMatrix.Avalonia.ViewModels.Progress;
using StabilityMatrix.Avalonia.Views;
using StabilityMatrix.Core.Api;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Converters.Json;
using StabilityMatrix.Core.Database;
using StabilityMatrix.Core.Extensions;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Models.Api;
using StabilityMatrix.Core.Models.Configs;
using StabilityMatrix.Core.Models.FileInterfaces;
using StabilityMatrix.Core.Models.Settings;
using StabilityMatrix.Core.Services;
using Application = Avalonia.Application;
using Logger = NLog.Logger;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
#if DEBUG
using StabilityMatrix.Avalonia.Diagnostics.LogViewer;
using StabilityMatrix.Avalonia.Diagnostics.LogViewer.Extensions;
#endif

namespace StabilityMatrix.Avalonia;

public sealed class App : Application
{
    private static readonly Lazy<Logger> LoggerLazy = new(LogManager.GetCurrentClassLogger);
    private static Logger Logger => LoggerLazy.Value;

    private readonly SemaphoreSlim onExitSemaphore = new(1, 1);

    private bool isAsyncDisposeComplete;

    private bool isOnExitComplete;

    [NotNull]
    public static IServiceProvider? Services { get; private set; }

    [NotNull]
    public static Visual? VisualRoot { get; internal set; }

    public static TopLevel TopLevel => TopLevel.GetTopLevel(VisualRoot)!;

    internal static bool IsHeadlessMode =>
        TopLevel.TryGetPlatformHandle()?.HandleDescriptor is null or "STUB";

    [NotNull]
    public static IStorageProvider? StorageProvider { get; internal set; }

    [NotNull]
    public static IClipboard? Clipboard { get; internal set; }

    // ReSharper disable once MemberCanBePrivate.Global
    [NotNull]
    public static IConfiguration? Config { get; private set; }

    // ReSharper disable once MemberCanBePrivate.Global
    public IClassicDesktopStyleApplicationLifetime? DesktopLifetime =>
        ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    public static new App? Current => (App?)Application.Current;

    /// <summary>
    /// Called before <see cref="Services"/> is built.
    /// Can be used by UI tests to override services.
    /// </summary>
    internal static event EventHandler<IServiceCollection>? BeforeBuildServiceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        SetFontFamily(GetPlatformDefaultFontFamily());

        // Set design theme
        if (Design.IsDesignMode)
        {
            RequestedThemeVariant = ThemeVariant.Dark;
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Remove DataAnnotations validation plugin since we're using INotifyDataErrorInfo from MvvmToolkit
        var dataValidationPluginsToRemove = BindingPlugins
            .DataValidators.OfType<DataAnnotationsValidationPlugin>()
            .ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }

        base.OnFrameworkInitializationCompleted();

        if (Design.IsDesignMode)
        {
            DesignData.DesignData.Initialize();
            Services = DesignData.DesignData.Services;
        }
        else
        {
            ConfigureServiceProvider();
        }

        if (DesktopLifetime is not null)
        {
            DesktopLifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Setup();

            // First time setup if needed
            var settingsManager = Services.GetRequiredService<ISettingsManager>();
            if (!settingsManager.IsEulaAccepted())
            {
                var setupWindow = Services.GetRequiredService<FirstLaunchSetupWindow>();
                var setupViewModel = Services.GetRequiredService<FirstLaunchSetupViewModel>();
                setupWindow.DataContext = setupViewModel;
                setupWindow.ShowAsDialog = true;
                setupWindow.ShowActivated = true;
                setupWindow.ShowAsyncCts = new CancellationTokenSource();

                setupWindow.ExtendClientAreaChromeHints = Program.Args.NoWindowChromeEffects
                    ? ExtendClientAreaChromeHints.NoChrome
                    : ExtendClientAreaChromeHints.PreferSystemChrome;

                DesktopLifetime.MainWindow = setupWindow;

                setupWindow.ShowAsyncCts.Token.Register(() =>
                {
                    if (setupWindow.Result == ContentDialogResult.Primary)
                    {
                        settingsManager.SetEulaAccepted();
                        ShowMainWindow();
                        DesktopLifetime.MainWindow.Show();
                    }
                    else
                    {
                        Shutdown();
                    }
                });
            }
            else
            {
                ShowMainWindow();
            }
        }
    }

    /// <summary>
    /// Set the default font family for the application.
    /// </summary>
    private void SetFontFamily(FontFamily fontFamily)
    {
        Resources["ContentControlThemeFontFamily"] = fontFamily;
    }

    /// <summary>
    /// Get the default font family for the current platform and language.
    /// </summary>
    public FontFamily GetPlatformDefaultFontFamily()
    {
        try
        {
            var fonts = new List<string>();

            if (Cultures.Current?.Name == "ja-JP")
            {
                return Resources["NotoSansJP"] as FontFamily
                    ?? throw new ApplicationException("Font NotoSansJP not found");
            }

            if (Compat.IsWindows)
            {
                fonts.Add(OSVersionHelper.IsWindows11() ? "Segoe UI Variable Text" : "Segoe UI");
            }
            else if (Compat.IsMacOS)
            {
                // Use Segoe fonts if installed, but we can't distribute them
                fonts.Add("Segoe UI Variable");
                fonts.Add("Segoe UI");

                fonts.Add("San Francisco");
                fonts.Add("Helvetica Neue");
                fonts.Add("Helvetica");
            }
            else
            {
                return FontFamily.Default;
            }

            return new FontFamily(string.Join(",", fonts));
        }
        catch (Exception e)
        {
            Logger.Error(e);

            return FontFamily.Default;
        }
    }

    /// <summary>
    /// Setup tasks to be run shortly before any window is shown
    /// </summary>
    private void Setup()
    {
        using var _ = CodeTimer.StartNew();

        // Setup uri handler for `stabilitymatrix://` protocol
        Program.UriHandler.RegisterUriScheme();
    }

    private void ShowMainWindow()
    {
        if (DesktopLifetime is null)
            return;

        var mainViewModel = Services.GetRequiredService<MainWindowViewModel>();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.DataContext = mainViewModel;

        mainWindow.ExtendClientAreaChromeHints = Program.Args.NoWindowChromeEffects
            ? ExtendClientAreaChromeHints.NoChrome
            : ExtendClientAreaChromeHints.PreferSystemChrome;

        var settingsManager = Services.GetRequiredService<ISettingsManager>();
        var windowSettings = settingsManager.Settings.WindowSettings;
        if (windowSettings != null && !Program.Args.ResetWindowPosition)
        {
            mainWindow.Position = new PixelPoint(windowSettings.X, windowSettings.Y);
            mainWindow.Width = windowSettings.Width;
            mainWindow.Height = windowSettings.Height;
            mainWindow.WindowState = windowSettings.IsMaximized ? WindowState.Maximized : WindowState.Normal;
        }
        else
        {
            mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        VisualRoot = mainWindow;
        StorageProvider = mainWindow.StorageProvider;
        Clipboard = mainWindow.Clipboard ?? throw new NullReferenceException("Clipboard is null");

        DesktopLifetime.MainWindow = mainWindow;
        DesktopLifetime.Exit += OnApplicationLifetimeExit;
        DesktopLifetime.ShutdownRequested += OnShutdownRequested;

        AppDomain.CurrentDomain.ProcessExit += OnExit;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // Since we're manually shutting down NLog in OnExit
        LogManager.AutoShutdown = false;
    }

    private static void ConfigureServiceProvider()
    {
        var services = ConfigureServices();

        BeforeBuildServiceProvider?.Invoke(null, services);

        Services = services.BuildServiceProvider();

        var settingsManager = Services.GetRequiredService<ISettingsManager>();

        if (Program.Args.DataDirectoryOverride is not null)
        {
            var normalizedDataDirPath = Path.GetFullPath(Program.Args.DataDirectoryOverride);

            if (Compat.IsWindows)
            {
                // ReSharper disable twice LocalizableElement
                normalizedDataDirPath = normalizedDataDirPath.Replace("\\\\", "\\");
            }

            settingsManager.SetLibraryDirOverride(normalizedDataDirPath);
        }

        if (settingsManager.TryFindLibrary())
        {
            Cultures.SetSupportedCultureOrDefault(
                settingsManager.Settings.Language,
                settingsManager.Settings.NumberFormatMode
            );
        }
        else
        {
            Cultures.TrySetSupportedCulture(Settings.GetDefaultCulture());
        }

        Services.GetRequiredService<ProgressManagerViewModel>().StartEventListener();
    }

    internal static void ConfigurePageViewModels(IServiceCollection services)
    {
        services.AddSingleton<MainWindowViewModel>(
            provider =>
                new MainWindowViewModel(
                    provider.GetRequiredService<ISettingsManager>(),
                    provider.GetRequiredService<IDiscordRichPresenceService>(),
                    provider.GetRequiredService<ServiceManager<ViewModelBase>>(),
                    provider.GetRequiredService<ITrackedDownloadService>(),
                    provider.GetRequiredService<IModelIndexService>(),
                    provider.GetRequiredService<Lazy<IModelDownloadLinkHandler>>(),
                    provider.GetRequiredService<INotificationService>()
                )
                {
                    Pages =
                    {
                        provider.GetRequiredService<PackageManagerViewModel>(),
                        provider.GetRequiredService<InferenceViewModel>(),
                        provider.GetRequiredService<CheckpointsPageViewModel>(),
                        provider.GetRequiredService<CheckpointBrowserViewModel>(),
                        provider.GetRequiredService<OutputsPageViewModel>(),
                        provider.GetRequiredService<WorkflowsPageViewModel>()
                    },
                    FooterPages = { provider.GetRequiredService<SettingsViewModel>() }
                }
        );
    }

    internal static void ConfigureDialogViewModels(IServiceCollection services, Type[] exportedTypes)
    {
        // Dialog factory
        services.AddSingleton<ServiceManager<ViewModelBase>>(provider =>
        {
            var serviceManager = new ServiceManager<ViewModelBase>();

            var serviceManagedTypes = exportedTypes
                .Select(
                    t => new { t, attributes = t.GetCustomAttributes(typeof(ManagedServiceAttribute), true) }
                )
                .Where(t1 => t1.attributes is { Length: > 0 })
                .Select(t1 => t1.t);

            foreach (var type in serviceManagedTypes)
            {
                if (!type.IsAssignableTo(typeof(ViewModelBase)))
                {
                    throw new InvalidOperationException(
                        $"Type {type.Name} with [ManagedService] attribute is not assignable to {nameof(ViewModelBase)}"
                    );
                }

                serviceManager.Register(type, () => (ViewModelBase)provider.GetRequiredService(type));
            }

            return serviceManager;
        });
    }

    internal static IServiceCollection ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddLazyInstance();

        services.AddMessagePipe();
        services.AddMessagePipeNamedPipeInterprocess("StabilityMatrix");

        var exportedTypes = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(a => a.FullName?.StartsWith("StabilityMatrix") == true)
            .SelectMany(a => a.GetExportedTypes())
            .ToArray();

        var transientTypes = exportedTypes
            .Select(t => new { t, attributes = t.GetCustomAttributes(typeof(TransientAttribute), false) })
            .Where(
                t1 =>
                    t1.attributes is { Length: > 0 }
                    && !t1.t.Name.Contains("Mock", StringComparison.OrdinalIgnoreCase)
            )
            .Select(t1 => new { Type = t1.t, Attribute = (TransientAttribute)t1.attributes[0] });

        foreach (var typePair in transientTypes)
        {
            if (typePair.Attribute.InterfaceType is null)
            {
                services.AddTransient(typePair.Type);
            }
            else
            {
                services.AddTransient(typePair.Attribute.InterfaceType, typePair.Type);
            }
        }

        var singletonTypes = exportedTypes
            .Select(t => new { t, attributes = t.GetCustomAttributes(typeof(SingletonAttribute), false) })
            .Where(
                t1 =>
                    t1.attributes is { Length: > 0 }
                    && !t1.t.Name.Contains("Mock", StringComparison.OrdinalIgnoreCase)
            )
            .Select(
                t1 => new { Type = t1.t, Attributes = t1.attributes.Cast<SingletonAttribute>().ToArray() }
            );

        foreach (var typePair in singletonTypes)
        {
            foreach (var attribute in typePair.Attributes)
            {
                if (attribute.InterfaceType is null)
                {
                    services.AddSingleton(typePair.Type);
                }
                else if (attribute.ImplType is not null)
                {
                    services.AddSingleton(attribute.InterfaceType, attribute.ImplType);
                }
                else
                {
                    services.AddSingleton(attribute.InterfaceType, typePair.Type);
                }

                // IDisposable registering
                var serviceType = attribute.InterfaceType ?? typePair.Type;

                if (serviceType == typeof(IDisposable) || serviceType == typeof(IAsyncDisposable))
                {
                    continue;
                }

                if (typePair.Type.IsAssignableTo(typeof(IDisposable)))
                {
                    Debug.WriteLine("Registering IDisposable: {Name}", typePair.Type.Name);
                    services.AddSingleton<IDisposable>(
                        provider => (IDisposable)provider.GetRequiredService(serviceType)
                    );
                }

                if (typePair.Type.IsAssignableTo(typeof(IAsyncDisposable)))
                {
                    Debug.WriteLine("Registering IAsyncDisposable: {Name}", typePair.Type.Name);
                    services.AddSingleton<IAsyncDisposable>(
                        provider => (IAsyncDisposable)provider.GetRequiredService(serviceType)
                    );
                }
            }
        }

        ConfigurePageViewModels(services);
        ConfigureDialogViewModels(services, exportedTypes);

        // Other services
        services.AddSingleton<ITrackedDownloadService, TrackedDownloadService>();
        services.AddSingleton<IDisposable>(
            provider => (IDisposable)provider.GetRequiredService<ITrackedDownloadService>()
        );

        // Rich presence
        services.AddSingleton<IDiscordRichPresenceService, DiscordRichPresenceService>();
        services.AddSingleton<IDisposable>(
            provider => provider.GetRequiredService<IDiscordRichPresenceService>()
        );

        Config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        services.Configure<DebugOptions>(Config.GetSection(nameof(DebugOptions)));

        if (Compat.IsWindows)
        {
            services.AddSingleton<IPrerequisiteHelper, WindowsPrerequisiteHelper>();
        }
        else if (Compat.IsLinux || Compat.IsMacOS)
        {
            services.AddSingleton<IPrerequisiteHelper, UnixPrerequisiteHelper>();
        }

        if (!Design.IsDesignMode)
        {
            services.AddSingleton<ILiteDbContext, LiteDbContext>();
            services.AddSingleton<IDisposable>(p => p.GetRequiredService<ILiteDbContext>());
        }

        services.AddTransient<IGitHubClient, GitHubClient>(_ =>
        {
            var client = new GitHubClient(new ProductHeaderValue("StabilityMatrix"));
            // var githubApiKey = Config["GithubApiKey"];
            // if (string.IsNullOrWhiteSpace(githubApiKey))
            //     return client;
            //
            // client.Credentials =
            //     new Credentials("");
            return client;
        });

        // Configure Refit and Polly
        var jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        jsonSerializerOptions.Converters.Add(new ObjectToInferredTypesConverter());
        jsonSerializerOptions.Converters.Add(new DefaultUnknownEnumConverter<CivitFileType>());
        jsonSerializerOptions.Converters.Add(new DefaultUnknownEnumConverter<CivitModelType>());
        jsonSerializerOptions.Converters.Add(new DefaultUnknownEnumConverter<CivitModelFormat>());
        jsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        jsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        var defaultRefitSettings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(jsonSerializerOptions)
        };

        // Refit settings for IApiFactory
        var defaultSystemTextJsonSettings = SystemTextJsonContentSerializer.GetDefaultJsonSerializerOptions();
        defaultSystemTextJsonSettings.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        var apiFactoryRefitSettings = new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(defaultSystemTextJsonSettings),
        };

        // HTTP Policies
        var retryStatusCodes = new[]
        {
            HttpStatusCode.RequestTimeout, // 408
            HttpStatusCode.InternalServerError, // 500
            HttpStatusCode.BadGateway, // 502
            HttpStatusCode.ServiceUnavailable, // 503
            HttpStatusCode.GatewayTimeout // 504
        };
        var delay = Backoff.DecorrelatedJitterBackoffV2(
            medianFirstRetryDelay: TimeSpan.FromMilliseconds(80),
            retryCount: 5
        );
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .OrResult(r => retryStatusCodes.Contains(r.StatusCode))
            .WaitAndRetryAsync(delay);

        // Shorter timeout for local requests
        var localTimeout = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(3));
        var localDelay = Backoff.DecorrelatedJitterBackoffV2(
            medianFirstRetryDelay: TimeSpan.FromMilliseconds(50),
            retryCount: 3
        );
        var localRetryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<TimeoutRejectedException>()
            .OrResult(r => retryStatusCodes.Contains(r.StatusCode))
            .WaitAndRetryAsync(
                localDelay,
                onRetryAsync: (_, _) =>
                {
                    Debug.WriteLine("Retrying local request...");
                    return Task.CompletedTask;
                }
            );

        // named client for update
        services.AddHttpClient("UpdateClient").AddPolicyHandler(retryPolicy);

        // Add Refit clients
        services
            .AddRefitClient<ICivitApi>(defaultRefitSettings)
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri("https://civitai.com");
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddPolicyHandler(retryPolicy);

        services
            .AddRefitClient<ICivitTRPCApi>(defaultRefitSettings)
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri("https://civitai.com");
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddPolicyHandler(retryPolicy);

        services
            .AddRefitClient<ILykosAuthApi>(defaultRefitSettings)
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri("https://auth.lykos.ai");
                c.Timeout = TimeSpan.FromSeconds(60);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddPolicyHandler(retryPolicy)
            .AddHttpMessageHandler(
                serviceProvider =>
                    new TokenAuthHeaderHandler(serviceProvider.GetRequiredService<LykosAuthTokenProvider>())
            );

        services
            .AddRefitClient<IOpenArtApi>(defaultRefitSettings)
            .ConfigureHttpClient(c =>
            {
                c.BaseAddress = new Uri("https://openart.ai/api/public/workflows");
                c.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddPolicyHandler(retryPolicy);

        // Add Refit client managers
        services.AddHttpClient("A3Client").AddPolicyHandler(localTimeout.WrapAsync(localRetryPolicy));

        services
            .AddHttpClient("DontFollowRedirects")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddPolicyHandler(retryPolicy);

        /*services.AddHttpClient("IComfyApi")
            .AddPolicyHandler(localTimeout.WrapAsync(localRetryPolicy));*/

        // Add Refit client factory
        services.AddSingleton<IApiFactory, ApiFactory>(
            provider =>
                new ApiFactory(provider.GetRequiredService<IHttpClientFactory>())
                {
                    RefitSettings = apiFactoryRefitSettings,
                }
        );

        ConditionalAddLogViewer(services);

        var logConfig = ConfigureLogging();

        // Add logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder
                .AddFilter("Microsoft.Extensions.Http", LogLevel.Warning)
                .AddFilter("Microsoft.Extensions.Http.DefaultHttpClientFactory", LogLevel.Warning)
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning);
            builder.SetMinimumLevel(LogLevel.Trace);
#if DEBUG
            builder.AddNLog(
                logConfig,
                new NLogProviderOptions
                {
                    IgnoreEmptyEventId = false,
                    CaptureEventId = EventIdCaptureType.Legacy
                }
            );
#else
            builder.AddNLog(logConfig);
#endif
        });

        return services;
    }

    /// <summary>
    /// Requests shutdown of the Current Application.
    /// </summary>
    /// <remarks>This returns asynchronously *without waiting* for Shutdown</remarks>
    /// <param name="exitCode">Exit code for the application.</param>
    /// <exception cref="NullReferenceException">If Application.Current is null</exception>
    public static void Shutdown(int exitCode = 0)
    {
        if (Current is null)
            throw new NullReferenceException("Current Application was null when Shutdown called");

        if (Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            try
            {
                var result = lifetime.TryShutdown(exitCode);
                Debug.WriteLine($"Shutdown: {result}");

                if (result)
                {
                    Environment.Exit(exitCode);
                }
            }
            catch (InvalidOperationException)
            {
                // Ignore in case already shutting down
            }
        }
        else
        {
            Environment.Exit(exitCode);
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        Debug.WriteLine("Start OnShutdownRequested");

        if (e.Cancel)
            return;

        // Check if we need to dispose IAsyncDisposables
        if (
            isAsyncDisposeComplete
            || Services.GetServices<IAsyncDisposable>().ToList() is not { Count: > 0 } asyncDisposables
        )
            return;

        // Cancel shutdown for now
        e.Cancel = true;
        isAsyncDisposeComplete = true;

        Debug.WriteLine("OnShutdownRequested Canceled: Disposing IAsyncDisposables");

        Dispatcher
            .UIThread.InvokeAsync(async () =>
            {
                foreach (var disposable in asyncDisposables)
                {
                    Debug.WriteLine($"Disposing IAsyncDisposable ({disposable.GetType().Name})");
                    try
                    {
                        await disposable.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.Fail(ex.ToString());
                    }
                }
            })
            .ContinueWith(_ =>
            {
                // Shutdown again
                Debug.WriteLine("Finished disposing IAsyncDisposables, shutting down");

                if (Dispatcher.UIThread.SupportsRunLoops)
                {
                    Dispatcher.UIThread.Invoke(() => Shutdown());
                }

                Environment.Exit(0);
            })
            .SafeFireAndForget();
    }

    private void OnApplicationLifetimeExit(object? sender, ControlledApplicationLifetimeExitEventArgs args)
    {
        Logger.Debug("OnApplicationLifetimeExit: {@Args}", args);

        OnExit(sender, args);
    }

    private void OnExit(object? sender, EventArgs _)
    {
        // Skip if already run
        if (isOnExitComplete)
        {
            return;
        }

        // Skip if another OnExit is running
        if (!onExitSemaphore.Wait(0))
        {
            // Block until the other OnExit is done to delay shutdown
            onExitSemaphore.Wait();
            onExitSemaphore.Release();
            return;
        }

        try
        {
            const int timeoutTotalMs = 10000;
            const int timeoutPerDisposeMs = 2000;

            var timeoutTotalCts = new CancellationTokenSource(timeoutTotalMs);

            var toDispose = Services.GetServices<IDisposable>().ToImmutableArray();

            Logger.Debug("OnExit: Preparing to Dispose {Count} Services", toDispose.Length);

            // Dispose IDisposable services
            foreach (var disposable in toDispose)
            {
                Logger.Debug("OnExit: Disposing {Name}", disposable.GetType().Name);

                using var instanceCts = CancellationTokenSource.CreateLinkedTokenSource(
                    timeoutTotalCts.Token,
                    new CancellationTokenSource(timeoutPerDisposeMs).Token
                );

                try
                {
                    Task.Run(() => disposable.Dispose(), instanceCts.Token).Wait(instanceCts.Token);
                }
                catch (OperationCanceledException)
                {
                    Logger.Warn("OnExit: Timeout disposing {Name}", disposable.GetType().Name);
                }
                catch (Exception e)
                {
                    Logger.Error(e, "OnExit: Failed to dispose {Name}", disposable.GetType().Name);
                }
            }

            var settingsManager = Services.GetRequiredService<ISettingsManager>();

            // If RemoveFolderLinksOnShutdown is set, delete all package junctions
            if (settingsManager is { IsLibraryDirSet: true, Settings.RemoveFolderLinksOnShutdown: true })
            {
                Logger.Debug("OnExit: Removing package junctions");

                using var instanceCts = CancellationTokenSource.CreateLinkedTokenSource(
                    timeoutTotalCts.Token,
                    new CancellationTokenSource(timeoutPerDisposeMs).Token
                );

                try
                {
                    Task.Run(
                            () =>
                            {
                                var sharedFolders = Services.GetRequiredService<ISharedFolders>();
                                sharedFolders.RemoveLinksForAllPackages();
                            },
                            instanceCts.Token
                        )
                        .Wait(instanceCts.Token);
                }
                catch (OperationCanceledException)
                {
                    Logger.Warn("OnExit: Timeout removing package junctions");
                }
                catch (Exception e)
                {
                    Logger.Error(e, "OnExit: Failed to remove package junctions");
                }
            }

            Logger.Debug("OnExit: Finished");
        }
        finally
        {
            isOnExitComplete = true;
            onExitSemaphore.Release();

            LogManager.Shutdown();
        }
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e
    )
    {
        if (e.Exception is not Exception unobservedEx)
            return;

        try
        {
            var notificationService = Services.GetRequiredService<INotificationService>();

            Dispatcher.UIThread.Invoke(() =>
            {
                var originException = unobservedEx.InnerException ?? unobservedEx;
                notificationService.ShowPersistent(
                    $"Unobserved Task Exception - {originException.GetType().Name}",
                    originException.Message
                );
            });

            // Consider the exception observed if we were able to show a notification
            e.SetObserved();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to show Unobserved Task Exception notification");
        }
    }

    private static LoggingConfiguration ConfigureLogging()
    {
        var setupBuilder = LogManager.Setup();

        ConditionalAddLogViewerNLog(setupBuilder);

        setupBuilder.LoadConfiguration(builder =>
        {
            // Filter some sources to be warn levels or above only
            builder.ForLogger("System.*").WriteToNil(NLog.LogLevel.Warn);
            builder.ForLogger("Microsoft.*").WriteToNil(NLog.LogLevel.Warn);
            builder.ForLogger("Microsoft.Extensions.Http.*").WriteToNil(NLog.LogLevel.Warn);

            // Disable console trace logging by default
            builder
                .ForLogger("StabilityMatrix.Avalonia.ViewModels.ConsoleViewModel")
                .WriteToNil(NLog.LogLevel.Debug);

            // Disable LoadableViewModelBase trace logging by default
            builder
                .ForLogger("StabilityMatrix.Avalonia.ViewModels.Base.LoadableViewModelBase")
                .WriteToNil(NLog.LogLevel.Debug);

            // Debug console logging
            /*if (Debugger.IsAttached)
            {
                builder
                    .ForLogger()
                    .FilterMinLevel(NLog.LogLevel.Trace)
                    .WriteTo(
                        new DebuggerTarget("debugger")
                        {
                            Layout = "[${level:uppercase=true}]\t${logger:shortName=true}\t${message}"
                        }
                    )
                    .WithAsync();
            }*/

            // Console logging
            builder
                .ForLogger()
                .FilterMinLevel(NLog.LogLevel.Trace)
                .WriteTo(
                    new ConsoleTarget("console")
                    {
                        Layout = "[${level:uppercase=true}]\t${logger:shortName=true}\t${message}",
                        DetectConsoleAvailable = true
                    }
                )
                .WithAsync();

            // File logging
            builder
                .ForLogger()
                .FilterMinLevel(NLog.LogLevel.Debug)
                .WriteTo(
                    new FileTarget("logfile")
                    {
                        Layout =
                            "${longdate}|${level:uppercase=true}|${logger}|${message:withexception=true}",
                        FileName = "${specialfolder:folder=ApplicationData}/StabilityMatrix/Logs/app.log",
                        ArchiveOldFileOnStartup = true,
                        ArchiveFileName =
                            "${specialfolder:folder=ApplicationData}/StabilityMatrix/Logs/app.{#}.log",
                        ArchiveDateFormat = "yyyy-MM-dd HH_mm_ss",
                        ArchiveNumbering = ArchiveNumberingMode.Date,
                        MaxArchiveFiles = 9
                    }
                )
                .WithAsync();

#if DEBUG
            // LogViewer target when debug mode
            builder
                .ForLogger()
                .FilterMinLevel(NLog.LogLevel.Trace)
                .WriteTo(new DataStoreLoggerTarget { Layout = "${message}" });
#endif
        });

        // Sentry
        if (SentrySdk.IsEnabled)
        {
            LogManager.Configuration.AddSentry(o =>
            {
                o.InitializeSdk = false;
                o.Layout = "${message}";
                o.ShutdownTimeoutSeconds = 5;
                o.IncludeEventDataOnBreadcrumbs = true;
                o.BreadcrumbLayout = "${logger}: ${message}";
                // Debug and higher are stored as breadcrumbs (default is Info)
                o.MinimumBreadcrumbLevel = NLog.LogLevel.Debug;
                // Error and higher is sent as event (default is Error)
                o.MinimumEventLevel = NLog.LogLevel.Error;
            });
        }

        LogManager.ReconfigExistingLoggers();

        return LogManager.Configuration;
    }

    /// <summary>
    /// Opens a dialog to save the current view as a screenshot.
    /// </summary>
    /// <remarks>Only available in debug builds.</remarks>
    [Conditional("DEBUG")]
    internal static void DebugSaveScreenshot(int dpi = 96)
    {
        const int scale = 2;
        dpi *= scale;

        var results = new List<MemoryStream>();
        var targets = new List<Visual?> { VisualRoot };

        foreach (var visual in targets.Where(x => x != null))
        {
            var rect = new Rect(visual!.Bounds.Size);

            var pixelSize = new PixelSize((int)rect.Width * scale, (int)rect.Height * scale);
            var dpiVector = new Vector(dpi, dpi);

            var ms = new MemoryStream();

            using (var bitmap = new RenderTargetBitmap(pixelSize, dpiVector))
            {
                bitmap.Render(visual);
                bitmap.Save(ms);
            }

            results.Add(ms);
        }

        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dest = await StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions()
                {
                    SuggestedFileName = "screenshot.png",
                    ShowOverwritePrompt = true
                }
            );

            if (dest?.TryGetLocalPath() is { } localPath)
            {
                var localFile = new FilePath(localPath);
                foreach (var (i, stream) in results.Enumerate())
                {
                    var name = localFile.NameWithoutExtension;
                    if (results.Count > 1)
                    {
                        name += $"_{i + 1}";
                    }

                    localFile = localFile.Directory!.JoinFile(name + ".png");
                    localFile.Create();

                    await using var fileStream = localFile.Info.OpenWrite();
                    stream.Seek(0, SeekOrigin.Begin);
                    await stream.CopyToAsync(fileStream);
                }
            }
        });
    }

    [Conditional("DEBUG")]
    private static void ConditionalAddLogViewer(IServiceCollection services)
    {
#if DEBUG
        services.AddLogViewer();
#endif
    }

    [Conditional("DEBUG")]
    private static void ConditionalAddLogViewerNLog(ISetupBuilder setupBuilder)
    {
#if DEBUG
        setupBuilder.SetupExtensions(
            extensionBuilder => extensionBuilder.RegisterTarget<DataStoreLoggerTarget>("DataStoreLogger")
        );
#endif
    }
}
