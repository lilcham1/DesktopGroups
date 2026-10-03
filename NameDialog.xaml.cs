using System.Windows;

namespace DesktopGroups;

/// <summary>Asks for a new group's name and only accepts names <see cref="GroupStore.NameProblem"/> allows.</summary>
public partial class NameDialog : Window
{
    NameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
        NameBox.TextChanged += (_, _) => Problem.Visibility = Visibility.Collapsed;
    }

    /// <summary>The accepted name, or null if cancelled.</summary>
    public static string? Ask()
    {
        var dialog = new NameDialog();
        return dialog.ShowDialog() == true ? dialog.NameBox.Text.Trim() : null;
    }

    void Create_Click(object sender, RoutedEventArgs e)
    {
        var problem = GroupStore.NameProblem(NameBox.Text.Trim());
        if (problem != null)
        {
            Problem.Text = problem;
            Problem.Visibility = Visibility.Visible;
            return;
        }
        DialogResult = true;
    }
}
