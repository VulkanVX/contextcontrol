namespace ContextControl.Workbench.ViewModels;

public sealed class ProjectGraphSearchSuggestionViewModel(
    ProjectNodeViewModel node,
    string title,
    string detail,
    string meta)
{
    public ProjectNodeViewModel Node { get; } = node;
    public string Title { get; } = title;
    public string Detail { get; } = detail;
    public string Meta { get; } = meta;
}
