﻿using System.ComponentModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using StabilityMatrix.Avalonia.Controls;
using StabilityMatrix.Avalonia.Services;
using StabilityMatrix.Avalonia.ViewModels.Base;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Models;
#pragma warning disable CS0657 // Not a valid attribute location for this declaration

namespace StabilityMatrix.Avalonia.ViewModels.Inference;

[View(typeof(ExtraNetworkCard))]
[ManagedService]
[Transient]
public partial class ExtraNetworkCardViewModel(IInferenceClientManager clientManager) : LoadableViewModelBase
{
    public const string ModuleKey = "ExtraNetwork";

    /// <summary>
    /// Whether user can toggle model weight visibility
    /// </summary>
    [JsonIgnore]
    public bool IsModelWeightToggleEnabled { get; set; }

    /// <summary>
    /// Whether user can toggle clip weight visibility
    /// </summary>
    [JsonIgnore]
    public bool IsClipWeightToggleEnabled { get; set; }

    [ObservableProperty]
    private HybridModelFile? selectedModel;

    [ObservableProperty]
    private bool isModelWeightEnabled;

    [ObservableProperty]
    [property: Category("Settings")]
    [property: DisplayName("CLIP Strength Adjustment")]
    private bool isClipWeightEnabled;

    [ObservableProperty]
    private double modelWeight = 1.0;

    [ObservableProperty]
    private double clipWeight = 1.0;

    public IInferenceClientManager ClientManager { get; } = clientManager;

    /// <inheritdoc />
    public override JsonObject SaveStateToJsonObject()
    {
        return SerializeModel(
            new ExtraNetworkCardModel
            {
                SelectedModelName = SelectedModel?.RelativePath,
                IsModelWeightEnabled = IsModelWeightEnabled,
                IsClipWeightEnabled = IsClipWeightEnabled,
                ModelWeight = ModelWeight,
                ClipWeight = ClipWeight
            }
        );
    }

    /// <inheritdoc />
    public override void LoadStateFromJsonObject(JsonObject state)
    {
        var model = DeserializeModel<ExtraNetworkCardModel>(state);

        SelectedModel = model.SelectedModelName is null
            ? null
            : ClientManager.LoraModels.FirstOrDefault(x => x.RelativePath == model.SelectedModelName);

        IsModelWeightEnabled = model.IsModelWeightEnabled;
        IsClipWeightEnabled = model.IsClipWeightEnabled;
        ModelWeight = model.ModelWeight;
        ClipWeight = model.ClipWeight;
    }

    internal class ExtraNetworkCardModel
    {
        public string? SelectedModelName { get; init; }
        public bool IsModelWeightEnabled { get; init; }
        public bool IsClipWeightEnabled { get; init; }
        public double ModelWeight { get; init; }
        public double ClipWeight { get; init; }
    }
}
