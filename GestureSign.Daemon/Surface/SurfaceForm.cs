using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GestureSign.Common.Configuration;
using GestureSign.Common.Log;
using GestureSign.Daemon.Native;
using ManagedWinapi.Windows;
using Microsoft.Win32;

namespace GestureSign.Daemon.Surface
{
    public class SurfaceForm : Form
    {
        #region Private Variables

        private Pen _drawingPen;
        private Pen _dirtyMarkerPen;
        private float _penWidth;
        int[] _lastStroke;
        Size _screenOffset = default(Size);
        DiBitmap _bitmap;
        private GraphicsPath _graphicsPath = new GraphicsPath();
        private GraphicsPath _dirtyGraphicsPath = new GraphicsPath();

        private bool _settingsChanged;
        private bool _isTrainingMode;
        private double _totalDistance;
        private Point _textPosition; // Fixed position for distance text
        private Rectangle _lastTextRect; // Last drawn text rectangle for clearing (bitmap coordinates)
        private Rectangle _lastTextRectScreen; // Last drawn text rectangle for screen update

        private const Int32 ULW_ALPHA = 0x00000002;

        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        #endregion

        #region Constructors

        public SurfaceForm()
        {
            CreateHandle();
            InitializeForm();
            AppConfig.ConfigChanged += AppConfig_ConfigChanged;
            // Respond to system event changes by reinitializing the form
            SystemEvents.DisplaySettingsChanged += AppConfig_ConfigChanged;
            SystemEvents.UserPreferenceChanged += AppConfig_ConfigChanged;
            //this.SetStyle(ControlStyles.DoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            //this.UpdateStyles();
        }

        #endregion

        #region Dispose

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    AppConfig.ConfigChanged -= AppConfig_ConfigChanged;
                    SystemEvents.DisplaySettingsChanged -= AppConfig_ConfigChanged;
                    SystemEvents.UserPreferenceChanged -= AppConfig_ConfigChanged;
                }

                _penWidth = 0;
                _bitmap?.Dispose();
                _graphicsPath?.Dispose();
                _dirtyGraphicsPath?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion

        #region Events

        private void AppConfig_ConfigChanged(object sender, EventArgs e)
        {
            ResetSurface();
        }

        #endregion

        #region Public Methods

        public new void Load()
        {

        }

        public void StartDrawing(List<Point> startPoints, bool isTrainingMode = false)
        {
            GestureSign.Common.Log.Logging.LogTrace($"[SurfaceForm] StartDrawing called - isTrainingMode={isTrainingMode}, _isTrainingMode before={_isTrainingMode}");

            if (_settingsChanged)
            {
                _settingsChanged = false;
                InitializeForm();
            }

            if (_penWidth <= 0) return;

            ClearSurfaces();

            //follow dynamic system color
            _drawingPen.Color = AppConfig.VisualFeedbackColor;
            _drawingPen.Width = _penWidth * DpiHelper.GetScreenDpi(startPoints.FirstOrDefault()) / 96f;

            _isTrainingMode = isTrainingMode;
            _totalDistance = 0;

            GestureSign.Common.Log.Logging.LogTrace($"[SurfaceForm] StartDrawing - _isTrainingMode set to {_isTrainingMode}");

            // Set fixed text position near start point (offset to avoid covering gesture)
            if (isTrainingMode && startPoints.Count > 0)
            {
                _textPosition = new Point(startPoints[0].X + 40, startPoints[0].Y - 60);
            }
        }

        public void EndDrawing()
        {
            GestureSign.Common.Log.Logging.LogTrace($"[SurfaceForm] EndDrawing called - _isTrainingMode before reset={_isTrainingMode}, _lastTextRect={_lastTextRect}");

            if (_penWidth <= 0 || _lastStroke == null)
                return;
            Hide();
            TopMost = false;

            ClearSurfaces();

            // Reset training mode flag and distance
            _isTrainingMode = false;
            _totalDistance = 0;
            _lastTextRect = Rectangle.Empty;
            _lastTextRectScreen = Rectangle.Empty;

            GestureSign.Common.Log.Logging.LogTrace($"[SurfaceForm] EndDrawing - _isTrainingMode reset to {_isTrainingMode}");
        }

        public void DrawPoints(List<List<Point>> points)
        {
            if (_penWidth > 0 && !(points.Count == 1 && points[0].Count == 1))
            {

                if (_bitmap == null || _lastStroke == null)
                {
                    ClearSurfaces();
                    try
                    {
                        _bitmap = new DiBitmap(this.Size);
                    }
                    catch (ApplicationException ex)
                    {
                        Logging.LogException(ex);
                    }
                }
                DrawSegments(points);
            }
        }

        #endregion

        #region Private Methods

        private void DrawSegments(List<List<Point>> points)
        {
            // Ensure that surface is visible
            if (!Visible)
            {
                TopMost = true;
                Show();
            }
            if (_lastStroke == null) { _lastStroke = new int[points.Count]; }
            if (_lastStroke.Length != points.Count) return;
            try
            {
                // Calculate total distance if in training mode
                if (_isTrainingMode && points.Count > 0)
                {
                    _totalDistance = 0;
                    var firstTrajectory = points[0];
                    for (int i = 1; i < firstTrajectory.Count; i++)
                    {
                        var p1 = firstTrajectory[i - 1];
                        var p2 = firstTrajectory[i];
                        _totalDistance += Math.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y));
                    }
                }

                _dirtyGraphicsPath.Reset();
                var surfaceGraphics = _bitmap.BeginDraw();
                var translatedPointList = new List<Point[]>(_lastStroke.Length);

                for (int i = 0; i < _lastStroke.Length; i++)
                {
                    // Create list of points that are new this draw
                    List<Point> newPoints = new List<Point>();
                    // Get number of points added since last draw including last point of last stroke and add new points to new points list

                    var iDelta = points[i].Count - _lastStroke[i] + 1;

                    newPoints.AddRange(points[i].Skip(points[i].Count - iDelta).Take(iDelta));
                    if (newPoints.Count < 2) continue;

                    var translatedPoints = newPoints.Select(TranslatePoint).ToArray();
                    // Draw new line segments to main drawing surface
                    _graphicsPath.AddLines(translatedPoints);

                    _dirtyGraphicsPath.AddLines(translatedPoints);
                    translatedPointList.Add(translatedPoints);
                }
                _dirtyGraphicsPath.Widen(_dirtyMarkerPen);
                surfaceGraphics.SetClip(_dirtyGraphicsPath);

                foreach (var pp in translatedPointList)
                    surfaceGraphics.DrawLines(_drawingPen, pp);

                _bitmap.EndDraw();

                // Draw distance text if in training mode (after EndDraw to avoid clipping)
                Rectangle textRect = Rectangle.Empty;
                if (_isTrainingMode && _totalDistance > 0)
                {
                    var textGraphics = _bitmap.BeginDraw();
                    textGraphics.ResetClip(); // Remove clip region for text

                    // Get text bounds before drawing
                    string distanceText = $"{_totalDistance:F0} px";
                    int tapThreshold = AppConfig.TapDistanceThreshold;
                    string thresholdText = $"(Threshold: {tapThreshold} px)";
                    using (Font font = new Font("Segoe UI", 16, FontStyle.Bold))
                    using (Font smallFont = new Font("Segoe UI", 12))
                    {
                        var distanceSize = textGraphics.MeasureString(distanceText, font);
                        var thresholdSize = textGraphics.MeasureString(thresholdText, smallFont);
                        float maxWidth = Math.Max(distanceSize.Width, thresholdSize.Width);
                        float totalHeight = distanceSize.Height + thresholdSize.Height + 4;

                        var translatedPos = TranslatePoint(_textPosition);
                        float textX = translatedPos.X;
                        float textY = translatedPos.Y;

                        if (textX + maxWidth + 20 > Width) textX = Width - maxWidth - 20;
                        if (textX < 10) textX = 10;
                        if (textY < 10) textY = 10;
                        if (textY + totalHeight + 10 > Height) textY = Height - totalHeight - 10;

                        textRect = new Rectangle((int)(textX - 8), (int)(textY - 4), (int)(maxWidth + 16), (int)(totalHeight + 8));
                    }

                    DrawDistanceText(textGraphics, points);
                    _bitmap.EndDraw();
                }

                UpdateDraw();

                // Update text area separately if in training mode
                if (_isTrainingMode && !textRect.IsEmpty)
                {
                    // Save original bitmap coordinates for clearing
                    _lastTextRect = textRect;

                    // Transform to screen coordinates for display
                    var screenTextRect = textRect;
                    screenTextRect.Offset(Bounds.Location);
                    screenTextRect.Intersect(Bounds);
                    screenTextRect.Offset(-Bounds.X, -Bounds.Y);
                    SetDiBitmap(_bitmap, screenTextRect, (byte)(AppConfig.Opacity * 0xFF));

                    // Save screen coordinates for clearing
                    _lastTextRectScreen = screenTextRect;
                }
            }
            catch (Exception e)
            {
                Logging.LogException(e);
                ClearSurfaces();
            }
            // this.CreateGraphics().DrawImage(bmp, 0, 0);

            // Set last stroke to copy of current stroke
            // ToList method creates value copy of stroke list and assigns it to last stroke
            _lastStroke = points.Select(p => p.Count).ToArray();
        }

        private void ResetSurface()
        {
            if (_lastStroke == null)
            {
                if (InvokeRequired) Invoke(new Action(InitializeForm));
                else InitializeForm();
            }
            else
            {
                if (InvokeRequired) Invoke(new Action(() => _settingsChanged = true));
                else _settingsChanged = true;
            }
        }

        private void InitializeForm()
        {
            // Set basic variables
            FormBorderStyle = FormBorderStyle.None;
            Name = "SurfaceForm";
            ShowIcon = false;
            StartPosition = FormStartPosition.Manual;
            Show();
            Hide();


            // Combine monitor screen sizes and set form size to combined size
            Rectangle rOutput = new Rectangle();

            foreach (Screen oScreen in Screen.AllScreens)
                rOutput = Rectangle.Union(rOutput, oScreen.Bounds);

            // 1 pixel margin for avoiding activating Focus assist
            Left = Screen.AllScreens.Min(s => s.Bounds.Left) + 1;
            Top = Screen.AllScreens.Min(s => s.Bounds.Top) + 1;
            Width = rOutput.Width - 1;
            Height = rOutput.Height - 1;
            // Store offset in class field
            _screenOffset = new Size(Location);

            InitializePen();
        }

        private void InitializePen()
        {
            _penWidth = AppConfig.VisualFeedbackWidth;
            _drawingPen = new Pen(AppConfig.VisualFeedbackColor, _penWidth * DpiHelper.GetSystemDpi() / 96f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            _dirtyMarkerPen = new Pen(Color.FromArgb(30, 0, 0, 0), (_drawingPen.Width + 4f) * 1.5f)
            {
                EndCap = LineCap.Round,
                StartCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
        }


        private Point TranslatePoint(Point point)
        {
            // Add point offset
            return Point.Subtract(point, _screenOffset);
        }

        private void DrawDistanceText(Graphics graphics, List<List<Point>> points)
        {
            if (points.Count == 0 || points[0].Count == 0) return;

            // Create text to display
            string distanceText = $"{_totalDistance:F0} px";
            int tapThreshold = AppConfig.TapDistanceThreshold;
            string thresholdText = $"(Threshold: {tapThreshold} px)";

            // Create font and brush
            using (Font font = new Font("Segoe UI", 16, FontStyle.Bold))
            using (Font smallFont = new Font("Segoe UI", 12))
            using (Brush textBrush = new SolidBrush(Color.White))
            using (Brush bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
            {
                // Measure text size
                var distanceSize = graphics.MeasureString(distanceText, font);
                var thresholdSize = graphics.MeasureString(thresholdText, smallFont);
                float maxWidth = Math.Max(distanceSize.Width, thresholdSize.Width);
                float totalHeight = distanceSize.Height + thresholdSize.Height + 4;

                // Use fixed position (translated to screen coordinates)
                var translatedPos = TranslatePoint(_textPosition);
                float textX = translatedPos.X;
                float textY = translatedPos.Y;

                // Ensure text stays on screen
                if (textX + maxWidth + 20 > Width)
                    textX = Width - maxWidth - 20;
                if (textX < 10)
                    textX = 10;
                if (textY < 10)
                    textY = 10;
                if (textY + totalHeight + 10 > Height)
                    textY = Height - totalHeight - 10;

                // Draw semi-transparent background to prevent ghosting
                RectangleF bgRect = new RectangleF(textX - 8, textY - 4, maxWidth + 16, totalHeight + 8);
                graphics.FillRectangle(bgBrush, bgRect);

                // Draw text
                graphics.DrawString(distanceText, font, textBrush, textX, textY);
                graphics.DrawString(thresholdText, smallFont, textBrush, textX, textY + distanceSize.Height + 2);
            }
        }

        private void ClearSurfaces()
        {
            _lastStroke = null;
            if (_bitmap != null)
            {
                using (_bitmap)
                {
                    var g = _bitmap.BeginDraw();

                    _graphicsPath.Widen(_dirtyMarkerPen);
                    g.SetClip(_graphicsPath);
                    g.Clear(Color.Transparent);

                    // Also clear text area if it was drawn (using bitmap coordinates)
                    if (!_lastTextRect.IsEmpty)
                    {
                        g.ResetClip();
                        g.SetClip(_lastTextRect);
                        g.Clear(Color.Transparent);
                    }

                    _bitmap.EndDraw();

                    var pathDirty = Rectangle.Ceiling(_graphicsPath.GetBounds());
                    pathDirty.Offset(Bounds.Location);
                    pathDirty.Intersect(Bounds);
                    pathDirty.Offset(-Bounds.X, -Bounds.Y); //挪回来变为基于窗口的坐标

                    SetDiBitmap(_bitmap, pathDirty);

                    // Also update text area to clear it from screen (using screen coordinates)
                    if (!_lastTextRectScreen.IsEmpty)
                    {
                        SetDiBitmap(_bitmap, _lastTextRectScreen);
                    }

                    _graphicsPath.Reset();
                    _dirtyGraphicsPath.Reset();

                }
                _bitmap = null;
            }

            // Reset text rects after clearing
            _lastTextRect = Rectangle.Empty;
            _lastTextRectScreen = Rectangle.Empty;
        }


        private void SetDiBitmap(DiBitmap bmp, Rectangle dirtyRect, byte opacity = 255)
        {
            SetHBitmap(bmp.HBitmap, Bounds, Point.Empty, dirtyRect, opacity);
        }

        //dirtyRect是绝对坐标（在多屏的情况下）
        private void SetHBitmap(IntPtr hBitmap, Rectangle newWindowBounds, Point drawAt, Rectangle dirtyRect, byte opacity)
        {
            // IntPtr screenDc = Win32.GDI32.GetDC(IntPtr.Zero);

            IntPtr memDc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                var winSize = new NativeMethods.Size(newWindowBounds.Width, newWindowBounds.Height);
                var winPos = new NativeMethods.Point(newWindowBounds.X, newWindowBounds.Y);

                var drawBmpAt = new NativeMethods.Point(drawAt.X, drawAt.Y);
                var blend = new NativeMethods.BLENDFUNCTION { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = opacity, AlphaFormat = AC_SRC_ALPHA };

                var updateInfo = new NativeMethods.UPDATELAYEREDWINDOWINFO
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.UPDATELAYEREDWINDOWINFO)),
                    dwFlags = ULW_ALPHA,
                    hdcDst = IntPtr.Zero,
                    hdcSrc = memDc
                };
                //NativeMethods.GetDC(IntPtr.Zero);//IntPtr.Zero; //ScreenDC

                // dirtyRect.X -= _bounds.X;
                // dirtyRect.Y -= _bounds.Y;

                //dirtyRect.Offset(-_bounds.X, -_bounds.Y);
                var dirRect = new NativeMethods.RECT(dirtyRect.X, dirtyRect.Y, dirtyRect.Right, dirtyRect.Bottom);

                unsafe
                {
                    updateInfo.pblend = &blend;
                    updateInfo.pptDst = &winPos;
                    updateInfo.psize = &winSize;
                    updateInfo.pptSrc = &drawBmpAt;
                    updateInfo.prcDirty = &dirRect;
                }

                NativeMethods.UpdateLayeredWindowIndirect(Handle, ref updateInfo);
                // Debug.Assert(NativeMethods.GetLastError() == 0);

                //NativeMethods.UpdateLayeredWindow(Handle, IntPtr.Zero, ref topPos, ref size, memDc, ref pointSource, 0, ref blend, GDI32.ULW_ALPHA);

            }
            finally
            {

                //GDI32.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldBitmap);
                    //Windows.DeleteObject(hBitmap); // The documentation says that we have to use the Windows.DeleteObject... but since there is no such method I use the normal DeleteObject from Win32 GDI and it's working fine without any resource leak.
                    //Win32.DeleteObject(hBitmap);
                }
                NativeMethods.DeleteDC(memDc);
            }
        }

        private void UpdateDraw()
        {
            var pathDirty = Rectangle.Ceiling(_dirtyGraphicsPath.GetBounds());
            pathDirty.Offset(Bounds.Location);
            pathDirty.Intersect(Bounds);
            pathDirty.Offset(-Bounds.X, -Bounds.Y); //挪回来变为基于窗口的坐标

            SetDiBitmap(_bitmap, /*_pathDirtyRect*/pathDirty, (byte)(AppConfig.Opacity * 0xFF));
        }

        #endregion

        #region Base Method Overrides

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams myParams = base.CreateParams;
                myParams.ExStyle = (int)WindowExStyleFlags.NOACTIVATE |
                                    (int)WindowExStyleFlags.TOOLWINDOW |
                                    (int)WindowExStyleFlags.TRANSPARENT |
                                    (int)WindowExStyleFlags.LAYERED;
                return myParams;
            }
        }
        #endregion
    }
}