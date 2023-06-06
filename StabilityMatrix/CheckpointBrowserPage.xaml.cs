﻿using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using StabilityMatrix.ViewModels;
using Wpf.Ui.Controls;

namespace StabilityMatrix;

public partial class CheckpointBrowserPage : Page
{
    public CheckpointBrowserPage(CheckpointBrowserViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    
    private void VirtualizingGridView_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        
        e.Handled = true;
        var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = MouseWheelEvent,
            Source = sender
        };
        if (((Control)sender).Parent is UIElement parent)
        {
            parent.RaiseEvent(eventArg);
        }
    }
}
