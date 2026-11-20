using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Darklands.Workshop.Text;

namespace Darklands.Workshop;

/// <summary>The pack's glossary (W-0 + E-text): its terms listed, one edited at a time (term, translation, note); saved with the pack.</summary>
internal sealed class GlossaryWindow : Window
{
    private readonly Glossary _glossary;
    private readonly ListBox _list = new() { Height = 260 };
    private readonly TextBox _term = new() { Watermark = "the English term (St. Gall, florin, Pfennig ...)" };
    private readonly TextBox _translation = new() { Watermark = "its translation" };
    private readonly TextBox _note = new() { Watermark = "a note (optional)" };

    public GlossaryWindow(Glossary glossary)
    {
        _glossary = glossary;
        Title = "Glossary";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        _list.ItemTemplate = new FuncDataTemplate<GlossaryTerm>((t, _) => new TextBlock { Text = t is null ? "" : $"{t.Term}  →  {t.Translation}   {t.Note}" });
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is GlossaryTerm t)
            {
                (_term.Text, _translation.Text, _note.Text) = (t.Term, t.Translation, t.Note);
            }
        };
        var set = new Button { Content = "Add or change" };
        set.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_term.Text))
            {
                _glossary.Set(new GlossaryTerm(_term.Text.Trim(), (_translation.Text ?? "").Trim(), (_note.Text ?? "").Trim()));
                Fill();
            }
        };
        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) =>
        {
            if (_list.SelectedItem is GlossaryTerm t)
            {
                _glossary.Remove(t.Term);
                Fill();
            }
        };
        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(set);
        buttons.Children.Add(remove);
        buttons.Children.Add(close);
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(12) };
        panel.Children.Add(_list);
        panel.Children.Add(_term);
        panel.Children.Add(_translation);
        panel.Children.Add(_note);
        panel.Children.Add(buttons);
        Content = panel;
        Fill();
    }

    private void Fill() => _list.ItemsSource = _glossary.Terms.OrderBy(t => t.Term, StringComparer.OrdinalIgnoreCase).ToList();
}

/// <summary>The pack's own card-text variables (docs/LOCALISATION.md): a name and its female and male words.</summary>
internal sealed class VariablesWindow : Window
{
    private readonly TextProject _project;
    private readonly ListBox _list = new() { Height = 220 };
    private readonly TextBox _name = new() { Watermark = "a name of letters and digits (him1): the card texts write $him1" };
    private readonly TextBox _female = new() { Watermark = "the words when the chosen character is a woman" };
    private readonly TextBox _male = new() { Watermark = "the words when the chosen character is a man" };
    private readonly TextBlock _status = new();

    public VariablesWindow(TextProject project)
    {
        _project = project;
        Title = "Pack variables";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        _list.ItemTemplate = new FuncDataTemplate<(string Name, string Female, string Male)>((v, _) => new TextBlock { Text = $"${v.Name}:  female {v.Female},  male {v.Male}" });
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is ValueTuple<string, string, string> v)
            {
                (_name.Text, _female.Text, _male.Text) = v;
            }
        };
        var set = new Button { Content = "Add or change" };
        set.Click += (_, _) =>
        {
            try
            {
                _project.SetVariable((_name.Text ?? "").Trim().TrimStart('$'), (_female.Text ?? "").Trim(), (_male.Text ?? "").Trim());
                _status.Text = "";
                Fill();
            }
            catch (ArgumentException e)
            {
                _status.Text = e.Message;
            }
        };
        var remove = new Button { Content = "Remove" };
        remove.Click += (_, _) =>
        {
            _project.RemoveVariable((_name.Text ?? "").Trim().TrimStart('$'));
            Fill();
        };
        var close = new Button { Content = "Close", IsCancel = true };
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(set);
        buttons.Children.Add(remove);
        buttons.Children.Add(close);
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Words the language needs that the English $Names do not give (a pronoun in each case), chosen by the character's sex.", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        panel.Children.Add(_list);
        panel.Children.Add(_name);
        panel.Children.Add(_female);
        panel.Children.Add(_male);
        panel.Children.Add(buttons);
        panel.Children.Add(_status);
        Content = panel;
        Fill();
    }

    private void Fill() => _list.ItemsSource = _project.Variables.ToList();
}
