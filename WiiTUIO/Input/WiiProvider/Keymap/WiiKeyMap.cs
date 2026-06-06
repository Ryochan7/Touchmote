using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WiimoteLib;
using WiiTUIO.Output.Handlers;
using WiiTUIO.Properties;

namespace WiiTUIO.Provider
{
    public class WiiKeyMap
    {
        private Dictionary<string, KeymapOutConfig> config;
        public Dictionary<string, KeymapOutConfig> Config => config;
        private Keymap keymap;

        public Action<WiiButtonEvent> OnButtonUp;
        public Action<WiiButtonEvent> OnButtonDown;
        public Action<WiiKeyMapConfigChangedEvent> OnConfigChanged;
        public Action<bool> OnRumble;

        private List<IOutputHandler> outputHandlers;

        public DateTime HomeButtonDown = DateTime.Now;

        private long id;
        private bool prevOffScreen = false;

        private int ShakeCounter = 0;        
        private DateTime lastShakeTime;
        private float lastAccel;
        private float smoothedAccelX;
        private float smoothedAccelY;
        private float smoothedAccelZ;

        private int nunShakeCounter = 0;        
        private DateTime nunLastShakeTime;
        private float nunLastAccel;
        private float smoothedNunAccelX;
        private float smoothedNunAccelY;
        private float smoothedNunAccelZ;

        private int STABILIZATION_FRAMES = Settings.Default.shake_init_stabilization_frames; // 10 stabilization frames avoid ghost shake when turning wiimote on off several times when using very low threshold
        private int wiimoteStabilizationCounter = 0;
        private int nunchukStabilizationCounter = 0;

        private Dictionary<string, bool> PressedButtons = new Dictionary<string, bool>()
        {
            {"OnScreen",false},
            {"OffScreen",false},
            {"AccelX+",false},
            {"AccelX-",false},
            {"AccelY+",false},
            {"AccelY-",false},
            {"AccelZ+",false},
            {"AccelZ-",false},
            {"Shake",false},
            {"Nunchuk.StickUp",false},
            {"Nunchuk.StickDown",false},
            {"Nunchuk.StickLeft",false},
            {"Nunchuk.StickRight",false},
            {"Nunchuk.AccelX+",false},
            {"Nunchuk.AccelX-",false},
            {"Nunchuk.AccelY+",false},
            {"Nunchuk.AccelY-",false},
            {"Nunchuk.AccelZ+",false},
            {"Nunchuk.AccelZ-",false},
            {"Nunchuk.Shake",false},
            {"Classic.StickLUp",false},
            {"Classic.StickLDown",false},
            {"Classic.StickLLeft",false},
            {"Classic.StickLRight",false},
            {"Classic.StickRUp",false},
            {"Classic.StickRDown",false},
            {"Classic.StickRLeft",false},
            {"Classic.StickRRight",false}
        };

        public WiiKeyMap(long id, Keymap keymap, List<IOutputHandler> outputHandlers)
        {
            this.id = id;

            this.SetKeymap(keymap);

            this.outputHandlers = outputHandlers;

            foreach (IOutputHandler outputHandler in outputHandlers)
            {
                if (outputHandler is IRumbleFeedback)
                {
                    IRumbleFeedback rumbleFeedback = (IRumbleFeedback)outputHandler;
                    rumbleFeedback.OnRumble += Xinput_OnRumble;
                }
            }
        }

        public void SetKeymap(Keymap keymap)
        {
            if (this.keymap == null || this.keymap.Equals(keymap))
            {
                this.config = new Dictionary<string, KeymapOutConfig>();

                foreach (KeymapInput input in KeymapDatabase.Current.getAvailableInputs())
                {
                    KeymapOutConfig outConfig = keymap.getConfigFor((int)id, input.Key);
                    if (outConfig != null)
                    {
                        this.config.Add(input.Key, outConfig);
                    }
                }

                KeymapOutConfig pointerConfig;
                if (this.config.TryGetValue("Pointer", out pointerConfig) && this.OnConfigChanged != null)
                {
                    this.OnConfigChanged(new WiiKeyMapConfigChangedEvent(keymap.getName(),keymap.getFilename(),pointerConfig.Stack.First().Key));
                }
            }
        }

        public void SendConfigChangedEvt()
        {
            KeymapOutConfig pointerConfig;
            if (this.keymap != null && this.config.TryGetValue("Pointer", out pointerConfig) && this.OnConfigChanged != null)
            {
                this.OnConfigChanged(new WiiKeyMapConfigChangedEvent(keymap.getName(), keymap.getFilename(), pointerConfig.Stack.First().Key));
            }
        }

        private void Xinput_OnRumble(byte big, byte small)
        {
#if DEBUG
            // Only perform console output on debug build
            Console.WriteLine("Xinput rumble: big=" + big + " small=" + small);
#endif
            if (this.OnRumble != null)
            {
                OnRumble(big > Settings.Default.xinput_rumbleThreshold_big || small > Settings.Default.xinput_rumbleThreshold_small);
            }
        }

        private void ProcessShake(
            string key,
            string fullKey,
            float delta,
            ref int counter,
            ref DateTime lastTime,
            float threshold,
            int count,
            int maxTimeInBetween,
            int pressedTime)
        {
            var now = DateTime.Now;

            if (!PressedButtons[key])
            {
                if (Math.Abs(delta) > threshold)
                {
                    if ((now - lastTime).TotalMilliseconds < maxTimeInBetween)
                    {
                        counter++;
                        if (counter >= count)
                        {
                            PressedButtons[key] = true;
                            executeButtonDown(fullKey);
                            counter = 0;
                        }
                    }
                    else
                    {
                        counter = 0;
                    }
                    lastTime = now;
                }
            }
            else if ((now - lastTime).TotalMilliseconds > pressedTime)
            {
                PressedButtons[key] = false;
                executeButtonUp(fullKey);
            }
        }

        private void ProcessNunchukShake(
            string key,
            string fullKey,
            float delta,
            ref int counter,
            ref DateTime lastTime,
            float threshold,
            int count,
            int maxTimeInBetween,
            int pressedTime)
        {
            var now = DateTime.Now;

            if (!PressedButtons[key])
            {
                if (Math.Abs(delta) > threshold)
                {
                    if ((now - lastTime).TotalMilliseconds < maxTimeInBetween)
                    {
                        counter++;
                        if (counter >= count)
                        {
                            PressedButtons[key] = true;
                            executeButtonDown(fullKey);
                            counter = 0;
                        }
                    }
                    else
                    {
                        counter = 0;
                    }
                    lastTime = now;
                }
            }
            else if ((now - lastTime).TotalMilliseconds > pressedTime)
            {
                PressedButtons[key] = false;
                executeButtonUp(fullKey);
            }
        }

        private string supportedSpecialCodes = "PointerToggle TouchMaster TouchSlave NextLayout disable";

        public void updateCursorPosition(CursorPos cursorPosition)
        {
            KeymapOutConfig outConfig;
            bool changeOffscreen = cursorPosition.OffScreen != prevOffScreen;

            if (cursorPosition.OffScreen != prevOffScreen) // Change pressed button if OffScreen value changes
            {
                foreach (var button in PressedButtons)
                {
                    if (button.Value)
                    {
                        // Only execute if OnScreen and OffScreen values are different.
                        // Only handle button up event here. Let later update routines handle
                        // button down events
                        string tempBtn = string.Concat("Offscreen", button.Key);
                        if (!this.IsInherited(tempBtn))
                        {
                            //Console.WriteLine(button.Key);
                            if (!cursorPosition.OffScreen)
                            {
                                this.executeButtonUp(tempBtn);
                                //this.executeButtonDown(button.Key);
                            }
                            else
                            {
                                this.executeButtonUp(button.Key);
                                //this.executeButtonDown("OffScreen." + button.Key);
                            }
                        }
                    }
                }
            }

            string inputKey = !cursorPosition.OffScreen ? "Pointer" : "Offscreen.Pointer";
            // Perform when onscreen or during screen change
            if ((!cursorPosition.OffScreen || changeOffscreen) &&
                this.config.TryGetValue("Pointer", out outConfig))
            {
                foreach (IOutputHandler handler in outputHandlers)
                {
                    ICursorHandler cursorHandler = handler as ICursorHandler;
                    if (cursorHandler != null)
                    {
                        foreach (KeymapOutput output in outConfig.Stack) //Will normally be only one output config
                        {
                            if (output.Cursor)
                            {
                                if (cursorHandler.setPosition(output.Key, cursorPosition))
                                {
                                    break; // we will break for the first accepting handler, for each output key
                                }
                            }
                        }
                    }
                }

                if (!prevOffScreen)
                {
                    PressedButtons["OnScreen"] = true;
                    this.executeButtonDown("Pointer");
                }
                else if (prevOffScreen && PressedButtons["OnScreen"])
                {
                    PressedButtons["OnScreen"] = false;
                    this.executeButtonUp("Pointer");
                }
            }

            // Perform when onscreen or during screen change
            if ((cursorPosition.OffScreen || changeOffscreen) &&
                this.config.TryGetValue("OffScreen.Pointer", out outConfig))
            {
                if (prevOffScreen)
                {
                    PressedButtons["OffScreen"] = true;
                    this.executeButtonDown("OffScreen.Pointer");
                }
                else if (!prevOffScreen && PressedButtons["OffScreen"])
                {
                    PressedButtons["OffScreen"] = false;
                    this.executeButtonUp("OffScreen.Pointer");
                }
            }

            prevOffScreen = cursorPosition.OffScreen;
        }

        public void updateAccelerometer(AccelState accelState)
        {
            string offscreen = string.Empty;
            if (prevOffScreen)
            {
                offscreen = "OffScreen.";
            }

            KeymapOutConfig outConfig;

            // Lean more towards current values
            smoothedAccelX = smoothedAccelX * 0.1f + accelState.Values.X * 0.9f;
            smoothedAccelY = smoothedAccelY * 0.1f + accelState.Values.Y * 0.9f;
            smoothedAccelZ = smoothedAccelZ * 0.1f + accelState.Values.Z * 0.9f;

            string tempBtnKey = string.Concat(offscreen, "AccelX+");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedAccelX > 0)
                {
                    updateStickHandlers(outConfig, smoothedAccelX);
                }
                else if (smoothedAccelX == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedAccelX > outConfig.Threshold && !PressedButtons["AccelX+"])
                {
                    PressedButtons["AccelX+"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedAccelX < outConfig.Threshold && PressedButtons["AccelX+"])
                {
                    PressedButtons["AccelX+"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "AccelX-");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedAccelX < 0)
                {
                    updateStickHandlers(outConfig, smoothedAccelX * -1);
                }
                else if (smoothedAccelX == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedAccelX * -1 > outConfig.Threshold && !PressedButtons["AccelX-"])
                {
                    PressedButtons["AccelX-"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedAccelX * -1 < outConfig.Threshold && PressedButtons["AccelX-"])
                {
                    PressedButtons["AccelX-"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "AccelY+");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedAccelY > 0)
                {
                    updateStickHandlers(outConfig, smoothedAccelY);
                }
                else if (smoothedAccelY == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedAccelY > outConfig.Threshold && !PressedButtons["AccelY+"])
                {
                    PressedButtons["AccelY+"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedAccelY < outConfig.Threshold && PressedButtons["AccelY+"])
                {
                    PressedButtons["AccelY+"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "AccelY-");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedAccelY < 0)
                {
                    updateStickHandlers(outConfig, smoothedAccelY * -1);
                }
                else if (smoothedAccelY == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedAccelY * -1 > outConfig.Threshold && !PressedButtons["AccelY-"])
                {
                    PressedButtons["AccelY-"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedAccelY * -1 < outConfig.Threshold && PressedButtons["AccelY-"])
                {
                    PressedButtons["AccelY-"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "AccelZ+");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedAccelZ > 0)
                {
                    updateStickHandlers(outConfig, smoothedAccelZ);
                }
                else if (smoothedAccelZ == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedAccelZ > outConfig.Threshold && !PressedButtons["AccelZ+"])
                {
                    PressedButtons["AccelZ+"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedAccelZ < outConfig.Threshold && PressedButtons["AccelZ+"])
                {
                    PressedButtons["AccelZ+"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "AccelZ-");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedAccelZ < 0)
                {
                    updateStickHandlers(outConfig, smoothedAccelZ * -1);
                }
                else if (smoothedAccelZ == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedAccelZ * -1 > outConfig.Threshold && !PressedButtons["AccelZ-"])
                {
                    PressedButtons["AccelZ-"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedAccelZ * -1 < outConfig.Threshold && PressedButtons["AccelZ-"])
                {
                    PressedButtons["AccelZ-"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }
            {
                var now = DateTime.Now;
                smoothedAccelX = smoothedAccelX * 0.9f + accelState.Values.X * 0.1f;
                smoothedAccelY = smoothedAccelY * 0.9f + accelState.Values.Y * 0.1f;
                smoothedAccelZ = smoothedAccelZ * 0.9f + accelState.Values.Z * 0.1f;
                float totalAccel = (float)Math.Sqrt(Math.Pow(smoothedAccelX, 2) + Math.Pow(smoothedAccelY, 2) + Math.Pow(smoothedAccelZ, 2));
                                
                if (wiimoteStabilizationCounter < STABILIZATION_FRAMES)
                {
                    wiimoteStabilizationCounter++;
                    lastAccel = totalAccel;
                    return;
                }

                float delta = totalAccel - lastAccel;

                if (this.config.ContainsKey(offscreen + "Shake"))
                {
                    ProcessShake("Shake", offscreen + "Shake", delta,
                        ref ShakeCounter, ref lastShakeTime,
                        (float)Settings.Default.shake_threshold,
                        Settings.Default.shake_count,
                        Settings.Default.shake_maxTimeInBetween,
                        Settings.Default.shake_pressedTime);
                }

                lastAccel = totalAccel;
            }
        }

        public void updateNunchuk(NunchukState nunchuk)
        {
            string offscreen = string.Empty;
            if (prevOffScreen)
            {
                offscreen = "OffScreen.";
            }

            KeymapOutConfig outConfig;

            string tempBtnKey = string.Concat(offscreen, "Nunchuk.StickRight");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (nunchuk.Joystick.X > 0)
                {
                    updateStickHandlers(outConfig, nunchuk.Joystick.X * 2);
                }
                else if (nunchuk.Joystick.X == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (nunchuk.Joystick.X * 2 > outConfig.Threshold && !PressedButtons["Nunchuk.StickRight"])
                {
                    PressedButtons["Nunchuk.StickRight"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (nunchuk.Joystick.X * 2 < outConfig.Threshold && PressedButtons["Nunchuk.StickRight"])
                {
                    PressedButtons["Nunchuk.StickRight"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Nunchuk.StickLeft");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (nunchuk.Joystick.X < 0)
                {
                    updateStickHandlers(outConfig, nunchuk.Joystick.X * -2);
                }
                else if (nunchuk.Joystick.X == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (nunchuk.Joystick.X * -2 > outConfig.Threshold && !PressedButtons["Nunchuk.StickLeft"])
                {
                    PressedButtons["Nunchuk.StickLeft"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (nunchuk.Joystick.X * -2 < outConfig.Threshold && PressedButtons["Nunchuk.StickLeft"])
                {
                    PressedButtons["Nunchuk.StickLeft"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Nunchuk.StickUp");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (nunchuk.Joystick.Y > 0)
                {
                    updateStickHandlers(outConfig, nunchuk.Joystick.Y * 2);
                }
                else if (nunchuk.Joystick.Y == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (nunchuk.Joystick.Y * 2 > outConfig.Threshold && !PressedButtons["Nunchuk.StickUp"])
                {
                    PressedButtons["Nunchuk.StickUp"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (nunchuk.Joystick.Y * 2 < outConfig.Threshold && PressedButtons["Nunchuk.StickUp"])
                {
                    PressedButtons["Nunchuk.StickUp"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Nunchuk.StickDown");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (nunchuk.Joystick.Y < 0)
                {
                    updateStickHandlers(outConfig, nunchuk.Joystick.Y * -2);
                }
                else if (nunchuk.Joystick.Y == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (nunchuk.Joystick.Y * -2 > outConfig.Threshold && !PressedButtons["Nunchuk.StickDown"])
                {
                    PressedButtons["Nunchuk.StickDown"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (nunchuk.Joystick.Y * -2 < outConfig.Threshold && PressedButtons["Nunchuk.StickDown"])
                {
                    PressedButtons["Nunchuk.StickDown"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            AccelState accelState = nunchuk.AccelState;

            tempBtnKey = string.Concat(offscreen, "Nunchuk.AccelX+");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedNunAccelX > 0)
                {
                    updateStickHandlers(outConfig, smoothedNunAccelX );
                }
                else if (smoothedNunAccelX == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedNunAccelX > outConfig.Threshold && !PressedButtons["Nunchuk.AccelX+"])
                {
                    PressedButtons["Nunchuk.AccelX+"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedNunAccelX < outConfig.Threshold && PressedButtons["Nunchuk.AccelX+"])
                {
                    PressedButtons["Nunchuk.AccelX+"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Nunchuk.AccelX-");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedNunAccelX < 0)
                {
                    updateStickHandlers(outConfig, smoothedNunAccelX * -1);
                }
                else if (smoothedNunAccelX == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedNunAccelX * -1 > outConfig.Threshold && !PressedButtons["Nunchuk.AccelX-"])
                {
                    PressedButtons["Nunchuk.AccelX-"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedNunAccelX * -1 < outConfig.Threshold && PressedButtons["Nunchuk.AccelX-"])
                {
                    PressedButtons["Nunchuk.AccelX-"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Nunchuk.AccelY+");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedNunAccelY > 0)
                {
                    updateStickHandlers(outConfig, smoothedNunAccelY);
                }
                else if (smoothedNunAccelY == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedNunAccelY > outConfig.Threshold && !PressedButtons["Nunchuk.AccelY+"])
                {
                    PressedButtons["Nunchuk.AccelY+"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedNunAccelY < outConfig.Threshold && PressedButtons["Nunchuk.AccelY+"])
                {
                    PressedButtons["Nunchuk.AccelY+"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Nunchuk.AccelY-");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedNunAccelY < 0)
                {
                    updateStickHandlers(outConfig, smoothedNunAccelY * -1);
                }
                else if (smoothedNunAccelY == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedNunAccelY * -1 > outConfig.Threshold && !PressedButtons["Nunchuk.AccelY-"])
                {
                    PressedButtons["Nunchuk.AccelY-"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedNunAccelY * -1 < outConfig.Threshold && PressedButtons["Nunchuk.AccelY-"])
                {
                    PressedButtons["Nunchuk.AccelY-"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Nunchuk.AccelZ+");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedNunAccelZ > 0)
                {
                    updateStickHandlers(outConfig, smoothedNunAccelZ);
                }
                else if (smoothedNunAccelZ == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedNunAccelZ > outConfig.Threshold && !PressedButtons["Nunchuk.AccelZ+"])
                {
                    PressedButtons["Nunchuk.AccelZ+"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedNunAccelZ < outConfig.Threshold && PressedButtons["Nunchuk.AccelZ+"])
                {
                    PressedButtons["Nunchuk.AccelZ+"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Nunchuk.AccelZ-");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (smoothedNunAccelZ < 0)
                {
                    updateStickHandlers(outConfig, smoothedNunAccelZ * -1);
                }
                else if (smoothedNunAccelZ == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (smoothedNunAccelZ * -1 > outConfig.Threshold && !PressedButtons["Nunchuk.AccelZ-"])
                {
                    PressedButtons["Nunchuk.AccelZ-"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (smoothedNunAccelZ * -1 < outConfig.Threshold && PressedButtons["Nunchuk.AccelZ-"])
                {
                    PressedButtons["Nunchuk.AccelZ-"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            {
                // Smoothed values update + shake detection
                var now = DateTime.Now;
                smoothedNunAccelX = smoothedNunAccelX * 0.9f + accelState.Values.X * 0.1f;
                smoothedNunAccelY = smoothedNunAccelY * 0.9f + accelState.Values.Y * 0.1f;
                smoothedNunAccelZ = smoothedNunAccelZ * 0.9f + accelState.Values.Z * 0.1f;

                float totalAccel = (float)Math.Sqrt(Math.Pow(smoothedNunAccelX, 2) + Math.Pow(smoothedNunAccelY, 2) + Math.Pow(smoothedNunAccelZ, 2));

                if (nunchukStabilizationCounter < STABILIZATION_FRAMES)
                {
                    nunchukStabilizationCounter++;
                    nunLastAccel = totalAccel;
                    return;
                }

                float delta = totalAccel - nunLastAccel;

                if (this.config.ContainsKey(offscreen + "Nunchuk.Shake"))
                {
                    ProcessNunchukShake("Nunchuk.Shake", offscreen + "Nunchuk.Shake", delta,
                        ref nunShakeCounter, ref nunLastShakeTime,
                        (float)Settings.Default.shake_nunchukthreshold,
                        Settings.Default.shake_nunchukcount,
                        Settings.Default.shake_maxTimeInBetween,
                        Settings.Default.shake_pressedTime);
                }

                nunLastAccel = totalAccel;
            }
        }

        public void updateClassicController(ClassicControllerState classic)
        {
            string offscreen = string.Empty;
            if (prevOffScreen)
            {
                offscreen = "OffScreen.";
            }

            KeymapOutConfig outConfig;

            string tempBtnKey = string.Concat(offscreen, "Classic.StickLRight");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (classic.JoystickL.X > 0)
                {
                    updateStickHandlers(outConfig, classic.JoystickL.X * 2);
                }
                else if (classic.JoystickL.X == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (classic.JoystickL.X * 2 > outConfig.Threshold && !PressedButtons["Classic.StickLRight"])
                {
                    PressedButtons["Classic.StickLRight"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (classic.JoystickL.X * 2 < outConfig.Threshold && PressedButtons["Classic.StickLRight"])
                {
                    PressedButtons["Classic.StickLRight"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Classic.StickLLeft");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (classic.JoystickL.X < 0)
                {
                    updateStickHandlers(outConfig, classic.JoystickL.X * -2);
                }
                else if (classic.JoystickL.X == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (classic.JoystickL.X * -2 > outConfig.Threshold && !PressedButtons["Classic.StickLLeft"])
                {
                    PressedButtons["Classic.StickLLeft"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (classic.JoystickL.X * -2 < outConfig.Threshold && PressedButtons["Classic.StickLLeft"])
                {
                    PressedButtons["Classic.StickLLeft"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Classic.StickLUp");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (classic.JoystickL.Y > 0)
                {
                    updateStickHandlers(outConfig, classic.JoystickL.Y * 2);
                }
                else if (classic.JoystickL.Y == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (classic.JoystickL.Y * 2 > outConfig.Threshold && !PressedButtons["Classic.StickLUp"])
                {
                    PressedButtons["Classic.StickLUp"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (classic.JoystickL.Y * 2 < outConfig.Threshold && PressedButtons["Classic.StickLUp"])
                {
                    PressedButtons["Classic.StickLUp"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Classic.StickLDown");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (classic.JoystickL.Y < 0)
                {
                    updateStickHandlers(outConfig, classic.JoystickL.Y * -2);
                }
                else if (classic.JoystickL.Y == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (classic.JoystickL.Y * -2 > outConfig.Threshold && !PressedButtons["Classic.StickLDown"])
                {
                    PressedButtons["Classic.StickLDown"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (classic.JoystickL.Y * -2 < outConfig.Threshold && PressedButtons["Classic.StickLDown"])
                {
                    PressedButtons["Classic.StickLDown"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Classic.StickRRight");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (classic.JoystickR.X > 0)
                {
                    updateStickHandlers(outConfig, classic.JoystickR.X * 2);
                }
                else if (classic.JoystickR.X == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (classic.JoystickR.X * 2 > outConfig.Threshold && !PressedButtons["Classic.StickRRight"])
                {
                    PressedButtons["Classic.StickRRight"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (classic.JoystickR.X * 2 < outConfig.Threshold && PressedButtons["Classic.StickRRight"])
                {
                    PressedButtons["Classic.StickRRight"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Classic.StickRLeft");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (classic.JoystickR.X < 0)
                {
                    updateStickHandlers(outConfig, classic.JoystickR.X * -2);
                }
                else if (classic.JoystickR.X == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (classic.JoystickR.X * -2 > outConfig.Threshold && !PressedButtons["Classic.StickRLeft"])
                {
                    PressedButtons["Classic.StickRLeft"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (classic.JoystickR.X * -2 < outConfig.Threshold && PressedButtons["Classic.StickRLeft"])
                {
                    PressedButtons["Classic.StickRLeft"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Classic.StickRUp");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (classic.JoystickR.Y > 0)
                {
                    updateStickHandlers(outConfig, classic.JoystickR.Y * 2);
                }
                else if (classic.JoystickR.Y == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (classic.JoystickR.Y * 2 > outConfig.Threshold && !PressedButtons["Classic.StickRUp"])
                {
                    PressedButtons["Classic.StickRUp"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (classic.JoystickR.Y * 2 < outConfig.Threshold && PressedButtons["Classic.StickRUp"])
                {
                    PressedButtons["Classic.StickRUp"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Classic.StickRDown");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                if (classic.JoystickR.Y < 0)
                {
                    updateStickHandlers(outConfig, classic.JoystickR.Y * -2);
                }
                else if (classic.JoystickR.Y == 0)
                {
                    updateStickHandlers(outConfig, 0);
                }

                if (classic.JoystickR.Y * -2 > outConfig.Threshold && !PressedButtons["Classic.StickRDown"])
                {
                    PressedButtons["Classic.StickRDown"] = true;
                    this.executeButtonDown(tempBtnKey);
                }
                else if (classic.JoystickR.Y * -2 < outConfig.Threshold && PressedButtons["Classic.StickRDown"])
                {
                    PressedButtons["Classic.StickRDown"] = false;
                    this.executeButtonUp(tempBtnKey);
                }
            }

            tempBtnKey = string.Concat(offscreen, "Classic.TriggerL");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                updateStickHandlers(outConfig, classic.TriggerL);
            }

            tempBtnKey = string.Concat(offscreen, "Classic.TriggerR");
            if (this.config.TryGetValue(tempBtnKey, out outConfig))
            {
                updateStickHandlers(outConfig, classic.TriggerR);
            }
        }

        private bool updateStickHandlers(KeymapOutConfig outConfig, double value)
        {
            foreach (IOutputHandler handler in outputHandlers)
            {
                IStickHandler stickHandler = handler as IStickHandler;
                if (stickHandler != null)
                {
                    foreach(KeymapOutput output in outConfig.Stack)
                    {
                        if (output.Continous)
                        {
                            double newValue = value;
                            //Make sure the value is not above 1
                            newValue = newValue > 1 ? 1 : newValue;
                            //Set value to 0 if it's within deadzone
                            newValue = newValue <= outConfig.Deadzone ? 0 : (newValue - outConfig.Deadzone) / (1 - outConfig.Deadzone);
                            
                            //Add the scaling from the config
                            newValue = newValue * outConfig.Scale;
                            if (stickHandler.setValue(output.Key.ToLower(), newValue))
                            {
                                break; // we will break for the first accepting handler, for each output key
                            }
                        }
                    }
                }
            }
            return false;
        }

        public void executeButtonUp(WiimoteButton button)
        {
            this.executeButtonUp(button.ToString());//ToString converts WiimoteButton.A to "A" for instance
        }

        public void executeButtonUp(NunchukButton button)
        {
            this.executeButtonUp(string.Concat("Nunchuk.", button.ToString()));
        }

        public void executeButtonUp(ClassicControllerButton button)
        {
            this.executeButtonUp(string.Concat("Classic.", button.ToString()));//ToString converts WiimoteButton.A to "A" for instance
        }

        public void executeButtonUp(string button)
        {
            bool handled = false;

            List<string> keyList = new List<string>();
            KeymapOutConfig outConfig;

            if (this.config.TryGetValue(button, out outConfig))
            {
                List<KeymapOutput> stack = new List<KeymapOutput>(outConfig.Stack);
                stack.Reverse();
                foreach (KeymapOutput output in stack)
                {
                    keyList.Add(output.Key);
                    if (!(output.Continous && KeymapDatabase.Current.getInput(button).Continous)) //Exclude the case when a stick is connected to a stick. It should not trigger the press action.
                    {
                        handled |= this.executeKeyUp(output.Key);
                    }
                }

            }

            if (OnButtonUp != null)
            {
                OnButtonUp(new WiiButtonEvent(keyList, button, handled));
            }
        }

        private bool executeKeyUp(string key)
        {
            foreach (IOutputHandler handler in outputHandlers)
            {
                IButtonHandler buttonHandler = handler as IButtonHandler;
                if (buttonHandler != null)
                {
                    if (buttonHandler.setButtonUp(key))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void executeButtonDown(WiimoteButton button)
        {
            this.executeButtonDown(button.ToString());
        }

        public void executeButtonDown(NunchukButton button)
        {
            this.executeButtonDown(string.Concat("Nunchuk.", button.ToString()));
        }

        public void executeButtonDown(ClassicControllerButton button)
        {
            this.executeButtonDown(string.Concat("Classic.", button.ToString()));
        }

        public void executeButtonDown(string button)
        {
            bool handled = false;
            List<string> keyList = new List<string>();
            KeymapOutConfig outConfig;
            if (this.config.TryGetValue(button, out outConfig) && outConfig != null)
            {
                HashSet<string> handledKeys = new HashSet<string>();
                List<KeymapOutput> stack = new List<KeymapOutput>(outConfig.Stack);
                foreach (KeymapOutput output in stack)
                {
                    keyList.Add(output.Key);

                    if (!(output.Continous && KeymapDatabase.Current.getInput(button).Continous)) //Exclude the case when a continous output is connected to a continous output. It should not trigger the button action.
                    {
                        if (handledKeys.Contains(output.Key)) //Repeat a button that has already been pressed
                        {
                            this.executeKeyUp(output.Key);
                        }
                        handledKeys.Add(output.Key);

                        handled |= this.executeKeyDown(output.Key);
                    }
                }
            }

            if (OnButtonDown != null)
            {
                OnButtonDown(new WiiButtonEvent(keyList, button, handled));
            }
        }

        private bool executeKeyDown(string key)
        {
            foreach (IOutputHandler handler in outputHandlers)
            {
                IButtonHandler buttonHandler = handler as IButtonHandler;
                if (buttonHandler != null)
                {
                    if (buttonHandler.setButtonDown(key))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public bool IsInherited(string button)
        {
            if (this.config.TryGetValue(button, out KeymapOutConfig currconfig))
                return currconfig.Inherited;

            return false;
        }

        public void FinishUpdate(CursorPos cursorPosition)
        {
            prevOffScreen = cursorPosition.OffScreen;
        }
    }

    public class WiiButtonEvent
    {
        public bool Handled;
        public List<string> Actions;
        public string Button;

        public WiiButtonEvent(List<string> actions, string button, bool handled = false)
        {
            this.Actions = actions;
            this.Button = button;
            this.Handled = handled;
        }

    }

    public class WiiKeyMapConfigChangedEvent
    {
        public string Name;
        public string Filename;
        public string Pointer;

        public WiiKeyMapConfigChangedEvent(string name, string filename, string pointer)
        {
            this.Name = name;
            this.Filename = filename;
            this.Pointer = pointer;
        }
    }
}
