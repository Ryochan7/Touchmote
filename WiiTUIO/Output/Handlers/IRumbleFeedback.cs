﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WiiTUIO.Output.Handlers
{
    public interface IRumbleFeedback
    {
        Action<Byte, Byte> OnRumble { get; set; }
    }
}
