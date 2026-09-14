// CC-DESC: Lazily exposes the selected chat session messages to transcript renderers.

using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ContextControl.Workbench.ViewModels;

public sealed class ChatMessageCollectionViewModel : IReadOnlyList<LocalLlmChatMessageViewModel>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly Dictionary<int, LocalLlmChatMessageViewModel> _cache = [];
    private ChatSessionViewModel? _session;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public int Count => _session?.MessageCount ?? 0;

    public LocalLlmChatMessageViewModel this[int index]
    {
        get
        {
            if (_session is null)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (index < 0 || index >= _session.MessageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            if (_cache.TryGetValue(index, out var message))
            {
                return message;
            }

            message = _session.CreateMessageAt(index);
            _cache[index] = message;
            return message;
        }
    }

    public void Load(ChatSessionViewModel? session)
    {
        if (ReferenceEquals(_session, session))
        {
            return;
        }

        _session = session;
        _cache.Clear();
        RaiseReset();
    }

    public void Clear()
    {
        Load(null);
    }

    public void Add(LocalLlmChatMessageViewModel message)
    {
        if (_session is null)
        {
            return;
        }

        var index = Math.Max(0, _session.MessageCount - 1);
        _cache[index] = message;
        OnPropertyChanged(nameof(Count));
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, message, index));
    }

    public IEnumerator<LocalLlmChatMessageViewModel> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(nameof(Count));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
