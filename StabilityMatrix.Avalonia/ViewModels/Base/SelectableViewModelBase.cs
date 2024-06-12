﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace StabilityMatrix.Avalonia.ViewModels.Base;

public partial class SelectableViewModelBase : ViewModelBase
{
    [ObservableProperty]
    private bool isSelected;

    [RelayCommand]
    private void ToggleSelection()
    {
        IsSelected = !IsSelected;
    }
}
