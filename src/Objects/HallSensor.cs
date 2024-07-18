using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DoorOpenDetector.Objects
{
   internal class HallSensor
   {
      public byte Contact { get; set; }

      [JsonIgnore]
      public bool IsOpen { get {  return Contact != 0; } }
   }
}
