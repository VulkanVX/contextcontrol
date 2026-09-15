// CC-DESC: Tooltip construction for the local LLM catalog surface.

// CC-DESC: Draws the LLM catalogue as a fixed-row virtualized surface for fast scrolling.

using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ContextControl.Workbench.ViewModels;

namespace ContextControl.Workbench.Controls;

public sealed partial class LocalLlmCatalogRenderControl
{
    private Control BuildIconPreviewToolTip(LocalLlmModelViewModel model)
    {
        var image = model.IconImage is null
            ? new TextBlock
            {
                Text = ResolveInitials(model.Provider),
                FontSize = 28,
                FontWeight = FontWeight.Black,
                Foreground = Resource("AccentBrush", AccentFallbackBrush),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
            : new Image
            {
                Source = model.IconImage,
                Width = 180,
                Height = 180,
                Stretch = Stretch.Uniform
            } as Control;

        return new Border
        {
            Padding = new Thickness(8),
            Background = Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush),
            BorderBrush = Resource("PanelBorderBrush", PanelBorderFallbackBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = image
        };
    }

    private Control BuildModelDetailToolTip(LocalLlmModelViewModel model)
    {
        var panel = new StackPanel { Spacing = 3, MaxWidth = 360 };
        if (ShouldWarnAboutMissingHuggingFaceToken(model))
        {
            var warning = new Border
            {
                Padding = new Thickness(5),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                BorderBrush = Resource("AccentBorderBrush", AccentBorderFallbackBrush),
                Background = Resource("CommandPrimaryBackgroundBrush", CommandPrimaryBackgroundFallbackBrush),
                Child = new TextBlock
                {
                    Text = model.HuggingFaceTokenWarning,
                    FontSize = 9,
                    FontWeight = FontWeight.ExtraBold,
                    Foreground = Resource("AccentBrush", AccentFallbackBrush),
                    TextWrapping = TextWrapping.Wrap
                }
            };
            panel.Children.Add(warning);
        }

        panel.Children.Add(new TextBlock
        {
            Text = $"{model.DisplayName} ({model.Id})",
            FontSize = 10,
            FontWeight = FontWeight.ExtraBold,
            Foreground = Resource("TextPrimaryBrush", TextPrimaryFallbackBrush),
            TextWrapping = TextWrapping.Wrap
        });
        foreach (var (label, value) in new[]
        {
            ("BASE", model.ModelBaseLabel),
            ("DEP", model.BackendRequirementLabel),
            ("SIZE", model.EffectiveDownloadSizeLabel),
            ("FIT", model.FitDetail),
            ("VRAM FIT", model.BigVramSummaryLabel),
            ("VRAM", model.MinimumRequirement),
            ("CTX", model.AdvertisedContext),
            ("OK", model.ComfortableContext),
            ("TPS", model.ExpectedSpeed),
            ("LIC", model.License),
            ("THK", model.ReasoningDescription),
            ("LOC", model.StorageLocationLabel),
            ("USE", model.PracticalUse)
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            panel.Children.Add(new Border
            {
                Padding = new Thickness(0, 2),
                BorderBrush = Resource("CommandBorderBrush", CommandBorderFallbackBrush),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(44, GridUnitType.Pixel),
                        new ColumnDefinition(1, GridUnitType.Star)
                    },
                    Children =
                    {
                        new TextBlock
                        {
                            Text = label,
                            FontSize = 9,
                            FontWeight = FontWeight.Black,
                            Foreground = Resource("AccentBrush", AccentFallbackBrush)
                        },
                        new TextBlock
                        {
                            [Grid.ColumnProperty] = 1,
                            Text = Clean(value, 80),
                            FontSize = 9,
                            FontWeight = FontWeight.Bold,
                            Foreground = Resource("TextMutedBrush", TextMutedFallbackBrush),
                            TextWrapping = TextWrapping.Wrap
                        }
                    },
                }
            });
        }

        return new Border
        {
            Padding = new Thickness(6),
            Background = Resource("EditorSurfaceBrush", EditorSurfaceFallbackBrush),
            BorderBrush = Resource("PanelBorderBrush", PanelBorderFallbackBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = panel
        };
    }

    private bool CanRunModelAction(LocalLlmModelViewModel? model)
    {
        return model is not null
            && (model.CanPull || model.CanInstallDependency || model.CanDownloadBackendModel || model.CanUninstall)
            && (PullModelCommand?.CanExecute(model) ?? false);
    }

}
