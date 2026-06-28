using System.Windows;

namespace Theater;

public partial class LibraryReportDialog : Window
{
    public LibraryReportDialog(string title, string report)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        ReportTextBox.Text = report;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
