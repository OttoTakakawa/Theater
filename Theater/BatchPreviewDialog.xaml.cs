using System.Windows;

namespace Theater;

public partial class BatchPreviewDialog : Window
{
    public bool Confirmed { get; private set; }

    public BatchPreviewDialog(string title, string preview)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        PreviewTextBox.Text = preview;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
    }
}
