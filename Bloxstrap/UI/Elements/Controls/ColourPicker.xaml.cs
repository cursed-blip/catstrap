using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Bloxstrap.UI.Elements.Controls
{
    public partial class ColourPicker : UserControl
    {
        public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(Color),
            typeof(ColourPicker),
            new FrameworkPropertyMetadata(
                Colors.OrangeRed,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedColorChanged));

        private static readonly DependencyPropertyKey SwatchBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SwatchBrush),
            typeof(Brush),
            typeof(ColourPicker),
            new PropertyMetadata(Brushes.Transparent));

        public static readonly DependencyProperty SwatchBrushProperty = SwatchBrushPropertyKey.DependencyProperty;

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public Brush SwatchBrush => (Brush)GetValue(SwatchBrushProperty);

        public event EventHandler? ColourCommitted;

        private double _hue;
        private double _saturation = 1;
        private double _brightness = 1;

        private bool _draggingSquare;
        private bool _draggingHue;
        private bool _typingHex;

        public ColourPicker()
        {
            InitializeComponent();

            Loaded += (_, _) => SyncFromColor(SelectedColor);
            SizeChanged += (_, _) => PositionHueThumb();
        }

        private static void OnSelectedColorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is ColourPicker picker && !picker._typingHex)
                picker.SyncFromColor((Color)e.NewValue);
        }

        private void SyncFromColor(Color color)
        {
            ToHsv(color, out _hue, out _saturation, out _brightness);

            UpdateSquareFill();
            PositionSquareThumb();
            PositionHueThumb();
            UpdateReadouts();
        }

        private void UpdateSquareFill()
        {
            var pure = HsvToColor(_hue, 1, 1);
            var brush = new SolidColorBrush(pure);
            brush.Freeze();

            SvBase.Fill = brush;
        }

        private void PositionSquareThumb()
        {
            double x = _saturation * SvCanvas.Width;
            double y = (1 - _brightness) * SvCanvas.Height;

            Canvas.SetLeft(SvThumb, x - SvThumb.Width / 2);
            Canvas.SetTop(SvThumb, y - SvThumb.Height / 2);
        }

        private void PositionHueThumb()
        {
            double width = HueCanvas.ActualWidth;

            if (width <= 0)
                return;

            CursorAt(HueThumb, HueCanvas, _hue / 360 * width);
        }

        private static void CursorAt(FrameworkElement thumb, Canvas canvas, double x)
        {
            double half = thumb.Width / 2;
            double furthest = Math.Max(0, canvas.ActualWidth - thumb.Width);

            Canvas.SetLeft(thumb, Math.Clamp(x - half, 0, furthest));
            Canvas.SetTop(thumb, 0);
        }

        private void UpdateReadouts()
        {
            Color color = ToColor();

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            SetValue(SwatchBrushPropertyKey, brush);

            RgbText.Text = $"R {color.R}   G {color.G}   B {color.B}";

            if (HexBox.IsFocused)
                return;

            _typingHex = true;

            try
            {
                HexBox.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            }
            finally
            {
                _typingHex = false;
            }
        }

        private Color ToColor() => HsvToColor(_hue, _saturation, _brightness);

        private void PushColor()
        {
            SetCurrentValue(SelectedColorProperty, ToColor());

            UpdateSquareFill();
            PositionSquareThumb();
            PositionHueThumb();
            UpdateReadouts();
        }

        private void Sv_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _draggingSquare = true;
            SvCanvas.CaptureMouse();
            DragSquare(e.GetPosition(SvCanvas));
        }

        private void Sv_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingSquare && e.LeftButton == MouseButtonState.Pressed)
                DragSquare(e.GetPosition(SvCanvas));
        }

        private void Sv_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _draggingSquare = false;
            SvCanvas.ReleaseMouseCapture();

            ColourCommitted?.Invoke(this, EventArgs.Empty);
        }

        private void DragSquare(Point point)
        {
            _saturation = Math.Clamp(point.X / SvCanvas.Width, 0, 1);
            _brightness = 1 - Math.Clamp(point.Y / SvCanvas.Height, 0, 1);

            PushColor();
        }

        private void Hue_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _draggingHue = true;
            HueCanvas.CaptureMouse();
            DragHue(e.GetPosition(HueCanvas));
        }

        private void Hue_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingHue && e.LeftButton == MouseButtonState.Pressed)
                DragHue(e.GetPosition(HueCanvas));
        }

        private void Hue_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _draggingHue = false;
            HueCanvas.ReleaseMouseCapture();

            ColourCommitted?.Invoke(this, EventArgs.Empty);
        }

        private void DragHue(Point point)
        {
            double width = HueCanvas.ActualWidth;

            if (width <= 0)
                return;

            _hue = Math.Clamp(point.X / width, 0, 1) * 360;

            PushColor();
        }

        private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_typingHex)
                return;

            string text = HexBox.Text.Trim();

            if (text.Length is not (4 or 7))
                return;

            Color? parsed = null;

            try
            {
                if (ColorConverter.ConvertFromString(text) is Color color)
                    parsed = color;
            }
            catch (Exception)
            {
                return;
            }

            if (parsed is not Color typed || typed == ToColor())
                return;

            _typingHex = true;

            try
            {
                ToHsv(typed, out _hue, out _saturation, out _brightness);
            }
            finally
            {
                _typingHex = false;
            }

            SetCurrentValue(SelectedColorProperty, typed);

            UpdateSquareFill();
            PositionSquareThumb();
            PositionHueThumb();
            UpdateReadouts();

            ColourCommitted?.Invoke(this, EventArgs.Empty);
        }

        private static void ToHsv(Color color, out double hue, out double saturation, out double brightness)
        {
            double r = color.R / 255d;
            double g = color.G / 255d;
            double b = color.B / 255d;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            brightness = max;
            saturation = max <= 0 ? 0 : delta / max;

            if (delta <= 0)
            {
                hue = 0;
                return;
            }

            if (max == r)
                hue = 60 * (((g - b) / delta) % 6);
            else if (max == g)
                hue = 60 * ((b - r) / delta + 2);
            else
                hue = 60 * ((r - g) / delta + 4);

            if (hue < 0)
                hue += 360;
        }

        private static Color HsvToColor(double hue, double saturation, double brightness)
        {
            hue = ((hue % 360) + 360) % 360;
            saturation = Math.Clamp(saturation, 0, 1);
            brightness = Math.Clamp(brightness, 0, 1);

            double c = brightness * saturation;
            double x = c * (1 - Math.Abs((hue / 60) % 2 - 1));
            double m = brightness - c;

            double r, g, b;

            switch ((int)(hue / 60))
            {
                case 0: (r, g, b) = (c, x, 0); break;
                case 1: (r, g, b) = (x, c, 0); break;
                case 2: (r, g, b) = (0, c, x); break;
                case 3: (r, g, b) = (0, x, c); break;
                case 4: (r, g, b) = (x, 0, c); break;
                default: (r, g, b) = (c, 0, x); break;
            }

            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }
    }
}
