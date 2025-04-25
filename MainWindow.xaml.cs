using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace MSPaintProj
{
    public partial class MainWindow : Window
    {
        private List<ToggleButton> _toolToggleButtons = new();
        private List<ToggleButton> _colorToggleButtons = new();
        private Stack<StrokeCollection> _undoStack = new();
        private Stack<StrokeCollection> _redoStack = new();

        private Color primaryColor = Colors.Black;
        private Color secondaryColor = Colors.White;
        private bool _isHandlingColorToggle = false;
        private double _currentZoomLevel = 1.0; 

        public MainWindow()
        {
            InitializeComponent();
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, ExecuteUndo, CanExecuteUndo));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, ExecuteRedo, CanExecuteRedo));
            InitializeToggleButtons();
            AttachCheckedEventHandlers();
            Color1.Foreground = new SolidColorBrush(primaryColor);
            Color2.Foreground = new SolidColorBrush(secondaryColor);
            Color1.IsChecked = true;
            inkCanvas.DefaultDrawingAttributes.Color = primaryColor;

            UpdateBrushSize(BrushSizeSlider.Value); 

            Pencil.IsChecked = true; 
            CanvasHost.SizeChanged += CanvasHost_SizeChanged;

            inkCanvas.StrokeCollected += InkCanvas_StrokeChanged;
            inkCanvas.StrokeErased += InkCanvas_StrokeChanged;
            _undoStack.Push(new StrokeCollection());

            ApplyZoom(); 
        }

        private void InkCanvas_StrokeChanged(object sender, EventArgs e)
        {
            var current = new StrokeCollection(inkCanvas.Strokes.Select(s => s.Clone()));
            if (_undoStack.Count == 0 || !AreStrokeCollectionsEqual(_undoStack.Peek(), current))
            {
                _undoStack.Push(current);
            }
            _redoStack.Clear();
        }

     private bool AreStrokeCollectionsEqual(StrokeCollection a, StrokeCollection b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!a[i].GetBounds().Equals(b[i].GetBounds()))
                    return false;
            }
            return true;
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
            }

            Color1.Checked += ColorToggleButton_Checked;
            Color2.Checked += ColorToggleButton_Checked;
        }

        private void ToolToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            var checkedButton = sender as ToggleButton;
            if (checkedButton == null || !_toolToggleButtons.Contains(checkedButton))
            {
                return;
            }

            foreach(var button in _toolToggleButtons)
            {
                if(button != checkedButton && button.IsChecked == true)
                {
                    button.Checked -= ToolToggleButton_Checked;
                    button.IsChecked = false;
                    button.Checked += ToolToggleButton_Checked;
                }
            }

            if (checkedButton == Pencil || checkedButton == Brush)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                BrushSizePanel.Visibility = Visibility.Visible;
                UpdateBrushSize(BrushSizeSlider.Value); 
            }
            else if (checkedButton == Eraser)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint; 
                BrushSizePanel.Visibility = Visibility.Visible;
                UpdateEraserSize(BrushSizeSlider.Value); 
            }
            else if (checkedButton == Selection)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Select;
                BrushSizePanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.None; 
                BrushSizePanel.Visibility = Visibility.Collapsed;
            }

        }

        private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (inkCanvas == null) return; 

            if (Eraser.IsChecked == true)
            {
                UpdateEraserSize(e.NewValue);
            }
            else 
            {
                UpdateBrushSize(e.NewValue);
            }
        }

        private void UpdateBrushSize(double size)
        {
             if (inkCanvas == null) return;
             inkCanvas.DefaultDrawingAttributes.Width = size;
             inkCanvas.DefaultDrawingAttributes.Height = size;
        }

         private void UpdateEraserSize(double size)
        {
             if (inkCanvas == null) return;
             inkCanvas.EraserShape = new EllipseStylusShape(size, size);
        }


        private void CanExecuteUndo(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = _undoStack.Count > 1;
        }

        private void ExecuteUndo(object sender, ExecutedRoutedEventArgs e)
        {
            if (_undoStack.Count > 1)
            {
                _redoStack.Push(_undoStack.Pop());
                var strokes = _undoStack.Peek();
                inkCanvas.Strokes.Clear();
                foreach (var s in strokes)
                    inkCanvas.Strokes.Add(s.Clone());
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
                var strokes = _redoStack.Pop();
                _undoStack.Push(new StrokeCollection(inkCanvas.Strokes.Select(s => s.Clone())));
                inkCanvas.Strokes.Clear();
                foreach (var s in strokes)
                    inkCanvas.Strokes.Add(s.Clone());
            }
        }

        private void CanvasHost_SizeChanged(object? sender, SizeChangedEventArgs? e)
        {
            double hostLeft = Canvas.GetLeft(CanvasHost);
            double hostTop = Canvas.GetTop(CanvasHost);
            double w = CanvasHost.ActualWidth;
            double h = CanvasHost.ActualHeight;
            double halfW = w / 2;
            double halfH = h / 2;
            double off = Thumb_TopLeft.ActualWidth / 2;

            void Pos(Thumb t, double x, double y)
            {
                Canvas.SetLeft(t, hostLeft + x - off);
                Canvas.SetTop(t, hostTop + y - off);
            }
            Pos(Thumb_TopLeft, 0, 0);
            Pos(Thumb_TopRight, w, 0);
            Pos(Thumb_BottomLeft, 0, h);
            Pos(Thumb_BottomRight, w, h);
            Pos(Thumb_Top, halfW, 0);
            Pos(Thumb_Bottom, halfW, h);
            Pos(Thumb_Left, 0, halfH);
            Pos(Thumb_Right, w, halfH);
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var thumb = (Thumb)sender;
            double w = CanvasHost.Width;
            double h = CanvasHost.Height;
            double dx = e.HorizontalChange;
            double dy = e.VerticalChange;

            double newW = w;
            double newH = h;
            double newLeft = Canvas.GetLeft(CanvasHost);
            double newTop = Canvas.GetTop(CanvasHost);

            if (thumb == Thumb_TopLeft)
            {
                newW = w - dx;
                newH = h - dy;
                newLeft += dx;
                newTop += dy;
            }
            else if (thumb == Thumb_Top)
            {
                newH = h - dy;
                newTop += dy;
            }
            else if (thumb == Thumb_TopRight)
            {
                newW = w + dx;
                newH = h - dy;
                newTop += dy;
            }
            else if (thumb == Thumb_Right)
            {
                newW = w + dx;
            }
            else if (thumb == Thumb_BottomRight)
            {
                newW = w + dx;
                newH = h + dy;
            }
            else if (thumb == Thumb_Bottom)
            {
                newH = h + dy;
            }
            else if (thumb == Thumb_BottomLeft)
            {
                newW = w - dx;
                newH = h + dy;
                newLeft += dx;
            }
            else if (thumb == Thumb_Left)
            {
                newW = w - dx;
                newLeft += dx;
            }

            double finalW = Math.Max(50, newW);
            double finalH = Math.Max(50, newH);

            if (finalW != newW)
            {
                if (thumb == Thumb_TopLeft || thumb == Thumb_Left || thumb == Thumb_BottomLeft)
                {
                    newLeft = Canvas.GetLeft(CanvasHost) + (w - finalW);
                }
            }
            if (finalH != newH)
            {
                 if (thumb == Thumb_TopLeft || thumb == Thumb_Top || thumb == Thumb_TopRight)
                 {
                     newTop = Canvas.GetTop(CanvasHost) + (h - finalH);
                 }
            }

            CanvasHost.Width = finalW;
            CanvasHost.Height = finalH;
            Canvas.SetLeft(CanvasHost, newLeft);
            Canvas.SetTop(CanvasHost, newTop);
        }

        private void OnMouseWheelZoom(object sender, MouseWheelEventArgs e)
        {
            const double step = 0.1;
            double zoomChange = e.Delta > 0 ? step : -step;
            _currentZoomLevel = Math.Max(0.1, Math.Min(5.0, _currentZoomLevel + zoomChange));

            ApplyZoom(); 

            e.Handled = true;
        }

        private void ApplyZoom()
        {
             if (ContainerCanvas.RenderTransform is ScaleTransform scaleTransform)
             {
                 scaleTransform.ScaleX = _currentZoomLevel;
                 scaleTransform.ScaleY = _currentZoomLevel;
             }
             else 
             {
                 ContainerCanvas.RenderTransform = new ScaleTransform(_currentZoomLevel, _currentZoomLevel);
             }
        }

        private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            CropStrokesToBounds();
            CanvasHost_SizeChanged(CanvasHost, null);
        }

        private void CropStrokesToBounds()
        {
            var cropRect = new Rect(0, 0, CanvasHost.ActualWidth, CanvasHost.ActualHeight);
            var toRemove = new StrokeCollection(inkCanvas.Strokes.Where(s => !cropRect.IntersectsWith(s.GetBounds())));

            if (toRemove.Count > 0)
            {
                 inkCanvas.Strokes.Remove(toRemove);
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button clickedButton && clickedButton.Foreground is SolidColorBrush brush)
            {
                Color selectedColor = brush.Color;

                if (Color1.IsChecked == true)
                {
                    primaryColor = selectedColor;
                    Color1.Foreground = new SolidColorBrush(primaryColor);
                    inkCanvas.DefaultDrawingAttributes.Color = primaryColor;
                }
                else 
                {
                    secondaryColor = selectedColor;
                    Color2.Foreground = new SolidColorBrush(secondaryColor);
                }
            }
        }
        private void ColorToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            if (_isHandlingColorToggle) return; 

            _isHandlingColorToggle = true;
            var checkedButton = sender as ToggleButton;

            if (checkedButton == Color1)
            {
                if (Color2.IsChecked == true)
                {
                    Color2.IsChecked = false;
                }
                inkCanvas.DefaultDrawingAttributes.Color = primaryColor; 
            }
            else if (checkedButton == Color2)
            {
                if (Color1.IsChecked == true)
                {
                    Color1.IsChecked = false;
                }
            }
            _isHandlingColorToggle = false;
        }
    }
}
