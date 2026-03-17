using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GestureSign.Common.Gestures;

namespace GestureSign.ControlPanel.Common
{
    public class GestureImage
    {
        #region  Private Variables

        // static Point Dpi = GetPixelsPerXLogicalInch();
        #endregion

        private static Point[] ScaleGesture(System.Drawing.Point[] input, double width, double height, out Size scaledSize)
        {
            // Create generic list of points to hold scaled stroke
            List<Point> scaledStroke = new List<Point>();

            // Get total width and height of gesture
            double fGestureOffsetLeft = input.Min(i => i.X);
            double fGestureOffsetTop = input.Min(i => i.Y);
            double fGestureWidth = input.Max(i => i.X) - fGestureOffsetLeft;
            double fGestureHeight = input.Max(i => i.Y) - fGestureOffsetTop;

            // Get each scale ratio
            double dScaleX = width / fGestureWidth;
            double dScaleY = height / fGestureHeight;

            // Scale on the longest axis
            if (fGestureWidth >= fGestureHeight)
            {
                // Scale on X axis
                // Clear current scaled stroke
                scaledStroke.Clear();

                scaledStroke.AddRange(input.Select(currentPoint => new Point(((currentPoint.X - fGestureOffsetLeft) * dScaleX), ((currentPoint.Y - fGestureOffsetTop) * dScaleX))));

                // Calculate new gesture width and height
                scaledSize = new Size(Math.Floor(fGestureWidth * dScaleX), Math.Floor(fGestureHeight * dScaleX));
            }
            else
            {
                // Scale on X axis
                // Clear current scaled stroke
                scaledStroke.Clear();

                scaledStroke.AddRange(input.Select(currentPoint => new Point((currentPoint.X - fGestureOffsetLeft) * dScaleY, (currentPoint.Y - fGestureOffsetTop) * dScaleY)));

                // Calculate new gesture width and height
                scaledSize = new Size(fGestureWidth * dScaleY, fGestureHeight * dScaleY);
            }

            return scaledStroke.ToArray();
        }

        public static DrawingImage CreateImage(PointPattern[] pointPatterns, Size size, Color color, bool featureFingerOnly = false, GestureModifiers modifiers = GestureModifiers.Default)
        {
            if (pointPatterns == null)
                return null;

            DrawingGroup drawingGroup = new DrawingGroup();

            for (int i = 0; i < pointPatterns.Length; i++)
            {
                PathGeometry pathGeometry = new PathGeometry();

                color.A = (byte)(0xFF - i * 0x55);
                SolidColorBrush brush = new SolidColorBrush(color);
                Pen drawingPen = new Pen(brush, size.Height / 20 + i * 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

                if (pointPatterns[i].Points == null) return null;
                var styles = pointPatterns[i].StrokeStyles;

                // 特征手指模式：只渲染特征手指那根轨迹
                int featureIndex = featureFingerOnly
                    ? GestureManager.GetFeatureFingerTrajectoryIndex(pointPatterns[i].Points.Length)
                    : -1;

                for (int j = 0; j < pointPatterns[i].Points.Length; j++)
                {
                    if (featureFingerOnly && j != featureIndex)
                        continue;
                    if (pointPatterns[i].Points[j].Length == 1)
                    {
                        Point center = new Point(size.Width * j + size.Width / 2, size.Height / 2);
                        bool isPrimaryBtn = modifiers.HasFlag(GestureModifiers.PrimaryButtonDown);
                        var style = styles != null && j < styles.Length
                            ? styles[j]
                            : StrokeDisplayStyle.FilledDot;

                        // PrimaryButtonDown 统一用描边效果（外圈 + 白色挖空 + 内圈）
                        if (isPrimaryBtn && style != StrokeDisplayStyle.HollowCircle)
                        {
                            double outerRadius = drawingPen.Thickness * 1.5;
                            double innerGapRadius = outerRadius - 1.5;
                            double innerRadius = drawingPen.Thickness * 0.4;

                            // 外圈
                            var outerDrawing = new GeometryDrawing(brush, null,
                                new EllipseGeometry(center, outerRadius, outerRadius));
                            outerDrawing.Freeze();
                            drawingGroup.Children.Add(outerDrawing);

                            // 白色挖空
                            var bgBrush = new SolidColorBrush(Colors.White);
                            bgBrush.Freeze();
                            var bgDrawing = new GeometryDrawing(bgBrush, null,
                                new EllipseGeometry(center, innerGapRadius, innerGapRadius));
                            bgDrawing.Freeze();
                            drawingGroup.Children.Add(bgDrawing);

                            // 内圈细点
                            var dotDrawing = new GeometryDrawing(brush, null,
                                new EllipseGeometry(center, innerRadius, innerRadius));
                            dotDrawing.Freeze();
                            drawingGroup.Children.Add(dotDrawing);
                        }
                        else
                        {
                            switch (style)
                            {
                                case StrokeDisplayStyle.HollowCircle:
                                    double hollowRadius = drawingPen.Thickness * 1.6;
                                    Pen hollowPen = new Pen(brush, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                                    hollowPen.Freeze();
                                    var hollowDrawing = new GeometryDrawing(null, hollowPen,
                                        new EllipseGeometry(center, hollowRadius, hollowRadius));
                                    hollowDrawing.Freeze();
                                    drawingGroup.Children.Add(hollowDrawing);
                                    break;

                                default:
                                    pathGeometry.AddGeometry(new EllipseGeometry(center,
                                        drawingPen.Thickness / 2, drawingPen.Thickness / 2));
                                    break;
                            }
                        }
                        continue;
                    }
                    StreamGeometry sg = new StreamGeometry { FillRule = FillRule.EvenOdd };
                    using (StreamGeometryContext sgc = sg.Open())
                    {
                        // Create new size object accounting for pen width
                        Size szeAdjusted = new Size(size.Width - drawingPen.Thickness - 1,
                            (size.Height - drawingPen.Thickness - 1));

                        Size scaledSize;
                        Point[] scaledPoints = ScaleGesture(pointPatterns[i].Points[j], szeAdjusted.Width - 10, szeAdjusted.Height - 10,
                            out scaledSize);

                        // Define size that will mark the offset to center the gesture
                        double iLeftOffset = (size.Width / 2) - (scaledSize.Width / 2);
                        double iTopOffset = (size.Height / 2) - (scaledSize.Height / 2);
                        Vector sizOffset = new Vector(iLeftOffset + j * size.Width, iTopOffset);
                        sgc.BeginFigure(Point.Add(scaledPoints[0], sizOffset), false, false);
                        foreach (Point p in scaledPoints)
                        {
                            sgc.LineTo(Point.Add(p, sizOffset), true, true);
                        }
                        DrawArrow(sgc, scaledPoints, sizOffset, drawingPen.Thickness);
                    }
                    sg.Freeze();
                    pathGeometry.AddGeometry(sg);
                }
                pathGeometry.Freeze();

                // PrimaryButtonDown：外层描边 + 背景挖空 + 细内层轨迹，形成空心描边效果
                if (modifiers.HasFlag(GestureModifiers.PrimaryButtonDown))
                {
                    double outerThickness = drawingPen.Thickness * 3;
                    double innerGap = outerThickness - 2.0; // 背景挖空宽度，留出边缘作为描边

                    // 1. 外层描边（原色）
                    var outerPen = new Pen(brush, outerThickness)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    outerPen.Freeze();
                    var outerDrawing = new GeometryDrawing(null, outerPen, pathGeometry);
                    outerDrawing.Freeze();
                    drawingGroup.Children.Add(outerDrawing);

                    // 2. 中间挖空（用背景色覆盖，形成空心效果）
                    var bgBrush = new SolidColorBrush(Colors.White);
                    bgBrush.Freeze();
                    var bgPen = new Pen(bgBrush, innerGap)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    bgPen.Freeze();
                    var bgDrawing = new GeometryDrawing(null, bgPen, pathGeometry);
                    bgDrawing.Freeze();
                    drawingGroup.Children.Add(bgDrawing);

                    // 3. 内层细轨迹
                    var innerPen = new Pen(brush, drawingPen.Thickness * 0.8)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round
                    };
                    innerPen.Freeze();
                    var innerDrawing = new GeometryDrawing(null, innerPen, pathGeometry);
                    innerDrawing.Freeze();
                    drawingGroup.Children.Add(innerDrawing);
                }
                else
                {
                    GeometryDrawing drawing = new GeometryDrawing(null, drawingPen, pathGeometry);
                    drawing.Freeze();
                    drawingGroup.Children.Add(drawing);
                }
            }

            // 修饰符标签：左上角（Ctrl/Shift/Alt）
            var keyModifiers = modifiers & ~GestureModifiers.PrimaryButtonDown;
            if (keyModifiers != GestureModifiers.Default)
            {
                var labelParts = new System.Collections.Generic.List<string>();
                if (modifiers.HasFlag(GestureModifiers.Ctrl)) labelParts.Add("Ctrl");
                if (modifiers.HasFlag(GestureModifiers.Shift)) labelParts.Add("Shift");
                if (modifiers.HasFlag(GestureModifiers.Alt)) labelParts.Add("Alt");
                string labelText = string.Join("+", labelParts);

                FormattedText modText = new FormattedText(
                    labelText,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    size.Height / 5,
                    new SolidColorBrush(color));

                GeometryDrawing modDrawing = new GeometryDrawing(
                    new SolidColorBrush(color),
                    null,
                    modText.BuildGeometry(new Point(5, 5)));
                modDrawing.Freeze();
                drawingGroup.Children.Add(modDrawing);
            }

            //  myPath.Data = sg;
            drawingGroup.Freeze();
            DrawingImage drawingImage = new DrawingImage(drawingGroup);
            drawingImage.Freeze();

            return drawingImage;

        }
        private static void DrawArrow(StreamGeometryContext streamGeometryContext, Point[] points, Vector sizeOffset, double thickness)
        {
            double headWidth = thickness;
            double headHeight = thickness * 0.8;

            Point pt1 = Point.Add(points[points.Length - 2], sizeOffset);
            Point pt2 = Point.Add(points[points.Length - 1], sizeOffset);

            double theta = Math.Atan2(pt1.Y - pt2.Y, pt1.X - pt2.X);
            double sint = Math.Sin(theta);
            double cost = Math.Cos(theta);


            Point pt3 = new Point(
                pt2.X + (headWidth * cost - headHeight * sint),
                pt2.Y + (headWidth * sint + headHeight * cost));

            Point pt4 = new Point(
                pt2.X + (headWidth * cost + headHeight * sint),
                pt2.Y - (headHeight * cost - headWidth * sint));

            streamGeometryContext.BeginFigure(pt1, true, false);
            streamGeometryContext.LineTo(pt2, true, true);
            streamGeometryContext.LineTo(pt3, true, true);
            streamGeometryContext.LineTo(pt2, true, true);
            streamGeometryContext.LineTo(pt4, true, true);
        }

    }
}
