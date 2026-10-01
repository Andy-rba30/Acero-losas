using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SlabRebar
{
    /// <summary>
    /// Ventana previa al armado: muestra que se ha detectado en cada losa seleccionada
    /// (espesor, huecos, vigas cercanas, o el motivo del rechazo) y deja elegir el armado:
    /// tipo de losa y direccion (general y por losa), viguetas, barra inferior, bastones,
    /// acero de temperatura, mallas de la losa maciza, recubrimientos y particion, con un
    /// esquema en planta y de la seccion que se redibuja con cada cambio. Los valores
    /// iniciales vienen de config.json y se pueden guardar como nuevos valores por
    /// defecto. Construida en codigo (sin XAML).
    /// </summary>
    public sealed class RebarOptionsWindow : Window
    {
        private readonly AppConfig _cfg;
        private readonly IList<string> _barTypes;
        private readonly IDictionary<string, double> _diametersMm;
        private readonly Dictionary<string, string> _typeByDisplay = new Dictionary<string, string>();
        private readonly IList<string> _hookTypes;
        private readonly IDictionary<string, double> _hookAngles;
        /// <summary>Medidas del gancho de cada par tipo de barra + tipo de gancho (clave HookDims.Key), leidas de Revit.</summary>
        private readonly IDictionary<string, HookDims> _hookDims;
        private readonly IList<HostAnalysis> _items;

        /// <summary>Configuracion final si el usuario pulso "Armar"; null si cancelo.</summary>
        public AppConfig Result { get; private set; }

        // grupos de entradas de cada tipo de losa: solo se muestran los del tipo que se va a armar
        private UIElement _aligGroup, _tempGroup, _macBottomGroup, _macTopGroup;
        // tipo de losa y direccion
        private ComboBox _kind, _dir;
        private TextBox _angle;
        // aligerada
        private TextBox _jw, _js, _ts, _jOff;
        private ComboBox _jbType, _jbCount, _jbHook;
        private TextBox _jbExt;
        private ComboBox _jtMode, _jtType, _jtHook;
        private TextBox _jtIntFrac, _jtEndFrac, _jtFixed, _jtExt;
        private CheckBox _teOn;
        private ComboBox _teType;
        private TextBox _teSp, _teDepth, _teExt;
        // maciza
        private ComboBox _bmType, _bmHook, _bsType;
        private TextBox _bmSp, _bmExt, _bsSp, _bsExt;
        private CheckBox _bsOn, _tsOn;
        private ComboBox _tmMode, _tmType, _tmHook, _tsType;
        private TextBox _tmSp, _tmIntFrac, _tmEndFrac, _tmFixed, _tmExt, _tsSp, _tsExt;
        // general
        private TextBox _coverB, _coverT, _coverE, _partition;
        private CheckBox _beams;
        private TextBlock _message, _partitionPreview, _previewCaption;
        private Button _buildButton;
        private PlanPreview _plan;
        private SectionPreview _section;

        private readonly Dictionary<HostAnalysis, (System.Windows.Documents.Run kind, System.Windows.Documents.Run detail)> _itemRuns
            = new Dictionary<HostAnalysis, (System.Windows.Documents.Run, System.Windows.Documents.Run)>();
        private readonly Dictionary<HostAnalysis, Border> _itemRows = new Dictionary<HostAnalysis, Border>();
        private HostAnalysis _selected;
        private bool _building = true;
        private bool _strictTypes;

        private const string NoHook = "(sin gancho)";
        private static readonly string[] DirModes = { "short", "long", "x", "y", "angle" };
        private static readonly string[] DirLabels = { "lado corto (luz menor)", "lado largo", "X del proyecto", "Y del proyecto", "angulo (grados)" };
        private static readonly string[] TopModes = { "bastones", "corrida", "ninguna" };
        private static readonly string[] TopLabels = { "bastones en los apoyos", "barra corrida", "ninguna" };
        private static readonly Thickness Pad = new Thickness(4, 2, 4, 2);
        private static readonly Brush SelectedBrush = RevitTheme.Selection;

        public RebarOptionsWindow(AppConfig cfg, IList<string> barTypes, IDictionary<string, double> diametersMm,
                                  IList<string> hookTypes, IDictionary<string, double> hookAngles, IDictionary<string, HookDims> hookDims,
                                  IList<HostAnalysis> items)
        {
            _hookAngles = hookAngles ?? new Dictionary<string, double>();
            _hookDims = hookDims ?? new Dictionary<string, HookDims>();
            _cfg = cfg;
            _cfg.Normalize();
            _diametersMm = diametersMm;
            _barTypes = barTypes.OrderBy(n => diametersMm.TryGetValue(n, out double mm) ? mm : 0)
                                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string n in _barTypes) _typeByDisplay[TypeDisplay(n)] = n;
            _hookTypes = hookTypes;
            _items = items;

            Title = "Armar losas";
            Width = 1240;
            Height = 860;
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

        // ------------------------------------------------------------------
        // Construccion de la interfaz
        // ------------------------------------------------------------------
        private UIElement BuildRoot()
        {
            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            UIElement elements = BuildElements();
            Grid.SetRow(elements, 0);
            root.Children.Add(elements);

            var body = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });

            var left = new StackPanel();
            left.Children.Add(BuildKindAndDirection());
            left.Children.Add(_aligGroup = BuildAligerada());
            left.Children.Add(_tempGroup = BuildTemperature());
            left.Children.Add(_macBottomGroup = BuildMacizaBottom());
            left.Children.Add(_macTopGroup = BuildMacizaTop());
            left.Children.Add(BuildGeneral());
            var scroll = new ScrollViewer
            {
                Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(scroll, 0);
            body.Children.Add(scroll);

            UIElement previews = BuildPreviews();
            Grid.SetColumn(previews, 1);
            body.Children.Add(previews);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            UIElement buttons = BuildButtons();
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);
            return root;
        }

        private UIElement BuildElements()
        {
            int ok = _items.Count(i => i.CanBuild);
            var group = new GroupBox
            {
                Header = "Losas seleccionadas: " + _items.Count + " (" + ok + " armables). Haz clic en una para verla en el esquema. " +
                         "A la derecha, el tipo de losa y la direccion propios de cada una (general = lo elegido abajo).",
                Padding = new Thickness(4)
            };
            var panel = new StackPanel();
            foreach (HostAnalysis item in _items)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                text.Inlines.Add(new System.Windows.Documents.Run(item.Tag) { FontWeight = FontWeights.Bold });
                var kindRun = new System.Windows.Documents.Run(item.Kind(_cfg) + ": ")
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = item.CanBuild ? RevitTheme.Ok : RevitTheme.Error
                };
                var detailRun = new System.Windows.Documents.Run(item.Detail(_cfg));
                text.Inlines.Add(kindRun);
                text.Inlines.Add(detailRun);
                _itemRuns[item] = (kindRun, detailRun);
                Grid.SetColumn(text, 0);
                row.Children.Add(text);

                if (item.CanBuild)
                {
                    var side = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    HostAnalysis captured = item;

                    side.Children.Add(new TextBlock { Text = "Tipo:", Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                    var kind = new ComboBox
                    {
                        Width = 110, ToolTip = "Tipo de losa de este elemento. General = el tipo por defecto elegido abajo (que puede deducirse del nombre del tipo de suelo)."
                    };
                    kind.Items.Add("(general)"); kind.Items.Add("aligerada"); kind.Items.Add("maciza");
                    kind.SelectedIndex = item.KindOverride == "aligerada" ? 1 : item.KindOverride == "maciza" ? 2 : 0;
                    kind.SelectionChanged += (s, e) => { captured.KindOverride = kind.SelectedIndex == 1 ? "aligerada" : kind.SelectedIndex == 2 ? "maciza" : ""; Refresh(); };
                    side.Children.Add(kind);

                    side.Children.Add(new TextBlock { Text = "Direccion:", Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                    var dir = new ComboBox
                    {
                        Width = 160, ToolTip = "Direccion de las viguetas (aligerada) o de las barras principales (maciza) de este elemento. General = la elegida abajo."
                    };
                    dir.Items.Add("(general)");
                    for (int i = 0; i < 4; i++) dir.Items.Add(DirLabels[i]);
                    int di = Array.IndexOf(DirModes, item.DirectionOverride);
                    dir.SelectedIndex = di >= 0 && di < 4 ? di + 1 : 0;
                    dir.SelectionChanged += (s, e) => { captured.DirectionOverride = dir.SelectedIndex <= 0 ? "" : DirModes[dir.SelectedIndex - 1]; Refresh(); };
                    side.Children.Add(dir);

                    Grid.SetColumn(side, 1);
                    row.Children.Add(side);
                }

                var border = new Border { Child = row, Padding = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(3) };
                if (item.CanBuild)
                {
                    border.Cursor = Cursors.Hand;
                    HostAnalysis captured = item;
                    border.MouseLeftButtonDown += (s, e) => { SelectItem(captured); Refresh(); };
                }
                _itemRows[item] = border;
                panel.Children.Add(border);
            }
            group.Content = new ScrollViewer
            {
                Content = panel, MaxHeight = 150,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return group;
        }

        private void SelectItem(HostAnalysis item)
        {
            _selected = item;
            foreach (var kv in _itemRows)
                kv.Value.Background = kv.Key == item ? SelectedBrush : Brushes.Transparent;
        }

        private UIElement BuildKindAndDirection()
        {
            var group = new GroupBox { Header = "Tipo de losa y direccion de las barras", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            _kind = new ComboBox { Margin = Pad };
            _kind.Items.Add("por el nombre del tipo (contiene \"aliger\" = aligerada)");
            _kind.Items.Add("aligerada (viguetas + ladrillo + losa superior)");
            _kind.Items.Add("maciza (mallas)");
            _kind.SelectedIndex = _cfg.KindAligerada ? 1 : _cfg.KindMaciza ? 2 : 0;
            AddRow(grid, r++, "Tipo por defecto:", _kind,
                   "Tipo de losa que se arma cuando la fila de la losa dice \"general\". Con la primera opcion se mira el nombre del tipo de suelo.");
            _dir = new ComboBox { Margin = Pad };
            foreach (string l in DirLabels) _dir.Items.Add(l);
            _dir.SelectedIndex = Math.Max(0, Array.IndexOf(DirModes, _cfg.Direction.Mode));
            AddRow(grid, r++, "Direccion (u):", _dir,
                   "Direccion de las viguetas (aligerada) o de las barras principales (maciza): paralelas al lado corto de la losa (lo normal: las " +
                   "viguetas cubren la luz menor), al lado largo, a los ejes X o Y del proyecto, o un angulo. La direccion perpendicular (v) es la " +
                   "del acero de temperatura o de la malla secundaria. Cambiable losa a losa en la lista de arriba.");
            _angle = NumBox(_cfg.Direction.AngleDeg);
            AddRow(grid, r++, "Angulo (grados):", _angle, "Angulo de la direccion u respecto al eje X del proyecto, solo con la opcion \"angulo\".");
            group.Content = grid;
            return group;
        }

        private UIElement BuildAligerada()
        {
            AligeradaCfg a = _cfg.Aligerada;
            var group = new GroupBox { Header = "Losa aligerada: viguetas, barra inferior y bastones", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            var geo = new StackPanel { Orientation = Orientation.Horizontal };
            _jw = NumBox(a.JoistWidthMm); _js = NumBox(a.JoistSpacingMm); _ts = NumBox(a.TopSlabMm);
            geo.Children.Add(_jw);
            geo.Children.Add(new TextBlock { Text = "ancho,", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            geo.Children.Add(_js);
            geo.Children.Add(new TextBlock { Text = "entre ejes,", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            geo.Children.Add(_ts);
            geo.Children.Add(new TextBlock { Text = "losa sup.", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            Hook(_jw); Hook(_js); Hook(_ts);
            AddRow(grid, r++, "Viguetas (mm):", geo,
                   "Ancho de la vigueta (100), separacion entre ejes (400 con ladrillo de 300) y espesor de la losa superior sobre el ladrillo (50). " +
                   "Las viguetas van a lo largo de u, repartidas en v.");
            _jOff = NumBox(a.FirstJoistOffsetMm);
            AddRow(grid, r++, "Primera vigueta (mm):", _jOff, "Distancia del borde de la losa al eje de la primera vigueta. 0 = reparto centrado en la losa.");

            _jbType = TypeCombo(a.Bottom.BarTypeName);
            AddRow(grid, r++, "Barra inferior:", _jbType, "Tipo de barra de la barra inferior de cada vigueta (momento positivo).");
            var jb = new StackPanel { Orientation = Orientation.Horizontal };
            _jbCount = new ComboBox { Margin = Pad, Width = 120 };
            _jbCount.Items.Add("1 por vigueta"); _jbCount.Items.Add("2 por vigueta");
            _jbCount.SelectedIndex = a.Bottom.Count >= 2 ? 1 : 0;
            Hook(_jbCount);
            jb.Children.Add(_jbCount);
            jb.Children.Add(new TextBlock { Text = "prolongacion (mm):", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _jbExt = NumBox(a.Bottom.ExtensionMm); Hook(_jbExt);
            jb.Children.Add(_jbExt);
            AddRow(grid, r++, "Inferiores:", jb,
                   "Numero de barras inferiores por vigueta (2 van lado a lado) y cuanto sobresalen del borde exterior de la losa hacia la viga " +
                   "(anclaje). 0 = paran al recubrimiento lateral. En los huecos siempre paran al recubrimiento.");
            _jbHook = HookCombo(a.Bottom.HookTypeName);
            AddRow(grid, r++, "Gancho inferior:", _jbHook, "Tipo de gancho en los extremos exteriores de la barra inferior (dobla hacia arriba), con o sin prolongacion: sin prolongacion el gancho queda dentro de la losa y su cara exterior guarda el recubrimiento. " +
                   "Su longitud la fija el tipo de barra (Editar tipo > Longitudes de gancho): si no cabe en el espesor, el esquema avisa. Sin gancho = recta.");

            _jtMode = new ComboBox { Margin = Pad };
            foreach (string l in TopLabels) _jtMode.Items.Add(l);
            _jtMode.SelectedIndex = Math.Max(0, Array.IndexOf(TopModes, a.Top.Mode));
            AddRow(grid, r++, "Barras superiores:", _jtMode,
                   "Bastones en los apoyos (momento negativo): en cada viga detectada que cruza la vigueta y en los extremos exteriores. Barra corrida = de " +
                   "extremo a extremo. Ninguna = sin acero superior en las viguetas.");
            _jtType = TypeCombo(a.Top.BarTypeName);
            AddRow(grid, r++, "Tipo de barra:", _jtType, "Tipo de barra de los bastones o de la barra superior corrida.");
            var fr = new StackPanel { Orientation = Orientation.Horizontal };
            _jtIntFrac = NumBox(a.Top.InteriorFraction); _jtEndFrac = NumBox(a.Top.EndFraction); _jtFixed = NumBox(a.Top.FixedLengthMm);
            Hook(_jtIntFrac); Hook(_jtEndFrac); Hook(_jtFixed);
            fr.Children.Add(_jtIntFrac);
            fr.Children.Add(new TextBlock { Text = "x luz en interiores,", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            fr.Children.Add(_jtEndFrac);
            fr.Children.Add(new TextBlock { Text = "en extremos, o fija (mm):", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            fr.Children.Add(_jtFixed);
            AddRow(grid, r++, "Longitud del baston:", fr,
                   "Longitud del baston medida desde la cara del apoyo, como fraccion de la luz libre adyacente: 0.25 = L/4 a cada lado de un apoyo " +
                   "interior, 0.2 = L/5 desde un apoyo extremo. Una longitud fija mayor que 0 anula las fracciones. Los bastones que se solapan se unen.");
            var jtx = new StackPanel { Orientation = Orientation.Horizontal };
            _jtExt = NumBox(a.Top.ExtensionMm); Hook(_jtExt);
            jtx.Children.Add(_jtExt);
            jtx.Children.Add(new TextBlock { Text = "gancho:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _jtHook = HookCombo(a.Top.HookTypeName); _jtHook.Width = 200; Hook(_jtHook);
            jtx.Children.Add(_jtHook);
            AddRow(grid, r++, "Prolongacion sup. (mm):", jtx,
                   "Cuanto sobresalen los bastones extremos (o la barra corrida) del borde exterior hacia la viga, y tipo de gancho en ese extremo (dobla hacia abajo).");
            group.Content = grid;
            return group;
        }

        private UIElement BuildTemperature()
        {
            TemperatureCfg t = _cfg.Aligerada.Temperature;
            var group = new GroupBox { Header = "Acero de temperatura (losa aligerada, perpendicular a las viguetas)", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            _teOn = new CheckBox { Content = "Colocar acero de temperatura en la losa superior", IsChecked = t.Enabled, Margin = Pad };
            AddRow(grid, r++, "", _teOn, "Barras a lo largo de v (perpendiculares a las viguetas) en la losa superior, repartidas en u.");
            _teType = TypeCombo(t.BarTypeName);
            AddRow(grid, r++, "Tipo de barra:", _teType, "Tipo de barra del acero de temperatura (normalmente 1/4\" o 6 mm).");
            _teSp = NumBox(t.SpacingMm);
            AddRow(grid, r++, "Separacion (mm):", _teSp, "Separacion maxima entre barras de temperatura (250 es lo habitual); se reparten por igual sin superarla.");
            _teDepth = NumBox(t.DepthMm);
            AddRow(grid, r++, "Profundidad del eje (mm):", _teDepth,
                   "Distancia de la cara superior al eje de la barra de temperatura. 0 = justo por debajo de los bastones (recubrimiento superior + diametro " +
                   "del baston + medio diametro), para que se crucen sin chocar. Se avisa si queda por debajo de la losa superior.");
            _teExt = NumBox(t.ExtensionMm);
            AddRow(grid, r++, "Prolongacion (mm):", _teExt, "Cuanto sobresale del borde exterior de la losa hacia la viga. 0 = para al recubrimiento lateral.");
            group.Content = grid;
            return group;
        }

        private UIElement BuildMacizaBottom()
        {
            MacizaCfg m = _cfg.Maciza;
            var group = new GroupBox { Header = "Losa maciza: malla inferior", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            _bmType = TypeCombo(m.BottomMain.BarTypeName);
            AddRow(grid, r++, "Principal (u), tipo:", _bmType, "Barras inferiores en la direccion principal u (la capa mas baja).");
            var bm = new StackPanel { Orientation = Orientation.Horizontal };
            _bmSp = NumBox(m.BottomMain.SpacingMm); Hook(_bmSp);
            bm.Children.Add(_bmSp);
            bm.Children.Add(new TextBlock { Text = "separacion, prolongacion:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _bmExt = NumBox(m.BottomMain.ExtensionMm); Hook(_bmExt);
            bm.Children.Add(_bmExt);
            bm.Children.Add(new TextBlock { Text = "gancho:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _bmHook = HookCombo(m.BottomMain.HookTypeName); _bmHook.Width = 160; Hook(_bmHook);
            bm.Children.Add(_bmHook);
            AddRow(grid, r++, "Principal (mm):", bm,
                   "Separacion maxima entre barras (se reparten por igual sin superarla), prolongacion mas alla del borde exterior hacia la viga " +
                   "(0 = paran al recubrimiento) y gancho en los extremos exteriores (dobla hacia arriba).");

            _bsOn = new CheckBox { Content = "Colocar secundaria (v): temperatura / reparticion, encima de la principal", IsChecked = m.BottomSecondary.Enabled, Margin = Pad };
            AddRow(grid, r++, "", _bsOn, "Barras inferiores perpendiculares, apoyadas sobre la capa principal. En una losa en dos direcciones es la otra armadura principal.");
            _bsType = TypeCombo(m.BottomSecondary.BarTypeName);
            AddRow(grid, r++, "Secundaria, tipo:", _bsType, "Tipo de barra de la capa inferior secundaria.");
            var bs = new StackPanel { Orientation = Orientation.Horizontal };
            _bsSp = NumBox(m.BottomSecondary.SpacingMm); Hook(_bsSp);
            bs.Children.Add(_bsSp);
            bs.Children.Add(new TextBlock { Text = "separacion, prolongacion:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _bsExt = NumBox(m.BottomSecondary.ExtensionMm); Hook(_bsExt);
            bs.Children.Add(_bsExt);
            AddRow(grid, r++, "Secundaria (mm):", bs, "Separacion maxima y prolongacion mas alla del borde exterior de la capa inferior secundaria.");
            group.Content = grid;
            return group;
        }

        private UIElement BuildMacizaTop()
        {
            MacizaCfg m = _cfg.Maciza;
            var group = new GroupBox { Header = "Losa maciza: malla superior", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            _tmMode = new ComboBox { Margin = Pad };
            foreach (string l in TopLabels) _tmMode.Items.Add(l);
            _tmMode.SelectedIndex = Math.Max(0, Array.IndexOf(TopModes, m.TopMain.Mode));
            AddRow(grid, r++, "Principal (u):", _tmMode,
                   "Barras superiores en la direccion principal: bastones en los apoyos (vigas detectadas y extremos exteriores), corridas o ninguna.");
            _tmType = TypeCombo(m.TopMain.BarTypeName);
            AddRow(grid, r++, "Principal, tipo:", _tmType, "Tipo de barra de la capa superior principal.");
            var tm = new StackPanel { Orientation = Orientation.Horizontal };
            _tmSp = NumBox(m.TopMain.SpacingMm); Hook(_tmSp);
            tm.Children.Add(_tmSp);
            tm.Children.Add(new TextBlock { Text = "separacion, prolongacion:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _tmExt = NumBox(m.TopMain.ExtensionMm); Hook(_tmExt);
            tm.Children.Add(_tmExt);
            tm.Children.Add(new TextBlock { Text = "gancho:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _tmHook = HookCombo(m.TopMain.HookTypeName); _tmHook.Width = 160; Hook(_tmHook);
            tm.Children.Add(_tmHook);
            AddRow(grid, r++, "Principal (mm):", tm, "Separacion maxima, prolongacion mas alla del borde exterior y gancho en los extremos exteriores (dobla hacia abajo).");
            var fr = new StackPanel { Orientation = Orientation.Horizontal };
            _tmIntFrac = NumBox(m.TopMain.InteriorFraction); _tmEndFrac = NumBox(m.TopMain.EndFraction); _tmFixed = NumBox(m.TopMain.FixedLengthMm);
            Hook(_tmIntFrac); Hook(_tmEndFrac); Hook(_tmFixed);
            fr.Children.Add(_tmIntFrac);
            fr.Children.Add(new TextBlock { Text = "x luz en interiores,", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            fr.Children.Add(_tmEndFrac);
            fr.Children.Add(new TextBlock { Text = "en extremos, o fija (mm):", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            fr.Children.Add(_tmFixed);
            AddRow(grid, r++, "Longitud del baston:", fr, "Igual que en la aligerada: fraccion de la luz libre a cada lado del apoyo, o longitud fija desde la cara del apoyo.");

            _tsOn = new CheckBox { Content = "Colocar secundaria (v) corrida, debajo de la principal", IsChecked = m.TopSecondary.Enabled, Margin = Pad };
            AddRow(grid, r++, "", _tsOn, "Barras superiores perpendiculares, corridas, colgadas bajo la capa superior principal.");
            _tsType = TypeCombo(m.TopSecondary.BarTypeName);
            AddRow(grid, r++, "Secundaria, tipo:", _tsType, "Tipo de barra de la capa superior secundaria.");
            var ts = new StackPanel { Orientation = Orientation.Horizontal };
            _tsSp = NumBox(m.TopSecondary.SpacingMm); Hook(_tsSp);
            ts.Children.Add(_tsSp);
            ts.Children.Add(new TextBlock { Text = "separacion, prolongacion:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _tsExt = NumBox(m.TopSecondary.ExtensionMm); Hook(_tsExt);
            ts.Children.Add(_tsExt);
            AddRow(grid, r++, "Secundaria (mm):", ts, "Separacion maxima y prolongacion mas alla del borde exterior de la capa superior secundaria.");
            group.Content = grid;
            return group;
        }

        private UIElement BuildGeneral()
        {
            var group = new GroupBox { Header = "Recubrimientos, apoyos y particion", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            var cov = new StackPanel { Orientation = Orientation.Horizontal };
            _coverB = NumBox(_cfg.CoverBottomMm); _coverT = NumBox(_cfg.CoverTopMm); _coverE = NumBox(_cfg.CoverEdgeMm);
            Hook(_coverB); Hook(_coverT); Hook(_coverE);
            cov.Children.Add(_coverB);
            cov.Children.Add(new TextBlock { Text = "inferior,", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            cov.Children.Add(_coverT);
            cov.Children.Add(new TextBlock { Text = "superior,", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            cov.Children.Add(_coverE);
            cov.Children.Add(new TextBlock { Text = "bordes y huecos", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            AddRow(grid, r++, "Recubrimientos (mm):", cov,
                   "Distancia de la cara inferior, de la cara superior y de los bordes (exteriores y de huecos) a la cara de la barra mas proxima.");
            _beams = new CheckBox { Content = "Usar las vigas que cruzan la losa como apoyos de los bastones", IsChecked = _cfg.DetectBeams, Margin = Pad };
            AddRow(grid, r++, "", _beams,
                   "Las vigas (armazon estructural) a la cota de la losa casi perpendiculares a u son apoyos: sobre cada una van bastones a los dos " +
                   "lados (L/4) y en las de borde el baston extremo sale de su cara. Su hormigon tambien cuenta como valido al comprobar las barras. " +
                   "Sin la casilla, los unicos apoyos son los extremos de la losa.");
            _partition = new TextBox { Text = _cfg.PartitionTemplate, Margin = Pad };
            AddRow(grid, r++, "Particion:", _partition, "Plantilla del parametro Particion de cada barra. Comodines: " + PartitionName.Help);
            _partitionPreview = new TextBlock { Foreground = RevitTheme.Muted, Margin = Pad, TextWrapping = TextWrapping.Wrap };
            AddRow(grid, r++, "", _partitionPreview, null);
            group.Content = grid;
            return group;
        }

        private UIElement BuildPreviews()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.6, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var planGroup = new GroupBox { Header = "Planta (rueda: zoom, arrastrar: mover, doble clic: encajar)", Padding = new Thickness(4) };
            var planPanel = new DockPanel();
            _previewCaption = new TextBlock { Foreground = RevitTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(_previewCaption, Dock.Top);
            planPanel.Children.Add(_previewCaption);
            var legend = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            LegendItem(legend, PlanColors.Bottom, "inferior (vigueta / principal)");
            LegendItem(legend, PlanColors.Top, "bastones / superior principal");
            LegendItem(legend, PlanColors.Temperature, "temperatura / inferior secundaria");
            LegendItem(legend, PlanColors.TopSecondary, "superior secundaria");
            LegendItem(legend, PlanColors.JoistEdge, "vigueta");
            LegendItem(legend, PlanColors.SupportEdge, "viga de apoyo");
            LegendItem(legend, PlanColors.Brick, "ladrillo (seccion)");
            DockPanel.SetDock(legend, Dock.Bottom);
            planPanel.Children.Add(legend);
            _plan = new PlanPreview { MinHeight = 220 };
            planPanel.Children.Add(new Border { BorderBrush = RevitTheme.Border, BorderThickness = new Thickness(1), Child = _plan });
            planGroup.Content = planPanel;
            Grid.SetRow(planGroup, 0);
            grid.Children.Add(planGroup);

            var secGroup = new GroupBox { Header = "Seccion transversal a media luz (rueda: zoom, arrastrar: mover, doble clic: encajar)", Padding = new Thickness(4), Margin = new Thickness(0, 6, 0, 0) };
            _section = new SectionPreview { MinHeight = 140 };
            secGroup.Content = new Border { BorderBrush = RevitTheme.Border, BorderThickness = new Thickness(1), Child = _section };
            Grid.SetRow(secGroup, 1);
            grid.Children.Add(secGroup);
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

            var save = new Button { Content = "Guardar como valores por defecto", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 4, 0) };
            save.ToolTip = "Guarda lo elegido en config.json (" + AppConfig.ConfigPath() + ") para las proximas veces.";
            save.Click += (s, e) =>
            {
                AppConfig c = ReadConfig(out string err);
                if (err != null) { _message.Text = err; return; }
                try { c.Save(); _message.Foreground = RevitTheme.Ok; _message.Text = "Guardado en " + AppConfig.ConfigPath(); }
                catch (Exception ex) { _message.Foreground = RevitTheme.Error; _message.Text = "No se pudo guardar: " + ex.Message; }
            };
            buttons.Children.Add(save);

            _buildButton = new Button { Content = "Armar", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(4, 0, 4, 0), FontWeight = FontWeights.SemiBold, IsDefault = true };
            _buildButton.Click += (s, e) => OnBuild();
            buttons.Children.Add(_buildButton);

            var cancel = new Button { Content = "Cancelar", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(cancel);

            panel.Children.Add(buttons);
            panel.Children.Add(_message);
            return panel;
        }

        // ------------------------------------------------------------------
        // Controles auxiliares
        // ------------------------------------------------------------------
        private static Grid FormGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private void AddRow(Grid grid, int row, string label, FrameworkElement control, string tip)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lb = new TextBlock { Text = label, Margin = Pad, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(lb, row); Grid.SetColumn(lb, 0);
            grid.Children.Add(lb);
            if (tip != null) { control.ToolTip = tip; lb.ToolTip = tip; }
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(control, row); Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            Hook(control);
        }

        private readonly HashSet<FrameworkElement> _hooked = new HashSet<FrameworkElement>();

        private void Hook(FrameworkElement c)
        {
            if (!_hooked.Add(c)) return;   // cada control se engancha una sola vez
            if (c is TextBox tb) tb.TextChanged += (s, e) => Refresh();
            else if (c is ComboBox cb) cb.SelectionChanged += (s, e) => Refresh();
            else if (c is CheckBox ck) { ck.Checked += (s, e) => Refresh(); ck.Unchecked += (s, e) => Refresh(); }
        }

        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static TextBox NumBox(double v) => new TextBox { Text = Num(v), Width = 70, HorizontalAlignment = HorizontalAlignment.Left, Margin = Pad };

        private string TypeDisplay(string name) =>
            _diametersMm.TryGetValue(name, out double mm) ? name + " (" + Num(mm) + " mm)" : name;

        private ComboBox TypeCombo(string current)
        {
            var cb = new ComboBox { Margin = Pad };
            foreach (string n in _barTypes) cb.Items.Add(TypeDisplay(n));
            string match = RebarGenerator.MatchName(_barTypes, current);
            cb.SelectedIndex = match == null ? -1 : _barTypes.IndexOf(match);
            return cb;
        }

        private string TypeOf(ComboBox cb) =>
            cb.SelectedItem is string d && _typeByDisplay.TryGetValue(d, out string n) ? n : "";

        private ComboBox HookCombo(string current)
        {
            var cb = new ComboBox { Margin = Pad };
            cb.Items.Add(NoHook);
            foreach (string n in _hookTypes) cb.Items.Add(n);
            string match = RebarGenerator.MatchName(_hookTypes, current);
            cb.SelectedIndex = match == null ? 0 : _hookTypes.IndexOf(match) + 1;
            return cb;
        }

        private static string HookOf(ComboBox cb) => cb.SelectedIndex <= 0 ? "" : (string)cb.SelectedItem;

        private double DiameterFt(string typeName)
        {
            string match = RebarGenerator.MatchName(_barTypes, typeName);
            return match != null && _diametersMm.TryGetValue(match, out double mm) ? SlabPlan.Mm(mm) : 0;
        }

        // ------------------------------------------------------------------
        // Lectura de la configuracion desde los controles
        // ------------------------------------------------------------------
        private AppConfig ReadConfig(out string error)
        {
            var errors = new List<string>();
            AppConfig c = _cfg.Clone();

            c.Kind = _kind.SelectedIndex == 1 ? "aligerada" : _kind.SelectedIndex == 2 ? "maciza" : "auto";
            c.Direction.Mode = DirModes[Math.Max(0, _dir.SelectedIndex)];
            c.Direction.AngleDeg = ReadNum(_angle, "angulo", -360, errors);

            AligeradaCfg a = c.Aligerada;
            a.JoistWidthMm = ReadNum(_jw, "ancho de vigueta", 1, errors);
            a.JoistSpacingMm = ReadNum(_js, "separacion de viguetas", 1, errors);
            a.TopSlabMm = ReadNum(_ts, "losa superior", 1, errors);
            a.FirstJoistOffsetMm = ReadNum(_jOff, "primera vigueta", 0, errors);
            if (a.JoistSpacingMm <= a.JoistWidthMm) errors.Add("la separacion de viguetas tiene que ser mayor que su ancho");
            a.Bottom.BarTypeName = TypeOf(_jbType);
            a.Bottom.Count = _jbCount.SelectedIndex == 1 ? 2 : 1;
            a.Bottom.ExtensionMm = ReadNum(_jbExt, "prolongacion inferior", 0, errors);
            a.Bottom.HookTypeName = HookOf(_jbHook);
            a.Top.Mode = TopModes[Math.Max(0, _jtMode.SelectedIndex)];
            a.Top.BarTypeName = TypeOf(_jtType);
            a.Top.InteriorFraction = ReadNum(_jtIntFrac, "fraccion en apoyos interiores", 0.01, errors);
            a.Top.EndFraction = ReadNum(_jtEndFrac, "fraccion en apoyos extremos", 0.01, errors);
            a.Top.FixedLengthMm = ReadNum(_jtFixed, "longitud fija del baston", 0, errors);
            a.Top.ExtensionMm = ReadNum(_jtExt, "prolongacion superior", 0, errors);
            a.Top.HookTypeName = HookOf(_jtHook);
            a.Temperature.Enabled = _teOn.IsChecked == true;
            a.Temperature.BarTypeName = TypeOf(_teType);
            a.Temperature.SpacingMm = ReadNum(_teSp, "separacion de temperatura", 1, errors);
            a.Temperature.DepthMm = ReadNum(_teDepth, "profundidad de temperatura", 0, errors);
            a.Temperature.ExtensionMm = ReadNum(_teExt, "prolongacion de temperatura", 0, errors);

            MacizaCfg m = c.Maciza;
            m.BottomMain.BarTypeName = TypeOf(_bmType);
            m.BottomMain.SpacingMm = ReadNum(_bmSp, "separacion inferior principal", 1, errors);
            m.BottomMain.ExtensionMm = ReadNum(_bmExt, "prolongacion inferior principal", 0, errors);
            m.BottomMain.HookTypeName = HookOf(_bmHook);
            m.BottomSecondary.Enabled = _bsOn.IsChecked == true;
            m.BottomSecondary.BarTypeName = TypeOf(_bsType);
            m.BottomSecondary.SpacingMm = ReadNum(_bsSp, "separacion inferior secundaria", 1, errors);
            m.BottomSecondary.ExtensionMm = ReadNum(_bsExt, "prolongacion inferior secundaria", 0, errors);
            m.TopMain.Mode = TopModes[Math.Max(0, _tmMode.SelectedIndex)];
            m.TopMain.BarTypeName = TypeOf(_tmType);
            m.TopMain.SpacingMm = ReadNum(_tmSp, "separacion superior principal", 1, errors);
            m.TopMain.ExtensionMm = ReadNum(_tmExt, "prolongacion superior principal", 0, errors);
            m.TopMain.HookTypeName = HookOf(_tmHook);
            m.TopMain.InteriorFraction = ReadNum(_tmIntFrac, "fraccion en apoyos interiores (maciza)", 0.01, errors);
            m.TopMain.EndFraction = ReadNum(_tmEndFrac, "fraccion en apoyos extremos (maciza)", 0.01, errors);
            m.TopMain.FixedLengthMm = ReadNum(_tmFixed, "longitud fija del baston (maciza)", 0, errors);
            m.TopSecondary.Enabled = _tsOn.IsChecked == true;
            m.TopSecondary.BarTypeName = TypeOf(_tsType);
            m.TopSecondary.SpacingMm = ReadNum(_tsSp, "separacion superior secundaria", 1, errors);
            m.TopSecondary.ExtensionMm = ReadNum(_tsExt, "prolongacion superior secundaria", 0, errors);

            c.CoverBottomMm = ReadNum(_coverB, "recubrimiento inferior", 0, errors);
            c.CoverTopMm = ReadNum(_coverT, "recubrimiento superior", 0, errors);
            c.CoverEdgeMm = ReadNum(_coverE, "recubrimiento de bordes", 0, errors);
            c.DetectBeams = _beams.IsChecked == true;
            c.PartitionTemplate = _partition.Text.Trim();
            c.Normalize();

            error = errors.Count == 0 ? null : string.Join(" | ", errors);
            return c;
        }

        private static bool TryNumber(string s, out double v)
        {
            s = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static double ReadNum(TextBox tb, string label, double min, List<string> errors)
        {
            if (!TryNumber(tb.Text, out double v) || v < min)
            {
                errors.Add(label + ": numero no valido" + (min > 0 ? " (minimo " + Num(min) + ")" : ""));
                tb.BorderBrush = RevitTheme.Error;
                return Math.Max(min, 0);
            }
            tb.ClearValue(Control.BorderBrushProperty);
            return v;
        }

        /// <summary>
        /// Diametros de cada capa con esta configuracion (sin tipo elegido, uno orientativo para
        /// poder ver el esquema) y medidas del gancho de las capas que lo llevan, para que el
        /// plan avise ya en la ventana si el gancho no cabe en el espesor.
        /// </summary>
        private PlanDiameters Diameters(AppConfig c, out bool allChosen)
        {
            allChosen = true;
            var d = new PlanDiameters();
            foreach ((BarLayer layer, LayerCfg lc, double fallbackMm) in new[]
            {
                (BarLayer.JoistBottom, (LayerCfg)c.Aligerada.Bottom, 12.7), (BarLayer.JoistTop, c.Aligerada.Top, 12.7), (BarLayer.Temperature, c.Aligerada.Temperature, 6.4),
                (BarLayer.BottomMain, c.Maciza.BottomMain, 9.5), (BarLayer.BottomSecondary, c.Maciza.BottomSecondary, 9.5),
                (BarLayer.TopMain, c.Maciza.TopMain, 9.5), (BarLayer.TopSecondary, c.Maciza.TopSecondary, 9.5)
            })
            {
                double ft = DiameterFt(lc.BarTypeName);
                if (ft <= 0) { ft = SlabPlan.Mm(fallbackMm); allChosen = false; }
                RebarGenerator.SetDiameter(d, layer, ft);
                if (!string.IsNullOrEmpty(lc.HookTypeName))
                {
                    string bar = RebarGenerator.MatchName(_barTypes, lc.BarTypeName);
                    string hk = RebarGenerator.MatchName(_hookTypes, lc.HookTypeName);
                    if (bar != null && hk != null && _hookDims.TryGetValue(HookDims.Key(bar, hk), out HookDims hd)) d.Hooks[layer] = hd;
                }
            }
            return d;
        }

        /// <summary>Tipos de barra que faltan para las capas que se van a colocar en alguna losa armable.</summary>
        private List<string> MissingTypes(AppConfig c)
        {
            var missing = new List<string>();
            var kinds = _items.Where(i => i.CanBuild).Select(i => i.KindFor(c)).Distinct().ToList();
            foreach (SlabKind kind in kinds)
                foreach ((BarLayer layer, LayerCfg lc) in RebarGenerator.LayersFor(kind, c))
                    if (string.IsNullOrEmpty(lc.BarTypeName) && !missing.Contains(Layers.Name(layer))) missing.Add(Layers.Name(layer));
            return missing;
        }

        private void MarkTypes(AppConfig c)
        {
            var kinds = _items.Where(i => i.CanBuild).Select(i => i.KindFor(c)).Distinct().ToList();
            var needed = new HashSet<BarLayer>();
            foreach (SlabKind kind in kinds)
                foreach ((BarLayer layer, LayerCfg _) in RebarGenerator.LayersFor(kind, c)) needed.Add(layer);
            foreach ((ComboBox cb, BarLayer layer) in new[]
            {
                (_jbType, BarLayer.JoistBottom), (_jtType, BarLayer.JoistTop), (_teType, BarLayer.Temperature),
                (_bmType, BarLayer.BottomMain), (_bsType, BarLayer.BottomSecondary), (_tmType, BarLayer.TopMain), (_tsType, BarLayer.TopSecondary)
            })
            {
                if (_strictTypes && needed.Contains(layer) && cb.SelectedIndex < 0) { cb.BorderBrush = RevitTheme.Error; cb.BorderThickness = new Thickness(2); }
                else { cb.ClearValue(Control.BorderBrushProperty); cb.ClearValue(Control.BorderThicknessProperty); }
            }
        }

        // ------------------------------------------------------------------
        // Actualizacion
        // ------------------------------------------------------------------
        private void Refresh()
        {
            if (_building) return;
            AppConfig scratch = ReadConfig(out string error);
            MarkTypes(scratch);

            // solo se muestran las entradas del tipo de losa que se va a armar (el general, el propio
            // de cada losa o el deducido por el nombre); sin losa armable, las del tipo por defecto
            var kinds = new HashSet<SlabKind>(_items.Where(i => i.CanBuild).Select(i => i.KindFor(scratch)));
            if (kinds.Count == 0)
            {
                if (!scratch.KindMaciza) kinds.Add(SlabKind.Aligerada);
                if (!scratch.KindAligerada) kinds.Add(SlabKind.Maciza);
            }
            Visibility alig = kinds.Contains(SlabKind.Aligerada) ? Visibility.Visible : Visibility.Collapsed;
            Visibility mac = kinds.Contains(SlabKind.Maciza) ? Visibility.Visible : Visibility.Collapsed;
            _aligGroup.Visibility = alig; _tempGroup.Visibility = alig;
            _macBottomGroup.Visibility = mac; _macTopGroup.Visibility = mac;

            // controles que dependen de otros
            _angle.IsEnabled = scratch.Direction.Mode == "angle";
            bool topJ = !scratch.Aligerada.Top.None;
            foreach (FrameworkElement fe in new FrameworkElement[] { _jtType, _jtIntFrac, _jtEndFrac, _jtFixed, _jtExt, _jtHook }) fe.IsEnabled = topJ;
            foreach (FrameworkElement fe in new FrameworkElement[] { _jtIntFrac, _jtEndFrac, _jtFixed }) fe.IsEnabled = topJ && !scratch.Aligerada.Top.Continuous;
            bool te = scratch.Aligerada.Temperature.Enabled;
            foreach (FrameworkElement fe in new FrameworkElement[] { _teType, _teSp, _teDepth, _teExt }) fe.IsEnabled = te;
            bool bs = scratch.Maciza.BottomSecondary.Enabled;
            foreach (FrameworkElement fe in new FrameworkElement[] { _bsType, _bsSp, _bsExt }) fe.IsEnabled = bs;
            bool tm = !scratch.Maciza.TopMain.None;
            foreach (FrameworkElement fe in new FrameworkElement[] { _tmType, _tmSp, _tmExt, _tmHook, _tmIntFrac, _tmEndFrac, _tmFixed }) fe.IsEnabled = tm;
            foreach (FrameworkElement fe in new FrameworkElement[] { _tmIntFrac, _tmEndFrac, _tmFixed }) fe.IsEnabled = tm && !scratch.Maciza.TopMain.Continuous;
            bool ts = scratch.Maciza.TopSecondary.Enabled;
            foreach (FrameworkElement fe in new FrameworkElement[] { _tsType, _tsSp, _tsExt }) fe.IsEnabled = ts;

            PlanDiameters d = Diameters(scratch, out bool allChosen);

            // estado de cada losa con esta configuracion
            int ok = 0;
            foreach (HostAnalysis item in _items)
            {
                bool good = ItemStatus(item, scratch, d, out string text, out _);
                if (good) ok++;
                if (_itemRuns.TryGetValue(item, out var runs))
                {
                    runs.kind.Text = item.Kind(scratch) + (item.CanBuild && item.KindIsAuto(scratch) ? " (por el nombre)" : "") + ": ";
                    runs.kind.Foreground = good ? RevitTheme.Ok : RevitTheme.Error;
                    runs.detail.Text = text;
                }
            }
            _buildButton.Content = "Armar " + ok + " elemento(s)";
            _buildButton.IsEnabled = ok > 0 && error == null;

            // esquema del elemento seleccionado
            if (_selected != null && _selected.CanBuild)
            {
                ItemStatus(_selected, scratch, d, out string text, out SlabPlan plan);
                SlabFrame frame = _selected.Frame(scratch);
                _previewCaption.Text = _selected.Tag + _selected.Kind(scratch) + ", " + frame.Describe() + ", " + _selected.Outline.Describe() +
                                       (allChosen ? "" : "  (hay capas sin tipo de barra elegido: diametros orientativos)");
                if (plan != null) { _plan.Show(frame, plan); _section.Show(frame, plan); }
                else { _plan.Clear(text); _section.Clear(text); }
                _partitionPreview.Text = "Ejemplo: " + _selected.Partition(scratch, "inferior de vigueta", "inferior");
            }
            else
            {
                _previewCaption.Text = "";
                _plan.Clear("Sin elemento armable");
                _section.Clear("");
                _partitionPreview.Text = "";
            }

            if (error != null) { _message.Foreground = RevitTheme.Error; _message.Text = error; }
            else if (_message.Foreground == RevitTheme.Error) _message.Text = "";
        }

        /// <summary>Estado de una losa con la configuracion dada: true si se puede armar, y el texto para su fila.</summary>
        private static bool ItemStatus(HostAnalysis item, AppConfig cfg, PlanDiameters d, out string text, out SlabPlan plan)
        {
            plan = null;
            if (!item.CanBuild) { text = item.Error; return false; }
            try
            {
                SlabFrame frame = item.Frame(cfg);
                plan = RebarGenerator.PlanFor(item, cfg, d);
                string head = frame.Describe() + ", " + item.Outline.Describe();
                if (plan.Error != null) { text = head + " -> SIN ARMAR: " + plan.Error; return false; }
                text = head + "; " + plan.Describe() + (plan.Warnings.Count > 0 ? " (" + string.Join("; ", plan.Warnings) + ")" : "");
                return plan.Bars.Count > 0;
            }
            catch (Exception ex)
            {
                text = item.Outline.Describe() + " -> SIN ARMAR: " + ex.Message;
                return false;
            }
        }

        private void OnBuild()
        {
            AppConfig c = ReadConfig(out string error);
            if (error != null) { _message.Foreground = RevitTheme.Error; _message.Text = error; return; }
            List<string> missing = MissingTypes(c);
            if (missing.Count > 0)
            {
                _strictTypes = true;
                MarkTypes(c);
                _message.Foreground = RevitTheme.Error;
                _message.Text = "Elige el tipo de barra de: " + string.Join(", ", missing) + ".";
                return;
            }
            Result = c;
            DialogResult = true;
            Close();
        }
    }
}
