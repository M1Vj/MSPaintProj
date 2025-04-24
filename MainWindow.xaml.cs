using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MSPaintProj
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            // reposition on load & whenever size changes
            CanvasHost.SizeChanged += CanvasHost_SizeChanged;
        }

        private void CanvasHost_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // grab both size AND offset of the white box
            double hostLeft = Canvas.GetLeft(CanvasHost);
            double hostTop = Canvas.GetTop(CanvasHost);
            double w = CanvasHost.ActualWidth;
            double h = CanvasHost.ActualHeight;
            double halfW = w / 2;
            double halfH = h / 2;
            double off = Thumb_TopLeft.ActualWidth / 2; // 8px→4

            void Pos(Thumb t, double x, double y)
            {
                // now include the host's left/top offset
                Canvas.SetLeft(t, hostLeft + x - off);
                Canvas.SetTop(t, hostTop + y - off);
            }

            // corners
            Pos(Thumb_TopLeft, 0, 0);
            Pos(Thumb_TopRight, w, 0);
            Pos(Thumb_BottomLeft, 0, h);
            Pos(Thumb_BottomRight, w, h);

            // edges
            Pos(Thumb_Top, halfW, 0);
            Pos(Thumb_Bottom, halfW, h);
            Pos(Thumb_Left, 0, halfH);
            Pos(Thumb_Right, w, halfH);
        }



        // grow/shrink + move CanvasHost based on which thumb is dragged
        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var thumb = (Thumb)sender;
            double w = CanvasHost.Width;
            double h = CanvasHost.Height;
            double dx = e.HorizontalChange;
            double dy = e.VerticalChange;

            void ApplySize(double newW, double newH)
            {
                CanvasHost.Width = Math.Max(50, newW);
                CanvasHost.Height = Math.Max(50, newH);
            }

            // each case adjusts size (and sometimes moves the host)
            if (thumb == Thumb_TopLeft)
            {
                ApplySize(w - dx, h - dy);
                Canvas.SetLeft(CanvasHost, Canvas.GetLeft(CanvasHost) + dx);
                Canvas.SetTop(CanvasHost, Canvas.GetTop(CanvasHost) + dy);
            }
            else if (thumb == Thumb_Top)
            {
                ApplySize(w, h - dy);
                Canvas.SetTop(CanvasHost, Canvas.GetTop(CanvasHost) + dy);
            }
            else if (thumb == Thumb_TopRight)
            {
                ApplySize(w + dx, h - dy);
                Canvas.SetTop(CanvasHost, Canvas.GetTop(CanvasHost) + dy);
            }
            else if (thumb == Thumb_Right)
            {
                ApplySize(w + dx, h);
            }
            else if (thumb == Thumb_BottomRight)
            {
                ApplySize(w + dx, h + dy);
            }
            else if (thumb == Thumb_Bottom)
            {
                ApplySize(w, h + dy);
            }
            else if (thumb == Thumb_BottomLeft)
            {
                ApplySize(w - dx, h + dy);
                Canvas.SetLeft(CanvasHost, Canvas.GetLeft(CanvasHost) + dx);
            }
            else if (thumb == Thumb_Left)
            {
                ApplySize(w - dx, h);
                Canvas.SetLeft(CanvasHost, Canvas.GetLeft(CanvasHost) + dx);
            }
        }
        private void OnMouseWheelZoom(object sender, MouseWheelEventArgs e)
        {
            const double step = 0.1;
            // roll up -> zoom in; roll down -> zoom out
            ZoomSlider.Value += e.Delta > 0 ? step : -step;
            e.Handled = true;  // prevent the ScrollViewer from scrolling
        }

        private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            CropStrokesToBounds();          // your existing stroke‐deletion
            CanvasHost_SizeChanged(null, null);  // force a re‐layout of thumbs
        }


        private void CropStrokesToBounds()
        {
            // define the “keep” region in InkCanvas coordinates
            var cropRect = new Rect(0, 0, CanvasHost.Width, CanvasHost.Height);

            // only remove strokes that do NOT intersect at all
            var toRemove = inkCanvas.Strokes
                .Where(s => !cropRect.IntersectsWith(s.GetBounds()))
                .ToArray();

            foreach (var s in toRemove)
                inkCanvas.Strokes.Remove(s);
        }



    }
}
