using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using UnionesAcero.Core.Analysis;
using UnionesAcero.Core.Codes;
using UnionesAcero.Core.Model;
using UnionesAcero.Core.Verification;
using UnionesAcero.Revit.Services;
using UnionesAcero.Revit.Settings;

namespace UnionesAcero.Revit.UI;

/// <summary>
/// Ventana previa al armado de nudos: lista de columnas seleccionadas (sección detectada y vigas
/// que llegan), opciones generales (normativa, materiales, barras, ganchos, recubrimientos),
/// tabla editable de las vigas de la columna seleccionada, y esquemas de planta y alzado que se
/// redibujan con cada cambio. Construida en código (sin XAML), con el tema oscuro de Revit.
/// </summary>
public sealed class JointWindow : Window
{
    private readonly PluginSettings _cfg;
    private readonly IList<ColumnItem> _items;
    private readonly IList<string> _barTypes;
    private readonly IDictionary<string, double> _diametersMm;
    private readonly Dictionary<string, string> _typeByDisplay = new();
    private readonly IList<string> _hookTypes;
    private readonly IDictionary<string, double> _hookAngles;

    /// <summary>Configuración final si el usuario pulsó "Armar"; null si canceló.</summary>
    public PluginSettings? Result { get; private set; }

    private ComboBox _code = null!, _topType = null!, _botType = null!, _hook = null!, _mode = null!;
    private TextBox _fy = null!, _fc = null!, _topN = null!, _botN = null!;
    private CheckBox _seismic = null!, _light = null!, _epoxy = null!, _infer = null!, _embedFar = null!, _stagger = null!, _highlight = null!;
    private TextBox _colCover = null!, _colTie = null!, _beamCover = null!, _beamStirrup = null!, _extension = null!, _clearance = null!, _rounding = null!;
    private TextBlock _message = null!, _previewCaption = null!, _beamsCaption = null!, _existingText = null!, _barsHint = null!;
    private Button _buildButton = null!;
    private Grid _beamsGrid = null!;
    private PlanPreview _plan = null!;
    private ElevationPreview _elevation = null!;

    private readonly Dictionary<ColumnItem, (System.Windows.Documents.Run Kind, System.Windows.Documents.Run Detail)> _itemRuns = new();
    private readonly Dictionary<ColumnItem, Border> _itemRows = new();
    private readonly List<(BeamItem Beam, TextBlock Top, TextBlock Bottom, Control[] TopInputs, Control[] BottomInputs)> _beamRows = new();
    private ColumnItem? _selected;
    private BeamItem? _selectedBeam;
    private ColumnItem? _beamsFor;
    private bool _building = true, _refreshingBeams;

    private const string General = "(general)";
    private static readonly Thickness Pad = new(4, 2, 4, 2);

    public JointWindow(PluginSettings cfg, IList<ColumnItem> items, IList<string> barTypes, IDictionary<string, double> diametersMm,
                       IList<string> hookTypes, IDictionary<string, double> hookAngles)
    {
        _cfg = cfg.Normalized();
        _items = items;
        _diametersMm = diametersMm;
        _barTypes = barTypes.OrderBy(n => diametersMm.TryGetValue(n, out var mm) ? mm : 0).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var n in _barTypes) _typeByDisplay[TypeDisplay(n)] = n;
        _hookTypes = hookTypes;
        _hookAngles = hookAngles;

        Title = "Nudos viga-columna";
        Width = 1240;
        Height = 840;
        MinWidth = 1000;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;
        FontSize = 12;

        RevitTheme.Apply(this);
        Content = BuildRoot();
        _selected = _items.FirstOrDefault(i => i.CanBuild);
        if (_selected != null) SelectItem(_selected);
        _building = false;
        Refresh();
    }

    // ------------------------------------------------------------------ interfaz
    private UIElement BuildRoot()
    {
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var elements = BuildElements();
        Grid.SetRow(elements, 0);
        root.Children.Add(elements);

        var body = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

        var left = new StackPanel();
        left.Children.Add(BuildBeams());
        left.Children.Add(BuildCode());
        left.Children.Add(BuildBars());
        left.Children.Add(BuildDetailing());
        var scroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(scroll, 0);
        body.Children.Add(scroll);

        var previews = BuildPreviews();
        Grid.SetColumn(previews, 1);
        body.Children.Add(previews);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var buttons = BuildButtons();
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        return root;
    }

    private UIElement BuildElements()
    {
        var ok = _items.Count(i => i.CanBuild);
        var group = new GroupBox { Header = $"Columnas seleccionadas: {_items.Count} ({ok} con vigas). Haz clic en una para ver su nudo.", Padding = new Thickness(4) };
        var outer = new StackPanel();
        outer.Children.Add(new TextBlock
        {
            Text = "Cómo funciona: 1) se lee la sección de la columna, las vigas que llegan y las barras ya modeladas (columna y vigas); " +
                   "2) para cada capa de cada viga se calcula el anclaje que cabe en la columna (recto, gancho a 90° o pasante) según la norma; " +
                   "3) vigas sin barras: se proponen barras nuevas; vigas con barras: se verifican y las que no cumplen se corrigen " +
                   "(misma barra, prolongada hasta el núcleo con gancho). Nada cambia en el modelo hasta pulsar Armar.",
            Foreground = RevitTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 6)
        });
        var panel = new StackPanel();
        foreach (var item in _items)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            text.Inlines.Add(new System.Windows.Documents.Run(item.Tag) { FontWeight = FontWeights.Bold });
            var kind = new System.Windows.Documents.Run(item.Kind + ": ") { FontWeight = FontWeights.SemiBold, Foreground = item.CanBuild ? RevitTheme.Ok : RevitTheme.Error };
            var detail = new System.Windows.Documents.Run(item.Describe());
            text.Inlines.Add(kind);
            text.Inlines.Add(detail);
            _itemRuns[item] = (kind, detail);
            var border = new Border { Child = text, Padding = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(3) };
            if (item.CanBuild)
            {
                border.Cursor = Cursors.Hand;
                var captured = item;
                border.MouseLeftButtonDown += (_, _) => { SelectItem(captured); Refresh(); };
            }
            _itemRows[item] = border;
            panel.Children.Add(border);
        }
        outer.Children.Add(new ScrollViewer { Content = panel, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        group.Content = outer;
        return group;
    }

    private void SelectItem(ColumnItem item)
    {
        _selected = item;
        _selectedBeam = item.Beams.FirstOrDefault(b => b.Include);
        foreach (var kv in _itemRows) kv.Value.Background = ReferenceEquals(kv.Key, item) ? RevitTheme.Selection : Brushes.Transparent;
    }

    private UIElement BuildBeams()
    {
        var group = new GroupBox { Header = "Vigas de la columna seleccionada", Padding = new Thickness(4) };
        var panel = new StackPanel();
        _beamsCaption = new TextBlock { Foreground = RevitTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 4) };
        panel.Children.Add(_beamsCaption);
        _beamsGrid = new Grid { Margin = new Thickness(4, 0, 4, 2) };
        panel.Children.Add(_beamsGrid);
        _existingText = new TextBlock { Foreground = RevitTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 0) };
        panel.Children.Add(_existingText);
        group.Content = panel;
        return group;
    }

    /// <summary>Reconstruye la tabla de vigas cuando cambia la columna; si no, solo actualiza las decisiones.</summary>
    private void RefreshBeams()
    {
        var item = _selected is { CanBuild: true } ? _selected : null;
        _refreshingBeams = true;
        try
        {
            if (!ReferenceEquals(_beamsFor, item))
            {
                _beamsFor = item;
                _beamRows.Clear();
                _beamsGrid.Children.Clear();
                _beamsGrid.RowDefinitions.Clear();
                _beamsGrid.ColumnDefinitions.Clear();
                if (item == null)
                {
                    _beamsCaption.Text = "Selecciona una columna con vigas en la lista.";
                    return;
                }
                _beamsCaption.Text = "Por viga: resultado de cada capa. Tipo y número de barras solo se usan para las capas SIN barras modeladas (vacío / 0 = valores generales). Clic en el nombre para ver su alzado.";
                foreach (var w in new[] { 24.0, 110, 150, 42, 150, 42 })
                    _beamsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
                _beamsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                _beamsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var headers = new[] { "", "Viga", "Superiores", "n", "Inferiores", "n", "Resultado sup. / inf." };
                for (var c = 0; c < headers.Length; c++)
                {
                    var tb = new TextBlock { Text = headers[c], Foreground = RevitTheme.Muted, Margin = Pad };
                    Grid.SetRow(tb, 0); Grid.SetColumn(tb, c);
                    _beamsGrid.Children.Add(tb);
                }

                var row = 1;
                foreach (var b in item.Beams)
                {
                    var captured = b;
                    _beamsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var include = new CheckBox { IsChecked = b.Include, Margin = Pad, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Incluir esta viga en el armado" };
                    include.Checked += (_, _) => { captured.Include = true; Refresh(); };
                    include.Unchecked += (_, _) => { captured.Include = false; Refresh(); };
                    Place(include, row, 0);

                    var name = new TextBlock { Text = b.Name, Margin = Pad, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = $"{b.Name}: {b.Base.Width:0} x {b.Base.Depth:0} mm; barras en el modelo: {b.ModelBarsText}" };
                    if (b.ModelBars.HasValue) name.Foreground = RevitTheme.Text; else name.Foreground = RevitTheme.Muted;
                    name.MouseLeftButtonDown += (_, _) => { _selectedBeam = captured; Refresh(); };
                    Place(name, row, 1);

                    var topType = TypeCombo(b.TopBarTypeName, withGeneral: true);
                    topType.SelectionChanged += (_, _) => { if (!_refreshingBeams) { captured.TopBarTypeName = TypeOf(topType, true); Refresh(); } };
                    Place(topType, row, 2);
                    var topN = CountBox(b.TopBarCount);
                    topN.TextChanged += (_, _) => { if (!_refreshingBeams) { captured.TopBarCount = int.TryParse(topN.Text.Trim(), out var n) ? Math.Max(0, n) : 0; Refresh(); } };
                    Place(topN, row, 3);
                    var botType = TypeCombo(b.BottomBarTypeName, withGeneral: true);
                    botType.SelectionChanged += (_, _) => { if (!_refreshingBeams) { captured.BottomBarTypeName = TypeOf(botType, true); Refresh(); } };
                    Place(botType, row, 4);
                    var botN = CountBox(b.BottomBarCount);
                    botN.TextChanged += (_, _) => { if (!_refreshingBeams) { captured.BottomBarCount = int.TryParse(botN.Text.Trim(), out var n) ? Math.Max(0, n) : 0; Refresh(); } };
                    Place(botN, row, 5);

                    var status = new StackPanel { Orientation = Orientation.Vertical, Margin = Pad, VerticalAlignment = VerticalAlignment.Center };
                    var top = new TextBlock { FontSize = 11 };
                    var bottom = new TextBlock { FontSize = 11 };
                    status.Children.Add(top);
                    status.Children.Add(bottom);
                    Place(status, row, 6);
                    _beamRows.Add((b, top, bottom, new Control[] { topType, topN }, new Control[] { botType, botN }));
                    row++;
                }
            }

            // actualizar decisiones y resaltado de la viga seleccionada
            foreach (var (b, top, bottom, topInputs, botInputs) in _beamRows)
            {
                LayerStatus(b, BarLayer.Top, top);
                LayerStatus(b, BarLayer.Bottom, bottom);
                // Tipo y número solo cuentan para capas que recibirán barras nuevas; si la capa ya
                // tiene barras (y no se eligió "ignorar y añadir") se bloquean para no confundir.
                SetEnabled(topInputs, LayerTakesNewBars(b, BarLayer.Top));
                SetEnabled(botInputs, LayerTakesNewBars(b, BarLayer.Bottom));
            }
            if (item != null)
            {
                var sets = item.Beams.Sum(b => b.ExistingBars.Count);
                var existing = item.Beams.Sum(b => b.Checks.Count(c => c.Status != CheckStatus.NotApplicable));
                var failing = item.Beams.Sum(b => b.Checks.Count(c => c.Status == CheckStatus.Fail));
                var fixes = item.FixCount;
                if (sets == 0)
                    _existingText.Text = "Las vigas no tienen barras longitudinales modeladas: se proponen barras nuevas.";
                else if (existing == 0)
                    _existingText.Text = $"Las vigas tienen {sets} conjunto(s) de barras, pero ninguno es una barra longitudinal que llegue a esta columna (o la viga no está conectada).";
                else
                    _existingText.Text = $"Barras ya modeladas: {existing} conjunto(s) revisados, {failing} no cumplen" +
                                         (fixes > 0 ? $", {fixes} se corregirán al armar" : "") +
                                         ". En los esquemas: a trazos verde = cumple, a trazos rojo = no cumple, naranja = corregida.";
                _existingText.Foreground = failing > 0 && fixes < failing ? RevitTheme.Error : failing > 0 ? RevitTheme.Warn : RevitTheme.Muted;
            }
        }
        finally { _refreshingBeams = false; }
    }

    private void LayerStatus(BeamItem b, BarLayer layer, TextBlock text)
    {
        var prefix = layer == BarLayer.Top ? "sup: " : "inf: ";
        if (!b.Include) { text.Text = prefix + "(excluida)"; text.Foreground = RevitTheme.Hint; return; }
        var j = b.Joint;
        if (j == null) { text.Text = prefix + "—"; text.Foreground = RevitTheme.Hint; return; }
        if (!j.Connected)
        {
            text.Text = prefix + (j.Diagnostics.FirstOrDefault()?.Message ?? "no conectada");
            text.Foreground = RevitTheme.Error;
            return;
        }
        var p = b.Plan(layer);
        var l = p?.Result;
        if (p == null || l == null) { text.Text = prefix + "—"; text.Foreground = RevitTheme.Hint; return; }

        string Req(AnchorageDecision d, double req, double prov) => d == AnchorageDecision.PassThrough
            ? $"pasante (h = {j.AvailableDepth:0})"
            : $"{JointAnalyzer.DecisionName(d)} (req. {req:0} / prov. {prov:0})";
        var newReq = l.Decision == AnchorageDecision.Straight ? l.StraightRequired : l.HookRequired;

        switch (p.Action)
        {
            case LayerAction.CreateNew:
            {
                var g = p.NewGroup;
                var bars = g != null ? $"{g.Count}Ø{g.Diameter:0} " : "";
                text.Text = prefix + "NUEVAS " + bars + Req(l.Decision, newReq, l.Provided);
                text.Foreground = RevitTheme.Ok;
                break;
            }
            case LayerAction.KeepExisting:
                text.Text = prefix + "existentes " + ExistingBars(b, p) + " cumplen: " + Short(p.Existing.FirstOrDefault()?.Message);
                text.Foreground = RevitTheme.Ok;
                break;
            case LayerAction.FixExisting:
            {
                var f = p.Fixes[0].Fix;
                text.Text = prefix + "existentes " + ExistingBars(b, p) + " NO cumplen → corregir: " + Req(f.Decision, f.RequiredLength, f.ProvidedLength) +
                            (p.Fixes.Count > 1 ? $" ({p.Fixes.Count} conjuntos)" : "") +
                            (p.Unfixable.Count > 0 ? $"; {p.Unfixable.Count} sin corrección posible" : "") +
                            (p.ExistingOk > 0 ? $"; {p.ExistingOk} ya cumplen" : "");
                text.Foreground = p.Unfixable.Count == 0 ? RevitTheme.Warn : RevitTheme.Error;
                break;
            }
            case LayerAction.VerifyOnly:
                text.Text = prefix + "existentes " + ExistingBars(b, p) + (p.ExistingFail > 0
                    ? $": {p.ExistingFail} NO cumplen (solo verificación): " + Short(p.Existing.FirstOrDefault(c => c.Status == CheckStatus.Fail)?.Message)
                    : " cumplen: " + Short(p.Existing.FirstOrDefault()?.Message));
                text.Foreground = p.ExistingFail > 0 ? RevitTheme.Error : RevitTheme.Ok;
                break;
            case LayerAction.Insufficient:
                text.Text = prefix + (p.Unfixable.Count > 0
                    ? "existentes NO cumplen y no hay corrección: " + Short(p.Unfixable[0].Reason)
                    : "INSUFICIENTE: " + Req(l.Decision, newReq, l.Provided));
                text.Foreground = RevitTheme.Error;
                break;
            default:
                text.Text = prefix + "—";
                text.Foreground = RevitTheme.Hint;
                break;
        }
        text.ToolTip = string.Join(Environment.NewLine,
            p.Existing.Select(c => $"[{c.Id}] {c.Message}")
             .Concat(p.Unfixable.Select(u => $"[{u.Bar.Id}] sin corrección: {u.Reason}"))
             .Concat(p.Fixes.Select(f => $"[{f.Bar.Id}] {f.Fix.Message}"))
             .DefaultIfEmpty(l.Formula));
    }

    private static string ExistingBars(BeamItem b, LayerPlan p)
    {
        var parts = p.Existing.Select(c => b.ExistingBars.FirstOrDefault(e => e.Bar.Id == c.Id).Bar)
            .Where(bar => bar != null).Select(bar => $"{bar!.Count}Ø{bar.Diameter:0}").ToList();
        return parts.Count == 0 ? "" : string.Join("+", parts);
    }

    private static string Short(string? msg)
    {
        if (string.IsNullOrEmpty(msg)) return "";
        var dot = msg.IndexOf(". ", StringComparison.Ordinal);
        return dot > 0 ? msg[..dot] : msg.TrimEnd('.');
    }

    /// <summary>¿La capa recibirá barras nuevas (no tiene barras modeladas, o se eligió ignorarlas)?</summary>
    private static bool LayerTakesNewBars(BeamItem b, BarLayer layer)
    {
        if (!b.Include || b.Joint is not { Connected: true }) return true;
        var p = b.Plan(layer);
        return p == null || p.Action is LayerAction.CreateNew || (p.Action == LayerAction.Insufficient && p.Unfixable.Count == 0);
    }

    private static void SetEnabled(IEnumerable<Control> controls, bool enabled)
    {
        foreach (var c in controls)
        {
            c.IsEnabled = enabled;
            c.Opacity = enabled ? 1 : 0.45;
        }
    }

    private void Place(FrameworkElement e, int row, int col)
    {
        Grid.SetRow(e, row); Grid.SetColumn(e, col);
        _beamsGrid.Children.Add(e);
    }

    private UIElement BuildCode()
    {
        var group = new GroupBox { Header = "Normativa y materiales", Padding = new Thickness(4) };
        var grid = FormGrid();
        var r = 0;
        _code = new ComboBox { Margin = Pad };
        foreach (var c in AnchorageCodes.All) _code.Items.Add(c.Name);
        _code.SelectedIndex = Math.Max(0, AnchorageCodes.All.ToList().FindIndex(c => c.Key == _cfg.CodeKey));
        AddRow(grid, r++, "Normativa:", _code, "Fórmulas de longitud de desarrollo recta (ld) y con gancho (ldh).");
        _fy = NumBox(_cfg.Fy);
        AddRow(grid, r++, "fy (MPa):", _fy, "Fluencia del acero (420 = Grado 60).");
        _fc = NumBox(_cfg.Fc);
        AddRow(grid, r++, "f'c (MPa):", _fc, "Resistencia del concreto.");
        _seismic = new CheckBox { Content = "Nudo de pórtico sísmico (capítulo sísmico: ldh = fy·db/(5.4·λ·√f'c), ld = 2.5 ó 3.25·ldh)", IsChecked = _cfg.SeismicJoint, Margin = Pad };
        AddRow(grid, r++, "", _seismic, "Desmarcado: fórmulas generales de anclaje (más cortas).");
        _light = new CheckBox { Content = "Concreto liviano (λ = 0.75)", IsChecked = _cfg.LightweightConcrete, Margin = Pad };
        AddRow(grid, r++, "", _light, null);
        _epoxy = new CheckBox { Content = "Barras con recubrimiento epóxico", IsChecked = _cfg.EpoxyCoated, Margin = Pad };
        AddRow(grid, r++, "", _epoxy, null);
        group.Content = grid;
        return group;
    }

    private UIElement BuildBars()
    {
        var group = new GroupBox { Header = "Barras y gancho (valores generales para todas las vigas)", Padding = new Thickness(4) };
        var grid = FormGrid();
        var r = 0;
        _barsHint = new TextBlock { Foreground = RevitTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = Pad };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_barsHint, r); Grid.SetColumn(_barsHint, 0); Grid.SetColumnSpan(_barsHint, 2);
        grid.Children.Add(_barsHint);
        r++;
        _topType = TypeCombo(_cfg.TopBarTypeName, withGeneral: false);
        var topRow = new StackPanel { Orientation = Orientation.Horizontal };
        _topN = CountBox(_cfg.TopBarCount);
        topRow.Children.Add(_topType);
        topRow.Children.Add(new TextBlock { Text = "×", Margin = new Thickness(4, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
        topRow.Children.Add(_topN);
        Hook(_topType);
        Hook(_topN);
        AddRow(grid, r++, "Capa superior:", topRow, "Tipo de barra (RebarBarType) y número de barras de la capa superior de las vigas.");
        _botType = TypeCombo(_cfg.BottomBarTypeName, withGeneral: false);
        var botRow = new StackPanel { Orientation = Orientation.Horizontal };
        _botN = CountBox(_cfg.BottomBarCount);
        botRow.Children.Add(_botType);
        botRow.Children.Add(new TextBlock { Text = "×", Margin = new Thickness(4, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
        botRow.Children.Add(_botN);
        Hook(_botType);
        Hook(_botN);
        AddRow(grid, r++, "Capa inferior:", botRow, "Tipo de barra y número de barras de la capa inferior.");
        _infer = new CheckBox { Content = "Si la viga ya tiene barras modeladas, proponer su diámetro y cantidad para las barras nuevas", IsChecked = _cfg.InferBarsFromModel, Margin = Pad };
        AddRow(grid, r++, "", _infer, "Solo cuando no se ha elegido nada arriba ni en la tabla de vigas.");
        _mode = new ComboBox { Margin = Pad };
        _mode.Items.Add("Verificarlas y corregir las que no cumplen (recomendado)");
        _mode.Items.Add("Solo verificarlas, sin modificar nada");
        _mode.Items.Add("Ignorarlas y añadir barras nuevas");
        _mode.SelectedIndex = _cfg.ExistingBarsAction switch { ExistingBarsAction.VerifyOnly => 1, ExistingBarsAction.AddNew => 2, _ => 0 };
        AddRow(grid, r++, "Vigas con barras modeladas:", _mode,
            "Corregir = cada conjunto de barras longitudinales que no cumple el anclaje se sustituye por la misma barra (mismo tipo, mismo número, misma posición) " +
            "cortada en la columna y prolongada hasta el núcleo, con gancho a 90° si el anclaje recto no cabe. Las que cumplen no se tocan y no se añaden barras nuevas en esa capa.");
        _hook = new ComboBox { Margin = Pad };
        foreach (var h in _hookTypes) _hook.Items.Add(HookDisplay(h));
        var hookMatch = MatchName(_hookTypes, _cfg.HookTypeName);
        _hook.SelectedIndex = hookMatch == null ? -1 : _hookTypes.IndexOf(hookMatch);
        AddRow(grid, r++, "Gancho de 90°:", _hook, "Tipo de gancho (RebarHookType) para los anclajes con gancho. La extensión la fija el tipo (la norma pide 12·db).");
        group.Content = grid;
        return group;
    }

    private UIElement BuildDetailing()
    {
        var group = new GroupBox { Header = "Recubrimientos y detallado", Padding = new Thickness(4) };
        var grid = FormGrid();
        var r = 0;
        _colCover = NumBox(_cfg.ColumnCoverMm);
        AddRow(grid, r++, "Recubrimiento columna (mm):", _colCover, "Se usa si la columna no tiene recubrimiento definido en Revit.");
        _colTie = NumBox(_cfg.ColumnTieDiameterMm);
        AddRow(grid, r++, "Ø estribo columna (mm):", _colTie, "El gancho debe quedar dentro del estribo: útil = disponible − recubrimiento − Ø estribo − holgura.");
        _beamCover = NumBox(_cfg.BeamCoverMm);
        AddRow(grid, r++, "Recubrimiento viga (mm):", _beamCover, "Se usa si la viga no tiene recubrimiento definido en Revit.");
        _beamStirrup = NumBox(_cfg.BeamStirrupDiameterMm);
        AddRow(grid, r++, "Ø estribo viga (mm):", _beamStirrup, "Para la cota de las capas y el reparto en el ancho.");
        _embedFar = new CheckBox { Content = "Llevar las barras hasta la cara lejana del núcleo (si no, solo la longitud requerida)", IsChecked = _cfg.EmbedToFarFace, Margin = Pad };
        AddRow(grid, r++, "", _embedFar, null);
        _extension = new TextBox { Text = _cfg.ExtensionIntoBeamMm.HasValue ? Num(_cfg.ExtensionIntoBeamMm.Value) : "", Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = Pad };
        AddRow(grid, r++, "Extensión en la viga (mm):", _extension, "Longitud de la barra dentro de la viga desde la cara de la columna. Vacío = 2·h + traslape (1.3·ld).");
        _clearance = NumBox(_cfg.HookClearanceMm);
        AddRow(grid, r++, "Holgura gancho–estribo (mm):", _clearance, null);
        _stagger = new CheckBox { Content = "Desplazar la capa cuando las barras de dos vigas se cruzan en el nudo", IsChecked = _cfg.AutoStaggerCrossingLayers, Margin = Pad };
        AddRow(grid, r++, "", _stagger, "Si no, solo se avisa.");
        _rounding = NumBox(_cfg.LengthRoundingMm);
        AddRow(grid, r++, "Redondeo de longitudes (mm):", _rounding, null);
        _highlight = new CheckBox { Content = "Al armar, marcar en rojo en la vista activa las barras existentes que no cumplen", IsChecked = _cfg.HighlightFailingExistingBars, Margin = Pad };
        AddRow(grid, r++, "", _highlight, null);
        group.Content = grid;
        return group;
    }

    private UIElement BuildPreviews()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.25, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var planGroup = new GroupBox { Header = "Planta del nudo, vista desde arriba (rueda: zoom, arrastrar: mover, doble clic: encajar, clic en una viga: su alzado)", Padding = new Thickness(4) };
        var planPanel = new DockPanel();
        _previewCaption = new TextBlock { Foreground = RevitTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(_previewCaption, Dock.Top);
        planPanel.Children.Add(_previewCaption);
        var legend = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        LegendItem(legend, PreviewColors.TopBar, "nueva, capa superior");
        LegendItem(legend, PreviewColors.BottomBar, "nueva, capa inferior (a trazos)");
        LegendItem(legend, PreviewColors.PassThrough, "pasante");
        LegendItem(legend, PreviewColors.Insufficient, "insuficiente ✖");
        LegendItem(legend, PreviewColors.ExistingOk, "existente cumple (a trazos)");
        LegendItem(legend, PreviewColors.ExistingFail, "existente no cumple (a trazos)");
        LegendItem(legend, PreviewColors.Fixed, "corregida");
        legend.Children.Add(new TextBlock { Text = "● = gancho a 90° (vertical)", Margin = new Thickness(0, 0, 12, 0) });
        DockPanel.SetDock(legend, Dock.Bottom);
        planPanel.Children.Add(legend);
        _plan = new PlanPreview { MinHeight = 200 };
        _plan.BeamClicked += b => { _selectedBeam = b; Refresh(); };
        planPanel.Children.Add(new Border { BorderBrush = RevitTheme.Border, BorderThickness = new Thickness(1), Child = _plan });
        planGroup.Content = planPanel;
        Grid.SetRow(planGroup, 0);
        grid.Children.Add(planGroup);

        var elvGroup = new GroupBox { Header = "Alzado de la viga seleccionada (corte por su eje: la viga a la izquierda entra en la columna por la derecha)", Padding = new Thickness(4), Margin = new Thickness(0, 6, 0, 0) };
        _elevation = new ElevationPreview { MinHeight = 160 };
        elvGroup.Content = new Border { BorderBrush = RevitTheme.Border, BorderThickness = new Thickness(1), Child = _elevation };
        Grid.SetRow(elvGroup, 1);
        grid.Children.Add(elvGroup);
        return grid;
    }

    private static void LegendItem(Panel panel, Brush brush, string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
        sp.Children.Add(new System.Windows.Shapes.Rectangle { Width = 12, Height = 12, Fill = brush, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(sp);
    }

    private UIElement BuildButtons()
    {
        var panel = new DockPanel();
        _message = new TextBlock { Foreground = RevitTheme.Error, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(buttons, Dock.Right);

        var save = new Button { Content = "Guardar como valores por defecto", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 4, 0), ToolTip = "Guarda lo elegido en config.json (" + PluginSettings.ConfigPath() + ") para las próximas veces." };
        save.Click += (_, _) =>
        {
            var c = ReadConfig(out var err);
            if (err != null) { _message.Text = err; return; }
            try { c.Save(); _message.Foreground = RevitTheme.Ok; _message.Text = "Guardado en " + PluginSettings.ConfigPath(); }
            catch (Exception ex) { _message.Foreground = RevitTheme.Error; _message.Text = "No se pudo guardar: " + ex.Message; }
        };
        buttons.Children.Add(save);

        _buildButton = new Button { Content = "Armar", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(4, 0, 4, 0), FontWeight = FontWeights.SemiBold, IsDefault = true };
        _buildButton.Click += (_, _) => OnBuild();
        buttons.Children.Add(_buildButton);
        buttons.Children.Add(new Button { Content = "Cancelar", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0), IsCancel = true });

        panel.Children.Add(buttons);
        panel.Children.Add(_message);
        return panel;
    }

    // ------------------------------------------------------------------ controles auxiliares
    private static Grid FormGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private void AddRow(Grid grid, int row, string label, FrameworkElement control, string? tip)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var lb = new TextBlock { Text = label, Margin = Pad, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(lb, row); Grid.SetColumn(lb, 0);
        grid.Children.Add(lb);
        if (tip != null) { control.ToolTip = tip; lb.ToolTip = tip; }
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(control, row); Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        Hook(control);
    }

    private void Hook(FrameworkElement c)
    {
        switch (c)
        {
            case TextBox tb: tb.TextChanged += (_, _) => Refresh(); break;
            case ComboBox cb: cb.SelectionChanged += (_, _) => Refresh(); break;
            case CheckBox ck: ck.Checked += (_, _) => Refresh(); ck.Unchecked += (_, _) => Refresh(); break;
        }
    }

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static TextBox NumBox(double v) => new() { Text = Num(v), Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = Pad };
    private static TextBox CountBox(int v) => new() { Text = v.ToString(CultureInfo.InvariantCulture), Width = 36, Margin = Pad };

    private string TypeDisplay(string name) => _diametersMm.TryGetValue(name, out var mm) ? name + " (" + Num(mm) + " mm)" : name;
    private string HookDisplay(string name) => _hookAngles.TryGetValue(name, out var deg) ? name + " (" + Num(deg) + "°)" : name;

    private ComboBox TypeCombo(string current, bool withGeneral)
    {
        var cb = new ComboBox { Margin = Pad };
        if (withGeneral) cb.Items.Add(General);
        foreach (var n in _barTypes) cb.Items.Add(TypeDisplay(n));
        var match = MatchName(_barTypes, current);
        cb.SelectedIndex = match == null ? (withGeneral ? 0 : -1) : _barTypes.IndexOf(match) + (withGeneral ? 1 : 0);
        return cb;
    }

    private string TypeOf(ComboBox cb, bool withGeneral)
    {
        if (withGeneral && cb.SelectedIndex <= 0) return "";
        return cb.SelectedItem is string d && _typeByDisplay.TryGetValue(d, out var n) ? n : "";
    }

    public static string? MatchName(IList<string> names, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        return names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
               ?? names.FirstOrDefault(n => n.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private double DiameterOf(string typeName)
    {
        var match = MatchName(_barTypes, typeName);
        return match != null && _diametersMm.TryGetValue(match, out var mm) ? mm : 0;
    }

    // ------------------------------------------------------------------ lectura de la configuración
    private PluginSettings ReadConfig(out string? error)
    {
        var errors = new List<string>();
        var c = _cfg.Clone();
        c.CodeKey = AnchorageCodes.All[Math.Max(0, _code.SelectedIndex)].Key;
        c.Fy = ReadNum(_fy, "fy", 1, errors);
        c.Fc = ReadNum(_fc, "f'c", 1, errors);
        c.SeismicJoint = _seismic.IsChecked == true;
        c.LightweightConcrete = _light.IsChecked == true;
        c.EpoxyCoated = _epoxy.IsChecked == true;
        c.TopBarTypeName = TypeOf(_topType, false);
        c.BottomBarTypeName = TypeOf(_botType, false);
        c.TopBarCount = (int)ReadNum(_topN, "barras superiores", 1, errors);
        c.BottomBarCount = (int)ReadNum(_botN, "barras inferiores", 1, errors);
        c.InferBarsFromModel = _infer.IsChecked == true;
        c.ExistingBarsMode = PluginSettings.ModeKey(_mode.SelectedIndex switch { 1 => ExistingBarsAction.VerifyOnly, 2 => ExistingBarsAction.AddNew, _ => ExistingBarsAction.Fix });
        c.HookTypeName = _hook.SelectedIndex >= 0 ? _hookTypes[_hook.SelectedIndex] : "";
        c.ColumnCoverMm = ReadNum(_colCover, "recubrimiento de columna", 0, errors);
        c.ColumnTieDiameterMm = ReadNum(_colTie, "estribo de columna", 0, errors);
        c.BeamCoverMm = ReadNum(_beamCover, "recubrimiento de viga", 0, errors);
        c.BeamStirrupDiameterMm = ReadNum(_beamStirrup, "estribo de viga", 0, errors);
        c.EmbedToFarFace = _embedFar.IsChecked == true;
        c.ExtensionIntoBeamMm = string.IsNullOrWhiteSpace(_extension.Text) ? null : ReadNum(_extension, "extensión en la viga", 0, errors);
        c.HookClearanceMm = ReadNum(_clearance, "holgura", 0, errors);
        c.AutoStaggerCrossingLayers = _stagger.IsChecked == true;
        c.LengthRoundingMm = ReadNum(_rounding, "redondeo", 0, errors);
        c.HighlightFailingExistingBars = _highlight.IsChecked == true;
        c.Normalized();
        error = errors.Count == 0 ? null : string.Join(" | ", errors);
        return c;
    }

    private static double ReadNum(TextBox tb, string label, double min, List<string> errors)
    {
        var text = tb.Text.Trim().Replace(',', '.');
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < min)
        {
            errors.Add(label + ": número no válido" + (min > 0 ? " (mínimo " + Num(min) + ")" : ""));
            tb.BorderBrush = RevitTheme.Error;
            return min;
        }
        tb.ClearValue(Control.BorderBrushProperty);
        return v;
    }

    // ------------------------------------------------------------------ actualización
    private void Refresh()
    {
        if (_building) return;
        var scratch = ReadConfig(out var error);

        var buildable = 0;
        var insufficient = 0;
        var fixes = 0;
        foreach (var item in _items)
        {
            if (!item.CanBuild) continue;
            var analysis = JointSession.Recompute(item, scratch, DiameterOf);
            insufficient += item.InsufficientCount;
            fixes += item.FixCount;
            if (item.HasWork) buildable++;
            if (_itemRuns.TryGetValue(item, out var runs))
            {
                var errors = analysis?.AllDiagnostics.Count(d => d.Severity == Severity.Error) ?? 0;
                var notConnected = analysis?.Joints.Count(j => !j.Connected) ?? 0;
                runs.Kind.Foreground = errors == 0 && item.InsufficientCount == 0 ? RevitTheme.Ok : RevitTheme.Error;
                runs.Detail.Text = item.Describe() + "; " + item.PlanSummary() +
                                   (notConnected > 0 ? $"; {notConnected} viga(s) no conectada(s)" : "") +
                                   (errors > 0 ? $"; {errors} error(es)" : "");
            }
        }

        var anyNew = _items.Where(i => i.CanBuild).SelectMany(i => i.Beams).Any(b => LayerTakesNewBars(b, BarLayer.Top) || LayerTakesNewBars(b, BarLayer.Bottom));
        SetEnabled(new Control[] { _topType, _topN, _botType, _botN, _infer, _extension }, anyNew);
        _barsHint.Text = anyNew
            ? "Tipo y número de barras para las capas que recibirán barras nuevas (las que no tienen barras modeladas)."
            : "Todas las capas ya tienen barras modeladas: se verifican y corrigen las existentes, así que tipo, número y extensión no se usan (bloqueados).";
        var missingTypes = (string.IsNullOrEmpty(scratch.TopBarTypeName) || string.IsNullOrEmpty(scratch.BottomBarTypeName)) &&
                           _items.Any(i => i.NewGroups > 0);
        _buildButton.Content = fixes > 0 ? $"Armar {buildable} columna(s) ({fixes} corrección(es))" : "Armar " + buildable + " columna(s)";
        _buildButton.IsEnabled = buildable > 0 && error == null;

        if (_selected is { CanBuild: true } && _selected.Analysis != null)
        {
            if (_selectedBeam == null || !_selected.Beams.Contains(_selectedBeam)) _selectedBeam = _selected.Beams.FirstOrDefault(b => b.Include);
            _previewCaption.Text = _selected.Tag + _selected.Kind + ", " + _selected.Describe() +
                                   (missingTypes ? "  (sin tipo de barra elegido para las barras nuevas: se usa el del modelo o Ø16 orientativo)" : "");
            _plan.Show(_selected, _selectedBeam);
            _elevation.Show(_selected, _selectedBeam);
        }
        else
        {
            _previewCaption.Text = "";
            _plan.Clear("Sin columna armable");
            _elevation.Clear("");
        }
        RefreshBeams();

        if (error != null) { _message.Foreground = RevitTheme.Error; _message.Text = error; }
        else if (insufficient > 0) { _message.Foreground = RevitTheme.Error; _message.Text = $"{insufficient} capa(s)/barra(s) con anclaje INSUFICIENTE: no se crearán ni corregirán; cambia el diámetro, el número de barras o la columna."; }
        else if (ReferenceEquals(_message.Foreground, RevitTheme.Error)) _message.Text = "";
    }

    private void OnBuild()
    {
        var c = ReadConfig(out var error);
        if (error != null) { _message.Foreground = RevitTheme.Error; _message.Text = error; return; }
        if (string.IsNullOrEmpty(c.HookTypeName) && _items.Any(i => i.ActivePlans.Any(p => p.NeedsHook)))
        {
            _message.Foreground = RevitTheme.Error;
            _message.Text = "Elige el tipo de gancho de 90°: hay barras que se anclan con gancho.";
            _hook.BorderBrush = RevitTheme.Error;
            return;
        }
        Result = c;
        DialogResult = true;
        Close();
    }
}
