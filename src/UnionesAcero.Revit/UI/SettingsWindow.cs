using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using UnionesAcero.Core.Codes;
using UnionesAcero.Revit.Settings;

namespace UnionesAcero.Revit.UI;

/// <summary>Formulario de configuración (código en lugar de XAML para mantener el proyecto simple).</summary>
public sealed class SettingsWindow : Window
{
    private readonly PluginSettings _settings;
    private readonly ComboBox _code = new();
    private readonly TextBox _fy = new(), _fc = new();
    private readonly CheckBox _seismic = new(), _lightweight = new(), _epoxy = new(), _infer = new(), _embedFar = new(), _stagger = new();
    private readonly TextBox _colCover = new(), _colTie = new(), _colBar = new();
    private readonly TextBox _beamCover = new(), _beamStirrup = new(), _topDb = new(), _topN = new(), _botDb = new(), _botN = new();
    private readonly TextBox _extension = new(), _clearance = new(), _rounding = new(), _search = new();

    public SettingsWindow(PluginSettings settings, IntPtr owner)
    {
        _settings = settings;
        Title = "Uniones Acero – Configuración";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        new WindowInteropHelper(this) { Owner = owner };

        foreach (var c in AnchorageCodes.All) _code.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Key });
        _code.SelectedIndex = Math.Max(0, AnchorageCodes.All.ToList().FindIndex(c => c.Key == settings.CodeKey));

        var root = new StackPanel { Margin = new Thickness(14), Width = 420 };
        root.Children.Add(Section("Normativa y materiales"));
        root.Children.Add(Row("Normativa", _code));
        root.Children.Add(Row("fy (MPa)", Num(_fy, settings.Fy)));
        root.Children.Add(Row("f'c (MPa)", Num(_fc, settings.Fc)));
        root.Children.Add(Check(_seismic, "Nudo de pórtico sísmico (fórmulas del capítulo sísmico)", settings.SeismicJoint));
        root.Children.Add(Check(_lightweight, "Concreto liviano (λ = 0.75)", settings.LightweightConcrete));
        root.Children.Add(Check(_epoxy, "Barras con recubrimiento epóxico", settings.EpoxyCoated));

        root.Children.Add(Section("Columna (si el modelo no define recubrimiento)"));
        root.Children.Add(Row("Recubrimiento (mm)", Num(_colCover, settings.ColumnCover)));
        root.Children.Add(Row("Ø estribo (mm)", Num(_colTie, settings.ColumnTieDiameter)));
        root.Children.Add(Row("Ø barra longitudinal (mm)", Num(_colBar, settings.ColumnBarDiameter)));

        root.Children.Add(Section("Vigas: barras por defecto"));
        root.Children.Add(Check(_infer, "Usar las barras ya modeladas en la viga si existen", settings.InferBarsFromModel));
        root.Children.Add(Row("Recubrimiento (mm)", Num(_beamCover, settings.BeamCover)));
        root.Children.Add(Row("Ø estribo (mm)", Num(_beamStirrup, settings.BeamStirrupDiameter)));
        root.Children.Add(Row("Superiores: Ø (mm) / cantidad", Pair(Num(_topDb, settings.TopBarDiameter), Num(_topN, settings.TopBarCount))));
        root.Children.Add(Row("Inferiores: Ø (mm) / cantidad", Pair(Num(_botDb, settings.BottomBarDiameter), Num(_botN, settings.BottomBarCount))));

        root.Children.Add(Section("Detallado"));
        root.Children.Add(Check(_embedFar, "Llevar las barras hasta la cara lejana del núcleo de la columna", settings.EmbedToFarFace));
        root.Children.Add(Check(_stagger, "Desplazar capas automáticamente cuando se cruzan barras de dos vigas", settings.AutoStaggerCrossingLayers));
        root.Children.Add(Row("Extensión en la viga (mm, vacío = 2h + traslape)", Num(_extension, settings.ExtensionIntoBeam)));
        root.Children.Add(Row("Holgura gancho–estribo (mm)", Num(_clearance, settings.HookClearance)));
        root.Children.Add(Row("Redondeo de longitudes (mm)", Num(_rounding, settings.LengthRounding)));
        root.Children.Add(Row("Radio de búsqueda de vigas (mm)", Num(_search, settings.BeamSearchDistance)));

        var ok = new Button { Content = "Guardar", Width = 100, Margin = new Thickness(5), IsDefault = true };
        ok.Click += (_, _) => { if (Apply()) { DialogResult = true; Close(); } };
        var cancel = new Button { Content = "Cancelar", Width = 100, Margin = new Thickness(5), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        root.Children.Add(new TextBlock
        {
            Text = "Archivo: " + PluginSettings.FilePath,
            FontSize = 10,
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        Content = root;
    }

    private bool Apply()
    {
        try
        {
            _settings.CodeKey = (string)((ComboBoxItem)_code.SelectedItem).Tag;
            _settings.Fy = Parse(_fy, "fy");
            _settings.Fc = Parse(_fc, "f'c");
            _settings.SeismicJoint = _seismic.IsChecked == true;
            _settings.LightweightConcrete = _lightweight.IsChecked == true;
            _settings.EpoxyCoated = _epoxy.IsChecked == true;
            _settings.ColumnCover = Parse(_colCover, "recubrimiento de columna");
            _settings.ColumnTieDiameter = Parse(_colTie, "estribo de columna");
            _settings.ColumnBarDiameter = Parse(_colBar, "barra de columna");
            _settings.InferBarsFromModel = _infer.IsChecked == true;
            _settings.BeamCover = Parse(_beamCover, "recubrimiento de viga");
            _settings.BeamStirrupDiameter = Parse(_beamStirrup, "estribo de viga");
            _settings.TopBarDiameter = Parse(_topDb, "Ø superior");
            _settings.TopBarCount = (int)Parse(_topN, "cantidad superior");
            _settings.BottomBarDiameter = Parse(_botDb, "Ø inferior");
            _settings.BottomBarCount = (int)Parse(_botN, "cantidad inferior");
            _settings.EmbedToFarFace = _embedFar.IsChecked == true;
            _settings.AutoStaggerCrossingLayers = _stagger.IsChecked == true;
            _settings.ExtensionIntoBeam = string.IsNullOrWhiteSpace(_extension.Text) ? null : Parse(_extension, "extensión en la viga");
            _settings.HookClearance = Parse(_clearance, "holgura");
            _settings.LengthRounding = Parse(_rounding, "redondeo");
            _settings.BeamSearchDistance = Parse(_search, "radio de búsqueda");
            _settings.Save();
            return true;
        }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "Valor no válido", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private static double Parse(TextBox box, string label)
    {
        var text = box.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"El valor de '{label}' no es un número.");
        return v;
    }

    private static TextBox Num(TextBox box, double? value)
    {
        box.Text = value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
        box.Width = 90;
        box.Margin = new Thickness(0, 0, 4, 0);
        return box;
    }

    private static UIElement Pair(UIElement a, UIElement b)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        p.Children.Add(a);
        p.Children.Add(b);
        return p;
    }

    private static UIElement Row(string label, UIElement editor)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(t, 0);
        Grid.SetColumn(editor, 1);
        if (editor is ComboBox cb) cb.Width = 184;
        g.Children.Add(t);
        g.Children.Add(editor);
        return g;
    }

    private static UIElement Check(CheckBox box, string label, bool value)
    {
        box.Content = label;
        box.IsChecked = value;
        box.Margin = new Thickness(0, 3, 0, 3);
        return box;
    }

    private static UIElement Section(string title) => new TextBlock
    {
        Text = title,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 10, 0, 4)
    };
}
