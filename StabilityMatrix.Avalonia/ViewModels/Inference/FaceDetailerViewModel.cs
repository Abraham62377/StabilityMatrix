﻿using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using StabilityMatrix.Avalonia.Controls;
using StabilityMatrix.Avalonia.Services;
using StabilityMatrix.Avalonia.ViewModels.Base;
using StabilityMatrix.Avalonia.ViewModels.Dialogs;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Models;
using StabilityMatrix.Core.Models.Api.Comfy;

namespace StabilityMatrix.Avalonia.ViewModels.Inference;

[View(typeof(FaceDetailerCard))]
[ManagedService]
[Transient]
public partial class FaceDetailerViewModel : LoadableViewModelBase
{
    private readonly ServiceManager<ViewModelBase> vmFactory;
    public const string ModuleKey = "FaceDetailer";

    [ObservableProperty]
    private bool guideSizeFor = true;

    [ObservableProperty]
    private int guideSize = 256;

    [ObservableProperty]
    private int maxSize = 768;

    [ObservableProperty]
    private int steps = 20;

    [ObservableProperty]
    private double cfg = 8;

    [ObservableProperty]
    private ComfySampler? sampler = ComfySampler.Euler;

    [ObservableProperty]
    private ComfyScheduler? scheduler = ComfyScheduler.Normal;

    [ObservableProperty]
    private double denoise = 0.5d;

    [ObservableProperty]
    private int feather = 5;

    [ObservableProperty]
    private bool noiseMask = true;

    [ObservableProperty]
    private bool forceInpaint = false;

    [ObservableProperty]
    private double bboxThreshold = 0.5d;

    [ObservableProperty]
    private int bboxDilation = 10;

    [ObservableProperty]
    private int bboxCropFactor = 3;

    [ObservableProperty]
    private string samDetectionHint = "center-1";

    [ObservableProperty]
    private int samDilation = 0;

    [ObservableProperty]
    private double samThreshold = 0.93d;

    [ObservableProperty]
    private int samBboxExpansion = 0;

    [ObservableProperty]
    private double samMaskHintThreshold = 0.7d;

    [ObservableProperty]
    private string samMaskHintUseNegative = "False";

    [ObservableProperty]
    private int dropSize = 10;

    [ObservableProperty]
    private int cycle = 1;

    [ObservableProperty]
    private HybridModelFile? bboxModel;

    [ObservableProperty]
    private HybridModelFile? segmModel;

    [ObservableProperty]
    private HybridModelFile? samModel;

    [ObservableProperty]
    private bool showSamModelSelector = true;

    [ObservableProperty]
    private bool useSeparatePrompt;

    [ObservableProperty]
    private string positivePrompt = string.Empty;

    [ObservableProperty]
    private string negativePrompt = string.Empty;

    /// <inheritdoc/>
    public FaceDetailerViewModel(
        IInferenceClientManager clientManager,
        ServiceManager<ViewModelBase> vmFactory
    )
    {
        this.vmFactory = vmFactory;
        ClientManager = clientManager;
        SeedCardViewModel = vmFactory.Get<SeedCardViewModel>();
        SeedCardViewModel.GenerateNewSeed();
        PromptCardViewModel = vmFactory.Get<PromptCardViewModel>();
    }

    [JsonPropertyName("DetailerSeed")]
    public SeedCardViewModel SeedCardViewModel { get; }

    [JsonPropertyName("DetailerPrompt")]
    public PromptCardViewModel PromptCardViewModel { get; }

    public ObservableCollection<string> SamDetectionHints { get; set; } =
        [
            "center-1",
            "horizontal-2",
            "vertical-2",
            "rect-4",
            "diamond-4",
            "mask-area",
            "mask-points",
            "mask-point-bbox",
            "none"
        ];

    public ObservableCollection<string> SamMaskHintUseNegatives { get; set; } = ["False", "Small", "Outter"];

    public IInferenceClientManager ClientManager { get; }

    [RelayCommand]
    private async Task RemoteDownload(HybridModelFile? modelFile)
    {
        if (modelFile?.DownloadableResource is not { } resource)
            return;

        var confirmDialog = vmFactory.Get<DownloadResourceViewModel>();
        confirmDialog.Resource = resource;
        confirmDialog.FileName = modelFile.FileName;

        if (await confirmDialog.GetDialog().ShowAsync() == ContentDialogResult.Primary)
        {
            confirmDialog.StartDownload();
        }
    }
}
