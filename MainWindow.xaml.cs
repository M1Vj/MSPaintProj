using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.IO;
using System.Windows.Shapes;

namespace MSPaintProj
{
    public partial class MainWindow : Window
    {
        private List<ToggleButton> _toolToggleButtons = new();
        private List<ToggleButton> _colorToggleButtons = new();
        private Stack<WriteableBitmap> _undoStack = new();
        private Stack<WriteableBitmap> _redoStack = new();
        private Color primaryColor = Colors.Black;
        private Color secondaryColor = Colors.White;
        private bool _isHandlingColorToggle = false;
        private double _currentZoomLevel = 1.0;
        private WriteableBitmap? _bitmap;
        private WriteableBitmap? _clipboardBitmap;
        private int _brushSize = 5;
        private bool _isDrawing = false;
        private Point _lastPoint;
        private bool _isErasing = false;
        private bool _isSelecting = false;
        private Int32Rect? _selectionRect = null;
        private bool _isMovingSelection = false;
        private Point? _selectionStartPoint = null; // For selection drag
        private Point? _moveStartPoint = null; // For move drag
        private Int32Rect? _moveStartRect = null;
        private int[]? _selectionPixels = null;
        private Rectangle _selectionVisual;
        private enum ShapeType { None, Rectangle, Ellipse, Line }
        private ShapeType _currentShape = ShapeType.None;
        private Point? _shapeStartPoint = null;
        private ContextMenu? _shapeMenu = null;

        public MainWindow()
        {
            InitializeComponent();
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, ExecuteUndo, CanExecuteUndo));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, ExecuteRedo, CanExecuteRedo));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Cut, ExecuteCut, CanExecuteCutCopy));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, ExecuteCopy, CanExecuteCutCopy));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, ExecutePaste, CanExecutePaste));
            InitializeToggleButtons();
            AttachCheckedEventHandlers();
            Color1.Foreground = new SolidColorBrush(primaryColor);
            Color2.Foreground = new SolidColorBrush(secondaryColor);
            Color1.IsChecked = true;
            _brushSize = (int)BrushSizeSlider.Value;
            Pencil.IsChecked = true;
            CanvasHost.SizeChanged += CanvasHost_SizeChanged;
            pixelImage.MouseLeftButtonDown += PixelImage_MouseLeftButtonDown;
            pixelImage.MouseMove += PixelImage_MouseMove;
            pixelImage.MouseLeftButtonUp += PixelImage_MouseLeftButtonUp;
            pixelImage.MouseLeave += (s, e) => PixelImage_MouseLeftButtonUp(s, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
            _selectionVisual = SelectionRectangleVisual;
            CanvasHost.Width = 1200;
            CanvasHost.Height = 800;
            InitBitmap((int)CanvasHost.Width, (int)CanvasHost.Height);
            PushUndo();
            ApplyZoomTransformation();
            Image.Click += Image_Click;
            Thumb_Left.DragDelta += ResizeThumb_DragDelta;
            Thumb_Right.DragDelta += ResizeThumb_DragDelta;
            Thumb_Top.DragDelta += ResizeThumb_DragDelta;
            Thumb_Bottom.DragDelta += ResizeThumb_DragDelta;
            Thumb_TopLeft.DragDelta += ResizeThumb_DragDelta;
            Thumb_TopRight.DragDelta += ResizeThumb_DragDelta;
            Thumb_BottomLeft.DragDelta += ResizeThumb_DragDelta;
            Thumb_BottomRight.DragDelta += ResizeThumb_DragDelta;
            Thumb_Left.DragCompleted += ResizeThumb_DragCompleted;
            Thumb_Right.DragCompleted += ResizeThumb_DragCompleted;
            Thumb_Top.DragCompleted += ResizeThumb_DragCompleted;
            Thumb_Bottom.DragCompleted += ResizeThumb_DragCompleted;
            Thumb_TopLeft.DragCompleted += ResizeThumb_DragCompleted;
            Thumb_TopRight.DragCompleted += ResizeThumb_DragCompleted;
            Thumb_BottomLeft.DragCompleted += ResizeThumb_DragCompleted;
            Thumb_BottomRight.DragCompleted += ResizeThumb_DragCompleted;
            _selectionVisual = SelectionRectangleVisual;
            this.Loaded += (s, e) => UpdateThumbPositions();
            this.SizeChanged += (s, e) => UpdateThumbPositions();
            ToolbarSaveButton.Click += Save_Click;
            this.PreviewMouseLeftButtonUp += MainWindow_PreviewMouseLeftButtonUp;
        }

        private void InitBitmap(int width, int height)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            ClearBitmap(_bitmap, Colors.White);
            pixelImage.Source = _bitmap;

            _selectionRect = null;

            if (_selectionVisual != null)
            {
                _selectionVisual.Visibility = Visibility.Collapsed;
            }
        }
        private void ClearBitmap(WriteableBitmap bmp, Color color)
        {
            if (bmp == null) return;
            int w = bmp.PixelWidth;
            int h = bmp.PixelHeight;
            int[] pixels = new int[w * h];
            int c = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = c;
            }

            try
            {
                bmp.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
            }
            catch (Exception ex) // Handle potential exceptions during WritePixels
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing bitmap: {ex.Message}");
            }
        }

        private void PushUndo()
        {
            if (_bitmap != null)
            {
                _undoStack.Push(_bitmap.Clone());
                _redoStack.Clear(); // Clear redo stack whenever a new action is performed
                CommandManager.InvalidateRequerySuggested(); // Update CanExecute status for commands
            }
        }

        private void InitializeToggleButtons()
        {
            _toolToggleButtons = new List<ToggleButton>
            {
                Selection, Pencil, Fill, Text, Eraser, ColorPicker, Magnifier, Brush, Shapes
            };
            _colorToggleButtons = new List<ToggleButton>
            {
                Color1, Color2
            };
        }

        private void AttachCheckedEventHandlers()
        {
            foreach (var button in _toolToggleButtons)
            {
                button.Checked += ToolToggleButton_Checked;
                button.Unchecked += ToolToggleButton_Unchecked; // Handle unchecking if needed
            }
            Color1.Checked += ColorToggleButton_Checked;
            Color2.Checked += ColorToggleButton_Checked;
            
        }

        private void ToolToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            var checkedButton = sender as ToggleButton;
            if (checkedButton == null || !_toolToggleButtons.Contains(checkedButton)) return;
            foreach (var button in _toolToggleButtons)
            {
                if (button != checkedButton && button.IsChecked == true)
                {
                    button.Checked -= ToolToggleButton_Checked;
                    button.IsChecked = false;
                    button.Checked += ToolToggleButton_Checked;
                }
            }
            // Reset all tool states when switching tools
            _isDrawing = false;
            _isErasing = false;
            _isSelecting = false;
            _isMovingSelection = false;
            Mouse.Capture(null);
            BrushSizePanel.Visibility = (checkedButton == Pencil || checkedButton == Brush || checkedButton == Eraser)
                                        ? Visibility.Visible
                                        : Visibility.Collapsed;
            if (checkedButton == Shapes)
            {
                ShowShapeMenu();
            }
            else
            {
                _currentShape = ShapeType.None;
            }
        }

        private void ShowShapeMenu()
        {
            if (_shapeMenu != null)
            {
                _shapeMenu.IsOpen = false;
            }
            _shapeMenu = new ContextMenu();
            var rectItem = new MenuItem { Header = "Rectangle" };
            var ellipseItem = new MenuItem { Header = "Ellipse" };
            var lineItem = new MenuItem { Header = "Line" };

            rectItem.Click += (s, e) => { _currentShape = ShapeType.Rectangle; _shapeMenu.IsOpen = false; };
            ellipseItem.Click += (s, e) => { _currentShape = ShapeType.Ellipse; _shapeMenu.IsOpen = false; };
            lineItem.Click += (s, e) => { _currentShape = ShapeType.Line; _shapeMenu.IsOpen = false; };

            _shapeMenu.Items.Add(rectItem);
            _shapeMenu.Items.Add(ellipseItem);
            _shapeMenu.Items.Add(lineItem);

            _shapeMenu.PlacementTarget = Shapes;
            _shapeMenu.Placement = PlacementMode.Bottom;
            _shapeMenu.IsOpen = true;
        }

        private void ToolToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            var uncheckedButton = sender as ToggleButton;
            if (uncheckedButton == Pencil || uncheckedButton == Brush || uncheckedButton == Eraser)
            {
                if (!Pencil.IsChecked.GetValueOrDefault() &&
                    !Brush.IsChecked.GetValueOrDefault() &&
                    !Eraser.IsChecked.GetValueOrDefault())
                {
                    BrushSizePanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ColorToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_isHandlingColorToggle) return;
            var checkedButton = sender as ToggleButton;
            if (checkedButton == null || !_colorToggleButtons.Contains(checkedButton)) return;
            _isHandlingColorToggle = true;
            foreach (var button in _colorToggleButtons)
            {
                if (button != checkedButton && button.IsChecked == true)
                {
                    button.Checked -= ColorToggleButton_Checked;
                    button.IsChecked = false;
                    button.Checked += ColorToggleButton_Checked;
                }
            }
            if (!checkedButton.IsChecked.GetValueOrDefault())
            {
                checkedButton.Checked -= ColorToggleButton_Checked;
                checkedButton.IsChecked = true;
                checkedButton.Checked += ColorToggleButton_Checked;
            }
            _isHandlingColorToggle = false;
        }

        private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _brushSize = (int)e.NewValue;
        }

        private bool IsEventOnCanvas(MouseEventArgs e)
        {
            // Only return true if the event is directly on the pixelImage, not on overlays or controls
            DependencyObject? current = e.OriginalSource as DependencyObject;
            while (current != null)
            {
                if (ReferenceEquals(current, pixelImage)) return true;
                // If the event is on a Button, Thumb, or other UIElement that is not pixelImage, return false
                if (current is Button || current is Thumb || current is ToggleButton || current is MenuItem)
                    return false;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private void PixelImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsEventOnCanvas(e)) return;
            if (_bitmap == null) return;
            var pos = e.GetPosition(pixelImage);
            int x = (int)Math.Max(0, Math.Min(_bitmap.PixelWidth - 1, pos.X));
            int y = (int)Math.Max(0, Math.Min(_bitmap.PixelHeight - 1, pos.Y));
            var clampedPos = new Point(x, y);

            if (Selection.IsChecked == true)
            {
                if (_selectionRect.HasValue && _selectionVisual.Visibility == Visibility.Visible)
                {
                    var rect = _selectionRect.Value;
                    if (x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height)
                    {
                        // Start moving selection
                        _isMovingSelection = true;
                        _moveStartPoint = clampedPos;
                        _moveStartRect = rect;
                        // Copy selection pixels
                        _selectionPixels = new int[rect.Width * rect.Height];
                        int[] srcPixels = new int[_bitmap.PixelWidth * _bitmap.PixelHeight];
                        _bitmap.CopyPixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), srcPixels, _bitmap.PixelWidth * 4, 0);
                        for (int sy = 0; sy < rect.Height; sy++)
                            for (int sx = 0; sx < rect.Width; sx++)
                                _selectionPixels[sy * rect.Width + sx] = srcPixels[(rect.Y + sy) * _bitmap.PixelWidth + (rect.X + sx)];
                        Mouse.Capture(pixelImage);
                        e.Handled = true;
                        return;
                    }
                }
                // Start new selection
                _isSelecting = true;
                _selectionStartPoint = clampedPos;
                _selectionRect = null;
                _selectionVisual.Visibility = Visibility.Collapsed;
                Mouse.Capture(pixelImage);
                e.Handled = true;
                return;
            }
            else if (Pencil.IsChecked == true || Brush.IsChecked == true)
            {
                _isDrawing = true;
                _lastPoint = clampedPos;
                DrawCircle((int)clampedPos.X, (int)clampedPos.Y, _brushSize, primaryColor);
                Mouse.Capture(pixelImage);
                e.Handled = true;
            }
            else if (Eraser.IsChecked == true)
            {
                _isErasing = true;
                _lastPoint = clampedPos;
                DrawCircle((int)clampedPos.X, (int)clampedPos.Y, _brushSize, Colors.White); // Eraser uses white
                Mouse.Capture(pixelImage);
                e.Handled = true;
            }
            else if (Fill.IsChecked == true)
            {
                Color fillColor = Color1.IsChecked == true ? primaryColor : secondaryColor;
                FloodFill((int)clampedPos.X, (int)clampedPos.Y, fillColor);
                PushUndo(); // Flood fill is a single action
                e.Handled = true;
            }
            else if (ColorPicker.IsChecked == true)
            {
                PickColor((int)clampedPos.X, (int)clampedPos.Y);
                e.Handled = true;
            }
            else if (Shapes.IsChecked == true && _currentShape != ShapeType.None)
            {
                _shapeStartPoint = clampedPos;
                Mouse.Capture(pixelImage);
                e.Handled = true;
            }
        }

        private void PixelImage_MouseMove(object sender, MouseEventArgs e)
        {
            if (!IsEventOnCanvas(e)) return;
            if (_bitmap == null || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(pixelImage);
            int x = (int)Math.Max(0, Math.Min(_bitmap.PixelWidth - 1, pos.X));
            int y = (int)Math.Max(0, Math.Min(_bitmap.PixelHeight - 1, pos.Y));
            var clampedPos = new Point(x, y);

            if (_isSelecting && _selectionStartPoint.HasValue)
            {
                int x0 = (int)_selectionStartPoint.Value.X;
                int y0 = (int)_selectionStartPoint.Value.Y;
                int x1 = (int)clampedPos.X;
                int y1 = (int)clampedPos.Y;
                int selX = Math.Min(x0, x1);
                int selY = Math.Min(y0, y1);
                int selW = Math.Abs(x1 - x0) + 1;
                int selH = Math.Abs(y1 - y0) + 1;
                if (selW > 1 && selH > 1)
                {
                    _selectionRect = new Int32Rect(selX, selY, selW, selH);
                    UpdateSelectionVisual();
                }
                else
                {
                    _selectionRect = null;
                    _selectionVisual.Visibility = Visibility.Collapsed;
                }
                e.Handled = true;
                return;
            }
            else if (_isMovingSelection && _moveStartPoint.HasValue && _moveStartRect.HasValue && _selectionPixels != null)
            {
                int dx = x - (int)_moveStartPoint.Value.X;
                int dy = y - (int)_moveStartPoint.Value.Y;
                var orig = _moveStartRect.Value;
                var newRect = new Int32Rect(orig.X + dx, orig.Y + dy, orig.Width, orig.Height);
                _selectionRect = newRect;
                UpdateSelectionVisual();
                e.Handled = true;
                return;
            }
            else if (_isDrawing || _isErasing)
            {
                Color drawColor = _isErasing ? Colors.White : primaryColor;
                DrawLine((int)_lastPoint.X, (int)_lastPoint.Y, (int)clampedPos.X, (int)clampedPos.Y, _brushSize, drawColor);
                _lastPoint = clampedPos;
                e.Handled = true;
            }
        }

        private void PixelImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!IsEventOnCanvas(e)) return;
            if (_bitmap == null) return;
            if (_isSelecting)
            {
                _isSelecting = false;
                _selectionStartPoint = null;
                Mouse.Capture(null);
                if (_selectionRect.HasValue && _selectionRect.Value.Width > 1 && _selectionRect.Value.Height > 1)
                {
                    UpdateSelectionVisual();
                }
                else
                {
                    _selectionRect = null;
                    _selectionVisual.Visibility = Visibility.Collapsed;
                }
                e.Handled = true;
                return;
            }
            if (_isMovingSelection && _moveStartPoint.HasValue && _moveStartRect.HasValue && _selectionPixels != null)
            {
                _isMovingSelection = false;
                Mouse.Capture(null);
                var oldRect = _moveStartRect.Value;
                var newRect = _selectionRect.Value;
                int[] pixels = new int[_bitmap.PixelWidth * _bitmap.PixelHeight];
                _bitmap.CopyPixels(pixels, _bitmap.PixelWidth * 4, 0);
                // Clear old area
                for (int y = 0; y < oldRect.Height; y++)
                    for (int x = 0; x < oldRect.Width; x++)
                        pixels[(oldRect.Y + y) * _bitmap.PixelWidth + (oldRect.X + x)] = (Colors.White.A << 24) | (Colors.White.R << 16) | (Colors.White.G << 8) | Colors.White.B;
                // Paste to new area
                for (int y = 0; y < newRect.Height; y++)
                    for (int x = 0; x < newRect.Width; x++)
                    {
                        int destX = newRect.X + x;
                        int destY = newRect.Y + y;
                        if (destX >= 0 && destX < _bitmap.PixelWidth && destY >= 0 && destY < _bitmap.PixelHeight)
                        {
                            int dstIdx = destY * _bitmap.PixelWidth + destX;
                            int srcIdx = y * newRect.Width + x;
                            if (srcIdx < _selectionPixels.Length)
                                pixels[dstIdx] = _selectionPixels[srcIdx];
                        }
                    }
                _bitmap.WritePixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), pixels, _bitmap.PixelWidth * 4, 0);
                pixelImage.Source = _bitmap;
                UpdateSelectionVisual();
                PushUndo();
                _moveStartPoint = null;
                _moveStartRect = null;
                _selectionPixels = null;
                e.Handled = true;
                return;
            }
            if (_shapeStartPoint.HasValue && Shapes.IsChecked == true && _currentShape != ShapeType.None)
            {
                var start = _shapeStartPoint.Value;
                var end = e.GetPosition(pixelImage);
                int x0 = (int)Math.Max(0, Math.Min(_bitmap.PixelWidth - 1, start.X));
                int y0 = (int)Math.Max(0, Math.Min(_bitmap.PixelHeight - 1, start.Y));
                int x1 = (int)Math.Max(0, Math.Min(_bitmap.PixelWidth - 1, end.X));
                int y1 = (int)Math.Max(0, Math.Min(_bitmap.PixelHeight - 1, end.Y));
                DrawShape(_currentShape, x0, y0, x1, y1, _brushSize, primaryColor);
                _shapeStartPoint = null;
                PushUndo();
                Mouse.Capture(null);
                e.Handled = true;
                return;
            }
        }

        private void DrawShape(ShapeType shape, int x0, int y0, int x1, int y1, int thickness, Color color)
        {
            switch (shape)
            {
                case ShapeType.Rectangle:
                    DrawLine(x0, y0, x1, y0, thickness, color); // Top
                    DrawLine(x1, y0, x1, y1, thickness, color); // Right
                    DrawLine(x1, y1, x0, y1, thickness, color); // Bottom
                    DrawLine(x0, y1, x0, y0, thickness, color); // Left
                    break;
                case ShapeType.Ellipse:
                    DrawEllipse(x0, y0, x1, y1, thickness, color);
                    break;
                case ShapeType.Line:
                    DrawLine(x0, y0, x1, y1, thickness, color);
                    break;
            }
        }

        private void DrawEllipse(int x0, int y0, int x1, int y1, int thickness, Color color)
        {
            if (_bitmap == null) return;
            int left = Math.Min(x0, x1);
            int right = Math.Max(x0, x1);
            int top = Math.Min(y0, y1);
            int bottom = Math.Max(y0, y1);
            int w = right - left;
            int h = bottom - top;
            if (w < 1 || h < 1) return;

            int cx = left + w / 2;
            int cy = top + h / 2;
            double rx = w / 2.0;
            double ry = h / 2.0;
            int c = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;

            int stride = _bitmap.PixelWidth * 4;
            int[] pixels = new int[_bitmap.PixelWidth * _bitmap.PixelHeight];
            _bitmap.CopyPixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), pixels, stride, 0);

            for (int y = top; y <= bottom; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    double dx = (x - cx) / rx;
                    double dy = (y - cy) / ry;
                    double dist = dx * dx + dy * dy;
                    if (dist <= 1.0 && dist >= 1.0 - (thickness / Math.Max(rx, ry)))
                    {
                        int idx = y * _bitmap.PixelWidth + x;
                        if (idx >= 0 && idx < pixels.Length)
                            pixels[idx] = c;
                    }
                }
            }
            _bitmap.WritePixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), pixels, stride, 0);
        }

        private void UpdateSelectionVisual()
        {
            if (_selectionRect.HasValue && _selectionVisual != null && _bitmap != null)
            {
                var rect = _selectionRect.Value;
                var topLeft = pixelImage.TranslatePoint(new Point(rect.X, rect.Y), CanvasOverlayGrid);
                double zoom = _currentZoomLevel;
                Canvas.SetLeft(_selectionVisual, topLeft.X / zoom);
                Canvas.SetTop(_selectionVisual, topLeft.Y / zoom);
                _selectionVisual.Width = rect.Width / zoom;
                _selectionVisual.Height = rect.Height / zoom;
                _selectionVisual.Visibility = Visibility.Visible;
            }
            else if (_selectionVisual != null)
            {
                _selectionVisual.Visibility = Visibility.Collapsed;
            }
        }

        private void DrawCircle(int cx, int cy, int radius, Color color)
        {
            if (_bitmap == null || radius < 1) return;

            int w = _bitmap.PixelWidth;
            int h = _bitmap.PixelHeight;
            int r_squared = radius * radius;
            int c = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;

            // Determine bounding box, clamping to bitmap dimensions
            int minX = Math.Max(0, cx - radius);
            int minY = Math.Max(0, cy - radius);
            int maxX = Math.Min(w, cx + radius + 1); // Exclusive upper bound
            int maxY = Math.Min(h, cy + radius + 1); // Exclusive upper bound

            if (minX >= maxX || minY >= maxY) return; // Nothing to draw

            int regionW = maxX - minX;
            int regionH = maxY - minY;
            var rect = new Int32Rect(minX, minY, regionW, regionH);
            int stride = regionW * 4;
            int[] regionPixels = new int[regionW * regionH];

            try
            {
                _bitmap.CopyPixels(rect, regionPixels, stride, 0);

                for (int y = 0; y < regionH; y++)
                {
                    for (int x = 0; x < regionW; x++)
                    {
                        int globalX = minX + x;
                        int globalY = minY + y;
                        int dx = globalX - cx;
                        int dy = globalY - cy;

                        if (dx * dx + dy * dy <= r_squared)
                        {
                            int idx = y * regionW + x;
                            regionPixels[idx] = c;
                        }
                    }
                }

                _bitmap.WritePixels(rect, regionPixels, stride, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error drawing circle: {ex.Message}");
            }
        }

        private void DrawLine(int x0, int y0, int x1, int y1, int radius, Color color)
        {
            if (_bitmap == null || radius < 1) return;

            // Bresenham's line algorithm adaptation to draw thick lines by drawing circles along the path
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, e2; /* error value e_xy */

            // Calculate bounding box for the entire line segment + radius
            int minX = Math.Max(0, Math.Min(x0, x1) - radius);
            int minY = Math.Max(0, Math.Min(y0, y1) - radius);
            int maxX = Math.Min(_bitmap.PixelWidth, Math.Max(x0, x1) + radius + 1);
            int maxY = Math.Min(_bitmap.PixelHeight, Math.Max(y0, y1) + radius + 1);

            if (minX >= maxX || minY >= maxY) return;

            int regionW = maxX - minX;
            int regionH = maxY - minY;
            var rect = new Int32Rect(minX, minY, regionW, regionH);
            int stride = regionW * 4;
            int[] regionPixels = new int[regionW * regionH];
            int c_int = (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;
            int r_squared = radius * radius;

            try
            {
                _bitmap.CopyPixels(rect, regionPixels, stride, 0);

                while (true)
                {
                    // Draw circle at current point (x0, y0) within the regionPixels buffer
                    for (int y = Math.Max(0, y0 - radius - minY); y < Math.Min(regionH, y0 + radius + 1 - minY); y++)
                    {
                        for (int x = Math.Max(0, x0 - radius - minX); x < Math.Min(regionW, x0 + radius + 1 - minX); x++)
                        {
                            int globalX = minX + x;
                            int globalY = minY + y;
                            int dist_dx = globalX - x0;
                            int dist_dy = globalY - y0;

                            if (dist_dx * dist_dx + dist_dy * dist_dy <= r_squared)
                            {
                                int idx = y * regionW + x;
                                regionPixels[idx] = c_int;
                            }
                        }
                    }

                    if (x0 == x1 && y0 == y1) break;
                    e2 = 2 * err;
                    if (e2 >= dy) { err += dy; x0 += sx; } /* e_xy+e_x > 0 */
                    if (e2 <= dx) { err += dx; y0 += sy; } /* e_xy+e_y < 0 */
                }

                _bitmap.WritePixels(rect, regionPixels, stride, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error drawing line: {ex.Message}");
            }
        }


        private void FloodFill(int startX, int startY, Color fillColor)
        {
            if (_bitmap == null || startX < 0 || startX >= _bitmap.PixelWidth || startY < 0 || startY >= _bitmap.PixelHeight)
                return;

            int w = _bitmap.PixelWidth;
            int h = _bitmap.PixelHeight;
            int[] pixels = new int[w * h];
            _bitmap.CopyPixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);

            int targetColorArgb = pixels[startY * w + startX];
            int fillColorArgb = (fillColor.A << 24) | (fillColor.R << 16) | (fillColor.G << 8) | fillColor.B;

            if (targetColorArgb == fillColorArgb)
                return; // No need to fill if target is already the fill color

            Queue<(int x, int y)> queue = new Queue<(int, int)>();
            queue.Enqueue((startX, startY));

            while (queue.Count > 0)
            {
                (int x, int y) = queue.Dequeue();

                if (x < 0 || x >= w || y < 0 || y >= h)
                    continue;

                int pixelIndex = y * w + x;
                if (pixels[pixelIndex] == targetColorArgb)
                {
                    pixels[pixelIndex] = fillColorArgb;

                    // Enqueue neighbors
                    queue.Enqueue((x + 1, y));
                    queue.Enqueue((x - 1, y));
                    queue.Enqueue((x, y + 1));
                    queue.Enqueue((x, y - 1));
                }
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
        }

        private void PickColor(int x, int y)
        {
            if (_bitmap == null || x < 0 || x >= _bitmap.PixelWidth || y < 0 || y >= _bitmap.PixelHeight)
                return;

            int[] pixel = new int[1];
            try
            {
                _bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
                int argb = pixel[0];
                Color picked = Color.FromArgb((byte)((argb >> 24) & 0xFF),
                                              (byte)((argb >> 16) & 0xFF),
                                              (byte)((argb >> 8) & 0xFF),
                                              (byte)(argb & 0xFF));

                if (Color1.IsChecked == true)
                {
                    primaryColor = picked;
                    Color1.Foreground = new SolidColorBrush(primaryColor);
                }
                else if (Color2.IsChecked == true)
                {
                    secondaryColor = picked;
                    Color2.Foreground = new SolidColorBrush(secondaryColor);
                }

                // Optionally switch back to the previous tool (e.g., Pencil)
                Pencil.IsChecked = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error picking color: {ex.Message}");
            }
        }


        private void CanExecuteUndo(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _undoStack.Count > 1; // Need more than the initial state to undo
        }

        private void ExecuteUndo(object sender, ExecutedRoutedEventArgs e)
        {
            if (_undoStack.Count > 1)
            {
                _redoStack.Push(_undoStack.Pop()); // Move current state to redo
                _bitmap = _undoStack.Peek().Clone(); // Get previous state
                pixelImage.Source = _bitmap;
                // Reset selection after undo/redo
                _selectionRect = null;
                _selectionVisual.Visibility = Visibility.Collapsed;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void CanExecuteRedo(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _redoStack.Count > 0;
        }

        private void ExecuteRedo(object sender, ExecutedRoutedEventArgs e)
        {
            if (_redoStack.Count > 0)
            {
                var bmpToRestore = _redoStack.Pop();
                _undoStack.Push(bmpToRestore.Clone()); // Push restored state back to undo
                _bitmap = bmpToRestore.Clone();
                pixelImage.Source = _bitmap;
                // Reset selection after undo/redo
                _selectionRect = null;
                _selectionVisual.Visibility = Visibility.Collapsed;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void CanExecuteCutCopy(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _bitmap != null && _selectionRect.HasValue && _selectionRect.Value.Width > 0 && _selectionRect.Value.Height > 0;
        }

        private void ExecuteCut(object sender, ExecutedRoutedEventArgs e)
        {
            if (_bitmap == null || !_selectionRect.HasValue) return;

            var rect = _selectionRect.Value;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // Copy first
            ExecuteCopy(sender, e); // This sets _clipboardBitmap

            // Then clear the area (fill with white)
            int[] pixels = new int[_bitmap.PixelWidth * _bitmap.PixelHeight];
            _bitmap.CopyPixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), pixels, _bitmap.PixelWidth * 4, 0);
            int whiteArgb = (Colors.White.A << 24) | (Colors.White.R << 16) | (Colors.White.G << 8) | Colors.White.B;

            for (int y = 0; y < rect.Height; y++)
            {
                for (int x = 0; x < rect.Width; x++)
                {
                    int sx = rect.X + x;
                    int sy = rect.Y + y;
                    if (sx >= 0 && sx < _bitmap.PixelWidth && sy >= 0 && sy < _bitmap.PixelHeight)
                    {
                        int idx = sy * _bitmap.PixelWidth + sx;
                        pixels[idx] = whiteArgb;
                    }
                }
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), pixels, _bitmap.PixelWidth * 4, 0);
            // pixelImage.Source = _bitmap; // Already updated by WritePixels

            // Clear selection after cut
            _selectionRect = null;
            _selectionVisual.Visibility = Visibility.Collapsed;

            PushUndo();
            CommandManager.InvalidateRequerySuggested(); // Update Paste command state
        }

        private void ExecuteCopy(object sender, ExecutedRoutedEventArgs e)
        {
            if (_bitmap == null || !_selectionRect.HasValue) return;

            var rect = _selectionRect.Value;
            if (rect.Width <= 0 || rect.Height <= 0) return;

            try
            {
                _clipboardBitmap = new WriteableBitmap(rect.Width, rect.Height, 96, 96, PixelFormats.Bgra32, null);
                // Source rect needs to be clamped to the source bitmap dimensions
                var clampedSourceRect = new Int32Rect(
                    Math.Max(0, rect.X),
                    Math.Max(0, rect.Y),
                    Math.Min(rect.Width, _bitmap.PixelWidth - Math.Max(0, rect.X)),
                    Math.Min(rect.Height, _bitmap.PixelHeight - Math.Max(0, rect.Y))
                );

                if (clampedSourceRect.Width <= 0 || clampedSourceRect.Height <= 0)
                {
                    _clipboardBitmap = null; // Nothing to copy
                    return;
                }

                // Adjust clipboard bitmap size if source was clamped
                if (clampedSourceRect.Width != rect.Width || clampedSourceRect.Height != rect.Height)
                {
                    _clipboardBitmap = new WriteableBitmap(clampedSourceRect.Width, clampedSourceRect.Height, 96, 96, PixelFormats.Bgra32, null);
                }


                int stride = clampedSourceRect.Width * 4;
                int[] buffer = new int[clampedSourceRect.Width * clampedSourceRect.Height];

                _bitmap.CopyPixels(clampedSourceRect, buffer, stride, 0);
                _clipboardBitmap.WritePixels(new Int32Rect(0, 0, clampedSourceRect.Width, clampedSourceRect.Height), buffer, stride, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error copying selection: {ex.Message}");
                _clipboardBitmap = null; // Ensure clipboard is null on error
            }
            CommandManager.InvalidateRequerySuggested(); // Update Paste command state
        }


        private void CanExecutePaste(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _clipboardBitmap != null;
        }

        private void ExecutePaste(object sender, ExecutedRoutedEventArgs e)
        {
            if (_bitmap == null || _clipboardBitmap == null) return;

            // Default paste location (top-left)
            int pasteX = 0;
            int pasteY = 0;

            // If there's an active selection, paste at its top-left
            if (_selectionRect.HasValue)
            {
                pasteX = _selectionRect.Value.X;
                pasteY = _selectionRect.Value.Y;
            }

            int clipW = _clipboardBitmap.PixelWidth;
            int clipH = _clipboardBitmap.PixelHeight;
            int[] srcPixels = new int[clipW * clipH];
            _clipboardBitmap.CopyPixels(new Int32Rect(0, 0, clipW, clipH), srcPixels, clipW * 4, 0);

            // Determine the actual area to write on the destination bitmap
            int targetX = Math.Max(0, pasteX);
            int targetY = Math.Max(0, pasteY);
            int targetW = Math.Min(clipW, _bitmap.PixelWidth - targetX);
            int targetH = Math.Min(clipH, _bitmap.PixelHeight - targetY);

            if (targetW <= 0 || targetH <= 0) return; // Nothing to paste within bounds

            int[] dstPixels = new int[_bitmap.PixelWidth * _bitmap.PixelHeight];
            _bitmap.CopyPixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), dstPixels, _bitmap.PixelWidth * 4, 0);

            // Copy pixels from clipboard buffer to destination buffer
            for (int y = 0; y < targetH; y++)
            {
                for (int x = 0; x < targetW; x++)
                {
                    int srcIdx = y * clipW + x; // Index in clipboard buffer
                    int dstIdx = (targetY + y) * _bitmap.PixelWidth + (targetX + x); // Index in destination buffer

                    if (srcIdx < srcPixels.Length && dstIdx < dstPixels.Length) // Bounds check
                    {
                        dstPixels[dstIdx] = srcPixels[srcIdx];
                    }
                }
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), dstPixels, _bitmap.PixelWidth * 4, 0);
            // pixelImage.Source = _bitmap; // Updated by WritePixels

            // Create a new selection around the pasted area
            _selectionRect = new Int32Rect(targetX, targetY, targetW, targetH);
            _selectionVisual.Visibility = Visibility.Visible;
            var topLeft = pixelImage.TranslatePoint(new Point(targetX, targetY), CanvasOverlayGrid);
            double zoom = _currentZoomLevel;
            Canvas.SetLeft(_selectionVisual, topLeft.X / zoom);
            Canvas.SetTop(_selectionVisual, topLeft.Y / zoom);
            _selectionVisual.Width = targetW / zoom;
            _selectionVisual.Height = targetH / zoom;
            Selection.IsChecked = true; // Activate selection tool

            PushUndo();
        }


        private void CanvasHost_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (_bitmap == null || e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;

            int newW = (int)e.NewSize.Width;
            int newH = (int)e.NewSize.Height;
            int oldW = _bitmap.PixelWidth;
            int oldH = _bitmap.PixelHeight;

            if (newW == oldW && newH == oldH) return;

            var newBmp = new WriteableBitmap(newW, newH, 96, 96, PixelFormats.Bgra32, null);

            ClearBitmap(newBmp, Colors.White);

            int copyW = Math.Min(oldW, newW);
            int copyH = Math.Min(oldH, newH);
            if (copyW > 0 && copyH > 0)
            {
                int[] buffer = new int[copyW * copyH];
                int stride = copyW * 4;
                try
                {
                    _bitmap.CopyPixels(new Int32Rect(0, 0, copyW, copyH), buffer, stride, 0);
                    newBmp.WritePixels(new Int32Rect(0, 0, copyW, copyH), buffer, stride, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error copying pixels during resize: {ex.Message}");
                }
            }

            _bitmap = newBmp;
            pixelImage.Source = _bitmap;

            UpdateSelectionVisualAfterResize();
            UpdateThumbPositions();
        }
        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb || _bitmap == null) return;

            double minWidth = 50, minHeight = 50;
            double currentWidth = CanvasHost.ActualWidth;
            double currentHeight = CanvasHost.ActualHeight;
            double newWidth = currentWidth;
            double newHeight = currentHeight;

            // Adjust width/height based on which thumb is dragged
            switch (thumb.Name)
            {
                case "Thumb_Right":
                case "Thumb_TopRight":
                case "Thumb_BottomRight":
                    newWidth = Math.Max(minWidth, currentWidth + e.HorizontalChange);
                    break;
                case "Thumb_Left":
                case "Thumb_TopLeft":
                case "Thumb_BottomLeft":
                    newWidth = Math.Max(minWidth, currentWidth - e.HorizontalChange);
                    break;
            }

            switch (thumb.Name)
            {
                case "Thumb_Bottom":
                case "Thumb_BottomLeft":
                case "Thumb_BottomRight":
                    newHeight = Math.Max(minHeight, currentHeight + e.VerticalChange);
                    break;
                case "Thumb_Top":
                case "Thumb_TopLeft":
                case "Thumb_TopRight":
                    newHeight = Math.Max(minHeight, currentHeight - e.VerticalChange);
                    break;
            }

            // Apply new dimensions (CanvasHost_SizeChanged will handle bitmap update)
            CanvasHost.Width = newWidth;
            CanvasHost.Height = newHeight;

            // Crucially, UpdateThumbPositions needs to be called *during* drag
            UpdateThumbPositions();
            UpdateSelectionVisualAfterResize(); // Keep selection visual clamped
        }


        private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            // The CanvasHost_SizeChanged event already handles the bitmap resize logic.
            // We just need to ensure the final state is pushed to the undo stack.
            PushUndo();
            UpdateThumbPositions(); // Final position update
            UpdateSelectionVisualAfterResize(); // Final clamp
        }

        private void UpdateSelectionVisualAfterResize()
        {
            if (_selectionRect.HasValue && _selectionVisual != null && _bitmap != null)
            {
                var rect = _selectionRect.Value;
                int maxW = _bitmap.PixelWidth;
                int maxH = _bitmap.PixelHeight;
                int x = Math.Max(0, Math.Min(rect.X, maxW));
                int y = Math.Max(0, Math.Min(rect.Y, maxH));
                int w = Math.Max(0, Math.Min(rect.Width, maxW - x));
                int h = Math.Max(0, Math.Min(rect.Height, maxH - y));
                if (w > 0 && h > 0)
                {
                    _selectionRect = new Int32Rect(x, y, w, h);
                    _selectionVisual.Visibility = Visibility.Visible;
                    var topLeft = pixelImage.TranslatePoint(new Point(x, y), CanvasOverlayGrid);
                    double zoom = _currentZoomLevel;
                    Canvas.SetLeft(_selectionVisual, topLeft.X / zoom);
                    Canvas.SetTop(_selectionVisual, topLeft.Y / zoom);
                    _selectionVisual.Width = w / zoom;
                    _selectionVisual.Height = h / zoom;
                }
                else
                {
                    _selectionRect = null;
                    _selectionVisual.Visibility = Visibility.Collapsed;
                }
            }
            else if (_selectionVisual != null)
            {
                _selectionVisual.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyZoomTransformation()
        {
            if (CanvasOverlayGrid == null) return;

            if (CanvasOverlayGrid.RenderTransform is not ScaleTransform st)
            {
                st = new ScaleTransform(1.0, 1.0);
                CanvasOverlayGrid.RenderTransform = st;
                CanvasOverlayGrid.RenderTransformOrigin = new Point(0.5, 0.5); // Zoom from center
            }
            st.ScaleX = _currentZoomLevel;
            st.ScaleY = _currentZoomLevel;

            // Update thumb positions after zoom changes scale
            UpdateThumbPositions();
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                double zoomDelta = e.Delta > 0 ? 0.1 : -0.1;
                double newZoomLevel = _currentZoomLevel + zoomDelta;
                _currentZoomLevel = Math.Max(0.1, Math.Min(5.0, newZoomLevel));
                ApplyZoomTransformation();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                var scrollViewer = FindScrollViewer(this);
                if (scrollViewer != null)
                {
                    double offset = scrollViewer.HorizontalOffset - e.Delta;
                    scrollViewer.ScrollToHorizontalOffset(offset);
                    e.Handled = true;
                }
            }
        }
        private ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            if (parent is ScrollViewer sv)
                return sv;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                var result = FindScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void Image_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button sourceButton) return;

            var menu = new ContextMenu();
            var rotate90 = new MenuItem { Header = "Rotate 90° Right" };
            var rotate180 = new MenuItem { Header = "Rotate 180°" };
            var rotate270 = new MenuItem { Header = "Rotate 90° Left" }; // Same as 270 clockwise
            // Add Flip options later if needed

            rotate90.Click += (s, ev) => RotateCanvas(90);
            rotate180.Click += (s, ev) => RotateCanvas(180);
            rotate270.Click += (s, ev) => RotateCanvas(270); // Or -90

            menu.Items.Add(rotate90);
            menu.Items.Add(rotate180);
            menu.Items.Add(rotate270);

            // Open context menu relative to the button
            menu.PlacementTarget = sourceButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void RotateCanvas(double angle)
        {
            if (_bitmap == null) return;

            // Use TransformedBitmap for rotation preview/calculation
            TransformedBitmap tb = new TransformedBitmap();
            tb.BeginInit();
            tb.Source = _bitmap; // Use current bitmap as source
            tb.Transform = new RotateTransform(angle);
            tb.EndInit();

            // Render the transformed bitmap to a new WriteableBitmap
            var rotatedBitmap = new WriteableBitmap(tb);

            // Update canvas size and bitmap reference
            int newWidth = rotatedBitmap.PixelWidth;
            int newHeight = rotatedBitmap.PixelHeight;

            CanvasHost.Width = newWidth;
            CanvasHost.Height = newHeight;
            // CanvasHost_SizeChanged will trigger, but we need to set the *content* here

            _bitmap = rotatedBitmap; // Assign the newly rotated bitmap
            pixelImage.Source = _bitmap; // Update the image display

            // Reset selection after rotation
            _selectionRect = null;
            _selectionVisual.Visibility = Visibility.Collapsed;

            PushUndo(); // Save the rotated state
            UpdateThumbPositions(); // Update thumbs for new dimensions
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_bitmap == null) return;

            var dlg = new SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPEG Image|*.jpg;*.jpeg|Bitmap Image|*.bmp|GIF Image|*.gif",
                DefaultExt = ".png",
                Title = "Save Image As"
            };

            if (dlg.ShowDialog() == true)
            {
                BitmapEncoder? encoder = null;
                string ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();

                switch (ext)
                {
                    case ".png":
                        encoder = new PngBitmapEncoder();
                        break;
                    case ".jpg":
                    case ".jpeg":
                        encoder = new JpegBitmapEncoder { QualityLevel = 90 }; // Example quality
                        break;
                    case ".bmp":
                        encoder = new BmpBitmapEncoder();
                        break;
                    case ".gif":
                        encoder = new GifBitmapEncoder();
                        break;
                    default:
                        MessageBox.Show("Unsupported file format selected.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                }

                if (encoder != null)
                {
                    try
                    {
                        encoder.Frames.Add(BitmapFrame.Create(_bitmap));
                        using (var fs = new FileStream(dlg.FileName, FileMode.Create))
                        {
                            encoder.Save(fs);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            Save_Click(sender, e); // "Save As" behaves like "Save" here
        }

        private void ImportMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // "Import" usually means adding to the *current* canvas, not replacing it.
            // Let's implement it as adding the image at (0,0) or selection top-left.
            var dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
                Title = "Import Image"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var img = new BitmapImage(new Uri(dlg.FileName));
                    var importedBitmap = new WriteableBitmap(img); // Convert imported image

                    if (_bitmap == null) // If canvas is somehow null, treat as Open
                    {
                        OpenImageFile(dlg.FileName);
                        return;
                    }

                    // Paste logic similar to ExecutePaste
                    int pasteX = 0;
                    int pasteY = 0;
                    if (_selectionRect.HasValue)
                    {
                        pasteX = _selectionRect.Value.X;
                        pasteY = _selectionRect.Value.Y;
                    }

                    int clipW = importedBitmap.PixelWidth;
                    int clipH = importedBitmap.PixelHeight;
                    int[] srcPixels = new int[clipW * clipH];
                    importedBitmap.CopyPixels(new Int32Rect(0, 0, clipW, clipH), srcPixels, clipW * 4, 0);

                    int targetX = Math.Max(0, pasteX);
                    int targetY = Math.Max(0, pasteY);
                    int targetW = Math.Min(clipW, _bitmap.PixelWidth - targetX);
                    int targetH = Math.Min(clipH, _bitmap.PixelHeight - targetY);

                    if (targetW <= 0 || targetH <= 0) return; // Paste location out of bounds

                    int[] dstPixels = new int[_bitmap.PixelWidth * _bitmap.PixelHeight];
                    _bitmap.CopyPixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), dstPixels, _bitmap.PixelWidth * 4, 0);

                    for (int y = 0; y < targetH; y++)
                    {
                        for (int x = 0; x < targetW; x++)
                        {
                            int srcIdx = y * clipW + x;
                            int dstIdx = (targetY + y) * _bitmap.PixelWidth + (targetX + x);
                            if (srcIdx < srcPixels.Length && dstIdx < dstPixels.Length)
                            {
                                dstPixels[dstIdx] = srcPixels[srcIdx];
                            }
                        }
                    }

                    _bitmap.WritePixels(new Int32Rect(0, 0, _bitmap.PixelWidth, _bitmap.PixelHeight), dstPixels, _bitmap.PixelWidth * 4, 0);
                    // pixelImage.Source = _bitmap; // Updated by WritePixels

                    PushUndo();

                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error importing image: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            // Optional: Prompt user to save changes if needed
            // if (HasUnsavedChanges()) { ... }

            InitBitmap((int)CanvasHost.Width, (int)CanvasHost.Height); // Reinitialize with current size
            _undoStack.Clear();
            _redoStack.Clear();
            PushUndo(); // Push the new blank state
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            // Optional: Prompt user to save changes if needed
            // if (HasUnsavedChanges()) { ... }

            var dlg = new OpenFileDialog
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
                Title = "Open Image"
            };

            if (dlg.ShowDialog() == true)
            {
                OpenImageFile(dlg.FileName);
            }
        }

        private void OpenImageFile(string filePath)
        {
            try
            {
                var img = new BitmapImage();
                // Use BeginInit/EndInit for better control and error handling
                img.BeginInit();
                img.UriSource = new Uri(filePath);
                img.CacheOption = BitmapCacheOption.OnLoad; // Load immediately
                img.EndInit();

                // Create WriteableBitmap from the loaded BitmapImage
                var wb = new WriteableBitmap(img);

                // Resize CanvasHost to match the opened image dimensions
                CanvasHost.Width = wb.PixelWidth;
                CanvasHost.Height = wb.PixelHeight;

                // Assign the new bitmap AFTER resizing CanvasHost
                _bitmap = wb;
                pixelImage.Source = _bitmap;

                // Clear undo/redo history for the new file
                _undoStack.Clear();
                _redoStack.Clear();
                PushUndo(); // Push the loaded image state

                // Reset selection
                _selectionRect = null;
                _selectionVisual.Visibility = Visibility.Collapsed;

                UpdateThumbPositions(); // Update thumbs for new size
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening file: {ex.Message}", "Open Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void UpdateThumbPositions()
        {
            if (!CanvasHost.IsMeasureValid || !CanvasHost.IsArrangeValid) return; // Wait for layout

            var host = CanvasHost;
            var parent = VisualTreeHelper.GetParent(host) as UIElement; // CanvasOverlayGrid or ScrollViewer ContentPresenter
            if (parent == null) return;

            try
            {
                // Get position relative to the immediate parent that handles layout (often the Grid/Canvas)
                var transform = host.TransformToAncestor(parent);
                var pos = transform.Transform(new Point(0, 0));
                double w = host.ActualWidth;
                double h = host.ActualHeight;
                double thumbOffsetX = -Thumb_TopLeft.Width / 2; // Assuming all thumbs are same size
                double thumbOffsetY = -Thumb_TopLeft.Height / 2;

                // Set thumb positions using Canvas.SetLeft/Top
                SetThumbPos(Thumb_TopLeft, pos.X + thumbOffsetX, pos.Y + thumbOffsetY);
                SetThumbPos(Thumb_TopRight, pos.X + w + thumbOffsetX, pos.Y + thumbOffsetY);
                SetThumbPos(Thumb_BottomLeft, pos.X + thumbOffsetX, pos.Y + h + thumbOffsetY);
                SetThumbPos(Thumb_BottomRight, pos.X + w + thumbOffsetX, pos.Y + h + thumbOffsetY);
                SetThumbPos(Thumb_Top, pos.X + w / 2 + thumbOffsetX, pos.Y + thumbOffsetY);
                SetThumbPos(Thumb_Bottom, pos.X + w / 2 + thumbOffsetX, pos.Y + h + thumbOffsetY);
                SetThumbPos(Thumb_Left, pos.X + thumbOffsetX, pos.Y + h / 2 + thumbOffsetY);
                SetThumbPos(Thumb_Right, pos.X + w + thumbOffsetX, pos.Y + h / 2 + thumbOffsetY);
            }
            catch (InvalidOperationException ex)
            {
                // Can happen if the ancestor is not found or layout is in progress
                System.Diagnostics.Debug.WriteLine($"Error updating thumb positions: {ex.Message}");
            }

        }

        private void SetThumbPos(Thumb thumb, double x, double y)
        {
            if (thumb == null) return;
            Canvas.SetLeft(thumb, x);
            Canvas.SetTop(thumb, y);
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Foreground is SolidColorBrush brush)
            {
                Color selectedColor = brush.Color;
                if (Color1.IsChecked == true)
                {
                    primaryColor = selectedColor;
                    Color1.Foreground = brush; // Update the toggle button visual
                }
                else if (Color2.IsChecked == true)
                {
                    secondaryColor = selectedColor;
                    Color2.Foreground = brush; // Update the toggle button visual
                }
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Optional: Prompt to save unsaved changes
            // if (HasUnsavedChanges()) { ... }
            Application.Current.Shutdown();
        }

        private void MainWindow_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Only release mouse capture and reset state if the mouse is captured by pixelImage
            if (Mouse.Captured == pixelImage)
            {
                Mouse.Capture(null);
                _isDrawing = false;
                _isErasing = false;
                _isSelecting = false;
                _isMovingSelection = false;
            }
        }
    }
}