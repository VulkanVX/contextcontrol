using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using ContextControl.Workbench.Services;

namespace ContextControl.Workbench.Views;

public sealed class HuggingFaceTokenTutorialWindow : Window
{
    private static readonly Uri AppIconUri = new("avares://ContextControl.Workbench/Assets/Icons/contextcontrol64x64.png");
    private const string TokenPageUrl = "https://huggingface.co/settings/tokens";
    private const string TokenDocsUrl = "https://huggingface.co/docs/hub/security-tokens";

    private readonly TextBlock _stepLabel;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock _bodyBlock;
    private readonly StackPanel _linkPanel;
    private readonly Button _backButton;
    private readonly Button _nextButton;
    private int _stepIndex;

    private static readonly TutorialStep[] Steps =
    [
        new(
            "Why this helps",
            "Some image models in ContextControl download through Hugging Face Hub. Without a token, those downloads are anonymous and can hit lower rate limits. Please add a HF access token to not get rate-limited while downloading this model.",
            [new("Token docs", TokenDocsUrl), new("Token page", TokenPageUrl)]),
        new(
            "Create a token",
            "Sign in to Hugging Face, open Settings -> Access Tokens, then choose New token. Name it ContextControl so it is easy to recognize later.",
            [new("Open token page", TokenPageUrl), new("Token docs", TokenDocsUrl)]),
        new(
            "Choose safe access",
            "For public model downloads, a Read token is enough. A fine-grained token with read access to the needed model is also good. Do not create a Write token for ContextControl downloads.",
            [new("Token scopes", TokenDocsUrl), new("Token page", TokenPageUrl)]),
        new(
            "Paste it in ContextControl",
            "Copy the hf_... token once it is created. Return to View -> Settings -> LLMs, paste it into HF token, then retry the Diffusers model download or image generation.",
            [new("Token page", TokenPageUrl), new("Token docs", TokenDocsUrl)]),
        new(
            "Keep it private",
            "Treat the token like a password. If it is leaked, delete or refresh it on Hugging Face and paste the new one here.",
            [new("Manage tokens", TokenPageUrl), new("Security docs", TokenDocsUrl)])
    ];

    public HuggingFaceTokenTutorialWindow()
    {
        Title = "Hugging Face token tutorial";
        Width = 620;
        Height = 430;
        MinWidth = 520;
        MinHeight = 360;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new WindowIcon(AssetLoader.Open(AppIconUri));

        _stepLabel = new TextBlock
        {
            FontSize = 9,
            FontWeight = FontWeight.Black,
            TextWrapping = TextWrapping.Wrap
        };
        _stepLabel.Classes.Add("muted");

        _titleBlock = new TextBlock
        {
            FontSize = 16,
            FontWeight = FontWeight.ExtraBold,
            TextWrapping = TextWrapping.Wrap
        };
        _titleBlock.Classes.Add("settings-title");

        _bodyBlock = new TextBlock
        {
            FontSize = 11,
            LineHeight = 17,
            TextWrapping = TextWrapping.Wrap
        };

        _linkPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };

        _backButton = CommandButton("Back");
        _backButton.Click += (_, _) =>
        {
            if (_stepIndex <= 0)
            {
                Close();
                return;
            }

            if (_stepIndex > 0)
            {
                _stepIndex--;
                RefreshStep();
            }
        };

        _nextButton = CommandButton("Next");
        _nextButton.Classes.Add("primary");
        _nextButton.Click += (_, _) =>
        {
            if (_stepIndex >= Steps.Length - 1)
            {
                Close();
                return;
            }

            _stepIndex++;
            RefreshStep();
        };

        var content = new Border
        {
            Margin = new Thickness(8),
            Padding = new Thickness(12),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 12,
                Children =
                {
                    BuildHeader(),
                    BuildBody(),
                    new Grid
                    {
                        [Grid.RowProperty] = 2,
                        ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                        Children =
                        {
                            new StackPanel
                            {
                                [Grid.ColumnProperty] = 2,
                                Orientation = Orientation.Horizontal,
                                Spacing = 8,
                                HorizontalAlignment = HorizontalAlignment.Right,
                                Children =
                                {
                                    _backButton,
                                    _nextButton
                                }
                            }
                        }
                    }
                }
            }
        };
        content.Classes.Add("settings-panel");

        Content = content;
        ApplyTheme("empty");
        RefreshStep();
    }

    public void ApplyTheme(
        string? themeKey,
        string? uiFontFamily = null,
        string? codeFontFamily = null,
        string? skinKey = null,
        string? uiFontColorModeKey = null,
        string? customUiFontColor = null)
    {
        WorkbenchThemeResources.Apply(this, themeKey, uiFontFamily, codeFontFamily, skinKey: skinKey, uiFontColorModeKey: uiFontColorModeKey, customUiFontColor: customUiFontColor);
        if (Resources.TryGetValue("AppBackgroundBrush", out var brush) && brush is IBrush background)
        {
            Background = background;
        }
    }

    private Control BuildHeader()
    {
        return new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock
                {
                    Text = "Hugging Face token",
                    FontSize = 12,
                    FontWeight = FontWeight.ExtraBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = "Use this only for Hugging Face-backed Diffusers downloads.",
                    FontSize = 9.5,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    private Control BuildBody()
    {
        var panel = new StackPanel
        {
            [Grid.RowProperty] = 1,
            Spacing = 10,
            Children =
            {
                _stepLabel,
                _titleBlock,
                _bodyBlock,
                _linkPanel,
                new Border
                {
                    Padding = new Thickness(8),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Child = new TextBlock
                    {
                        Text = "Affected today: Diffusers image models such as Stable Diffusion, Tiny Stable Diffusion, SSD-1B, LCM DreamShaper, SD Turbo, and FLUX.2 Klein 4B Diffusers. Ollama chat models do not need this token.",
                        FontSize = 9.5,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
        ((Border)panel.Children[^1]).Classes.Add("rule-card");
        return panel;
    }

    private void RefreshStep()
    {
        var step = Steps[_stepIndex];
        _stepLabel.Text = $"Step {_stepIndex + 1:N0} of {Steps.Length:N0}";
        _titleBlock.Text = step.Title;
        _bodyBlock.Text = step.Body;
        _backButton.IsEnabled = true;
        _backButton.Content = _stepIndex <= 0 ? "Close" : "Back";
        _nextButton.Content = _stepIndex >= Steps.Length - 1 ? "Close" : "Next";
        _linkPanel.Children.Clear();
        foreach (var link in step.Links)
        {
            var button = CommandButton(link.Label);
            button.Click += (_, _) => OpenExternal(link.Url);
            _linkPanel.Children.Add(button);
        }

        _linkPanel.IsVisible = _linkPanel.Children.Count > 0;
    }

    private static Button CommandButton(string text)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 26,
            MinWidth = 84,
            Padding = new Thickness(10, 0),
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Classes.Add("dialog-command");
        return button;
    }

    private static void OpenExternal(string url)
    {
        try
        {
            ExternalBrowserService.Open(ExternalBrowserService.DefaultTarget, url);
        }
        catch
        {
            // The tutorial remains usable even if the system browser cannot be opened.
        }
    }

    private sealed record TutorialStep(string Title, string Body, IReadOnlyList<TutorialLink> Links);

    private sealed record TutorialLink(string Label, string Url);
}
