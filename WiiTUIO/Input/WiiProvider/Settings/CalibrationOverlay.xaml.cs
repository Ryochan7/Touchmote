using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Timers;
using WiiTUIO.DeviceUtils;
using WiiTUIO.Properties;
using PointF = WiimoteLib.PointF;
using System.Diagnostics;
using WiiTUIO.Filters;

namespace WiiTUIO.Provider
{
    /// <summary>
    /// Interaction logic for CalibrationOverlay.xaml
    /// </summary>
    public partial class CalibrationOverlay : Window
    {
        private enum CalibrationStep : ushort
        {
            None,
            CenterScreen,
            BottomRight,
            TopLeft,
            Done,
        }

        private const string CALIB_TEST_INTRO_TEXT = " Calib Test. Press A or B to start calibration. \nPress Home to Close.";

        private WiiKeyMapper keyMapper;
        private static CalibrationOverlay defaultInstance;

        private System.Windows.Forms.Screen primaryScreen;
        private IntPtr previousForegroundWindow = IntPtr.Zero;

        private Timer buttonTimer;

        private bool hidden = true;
        private bool timerElapsed = false;

        private CalibrationStep step = CalibrationStep.None;

        private float topOffset;
        private float bottomOffset;
        private float leftOffset;
        private float rightOffset;

        private float topBackup;
        private float bottomBackup;
        private float leftBackup;
        private float rightBackup;
        private float tlBackup;
        private float trBackup;
        private float centerXBackup;
        private float centerYBackup;

        private double marginXBackup;
        private double marginYBackup;

        // Constant for the side length of the triangle
        private const double TRIANGLE_SIDE_LENGTH = 20.0;

        private CalibPointsViewModel calibPointVM;

        /// <summary>
        /// An event which is raised once calibration is finished.
        /// </summary>
        public event Action OnCalibrationFinished;

        public static CalibrationOverlay Current
        {
            get
            {
                if (defaultInstance == null)
                {
                    defaultInstance = new CalibrationOverlay();
                }
                return defaultInstance;
            }
        }

        public CalibrationOverlay()
        {
            InitializeComponent();

            primaryScreen = DeviceUtil.GetScreen(Settings.Default.primaryMonitor);

            Settings.Default.PropertyChanged += SettingsChanged;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;

            this.CalibrationCanvas.Visibility = Visibility.Hidden;

            buttonTimer = new Timer();
            buttonTimer.Interval = 1000;
            buttonTimer.AutoReset = true;
            buttonTimer.Elapsed += buttonTimer_Elapsed;

            calibPointVM = new CalibPointsViewModel();
            SetupCalibPointEvents();

            //Compensate for DPI settings

            Loaded += (o, e) =>
            {
                this.updateWindowToScreen(primaryScreen);

                //Prevent OverlayWindow from showing up in alt+tab menu.
                UIHelpers.HideFromAltTab(this);
            };
        }

        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            this.primaryScreen = DeviceUtils.DeviceUtil.GetScreen(Settings.Default.primaryMonitor);
            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                this.updateWindowToScreen(primaryScreen);
            }));
        }

        private void updateWindowToScreen(System.Windows.Forms.Screen screen)
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            Matrix transformMatrix = source.CompositionTarget.TransformToDevice;

            this.Width = screen.Bounds.Width * transformMatrix.M22;
            this.Height = screen.Bounds.Height * transformMatrix.M11;
            UIHelpers.SetWindowPos((new WindowInteropHelper(this)).Handle, IntPtr.Zero, screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height, UIHelpers.SetWindowPosFlags.SWP_NOACTIVATE | UIHelpers.SetWindowPosFlags.SWP_NOZORDER);
            this.CalibrationCanvas.Width = this.Width;
            this.CalibrationCanvas.Height = this.Height;
            UIHelpers.TopmostFix(this);
        }

        private void SettingsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "primaryMonitor")
            {
                primaryScreen = DeviceUtil.GetScreen(Settings.Default.primaryMonitor);
                Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    this.updateWindowToScreen(primaryScreen);
                }));
            }
        }

        public void StartCalibration(WiiKeyMapper keyMapper)
        {
            if (this.hidden)
            {
                this.hidden = false;

                this.keyMapper = keyMapper;
                this.keyMapper.SwitchToCalibration();
                this.keyMapper.OnButtonDown += keyMapper_OnButtonDown;
                this.keyMapper.OnButtonUp += keyMapper_OnButtonUp;
                buttonTimer.Elapsed += buttonTimer_Elapsed;

                previousForegroundWindow = UIHelpers.GetForegroundWindow();
                if (previousForegroundWindow == null)
                {
                    previousForegroundWindow = IntPtr.Zero;
                }

                Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    this.Activate();

                    // Hide all lines and triangles initially
                    VerticalLineLeft.Visibility = Visibility.Hidden;
                    VerticalLineRight.Visibility = Visibility.Hidden;
                    HorizontalLineCenter.Visibility = Visibility.Hidden;
                    VerticalLineCenter.Visibility = Visibility.Hidden;

                    TriangleLeftTop.Visibility = Visibility.Hidden;
                    TriangleLeftBottom.Visibility = Visibility.Hidden;
                    TriangleRightTop.Visibility = Visibility.Hidden;
                    TriangleRightBottom.Visibility = Visibility.Hidden;
                    TriangleCenterTop.Visibility = Visibility.Hidden;
                    TriangleCenterBottom.Visibility = Visibility.Hidden;
                    TriangleCenterLeft.Visibility = Visibility.Hidden;
                    TriangleCenterRight.Visibility = Visibility.Hidden;

                    // Hide all grid lines by default
                    GridLineV1.Visibility = Visibility.Hidden;
                    GridLineV2.Visibility = Visibility.Hidden;
                    GridLineV3.Visibility = Visibility.Hidden;
                    GridLineV4.Visibility = Visibility.Hidden;
                    GridLineV5.Visibility = Visibility.Hidden;
                    GridLineH1.Visibility = Visibility.Hidden;
                    GridLineH2.Visibility = Visibility.Hidden;
                    GridLineH3.Visibility = Visibility.Hidden;
                    GridLineH4.Visibility = Visibility.Hidden;
                    GridLineH5.Visibility = Visibility.Hidden;
                    // Hide all lines and triangles initially
                    VerticalLineLeft.Visibility = Visibility.Hidden;
                    VerticalLineRight.Visibility = Visibility.Hidden;
                    HorizontalLineCenter.Visibility = Visibility.Hidden;
                    VerticalLineCenter.Visibility = Visibility.Hidden;

                    TriangleLeftTop.Visibility = Visibility.Hidden;
                    TriangleLeftBottom.Visibility = Visibility.Hidden;
                    TriangleRightTop.Visibility = Visibility.Hidden;
                    TriangleRightBottom.Visibility = Visibility.Hidden;
                    TriangleCenterTop.Visibility = Visibility.Hidden;
                    TriangleCenterBottom.Visibility = Visibility.Hidden;
                    TriangleCenterLeft.Visibility = Visibility.Hidden;
                    TriangleCenterRight.Visibility = Visibility.Hidden;

                    // Hide all grid lines by default
                    GridLineV1.Visibility = Visibility.Hidden;
                    GridLineV2.Visibility = Visibility.Hidden;
                    GridLineV3.Visibility = Visibility.Hidden;
                    GridLineV4.Visibility = Visibility.Hidden;
                    GridLineV5.Visibility = Visibility.Hidden;
                    GridLineH1.Visibility = Visibility.Hidden;
                    GridLineH2.Visibility = Visibility.Hidden;
                    GridLineH3.Visibility = Visibility.Hidden;
                    GridLineH4.Visibility = Visibility.Hidden;
                    GridLineH5.Visibility = Visibility.Hidden;

                    /*Color pointColor = IDColor.getColor(keyMapper.WiimoteID);
                    pointColor.R = (byte)(pointColor.R * 0.8);
                    pointColor.G = (byte)(pointColor.G * 0.8);
                    pointColor.B = (byte)(pointColor.B * 0.8);
                    SolidColorBrush brush = new SolidColorBrush(pointColor);
                    */

                    // Calculate the height of an equilateral triangle (distance from vertex to base)
                    double triangleHeight = TRIANGLE_SIDE_LENGTH * Math.Sqrt(3) / 2;
                    // Half the base of the triangle
                    double halfBase = TRIANGLE_SIDE_LENGTH / 2;

                    double centerX = this.ActualWidth / 2; // Defined here for use in both modes
                    double centerY = this.ActualHeight / 2; // Defined here for use in both modes

                    SolidColorBrush currentBrush = new SolidColorBrush(Colors.Green); // Default green color

                    // If keyMapper is available, use its color for consistency
                    if (keyMapper != null)
                    {
                        Color pointColor = IDColor.getColor(keyMapper.WiimoteID);
                        pointColor.R = (byte)(pointColor.R * 0.8);
                        pointColor.G = (byte)(pointColor.G * 0.8);
                        pointColor.B = (byte)(pointColor.B * 0.8);
                        currentBrush = new SolidColorBrush(pointColor);
                    }

                    // Calculate a lighter green color for the grid (even lighter)
                    Color lighterGreen = Color.FromArgb(
                        128,
                        (byte)Math.Min(255, currentBrush.Color.R + 100), // Increased brightness
                        (byte)Math.Min(255, currentBrush.Color.G + 100), // Increased brightness
                        (byte)Math.Min(255, currentBrush.Color.B + 100)  // Increased brightness
                    );
                    SolidColorBrush lighterBrush = new SolidColorBrush(lighterGreen);


                    if (Settings.Default.pointer_4IRMode == "diamond")
                    {
                        // Horizontal line from right center to left
                        HorizontalLineCenter.X1 = this.ActualWidth;
                        HorizontalLineCenter.Y1 = centerY;
                        HorizontalLineCenter.X2 = 0;
                        HorizontalLineCenter.Y2 = centerY;
                        HorizontalLineCenter.Stroke = currentBrush;
                        HorizontalLineCenter.Visibility = Visibility.Visible;

                        // Vertical line from top center to bottom
                        VerticalLineCenter.X1 = centerX;
                        VerticalLineCenter.Y1 = 0;
                        VerticalLineCenter.X2 = centerX;
                        VerticalLineCenter.Y2 = this.ActualHeight;
                        VerticalLineCenter.Stroke = currentBrush;
                        VerticalLineCenter.Visibility = Visibility.Visible;

                        // Triangles for central lines (diamond)
                        // Top Center Triangle (base at X=centerX, Y=0, points downwards)
                        TriangleCenterTop.Points = new PointCollection
                        {
                            new System.Windows.Point(centerX, triangleHeight),
                            new System.Windows.Point(centerX - halfBase, 0),
                            new System.Windows.Point(centerX + halfBase, 0)
                        };
                        TriangleCenterTop.Fill = currentBrush;
                        TriangleCenterTop.Visibility = Visibility.Visible;

                        // Bottom Center Triangle (base at X=centerX, Y=ActualHeight, points upwards)
                        TriangleCenterBottom.Points = new PointCollection
                        {
                            new System.Windows.Point(centerX, this.ActualHeight - triangleHeight),
                            new System.Windows.Point(centerX - halfBase, this.ActualHeight),
                            new System.Windows.Point(centerX + halfBase, this.ActualHeight)
                        };
                        TriangleCenterBottom.Fill = currentBrush;
                        TriangleCenterBottom.Visibility = Visibility.Visible;

                        // Left Center Triangle (base at Y=centerY, X=0, points right)
                        TriangleCenterLeft.Points = new PointCollection
                        {
                            new System.Windows.Point(triangleHeight, centerY),
                            new System.Windows.Point(0, centerY - halfBase),
                            new System.Windows.Point(0, centerY + halfBase)
                        };
                        TriangleCenterLeft.Fill = currentBrush;
                        TriangleCenterLeft.Visibility = Visibility.Visible;

                        // Right Center Triangle (base at Y=centerY, X=ActualWidth, points left)
                        TriangleCenterRight.Points = new PointCollection
                        {
                            new System.Windows.Point(this.ActualWidth - triangleHeight, centerY),
                            new System.Windows.Point(this.ActualWidth, centerY - halfBase),
                            new System.Windows.Point(this.ActualWidth, centerY + halfBase)
                        };
                        TriangleCenterRight.Fill = currentBrush;
                        TriangleCenterRight.Visibility = Visibility.Visible;
                    }
                    else if (Settings.Default.pointer_4IRMode == "square" || Settings.Default.pointer_4IRMode == "none")
                    {
                        // Logic for "none" or "square" mode (existing vertical lines)
                        double squareSide = this.ActualHeight; // Assuming the "square" is based on height
                                                               // Vertical lines extend across the entire height
                        double leftLineX = centerX - (squareSide / 2);
                        double rightLineX = centerX + (squareSide / 2);

                        VerticalLineLeft.X1 = leftLineX;
                        VerticalLineLeft.Y1 = 0;
                        VerticalLineLeft.X2 = leftLineX;
                        VerticalLineLeft.Y2 = this.ActualHeight;
                        VerticalLineLeft.Stroke = currentBrush;
                        VerticalLineLeft.Visibility = Visibility.Visible;

                        VerticalLineRight.X1 = rightLineX;
                        VerticalLineRight.Y1 = 0;
                        VerticalLineRight.X2 = rightLineX;
                        VerticalLineRight.Y2 = this.ActualHeight;
                        VerticalLineRight.Stroke = currentBrush;
                        VerticalLineRight.Visibility = Visibility.Visible;

                        // Triangles for vertical lines (none/square)
                        // Top Left Triangle (base at Y=0, vertex at leftLineX, points downwards)
                        TriangleLeftTop.Points = new PointCollection
                        {
                            new System.Windows.Point(leftLineX, triangleHeight),
                            new System.Windows.Point(leftLineX - halfBase, 0),
                            new System.Windows.Point(leftLineX + halfBase, 0)
                        };
                        TriangleLeftTop.Fill = currentBrush;
                        TriangleLeftTop.Visibility = Visibility.Visible;

                        // Bottom Left Triangle (base at Y=ActualHeight, vertex at leftLineX, points upwards)
                        TriangleLeftBottom.Points = new PointCollection
                        {
                            new System.Windows.Point(leftLineX, this.ActualHeight - triangleHeight),
                            new System.Windows.Point(leftLineX - halfBase, this.ActualHeight),
                            new System.Windows.Point(leftLineX + halfBase, this.ActualHeight)
                        };
                        TriangleLeftBottom.Fill = currentBrush;
                        TriangleLeftBottom.Visibility = Visibility.Visible;

                        // Top Right Triangle (base at Y=0, vertex at rightLineX, points downwards)
                        TriangleRightTop.Points = new PointCollection
                        {
                            new System.Windows.Point(rightLineX, triangleHeight),
                            new System.Windows.Point(rightLineX + halfBase, 0),
                            new System.Windows.Point(rightLineX - halfBase, 0)
                        };
                        TriangleRightTop.Fill = currentBrush;
                        TriangleRightTop.Visibility = Visibility.Visible;

                        // Bottom Right Triangle (base at Y=ActualHeight, vertex at rightLineX, points upwards)
                        TriangleRightBottom.Points = new PointCollection
                        {
                            new System.Windows.Point(rightLineX, this.ActualHeight - triangleHeight),
                            new System.Windows.Point(rightLineX + halfBase, this.ActualHeight),
                            new System.Windows.Point(rightLineX - halfBase, this.ActualHeight)
                        };
                        TriangleRightBottom.Fill = currentBrush;
                        TriangleRightBottom.Visibility = Visibility.Visible;

                        // --- Logic for the grid ---
                        // The grid will have 5 vertical and 5 horizontal lines, creating 4x4 sections.
                        // The first vertical and horizontal lines will be centered.
                        double gridSpacingX = this.ActualWidth / 6; // For 5 vertical lines (6 sections)
                        double gridSpacingY = this.ActualHeight / 6; // For 5 horizontal lines (6 sections)

                        // Define the dash array for dashed lines
                        DoubleCollection dashArray = new DoubleCollection { 2, 2 }; // 2 units on, 2 units off

                        // Vertical grid lines
                        // The central line (GridLineV3) is already at centerX
                        GridLineV1.X1 = centerX - 2 * gridSpacingX; GridLineV1.Y1 = 0; GridLineV1.X2 = centerX - 2 * gridSpacingX; GridLineV1.Y2 = this.ActualHeight; GridLineV1.Stroke = lighterBrush; GridLineV1.StrokeDashArray = dashArray; GridLineV1.Visibility = Visibility.Visible;
                        GridLineV2.X1 = centerX - gridSpacingX; GridLineV2.Y1 = 0; GridLineV2.X2 = centerX - gridSpacingX; GridLineV2.Y2 = this.ActualHeight; GridLineV2.Stroke = lighterBrush; GridLineV2.StrokeDashArray = dashArray; GridLineV2.Visibility = Visibility.Visible;
                        GridLineV3.X1 = centerX; GridLineV3.Y1 = 0; GridLineV3.X2 = centerX; GridLineV3.Y2 = this.ActualHeight; GridLineV3.Stroke = lighterBrush; GridLineV3.StrokeDashArray = dashArray; GridLineV3.Visibility = Visibility.Visible; // Central vertical line
                        GridLineV4.X1 = centerX + gridSpacingX; GridLineV4.Y1 = 0; GridLineV4.X2 = centerX + gridSpacingX; GridLineV4.Y2 = this.ActualHeight; GridLineV4.Stroke = lighterBrush; GridLineV4.StrokeDashArray = dashArray; GridLineV4.Visibility = Visibility.Visible;
                        GridLineV5.X1 = centerX + 2 * gridSpacingX; GridLineV5.Y1 = 0; GridLineV5.X2 = centerX + 2 * gridSpacingX; GridLineV5.Y2 = this.ActualHeight; GridLineV5.Stroke = lighterBrush; GridLineV5.StrokeDashArray = dashArray; GridLineV5.Visibility = Visibility.Visible;

                        // Horizontal grid lines
                        // The central line (GridLineH3) is already at centerY
                        GridLineH1.X1 = 0; GridLineH1.Y1 = centerY - 2 * gridSpacingY; GridLineH1.X2 = this.ActualWidth; GridLineH1.Y2 = centerY - 2 * gridSpacingY; GridLineH1.Stroke = lighterBrush; GridLineH1.StrokeDashArray = dashArray; GridLineH1.Visibility = Visibility.Visible;
                        GridLineH2.X1 = 0; GridLineH2.Y1 = centerY - gridSpacingY; GridLineH2.X2 = this.ActualWidth; GridLineH2.Y2 = centerY - gridSpacingY; GridLineH2.Stroke = lighterBrush; GridLineH2.StrokeDashArray = dashArray; GridLineH2.Visibility = Visibility.Visible;
                        GridLineH3.X1 = 0; GridLineH3.Y1 = centerY; GridLineH3.X2 = this.ActualWidth; GridLineH3.Y2 = centerY; GridLineH3.Stroke = lighterBrush; GridLineH3.StrokeDashArray = dashArray; GridLineH3.Visibility = Visibility.Visible; // Central horizontal line
                        GridLineH4.X1 = 0; GridLineH4.Y1 = centerY + gridSpacingY; GridLineH4.X2 = this.ActualWidth; GridLineH4.Y2 = centerY + gridSpacingY; GridLineH4.Stroke = lighterBrush; GridLineH4.StrokeDashArray = dashArray; GridLineH4.Visibility = Visibility.Visible;
                        GridLineH5.X1 = 0; GridLineH5.Y1 = centerY + 2 * gridSpacingY; GridLineH5.X2 = this.ActualWidth; GridLineH5.Y2 = centerY + 2 * gridSpacingY; GridLineH5.Stroke = lighterBrush; GridLineH5.StrokeDashArray = dashArray; GridLineH5.Visibility = Visibility.Visible;
                    }

                    this.wiimoteNo.Text = "Wiimote " + keyMapper.WiimoteID;
                    this.wiimoteNo.Foreground = currentBrush;
                    this.insText2.Text = CALIB_TEST_INTRO_TEXT;

                    this.TextBorder.SetValue(Canvas.LeftProperty, 0.5 * this.ActualWidth - (this.TextBorder.ActualWidth / 2));
                    this.TextBorder.SetValue(Canvas.TopProperty, 0.25 * this.ActualHeight - (this.TextBorder.ActualHeight / 2));

                    this.CalibrationCanvas.Opacity = 0.0;
                    this.CalibrationCanvas.Visibility = Visibility.Visible;

                    this.elipse.Stroke = this.lineX.Stroke = this.lineY.Stroke = currentBrush;
                    this.elipse.Fill = new SolidColorBrush(Colors.Black);
                    this.elipse.Fill.Opacity = 0.9;

                    this.elipseTL.Stroke = this.lineXTL.Stroke = this.lineYTL.Stroke = currentBrush;
                    this.elipseTL.Fill = new SolidColorBrush(Colors.Black);
                    this.elipseTL.Fill.Opacity = 0.9;

                    this.elipseBR.Stroke = this.lineXBR.Stroke = this.lineYBR.Stroke = currentBrush;
                    this.elipseBR.Fill = new SolidColorBrush(Colors.Black);
                    this.elipseBR.Fill.Opacity = 0.9;

                    DoubleAnimation animation = UIHelpers.createDoubleAnimation(1.0, 200, false);
                    animation.FillBehavior = FillBehavior.HoldEnd;
                    animation.Completed += delegate (object sender, EventArgs pEvent)
                    {

                    };
                    this.CalibrationCanvas.BeginAnimation(FrameworkElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);

                    this.CalibrationTopLeftPoint.Visibility = Visibility.Visible;
                    this.CalibrationBottomRightPoint.Visibility = Visibility.Visible;

                    DataContext = null;
                    calibPointVM.DisplayDoneVis = true;
                    PopulateCalibPointVM();
                    DataContext = calibPointVM;
                }), null);

                this.movePoint(0.5, 0.5);
                step = CalibrationStep.None;
            }
        }

        private void PopulateCalibPointVM()
        {
            calibPointVM.TopLeftXCoorAdj = this.keyMapper.settings.Left;
            calibPointVM.TopLeftYCoorAdj = this.keyMapper.settings.Top;
            calibPointVM.BottomRightXCoorAdj = this.keyMapper.settings.Right;
            calibPointVM.BottomRightYCoorAdj = this.keyMapper.settings.Bottom;

            calibPointVM.CenterXCoorAdj = this.keyMapper.settings.CenterX;
            calibPointVM.CenterYCoorAdj = this.keyMapper.settings.CenterY;
        }

        private void SetupCalibPointEvents()
        {
            calibPointVM.TopLeftXCoorAdjChanged += CalibPointVM_TopLeftXCoorAdjChanged;
            calibPointVM.TopLeftYCoorAdjChanged += CalibPointVM_TopLeftYCoorAdjChanged;
            calibPointVM.BottomRightXCoorAdjChanged += CalibPointVM_BottomRightXCoorAdjChanged;
            calibPointVM.BottomRightYCoorAdjChanged += CalibPointVM_BottomRightYCoorAdjChanged;
            calibPointVM.CenterXCoorAdjChanged += CalibPointVM_CenterXCoorAdjChanged;
            calibPointVM.CenterYCoorAdjChanged += CalibPointVM_CenterYCoorAdjChanged;
        }

        private void CalibPointVM_CenterYCoorAdjChanged(object sender, EventArgs e)
        {
            this.keyMapper.settings.CenterY = (float)calibPointVM.CenterYCoorAdj;
        }

        private void CalibPointVM_CenterXCoorAdjChanged(object sender, EventArgs e)
        {
            this.keyMapper.settings.CenterX = (float)calibPointVM.CenterXCoorAdj;
        }

        private void CalibPointVM_BottomRightYCoorAdjChanged(object sender, EventArgs e)
        {
            this.keyMapper.settings.Bottom = (float)calibPointVM.BottomRightYCoorAdj;
        }

        private void CalibPointVM_BottomRightXCoorAdjChanged(object sender, EventArgs e)
        {
            this.keyMapper.settings.Right = (float)calibPointVM.BottomRightXCoorAdj;
        }

        private void CalibPointVM_TopLeftYCoorAdjChanged(object sender, EventArgs e)
        {
            this.keyMapper.settings.Top = (float)calibPointVM.TopLeftYCoorAdj;
        }

        private void CalibPointVM_TopLeftXCoorAdjChanged(object sender, EventArgs e)
        {
            this.keyMapper.settings.Left = (float)calibPointVM.TopLeftXCoorAdj;
        }

        void OverlayWindow_KeyUp(object sender, KeyEventArgs e)
        {
            if (!this.hidden)
            {
                if (e.Key == Key.Escape)
                {
                    HideOverlay();
                }
            }
        }

        private void HideOverlay()
        {
            if (!this.hidden)
            {
                this.hidden = true;
                this.timerElapsed = false;

                this.keyMapper.OnButtonUp -= keyMapper_OnButtonUp;
                this.keyMapper.OnButtonDown -= keyMapper_OnButtonDown;
                this.keyMapper.SwitchToFallback();
                buttonTimer.Elapsed -= buttonTimer_Elapsed;

                Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    if (previousForegroundWindow != IntPtr.Zero)
                    {
                        UIHelpers.SetForegroundWindow(previousForegroundWindow);
                    }
                    DoubleAnimation animation = UIHelpers.createDoubleAnimation(0.0, 200, false);
                    animation.FillBehavior = FillBehavior.HoldEnd;
                    animation.Completed += delegate (object sender, EventArgs pEvent)
                    {
                        this.CalibrationCanvas.Visibility = Visibility.Hidden;

                    };
                    this.CalibrationCanvas.BeginAnimation(FrameworkElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);

                    calibPointVM.DisplayDoneVis = false;
                }), null);

                step = CalibrationStep.None;
            }
        }

        private void finishedCalibration()
        {
            if (Settings.Default.pointer_4IRMode != "none")
            {
                Settings.Default.Save();
            }

            this.keyMapper.settings.SaveCalibrationData();

            // Need to back up current values now. Keeps a cancel from
            // resetting settings to zero.
            topBackup = this.keyMapper.settings.Top;
            bottomBackup = this.keyMapper.settings.Bottom;
            leftBackup = this.keyMapper.settings.Left;
            rightBackup = this.keyMapper.settings.Right;

            centerXBackup = this.keyMapper.settings.CenterX;
            centerYBackup = this.keyMapper.settings.CenterY;
            tlBackup = this.keyMapper.settings.TLled;
            trBackup = this.keyMapper.settings.TRled;

            //this.HideOverlay();

            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                DataContext = null;
                calibPointVM.DisplayDoneVis = true;
                PopulateCalibPointVM();
                DataContext = calibPointVM;
            }), null);
        }

        public void CloseCalibration()
        {
            if (step == CalibrationStep.None || step == CalibrationStep.Done)
            {
                HideOverlay();
            }
            else
            {
                CancelCalibration();
            }
        }

        public void CancelCalibration()
        {
            this.keyMapper.settings.Top = topBackup;
            this.keyMapper.settings.Bottom = bottomBackup;
            this.keyMapper.settings.Left = leftBackup;
            this.keyMapper.settings.Right = rightBackup;

            if (Settings.Default.pointer_4IRMode != "none")
            {
                this.keyMapper.settings.CenterX = centerXBackup;
                this.keyMapper.settings.CenterY = centerYBackup;
                this.keyMapper.settings.TLled = tlBackup;
                this.keyMapper.settings.TRled = trBackup;

                //Settings.Default.CalibrationMarginX = marginXBackup;
                //Settings.Default.CalibrationMarginY = marginYBackup;

                Settings.Default.Save();
            }

            this.keyMapper.settings.SaveCalibrationData();

            this.HideOverlay();
        }

        private void keyMapper_OnButtonUp(WiiButtonEvent e)
        {
            e.Button = e.Button.Replace("OffScreen.", "");
            if (e.Button.ToLower().Equals("a") || e.Button.ToLower().Equals("b"))
            {
                this.buttonTimer.Stop();

                Dispatcher.BeginInvoke(new Action(delegate ()
                {
                    this.wiimoteNo.Text = "Wiimote " + keyMapper.WiimoteID + ":";
                    this.insText2.Text = " aim at the targets and press A or B to calibrate";

                    this.TextBorder.UpdateLayout();
                    this.TextBorder.SetValue(Canvas.LeftProperty, 0.5 * this.ActualWidth - (this.TextBorder.ActualWidth / 2));
                    this.TextBorder.SetValue(Canvas.TopProperty, 0.25 * this.ActualHeight - (this.TextBorder.ActualHeight / 2));
                }), null);

                if (this.timerElapsed || step == CalibrationStep.None ||
                    step == CalibrationStep.Done)
                {
                    switch (step)
                    {
                        case CalibrationStep.None:
                            Dispatcher.BeginInvoke(new Action(delegate ()
                            {
                                this.CalibrationTopLeftPoint.Visibility = Visibility.Hidden;
                                this.CalibrationBottomRightPoint.Visibility = Visibility.Hidden;
                            }), null);

                            //marginXBackup = Settings.Default.CalibrationMarginX;
                            //marginYBackup = Settings.Default.CalibrationMarginY;

                            topBackup = this.keyMapper.settings.Top;
                            bottomBackup = this.keyMapper.settings.Bottom;
                            leftBackup = this.keyMapper.settings.Left;
                            rightBackup = this.keyMapper.settings.Right;

                            if (Settings.Default.pointer_4IRMode == "none")
                            {
                                this.movePoint(1 - marginXBackup, 1 - marginYBackup);

                                step = CalibrationStep.BottomRight;
                            }
                            else
                            {
                                //Settings.Default.CalibrationMarginX = 0;
                                //Settings.Default.CalibrationMarginY = 0;

                                centerXBackup = this.keyMapper.settings.CenterX;
                                centerYBackup = this.keyMapper.settings.CenterY;
                                tlBackup = this.keyMapper.settings.TLled;
                                trBackup = this.keyMapper.settings.TRled;

                                this.keyMapper.settings.Top = 0;
                                this.keyMapper.settings.Bottom = 1;
                                this.keyMapper.settings.Left = 0;
                                this.keyMapper.settings.Right = 1;

                                this.keyMapper.settings.CenterX = 0.5f;
                                this.keyMapper.settings.CenterY = 0.5f;
                                this.keyMapper.settings.TLled = 0.25f;
                                this.keyMapper.settings.TRled = 0.75f;

                                topOffset = 0;
                                bottomOffset = 1;
                                leftOffset = 0;
                                rightOffset = 1;

                                this.movePoint(0.5, 0.5);

                                step = CalibrationStep.CenterScreen;
                            }

                            Dispatcher.BeginInvoke(new Action(delegate ()
                            {
                                calibPointVM.DisplayDoneVis = false;
                                DataContext = null;
                                DataContext = calibPointVM;
                            }), null);

                            break;

                        case CalibrationStep.CenterScreen:
                            this.movePoint(1 - marginXBackup, 1 - marginYBackup);

                            step = CalibrationStep.BottomRight;
                            break;
                        case CalibrationStep.BottomRight:
                            this.movePoint(marginXBackup, marginYBackup);

                            step = CalibrationStep.TopLeft;
                            break;

                        case CalibrationStep.TopLeft:
                            if (Settings.Default.pointer_4IRMode != "none")
                            {
                                this.keyMapper.settings.Top = topOffset;
                                this.keyMapper.settings.Bottom = bottomOffset;
                                this.keyMapper.settings.Left = leftOffset;
                                this.keyMapper.settings.Right = rightOffset;

                                //Settings.Default.CalibrationMarginX = marginXBackup;
                                //Settings.Default.CalibrationMarginY = marginYBackup;
                            }

                            this.movePoint(0.5, 0.5);
                            Dispatcher.BeginInvoke(new Action(delegate ()
                            {
                                // Show all three calibration points for final review
                                this.CalibrationTopLeftPoint.Visibility = Visibility.Visible;
                                this.CalibrationBottomRightPoint.Visibility = Visibility.Visible;

                                this.wiimoteNo.Text = null;
                                this.insText2.Text = "Press A confirm calibration, press B to restart calibration";

                                this.TextBorder.UpdateLayout();
                                this.TextBorder.SetValue(Canvas.LeftProperty, 0.5 * this.ActualWidth - (this.TextBorder.ActualWidth / 2));
                                this.TextBorder.SetValue(Canvas.TopProperty, 0.25 * this.ActualHeight - (this.TextBorder.ActualHeight / 2));
                            }), null);

                            step = CalibrationStep.Done;

                            break;
                        case CalibrationStep.Done:
                            this.movePoint(0.5, 0.5);

                            Dispatcher.BeginInvoke(new Action(delegate ()
                            {
                                this.wiimoteNo.Text = "Wiimote " + keyMapper.WiimoteID;
                                this.insText2.Text = CALIB_TEST_INTRO_TEXT;

                                this.TextBorder.UpdateLayout();
                                this.TextBorder.SetValue(Canvas.LeftProperty, 0.5 * this.ActualWidth - (this.TextBorder.ActualWidth / 2));
                                this.TextBorder.SetValue(Canvas.TopProperty, 0.25 * this.ActualHeight - (this.TextBorder.ActualHeight / 2));

                                // Show all three calibration points
                                this.CalibrationTopLeftPoint.Visibility = Visibility.Visible;
                                this.CalibrationBottomRightPoint.Visibility = Visibility.Visible;

                                DataContext = null;
                                PopulateCalibPointVM();
                                DataContext = calibPointVM;
                            }), null);

                            step = CalibrationStep.None;

                            break;

                        default: break;
                    }
                }

                this.timerElapsed = false;
            }
        }

        private void keyMapper_OnButtonDown(WiiButtonEvent e)
        {
            e.Button = e.Button.Replace("OffScreen.", "");
            if (step == CalibrationStep.Done)
            {
                if (e.Button.ToLower().Equals("a"))
                {
                    finishedCalibration();
                }
                else if (e.Button.ToLower().Equals("b"))
                {
                    if (Settings.Default.pointer_4IRMode == "none")
                    {
                        this.movePoint(1 - marginXBackup, 1 - marginYBackup);

                        Dispatcher.BeginInvoke(new Action(delegate ()
                        {
                            this.CalibrationTopLeftPoint.Visibility = Visibility.Hidden;
                            this.CalibrationBottomRightPoint.Visibility = Visibility.Hidden;
                        }), null);

                        step = CalibrationStep.BottomRight;
                    }
                    else
                    {
                        this.movePoint(0.5, 0.5);

                        Dispatcher.BeginInvoke(new Action(delegate ()
                        {
                            this.CalibrationTopLeftPoint.Visibility = Visibility.Hidden;
                            this.CalibrationBottomRightPoint.Visibility = Visibility.Hidden;
                        }), null);

                        step = CalibrationStep.CenterScreen;
                    }
                }
            }
            else if (step == CalibrationStep.None)
            {
                if (e.Button.ToLower().Equals("a") || e.Button.ToLower().Equals("b"))
                {
                    Dispatcher.BeginInvoke(new Action(delegate ()
                    {
                        this.wiimoteNo.Text = null;
                        this.insText2.Text = " Release to Begin";

                        this.TextBorder.UpdateLayout();
                        this.TextBorder.SetValue(Canvas.LeftProperty, 0.5 * this.ActualWidth - (this.TextBorder.ActualWidth / 2));
                        this.TextBorder.SetValue(Canvas.TopProperty, 0.25 * this.ActualHeight - (this.TextBorder.ActualHeight / 2));
                    }), null);
                }
            }
            else if (e.Button.ToLower().Equals("a") || e.Button.ToLower().Equals("b"))
            {
                if (!this.keyMapper.cursorPos.OutOfReach)
                {
                    this.buttonTimer.Start();
                    Dispatcher.BeginInvoke(new Action(delegate ()
                    {
                        this.wiimoteNo.Text = null;
                        this.insText2.Text = "Hold";

                        this.TextBorder.UpdateLayout();
                        this.TextBorder.SetValue(Canvas.LeftProperty, 0.5 * this.ActualWidth - (this.TextBorder.ActualWidth / 2));
                        this.TextBorder.SetValue(Canvas.TopProperty, 0.25 * this.ActualHeight - (this.TextBorder.ActualHeight / 2));
                    }), null);
                }
                else
                {
                    Dispatcher.BeginInvoke(new Action(delegate ()
                    {
                        this.wiimoteNo.Text = null;
                        this.insText2.Text = "Can't find sensors. Make sure you're at a proper distance and pointing at the screen";

                        this.TextBorder.UpdateLayout();
                        this.TextBorder.SetValue(Canvas.LeftProperty, 0.5 * this.ActualWidth - (this.TextBorder.ActualWidth / 2));
                        this.TextBorder.SetValue(Canvas.TopProperty, 0.25 * this.ActualHeight - (this.TextBorder.ActualHeight / 2));
                    }), null);
                }
            }
        }

        void buttonTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            this.buttonTimer.Stop();
            this.timerElapsed = true;

            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                this.wiimoteNo.Text = null;
                this.insText2.Text = "Release";

                this.TextBorder.UpdateLayout();
                this.TextBorder.SetValue(Canvas.LeftProperty, 0.5 * this.ActualWidth - (this.TextBorder.ActualWidth / 2));
                this.TextBorder.SetValue(Canvas.TopProperty, 0.25 * this.ActualHeight - (this.TextBorder.ActualHeight / 2));
            }), null);

            switch (step)
            {
                case CalibrationStep.CenterScreen:
                    //this.keyMapper.settings.CenterX = (float)((this.keyMapper.cursorPos.RelativeX - 2) * Math.Cos(this.keyMapper.cursorPos.Rotation) - (this.keyMapper.cursorPos.RelativeY - 2) * Math.Sin(this.keyMapper.cursorPos.Rotation) + 2);
                    //this.keyMapper.settings.CenterY = (float)((this.keyMapper.cursorPos.RelativeX - 2) * Math.Sin(-this.keyMapper.cursorPos.Rotation) + (this.keyMapper.cursorPos.RelativeY - 2) * Math.Cos(-this.keyMapper.cursorPos.Rotation) + 2);

                    //float testCenterX = (float)((this.keyMapper.cursorPos.RelativeX - 2) * Math.Cos(this.keyMapper.cursorPos.Rotation) - (this.keyMapper.cursorPos.RelativeY - 2) * Math.Sin(this.keyMapper.cursorPos.Rotation) + 2);
                    //float testCenterY = (float)((this.keyMapper.cursorPos.RelativeX - 2) * Math.Sin(-this.keyMapper.cursorPos.Rotation) + (this.keyMapper.cursorPos.RelativeY - 2) * Math.Cos(-this.keyMapper.cursorPos.Rotation) + 2);

                    PointF rotatePt = rotatePoint(new PointF()
                    { X = (float)this.keyMapper.cursorPos.RelativeX - 0.5f,
                    Y = (float)this.keyMapper.cursorPos.RelativeY - 0.5f }, this.keyMapper.cursorPos.Rotation);
                    this.keyMapper.settings.CenterX = rotatePt.X + 0.5f;
                    this.keyMapper.settings.CenterY = rotatePt.Y + 0.5f;

                    //Trace.WriteLine($"STABLE: {this.keyMapper.settings.CenterX} | {this.keyMapper.settings.CenterY}");
                    //Trace.WriteLine($"OLD UNSTABLE: {testCenterX} | {testCenterY}");

                    this.keyMapper.settings.TLled = (float)(0.5 - ((this.keyMapper.cursorPos.Width / this.keyMapper.cursorPos.Height) / 4));
                    this.keyMapper.settings.TRled = (float)(0.5 + ((this.keyMapper.cursorPos.Width / this.keyMapper.cursorPos.Height) / 4));
                    break;
                case CalibrationStep.BottomRight:
                    if (Settings.Default.pointer_4IRMode == "none")
                    {
                        this.keyMapper.settings.Bottom = (float)this.keyMapper.cursorPos.RelativeY;
                        this.keyMapper.settings.Right = (float)this.keyMapper.cursorPos.RelativeX;
                    }
                    else
                    {
                        bottomOffset = (float)this.keyMapper.cursorPos.LightbarY;
                        rightOffset = (float)this.keyMapper.cursorPos.LightbarX;
                    }
                    break;
                case CalibrationStep.TopLeft:
                    if (Settings.Default.pointer_4IRMode == "none")
                    {
                        this.keyMapper.settings.Top = (float)this.keyMapper.cursorPos.RelativeY;
                        this.keyMapper.settings.Left = (float)this.keyMapper.cursorPos.RelativeX;
                    }
                    else
                    {
                        topOffset = (float)this.keyMapper.cursorPos.LightbarY;
                        leftOffset = (float)this.keyMapper.cursorPos.LightbarX;
                    }
                    break;
                default: break;
            }
        }

        private Point movePoint(double fNormalX, double fNormalY)
        {
            Point tPoint = new Point(fNormalX * this.ActualWidth, fNormalY * this.ActualHeight);

            Dispatcher.BeginInvoke(new Action(delegate ()
            {
                this.CalibrationPoint.Visibility = Visibility.Visible;

                this.CalibrationPoint.SetValue(Canvas.LeftProperty, tPoint.X - (this.CalibrationPoint.ActualWidth / 2));
                this.CalibrationPoint.SetValue(Canvas.TopProperty, tPoint.Y - (this.CalibrationPoint.ActualHeight / 2));

            }), null);

            return tPoint;
        }

        public bool OverlayIsOn()
        {
            return !this.hidden;
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
    }

    class CalibPointsViewModel
    {
        private bool displayDoneVis = true;
        public bool DisplayDoneVis
        {
            get => displayDoneVis;
            set
            {
                if (displayDoneVis == value) return;
                displayDoneVis = value;
            }
        }
        public event EventHandler DisplayDoneVisChanged;

        private double topLeftXCoorAdj;
        public double TopLeftXCoorAdj
        {
            get => topLeftXCoorAdj;
            set
            {
                if (topLeftXCoorAdj == value) return;
                topLeftXCoorAdj = value;
                TopLeftXCoorAdjChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler TopLeftXCoorAdjChanged;

        private double topLeftYCoorAdj;
        public double TopLeftYCoorAdj
        {
            get => topLeftYCoorAdj;
            set
            {
                if (topLeftYCoorAdj == value) return;
                topLeftYCoorAdj = value;
                TopLeftYCoorAdjChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler TopLeftYCoorAdjChanged;

        private double bottomRightXCoorAdj;
        public double BottomRightXCoorAdj
        {
            get => bottomRightXCoorAdj;
            set
            {
                if (bottomRightXCoorAdj == value) return;
                bottomRightXCoorAdj = value;
                BottomRightXCoorAdjChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler BottomRightXCoorAdjChanged;

        private double bottomRightYCoorAdj;
        public double BottomRightYCoorAdj
        {
            get => bottomRightYCoorAdj;
            set
            {
                if (bottomRightYCoorAdj == value) return;
                bottomRightYCoorAdj = value;
                BottomRightYCoorAdjChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler BottomRightYCoorAdjChanged;

        private double centerXCoorAdj;
        public double CenterXCoorAdj
        {
            get => centerXCoorAdj;
            set
            {
                if (centerXCoorAdj == value) return;
                centerXCoorAdj = value;
                CenterXCoorAdjChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler CenterXCoorAdjChanged;

        private double centerYCoorAdj;
        public double CenterYCoorAdj
        {
            get => centerYCoorAdj;
            set
            {
                if (centerYCoorAdj == value) return;
                centerYCoorAdj = value;
                CenterYCoorAdjChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        public event EventHandler CenterYCoorAdjChanged;
    }
}
