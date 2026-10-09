using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Noto.App.ViewModels;

// The in-app guide (docs/07): one page that explains the concepts the UI cannot explain on its own.
// Tooltips and ⓘ buttons call Open(topicId) so a confused user lands on the exact page they need.
public sealed partial class HelpViewModel : ObservableObject
{
    public HelpViewModel(string? topicId = null)
    {
        Topics = new ObservableCollection<HelpTopic>(HelpContent.Topics);
        Open(topicId);
    }

    public ObservableCollection<HelpTopic> Topics { get; }

    [ObservableProperty]
    HelpTopic? _selected;

    [ObservableProperty]
    string _filter = "";

    [ObservableProperty]
    bool _noMatches;

    [RelayCommand]
    void Select(HelpTopic topic) => Selected = topic;

    partial void OnFilterChanged(string value)
    {
        var query = value.Trim();
        Topics.Clear();
        foreach (var topic in HelpContent.Topics.Where(t => Matches(t, query)))
            Topics.Add(topic);
        NoMatches = Topics.Count == 0;
        if (!Topics.Contains(Selected ?? HelpContent.Topics[0]))
            Selected = Topics.FirstOrDefault();
    }

    // Deep link: select a topic by id (used by tooltips, ⓘ buttons and the command bar).
    public void Open(string? topicId)
    {
        Selected = HelpContent.Find(topicId) ?? Topics.FirstOrDefault();
    }

    static bool Matches(HelpTopic topic, string query)
    {
        if (query.Length == 0)
            return true;
        if (Contains(topic.Title, query) || Contains(topic.Summary, query))
            return true;
        foreach (var section in topic.Sections)
        {
            if (Contains(section.Heading, query) || Contains(section.Body, query))
                return true;
            if (section.Points is null)
                continue;
            foreach (var point in section.Points)
                if (Contains(point.Label, query) || Contains(point.Text, query))
                    return true;
        }
        return false;
    }

    static bool Contains(string haystack, string query) =>
        haystack.Contains(query, StringComparison.OrdinalIgnoreCase);
}
