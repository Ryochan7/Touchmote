using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WiiTUIO.Output.Handlers.Touch;
using WiiTUIO.Output.Handlers.Xinput;

namespace WiiTUIO.Output.Handlers
{
    public class HandlerFactory
    {
        private Dictionary<long, List<IOutputHandler>> outputHandlers;

        public HandlerFactory()
        {
            outputHandlers = new Dictionary<long, List<IOutputHandler>>();
        }

        private List<IOutputHandler> createOutputHandlers(long id)
        {
            List<IOutputHandler> all = new List<IOutputHandler>();
            Thread temp = new Thread(() =>
            {
                //IOutputHandler keyboardHandler = VmultiDevice.Current.isAvailable() ? (IOutputHandler)(VmultiKeyboardHandler.Default) : (IOutputHandler)(new KeyboardHandler());
                bool fakerAvailable = FakerInputDevice.Current.isAvailable();
                IOutputHandler keyboardHandler = null;
                if (fakerAvailable)
                {
                    keyboardHandler = FakerInputKeyboardHandler.Default;
                }
                else
                {
                    keyboardHandler = new KeyboardHandler();
                }

                all.Add(keyboardHandler);
                //all.Add(new MouseHandler());
                IOutputHandler mouseHandler = null;
                if (fakerAvailable)
                {
                    mouseHandler = new FakerInputMouseHandler(FakerInputDevice.Current);
                }
                else
                {
                    mouseHandler = new MouseHandler();
                }

                all.Add(mouseHandler);
                all.Add(new ViGEmHandler(id));
                //all.Add(new TouchHandler(TouchOutputFactory.getCurrentProviderHandler(),id));
                all.Add(new CursorHandler(id));
            });

            temp.IsBackground = false;
            temp.Priority = ThreadPriority.AboveNormal;
            temp.Start();
            temp.Join();

            return all;
        }

        public List<IOutputHandler> getOutputHandlers(long id)
        {
            List<IOutputHandler> handlerList;
            if (outputHandlers.TryGetValue(id, out handlerList))
            {
                return handlerList;
            }
            else
            {
                handlerList = this.createOutputHandlers(id);
                return handlerList;
            }
        }

    }
}
