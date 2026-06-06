using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WiiTUIO.Provider;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using System.Windows;
using System.Diagnostics;
using WiiTUIO.Filters;
using WiiTUIO.Properties;

namespace WiiTUIO.Output.Handlers.Xinput
{
    public class ViGEmHandler : IButtonHandler, IStickHandler, IRumbleFeedback, ICursorHandler
    {
        private class StickLightData
        {
            private const double CENTER_PT_DEFAULT = 0.5;

            public OneEuroFilter testLightFilterX = new OneEuroFilter(Settings.Default.test_lightgun_oneeuro_mincutoff,
                Settings.Default.test_lightgun_oneeuro_beta, 1.0);
            public OneEuroFilter testLightFilterY = new OneEuroFilter(Settings.Default.test_lightgun_oneeuro_mincutoff,
                Settings.Default.test_lightgun_oneeuro_beta, 1.0);
            public Point previousLightCursorPoint = new Point(CENTER_PT_DEFAULT, CENTER_PT_DEFAULT);
            public long previousLightTime = Stopwatch.GetTimestamp();

            public void Reset()
            {
                // Create empty smoothing filters on profile reset
                testLightFilterX = new OneEuroFilter(Settings.Default.test_lightgun_oneeuro_mincutoff,
                    Settings.Default.test_lightgun_oneeuro_beta, 1.0);
                testLightFilterY = new OneEuroFilter(Settings.Default.test_lightgun_oneeuro_mincutoff,
                    Settings.Default.test_lightgun_oneeuro_beta, 1.0);

                previousLightCursorPoint = new Point(CENTER_PT_DEFAULT, CENTER_PT_DEFAULT);
                previousLightTime = Stopwatch.GetTimestamp();
            }
        }

        private const string PREFIX = "360.";

        private ViGEmBusClient viGEmClient;
        private ViGEmBus360Device device;
        private long id;
        private CursorPositionHelper cursorPositionHelper;

        private StickLightData leftStickLight = new StickLightData();
        private StickLightData rightStickLight = new StickLightData();

        public Action<byte, byte> OnRumble { get; set; }

        private bool rumble1Running = false;
        private bool rumble2Running = false;
        private readonly object rumbleLock = new object();

        public ViGEmHandler(long id)
        {
            this.id = id;
            viGEmClient = ViGEmBusClientSingleton.Default;
            cursorPositionHelper = new CursorPositionHelper();
            device = new ViGEmBus360Device(viGEmClient.VigemTestClient);
            device.OnRumble += Device_OnRumble;

            leftStickLight.Reset();
            rightStickLight.Reset();
        }

        private void Device_OnRumble(byte arg1, byte arg2)
        {
            OnRumble?.Invoke(arg1, arg2);
        }

        public bool connect()
        {
            if (viGEmClient.VigemTestClient != null)
            {
                device.Connect();
                return true;
            }

            return false;
        }

        public bool disconnect()
        {
            if (viGEmClient.VigemTestClient != null)
            {
                device.Disconnect();
                viGEmClient = null;
                return true;
            }

            return false;
        }

        public bool setButtonDown(string key)
        {
            string lowerKey = key.ToLower();
            if (key.Length > 4 && lowerKey.Substring(0, 4).Equals(PREFIX))
            {
                string button = lowerKey.Substring(4);
                switch (button)
                {
                    case "rumble":
                        OnRumble?.Invoke(255, 255);
                        break;
                    case "rumble1":
                        lock (rumbleLock)
                        {
                            if (!rumble1Running)
                            {
                                rumble1Running = true;
                                Thread rumbleThread = new Thread(() =>
                                {
                                    OnRumble?.Invoke(255, 255);
                                    Thread.Sleep(110);
                                    OnRumble?.Invoke(0, 0);

                                    rumble1Running = false;
                                });
                                rumbleThread.Start();
                            }
                        }
                        break;
                    case "rumble2":
                        lock (rumbleLock)
                        {
                            if (!rumble2Running)
                            {
                                rumble2Running = true;
                                Thread rumbleThread = new Thread(() =>
                                {
                                    while (rumble2Running)
                                    {
                                        OnRumble?.Invoke(255, 255);
                                        Thread.Sleep(Settings.Default.mGunIntensity10to200);
                                        OnRumble?.Invoke(0, 0);
                                        Thread.Sleep(Settings.Default.mGunIntensity10to200);
                                    }
                                });
                                rumbleThread.Start();
                            }
                        }
                        break;
                    case "triggerr":
                        device.Cont.RightTrigger = 255;
                        break;
                    case "triggerl":
                        device.Cont.LeftTrigger = 255;
                        break;
                    case "a":
                        device.Cont.SetButtonState(Xbox360Button.A, true);
                        break;
                    case "b":
                        device.Cont.SetButtonState(Xbox360Button.B, true);
                        break;
                    case "x":
                        device.Cont.SetButtonState(Xbox360Button.X, true);
                        break;
                    case "y":
                        device.Cont.SetButtonState(Xbox360Button.Y, true);
                        break;
                    case "back":
                        device.Cont.SetButtonState(Xbox360Button.Back, true);
                        break;
                    case "start":
                        device.Cont.SetButtonState(Xbox360Button.Start, true);
                        break;
                    case "stickpressl":
                        device.Cont.SetButtonState(Xbox360Button.LeftThumb, true);
                        break;
                    case "stickpressr":
                        device.Cont.SetButtonState(Xbox360Button.RightThumb, true);
                        break;
                    case "up":
                        device.Cont.SetButtonState(Xbox360Button.Up, true);
                        break;
                    case "down":
                        device.Cont.SetButtonState(Xbox360Button.Down, true);
                        break;
                    case "right":
                        device.Cont.SetButtonState(Xbox360Button.Right, true);
                        break;
                    case "left":
                        device.Cont.SetButtonState(Xbox360Button.Left, true);
                        break;
                    case "guide":
                        device.Cont.SetButtonState(Xbox360Button.Guide, true);
                        break;
                    case "bumperl":
                        device.Cont.SetButtonState(Xbox360Button.LeftShoulder, true);
                        break;
                    case "bumperr":
                        device.Cont.SetButtonState(Xbox360Button.RightShoulder, true);
                        break;
                    case "stickrright":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbX, 32767);
                        break;
                    case "stickrup":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbY, 32767);
                        break;
                    case "sticklright":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbX, 32767);
                        break;
                    case "sticklup":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbY, 32767);
                        break;
                    case "stickrleft":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbX, -32768);
                        break;
                    case "stickrdown":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbY, -32768);
                        break;
                    case "sticklleft":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbX, -32768);
                        break;
                    case "stickldown":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbY, -32768);
                        break;
                    default:
                        return false; //No valid key code was found
                }
                return true;
            }
            return false;
        }

        public bool setButtonUp(string key)
        {
            string lowerKey = key.ToLower();
            if (key.Length > 4 && lowerKey.Substring(0, 4).Equals(PREFIX))
            {
                string button = lowerKey.Substring(4);
                switch (button)
                {
                    case "rumble":
                        OnRumble?.Invoke(0, 0);
                        break;
                    case "rumble1":
                        OnRumble?.Invoke(0, 0);
                        break;
                    case "rumble2":
                        lock (rumbleLock)
                        {
                            rumble2Running = false;
                        }
                        OnRumble?.Invoke(0, 0);
                        break;
                    case "triggerr":
                        device.Cont.RightTrigger = 0;
                        break;
                    case "triggerl":
                        device.Cont.LeftTrigger = 0;
                        break;
                    case "a":
                        device.Cont.SetButtonState(Xbox360Button.A, false);
                        break;
                    case "b":
                        device.Cont.SetButtonState(Xbox360Button.B, false);
                        break;
                    case "x":
                        device.Cont.SetButtonState(Xbox360Button.X, false);
                        break;
                    case "y":
                        device.Cont.SetButtonState(Xbox360Button.Y, false);
                        break;
                    case "back":
                        device.Cont.SetButtonState(Xbox360Button.Back, false);
                        break;
                    case "start":
                        device.Cont.SetButtonState(Xbox360Button.Start, false);
                        break;
                    case "stickpressl":
                        device.Cont.SetButtonState(Xbox360Button.LeftThumb, false);
                        break;
                    case "stickpressr":
                        device.Cont.SetButtonState(Xbox360Button.RightThumb, false);
                        break;
                    case "up":
                        device.Cont.SetButtonState(Xbox360Button.Up, false);
                        break;
                    case "down":
                        device.Cont.SetButtonState(Xbox360Button.Down, false);
                        break;
                    case "right":
                        device.Cont.SetButtonState(Xbox360Button.Right, false);
                        break;
                    case "left":
                        device.Cont.SetButtonState(Xbox360Button.Left, false);
                        break;
                    case "guide":
                        device.Cont.SetButtonState(Xbox360Button.Guide, false);
                        break;
                    case "bumperl":
                        device.Cont.SetButtonState(Xbox360Button.LeftShoulder, false);
                        break;
                    case "bumperr":
                        device.Cont.SetButtonState(Xbox360Button.RightShoulder, false);
                        break;
                    case "stickrright":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                        break;
                    case "stickrup":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbY, 0);
                        break;
                    case "sticklright":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
                        break;
                    case "sticklup":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
                        break;
                    case "stickrleft":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbX, 0);
                        break;
                    case "stickrdown":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbY, 0);
                        break;
                    case "sticklleft":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbX, 0);
                        break;
                    case "stickldown":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbY, 0);
                        break;
                    default:
                        return false; //No valid key code was found
                }
                return true;
            }
            return false;
        }

        public bool setPosition(string key, CursorPos cursorPos)
        {
            key = key.ToLower();
            if (key.Equals("360.stickl") || key.Equals("360.stickr"))
            {
                if (!cursorPos.OutOfReach)
                {
                    Point smoothedPos = cursorPositionHelper.getFPSRelativePosition(new Point(cursorPos.MarginX, cursorPos.MarginY));

                    double smoothedX = smoothedPos.X;
                    //double smoothedY = 1 - smoothedPos.Y; // Y is inverted
                    double smoothedY = smoothedPos.Y;

                    switch (key)
                    {
                        case "360.stickl":
                            device.Cont.SetAxisValue(Xbox360Axis.LeftThumbX, AxisScale(smoothedX, false));
                            device.Cont.SetAxisValue(Xbox360Axis.LeftThumbY, AxisScale(smoothedY, true));
                            break;
                        case "360.stickr":
                            device.Cont.SetAxisValue(Xbox360Axis.RightThumbX, AxisScale(smoothedX, false));
                            device.Cont.SetAxisValue(Xbox360Axis.RightThumbY, AxisScale(smoothedY, true));
                            break;
                    }
                    return true;

                }
            }
            else if (key.Equals("360.stickl-light") || key.Equals("360.stickr-light") ||
                key.Equals("360.stickl-light-4:3") || key.Equals("360.stickr-light-4:3"))
            {
                long currentTime = Stopwatch.GetTimestamp();
                bool useLeftStick = key.Equals("360.stickl-light") || key.Equals("360.stickl-light-4:3");
                StickLightData tempStickData = useLeftStick ?
                    leftStickLight : rightStickLight;

                long timeElapsed = currentTime - tempStickData.previousLightTime;
                double elapsedMs = timeElapsed * (1.0 / Stopwatch.Frequency);
                //Trace.WriteLine($"ELAPSED DUR: {elapsed}");
                tempStickData.previousLightTime = currentTime;

                if (!cursorPos.OffScreen)
                {
                    Point smoothedPos = new Point();
                    // Adjust sensitivity to work around rounding in filter method
                    smoothedPos.X = tempStickData.testLightFilterX.Filter(cursorPos.LightbarX * 1.001, 1.0 / elapsedMs);
                    smoothedPos.Y = tempStickData.testLightFilterY.Filter(cursorPos.LightbarY * 1.001, 1.0 / elapsedMs);

                    double tempX = smoothedPos.X;

                    // Filter does not go back to absolute zero for reasons. Check
                    // for low number and reset to zero
                    if (Math.Abs(smoothedPos.X) < 0.0001) smoothedPos.X = 0.0;
                    if (Math.Abs(smoothedPos.Y) < 0.0001) smoothedPos.Y = 0.0;

                    // Clamp values
                    smoothedPos.X = Math.Min(1.0, Math.Max(0.0, smoothedPos.X));
                    smoothedPos.Y = Math.Min(1.0, Math.Max(0.0, smoothedPos.Y));

                    short axisX = AxisScale(smoothedPos.X, false);
                    short axisY = AxisScale(smoothedPos.Y, true);

                    //Console.WriteLine($"X: {cursorPos.LightbarX} | Y {cursorPos.LightbarY}");
                    //Console.WriteLine($"AXIS X: {axisX} | AXIS Y {axisY} | {smoothedPos.X} | {tempX}");

                    device.Cont.SetAxisValue(useLeftStick ? Xbox360Axis.LeftThumbX : Xbox360Axis.RightThumbX,
                        axisX);

                    device.Cont.SetAxisValue(useLeftStick ? Xbox360Axis.LeftThumbY : Xbox360Axis.RightThumbY,
                        axisY);

                    tempStickData.previousLightCursorPoint = new Point(cursorPos.LightbarX, cursorPos.LightbarY);
                }
                else
                {
                    // Transform coord system to -0.5 to 0.5
                    double finalX = 0.0;
                    double finalY = 0.0;

                    // If IR cursor is still in range, use raw lightbar coords to find angle. Else, use previous coords
                    double tempX = cursorPos.OutOfReach ? tempStickData.previousLightCursorPoint.X : cursorPos.RawLightbarX;
                    double tempY = cursorPos.OutOfReach ? tempStickData.previousLightCursorPoint.Y : cursorPos.RawLightbarY;

                    double halfX = tempX - 0.5;
                    double halfY = tempY - 0.5;
                    double signX = Math.Sign(halfX);
                    double signY = Math.Sign(halfY);

                    // Take accel angle and max side. Find approximate value for other
                    // side
                    double angle = Math.Atan2(halfY, halfX);

                    if (angle == 0)
                    {
                        finalX = finalY = 0.5;
                    }
                    else if (Math.Abs(halfX) >= Math.Abs(halfY))
                    {
                        finalX = 0.5 * signX;
                        double temp = Math.Cos(angle);
                        double tempHyp = temp != 0.0 ? finalX / temp : 0.0;
                        finalY = tempHyp * Math.Sin(angle);

                        finalX += 0.5;
                        finalY += 0.5;
                    }
                    else
                    {
                        finalY = 0.5 * signY;
                        double temp = Math.Sin(angle);
                        double tempHyp = temp != 0.0 ? finalY / temp : 0.0;
                        finalX = tempHyp * Math.Cos(angle);

                        finalX += 0.5;
                        finalY += 0.5;
                    }

                    Point smoothedPos = new Point();
                    // Adjust sensitivity to work around rounding in filter method
                    smoothedPos.X = tempStickData.testLightFilterX.Filter(finalX * 1.001, 1.0 / elapsedMs);
                    smoothedPos.Y = tempStickData.testLightFilterY.Filter(finalY * 1.001, 1.0 / elapsedMs);

                    //double tempX = smoothedPos.X;

                    // Filter does not go back to absolute zero for reasons. Check
                    // for low number and reset to zero
                    if (Math.Abs(smoothedPos.X) < 0.0001) smoothedPos.X = 0.0;
                    if (Math.Abs(smoothedPos.Y) < 0.0001) smoothedPos.Y = 0.0;

                    // Clamp values
                    smoothedPos.X = Math.Min(1.0, Math.Max(0.0, smoothedPos.X));
                    smoothedPos.Y = Math.Min(1.0, Math.Max(0.0, smoothedPos.Y));

                    short axisX = AxisScale(finalX, false);
                    short axisY = AxisScale(finalY, true);

                    //Console.WriteLine($"X: {cursorPos.LightbarX} | Y {cursorPos.LightbarY}");
                    //Console.WriteLine($"AXIS X: {axisX} | AXIS Y {axisY} | {smoothedPos.X} | {tempX}");

                    device.Cont.SetAxisValue(useLeftStick ? Xbox360Axis.LeftThumbX : Xbox360Axis.RightThumbX,
                        axisX);

                    device.Cont.SetAxisValue(useLeftStick ? Xbox360Axis.LeftThumbY : Xbox360Axis.RightThumbY,
                        axisY);

                    ////testLightFilterX.Filter(0.5, 1.0 / 0.008);
                    ////testLightFilterY.Filter(0.5, 1.0 / 0.008);
                    // Save last known position to smoothing buffer
                    //tempStickData.testLightFilterX.Filter(tempStickData.previousLightCursorPoint.X * 1.001, 1.0 / elapsedMs);
                    //tempStickData.testLightFilterY.Filter(tempStickData.previousLightCursorPoint.Y * 1.001, 1.0 / elapsedMs);
                }

                return true;
            }


            return false;
        }

        public bool setValue(string key, double value)
        {
            string lowerKey = key.ToLower();
            if (key.Length > 4 && lowerKey.Substring(0, 4).Equals(PREFIX))
            {
                key = lowerKey.Substring(4);
                //Make sure value is in range 0-1
                value = value > 1 ? 1 : value;
                value = value < 0 ? 0 : value;
                switch (key)
                {
                    case "sticklright":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbX,
                            AxisScale(0.5 * value + 0.5, false));
                        break;
                    case "sticklleft":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbX,
                            AxisScale(0.5 * -value + 0.5, false));
                        break;
                    case "sticklup":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbY,
                            AxisScale(0.5 * -value + 0.5, true));
                        break;
                    case "stickldown":
                        device.Cont.SetAxisValue(Xbox360Axis.LeftThumbY,
                            AxisScale(0.5 * value + 0.5, true));
                        break;
                    case "stickrright":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbX,
                            AxisScale(0.5 * value + 0.5, false));
                        break;
                    case "stickrleft":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbX,
                            AxisScale(0.5 * -value + 0.5, false));
                        break;
                    case "stickrup":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbY,
                            AxisScale(0.5 * -value + 0.5, true));
                        break;
                    case "stickrdown":
                        device.Cont.SetAxisValue(Xbox360Axis.RightThumbY,
                            AxisScale(0.5 * value + 0.5, true));
                        break;
                    case "triggerr":
                        device.Cont.RightTrigger = (byte)(value * 255);
                        break;
                    case "triggerl":
                        device.Cont.LeftTrigger = (byte)(value * 255);
                        break;
                    default:
                        return false; //No valid key was found
                }
                return true;
            }
            return false;
        }

        public bool startUpdate()
        {
            return true;
        }

        public bool endUpdate()
        {
            if (viGEmClient.VigemTestClient != null)
            {
                device.Update();
                return true;
            }

            return false;
        }

        public bool reset()
        {
            device.Reset();

            // Reset stick lightgun data on profile change
            leftStickLight.Reset();
            rightStickLight.Reset();
            return true;
        }

        private short AxisScale(double Value, bool Flip)
        {
            unchecked
            {
                float temp = (float)Value;
                if (Flip) temp = (temp - 0.5f) * -1.0f + 0.5f;

                return (short)(temp * ViGEmBus360Device.outputResolution + (-32768));
            }

            /*unchecked
            {
                Value -= 0x80;

                //float temp = (Value - (-128)) / (float)inputResolution;
                float temp = (Value - (-128)) * reciprocalInputResolution;
                if (Flip) temp = (temp - 0.5f) * -1.0f + 0.5f;

                return (short)(temp * outputResolution + (-32768));
            }
            */
        }
    }
}
