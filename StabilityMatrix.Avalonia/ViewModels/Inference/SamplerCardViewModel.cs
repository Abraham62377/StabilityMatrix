﻿using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using StabilityMatrix.Avalonia.Controls;
using StabilityMatrix.Avalonia.Services;
using StabilityMatrix.Avalonia.ViewModels.Base;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Models.Api.Comfy;

namespace StabilityMatrix.Avalonia.ViewModels.Inference;

[View(typeof(SamplerCard))]
public partial class SamplerCardViewModel : LoadableViewModelBase
{
    [ObservableProperty]
    private bool isRefinerStepsEnabled;

    [ObservableProperty]
    private int steps = 20;

    [ObservableProperty]
    private int refinerSteps = 10;

    [ObservableProperty]
    private bool isDenoiseStrengthEnabled;

    [ObservableProperty]
    private double denoiseStrength = 1;

    [ObservableProperty]
    private bool isCfgScaleEnabled;

    [ObservableProperty]
    private double cfgScale = 7;

    [ObservableProperty]
    private bool isDimensionsEnabled;

    [ObservableProperty]
    private int width = 512;

    [ObservableProperty]
    private int height = 512;

    [ObservableProperty]
    private bool isSamplerSelectionEnabled;

    [ObservableProperty]
    private ComfySampler? selectedSampler = new ComfySampler("euler_ancestral");

    [ObservableProperty]
    private bool isSchedulerSelectionEnabled;

    [ObservableProperty]
    private ComfyScheduler? selectedScheduler = new ComfyScheduler("normal");

    [JsonIgnore]
    public IInferenceClientManager ClientManager { get; }

    public SamplerCardViewModel(IInferenceClientManager clientManager)
    {
        ClientManager = clientManager;
    }

    /*/// <inheritdoc />
    public override void LoadStateFromJsonObject(JsonObject state)
    {
        var model = DeserializeModel<SamplerCardModel>(state);

        Steps = model.Steps;
        IsDenoiseStrengthEnabled = model.IsDenoiseStrengthEnabled;
        DenoiseStrength = model.DenoiseStrength;
        IsCfgScaleEnabled = model.IsCfgScaleEnabled;
        CfgScale = model.CfgScale;
        IsDimensionsEnabled = model.IsDimensionsEnabled;
        Width = model.Width;
        Height = model.Height;
        IsSamplerSelectionEnabled = model.IsSamplerSelectionEnabled;
        SelectedSampler = model.SelectedSampler is null
            ? null
            : new ComfySampler(model.SelectedSampler);
    }

    /// <inheritdoc />
    public override JsonObject SaveStateToJsonObject()
    {
        return SerializeModel(
            new SamplerCardModel
            {
                Steps = Steps,
                IsDenoiseStrengthEnabled = IsDenoiseStrengthEnabled,
                DenoiseStrength = DenoiseStrength,
                IsCfgScaleEnabled = IsCfgScaleEnabled,
                CfgScale = CfgScale,
                IsDimensionsEnabled = IsDimensionsEnabled,
                Width = Width,
                Height = Height,
                IsSamplerSelectionEnabled = IsSamplerSelectionEnabled,
                SelectedSampler = SelectedSampler?.Name
            }
        );
    }*/
}
