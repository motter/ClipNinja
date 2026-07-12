using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
// ImplicitUsings pulls in System.IO globally, which also defines Path —
// alias to the Shapes one we actually mean, or every `new Path {…}`
// below is a CS0104 ambiguous-reference error.
using Path = System.Windows.Shapes.Path;

namespace ClipNinjaV2.Views;

/// <summary>
/// Image annotation editor: arrow, box, line, highlight, and obfuscate
/// (pixelate) tools with undo and a five-swatch color palette drawn
/// from the app's desert-sunset theme.
///
/// Design notes:
///  • The image renders at 1:1 pixel scale inside a ScrollViewer, so
///    coordinates map directly — no DPI math needed for hit-testing.
///    (The bitmaps ClipNinja stores are always 96-DPI-normalized by
///    the capture pipeline, so DIU == pixel here.)
///  • Annotations are WPF Shape elements on a Canvas overlay while
///    editing (cheap to add/remove for undo). On Save we render
///    image + canvas together into a RenderTargetBitmap and hand the
///    flattened result back to the caller.
///  • Highlight = semi-transparent fill (40% alpha) so text stays
///    readable under it — classic highlighter behavior.
///  • Obfuscate = pixelate: the selected region of the SOURCE image
///    is downscaled to ~12px blocks and re-upscaled with nearest-
///    neighbor. During the drag you see a dashed preview rectangle;
///    the mosaic is computed once on mouse-up (recomputing it every
///    mouse-move would chug on large regions).
///  • Undo = pop the last element off the canvas, regardless of type.
///
/// Returns the annotated bitmap, or null if the user canceled or made
/// no changes.
/// </summary>
public static class ImageAnnotator
{
    private enum Tool { Select, Arrow, Box, Line, Highlight, Obfuscate, Text, Number }

    /// <summary>Attached to every committed annotation via Tag. Gives
    /// the Select tool what it needs to move/resize without re-deriving
    /// geometry from rendered output: the kind (drives which handles
    /// appear) and the two defining points (endpoints for line/arrow,
    /// opposite corners for box/highlight/obfuscate; P1 = anchor point
    /// for text/number). Mutated in place as the user drags — the
    /// UIElement is never replaced, so the undo list stays valid.</summary>
    private sealed class AnnotMeta
    {
        public string Kind = "";
        public Point P1;
        public Point P2;
    }

    public static BitmapSource? Show(Window owner, BitmapSource source)
    {
        BitmapSource? result = null;

        var dlg = new Window
        {
            Owner = owner,
            Title = "Annotate image — drag to draw",
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x26, 0x20)),
            // Size to the image plus chrome, but clamp to 90% of the
            // screen so huge screenshots don't create an unusable
            // window. ScrollViewer handles overflow.
            Width = Math.Min(source.PixelWidth + 60, SystemParameters.WorkArea.Width * 0.9),
            Height = Math.Min(source.PixelHeight + 130, SystemParameters.WorkArea.Height * 0.9),
            MinWidth = 420,
            MinHeight = 300,
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // toolbar
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // canvas
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // buttons

        // ── Toolbar ───────────────────────────────────────────────────
        var currentTool = Tool.Arrow;
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10, 8, 10, 8),
        };
        var toolButtons = new Dictionary<Tool, ToggleButton>();

        ToggleButton MakeToolButton(Tool tool, string glyph, string tip)
        {
            var b = new ToggleButton
            {
                Content = glyph,
                FontSize = 16,
                Width = 44,
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                ToolTip = tip,
                IsChecked = tool == currentTool,
            };
            b.Click += (_, _) =>
            {
                currentTool = tool;
                foreach (var kv in toolButtons) kv.Value.IsChecked = kv.Key == tool;
            };
            toolButtons[tool] = b;
            toolbar.Children.Add(b);
            return b;
        }

        MakeToolButton(Tool.Select, "↖", "Select — click an annotation to move / resize / delete it");
        MakeToolButton(Tool.Arrow, "↗", "Arrow — drag from tail to tip");
        MakeToolButton(Tool.Box, "▢", "Box — drag corner to corner");
        MakeToolButton(Tool.Line, "╱", "Line — drag from end to end");
        MakeToolButton(Tool.Highlight, "▆", "Highlight — semi-transparent marker box");
        MakeToolButton(Tool.Obfuscate, "▦", "Obfuscate — pixelate a region (hide names, emails, secrets)");
        MakeToolButton(Tool.Text, "T", "Text — click to type a label on the image");
        MakeToolButton(Tool.Number, "①", "Number — each click drops the next step number (1, 2, 3…) for walkthroughs");

        // ── Size selector: S / M / L ──────────────────────────────────
        // Drives stroke width for arrow/box/line (2 / 3.5 / 6 px), font
        // size for the text tool (13 / 18 / 26 px), and badge size for
        // the number tool. One control, consistent meaning everywhere.
        double strokeWidth = 3.5;
        double textSize = 18;
        var sizeDefs = new (string label, double stroke, double font, string tip)[]
        {
            ("S", 2.0, 13, "Small — thin strokes, small text"),
            ("M", 3.5, 18, "Medium"),
            ("L", 6.0, 26, "Large — thick strokes, big text"),
        };
        var sizeButtons = new List<ToggleButton>();
        var sizePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var (label, stroke, font, tip) in sizeDefs)
        {
            var b = new ToggleButton
            {
                Content = label,
                FontSize = 11,
                Width = 28,
                Height = 26,
                Margin = new Thickness(0, 0, 3, 0),
                Cursor = Cursors.Hand,
                ToolTip = tip,
                IsChecked = label == "M",
            };
            b.Click += (_, _) =>
            {
                strokeWidth = stroke;
                textSize = font;
                foreach (var sb in sizeButtons) sb.IsChecked = sb == b;
            };
            sizeButtons.Add(b);
            sizePanel.Children.Add(b);
        }
        toolbar.Children.Add(sizePanel);

        // ── Color swatches — desert-sunset palette from the app theme ──
        // Applies to arrow / box / line stroke and highlight fill.
        // (Obfuscate ignores color; a mosaic has no ink.)
        var currentColor = Color.FromRgb(0xF4, 0xB8, 0x44);  // Sun Gold default
        var swatchDefs = new (Color color, string name)[]
        {
            (Color.FromRgb(0xF4, 0xB8, 0x44), "Sun gold"),
            (Color.FromRgb(0xE5, 0x9A, 0x2A), "Amber"),
            (Color.FromRgb(0x7F, 0xB0, 0x69), "Agave green"),
            (Color.FromRgb(0x8D, 0xA9, 0xB8), "Sky blue"),
            (Color.FromRgb(0xD9, 0x55, 0x3F), "Clay red"),
        };
        var swatchButtons = new List<Border>();
        var swatchPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (var (color, name) in swatchDefs)
        {
            var isDefault = color == currentColor;
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(isDefault ? 2 : 0),
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.Hand,
                ToolTip = name,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                currentColor = color;
                foreach (var s in swatchButtons)
                    s.BorderThickness = new Thickness(s == swatch ? 2 : 0);
            };
            swatchButtons.Add(swatch);
            swatchPanel.Children.Add(swatch);
        }
        toolbar.Children.Add(swatchPanel);

        var undoBtn = new Button
        {
            Content = "↩ Undo",
            FontSize = 13,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(16, 0, 0, 0),
            Cursor = Cursors.Hand,
            ToolTip = "Remove the last annotation (Ctrl+Z)",
            IsEnabled = false,
        };
        toolbar.Children.Add(undoBtn);

        toolbar.Children.Add(new TextBlock
        {
            Text = "Highlight is 40% transparent • Obfuscate pixelates the region",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x82, 0x74)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        });

        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        // ── Editing surface ───────────────────────────────────────────
        // Image at 1:1 with a Canvas overlay of identical size. Both in
        // a Grid inside a ScrollViewer so large screenshots scroll.
        var imageEl = new Image
        {
            Source = source,
            Width = source.PixelWidth,
            Height = source.PixelHeight,
            Stretch = Stretch.None,
            SnapsToDevicePixels = true,
        };
        var overlay = new Canvas
        {
            Width = source.PixelWidth,
            Height = source.PixelHeight,
            Background = Brushes.Transparent,  // hit-testable everywhere
            Cursor = Cursors.Cross,
        };
        var surface = new Grid { Width = source.PixelWidth, Height = source.PixelHeight };
        surface.Children.Add(imageEl);
        surface.Children.Add(overlay);
        // Handle layer sits ABOVE the overlay so selection handles get
        // first crack at mouse input. IsHitTestVisible=false when idle
        // (Background=null makes empty space click-through regardless).
        // Handles are hidden before Save so they never bake into the
        // flattened output.
        var handleLayer = new Canvas
        {
            Width = source.PixelWidth,
            Height = source.PixelHeight,
            Background = null,  // clicks pass through empty areas
        };
        surface.Children.Add(handleLayer);

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = surface,
            Margin = new Thickness(10, 0, 10, 0),
        };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);

        // ── Drawing interaction ───────────────────────────────────────
        // (strokeWidth / textSize are declared with the S/M/L selector
        // in the toolbar section above.)

        // Undo is a List used as a stack (not Stack<T>) so the Select
        // tool's Delete can remove an element from the MIDDLE without
        // disturbing the rest of the ordering.
        var undoStack = new List<UIElement>();
        Point dragStart = default;
        UIElement? liveShape = null;   // shape being resized during drag
        bool drawing = false;

        // Clamp a drag rectangle to the image bounds and integerize —
        // used by obfuscate (pixel sampling can't go out of bounds) and
        // handy for keeping highlights inside the image.
        (int x, int y, int w, int h) ClampRect(Point a, Point b)
        {
            int x1 = (int)Math.Clamp(Math.Min(a.X, b.X), 0, source.PixelWidth);
            int y1 = (int)Math.Clamp(Math.Min(a.Y, b.Y), 0, source.PixelHeight);
            int x2 = (int)Math.Clamp(Math.Max(a.X, b.X), 0, source.PixelWidth);
            int y2 = (int)Math.Clamp(Math.Max(a.Y, b.Y), 0, source.PixelHeight);
            return (x1, y1, x2 - x1, y2 - y1);
        }

        // Pixelate a region of the SOURCE image: crop → downscale to
        // ~12px blocks → display upscaled with nearest-neighbor. The
        // result is a mosaic that genuinely destroys the detail
        // underneath (it's baked from the real pixels, so saving
        // flattens exactly what's previewed). Returns null for regions
        // too small to matter.
        UIElement? MakePixelated(Point a, Point b)
        {
            var (x, y, w, h) = ClampRect(a, b);
            if (w < 4 || h < 4) return null;
            try
            {
                var crop = new CroppedBitmap(source, new System.Windows.Int32Rect(x, y, w, h));
                // Downscale so each mosaic block is ~12px of the original,
                // with at least 1px in each dimension, then let the Image
                // upscale it back with NearestNeighbor for hard block edges.
                const double blockPx = 12.0;
                int smallW = Math.Max(1, (int)Math.Round(w / blockPx));
                int smallH = Math.Max(1, (int)Math.Round(h / blockPx));
                var small = new TransformedBitmap(crop,
                    new ScaleTransform((double)smallW / w, (double)smallH / h));
                var img = new Image
                {
                    Source = small,
                    Width = w,
                    Height = h,
                    Stretch = Stretch.Fill,
                };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                Canvas.SetLeft(img, x);
                Canvas.SetTop(img, y);
                return img;
            }
            catch { return null; }
        }

        // Build (or rebuild) the shape for the current drag rectangle.
        // Called on every MouseMove — replacing the live shape wholesale
        // is simpler than mutating geometry in place, and cheap at
        // human drag speeds. `final` distinguishes the mouse-up build:
        // obfuscate shows a cheap dashed preview during the drag and
        // only computes the actual mosaic once, at the end.
        // Arrow geometry builder — shared by BuildShape (creation) and
        // the Select tool (endpoint drags rebuild the SAME Path's Data
        // in place, so the element identity and undo entry survive).
        static Geometry BuildArrowGeometry(Point a, Point b)
        {
            var geo = new GeometryGroup();
            geo.Children.Add(new LineGeometry(a, b));
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 2)
            {
                double headLen = Math.Clamp(len * 0.22, 8, 26);
                double angle = Math.Atan2(dy, dx);
                const double spread = Math.PI / 7;  // ~26° per side
                var h1 = new Point(
                    b.X - headLen * Math.Cos(angle - spread),
                    b.Y - headLen * Math.Sin(angle - spread));
                var h2 = new Point(
                    b.X - headLen * Math.Cos(angle + spread),
                    b.Y - headLen * Math.Sin(angle + spread));
                geo.Children.Add(new LineGeometry(b, h1));
                geo.Children.Add(new LineGeometry(b, h2));
            }
            return geo;
        }

        UIElement? BuildShape(Point a, Point b, bool final)
        {
            var stroke = new SolidColorBrush(currentColor);
            stroke.Freeze();
            switch (currentTool)
            {
                case Tool.Box:
                {
                    var rect = new Rectangle
                    {
                        Stroke = stroke,
                        StrokeThickness = strokeWidth,
                        Width = Math.Abs(b.X - a.X),
                        Height = Math.Abs(b.Y - a.Y),
                        Tag = new AnnotMeta { Kind = "box", P1 = a, P2 = b },
                    };
                    Canvas.SetLeft(rect, Math.Min(a.X, b.X));
                    Canvas.SetTop(rect, Math.Min(a.Y, b.Y));
                    return rect;
                }
                case Tool.Line:
                {
                    return new Line
                    {
                        Stroke = stroke,
                        StrokeThickness = strokeWidth,
                        X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Tag = new AnnotMeta { Kind = "line", P1 = a, P2 = b },
                    };
                }
                case Tool.Highlight:
                {
                    // Classic highlighter: 40% alpha fill, no stroke, so
                    // whatever's underneath stays readable. Clamped to
                    // the image so a sloppy drag doesn't spill color
                    // into the (transparent) canvas margin.
                    var (x, y, w, h) = ClampRect(a, b);
                    var fill = new SolidColorBrush(Color.FromArgb(
                        0x66, currentColor.R, currentColor.G, currentColor.B));
                    fill.Freeze();
                    var rect = new Rectangle
                    {
                        Fill = fill,
                        Width = w,
                        Height = h,
                        Tag = new AnnotMeta { Kind = "highlight", P1 = new Point(x, y), P2 = new Point(x + w, y + h) },
                    };
                    Canvas.SetLeft(rect, x);
                    Canvas.SetTop(rect, y);
                    return rect;
                }
                case Tool.Obfuscate:
                {
                    if (final)
                    {
                        var img = MakePixelated(a, b);
                        if (img is FrameworkElement fe)
                        {
                            var (x, y, w, h) = ClampRect(a, b);
                            fe.Tag = new AnnotMeta { Kind = "obfuscate", P1 = new Point(x, y), P2 = new Point(x + w, y + h) };
                        }
                        return img;
                    }
                    // Drag preview: dark translucent rect with a dashed
                    // border — communicates "this area will be blocked
                    // out" without paying for pixelation on every move.
                    var (px, py, pw, ph) = ClampRect(a, b);
                    var rect = new Rectangle
                    {
                        Fill = new SolidColorBrush(Color.FromArgb(0x50, 0x00, 0x00, 0x00)),
                        Stroke = Brushes.White,
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection { 4, 3 },
                        Width = pw,
                        Height = ph,
                    };
                    Canvas.SetLeft(rect, px);
                    Canvas.SetTop(rect, py);
                    return rect;
                }
                default: // Arrow
                {
                    return new Path
                    {
                        Stroke = stroke,
                        StrokeThickness = strokeWidth,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        Data = BuildArrowGeometry(a, b),
                        Tag = new AnnotMeta { Kind = "arrow", P1 = a, P2 = b },
                    };
                }
            }
        }

        // Number tool counter. Undo decrements it (see DoUndo) so a
        // mis-click doesn't leave a gap in the sequence.
        int nextNumber = 1;

        // Commit a floating text editor into a permanent label on the
        // overlay. Shared by Enter, focus-loss, and tool-switch.
        // The committed label is a Border: background = dark tint of
        // the chosen color (readable over any screenshot content),
        // border + text = the chosen color. Matches the editor's look
        // so commit is WYSIWYG.
        void CommitTextEditor(TextBox editor)
        {
            var text = editor.Text?.TrimEnd() ?? "";
            double x = Canvas.GetLeft(editor);
            double y = Canvas.GetTop(editor);
            overlay.Children.Remove(editor);
            if (string.IsNullOrWhiteSpace(text)) return;  // empty = never happened
            var label = new Border
            {
                Background = editor.Background,
                BorderBrush = editor.BorderBrush,
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 2, 5, 2),
                Tag = new AnnotMeta { Kind = "text", P1 = new Point(x, y) },
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = editor.FontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = editor.Foreground,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = Math.Max(80, source.PixelWidth - x - 12),
                },
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            overlay.Children.Add(label);
            undoStack.Add(label);
            undoBtn.IsEnabled = true;
        }

        // Mix the chosen color into a dark base for the label
        // background: dark enough for contrast against any screenshot,
        // tinted enough to visibly belong to the chosen swatch.
        static Color TintedDark(Color c) => Color.FromArgb(
            0xD9,                       // ~85% opaque
            (byte)(c.R * 0.22 + 0x14),  // 22% of the hue over near-black
            (byte)(c.G * 0.22 + 0x12),
            (byte)(c.B * 0.22 + 0x10));

        // Place a live text editor at the click point, styled to match
        // the final label exactly. Enter or clicking elsewhere commits;
        // Esc cancels just the label.
        void PlaceTextEditor(Point at)
        {
            var fgBrush = new SolidColorBrush(currentColor);
            fgBrush.Freeze();
            var bgBrush = new SolidColorBrush(TintedDark(currentColor));
            bgBrush.Freeze();
            var editor = new TextBox
            {
                FontSize = textSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = fgBrush,
                Background = bgBrush,
                BorderBrush = fgBrush,
                BorderThickness = new Thickness(1.5),
                CaretBrush = fgBrush,
                MinWidth = 40,
                AcceptsReturn = false,
                Padding = new Thickness(4, 1, 4, 1),
            };
            Canvas.SetLeft(editor, at.X);
            Canvas.SetTop(editor, at.Y);
            overlay.Children.Add(editor);
            editor.Loaded += (_, _) => editor.Focus();
            editor.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter) { CommitTextEditor(editor); ke.Handled = true; }
                else if (ke.Key == Key.Escape) { overlay.Children.Remove(editor); ke.Handled = true; }
            };
            editor.LostFocus += (_, _) =>
            {
                // Guard: LostFocus can fire after Enter already committed
                // and removed the editor — only commit if still attached.
                if (overlay.Children.Contains(editor)) CommitTextEditor(editor);
            };
        }

        // Drop a numbered badge: filled circle in the current color with
        // the number in white. Size scales with the S/M/L selector.
        void PlaceNumberBadge(Point at)
        {
            double d = textSize * 1.6;  // badge diameter tracks text size
            var fill = new SolidColorBrush(currentColor);
            fill.Freeze();
            var badge = new Grid
            {
                Width = d,
                Height = d,
                // Meta marks this as a number badge (DoUndo decrements
                // the counter when one is popped) and records the anchor
                // for the Select tool's move.
                Tag = new AnnotMeta { Kind = "number", P1 = at },
            };
            badge.Children.Add(new Ellipse
            {
                Fill = fill,
                Stroke = Brushes.White,
                StrokeThickness = Math.Max(1.5, d * 0.06),
            });
            badge.Children.Add(new TextBlock
            {
                Text = nextNumber.ToString(),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = d * 0.52,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            // Center the badge on the click point — pointing AT things
            // is the whole use case.
            Canvas.SetLeft(badge, at.X - d / 2);
            Canvas.SetTop(badge, at.Y - d / 2);
            overlay.Children.Add(badge);
            undoStack.Add(badge);
            undoBtn.IsEnabled = true;
            nextNumber++;
        }

        // ══ Selection subsystem (Select tool) ═════════════════════════
        // Selected element gets handles on the handleLayer:
        //   line/arrow          → a handle at each endpoint (drag to
        //                         re-aim / lengthen / change angle)
        //   box/highlight/
        //   obfuscate           → 4 corner handles (drag to resize)
        //   text/number         → no handles; body-drag moves them
        // Dragging the element body moves the whole thing. All edits
        // MUTATE the existing element (never replace it), so the undo
        // list entries stay valid. Handles live on a separate layer
        // that's hidden before Save, so they can't bake into output.
        UIElement? selected = null;
        FrameworkElement? dragHandle = null;   // which handle is being dragged
        bool movingBody = false;
        Point moveGrabPoint = default;         // pointer pos at body-grab time

        AnnotMeta? MetaOf(UIElement? el) => (el as FrameworkElement)?.Tag as AnnotMeta;

        // Re-apply an element's meta to its visual properties. The one
        // mutation path used by both body-moves and handle-drags.
        void ApplyMeta(UIElement el)
        {
            var m = MetaOf(el);
            if (m is null) return;
            switch (m.Kind)
            {
                case "line":
                    if (el is Line ln) { ln.X1 = m.P1.X; ln.Y1 = m.P1.Y; ln.X2 = m.P2.X; ln.Y2 = m.P2.Y; }
                    break;
                case "arrow":
                    if (el is Path p) p.Data = BuildArrowGeometry(m.P1, m.P2);
                    break;
                case "box":
                case "highlight":
                case "obfuscate":
                    if (el is FrameworkElement fe)
                    {
                        double x = Math.Min(m.P1.X, m.P2.X), y = Math.Min(m.P1.Y, m.P2.Y);
                        fe.Width = Math.Max(4, Math.Abs(m.P2.X - m.P1.X));
                        fe.Height = Math.Max(4, Math.Abs(m.P2.Y - m.P1.Y));
                        Canvas.SetLeft(fe, x);
                        Canvas.SetTop(fe, y);
                    }
                    break;
                case "text":
                    Canvas.SetLeft((FrameworkElement)el, m.P1.X);
                    Canvas.SetTop((FrameworkElement)el, m.P1.Y);
                    break;
                case "number":
                    if (el is FrameworkElement badge)
                    {
                        Canvas.SetLeft(badge, m.P1.X - badge.Width / 2);
                        Canvas.SetTop(badge, m.P1.Y - badge.Height / 2);
                    }
                    break;
            }
        }

        FrameworkElement MakeHandle(string role)
        {
            var h = new Rectangle
            {
                Width = 9,
                Height = 9,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(0xE5, 0x9A, 0x2A)),
                StrokeThickness = 1.5,
                Cursor = Cursors.SizeAll,
                Tag = role,  // "p1"/"p2" endpoints, or "nw"/"ne"/"sw"/"se" corners
            };
            h.MouseLeftButtonDown += (_, e) =>
            {
                dragHandle = h;
                handleLayer.CaptureMouse();
                e.Handled = true;
            };
            return h;
        }

        void PositionHandle(FrameworkElement h, Point at)
        {
            Canvas.SetLeft(h, at.X - h.Width / 2);
            Canvas.SetTop(h, at.Y - h.Height / 2);
        }

        void RefreshHandles()
        {
            handleLayer.Children.Clear();
            var m = MetaOf(selected);
            if (selected is null || m is null) return;
            switch (m.Kind)
            {
                case "line":
                case "arrow":
                {
                    var h1 = MakeHandle("p1"); PositionHandle(h1, m.P1); handleLayer.Children.Add(h1);
                    var h2 = MakeHandle("p2"); PositionHandle(h2, m.P2); handleLayer.Children.Add(h2);
                    break;
                }
                case "box":
                case "highlight":
                case "obfuscate":
                {
                    double x1 = Math.Min(m.P1.X, m.P2.X), y1 = Math.Min(m.P1.Y, m.P2.Y);
                    double x2 = Math.Max(m.P1.X, m.P2.X), y2 = Math.Max(m.P1.Y, m.P2.Y);
                    // Normalize meta so corner roles are stable while dragging.
                    m.P1 = new Point(x1, y1);
                    m.P2 = new Point(x2, y2);
                    var nw = MakeHandle("nw"); PositionHandle(nw, new Point(x1, y1)); handleLayer.Children.Add(nw);
                    var ne = MakeHandle("ne"); PositionHandle(ne, new Point(x2, y1)); handleLayer.Children.Add(ne);
                    var sw = MakeHandle("sw"); PositionHandle(sw, new Point(x1, y2)); handleLayer.Children.Add(sw);
                    var se = MakeHandle("se"); PositionHandle(se, new Point(x2, y2)); handleLayer.Children.Add(se);
                    break;
                }
                // text / number: move-only via body drag, no handles.
            }
        }

        void ClearSelection()
        {
            selected = null;
            dragHandle = null;
            movingBody = false;
            handleLayer.Children.Clear();
        }

        // Walk from the event's original source up to the direct child
        // of the overlay (number badges are Grids containing an Ellipse
        // + TextBlock, so the source is usually a grandchild).
        UIElement? HitAnnotation(object originalSource)
        {
            var cur = originalSource as DependencyObject;
            while (cur is not null)
            {
                if (cur is UIElement el && overlay.Children.Contains(el))
                    return ReferenceEquals(el, overlay) ? null : el;
                cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
            }
            return null;
        }

        // Handle drags land on the handleLayer (it has mouse capture).
        handleLayer.MouseMove += (_, e) =>
        {
            if (dragHandle is null || selected is null) return;
            var m = MetaOf(selected);
            if (m is null) return;
            var pos = e.GetPosition(overlay);
            switch (dragHandle.Tag as string)
            {
                case "p1": m.P1 = pos; break;
                case "p2": m.P2 = pos; break;
                case "nw": m.P1 = pos; break;
                case "se": m.P2 = pos; break;
                case "ne": m.P1 = new Point(m.P1.X, pos.Y); m.P2 = new Point(pos.X, m.P2.Y); break;
                case "sw": m.P1 = new Point(pos.X, m.P1.Y); m.P2 = new Point(m.P2.X, pos.Y); break;
            }
            ApplyMeta(selected);
            RefreshHandles();
        };
        handleLayer.MouseLeftButtonUp += (_, _) =>
        {
            if (dragHandle is not null)
            {
                dragHandle = null;
                handleLayer.ReleaseMouseCapture();
            }
        };

        overlay.MouseLeftButtonDown += (_, e) =>
        {
            // Select mode: hit-test annotations; click body to select +
            // start moving, click empty space to deselect.
            if (currentTool == Tool.Select)
            {
                var hit = HitAnnotation(e.OriginalSource);
                if (hit is not null && MetaOf(hit) is not null)
                {
                    selected = hit;
                    RefreshHandles();
                    movingBody = true;
                    moveGrabPoint = e.GetPosition(overlay);
                    overlay.CaptureMouse();
                }
                else
                {
                    ClearSelection();
                }
                e.Handled = true;
                return;
            }
            // Click-to-place tools handle everything on the down-click;
            // no drag state.
            if (currentTool == Tool.Text)
            {
                PlaceTextEditor(e.GetPosition(overlay));
                e.Handled = true;
                return;
            }
            if (currentTool == Tool.Number)
            {
                PlaceNumberBadge(e.GetPosition(overlay));
                e.Handled = true;
                return;
            }
            dragStart = e.GetPosition(overlay);
            drawing = true;
            overlay.CaptureMouse();
        };
        overlay.MouseMove += (_, e) =>
        {
            // Body-move in select mode: shift the selected element by
            // the pointer delta via its meta, then re-apply.
            if (movingBody && selected is not null && MetaOf(selected) is { } mm)
            {
                var pos = e.GetPosition(overlay);
                var dx = pos.X - moveGrabPoint.X;
                var dy = pos.Y - moveGrabPoint.Y;
                moveGrabPoint = pos;
                mm.P1 = new Point(mm.P1.X + dx, mm.P1.Y + dy);
                mm.P2 = new Point(mm.P2.X + dx, mm.P2.Y + dy);
                ApplyMeta(selected);
                RefreshHandles();
                return;
            }
            if (!drawing) return;
            var current = e.GetPosition(overlay);
            if (liveShape is not null) overlay.Children.Remove(liveShape);
            liveShape = BuildShape(dragStart, current, final: false);
            if (liveShape is not null) overlay.Children.Add(liveShape);
        };
        overlay.MouseLeftButtonUp += (_, e) =>
        {
            if (movingBody)
            {
                movingBody = false;
                overlay.ReleaseMouseCapture();
                return;
            }
            if (!drawing) return;
            drawing = false;
            overlay.ReleaseMouseCapture();
            var end = e.GetPosition(overlay);
            if (liveShape is not null) overlay.Children.Remove(liveShape);
            liveShape = null;
            // Ignore degenerate clicks (no meaningful drag distance) —
            // otherwise a stray click leaves an invisible 0-size shape
            // on the undo stack, which feels like undo "not working".
            if (Math.Abs(end.X - dragStart.X) < 3 && Math.Abs(end.Y - dragStart.Y) < 3) return;
            var final = BuildShape(dragStart, end, final: true);
            if (final is null) return;  // e.g. obfuscate region fully out of bounds
            overlay.Children.Add(final);
            undoStack.Add(final);
            undoBtn.IsEnabled = true;
        };

        void DoUndo()
        {
            if (undoStack.Count == 0) return;
            var popped = undoStack[^1];
            undoStack.RemoveAt(undoStack.Count - 1);
            overlay.Children.Remove(popped);
            // Popping a number badge rewinds the counter so the next
            // click reuses that number — no gaps in the sequence.
            if (popped is FrameworkElement fe && fe.Tag is AnnotMeta { Kind: "number" })
                nextNumber = Math.Max(1, nextNumber - 1);
            ClearSelection();  // handles may point at the removed element
            undoBtn.IsEnabled = undoStack.Count > 0;
        }
        undoBtn.Click += (_, _) => DoUndo();

        // Tool-switch housekeeping: leaving Select clears the selection;
        // the cursor communicates the mode (arrow = select, cross =
        // draw). Attached AFTER the primary click handler in
        // MakeToolButton, so currentTool is already updated when this
        // runs. Declared here (after the selection subsystem) so all
        // captured locals exist.
        foreach (var kv in toolButtons)
        {
            kv.Value.Click += (_, _) =>
            {
                ClearSelection();
                overlay.Cursor = currentTool == Tool.Select ? Cursors.Arrow : Cursors.Cross;
            };
        }

        dlg.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control) { DoUndo(); e.Handled = true; }
            else if (e.Key == Key.Delete && selected is not null)
            {
                // Delete the selected annotation. Removing from the
                // middle of the undo list is exactly why it's a List —
                // remaining undo order is preserved. Number badges
                // don't renumber on middle-delete (a gap is more honest
                // than silently reshuffling the remaining steps).
                overlay.Children.Remove(selected);
                undoStack.Remove(selected);
                ClearSelection();
                undoBtn.IsEnabled = undoStack.Count > 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                // Esc peels back one layer at a time: selection first,
                // dialog second.
                if (selected is not null) { ClearSelection(); e.Handled = true; }
                else dlg.DialogResult = false;
            }
        };

        // ── Buttons ───────────────────────────────────────────────────
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(10, 8, 10, 10),
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(14, 5, 14, 5),
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
            Cursor = Cursors.Hand,
        };
        var saveBtn = new Button
        {
            Content = "Save annotations",
            Padding = new Thickness(14, 5, 14, 5),
            FontWeight = FontWeights.Bold,
            Cursor = Cursors.Hand,
        };
        saveBtn.Click += (_, _) =>
        {
            // Clear selection FIRST — handles live on a layer inside the
            // rendered surface, and baked-in selection handles would be
            // a terrible souvenir.
            ClearSelection();
            if (undoStack.Count == 0)
            {
                // Nothing drawn — treat as cancel, no re-encode churn.
                dlg.DialogResult = false;
                return;
            }
            result = Flatten(source, surface);
            dlg.DialogResult = result is not null;
        };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(saveBtn);
        Grid.SetRow(btnRow, 2);
        root.Children.Add(btnRow);

        dlg.Content = root;
        var ok = dlg.ShowDialog();
        return ok == true ? result : null;
    }

    /// <summary>
    /// Render the image + annotation overlay into a flat bitmap at the
    /// source's native pixel size. The surface Grid is already sized
    /// 1:1 with the bitmap, so RenderTargetBitmap at 96 DPI captures
    /// pixel-perfect output (all ClipNinja bitmaps are 96-DPI
    /// normalized by the capture pipeline).
    /// </summary>
    private static BitmapSource? Flatten(BitmapSource source, Grid surface)
    {
        try
        {
            // Ensure layout is current — the surface is live in the
            // visual tree so this is usually a no-op, but belt and
            // suspenders before rendering.
            surface.Measure(new Size(source.PixelWidth, source.PixelHeight));
            surface.Arrange(new Rect(0, 0, source.PixelWidth, source.PixelHeight));

            var rtb = new RenderTargetBitmap(
                source.PixelWidth, source.PixelHeight,
                96, 96, PixelFormats.Pbgra32);
            rtb.Render(surface);
            rtb.Freeze();
            return rtb;
        }
        catch
        {
            return null;
        }
    }
}
