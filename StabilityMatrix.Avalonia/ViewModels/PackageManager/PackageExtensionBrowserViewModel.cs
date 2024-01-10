using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using AsyncAwaitBestPractices;
using Avalonia.Controls;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using DynamicData.Binding;
using FluentAvalonia.UI.Controls;
using KGySoft.CoreLibraries;
using StabilityMatrix.Avalonia.Collections;
using StabilityMatrix.Avalonia.Languages;
using StabilityMatrix.Avalonia.Services;
using StabilityMatrix.Avalonia.ViewModels.Base;
using StabilityMatrix.Avalonia.ViewModels.Dialogs;
using StabilityMatrix.Avalonia.Views.PackageManager;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Extensions;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Models;
using StabilityMatrix.Core.Models.PackageModification;
using StabilityMatrix.Core.Models.Packages.Extensions;
using StabilityMatrix.Core.Services;

namespace StabilityMatrix.Avalonia.ViewModels.PackageManager;

[View(typeof(PackageExtensionBrowserView))]
[Transient]
[ManagedService]
public partial class PackageExtensionBrowserViewModel : ViewModelBase, IDisposable
{
    private readonly INotificationService notificationService;
    private readonly ISettingsManager settingsManager;
    private readonly ServiceManager<ViewModelBase> vmFactory;
    private readonly CompositeDisposable cleanUp;

    public PackagePair? PackagePair { get; set; }

    [ObservableProperty]
    private bool isLoading;

    private SourceCache<PackageExtension, string> availableExtensionsSource =
        new(ext => ext.Author + ext.Title + ext.Reference);

    public IObservableCollection<SelectableItem<PackageExtension>> SelectedAvailableItems { get; } =
        new ObservableCollectionExtended<SelectableItem<PackageExtension>>();

    public SearchCollection<
        SelectableItem<PackageExtension>,
        string,
        string
    > AvailableItemsSearchCollection { get; }

    private SourceCache<InstalledPackageExtension, string> installedExtensionsSource =
        new(
            ext =>
                ext.Paths.FirstOrDefault()?.ToString() ?? ext.GitRepositoryUrl ?? ext.GetHashCode().ToString()
        );

    public IObservableCollection<SelectableItem<InstalledPackageExtension>> SelectedInstalledItems { get; } =
        new ObservableCollectionExtended<SelectableItem<InstalledPackageExtension>>();

    public SearchCollection<
        SelectableItem<InstalledPackageExtension>,
        string,
        string
    > InstalledItemsSearchCollection { get; }

    public IObservableCollection<InstalledPackageExtension> InstalledExtensions { get; } =
        new ObservableCollectionExtended<InstalledPackageExtension>();

    public PackageExtensionBrowserViewModel(
        INotificationService notificationService,
        ISettingsManager settingsManager,
        ServiceManager<ViewModelBase> vmFactory
    )
    {
        this.notificationService = notificationService;
        this.settingsManager = settingsManager;
        this.vmFactory = vmFactory;

        var availableItemsChangeSet = availableExtensionsSource
            .Connect()
            .Transform(ext => new SelectableItem<PackageExtension>(ext))
            .ObserveOn(SynchronizationContext.Current!)
            .Publish();

        availableItemsChangeSet
            .AutoRefresh(item => item.IsSelected)
            .Filter(item => item.IsSelected)
            .Bind(SelectedAvailableItems)
            .Subscribe();

        var installedItemsChangeSet = installedExtensionsSource
            .Connect()
            .Transform(ext => new SelectableItem<InstalledPackageExtension>(ext))
            .ObserveOn(SynchronizationContext.Current!)
            .Publish();

        installedItemsChangeSet
            .AutoRefresh(item => item.IsSelected)
            .Filter(item => item.IsSelected)
            .Bind(SelectedInstalledItems)
            .Subscribe();

        cleanUp = new CompositeDisposable(
            AvailableItemsSearchCollection = new SearchCollection<
                SelectableItem<PackageExtension>,
                string,
                string
            >(
                availableItemsChangeSet,
                query =>
                    string.IsNullOrWhiteSpace(query)
                        ? _ => true
                        : x => x.Item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            ),
            availableItemsChangeSet.Connect(),
            InstalledItemsSearchCollection = new SearchCollection<
                SelectableItem<InstalledPackageExtension>,
                string,
                string
            >(
                installedItemsChangeSet,
                query =>
                    string.IsNullOrWhiteSpace(query)
                        ? _ => true
                        : x => x.Item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            ),
            installedItemsChangeSet.Connect()
        );
    }

    public void AddExtensions(
        IEnumerable<PackageExtension> packageExtensions,
        IEnumerable<InstalledPackageExtension> installedExtensions
    )
    {
        availableExtensionsSource.AddOrUpdate(packageExtensions);
        installedExtensionsSource.AddOrUpdate(installedExtensions);
    }

    [RelayCommand]
    public async Task InstallSelectedExtensions()
    {
        var extensions = SelectedAvailableItems.Select(x => x.Item).ToArray();

        if (extensions.Length == 0)
            return;

        var steps = extensions
            .Select(
                ext =>
                    new InstallExtensionStep(
                        PackagePair!.BasePackage.ExtensionManager!,
                        PackagePair.InstalledPackage,
                        ext
                    )
            )
            .Cast<IPackageStep>()
            .ToArray();

        var runner = new PackageModificationRunner { ShowDialogOnStart = true };
        EventManager.Instance.OnPackageInstallProgressAdded(runner);

        await runner.ExecuteSteps(steps);

        ClearSelection();

        RefreshBackground();
    }

    [RelayCommand]
    public async Task UpdateSelectedExtensions()
    {
        var extensions = SelectedInstalledItems.Select(x => x.Item).ToArray();

        if (extensions.Length == 0)
            return;

        var steps = extensions
            .Select(
                ext =>
                    new UpdateExtensionStep(
                        PackagePair!.BasePackage.ExtensionManager!,
                        PackagePair.InstalledPackage,
                        ext
                    )
            )
            .Cast<IPackageStep>()
            .ToArray();

        var runner = new PackageModificationRunner { ShowDialogOnStart = true };
        EventManager.Instance.OnPackageInstallProgressAdded(runner);

        await runner.ExecuteSteps(steps);

        ClearSelection();

        RefreshBackground();
    }

    [RelayCommand]
    public async Task UninstallSelectedExtensions()
    {
        var extensions = SelectedInstalledItems.Select(x => x.Item).ToArray();

        if (extensions.Length == 0)
            return;

        var steps = extensions
            .Select(
                ext =>
                    new UninstallExtensionStep(
                        PackagePair!.BasePackage.ExtensionManager!,
                        PackagePair.InstalledPackage,
                        ext
                    )
            )
            .Cast<IPackageStep>()
            .ToArray();

        var runner = new PackageModificationRunner { ShowDialogOnStart = true };
        EventManager.Instance.OnPackageInstallProgressAdded(runner);

        await runner.ExecuteSteps(steps);

        ClearSelection();

        RefreshBackground();
    }

    [RelayCommand]
    public async Task OpenExtensionsSettingsDialog()
    {
        if (PackagePair is null)
            return;

        var grid = new ExtensionSettingsPropertyGrid
        {
            ManifestUrls = new BindingList<string>(
                PackagePair?.InstalledPackage.ExtraExtensionManifestUrls ?? []
            )
        };

        var dialog = vmFactory
            .Get<PropertyGridViewModel>(vm =>
            {
                vm.Title = $"{Resources.Label_Settings}";
                vm.SelectedObject = grid;
                vm.IncludeCategories = ["Base"];
            })
            .GetSaveDialog();

        dialog.MinDialogWidth = 750;
        dialog.MaxDialogWidth = 750;

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await using var _ = settingsManager.BeginTransaction();

            PackagePair!.InstalledPackage.ExtraExtensionManifestUrls = grid.ManifestUrls.ToList();
        }
    }

    /// <inheritdoc />
    public override async Task OnLoadedAsync()
    {
        await base.OnLoadedAsync();

        await Refresh();
    }

    [RelayCommand]
    public async Task Refresh()
    {
        if (PackagePair is null)
            return;

        IsLoading = true;

        try
        {
            if (Design.IsDesignMode)
            {
                var (availableExtensions, installedExtensions) = SynchronizeExtensions(
                    availableExtensionsSource.Items,
                    installedExtensionsSource.Items
                );

                availableExtensionsSource.EditDiff(availableExtensions);
                installedExtensionsSource.EditDiff(installedExtensions);

                await Task.Delay(250);
            }
            else
            {
                await RefreshCore();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void RefreshBackground()
    {
        RefreshCore()
            .SafeFireAndForget(ex =>
            {
                notificationService.ShowPersistent("Failed to refresh extensions", ex.ToString());
            });
    }

    private async Task RefreshCore()
    {
        using var _ = CodeTimer.StartDebug();

        if (PackagePair?.BasePackage.ExtensionManager is not { } extensionManager)
        {
            throw new NotSupportedException(
                $"The package {PackagePair?.BasePackage} does not support extensions."
            );
        }

        var availableExtensions = await extensionManager.GetManifestExtensionsAsync(
            extensionManager.GetManifests(PackagePair.InstalledPackage)
        );

        var installedExtensions = await extensionManager.GetInstalledExtensionsAsync(
            PackagePair.InstalledPackage
        );

        // Synchronize
        (availableExtensions, installedExtensions) = SynchronizeExtensions(
            availableExtensions,
            installedExtensions
        );

        await Task.Run(() =>
        {
            availableExtensionsSource.Edit(updater =>
            {
                updater.Load(availableExtensions);
            });

            installedExtensionsSource.Edit(updater =>
            {
                updater.Load(installedExtensions);
            });
        });
    }

    public void ClearSelection()
    {
        foreach (var item in SelectedAvailableItems.ToImmutableArray())
        {
            item.IsSelected = false;
        }

        foreach (var item in SelectedInstalledItems.ToImmutableArray())
        {
            item.IsSelected = false;
        }
    }

    [Pure]
    private static (
        IEnumerable<PackageExtension>,
        IEnumerable<InstalledPackageExtension>
    ) SynchronizeExtensions(
        IEnumerable<PackageExtension> extensions,
        IEnumerable<InstalledPackageExtension> installedExtensions
    )
    {
        using var _ = CodeTimer.StartDebug();

        var extensionsArr = extensions.ToImmutableArray();

        // For extensions, map their file paths for lookup
        var repoToExtension = extensionsArr
            .SelectMany(ext => ext.Files.Select(path => (path, ext)))
            .ToLookup(kv => kv.path.ToString().StripEnd(".git"))
            .ToDictionary(group => group.Key, x => x.First().ext);

        // For installed extensions, add remote repo if available
        var installedExtensionsWithDefinition = installedExtensions.Select(
            installedExt =>
                installedExt.GitRepositoryUrl is null
                || !repoToExtension.TryGetValue(
                    installedExt.GitRepositoryUrl.StripEnd(".git"),
                    out var mappedExt
                )
                    ? installedExt
                    : installedExt with
                    {
                        Definition = mappedExt
                    }
        );

        return (extensionsArr, installedExtensionsWithDefinition);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        availableExtensionsSource.Dispose();
        installedExtensionsSource.Dispose();

        cleanUp.Dispose();

        GC.SuppressFinalize(this);
    }

    private class ExtensionSettingsPropertyGrid : AbstractNotifyPropertyChanged
    {
        [Category("Base")]
        [DisplayName("Extension Manifest Sources")]
        public BindingList<string> ManifestUrls { get; init; } = [];
    }
}
