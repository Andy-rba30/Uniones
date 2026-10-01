using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace UnionesAcero.Revit.UI;

/// <summary>Ventana sencilla para mostrar informes largos con opción de copiar o guardar.</summary>
public sealed class ReportWindow : Window
{
    private readonly TextBox _text;

    public ReportWindow(string title, string content, IntPtr owner)
    {
        Title = title;
        Width = 820;
        Height = 620;
        MinWidth = 500;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        new WindowInteropHelper(this) { Owner = owner };

        _text = new TextBox
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(10)
        };

        var copy = new Button { Content = "Copiar", Width = 100, Margin = new Thickness(5) };
        copy.Click += (_, _) => Clipboard.SetText(_text.Text);
        var save = new Button { Content = "Guardar…", Width = 100, Margin = new Thickness(5) };
        save.Click += (_, _) => Save();
        var close = new Button { Content = "Cerrar", Width = 100, Margin = new Thickness(5), IsCancel = true };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10, 0, 10, 10) };
        buttons.Children.Add(copy);
        buttons.Children.Add(save);
        buttons.Children.Add(close);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_text, 0);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(_text);
        grid.Children.Add(buttons);
        Content = grid;
    }

    private void Save()
    {
        var dlg = new SaveFileDialog { Filter = "Texto (*.txt)|*.txt", FileName = "nudo.txt" };
        if (dlg.ShowDialog(this) == true)
            System.IO.File.WriteAllText(dlg.FileName, _text.Text);
    }

    public static void Show(string title, string content, IntPtr owner)
        => new ReportWindow(title, content, owner).ShowDialog();
}
