using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Media3D;
using WiimoteLib;
using WiiTUIO.Filters;
using WiiTUIO.Properties;
using Point = WiimoteLib.Point;

namespace WiiTUIO.Provider
{
    public class ScreenPositionCalculator
    {
        private int wiimoteId = 0;

        private int minXPos;
        private int maxXPos;
        private int maxWidth;

        private uint[] see = new uint[4];

        private PointF[] finalPos = new PointF[4];

        private float xDistTop;
        private float xDistBottom;
        private float yDistLeft;
        private float yDistRight;

        float angleTop;
        float angleBottom;
        float angleLeft;
        float angleRight;

        double angle;
        float height;
        float width;

        private float[] angleOffset = new float[4];

        private int minYPos;
        private int maxYPos;
        private int maxHeight;
        private int SBPositionOffset;
        private double CalcMarginOffsetY;

        private double midMarginX;
        private double midMarginY;
        private double marginBoundsX;
        private double marginBoundsY;

        private PointF topLeftPt = new PointF();
        private PointF bottomRightPt = new PointF();
        private PointF trueTopLeftPt = new PointF();
        private PointF trueBottomRightPt = new PointF();
        private double boundsX;
        private double boundsY;

        // Use 0.0 to mean use full mapped range
        private double targetAspectRatio = 0.0;

        private double smoothedX, smoothedZ;
        private int orientation;

        private int leftPoint = -1;

        private Warper pWarper;

        private CursorPos lastPos;

        private Screen primaryScreen;

        private RadiusBuffer smoothingBuffer;
        private CoordFilter coordFilter;

        private int lastIrPoint1 = -1;
        private int lastIrPoint2 = -1;

        public CalibrationSettings settings;

        private float offsetTR, offsetBR, offsetBL, offsetTL; // Offsets de ángulo
        private float angleTR, angleBR, angleBL, angleTL; // Ángulos de los lados

        public ScreenPositionCalculator(int id, CalibrationSettings settings)
        {
            this.wiimoteId = id;
            this.settings = settings;
            this.pWarper = new Warper(this.settings);
            this.primaryScreen = DeviceUtils.DeviceUtil.GetScreen(Settings.Default.primaryMonitor);
            this.recalculateScreenBounds(this.primaryScreen);

            Settings.Default.PropertyChanged += SettingsChanged;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            this.settings.PropertyChanged += SettingsChanged;

            lastPos = new CursorPos(0, 0, 0, 0, 0);

            coordFilter = new CoordFilter();
            this.smoothingBuffer = new RadiusBuffer(Settings.Default.pointer_positionSmoothing);

            //topLeftPt = new PointF() { X = 0.0f, Y = 0.0f };
            //centerPt = new PointF() { X = 0.5f, Y = 0.5f };
        }

        private void SettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "primaryMonitor")
            {
                this.primaryScreen = DeviceUtils.DeviceUtil.GetScreen(Settings.Default.primaryMonitor);
                Console.WriteLine("Setting primary monitor for screen position calculator to " + this.primaryScreen.Bounds);
                this.recalculateScreenBounds(this.primaryScreen);
            }
            else if (e.PropertyName == "Left" || e.PropertyName == "Right" || e.PropertyName == "Top" || e.PropertyName == "Bottom")
            {
                trueTopLeftPt.X = topLeftPt.X = this.settings.Left;
                trueTopLeftPt.Y = topLeftPt.Y = this.settings.Top;
                trueBottomRightPt.X = bottomRightPt.X = this.settings.Right;
                trueBottomRightPt.Y = bottomRightPt.Y = this.settings.Bottom;
                recalculateLightgunCoordBounds();
            }
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            this.primaryScreen = DeviceUtils.DeviceUtil.GetScreen(Settings.Default.primaryMonitor);
            recalculateScreenBounds(this.primaryScreen);
        }

        private void recalculateScreenBounds(Screen screen)
        {
            Console.WriteLine("Setting primary monitor for screen position calculator to " + this.primaryScreen.Bounds);
            minXPos = -(int)(screen.Bounds.Width * Settings.Default.pointer_marginsLeftRight);
            maxXPos = screen.Bounds.Width + (int)(screen.Bounds.Width * Settings.Default.pointer_marginsLeftRight);
            maxWidth = maxXPos - minXPos;
            minYPos = -(int)(screen.Bounds.Height * Settings.Default.pointer_marginsTopBottom);
            maxYPos = screen.Bounds.Height + (int)(screen.Bounds.Height * Settings.Default.pointer_marginsTopBottom);
            maxHeight = maxYPos - minYPos;
            SBPositionOffset = (int)(screen.Bounds.Height * Settings.Default.pointer_sensorBarPosCompensation);
            CalcMarginOffsetY = Settings.Default.pointer_sensorBarPosCompensation;

            midMarginX = Settings.Default.pointer_marginsLeftRight * 0.5;
            midMarginY = Settings.Default.pointer_marginsTopBottom * 0.5;
            marginBoundsX = 1 / (1 - Settings.Default.pointer_marginsLeftRight);
            marginBoundsY = 1 / (1 - Settings.Default.pointer_marginsTopBottom);

            trueTopLeftPt.X = topLeftPt.X = this.settings.Left;
            trueTopLeftPt.Y = topLeftPt.Y = this.settings.Top;
            trueBottomRightPt.X = bottomRightPt.X = this.settings.Right;
            trueBottomRightPt.Y = bottomRightPt.Y = this.settings.Bottom;

            if (targetAspectRatio == 0.0)
            {
                recalculateLightgunCoordBounds();
            }
            else
            {
                RecalculateLightgunAspect(targetAspectRatio);
            }
        }

        private void recalculateLightgunCoordBounds()
        {
            if (Settings.Default.pointer_4IRMode == "diamond")
            {
                boundsX = (Math.Abs(this.settings.Right - this.settings.Left) > double.Epsilon) ? 1.0 / (this.settings.Right - this.settings.Left) : 0;
                boundsY = (Math.Abs(this.settings.Bottom - this.settings.Top) > double.Epsilon) ? 1.0 / (this.settings.Bottom - this.settings.Top) : 0;
            }
            else if (Settings.Default.pointer_4IRMode == "square" ||
                Settings.Default.pointer_4IRMode == "none")
            {
                //Console.WriteLine($"KEEP ME INSIDE {1 - Settings.Default.CalibrationMarginX * 2}");
                boundsX = (1.0f) / (bottomRightPt.X - topLeftPt.X);
                boundsY = (1.0f) / (bottomRightPt.Y - topLeftPt.Y);
            }
        }

        private float NormalizeAngle(float angle)
        {
            while (angle > MathF.PI) angle -= 2 * MathF.PI;
            while (angle < -MathF.PI) angle += 2 * MathF.PI;
            return angle;
        }

        public CursorPos CalculateCursorPos(WiimoteState wiimoteState)
        {
            int x;
            int y;
            double marginX, marginY = 0.0;
            double lightbarX = 0.0;
            double lightbarY = 0.0;
            int offsetY = 0;
            double marginOffsetY = 0.0;

            IRState irState = wiimoteState.IRState;

            PointF median = new PointF();
            PointF resultPos = new PointF();

            if (Settings.Default.pointer_4IRMode == "none")
            {
                int irPoint1 = 0;
                int irPoint2 = 0;
                bool foundMidpoint = false;
                // First check if previously found points are still detected.
                // Prefer those points first
                if (lastIrPoint1 != -1 && lastIrPoint2 != -1)
                {
                    if (irState.IRSensors[lastIrPoint1].Found &&
                        irState.IRSensors[lastIrPoint2].Found)
                    {
                        foundMidpoint = true;
                        irPoint1 = lastIrPoint1;
                        irPoint2 = lastIrPoint2;
                    }
                }

                // If no midpoint found from previous points, check all available
                // IR points for a possible midpoint
                for (int i = 0; !foundMidpoint && i < irState.IRSensors.Count(); i++)
                {
                    if (irState.IRSensors[i].Found)
                    {
                        for (int j = i + 1; j < irState.IRSensors.Count() && !foundMidpoint; j++)
                        {
                            if (irState.IRSensors[j].Found)
                            {
                                foundMidpoint = true;

                                irPoint1 = i;
                                irPoint2 = j;
                            }
                        }
                    }
                }

                if (foundMidpoint)
                {
                    int i = irPoint1;
                    int j = irPoint2;
                    median.X = (irState.IRSensors[i].Position.X + irState.IRSensors[j].Position.X) / 2.0f;
                    median.Y = (irState.IRSensors[i].Position.Y + irState.IRSensors[j].Position.Y) / 2.0f;

                    if (Settings.Default.pointer_considerRotation)
                    {
                        smoothedX = smoothedX * 0.9f + wiimoteState.AccelState.RawValues.X * 0.1f;
                        smoothedZ = smoothedZ * 0.9f + wiimoteState.AccelState.RawValues.Z * 0.1f;

                        int l = leftPoint, r;
                        if (leftPoint == -1)
                        {
                            double absx = Math.Abs(smoothedX - 128), absz = Math.Abs(smoothedZ - 128);

                            if (orientation == 0 || orientation == 2) absx -= 5;
                            if (orientation == 1 || orientation == 3) absz -= 5;

                            if (absz >= absx)
                            {
                                if (absz > 5)
                                    orientation = (smoothedZ > 128) ? 0 : 2;
                            }
                            else
                            {
                                if (absx > 5)
                                    orientation = (smoothedX > 128) ? 3 : 1;
                            }

                            switch (orientation)
                            {
                                case 0: l = (irState.IRSensors[i].RawPosition.X < irState.IRSensors[j].RawPosition.X) ? i : j; break;
                                case 1: l = (irState.IRSensors[i].RawPosition.Y > irState.IRSensors[j].RawPosition.Y) ? i : j; break;
                                case 2: l = (irState.IRSensors[i].RawPosition.X > irState.IRSensors[j].RawPosition.X) ? i : j; break;
                                case 3: l = (irState.IRSensors[i].RawPosition.Y < irState.IRSensors[j].RawPosition.Y) ? i : j; break;
                            }
                        }
                        leftPoint = l;
                        r = l == i ? j : i;

                        double dx = irState.IRSensors[r].RawPosition.X - irState.IRSensors[l].RawPosition.X;
                        double dy = irState.IRSensors[r].RawPosition.Y - irState.IRSensors[l].RawPosition.Y;

                        double d = Math.Sqrt(dx * dx + dy * dy);

                        dx /= d;
                        dy /= d;

                        angle = Math.Atan2(dy, dx);

                        if (Settings.Default.pointer_considerRotation)
                        {
                            median.X = median.X - 0.5F;
                            median.Y = median.Y - 0.5F;

                            median = this.rotatePoint(median, angle);

                            median.X = median.X + 0.5F;
                            median.Y = median.Y + 0.5F;
                        }
                    }

                    lastIrPoint1 = irPoint1;
                    lastIrPoint2 = irPoint2;
                }
                else if (!foundMidpoint)
                {
                    CursorPos err = lastPos;
                    err.OutOfReach = err.OffScreen = true;
                    leftPoint = -1;
                    lastIrPoint1 = -1;
                    lastIrPoint2 = -1;

                    return err;
                }

                //int offsetY = 0;
                //double marginOffsetY = 0.0;

                // No need to invert here. It will be done later
                //median.X = 1 - median.X;

                if (Properties.Settings.Default.pointer_sensorBarPos == "top")
                {
                    offsetY = -SBPositionOffset;
                    marginOffsetY = -CalcMarginOffsetY;
                }
                else if (Properties.Settings.Default.pointer_sensorBarPos == "bottom")
                {
                    offsetY = SBPositionOffset;
                    marginOffsetY = CalcMarginOffsetY;
                }

                resultPos = median;
                // Need to invert X coordinate for resultPos here
                resultPos.X = 1 - resultPos.X;
            }
            else if (Settings.Default.pointer_4IRMode == "square")
            {
                byte seenFlags = 0;
                double Roll = Math.Atan2(wiimoteState.AccelState.Values.X, wiimoteState.AccelState.Values.Z);

                bool fourPtsFound = irState.IRSensors[0].Found == true &&
                    irState.IRSensors[1].Found == true &&
                    irState.IRSensors[2].Found == true && irState.IRSensors[3].Found == true;

                if (fourPtsFound)
                {
                    median.Y = (irState.IRSensors[0].Position.Y + irState.IRSensors[1].Position.Y + irState.IRSensors[2].Position.Y + irState.IRSensors[3].Position.Y + 0.002f) / 4;
                    median.X = (irState.IRSensors[0].Position.X + irState.IRSensors[1].Position.X + irState.IRSensors[2].Position.X + irState.IRSensors[3].Position.X + 0.002f) / 4;
                }

                for (int i = 0; i < 4; i++)
                {
                    if (irState.IRSensors[i].Found)
                    {
                        double point_angle = Math.Atan2(irState.IRSensors[i].Position.Y - median.Y, irState.IRSensors[i].Position.X - median.X) - Roll;
                        if (point_angle < 0) point_angle += 2 * Math.PI;

                        int index = (int)(point_angle / (Math.PI / 2));

                        finalPos[index] = irState.IRSensors[i].Position;
                        see[index] = (see[index] << 1) | 1;
                        seenFlags |= (byte)(1 << index);
                    }
                    else
                        see[i] = 0;
                }

                //Console.WriteLine($"LED 1 {finalPos[0].X} {finalPos[0].Y}");
                //double testAngle = Math.Atan2(wiimoteState.AccelState.Values.X, wiimoteState.AccelState.Values.Z);
                //Console.WriteLine($"{wiimoteState.AccelState.Values.X} {wiimoteState.AccelState.Values.Y} {wiimoteState.AccelState.Values.Z}");
                //Console.WriteLine($"KJKDD: {testAngle}");

                if (see.Count(seen => seen == 0) >= 2)
                {
                    CursorPos err = lastPos;
                    err.OutOfReach = true;
                    err.OffScreen = true;

                    return err;
                }

                while ((seenFlags & 15) != 0 && (seenFlags & 15) != 15)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if ((seenFlags & (1 << i)) == 0)
                        {
                            see[i] = 0;
                            int[] neighbors;
                            switch (i)
                            {
                                case 0:
                                    neighbors = new[] { 3, 1 };
                                    break;
                                case 1:
                                    neighbors = new[] { 2, 0 };
                                    break;
                                case 2:
                                    neighbors = new[] { 1, 3 };
                                    break;
                                case 3:
                                    neighbors = new[] { 0, 2 };
                                    break;
                                default:
                                    neighbors = Array.Empty<int>();
                                    break;
                            }

                            foreach (int neighbor in neighbors)
                            {
                                float f = 0;
                                if ((seenFlags & (1 << neighbor)) != 0) // Check if the bit for the neighbor is set
                                {
                                    switch (i)
                                    {
                                        case 0:
                                            f = angleBottom - angleOffset[neighbor];
                                            break;
                                        case 1:
                                            f = angleBottom + (angleOffset[neighbor] - MathF.PI);
                                            break;
                                        case 2:
                                            f = angleTop + angleOffset[neighbor];
                                            break;
                                        case 3:
                                            f = angleTop - (angleOffset[neighbor] - MathF.PI);
                                            break;
                                    }
                                }

                                float distance = 0;
                                switch (i)
                                {
                                    case 0:
                                        distance = (neighbor == 3) ? yDistRight : xDistBottom;
                                        break;
                                    case 1:
                                        distance = (neighbor == 2) ? yDistLeft : xDistBottom;
                                        break;
                                    case 2:
                                        distance = (neighbor == 1) ? yDistLeft : xDistTop;
                                        break;
                                    case 3:
                                        distance = (neighbor == 0) ? yDistRight : xDistTop;
                                        break;
                                }

                                finalPos[i].X = finalPos[neighbor].X + distance * MathF.Cos(f);
                                finalPos[i].Y = finalPos[neighbor].Y + distance * -MathF.Sin(f);
                                seenFlags |= (byte)(1 << i);
                                break;
                            }
                        }
                    }
                    if ((seenFlags & 15) == 15) break;
                }

                pWarper.setSource(finalPos[0].X, finalPos[0].Y, finalPos[1].X, finalPos[1].Y, finalPos[2].X, finalPos[2].Y, finalPos[3].X, finalPos[3].Y);
                float[] fWarped = pWarper.warp();
                resultPos.X = fWarped[0];
                resultPos.Y = fWarped[1];
                //Console.WriteLine($"OUT {resultPos.X} | {resultPos.Y}");

                if (!fourPtsFound)
                {
                    median.Y = (finalPos[0].Y + finalPos[1].Y + finalPos[2].Y + finalPos[3].Y + 0.002f) / 4;
                    median.X = (finalPos[0].X + finalPos[1].X + finalPos[2].X + finalPos[3].X + 0.002f) / 4;
                }

                // If 4 LEDS can be seen and loop has run through 5 times update offsets and height      
                if (((1 << 5) & see[0] & see[1] & see[2] & see[3]) != 0)
                {
                    angleOffset[0] = angleTop - (angleLeft - MathF.PI);
                    angleOffset[1] = -(angleTop - angleRight);
                    angleOffset[2] = -(angleBottom - angleLeft);
                    angleOffset[3] = angleBottom - (angleRight - MathF.PI);
                    height = (yDistLeft + yDistRight) / 2.0f;
                    width = (xDistTop + xDistBottom) / 2.0f;
                }

                // If 2 LEDS can be seen and loop has run through 5 times update angle and distances
                if (((1 << 5) & see[2] & see[1]) != 0)
                {
                    angleLeft = MathF.Atan2(finalPos[1].Y - finalPos[2].Y, finalPos[2].X - finalPos[1].X);
                    yDistLeft = MathF.Hypot((finalPos[2].Y - finalPos[1].Y), (finalPos[2].X - finalPos[1].X));
                }

                if (((1 << 5) & see[0] & see[3]) != 0)
                {
                    angleRight = MathF.Atan2(finalPos[0].Y - finalPos[3].Y, finalPos[3].X - finalPos[0].X);
                    yDistRight = MathF.Hypot((finalPos[0].Y - finalPos[3].Y), (finalPos[0].X - finalPos[3].X));
                }

                if (((1 << 5) & see[2] & see[3]) != 0)
                {
                    angleTop = MathF.Atan2(finalPos[2].Y - finalPos[3].Y, finalPos[3].X - finalPos[2].X);
                    xDistTop = MathF.Hypot((finalPos[2].Y - finalPos[3].Y), (finalPos[2].X - finalPos[3].X));
                }

                if (((1 << 5) & see[0] & see[1]) != 0)
                {
                    angleBottom = MathF.Atan2(finalPos[1].Y - finalPos[0].Y, finalPos[0].X - finalPos[1].X);
                    xDistBottom = MathF.Hypot((finalPos[1].Y - finalPos[0].Y), (finalPos[1].X - finalPos[0].X));
                }

                // Add tilt correction
                //angle = -(MathF.Atan2(finalPos[0].Y - finalPos[1].Y, finalPos[1].X - finalPos[0].X) + MathF.Atan2(finalPos[2].Y - finalPos[3].Y, finalPos[3].X - finalPos[2].X)) / 2;
                //if (angle < 0) angle += MathF.PI * 2;
                angle = Roll;
                //Console.WriteLine($"ANGLE: {angle}");

                /*if (see.Count(seen => seen == 0) >= 2 || Double.IsNaN(resultPos.X) || Double.IsNaN(resultPos.Y))
                {
                    CursorPos err = lastPos;
                    err.OutOfReach = true;
                    err.OffScreen = true;

                    return err;
                }
                */
            }
            else if (Settings.Default.pointer_4IRMode == "diamond")
            {
                byte seenFlags = 0;
                int foundCount = 0;
                var visiblePoints = new List<PointF>();

                // --- PHASE 1: Identify points ---
                for (int i = 0; i < 4; i++)
                {
                    if (irState.IRSensors[i].Found)
                    {
                        visiblePoints.Add(irState.IRSensors[i].Position);
                        foundCount++;
                    }
                }

                if (foundCount >= 3)
                {
                    double Roll = Math.Atan2(wiimoteState.AccelState.Values.X, wiimoteState.AccelState.Values.Z);

                    median = new PointF();
                    foreach (var p in visiblePoints) { median.X += p.X; median.Y += p.Y; }
                    median.X /= foundCount;
                    median.Y /= foundCount;
                    foreach (var p in visiblePoints)
                    {
                        double point_angle = Math.Atan2(p.Y - median.Y, p.X - median.X) - Roll;
                        point_angle += (MathF.PI / 4);
                        if (point_angle < 0) point_angle += 2 * MathF.PI;
                        if (point_angle > 2 * MathF.PI) point_angle -= 2 * MathF.PI;
                        int index = (int)(point_angle / (MathF.PI / 2));
                        int finalIndex = 0;
                        switch (index)
                        {
                            case 0: finalIndex = 1; break; // Right
                            case 1: finalIndex = 2; break; // Bottom
                            case 2: finalIndex = 3; break; // Left
                            case 3: finalIndex = 0; break; // Top
                        }
                        finalPos[finalIndex] = p;
                        seenFlags |= (byte)(1 << finalIndex);
                    }
                }

                // --- PHASE 2: Reconstruction ---
                if (foundCount == 3)
                {
                    // Tu lógica perfecta para 3 puntos
                    int missingIdx = -1;
                    for (int i = 0; i < 4; i++) { if ((seenFlags & (1 << i)) == 0) { missingIdx = i; break; } }
                    int prevIdx = (missingIdx + 3) % 4;
                    int nextIdx = (missingIdx + 1) % 4;
                    int oppositeIdx = (missingIdx + 2) % 4;
                    PointF center = new PointF { X = (finalPos[prevIdx].X + finalPos[nextIdx].X) / 2, Y = (finalPos[prevIdx].Y + finalPos[nextIdx].Y) / 2 };
                    finalPos[missingIdx] = new PointF { X = 2 * center.X - finalPos[oppositeIdx].X, Y = 2 * center.Y - finalPos[oppositeIdx].Y };

                    seenFlags = 0x0F;
                }

                // 3. APRENDIZAJE CONTINUO (Lógica OpenFIRE)
                if (seenFlags != 0)
                {
                    const uint STABILITY_CHECK = (1 << 5);
                    for (int i = 0; i < 4; i++)
                    {
                        if ((seenFlags & (1 << i)) != 0) { see[i] = (see[i] << 1) | 1; }
                        else { see[i] = 0; }
                    }
                    if ((seenFlags & 0x0F) == 0x0F)
                    {
                        angleTR = MathF.Atan2(finalPos[0].Y - finalPos[1].Y, finalPos[1].X - finalPos[0].X);
                        angleBR = MathF.Atan2(finalPos[1].Y - finalPos[2].Y, finalPos[2].X - finalPos[1].X);
                        angleBL = MathF.Atan2(finalPos[2].Y - finalPos[3].Y, finalPos[3].X - finalPos[2].X);
                        angleTL = MathF.Atan2(finalPos[3].Y - finalPos[0].Y, finalPos[0].X - finalPos[3].X);
                        double current_global_angle = MathF.Atan2(finalPos[3].Y - finalPos[1].Y, finalPos[1].X - finalPos[3].X);
                        offsetTR = NormalizeAngle(angleTR - (float)current_global_angle);
                        offsetBR = NormalizeAngle(angleBR - (float)current_global_angle);
                        offsetBL = NormalizeAngle(angleBL - (float)current_global_angle);
                        offsetTL = NormalizeAngle(angleTL - (float)current_global_angle);
                    }

                    if ((see[0] & see[1] & STABILITY_CHECK) == STABILITY_CHECK)
                    {
                        angle = MathF.Atan2(finalPos[0].Y - finalPos[1].Y, finalPos[1].X - finalPos[0].X) - offsetTR;
                    }
                    else if ((see[1] & see[2] & STABILITY_CHECK) == STABILITY_CHECK)
                    {
                        angle = MathF.Atan2(finalPos[1].Y - finalPos[2].Y, finalPos[2].X - finalPos[1].X) - offsetBR;
                    }
                    else if ((see[2] & see[3] & STABILITY_CHECK) == STABILITY_CHECK)
                    {
                        angle = MathF.Atan2(finalPos[2].Y - finalPos[3].Y, finalPos[3].X - finalPos[2].X) - offsetBL;
                    }
                    else if ((see[3] & see[0] & STABILITY_CHECK) == STABILITY_CHECK)
                    {
                        angle = MathF.Atan2(finalPos[3].Y - finalPos[0].Y, finalPos[0].X - finalPos[3].X) - offsetTL;
                    }

                    //double Roll = Math.Atan2(wiimoteState.AccelState.Values.X, wiimoteState.AccelState.Values.Z);
                    //angle = Roll;
                }

                // --- PHASE 3: WARPER Y SALIDA ---
                if ((seenFlags & 0x0F) == 0x0F)
                {
                    width = MathF.Hypot(finalPos[1].Y - finalPos[3].Y, finalPos[1].X - finalPos[3].X);
                    height = MathF.Hypot(finalPos[2].Y - finalPos[0].Y, finalPos[2].X - finalPos[0].X);


                    pWarper.setSource(finalPos[1].X, finalPos[1].Y, finalPos[2].X, finalPos[2].Y, finalPos[3].X, finalPos[3].Y, finalPos[0].X, finalPos[0].Y);
                    float[] fWarped = pWarper.warp();
                    resultPos.X = fWarped[0];
                    resultPos.Y = fWarped[1];

                    /*for (int i = 0; i < 4; i++)
                    {
                        Trace.Write($"{i}: [{finalPos[i].X}, {finalPos[i].Y}, ");
                    }
                    */

                    //Trace.WriteLine("");

                    if (double.IsNaN(resultPos.X) || double.IsNaN(resultPos.Y))
                    {
                        CursorPos err = lastPos;
                        err.OutOfReach = true;
                        err.OffScreen = true;

                        return err;
                    }
                }
                else
                {
                    CursorPos err = lastPos;
                    err.OutOfReach = true;
                    err.OffScreen = true;

                    return err;
                }

            }


            /*System.Windows.Point filteredPoint = coordFilter.AddGetFilteredCoord(new System.Windows.Point(relativePosition.X, relativePosition.Y), 1.0, 1.0);

            relativePosition.X = (float)filteredPoint.X;
            relativePosition.Y = (float)filteredPoint.Y;

            Vector smoothedPoint = smoothingBuffer.AddAndGet(new Vector(relativePosition.X, relativePosition.Y));
            */

            median.X = 1 - median.X;
            x = Convert.ToInt32((float)maxWidth * (median.X) + minXPos);
            y = Convert.ToInt32((float)maxHeight * median.Y + minYPos) + offsetY;

            marginX = Math.Min(1.0, Math.Max(0.0, (median.X - midMarginX) * marginBoundsX));
            marginY = Math.Min(1.0, Math.Max(0.0, (median.Y - (marginOffsetY + midMarginX)) * marginBoundsY));

            //System.Diagnostics.Trace.WriteLine($"{marginY} | {relativePosition.Y}");

            lightbarX = (resultPos.X - topLeftPt.X) * boundsX;
            lightbarY = (resultPos.Y - topLeftPt.Y) * boundsY;

            //Trace.WriteLine($"Y: {resultPos.Y} | {lightbarY} | {topLeftPt.Y}");
            //System.Diagnostics.Trace.WriteLine($"X {lightbarX} | {relativePosition.X}");
            //System.Diagnostics.Trace.WriteLine($"Y {lightbarY} | {relativePosition.Y}");

            if (x <= 0)
            {
                x = 0;
            }
            else if (x >= primaryScreen.Bounds.Width)
            {
                x = primaryScreen.Bounds.Width - 1;
            }
            if (y <= 0)
            {
                y = 0;
            }
            else if (y >= primaryScreen.Bounds.Height)
            {
                y = primaryScreen.Bounds.Height - 1;
            }

            //Console.WriteLine("{0} {1} {2}", relativePosition.X, marginX, x / (double)primaryScreen.Bounds.Width);

            //CursorPos result = new CursorPos(x, y, smoothedPoint.X, smoothedPoint.Y, smoothedRotation);
            CursorPos result = new CursorPos(x, y, median.X, median.Y, angle,
                marginX, marginY, lightbarX, lightbarY, width, height);

            // Check if lightgun coors are considered offscreen. Clamp here
            if (lightbarX < 0.0 || lightbarX > 1.0 || lightbarY < 0.0 || lightbarY > 1.0)
            {
                result.OffScreen = true;
                result.LightbarX = Math.Min(1.0, Math.Max(0.0, lightbarX));
                result.LightbarY = Math.Min(1.0, Math.Max(0.0, lightbarY));
            }

            //Console.WriteLine($"SHITTER {lightbarX} | {lightbarY} | {result.OffScreen}");
            lastPos = result;
            return result;
        }

        private PointF rotatePoint(PointF point, double angle)
        {
            double sin = Math.Sin(angle * -1);
            double cos = Math.Cos(angle * -1);

            double xnew = point.X * cos - point.Y * sin;
            double ynew = point.X * sin + point.Y * cos;

            PointF result;

            xnew = Math.Min(0.5, Math.Max(-0.5, xnew));
            ynew = Math.Min(0.5, Math.Max(-0.5, ynew));

            result.X = (float)xnew;
            result.Y = (float)ynew;

            return result;
        }

        public void RecalculateFullLightgun()
        {
            targetAspectRatio = 0.0;

            topLeftPt = trueTopLeftPt;
            bottomRightPt = trueBottomRightPt;

            recalculateLightgunCoordBounds();
        }

        public void RecalculateLightgunAspect(double targetAspect)
        {
            this.targetAspectRatio = targetAspect;
            float boundLeftX = 0.0f, boundTopY = 0.0f;
            float boundRightX = 1.0f, boundBottomY = 1.0f;

            double sourceAspect = primaryScreen.Bounds.Width /
                (double)primaryScreen.Bounds.Height;

            if (sourceAspect > targetAspect)
            {
                // Pillarbox
                double dead = (1.0 - (targetAspect / sourceAspect)) / 2.0;
                boundLeftX = (float)dead;
                boundRightX = (float)(1.0 - dead);
            }
            else
            {
                // Letterbox
                double dead = (1.0 - (sourceAspect / targetAspect)) / 2.0;
                boundTopY = (float)dead;
                boundBottomY = (float)(1.0 - dead);
            }

            topLeftPt = new PointF()
            {
                X = boundLeftX,
                Y = boundTopY
            };

            bottomRightPt = new PointF()
            {
                X = boundRightX,
                Y = boundBottomY
            };

            recalculateLightgunCoordBounds();
        }
    }

    public static class MathF
    {
        public const float PI = (float)Math.PI;
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Cos(float d) => (float)Math.Cos(d);
        public static float Round(float a) => (float)Math.Round(a);
        public static float Sin(float a) => (float)Math.Sin(a);
        public static float Hypot(float p, float b) => (float)Math.Sqrt(Math.Pow(p, 2) + Math.Pow(b, 2));
        public static float Sqrt(float d) => (float)Math.Sqrt(d);
        public static float Max(float val1, float val2) => (float)Math.Max(val1, val2);
        public static float Min(float val1, float val2) => (float)Math.Min(val1, val2);
    }
}
